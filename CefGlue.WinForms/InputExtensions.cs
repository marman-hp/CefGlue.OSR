using System;
using System.Windows.Forms;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.WinForms.Platform;

namespace Xilium.CefGlue.WinForms
{
    internal static class InputExtensions
    {

        public static CefMouseEvent AsCefMouseEvent(this MouseEventArgs e, Control control, bool isMouseUp = false, float scaleFactor = 1.0f)
        {
            var modifiers = CefEventFlags.None;

            if (!isMouseUp)
            {
                switch (e.Button)
                {
                    case MouseButtons.Left:
                        modifiers |= CefEventFlags.LeftMouseButton;
                        break;

                    case MouseButtons.Right:
                        modifiers |= CefEventFlags.RightMouseButton;
                        break;

                    case MouseButtons.Middle:
                        modifiers |= CefEventFlags.MiddleMouseButton;
                        break;
                }
            }

            if ((Control.ModifierKeys & Keys.Control) != 0)
                modifiers |= CefEventFlags.ControlDown;

            if ((Control.ModifierKeys & Keys.Shift) != 0)
                modifiers |= CefEventFlags.ShiftDown;

            if ((Control.ModifierKeys & Keys.Alt) != 0)
                modifiers |= CefEventFlags.AltDown;

            int x = e.X;
            int y = e.Y;

            if (control is BitmapControl bmp && bmp.IsPopupSelect)
            {
                x += control.Left;
                y += control.Top;
            }

            return new CefMouseEvent((int)(x * scaleFactor), (int)(y * scaleFactor), modifiers);
        }
        public static CefMouseButtonType AsCefMouseButtonType( this MouseButtons button)
        {
            switch (button)
            {
                case MouseButtons.Middle:
                    return CefMouseButtonType.Middle;

                case MouseButtons.Right:
                    return CefMouseButtonType.Right;

                default:
                    return CefMouseButtonType.Left;
            }
        }

        public static CefKeyEvent AsCefKeyEvent(this KeyEventArgs e, bool keyUp)
        {
            var modifiers = CefEventFlags.None;

            if (e.Control)
                modifiers |= CefEventFlags.ControlDown;

            if (e.Shift)
                modifiers |= CefEventFlags.ShiftDown;

            if (e.Alt)
                modifiers |= CefEventFlags.AltDown;

            return new CefKeyEvent
            {
                EventType = keyUp ? CefKeyEventType.KeyUp : CefKeyEventType.RawKeyDown,
                WindowsKeyCode = (int)e.KeyCode,
                NativeKeyCode = (int)e.KeyValue,
                Modifiers = modifiers,
                IsSystemKey = e.Alt
            };
        }

        public static CefKeyEvent AsCefKeyEvent(this KeyPressEventArgs e)
        {
            return new CefKeyEvent
            {
                EventType = CefKeyEventType.Char,
                Character = e.KeyChar,
                UnmodifiedCharacter = e.KeyChar,
                WindowsKeyCode = e.KeyChar,
                NativeKeyCode = e.KeyChar
            };
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

        public static CefMouseEvent AsCefMouseEvent(this System.Windows.Forms.DragEventArgs eventArgs, System.Windows.Forms.Control mouseCoordinatesReferencial)
        {
            var screenPos = new System.Drawing.Point(eventArgs.X, eventArgs.Y);
            var localPos = mouseCoordinatesReferencial.PointToClient(screenPos);
            var cefModifiers = System.Windows.Forms.Control.ModifierKeys.AsCefKeyboardModifiers();

            return new CefMouseEvent(localPos.X, localPos.Y, cefModifiers);
        }

        public static CefEventFlags AsCefKeyboardModifiers(this System.Windows.Forms.Keys modifiers)
        {
            CefEventFlags flags = CefEventFlags.None;

            if (modifiers.HasFlag(System.Windows.Forms.Keys.Shift)) flags |= CefEventFlags.ShiftDown;
            if (modifiers.HasFlag(System.Windows.Forms.Keys.Control)) flags |= CefEventFlags.ControlDown;
            if (modifiers.HasFlag(System.Windows.Forms.Keys.Alt)) flags |= CefEventFlags.AltDown;

            return flags;
        }

        public static CefDragData AsCefDragData(this System.Windows.Forms.IDataObject dataObject)
        {
            var dragData = CefDragData.Create();

            if (dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])dataObject.GetData(DataFormats.FileDrop);
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        dragData.AddFile(file, System.IO.Path.GetFileName(file));
                    }
                }
            }

            if (dataObject.GetDataPresent(DataFormats.Html))
            {
                var html = (string)dataObject.GetData(DataFormats.Html);
                dragData.SetFragmentHtml(html);
            }

            string textData = null;

            if (dataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                textData = (string)dataObject.GetData(DataFormats.UnicodeText);
            }
            else if (dataObject.GetDataPresent(DataFormats.Text))
            {
                textData = (string)dataObject.GetData(DataFormats.Text);
            }

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

            var formats = dataObject.GetFormats();
            foreach (var format in formats)
            {
                if (format == DataFormats.FileDrop ||
                    format == DataFormats.Html ||
                    format == DataFormats.Text ||
                    format == DataFormats.UnicodeText)
                {
                    continue;
                }

                var customData = dataObject.GetData(format);
                if (customData is string stringData && !string.IsNullOrEmpty(stringData) && format.Contains("text"))
                {
                    dragData.SetFragmentText(stringData);
                }
            }

            return dragData;
        }
    }

}