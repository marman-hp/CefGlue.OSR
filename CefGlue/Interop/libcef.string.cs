namespace Xilium.CefGlue.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;

    internal static unsafe partial class libcef
    {

        [DllImport(DllName, EntryPoint = "cef_string_utf16_set", CallingConvention = CEF_CALL)]
        public static extern int string_set(char* src, UIntPtr src_len, cef_string_t* output, int copy);

        [DllImport(DllName, EntryPoint = "cef_string_utf16_clear", CallingConvention = CEF_CALL)]
        public static extern void string_clear(cef_string_t* str);

        [DllImport(DllName, EntryPoint = "cef_string_userfree_utf16_alloc", CallingConvention = CEF_CALL)]
        public static extern cef_string_userfree* string_userfree_alloc();

        [DllImport(DllName, EntryPoint = "cef_string_userfree_utf16_free", CallingConvention = CEF_CALL)]
        public static extern void string_userfree_free(cef_string_userfree* str);
    }
}
