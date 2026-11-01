using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.WinUI;

namespace Xilium.CefGlue.Demo.WinUI
{
    public sealed partial class BrowserTabView : UserControl, IDisposable
    {
        public CefGlueBrowser Browser { get; }

        public event Action<string> TitleChanged;

        public event Action<string> StatusTextChanged;

        public string LastStatus { get; private set; } = "Done";

        public event Action<bool> FullscreenModeChanged;

        public FrameworkElement Control => Browser.Control;

        public BrowserTabView(CefGlueHost app, Window hostWindow, string initialUrl = null, Action<string> openInNewTab = null)
        {
            InitializeComponent();

            Browser = app.CreateBrowser(new CefGlueBrowserOptions
            {
                SetupCefBrowserSettings = () => new CefBrowserSettings
                {
                    WindowlessFrameRate = 60,
                },
                SetupCefWindowInfo = () =>  {
                    var wndinfo = CefWindowInfo.Create();
                    wndinfo.SharedTextureEnabled = true;
                    return wndinfo;
                }
            });

            Browser.HostWindow = hostWindow;

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

        public void FocusBrowserContent() => Browser.Control.Focus(FocusState.Programmatic);

        private GridLength _lastDevToolsHeight = new GridLength(300);
        private bool _devToolsOpen;

        public void OpenDevTools() => Browser.ShowDevToolOSR(DevToolsContentHost);

        public void CloseDevTools() => Browser.CloseDevToolOSR();

        private void OnBrowserDevToolsOpened(FrameworkElement control)
        {
            _devToolsOpen = true;
            DevToolsSplitterRow.Height = new GridLength(5);
            DevToolsRow.Height = _lastDevToolsHeight;
            DevToolsSplitter.Visibility = Visibility.Visible;
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
            DevToolsSplitter.Visibility = Visibility.Collapsed;
        }

        private void OnCloseDevToolsClick(object sender, RoutedEventArgs e) => CloseDevTools();

        private bool _isDraggingDevToolsSplitter;
        private double _dragStartPointerY;
        private double _dragStartDevToolsRowHeight;

        private void OnDevToolsSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingDevToolsSplitter = true;
            _dragStartPointerY = e.GetCurrentPoint(RootGrid).Position.Y;
            _dragStartDevToolsRowHeight = DevToolsRow.Height.Value;
            ((UIElement)sender).CapturePointer(e.Pointer);
        }

        private void OnDevToolsSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingDevToolsSplitter)
            {
                return;
            }

            var currentY = e.GetCurrentPoint(RootGrid).Position.Y;
            var delta = currentY - _dragStartPointerY;
            var newHeight = Math.Max(0, _dragStartDevToolsRowHeight - delta);
            DevToolsRow.Height = new GridLength(newHeight);
        }

        private void OnDevToolsSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingDevToolsSplitter = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        }

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AddressBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
                FullscreenModeChanged?.Invoke(fullscreen);
            });
        }

        private void OnBrowserLoadStart(object sender, LoadStartEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
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

            DispatcherQueue.TryEnqueue(() =>
            {
                LastStatus = $"Done · {Browser.RenderMode}";
                StatusTextChanged?.Invoke(LastStatus);
            });
        }

        private void OnAddressBarKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Browser.Address = AddressBar.Text;
            }
        }

        public void Dispose() => Browser.Dispose();
    }
}
