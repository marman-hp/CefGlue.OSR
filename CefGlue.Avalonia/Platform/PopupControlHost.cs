using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia.Common;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal class PopupControlHost : IOffScreenPopupHost
    {
        private readonly BaseCefControl _mainControl;
        private readonly PopupLayer _popupLayer;
        private readonly AvaloniaBitmapSurface _surface;
        private readonly OffScreenRenderSurface _renderSurface;

        public PopupControlHost(Grid rootGrid, BaseCefControl mainControl)
        {
            _mainControl = mainControl;

            _popupLayer = new PopupLayer();
            rootGrid.Children.Add(_popupLayer);

            _surface = new AvaloniaBitmapSurface();
            _surface.FrameReady = OnFrameReady;
            _renderSurface = new AvaloniaRenderSurface<AvaloniaBitmapSurface, byte[]>(_surface);
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            _popupLayer.UpdateFrame(_surface);
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

            Dispatcher.UIThread.Post(() => _popupLayer.Margin = new Thickness(x, y, 0, 0));
        }

        public void Open()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _popupLayer.IsVisible = true;

                _mainControl.Browser?.GetHost()?.SetFocus(true);
            });
        }

        public void Close()
        {
            Dispatcher.UIThread.Post(() => _popupLayer.IsVisible = false);
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

    internal sealed class PopupLayer : Control
    {
        private readonly object _bufferLock = new object();
        private IntPtr _nativeBuffer = IntPtr.Zero;
        private int _bufferCapacityBytes;
        private int _width;
        private int _height;
        private bool _hasFrame;
        private bool _disposed;

        private const int RingSize = 4;
        private readonly IntPtr[] _copyBuffers = new IntPtr[RingSize];
        private readonly int[] _copyBufferCapacities = new int[RingSize];
        private int _nextCopyBufferIndex;

        public PopupLayer()
        {
            IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsVisible = false;
        }

        public unsafe void UpdateFrame(IFrameSurface<byte[]> surface)
        {
            if (_disposed)
            {
                return;
            }

            var width = surface.Width;
            var height = surface.Height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            lock (_bufferLock)
            {
                EnsureBufferCapacity(width, height);

                var d = new CefRectangle();
                surface.CopyFrameSurfaceInto(_nativeBuffer, width * 4, width, height, ref d);

                if (d.Width <= 0 || d.Height <= 0)
                {
                    return;
                }

                _width = width;
                _height = height;
                _hasFrame = true;
            }

            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        private const int SizeRoundingGrid = 64;

        private static int RoundUpToGrid(int value) =>
            ((value + SizeRoundingGrid - 1) / SizeRoundingGrid) * SizeRoundingGrid;

        private void EnsureBufferCapacity(int width, int height)
        {
            var required = width * height * 4;

            if (_nativeBuffer != IntPtr.Zero && required <= _bufferCapacityBytes)
            {
                return;
            }

            if (_nativeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_nativeBuffer);
            }

            var allocatedWidth = RoundUpToGrid(width);
            var allocatedHeight = RoundUpToGrid(height);
            var allocated = allocatedWidth * allocatedHeight * 4;

            _nativeBuffer = Marshal.AllocHGlobal(allocated);
            _bufferCapacityBytes = allocated;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            IntPtr bufferCopy;
            int width, height;

            lock (_bufferLock)
            {
                if (!_hasFrame || _nativeBuffer == IntPtr.Zero || _width <= 0 || _height <= 0)
                {
                    return;
                }

                width = _width;
                height = _height;

                var byteCount = width * height * 4;
                var slot = _nextCopyBufferIndex;
                _nextCopyBufferIndex = (_nextCopyBufferIndex + 1) % RingSize;

                if (_copyBuffers[slot] == IntPtr.Zero || _copyBufferCapacities[slot] < byteCount)
                {
                    if (_copyBuffers[slot] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_copyBuffers[slot]);
                    }

                    var allocatedBytes = RoundUpToGrid(width) * RoundUpToGrid(height) * 4;
                    _copyBuffers[slot] = Marshal.AllocHGlobal(allocatedBytes);
                    _copyBufferCapacities[slot] = allocatedBytes;
                }

                bufferCopy = _copyBuffers[slot];

                unsafe
                {
                    Buffer.MemoryCopy((void*)_nativeBuffer, (void*)bufferCopy, _copyBufferCapacities[slot], byteCount);
                }
            }

            context.Custom(new FrameDrawOp(new Rect(0, 0, width, height), bufferCopy, width, height));
        }

        public void ReleaseBuffer()
        {
            lock (_bufferLock)
            {
                _disposed = true;
                _hasFrame = false;

                if (_nativeBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_nativeBuffer);
                    _nativeBuffer = IntPtr.Zero;
                    _bufferCapacityBytes = 0;
                }

                for (var i = 0; i < RingSize; i++)
                {
                    if (_copyBuffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_copyBuffers[i]);
                        _copyBuffers[i] = IntPtr.Zero;
                        _copyBufferCapacities[i] = 0;
                    }
                }
            }
        }
    }
}
