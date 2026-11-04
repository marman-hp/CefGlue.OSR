using System;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// Owns the actual pixel buffers for an offscreen frame - a back/front double buffer.
    /// Mirrors CefGlue.Avalonia's AvaloniaBitmapSurface (see its remarks for why a back/front
    /// split, instead of patching dirty rects straight into a buffer the UI reads directly) -
    /// entirely platform-agnostic, ported near verbatim.
    /// </summary>
    internal class GtkBitmapSurface : IFrameSurface<byte[]>, IDisposable
    {
        private const int BytesPerPixel = 4;

        private readonly object _frontLock = new object();

        private byte[] _back;
        private byte[] _front;

        private int _width;
        private int _height;
        private int _stride;
        private CefRectangle _lastDirtyRegion;

        public Action<CefRectangle> FrameReady { get; set; }

        public bool ShowOverlayInfo { get; set; } = true;

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
            }

            FrameReady?.Invoke(_lastDirtyRegion);
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

        public void CopyFrameSurfaceInto(IntPtr destination, int destinationStride, int destinationWidth, int destinationHeight, ref CefRectangle dirtyRect)
        {
            dirtyRect = default;
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
