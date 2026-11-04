using Gtk;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.GTK.Common;
using Xilium.CefGlue.GTK.Platform;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// The GTK CEF browser, mirroring CefGlue.Avalonia's CefGlueBrowser - owns a Widget rather
    /// than being one (see BaseCefBrowser's own doc comment for why). OSR-only, like every
    /// CefGlue.OSR host.
    /// </summary>
    public class CefGlueBrowser : BaseCefBrowser
    {
        private readonly Overlay _overlay;
        private readonly BaseCefControl _activeControl;

        public CefGlueBrowser() : this(null) { }

        public CefGlueBrowser(
            System.Func<CefRequestContext> cefRequestContextFactory = null,
            System.Func<CefWindowInfo> setupCefWindowInfo = null,
            System.Func<CefBrowserSettings> setupCefBrowserSettings = null,
            bool useTextureControl = false,
            CefGlueHost host = null)
            : base(cefRequestContextFactory, host)
        {
            _uiContext = System.Threading.SynchronizationContext.Current;
            _uiThreadId = System.Environment.CurrentManagedThreadId;

            _activeControl = ResolveControl(setupCefWindowInfo, useTextureControl);
            _activeControl.UseSafeResize = true;

            _overlay = Overlay.New();
            _overlay.Child = _activeControl.Widget;

            const uint StyleProviderPriorityApplication = 600;
            var overlayCss = CssProvider.New();
            overlayCss.LoadFromString("overlay { background: transparent; }");
            _overlay.GetStyleContext().AddProvider(overlayCss, StyleProviderPriorityApplication);

            if (_activeControl is TextureControl textureControl)
            {
                _overlay.AddOverlay(textureControl.InfoOverlay);
            }

            BuildAdapter();

            Adapter.SetupCefWindowInfo = setupCefWindowInfo;
            Adapter.SetupCefBrowserSettings = setupCefBrowserSettings;
            Adapter.Initialized += () =>
            {
                _activeControl.AttachBrowser(UnderlyingBrowser);
            };
        }

        /// <summary>
        /// TextureControl (true GPU->GPU dma-buf-straight-to-Gtk.Picture passthrough,
        /// GtkTextureRenderSurface) is a CAUTIOUS OPT-IN, separate from SharedTextureEnabled -
        /// unlike CefGlue.WinUI (where SharedTextureEnabled alone is enough to pick TextureControl,
        /// because SwapChainPanel is a native D3D11 composition target with no fragile bridge to be
        /// cautious about), GTK's dma-buf import path is fresh (built this session) and the
        /// mmap-based GtkRenderSurface accelerated-CPU-readback path is what's actually proven
        /// stable across resize/focus-change/long-playback testing so far. SharedTextureEnabled
        /// alone still selects that proven path (BitmapControl) here - useTextureControl must be
        /// explicitly requested to try the new GPU-passthrough path instead. Mirrors
        /// CefGlue.Avalonia's ORIGINAL cautious-opt-in pattern for its own (since-removed)
        /// TextureControl attempt, referenced in WinUI's ResolveControl remarks.
        /// </summary>
        private static BaseCefControl ResolveControl(System.Func<CefWindowInfo> setupCefWindowInfo, bool useTextureControl)
        {
            if (useTextureControl)
            {
                var windowInfo = setupCefWindowInfo?.Invoke();
                if (windowInfo?.SharedTextureEnabled == true)
                {
                    return new TextureControl();
                }
            }

            return new BitmapControl();
        }

        /// <summary>The widget to place in the caller's own widget tree.</summary>
        public Widget Widget => _overlay;

        /// <summary>Gives keyboard focus to the page.</summary>
        public void Focus()
        {
            _activeControl.FocusContent();
            UnderlyingBrowser?.GetHost()?.SetFocus(true);
        }

        private GtkBitmapSurface _bitmapSurface;
        private GtkTextureRenderSurface _textureSurface;
        private bool _showOverlayInfo;

        /// <summary>FPS / diagnostics overlay (off by default, like the other hosts).</summary>
        public bool ShowOverlayInfo
        {
            get => _showOverlayInfo;
            set
            {
                _showOverlayInfo = value;
                if (_bitmapSurface != null)
                {
                    _bitmapSurface.ShowOverlayInfo = value;
                }
                if (_textureSurface != null)
                {
                    _textureSurface.ShowOverlayInfo = value;
                }

                UnderlyingBrowser?.GetHost()?.Invalidate(CefPaintElementType.View);
            }
        }

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            if (_activeControl is TextureControl textureControl)
            {
                var textureHost = new OffScreenControlHost<TextureControl, System.IntPtr>(textureControl);
                _textureSurface = new GtkTextureRenderSurface { ShowOverlayInfo = _showOverlayInfo };
                textureHost.AttachRenderSurface(_textureSurface);
                textureControl.AttachRenderSurface(_textureSurface);
                return textureHost;
            }

            var host = new OffScreenControlHost<BitmapControl, byte[]>((BitmapControl)_activeControl);
            _bitmapSurface = new GtkBitmapSurface { ShowOverlayInfo = _showOverlayInfo };
            host.AttachSurface(_bitmapSurface);
            return host;
        }

        /// <summary>
        /// No-host popup - see Platform/PopupControlHost.cs's own remarks. Composites
        /// straight into an overlay layer added as a Gtk.Overlay child of _overlay, instead of a
        /// second Gtk.Popover + second BitmapControl - eliminates the whole class of
        /// Wayland/xdg_popup-specific workarounds this method used to need (Autohide grab silently
        /// failing when Popup()/Popdown() run outside a live input event, outside-click dismissal
        /// depending on a SetFocus(false) side channel instead of GTK's own mechanism) along with
        /// the general host-vs-CEF focus-fighting problem every platform head's original popup
        /// design shared.
        /// </summary>
        protected internal override IOffScreenPopupHost CreatePopupHost()
        {
            return new PopupControlHost(_overlay, _activeControl);
        }

        private readonly System.Threading.SynchronizationContext _uiContext;
        private readonly int _uiThreadId;

        /// <summary>Closes the browser (e.g. when its window goes away).</summary>
        public void CloseBrowser()
        {
            UnderlyingBrowser?.GetHost()?.CloseBrowser(true);
        }

        internal void PostToUi(System.Action action)
        {
            if (_uiContext == null)
            {
                action();
                return;
            }

            _uiContext.Post(_ => action(), null);
        }

        /// <summary>Runs <paramref name="action"/> on the GTK main loop and waits for it (CEF callbacks need a result).</summary>
        internal void InvokeOnUi(System.Action action)
        {
            if (_uiContext == null || System.Environment.CurrentManagedThreadId == _uiThreadId)
            {
                action();
                return;
            }

            using var completed = new System.Threading.ManualResetEventSlim(false);
            System.Exception exception = null;
            _uiContext.Post(_ =>
            {
                try
                {
                    action();
                }
                catch (System.Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    completed.Set();
                }
            }, null);
            completed.Wait();

            if (exception != null)
            {
                throw exception;
            }
        }

        private readonly System.Collections.Generic.List<CefGlueBrowser> _popups = new System.Collections.Generic.List<CefGlueBrowser>();

        /// <summary>
        /// Creates the browser for a window.open popup: call it from LifeSpanHandler.OnBeforePopup (any thread -
        /// it runs on the GTK main loop itself), hand back <paramref name="popUpClient"/> and
        /// <paramref name="popUpDictionary"/>, and return false. The library shows the popup through the
        /// IPopupPresenter (default: a transient Gtk.Window), closes it when the page calls window.close() and
        /// disposes it - same as the other hosts.
        /// </summary>
        public void CreatePopupBrowser(ref CefClient popUpClient,
                                        ref CefDictionaryValue popUpDictionary,
                                        CefWindowInfo windowInfo,
                                        CefPopupFeatures popupFeatures,
                                        CefWindowOpenDisposition targetDisposition)
        {
            CefClient client = null;
            CefDictionaryValue extraInfo = null;
            InvokeOnUi(() => CreatePopupBrowserOnUi(ref client, ref extraInfo, windowInfo, popupFeatures));
            popUpClient = client;
            popUpDictionary = extraInfo;
        }

        private void CreatePopupBrowserOnUi(ref CefClient popUpClient,
                                            ref CefDictionaryValue popUpDictionary,
                                            CefWindowInfo windowInfo,
                                            CefPopupFeatures popupFeatures)
        {
            windowInfo.SharedTextureEnabled = false;

            var popupChild = new CefGlueBrowser(host: Host);

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild, this);

            var presenter = Host.TryCreateInstance<IPopupPresenter<CefGlueBrowser>>() ?? new WindowPopupPresenter();
            var request = new PopupRequest<CefGlueBrowser>(this, popupChild, popupFeatures);
            popupChild.LifeSpanHandler = new PopupLifeSpanHandler(this, popupChild, presenter, request);

            _popups.Add(popupChild);
        }

        /// <summary>Forgets and disposes a popup this browser opened (closed, or its creation aborted).</summary>
        internal void DisposePopup(CefGlueBrowser popup)
        {
            if (_popups.Remove(popup))
            {
                popup.Dispose();
            }
        }
    }

    /// <summary>
    /// Lifecycle of a window.open popup: shows it through the <see cref="IPopupPresenter{TBrowser}"/>, closes the
    /// browser when its UI goes away, and handles the page closing itself (window.close) without crashing
    /// libcef - mirrors the WinForms, WinUI, Avalonia and WPF PopupLifeSpanHandler.
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
                _opener.PostToUi(() => _self.Address = targetUrl);
                return true;
            }

            _self.CreatePopupBrowser(ref client, ref extraInfo, windowInfo, popupFeatures, targetDisposition);
            return false;
        }

        protected override void OnAfterCreated(CefBrowser browser)
        {
            _opener.PostToUi(() =>
            {
                var host = _presenter.Show(_request);
                _host = host;
                if (host != null)
                {
                    host.Closed += OnHostClosed;
                }
            });
        }

        private void OnHostClosed()
        {
            if (_hostGone)
            {
                return;
            }

            _hostGone = true;
            if (!_closed)
            {
                _closing = true;
                _self.CloseBrowser();
                _opener.PostToUi(() => _opener.Focus());
            }
        }

        protected override bool DoClose(CefBrowser browser)
        {
            if (_closing)
            {
                return false;
            }

            _closing = true;
            var host = _host;
            if (host == null)
            {
                return false;
            }

            _opener.PostToUi(() => host.Close());
            return true;
        }

        protected override void OnBeforeClose(CefBrowser browser)
        {
            _closed = true;
            _opener.PostToUi(() =>
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
            => _opener.PostToUi(() => _opener.DisposePopup(_self));
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
            _opener.PostToUi(() =>
            {
                if (_target.Widget.GetRoot() is Window window && window != _opener.Widget.GetRoot() as Window)
                {
                    window.Title = title;
                }
            });
        }
    }
}
