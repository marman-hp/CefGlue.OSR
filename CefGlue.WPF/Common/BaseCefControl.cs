using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.WPF.Common
{
    public abstract class BaseCefControl : ContentControl
    {
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_SIZE = 0x0005;
        private const int SIZE_MINIMIZED = 1;

        private HwndSource _hwndSource;
        private CefBrowser _browser;

        public event Action SafeResize;

        public event Action ResizeBegin;

        public bool IsResizing { get; private set; }

        public bool UseSafeResize { get; set; }

        public bool IsContextMenuOpen { get; set; }

        public CefBrowser Browser => _browser;

        public void EndResize()
        {
            SafeResize?.Invoke();
        }

        public void BeginResize()
        {
            ResizeBegin?.Invoke();
        }

        public void AttachBrowser(CefBrowser browser) => _browser = browser;

        protected BaseCefControl()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_hwndSource != null)
            {
                return;
            }

            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }

        protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_ENTERSIZEMOVE:
                    IsResizing = true;
                    ResizeBegin?.Invoke();
                    break;

                case WM_EXITSIZEMOVE:
                    IsResizing = false;
                    SafeResize?.Invoke();
                    break;

                case WM_SIZE:
                    if (!IsResizing && wParam.ToInt32() != SIZE_MINIMIZED)
                    {
                        ResizeBegin?.Invoke();
                        SafeResize?.Invoke();
                    }
                    break;
            }

            return IntPtr.Zero;
        }
    }
}
