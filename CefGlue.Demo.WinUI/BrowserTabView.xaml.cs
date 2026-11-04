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
            Browser.ContextMenuHandler = new LinkContextMenuHandler(openInNewTab);
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

        private void OnBrowserDevToolsOpened(FrameworkElement control)
        {
            _devToolsOpen = true;
            // First open: page ~60%, DevTools ~40%; after that, keep the width the user dragged to.
            var width = _lastDevToolsWidth > 0 ? _lastDevToolsWidth : Math.Max(320, ContentGrid.ActualWidth * 0.4);
            DevToolsSplitterColumn.Width = new GridLength(SplitterWidth);
            DevToolsColumn.Width = new GridLength(ClampDevToolsWidth(width));
            DevToolsSplitter.Visibility = Visibility.Visible;
        }

        private void OnBrowserDevToolsClosed()
        {
            _devToolsOpen = false;

            if (DevToolsColumn.Width.Value > 0)
            {
                _lastDevToolsWidth = DevToolsColumn.Width.Value;
            }

            DevToolsSplitterColumn.Width = new GridLength(0);
            DevToolsColumn.Width = new GridLength(0);
            DevToolsSplitter.Visibility = Visibility.Collapsed;
        }

        private double ClampDevToolsWidth(double width)
        {
            var max = Math.Max(MinDevToolsWidth, ContentGrid.ActualWidth - SplitterWidth - MinPageWidth);
            return Math.Clamp(width, MinDevToolsWidth, max);
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

        private bool _isDraggingDevToolsSplitter;
        private double _dragStartPointerX;
        private double _dragStartDevToolsWidth;

        private void OnDevToolsSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingDevToolsSplitter = true;
            _dragStartPointerX = e.GetCurrentPoint(RootGrid).Position.X;
            _dragStartDevToolsWidth = DevToolsColumn.Width.Value;
            ((UIElement)sender).CapturePointer(e.Pointer);
        }

        private void OnDevToolsSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingDevToolsSplitter)
            {
                return;
            }

            // Dragging left widens DevTools.
            var delta = e.GetCurrentPoint(RootGrid).Position.X - _dragStartPointerX;
            DevToolsColumn.Width = new GridLength(ClampDevToolsWidth(_dragStartDevToolsWidth - delta));
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
                NavigationBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
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
