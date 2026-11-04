using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gdk;
using Gtk;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.GTK.Common;

namespace Xilium.CefGlue.GTK.Platform
{
    /// <summary>
    /// No-host popup-select host - a state holder (rect/visibility/frame) that owns a plain
    /// Gtk.Picture added as an overlay child of the Gtk.Overlay CefGlueBrowser now wraps
    /// _activeControl's own widget in, not a second Gtk.Popover + second BitmapControl.
    /// Redesigned (2026-08-10) to replace the previous version, which wrapped a real Gtk.Popover -
    /// see CefGlue.WinForms'/CefGlue.WPF's/CefGlue.WinUI's/CefGlue.Avalonia's identical
    /// PopupControlHost redesigns for the general root-cause writeup (a second real surface is a
    /// second real OS/UI-framework focus-capable entity, independent of CEF's own
    /// SetFocus(true/false) concept). GTK's own Popover was ALSO its own specific mess on top of
    /// that general problem - Wayland xdg_popup's implicit compositor grab ate outside clicks
    /// before they ever reached CEF at all (unrelated to Autohide, which made popup rendering fail
    /// outright when toggled on instead - see git history), and its GNOME theme border/shadow never
    /// matched what a real browser's own borderless custom-drawn &lt;select&gt; dropdown looks like
    /// on any OS. None of that exists anymore now that the popup is just another Gtk.Picture
    /// layered via Gtk.Overlay - a plain compositor overlay, not a native xdg_popup surface at all.
    ///
    /// The popup's pixel buffer still goes through the exact same GtkBitmapSurface/GtkRenderSurface/
    /// CpuStagedRenderSurface pipeline the main view already uses (mirroring BitmapControl.
    /// UpdateTexture's identical Gdk.MemoryTexture-from-BGRA-bytes construction here) - none of that
    /// low-level pixel code is touched or duplicated.
    /// </summary>
    internal class PopupControlHost : IOffScreenPopupHost
    {
        private readonly BaseCefControl _mainControl;
        private readonly Picture _popupPicture;
        private readonly GtkBitmapSurface _surface;
        private readonly OffScreenRenderSurface _renderSurface;

        private readonly SynchronizationContext _uiContext = SynchronizationContext.Current;

        public PopupControlHost(Overlay overlay, BaseCefControl mainControl)
        {
            _mainControl = mainControl;

            _popupPicture = Picture.New();
            _popupPicture.ContentFit = ContentFit.Fill;
            _popupPicture.Halign = Align.Start;
            _popupPicture.Valign = Align.Start;
            _popupPicture.Visible = false;

            _popupPicture.CanTarget = false;

            overlay.AddOverlay(_popupPicture);

            _surface = new GtkBitmapSurface { ShowOverlayInfo = false };
            _surface.FrameReady = OnFrameReady;
            _renderSurface = new GtkRenderSurface<GtkBitmapSurface, byte[]>(_surface);
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            if (_surface == null)
            {
                return;
            }

            var region = new CefRectangle();
            var frame = _surface.GetFrameSurface(ref region);

            if (frame == null || region.Width <= 0 || region.Height <= 0)
            {
                return;
            }

            _uiContext.Post(_ =>
            {
                var stride = region.Width * 4;
                var texture = MemoryTexture.New(region.Width, region.Height, MemoryFormat.B8g8r8a8Premultiplied, GLib.Bytes.New(frame), (UIntPtr)stride);
                _popupPicture.SetPaintable(texture);
            }, null);
        }

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int OffsetX { get; private set; }
        public int OffsetY { get; private set; }

        /// <summary>
        /// MarginStart/MarginTop on a Start/Start-aligned widget is the standard cross-toolkit
        /// pseudo-absolute-positioning trick (mirrors every other platform head's identical Margin/
        /// offset approach) - acts as an x/y offset from the Gtk.Overlay's own origin, the same
        /// main-view-relative coordinate space CEF's OnPopupSize rect already uses.
        /// </summary>
        public void MoveAndResize(int x, int y, int width, int height)
        {
            OffsetX = x;
            OffsetY = y;
            Width = width;
            Height = height;

            _uiContext.Post(_ =>
            {
                _popupPicture.MarginStart = x;
                _popupPicture.MarginTop = y;
                _popupPicture.SetSizeRequest(width, height);
            }, null);
        }

        public void Open()
        {
            _uiContext.Post(_ =>
            {
                _popupPicture.Visible = true;

                _mainControl.Browser?.GetHost()?.SetFocus(true);
            }, null);
        }

        public void Close()
        {
            _uiContext.Post(_ =>
            {
                _popupPicture.Visible = false;
            }, null);
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
