namespace Xilium.CefGlue.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;

    internal static unsafe partial class libcef
    {
        [DllImport(DllName, EntryPoint = "cef_basetime_now", CallingConvention = CEF_CALL)]
        public static extern CefBaseTime basetime_now();

        [DllImport(DllName, EntryPoint = "cef_time_to_basetime", CallingConvention = CEF_CALL)]
        public static extern int time_to_basetime(in CefTime from, out CefBaseTime to);

        [DllImport(DllName, EntryPoint = "cef_time_from_basetime", CallingConvention = CEF_CALL)]
        public static extern int time_from_basetime(CefBaseTime from, out CefTime to);

    }
}
