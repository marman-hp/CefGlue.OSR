using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using Xilium.CefGlue.WinForms.Ime;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinForms.Common;

namespace Xilium.CefGlue.WinForms.Platform
{
    public class OffScreenControlHost<TControl, TTarget> : CefControl<TControl>, IOffScreenTargetHost
                    where TControl : BaseCefControl , IFrameSurfaceHost<TTarget>
    {
        private OffScreenRenderSurface _renderSurface;
        public OffScreenRenderSurface RenderSurface => _renderSurface;

        private Bitmap _dragImage;
        private CefDragData _currentDragData;

        private Point _cachedScreenLocation;
        private Form _hostForm;

        public event Action LostFocus;

        public event CefGlue.Common.Platform.KeyEventHandler KeyDown;
        public event CefGlue.Common.Platform.KeyEventHandler KeyUp;

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

        public bool IsWindowPopupMode => _control.Browser?.IsPopup == true ? true : false;

        public OffScreenControlHost(TControl control) : base(control)
        {

            control.ImeMode = ImeMode.On;

            control.VisibleChanged += VisibleChanged;

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.KeyPress += OnKeyPress;

            control.LostFocus += OnLostFocus;

            control.MouseMove += OnMouseMove;
            control.MouseLeave += OnMouseLeave;

            control.GenericMouseDown += OnMouseDown;
            control.GenericMouseUp += OnMouseUp;

            control.MouseWheel += OnMouseWheel;

            control.DragEnter += OnDragEnter;
            control.DragOver += OnDragOver;
            control.DragLeave += OnDragLeave;
            control.DragDrop += OnDrop;

            control.SafeOnLayaoutState += OnSafeLayout;
            control.AllowDrop = true;

            control.GiveFeedback += Control_GiveFeedback;

            control.HandleCreated += OnHandleCreatedUpdateScreenLocation;
            control.HandleDestroyed += (_, _) =>
            {
                DisposeIme();
            };
            if (control.IsHandleCreated)
            {
                OnHandleCreatedUpdateScreenLocation(control, EventArgs.Empty);
            }
            control.Move += (_, _) => UpdateCachedScreenLocation();
            control.Resize += (_, _) => UpdateCachedScreenLocation();
        }

        private TsfImeController _ime;
        private volatile CefRectangle[] _imeBounds = Array.Empty<CefRectangle>();
        private TsfRect? _lastImeRect;

        private void InitializeIme()
        {
            DisposeIme();

            if (_control.IsPopupSelect || !_control.IsHandleCreated)
            {
                return;
            }

            try
            {
                var ime = new TsfImeController(_control.Handle, GetImeTextExt, GetImeScreenExt, SynchronizationContext.Current);
                ime.CompositionUpdated += (text, caret, clauses) =>
                {
                    if (text.Length == 0)
                    {
                        _imeBounds = Array.Empty<CefRectangle>();
                    }

                    CompositionUpdatedWithClauses?.Invoke(text, caret, ToImeClauses(clauses));
                };
                ime.CompositionCommitted += text =>
                {
                    _imeBounds = Array.Empty<CefRectangle>();
                    CompositionCommitted?.Invoke(text);
                };
                _ime = ime;

                _control.TsfImeAttached = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CefGlue.WinForms] TSF IME init failed: {ex}");
                System.Diagnostics.Debug.WriteLine($"TSF controller FAILED: {ex}");
                _control.TsfImeAttached = false;
            }
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

        private void DisposeIme()
        {
            var ime = _ime;
            _ime = null;
            _control.TsfImeAttached = false;
            ime?.Dispose();
        }

        public void SetTextInputActive(bool active)
        {
            if (!_control.IsHandleCreated || _control.IsDisposed)
            {
                return;
            }

            _control.BeginInvoke((Action)(() =>
            {
                _ime?.SetActive(active, _control.Focused);
            }));
        }

        public void UpdateImeCompositionBounds(CefRange selectedRange, CefRectangle[] characterBounds)
        {
            if (characterBounds == null || characterBounds.Length == 0)
            {
                return;
            }

            _imeBounds = characterBounds;

            if (_control.IsHandleCreated && !_control.IsDisposed)
            {
                _control.BeginInvoke((Action)(() => _ime?.NotifyLayoutChanged()));
            }
        }

        private TsfRect? GetImeScreenExt()
        {
            var origin = _control.PointToScreen(Point.Empty);
            return new TsfRect { left = origin.X, top = origin.Y, right = origin.X + _control.Width, bottom = origin.Y + _control.Height };
        }

        private TsfRect? GetImeTextExt(int start, int end)
        {
            var bounds = _ime?.IsComposing == true ? _imeBounds : Array.Empty<CefRectangle>();
            var scale = _renderSurface?.DeviceScaleFactor ?? 1f;
            if (scale <= 0)
            {
                scale = 1f;
            }

            if (bounds.Length > 0)
            {
                int left, top, right, bottom;
                if (end <= start)
                {
                    var b = bounds[Math.Min(start, bounds.Length - 1)];
                    var x = start >= bounds.Length ? b.X + b.Width : b.X;
                    left = right = x;
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

                var origin = _control.PointToScreen(Point.Empty);
                _lastImeRect = new TsfRect
                {
                    left = origin.X + (int)Math.Round(left * scale),
                    top = origin.Y + (int)Math.Round(top * scale),
                    right = origin.X + (int)Math.Round(right * scale),
                    bottom = origin.Y + (int)Math.Round(bottom * scale),
                };
                return _lastImeRect;
            }

            if (_lastImeRect != null)
            {
                return _lastImeRect;
            }

            var o = _control.PointToScreen(Point.Empty);
            return new TsfRect { left = o.X + 20, top = o.Y + 20, right = o.X + 20, bottom = o.Y + 40 };
        }

        private void OnHandleCreatedUpdateScreenLocation(object sender, EventArgs e)
        {
            InitializeIme();
            UpdateCachedScreenLocation();

            var form = _control.FindForm();
            if (form != null && form != _hostForm)
            {
                if (_hostForm != null)
                {
                    _hostForm.Move -= OnHostFormMoved;
                }

                _hostForm = form;
                _hostForm.Move += OnHostFormMoved;
            }
        }

        private void OnHostFormMoved(object sender, EventArgs e)
        {
            UpdateCachedScreenLocation();
        }

        private void UpdateCachedScreenLocation()
        {
            if (!_control.IsHandleCreated)
            {
                return;
            }

            _cachedScreenLocation = _control.PointToScreen(System.Drawing.Point.Empty);

            _ime?.NotifyLayoutChanged();
        }

        private bool fromMinimize;
        private void OnSafeLayout(object o, FormWindowState state)
        {
            if (state == FormWindowState.Minimized)
            {
                VisibilityChanged?.Invoke(false);
                fromMinimize = true;
            }
            else if (state == FormWindowState.Maximized || state == FormWindowState.Normal)
            {
                bool wasMinimized = fromMinimize;
                fromMinimize = false;

                _control.BeginInvoke(() =>
                {
                    _control.SafeResize.Invoke(this, EventArgs.Empty);

                    if (wasMinimized)
                        VisibilityChanged?.Invoke(true);
                });

            }
        }

        private void VisibleChanged(object sender, EventArgs e)
        {
            VisibilityChanged?.Invoke(_control.Visible);
        }

        public virtual void Focus()
        {
            if (_control.IsPopupSelect)
                return;

            _control.Focus();

        }

        private void OnLostFocus(object sender, EventArgs e)
        {
            LostFocus?.Invoke();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            MouseWheelChanged?.Invoke(e.AsCefMouseEvent(_control), 0, (int)e.Delta);
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {

            MouseButtonPressed?.Invoke(this, e.AsCefMouseEvent(_control), e.Button.AsCefMouseButtonType(), e.Clicks);
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            MouseButtonReleased?.Invoke(e.AsCefMouseEvent(_control, true), e.Button.AsCefMouseButtonType());
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {

            System.Drawing.Point clientPoint = _control.PointToClient(Control.MousePosition);

            var ev = new CefMouseEvent(clientPoint.X, clientPoint.Y, CefEventFlags.None);
            MouseLeave?.Invoke(ev);

        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            MouseMoved?.Invoke(e.AsCefMouseEvent(_control));

        }

        private void OnKeyPress(object sender, KeyPressEventArgs e)
        {
            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(), out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ProcessKey)
            {
                return;
            }

            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ProcessKey)
            {
                return;
            }

            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            var key = e.KeyData;

            e.Handled = handled;
        }

        public void AttachSurface<TSurface>(TSurface surface)  where TSurface : IFrameSurface<TTarget>
        {
            _control.AttachSurface(surface);
            _renderSurface = new WinFormsRenderSurface<TSurface, TTarget>(surface);
        }

        public override bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            _control.SetCursorHandler(cursorHandle);
            return true;
        }

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            int physicalX = (int)Math.Round(point.X * deviceScaleFactor);
            int physicalY = (int)Math.Round(point.Y * deviceScaleFactor);

            return new CefPoint(_cachedScreenLocation.X + physicalX, _cachedScreenLocation.Y + physicalY);
        }

        private bool _isFullscreen;
        private FormBorderStyle _previousFormBorderStyle;
        private FormWindowState _previousWindowState;
        private Rectangle _previousBounds;

        public void ReleaseMouseCapture() { }

        public void SetFullscreen(bool fullscreen)
        {
            var form = _control.FindForm();
            if (form == null)
            {
                return;
            }

            void Apply()
            {
                if (fullscreen == _isFullscreen)
                {
                    return;
                }

                _control.BeginResize();

                if (fullscreen)
                {
                    _previousFormBorderStyle = form.FormBorderStyle;
                    _previousWindowState = form.WindowState;
                    _previousBounds = form.Bounds;

                    form.WindowState = FormWindowState.Normal;
                    form.FormBorderStyle = FormBorderStyle.None;

                    form.WindowState = FormWindowState.Maximized;
                }
                else
                {
                    form.FormBorderStyle = _previousFormBorderStyle;
                    form.WindowState = _previousWindowState;

                    if (_previousWindowState == FormWindowState.Normal)
                    {
                        form.Bounds = _previousBounds;
                    }
                }

                _isFullscreen = fullscreen;

                form.BeginInvoke((Action)_control.EndResize);
            }

            if (form.InvokeRequired)
            {
                form.BeginInvoke((Action)Apply);
            }
            else
            {
                Apply();
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.DropImageType = DropImageType.NoImage;
            e.Effect = MapCefOpsToWinFormsEffect(_currentAllowedDragOps);

            var p = _control.PointToClient(new Point(e.X, e.Y));
            var mouseEvent = new CefMouseEvent(p.X, p.Y, CefEventFlags.None);

            DragEnter?.Invoke(mouseEvent, e.Data.AsCefDragData(), e.AllowedEffect.AsCefDragOperationsMask());
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var p = _control.PointToClient(new Point(e.X, e.Y));
            e.Effect = MapCefOpsToWinFormsEffect(_currentAllowedDragOps);

            var mouseEvent = new CefMouseEvent(p.X, p.Y, CefEventFlags.LeftMouseButton);

            DragOver?.Invoke(mouseEvent, e.AllowedEffect.AsCefDragOperationsMask());
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            var p = _control.PointToClient(new Point(e.X, e.Y));
            var mouseEvent = new CefMouseEvent(p.X, p.Y, CefEventFlags.None);

            Drop?.Invoke(mouseEvent, e.AllowedEffect.AsCefDragOperationsMask());
        }

        private void OnDragLeave(object sender, EventArgs e)
        {
            DragLeave?.Invoke();
        }

        private void Control_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            var p = _control.PointToClient(new Point(Cursor.Position.X, Cursor.Position.Y));

            var isOutside = p.X < 0 || p.Y < 0 ||
                     p.X > _control.ClientSize.Width ||
                     p.Y > _control.ClientSize.Height;

            e.DragImage = isOutside ? null : _dragImage;
            e.UseDefaultCursors = true;
        }

        public Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            if (_currentDragData != null)
            {
                return Task.FromResult(CefDragOperationsMask.None);
            }

            _currentDragData = dragData.Clone();
            var tcs = new TaskCompletionSource<CefDragOperationsMask>();
            var hotspot = _currentDragData.GetImageHotspot();

            if (_currentDragData.HasImage)
            {
                using (var cefImage = _currentDragData.GetImage())
                {
                    if (cefImage != null)
                    {
                        using (var binary = cefImage.GetAsBitmap(1f, CefColorType.Bgra8888, CefAlphaType.Opaque, out var w, out var h))
                        {
                            if (binary != null)
                            {
                                _dragImage = ConvertBinaryToBitmap(binary, w, h);
                            }
                        }
                    }
                }
            }

            _control.BeginInvoke(new Action(() =>
            {
                try
                {
                    var dataObject = BuildDataObject(dragData);
                    var effects = MapToWinForms(allowedOps);

                    var result = _control.DoDragDrop(dataObject, effects, _dragImage, new Point(hotspot.X, hotspot.Y), false);

                    tcs.SetResult(MapBack(result));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    _dragImage?.Dispose();
                    _currentDragData = null;
                    _dragImage = null;
                }
            }));

            return tcs.Task;
        }

        private Bitmap ConvertBinaryToBitmap(CefBinaryValue binaryValue, int width, int height)
        {
            var buffer = new byte[binaryValue.Size];
            binaryValue.GetData(buffer, (int)binaryValue.Size, 0);

            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            return bitmap;
        }

        private CefDragOperationsMask MapBack(System.Windows.Forms.DragDropEffects effects)
        {
            CefDragOperationsMask mask = CefDragOperationsMask.None;

            if ((effects & System.Windows.Forms.DragDropEffects.Copy) != 0)
                mask |= CefDragOperationsMask.Copy;

            if ((effects & System.Windows.Forms.DragDropEffects.Move) != 0)
                mask |= CefDragOperationsMask.Move;

            if ((effects & System.Windows.Forms.DragDropEffects.Link) != 0)
                mask |= CefDragOperationsMask.Link;

            return mask;
        }

        private System.Windows.Forms.DragDropEffects MapToWinForms(CefDragOperationsMask mask)
        {
            System.Windows.Forms.DragDropEffects effects = System.Windows.Forms.DragDropEffects.None;

            if (mask.HasFlag(CefDragOperationsMask.Copy))
                effects |= System.Windows.Forms.DragDropEffects.Copy;

            if (mask.HasFlag(CefDragOperationsMask.Move))
                effects |= System.Windows.Forms.DragDropEffects.Move;

            if (mask.HasFlag(CefDragOperationsMask.Link))
                effects |= System.Windows.Forms.DragDropEffects.Link;

            return effects;
        }

        private static readonly Regex UrlRegex = new Regex(@"(https?:\/\/)?(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private System.Windows.Forms.DataObject BuildDataObject(CefDragData dragData)
        {
            var dataObject = new System.Windows.Forms.DataObject();

            if (!string.IsNullOrEmpty(dragData.FragmentText))
            {
                dataObject.SetData(DataFormats.Text, dragData.FragmentText);
                dataObject.SetData(DataFormats.UnicodeText, dragData.FragmentText);
            }

            string IsLinkInsideFragment(string target) =>
                UrlRegex.IsMatch(target) ? UrlRegex.Matches(target).Select(m => m.Value).FirstOrDefault() : "";

            bool IsFileUrl(string url) =>
                Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(System.IO.Path.GetExtension(uri.AbsolutePath));

            if (!string.IsNullOrEmpty(dragData.FragmentHtml))
            {
                var url = IsLinkInsideFragment(dragData.FragmentHtml);
                if (!string.IsNullOrEmpty(url))
                {
                    url = IsFileUrl(url) ? ":about:blank#blocked" : dragData.LinkUrl;
                }

                var html = FormatHtmlForWindows(dragData.FragmentHtml, url);
                dataObject.SetData(DataFormats.Html, false, html);

                var urlBytesWide = Encoding.Unicode.GetBytes(html + "\0");
                dataObject.SetData("text/html", false, urlBytesWide);
            }

            if (!string.IsNullOrEmpty(dragData.LinkUrl) )
            {
                var text = dragData.FragmentText ?? dragData.LinkUrl;
                dataObject.SetData(DataFormats.Text, text);
                dataObject.SetData(DataFormats.UnicodeText, text);
            }

            var fileNames = dragData.GetFileNames();
            if (fileNames != null && fileNames.Length > 0)
            {
                dataObject.SetData(DataFormats.FileDrop, fileNames.ToArray());
            }

            return dataObject;
        }

        private string FormatHtmlForWindows(string html, string sourceUrl = "")
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

        private CefDragOperationsMask _currentAllowedDragOps = CefDragOperationsMask.None;

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentAllowedDragOps = allowedOps;
        }

        private DragDropEffects MapCefOpsToWinFormsEffect(CefDragOperationsMask ops)
        {
            if (ops == CefDragOperationsMask.None) return DragDropEffects.None;

            DragDropEffects result = DragDropEffects.None;
            if ((ops & CefDragOperationsMask.Copy) != 0) result |= DragDropEffects.Copy;
            if ((ops & CefDragOperationsMask.Move) != 0) result |= DragDropEffects.Move;
            if ((ops & CefDragOperationsMask.Link) != 0) result |= DragDropEffects.Link;
            return result;
        }

    }

}
