using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Demo.Avalonia
{
    public partial class BrowserTabView : UserControl, IDisposable
    {
        public CefGlueBrowser Browser { get; }

        public event Action<string> TitleChanged;

        public event Action<string> StatusTextChanged;

        public string LastStatus { get; private set; } = "Done";

        public event Action<bool> FullscreenModeChanged;

        public Control Control => Browser.Control;

        private ColumnDefinition PageColumn => ContentGrid.ColumnDefinitions[0];
        private ColumnDefinition DevToolsSplitterColumn => ContentGrid.ColumnDefinitions[1];
        private ColumnDefinition DevToolsColumn => ContentGrid.ColumnDefinitions[2];

        public BrowserTabView(CefGlueHost app, string initialUrl = null, Action<string> openInNewTab = null)
        {
            InitializeComponent();

            Browser = app.CreateBrowser(new CefGlueBrowserOptions
            {
                SetupCefBrowserSettings = () => new CefBrowserSettings
                {
                    WindowlessFrameRate = 60,
                },
              SetupCefWindowInfo = () =>
              {
                  var c = CefWindowInfo.Create();
                  c.SharedTextureEnabled = true;
                  return c;
              },
              // Default is BitmapReadback; CEFGLUE_DEMO_TEXTURE_CONTROL=1 tries zero-copy TextureControl.
              UseTextureControl = Environment.GetEnvironmentVariable("CEFGLUE_DEMO_TEXTURE_CONTROL")?.Trim() == "1",
            });

            Browser.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Browser.Control.VerticalAlignment = VerticalAlignment.Stretch;

            BrowserContainer.Children.Add(Browser.Control);

            Browser.LoadStart += OnBrowserLoadStart;
            Browser.LoadEnd += OnBrowserLoadEnd;
            Browser.TitleChanged += (s, title) => TitleChanged?.Invoke(title);
            Browser.FullscreenModeChanged += OnBrowserFullscreenModeChanged;
            Browser.LifeSpanHandler = new BrowserLifeSpanHandler(Browser, openInNewTab);
            Browser.ContextMenuHandler = new LinkContextMenuHandler(openInNewTab);
            if (Browser.RenderModeReason != null)
            {
                Console.WriteLine($"[demo] RenderMode {Browser.RenderMode}: {Browser.RenderModeReason}");
            }
            Browser.RenderModeChanged += (mode, reason) =>
            {
                Console.WriteLine($"[demo] RenderMode -> {mode}: {reason}");
                LastStatus = $"Done · {mode}";
                StatusTextChanged?.Invoke(LastStatus);
            };

            Browser.DevToolsOpened += OnBrowserDevToolsOpened;
            Browser.DevToolsClosed += OnBrowserDevToolsClosed;

            Browser.Address = initialUrl ?? "https://google.com";
        }

        public void DetachControl() => BrowserContainer.Children.Remove(Browser.Control);

        public void ReattachControl()
        {
            if (!BrowserContainer.Children.Contains(Browser.Control))
            {
                BrowserContainer.Children.Add(Browser.Control);
            }
        }

        public void FocusBrowserContent() => Browser.Focus();

        private const double MinDevToolsWidth = 200;
        private const double MinPageWidth = 100;
        private const double SplitterWidth = 5;

        private double _lastDevToolsWidth;
        private bool _devToolsOpen;

        public bool IsDevToolsOpen => _devToolsOpen;

        public void OpenDevTools() => Browser.ShowDevToolOSR(DevToolsContentHost);

        public void CloseDevTools() => Browser.CloseDevToolOSR();

        public void ToggleDevTools()
        {
            if (_devToolsOpen)
            {
                CloseDevTools();
            }
            else
            {
                OpenDevTools();
            }
        }

        private void OnBrowserDevToolsOpened(Control control)
        {
            _devToolsOpen = true;
            // First open: page ~60%, DevTools ~40%; after that, keep the width the user dragged to.
            var available = ContentGrid.Bounds.Width;
            var width = _lastDevToolsWidth > 0 ? _lastDevToolsWidth : Math.Max(320, available * 0.4);
            width = Math.Clamp(width, MinDevToolsWidth, Math.Max(MinDevToolsWidth, available - SplitterWidth - MinPageWidth));

            PageColumn.MinWidth = MinPageWidth;
            DevToolsSplitterColumn.Width = new GridLength(SplitterWidth);
            DevToolsColumn.Width = new GridLength(width);
            DevToolsColumn.MinWidth = MinDevToolsWidth;
            DevToolsSplitter.IsVisible = true;
        }

        private void OnBrowserDevToolsClosed()
        {
            _devToolsOpen = false;

            if (DevToolsColumn.ActualWidth > 0)
            {
                _lastDevToolsWidth = DevToolsColumn.ActualWidth;
            }

            // MinWidth first, or the column keeps 200px.
            DevToolsColumn.MinWidth = 0;
            PageColumn.MinWidth = 0;
            DevToolsSplitterColumn.Width = new GridLength(0);
            DevToolsColumn.Width = new GridLength(0);
            DevToolsSplitter.IsVisible = false;
        }

        private void OnCloseDevToolsClick(object sender, RoutedEventArgs e) => CloseDevTools();

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Browser.CanGoBack)
            {
                Browser.GoBack();
            }
        }

        private void OnForwardClick(object sender, RoutedEventArgs e)
        {
            if (Browser.CanGoForward)
            {
                Browser.GoForward();
            }
        }

        private void OnReloadClick(object sender, RoutedEventArgs e) => Browser.Reload();

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                NavigationBar.IsVisible = !fullscreen;
                FullscreenModeChanged?.Invoke(fullscreen);
            });
        }

        private void OnBrowserLoadStart(object sender, LoadStartEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                AddressBar.Text = e.Frame.Url;
                LastStatus = $"Loading {e.Frame.Url}...";
                StatusTextChanged?.Invoke(LastStatus);
            });
        }

        private void OnBrowserLoadEnd(object sender, LoadEndEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                LastStatus = $"Done · {Browser.RenderMode}";
                StatusTextChanged?.Invoke(LastStatus);
            });
        }

        private void OnAddressBarKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Browser.Address = AddressBar.Text;
            }
        }

        public void Dispose()
        {
            Browser.CloseDevToolOSR();
            Browser.Dispose();
        }
    }
}
