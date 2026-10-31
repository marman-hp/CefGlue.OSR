using System.Collections.Generic;
using Microsoft.UI.Input;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal static class CursorMapper
    {
        private static readonly Dictionary<CefCursorType, InputSystemCursorShape> _map = new()
        {
            { CefCursorType.Pointer, InputSystemCursorShape.Arrow },
            { CefCursorType.Cross, InputSystemCursorShape.Cross },
            { CefCursorType.Hand, InputSystemCursorShape.Hand },
            { CefCursorType.IBeam, InputSystemCursorShape.IBeam },
            { CefCursorType.Wait, InputSystemCursorShape.Wait },
            { CefCursorType.Help, InputSystemCursorShape.Help },
            { CefCursorType.EastResize, InputSystemCursorShape.SizeWestEast },
            { CefCursorType.WestResize, InputSystemCursorShape.SizeWestEast },
            { CefCursorType.EastWestResize, InputSystemCursorShape.SizeWestEast },
            { CefCursorType.ColumnResize, InputSystemCursorShape.SizeWestEast },
            { CefCursorType.NorthResize, InputSystemCursorShape.SizeNorthSouth },
            { CefCursorType.SouthResize, InputSystemCursorShape.SizeNorthSouth },
            { CefCursorType.NorthSouthResize, InputSystemCursorShape.SizeNorthSouth },
            { CefCursorType.RowResize, InputSystemCursorShape.SizeNorthSouth },
            { CefCursorType.NorthEastResize, InputSystemCursorShape.SizeNortheastSouthwest },
            { CefCursorType.SouthWestResize, InputSystemCursorShape.SizeNortheastSouthwest },
            { CefCursorType.NorthEastSouthWestResize, InputSystemCursorShape.SizeNortheastSouthwest },
            { CefCursorType.NorthWestResize, InputSystemCursorShape.SizeNorthwestSoutheast },
            { CefCursorType.SouthEastResize, InputSystemCursorShape.SizeNorthwestSoutheast },
            { CefCursorType.NorthWestSouthEastResize, InputSystemCursorShape.SizeNorthwestSoutheast },
            { CefCursorType.Move, InputSystemCursorShape.SizeAll },
            { CefCursorType.Grab, InputSystemCursorShape.Hand },
            { CefCursorType.Grabbing, InputSystemCursorShape.Hand },
            { CefCursorType.NotAllowed, InputSystemCursorShape.UniversalNo },
            { CefCursorType.NoDrop, InputSystemCursorShape.UniversalNo },
            { CefCursorType.DragAndDropNone, InputSystemCursorShape.UniversalNo },
        };

        public static InputSystemCursorShape GetShape(CefCursorType cursorType)
        {
            return _map.TryGetValue(cursorType, out var shape) ? shape : InputSystemCursorShape.Arrow;
        }
    }
}
