using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.WPF.Platform
{
    internal static class NativeMethods
    {
        public const int WM_IME_COMPOSITION = 0x010F;

        public const int GCS_COMPSTR = 0x0008;
        public const int GCS_CURSORPOS = 0x0080;
        public const int GCS_RESULTSTR = 0x0800;

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        public static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);
    }
}
