using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Runtime.InteropServices;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia.Common
{
    internal class BitmapControl : BaseCefControl, IFrameSurfaceHost<byte[]>
    {
        private readonly Border _focusProxy;
        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;
        private IFrameSurface<byte[]> _surface;

        private readonly object _bufferLock = new object();
        private IntPtr _nativeBuffer = IntPtr.Zero;
        private int _bufferCapacityBytes;
        private int _width;
        private int _height;
        private bool _hasFrame;

        private readonly object _presentLock = new object();
        private bool _presentPending;
        private bool _disposed;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        private bool _isResizeGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;

        private const int RingSize = 4;
        private readonly IntPtr[] _copyBuffers = new IntPtr[RingSize];
        private readonly int[] _copyBufferCapacities = new int[RingSize];
        private int _nextCopyBufferIndex;

        public BitmapControl()
        {
            _focusProxy = new Border
            {
                Background = Brushes.Transparent,
                Focusable = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            _fpsText = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,Ubuntu Mono,monospace"),
                FontWeight = FontWeight.Normal,
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
                IsVisible = false,
            };

            var grid = new Grid();
            grid.Children.Add(_focusProxy);
            grid.Children.Add(_fpsOverlay);

            Content = grid;

            ResizeBegin += OnResizeBegin;
            SafeResize += OnResizeEnd;
        }

        public override void FocusContent() => _focusProxy.Focus();

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
                (int)Bounds.Width == _preResizeWidth &&
                (int)Bounds.Height == _preResizeHeight)
            {
                _isResizeGhosting = false;
            }
        }

        public void AttachSurface(IFrameSurface<byte[]> surface)
        {
            _surface = surface;
            _surface.FrameReady = OnFrameReady;
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            if (_surface == null || _disposed)
            {
                return;
            }

            var width = _surface.Width;
            var height = _surface.Height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            lock (_bufferLock)
            {
                EnsureBufferCapacity(width, height);

                var d = new CefRectangle();
                unsafe
                {
                    _surface.CopyFrameSurfaceInto(_nativeBuffer, width * 4, width, height, ref d);
                }

                if (d.Width <= 0 || d.Height <= 0)
                {
                    return;
                }

                _width = width;
                _height = height;
                _hasFrame = true;
            }

            lock (_presentLock)
            {
                if (_presentPending)
                {
                    return;
                }

                _presentPending = true;
            }

            Dispatcher.UIThread.Post(RequestPresent, DispatcherPriority.Render);
        }

        private void RequestPresent()
        {
            lock (_presentLock)
            {
                _presentPending = false;
            }

            if (_disposed)
            {
                return;
            }

            if (_isResizeGhosting && _resizeEnded)
            {
                int w, h;
                lock (_bufferLock)
                {
                    w = _width;
                    h = _height;
                }

                if (w != _preResizeWidth || h != _preResizeHeight)
                {
                    _isResizeGhosting = false;
                }
            }

            if (_surface != null && _surface.ShowOverlayInfo)
            {
                UpdateInfoOverlay();
            }
            else if (_fpsOverlay.IsVisible)
            {
                _fpsOverlay.IsVisible = false;
            }

            InvalidateVisual();
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
                _fpsOverlay.IsVisible = false;
            }

            InvalidateVisual();
        }

        private const int SizeRoundingGrid = 64;

        private static int RoundUpToGrid(int value) =>
            ((value + SizeRoundingGrid - 1) / SizeRoundingGrid) * SizeRoundingGrid;

        private void EnsureBufferCapacity(int width, int height)
        {
            var required = width * height * 4;

            if (_nativeBuffer != IntPtr.Zero && required <= _bufferCapacityBytes)
            {
                return;
            }

            if (_nativeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_nativeBuffer);
            }

            var allocatedWidth = RoundUpToGrid(width);
            var allocatedHeight = RoundUpToGrid(height);
            var allocated = allocatedWidth * allocatedHeight * 4;

            _nativeBuffer = Marshal.AllocHGlobal(allocated);
            _bufferCapacityBytes = allocated;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            IntPtr bufferCopy;
            int width, height;

            lock (_bufferLock)
            {
                if (!_hasFrame || _nativeBuffer == IntPtr.Zero || _width <= 0 || _height <= 0)
                {
                    return;
                }

                width = _width;
                height = _height;

                var byteCount = width * height * 4;
                var slot = _nextCopyBufferIndex;
                _nextCopyBufferIndex = (_nextCopyBufferIndex + 1) % RingSize;

                if (_copyBuffers[slot] == IntPtr.Zero || _copyBufferCapacities[slot] < byteCount)
                {
                    if (_copyBuffers[slot] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_copyBuffers[slot]);
                    }

                    var allocatedBytes = RoundUpToGrid(width) * RoundUpToGrid(height) * 4;
                    _copyBuffers[slot] = Marshal.AllocHGlobal(allocatedBytes);
                    _copyBufferCapacities[slot] = allocatedBytes;
                }

                bufferCopy = _copyBuffers[slot];

                unsafe
                {
                    Buffer.MemoryCopy((void*)_nativeBuffer, (void*)bufferCopy, _copyBufferCapacities[slot], byteCount);
                }
            }

            var targetRect = _isResizeGhosting
                ? new Rect(0, 0, Bounds.Width, Bounds.Height)
                : new Rect(0, 0, width, height);

            context.Custom(new FrameDrawOp(targetRect, bufferCopy, width, height));
        }

        private void UpdateInfoOverlay()
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _fpsOverlay.IsVisible = true;

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

        public void ReleaseBuffer()
        {
            lock (_bufferLock)
            {
                _disposed = true;
                _hasFrame = false;

                if (_nativeBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_nativeBuffer);
                    _nativeBuffer = IntPtr.Zero;
                    _bufferCapacityBytes = 0;
                }

                for (var i = 0; i < RingSize; i++)
                {
                    if (_copyBuffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_copyBuffers[i]);
                        _copyBuffers[i] = IntPtr.Zero;
                        _copyBufferCapacities[i] = 0;
                    }
                }
            }
        }
    }

    internal sealed class FrameDrawOp : ICustomDrawOperation
    {
        private readonly IntPtr _buffer;
        private readonly int _width;
        private readonly int _height;

        public FrameDrawOp(Rect bounds, IntPtr buffer, int width, int height)
        {
            Bounds = bounds;
            _buffer = buffer;
            _width = width;
            _height = height;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (_buffer == IntPtr.Zero)
            {
                return;
            }

            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease == null)
            {
                return;
            }

            using var l = lease.Lease();
            var canvas = l.SkCanvas;

            var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var img = SKImage.FromPixels(info, _buffer, _width * 4);

            var destRect = new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height);
            canvas.DrawImage(img, destRect);
        }
    }
}
