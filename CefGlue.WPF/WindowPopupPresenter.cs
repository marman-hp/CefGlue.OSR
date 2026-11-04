using System;
using System.Windows;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.WPF
{
    /// <summary>
    /// Default <see cref="IPopupPresenter{TBrowser}"/> for WPF: shows a window.open popup in its own Window,
    /// owned by the opener's window, at the size the page asked for (808x600 when it didn't).
    /// </summary>
    public sealed class WindowPopupPresenter : IPopupPresenter<CefGlueBrowser>
    {
        public IPopupHost Show(PopupRequest<CefGlueBrowser> request)
        {
            var window = new Window
            {
                Width = request.Width ?? 808,
                Height = request.Height ?? 600,
                Content = request.Popup.Control,
                Owner = Window.GetWindow(request.Opener.Control),
            };

            var host = new WindowPopupHost(window);
            window.Show();
            window.Activate();
            return host;
        }

        private sealed class WindowPopupHost : IPopupHost
        {
            private readonly Window _window;
            private bool _closed;

            public WindowPopupHost(Window window)
            {
                _window = window;
                // Closing, not Closed: the browser is closed while its control is still in the window.
                _window.Closing += (s, e) =>
                {
                    if (!e.Cancel)
                    {
                        RaiseClosed();
                    }
                };
            }

            public event Action Closed;

            public void Close()
            {
                if (!_closed)
                {
                    _window.Close();
                }
            }

            private void RaiseClosed()
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                Closed?.Invoke();
            }
        }
    }
}
