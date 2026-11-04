using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.WPF;

namespace Xilium.CefGlue.Demo.WPF
{
    public partial class BrowserTabView : System.Windows.Controls.UserControl, IDisposable
    {
        public CefGlueBrowser Browser { get; }

        public event Action<string> TitleChanged;

        public event Action<string> StatusTextChanged;

        public string LastStatus { get; private set; } = "Done";

        public event Action<bool> FullscreenModeChanged;

        public UIElement Control => Browser.Control;

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
                    var windowInfo = CefWindowInfo.Create();
                    windowInfo.SharedTextureEnabled = true;
                    return windowInfo;
                }
            });

            Browser.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Browser.Control.VerticalAlignment = VerticalAlignment.Stretch;
            RenderOptions.SetBitmapScalingMode(Browser.Control, BitmapScalingMode.HighQuality);
            browserContainer.Children.Add(Browser.Control);

            Browser.LoadStart += OnBrowserLoadStart;
            Browser.LoadEnd += OnBrowserLoadEnd;
            Browser.TitleChanged += (s, title) => TitleChanged?.Invoke(title);
            Browser.FullscreenModeChanged += OnBrowserFullscreenModeChanged;
            Browser.LifeSpanHandler = new BrowserLifeSpanHandler(Browser, openInNewTab);
            Browser.ContextMenuHandler = new LinkContextMenuHandler(openInNewTab);
            Browser.DevToolsOpened += OnBrowserDevToolsOpened;
            Browser.DevToolsClosed += OnBrowserDevToolsClosed;
            Browser.RenderModeChanged += OnBrowserRenderModeChanged;

            Browser.Address = initialUrl ?? "https://google.com";
        }

        public void DetachControl()
        {
            browserContainer.Children.Remove(Browser.Control);
        }

        public void ReattachControl()
        {
            if (Browser.Control.Parent != null)
            {
                return;
            }

            if (!browserContainer.Children.Contains(Browser.Control))
            {
                browserContainer.Children.Add(Browser.Control);
            }
        }

        public void FocusBrowserContent() => Browser.Focus();

        private const double MinDevToolsWidth = 200;
        private const double MinPageWidth = 100;
        private const double SplitterWidth = 5;

        private double _lastDevToolsWidth;
        private bool _devToolsOpen;

        public bool IsDevToolsOpen => _devToolsOpen;

        public void OpenDevTools() => Browser.ShowDevToolOSR(devToolsContentHost);

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

        private void OnBrowserDevToolsOpened(FrameworkElement control)
        {
            _devToolsOpen = true;
            // First open: page ~60%, DevTools ~40%; after that, keep the width the user dragged to.
            var available = contentGrid.ActualWidth;
            var width = _lastDevToolsWidth > 0 ? _lastDevToolsWidth : Math.Max(320, available * 0.4);
            width = Math.Clamp(width, MinDevToolsWidth, Math.Max(MinDevToolsWidth, available - SplitterWidth - MinPageWidth));

            pageColumn.MinWidth = MinPageWidth;
            devToolsSplitterColumn.Width = new GridLength(SplitterWidth);
            devToolsColumn.Width = new GridLength(width);
            devToolsColumn.MinWidth = MinDevToolsWidth;
            devToolsSplitter.Visibility = Visibility.Visible;
        }

        private void OnBrowserDevToolsClosed()
        {
            _devToolsOpen = false;

            if (devToolsColumn.ActualWidth > 0)
            {
                _lastDevToolsWidth = devToolsColumn.ActualWidth;
            }

            // MinWidth first, or the column keeps 200px.
            devToolsColumn.MinWidth = 0;
            pageColumn.MinWidth = 0;
            devToolsSplitterColumn.Width = new GridLength(0);
            devToolsColumn.Width = new GridLength(0);
            devToolsSplitter.Visibility = Visibility.Collapsed;
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
            Dispatcher.BeginInvoke((Action)(() =>
            {
                navigationBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
                FullscreenModeChanged?.Invoke(fullscreen);
            }));
        }

        private void OnBrowserLoadStart(object sender, LoadStartEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.BeginInvoke((Action)(() =>
            {
                addressBar.Text = e.Frame.Url;
                LastStatus = $"Loading {e.Frame.Url}...";
                StatusTextChanged?.Invoke(LastStatus);
            }));
        }

        private void OnBrowserLoadEnd(object sender, LoadEndEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.BeginInvoke((Action)(() =>
            {
                LastStatus = $"Done · {Browser.RenderMode}";
                StatusTextChanged?.Invoke(LastStatus);
            }));
        }

        private void OnBrowserRenderModeChanged(OsrRenderMode mode, string reason)
        {
            LastStatus = $"Done · {mode}";
            StatusTextChanged?.Invoke(LastStatus);
        }

        private void OnAddressBarKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Browser.Address = addressBar.Text;
            }
        }

        public void Dispose()
        {
            Browser.CloseDevToolOSR();
            Browser.Dispose();
        }
    }
}
