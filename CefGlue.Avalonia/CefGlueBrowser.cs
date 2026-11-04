using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia.Common;
using Xilium.CefGlue.Avalonia.Platform;
using Xilium.CefGlue.Avalonia.Platform.Linux;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia
{
    public class CefGlueBrowser : BaseCefBrowser
    {
        private readonly Grid _rootGrid;
        private BaseCefControl _activeControl;
        private Func<CefWindowInfo> _setupCefWindowInfo;
        private Func<CefBrowserSettings> _setupCefBrowserSettings;

        public CefGlueBrowser() : this(null) { }

        public CefGlueBrowser(Func<CefRequestContext> cefRequestContextFactory = null,
                              Func<CefWindowInfo> setupCefWindowInfo = null,
                              Func<CefBrowserSettings> setupCefBrowserSettings = null,
                              CefGlueHost host = null,
                              bool useTextureControl = false)
            : base(cefRequestContextFactory, host)
        {
            _setupCefWindowInfo = setupCefWindowInfo;
            _setupCefBrowserSettings = setupCefBrowserSettings;

            _rootGrid = new Grid();

            var windowInfo = setupCefWindowInfo?.Invoke();
            var control = ResolveControl(windowInfo, useTextureControl);

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

            RenderMode = control is TextureControl or GlTextureControl ? OsrRenderMode.Texture
                : windowInfo?.SharedTextureEnabled == true && _textureFallbackReason == null ? OsrRenderMode.BitmapReadback
                : OsrRenderMode.Bitmap;
            RenderModeReason = _textureFallbackReason;

            AttachTarget(control);
        }

        /// <summary>
        /// When true (default), a browser asking for shared-texture rendering (Texture or BitmapReadback) falls back
        /// to plain Bitmap if the GPU can't do it, instead of rendering nothing. Read when each browser is created.
        /// </summary>
        public static bool AllowTextureFallback { get; set; } = true;

        /// <summary>How this browser's frames currently reach the screen.</summary>
        public OsrRenderMode RenderMode { get; private set; }

        /// <summary>Why <see cref="RenderMode"/> isn't what was asked for; null when it is.</summary>
        public string RenderModeReason { get; private set; }

        /// <summary>
        /// Raised on the UI thread when the render mode changes after creation (Texture → TextureDegraded, or
        /// BitmapReadback → Bitmap when CEF turns out to paint in software).
        /// A fallback decided at creation is only visible through <see cref="RenderMode"/>.
        /// </summary>
        public event Action<OsrRenderMode, string> RenderModeChanged;

        private string _textureFallbackReason;

        private void OnTextureSurfaceDegraded(AvaloniaBitmapSurface cpuFrames, string reason)
        {
            _logger.Warn($"TextureControl degraded to CPU rendering: {reason}");

            Dispatcher.UIThread.Post(() =>
            {
                RenderMode = OsrRenderMode.TextureDegraded;
                RenderModeReason = reason;
                RenderModeChanged?.Invoke(RenderMode, reason);
            });
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

            _activeControl = target;

            _rootGrid.Children.Add(_activeControl);

            if (target is TextureControl textureControlWithOverlay)
            {
                _rootGrid.Children.Add(textureControlWithOverlay.FpsOverlay);
            }
            else if (target is GlTextureControl glTextureControlWithOverlay)
            {
                _rootGrid.Children.Add(glTextureControlWithOverlay.FpsOverlay);
                glTextureControlWithOverlay.Failed += reason => _logger.Error($"GL texture rendering can't show frames: {reason}");
            }

            base.AttachTarget(CreateOffScreenControlHost());

            Adapter.SetupCefWindowInfo = _setupCefWindowInfo;
            Adapter.SetupCefBrowserSettings = _setupCefBrowserSettings;
            Adapter.Initialized += () =>
            {
                target.AttachBrowser(UnderlyingBrowser);
            };

            return target;
        }

        /// <summary>
        /// Avalonia has three modes: SharedTextureEnabled off → Bitmap (OnPaint); on → BitmapReadback
        /// (GPU frame read back into a bitmap, Windows only); on + <paramref name="useTextureControl"/> → Texture
        /// (zero-copy: D3D11 on Windows, EGL dma-buf on Linux). The probe guards both shared-texture modes; when it
        /// fails the browser drops to plain Bitmap instead of rendering nothing.
        /// </summary>
        protected virtual BaseCefControl ResolveControl(CefWindowInfo info, bool useTextureControl)
        {
            if (info?.SharedTextureEnabled == true)
            {
                var unsupportedReason = AllowTextureFallback ? SharedTextureSupport.GetUnsupportedReason() : null;
                if (unsupportedReason == null && AllowTextureFallback && !useTextureControl && !OperatingSystem.IsWindows())
                {
                    unsupportedReason = "Reading shared textures back to the CPU is only implemented on Windows (use UseTextureControl).";
                }
#if DEBUG
                // Test hook for the runtime fallback: start on TextureControl even when the probe would refuse it.
                if (Environment.GetEnvironmentVariable("CEFGLUE_DEBUG_SKIP_TEXTURE_PROBE")?.Trim() == "1")
                {
                    unsupportedReason = null;
                }
#endif
                if (unsupportedReason != null)
                {
                    _textureFallbackReason = unsupportedReason;
                }
                else if (useTextureControl)
                {
                    return OperatingSystem.IsLinux()
                        ? new GlTextureControl { UseSafeResize = true }
                        : new TextureControl { UseSafeResize = true };
                }
            }

            return new BitmapControl { UseSafeResize = true };
        }

        public Control Control => _rootGrid;

        public bool UseOSRDirtyRect { get; set; } = true;

        private AvaloniaBitmapSurface _surface;
        private AvaloniaTextureRenderSurface _textureSurface;
        private AvaloniaGlTextureRenderSurface _glTextureSurface;
        private bool _showOverlayInfo = false;

        public bool ShowOverlayInfo
        {
            get => _surface?.ShowOverlayInfo ?? _textureSurface?.ShowOverlayInfo ?? _glTextureSurface?.ShowOverlayInfo ?? _showOverlayInfo;
            set
            {
                _showOverlayInfo = value;
                if (_surface != null)
                {
                    _surface.ShowOverlayInfo = value;
                    (_activeControl as BitmapControl)?.RefreshOverlay();
                }
                else if (_textureSurface != null)
                {
                    _textureSurface.ShowOverlayInfo = value;
                    (_activeControl as TextureControl)?.RefreshOverlay();
                }
                else if (_glTextureSurface != null)
                {
                    _glTextureSurface.ShowOverlayInfo = value;
                    (_activeControl as GlTextureControl)?.RefreshOverlay();
                }
            }
        }

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            if (_activeControl is TextureControl textureControl)
            {
                var textureHost = new OffScreenControlHost<TextureControl, IntPtr>(textureControl);
                _textureSurface = new AvaloniaTextureRenderSurface
                {
                    UseOSRDirtyRect = UseOSRDirtyRect,
                    ShowOverlayInfo = _showOverlayInfo,
                    AllowCpuFallback = AllowTextureFallback,
                };
                _textureSurface.Degraded += OnTextureSurfaceDegraded;
                textureHost.AttachRenderSurface(_textureSurface);
                textureControl.AttachRenderSurface(_textureSurface);
                return textureHost;
            }

            if (_activeControl is GlTextureControl glTextureControl)
            {
                var glTextureHost = new OffScreenControlHost<GlTextureControl, IntPtr>(glTextureControl);
                _glTextureSurface = new AvaloniaGlTextureRenderSurface
                {
                    UseOSRDirtyRect = UseOSRDirtyRect,
                    ShowOverlayInfo = _showOverlayInfo,
                    AllowCpuFallback = AllowTextureFallback,
                };
                _glTextureSurface.Degraded += OnTextureSurfaceDegraded;
                glTextureHost.AttachRenderSurface(_glTextureSurface);
                glTextureControl.AttachRenderSurface(_glTextureSurface);
                return glTextureHost;
            }

            var bitmapHost = new OffScreenControlHost<BitmapControl, byte[]>((BitmapControl)_activeControl);
            _surface = new AvaloniaBitmapSurface { UseOSRDirtyRect = UseOSRDirtyRect, ShowOverlayInfo = _showOverlayInfo };
            bitmapHost.AttachSurface(_surface);

            if (RenderMode == OsrRenderMode.BitmapReadback)
            {
                // The probe can't see Chromium's GPU blocklist (e.g. VMware SVGA 3D passes our D3D11 check, yet CEF
                // composites in software). Rendering still works - BitmapControl takes OnPaint too - but report it.
                var renderSurface = bitmapHost.RenderSurface;
                void OnFrameAccepted(int width, int height)
                {
                    if (renderSurface.IsAccelerated)
                    {
                        return;
                    }

                    renderSurface.FrameAccepted -= OnFrameAccepted;
                    const string Reason = "CEF is painting in software (OnPaint): GPU compositing is unavailable or blocklisted.";
                    _logger.Warn($"Shared textures requested but not used: {Reason}");

                    Dispatcher.UIThread.Post(() =>
                    {
                        RenderMode = OsrRenderMode.Bitmap;
                        RenderModeReason = Reason;
                        RenderModeChanged?.Invoke(RenderMode, Reason);
                    });
                }
                renderSurface.FrameAccepted += OnFrameAccepted;
            }

            return bitmapHost;
        }

        protected internal override IOffScreenPopupHost CreatePopupHost()
        {
            return new PopupControlHost(_rootGrid, _activeControl);
        }

        public void CloseBrowser()
        {
            UnderlyingBrowser?.GetHost()?.CloseBrowser(true);
        }

        public void OverrideDeviceScaleFactor(float deviceScaleFactor)
        {
            (Adapter as CefGlueOffScreenAdapter)?.OverrideDeviceScaleFactor(deviceScaleFactor);
        }

        public event Action<Control> DevToolsOpened;
        public event Action DevToolsClosed;

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

        public void Focus()
        {
            _activeControl.FocusContent();
            UnderlyingBrowser?.GetHost()?.SetFocus(true);
        }

        public record PopupWindowInfo(CefGlueBrowser Browser, global::Avalonia.Size PopupSize, CefWindowOpenDisposition TargetDisposition);

        private readonly List<PopupWindowInfo> _popupCollection = new List<PopupWindowInfo>();

        public void CreatePopupBrowser(ref CefClient popUpClient,
                                        ref CefDictionaryValue popUpDictionary,
                                        CefWindowInfo windowInfo,
                                        CefPopupFeatures popupFeatures,
                                        CefWindowOpenDisposition targetDisposition)
        {
            var popupChild = new CefGlueBrowser(host: Host);

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild, this);

            // WHERE the popup shows is the app's choice (IPopupPresenter, default: an owned Window); its
            // lifecycle stays here, in PopupLifeSpanHandler - same as CefGlue.WinForms/WinUI.
            var presenter = Host.TryCreateInstance<IPopupPresenter<CefGlueBrowser>>() ?? new WindowPopupPresenter();
            var request = new PopupRequest<CefGlueBrowser>(this, popupChild, popupFeatures);
            popupChild.LifeSpanHandler = new PopupLifeSpanHandler(this, popupChild, presenter, request);

            var size = (popupFeatures.Width == null || popupFeatures.Height == null)
                ? new global::Avalonia.Size()
                : new global::Avalonia.Size(popupFeatures.Width.Value, popupFeatures.Height.Value);

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

        /// <summary>Forgets and disposes a popup this browser opened (closed, or its creation aborted).</summary>
        internal void DisposePopup(CefGlueBrowser popup)
        {
            if (_popupCollection.RemoveAll(entry => ReferenceEquals(entry.Browser, popup)) > 0)
            {
                popup.Dispose();
            }
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
    }

    /// <summary>
    /// Lifecycle of a window.open popup: shows it through the <see cref="IPopupPresenter{TBrowser}"/>, closes the
    /// browser when its UI goes away, and handles the page closing itself (window.close) without crashing
    /// libcef - mirrors CefGlue.WinForms' and CefGlue.WinUI's PopupLifeSpanHandler.
    /// </summary>
    internal class PopupLifeSpanHandler : LifeSpanHandler
    {
        private readonly CefGlueBrowser _opener;
        private readonly CefGlueBrowser _self;
        private readonly IPopupPresenter<CefGlueBrowser> _presenter;
        private readonly PopupRequest<CefGlueBrowser> _request;
        private IPopupHost _host;
        private volatile bool _closing;
        private volatile bool _closed;
        private bool _hostGone;

        public PopupLifeSpanHandler(CefGlueBrowser opener, CefGlueBrowser self, IPopupPresenter<CefGlueBrowser> presenter, PopupRequest<CefGlueBrowser> request)
        {
            _opener = opener;
            _self = self;
            _presenter = presenter;
            _request = request;
        }

        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl,
            string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture,
            CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client,
            CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
        {
            if (targetDisposition != CefWindowOpenDisposition.NewPopup)
            {
                Dispatcher.UIThread.Post(() => _self.Address = targetUrl);
                return true;
            }

            // A popup opening its own popup - goes through the same presenter.
            CefClient outClient = null;
            CefDictionaryValue outExtra = null;
            Dispatcher.UIThread.Invoke(() =>
            {
                _self.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });
            client = outClient;
            extraInfo = outExtra;
            return false;
        }

        protected override void OnAfterCreated(CefBrowser browser)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var host = _presenter.Show(_request);
                _host = host;
                if (host != null)
                {
                    host.Closed += OnHostClosed;
                }
            });
        }

        // UI thread: the popup's UI is gone (the user closed it, or after DoClose asked the host to close).
        private void OnHostClosed()
        {
            if (_hostGone)
            {
                return;
            }

            _hostGone = true;
            if (!_closed)
            {
                // Our own close: DoClose lets this one through.
                _closing = true;
                _self.CloseBrowser();
                Dispatcher.UIThread.Post(() => _opener.Focus());
            }
        }

        protected override bool DoClose(CefBrowser browser)
        {
            if (_closing)
            {
                return false;
            }

            // The page closed itself (window.close, e.g. a failed "Sign in with Google" popup). Letting CEF
            // tear the browser down while its UI still hosts it crashes libcef, so cancel CEF's close and
            // close the UI instead - its Closed event closes the browser again, which then goes through.
            // Same approach as CAS's HeadlessPopupLifeSpanHandler.DoClose.
            _closing = true;
            var host = _host;
            if (host == null)
            {
                return false;
            }

            Dispatcher.UIThread.Post(() => host.Close());
            return true;
        }

        // CEF is still inside its close sequence in these two: remove the UI (if CEF closed the browser some
        // other way) and dispose afterwards, on the UI thread.
        protected override void OnBeforeClose(CefBrowser browser)
        {
            _closed = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_hostGone)
                {
                    _hostGone = true;
                    _host?.Close();
                }
                _opener.DisposePopup(_self);
            });
        }

        protected override void OnBeforePopupAborted(CefBrowser browser, int popupId)
            => Dispatcher.UIThread.Post(() => _opener.DisposePopup(_self));
    }

    internal class PopupDisplayHandler : DisplayHandler
    {
        private readonly CefGlueBrowser _target;
        private readonly CefGlueBrowser _opener;

        public PopupDisplayHandler(CefGlueBrowser target, CefGlueBrowser opener)
        {
            _target = target;
            _opener = opener;
        }

        protected override void OnTitleChange(CefBrowser browser, string title)
        {
            var window = TopLevel.GetTopLevel(_target.Control) as Window;
            // Only a window of the popup's own: a presenter that shows popups in a tab keeps the main title.
            if (window != null && window != TopLevel.GetTopLevel(_opener.Control))
            {
                Dispatcher.UIThread.Post(() => window.Title = title);
            }
        }
    }
}
