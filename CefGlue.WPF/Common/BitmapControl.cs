using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.WPF.Platform;

namespace Xilium.CefGlue.WPF.Common
{
    public class BitmapControl : BaseCefControl, IFrameSurfaceHost<byte[]>
    {
        internal bool TsfImeAttached { get; set; }

        public event Action<string, int> ImeCompositionUpdated;

        public event Action<string> ImeCompositionCommitted;

        private readonly bool _isPopupSelect;

        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;
        private IFrameSurface<byte[]> _surface;
        private WriteableBitmap _bitmap;

        private readonly object _presentLock = new object();
        private bool _presentPending;

        private IFrameSurface<byte[]> _popupSurface;
        private WriteableBitmap _popupBitmap;
        private Rect _popupRect;
        private bool _popupVisible;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        public BitmapControl(bool isPopupSelect = false)
        {
            _isPopupSelect = isPopupSelect;
            UseSafeResize = !isPopupSelect;

            FocusVisualStyle = null;

            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

            _fpsText = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Normal,
                FontSize = 11,
            };

            _fpsOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 128, 0, 128)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _fpsText,
                Visibility = Visibility.Collapsed,
            };

            Content = _fpsOverlay;

            if (!isPopupSelect)
            {
                ResizeBegin += OnResizeBegin;
                SafeResize += OnResizeEnd;
            }
        }

        private bool _isResizeGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;

        private void OnResizeBegin()
        {
            _isResizeGhosting = true;
            _resizeEnded = false;
            _preResizeWidth = _surface?.Width ?? 0;
            _preResizeHeight = _surface?.Height ?? 0;
        }

        private void OnResizeEnd()
        {
            _resizeEnded = true;

            if (_isResizeGhosting &&
                (int)RenderSize.Width == _preResizeWidth &&
                (int)RenderSize.Height == _preResizeHeight)
            {
                _isResizeGhosting = false;
                InvalidateVisual();
            }
        }

        public bool IsPopupSelect => _isPopupSelect;

        public void AttachSurface(IFrameSurface<byte[]> surface)
        {
            _surface = surface;
            _surface.FrameReady = OnFrameReady;
        }

        public void RefreshOverlay()
        {
            if (_surface == null)
            {
                return;
            }

            if (_surface.ShowOverlayInfo)
            {
                UpdateInfoOverlay();
            }
            else
            {
                _fpsOverlay.Visibility = Visibility.Collapsed;
            }
        }

        internal void AttachPopupSurface(IFrameSurface<byte[]> surface)
        {
            _popupSurface = surface;
            _popupSurface.FrameReady = OnPopupFrameReady;
        }

        private void OnPopupFrameReady(CefRectangle dirtyRect)
        {
            Dispatcher.InvokeAsync(PresentPopup, DispatcherPriority.Render);
        }

        private void PresentPopup()
        {
            if (_popupSurface == null)
            {
                return;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var width = _popupSurface.Width;
                var height = _popupSurface.Height;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                if (_popupBitmap == null || _popupBitmap.PixelWidth != width || _popupBitmap.PixelHeight != height)
                {
                    _popupBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                }

                var dirtyRect = new CefRectangle();
                bool presented;

                _popupBitmap.Lock();
                try
                {
                    _popupSurface.CopyFrameSurfaceInto(_popupBitmap.BackBuffer, _popupBitmap.BackBufferStride, width, height, ref dirtyRect);
                    presented = dirtyRect.Width > 0 && dirtyRect.Height > 0;

                    if (presented)
                    {
                        _popupBitmap.AddDirtyRect(new Int32Rect(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height));
                    }
                }
                finally
                {
                    _popupBitmap.Unlock();
                }

                if (!presented)
                {
                    continue;
                }

                if (_popupVisible)
                {
                    InvalidateVisual();
                }

                return;
            }
        }

        internal void SetPopupRect(Rect rect)
        {
            _popupRect = rect;
            InvalidateVisual();
        }

        internal void SetPopupVisible(bool visible)
        {
            _popupVisible = visible;
            InvalidateVisual();
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            lock (_presentLock)
            {
                if (_presentPending)
                {
                    return;
                }

                _presentPending = true;
            }

            Dispatcher.InvokeAsync(Present, DispatcherPriority.Render);
        }

        private void Present()
        {
            lock (_presentLock)
            {
                _presentPending = false;
            }

            if (_surface == null)
            {
                return;
            }

            if (_isResizeGhosting && _resizeEnded &&
                (_surface.Width != _preResizeWidth || _surface.Height != _preResizeHeight))
            {
                _isResizeGhosting = false;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var width = _surface.Width;
                var height = _surface.Height;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                {
                    _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                }

                var dirtyRect = new CefRectangle();
                bool presented;

                _bitmap.Lock();
                try
                {
                    _surface.CopyFrameSurfaceInto(_bitmap.BackBuffer, _bitmap.BackBufferStride, width, height, ref dirtyRect);
                    presented = dirtyRect.Width > 0 && dirtyRect.Height > 0;

                    if (presented)
                    {
                        _bitmap.AddDirtyRect(new Int32Rect(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height));
                    }
                }
                finally
                {
                    _bitmap.Unlock();
                }

                if (!presented)
                {
                    continue;
                }

                if (_surface.ShowOverlayInfo)
                {
                    UpdateInfoOverlay();
                }
                else
                {
                    _fpsOverlay.Visibility = Visibility.Collapsed;
                }

                InvalidateVisual();
                return;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            var bitmap = _bitmap;
            if (bitmap == null)
            {
                return;
            }

            var targetRect = _isResizeGhosting
                ? new Rect(0, 0, RenderSize.Width, RenderSize.Height)
                : new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);

            drawingContext.DrawImage(bitmap, targetRect);

            if (_popupVisible && _popupBitmap != null)
            {
                drawingContext.DrawImage(_popupBitmap, _popupRect);
            }
        }

        private void UpdateInfoOverlay()
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _fpsOverlay.Visibility = Visibility.Visible;

            _frameCount++;
            var currentElapsed = _fpsStopwatch.ElapsedMilliseconds;

            if (currentElapsed - _lastFpsElapsed >= 1000)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsElapsed = currentElapsed;
            }

            _fpsText.Text =
                $"FPS         : {_fps}\n" +
                $"Texture     : {(_surface.IsAccelerated ? "Enable" : "Disable")}\n" +
                $"CEF version : {OverlayDiagnostics.CefVersion}\n" +
                $"Processor   : {OverlayDiagnostics.ProcessorName}\n" +
                $"GPU         : {OverlayDiagnostics.GpuName}\n" +
                $"Mem Usage   : {OverlayDiagnostics.MemoryUsageMb:F1} MB\n" +
                $"CPU Usage   : {OverlayDiagnostics.CpuUsagePercent:F1} %\n" +
                $"GPU Usage   : {OverlayDiagnostics.GpuUsagePercent:F1} %";
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_IME_STARTCOMPOSITION = 0x010D;
            const int WM_IME_ENDCOMPOSITION = 0x010E;
            const int WM_IME_CHAR = 0x0286;

            if (TsfImeAttached &&
                (msg == WM_IME_STARTCOMPOSITION || msg == WM_IME_ENDCOMPOSITION ||
                 msg == NativeMethods.WM_IME_COMPOSITION || msg == WM_IME_CHAR))
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_IME_COMPOSITION)
            {
                HandleImeComposition(hwnd, (int)lParam);
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        private void HandleImeComposition(IntPtr hWnd, int compositionFlags)
        {
            var himc = NativeMethods.ImmGetContext(hWnd);
            if (himc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if ((compositionFlags & NativeMethods.GCS_RESULTSTR) != 0)
                {
                    var resultText = ReadImeCompositionString(himc, NativeMethods.GCS_RESULTSTR);
                    if (!string.IsNullOrEmpty(resultText))
                    {
                        ImeCompositionCommitted?.Invoke(resultText);
                    }
                }
                else if ((compositionFlags & NativeMethods.GCS_COMPSTR) != 0)
                {
                    var compositionText = ReadImeCompositionString(himc, NativeMethods.GCS_COMPSTR);

                    var cursorPosition = NativeMethods.ImmGetCompositionString(himc, NativeMethods.GCS_CURSORPOS, null, 0);

                    ImeCompositionUpdated?.Invoke(compositionText, cursorPosition);
                }
            }
            finally
            {
                NativeMethods.ImmReleaseContext(hWnd, himc);
            }
        }

        private static string ReadImeCompositionString(IntPtr himc, int gcsIndex)
        {
            var byteCount = NativeMethods.ImmGetCompositionString(himc, gcsIndex, null, 0);
            if (byteCount <= 0)
            {
                return string.Empty;
            }

            var buffer = new byte[byteCount];
            NativeMethods.ImmGetCompositionString(himc, gcsIndex, buffer, byteCount);
            return System.Text.Encoding.Unicode.GetString(buffer);
        }
    }
}
