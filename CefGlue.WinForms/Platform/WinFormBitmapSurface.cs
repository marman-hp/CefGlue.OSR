using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.WinForms
{
    public class WinFormBitmapSurface : IFrameSurface<Bitmap>, IDisposable
    {
        private readonly object _lock = new object();

        private Bitmap _front;
        private Bitmap _back;
        private CefRectangle _lastDirtyRegion;

        public Action<CefRectangle> FrameReady { get; set; }

        public bool ShowOverlayInfo { get; set; } = false;

        public bool IsAccelerated { get; set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public void BeginFrame(int width, int height)
        {
            lock (_lock)
            {
                var oldBack = _back;
                var oldFront = _front;

                _back = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                _front = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

                Width = width;
                Height = height;
                _lastDirtyRegion = new CefRectangle(0, 0, width, height);

                oldBack?.Dispose();
                oldFront?.Dispose();
            }
        }

        public unsafe void CopyFrame(IntPtr buffer, int stride, CefRectangle region)
        {
            CopyRegion(buffer, stride, region);
        }

        public unsafe void CopyDirtyFrame(IntPtr buffer, int stride, CefRectangle region)
        {
            CopyRegion(buffer, stride, region);
        }

        public void CopyFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects) { }

        private unsafe void CopyRegion(IntPtr buffer, int stride, CefRectangle region)
        {
            lock (_lock)
            {
                if (_back == null)
                {
                    return;
                }

                if (region.X < 0 || region.Y < 0 ||
                    region.X + region.Width > _back.Width ||
                    region.Y + region.Height > _back.Height)
                {
                    return;
                }

                _lastDirtyRegion = region;

                var rect = new Rectangle(0, 0, _back.Width, _back.Height);
                var data = _back.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

                try
                {
                    var srcSpan = new ReadOnlySpan<byte>((byte*)buffer, _back.Height * stride);
                    var dstSpan = new Span<byte>((byte*)data.Scan0, _back.Height * data.Stride);

                    var bytesPerRow = region.Width * 4;

                    for (var y = 0; y < region.Height; y++)
                    {
                        var srcOffset = (region.Y + y) * stride + region.X * 4;
                        var dstOffset = (region.Y + y) * data.Stride + region.X * 4;

                        srcSpan.Slice(srcOffset, bytesPerRow).CopyTo(dstSpan.Slice(dstOffset, bytesPerRow));
                    }
                }
                finally
                {
                    _back.UnlockBits(data);
                }

                if (_front == null)
                {
                    return;
                }

                var lockRect = new Rectangle(0, 0, _back.Width, _back.Height);
                var srcData = _back.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                var dstData = _front.LockBits(lockRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

                try
                {
                    var totalByteSize = (long)_back.Height * srcData.Stride;
                    Buffer.MemoryCopy(srcData.Scan0.ToPointer(), dstData.Scan0.ToPointer(), totalByteSize, totalByteSize);
                }
                finally
                {
                    _back.UnlockBits(srcData);
                    _front.UnlockBits(dstData);
                }
            }
        }

        public void EndFrame()
        {
            if (_front != null)
            {
                FrameReady?.Invoke(_lastDirtyRegion);
            }
        }

        public void CopyFrameSurfaceInto(IntPtr destination, int destinationStride, int destinationWidth, int destinationHeight, ref CefRectangle dirtyRect)
        {
            throw new NotSupportedException($"{nameof(WinFormBitmapSurface)} does not support {nameof(CopyFrameSurfaceInto)} - use {nameof(GetFrameSurface)} instead.");
        }

        public Bitmap GetFrameSurface(ref CefRectangle dirtyRect)
        {
            lock (_lock)
            {
                if (_front == null)
                {
                    return null;
                }

                dirtyRect = new CefRectangle(0, 0, _front.Width, _front.Height);
                return CopyBitmapRegion(_front, dirtyRect);
            }
        }

        private static unsafe Bitmap CopyBitmapRegion(Bitmap src, CefRectangle rect)
        {
            var srcRect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
            var dst = new Bitmap(rect.Width, rect.Height, src.PixelFormat);

            var srcData = src.LockBits(srcRect, ImageLockMode.ReadOnly, src.PixelFormat);
            var dstData = dst.LockBits(new Rectangle(0, 0, rect.Width, rect.Height), ImageLockMode.WriteOnly, src.PixelFormat);

            try
            {
                var bytesPerPixel = Image.GetPixelFormatSize(src.PixelFormat) / 8;
                var rowBytes = rect.Width * bytesPerPixel;

                for (var y = 0; y < rect.Height; y++)
                {
                    var srcRow = (byte*)srcData.Scan0 + (y * srcData.Stride);
                    var dstRow = (byte*)dstData.Scan0 + (y * dstData.Stride);

                    Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                }
            }
            finally
            {
                src.UnlockBits(srcData);
                dst.UnlockBits(dstData);
            }

            return dst;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _front?.Dispose();
                _back?.Dispose();
                _front = null;
                _back = null;
            }
        }
    }
}
