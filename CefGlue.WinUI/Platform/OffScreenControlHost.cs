using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinUI.Common;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal class OffScreenControlHost<TControl, TFrame> : CefControl<TControl>, IOffScreenTargetHost
        where TControl : BaseCefControl
    {
        private OffScreenRenderSurface _renderSurface;

        private CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;

        private Microsoft.UI.Input.PointerPoint _lastPointerPoint;

        private bool _pendingMouseMove;
        private CefMouseEvent _pendingMouseMoveEvent;

        private Windows.Foundation.Point _cachedControlOffset;

        private CefDragData _pendingDragData;
        private CefDragOperationsMask _pendingAllowedOps;

        private CefMouseButtonType _lastClickButton;
        private Windows.Foundation.Point _lastClickPosition;
        private long _lastClickTimestampMs;
        private int _clickCount;

        private const double DoubleClickDistance = 4;

        private int ComputeClickCount(CefMouseButtonType button, Windows.Foundation.Point position)
        {
            var now = Environment.TickCount64;
            var withinTime = _lastClickTimestampMs != 0 && (now - _lastClickTimestampMs) <= NativeInterop.DoubleClickTimeMs;
            var withinDistance = Math.Abs(position.X - _lastClickPosition.X) <= DoubleClickDistance
                && Math.Abs(position.Y - _lastClickPosition.Y) <= DoubleClickDistance;

            _clickCount = (button == _lastClickButton && withinTime && withinDistance) ? _clickCount + 1 : 1;

            _lastClickButton = button;
            _lastClickPosition = position;
            _lastClickTimestampMs = now;

            return _clickCount;
        }

        public event Action LostFocus;
        public event Xilium.CefGlue.Common.Platform.KeyEventHandler KeyDown;
        public event Xilium.CefGlue.Common.Platform.KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;

        public event Action<string, int> CompositionUpdated;
        public event Action<string> CompositionCommitted;
        public event Action<string, int, ImeCompositionClause[]> CompositionUpdatedWithClauses;

        public event Action<IOffScreenTargetHost, CefMouseEvent, CefMouseButtonType, int> MouseButtonPressed;
        public event Action<CefMouseEvent, CefMouseButtonType> MouseButtonReleased;
        public event Action<CefMouseEvent> MouseLeave;
        public event Action<CefMouseEvent> MouseMoved;
        public event Action<CefMouseEvent, int, int> MouseWheelChanged;
        public event Action<CefMouseEvent, CefDragData, CefDragOperationsMask> DragEnter;
        public event Action<CefMouseEvent, CefDragOperationsMask> DragOver;
        public event Action DragLeave;
        public event Action<CefMouseEvent, CefDragOperationsMask> Drop;
        public event Action<float> ScreenInfoChanged;
        public event Action<bool> VisibilityChanged;
        public event Action Reattached;

        public OffScreenControlHost(TControl control) : base(control)
        {
            control.LostFocus += OnLostFocus;

            control.PointerMoved += OnPointerMoved;
            control.PointerExited += OnPointerExited;
            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;
            control.PointerWheelChanged += OnPointerWheelChanged;

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.CharacterReceived += OnCharacterReceived;

            control.GotFocus += OnImeGotFocus;
            control.LostFocus += OnImeLostFocus;

            control.AllowDrop = true;
            control.DragEnter += OnDragEnter;
            control.DragOver += OnDragOver;
            control.DragLeave += OnDragLeave;
            control.Drop += OnDrop;
            control.DragStarting += OnDragStarting;

            control.Loaded += OnControlLoaded;
            control.Unloaded += OnControlUnloaded;

            control.LayoutUpdated += OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object sender, object e)
        {
            _cachedControlOffset = _control.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        }

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public void AttachSurface<TSurface>(TSurface surface) where TSurface : IFrameSurface<TFrame>
        {
            ((IFrameSurfaceHost<TFrame>)_control).AttachSurface(surface);
            _renderSurface = new WinUIRenderSurface<TSurface, TFrame>(surface);
        }

        public void AttachRenderSurface(OffScreenRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
        }

        private float _lastRasterizationScale = -1f;

        private bool? _reportedVisible;
        private XamlRoot _subscribedXamlRoot;
        private bool _visibilityReconcileQueued;
        private bool _unloadedSinceReconcile;

        private void OnControlLoaded(object sender, RoutedEventArgs e) => QueueVisibilityReconcile();

        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            _unloadedSinceReconcile = true;
            QueueVisibilityReconcile();
        }

        private void QueueVisibilityReconcile()
        {
            if (_visibilityReconcileQueued)
            {
                return;
            }

            _visibilityReconcileQueued = _control.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ReconcileVisibility);
        }

        private void ReconcileVisibility()
        {
            _visibilityReconcileQueued = false;
            var visible = _control.IsLoaded;
            var reparented = visible && _reportedVisible == true && _unloadedSinceReconcile;
            _unloadedSinceReconcile = false;
            System.Diagnostics.Debug.WriteLine($"[OffScreenControlHost] visibility reconcile: isLoaded={visible} (last reported: {_reportedVisible?.ToString() ?? "none"})");

            var root = visible ? _control.XamlRoot : null;
            if (!ReferenceEquals(root, _subscribedXamlRoot))
            {
                if (_subscribedXamlRoot != null)
                {
                    _subscribedXamlRoot.Changed -= OnXamlRootChanged;
                }

                _subscribedXamlRoot = root;
                if (root != null)
                {
                    root.Changed += OnXamlRootChanged;
                    var scale = (float)root.RasterizationScale;
                    if (scale != _lastRasterizationScale)
                    {
                        _lastRasterizationScale = scale;
                        ScreenInfoChanged?.Invoke(scale);
                    }
                }
            }

            if (_reportedVisible != visible)
            {
                _reportedVisible = visible;
                VisibilityChanged?.Invoke(visible);
            }
            else if (reparented)
            {
                System.Diagnostics.Debug.WriteLine("[OffScreenControlHost] reparented while visible - Reattached");
                Reattached?.Invoke();
            }
        }

        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            var scale = (float)sender.RasterizationScale;
            if (scale == _lastRasterizationScale)
            {
                return;
            }

            _lastRasterizationScale = scale;
            ScreenInfoChanged?.Invoke(scale);
        }

        private const Windows.System.VirtualKey ProcessKey = (Windows.System.VirtualKey)229;

        private WinUICoreTextIme _ime;
        private bool _textInputActive;
        private volatile CefRectangle[] _imeBounds = Array.Empty<CefRectangle>();
        private Windows.Foundation.Rect? _lastImeRect;

        private string _charsSeen = string.Empty;

        private bool EnsureIme()
        {
            if (_ime != null)
            {
                return true;
            }

            if (_control.HostWindow == null)
            {
                return false;
            }

            try
            {
                _ime = new WinUICoreTextIme(
                    _control.DispatcherQueue,
                    GetImeLayout,
                    OnImeUpdated,
                    OnImeCommitted,
                    OnImeDirectText);

                var hwnd = WindowNative.GetWindowHandle(_control.HostWindow);
                var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
                appWindow.Changed += (_, change) =>
                {
                    if (change.DidPositionChange || change.DidSizeChange)
                    {
                        _control.DispatcherQueue.TryEnqueue(() => _ime?.NotifyLayoutChanged());
                    }
                };
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[host {GetHashCode():X}] CoreText IME FAILED: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
                return false;
            }
        }

        private bool ControlHasFocus => _control.FocusState != FocusState.Unfocused;

        private void ActivateIme()
        {
            if (!EnsureIme())
            {
                return;
            }

            var active = _textInputActive && ControlHasFocus;
            _ime.SetActive(active);

            if (active)
            {
                var hwnd = WindowNative.GetWindowHandle(_control.HostWindow);
                var focus = Xilium.CefGlue.WinForms.Ime.ImeOpenStatus.FocusedDescendantOf(hwnd);
                Xilium.CefGlue.WinForms.Ime.ImeOpenStatus.EnsureOpen(focus != IntPtr.Zero ? focus : hwnd);
            }
        }

        public void SetTextInputActive(bool active)
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _textInputActive = active;
                ActivateIme();
            });
        }

        public void UpdateImeCompositionBounds(CefRange selectedRange, CefRectangle[] characterBounds)
        {
            if (characterBounds == null || characterBounds.Length == 0)
            {
                return;
            }

            _imeBounds = characterBounds;
            _control.DispatcherQueue.TryEnqueue(() => _ime?.NotifyLayoutChanged());
        }

        private void OnImeGotFocus(object sender, RoutedEventArgs e)
        {
            _control.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ActivateIme);
        }

        private void OnImeLostFocus(object sender, RoutedEventArgs e)
        {
            _ime?.SetActive(false);
        }

        private void OnImeUpdated(string text, int caret, ImeCompositionClause[] clauses)
        {
            if (text.Length == 0)
            {
                _imeBounds = Array.Empty<CefRectangle>();
            }

            CompositionUpdatedWithClauses?.Invoke(text, caret, clauses);
        }

        private void OnImeCommitted(string text)
        {
            _imeBounds = Array.Empty<CefRectangle>();
            CompositionCommitted?.Invoke(text);
        }

        private void OnImeDirectText(string text)
        {
            var seen = _charsSeen;
            _charsSeen = string.Empty;

            if (seen.EndsWith(text, StringComparison.Ordinal))
            {
                return;
            }

            CompositionCommitted?.Invoke(text);
        }

        private float ImeScale
        {
            get
            {
                var scale = _renderSurface?.DeviceScaleFactor ?? 1f;
                return scale > 0 ? scale : 1f;
            }
        }

        private (Windows.Foundation.Rect Text, Windows.Foundation.Rect Control) GetImeLayout(int start, int end)
        {
            var scale = ImeScale;
            var hwnd = WindowNative.GetWindowHandle(_control.HostWindow);
            var client = NativeInterop.GetClientAreaScreenOrigin(hwnd);
            var offset = _cachedControlOffset;
            var ox = client.X + offset.X * scale;
            var oy = client.Y + offset.Y * scale;

            var control = new Windows.Foundation.Rect(ox, oy, _control.ActualWidth * scale, _control.ActualHeight * scale);

            var bounds = _ime?.IsComposing == true ? _imeBounds : Array.Empty<CefRectangle>();
            if (bounds.Length > 0)
            {
                int left, top, right, bottom;
                if (end <= start)
                {
                    var b = bounds[Math.Min(start, bounds.Length - 1)];
                    left = right = start >= bounds.Length ? b.X + b.Width : b.X;
                    top = b.Y;
                    bottom = b.Y + b.Height;
                }
                else
                {
                    var first = Math.Min(start, bounds.Length - 1);
                    var last = Math.Min(end, bounds.Length) - 1;
                    left = int.MaxValue; top = int.MaxValue; right = int.MinValue; bottom = int.MinValue;
                    for (var i = first; i <= Math.Max(first, last); i++)
                    {
                        var b = bounds[i];
                        left = Math.Min(left, b.X);
                        top = Math.Min(top, b.Y);
                        right = Math.Max(right, b.X + b.Width);
                        bottom = Math.Max(bottom, b.Y + b.Height);
                    }
                }

                _lastImeRect = new Windows.Foundation.Rect(ox + left * scale, oy + top * scale, (right - left) * scale, (bottom - top) * scale);
                return (_lastImeRect.Value, control);
            }

            return (_lastImeRect ?? new Windows.Foundation.Rect(ox + 20, oy + 20, 1, 20), control);
        }

        private void OnCharacterReceived(object sender, CharacterReceivedRoutedEventArgs e)
        {
            if (_ime?.IsComposing == true)
            {
                e.Handled = true;
                return;
            }

            _charsSeen += e.Character.ToString();
            var handled = false;
            TextInput?.Invoke(e.Character.ToString(), out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == ProcessKey)
            {
                return;
            }

            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == ProcessKey)
            {
                return;
            }

            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            var key = e.Key;
            if (key == Windows.System.VirtualKey.Tab
                || key == Windows.System.VirtualKey.Home || key == Windows.System.VirtualKey.End
                || key == Windows.System.VirtualKey.Up || key == Windows.System.VirtualKey.Down
                || key == Windows.System.VirtualKey.Left || key == Windows.System.VirtualKey.Right)
            {
                handled = true;
            }

            e.Handled = handled;
        }

        protected virtual Windows.Foundation.Point MousePositionOffset => default;

        private float CurrentDeviceScaleFactor => RenderSurface?.DeviceScaleFactor ?? 1f;

        protected override float ContextMenuScaleFactor => CurrentDeviceScaleFactor;

        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            MouseWheelChanged?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), 0, point.Properties.MouseWheelDelta);
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var button = e.AsCefMouseButtonType(_control);
            MouseButtonReleased?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), button);
            if (button == CefMouseButtonType.Left)
            {
                _control.ReleasePointerCapture(e.Pointer);
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerPoint = e.GetCurrentPoint(_control);
            var button = e.AsCefMouseButtonType(_control);
            var clickCount = ComputeClickCount(button, _lastPointerPoint.Position);
            MouseButtonPressed?.Invoke(this, e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), button, clickCount);
            if (button == CefMouseButtonType.Left)
            {
                _control.CapturePointer(e.Pointer);
            }

            e.Handled = true;
        }

        public void ReleaseMouseCapture()
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _control.ReleasePointerCaptures();
                }
                catch (COMException)
                {
                }
            });
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            MouseLeave?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor));
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _lastPointerPoint = e.GetCurrentPoint(_control);
            _pendingMouseMoveEvent = e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor);

            if (_pendingMouseMove)
            {
                return;
            }

            _pendingMouseMove = true;
            _control.DispatcherQueue.TryEnqueue(FlushPendingMouseMove);
        }

        private void FlushPendingMouseMove()
        {
            _pendingMouseMove = false;
            MouseMoved?.Invoke(_pendingMouseMoveEvent);
        }

        private void OnDragEnter(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = e.AllowedOperations;
            _ = FireDragEnterAsync(e);
        }

        private async Task FireDragEnterAsync(Microsoft.UI.Xaml.DragEventArgs e)
        {
            CefDragData dragData;
            try
            {
                dragData = await e.GetDragDataAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CefGlue.WinUI] OnDragEnter: GetDragDataAsync failed after returning synchronously - {ex}");
                return;
            }

            DragEnter?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), dragData, e.AllowedOperations.AsCefDragOperationsMask());
        }

        private void OnDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = _currentAllowedDragOps.AsDataPackageOperation();
            DragOver?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), e.AllowedOperations.AsCefDragOperationsMask());
        }

        private void OnDragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            DragLeave?.Invoke();
        }

        private void OnDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            Drop?.Invoke(e.AsCefMouseEvent(_control, MousePositionOffset, CurrentDeviceScaleFactor), e.AllowedOperations.AsCefDragOperationsMask());
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_control.IsContextMenuOpen)
            {
                return;
            }

            LostFocus?.Invoke();
        }

        public virtual void Focus()
        {
            _control.DispatcherQueue.TryEnqueue(() => _control.Focus(FocusState.Pointer));
        }

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            if (_control.HostWindow == null)
            {
                return new CefPoint(point.X, point.Y);
            }

            var hwnd = WindowNative.GetWindowHandle(_control.HostWindow);
            var origin = NativeInterop.GetClientAreaScreenOrigin(hwnd);

            var controlOffset = _cachedControlOffset;

            return new CefPoint(
                origin.X + (int)(controlOffset.X * deviceScaleFactor) + point.X,
                origin.Y + (int)(controlOffset.Y * deviceScaleFactor) + point.Y);
        }

        public CefRectangle GetScreenBounds(float deviceScaleFactor) => default;

        public void SetFullscreen(bool fullscreen)
        {
            void Apply()
            {
                if (_control.HostWindow == null)
                {
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(_control.HostWindow);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow?.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
            }

            if (_control.DispatcherQueue.HasThreadAccess)
            {
                Apply();
            }
            else
            {
                _control.DispatcherQueue.TryEnqueue(Apply);
            }
        }

        public async Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            var pointerPoint = _lastPointerPoint;
            if (pointerPoint == null)
            {
                return CefDragOperationsMask.None;
            }

            var tcs = new TaskCompletionSource<CefDragOperationsMask>();

            _control.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    _control.ReleasePointerCaptures();
                }
                catch (COMException)
                {
                }

                try
                {
                    _pendingDragData = dragData;
                    _pendingAllowedOps = allowedOps;
                    var operation = await _control.StartDragAsync(pointerPoint);
                    tcs.SetResult(operation.AsCefDragOperationsMask());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    _pendingDragData = null;
                    _currentAllowedDragOps = CefDragOperationsMask.None;
                }
            });

            return await tcs.Task;
        }

        private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
        {
            var dragData = _pendingDragData;
            if (dragData == null)
            {
                args.Cancel = true;
                return;
            }

            args.Data.RequestedOperation = _pendingAllowedOps.AsDataPackageOperation();

            if (!string.IsNullOrEmpty(dragData.FragmentText))
            {
                args.Data.SetText(dragData.FragmentText);
            }
            else if (!string.IsNullOrEmpty(dragData.LinkUrl))
            {
                args.Data.SetText(dragData.LinkUrl);
            }

            if (!string.IsNullOrEmpty(dragData.FragmentHtml))
            {
                args.Data.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(dragData.FragmentHtml));
            }

            if (!string.IsNullOrEmpty(dragData.LinkUrl) && Uri.TryCreate(dragData.LinkUrl, UriKind.Absolute, out var linkUri))
            {
                args.Data.SetWebLink(linkUri);
            }

            var fileNames = dragData.GetFileNames();
            if (fileNames != null && fileNames.Length > 0)
            {
                var deferral = args.GetDeferral();
                _ = PopulateFilesAsync(args.Data, fileNames, deferral);
            }

            if (dragData.HasImage)
            {
                using (var cefImage = dragData.GetImage())
                {
                    if (cefImage != null)
                    {
                        using (var binary = cefImage.GetAsBitmap(1f, CefColorType.Bgra8888, CefAlphaType.Premultiplied, out var width, out var height))
                        {
                            if (binary != null && width > 0 && height > 0)
                            {
                                var buffer = new byte[width * 4 * height];
                                binary.GetData(buffer, buffer.Length, 0);

                                var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
                                var hotspot = dragData.GetImageHotspot();
                                args.DragUI.SetContentFromSoftwareBitmap(bitmap, new Windows.Foundation.Point(hotspot.X, hotspot.Y));
                            }
                        }
                    }
                }
            }
        }

        private static async Task PopulateFilesAsync(DataPackage data, string[] filePaths, DragOperationDeferral deferral)
        {
            try
            {
                var items = new List<IStorageItem>(filePaths.Length);
                foreach (var path in filePaths)
                {
                    items.Add(await StorageFile.GetFileFromPathAsync(path));
                }

                data.SetStorageItems(items);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CefGlue.WinUI] OnDragStarting: failed to populate dragged files - {ex}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentAllowedDragOps = allowedOps;
        }
    }
}
