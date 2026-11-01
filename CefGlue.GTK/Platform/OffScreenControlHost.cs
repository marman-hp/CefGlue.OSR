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
    /// in role. v1 scaffold: mouse/keyboard/resize/focus are wired; drag-and-drop, context menu,
    /// tooltips, fullscreen and cursor updates are stubbed (no-op/never-fire) - not because GTK can't
    /// do them, but because getting a page rendering and interactive at all is the first milestone
    /// (mirrors this whole project's "Hello World, then build up incrementally" pattern for every
    /// other platform head so far).
    /// </summary>
    internal class OffScreenControlHost<TControl, TFrame> : IOffScreenTargetHost
        where TControl : BaseCefControl
    {
        protected readonly TControl _control;
        private readonly Widget _widget;
        private OffScreenRenderSurface _renderSurface;
        private Popover _currentContextMenu;

        // Every Gtk/Gdk call in this class has to run on the GTK main loop thread - GTK isn't
        // thread-safe, and calling it from a foreign thread is a confirmed live crash class in
        // this project already (see PopupControlHost's own remarks, and OpenContextMenu's below:
        // a g_ref_count_inc-on-NULL segfault was traced back to exactly this before it was fixed).
        // CefGlueAdapter.OnHandleOpenContextMenu calls straight through from whatever thread
        // CEF itself invoked the context-menu callback on, with no marshaling of its own - every
        // platform head's OpenContextMenu is expected to marshal itself (mirrors CefGlue.WPF's
        // Dispatcher.BeginInvoke/CefGlue.WinUI's DispatcherQueue.TryEnqueue/CefGlue.Avalonia's
        // Dispatcher.UIThread.Post at the top of their own identical methods).
        private readonly SynchronizationContext _uiContext = SynchronizationContext.Current;

        public event Action GotFocus;
        public event Action LostFocus;
        public event Action<CefSize> SizeChanged;
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        // No GTK IME bridge yet - text arrives per key through TextInput.
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
                // CEF only ever calls OpenContextMenu in response to a right-click it already
                // processed - but the native GTK release half of that SAME right-click apparently
                // never reaches clickController.OnReleased once the Popover below takes over/grabs
                // input (confirmed live, 2026-08-17: _currentModifiers still showed RightMouseButton
                // set on every subsequent press, forever, after opening any context menu - clean on
                // fresh app start, permanently broken the instant the very first right-click opened
                // a menu, completely independent of navigation - was misdiagnosed as a back-
                // navigation bug at first because it happened to be tested that way). Every
                // MouseMoved sent afterward then falsely reported "left AND right both held" during
                // an ordinary single-button drag, which Blink's own drag/selection gesture
                // recognition apparently refuses to treat as a valid single-button drag - explains
                // why plain click (no sustained motion) kept working but click-and-hold-drag
                // (text selection, DnD start) broke permanently after the first right-click of the
                // session. Clear it explicitly here, the one place we know for certain a right-click
                // just happened whether or not GTK's own release event ever shows up.
                _currentModifiers &= ~CefEventFlags.RightMouseButton;

                // Defensive: a fast second right-click can arrive before an earlier menu's own
                // Closed handler has actually run - force it closed first (mirrors CefGlue.WinUI's
                // identical guard) so ITS callback gets cancelled instead of leaking, rather than
                // letting this new one stomp _currentContextMenu out from under it.
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
                            if (isHandled) return; // guard double click
                            isHandled = true;
                        }
                        callback.Continue(commandId, CefEventFlags.None);
                        popover.Popdown();
                    }

                    if (entry.IsChecked.HasValue)
                    {
                        var check = CheckButton.New();
                        check.Label = label;
                        // CEF's '&' mnemonic markers get translated to '_' below (the classic
                        // Win32/WPF convention) - GTK uses the same '_' character for ITS OWN
                        // mnemonic convention, but only interprets it when UseUnderline is set;
                        // without this the raw underscore shows up literally in the label
                        // (confirmed live, 2026-08-11: "_Undo", "Cu_t" etc. rendered as-is).
                        check.UseUnderline = true;
                        check.Active = entry.IsChecked.Value;
                        check.Sensitive = entry.IsEnabled;
                        // CheckButton lays its own indicator+label out left-aligned already, but
                        // pin it explicitly so it doesn't stretch/center if a sibling Button's Fill
                        // widens the row (see the plain-Button branch below for why that happens).
                        check.Halign = Align.Start;
                        check.OnToggled += (_, _) => Pick();
                        box.Append(check);
                    }
                    else
                    {
                        var button = Button.New();
                        // Button.Label auto-creates an internal Label that CENTERS by default
                        // (confirmed live, 2026-08-11: every row's text sat in the middle instead
                        // of flush left like a real menu). Building the Label explicitly with
                        // Halign=Start, then stretching the Button itself to the row's full width
                        // (Halign=Fill) so that left-alignment has somewhere to actually show
                        // against, is the fix - setting Halign on the Button alone doesn't touch
                        // its internal Label's own alignment.
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
                // Popover centers itself horizontally over PointingTo by default (its speech-
                // bubble-arrow heritage) - Start pins its left edge to the anchor rect's left edge
                // instead, so the menu's top-left corner lands at the click point like a native
                // context menu, instead of straddling it centered (confirmed live, 2026-08-11).
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

                    // Posted rather than run inline: lets any spurious EventControllerFocus.OnLeave
                    // GTK fires while the Popover is closing/unparenting be dispatched FIRST, while
                    // IsContextMenuOpen is still true and still swallowing it - mirrors
                    // CefGlue.WinUI's identical defensive posting for the same MenuFlyout-close race.
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

            // HandleCursorChange (CefGlueAdapter.cs) calls straight through from whatever
            // thread CEF invoked it on, same as OpenContextMenu - marshal onto the GTK main loop
            // before touching Gdk/Widget.Cursor (see OpenContextMenu's own remarks on why).
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
        }

        public void ReleaseMouseCapture()
        {
        }

        // v1 scope (see HookDragAndDrop's own remarks): only FragmentText/LinkUrl are transferred
        // out - no HTML clipboard format, no file drag-out. Drag preview icon IS wired (see
        // SetDragIcon) when CEF supplies one.
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
                // No press has been observed on this control yet this session - nothing to
                // attribute the drag to (Gdk.Drag.Begin requires a real Device).
                return Task.FromResult(CefDragOperationsMask.None);
            }

            _outboundDragTcs = new TaskCompletionSource<CefDragOperationsMask>();

            // Safety net - confirmed live (2026-08-15) that GTK's own Drag machinery can get wedged
            // mid-drag (a "gdk_drop_set_actions: assertion 'priv->state == GDK_DROP_STATE_NONE'
            // failed" inside CEF's own embedded GTK/GDK usage was the trigger observed) such that
            // NEITHER Drag.OnDndFinished NOR Drag.OnCancel ever fires afterward - with nothing else
            // resolving _outboundDragTcs, StartDrag's own guard at the top of this method
            // (`if (_outboundDragTcs != null) return None`) then silently short-circuits every
            // future drag attempt forever, permanently wedging outbound DnD until the process is
            // restarted. 30s is
            // deliberately generous - a real user CAN legitimately hold a drag paused mid-air far
            // longer than any single cross-app XDND round-trip would ever take, and a timeout
            // firing on a still-legitimate in-progress drag would be worse than the bug it's
            // guarding against. Does nothing if the drag already resolved normally by then -
            // TrySetResult on an already-completed TaskCompletionSource is a safe no-op.
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

                // Chrome's own content-area URL-recognition fallback (the path that navigates when
                // you drop a link on a page that has no dragover handler of its own) only checks
                // "text/uri-list" and "text/x-moz-url" - confirmed by reading Chromium's own source
                // (ui/base/x/selection_utils.cc GetURLAtomsFrom) - "text/plain" is NOT in that list,
                // no matter how URL-shaped its content looks. ContentProvider.NewForValue above only
                // ever advertises "text/plain"/"text/plain;charset=utf-8"/"gchararray" (confirmed
                // live), so a link dragged out of this app could never be recognized as
                // navigable by Chrome, however correct its own X11 DnD transport is - a completely
                // separate, simpler bug than CefGlue.Avalonia's still-unresolved grab-mechanism gap
                // (that one DOES offer text/uri-list and still gets rejected - see its own remarks).
                // Firefox, tested live, accepts the plain-text-only version fine (evidently more
                // lenient about recognizing URL-shaped plain text) - only Chrome needed this.
                var provider = textProvider;
                ContentProvider uriListProvider = null;
                if (!string.IsNullOrEmpty(dragData.LinkUrl))
                {
                    var uriListBytes = Encoding.UTF8.GetBytes(dragData.LinkUrl + "\r\n");
                    uriListProvider = ContentProvider.NewForBytes("text/uri-list", GLib.Bytes.New(uriListBytes));

                    // Gdk.ContentProvider has no public NewUnion (see GdkContentProviderInterop's
                    // own remarks) - call the real native function directly and wrap its result.
                    var providerHandles = new[]
                    {
                        textProvider.Handle.DangerousGetHandle(),
                        uriListProvider.Handle.DangerousGetHandle(),
                    };
                    var unionHandle = GdkContentProviderInterop.NewUnion(providerHandles, (UIntPtr)providerHandles.Length);
                    if (unionHandle != IntPtr.Zero)
                    {
                        provider = ContentProvider.NewFromPointer(unionHandle, true);

                        // gdk_content_provider_new_union takes ownership (transfer-full) of the
                        // providers it's given - the union now owns these two references. Without
                        // this, textProvider's/uriListProvider's own C# wrappers would ALSO still
                        // believe they own them and unref again on their own finalization, double-
                        // freeing the same native object as the union's eventual disposal -
                        // confirmed live (2026-08-15) as a crash specifically on the SECOND drag
                        // (deterministic once the first drag's finalizers actually ran, not GC-
                        // timing-random like the separate _activeDrag bug): "GLib-GObject: instance
                        // with invalid (NULL) class pointer", "g_signal_handlers_disconnect_matched:
                        // assertion 'G_TYPE_CHECK_INSTANCE (instance)' failed", "g_object_unref:
                        // assertion 'G_IS_OBJECT (object)' failed". SetHandleAsInvalid is the
                        // standard SafeHandle API for exactly this "ownership transferred elsewhere"
                        // situation - it stops ReleaseHandle (the unref) from ever running for these
                        // two, since the resource genuinely isn't theirs to free anymore.
                        textProvider.Handle.SetHandleAsInvalid();
                        uriListProvider.Handle.SetHandleAsInvalid();
                    }
                }

                // Root the single, unambiguously-owned provider actually being used for the drag's
                // whole lifetime - see _activeDragProviders' own remarks for why this class of bug
                // happens at all. textProvider/uriListProvider don't need rooting here when a union
                // was built (their handles were just invalidated above - nothing left to protect),
                // only in the plain-text-only path where textProvider IS provider (same object).
                _activeDragProviders = new object[] { provider };

                // x/y are widget-local (as given by CEF) - passed straight through as the
                // surface-relative drag origin. UNPROVEN without live testing whether this needs
                // translating via _widget.TranslateCoordinates(_widget.GetRoot(), ...) instead for
                // a widget that isn't flush with its surface's own origin.
                var drag = Drag.Begin(surface, _lastPointerDevice, provider, ToGdkActions(allowedOps), x, y);

                if (drag == null)
                {
                    timeoutCts.Dispose();
                    _outboundDragTcs?.TrySetResult(CefDragOperationsMask.None);
                    return;
                }

                SetDragIcon(drag, dragData);

                // Root the managed wrapper for the drag's whole lifetime - see _activeDrag's own
                // remarks for why (premature GC/finalization mid-flight, intermittent crash fix).
                _activeDrag = drag;

                // Both timing-delay theories tried before this (delaying Drop itself, then delaying
                // just the outbound completion signal) were confirmed live NOT to change the
                // failure pattern at all - ruled out as pure IPC/timing races. Real cause found:
                // see _activeOutboundDrag's own remarks (dual conflicting mouse-event stream to
                // CEF during the async drag) - kept as a plain, undelayed resolution again now that
                // the actual fix lives in the suppression flag below instead.
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

        // Mirrors CefGlue.WinForms' identical GetImage/GetAsBitmap/GetImageHotspot use (see its own
        // StartDrag) - CEF renders its own drag-preview snapshot of whatever's being dragged (a
        // link, selected text, etc.) and hands it over as a plain BGRA bitmap, same format/pipeline
        // BitmapControl.UpdateTexture already builds a Gdk.MemoryTexture from elsewhere in this
        // file's own platform - reused here instead of duplicating the pixel-format assumptions.
        // Gtk.DragIcon.SetFromPaintable is the real, documented GTK4 API for this (distinct from
        // GtkDragSource.SetIcon, which doesn't apply here since this project doesn't use
        // GtkDragSource at all - see HookDragAndDrop's own remarks on why). No-ops (GTK's own
        // default/no icon) when CEF doesn't supply an image for this particular drag.
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

                // Hard guard against a native OOB read - confirmed live (2026-08-11) that skipping
                // this caused a heap-corrupting crash ("malloc(): unaligned tcache chunk detected")
                // after several drags. gdk_memory_texture_new reads h*stride bytes FROM the GBytes
                // using the stride WE pass it - if CEF's own reported binary.Size doesn't actually
                // cover w*h*4 (padding, a stride CEF itself uses internally that differs from our
                // assumed tightly-packed w*4, etc.), GTK's native copy reads past the end of our
                // buffer and corrupts adjacent heap memory. WinForms' equivalent code makes the
                // exact same w*h*4 assumption but can't overrun this way - Marshal.Copy there is
                // bounded by buffer.Length into an already-w*h*4-sized destination, not by a
                // separately-passed stride read from the source. Bail out (no drag icon, not a
                // crash) rather than assume the size always lines up.
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
                // No drag icon, not a crash - see the OOB-read guard above's own remarks.
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

        // Single-action variant of ToGdkActions for enter/motion's own return value, which (like
        // DropTarget.New's own accepted-actions set) GTK requires to be exactly one bit, not a
        // combined mask (see the "did not return a unique preferred action" GTK-CRITICAL this
        // project already hit once from getting that wrong). None (no accepted action right now)
        // is what makes the OS cursor show a plain "dragging" icon instead of the action-specific
        // one (+, move-arrow, link-arrow) - Copy/Move/Link picked in that priority order when
        // CEF's own live UpdateDragCursor feedback allows more than one.
        // Only ever called from the INBOUND path (dropTarget.OnEnter/OnMotion below) - both must
        // return a real, single DragAction to GtkDropTarget, which internally turns that into a
        // gdk_drop_status()/gdk_drop_set_actions() call on the live GdkDrop. Confirmed live
        // (2026-08-15) this could return DragAction.None on the very first OnEnter of a drag
        // session, before CEF's own async UpdateDragCursor response has had a chance to update
        // _currentAllowedDragOps away from its CefDragOperationsMask.None default (a brand new
        // control, or the first inbound drag after one that never set it) - suspected root cause of
        // a GDK-drop state corruption that only surfaces later, after several drags, as
        // "gdk_drop_set_actions: assertion 'priv->state == GDK_DROP_STATE_NONE' failed" followed by
        // outbound AND inbound DnD both permanently wedging (process restart required) - reproduced
        // on both native Xorg and native Wayland (ruling out an X11-specific cause), NOT reproduced
        // on CefGlue.Avalonia doing the identical drop-from-Chrome test (rules out a CEF-engine-
        // internal bug - Avalonia embeds the exact same CEF binary but never touches Gtk.DropTarget
        // at all, since its own X11 DnD source is hand-rolled raw Xlib, not GTK). Falls back to Copy
        // instead - matches this method's own neighboring intent (see HookDragAndDrop's "Copy is
        // the one CEF's own DragOperationsMask almost always includes, so it's the safe default
        // 'yes, I can accept this' answer here") which the old `return DragAction.None` fallback
        // contradicted.
        private static DragAction PreferredGdkAction(CefDragOperationsMask ops)
        {
            if (ops.HasFlag(CefDragOperationsMask.Copy)) return DragAction.Copy;
            if (ops.HasFlag(CefDragOperationsMask.Move)) return DragAction.Move;
            if (ops.HasFlag(CefDragOperationsMask.Link)) return DragAction.Link;
            return DragAction.Copy;
        }

        // ---- Input wiring ----

        private CefEventFlags _currentModifiers;

        // Tracked from motionController's own OnMotion below - GtkEventControllerScroll's "scroll"
        // signal carries only a delta, never a position, but CEF needs a real (x,y) to hit-test
        // which element/frame a wheel event should scroll (see scrollController.OnScroll's own
        // remarks for the live bug this was fixed for).
        private int _lastMouseX;
        private int _lastMouseY;

        private void HookInput()
        {
            _widget.CanFocus = true;

            var focusController = EventControllerFocus.New();
            focusController.OnEnter += (_, _) =>
            {
                // TEMP DIAGNOSTIC (2026-08-11) - resize-lostfocus investigation, remove once resolved.
                Console.Error.WriteLine("[FOCUS] EventControllerFocus.OnEnter -> GotFocus");
                GotFocus?.Invoke();
            };
            focusController.OnLeave += (_, _) =>
            {
                // TEMP DIAGNOSTIC (2026-08-11) - resize-lostfocus investigation, remove once resolved.
                Console.Error.WriteLine($"[FOCUS] EventControllerFocus.OnLeave (IsContextMenuOpen={_control.IsContextMenuOpen})");

                // Spurious - GTK clears focus off this widget while our own context menu (an
                // Overlay child, see OpenContextMenu) is open/closing, which isn't a real browser
                // focus loss (see IsContextMenuOpen).
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
                // Suppressed while an outbound Gdk.Drag is active (see _activeOutboundDrag's own
                // remarks) - Gdk.Drag.Begin is asynchronous/non-blocking, unlike WinForms'/WPF's
                // native DoDragDrop (which blocks the whole thread for the drag's duration, so
                // ordinary mouse handlers there structurally can't fire mid-drag at all). Without
                // this guard, CEF was getting BOTH a plain MouseMoved stream (as if just hovering)
                // AND the DragOver/Drop sequence for the same pointer movement simultaneously -
                // confirmed live (2026-08-10) to be the actual cause of the "first self-drag of a
                // session always drops nothing, every one after that works" bug (ruled out as a
                // CEF-engine-level issue first: WinForms/WPF/WinUI, sharing the identical
                // DragTargetDragEnter/DragTargetDrop call sequence, show no such symptom at all -
                // narrows this specifically to GTK's own non-blocking drag/mouse-forwarding
                // interaction).
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
            clickController.Button = 0; // any button
            clickController.OnPressed += (sender, args) =>
            {
                // Same suppression as motionController.OnMotion above - a press landing on the
                // browser DURING an active outbound drag (e.g. the drop itself, mouse button
                // release-as-press artifacts) shouldn't ALSO be forwarded as an ordinary click.
                if (_activeOutboundDrag)
                {
                    return;
                }

                _widget.GrabFocus();

                // Claim the sequence so this controller (and, critically, the SEPARATE
                // EventControllerMotion above) keeps receiving events for the rest of this
                // press-drag-release cycle even once the pointer strays outside the widget's own
                // allocated bounds - GTK4 only routes motion to whichever controller currently
                // owns the grab for the sequence, not just "whatever's under the cursor right
                // now". Without this, dragging something that needs continuous mousemove while a
                // button is held (e.g. a page's own scrollbar thumb, right at the widget's edge)
                // silently stopped forwarding MouseMoved to CEF the moment the cursor drifted a
                // pixel past the edge - a plain click still worked fine since that's just
                // press+release at the same point, no motion tracking needed (confirmed live,
                // 2026-08-07).
                clickController.SetState(Gtk.EventSequenceState.Claimed);

                var button = ToButtonType(clickController.GetCurrentButton());

                // CefMouseEvent.Modifiers needs the held-button bit set on every MouseMoved sent
                // while that button stays down, not just on the press/release events themselves -
                // Blink's own drag interactions (e.g. dragging a page's scrollbar thumb) key off
                // "did this mousemove arrive with the button held" to tell a drag apart from a
                // plain hover, and _currentModifiers was previously only ever touched by keyboard
                // events. Without this, every MouseMoved during a held-button drag reported no
                // buttons at all, so the thumb never actually dragged even though a plain
                // click (no motion needed) worked fine (confirmed live, 2026-08-07).
                _currentModifiers |= ToButtonModifier(button);

                MouseButtonPressed?.Invoke(this, BuildMouseEvent((int)args.X, (int)args.Y), button, (int)args.NPress);

                // Cached for StartDrag below - Gdk.Drag.Begin needs the originating pointer Device,
                // but StartDrag is called by CEF asynchronously (Blink decided a drag should start
                // internally), not from inside a GTK event handler where a Device would be directly
                // available - same reason CefGlue.Avalonia's own OffScreenControlHost caches its
                // last PointerPressed event for the identical purpose (see its own StartDrag remarks).
                _lastPointerDevice = clickController.GetDevice();
            };
            clickController.OnReleased += (sender, args) =>
            {
                var button = ToButtonType(clickController.GetCurrentButton());
                _currentModifiers &= ~ToButtonModifier(button);

                // Same suppression as OnPressed/motionController above - the release that performs
                // the drop itself shouldn't ALSO be forwarded as an ordinary button-up.
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
                // CEF expects "delta", positive = scroll up/left - GTK reports the opposite sign
                // convention (positive = content moves down/right), same mismatch WinForms/WPF
                // already correct for with their own native wheel deltas.
                //
                // Position was hardcoded to (0,0) here before (2026-08-11 fix) - GTK4's "scroll"
                // signal itself carries ONLY the delta (dx/dy), no cursor position at all (real
                // GtkEventControllerScroll API: `gboolean scroll(self, gdouble dx, gdouble dy)`).
                // But CEF/Blink needs a real position to hit-test WHICH element/frame the wheel
                // event should scroll - reporting (0,0) unconditionally meant every wheel event
                // looked like it happened at the browser's top-left corner regardless of where the
                // cursor actually was, so anything not scrollable at that exact point (e.g. a page
                // with its main scrollable content, or an iframe, positioned anywhere else) never
                // received the event at all. _lastMouseX/Y is tracked from motionController's own
                // OnMotion (the closest thing GTK gives us to "where is the pointer right now" from
                // inside this handler) and reused here instead.
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

        // ---- Drag and drop (v1 scope: text + link, both directions - see project memory before
        // extending: file-drop, HTML-fragment-with-clipboard-header, and a custom drag image were
        // deliberately deferred) ----

        // Cached once GtkDropTarget's own "drop" signal hands over the fully-transferred value
        // (see OnDrop below) - CEF's IOffScreenTargetHost contract only carries CefDragData on
        // DragEnter, not DragOver/Drop (mirrors every other platform's own interface).
        private CefDragData _currentInboundDragData;

        private void HookDragAndDrop()
        {
            // Inbound: something being dragged INTO this widget from outside (another app, or
            // another CEF page). GtkDropTarget.Preload (gtk_drop_target_set_preload) is the real,
            // properly-bound public API for reading the value EARLY (before the drop itself) -
            // once set, GTK eagerly starts the transfer as soon as a drag enters, and GetValue()/
            // the Value property populate as soon as it resolves, checked opportunistically on
            // each "motion" below rather than needing an explicit value-changed notification.
            //
            // An EARLIER attempt (2026-08-10) reached for the same goal by calling the raw
            // Gdk.Internal.Drop.ReadValueAsync + Gio.AsyncResultHelper.NewFromPointer bridge
            // directly (no Task-wrapped public overload exists for ReadValueAsync itself) -
            // reverted the same day after live testing: that read failed ("Could not retrieve
            // type from class struct"), and segfaulted the whole process on a second attempt.
            // Preload sidesteps all of that - no raw P/Invoke, no manual async-result marshaling,
            // just the public GObject property GTK already exposes for exactly this.
            //
            // Why this matters beyond cursor cosmetics (confirmed live, 2026-08-10): every page
            // navigation gets a fresh renderer process (Site Isolation), and EVERY fresh
            // renderer's first self-drag drops nothing while every one after it on that same page
            // succeeds - with GTK-side Enter/Motion/Drop signals byte-for-byte identical between
            // the failing and succeeding attempt (confirmed via diagnostic logging). WinForms/WPF
            // don't share this symptom despite an identical CEF-side call sequence and identical
            // MultiThreadedMessageLoop=true config - but they DO send a live, fully-populated
            // DragTargetDragOver stream throughout the whole hover (their native DnD data is
            // synchronously available from the start), where this project's own v1 GTK code sent
            // NONE (DragOver was gated behind already-resolved data that never arrived before
            // Drop). Best working theory: a fresh renderer's Blink-side drag controller needs a
            // real Enter+Over sequence, not just Enter immediately followed by Drop, to consider a
            // drag session properly established before it'll accept the Drop.
            CreateAndAttachDropTarget();
        }

        // Extracted (2026-08-15) from HookDragAndDrop so it can be called again to recover: a real
        // inbound drop from Chrome was confirmed live to leave GTK's own native drop-tracking
        // permanently wedged ("gdk_drop_set_actions: assertion 'priv->state == GDK_DROP_STATE_NONE'
        // failed" inside CEF's own embedded GTK/GDK usage - content/browser/browser_main_loop.cc),
        // after which EVERY further drag (inbound AND outbound - they evidently share the same
        // underlying native DnD engine on this surface) silently stops working until the process is
        // restarted. There is no way to observe the native assertion itself from managed code (it's
        // a GLib-level stderr print, not a catchable .NET exception) - _inboundStuckTimer below is
        // the only practical mitigation: if an inbound drag Enters but neither Leaves nor Drops
        // within InboundStuckTimeoutSeconds, assume this GdkDrop is wedged and rebuild the whole
        // DropTarget controller from scratch (detach the broken one, construct and attach a fresh
        // one) rather than trying to reset just the now-unrecoverable native object's own state.
        //
        // NOTE (2026-08-17): the actual, unrelated bug that motivated most of this file's DnD
        // investigation turned out to be a stuck _currentModifiers RightMouseButton bit left over
        // from the FIRST context menu opened each session (see OpenContextMenu's own remarks) -
        // nothing to do with GdkDrop/DropTarget at all. This wedge-recovery mechanism is a genuine,
        // independently-useful safety net kept as-is; it just wasn't the root cause being chased.
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

            // enter/motion must return a SINGLE preferred action, not the OR'd set DropTarget.New
            // itself was constructed with (confirmed live, 2026-08-10: returning the full
            // Copy|Move|Link combination here throws a GTK-CRITICAL "did not return a unique
            // preferred action" and silently breaks the whole drag - nothing ever reaches Drop).
            // Copy is the one CEF's own DragOperationsMask almost always includes, so it's the
            // safe default "yes, I can accept this" answer here.
            dropTarget.OnEnter += (sender, args) =>
            {
                // Arm/re-arm the stuck-drag watchdog - see _inboundStuckTimer's own remarks
                // (CreateAndAttachDropTarget). Disarmed in OnLeave/OnDrop below.
                _inboundStuckTimer?.Change(TimeSpan.FromSeconds(InboundStuckTimeoutSeconds), Timeout.InfiniteTimeSpan);

                // Deliberately NOT reset here (tried resetting to None, then to Copy, both on
                // 2026-08-11 - the None version visibly flashed the cursor to a plain arrow at the
                // start of every drag since OnMotion reads this field again almost immediately,
                // before CEF's own UpdateDragCursor response for THIS drag has had time to arrive
                // over its async round-trip). Mirrors CefGlue.WinForms' own OnDragEnter, which
                // never resets its equivalent field either (`e.Effect =
                // MapCefOpsToWinFormsEffect(_currentAllowedDragOps)`, used as-is, stale or not) -
                // simplest and already proven correct there, so matched here instead of guarding
                // against staleness that apparently isn't a real problem in practice.
                return PreferredGdkAction(_currentAllowedDragOps);
            };

            dropTarget.OnMotion += (sender, args) =>
            {
                var x = (int)args.X;
                var y = (int)args.Y;

                // Opportunistic check every motion tick - once Preload's own eager transfer
                // resolves, GetValue() stops returning null. try/catch since a GObject.Value
                // wrapping an unset/wrong-type value can throw on GetString() rather than
                // returning null cleanly (confirmed pattern elsewhere in this codebase's own GTK
                // interop - never assume a GValue accessor is purely defensive).
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
                        // GValue accessor threw on an unset/wrong-type value - see this block's own
                        // remarks. Nothing to do, next motion tick will try again.
                    }
                }

                if (_currentInboundDragData != null)
                {
                    DragOver?.Invoke(BuildMouseEvent(x, y), InboundAllowedOps(dropTarget));
                }

                // Reflects CEF's own live per-position accept-state (_currentAllowedDragOps, kept
                // current by UpdateDragCursor - CEF calls that in response to each
                // DragTargetDragOver, now that Preload above actually gets a real stream of those
                // flowing during hover) instead of a hardcoded Copy - this is what makes the OS
                // cursor dynamically switch between a plain "dragging" icon and a "will accept (+)"
                // icon matching real Chromium's own behavior on the same OS, rather than showing
                // "+" unconditionally for the whole drag regardless of what's actually underneath.
                return PreferredGdkAction(_currentAllowedDragOps);
            };

            dropTarget.OnLeave += (sender, args) =>
            {
                // Disarm the stuck-drag watchdog - this session ended cleanly.
                _inboundStuckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                _currentInboundDragData = null;
                DragLeave?.Invoke();
            };

            dropTarget.OnDrop += (sender, args) =>
            {
                // Disarm the stuck-drag watchdog - Drop firing at all (whatever the outcome below)
                // means this GdkDrop progressed normally, not wedged.
                _inboundStuckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                var x = (int)args.X;
                var y = (int)args.Y;

                // The SOURCE's own declared allowed actions (Gdk.Drop.Actions), NOT
                // _currentAllowedDragOps - that field only ever gets populated for OUTBOUND drags
                // via UpdateDragCursor (CEF calling back INTO us), so for an inbound drag it never
                // leaves its None default. Confirmed live (2026-08-10): using
                // _currentAllowedDragOps here meant CefGlueOffScreenAdapter always told
                // Blink "allowed effects = None" on every DragTargetDragEnter/Over/Drop call,
                // which made it silently refuse the drop every time - nothing else was wrong.
                var allowedOps = InboundAllowedOps(dropTarget);

                // Race fallback: OnEnter's own async read hadn't resolved yet (fast drag) - build
                // from the value GtkDropTarget hands over directly to THIS signal instead (per its
                // own contract, guaranteed fully transferred by the time "drop" fires) and send the
                // DragEnter CEF never got, immediately before Drop, so CEF still sees them in order.
                if (_currentInboundDragData == null)
                {
                    var text = args.Value.GetString();
                    var newDragData = BuildInboundDragData(text);
                    if (newDragData == null)
                    {
                        return false;
                    }

                    // REVERTED BACK TO SYNCHRONOUS (2026-08-15) - deferring this via _uiContext.Post
                    // (tried the same day) was confirmed live to break EVERY drop unconditionally,
                    // not just intermittently like the original bug: returning `true` from OnDrop
                    // tells GTK the drop is fully consumed, so GTK immediately finishes/tears down
                    // this GdkDrop - our deferred callback then ran against a drop GTK had already
                    // torn down, failing every single time instead of occasionally wedging. A
                    // strictly worse trade (100% failure vs. occasional lockup after several
                    // successes) - reverted.
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

            // Lazily created once, reused across every rebuild - Timer.Change (used in OnEnter/
            // OnLeave/OnDrop above) re-arms/disarms the SAME timer rather than needing a fresh one
            // per drag session. Starts disarmed (Timeout.InfiniteTimeSpan due time - never fires
            // until OnEnter arms it). The callback runs on a ThreadPool thread (System.Threading.
            // Timer's own contract) - marshaled onto the GTK main loop via _uiContext.Post before
            // touching any widget/controller, same as every other cross-thread entry point in this
            // class.
            if (_inboundStuckTimer == null)
            {
                _inboundStuckTimer = new Timer(_ => _uiContext.Post(__ => CreateAndAttachDropTarget(), null),
                    null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            // Outbound (StartDrag below) deliberately does NOT use a GtkDragSource controller at
            // all - confirmed against the real GTK4 API (docs.gtk.org) that GtkDragSource has no
            // programmatic "start now" method (no gtk_drag_source_drag_begin - it only ever starts
            // a drag from its OWN internal press-and-move-past-threshold gesture recognition,
            // which would race against CEF/Blink's own independent, already-happened decision that
            // a drag should start now). Gdk.Drag.Begin(surface, device, content, actions, x, y) is
            // the actual lower-level, genuinely-programmatic entry point (bypasses GtkDragSource's
            // gesture layer entirely) - used directly in StartDrag instead. Nothing to attach here.
        }

        // Mirrors CefGlue.Avalonia's own AsCefDragData classification (InputExtensions.cs) - plain
        // text always becomes FragmentText; ALSO tagged as a link only when it's a genuine
        // well-formed absolute URI, not just any dragged text that happens to look URL-ish.
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

        // What the DRAG SOURCE (not us) declared it's willing to let happen - Gdk.Drop.Actions
        // mirrors CefGlue.WinForms' identical use of the native e.AllowedEffect (the OS-level
        // drag's own source-declared field) for this exact call, rather than any locally-tracked
        // state. Falls back to Copy (matches OnEnter/OnMotion's own preferred-action default) if
        // CurrentDrop is somehow unavailable.
        private static CefDragOperationsMask InboundAllowedOps(DropTarget dropTarget)
        {
            var drop = dropTarget.CurrentDrop;
            return drop != null ? FromGdkAction(drop.GetActions()) : CefDragOperationsMask.Copy;
        }

        private Device _lastPointerDevice;
        private CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;
        private TaskCompletionSource<CefDragOperationsMask> _outboundDragTcs;

        // Roots the managed Drag wrapper for the whole in-flight native operation - StartDrag
        // previously only held it in a local variable inside the _uiContext.Post lambda, which
        // returns almost immediately after Drag.Begin + subscribing OnDndFinished/OnCancel. With
        // nothing else keeping the managed wrapper alive, .NET's GC could collect/finalize it while
        // the native GdkDrag was still mid-flight (an XDND exchange with an external app like Chrome
        // can take a couple seconds) - confirmed live as the cause of an intermittent, GC-timing-
        // dependent crash ("kadang crash, ga selalu, trigger yang sama" - 2026-08-15):
        // GLib-GObject:ERROR:.../gsignal.c:4069:invalid_closure_notify: assertion failed:
        // (handler != NULL), Aborted (core dumped) - the finalizer disconnecting a signal handler
        // GLib's own table no longer recognized as valid, a classic premature-collection symptom.
        // Cleared once the drag actually finishes/cancels, in both completion handlers below.
        private Drag _activeDrag;

        // Same rooting problem, same fix, one layer down: ContentProvider.NewForValue/NewForBytes/
        // the raw-P/Invoke union wrapper (see GdkContentProviderInterop) were ALSO only held in
        // local variables inside the same lambda - confirmed live (2026-08-15) as a second,
        // consistently-reproducing (not GC-timing-random like _activeDrag's own bug) crash
        // immediately after a successful cross-app drag (Drag.OnDndFinished), inside CEF's own
        // embedded Chromium engine's GTK/GLib integration: "GLib-GObject: instance with invalid
        // (NULL) class pointer", "g_signal_handlers_disconnect_matched: assertion
        // 'G_TYPE_CHECK_INSTANCE (instance)' failed", "g_object_unref: assertion 'G_IS_OBJECT
        // (object)' failed" - same premature-finalization signature as _activeDrag's bug, just for
        // the provider chain instead of the Drag object itself. Rooting all three (kept as a plain
        // object[] since only lifetime matters here, not the specific type) for the drag's whole
        // duration fixes it the same way.
        private object[] _activeDragProviders;

        // True from the moment Gdk.Drag.Begin succeeds (StartDrag) until the drag finishes/cancels
        // - guards HookInput's ordinary mouse forwarding (motionController/clickController) so it
        // doesn't ALSO deliver plain MouseMoved/MouseButtonPressed/MouseButtonReleased to CEF while
        // GTK's own asynchronous drag machinery is independently delivering DragOver/Drop for the
        // same physical pointer movement - see HookInput's own remarks at each guarded call site
        // for the full story of why this was needed (root-caused 2026-08-10 after ruling out CEF-
        // engine-level and pure-timing explanations via live testing on Windows platforms + delay
        // experiments, neither of which reproduced/fixed the symptom).
        private bool _activeOutboundDrag;

        // virtual so PopupControlHost can translate widget-local coordinates into the main view's
        // coordinate space - see its own override for why that's necessary at all.
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

        // Best-effort GDK keyval -> Windows VK code mapping (CEF uses VK codes as its
        // cross-platform key-code convention regardless of host OS). Covers the common editing/
        // navigation keys plus alphanumerics (which line up with uppercase ASCII); anything else
        // falls back to the raw keyval - imperfect for uncommon keys, fine for a v1 scaffold since
        // regular typing goes through Character/TextInput, not WindowsKeyCode.
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
