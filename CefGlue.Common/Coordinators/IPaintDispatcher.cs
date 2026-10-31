using System;

namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IPaintDispatcher
    {
        void Attach(CefBrowserContext context);

        void HandleViewPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects, bool isPopup);

        void HandleAcceleratedPaint(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects, bool isPopup);
    }
}
