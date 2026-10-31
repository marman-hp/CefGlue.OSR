using System.Windows;
using System.Windows.Input;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.WPF;

namespace Xilium.CefGlue.Demo.WPF
{
    public partial class MainWindow : Window
    {
        private readonly CefGlueBrowser _browser;

        public MainWindow()
        {
            InitializeComponent();

            _browser = new CefGlueBrowser(
                setupCefBrowserSettings: () => new CefBrowserSettings
                {
                    WindowlessFrameRate = 60,
                });

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

            Dispatcher.BeginInvoke((System.Action)(() =>
            {
                AddressTextBox.Text = e.Frame.Url;
            }));
        }

        private void OnBrowserFirstLoadEnd(object sender, LoadEndEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            _browser.LoadEnd -= OnBrowserFirstLoadEnd;

            Dispatcher.BeginInvoke((System.Action)(() =>
            {
                _browser.Focus();
            }));
        }

        private void OnBrowserTitleChanged(object sender, string title)
        {
            Dispatcher.BeginInvoke((System.Action)(() =>
            {
                Title = string.IsNullOrEmpty(title) ? "CefGlue.WPF Demo" : title;
            }));
        }

        private void OnBrowserFullscreenModeChanged(object sender, bool fullscreen)
        {
            Dispatcher.BeginInvoke((System.Action)(() =>
            {
                Toolbar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
            }));
        }

        private void OnAddressTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
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

        private void OnWindowClosed(object sender, System.EventArgs e)
        {
            _browser.Dispose();
        }
    }
}
