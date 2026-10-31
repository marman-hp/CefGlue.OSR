using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;

namespace Xilium.CefGlue.Demo.Avalonia
{
    public partial class MainWindow : Window
    {
        private readonly CefGlueBrowser _browser;
        private bool _closed;

        public MainWindow()
        {
            InitializeComponent();
            Title = "CefGlue.Avalonia Demo";

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

            _browser.Address = "https://www.google.com";

            Closed += OnWindowClosed;
        }

        private void OnBrowserLoadStart(object sender, LoadStartEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_closed)
                {
                    return;
                }

                AddressTextBox.Text = e.Frame.Url;
            });
        }

        private void OnBrowserFirstLoadEnd(object sender, LoadEndEventArgs e)
        {
            if (!e.Frame.IsMain)
            {
                return;
            }

            _browser.LoadEnd -= OnBrowserFirstLoadEnd;

            Dispatcher.UIThread.Post(() =>
            {
                if (_closed)
                {
                    return;
                }

                _browser.Focus();
            });
        }

        private void OnBrowserTitleChanged(object sender, string title)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed)
                {
                    return;
                }

                Title = string.IsNullOrEmpty(title) ? "CefGlue.Avalonia Demo" : title;
            });
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
            _closed = true;
            _browser.Dispose();
        }
    }
}
