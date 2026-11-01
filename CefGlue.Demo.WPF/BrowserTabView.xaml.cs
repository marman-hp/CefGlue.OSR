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

        private GridLength _lastDevToolsHeight = new GridLength(300);

        public void OpenDevTools() => Browser.ShowDevToolOSR(devToolsContentHost);

        public void CloseDevTools() => Browser.CloseDevToolOSR();

        private void OnBrowserDevToolsOpened(FrameworkElement control)
        {
            devToolsSplitterRow.Height = new GridLength(5);
            devToolsRow.Height = _lastDevToolsHeight;
            devToolsSplitter.Visibility = Visibility.Visible;
        }

        private void OnBrowserDevToolsClosed()
        {

            if (devToolsRow.Height.Value > 0)
            {
                _lastDevToolsHeight = devToolsRow.Height;
            }

            devToolsSplitterRow.Height = new GridLength(0);
            devToolsRow.Height = new GridLength(0);
            devToolsSplitter.Visibility = Visibility.Collapsed;
        }

        private void OnCloseDevToolsClick(object sender, RoutedEventArgs e) => CloseDevTools();

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                addressBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
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
