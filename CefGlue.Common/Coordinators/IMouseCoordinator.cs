using System;

namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IMouseCoordinator
    {
        void Attach(CefBrowserContext context);

        void HandleMouseMove(CefMouseEvent mouseEvent);

        void HandleMouseLeave(CefMouseEvent mouseEvent);

        void HandleMouseButtonDown(Action focusControl, CefMouseEvent mouseEvent, CefMouseButtonType mouseButton, int clickCount);

        void HandleMouseButtonUp(CefMouseEvent mouseEvent, CefMouseButtonType mouseButton);

        void HandleMouseWheel(CefMouseEvent mouseEvent, int deltaX, int deltaY);
    }
}
