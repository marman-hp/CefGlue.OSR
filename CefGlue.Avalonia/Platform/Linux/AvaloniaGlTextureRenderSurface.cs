using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia.Platform.Linux
{
    /// <summary>
    /// Linux counterpart of AvaloniaTextureRenderSurface: CEF's accelerated frames arrive as dma-bufs, which
    /// GlQuadPresenter imports as EGLImages on Avalonia's GL thread. This class only dup()s the plane fds (they
    /// are valid for the duration of the CEF callback only) and hands the latest frame over.
    /// </summary>
    internal sealed class AvaloniaGlTextureRenderSurface : OffScreenRenderSurface
    {
        private const string LibC = "libc.so.6";

        [DllImport(LibC)] private static extern int dup(int oldfd);
        [DllImport(LibC)] private static extern int close(int fd);

        public readonly struct PlaneData
        {
            public readonly int FileDescriptor;
            public readonly uint Offset;
            public readonly uint Stride;

            public PlaneData(int fileDescriptor, uint offset, uint stride)
            {
                FileDescriptor = fileDescriptor;
                Offset = offset;
                Stride = stride;
            }
        }

        /// <summary>One frame's dma-buf import data. Whoever takes it from <see cref="ConsumePendingFrame"/>
        /// owns the duplicated fds and must close them with <see cref="CloseFrame"/>.</summary>
        public sealed class PendingFrame
        {
            public int Width;
            public int Height;
            public uint FourCc;
            public ulong Modifier;
            public int PlaneCount;
            public PlaneData[] Planes;
        }

        // DRM fourcc 'AR24' / 'AB24', matching CefColorType.Bgra8888 / Rgba8888.
        private const uint DrmFormatArgb8888 = 0x34325241;
        private const uint DrmFormatAbgr8888 = 0x34324241;

        private readonly object _frameLock = new object();
        private PendingFrame _pendingFrame;
        private bool _disposed;

        // CPU fallback ("degraded" mode), entered when CEF paints in software (OnPaint).
        private AvaloniaBitmapSurface _cpuFrames;
        private AvaloniaRenderSurface<AvaloniaBitmapSurface, byte[]> _cpuPipeline;

        public override bool AllowsTransparency => true;

        public bool UseOSRDirtyRect { get; set; } = true;

        private bool _showOverlayInfo;

        public bool ShowOverlayInfo
        {
            get => _showOverlayInfo;
            set
            {
                _showOverlayInfo = value;
                if (_cpuFrames != null)
                {
                    _cpuFrames.ShowOverlayInfo = value;
                }
            }
        }

        public bool IsDegraded => _cpuFrames != null;

        /// <summary>False keeps the old behaviour: software frames are dropped.</summary>
        public bool AllowCpuFallback { get; set; } = true;

        /// <summary>Raised once, on the CEF thread, when the surface switches to CPU rendering.</summary>
        public event Action<AvaloniaBitmapSurface, string> Degraded;

        /// <summary>Raised (CEF thread) when a new GPU frame is ready to import.</summary>
        public event Action FrameReady;

        protected override Task ExecuteInUIThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        protected override (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info)
            => (info.Extra.CodedSize.Width, info.Extra.CodedSize.Height);

        protected override void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
        {
            // A shared-texture browser only gets OnPaint when CEF is not GPU compositing (GPU process failed,
            // blocklisted, or CEF runtime without its GL libraries).
            if (!AllowCpuFallback)
            {
                return;
            }

            if (!IsDegraded)
            {
                _cpuFrames = new AvaloniaBitmapSurface { UseOSRDirtyRect = UseOSRDirtyRect, ShowOverlayInfo = _showOverlayInfo };
                _cpuPipeline = new AvaloniaRenderSurface<AvaloniaBitmapSurface, byte[]>(_cpuFrames);
                DropPendingFrame();
                Degraded?.Invoke(_cpuFrames, "CEF is painting in software (OnPaint) instead of shared textures.");
            }

            _cpuPipeline.Resize(Width, Height);
            _cpuPipeline.DeviceScaleFactor = DeviceScaleFactor;
            _cpuPipeline.Render(buffer, width, height, dirtyRects);
        }

        protected override void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            var width = info.Extra.CodedSize.Width;
            var height = info.Extra.CodedSize.Height;

            if (IsDegraded || width <= 0 || height <= 0 || info.PlaneCount <= 0)
            {
                return;
            }

            var planeCount = Math.Min(info.PlaneCount, 4);
            var planes = new PlaneData[planeCount];

            for (var i = 0; i < planeCount; i++)
            {
                var source = info.Planes[i];
                var duplicated = dup(source.FileDescriptor);
                if (duplicated < 0)
                {
                    for (var j = 0; j < i; j++)
                    {
                        close(planes[j].FileDescriptor);
                    }
                    return;
                }

                planes[i] = new PlaneData(duplicated, (uint)source.Offset, source.Stride);
            }

            var frame = new PendingFrame
            {
                Width = width,
                Height = height,
                FourCc = info.Format == CefColorType.Rgba8888 ? DrmFormatAbgr8888 : DrmFormatArgb8888,
                Modifier = info.Modifier,
                PlaneCount = planeCount,
                Planes = planes,
            };

            PendingFrame previous;
            lock (_frameLock)
            {
                if (_disposed)
                {
                    CloseFrame(frame);
                    return;
                }

                // Only the latest frame is worth showing; close one the presenter never got to.
                previous = _pendingFrame;
                _pendingFrame = frame;
            }

            if (previous != null)
            {
                CloseFrame(previous);
            }

            FrameReady?.Invoke();
        }

        /// <summary>Takes the latest frame (null if nothing new); the caller owns its fds.</summary>
        public PendingFrame ConsumePendingFrame()
        {
            lock (_frameLock)
            {
                var frame = _pendingFrame;
                _pendingFrame = null;
                return frame;
            }
        }

        public static void CloseFrame(PendingFrame frame)
        {
            for (var i = 0; i < frame.PlaneCount; i++)
            {
                close(frame.Planes[i].FileDescriptor);
            }
        }

        private void DropPendingFrame()
        {
            var frame = ConsumePendingFrame();
            if (frame != null)
            {
                CloseFrame(frame);
            }
        }

        public override void Dispose()
        {
            lock (_frameLock)
            {
                _disposed = true;
            }

            DropPendingFrame();
            _cpuPipeline?.Dispose();
            _cpuFrames?.Dispose();

            base.Dispose();
        }
    }
}
