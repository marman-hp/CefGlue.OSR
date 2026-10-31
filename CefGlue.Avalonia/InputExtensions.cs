using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Xilium.CefGlue.Avalonia
{
    internal static class InputExtensions
    {
        private static readonly DataFormat<string> UriListFormat = DataFormat.CreateStringPlatformFormat("text/uri-list");

        private static readonly DataFormat<byte[]> HtmlFormat = DataFormat.CreateBytesPlatformFormat("text/html");
        private static readonly DataFormat<string> WindowsHtmlFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

        public static CefMouseEvent AsCefMouseEvent(this PointerEventArgs eventArgs, Visual mousePositionReferential, Point offset = default)
        {
            var cursorPos = mousePositionReferential.IsAttachedToVisualTree() ? eventArgs.GetPosition(mousePositionReferential) : new Point(0, 0);

            return new CefMouseEvent((int)(cursorPos.X + offset.X), (int)(cursorPos.Y + offset.Y), eventArgs.AsCefEventFlags());
        }

        public static CefMouseButtonType AsCefMouseButtonType(this PointerEventArgs eventArgs)
        {
            switch (eventArgs.GetCurrentPoint(null).Properties.PointerUpdateKind.GetMouseButton())
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
            var modifiers = eventArgs.KeyModifiers.AsCefKeyboardModifiers();
            return new CefKeyEvent()
            {
                EventType = isKeyUp ? CefKeyEventType.KeyUp : CefKeyEventType.RawKeyDown,
                WindowsKeyCode = KeyInterop.VirtualKeyFromKey(eventArgs.Key),
                NativeKeyCode = (int)modifiers,
                IsSystemKey = eventArgs.Key == Key.System,
                Modifiers = modifiers
            };
        }

        public static CefEventFlags AsCefEventFlags(this PointerEventArgs eventArgs)
        {
            var flags = CefEventFlags.None;
            var properties = eventArgs.GetCurrentPoint(null).Properties;

            if (properties.IsLeftButtonPressed)
            {
                flags |= CefEventFlags.LeftMouseButton;
            }

            if (properties.IsMiddleButtonPressed)
            {
                flags |= CefEventFlags.MiddleMouseButton;
            }

            if (properties.IsRightButtonPressed)
            {
                flags |= CefEventFlags.RightMouseButton;
            }

            return flags;
        }

        public static CefEventFlags AsCefKeyboardModifiers(this KeyModifiers keyboardModifiers)
        {
            var modifiers = new CefEventFlags();

            if (keyboardModifiers.HasFlag(KeyModifiers.Alt))
            {
                modifiers |= CefEventFlags.AltDown;
            }

            if (keyboardModifiers.HasFlag(KeyModifiers.Control))
            {
                modifiers |= CefEventFlags.ControlDown;
            }

            if (keyboardModifiers.HasFlag(KeyModifiers.Shift))
            {
                modifiers |= CefEventFlags.ShiftDown;
            }

            return modifiers;
        }

        public static CefMouseEvent AsCefMouseEvent(this DragEventArgs eventArgs, Visual mouseCoordinatesReferencial, Point offset = default)
        {
            var cursorPos = eventArgs.GetPosition(mouseCoordinatesReferencial);

            return new CefMouseEvent((int)(cursorPos.X + offset.X), (int)(cursorPos.Y + offset.Y), eventArgs.KeyModifiers.AsCefKeyboardModifiers());
        }

        public static CefDragOperationsMask AsCefDragOperationsMask(this DragDropEffects dragDropEffects)
        {
            var operationsCount = 0;
            var operations = CefDragOperationsMask.None;

            if (dragDropEffects.HasFlag(DragDropEffects.Copy))
            {
                operations |= CefDragOperationsMask.Copy;
                operationsCount++;
            }

            if (dragDropEffects.HasFlag(DragDropEffects.Move))
            {
                operations |= CefDragOperationsMask.Move;
                operationsCount++;
            }

            if (dragDropEffects.HasFlag(DragDropEffects.Link))
            {
                operations |= CefDragOperationsMask.Link;
                operationsCount++;
            }

            if (operationsCount == 3)
            {
                return CefDragOperationsMask.Every;
            }

            return operations;
        }

        public static DragDropEffects AsDragDropEffects(this CefDragOperationsMask mask)
        {
            if (mask.HasFlag(CefDragOperationsMask.Every))
            {
                return DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
            }

            if (mask.HasFlag(CefDragOperationsMask.Copy))
            {
                return DragDropEffects.Copy;
            }

            if (mask.HasFlag(CefDragOperationsMask.Move))
            {
                return DragDropEffects.Move;
            }

            if (mask.HasFlag(CefDragOperationsMask.Link))
            {
                return DragDropEffects.Link;
            }

            return DragDropEffects.None;
        }

        public static CefDragData GetDragData(this DragEventArgs e)
        {
            var dragData = CefDragData.Create();
            var dataTransfer = e.DataTransfer;

            var files = dataTransfer.TryGetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    var filePath = file.Path.LocalPath;
                    var displayName = Path.GetFileName(filePath);
                    dragData.AddFile(filePath.Replace("\\", "/"), displayName);
                }
            }

            var textData = dataTransfer.TryGetText();
            if (!string.IsNullOrEmpty(textData))
            {
                dragData.SetFragmentText(textData);

                if (Uri.IsWellFormedUriString(textData, UriKind.Absolute))
                {
                    dragData.SetFragmentBaseURL(textData);
                    dragData.SetLinkURL(textData);
                    dragData.SetLinkTitle(textData);
                }
            }

            if (dataTransfer.Contains(UriListFormat) && dataTransfer.TryGetValue(UriListFormat) is string uriList && !string.IsNullOrEmpty(uriList))
            {
                var firstUri = FirstUriListEntry(uriList);
                if (!string.IsNullOrEmpty(firstUri) && Uri.IsWellFormedUriString(firstUri, UriKind.Absolute))
                {
                    dragData.SetFragmentBaseURL(firstUri);
                    dragData.SetLinkURL(firstUri);
                    dragData.SetLinkTitle(firstUri);

                    if (string.IsNullOrEmpty(dragData.FragmentText))
                    {
                        dragData.SetFragmentText(firstUri);
                    }
                }
            }

            var htmlBytes = dataTransfer.Contains(HtmlFormat) ? dataTransfer.TryGetValue(HtmlFormat) : null;
            var html = htmlBytes != null ? DecodeHtmlBytes(htmlBytes)
                : dataTransfer.Contains(WindowsHtmlFormat) ? dataTransfer.TryGetValue(WindowsHtmlFormat)
                : null;

            if (!string.IsNullOrEmpty(html))
            {
                dragData.SetFragmentHtml(html);
            }

            return dragData;
        }

        private static string DecodeHtmlBytes(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static string FirstUriListEntry(string uriList)
        {
            foreach (var rawLine in uriList.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length > 0 && line[0] != '#')
                {
                    return line;
                }
            }

            return null;
        }
    }
}
