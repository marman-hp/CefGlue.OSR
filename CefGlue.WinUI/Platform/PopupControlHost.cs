using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinUI.Common;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal class PopupControlHost : IOffScreenPopupHost
    {
        private readonly BaseCefControl _mainControl;
        private readonly Image _popupImage;
        private readonly WinUIBitmapSurface _surface;
        private readonly OffScreenRenderSurface _renderSurface;
        private WriteableBitmap _popupBitmap;

        public PopupControlHost(Grid rootGrid, BaseCefControl mainControl)
        {
            _mainControl = mainControl;

            _popupImage = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,

                IsHitTestVisible = false,
            };

            rootGrid.Children.Add(_popupImage);

            _surface = new WinUIBitmapSurface { ShowOverlayInfo = false };
            _surface.FrameReady = OnFrameReady;
            _renderSurface = new WinUIRenderSurface<WinUIBitmapSurface, byte[]>(_surface);
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            _mainControl.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, PresentPopup);
        }

        private unsafe void PresentPopup()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var width = _surface.Width;
                var height = _surface.Height;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                if (_popupBitmap == null || _popupBitmap.PixelWidth != width || _popupBitmap.PixelHeight != height)
                {
                    _popupBitmap = new WriteableBitmap(width, height);
                    _popupImage.Source = _popupBitmap;
                }

                var dirtyRect = new CefRectangle();
                var stride = width * 4;
                var byteCount = stride * height;

                var scratch = ArrayPool<byte>.Shared.Rent(byteCount);
                bool presented;
                try
                {
                    fixed (byte* p = scratch)
                    {
                        _surface.CopyFrameSurfaceInto((IntPtr)p, stride, width, height, ref dirtyRect);
                    }

                    presented = dirtyRect.Width > 0 && dirtyRect.Height > 0;

                    if (presented)
                    {
                        using var pixelStream = _popupBitmap.PixelBuffer.AsStream();
                        pixelStream.Write(scratch, 0, byteCount);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }

                if (!presented)
                {
                    continue;
                }

                _popupBitmap.Invalidate();
                return;
            }
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

            _mainControl.DispatcherQueue.TryEnqueue(() => _popupImage.Margin = new Thickness(x, y, 0, 0));
        }

        public void Open()
        {
            _mainControl.DispatcherQueue.TryEnqueue(() =>
            {
                _popupImage.Visibility = Visibility.Visible;

                _mainControl.Browser?.GetHost()?.SetFocus(true);
            });
        }

        public void Close()
        {
            _mainControl.DispatcherQueue.TryEnqueue(() => _popupImage.Visibility = Visibility.Collapsed);
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
