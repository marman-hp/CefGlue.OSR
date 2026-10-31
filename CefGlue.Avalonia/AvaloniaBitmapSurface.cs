using System;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia
{
    internal class AvaloniaBitmapSurface : IFrameSurface<byte[]>, IDisposable
    {
        private const int BytesPerPixel = 4;

        private readonly object _frontLock = new object();

        private byte[] _back;
        private byte[] _front;

        private int _width;
        private int _height;
        private int _stride;
        private CefRectangle _lastDirtyRegion;

        private CefRectangle _pendingDirtyRegion;
        private bool _hasPendingDirtyRegion;

        public bool UseOSRDirtyRect { get; set; } = true;

        public Action<CefRectangle> FrameReady { get; set; }

        public bool ShowOverlayInfo { get; set; } = false;

        public bool IsAccelerated { get; set; }

        public int Width => _width;

        public int Height => _height;

        public void BeginFrame(int width, int height)
        {
            var stride = width * BytesPerPixel;
            var back = new byte[(long)stride * height];
            var front = new byte[(long)stride * height];

            lock (_frontLock)
            {
                _width = width;
                _height = height;
                _stride = stride;
                _front = front;
                _lastDirtyRegion = new CefRectangle(0, 0, width, height);
                _pendingDirtyRegion = _lastDirtyRegion;
                _hasPendingDirtyRegion = true;
            }

            _back = back;
        }

        public void CopyFrame(IntPtr buffer, int stride, CefRectangle region)
        {
            CopyRegion(buffer, stride, region);
        }

        public void CopyDirtyFrame(IntPtr buffer, int stride, CefRectangle region)
        {
            CopyRegion(buffer, stride, region);
        }

        private unsafe void CopyRegion(IntPtr buffer, int stride, CefRectangle region)
        {
            var back = _back;

            if (back == null)
            {
                return;
            }

            if (region.X < 0 || region.Y < 0 ||
                region.X + region.Width > _width || region.Y + region.Height > _height)
            {
                return;
            }

            _lastDirtyRegion = region;

            var rowBytes = region.Width * BytesPerPixel;

            fixed (byte* dst = back)
            {
                var src = (byte*)buffer;

                for (var y = 0; y < region.Height; y++)
                {
                    var srcRow = src + (long)(region.Y + y) * stride + (long)region.X * BytesPerPixel;
                    var dstRow = dst + (long)(region.Y + y) * _stride + (long)region.X * BytesPerPixel;

                    Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                }
            }
        }

        public void EndFrame()
        {
            var back = _back;

            if (back == null)
            {
                return;
            }

            lock (_frontLock)
            {
                if (_front != null && _front.Length == back.Length)
                {
                    Buffer.BlockCopy(back, 0, _front, 0, back.Length);
                }

                _pendingDirtyRegion = _hasPendingDirtyRegion
                    ? Union(_pendingDirtyRegion, _lastDirtyRegion)
                    : _lastDirtyRegion;
                _hasPendingDirtyRegion = true;
            }

            FrameReady?.Invoke(_lastDirtyRegion);
        }

        private static CefRectangle Union(CefRectangle a, CefRectangle b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.X + a.Width, b.X + b.Width);
            var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new CefRectangle(x, y, right - x, bottom - y);
        }

        public byte[] GetFrameSurface(ref CefRectangle dirtyRect)
        {
            lock (_frontLock)
            {
                if (_front == null)
                {
                    return null;
                }

                dirtyRect = new CefRectangle(0, 0, _width, _height);
                return (byte[])_front.Clone();
            }
        }

        public unsafe void CopyFrameSurfaceInto(IntPtr destination, int destinationStride, int destinationWidth, int destinationHeight, ref CefRectangle dirtyRect)
        {
            lock (_frontLock)
            {
                if (_front == null)
                {
                    dirtyRect = default;
                    return;
                }

                if (_width != destinationWidth || _height != destinationHeight)
                {
                    dirtyRect = default;
                    return;
                }

                if (!UseOSRDirtyRect)
                {
                    dirtyRect = new CefRectangle(0, 0, _width, _height);

                    fixed (byte* src = _front)
                    {
                        if (destinationStride == _stride)
                        {
                            Buffer.MemoryCopy(src, (void*)destination, _front.Length, _front.Length);
                        }
                        else
                        {
                            var dst = (byte*)destination;

                            for (var y = 0; y < _height; y++)
                            {
                                Buffer.MemoryCopy(src + (long)y * _stride, dst + (long)y * destinationStride, _stride, _stride);
                            }
                        }
                    }

                    return;
                }

                if (!_hasPendingDirtyRegion)
                {
                    dirtyRect = default;
                    return;
                }

                var region = _pendingDirtyRegion;
                _hasPendingDirtyRegion = false;
                _pendingDirtyRegion = default;

                dirtyRect = region;

                var rowBytes = region.Width * BytesPerPixel;

                fixed (byte* src = _front)
                {
                    var dst = (byte*)destination;

                    for (var y = 0; y < region.Height; y++)
                    {
                        var srcRow = src + (long)(region.Y + y) * _stride + (long)region.X * BytesPerPixel;
                        var dstRow = dst + (long)(region.Y + y) * destinationStride + (long)region.X * BytesPerPixel;

                        Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_frontLock)
            {
                _front = null;
            }

            _back = null;
        }

        public void CopyFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            throw new NotImplementedException();
        }
    }
}
