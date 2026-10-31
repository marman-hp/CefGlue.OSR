namespace Xilium.CefGlue.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;

    internal static unsafe partial class libcef
    {
        [DllImport(DllName, EntryPoint = "cef_set_osmodal_loop", CallingConvention = libcef.CEF_CALL)]
        public static extern void set_osmodal_loop(int osModalLoop);
    }
}
