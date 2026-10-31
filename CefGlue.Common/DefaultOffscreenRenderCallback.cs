using System;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.Common
{
    public class DefaultOffscreenRenderCallback : IOffscreenRenderCallback
    {
        private readonly IPaintDispatcher _paintDispatcher;
        private readonly IDragDropCoordinator _dragDropCoordinator;

        protected CefBrowserContext Context { get; private set; }

        public DefaultOffscreenRenderCallback(IPaintDispatcher paintDispatcher, IDragDropCoordinator dragDropCoordinator)
        {
            _paintDispatcher = paintDispatcher;
            _dragDropCoordinator = dragDropCoordinator;
        }

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
        }

        public virtual void GetViewRect(out CefRectangle rect)
        {
            var result = new CefRectangle(0, 0, Context.Target.RenderSurface.Width, Context.Target.RenderSurface.Height);

            if (result.Width <= 0 || result.Height <= 0)
            {
                rect = new CefRectangle(0, 0, Math.Max(1, result.Width), Math.Max(1, result.Height));
            }
            else
            {
                rect = result;
            }
        }

        public virtual void GetScreenPoint(int viewX, int viewY, ref int screenX, ref int screenY)
        {
            var resultX = screenX;
            var resultY = screenY;

            Context.WithErrorHandling(nameof(GetScreenPoint), () =>
            {
                var point = Context.Target.PointToScreen(new CefPoint(viewX, viewY), Context.Target.RenderSurface.DeviceScaleFactor);
                resultX = point.X;
                resultY = point.Y;
            });

            screenX = resultX;
            screenY = resultY;
        }

        public virtual void GetScreenInfo(CefScreenInfo screenInfo)
        {
            screenInfo.DeviceScaleFactor = Context.Target.RenderSurface.DeviceScaleFactor;
        }

        public virtual void HandleVirtualKeyboardRequested(CefTextInputMode inputMode) { }

        public virtual void HandlePopupShow(bool show)
        {
            Context.WithErrorHandling(nameof(HandlePopupShow), () =>
            {
                if (show)
                {
                    Context.Target.ReleaseMouseCapture();
                    Context.Popup.Open();
                }
                else
                {
                    Context.Popup.Close();
                }
            });
        }

        public virtual void HandlePopupSizeChange(CefRectangle rect)
        {
            Context.WithErrorHandling(nameof(HandlePopupSizeChange), () =>
            {
                Context.Popup.RenderSurface.Resize(rect.Width, rect.Height);
                Context.Popup.MoveAndResize(rect.X, rect.Y, rect.Width, rect.Height);
            });
        }

        public virtual void HandleViewPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects, bool isPopup)
        {
            _paintDispatcher.HandleViewPaint(buffer, width, height, dirtyRects, isPopup);
        }

        public virtual void HandleAcceleratedPaint(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects, bool isPopup)
        {
            _paintDispatcher.HandleAcceleratedPaint(info, dirtyRects, isPopup);
        }

        public virtual void HandleStartDragging(CefBrowser browser, CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            _dragDropCoordinator.HandleStartDragging(browser, dragData, allowedOps, x, y);
        }

        public virtual void HandleUpdateDragCursor(CefBrowser browser, CefDragOperationsMask operation)
        {
            _dragDropCoordinator.HandleUpdateDragCursor(operation);
        }
    }
}
