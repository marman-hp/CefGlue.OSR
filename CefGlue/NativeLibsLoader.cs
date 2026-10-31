using System;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Xilium.CefGlue;

namespace CefGlue
{
    internal static class NativeLibsLoader
    {
        public static void Install()
        {
#if NET5_0_OR_GREATER
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, libName) =>
            {
                if (CefRuntimeLocator.FindLibrary(libName) is { } libPath)
                {
                    return NativeLibrary.Load(libPath);
                }

                return IntPtr.Zero;
            };
#endif
        }
    }
}
