using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal static class NativeInterop
    {
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        public static uint DoubleClickTimeMs => GetDoubleClickTime();

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public static (int X, int Y) GetClientAreaScreenOrigin(IntPtr hwnd)
        {
            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);
            return (origin.X, origin.Y);
        }

        [ComImport]
        [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISwapChainPanelNative
        {
            void SetSwapChain(IntPtr swapChain);
        }

        public static void SetSwapChainOnPanel(SwapChainPanel panel, IntPtr swapChain)
        {
            var native = panel.As<ISwapChainPanelNative>();
            native.SetSwapChain(swapChain);
        }
    }
}
