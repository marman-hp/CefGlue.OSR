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
        // DRM fourcc codes (drm_fourcc.h) matching CefColorType's two possible values. Fourcc names
        // describe MEMORY byte order low-to-high, which is why e.g. DRM_FORMAT_ARGB8888 (bytes
        // B,G,R,A in memory) is the match for CefColorType.Bgra8888, not the seemingly-obvious
        // ARGB name - same "read backwards" mapping CEF's own Bgra8888/Cairo's Format.Argb32
        // already relied on elsewhere in this codebase.
        private const uint DrmFormatArgb8888 = 0x34325241; // fourcc('A','R','2','4') - CefColorType.Bgra8888
        private const uint DrmFormatAbgr8888 = 0x34324241; // fourcc('A','B','2','4') - CefColorType.Rgba8888

        // drm_fourcc.h: #define DRM_FORMAT_MOD_LINEAR fourcc_mod_code(NONE, 0) == 0. A plane with
        // this modifier is a flat row-major buffer - safe to read via a raw mmap of its fd. Any
        // other modifier means tiled/compressed GPU-native layout that only the GPU driver knows
        // how to interpret, which is why the ReadViaMmap fast path below is gated on this.
        private const ulong DrmFormatModLinear = 0;

        private IntPtr _downloadBuffer;

        // Captured here on construction, which happens during CefGlueBrowser's own setup (GTK main
        // thread), same pattern BitmapControl uses. Needed because ExecuteInUIThread below was a
        // silent no-op until 2026-08-04 - harmless for the CPU OnPaint path (DeliverSoftwareFrame
        // never touches any Gtk/Gdk API, just raw memory copies; the actual Gtk/Gdk calls happen
        // later via BitmapControl's own _uiContext.Post) but NOT harmless for accelerated paint:
        // ReadAcceleratedTexture below calls straight into Gdk.Display/DmabufTextureBuilder/raw
        // gdk_* P/Invoke, and GTK/GDK are not thread-safe - confirmed live, calling them from
        // whatever thread CEF invokes OnAcceleratedPaint on (CEF's own "CrBrowserMain" thread, NOT
        // the GTK main loop thread) segfaulted with a corrupted/unsymbolizable stack.
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

        // Reverted to a plain inline no-op (2026-08-05) - the "always Post" version below (kept in
        // a comment for the next accelerated-paint session) was written to fix a GSK reentrancy
        // issue specific to the accelerated/DMA-BUF path (ReadAcceleratedTexture calling Gdk/Gsk
        // APIs directly), but SharedTextureEnabled is off right now so that path never runs at all -
        // and the Post-based version turned out to be the wrong default for the CPU OnPaint path:
        // confirmed live it introduced a NEW crash on native Linux that wasn't there before (this
        // path never touched any Gtk/Gdk API directly to begin with - see _uiContext's remarks - so
        // forcing every delivery through an extra async main-loop hop was pure unnecessary risk for
        // this path specifically). Do not reapply the Post version here without re-scoping it to
        // only the accelerated path (e.g. a second, accelerated-only override, or an isAccelerated
        // check) - see project memory for the full accelerated-paint investigation.
        //
        // protected override Task ExecuteInUIThread(Action action)
        // {
        //     if (_uiContext == null)
        //     {
        //         action();
        //         return Task.CompletedTask;
        //     }
        //
        //     var tcs = new TaskCompletionSource();
        //
        //     _uiContext.Post(_ =>
        //     {
        //         try
        //         {
        //             action();
        //             tcs.SetResult();
        //         }
        //         catch (Exception ex)
        //         {
        //             tcs.SetException(ex);
        //         }
        //     }, null);
        //
        //     return tcs.Task;
        // }
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

        // GPU->CPU readback: import CEF's DMA-BUF planes as a Gdk.Texture (zero-copy on the way
        // in), then immediately download it to a plain CPU buffer via gdk_texture_download - NOT a
        // true zero-copy end-to-end display path (that would mean handing a GL/dmabuf texture
        // straight to GskGLRenderer instead of ever landing in CPU memory), but reuses the
        // already-proven-stable CPU display pipeline (BitmapControl/Gdk.MemoryTexture) for the
        // output side, and is a reasonable first checkpoint: confirms the DMA-BUF import itself
        // works (doesn't crash, doesn't error) before attempting the bigger, riskier change of
        // bypassing CPU staging entirely. See project memory for the full zero-copy plan.
        protected override unsafe PixelBuffer ReadAcceleratedTexture(CefAcceleratedPaintInfo info)
        {
            var width = info.Extra.CodedSize.Width;
            var height = info.Extra.CodedSize.Height;
            var stride = width * BytesPerPixel;
            var byteCount = stride * height;

            // Fast path (2026-08-05): mmap+memcpy a single-plane dma-buf directly, without EVER
            // calling into GdkDmabufTextureBuilder/gdk_texture_download/GSK. This sidesteps the
            // libdrm_amdgpu.so.1 hang confirmed live when gdk_texture_download() on an imported
            // dma-buf races GSK's own render cycle (see LibcInterop.cs remarks + project memory).
            //
            // Originally gated on info.Modifier == DRM_FORMAT_MOD_LINEAR (0), on the assumption CEF
            // would report that for a simple row-major buffer. Confirmed live (2026-08-05) that's
            // wrong: CEF actually reports info.Modifier == DRM_FORMAT_MOD_INVALID
            // (0x00ffffffffffffff, i.e. "no explicit modifier was negotiated") for this OSR export,
            // never DRM_FORMAT_MOD_LINEAR - so that gate never matched and this path never ran.
            // DRM_FORMAT_MOD_INVALID doesn't guarantee a flat layout the way MOD_LINEAR would (the
            // AMDGPU driver could still hand back something tile-swizzled), but the whole point of
            // this experiment is finding out whether skipping GDK/GSK entirely avoids the hang - a
            // wrong-but-harmless visual (garbled frame) is trivially recoverable and instantly
            // visible, unlike the confirmed system/app-level hang on the GdkTexture path below. So:
            // gate on PlaneCount == 1 only now, and rely on the visual result (plus the hang-repro
            // steps - alt-tab/focus-change) to tell us whether this assumption holds on this specific
            // AMD Vega/Mesa setup. If frames come out visibly corrupted, that's the signal MOD_INVALID
            // means tiled here and this path needs a real modifier-aware un-swizzle (or to be
            // abandoned) - but see it before concluding rather than guessing.
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

        // mmaps a linear (untiled) dma-buf plane and copies it straight into _downloadBuffer,
        // never touching any Gdk/Gsk API - see the fast-path remarks in ReadAcceleratedTexture and
        // LibcInterop.cs for why. The CEF docs on CefAcceleratedPaintInfo say the plane's fd "cannot
        // be cached and cannot be accessed outside of this callback", so the mmap+copy+munmap all
        // happen synchronously here, inside the same OnAcceleratedPaint callback that owns it -
        // mirrors the fd-lifetime constraint the GdkTexture path below already has to respect.
        private unsafe void ReadViaMmap(CefAcceleratedPaintNativePixmapPlane plane, int stride, int height, int byteCount)
        {
            var planeLength = (long)plane.Stride * height;
            if ((long)plane.Size > planeLength)
            {
                planeLength = (long)plane.Size;
            }

            // mmap's offset argument must be page-aligned; if the plane's own offset isn't, map
            // from the nearest preceding page boundary instead and skip the remainder (offsetDelta)
            // once mapped. Single-plane BGRA/RGBA surfaces are expected to have offset 0, so this is
            // normally a no-op, but handled generally rather than assumed.
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
                    // Source row pitch differs from our destination stride - copy row by row rather
                    // than assuming a single flat memcpy is safe.
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
