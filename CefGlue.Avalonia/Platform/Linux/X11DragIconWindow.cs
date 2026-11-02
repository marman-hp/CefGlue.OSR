using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.Avalonia.Platform.Linux
{
    /// <summary>
    /// Outbound drag thumbnail on X11: an override-redirect window showing CEF's drag image
    /// (CefDragData.GetImage), moved with the pointer, the way GTK/Qt/Chrome draw their XDND drag icons.
    ///
    /// The window gets an empty input shape, so the X server never resolves the pointer to it: Avalonia's
    /// XDND source (XQueryPointer/XTranslateCoordinates) keeps finding the real target underneath, and the
    /// window under the cursor never produces Enter/Leave. Without that the icon itself became the drop
    /// target and drops failed intermittently.
    ///
    /// Uses its own Display connection. Xlib connections are not thread-safe, so every call must come
    /// from one thread (the UI thread).
    /// </summary>
    internal sealed class X11DragIconWindow
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXfixes = "libXfixes.so.3";

        private const int InputOutput = 1;
        private const int ZPixmap = 2;
        private const int TrueColorClass = 4;
        private const int AllocNone = 0;
        private const int ShapeInput = 2;

        private const long CWBackPixel = 1L << 1;
        private const long CWBorderPixel = 1L << 3;
        private const long CWOverrideRedirect = 1L << 9;
        private const long CWColormap = 1L << 13;

        [StructLayout(LayoutKind.Sequential)]
        private struct XVisualInfo
        {
            public IntPtr visual;
            public IntPtr visualid;
            public int screen;
            public int depth;
            public int c_class;
            public IntPtr red_mask;
            public IntPtr green_mask;
            public IntPtr blue_mask;
            public int colormap_size;
            public int bits_per_rgb;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XSetWindowAttributes
        {
            public IntPtr background_pixmap;
            public IntPtr background_pixel;
            public IntPtr border_pixmap;
            public IntPtr border_pixel;
            public int bit_gravity;
            public int win_gravity;
            public int backing_store;
            public IntPtr backing_planes;
            public IntPtr backing_pixel;
            public int save_under;
            public IntPtr event_mask;
            public IntPtr do_not_propagate_mask;
            public int override_redirect;
            public IntPtr colormap;
            public IntPtr cursor;
        }

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr displayName);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XDefaultScreen(IntPtr display);
        [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] private static extern IntPtr XDefaultVisual(IntPtr display, int screenNumber);
        [DllImport(LibX11)] private static extern int XDefaultDepth(IntPtr display, int screenNumber);
        [DllImport(LibX11)] private static extern int XMatchVisualInfo(IntPtr display, int screen, int depth, int visualClass, out XVisualInfo visualInfo);
        [DllImport(LibX11)] private static extern IntPtr XCreateColormap(IntPtr display, IntPtr window, IntPtr visual, int alloc);
        [DllImport(LibX11)] private static extern int XFreeColormap(IntPtr display, IntPtr colormap);

        [DllImport(LibX11)]
        private static extern IntPtr XCreateWindow(
            IntPtr display, IntPtr parent, int x, int y, uint width, uint height,
            uint borderWidth, int depth, uint windowClass, IntPtr visual,
            UIntPtr valueMask, ref XSetWindowAttributes attributes);

        [DllImport(LibX11)] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
        [DllImport(LibX11)] private static extern int XMapRaised(IntPtr display, IntPtr window);
        [DllImport(LibX11)] private static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);
        [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
        [DllImport(LibX11)] private static extern IntPtr XCreateGC(IntPtr display, IntPtr drawable, UIntPtr valueMask, IntPtr values);
        [DllImport(LibX11)] private static extern int XFreeGC(IntPtr display, IntPtr gc);

        [DllImport(LibX11)]
        private static extern IntPtr XCreateImage(
            IntPtr display, IntPtr visual, uint depth, int format, int offset, IntPtr data,
            uint width, uint height, int bitmapPad, int bytesPerLine);

        [DllImport(LibX11)]
        private static extern int XPutImage(
            IntPtr display, IntPtr drawable, IntPtr gc, IntPtr image,
            int srcX, int srcY, int destX, int destY, uint width, uint height);

        [DllImport(LibX11)] private static extern int XFree(IntPtr data);

        [DllImport(LibX11)]
        private static extern int XQueryPointer(
            IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
            out int rootX, out int rootY, out int winX, out int winY, out uint mask);

        [DllImport(LibXfixes)] private static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int count);
        [DllImport(LibXfixes)] private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int shapeKind, int xOffset, int yOffset, IntPtr region);
        [DllImport(LibXfixes)] private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);

        private IntPtr _display;
        private IntPtr _rootWindow;
        private IntPtr _window;
        private IntPtr _colormap;
        private int _hotspotX;
        private int _hotspotY;

        /// <summary>
        /// True when an ARGB visual is used; the pixels passed to <see cref="Show"/> must then be premultiplied.
        /// </summary>
        public bool HasAlpha { get; private set; }

        /// <summary>Opens the display connection. Returns false when X11 is not reachable.</summary>
        public bool Open()
        {
            try
            {
                _display = XOpenDisplay(IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                return false;
            }

            if (_display == IntPtr.Zero)
            {
                return false;
            }

            _rootWindow = XDefaultRootWindow(_display);
            HasAlpha = XMatchVisualInfo(_display, XDefaultScreen(_display), 32, TrueColorClass, out _) != 0;
            return true;
        }

        public void Show(byte[] bgraPixels, int width, int height, int hotspotX, int hotspotY)
        {
            _hotspotX = hotspotX;
            _hotspotY = hotspotY;

            var screen = XDefaultScreen(_display);
            IntPtr visual;
            int depth;
            long valueMask = CWOverrideRedirect;
            var attributes = new XSetWindowAttributes { override_redirect = 1 };

            if (HasAlpha && XMatchVisualInfo(_display, screen, 32, TrueColorClass, out var argb) != 0)
            {
                // A visual that differs from the root's needs its own colormap and border pixel.
                visual = argb.visual;
                depth = 32;
                _colormap = XCreateColormap(_display, _rootWindow, visual, AllocNone);
                attributes.colormap = _colormap;
                valueMask |= CWColormap | CWBorderPixel | CWBackPixel;
            }
            else
            {
                visual = XDefaultVisual(_display, screen);
                depth = XDefaultDepth(_display, screen);
            }

            TryPollPointer(out var rootX, out var rootY);
            _window = XCreateWindow(
                _display, _rootWindow, rootX - hotspotX, rootY - hotspotY, (uint)width, (uint)height,
                0, depth, InputOutput, visual, (UIntPtr)valueMask, ref attributes);

            try
            {
                var emptyRegion = XFixesCreateRegion(_display, IntPtr.Zero, 0);
                XFixesSetWindowShapeRegion(_display, _window, ShapeInput, 0, 0, emptyRegion);
                XFixesDestroyRegion(_display, emptyRegion);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Without an empty input shape the icon would steal the drop target; don't show it.
                Close();
                return;
            }

            XMapRaised(_display, _window);
            Blit(bgraPixels, width, height, visual, depth);
        }

        private unsafe void Blit(byte[] bgraPixels, int width, int height, IntPtr visual, int depth)
        {
            var gc = XCreateGC(_display, _window, UIntPtr.Zero, IntPtr.Zero);
            fixed (byte* data = bgraPixels)
            {
                // XFree (not XDestroyImage): the pixel buffer is managed memory.
                var image = XCreateImage(_display, visual, (uint)depth, ZPixmap, 0, (IntPtr)data, (uint)width, (uint)height, 32, width * 4);
                if (image != IntPtr.Zero)
                {
                    XPutImage(_display, _window, gc, image, 0, 0, 0, 0, (uint)width, (uint)height);
                    XFree(image);
                }
            }
            XFreeGC(_display, gc);
            XFlush(_display);
        }

        /// <summary>Moves the icon to the pointer. The XDND grab keeps pointer events away from Avalonia, so it is polled.</summary>
        public void FollowPointer()
        {
            if (_window != IntPtr.Zero && TryPollPointer(out var rootX, out var rootY))
            {
                XMoveWindow(_display, _window, rootX - _hotspotX, rootY - _hotspotY);
                XFlush(_display);
            }
        }

        private bool TryPollPointer(out int rootX, out int rootY)
        {
            return XQueryPointer(_display, _rootWindow, out _, out _, out rootX, out rootY, out _, out _, out _) != 0;
        }

        public void Close()
        {
            if (_display == IntPtr.Zero)
            {
                return;
            }

            if (_window != IntPtr.Zero)
            {
                XDestroyWindow(_display, _window);
                _window = IntPtr.Zero;
            }

            if (_colormap != IntPtr.Zero)
            {
                XFreeColormap(_display, _colormap);
                _colormap = IntPtr.Zero;
            }

            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }
}
