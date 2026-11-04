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
            // Browsers are created on the GTK main loop (Application.RunWithSynchronizationContext's context);
            // popup callbacks arrive on CEF's own thread and are marshaled back through this.
            _uiContext = System.Threading.SynchronizationContext.Current;
            _uiThreadId = System.Environment.CurrentManagedThreadId;

            _activeControl = ResolveControl(setupCefWindowInfo, useTextureControl);
            // Main control only - matches CefGlue.WPF's/CefGlue.WinUI's identical convention (see
            // BaseCefControl.UseSafeResize's remarks).
            _activeControl.UseSafeResize = true;

            // Wraps _activeControl's own widget so CreatePopupHost has somewhere to layer the
            // popup as an overlay child, regardless of whether _activeControl is BitmapControl or
            // TextureControl (see PopupControlHost's own remarks - popup rendering can't live
            // inside either control's own private widget tree, since it can't assume which one
            // _activeControl actually is). Mirrors CefGlue.WinUI's/CefGlue.Avalonia's identical
            // wrapper Grid, just using Gtk.Overlay - GTK4's own built-in "layer floating content
            // over a main child" widget - instead of a Grid+Margin trick, since GTK has a primitive
            // purpose-built for exactly this.
            _overlay = Overlay.New();
            _overlay.Child = _activeControl.Widget;

            // Gtk.Overlay paints its own GTK-theme background by default (confirmed live,
            // 2026-08-10 on WSLg: a plain grey fill visible wherever the main content doesn't
            // fully cover it, instead of whatever background the consumer's own window/layout had
            // - _activeControl.Widget used to be added directly to the consumer's own tree before
            // this wrapper existed, inheriting THEIR background instead). Scoped CssProvider (only
            // applied to this one Overlay instance, not globally) instead of a global stylesheet
            // change - same pattern as PopupControlHost's own scoped padding-strip provider.
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

                // A static page sends no frames; ask for one so the overlay appears/disappears now.
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
        /// No-host popup (2026-08-10) - see Platform/PopupControlHost.cs's own remarks. Composites
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
            // CEF hands the popup the opener's window info; the popup always gets a BitmapControl, so make it
            // paint plain bitmaps even when the opener uses shared textures.
            windowInfo.SharedTextureEnabled = false;

            var popupChild = new CefGlueBrowser(host: Host);

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild, this);

            // WHERE the popup shows is the app's choice (IPopupPresenter, default: a transient Gtk.Window); its
            // lifecycle stays here, in PopupLifeSpanHandler - same as the other hosts.
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

            // A popup opening its own popup - goes through the same presenter.
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
                _opener.PostToUi(() => _opener.Focus());
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

            _opener.PostToUi(() => host.Close());
            return true;
        }

        // CEF is still inside its close sequence in these two: remove the UI (if CEF closed the browser some
        // other way) and dispose afterwards, on the GTK main loop.
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
                // Only a window of the popup's own: a presenter that shows popups in a tab keeps the main title.
                if (_target.Widget.GetRoot() is Window window && window != _opener.Widget.GetRoot() as Window)
                {
                    window.Title = title;
                }
            });
        }
    }
}
