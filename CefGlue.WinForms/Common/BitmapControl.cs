using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.WinForms;
using Xilium.CefGlue.WinForms.Common;
using Xilium.CefGlue.WinForms.Platform;

namespace Xilium.CefGlue.Common
{
    public partial class BitmapControl : BaseCefControl, IFrameSurfaceHost<Bitmap>
    {
        private IFrameSurface<Bitmap> _surface;
        private Bitmap _backBufferBmp;
        private readonly object _renderLock = new object();

        private readonly object _popupLock = new object();
        private Bitmap _popupBackBufferBmp;
        private Rectangle _popupRect;
        private bool _popupVisible;

        private IntPtr _popupHBitmap = IntPtr.Zero;
        private IntPtr _popupMemHdc = IntPtr.Zero;
        private IntPtr _popupOldBitmap = IntPtr.Zero;

        internal void UpdatePopupFrame(Bitmap frame)
        {
            lock (_popupLock)
            {
                var old = _popupBackBufferBmp;
                _popupBackBufferBmp = frame;

                ReleasePopupGdiResourcesLocked();

                _popupHBitmap = frame.GetHbitmap();
                _popupMemHdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
                _popupOldBitmap = NativeMethods.SelectObject(_popupMemHdc, _popupHBitmap);

                old?.Dispose();
            }

            OnPopupPresentationChanged(_popupBackBufferBmp, _popupRect, _popupVisible);

            if (IsHandleCreated && !IsDisposed)
            {
                Invalidate(_popupRect);
            }
        }

        private void ReleasePopupGdiResourcesLocked()
        {
            if (_popupMemHdc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_popupMemHdc, _popupOldBitmap);
                NativeMethods.DeleteDC(_popupMemHdc);
                _popupMemHdc = IntPtr.Zero;
            }

            if (_popupHBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_popupHBitmap);
                _popupHBitmap = IntPtr.Zero;
            }

            _popupOldBitmap = IntPtr.Zero;
        }

        internal void SetPopupRect(Rectangle rect)
        {
            _popupRect = rect;

            OnPopupPresentationChanged(_popupBackBufferBmp, _popupRect, _popupVisible);

            if (IsHandleCreated && !IsDisposed)
            {
                Invalidate();
            }
        }

        internal void SetPopupVisible(bool visible)
        {
            _popupVisible = visible;

            OnPopupPresentationChanged(_popupBackBufferBmp, _popupRect, _popupVisible);

            if (IsHandleCreated && !IsDisposed)
            {
                Invalidate(_popupRect);
            }
        }

        protected virtual void OnPopupPresentationChanged(Bitmap frame, Rectangle rect, bool visible)
        {
        }

        private bool _isResizingOrMoving;

        private bool _isGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;

        private readonly bool _popupSelect;

        public BitmapControl(bool isPopupSelect = false)
        {
            _popupSelect = isPopupSelect;
            if (isPopupSelect)
                Visible = false;

            UseSafeResize = !isPopupSelect;
        }

        public override bool IsPopupSelect => _popupSelect;

        protected override bool CanEnableIme => TsfImeAttached;

        private bool _parentEventsWired;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            WireParentResizeEvents();
        }

        private Form _wiredForm;
        private EventHandler _formResizeBeginHandler;
        private EventHandler _formResizeHandler;
        private EventHandler _formResizeEndHandler;

        public void WireParentResizeEvents()
        {
            if (!UseSafeResize || Parent == null || _parentEventsWired)
                return;

            _parentEventsWired = true;
            ClientSize = PinnedSize ?? Parent.Size;
            SafeResize?.Invoke(this, EventArgs.Empty);

            var form = FindForm();
            if (form == null) return;

            _wiredForm = form;

            _formResizeBeginHandler = (s, args) => BeginResize();
            _formResizeHandler = (s, args) =>
            {
                if (!_isResizingOrMoving)
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() => SafeOnLayaoutState?.Invoke(this, form.WindowState));
                    }
                }
            };
            _formResizeEndHandler = (s, args) => EndResize();

            form.ResizeBegin += _formResizeBeginHandler;
            form.Resize += _formResizeHandler;
            form.ResizeEnd += _formResizeEndHandler;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_wiredForm != null)
            {
                if (_formResizeBeginHandler != null) _wiredForm.ResizeBegin -= _formResizeBeginHandler;
                if (_formResizeHandler != null) _wiredForm.Resize -= _formResizeHandler;
                if (_formResizeEndHandler != null) _wiredForm.ResizeEnd -= _formResizeEndHandler;
                _wiredForm = null;
            }

            base.OnHandleDestroyed(e);
        }

        public override void BeginResize()
        {
            _isResizingOrMoving = true;
            _resizeEnded = false;
            _isGhosting = true;
            _preResizeWidth = _surface?.Width ?? 0;
            _preResizeHeight = _surface?.Height ?? 0;
        }

        public override void EndResize()
        {
            _isResizingOrMoving = false;
            _resizeEnded = true;
            ClientSize = PinnedSize ?? Parent?.ClientSize ?? Size;
            SafeResize?.Invoke(this, EventArgs.Empty);

            if (_isGhosting && Width == _preResizeWidth && Height == _preResizeHeight)
            {
                _isGhosting = false;
            }

            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        void IFrameSurfaceHost<Bitmap>.AttachSurface(IFrameSurface<Bitmap> surface)
        {
            _surface = surface;
            InitSurface();
        }

        private volatile bool _frameReadyQueued;

        private void InitSurface()
        {
            _surface.FrameReady = _ =>
            {
                if (_frameReadyQueued)
                {
                    return;
                }
                _frameReadyQueued = true;

                BeginInvoke((Action)(() =>
                {
                    _frameReadyQueued = false;

                    if (IsDisposed || !IsHandleCreated || Width <= 0 || Height <= 0)
                    {
                        return;
                    }

                    UpdateMainFrame();
                    Invalidate();
                }));
            };
        }

        private void UpdateMainFrame()
        {
            var dirtyRect = new CefRectangle();

            lock (_renderLock)
            {
                using (var clonedBmp = _surface.GetFrameSurface(ref dirtyRect))
                {
                    if (clonedBmp == null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
                    {
                        return;
                    }

                    var bmpWidth = clonedBmp.Width;
                    var bmpHeight = clonedBmp.Height;

                    var isFullFrame = dirtyRect.X == 0 && dirtyRect.Y == 0 &&
                                       dirtyRect.Width >= bmpWidth && dirtyRect.Height >= bmpHeight;

                    if (_backBufferBmp == null || _backBufferBmp.Width != bmpWidth || _backBufferBmp.Height != bmpHeight)
                    {
                        _backBufferBmp?.Dispose();
                        _backBufferBmp = new Bitmap(bmpWidth, bmpHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                        using (var g = Graphics.FromImage(_backBufferBmp))
                        {
                            g.Clear(Color.White);
                        }
                    }

                    using (var gBack = Graphics.FromImage(_backBufferBmp))
                    {
                        gBack.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        gBack.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                        if (isFullFrame)
                        {
                            gBack.Clear(Color.White);
                            gBack.DrawImage(clonedBmp, 0, 0, clonedBmp.Width, clonedBmp.Height);
                        }
                        else
                        {
                            var destRect = new Rectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
                            var srcRect = new Rectangle(0, 0, clonedBmp.Width, clonedBmp.Height);

                            gBack.FillRectangle(Brushes.White, destRect);
                            gBack.DrawImage(clonedBmp, destRect, srcRect, GraphicsUnit.Pixel);
                        }
                    }

                    if (_surface.ShowOverlayInfo)
                    {
                        DrawInfoOverlay();
                    }
                }
            }
        }

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        protected override void WndProc(ref Message m)
        {

            switch (m.Msg)
            {
                case NativeMethods.WM_SETCURSOR:
                    int hitTest = m.LParam.ToInt32() & 0xFFFF;

                    if (hitTest == 1)
                    {
                        if (CursorHandler != IntPtr.Zero)
                        {
                            NativeMethods.SetCursor(CursorHandler);

                            m.Result = (IntPtr)1;
                            return;
                        }
                    }

                    break;

                case 0x0014:
                    m.Result = (IntPtr)1;
                    return;

                case NativeMethods.WM_PAINT:
                    NativeMethods.PAINTSTRUCT ps;
                    IntPtr hdc = NativeMethods.BeginPaint(m.HWnd, out ps);

                    var paintRect = Rectangle.FromLTRB(ps.rcPaint.Left, ps.rcPaint.Top, ps.rcPaint.Right, ps.rcPaint.Bottom);
                    var popupOnly = _popupVisible && _popupRect.Width > 0 && _popupRect.Height > 0 &&
                                     _popupRect.Contains(paintRect);

                    if (popupOnly)
                    {
                        PresentPopup(hdc);
                    }
                    else
                    {
                        Present(hdc, ps);
                    }

                    NativeMethods.EndPaint(m.HWnd, ref ps);

                    m.Result = IntPtr.Zero;
                    return;

            }

            base.WndProc(ref m);
        }

        private void Present(IntPtr hdc, NativeMethods.PAINTSTRUCT ps)
        {
            const int SRCCOPY = 0x00CC0020;

            lock (_renderLock)
            {
                if (_isGhosting && _resizeEnded && _surface.Width == Width && _surface.Height == Height)
                {
                    _isGhosting = false;
                }

                if (_backBufferBmp == null)
                {
                    return;
                }

                IntPtr hBmp = _backBufferBmp.GetHbitmap();
                IntPtr memHdc = NativeMethods.CreateCompatibleDC(hdc);
                IntPtr oldBmp = NativeMethods.SelectObject(memHdc, hBmp);

                if (_isGhosting)
                {
                    const int COLORONCOLOR = 3;
                    NativeMethods.SetStretchBltMode(hdc, COLORONCOLOR);
                    NativeMethods.StretchBlt(hdc, 0, 0, Width, Height, memHdc, 0, 0, _backBufferBmp.Width, _backBufferBmp.Height, SRCCOPY);
                }
                else
                {
                    int clipX = ps.rcPaint.Left;
                    int clipY = ps.rcPaint.Top;
                    int clipW = ps.rcPaint.Right - ps.rcPaint.Left;
                    int clipH = ps.rcPaint.Bottom - ps.rcPaint.Top;

                    NativeMethods.BitBlt(hdc, clipX, clipY, clipW, clipH, memHdc, clipX, clipY, SRCCOPY);
                }
                NativeMethods.SelectObject(memHdc, oldBmp);
                NativeMethods.DeleteDC(memHdc);
                NativeMethods.DeleteObject(hBmp);
            }

            PresentPopup(hdc);
        }

        private void PresentPopup(IntPtr hdc)
        {
            if (!_popupVisible)
            {
                return;
            }

            const int SRCCOPY = 0x00CC0020;

            lock (_popupLock)
            {
                if (_popupMemHdc == IntPtr.Zero || _popupRect.Width <= 0 || _popupRect.Height <= 0)
                {
                    return;
                }

                NativeMethods.BitBlt(hdc, _popupRect.X, _popupRect.Y, _popupRect.Width, _popupRect.Height,
                    _popupMemHdc, 0, 0, SRCCOPY);
            }
        }

        private void DrawInfoOverlay()
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _frameCount++;
            var currentElapsed = _fpsStopwatch.ElapsedMilliseconds;

            if (currentElapsed - _lastFpsElapsed >= 1000)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsElapsed = currentElapsed;
            }

            var text =
                $"FPS         : {_fps}\n" +
                $"Texture     : {(_surface.IsAccelerated ? "Enable" : "Disable")}\n" +
                $"CEF version : {OverlayDiagnostics.CefVersion}\n" +
                $"Processor   : {OverlayDiagnostics.ProcessorName}\n" +
                $"GPU         : {OverlayDiagnostics.GpuName}\n" +
                $"Mem Usage   : {OverlayDiagnostics.MemoryUsageMb:F1} MB\n" +
                $"CPU Usage   : {OverlayDiagnostics.CpuUsagePercent:F1} %\n" +
                $"GPU Usage   : {OverlayDiagnostics.GpuUsagePercent:F1} %";

            using (var g = Graphics.FromImage(_backBufferBmp))
            using (var font = new Font("Consolas", 9, FontStyle.Regular))
            using (var background = new SolidBrush(Color.FromArgb(150, Color.Purple)))
            {
                var size = g.MeasureString(text, font);
                var boxWidth = size.Width + 12;
                var boxHeight = size.Height + 8;

                g.FillRectangle(background, _backBufferBmp.Width - boxWidth - 10, 10, boxWidth, boxHeight);
                g.DrawString(text, font, Brushes.White, _backBufferBmp.Width - boxWidth - 4, 14);
            }
        }

        public void RefreshOverlay()
        {
            Browser?.GetHost()?.Invalidate(CefPaintElementType.View);
        }

        private void DisposeRenderState()
        {
            if (_surface != null)
            {
                _surface.FrameReady = null;
            }

            lock (_renderLock)
            {
                _backBufferBmp?.Dispose();
                _backBufferBmp = null;
            }

            lock (_popupLock)
            {
                ReleasePopupGdiResourcesLocked();
                _popupBackBufferBmp?.Dispose();
                _popupBackBufferBmp = null;
            }
        }
    }
}
