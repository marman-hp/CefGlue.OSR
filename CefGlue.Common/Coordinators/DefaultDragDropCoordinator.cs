namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultDragDropCoordinator : IDragDropCoordinator
    {
        private CefDragData _lastDragData;

        private bool _isOutboundDragInProgress;

        protected CefBrowserContext Context { get; private set; }

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
        }

        public virtual void HandleDragEnter(CefMouseEvent mouseEvent, CefDragData dragData, CefDragOperationsMask effects)
        {
            _lastDragData = dragData;

            Context.WithErrorHandling(nameof(HandleDragEnter), () =>
            {
                var browserHost = Context.GetBrowserHost();
                browserHost?.DragTargetDragEnter(dragData, mouseEvent, effects);
                browserHost?.DragTargetDragOver(mouseEvent, effects);
            });
        }

        public virtual void HandleDragOver(CefMouseEvent mouseEvent, CefDragOperationsMask effects)
        {
            Context.WithErrorHandling(nameof(HandleDragOver), () =>
            {
                Context.GetBrowserHost()?.DragTargetDragOver(mouseEvent, effects);
            });
        }

        public virtual void HandleDragLeave()
        {
            Context.WithErrorHandling(nameof(HandleDragLeave), () =>
            {
                Context.GetBrowserHost()?.DragTargetDragLeave();
            });
        }

        public virtual void HandleDrop(CefMouseEvent mouseEvent, CefDragOperationsMask effects)
        {
            Context.WithErrorHandling(nameof(HandleDrop), () =>
            {
                var browserHost = Context.GetBrowserHost();
                if (_lastDragData != null)
                {
                    browserHost?.DragTargetDragEnter(_lastDragData, mouseEvent, effects);
                }
                browserHost?.DragTargetDragOver(mouseEvent, effects);
                browserHost?.DragTargetDrop(mouseEvent);
            });
        }

        public virtual void HandleStartDragging(CefBrowser browser, CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            if (_isOutboundDragInProgress)
            {
                return;
            }
            _isOutboundDragInProgress = true;

            Context.WithErrorHandling(nameof(HandleStartDragging), async () =>
            {
                try
                {
                    var result = await Context.Target.StartDrag(dragData, allowedOps, x, y);
                    var browserHost = Context.GetBrowserHost();
                    browserHost?.DragSourceEndedAt(x, y, result);
                    browserHost?.DragSourceSystemDragEnded();
                }
                finally
                {
                    _isOutboundDragInProgress = false;
                }
            });
        }

        public virtual void HandleUpdateDragCursor(CefDragOperationsMask operation)
        {
            Context.Target.UpdateDragCursor(operation);
        }
    }
}
