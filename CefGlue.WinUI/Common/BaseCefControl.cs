using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.WinUI.Platform;

namespace Xilium.CefGlue.WinUI.Common
{
    public abstract class BaseCefControl : UserControl
    {
        private CefBrowser _browser;
        private Window _hostWindow;
        private bool _hooked;

        public event Action SafeResize;

        public event Action ResizeBegin;

        public bool IsResizing { get; private set; }

        public bool UseSafeResize { get; set; }

        public bool IsContextMenuOpen { get; set; }

        public CefBrowser Browser => _browser;

        public void AttachBrowser(CefBrowser browser) => _browser = browser;

        public bool OSREnabled => CefRuntimeLoader.IsOSREnabled;

        public void ApplyCursor(InputCursor cursor)
        {
            ProtectedCursor = cursor;
        }

        public Window HostWindow
        {
            get => _hostWindow;
            set
            {
                if (_hostWindow == value)
                {
                    return;
                }

                UnhookResize();
                _hostWindow = value;
                HookResize();
            }
        }

        protected BaseCefControl()
        {
            Loaded += (_, _) => QueueResizeHookReconcile();
            Unloaded += (_, _) => QueueResizeHookReconcile();
        }

        private bool _resizeHookReconcileQueued;

        private void QueueResizeHookReconcile()
        {
            if (_resizeHookReconcileQueued)
            {
                return;
            }

            _resizeHookReconcileQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _resizeHookReconcileQueued = false;
                if (IsLoaded)
                {
                    HookResize();
                }
                else
                {
                    UnhookResize();
                }
            });
        }

        private void HookResize()
        {
            if (_hooked || _hostWindow == null)
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(_hostWindow);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _hooked = WindowSubclassHelper.TryHook(hwnd, this, OnWindowMessage);
        }

        private void UnhookResize()
        {
            if (!_hooked)
            {
                return;
            }

            WindowSubclassHelper.Unhook(this);
            _hooked = false;
            IsResizing = false;
        }

        private void OnWindowMessage(WindowMessage message)
        {
            switch (message)
            {
                case WindowMessage.EnterSizeMove:
                    IsResizing = true;
                    ResizeBegin?.Invoke();
                    break;

                case WindowMessage.ExitSizeMove:
                    IsResizing = false;
                    SafeResize?.Invoke();
                    break;
            }
        }
    }
}
