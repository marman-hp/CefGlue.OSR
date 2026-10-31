using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Xilium.CefGlue.WPF
{
    internal static class InputExtensions
    {
        public static CefMouseEvent AsCefMouseEvent(this MouseEventArgs eventArgs, IInputElement mouseCoordinatesReferencial, Point offset = default)
        {
            var cursorPos = eventArgs.GetPosition(mouseCoordinatesReferencial);
            var modifiers = CefEventFlags.None;

            if (eventArgs.LeftButton == MouseButtonState.Pressed)
            {
                modifiers |= CefEventFlags.LeftMouseButton;
            }
            if (eventArgs.RightButton == MouseButtonState.Pressed)
            {
                modifiers |= CefEventFlags.RightMouseButton;
            }
            if (eventArgs.MiddleButton == MouseButtonState.Pressed)
            {
                modifiers |= CefEventFlags.MiddleMouseButton;
            }

            return new CefMouseEvent((int) (cursorPos.X + offset.X), (int) (cursorPos.Y + offset.Y), modifiers | Keyboard.Modifiers.AsCefKeyboardModifiers());
        }

        public static CefMouseButtonType AsCefMouseButtonType(this MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Middle:
                    return CefMouseButtonType.Middle;
                case MouseButton.Right:
                    return CefMouseButtonType.Right;
                default:
                    return CefMouseButtonType.Left;
            }
        }

        public static CefKeyEvent AsCefKeyEvent(this KeyEventArgs eventArgs, bool isKeyUp)
        {
            var modifiers = eventArgs.KeyboardDevice.Modifiers.AsCefKeyboardModifiers();
            return new CefKeyEvent()
            {
                EventType = isKeyUp ? CefKeyEventType.KeyUp : CefKeyEventType.RawKeyDown,
                WindowsKeyCode = KeyInterop.VirtualKeyFromKey(eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key),
                NativeKeyCode = 0,
                IsSystemKey = eventArgs.Key == Key.System,
                Modifiers = modifiers
            };
        }

        public static CefEventFlags AsCefKeyboardModifiers(this ModifierKeys keyboardModifiers)
        {
            CefEventFlags modifiers = new CefEventFlags();

            if (keyboardModifiers.HasFlag(ModifierKeys.Alt))
                modifiers |= CefEventFlags.AltDown;

            if (keyboardModifiers.HasFlag(ModifierKeys.Control))
                modifiers |= CefEventFlags.ControlDown;

            if (keyboardModifiers.HasFlag(ModifierKeys.Shift))
                modifiers |= CefEventFlags.ShiftDown;

            return modifiers;
        }

        public static CefMouseEvent AsCefMouseEvent(this DragEventArgs eventArgs, IInputElement mouseCoordinatesReferencial, Point offset = default)
        {
            var cursorPos = eventArgs.GetPosition(mouseCoordinatesReferencial);

            return new CefMouseEvent((int)(cursorPos.X + offset.X), (int)(cursorPos.Y + offset.Y), Keyboard.Modifiers.AsCefKeyboardModifiers());
        }

        public static CefDragOperationsMask AsCefDragOperationsMask(this DragDropEffects dragDropEffects)
        {
            var operations = CefDragOperationsMask.None;

            if (dragDropEffects.HasFlag(DragDropEffects.All))
            {
                operations |= CefDragOperationsMask.Every;
            }
            if (dragDropEffects.HasFlag(DragDropEffects.Copy))
            {
                operations |= CefDragOperationsMask.Copy;
            }
            if (dragDropEffects.HasFlag(DragDropEffects.Move))
            {
                operations |= CefDragOperationsMask.Move;
            }
            if (dragDropEffects.HasFlag(DragDropEffects.Link))
            {
                operations |= CefDragOperationsMask.Link;
            }

            return operations;
        }

        public static DragDropEffects AsDragDropEffects(this CefDragOperationsMask ops)
        {
            if (ops == CefDragOperationsMask.None) return DragDropEffects.None;

            DragDropEffects result = DragDropEffects.None;
            if ((ops & CefDragOperationsMask.Copy) != 0) result |= DragDropEffects.Copy;
            if ((ops & CefDragOperationsMask.Move) != 0) result |= DragDropEffects.Move;
            if ((ops & CefDragOperationsMask.Link) != 0) result |= DragDropEffects.Link;
            return result;
        }

        public static CefDragData GetDragData(this DragEventArgs e)
        {
            var dragData = CefDragData.Create();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var filePath in files)
                {
                    var displayName = Path.GetFileName(filePath);
                    dragData.AddFile(filePath.Replace("\\", "/"), displayName);
                }
            }

            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var textData = (string)e.Data.GetData(DataFormats.Text);
                dragData.SetFragmentText(textData);

                var htmlData = (string)e.Data.GetData(DataFormats.Html);
                if (!string.IsNullOrEmpty(htmlData))
                {
                    dragData.SetFragmentHtml(htmlData);
                }

                if (!string.IsNullOrEmpty(textData) && Uri.IsWellFormedUriString(textData, UriKind.Absolute))
                {
                    dragData.SetFragmentBaseURL(textData);
                    dragData.SetLinkURL(textData);
                    dragData.SetLinkTitle(textData);
                }
            }

            return dragData;
        }
    }
}
