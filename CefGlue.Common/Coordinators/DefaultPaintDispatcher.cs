using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultPaintDispatcher : IPaintDispatcher
    {
        protected CefBrowserContext Context { get; private set; }

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
        }

        private OffScreenRenderSurface GetRenderSurface(bool isPopup)
            => isPopup ? Context.Popup.RenderSurface : Context.Target.RenderSurface;

        public virtual void HandleViewPaint(System.IntPtr buffer, int width, int height, CefRectangle[] dirtyRects, bool isPopup)
        {
            var renderHandler = GetRenderSurface(isPopup);

            const string ScopeName = nameof(HandleViewPaint);

            Context.WithErrorHandling(ScopeName, () =>
            {
                renderHandler?.Render(buffer, width, height, dirtyRects)
                              .ContinueWith(t => Context.HandleException(ScopeName, t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            });
        }

        public virtual void HandleAcceleratedPaint(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects, bool isPopup)
        {
            var renderHandler = GetRenderSurface(isPopup);

            const string ScopeName = nameof(HandleAcceleratedPaint);

            Context.WithErrorHandling(ScopeName, () =>
            {
                renderHandler?.Render(info, dirtyRects)
                              .ContinueWith(t => Context.HandleException(ScopeName, t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            });
        }
    }
}
