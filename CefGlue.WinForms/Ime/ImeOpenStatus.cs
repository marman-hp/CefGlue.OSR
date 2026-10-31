using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.WinForms.Ime
{
    internal static class ImeOpenStatus
    {
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmGetOpenStatus(IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool open);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        public static void EnsureOpen(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var primaryLanguage = (int)(GetKeyboardLayout(0).ToInt64() & 0x3FF);
            if (primaryLanguage != 0x11   && primaryLanguage != 0x04   && primaryLanguage != 0x12  )
            {
                return;
            }

            var himc = ImmGetContext(hwnd);
            if (himc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (!ImmGetOpenStatus(himc))
                {
                    ImmSetOpenStatus(himc, true);
                }
            }
            finally
            {
                ImmReleaseContext(hwnd, himc);
            }
        }

        public static IntPtr FocusedDescendantOf(IntPtr top)
        {
            var focus = GetFocus();
            return focus != IntPtr.Zero && focus != top && IsChild(top, focus) ? focus : IntPtr.Zero;
        }

    }
}
