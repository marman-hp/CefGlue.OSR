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

        private RowDefinition DevToolsSplitterRow => RootGrid.RowDefinitions[2];
        private RowDefinition DevToolsRow => RootGrid.RowDefinitions[3];

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
            Browser.RenderModeChanged += (mode, _) =>
            {
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

        private GridLength _lastDevToolsHeight = new GridLength(300);
        private bool _devToolsOpen;

        public void OpenDevTools() => Browser.ShowDevToolOSR(DevToolsContentHost);

        public void CloseDevTools() => Browser.CloseDevToolOSR();

        private void OnBrowserDevToolsOpened(Control control)
        {
            _devToolsOpen = true;
            DevToolsSplitterRow.Height = new GridLength(5);
            DevToolsRow.Height = _lastDevToolsHeight;
            DevToolsSplitter.IsVisible = true;
        }

        private void OnBrowserDevToolsClosed()
        {
            _devToolsOpen = false;

            if (DevToolsRow.Height.Value > 0)
            {
                _lastDevToolsHeight = DevToolsRow.Height;
            }

            DevToolsSplitterRow.Height = new GridLength(0);
            DevToolsRow.Height = new GridLength(0);
            DevToolsSplitter.IsVisible = false;
        }

        private void OnCloseDevToolsClick(object sender, RoutedEventArgs e) => CloseDevTools();

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AddressBar.IsVisible = !fullscreen;
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
