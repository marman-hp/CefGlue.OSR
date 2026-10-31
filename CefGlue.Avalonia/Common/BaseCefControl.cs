using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia.Platform.Windows;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Avalonia.Common
{
    public abstract class BaseCefControl : ContentControl
    {
        protected override Type StyleKeyOverride => typeof(ContentControl);

        private CefBrowser _browser;

        public CefBrowser Browser => _browser;

        public void AttachBrowser(CefBrowser browser) => _browser = browser;

        public bool OSREnabled => CefRuntimeLoader.IsOSREnabled;

        public virtual void FocusContent()
        {
            Focus();
        }

        public bool IsContextMenuOpen { get; set; }

        public bool IsPopupSelectOpen { get; set; }

        private const int ResizeSettleMilliseconds = 50;

        private Window _hostWindow;
        private DispatcherTimer _resizeSettleTimer;
        private bool _nativeResizeHooked;
        private bool _nativeDragActive;

        public event Action SafeResize;

        public event Action ResizeBegin;

        public bool IsResizing { get; private set; }

        public bool UseSafeResize { get; set; }

        protected BaseCefControl()
        {
            ResizeBegin += () => Browser?.GetHost()?.NotifyMoveOrResizeStarted();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _hostWindow = TopLevel.GetTopLevel(this) as Window;
            if (_hostWindow != null)
            {
                _hostWindow.Resized += OnHostWindowResized;

                if (OperatingSystem.IsWindows())
                {
                    var hwnd = _hostWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        _nativeResizeHooked = WindowSubclassHelper.TryHook(hwnd, this, OnNativeWindowMessage);
                    }
                }
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (_hostWindow != null)
            {
                _hostWindow.Resized -= OnHostWindowResized;
                _hostWindow = null;
            }

            if (_nativeResizeHooked)
            {
                WindowSubclassHelper.Unhook(this);
                _nativeResizeHooked = false;
            }

            _resizeSettleTimer?.Stop();
        }

        private void OnHostWindowResized(object sender, WindowResizedEventArgs e)
        {
            if (!UseSafeResize)
            {
                return;
            }

            if (OperatingSystem.IsWindows() && _nativeResizeHooked)
            {
                if (_nativeDragActive)
                {
                    return;
                }

                if (!IsResizing)
                {
                    IsResizing = true;
                    ResizeBegin?.Invoke();
                }

                EndResize();
                return;
            }

            if (!IsResizing)
            {
                IsResizing = true;
                ResizeBegin?.Invoke();
            }

            if (_resizeSettleTimer == null)
            {
                _resizeSettleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(ResizeSettleMilliseconds),
                };
                _resizeSettleTimer.Tick += OnResizeSettleTimerTick;
            }

            _resizeSettleTimer.Stop();
            _resizeSettleTimer.Start();
        }

        private void OnNativeWindowMessage(WindowMessage message)
        {
            switch (message)
            {
                case WindowMessage.EnterSizeMove:
                    _nativeDragActive = true;
                    if (!IsResizing)
                    {
                        IsResizing = true;
                        ResizeBegin?.Invoke();
                    }
                    break;

                case WindowMessage.ExitSizeMove:
                    _nativeDragActive = false;
                    _resizeSettleTimer?.Stop();
                    EndResize();
                    break;
            }
        }

        private void OnResizeSettleTimerTick(object sender, EventArgs e)
        {
            _resizeSettleTimer.Stop();
            EndResize();
        }

        private void EndResize()
        {
            if (IsResizing)
            {
                IsResizing = false;
                SafeResize?.Invoke();
            }
        }
    }
}
