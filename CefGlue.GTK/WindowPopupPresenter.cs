using System;
using Gtk;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// Default <see cref="IPopupPresenter{TBrowser}"/> for GTK: shows a window.open popup in its own Gtk.Window,
    /// transient for the opener's window, at the size the page asked for (808x600 when it didn't).
    /// </summary>
    public sealed class WindowPopupPresenter : IPopupPresenter<CefGlueBrowser>
    {
        public IPopupHost Show(PopupRequest<CefGlueBrowser> request)
        {
            var window = Window.New();
            window.Title = "Popup";
            window.SetDefaultSize(request.Width ?? 808, request.Height ?? 600);
            window.Child = request.Popup.Widget;

            if (request.Opener.Widget.GetRoot() is Window owner)
            {
                window.TransientFor = owner;
                window.Application = owner.Application;
            }

            var host = new WindowPopupHost(window);
            window.Present();
            return host;
        }

        private sealed class WindowPopupHost : IPopupHost
        {
            private readonly Window _window;
            private bool _closed;

            public WindowPopupHost(Window window)
            {
                _window = window;
                _window.OnCloseRequest += (_, _) =>
                {
                    RaiseClosed();
                    return false;
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
