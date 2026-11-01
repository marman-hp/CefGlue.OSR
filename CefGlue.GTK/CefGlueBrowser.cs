using Gtk;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
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

        protected internal override IOffScreenTargetHost CreateOffScreenControlHost()
        {
            if (_activeControl is TextureControl textureControl)
            {
                var textureHost = new OffScreenControlHost<TextureControl, System.IntPtr>(textureControl);
                var renderSurface = new GtkTextureRenderSurface();
                textureHost.AttachRenderSurface(renderSurface);
                textureControl.AttachRenderSurface(renderSurface);
                return textureHost;
            }

            var host = new OffScreenControlHost<BitmapControl, byte[]>((BitmapControl)_activeControl);
            host.AttachSurface(new GtkBitmapSurface());
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
    }
}
