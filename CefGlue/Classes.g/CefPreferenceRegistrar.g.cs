namespace Xilium.CefGlue
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Xilium.CefGlue.Interop;

    public sealed unsafe partial class CefPreferenceRegistrar
    {
        internal static CefPreferenceRegistrar FromNative(cef_preference_registrar_t* ptr)
        {
            return new CefPreferenceRegistrar(ptr);
        }

        internal static CefPreferenceRegistrar FromNativeOrNull(cef_preference_registrar_t* ptr)
        {
            if (ptr == null) return null;
            return new CefPreferenceRegistrar(ptr);
        }

        private cef_preference_registrar_t* _self;
        private int _disposed = 0;

        private CefPreferenceRegistrar(cef_preference_registrar_t* ptr)
        {
            if (ptr == null) throw new ArgumentNullException("ptr");
            _self = ptr;
        }

        internal cef_preference_registrar_t* ToNative()
        {
            return _self;
        }
    }
}
