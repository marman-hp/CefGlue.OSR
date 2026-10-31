using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Xilium.CefGlue.Demo.WinUI
{
    internal sealed class CursorGrid : Grid
    {
        public InputSystemCursorShape HoverCursorShape { get; set; } = InputSystemCursorShape.Arrow;

        public CursorGrid()
        {
            PointerEntered += (s, e) => ProtectedCursor = InputSystemCursor.Create(HoverCursorShape);
            PointerExited += (s, e) => ProtectedCursor = null;
        }
    }
}
