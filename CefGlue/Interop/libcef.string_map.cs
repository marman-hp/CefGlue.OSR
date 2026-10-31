namespace Xilium.CefGlue.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;

    internal static unsafe partial class libcef
    {
        [DllImport(DllName, EntryPoint = "cef_string_map_alloc", CallingConvention = CEF_CALL)]
        public static extern cef_string_map* string_map_alloc();

        [DllImport(DllName, EntryPoint = "cef_string_map_size", CallingConvention = CEF_CALL)]
        private static extern UIntPtr string_map_size_core(cef_string_map* map);

        public static int string_map_size(cef_string_map* map)
        {
            return checked((int)string_map_size_core(map));
        }

        [DllImport(DllName, EntryPoint = "cef_string_map_find", CallingConvention = CEF_CALL)]
        public static extern int string_map_find(cef_string_map* map, cef_string_t* key, cef_string_t* value);

        [DllImport(DllName, EntryPoint = "cef_string_map_key", CallingConvention = CEF_CALL)]
        private static extern int string_map_key_core(cef_string_map* map, UIntPtr index, cef_string_t* key);

        public static int string_map_key(cef_string_map* map, int index, cef_string_t* key)
        {
            return string_map_key_core(map, checked((UIntPtr)index), key);
        }

        [DllImport(DllName, EntryPoint = "cef_string_map_value", CallingConvention = CEF_CALL)]
        private static extern int string_map_value_core(cef_string_map* map, UIntPtr index, cef_string_t* value);

        public static int string_map_value(cef_string_map* map, int index, cef_string_t* value)
        {
            return string_map_value_core(map, checked((UIntPtr)index), value);
        }

        [DllImport(DllName, EntryPoint = "cef_string_map_append", CallingConvention = CEF_CALL)]
        public static extern int string_map_append(cef_string_map* map, cef_string_t* key, cef_string_t* value);

        [DllImport(DllName, EntryPoint = "cef_string_map_clear", CallingConvention = CEF_CALL)]
        public static extern void string_map_clear(cef_string_map* map);

        [DllImport(DllName, EntryPoint = "cef_string_map_free", CallingConvention = CEF_CALL)]
        public static extern void string_map_free(cef_string_map* map);
    }
}
