using System;
using Avalonia.Controls;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.Avalonia
{
    /// <summary>
    /// Default <see cref="IPopupPresenter{TBrowser}"/> for Avalonia: shows a window.open popup in its own Window,
    /// owned by the opener's window, at the size the page asked for (808x600 when it didn't).
    /// </summary>
    public sealed class WindowPopupPresenter : IPopupPresenter<CefGlueBrowser>
    {
        public IPopupHost Show(PopupRequest<CefGlueBrowser> request)
        {
            var window = new Window
            {
                Title = "Popup",
                Width = request.Width ?? 808,
                Height = request.Height ?? 600,
                Content = request.Popup.Control,
            };

            var host = new WindowPopupHost(window);

            if (TopLevel.GetTopLevel(request.Opener.Control) is Window ownerWindow)
            {
                window.Show(ownerWindow);
            }
            else
            {
                window.Show();
            }

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
