using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;

namespace Xilium.CefGlue.Avalonia.Platform.Windows
{
    internal enum WindowMessage
    {
        EnterSizeMove,
        ExitSizeMove,
    }

    internal static class WindowSubclassHelper
    {
        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;

        [DllImport("Comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("Comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        private sealed class Hook
        {
            public Action<WindowMessage> Callback;
            public SUBCLASSPROC Proc;
            public IntPtr Hwnd;
            public UIntPtr Id;
        }

        private static readonly ConditionalWeakTable<Control, Hook> _hooks = new();
        private static long _nextId;

        public static bool TryHook(IntPtr hwnd, Control owner, Action<WindowMessage> callback)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var id = new UIntPtr((ulong)Interlocked.Increment(ref _nextId));
            var hook = new Hook { Callback = callback, Hwnd = hwnd, Id = id };
            hook.Proc = (h, msg, w, l, uid, refData) =>
            {
                if (msg == WM_ENTERSIZEMOVE)
                {
                    hook.Callback(WindowMessage.EnterSizeMove);
                }
                else if (msg == WM_EXITSIZEMOVE)
                {
                    hook.Callback(WindowMessage.ExitSizeMove);
                }

                return DefSubclassProc(h, msg, w, l);
            };

            if (!SetWindowSubclass(hwnd, hook.Proc, id, IntPtr.Zero))
            {
                return false;
            }

            _hooks.AddOrUpdate(owner, hook);
            return true;
        }

        public static void Unhook(Control owner)
        {
            if (_hooks.TryGetValue(owner, out var hook))
            {
                RemoveWindowSubclass(hook.Hwnd, hook.Proc, hook.Id);
                _hooks.Remove(owner);
            }
        }
    }
}
