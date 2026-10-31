using System;
using System.Threading.Tasks;

namespace Xilium.CefGlue.Common.Helpers
{
    internal abstract class CpuStagedRenderSurface : OffScreenRenderSurface
    {
        private readonly CpuFrameStagingBuffer _staging = new CpuFrameStagingBuffer();

        private readonly object _deliveryLock = new object();

        protected abstract int BytesPerPixel { get; }

        protected abstract int RenderedWidth { get; }

        protected abstract int RenderedHeight { get; }

        protected abstract bool UpdateDirtyRegionsOnly { get; }

        protected abstract void CreateSurface(int width, int height);

        protected abstract Action BeginSurfaceUpdate();

        protected abstract void UpdateBitmap(
            IntPtr sourceBuffer,
            int sourceBufferSize,
            int stride,
            CefRectangle updateRegion);

        protected abstract PixelBuffer ReadAcceleratedTexture(CefAcceleratedPaintInfo info);

        protected abstract void ReleaseAcceleratedTexture();

        protected override (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info)
            => (info.Extra.VisibleRectangle.Width, info.Extra.VisibleRectangle.Height);

        protected override void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
        {
            lock (_deliveryLock)
            {
                var stride = width * BytesPerPixel;
                var staged = _staging.CopyIn(buffer, width, height, stride, stride * height);

                if (staged is { } pixelBuffer)
                {
                    InnerRender(pixelBuffer, dirtyRects);
                }
            }
        }

        protected override void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            lock (_deliveryLock)
            {
                try
                {
                    var source = ReadAcceleratedTexture(info);
                    var staged = _staging.CopyIn(source.Pointer, source.Width, source.Height, source.Stride, source.ByteCount);

                    if (staged is { } pixelBuffer)
                    {
                        InnerRender(pixelBuffer, dirtyRects);
                    }
                }
                finally
                {
                    ReleaseAcceleratedTexture();
                }
            }
        }

        private void InnerRender(PixelBuffer buffer, CefRectangle[] dirtyRects)
        {
            if (RenderedWidth != buffer.Width || RenderedHeight != buffer.Height)
            {
                CreateSurface(buffer.Width, buffer.Height);
            }

            var endSurfaceUpdate = BeginSurfaceUpdate();

            try
            {
                if (!IsAccelerated && UpdateDirtyRegionsOnly && dirtyRects != null && dirtyRects.Length > 0)
                {
                    foreach (var dirtyRect in dirtyRects)
                    {
                        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
                        {
                            continue;
                        }

                        UpdateBitmap(
                            buffer.Pointer,
                            buffer.ByteCount,
                            buffer.Stride,
                            dirtyRect);
                    }

                    return;
                }

                UpdateBitmap(
                    buffer.Pointer,
                    buffer.ByteCount,
                    buffer.Stride,
                    new CefRectangle(0, 0, buffer.Width, buffer.Height));
            }
            finally
            {
                endSurfaceUpdate();
            }
        }

        public override void Dispose()
        {
            lock (_deliveryLock)
            {
                _staging.Dispose();
            }

            base.Dispose();
        }
    }
}
