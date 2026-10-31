using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.WinUI;

namespace Xilium.CefGlue.Demo.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly CefGlueBrowser _browser;

        private bool _closed;

        public MainWindow()
        {
            InitializeComponent();
            Title = "CefGlue.WinUI Demo";

            _browser = new CefGlueBrowser(
                setupCefBrowserSettings: () => new CefBrowserSettings
                {
                    WindowlessFrameRate = 60,
                },
                setupCefWindowInfo: () =>
                {
                    var windowInfo = CefWindowInfo.Create();
                    windowInfo.SharedTextureEnabled = true;
                    return windowInfo;
                });

            _browser.HostWindow = this;

            _browser.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            _browser.Control.VerticalAlignment = VerticalAlignment.Stretch;
            BrowserContainer.Children.Add(_browser.Control);

            _browser.LoadStart += OnBrowserLoadStart;
            _browser.LoadEnd += OnBrowserFirstLoadEnd;
            _browser.TitleChanged += OnBrowserTitleChanged;
            _browser.FullscreenModeChanged += OnBrowserFullscreenModeChanged;

            _browser.Address = "https://www.google.com";

            Closed += OnWindowClosed;
        }

        private void OnBrowserLoadStart(object sender, LoadStartEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed)
                {
                    return;
                }

                try
                {
                    AddressTextBox.Text = e.Frame.Url;
                }
                catch (COMException)
                {
                }
            });
        }

        private void OnBrowserFirstLoadEnd(object sender, LoadEndEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            _browser.LoadEnd -= OnBrowserFirstLoadEnd;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed)
                {
                    return;
                }

                try
                {
                    _browser.Control.Focus(FocusState.Programmatic);
                }
                catch (COMException)
                {
                }
            });
        }

        private void OnBrowserTitleChanged(object sender, string title)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed)
                {
                    return;
                }

                try
                {
                    Title = string.IsNullOrEmpty(title) ? "CefGlue.WinUI Demo" : title;
                }
                catch (COMException)
                {
                }
            });
        }

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed)
                {
                    return;
                }

                Toolbar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        private void OnAddressTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                _browser.Address = AddressTextBox.Text;
            }
        }

        private void OnGoButtonClick(object sender, RoutedEventArgs e)
        {
            _browser.Address = AddressTextBox.Text;
        }

        private void OnReloadButtonClick(object sender, RoutedEventArgs e)
        {
            _browser.Reload();
        }

        private void OnOpenDevToolsButtonClick(object sender, RoutedEventArgs e)
        {
            _browser.ShowDeveloperTools();
        }

        private void OnWindowClosed(object sender, WindowEventArgs e)
        {
            _closed = true;
            _browser.Dispose();

            Application.Current.Exit();
        }
    }
}
