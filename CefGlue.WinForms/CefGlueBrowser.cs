using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinForms.Common;
using Xilium.CefGlue.WinForms.Platform;

namespace Xilium.CefGlue.WinForms
{

    public class CefGlueBrowser : BaseCefBrowser
    {

        private Control _activeControl;
        private EventHandler _activeControlHandleCreatedHandler;
        private readonly Func<CefWindowInfo> _setupCefWindowInfo;
        private readonly Func<CefBrowserSettings> _setupCefBrowserSettings;

        public CefGlueBrowser() : this(null)
        {

        }

        public CefGlueBrowser(  Func<CefRequestContext> cefRequestContextFactory = null,
                                Func<CefWindowInfo> setupCefWindowInfo = null,
                                Func<CefBrowserSettings> setupCefBrowserSettings  = null,
                                 bool IsPopupWindow = false,
                                 CefGlueHost host = null  ) : base(cefRequestContextFactory, host)
        {
            _setupCefWindowInfo = setupCefWindowInfo;
            _setupCefBrowserSettings = setupCefBrowserSettings;

            var windowInfo = setupCefWindowInfo?.Invoke();
            if (windowInfo?.SharedTextureEnabled == true)
            {
                _textureFallbackReason = ProbeSharedTextures();
            }

            if (_textureFallbackReason != null)
            {
                // The adapter calls the setup delegate again when it creates the browser; keep CEF on
                // plain OnPaint there instead of shared textures the bitmap path would have to read back.
                _setupCefWindowInfo = () =>
                {
                    var info = setupCefWindowInfo();
                    if (info != null)
                    {
                        info.SharedTextureEnabled = false;
                    }
                    return info;
                };
                _logger.Warn($"Shared-texture rendering not supported, using plain bitmap: {_textureFallbackReason}");
            }

            RenderMode = windowInfo?.SharedTextureEnabled == true && _textureFallbackReason == null
                ? OsrRenderMode.BitmapReadback
                : OsrRenderMode.Bitmap;
            RenderModeReason = _textureFallbackReason;

            AttachTarget(ResolveControl());
        }

        /// <summary>
        /// When true (default), a browser asking for shared-texture rendering (BitmapReadback) falls back to plain
        /// Bitmap if the GPU can't do it, instead of rendering nothing. Read when each browser is created.
        /// </summary>
        public static bool AllowTextureFallback { get; set; } = true;

        /// <summary>
        /// How this browser's frames currently reach the screen: <see cref="OsrRenderMode.Bitmap"/> or
        /// <see cref="OsrRenderMode.BitmapReadback"/> (WinForms has no zero-copy texture control).
        /// </summary>
        public OsrRenderMode RenderMode { get; private set; }

        /// <summary>Why <see cref="RenderMode"/> isn't what was asked for; null when it is.</summary>
        public string RenderModeReason { get; private set; }

        /// <summary>
        /// Raised on the UI thread when the render mode changes after creation (BitmapReadback → Bitmap when CEF
        /// turns out to paint in software). A fallback decided at creation is only visible through <see cref="RenderMode"/>.
        /// </summary>
        public event Action<OsrRenderMode, string> RenderModeChanged;

        private readonly string _textureFallbackReason;

        /// <summary>
        /// BitmapReadback opens CEF's shared texture on our own D3D11 device; without one every frame would fail
        /// and the control would stay blank. Returns null when shared textures can be used.
        /// </summary>
        private static string ProbeSharedTextures()
        {
            var unsupportedReason = AllowTextureFallback ? SharedTextureSupport.GetUnsupportedReason() : null;
#if DEBUG
            // Test hook: keep BitmapReadback even when the probe would refuse it.
            if (Environment.GetEnvironmentVariable("CEFGLUE_DEBUG_SKIP_TEXTURE_PROBE")?.Trim() == "1")
            {
                unsupportedReason = null;
            }
#endif
            return unsupportedReason;
        }

        public new T AttachTarget<T>(T target) where T : BaseCefControl
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (_adapter != null)
            {
                throw new InvalidOperationException("AttachTarget can only be called before the browser has been created - this CefGlueBrowser is already attached and initialized.");
            }

            if (target is not BitmapControl)
            {
                throw new ArgumentException($"CefGlue.OSR renders off-screen only and needs a {nameof(BitmapControl)}, not {target.GetType().Name}.", nameof(target));
            }

            if (_activeControl != null)
            {
                if (_activeControlHandleCreatedHandler != null)
                {
                    _activeControl.HandleCreated -= _activeControlHandleCreatedHandler;
                }

                if (!ReferenceEquals(_activeControl, target) && !_activeControl.IsHandleCreated)
                {
                    _activeControl.Dispose();
                }
            }

            _activeControl = target;

            _activeControlHandleCreatedHandler = (_, _) =>
            {
                base.AttachTarget(CreateOffScreenControlHost());

                _adapter.SetupCefWindowInfo = _setupCefWindowInfo;
                _adapter.SetupCefBrowserSettings = _setupCefBrowserSettings;
                _adapter.Initialized += () =>
                {
                    target.AttachBrowser(UnderlyingBrowser);
                };
            };

            if (_activeControl.IsHandleCreated)
            {
                _activeControlHandleCreatedHandler(_activeControl, EventArgs.Empty);
            }
            else
            {
                _activeControl.HandleCreated += _activeControlHandleCreatedHandler;
            }

            return target;
        }

        protected virtual BaseCefControl ResolveControl()
        {
            return new BitmapControl { UseSafeResize = true };
        }

        public Control Control => _activeControl;

        private WinFormBitmapSurface _surface;
        private bool _showOverlayInfo = false;

        public bool ShowOverlayInfo
        {
            get => _surface?.ShowOverlayInfo ?? _showOverlayInfo;
            set
            {
                _showOverlayInfo = value;
                if (_surface != null)
                {
                    _surface.ShowOverlayInfo = value;
                    (_activeControl as BitmapControl)?.RefreshOverlay();
                }
            }
        }

        public void CloseBrowser() {
            UnderlyingBrowser?.GetHost()?.CloseBrowser(true);
        }

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            _surface = new WinFormBitmapSurface { ShowOverlayInfo = _showOverlayInfo };
            var _windowOsr = new OffScreenControlHost<BitmapControl, Bitmap>((BitmapControl)_activeControl);
            _windowOsr.AttachSurface(_surface);

            if (RenderMode == OsrRenderMode.BitmapReadback)
            {
                // The probe can't see Chromium's GPU blocklist (e.g. VMware SVGA 3D passes our D3D11 check, yet CEF
                // composites in software). Rendering still works - BitmapControl takes OnPaint too - but report it.
                var renderSurface = _windowOsr.RenderSurface;
                var control = _activeControl;
                void OnFrameAccepted(int width, int height)
                {
                    if (renderSurface.IsAccelerated)
                    {
                        return;
                    }

                    renderSurface.FrameAccepted -= OnFrameAccepted;
                    const string Reason = "CEF is painting in software (OnPaint): GPU compositing is unavailable or blocklisted.";
                    _logger.Warn($"Shared textures requested but not used: {Reason}");

                    if (control.IsHandleCreated && !control.IsDisposed)
                    {
                        control.BeginInvoke((Action)(() =>
                        {
                            RenderMode = OsrRenderMode.Bitmap;
                            RenderModeReason = Reason;
                            RenderModeChanged?.Invoke(RenderMode, Reason);
                        }));
                    }
                }
                renderSurface.FrameAccepted += OnFrameAccepted;
            }

            return _windowOsr;
        }

        protected internal override IOffScreenPopupHost CreatePopupHost()
        {
            if (_activeControl is BitmapControl bitmapControl)
            {
                return new PopupControlHost(bitmapControl);
            }

            return new NullPopupHost();
        }

        private readonly List<CefGlueBrowser> _pendingPopups = new List<CefGlueBrowser>();
        private readonly Dictionary<int, CefGlueBrowser> _popups = new Dictionary<int, CefGlueBrowser>();

        public void CreatePopupBrowser(ref CefClient popUpClient,
                                       ref CefDictionaryValue popUpDictionary,
                                       CefWindowInfo windowInfo,
                                       CefPopupFeatures popupFeatures,
                                       CefWindowOpenDisposition targetDisposition)
        {
            var popupChild = new CefGlueBrowser(IsPopupWindow: true);

            popupChild.BuildAdapter();

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild);
            popupChild.Control.Dock = DockStyle.Fill;

            var rect = (popupFeatures.Width == null || popupFeatures.Height == null)
                ? new Size() : new Size(popupFeatures.Width.Value, popupFeatures.Height.Value);

            popupChild.LifeSpanHandler = new PopupLifeSpanHandler(this, popupChild, rect);

            _pendingPopups.Add(popupChild);
        }

        internal void PromotePopup(int identifier, CefGlueBrowser popup)
        {
            _pendingPopups.Remove(popup);
            _popups[identifier] = popup;
        }

        internal void RemovePopupByIdentifier(int identifier)
        {
            if (_popups.Remove(identifier, out var popup))
            {
                popup.Dispose();
            }
        }

        internal void AbortPendingPopup(CefGlueBrowser popup)
        {
            if (_pendingPopups.Remove(popup))
            {
                popup.Dispose();
            }
        }

        public event Action<Control> DevToolsOpened;

        public event Action DevToolsClosed;

        private CefGlueBrowser _devToolsOsrBrowser;
        private bool _devToolsOsrOpening;
        private Control _devToolsOsrAttachedTarget;

        private static readonly HttpClient _devToolsOsrHttpClient = new HttpClient();

        private sealed class DevToolsTarget
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }
        }

        public async void ShowDevToolOSR(Control target = null)
        {
            if (_devToolsOsrBrowser != null || _devToolsOsrOpening)
            {
                return;
            }

            _devToolsOsrOpening = true;
            try
            {
                var url = "devtools://devtools/bundled/inspector.html";
                var port = CefRuntimeLoader.RemoteDebuggingPort;

                if (port > 0)
                {
                    try
                    {
                        var json = await _devToolsOsrHttpClient.GetStringAsync($"http://127.0.0.1:{port}/json/list");
                        var targets = JsonSerializer.Deserialize<List<DevToolsTarget>>(json);
                        var devToolsTarget = targets?.FirstOrDefault(t => t.Url == Address) ?? targets?.FirstOrDefault();

                        if (devToolsTarget != null)
                        {
                            url = $"devtools://devtools/bundled/inspector.html?ws=127.0.0.1:{port}/devtools/page/{devToolsTarget.Id}";
                        }
                    }
                    catch (HttpRequestException)
                    {
                    }
                }

                _devToolsOsrBrowser = new CefGlueBrowser();
                _devToolsOsrBrowser.Control.Dock = DockStyle.Fill;

                _devToolsOsrAttachedTarget = target;
                target?.Controls.Add(_devToolsOsrBrowser.Control);

                DevToolsOpened?.Invoke(_devToolsOsrBrowser.Control);

                _devToolsOsrBrowser.Address = url;
            }
            finally
            {
                _devToolsOsrOpening = false;
            }
        }

        public void CloseDevToolOSR()
        {
            if (_devToolsOsrBrowser == null)
            {
                return;
            }

            _devToolsOsrAttachedTarget?.Controls.Remove(_devToolsOsrBrowser.Control);
            _devToolsOsrAttachedTarget = null;

            _devToolsOsrBrowser.Dispose();
            _devToolsOsrBrowser = null;

            DevToolsClosed?.Invoke();
        }
    }

    internal class PopupLifeSpanHandler : LifeSpanHandler
    {
        private readonly CefGlueBrowser _opener;
        private readonly CefGlueBrowser _self;
        private readonly Size _rect;

        public PopupLifeSpanHandler(CefGlueBrowser opener, CefGlueBrowser self, Size rect)
        {
            _opener = opener;
            _self = self;
            _rect = rect;
        }

        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl,
            string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture,
            CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client,
            CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
        {
            if (targetDisposition != CefWindowOpenDisposition.NewPopup)
            {
                _self.Address = targetUrl;
                return true;
            }

            CefClient outClient = null;
            CefDictionaryValue outExtra = null;
            var parentForm = _self.Control.FindForm();
            parentForm.Invoke(() =>
            {
                _self.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });
            client = outClient;
            extraInfo = outExtra;
            return false;
        }

        protected override void OnAfterCreated(CefBrowser browser)
        {
            _opener.PromotePopup(browser.Identifier, _self);

            var parentForm = _opener.Control.FindForm();
            parentForm.Invoke(() =>
            {
                var f = new Form { Owner = parentForm, Size = _rect.IsEmpty ? new Size(808, 600) : _rect };
                f.CreateControl();
                f.Controls.Add(_self.Control);

                if (_self.Control is BitmapControl bitmapControl)
                {
                    bitmapControl.WireParentResizeEvents();
                }

                f.FormClosing += (o, e) => _self.CloseBrowser();
                f.Show();
                f.Activate();
            });
        }

        protected override void OnBeforeClose(CefBrowser browser) => _opener.RemovePopupByIdentifier(browser.Identifier);

        protected override void OnBeforePopupAborted(CefBrowser browser, int popupId) => _opener.AbortPendingPopup(_self);
    }

    internal sealed class NullPopupHost : IOffScreenPopupHost
    {
        public OffScreenRenderSurface RenderSurface => null;
        public int Width => 0;
        public int Height => 0;
        public int OffsetX => 0;
        public int OffsetY => 0;

        public void Open() { }
        public void Close() { }
        public void MoveAndResize(int x, int y, int width, int height) { }

        public event Action GotFocus;
        public event Action<CefSize> SizeChanged;
        public event Action LostFocus;
        public event Xilium.CefGlue.Common.Platform.KeyEventHandler KeyDown;
        public event Xilium.CefGlue.Common.Platform.KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        public event Action<string, int> CompositionUpdated;
        public event Action<string> CompositionCommitted;
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

        public void Focus() { }
        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor) => point;
        public void SetFullscreen(bool fullscreen) { }
        public void ReleaseMouseCapture() { }
        public Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y) => Task.FromResult(CefDragOperationsMask.None);
        public void UpdateDragCursor(CefDragOperationsMask allowedOps) { }
        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight) => null;
        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback) => callback.Cancel();
        public void CloseContextMenu() { }
        public void InitializeRender(IntPtr browserHandle) { }
        public void DestroyRender() { }
        public bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType) => false;
    }

    internal class PopupDisplayHandler : DisplayHandler
    {
        private readonly CefGlueBrowser _target;

        public PopupDisplayHandler(CefGlueBrowser targetForm)
        {
            _target= targetForm;

        }

        protected override void OnTitleChange(CefBrowser browser, string title)
        {
            var f = _target.Control.FindForm();
            if (f != null)
            {
                f.Invoke(()=>   f.Text = title);
            }
        }
    }

}
