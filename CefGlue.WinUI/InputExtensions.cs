using System;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Xilium.CefGlue.WinUI
{
    internal static class InputExtensions
    {
        public static CefMouseEvent AsCefMouseEvent(this PointerRoutedEventArgs eventArgs, UIElement referential, Windows.Foundation.Point offset = default, float deviceScaleFactor = 1f)
        {
            var point = eventArgs.GetCurrentPoint(referential);
            var props = point.Properties;
            var modifiers = CefEventFlags.None;

            if (props.IsLeftButtonPressed) modifiers |= CefEventFlags.LeftMouseButton;
            if (props.IsRightButtonPressed) modifiers |= CefEventFlags.RightMouseButton;
            if (props.IsMiddleButtonPressed) modifiers |= CefEventFlags.MiddleMouseButton;

            return new CefMouseEvent(
                (int)(point.Position.X / deviceScaleFactor + offset.X),
                (int)(point.Position.Y / deviceScaleFactor + offset.Y),
                modifiers | GetCefKeyboardModifiers());
        }

        public static CefMouseButtonType AsCefMouseButtonType(this PointerRoutedEventArgs eventArgs, UIElement referential)
        {
            var kind = eventArgs.GetCurrentPoint(referential).Properties.PointerUpdateKind;
            switch (kind)
            {
                case Microsoft.UI.Input.PointerUpdateKind.MiddleButtonPressed:
                case Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased:
                    return CefMouseButtonType.Middle;
                case Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed:
                case Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased:
                    return CefMouseButtonType.Right;
                default:
                    return CefMouseButtonType.Left;
            }
        }

        public static CefKeyEvent AsCefKeyEvent(this KeyRoutedEventArgs eventArgs, bool isKeyUp)
        {
            return new CefKeyEvent
            {
                EventType = isKeyUp ? CefKeyEventType.KeyUp : CefKeyEventType.RawKeyDown,
                WindowsKeyCode = (int)eventArgs.Key,
                NativeKeyCode = 0,
                IsSystemKey = eventArgs.Key == VirtualKey.Menu,
                Modifiers = GetCefKeyboardModifiers(),
            };
        }

        public static CefEventFlags GetCefKeyboardModifiers()
        {
            var modifiers = CefEventFlags.None;

            if (IsDown(VirtualKey.Menu)) modifiers |= CefEventFlags.AltDown;
            if (IsDown(VirtualKey.Control)) modifiers |= CefEventFlags.ControlDown;
            if (IsDown(VirtualKey.Shift)) modifiers |= CefEventFlags.ShiftDown;

            return modifiers;
        }

        private static bool IsDown(VirtualKey key)
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
            return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        public static CefMouseEvent AsCefMouseEvent(this Microsoft.UI.Xaml.DragEventArgs eventArgs, UIElement referential, Windows.Foundation.Point offset = default, float deviceScaleFactor = 1f)
        {
            var pos = eventArgs.GetPosition(referential);
            return new CefMouseEvent(
                (int)(pos.X / deviceScaleFactor + offset.X),
                (int)(pos.Y / deviceScaleFactor + offset.Y),
                GetCefKeyboardModifiers());
        }

        public static CefDragOperationsMask AsCefDragOperationsMask(this DataPackageOperation ops)
        {
            if (ops == DataPackageOperation.None)
            {
                return CefDragOperationsMask.None;
            }

            var mask = CefDragOperationsMask.None;
            if ((ops & DataPackageOperation.Copy) != 0) mask |= CefDragOperationsMask.Copy;
            if ((ops & DataPackageOperation.Move) != 0) mask |= CefDragOperationsMask.Move;
            if ((ops & DataPackageOperation.Link) != 0) mask |= CefDragOperationsMask.Link;
            return mask;
        }

        public static DataPackageOperation AsDataPackageOperation(this CefDragOperationsMask mask)
        {
            if (mask == CefDragOperationsMask.None)
            {
                return DataPackageOperation.None;
            }

            var ops = DataPackageOperation.None;
            if ((mask & CefDragOperationsMask.Copy) != 0) ops |= DataPackageOperation.Copy;
            if ((mask & CefDragOperationsMask.Move) != 0) ops |= DataPackageOperation.Move;
            if ((mask & CefDragOperationsMask.Link) != 0) ops |= DataPackageOperation.Link;
            return ops;
        }

        public static async Task<CefDragData> GetDragDataAsync(this Microsoft.UI.Xaml.DragEventArgs eventArgs)
        {
            var dragData = CefDragData.Create();
            var view = eventArgs.DataView;

            if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        dragData.AddFile(file.Path.Replace("\\", "/"), file.Name);
                    }
                }
            }

            if (view.Contains(StandardDataFormats.Text))
            {
                var text = await view.GetTextAsync();
                dragData.SetFragmentText(text);

                if (view.Contains(StandardDataFormats.Html))
                {
                    var html = await view.GetHtmlFormatAsync();
                    if (!string.IsNullOrEmpty(html))
                    {
                        dragData.SetFragmentHtml(html);
                    }
                }

                if (!string.IsNullOrEmpty(text) && Uri.IsWellFormedUriString(text, UriKind.Absolute))
                {
                    dragData.SetFragmentBaseURL(text);
                    dragData.SetLinkURL(text);
                    dragData.SetLinkTitle(text);
                }
            }
            else if (view.Contains(StandardDataFormats.WebLink))
            {
                var uri = await view.GetWebLinkAsync();
                if (uri != null)
                {
                    var url = uri.ToString();
                    dragData.SetLinkURL(url);
                    dragData.SetLinkTitle(url);
                }
            }

            return dragData;
        }
    }
}
