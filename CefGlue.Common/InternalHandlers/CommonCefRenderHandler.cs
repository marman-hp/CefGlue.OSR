using System;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers.Logger;

namespace Xilium.CefGlue.Common.InternalHandlers
{
    internal sealed class CommonCefRenderHandler : CefRenderHandler
    {
        private readonly IOffscreenRenderCallback _owner;
        private readonly Func<RenderHandler> _getRenderHandler;
        private readonly ILogger _logger;

        public CommonCefRenderHandler(IOffscreenRenderCallback owner, Func<RenderHandler> getRenderHandler, ILogger logger)
        {
            if (owner == null)
            {
                throw new ArgumentNullException("owner");
            }

            if (getRenderHandler == null)
            {
                throw new ArgumentNullException("getRenderHandler");
            }

            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            _owner = owner;
            _getRenderHandler = getRenderHandler;
            _logger = logger;
        }

        protected override CefAccessibilityHandler GetAccessibilityHandler()
        {
            return _getRenderHandler()?.HandleGetAccessibilityHandler();
        }

        protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
        {
            _owner.GetViewRect(out rect);
        }

        protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
        {
            _owner.GetScreenPoint(viewX, viewY, ref screenX, ref screenY);
            return true;
        }

        protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
        {
            _owner.GetScreenInfo(screenInfo);
            return true;
        }

        protected override void OnPopupShow(CefBrowser browser, bool show)
        {
            _owner.HandlePopupShow(show);
        }

        protected override void OnPopupSize(CefBrowser browser, CefRectangle rect)
        {
            _owner.HandlePopupSizeChange(rect);
        }

        protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo info)
        {
            _owner.HandleAcceleratedPaint(info, dirtyRects, type == CefPaintElementType.Popup);
        }

        protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
        {
            _owner.HandleViewPaint(buffer, width, height, dirtyRects, type == CefPaintElementType.Popup);
        }

        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
        {
            _getRenderHandler()?.HandleScrollOffsetChanged(browser, x, y);
        }

        protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
        {
            ImeBoundsChanged?.Invoke(selectedRange, characterBounds);
            _getRenderHandler()?.HandleImeCompositionRangeChanged(browser, selectedRange, characterBounds);
        }

        public Action<CefRange, CefRectangle[]> ImeBoundsChanged { get; set; }

        public Action<bool> TextInputActiveChanged { get; set; }

        protected override void OnVirtualKeyboardRequested(CefBrowser browser, CefTextInputMode inputMode)
        {
            TextInputActiveChanged?.Invoke(inputMode != CefTextInputMode.None);
            _owner.HandleVirtualKeyboardRequested(inputMode);
        }

        protected override bool StartDragging(CefBrowser browser, CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            _owner.HandleStartDragging(browser, dragData, allowedOps, x, y);

            return true;
        }

        protected override void UpdateDragCursor(CefBrowser browser, CefDragOperationsMask operation)
        {
            _owner.HandleUpdateDragCursor(browser, operation);
        }

    }
}
