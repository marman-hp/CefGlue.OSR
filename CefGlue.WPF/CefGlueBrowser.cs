using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WPF.Common;
using Xilium.CefGlue.WPF.Platform;

namespace Xilium.CefGlue.WPF
{
    public class CefGlueBrowser : BaseCefBrowser
    {
        private BaseCefControl _activeControl;
        private Func<CefWindowInfo> _setupCefWindowInfo;
        private Func<CefBrowserSettings> _setupCefBrowserSettings;

        public CefGlueBrowser() : this(null) { }

        public CefGlueBrowser(Func<CefRequestContext> cefRequestContextFactory = null,
                              Func<CefWindowInfo> setupCefWindowInfo = null,
                              Func<CefBrowserSettings> setupCefBrowserSettings = null,
                              CefGlueHost host = null)
            : base(cefRequestContextFactory, host)
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
        /// <see cref="OsrRenderMode.BitmapReadback"/> (WPF has no zero-copy texture control).
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

            _activeControl = target;
            KeyboardNavigation.SetAcceptsReturn(_activeControl, true);

            base.AttachTarget(CreateOffScreenControlHost());

            Adapter.SetupCefWindowInfo = _setupCefWindowInfo;
            Adapter.SetupCefBrowserSettings = _setupCefBrowserSettings;
            Adapter.Initialized += () =>
            {
                target.AttachBrowser(UnderlyingBrowser);
            };

            return target;
        }

        protected virtual BaseCefControl ResolveControl()
        {
            var bitmapControl = new BitmapControl();

            bitmapControl.UseSafeResize = true;
            return bitmapControl;
        }

        public FrameworkElement Control => _activeControl;

        public void Focus()
        {
            _activeControl.Focus();
            UnderlyingBrowser?.GetHost()?.SetFocus(true);
        }

        public void OverrideDeviceScaleFactor(float deviceScaleFactor)
        {
            (Adapter as CefGlueOffScreenAdapter)?.OverrideDeviceScaleFactor(deviceScaleFactor);
        }

        public bool UseOSRDirtyRect { get; set; } = true;

        private WpfBitmapSurface _surface;
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

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            var host = new OffScreenControlHost<BitmapControl, byte[]>((BitmapControl)_activeControl);
            _surface = new WpfBitmapSurface { UseOSRDirtyRect = UseOSRDirtyRect, ShowOverlayInfo = _showOverlayInfo };
            host.AttachSurface(_surface);

            if (RenderMode == OsrRenderMode.BitmapReadback)
            {
                // The probe can't see Chromium's GPU blocklist (e.g. VMware SVGA 3D passes our D3D11 check, yet CEF
                // composites in software). Rendering still works - BitmapControl takes OnPaint too - but report it.
                var renderSurface = host.RenderSurface;
                var dispatcher = _activeControl.Dispatcher;
                void OnFrameAccepted(int width, int height)
                {
                    if (renderSurface.IsAccelerated)
                    {
                        return;
                    }

                    renderSurface.FrameAccepted -= OnFrameAccepted;
                    const string Reason = "CEF is painting in software (OnPaint): GPU compositing is unavailable or blocklisted.";
                    _logger.Warn($"Shared textures requested but not used: {Reason}");

                    dispatcher.BeginInvoke((Action)(() =>
                    {
                        RenderMode = OsrRenderMode.Bitmap;
                        RenderModeReason = Reason;
                        RenderModeChanged?.Invoke(RenderMode, Reason);
                    }));
                }
                renderSurface.FrameAccepted += OnFrameAccepted;
            }

            return host;
        }

        protected internal override IOffScreenPopupHost CreatePopupHost()
        {
            return new PopupControlHost((BitmapControl)_activeControl);
        }

        public void CloseBrowser()
        {
            UnderlyingBrowser?.GetHost()?.CloseBrowser(true);
        }

        public record PopupWindowInfo(CefGlueBrowser Browser, Size PopupSize, CefWindowOpenDisposition TargetDisposition);

        private readonly List<PopupWindowInfo> _popupCollection = new List<PopupWindowInfo>();

        public void CreatePopupBrowser(ref CefClient popUpClient,
                                        ref CefDictionaryValue popUpDictionary,
                                        CefWindowInfo windowInfo,
                                        CefPopupFeatures popupFeatures,
                                        CefWindowOpenDisposition targetDisposition)
        {
            var popupChild = new CefGlueBrowser();

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild);
            popupChild.LifeSpanHandler = LifeSpanHandler;

            var size = (popupFeatures.Width == null || popupFeatures.Height == null)
                ? new Size()
                : new Size(popupFeatures.Width.Value, popupFeatures.Height.Value);

            _popupCollection.Add(new PopupWindowInfo(popupChild, size, targetDisposition));
        }

        public PopupWindowInfo GetPopup(CefBrowser browser)
        {
            if (browser == null)
            {
                return null;
            }

            return _popupCollection.Find(entry => entry.Browser.UnderlyingBrowser?.Identifier == browser.Identifier);
        }

        public void RemovePopup(CefBrowser browser)
        {
            var item = _popupCollection.SingleOrDefault(entry => entry.Browser.UnderlyingBrowser?.Identifier == browser.Identifier);
            if (item != null)
            {
                _popupCollection.Remove(item);
                item.Browser?.Dispose();
            }
        }

        private CefBrowser _devToolsCefBrowser;

        public event Action<FrameworkElement> DevToolsOpened;

        public event Action DevToolsClosed;

        public void SetupDevToolsWindowed(CefWindowInfo windowInfo, ref CefClient client)
        {
            var parentWindow = Window.GetWindow(Control);
            var parentHandle = parentWindow != null ? new WindowInteropHelper(parentWindow).Handle : IntPtr.Zero;

            windowInfo.SetAsChild(parentHandle, new CefRectangle(0, 0, (int)Control.ActualWidth, (int)Control.ActualHeight));

            var lifeSpanHandler = new DevToolsLifeSpanHandler(HandleDevToolsBrowserCreated, HandleDevToolsBrowserClosed);
            client = new DevToolsCefClient(lifeSpanHandler);
        }

        private void HandleDevToolsBrowserCreated(CefBrowser cefBrowser)
        {
            _devToolsCefBrowser = cefBrowser;

            var hwnd = cefBrowser.GetHost().GetWindowHandle();

            Control.Dispatcher.Invoke(() =>
            {
                var host = new ExtendedWpfNativeControlHost(hwnd);
                DevToolsOpened?.Invoke(host);
            });
        }

        private void HandleDevToolsBrowserClosed(CefBrowser cefBrowser)
        {
            if (_devToolsCefBrowser?.Identifier != cefBrowser.Identifier)
            {
                return;
            }

            _devToolsCefBrowser = null;
            try
            {
                Control.Dispatcher.Invoke(() => DevToolsClosed?.Invoke());
            }
            catch {}
        }

        private CefGlueBrowser _devToolsOsrBrowser;
        private bool _devToolsOsrOpening;
        private Panel _devToolsOsrAttachedTarget;

        private static readonly HttpClient _devToolsOsrHttpClient = new HttpClient();

        private sealed class DevToolsTarget
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }
        }

        public async void ShowDevToolOSR(Panel target = null)
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
                _devToolsOsrBrowser.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
                _devToolsOsrBrowser.Control.VerticalAlignment = VerticalAlignment.Stretch;
                _devToolsOsrBrowser.Address = url;

                _devToolsOsrAttachedTarget = target;
                target?.Children.Add(_devToolsOsrBrowser.Control);

                DevToolsOpened?.Invoke(_devToolsOsrBrowser.Control);
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

            _devToolsOsrAttachedTarget?.Children.Remove(_devToolsOsrBrowser.Control);
            _devToolsOsrAttachedTarget = null;

            _devToolsOsrBrowser.Dispose();
            _devToolsOsrBrowser = null;

            DevToolsClosed?.Invoke();
        }
    }

    internal sealed class DevToolsCefClient : CefClient
    {
        private readonly CefLifeSpanHandler _lifeSpanHandler;

        public DevToolsCefClient(CefLifeSpanHandler lifeSpanHandler)
        {
            _lifeSpanHandler = lifeSpanHandler;
        }

        protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
    }

    internal sealed class DevToolsLifeSpanHandler : CefLifeSpanHandler
    {
        private readonly Action<CefBrowser> _onCreated;
        private readonly Action<CefBrowser> _onClosed;

        public DevToolsLifeSpanHandler(Action<CefBrowser> onCreated, Action<CefBrowser> onClosed)
        {
            _onCreated = onCreated;
            _onClosed = onClosed;
        }

        protected override void OnAfterCreated(CefBrowser browser) => _onCreated(browser);

        protected override void OnBeforeClose(CefBrowser browser) => _onClosed(browser);
    }

    internal class PopupDisplayHandler : DisplayHandler
    {
        private readonly CefGlueBrowser _target;

        public PopupDisplayHandler(CefGlueBrowser target)
        {
            _target = target;
        }

        protected override void OnTitleChange(CefBrowser browser, string title)
        {
            _target.Control.Dispatcher.Invoke(() =>
            {
                var window = Window.GetWindow(_target.Control);
                if (window != null)
                {
                    window.Title = title;
                }
            });
        }
    }
}
