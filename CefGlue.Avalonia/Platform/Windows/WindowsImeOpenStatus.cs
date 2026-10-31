using System;
using System.Runtime.InteropServices;

namespace Xilium.CefGlue.Avalonia.Platform.Windows
{
    internal static class WindowsImeOpenStatus
    {
        private const int LangJapanese = 0x11;
        private const int LangChinese = 0x04;
        private const int LangKorean = 0x12;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool open);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        public static void EnsureOpen(IntPtr hwnd)
        {
            if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            {
                return;
            }

            var primaryLanguage = (int)(GetKeyboardLayout(0).ToInt64() & 0x3FF);
            if (primaryLanguage != LangJapanese && primaryLanguage != LangChinese && primaryLanguage != LangKorean)
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
    }
}
