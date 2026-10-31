using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WPF.Common;

namespace Xilium.CefGlue.WPF.Platform
{
    public class PopupControlHost : IOffScreenPopupHost
    {
        private readonly BitmapControl _mainControl;
        private readonly WpfBitmapSurface _surface;
        private readonly OffScreenRenderSurface _renderSurface;

        public PopupControlHost(BitmapControl mainControl)
        {
            _mainControl = mainControl;

            _surface = new WpfBitmapSurface { ShowOverlayInfo = false };
            _renderSurface = new WpfRenderSurface<WpfBitmapSurface, byte[]>(_surface);
            _mainControl.AttachPopupSurface(_surface);
        }

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int OffsetX { get; private set; }
        public int OffsetY { get; private set; }

        public void MoveAndResize(int x, int y, int width, int height)
        {
            OffsetX = x;
            OffsetY = y;
            Width = width;
            Height = height;

            _mainControl.Dispatcher.BeginInvoke(new Action(() =>
                _mainControl.SetPopupRect(new Rect(x, y, width, height))));
        }

        public void Open()
        {
            _mainControl.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mainControl.SetPopupVisible(true);

                _mainControl.Browser?.GetHost()?.SetFocus(true);
            }));
        }

        public void Close()
        {
            _mainControl.Dispatcher.BeginInvoke(new Action(() =>
                _mainControl.SetPopupVisible(false)));
        }

        public event Action GotFocus;
        public event Action<CefSize> SizeChanged;
        public event Action LostFocus;
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        public event Action<string, int> CompositionUpdated;
        public event Action<string> CompositionCommitted;
        public event Action<IOffScreenTargetHost, CefMouseEvent, CefMouseButtonType, int> MouseButtonPressed;
        public event Action<CefMouseEvent, CefMouseButtonType> MouseButtonReleased;
        public event Action<CefMouseEvent> MouseLeave;
        public event Action<CefMouseEvent> MouseMoved;
        public event Action<CefMouseEvent, int, int> MouseWheelChanged;
        public event Action<CefMouseEvent, CefDragData, CefDragOperationsMask> DragEnter;
        public event Action<CefMouseEvent, CefDragOperationsMask> DragOver;
        public event Action DragLeave;
        public event Action<CefMouseEvent, CefDragOperationsMask> Drop;
        public event Action<float> ScreenInfoChanged;
        public event Action<bool> VisibilityChanged;

        public void Focus()
        {
        }

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor) => point;

        public void SetFullscreen(bool fullscreen)
        {
        }

        public void ReleaseMouseCapture()
        {
        }

        public Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
            => Task.FromResult(CefDragOperationsMask.None);

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
        }

        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight) => null;

        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            callback.Cancel();
        }

        public void CloseContextMenu()
        {
        }

        public void InitializeRender(IntPtr browserHandle)
        {
        }

        public void DestroyRender()
        {
        }

        public bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType) => false;
    }
}
