using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// Raw EGL P/Invoke for importing a CEF dma-buf as an EGLImage and resolving GL function
    /// pointers - GirCore has no EGL binding at all (it's a windowing-system-integration API, not
    /// part of any .gir-described GObject library), so every call here goes straight against the
    /// real shared library, same convention as GdkDmabufInterop's own remarks for why.
    ///
    /// eglCreateImageKHR with EGL_LINUX_DMA_BUF_EXT is called with EGL_NO_CONTEXT (this target
    /// imports a memory object, not a live GL texture/renderbuffer from a source context, so no
    /// source context is needed - only a display) - matches every reference use of this extension
    /// (Weston, GStreamer's gst-plugins-bad EGL sink, wlroots).
    /// </summary>
    internal static class EglInterop
    {
        private const string EglLibrary = "libEGL.so.1";

        public const int EGL_NONE = 0x3038;
        public const int EGL_WIDTH = 0x3057;
        public const int EGL_HEIGHT = 0x3056;
        public const int EGL_LINUX_DMA_BUF_EXT = 0x3270;
        public const int EGL_LINUX_DRM_FOURCC_EXT = 0x3271;
        public const int EGL_DMA_BUF_PLANE0_FD_EXT = 0x3272;
        public const int EGL_DMA_BUF_PLANE0_OFFSET_EXT = 0x3273;
        public const int EGL_DMA_BUF_PLANE0_PITCH_EXT = 0x3274;
        public const int EGL_DMA_BUF_PLANE1_FD_EXT = 0x3275;
        public const int EGL_DMA_BUF_PLANE1_OFFSET_EXT = 0x3276;
        public const int EGL_DMA_BUF_PLANE1_PITCH_EXT = 0x3277;
        public const int EGL_DMA_BUF_PLANE2_FD_EXT = 0x3278;
        public const int EGL_DMA_BUF_PLANE2_OFFSET_EXT = 0x3279;
        public const int EGL_DMA_BUF_PLANE2_PITCH_EXT = 0x327A;
        public const int EGL_DMA_BUF_PLANE3_FD_EXT = 0x3440;
        public const int EGL_DMA_BUF_PLANE3_OFFSET_EXT = 0x3441;
        public const int EGL_DMA_BUF_PLANE3_PITCH_EXT = 0x3442;
        public const int EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT = 0x3443;
        public const int EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT = 0x3444;
        public const int EGL_DMA_BUF_PLANE1_MODIFIER_LO_EXT = 0x3445;
        public const int EGL_DMA_BUF_PLANE1_MODIFIER_HI_EXT = 0x3446;
        public const int EGL_DMA_BUF_PLANE2_MODIFIER_LO_EXT = 0x3447;
        public const int EGL_DMA_BUF_PLANE2_MODIFIER_HI_EXT = 0x3448;
        public const int EGL_DMA_BUF_PLANE3_MODIFIER_LO_EXT = 0x3449;
        public const int EGL_DMA_BUF_PLANE3_MODIFIER_HI_EXT = 0x344A;
        public const int EGL_IMAGE_PRESERVED_KHR = 0x30D2;
        public const int EGL_TRUE = 1;

        public static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;
        public static readonly IntPtr EGL_NO_IMAGE_KHR = IntPtr.Zero;

        [DllImport(EglLibrary)]
        public static extern IntPtr eglGetCurrentDisplay();

        [DllImport(EglLibrary)]
        public static extern IntPtr eglGetCurrentContext();

        [DllImport(EglLibrary)]
        public static extern IntPtr eglGetProcAddress(string procname);

        // EGL_KHR_image_base entry points: extensions, so libglvnd's libEGL.so.1 doesn't export them -
        // they have to come from eglGetProcAddress.
        public delegate IntPtr CreateImageKHRDelegate(IntPtr dpy, IntPtr ctx, int target, IntPtr buffer, int[] attribList);
        public delegate uint DestroyImageKHRDelegate(IntPtr dpy, IntPtr image);

        private static CreateImageKHRDelegate _createImage;
        private static DestroyImageKHRDelegate _destroyImage;

        public static IntPtr eglCreateImageKHR(IntPtr dpy, IntPtr ctx, int target, IntPtr buffer, int[] attribList)
            => (_createImage ??= Load<CreateImageKHRDelegate>("eglCreateImageKHR"))(dpy, ctx, target, buffer, attribList);

        public static bool eglDestroyImageKHR(IntPtr dpy, IntPtr image)
            => (_destroyImage ??= Load<DestroyImageKHRDelegate>("eglDestroyImageKHR"))(dpy, image) != 0;

        private static T Load<T>(string name) where T : Delegate
        {
            var ptr = eglGetProcAddress(name);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"eglGetProcAddress(\"{name}\") returned NULL.");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }
    }
}
