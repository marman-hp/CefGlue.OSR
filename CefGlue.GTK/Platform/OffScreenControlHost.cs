using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gdk;
using Gtk;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.GTK.Common;
using Xilium.CefGlue.GTK.Interop;

namespace Xilium.CefGlue.GTK.Platform
{
    /// <summary>
    /// GTK's IOffScreenTargetHost, mirroring CefGlue.Avalonia's/CefGlue.WinForms' OffScreenControlHost
    /// in role. Mouse/keyboard/resize/focus, drag-and-drop, context menu, cursor and fullscreen are
    /// wired; tooltips are still stubbed.
    /// </summary>
    internal class OffScreenControlHost<TControl, TFrame> : IOffScreenTargetHost
        where TControl : BaseCefControl
    {
        protected readonly TControl _control;
        private readonly Widget _widget;
        private OffScreenRenderSurface _renderSurface;
        private Popover _currentContextMenu;

        private readonly SynchronizationContext _uiContext = SynchronizationContext.Current;

        public event Action GotFocus;
        public event Action LostFocus;
        public event Action<CefSize> SizeChanged;
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        public event Action<string, int> CompositionUpdated { add { } remove { } }
        public event Action<string> CompositionCommitted { add { } remove { } }
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

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public OffScreenControlHost(TControl control)
        {
            _control = control;
            _widget = control.Widget;

            _control.SizeChanged += (width, height) => SizeChanged?.Invoke(new CefSize(width, height));

            HookInput();
        }

        public void AttachSurface<TSurface>(TSurface surface) where TSurface : IFrameSurface<TFrame>
        {
            ((IFrameSurfaceHost<TFrame>)_control).AttachSurface(surface);
            _renderSurface = new GtkRenderSurface<TSurface, TFrame>(surface);
        }

        /// <summary>
        /// For controls that own a direct GPU-handle-passthrough OffScreenRenderSurface
        /// (TextureControl/GtkTextureRenderSurface) instead of going through
        /// IFrameSurface&lt;T&gt;/IFrameSurfaceHost&lt;T&gt; at all. Mirrors CefGlue.WinUI's
        /// identical method - the render surface itself is wired to the control separately, at the
        /// CefGlueBrowser call site (see CefGlueBrowser.CreateOffScreenControlHost), not forwarded
        /// from here.
        /// </summary>
        public void AttachRenderSurface(OffScreenRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
        }

        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            return XNativeMethods.CreateHostWindow();
        }

        /// <summary>
        /// GTK4 has no built-in "flat list of clickable rows with a callback" menu widget short of
        /// a full Gio.Menu/action-group model (which, per GTK4's own docs, can't host arbitrary
        /// child widgets like our Button/CheckButton rows anyway - a plain Gtk.Popover with a
        /// hand-built Box child, gtk_popover_set_child()'s documented escape hatch for exactly
        /// this case, is the right tool here) - mirrors every other platform head's identical
        /// hand-rolled native-menu-from-MenuEntry approach (CefGlue.WPF's/WinUI's/Avalonia's own
        /// CefControl.OpenContextMenu, each building their own framework's real ContextMenu/
        /// MenuFlyout from the same list). Like those, SubEntries (real submenus) aren't handled -
        /// none of this project's platform heads recurse into them yet, this isn't a regression
        /// specific to GTK.
        ///
        /// An earlier version of this method built the same Popover directly on whatever thread
        /// CEF invoked this on (no _uiContext.Post) - confirmed live (2026-08-11, both WSLg and
        /// native Linux) to eventually crash with a GLib g_ref_count_inc-on-NULL assertion after a
        /// handful of open/close cycles, the classic signature of unsynchronized concurrent
        /// GObject refcount access. Wrapping the whole body in _uiContext.Post fixes that at the
        /// source - see the class-level remarks on why every Gtk/Gdk call needs to run on the GTK
        /// main loop thread.
        /// </summary>
        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            _uiContext.Post(_ =>
            {
                _currentModifiers &= ~CefEventFlags.RightMouseButton;

                _currentContextMenu?.Popdown();

                _control.IsContextMenuOpen = true;

                var popover = Popover.New();
                var menuLock = new object();
                var isHandled = false;

                var box = Box.New(Orientation.Vertical, 0);

                foreach (var entry in menuEntries)
                {
                    if (entry.IsSeparator)
                    {
                        box.Append(Separator.New(Orientation.Horizontal));
                        continue;
                    }

                    var label = entry.Label?.Replace("&", "_") ?? "";
                    var commandId = entry.CommandId;

                    void Pick()
                    {
                        lock (menuLock)
                        {
                            if (isHandled) return;
                            isHandled = true;
                        }
                        callback.Continue(commandId, CefEventFlags.None);
                        popover.Popdown();
                    }

                    if (entry.IsChecked.HasValue)
                    {
                        var check = CheckButton.New();
                        check.Label = label;
                        check.UseUnderline = true;
                        check.Active = entry.IsChecked.Value;
                        check.Sensitive = entry.IsEnabled;
                        check.Halign = Align.Start;
                        check.OnToggled += (_, _) => Pick();
                        box.Append(check);
                    }
                    else
                    {
                        var button = Button.New();
                        var buttonLabel = Label.New(label);
                        buttonLabel.UseUnderline = true;
                        buttonLabel.Halign = Align.Start;
                        button.Child = buttonLabel;
                        button.Halign = Align.Fill;
                        button.Sensitive = entry.IsEnabled;
                        button.AddCssClass("flat");
                        button.OnClicked += (_, _) => Pick();
                        box.Append(button);
                    }
                }

                popover.SetParent(_widget);
                popover.HasArrow = false;
                popover.Autohide = true;
                popover.Halign = Align.Start;
                popover.Position = PositionType.Bottom;
                popover.SetPointingTo(new Gdk.Rectangle { X = x, Y = y, Width = 1, Height = 1 });
                popover.Child = box;

                popover.OnClosed += (_, _) =>
                {
                    lock (menuLock)
                    {
                        if (!isHandled)
                        {
                            callback.Cancel();
                        }
                        isHandled = true;
                    }

                    if (_currentContextMenu == popover)
                    {
                        _currentContextMenu = null;
                    }

                    popover.Unparent();

                    _uiContext.Post(__ =>
                    {
                        _control.IsContextMenuOpen = false;
                        _control.FocusContent();
                    }, null);
                };

                _currentContextMenu = popover;
                popover.Popup();
            }, null);
        }

        public void CloseContextMenu()
        {
            _uiContext.Post(_ => _currentContextMenu?.Popdown(), null);
        }

        public void InitializeRender(IntPtr browserHandle)
        {
        }

        public void DestroyRender()
        {
        }

        /// <summary>
        /// GTK4's Gdk.Cursor.NewFromName takes the SAME cursor-name strings as CSS's own `cursor`
        /// property (https://www.w3.org/TR/css-ui-4/#cursor - GTK's cursor spec is explicitly
        /// modeled on it), which lines up closely with CefCursorType's own values (Chromium's
        /// cursor enum is itself CSS-cursor-keyword-derived) - a name-string lookup table instead
        /// of a native-handle/bitmap mapping (unlike WinForms' HCURSOR-based SetCursor). Panning
        /// cursors (EastPanning etc.) have no direct CSS equivalent - approximated with the
        /// matching directional resize cursor, same approximation CefGlue.Avalonia's own
        /// CursorsProvider effectively makes by falling back to None for most of them (this
        /// mapping is more complete since GTK has real named cursors for things Avalonia's
        /// StandardCursorType enum doesn't, e.g. "cell", "copy", "zoom-in", "context-menu").
        /// cursorHandle (a native HCURSOR-style handle, mainly relevant for CefCursorType.Custom)
        /// isn't used - no code path here to turn an arbitrary native handle into a Gdk.Cursor.
        /// </summary>
        private static readonly Dictionary<CefCursorType, string> CursorNames = new()
        {
            { CefCursorType.Pointer, "default" },
            { CefCursorType.Cross, "crosshair" },
            { CefCursorType.Hand, "pointer" },
            { CefCursorType.IBeam, "text" },
            { CefCursorType.Wait, "wait" },
            { CefCursorType.Help, "help" },
            { CefCursorType.EastResize, "e-resize" },
            { CefCursorType.NorthResize, "n-resize" },
            { CefCursorType.NorthEastResize, "ne-resize" },
            { CefCursorType.NorthWestResize, "nw-resize" },
            { CefCursorType.SouthResize, "s-resize" },
            { CefCursorType.SouthEastResize, "se-resize" },
            { CefCursorType.SouthWestResize, "sw-resize" },
            { CefCursorType.WestResize, "w-resize" },
            { CefCursorType.NorthSouthResize, "ns-resize" },
            { CefCursorType.EastWestResize, "ew-resize" },
            { CefCursorType.NorthEastSouthWestResize, "nesw-resize" },
            { CefCursorType.NorthWestSouthEastResize, "nwse-resize" },
            { CefCursorType.ColumnResize, "col-resize" },
            { CefCursorType.RowResize, "row-resize" },
            { CefCursorType.MiddlePanning, "all-scroll" },
            { CefCursorType.EastPanning, "e-resize" },
            { CefCursorType.NorthPanning, "n-resize" },
            { CefCursorType.NorthEastPanning, "ne-resize" },
            { CefCursorType.NorthWestPanning, "nw-resize" },
            { CefCursorType.SouthPanning, "s-resize" },
            { CefCursorType.SouthEastPanning, "se-resize" },
            { CefCursorType.SouthWestPanning, "sw-resize" },
            { CefCursorType.WestPanning, "w-resize" },
            { CefCursorType.Move, "move" },
            { CefCursorType.VerticalText, "vertical-text" },
            { CefCursorType.Cell, "cell" },
            { CefCursorType.ContextMenu, "context-menu" },
            { CefCursorType.Alias, "alias" },
            { CefCursorType.Progress, "progress" },
            { CefCursorType.NoDrop, "no-drop" },
            { CefCursorType.Copy, "copy" },
            { CefCursorType.None, "none" },
            { CefCursorType.NotAllowed, "not-allowed" },
            { CefCursorType.ZoomIn, "zoom-in" },
            { CefCursorType.ZoomOut, "zoom-out" },
            { CefCursorType.Grab, "grab" },
            { CefCursorType.Grabbing, "grabbing" },
            { CefCursorType.MiddlePanningVertical, "ns-resize" },
            { CefCursorType.MiddlePanningHorizontal, "ew-resize" },
            { CefCursorType.DragAndDropNone, "no-drop" },
            { CefCursorType.DragAndDropMove, "move" },
            { CefCursorType.DragAndDropCopy, "copy" },
            { CefCursorType.DragAndDropLink, "alias" },
        };

        public bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            var name = CursorNames.TryGetValue(cursorType, out var cursorName) ? cursorName : "default";

            _uiContext.Post(_ =>
            {
                _widget.Cursor = Gdk.Cursor.NewFromName(name, null);
            }, null);

            return true;
        }

        public virtual void Focus()
        {
            _control.FocusContent();
        }

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            return point;
        }

        public CefRectangle GetScreenBounds(float deviceScaleFactor) => default;

        public void SetFullscreen(bool fullscreen)
        {
            _uiContext.Post(_ =>
            {
                if (_widget.GetRoot() is not Window window)
                {
                    return;
                }

                if (fullscreen)
                {
                    window.Fullscreen();
                }
                else
                {
                    window.Unfullscreen();
                }
            }, null);
        }

        public void ReleaseMouseCapture()
        {
        }

        public Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            if (_outboundDragTcs != null)
            {
                return Task.FromResult(CefDragOperationsMask.None);
            }

            var text = dragData.FragmentText;
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(dragData.LinkUrl))
            {
                text = dragData.LinkUrl;
            }

            if (string.IsNullOrEmpty(text))
            {
                return Task.FromResult(CefDragOperationsMask.None);
            }

            if (_lastPointerDevice == null)
            {
                return Task.FromResult(CefDragOperationsMask.None);
            }

            _outboundDragTcs = new TaskCompletionSource<CefDragOperationsMask>();

            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var stuckOutboundDragTcs = _outboundDragTcs;
            timeoutCts.Token.Register(() =>
            {
                if (stuckOutboundDragTcs.TrySetResult(CefDragOperationsMask.None))
                {
                    _activeOutboundDrag = false;
                    _activeDrag = null;
                    _activeDragProviders = null;
                }
            });

            _uiContext.Post(_ =>
            {
                var surface = _widget.GetNative()?.GetSurface();

                if (surface == null)
                {
                    timeoutCts.Dispose();
                    _outboundDragTcs?.TrySetResult(CefDragOperationsMask.None);
                    return;
                }

                var textProvider = ContentProvider.NewForValue(new GObject.Value(text));

                var provider = textProvider;
                ContentProvider uriListProvider = null;
                if (!string.IsNullOrEmpty(dragData.LinkUrl))
                {
                    var uriListBytes = Encoding.UTF8.GetBytes(dragData.LinkUrl + "\r\n");
                    uriListProvider = ContentProvider.NewForBytes("text/uri-list", GLib.Bytes.New(uriListBytes));

                    var providerHandles = new[]
                    {
                        textProvider.Handle.DangerousGetHandle(),
                        uriListProvider.Handle.DangerousGetHandle(),
                    };
                    var unionHandle = GdkContentProviderInterop.NewUnion(providerHandles, (UIntPtr)providerHandles.Length);
                    if (unionHandle != IntPtr.Zero)
                    {
                        provider = ContentProvider.NewFromPointer(unionHandle, true);

                        textProvider.Handle.SetHandleAsInvalid();
                        uriListProvider.Handle.SetHandleAsInvalid();
                    }
                }

                _activeDragProviders = new object[] { provider };

                var drag = Drag.Begin(surface, _lastPointerDevice, provider, ToGdkActions(allowedOps), x, y);

                if (drag == null)
                {
                    timeoutCts.Dispose();
                    _outboundDragTcs?.TrySetResult(CefDragOperationsMask.None);
                    return;
                }

                SetDragIcon(drag, dragData);

                _activeDrag = drag;

                _activeOutboundDrag = true;

                drag.OnDndFinished += (_, _) =>
                {
                    var selectedAction = drag.GetSelectedAction();
                    _activeOutboundDrag = false;
                    _activeDrag = null;
                    _activeDragProviders = null;
                    timeoutCts.Dispose();
                    _outboundDragTcs?.TrySetResult(FromGdkAction(selectedAction));
                };
                drag.OnCancel += (_, _) =>
                {
                    _activeOutboundDrag = false;
                    _activeDrag = null;
                    _activeDragProviders = null;
                    timeoutCts.Dispose();
                    _outboundDragTcs?.TrySetResult(CefDragOperationsMask.None);
                };
            }, null);

            return _outboundDragTcs.Task.ContinueWith(t =>
            {
                _outboundDragTcs = null;
                return t.Result;
            });
        }

        private static void SetDragIcon(Drag drag, CefDragData dragData)
        {
            if (!dragData.HasImage)
            {
                return;
            }

            try
            {
                using var cefImage = dragData.GetImage();
                if (cefImage == null)
                {
                    return;
                }

                using var binary = cefImage.GetAsBitmap(1f, CefColorType.Bgra8888, CefAlphaType.Opaque, out var w, out var h);
                if (binary == null || w <= 0 || h <= 0)
                {
                    return;
                }

                var buffer = new byte[binary.Size];
                binary.GetData(buffer, (int)binary.Size, 0);

                var stride = w * 4;

                if (buffer.Length < h * stride)
                {
                    return;
                }

                var texture = MemoryTexture.New(w, h, MemoryFormat.B8g8r8a8Premultiplied, GLib.Bytes.New(buffer), (UIntPtr)stride);

                var hotspot = dragData.GetImageHotspot();
                DragIcon.SetFromPaintable(drag, texture, hotspot.X, hotspot.Y);
            }
            catch (Exception)
            {
            }
        }

        private static CefDragOperationsMask FromGdkAction(DragAction action)
        {
            var mask = CefDragOperationsMask.None;
            if (action.HasFlag(DragAction.Copy)) mask |= CefDragOperationsMask.Copy;
            if (action.HasFlag(DragAction.Move)) mask |= CefDragOperationsMask.Move;
            if (action.HasFlag(DragAction.Link)) mask |= CefDragOperationsMask.Link;
            return mask;
        }

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentAllowedDragOps = allowedOps;
        }

        private static DragAction ToGdkActions(CefDragOperationsMask ops)
        {
            var actions = DragAction.None;
            if (ops.HasFlag(CefDragOperationsMask.Copy)) actions |= DragAction.Copy;
            if (ops.HasFlag(CefDragOperationsMask.Move)) actions |= DragAction.Move;
            if (ops.HasFlag(CefDragOperationsMask.Link)) actions |= DragAction.Link;
            return actions;

        }

        private static DragAction PreferredGdkAction(CefDragOperationsMask ops)
        {
            if (ops.HasFlag(CefDragOperationsMask.Copy)) return DragAction.Copy;
            if (ops.HasFlag(CefDragOperationsMask.Move)) return DragAction.Move;
            if (ops.HasFlag(CefDragOperationsMask.Link)) return DragAction.Link;
            return DragAction.Copy;
        }

        private CefEventFlags _currentModifiers;

        private int _lastMouseX;
        private int _lastMouseY;

        private void HookInput()
        {
            _widget.CanFocus = true;

            var focusController = EventControllerFocus.New();
            focusController.OnEnter += (_, _) =>
            {
                Console.Error.WriteLine("[FOCUS] EventControllerFocus.OnEnter -> GotFocus");
                GotFocus?.Invoke();
            };
            focusController.OnLeave += (_, _) =>
            {
                Console.Error.WriteLine($"[FOCUS] EventControllerFocus.OnLeave (IsContextMenuOpen={_control.IsContextMenuOpen})");

                if (_control.IsContextMenuOpen)
                {
                    return;
                }
                LostFocus?.Invoke();
            };
            _widget.AddController(focusController);

            var motionController = EventControllerMotion.New();
            motionController.OnMotion += (_, args) =>
            {
                _lastMouseX = (int)args.X;
                _lastMouseY = (int)args.Y;

                if (_activeOutboundDrag)
                {
                    return;
                }
                MouseMoved?.Invoke(BuildMouseEvent((int)args.X, (int)args.Y));
            };
            motionController.OnLeave += (_, _) =>
            {
                if (_activeOutboundDrag)
                {
                    return;
                }
                MouseLeave?.Invoke(BuildMouseEvent(0, 0));
            };
            _widget.AddController(motionController);

            var clickController = GestureClick.New();
            clickController.Button = 0;
            clickController.OnPressed += (sender, args) =>
            {
                if (_activeOutboundDrag)
                {
                    return;
                }

                _widget.GrabFocus();

                clickController.SetState(Gtk.EventSequenceState.Claimed);

                var button = ToButtonType(clickController.GetCurrentButton());

                _currentModifiers |= ToButtonModifier(button);

                MouseButtonPressed?.Invoke(this, BuildMouseEvent((int)args.X, (int)args.Y), button, (int)args.NPress);

                _lastPointerDevice = clickController.GetDevice();
            };
            clickController.OnReleased += (sender, args) =>
            {
                var button = ToButtonType(clickController.GetCurrentButton());
                _currentModifiers &= ~ToButtonModifier(button);

                if (_activeOutboundDrag)
                {
                    return;
                }

                MouseButtonReleased?.Invoke(BuildMouseEvent((int)args.X, (int)args.Y), button);
            };
            _widget.AddController(clickController);

            var scrollController = EventControllerScroll.New(EventControllerScrollFlags.BothAxes);
            scrollController.OnScroll += (_, args) =>
            {
                MouseWheelChanged?.Invoke(BuildMouseEvent(_lastMouseX, _lastMouseY), (int)(-args.Dx * 40), (int)(-args.Dy * 40));
                return true;
            };
            _widget.AddController(scrollController);

            var keyController = EventControllerKey.New();
            keyController.OnKeyPressed += (_, args) =>
            {
                _currentModifiers = ToEventFlags(args.State);
                var handled = false;
                KeyDown?.Invoke(BuildKeyEvent(args.Keyval, CefKeyEventType.RawKeyDown), out handled);

                var character = (char)Gdk.Functions.KeyvalToUnicode(args.Keyval);
                if (character != 0 && !char.IsControl(character))
                {
                    var textHandled = false;
                    TextInput?.Invoke(character.ToString(), out textHandled);
                    handled = handled || textHandled;
                }

                return handled;
            };
            keyController.OnKeyReleased += (sender, args) =>
            {
                _currentModifiers = ToEventFlags(args.State);
                KeyUp?.Invoke(BuildKeyEvent(args.Keyval, CefKeyEventType.KeyUp), out var handled);
            };
            _widget.AddController(keyController);

            HookDragAndDrop();
        }

        private CefDragData _currentInboundDragData;

        private void HookDragAndDrop()
        {
            CreateAndAttachDropTarget();
        }

        private DropTarget _dropTarget;
        private Timer _inboundStuckTimer;
        private const int InboundStuckTimeoutSeconds = 30;

        private void CreateAndAttachDropTarget()
        {
            if (_dropTarget != null)
            {
                _widget.RemoveController(_dropTarget);
            }

            var dropTarget = DropTarget.New(GObject.Type.String, DragAction.Copy | DragAction.Move | DragAction.Link);
            dropTarget.SetPreload(true);

            dropTarget.OnEnter += (sender, args) =>
            {
                _inboundStuckTimer?.Change(TimeSpan.FromSeconds(InboundStuckTimeoutSeconds), Timeout.InfiniteTimeSpan);

                return PreferredGdkAction(_currentAllowedDragOps);
            };

            dropTarget.OnMotion += (sender, args) =>
            {
                var x = (int)args.X;
                var y = (int)args.Y;

                if (_currentInboundDragData == null)
                {
                    try
                    {
                        var preloadedValue = dropTarget.GetValue();
                        var text = preloadedValue?.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            var dragData = BuildInboundDragData(text);
                            if (dragData != null)
                            {
                                _currentInboundDragData = dragData;
                                DragEnter?.Invoke(BuildMouseEvent(x, y), dragData, InboundAllowedOps(dropTarget));
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (_currentInboundDragData != null)
                {
                    DragOver?.Invoke(BuildMouseEvent(x, y), InboundAllowedOps(dropTarget));
                }

                return PreferredGdkAction(_currentAllowedDragOps);
            };

            dropTarget.OnLeave += (sender, args) =>
            {
                _inboundStuckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                _currentInboundDragData = null;
                DragLeave?.Invoke();
            };

            dropTarget.OnDrop += (sender, args) =>
            {
                _inboundStuckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                var x = (int)args.X;
                var y = (int)args.Y;

                var allowedOps = InboundAllowedOps(dropTarget);

                if (_currentInboundDragData == null)
                {
                    var text = args.Value.GetString();
                    var newDragData = BuildInboundDragData(text);
                    if (newDragData == null)
                    {
                        return false;
                    }

                    DragEnter?.Invoke(BuildMouseEvent(x, y), newDragData, allowedOps);
                    Drop?.Invoke(BuildMouseEvent(x, y), allowedOps);
                    return true;
                }

                Drop?.Invoke(BuildMouseEvent(x, y), allowedOps);
                _currentInboundDragData = null;
                return true;
            };

            _dropTarget = dropTarget;
            _widget.AddController(dropTarget);

            if (_inboundStuckTimer == null)
            {
                _inboundStuckTimer = new Timer(_ => _uiContext.Post(__ => CreateAndAttachDropTarget(), null),
                    null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        private static CefDragData BuildInboundDragData(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var dragData = CefDragData.Create();
            dragData.SetFragmentText(text);

            if (Uri.IsWellFormedUriString(text, UriKind.Absolute))
            {
                dragData.SetFragmentBaseURL(text);
                dragData.SetLinkURL(text);
                dragData.SetLinkTitle(text);
            }

            return dragData;
        }

        private static CefDragOperationsMask InboundAllowedOps(DropTarget dropTarget)
        {
            var drop = dropTarget.CurrentDrop;
            return drop != null ? FromGdkAction(drop.GetActions()) : CefDragOperationsMask.Copy;
        }

        private Device _lastPointerDevice;
        private CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;
        private TaskCompletionSource<CefDragOperationsMask> _outboundDragTcs;

        private Drag _activeDrag;

        private object[] _activeDragProviders;

        private bool _activeOutboundDrag;

        protected virtual CefMouseEvent BuildMouseEvent(int x, int y) => new CefMouseEvent(x, y, _currentModifiers);

        private CefKeyEvent BuildKeyEvent(uint keyval, CefKeyEventType type)
        {
            var character = (char)Gdk.Functions.KeyvalToUnicode(keyval);

            return new CefKeyEvent
            {
                EventType = type,
                Modifiers = _currentModifiers,
                WindowsKeyCode = ToWindowsKeyCode(keyval),
                NativeKeyCode = (int)keyval,
                Character = character,
                UnmodifiedCharacter = character,
            };
        }

        private static CefMouseButtonType ToButtonType(uint gdkButton) => gdkButton switch
        {
            1 => CefMouseButtonType.Left,
            2 => CefMouseButtonType.Middle,
            3 => CefMouseButtonType.Right,
            _ => CefMouseButtonType.Left,
        };

        private static CefEventFlags ToButtonModifier(CefMouseButtonType button) => button switch
        {
            CefMouseButtonType.Left => CefEventFlags.LeftMouseButton,
            CefMouseButtonType.Middle => CefEventFlags.MiddleMouseButton,
            CefMouseButtonType.Right => CefEventFlags.RightMouseButton,
            _ => CefEventFlags.None,
        };

        private static CefEventFlags ToEventFlags(ModifierType state)
        {
            var flags = CefEventFlags.None;
            if (state.HasFlag(ModifierType.ShiftMask)) flags |= CefEventFlags.ShiftDown;
            if (state.HasFlag(ModifierType.ControlMask)) flags |= CefEventFlags.ControlDown;
            if (state.HasFlag(ModifierType.AltMask)) flags |= CefEventFlags.AltDown;
            return flags;
        }

        private static int ToWindowsKeyCode(uint keyval) => keyval switch
        {
            Gdk.Constants.KEY_BackSpace => 0x08,
            Gdk.Constants.KEY_Tab => 0x09,
            Gdk.Constants.KEY_Return => 0x0D,
            Gdk.Constants.KEY_Escape => 0x1B,
            Gdk.Constants.KEY_space => 0x20,
            Gdk.Constants.KEY_Delete => 0x2E,
            Gdk.Constants.KEY_Left => 0x25,
            Gdk.Constants.KEY_Up => 0x26,
            Gdk.Constants.KEY_Right => 0x27,
            Gdk.Constants.KEY_Down => 0x28,
            Gdk.Constants.KEY_Home => 0x24,
            Gdk.Constants.KEY_End => 0x23,
            >= Gdk.Constants.KEY_a and <= Gdk.Constants.KEY_z => (int)(keyval - Gdk.Constants.KEY_a + 'A'),
            >= Gdk.Constants.KEY_A and <= Gdk.Constants.KEY_Z => (int)keyval,
            >= Gdk.Constants.KEY_0 and <= Gdk.Constants.KEY_9 => (int)keyval,
            _ => (int)keyval,
        };
    }

    /// <summary>
    /// P/Invoke for OffScreenControlHost's throwaway host-view handle - kept outside
    /// OffScreenControlHost&lt;TControl, TFrame&gt; because DllImport can't be applied to a
    /// method nested inside a generic type. Mirrors CefGlue.Avalonia's Platform/Linux/XWindow.cs.
    /// </summary>
    internal static class XNativeMethods
    {
        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(string displayName);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, UIntPtr border, UIntPtr background);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        private static IntPtr _display;

        public static IntPtr CreateHostWindow()
        {
            if (_display == IntPtr.Zero)
            {
                _display = XOpenDisplay(null);
            }

            var handle = XCreateSimpleWindow(_display, XDefaultRootWindow(_display), 0, 0, 1, 1, 0, (UIntPtr)0, (UIntPtr)0);
            XFlush(_display);
            return handle;
        }
    }
}
