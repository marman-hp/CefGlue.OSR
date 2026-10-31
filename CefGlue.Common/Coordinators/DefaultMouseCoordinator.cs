namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultMouseCoordinator : IMouseCoordinator
    {
        protected CefBrowserContext Context { get; private set; }

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
        }

        private void SendMouseClickEvent(CefMouseEvent mouseEvent, CefMouseButtonType mouseButton, bool isMouseUp, int clickCount)
        {
            Context.GetBrowserHost()?.SendMouseClickEvent(mouseEvent, mouseButton, isMouseUp, clickCount);
        }

        public virtual void HandleMouseMove(CefMouseEvent mouseEvent)
        {
            Context.WithErrorHandling(nameof(HandleMouseMove), () =>
            {
                Context.GetBrowserHost()?.SendMouseMoveEvent(mouseEvent, false);
            });
        }

        public virtual void HandleMouseLeave(CefMouseEvent mouseEvent)
        {
            Context.WithErrorHandling(nameof(HandleMouseLeave), () =>
            {
                Context.GetBrowserHost()?.SendMouseMoveEvent(mouseEvent, true);
            });
        }

        public virtual void HandleMouseButtonDown(System.Action focusControl, CefMouseEvent mouseEvent, CefMouseButtonType mouseButton, int clickCount)
        {
            Context.WithErrorHandling(nameof(HandleMouseButtonDown), () =>
            {
                focusControl();
                var browserHost = Context.GetBrowserHost();
                if (browserHost != null)
                {
                    browserHost.SetFocus(true);
                    SendMouseClickEvent(mouseEvent, mouseButton, false, clickCount);
                }
            });
        }

        public virtual void HandleMouseButtonUp(CefMouseEvent mouseEvent, CefMouseButtonType mouseButton)
        {
            Context.WithErrorHandling(nameof(HandleMouseButtonUp), () =>
            {
                if (Context.GetBrowserHost() != null)
                {
                    SendMouseClickEvent(mouseEvent, mouseButton, true, 1);
                }
            });
        }

        public virtual void HandleMouseWheel(CefMouseEvent mouseEvent, int deltaX, int deltaY)
        {
            Context.WithErrorHandling(nameof(HandleMouseWheel), () =>
            {
                Context.GetBrowserHost()?.SendMouseWheelEvent(mouseEvent, deltaX, deltaY);
            });
        }
    }
}
