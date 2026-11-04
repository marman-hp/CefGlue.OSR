using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.WinUI
{
    /// <summary>
    /// Default <see cref="IPopupPresenter{TBrowser}"/> for WinUI: shows a window.open popup in its own Window,
    /// owned by the opener's window, at the size the page asked for (808x600 when it didn't).
    /// </summary>
    public sealed class WindowPopupPresenter : IPopupPresenter<CefGlueBrowser>
    {
        private const int GWLP_HWNDPARENT = -8;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public IPopupHost Show(PopupRequest<CefGlueBrowser> request)
        {
            var popup = request.Popup;
            var opener = request.Opener;

            var window = new Window { Title = "Popup" };
            popup.HostWindow = window;

            var root = new Grid();
            root.Children.Add(popup.Control);
            window.Content = root;

            if (opener.HostWindow != null)
            {
                SetWindowLongPtr(WindowNative.GetWindowHandle(window), GWLP_HWNDPARENT, WindowNative.GetWindowHandle(opener.HostWindow));
            }

            var host = new WindowPopupHost(window);
            window.Activate();
            window.AppWindow?.Resize(new Windows.Graphics.SizeInt32(request.Width ?? 808, request.Height ?? 600));
            return host;
        }

        private sealed class WindowPopupHost : IPopupHost
        {
            private readonly Window _window;
            private bool _closed;

            public WindowPopupHost(Window window)
            {
                _window = window;
                _window.Closed += (s, e) => RaiseClosed();
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
