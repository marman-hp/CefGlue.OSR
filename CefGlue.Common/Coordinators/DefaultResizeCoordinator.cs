using System;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.Common.Shared.Helpers;

namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultResizeCoordinator : IResizeCoordinator, IDisposable
    {
        protected CefBrowserContext Context { get; private set; }
        private OffScreenRenderSurface _renderSurface;

        private volatile bool _resizeAckPending;
        private int _pendingResizeWidth;
        private int _pendingResizeHeight;
        private bool _hasPendingResize;
        private int _resizeGeneration;

        private static readonly TimeSpan ResizeStallTimeout = OperatingSystem.IsLinux()
            ? TimeSpan.FromMilliseconds(150)
            : TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan InvalidateRetryInterval = OperatingSystem.IsLinux()
            ? TimeSpan.FromMilliseconds(50)
            : TimeSpan.FromMilliseconds(30);
        private static readonly int MaxInvalidateAttempts = OperatingSystem.IsLinux() ? 5 : 3;

        private const bool UseResizeAckCoalescing = true;

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
            _renderSurface = context.Target.RenderSurface;
            _renderSurface.FrameAccepted += HandleFrameAccepted;
        }

        public virtual void RequestResize(int newWidth, int newHeight)
        {
            if (newWidth <= 0 || newHeight <= 0)
            {
                return;
            }

            if (_renderSurface.Width == newWidth && _renderSurface.Height == newHeight)
            {
                return;
            }

            if (UseResizeAckCoalescing && _resizeAckPending)
            {
                _pendingResizeWidth = newWidth;
                _pendingResizeHeight = newHeight;
                _hasPendingResize = true;
                return;
            }

            ApplyResize(newWidth, newHeight);
        }

        private void ApplyResize(int newWidth, int newHeight)
        {
            _resizeAckPending = true;
            _renderSurface.Resize(newWidth, newHeight);
            Context.GetBrowserHost()?.WasResized();

            var generation = Interlocked.Increment(ref _resizeGeneration);
            ArmResizeStallWatchdog(generation);

            Context.Logger.Debug($"Browser resized {newWidth}x{newHeight}");
        }

        public virtual void HandleFrameAccepted(int width, int height)
        {
            if (!_resizeAckPending)
            {
                return;
            }

            bool matchesLogical = width == _renderSurface.Width && height == _renderSurface.Height;
            bool matchesScaled = width == _renderSurface.ScaledWidth && height == _renderSurface.ScaledHeight;
            if (!matchesLogical && !matchesScaled)
            {
                return;
            }

            Interlocked.Increment(ref _resizeGeneration);
            _resizeAckPending = false;

            if (_hasPendingResize)
            {
                _hasPendingResize = false;
                ApplyResize(_pendingResizeWidth, _pendingResizeHeight);
            }
        }

        public virtual void ArmStallWatchdog()
        {
            _resizeAckPending = true;
            var generation = Interlocked.Increment(ref _resizeGeneration);
            ArmResizeStallWatchdog(generation);
        }

        private void ArmResizeStallWatchdog(int generation)
        {
            ActionTask.Run(async () =>
            {
                await Task.Delay(ResizeStallTimeout);

                for (var attempt = 0; attempt < MaxInvalidateAttempts; attempt++)
                {
                    var browserHost = Context.GetBrowserHost();
                    if (generation != _resizeGeneration || !_resizeAckPending || browserHost == null)
                    {
                        return;
                    }

                    browserHost.NotifyScreenInfoChanged();
                    browserHost.Invalidate(CefPaintElementType.View);
                    await Task.Delay(InvalidateRetryInterval);
                }
                Context.GetBrowserHost()?.SetFocus(true);

                if (generation == _resizeGeneration && _resizeAckPending)
                {
                    var gateMessage = $"Resize ack never arrived for {_renderSurface.Width}x{_renderSurface.Height} - releasing the resize gate";
                    Context.Logger.Debug(gateMessage);
                    System.Diagnostics.Debug.WriteLine("[DefaultResizeCoordinator] " + gateMessage);
                    Interlocked.Increment(ref _resizeGeneration);
                    _resizeAckPending = false;

                    if (_hasPendingResize)
                    {
                        _hasPendingResize = false;
                        ApplyResize(_pendingResizeWidth, _pendingResizeHeight);
                    }
                }
            });
        }

        public virtual void Dispose()
        {
            _renderSurface.FrameAccepted -= HandleFrameAccepted;
        }
    }
}
