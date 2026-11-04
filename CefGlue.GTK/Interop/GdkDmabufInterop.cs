using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// Raw P/Invoke for the two native functions GirCore 0.8.1 doesn't generate a binding for:
    /// gdk_dmabuf_texture_builder_build (Gdk.DmabufTextureBuilder exposes every setter but no way
    /// to actually build the resulting texture - confirmed via reflection against
    /// Gdk-4.0.dll/0.8.1, both the public wrapper and its internal P/Invoke layer are missing it,
    /// unlike Gdk.GLTextureBuilder which at least has it internally) and gdk_texture_download
    /// (exists as a Gdk.Internal.Texture P/Invoke but likewise isn't exposed on the public
    /// Gdk.Texture wrapper). Declared directly against the real shared library rather than
    /// GirCore's own "Gdk"-named DllImport (that logical name only resolves via a
    /// NativeLibrary.SetDllImportResolver GirCore registers for ITS OWN assembly - it would not
    /// resolve from a DllImport declared in this assembly).
    /// </summary>
    internal static class GdkDmabufInterop
    {
        private const string GtkLibrary = "libgtk-4.so.1";
        private const string GLibLibrary = "libglib-2.0.so.0";

        [DllImport(GtkLibrary, EntryPoint = "gdk_dmabuf_texture_builder_build")]
        public static extern IntPtr DmabufTextureBuilderBuild(IntPtr self, IntPtr destroy, IntPtr data, out IntPtr error);

        [DllImport(GtkLibrary, EntryPoint = "gdk_texture_download")]
        public static extern void TextureDownload(IntPtr texture, IntPtr data, UIntPtr stride);

        [DllImport(GLibLibrary, EntryPoint = "g_error_free")]
        public static extern void ErrorFree(IntPtr error);
    }
}
