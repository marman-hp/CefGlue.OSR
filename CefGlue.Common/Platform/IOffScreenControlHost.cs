using System;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Common.Platform
{
    public delegate void KeyEventHandler(CefKeyEvent e, out bool handled);
    public delegate void TextInputEventHandler(string text, out bool handled);

    public interface IOffScreenTargetHost : ITarget
    {
        event Action LostFocus;

        event KeyEventHandler KeyDown;
        event KeyEventHandler KeyUp;
        event TextInputEventHandler TextInput;

        event Action<string, int> CompositionUpdated;

        event Action<string> CompositionCommitted;

        event Action<string, int, ImeCompositionClause[]> CompositionUpdatedWithClauses { add { } remove { } }

        event Action<IOffScreenTargetHost, CefMouseEvent, CefMouseButtonType, int> MouseButtonPressed;
        event Action<CefMouseEvent, CefMouseButtonType> MouseButtonReleased;
        event Action<CefMouseEvent> MouseLeave;
        event Action<CefMouseEvent> MouseMoved;
        event Action<CefMouseEvent, int, int> MouseWheelChanged;

        event Action<CefMouseEvent, CefDragData, CefDragOperationsMask> DragEnter;
        event Action<CefMouseEvent, CefDragOperationsMask> DragOver;

        event Action DragLeave;
        event Action<CefMouseEvent, CefDragOperationsMask> Drop;

        event Action<float> ScreenInfoChanged;
        event Action<bool> VisibilityChanged;

        event Action Reattached { add { } remove { } }

        void SetTextInputActive(bool active) { }

        void UpdateImeCompositionBounds(CefRange selectedRange, CefRectangle[] characterBounds) { }

        void Focus();
        CefPoint PointToScreen(CefPoint point, float deviceScaleFactor);

        void SetFullscreen(bool fullscreen);

        void ReleaseMouseCapture();

        Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y);
        void UpdateDragCursor(CefDragOperationsMask allowedOps);

        OffScreenRenderSurface RenderSurface { get; }
    }
}