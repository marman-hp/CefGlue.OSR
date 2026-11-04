using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Xilium.CefGlue.Avalonia.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal class OffScreenControlHost<TControl, TFrame> : CefControl<TControl>, IOffScreenTargetHost
        where TControl : BaseCefControl
    {
        private const int MouseWheelDelta = 100;

        private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
        private static readonly DataFormat<string> UriListFormat = DataFormat.CreateStringPlatformFormat("text/uri-list");

        private PointerPressedEventArgs _lastPointerEvent;
        private Cursor _currentDragCursor;
        private Cursor _previousCursor;
        private volatile CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;

        private CefDragOperationsMask? _lastAppliedCursorOps;

        private long _lastDragOverForwardMs = -1;
        private const long DragOverThrottleMs = 16;

        private bool _activeOutboundDrag;

        private bool _isDragOverOwnControl;

        private OffScreenRenderSurface _renderSurface;

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

        public OffScreenControlHost(TControl control) : base(control)
        {
            DragDrop.SetAllowDrop(control, true);

            control.AttachedToVisualTree += OnAttachedToVisualTree;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;

            control.LostFocus += OnLostFocus;

            control.PointerMoved += OnPointerMoved;
            control.PointerExited += OnPointerExited;

            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;

            control.PointerWheelChanged += OnPointerWheelChanged;

            control.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            control.AddHandler(DragDrop.DropEvent, OnDrop);

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.TextInput += OnTextInput;

            _imeClient = new AvaloniaImeClient(control, GetImeCursorRect);
            _imeClient.PreeditChanged += OnPreeditChanged;
            // No IME on Linux yet: Avalonia 12.1's D-Bus IME (IBus/Fcitx) loses the "client draws preedit"
            // capability when it is sent before its D-Bus connection is up, so the preedit stays in the IME's
            // own window and typing gets stuck. Without a client, keys reach the page as plain text.
            control.TextInputMethodClientRequested += (_, e) => e.Client = OperatingSystem.IsLinux() ? null : _imeClient;

            // The IME stays enabled while the browser has focus, except on a password field (see
            // SetPasswordFieldFocused): CEF doesn't reliably report when a text field gets focus
            // (OnVirtualKeyboardRequested may never say "text", e.g. a page focusing its own search box).
        }

        // Avalonia asks the FOCUSED element whether the IME is enabled, and keyboard focus is on the control's
        // focus proxy (a child), so the setting has to go on the children too, not just on the control.
        private void SetImeEnabled(bool enabled)
        {
            foreach (var element in _control.GetSelfAndLogicalDescendants().OfType<InputElement>())
            {
                InputMethod.SetIsInputMethodEnabled(element, enabled);
            }
        }

        private readonly AvaloniaImeClient _imeClient;
        private volatile CefRectangle[] _imeBounds = Array.Empty<CefRectangle>();
        private Rect _lastImeRect = new Rect(0, 0, 1, 20);
        private int _preeditCursor;
        private bool _preeditActive;
        private bool _commitWindowOpen;

        public void SetTextInputActive(bool active)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (active)
                {
                    EnsureImeOpen();
                }
            });
        }

        public void SetPasswordFieldFocused(bool focused)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetImeEnabled(!focused);
            });
        }

        private void EnsureImeOpen()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var hwnd = TopLevel.GetTopLevel(_control)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            Xilium.CefGlue.Avalonia.Platform.Windows.WindowsImeOpenStatus.EnsureOpen(hwnd);
        }

        public void UpdateImeCompositionBounds(CefRange selectedRange, CefRectangle[] characterBounds)
        {
            if (characterBounds == null || characterBounds.Length == 0)
            {
                return;
            }

            _imeBounds = characterBounds;
            Dispatcher.UIThread.Post(() => _imeClient.NotifyCursorRectangleChanged());
        }

        private Rect GetImeCursorRect()
        {
            var bounds = _imeBounds;
            if (_preeditActive && bounds.Length > 0)
            {
                var index = Math.Min(_preeditCursor, bounds.Length);
                var reference = bounds[Math.Min(index, bounds.Length - 1)];
                var x = index >= bounds.Length ? reference.X + reference.Width : reference.X;
                _lastImeRect = new Rect(x, reference.Y, 1, reference.Height);
            }

            return _lastImeRect;
        }

        private void OnPreeditChanged(string text, int cursor)
        {
            if (text.Length > 0)
            {
                _preeditActive = true;
                _commitWindowOpen = false;
                _preeditCursor = cursor;
                CompositionUpdated?.Invoke(text, cursor);
                return;
            }

            if (!_preeditActive)
            {
                return;
            }

            _preeditActive = false;
            _imeBounds = Array.Empty<CefRectangle>();
            _commitWindowOpen = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_commitWindowOpen)
                {
                    _commitWindowOpen = false;
                    CompositionUpdated?.Invoke(string.Empty, 0);
                }
            }, DispatcherPriority.Background);
        }

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public void AttachSurface<TSurface>(TSurface surface) where TSurface : IFrameSurface<TFrame>
        {
            ((IFrameSurfaceHost<TFrame>)_control).AttachSurface(surface);
            _renderSurface = new AvaloniaRenderSurface<TSurface, TFrame>(surface);
        }

        public void AttachRenderSurface(OffScreenRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
        }

        private void OnTextInput(object sender, TextInputEventArgs e)
        {
            if ((_preeditActive || _commitWindowOpen) && !string.IsNullOrEmpty(e.Text))
            {
                _preeditActive = false;
                _commitWindowOpen = false;
                _imeBounds = Array.Empty<CefRectangle>();
                CompositionCommitted?.Invoke(e.Text);
                e.Handled = true;
                return;
            }

            var handled = false;
            TextInput?.Invoke(e.Text, out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            var key = e.Key;
            if (key == Key.Tab
                || key == Key.Home || key == Key.End
                || key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right
            )
            {
                handled = true;
            }

            e.Handled = handled;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            _isDragOverOwnControl = false;
            _lastAppliedCursorOps = null;
            _lastDragOverForwardMs = -1;

            if (OperatingSystem.IsWindows())
            {
                var screen = _control.PointToScreen(e.GetPosition(_control));
                NativeDragDropHelper.NotifyDrop(e.DataTransfer, screen.X, screen.Y, (int)e.DragEffects);
            }

            _control.Cursor = _previousCursor;
            Drop?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.DragEffects.AsCefDragOperationsMask());
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (_currentAllowedDragOps != CefDragOperationsMask.None)
            {
                e.DragEffects = _currentAllowedDragOps.AsDragDropEffects();
            }

            if (OperatingSystem.IsWindows())
            {
                var screen = _control.PointToScreen(e.GetPosition(_control));
                NativeDragDropHelper.NotifyDragOver(screen.X, screen.Y, (int)_currentAllowedDragOps.AsDragDropEffects());
            }

            var nowMs = Environment.TickCount64;
            if (_lastDragOverForwardMs < 0 || nowMs - _lastDragOverForwardMs >= DragOverThrottleMs)
            {
                _lastDragOverForwardMs = nowMs;
                DragOver?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.DragEffects.AsCefDragOperationsMask());
            }

            if (_lastAppliedCursorOps != _currentAllowedDragOps)
            {
                _control.Cursor = _currentDragCursor;
                _lastAppliedCursorOps = _currentAllowedDragOps;
            }
        }

        private void OnDragLeave(object sender, RoutedEventArgs e)
        {
            _isDragOverOwnControl = false;
            _lastAppliedCursorOps = null;

            if (OperatingSystem.IsWindows())
            {
                NativeDragDropHelper.NotifyDragLeave();
            }

            DragLeave?.Invoke();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (_isDragOverOwnControl)
            {
                OnDragOver(sender, e);
                return;
            }

            _isDragOverOwnControl = true;
            _previousCursor = _control.Cursor;

            if (OperatingSystem.IsWindows())
            {
                var hwnd = global::Avalonia.Controls.TopLevel.GetTopLevel(_control)?.TryGetPlatformHandle()?.Handle;
                if (hwnd.HasValue && hwnd.Value != IntPtr.Zero)
                {
                    var screen = _control.PointToScreen(e.GetPosition(_control));
                    NativeDragDropHelper.NotifyDragEnter(hwnd.Value, e.DataTransfer, screen.X, screen.Y, (int)e.DragEffects);
                }
            }

            DragEnter?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.GetDragData(), e.DragEffects.AsCefDragOperationsMask());
        }

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            MouseWheelChanged?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), (int)e.Delta.X * MouseWheelDelta, (int)e.Delta.Y * MouseWheelDelta);
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_activeOutboundDrag)
            {
                return;
            }

            var button = e.AsCefMouseButtonType();
            var ev = e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset);
            MouseButtonReleased?.Invoke(ev, button);
            if (button == CefMouseButtonType.Left)
            {
                e.Pointer.Capture(null);
            }
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _lastPointerEvent = e;

            if (_activeOutboundDrag)
            {
                return;
            }

            var button = e.AsCefMouseButtonType();
            var ev = e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset);
            MouseButtonPressed?.Invoke(this, ev, button, e.ClickCount);
        }

        private void OnPointerExited(object sender, PointerEventArgs e)
        {
            if (_activeOutboundDrag)
            {
                return;
            }
            MouseLeave?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset));
        }

        public void ReleaseMouseCapture()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _lastPointerEvent?.Pointer?.Capture(null);
            }
            else
            {
                Dispatcher.UIThread.Invoke(() => _lastPointerEvent?.Pointer?.Capture(null));
            }
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (_activeOutboundDrag)
            {
                return;
            }
            MouseMoved?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset));
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_control.IsContextMenuOpen)
            {
                return;
            }

            if (_control.IsPopupSelectOpen)
            {
                _control.IsPopupSelectOpen = false;
                return;
            }

            LostFocus?.Invoke();
        }

        private Window _imeWatchedWindow;

        private void OnImeHostWindowPositionChanged(object sender, PixelPointEventArgs e) =>
            _imeClient.NotifyCursorRectangleChanged();

        private void OnDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (_imeWatchedWindow != null)
            {
                _imeWatchedWindow.PositionChanged -= OnImeHostWindowPositionChanged;
                _imeWatchedWindow = null;
            }

            _lastPointerEvent = null;
            _previousCursor = null;
            _currentDragCursor = null;

            VisibilityChanged?.Invoke(false);
        }

        private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            VisibilityChanged?.Invoke(true);

            var topLevel = TopLevel.GetTopLevel(_control);

            if (topLevel is Window window && !ReferenceEquals(window, _imeWatchedWindow))
            {
                if (_imeWatchedWindow != null)
                {
                    _imeWatchedWindow.PositionChanged -= OnImeHostWindowPositionChanged;
                }

                _imeWatchedWindow = window;
                window.PositionChanged += OnImeHostWindowPositionChanged;
            }

            if (RenderSurface != null && topLevel != null && topLevel.RenderScaling != RenderSurface.DeviceScaleFactor)
            {
                RenderSurface.DeviceScaleFactor = (float)topLevel.RenderScaling;
                ScreenInfoChanged?.Invoke(RenderSurface.DeviceScaleFactor);
            }
        }

        protected virtual Visual MousePositionReferential => _control;

        protected virtual Point MousePositionOffset => default;

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            var screenCoordinates = Dispatcher.UIThread.CheckAccess()
                ? _control.PointToScreen(new Point(point.X, point.Y))
                : Dispatcher.UIThread.Invoke(() => _control.PointToScreen(new Point(point.X, point.Y)));

            return new CefPoint(screenCoordinates.X, screenCoordinates.Y);
        }

        private WindowState _previousWindowState;

        public void SetFullscreen(bool fullscreen)
        {
            void Apply()
            {
                var window = TopLevel.GetTopLevel(_control) as Window;
                if (window == null)
                {
                    return;
                }

                if (fullscreen)
                {
                    if (window.WindowState == WindowState.FullScreen)
                    {
                        return;
                    }

                    _previousWindowState = window.WindowState;
                    window.WindowState = WindowState.FullScreen;
                }
                else
                {
                    if (window.WindowState != WindowState.FullScreen)
                    {
                        return;
                    }

                    window.WindowState = _previousWindowState;
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply);
            }
        }

        public override IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            var platformHandle = GetHostWindowPlatformHandle();
            return platformHandle?.Handle;
        }

        public virtual void Focus()
        {
            _control.FocusContent();
        }

        public async Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            try
            {
                return await StartDragCore(dragData, allowedOps, x, y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AVALONIA-DND] StartDrag EXCEPTION: {ex}");
                throw;
            }
        }

        private async Task<CefDragOperationsMask> StartDragCore(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            var lastPointerEvent = this._lastPointerEvent;
            if (lastPointerEvent == null)
            {
                return CefDragOperationsMask.None;
            }

            CefDragOperationsMask result;

            if (OperatingSystem.IsWindows())
            {
                result = await Dispatcher.UIThread.InvokeAsync(() => RunNativeDrag(dragData, allowedOps));
            }
            else
            {
                var item = new DataTransferItem();
                item.SetText(dragData.FragmentText ?? dragData.LinkUrl ?? "");

                if (!string.IsNullOrEmpty(dragData.LinkUrl))
                {
                    item.Set(UriListFormat, dragData.LinkUrl);
                }

                if (!string.IsNullOrEmpty(dragData.FragmentHtml))
                {
                    item.Set(HtmlFormat, dragData.FragmentHtml);
                }

                var dataTransfer = new DataTransfer();
                dataTransfer.Add(item);

                _activeOutboundDrag = true;
                try
                {
                    // The drag icon lives entirely on the UI thread: its Xlib connection is not thread-safe.
                    var effects = await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var dragIcon = OperatingSystem.IsLinux() ? TryShowDragIcon(dragData) : null;
                        var dragIconTimer = dragIcon == null ? null
                            : new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => dragIcon.FollowPointer());
                        dragIconTimer?.Start();
                        try
                        {
                            return await DragDrop.DoDragDropAsync(lastPointerEvent, dataTransfer, allowedOps.AsDragDropEffects());
                        }
                        finally
                        {
                            dragIconTimer?.Stop();
                            dragIcon?.Close();
                        }
                    });
                    result = effects.AsCefDragOperationsMask();
                }
                finally
                {
                    _activeOutboundDrag = false;
                }
            }

            this._lastPointerEvent = null;
            _previousCursor = null;
            _currentDragCursor = null;
            return result;
        }

        // CEF's drag image (the same one the Windows path hands to IDragSourceHelper) in an X11 icon window.
        private static Linux.X11DragIconWindow TryShowDragIcon(CefDragData dragData)
        {
            if (!dragData.HasImage)
            {
                return null;
            }

            var icon = new Linux.X11DragIconWindow();
            try
            {
                if (!icon.Open())
                {
                    return null;
                }

                using var cefImage = dragData.GetImage();
                if (cefImage == null)
                {
                    icon.Close();
                    return null;
                }

                var alphaType = icon.HasAlpha ? CefAlphaType.Premultiplied : CefAlphaType.Opaque;
                using var binary = cefImage.GetAsBitmap(1f, CefColorType.Bgra8888, alphaType, out var width, out var height);
                if (binary == null || width <= 0 || height <= 0)
                {
                    icon.Close();
                    return null;
                }

                var pixels = new byte[width * 4 * height];
                binary.GetData(pixels, pixels.Length, 0);

                var hotspot = dragData.GetImageHotspot();
                icon.Show(pixels, width, height, hotspot.X, hotspot.Y);
                return icon;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AVALONIA-DND] drag icon failed: {ex}");
                icon.Close();
                return null;
            }
        }

        private CefDragOperationsMask RunNativeDrag(CefDragData dragData, CefDragOperationsMask allowedOps)
        {
            using (var comDataObject = new ComDataObject())
            {
                if (!string.IsNullOrEmpty(dragData.FragmentText))
                {
                    comDataObject.SetHGlobalData(ComDataObject.CF_TEXT, System.Text.Encoding.Default.GetBytes(dragData.FragmentText + "\0"));
                    comDataObject.SetHGlobalData(ComDataObject.CF_UNICODETEXT, System.Text.Encoding.Unicode.GetBytes(dragData.FragmentText + "\0"));
                }

                if (!string.IsNullOrEmpty(dragData.FragmentHtml))
                {
                    var html = FormatHtmlForWindows(dragData.FragmentHtml);
                    comDataObject.SetTextFormat("HTML Format", System.Text.Encoding.UTF8.GetBytes(html));
                    comDataObject.SetTextFormat("text/html", System.Text.Encoding.Unicode.GetBytes(html + "\0"));
                }

                if (!string.IsNullOrEmpty(dragData.LinkUrl))
                {
                    var text = dragData.FragmentText ?? dragData.LinkUrl;
                    comDataObject.SetHGlobalData(ComDataObject.CF_TEXT, System.Text.Encoding.Default.GetBytes(text + "\0"));
                    comDataObject.SetHGlobalData(ComDataObject.CF_UNICODETEXT, System.Text.Encoding.Unicode.GetBytes(text + "\0"));
                }

                var fileNames = dragData.GetFileNames();
                if (fileNames != null && fileNames.Length > 0)
                {
                    comDataObject.SetFileDropList(fileNames);
                }

                NativeDragDropHelper.AttachDragImageIfPresent(dragData, comDataObject);

                var allowedEffects = 0;
                if (allowedOps.HasFlag(CefDragOperationsMask.Every))
                {
                    allowedEffects = NativeDragDropHelper.DROPEFFECT_COPY | NativeDragDropHelper.DROPEFFECT_MOVE | NativeDragDropHelper.DROPEFFECT_LINK;
                }
                else
                {
                    if (allowedOps.HasFlag(CefDragOperationsMask.Copy)) allowedEffects |= NativeDragDropHelper.DROPEFFECT_COPY;
                    if (allowedOps.HasFlag(CefDragOperationsMask.Move)) allowedEffects |= NativeDragDropHelper.DROPEFFECT_MOVE;
                    if (allowedOps.HasFlag(CefDragOperationsMask.Link)) allowedEffects |= NativeDragDropHelper.DROPEFFECT_LINK;
                }

                _activeOutboundDrag = true;
                try
                {
                    var resultEffect = NativeDragDropHelper.RunDoDragDrop(comDataObject, allowedEffects, () => _currentAllowedDragOps, () => _isDragOverOwnControl);

                    var mask = CefDragOperationsMask.None;
                    if ((resultEffect & NativeDragDropHelper.DROPEFFECT_COPY) != 0) mask |= CefDragOperationsMask.Copy;
                    if ((resultEffect & NativeDragDropHelper.DROPEFFECT_MOVE) != 0) mask |= CefDragOperationsMask.Move;
                    if ((resultEffect & NativeDragDropHelper.DROPEFFECT_LINK) != 0) mask |= CefDragOperationsMask.Link;
                    return mask;
                }
                finally
                {
                    _activeOutboundDrag = false;
                }
            }
        }

        private static string FormatHtmlForWindows(string html, string sourceUrl = "")
        {
            var fragment = html;

            var fullHtml =
                "<html>\r\n" +
                "<body>\r\n" +
                "<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n" +
                "</body>\r\n" +
                "</html>\r\n";

            var headerTemplate = "Version:0.9\r\n" +
                                    "StartHTML:0000000000\r\n" +
                                    "EndHTML:0000000000\r\n" +
                                    "StartFragment:0000000000\r\n" +
                                    "EndFragment:0000000000\r\n" +
                                    (string.IsNullOrEmpty(sourceUrl) ? "" : $"SourceURL:{sourceUrl}\r\n");

            var enc = System.Text.Encoding.UTF8;

            var startHtml = enc.GetByteCount(headerTemplate);
            var endHtml = startHtml + enc.GetByteCount(fullHtml);

            var startFragCharIndex = fullHtml.IndexOf("<!--StartFragment-->") + "<!--StartFragment-->".Length;
            var endFragCharIndex = fullHtml.IndexOf("<!--EndFragment-->");
            var startFragment = startHtml + enc.GetByteCount(fullHtml.Substring(0, startFragCharIndex));
            var endFragment = startHtml + enc.GetByteCount(fullHtml.Substring(0, endFragCharIndex));

            var finalHeader = headerTemplate
                .Replace("StartHTML:0000000000", $"StartHTML:{startHtml:D10}")
                .Replace("EndHTML:0000000000", $"EndHTML:{endHtml:D10}")
                .Replace("StartFragment:0000000000", $"StartFragment:{startFragment:D10}")
                .Replace("EndFragment:0000000000", $"EndFragment:{endFragment:D10}");

            return finalHeader + fullHtml;
        }

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentAllowedDragOps = allowedOps;
            _currentDragCursor = CursorsProvider.GetCursorFromCefType(allowedOps);
        }

        public override bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            if (_activeOutboundDrag || _isDragOverOwnControl)
            {
                return true;
            }

            var cursor = CursorsProvider.GetCursorFromCefType(cursorType);
            Dispatcher.UIThread.Post(() => _control.Cursor = cursor);
            return true;
        }
    }
}
