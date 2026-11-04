using System;
using System.Threading;
using Gtk;
using Xilium.CefGlue.GTK.Interop;
using Xilium.CefGlue.GTK.Platform;

namespace Xilium.CefGlue.GTK.Common
{
    /// <summary>
    /// Base wrapper hosting a CEF browser (OSR only - GTK head doesn't do windowed mode, see
    /// CefGlue.Common's BaseCefBrowser doc comment for why the browser class itself is never a UI
    /// control). Mirrors CefGlue.WinForms'/CefGlue.WPF's BaseCefControl in role, but - like
    /// CefGlueBrowser (composition, not inheritance) - wraps a Gtk.Widget instead of being one:
    /// GObject subclassing across the managed/native boundary is a much less proven path in
    /// gir.core than plain composition, and this project has no windowed-mode variant that would
    /// need to share a common Widget base with BitmapControl anyway.
    /// </summary>
    internal abstract class BaseCefControl
    {
        private CefBrowser _browser;

        public CefBrowser Browser => _browser;

        public void AttachBrowser(CefBrowser browser) => _browser = browser;

        /// <summary>The actual GTK widget to place in the caller's own widget tree.</summary>
        public abstract Widget Widget { get; }

        /// <summary>Focuses whatever widget actually receives keyboard input for this control.</summary>
        public abstract void FocusContent();

        /// <summary>
        /// When true, resize is batched through the settle-debounce in <see
        /// cref="ReportAllocatedSize"/> instead of firing <see cref="SizeChanged"/> on every
        /// tick-callback frame - mirrors CefGlue.WPF's/CefGlue.WinUI's identical property (see
        /// their BaseCefControl.UseSafeResize). Same convention as those heads: set true on the
        /// main control (CefGlueBrowser wires this up), left false (the default) on a popup
        /// select's own control - a popup's repaints are small/low-frequency and it needs precise
        /// immediate sizing to position correctly, no benefit from batching/ghosting there.
        /// </summary>
        public bool UseSafeResize { get; set; }

        /// <summary>
        /// Suppresses OffScreenControlHost's LostFocus while a native GTK context menu (Popover)
        /// is open/closing - a Popover is its own focus-capable widget, so GTK moves keyboard
        /// focus off the main widget while one is shown, which would otherwise reach CEF as a
        /// real SetFocus(false) and drop the in-flight RunContextMenuCallback/OnContextMenuCommand
        /// before the user can pick anything. Mirrors CefGlue.Avalonia's/WPF's/WinUI's identical
        /// IsContextMenuOpen property and rationale.
        /// </summary>
        public bool IsContextMenuOpen { get; set; }

        /// <summary>
        /// Fired whenever the widget's allocated size changes and <see cref="UseSafeResize"/> is
        /// false (every tick-callback frame, immediately) - or, when true, at most once per resize
        /// "session": only once the size has stopped changing for <see
        /// cref="SettleTickThreshold"/> consecutive tick-callback frames, not on every intermediate
        /// size during an active drag. This is what CEF's WasResized() ends up gated on (via
        /// IOffScreenTargetHost/CefGlueOffScreenAdapter), so firing it on every tick meant every fast
        /// drag hammered CEF's hold_resize_ gating dozens of times per second (project memory,
        /// "Bug baru ke-5": the whole resize-ack-coalescing/stall-watchdog machinery in
        /// DefaultResizeCoordinator (CefGlue.Common) exists to cope with that). Mirrors CefGlue.Avalonia's
        /// ResizeBegin/SafeResize pair - GTK4 has no native resize-begin/end signal any more than
        /// Avalonia's own X11 backend does (confirmed there: WindowResizeReason is always
        /// Unspecified on Linux, never User - see Avalonia's BaseCefControl.OnHostWindowResized
        /// remarks), so both fall back to the identical technique: treat "no further size change
        /// for N ms/ticks" as settled. Unlike WinUI's SwapChainPanel (which needs an explicit
        /// ScaleTransform "ghost" to visually track the live widget size during the suppressed
        /// drag), Gtk.Picture's ContentFit.Fill already auto-stretches whatever Paintable it's
        /// currently holding to the widget's live allocated size on every layout pass - the
        /// ghost-stretch behavior comes for free here when UseSafeResize is true, no extra code
        /// needed for it specifically.
        /// </summary>
        public event Action<int, int> SizeChanged;

        private const int SettleTickThreshold = 3;

        private const int ResizeSettleMilliseconds = 50;

        private int _lastCandidateWidth = -1;
        private int _lastCandidateHeight = -1;
        private int _stableTickCount;
        private int _lastReportedWidth = -1;
        private int _lastReportedHeight = -1;

        private X11ResizeWatcher _x11ResizeWatcher;
        private bool _watcherInitAttempted;
        private SynchronizationContext _uiContext;

        /// <summary>
        /// Called from a subclass's own per-frame tick callback (Picture.AddTickCallback - GTK4
        /// exposes no per-size-change signal without subclassing, which this project avoids, so
        /// polling GetWidth()/GetHeight() every frame is the standard composition-friendly way to
        /// notice allocated-size changes) with the widget's CURRENT allocated size every single
        /// tick, unconditionally. Raises <see cref="SizeChanged"/> immediately when <see
        /// cref="UseSafeResize"/> is false, or only once that size has held steady for <see
        /// cref="SettleTickThreshold"/> consecutive calls AND differs from the last size actually
        /// reported when true - see that event's own remarks for why.
        /// </summary>
        protected void ReportAllocatedSize(int width, int height)
        {
            if (!UseSafeResize)
            {
                if (width != _lastReportedWidth || height != _lastReportedHeight)
                {
                    _lastReportedWidth = width;
                    _lastReportedHeight = height;
                    SizeChanged?.Invoke(width, height);
                }
                return;
            }

            if (!_watcherInitAttempted)
            {
                _watcherInitAttempted = true;
                _uiContext = SynchronizationContext.Current;
                TryStartX11ResizeWatcher();
            }

            if (_x11ResizeWatcher?.IsResizing == true)
            {
                return;
            }

            if (width == _lastCandidateWidth && height == _lastCandidateHeight)
            {
                _stableTickCount++;
            }
            else
            {
                _lastCandidateWidth = width;
                _lastCandidateHeight = height;
                _stableTickCount = 0;
            }

            if (_stableTickCount == SettleTickThreshold &&
                (width != _lastReportedWidth || height != _lastReportedHeight))
            {
                _lastReportedWidth = width;
                _lastReportedHeight = height;
                SizeChanged?.Invoke(width, height);
                ReclaimFocusAfterResize();
            }
        }

        /// <summary>
        /// Best-effort: resolves this control's widget's real X11 window ID and starts an
        /// X11ResizeWatcher on it. Left null (falling back to the tick-callback path above) if the
        /// widget isn't realized yet, its surface isn't backed by X11 (e.g. a Wayland session), or
        /// the watcher's own second Xlib connection fails to open.
        /// </summary>
        private void TryStartX11ResizeWatcher()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            try
            {
                var surface = Widget?.GetNative()?.GetSurface();
                if (surface == null)
                {
                    return;
                }

                var xid = GdkX11Interop.SurfaceGetXid(surface.Handle.DangerousGetHandle());
                if (xid == IntPtr.Zero)
                {
                    return;
                }

                var watcher = new X11ResizeWatcher(
                    xid,
                    TimeSpan.FromMilliseconds(ResizeSettleMilliseconds),
                    onResizeBegin: null,
                    onResizeSettled: () => _uiContext.Post(_ =>
                    {
                        var widget = Widget;
                        if (widget == null)
                        {
                            return;
                        }

                        var width = widget.GetWidth();
                        var height = widget.GetHeight();
                        if (width != _lastReportedWidth || height != _lastReportedHeight)
                        {
                            _lastReportedWidth = width;
                            _lastReportedHeight = height;
                            SizeChanged?.Invoke(width, height);
                            ReclaimFocusAfterResize();
                        }
                    }, null));

                _x11ResizeWatcher = watcher.Start() ? watcher : null;

                Console.Error.WriteLine(_x11ResizeWatcher != null
                    ? "[RESIZE] X11ResizeWatcher started - button-release-gated settle active."
                    : "[RESIZE] X11ResizeWatcher failed to start - falling back to tick-callback settle.");
            }
            catch
            {
                _x11ResizeWatcher = null;
            }
        }

        private void ReclaimFocusAfterResize()
        {
            Console.Error.WriteLine("[FOCUS] ReclaimFocusAfterResize called");
            FocusContent();
        }
    }
}
