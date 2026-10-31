using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WPF.Common;
using Xilium.CefGlue.WinForms.Ime;

namespace Xilium.CefGlue.WPF.Platform
{
    public class OffScreenControlHost<TControl, TFrame> : CefControl<TControl>, IOffScreenTargetHost
        where TControl : BaseCefControl, IFrameSurfaceHost<TFrame>
    {
        private Point _browserScreenLocation;

        private OffScreenRenderSurface _renderSurface;

        private CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;
        private CefDragData _currentDragData;

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

        public OffScreenControlHost(TControl control) : base(control)
        {
            control.AllowDrop = true;

            InputMethod.SetPreferredImeState(control, InputMethodState.On);

            control.IsVisibleChanged += OnIsVisibleChanged;
            control.LostKeyboardFocus += OnLostFocus;
            control.GotKeyboardFocus += OnImeGotKeyboardFocus;
            control.LostKeyboardFocus += OnImeLostKeyboardFocus;

            control.PreviewMouseDown += OnPreviewMouseDown;

            control.MouseMove += OnMouseMove;
            control.MouseLeave += OnMouseLeave;
            control.MouseDown += OnMouseDown;
            control.MouseUp += OnMouseUp;
            control.MouseWheel += OnMouseWheel;

            control.DragEnter += OnDragEnter;
            control.DragOver += OnDragOver;
            control.DragLeave += OnDragLeave;
            control.Drop += OnDrop;

            control.Loaded += OnLoaded;
            control.Unloaded += OnUnloaded;

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;

            control.TextInput += OnTextInput;
            control.GiveFeedback += Control_GiveFeedback;

            if (control is BitmapControl bitmapControl)
            {
                bitmapControl.ImeCompositionUpdated += (text, cursorPos) => CompositionUpdated?.Invoke(text, cursorPos);
                bitmapControl.ImeCompositionCommitted += text => CompositionCommitted?.Invoke(text);
            }
        }

        public OffScreenRenderSurface RenderSurface => _renderSurface;

        public void AttachSurface<TSurface>(TSurface surface) where TSurface : IFrameSurface<TFrame>
        {
            _control.AttachSurface(surface);
            _renderSurface = new WpfRenderSurface<TSurface, TFrame>(surface);
        }

        private void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            var handled = false;
            TextInput?.Invoke(e.Text, out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            var key = e.Key;
            if (key == Key.Tab
                || key == Key.Home || key == Key.End
                || key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right
            )
            {
                handled = true;
            }

            e.Handled = handled;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            ShellDragImageHelper.NotifyDrop(e.Data, _control.PointToScreen(e.GetPosition(_control)), (int)e.Effects);
            Drop?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.AllowedEffects.AsCefDragOperationsMask());
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            ShellDragImageHelper.NotifyDragLeave();
            DragLeave?.Invoke();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = _currentAllowedDragOps.AsDragDropEffects();
            ShellDragImageHelper.NotifyDragOver(_control.PointToScreen(e.GetPosition(_control)), (int)e.Effects);
            DragOver?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.AllowedEffects.AsCefDragOperationsMask());
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = _currentAllowedDragOps.AsDragDropEffects();

            var hwndSource = PresentationSource.FromVisual(_control) as HwndSource;
            if (hwndSource != null)
            {
                ShellDragImageHelper.NotifyDragEnter(hwndSource.Handle, e.Data, _control.PointToScreen(e.GetPosition(_control)), (int)e.Effects);
            }

            DragEnter?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.GetDragData(), e.AllowedEffects.AsCefDragOperationsMask());
        }

        private void Control_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (_currentAllowedDragOps == CefDragOperationsMask.None)
            {
                e.UseDefaultCursors = false;
                e.Handled = true;
                Mouse.SetCursor(Cursors.No);
            }
            else
            {
                e.UseDefaultCursors = true;
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            MouseWheelChanged?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), 0, (int)e.Delta);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {

            MouseButtonReleased?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.ChangedButton.AsCefMouseButtonType());
            if (e.ChangedButton == MouseButton.Left)
            {
                Mouse.Capture(null);
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _control.Focus();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            MouseButtonPressed?.Invoke(this, e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset), e.ChangedButton.AsCefMouseButtonType(), e.ClickCount);
            if (e.ChangedButton == MouseButton.Left)
            {
                Mouse.Capture(_control);
            }
        }

        public void ReleaseMouseCapture()
        {
            if (Mouse.Captured == _control)
            {
                Mouse.Capture(null);
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            MouseLeave?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset));
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            MouseMoved?.Invoke(e.AsCefMouseEvent(MousePositionReferential, MousePositionOffset));
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_control.IsContextMenuOpen)
            {
                return;
            }

            LostFocus?.Invoke();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            VisibilityChanged?.Invoke(_control.IsVisible);
        }

        private static readonly HashSet<IntPtr> ImeClosers = new();

        private TsfImeSession _imeSession;
        private ImeClient _imeClient;
        private bool _textInputActive;
        private volatile CefRectangle[] _imeBounds = Array.Empty<CefRectangle>();
        private TsfRect? _lastImeRect;

        private sealed class ImeClient : ITsfImeClient
        {
            private readonly OffScreenControlHost<TControl, TFrame> _host;

            public ImeClient(OffScreenControlHost<TControl, TFrame> host) => _host = host;

            public TsfRect? GetCompositionExt(int start, int end) => _host.GetImeTextExt(start, end);

            public TsfRect? GetScreenExt() => _host.GetImeScreenExt();

            public void OnCompositionUpdated(string text, int caret, TsfClause[] clauses)
            {
                if (text.Length == 0)
                {
                    _host._imeBounds = Array.Empty<CefRectangle>();
                }

                _host.CompositionUpdatedWithClauses?.Invoke(text, caret, ToImeClauses(clauses));
            }

            public void OnCompositionCommitted(string text)
            {
                _host._imeBounds = Array.Empty<CefRectangle>();
                _host.CompositionCommitted?.Invoke(text);
            }
        }

        private bool EnsureImeSession()
        {
            if (_imeSession != null || !(_control is BitmapControl bitmapControl))
            {
                return _imeSession != null;
            }

            if (!(PresentationSource.FromVisual(_control) is HwndSource hwndSource) || hwndSource.IsDisposed)
            {
                return false;
            }

            try
            {
                var hwnd = hwndSource.Handle;
                _imeSession = TsfImeSession.GetOrCreate(hwnd, SynchronizationContext.Current);
                _imeClient = new ImeClient(this);

                lock (ImeClosers)
                {
                    if (ImeClosers.Add(hwnd))
                    {
                        hwndSource.Disposed += (_, _) =>
                        {
                            lock (ImeClosers) { ImeClosers.Remove(hwnd); }
                            TsfImeSession.Close(hwnd);
                        };
                    }
                }

                bitmapControl.TsfImeAttached = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[wpf host {GetHashCode():X}] TSF session FAILED: {ex}");
                _imeSession = null;
                bitmapControl.TsfImeAttached = false;
                return false;
            }
        }

        public void SetTextInputActive(bool active)
        {
            _control.Dispatcher.BeginInvoke(new Action(() =>
            {
                _textInputActive = active;
                if (EnsureImeSession())
                {
                    _imeSession.SetActive(_imeClient, active && _control.IsKeyboardFocused, _control.IsKeyboardFocused);
                }
            }));
        }

        public void UpdateImeCompositionBounds(CefRange selectedRange, CefRectangle[] characterBounds)
        {
            if (characterBounds == null || characterBounds.Length == 0)
            {
                return;
            }

            _imeBounds = characterBounds;
            _control.Dispatcher.BeginInvoke(new Action(() => _imeSession?.NotifyLayoutChanged(_imeClient)));
        }

        private void OnImeGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _control.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_textInputActive && EnsureImeSession())
                {
                    _imeSession.SetActive(_imeClient, true, _control.IsKeyboardFocused);
                    _imeSession.ReassertFocus(_imeClient);
                }
            }));
        }

        private void OnImeLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _imeSession?.SetActive(_imeClient, false, false);
        }

        private TsfRect? GetImeScreenExt()
        {
            var scale = (PresentationSource.FromVisual(_control)?.CompositionTarget?.TransformToDevice.M11) ?? 1.0;
            var origin = _control.PointToScreen(new Point(0, 0));
            return new TsfRect
            {
                left = (int)origin.X,
                top = (int)origin.Y,
                right = (int)(origin.X + _control.ActualWidth * scale),
                bottom = (int)(origin.Y + _control.ActualHeight * scale),
            };
        }

        private TsfRect? GetImeTextExt(int start, int end)
        {
            var bounds = _imeSession?.IsComposing == true ? _imeBounds : Array.Empty<CefRectangle>();
            var scale = _renderSurface?.DeviceScaleFactor ?? 1f;
            if (scale <= 0)
            {
                scale = 1f;
            }

            var origin = _control.PointToScreen(new Point(0, 0));

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

                _lastImeRect = new TsfRect
                {
                    left = (int)(origin.X + left * scale),
                    top = (int)(origin.Y + top * scale),
                    right = (int)(origin.X + right * scale),
                    bottom = (int)(origin.Y + bottom * scale),
                };
                return _lastImeRect;
            }

            if (_lastImeRect != null)
            {
                return _lastImeRect;
            }

            return new TsfRect { left = (int)origin.X + 20, top = (int)origin.Y + 20, right = (int)origin.X + 20, bottom = (int)origin.Y + 40 };
        }

        private static ImeCompositionClause[] ToImeClauses(TsfClause[] clauses)
        {
            if (clauses == null)
            {
                return null;
            }

            var result = new ImeCompositionClause[clauses.Length];
            for (var i = 0; i < clauses.Length; i++)
            {
                var c = clauses[i];

                var style = c.LineStyle switch
                {
                    0 => CefCompositionUnderlineStyle.None,
                    2 => CefCompositionUnderlineStyle.Dot,
                    3 => CefCompositionUnderlineStyle.Dash,
                    _ => CefCompositionUnderlineStyle.Solid,
                };

                result[i] = new ImeCompositionClause(c.Start, c.End, c.Target || c.BoldLine, style, c.LineColorArgb);
            }

            return result;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureImeSession();
            var element = (FrameworkElement)sender;
            PresentationSource.AddSourceChangedHandler(element, OnPresentationSourceChanged);
            var source = PresentationSource.FromVisual(element);
            UpdatePresentationSource(source, source);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            PresentationSource.RemoveSourceChangedHandler(element, OnPresentationSourceChanged);
        }

        private void OnPresentationSourceChanged(object sender, SourceChangedEventArgs e)
        {
            UpdatePresentationSource(e.OldSource, e.NewSource);
        }

        private void UpdatePresentationSource(PresentationSource oldSource, PresentationSource newSource)
        {
            if (oldSource?.RootVisual is Window oldWindow)
            {
                oldWindow.StateChanged -= OnHostWindowStateChanged;
                oldWindow.LocationChanged -= OnHostWindowLocationChanged;
            }

            if (newSource != null)
            {
                _browserScreenLocation = GetBrowserScreenLocation();

                var matrix = newSource.CompositionTarget.TransformToDevice;
                ScreenInfoChanged?.Invoke((float)matrix.M11);

                if (newSource.RootVisual is Window window)
                {
                    window.StateChanged += OnHostWindowStateChanged;
                    window.LocationChanged += OnHostWindowLocationChanged;
                }
            }
        }

        protected virtual IInputElement MousePositionReferential => _control;

        protected virtual Point MousePositionOffset => default;

        public virtual void Focus()
        {
            _control.Focus();
        }

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            return new CefPoint((int) (_browserScreenLocation.X + point.X * deviceScaleFactor), (int) (_browserScreenLocation.Y + point.Y * deviceScaleFactor));
        }

        private bool _isFullscreen;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;
        private WindowState _previousWindowState;
        private double _previousLeft;
        private double _previousTop;
        private double _previousWidth;
        private double _previousHeight;
        private Brush _previousBackground;
        private Window _fullscreenHookedWindow;

        public void SetFullscreen(bool fullscreen)
        {
            void Apply()
            {
                var window = Window.GetWindow(_control);
                if (window == null)
                {
                    return;
                }

                if (fullscreen == _isFullscreen)
                {
                    return;
                }

                EnsureFullscreenNcHook(window);
                _isFullscreen = fullscreen;

                if (fullscreen)
                {
                    _previousWindowStyle = window.WindowStyle;
                    _previousResizeMode = window.ResizeMode;
                    _previousWindowState = window.WindowState;
                    _previousLeft = window.Left;
                    _previousTop = window.Top;
                    _previousWidth = window.Width;
                    _previousHeight = window.Height;
                    _previousBackground = window.Background;

                    window.Background = Brushes.Black;
                    window.WindowStyle = WindowStyle.None;
                    window.ResizeMode = ResizeMode.NoResize;
                    window.WindowState = WindowState.Normal;

                    if (TryGetMonitorBoundsInDips(window, out var bounds))
                    {
                        window.Left = bounds.Left;
                        window.Top = bounds.Top;
                        window.Width = bounds.Width;
                        window.Height = bounds.Height;

                    }
                }
                else
                {
                    window.WindowStyle = _previousWindowStyle;
                    window.ResizeMode = _previousResizeMode;
                    window.WindowState = _previousWindowState;
                    window.Background = _previousBackground;

                    if (_previousWindowState == WindowState.Normal)
                    {
                        window.Left = _previousLeft;
                        window.Top = _previousTop;
                        window.Width = _previousWidth;
                        window.Height = _previousHeight;
                    }
                }
            }

            if (_control.Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                _control.Dispatcher.BeginInvoke((Action)Apply);
            }
        }

        private void EnsureFullscreenNcHook(Window window)
        {
            if (_fullscreenHookedWindow == window)
            {
                return;
            }

            if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
            {
                hwndSource.AddHook(FullscreenNcWndProc);
                _fullscreenHookedWindow = window;
            }
        }

        private IntPtr FullscreenNcWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCCALCSIZE = 0x0083;

            if (msg == WM_NCCALCSIZE && _isFullscreen && wParam != IntPtr.Zero)
            {
                handled = true;
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        public override bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            _control.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() =>
                {
                    var cursor = CursorInteropHelper.Create(new SafeFileHandle(cursorHandle, false));
                    _control.Cursor = cursor;
                }));

            return true;
        }

        public Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            if (_currentDragData != null)
            {
                return Task.FromResult(CefDragOperationsMask.None);
            }

            _currentDragData = dragData.Clone();
            var tcs = new TaskCompletionSource<CefDragOperationsMask>();

            _control.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    var comDataObject = new ComDataObject();
                    var dataObject = BuildDataObject(dragData, comDataObject);

                    ShellDragImageHelper.AttachDragImageIfPresent(_currentDragData, comDataObject);

                    var result = DragDrop.DoDragDrop(_control, dataObject, allowedOps.AsDragDropEffects());
                    tcs.SetResult(result.AsCefDragOperationsMask());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    _currentDragData = null;
                }
            }));

            return tcs.Task;
        }

        private static readonly Regex UrlRegex = new Regex(@"(https?:\/\/)?(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private DataObject BuildDataObject(CefDragData dragData, ComDataObject comDataObject)
        {
            if (!string.IsNullOrEmpty(dragData.FragmentText))
            {
                comDataObject.SetTextFormat(DataFormats.Text, Encoding.Default.GetBytes(dragData.FragmentText + "\0"));
                comDataObject.SetTextFormat(DataFormats.UnicodeText, Encoding.Unicode.GetBytes(dragData.FragmentText + "\0"));
            }

            string IsLinkInsideFragment(string target)
            {
                return UrlRegex.IsMatch(target) ? UrlRegex.Matches(target).Select(m => m.Value).FirstOrDefault() : "";
            }

            bool IsFileUrl(string url)
            {
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(System.IO.Path.GetExtension(uri.AbsolutePath));
            }

            if (!string.IsNullOrEmpty(dragData.FragmentHtml))
            {
                var url = IsLinkInsideFragment(dragData.FragmentHtml);
                if (!string.IsNullOrEmpty(url))
                {
                    url = IsFileUrl(url) ? ":about:blank#blocked" : dragData.LinkUrl;
                }

                var html = FormatHtmlForWindows(dragData.FragmentHtml, url);
                comDataObject.SetTextFormat(DataFormats.Html, Encoding.UTF8.GetBytes(html));

                var urlBytesWide = Encoding.Unicode.GetBytes(html + "\0");
                comDataObject.SetTextFormat("text/html", urlBytesWide);
            }

            if (!string.IsNullOrEmpty(dragData.LinkUrl))
            {
                var text = dragData.FragmentText ?? dragData.LinkUrl;
                comDataObject.SetTextFormat(DataFormats.Text, Encoding.Default.GetBytes(text + "\0"));
                comDataObject.SetTextFormat(DataFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0"));
            }

            var fileNames = dragData.GetFileNames();
            if (fileNames != null && fileNames.Length > 0)
            {
                comDataObject.SetFileDropList(fileNames);
            }

            return new DataObject(comDataObject);
        }

        private static string FormatHtmlForWindows(string html, string sourceUrl = "")
        {
            var fragment = html;

            var fullHtml =
                "<html>\r\n" +
                "<body>\r\n" +
                "<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n" +
                "</body>\r\n" +
                "</html>\r\n";

            var headerTemplate = "Version:0.9\r\n" +
                                    "StartHTML:0000000000\r\n" +
                                    "EndHTML:0000000000\r\n" +
                                    "StartFragment:0000000000\r\n" +
                                    "EndFragment:0000000000\r\n" +
                                    (string.IsNullOrEmpty(sourceUrl) ? "" : $"SourceURL:{sourceUrl}\r\n");

            var enc = Encoding.UTF8;

            var startHtml = enc.GetByteCount(headerTemplate);
            var endHtml = startHtml + enc.GetByteCount(fullHtml);

            var startFragCharIndex = fullHtml.IndexOf("<!--StartFragment-->") + "<!--StartFragment-->".Length;
            var endFragCharIndex = fullHtml.IndexOf("<!--EndFragment-->");
            var startFragment = startHtml + enc.GetByteCount(fullHtml.Substring(0, startFragCharIndex));
            var endFragment = startHtml + enc.GetByteCount(fullHtml.Substring(0, endFragCharIndex));

            var finalHeader = headerTemplate
                .Replace("StartHTML:0000000000", $"StartHTML:{startHtml:D10}")
                .Replace("EndHTML:0000000000", $"EndHTML:{endHtml:D10}")
                .Replace("StartFragment:0000000000", $"StartFragment:{startFragment:D10}")
                .Replace("EndFragment:0000000000", $"EndFragment:{endFragment:D10}");

            return finalHeader + fullHtml;
        }

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentAllowedDragOps = allowedOps;
        }

        private void OnHostWindowLocationChanged(object sender, EventArgs e)
        {
            _browserScreenLocation = GetBrowserScreenLocation();

            _imeSession?.NotifyLayoutChanged(_imeClient);
        }

        private void OnHostWindowStateChanged(object sender, EventArgs e)
        {
            _imeSession?.NotifyLayoutChanged(_imeClient);

            var window = (Window)sender;

            switch (window.WindowState)
            {
                case WindowState.Normal:
                case WindowState.Maximized:
                    VisibilityChanged?.Invoke(_control.IsVisible);
                    break;

                case WindowState.Minimized:
                    VisibilityChanged?.Invoke(false);
                    break;
            }
        }

        private Point GetBrowserScreenLocation()
        {
            if (PresentationSource.FromVisual(_control) != null)
            {
                var wpfPoint = _control.PointToScreen(new Point());
                return new Point((int) wpfPoint.X, (int) wpfPoint.Y);
            }
            return new Point(0, 0);
        }

        private static bool TryGetMonitorBoundsInDips(Window window, out Rect bounds)
        {
            bounds = default;

            if (!(PresentationSource.FromVisual(window) is HwndSource hwndSource))
            {
                return false;
            }

            var monitor = FullscreenNativeMethods.MonitorFromWindow(hwndSource.Handle, FullscreenNativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new FullscreenNativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<FullscreenNativeMethods.MONITORINFO>() };

            if (!FullscreenNativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            var transform = hwndSource.CompositionTarget.TransformFromDevice;
            var topLeft = transform.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
            var bottomRight = transform.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));

            bounds = new Rect(topLeft, bottomRight);
            return true;
        }
    }

    internal static class FullscreenNativeMethods
    {
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
