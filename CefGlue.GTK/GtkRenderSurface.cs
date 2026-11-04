using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Gdk;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.GTK.Interop;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// Bridges CEF's offscreen paint callbacks to a <see cref="IFrameSurface{TFrame}"/> via
    /// CpuStagedRenderSurface's CPU-staging pipeline, mirroring CefGlue.Avalonia's
    /// AvaloniaRenderSurface&lt;TSurface, TFrame&gt;.
    /// </summary>
    internal class GtkRenderSurface<TSurface, TFrame> : CpuStagedRenderSurface
        where TSurface : IFrameSurface<TFrame>
    {
        private const uint DrmFormatArgb8888 = 0x34325241;
        private const uint DrmFormatAbgr8888 = 0x34324241;

        private const ulong DrmFormatModLinear = 0;

        private IntPtr _downloadBuffer;

        private readonly SynchronizationContext _uiContext = SynchronizationContext.Current;

        public GtkRenderSurface(TSurface surface)
        {
            _surface = surface;
        }

        private readonly TSurface _surface;

        public override bool AllowsTransparency => true;

        protected override int BytesPerPixel => 4;

        protected override bool UpdateDirtyRegionsOnly => true;

        protected override int RenderedWidth => _surface?.Width ?? 0;

        protected override int RenderedHeight => _surface?.Height ?? 0;

        protected override void CreateSurface(int width, int height)
        {
            _surface.BeginFrame(width, height);
        }

        protected override Action BeginSurfaceUpdate()
        {
            return () => _surface.EndFrame();
        }

        protected override Task ExecuteInUIThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        protected override void UpdateBitmap(IntPtr sourceBuffer, int sourceBufferSize, int stride, CefRectangle updateRegion)
        {
            _surface.IsAccelerated = IsAccelerated;
            _surface.CopyDirtyFrame(sourceBuffer, stride, updateRegion);
        }

        protected override unsafe PixelBuffer ReadAcceleratedTexture(CefAcceleratedPaintInfo info)
        {
            var width = info.Extra.CodedSize.Width;
            var height = info.Extra.CodedSize.Height;
            var stride = width * BytesPerPixel;
            var byteCount = stride * height;

            if (info.PlaneCount == 1)
            {
                if (_downloadBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_downloadBuffer);
                }

                _downloadBuffer = Marshal.AllocHGlobal(byteCount);
                ReadViaMmap(info.Planes[0], stride, height, byteCount);

                return new PixelBuffer(_downloadBuffer, width, height, stride, byteCount);
            }

            using var builder = DmabufTextureBuilder.New();
            builder.Display = Display.GetDefault();
            builder.Width = (uint)width;
            builder.Height = (uint)height;
            builder.Fourcc = info.Format == CefColorType.Rgba8888 ? DrmFormatAbgr8888 : DrmFormatArgb8888;
            builder.Modifier = info.Modifier;
            builder.Premultiplied = true;
            builder.NPlanes = (uint)info.PlaneCount;

            for (var i = 0; i < info.PlaneCount; i++)
            {
                var plane = info.Planes[i];
                var planeIndex = (uint)i;
                builder.SetFd(planeIndex, plane.FileDescriptor);
                builder.SetStride(planeIndex, plane.Stride);
                builder.SetOffset(planeIndex, (uint)plane.Offset);
            }

            var texturePtr = GdkDmabufInterop.DmabufTextureBuilderBuild(
                builder.Handle.DangerousGetHandle(), IntPtr.Zero, IntPtr.Zero, out var errorPtr);

            if (texturePtr == IntPtr.Zero)
            {
                if (errorPtr != IntPtr.Zero)
                {
                    GdkDmabufInterop.ErrorFree(errorPtr);
                }

                throw new InvalidOperationException("gdk_dmabuf_texture_builder_build failed - see stderr for the GLib-logged reason.");
            }

            using var texture = Texture.NewFromPointer(texturePtr, ownsHandle: true);

            if (_downloadBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_downloadBuffer);
            }

            _downloadBuffer = Marshal.AllocHGlobal(byteCount);

            GdkDmabufInterop.TextureDownload(texturePtr, _downloadBuffer, (UIntPtr)stride);

            return new PixelBuffer(_downloadBuffer, width, height, stride, byteCount);
        }

        private unsafe void ReadViaMmap(CefAcceleratedPaintNativePixmapPlane plane, int stride, int height, int byteCount)
        {
            var planeLength = (long)plane.Stride * height;
            if ((long)plane.Size > planeLength)
            {
                planeLength = (long)plane.Size;
            }

            var pageSize = Environment.SystemPageSize;
            var alignedOffset = (long)plane.Offset / pageSize * pageSize;
            var offsetDelta = (long)plane.Offset - alignedOffset;
            var mapLength = offsetDelta + planeLength;

            var mapped = LibcInterop.Mmap(IntPtr.Zero, (IntPtr)mapLength, LibcInterop.ProtRead, LibcInterop.MapShared, plane.FileDescriptor, (IntPtr)alignedOffset);

            if (mapped == LibcInterop.MapFailed)
            {
                throw new InvalidOperationException($"mmap failed on dma-buf fd (errno {Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                var src = (byte*)mapped + offsetDelta;
                var dst = (byte*)_downloadBuffer;

                if (plane.Stride == stride)
                {
                    Buffer.MemoryCopy(src, dst, byteCount, byteCount);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(src + (long)y * plane.Stride, dst + (long)y * stride, stride, stride);
                    }
                }
            }
            finally
            {
                LibcInterop.Munmap(mapped, (IntPtr)mapLength);
            }
        }

        protected override void ReleaseAcceleratedTexture()
        {
            if (_downloadBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_downloadBuffer);
                _downloadBuffer = IntPtr.Zero;
            }
        }
    }
}
