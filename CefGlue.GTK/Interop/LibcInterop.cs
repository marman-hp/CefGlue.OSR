using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// Raw P/Invoke against libc for mmap/munmap - lets GtkRenderSurface read a linear (untiled)
    /// dma-buf plane directly, bypassing GDK/GSK texture APIs entirely. See
    /// GtkRenderSurface.ReadViaMmap: gdk_texture_download() on an imported dma-buf texture was
    /// confirmed live (2026-08-05, native Linux) to hang at the libdrm_amdgpu.so.1/kernel level,
    /// right after a "gsk_gl_driver_begin_frame: assertion 'self->in_frame == FALSE' failed"
    /// CRITICAL - i.e. it collides with GSK's own render cycle. mmap never touches GTK/GDK/GSK at
    /// all, so that collision can't happen via this path.
    /// </summary>
    internal static class LibcInterop
    {
        private const string LibC = "libc.so.6";

        public const int ProtRead = 0x1;
        public const int MapShared = 0x01;
        public static readonly IntPtr MapFailed = new IntPtr(-1);

        // void *mmap(void *addr, size_t length, int prot, int flags, int fd, off_t offset);
        [DllImport(LibC, EntryPoint = "mmap", SetLastError = true)]
        public static extern IntPtr Mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

        // int munmap(void *addr, size_t length);
        [DllImport(LibC, EntryPoint = "munmap", SetLastError = true)]
        public static extern int Munmap(IntPtr addr, IntPtr length);

        [DllImport(LibC, EntryPoint = "dup", SetLastError = true)]
        public static extern int Dup(int fd);

        [DllImport(LibC, EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);
    }
}
