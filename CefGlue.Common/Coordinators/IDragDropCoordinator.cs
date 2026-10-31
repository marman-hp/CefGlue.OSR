namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IDragDropCoordinator
    {
        void Attach(CefBrowserContext context);

        void HandleDragEnter(CefMouseEvent mouseEvent, CefDragData dragData, CefDragOperationsMask effects);

        void HandleDragOver(CefMouseEvent mouseEvent, CefDragOperationsMask effects);

        void HandleDragLeave();

        void HandleDrop(CefMouseEvent mouseEvent, CefDragOperationsMask effects);

        void HandleStartDragging(CefBrowser browser, CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y);

        void HandleUpdateDragCursor(CefDragOperationsMask operation);
    }
}
