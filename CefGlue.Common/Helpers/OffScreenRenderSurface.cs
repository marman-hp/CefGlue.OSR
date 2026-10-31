using System;
using System.Threading.Tasks;

namespace Xilium.CefGlue.Common.Helpers
{
    public abstract class OffScreenRenderSurface : IDisposable
    {
        private readonly object _gateLock = new object();
        private int _width;
        private int _height;
        private float _deviceScaleFactor = 1.0f;

        public abstract bool AllowsTransparency { get; }

        public bool IsAccelerated { get; private set; }

        public event Action<int, int> FrameAccepted;

        public void Resize(int width, int height)
        {
            lock (_gateLock)
            {
                _width = width;
                _height = height;
            }
        }

        public float DeviceScaleFactor
        {
            get { lock (_gateLock) return _deviceScaleFactor; }
            set { lock (_gateLock) _deviceScaleFactor = value; }
        }

        public int Width { get { lock (_gateLock) return _width; } }

        public int Height { get { lock (_gateLock) return _height; } }

        public int ScaledWidth { get { lock (_gateLock) return ScaleUnlocked(_width, _deviceScaleFactor); } }

        public int ScaledHeight { get { lock (_gateLock) return ScaleUnlocked(_height, _deviceScaleFactor); } }

        private static int ScaleUnlocked(int value, float scale) => (int)Math.Ceiling(scale * value);

        protected abstract Task ExecuteInUIThread(Action action);

        protected abstract (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info);

        protected abstract void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects);

        protected abstract void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects);

        public Task Render(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
            => RunGated(width, height, isAccelerated: false, () => DeliverSoftwareFrame(buffer, width, height, dirtyRects));

        public Task Render(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            var (width, height) = GetAcceleratedFrameSize(info);
            return RunGated(width, height, isAccelerated: true, () => DeliverAcceleratedFrame(info, dirtyRects));
        }

        private Task RunGated(int width, int height, bool isAccelerated, Action deliver)
        {
            return ExecuteInUIThread(() =>
            {
                bool accepted;
                lock (_gateLock)
                {
                    accepted = width > 0 && height > 0
                        && width == ScaleUnlocked(_width, _deviceScaleFactor)
                        && height == ScaleUnlocked(_height, _deviceScaleFactor);

                    if (accepted)
                    {
                        IsAccelerated = isAccelerated;
                    }
                }

                if (!accepted)
                {
                    return;
                }

                deliver();
                FrameAccepted?.Invoke(width, height);
            });
        }

        public virtual void Dispose() => GC.SuppressFinalize(this);
    }
}
