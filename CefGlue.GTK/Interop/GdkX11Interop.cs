using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// Raw P/Invoke for gdk_x11_surface_get_xid - GirCore has no typed GdkX11 binding referenced
    /// in this project (would need the separate GirCore.GdkX11-4.0 package), and this project's
    /// own established convention for a single missing/unbound native call is a small direct
    /// DllImport against the real shared library rather than pulling in a whole extra package -
    /// see GdkDmabufInterop's identical rationale for gdk_dmabuf_texture_builder_build/
    /// gdk_texture_download. Needed to resolve a Gdk.Surface's raw X11 window ID for
    /// X11ResizeWatcher (which is plain Xlib and knows nothing about GTK/GObject types).
    /// </summary>
    internal static class GdkX11Interop
    {
        private const string GtkLibrary = "libgtk-4.so.1";

        // Window gdk_x11_surface_get_xid(GdkSurface* surface); - Window/XID is an unsigned long,
        // same width as IntPtr on every platform this project targets (64-bit Linux).
        [DllImport(GtkLibrary, EntryPoint = "gdk_x11_surface_get_xid")]
        private static extern IntPtr SurfaceGetXidUnchecked(IntPtr surface);

        [DllImport("libgobject-2.0.so.0")]
        private static extern IntPtr g_type_name_from_instance(IntPtr instance);

        /// <summary>
        /// The surface's XID, or zero when it isn't a GdkX11Surface. gdk_x11_surface_get_xid doesn't check
        /// the type itself: on a Wayland surface it returns whatever sits at the xid field's offset, and
        /// XSelectInput on that garbage id kills the process with a fatal BadWindow.
        /// </summary>
        public static IntPtr SurfaceGetXid(IntPtr surface)
        {
            if (surface == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var typeName = Marshal.PtrToStringAnsi(g_type_name_from_instance(surface));
            return typeName != null && typeName.StartsWith("GdkX11", StringComparison.Ordinal)
                ? SurfaceGetXidUnchecked(surface)
                : IntPtr.Zero;
        }
    }
}
