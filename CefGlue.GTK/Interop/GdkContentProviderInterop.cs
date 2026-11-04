using System.Runtime.InteropServices;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// Raw P/Invoke for gdk_content_provider_new_union - present internally in GirCore 0.8.1
    /// (Gdk.Internal.ContentProvider.NewUnion) but not exposed on the public Gdk.ContentProvider
    /// wrapper, and that internal type isn't accessible from this assembly anyway. Declared
    /// directly against the real shared library, same rationale/convention as GdkDmabufInterop and
    /// GdkX11Interop's identical situations. Needed to offer multiple content types (plain text AND
    /// text/uri-list) on a single outbound drag - see OffScreenControlHost.StartDrag's own remarks
    /// for why text/uri-list specifically has to be added this way (Gdk.ContentProvider.NewForValue
    /// alone only ever advertises text/plain-family formats).
    /// </summary>
    internal static class GdkContentProviderInterop
    {
        private const string GtkLibrary = "libgtk-4.so.1";

        [DllImport(GtkLibrary, EntryPoint = "gdk_content_provider_new_union")]
        public static extern System.IntPtr NewUnion(System.IntPtr[] providers, System.UIntPtr nProviders);
    }
}
