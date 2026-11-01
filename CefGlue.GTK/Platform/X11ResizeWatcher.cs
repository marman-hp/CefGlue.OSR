using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Xilium.CefGlue.GTK.Platform
{
    /// <summary>
    /// Watches a foreign X11 window's ConfigureNotify stream for resize-settle detection, gated on
    /// the real global mouse-button state so it only fires once the user has actually released the
    /// button - not merely paused moving. Ported verbatim (2026-08-15) from
    /// CefGlue.Avalonia.Platform.Linux.X11ResizeWatcher, whose own remarks this mirrors: pure Xlib,
    /// no toolkit dependency, so a straight copy into this project's own namespace matches this
    /// codebase's established "small local interop file per platform head" convention (see that
    /// file's own reference to X11DragIconWindow.cs for the same precedent) rather than sharing one
    /// assembly across heads. GTK4 has no more of a native resize-begin/end signal on Linux than
    /// Avalonia's own X11 backend does (both report nothing beyond raw ConfigureNotify), so the
    /// exact same technique applies unchanged: any client that knows a window's XID can open its
    /// own independent Xlib connection and select StructureNotifyMask on it, receiving
    /// ConfigureNotify completely independently of GTK's own connection - and XQueryPointer reports
    /// the true global button state regardless of which window/WM currently holds the resize grab.
    /// </summary>
    internal sealed class X11ResizeWatcher : IDisposable
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibC = "libc.so.6";

        private const int ConfigureNotify = 22;
        private const long StructureNotifyMask = 1L << 17;
        private const int XEventSize = 192; // raw XEvent union size - same buffer every X11 head in this project allocates for XNextEvent.

        private const uint AnyButtonMask = 0x1F00; // Button1Mask|Button2Mask|Button3Mask|Button4Mask|Button5Mask

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int fd;
            public short events;
            public short revents;
        }

        private const short PollIn = 0x0001;

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);
        [DllImport(LibX11)] private static extern int XPending(IntPtr display);
        [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
        [DllImport(LibX11)] private static extern int XConnectionNumber(IntPtr display);
        [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(LibX11)]
        private static extern int XQueryPointer(
            IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr childReturn,
            out int rootXReturn, out int rootYReturn, out int winXReturn, out int winYReturn,
            out uint maskReturn);

        [DllImport(LibC)] private static extern int poll([In, Out] PollFd[] fds, IntPtr nfds, int timeout);

        private const int IdlePollMs = 200;
        private const int ActivePollMs = 30;

        private readonly IntPtr _windowId;
        private readonly TimeSpan _settleDelay;

        // Both invoked from THIS class' own watcher thread, never the GTK main loop - callers are
        // responsible for marshaling (mirrors every other cross-thread entry point in this
        // project, e.g. BitmapControl.OnFrameReady's _uiContext.Post).
        private readonly Action _onResizeBegin;
        private readonly Action _onResizeSettled;

        private IntPtr _display;
        private IntPtr _rootWindow;
        private Thread _thread;
        private volatile bool _running;

        public X11ResizeWatcher(IntPtr windowId, TimeSpan settleDelay, Action onResizeBegin, Action onResizeSettled)
        {
            _windowId = windowId;
            _settleDelay = settleDelay;
            _onResizeBegin = onResizeBegin;
            _onResizeSettled = onResizeSettled;
        }

        /// <summary>Opens its own Display connection and starts the watcher thread. False if the
        /// connection couldn't be opened - caller should fall back to the tick-callback settle
        /// debounce (BaseCefControl.ReportAllocatedSize's existing default path).</summary>
        public bool Start()
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                return false;
            }

            _rootWindow = XDefaultRootWindow(_display);
            XSelectInput(_display, _windowId, StructureNotifyMask);

            _running = true;
            _thread = new Thread(RunLoop) { IsBackground = true, Name = "CefGlue X11 resize watcher" };
            _thread.Start();
            return true;
        }

        private void RunLoop()
        {
            var eventBuffer = Marshal.AllocHGlobal(XEventSize);
            try
            {
                var fd = XConnectionNumber(_display);
                var pollFds = new PollFd[1] { new PollFd { fd = fd, events = PollIn } };

                var isResizing = false;
                var lastConfigureNotify = Stopwatch.StartNew();

                while (_running)
                {
                    while (_running && XPending(_display) > 0)
                    {
                        XNextEvent(_display, eventBuffer);
                        if (Marshal.ReadInt32(eventBuffer) == ConfigureNotify)
                        {
                            if (!isResizing)
                            {
                                isResizing = true;
                                _onResizeBegin?.Invoke();
                            }

                            lastConfigureNotify.Restart();
                        }
                    }

                    if (!_running)
                    {
                        break;
                    }

                    if (isResizing && lastConfigureNotify.Elapsed >= _settleDelay && !IsAnyButtonHeld())
                    {
                        isResizing = false;
                        _onResizeSettled?.Invoke();
                    }

                    poll(pollFds, (IntPtr)1, isResizing ? ActivePollMs : IdlePollMs);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(eventBuffer);
            }
        }

        private bool IsAnyButtonHeld()
        {
            XQueryPointer(_display, _rootWindow, out _, out _, out _, out _, out _, out _, out var mask);
            return (mask & AnyButtonMask) != 0;
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(500);

            if (_display != IntPtr.Zero)
            {
                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
    }
}
