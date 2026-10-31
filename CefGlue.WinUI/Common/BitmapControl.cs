using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.WinUI.Common
{
    internal sealed class BitmapControl : BaseCefControl, IFrameSurfaceHost<byte[]>
    {
        private readonly Grid _root;
        private readonly Image _image;
        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;

        private IFrameSurface<byte[]> _surface;
        private WriteableBitmap _bitmap;

        private byte[] _scratch;

        private readonly object _presentLock = new object();
        private bool _presentPending;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        private bool _isResizeGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;

        public BitmapControl()
        {
            UseSafeResize = true;
            IsTabStop = true;

            _image = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            _fpsText = new TextBlock
            {
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            };

            _fpsOverlay = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(150, 128, 0, 128)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _fpsText,
                Visibility = Visibility.Collapsed,
            };

            _root = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            _root.Children.Add(_image);
            _root.Children.Add(_fpsOverlay);
            Content = _root;

            ResizeBegin += OnResizeBegin;
            SafeResize += OnResizeEnd;
            SizeChanged += OnControlSizeChanged;
        }

        public void AttachSurface(IFrameSurface<byte[]> surface)
        {
            _surface = surface;
            _surface.FrameReady = OnFrameReady;
        }

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
                (int)ActualWidth == _preResizeWidth &&
                (int)ActualHeight == _preResizeHeight)
            {
                _isResizeGhosting = false;
                ApplyStretch();
            }
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsResizing)
            {
                if (_isResizeGhosting)
                {
                    ApplyStretch();
                }
                return;
            }

            OnResizeBegin();
            OnResizeEnd();
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

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, Present);
        }

        private unsafe void Present()
        {
            lock (_presentLock)
            {
                _presentPending = false;
            }

            if (_surface == null || !IsLoaded || XamlRoot == null)
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

                var stride = width * 4;
                var byteCount = stride * height;

                if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                {
                    _bitmap = new WriteableBitmap(width, height);
                    _image.Source = _bitmap;

                    _scratch = new byte[byteCount];
                }

                var dirtyRect = new CefRectangle();
                var scratch = _scratch;
                bool presented;

                fixed (byte* p = scratch)
                {
                    _surface.CopyFrameSurfaceInto((System.IntPtr)p, stride, width, height, ref dirtyRect);
                }

                presented = dirtyRect.Width > 0 && dirtyRect.Height > 0;

                if (presented)
                {
                    using var pixelStream = _bitmap.PixelBuffer.AsStream();
                    var rowBytes = dirtyRect.Width * 4;

                    for (var y = 0; y < dirtyRect.Height; y++)
                    {
                        var rowOffset = (dirtyRect.Y + y) * stride + dirtyRect.X * 4;
                        pixelStream.Seek(rowOffset, SeekOrigin.Begin);
                        pixelStream.Write(scratch, rowOffset, rowBytes);
                    }
                }

                if (!presented)
                {
                    continue;
                }

                _bitmap.Invalidate();

                if (_surface.ShowOverlayInfo)
                {
                    UpdateInfoOverlay();
                }
                else if (_fpsOverlay.Visibility == Visibility.Visible)
                {
                    _fpsOverlay.Visibility = Visibility.Collapsed;
                }

                ApplyStretch();
                return;
            }
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

        private void ApplyStretch()
        {
            if (_isResizeGhosting)
            {
                _image.Stretch = Stretch.Fill;
                _image.Width = ActualWidth;
                _image.Height = ActualHeight;
            }
            else
            {
                _image.Stretch = Stretch.None;
                _image.Width = double.NaN;
                _image.Height = double.NaN;
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
    }
}
