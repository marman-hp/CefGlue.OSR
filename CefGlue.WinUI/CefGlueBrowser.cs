using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinUI.Common;
using Xilium.CefGlue.WinUI.Platform;

namespace Xilium.CefGlue.WinUI
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
                              CefGlueHost host = null)
            : base(cefRequestContextFactory, host)
        {
            _setupCefWindowInfo = setupCefWindowInfo;
            _setupCefBrowserSettings = setupCefBrowserSettings;

            _rootGrid = new Grid();

            var windowInfo = setupCefWindowInfo?.Invoke();
            var control = ResolveControl(windowInfo);

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
                _logger.Warn($"TextureControl not supported, using BitmapControl: {_textureFallbackReason}");
            }

            // BitmapReadback only when a subclass's ResolveControl picks BitmapControl with shared textures on.
            RenderMode = control is TextureControl ? OsrRenderMode.Texture
                : windowInfo?.SharedTextureEnabled == true && _textureFallbackReason == null ? OsrRenderMode.BitmapReadback
                : OsrRenderMode.Bitmap;
            RenderModeReason = _textureFallbackReason;

            AttachTarget(control);
        }

        /// <summary>
        /// When true (default), a browser asking for shared-texture rendering falls back to the bitmap path
        /// if the GPU can't do it, instead of rendering nothing. Read when each browser is created.
        /// </summary>
        public static bool AllowTextureFallback { get; set; } = true;

        /// <summary>How this browser's frames currently reach the screen.</summary>
        public OsrRenderMode RenderMode { get; private set; }

        /// <summary>Why <see cref="RenderMode"/> isn't what was asked for; null when it is.</summary>
        public string RenderModeReason { get; private set; }

        /// <summary>
        /// Raised on the UI thread when the render mode changes after creation (Texture → TextureDegraded).
        /// A fallback decided at creation is only visible through <see cref="RenderMode"/>.
        /// </summary>
        public event Action<OsrRenderMode, string> RenderModeChanged;

        private void OnTextureSurfaceDegraded(WinUIBitmapSurface cpuFrames, string reason)
        {
            _logger.Warn($"TextureControl degraded to CPU rendering: {reason}");

            _rootGrid.DispatcherQueue.TryEnqueue(() =>
            {
                RenderMode = OsrRenderMode.TextureDegraded;
                RenderModeReason = reason;
                RenderModeChanged?.Invoke(RenderMode, reason);
            });
        }

        private string _textureFallbackReason;

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

            base.AttachTarget(CreateOffScreenControlHost());

            Adapter.SetupCefWindowInfo = _setupCefWindowInfo;
            Adapter.SetupCefBrowserSettings = _setupCefBrowserSettings;
            Adapter.Initialized += () => target.AttachBrowser(UnderlyingBrowser);

            return target;
        }

        protected virtual BaseCefControl ResolveControl(CefWindowInfo windowInfo)
        {
            if (windowInfo?.SharedTextureEnabled == true)
            {
                var unsupportedReason = AllowTextureFallback ? SharedTextureSupport.GetUnsupportedReason() : null;
#if DEBUG
                // Test hook for the runtime fallback: start on TextureControl even when the probe would refuse it.
                if (Environment.GetEnvironmentVariable("CEFGLUE_DEBUG_SKIP_TEXTURE_PROBE")?.Trim() == "1")
                {
                    unsupportedReason = null;
                }
#endif
                if (unsupportedReason == null)
                {
                    return new TextureControl { UseSafeResize = true };
                }

                _textureFallbackReason = unsupportedReason;
            }

            return new BitmapControl { UseSafeResize = true };
        }

        public FrameworkElement Control => _rootGrid;

        public Window HostWindow
        {
            get => _activeControl.HostWindow;
            set => _activeControl.HostWindow = value;
        }

        public void Focus()
        {
            _activeControl.Focus(FocusState.Programmatic);
            UnderlyingBrowser?.GetHost()?.SetFocus(true);
        }

        public void OverrideDeviceScaleFactor(float deviceScaleFactor)
        {
            (Adapter as CefGlueOffScreenAdapter)?.OverrideDeviceScaleFactor(deviceScaleFactor);
        }

        public bool UseOSRDirtyRect { get; set; } = true;

        private WinUIBitmapSurface _surface;
        private WinUITextureRenderSurface _textureSurface;
        private bool _showOverlayInfo = false;

        public bool ShowOverlayInfo
        {
            get => _surface?.ShowOverlayInfo ?? _textureSurface?.ShowOverlayInfo ?? _showOverlayInfo;
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
            }
        }

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            if (_activeControl is TextureControl textureControl)
            {
                var textureHost = new OffScreenControlHost<TextureControl, IntPtr>(textureControl);
                _textureSurface = new WinUITextureRenderSurface
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

            var bitmapHost = new OffScreenControlHost<BitmapControl, byte[]>((BitmapControl)_activeControl);
            _surface = new WinUIBitmapSurface { UseOSRDirtyRect = UseOSRDirtyRect, ShowOverlayInfo = _showOverlayInfo };
            bitmapHost.AttachSurface(_surface);
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

        public event Action<FrameworkElement> DevToolsOpened;
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

                _devToolsOsrBrowser = new CefGlueBrowser
                {
                    HostWindow = HostWindow,
                };
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

        public record PopupWindowInfo(CefGlueBrowser Browser, Windows.Foundation.Size PopupSize, CefWindowOpenDisposition TargetDisposition);

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

            var size = (popupFeatures.Width == null || popupFeatures.Height == null)
                ? new Windows.Foundation.Size()
                : new Windows.Foundation.Size(popupFeatures.Width.Value, popupFeatures.Height.Value);

            // WHERE the popup shows is the app's choice (IPopupPresenter, default: an owned Window); its
            // lifecycle stays here, in PopupLifeSpanHandler - same as CefGlue.WinForms' popups.
            var presenter = Host.TryCreateInstance<IPopupPresenter<CefGlueBrowser>>() ?? new WindowPopupPresenter();
            var request = new PopupRequest<CefGlueBrowser>(this, popupChild, popupFeatures);
            popupChild.LifeSpanHandler = new PopupLifeSpanHandler(this, popupChild, presenter, request);

            _popupCollection.Add(new PopupWindowInfo(popupChild, size, targetDisposition));
        }

        /// <summary>Forgets and disposes a popup this browser opened (closed, or its creation aborted).</summary>
        internal void DisposePopup(CefGlueBrowser popup)
        {
            if (_popupCollection.RemoveAll(entry => ReferenceEquals(entry.Browser, popup)) > 0)
            {
                popup.Dispose();
            }
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
    }

    /// <summary>
    /// Lifecycle of a window.open popup: shows it through the <see cref="IPopupPresenter{TBrowser}"/>, closes the
    /// browser when its UI goes away, and handles the page closing itself (window.close) without crashing
    /// libcef - mirrors CefGlue.WinForms' PopupLifeSpanHandler.
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
                _self.Control.DispatcherQueue.TryEnqueue(() => _self.Address = targetUrl);
                return true;
            }

            // A popup opening its own popup - goes through the same presenter.
            CefClient outClient = null;
            CefDictionaryValue outExtra = null;
            InvokeSync(_self.Control.DispatcherQueue, () =>
            {
                _self.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });
            client = outClient;
            extraInfo = outExtra;
            return false;
        }

        protected override void OnAfterCreated(CefBrowser browser)
        {
            _opener.Control.DispatcherQueue.TryEnqueue(() =>
            {
                // A presenter that makes its own window sets HostWindow to it; one that shows the popup
                // inside the opener's window (a tab) keeps this.
                _self.HostWindow = _opener.HostWindow;

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
                RefocusOpener();
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
            return host != null && _opener.Control.DispatcherQueue.TryEnqueue(() => host.Close());
        }

        // CEF is still inside its close sequence in these two: remove the UI (if CEF closed the browser some
        // other way) and dispose afterwards, on the UI thread.
        protected override void OnBeforeClose(CefBrowser browser)
        {
            _closed = true;
            if (!_opener.Control.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_hostGone)
                    {
                        _hostGone = true;
                        _host?.Close();
                    }
                    _opener.DisposePopup(_self);
                }))
            {
                _opener.DisposePopup(_self);
            }
        }

        protected override void OnBeforePopupAborted(CefBrowser browser, int popupId)
        {
            if (!_opener.Control.DispatcherQueue.TryEnqueue(() => _opener.DisposePopup(_self)))
            {
                _opener.DisposePopup(_self);
            }
        }

        private void RefocusOpener()
        {
            var ownerWindow = _opener.HostWindow;
            if (ownerWindow != null && ownerWindow != _self.HostWindow)
            {
                void OnOwnerActivated(object sender, WindowActivatedEventArgs args)
                {
                    ownerWindow.Activated -= OnOwnerActivated;

                    if (args.WindowActivationState != WindowActivationState.Deactivated)
                    {
                        _opener.Focus();
                    }
                }

                ownerWindow.Activated += OnOwnerActivated;
            }

            _opener.Control.DispatcherQueue.TryEnqueue(() => _opener.Focus());
        }

        private static void InvokeSync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            using var completed = new System.Threading.ManualResetEventSlim(false);
            Exception exception = null;

            var enqueued = dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    completed.Set();
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue the popup creation on the UI thread.");
            }

            completed.Wait();

            if (exception != null)
            {
                throw exception;
            }
        }
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
            // Only a window of the popup's own: a presenter that shows popups in a tab keeps the main title.
            if (_target.HostWindow != null && _target.HostWindow != _opener.HostWindow)
            {
                _target.HostWindow.DispatcherQueue.TryEnqueue(() => _target.HostWindow.Title = title);
            }
        }
    }
}
