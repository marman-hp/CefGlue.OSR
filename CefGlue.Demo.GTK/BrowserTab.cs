using System;
using System.Threading;
using Gtk;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.GTK;

namespace Xilium.CefGlue.Demo.GTK
{
    /// <summary>One notebook page: navigation bar + browser. Browser events arrive on CEF threads and are posted to GTK.</summary>
    internal sealed class BrowserTab : IDisposable
    {
        private readonly SynchronizationContext _uiContext;
        private readonly CefGlueHost _app;
        private readonly bool _useTexture;
        private readonly Box _navigationBar;
        private readonly Entry _addressEntry;
        private readonly Paned _splitter;
        private Box _devToolsPanel;
        private CefGlueBrowser _devToolsBrowser;

        public CefGlueBrowser Browser { get; }

        public Box Page { get; }

        public string LastStatus { get; private set; } = "Done";

        public event Action<string> TitleChanged;
        public event Action<string> StatusChanged;
        public event Action<bool> FullscreenChanged;

        public BrowserTab(CefGlueHost app, bool useTexture, string initialUrl, Action<string> openInNewTab)
        {
            _uiContext = SynchronizationContext.Current;
            _app = app;
            _useTexture = useTexture;

            Browser = CreateBrowser();
            Browser.LifeSpanHandler = new NewTabLifeSpanHandler(Browser, openInNewTab);
            Browser.ContextMenuHandler = new LinkContextMenuHandler(openInNewTab);

            var backButton = Button.NewFromIconName("go-previous-symbolic");
            backButton.OnClicked += (_, _) => { if (Browser.CanGoBack) Browser.GoBack(); };
            var forwardButton = Button.NewFromIconName("go-next-symbolic");
            forwardButton.OnClicked += (_, _) => { if (Browser.CanGoForward) Browser.GoForward(); };
            var reloadButton = Button.NewFromIconName("view-refresh-symbolic");
            reloadButton.OnClicked += (_, _) => Browser.Reload();

            _addressEntry = Entry.New();
            _addressEntry.Hexpand = true;
            _addressEntry.OnActivate += (_, _) => Navigate(_addressEntry.GetText());

            _navigationBar = Box.New(Orientation.Horizontal, 4);
            _navigationBar.Append(backButton);
            _navigationBar.Append(forwardButton);
            _navigationBar.Append(reloadButton);
            _navigationBar.Append(_addressEntry);
            _navigationBar.SetMarginTop(4);
            _navigationBar.SetMarginBottom(4);
            _navigationBar.SetMarginStart(4);
            _navigationBar.SetMarginEnd(4);

            // Page on the left, DevTools docked on the right once opened (Chrome-style).
            _splitter = Paned.New(Orientation.Horizontal);
            _splitter.Vexpand = true;
            _splitter.SetStartChild(Browser.Widget);
            _splitter.SetResizeStartChild(true);
            _splitter.SetShrinkStartChild(false);
            _splitter.SetResizeEndChild(false);
            _splitter.SetShrinkEndChild(false);

            Page = Box.New(Orientation.Vertical, 0);
            Page.Append(_navigationBar);
            Page.Append(_splitter);

            Browser.AddressChanged += (_, address) => Post(() => _addressEntry.SetText(address));
            Browser.TitleChanged += (_, title) => Post(() => TitleChanged?.Invoke(title));
            Browser.LoadStart += (_, e) =>
            {
                if (e.Frame.IsMain)
                {
                    var url = e.Frame.Url;
                    Post(() => SetStatus($"Loading {url}..."));
                }
            };
            Browser.LoadEnd += (_, e) =>
            {
                if (e.Frame.IsMain)
                {
                    Post(() => SetStatus($"Done · {(useTexture ? "Texture" : "Bitmap")}"));
                }
            };
            Browser.FullscreenModeChanged += (_, fullscreen) => Post(() =>
            {
                _navigationBar.Visible = !fullscreen;
                if (_devToolsPanel != null)
                {
                    _devToolsPanel.Visible = !fullscreen;
                }
                FullscreenChanged?.Invoke(fullscreen);
            });

            Navigate(initialUrl ?? "https://www.google.com");
        }

        public void Navigate(string url)
        {
            _addressEntry.SetText(url);
            Browser.Address = url;
        }

        public void Focus() => Browser.Focus();

        public bool IsDevToolsOpen => _devToolsBrowser != null;

        /// <summary>
        /// Last zoom level set on this tab. Kept here because Browser.ZoomLevel can't be read back on
        /// Linux: CEF runs a multi-threaded message loop there, and GetZoomLevel off the CEF UI thread
        /// just returns 0 (setting it is fine - CEF posts it to the UI thread).
        /// </summary>
        public double ZoomLevel { get; private set; }

        public void SetZoomLevel(double zoomLevel)
        {
            ZoomLevel = zoomLevel;
            Browser.ZoomLevel = zoomLevel;
        }

        private CefGlueBrowser CreateBrowser() => _app.CreateBrowser(new CefGlueBrowserOptions
        {
            SetupCefBrowserSettings = () => new CefBrowserSettings
            {
                WindowlessFrameRate = 60,
                BackgroundColor = new CefColor(255, 255, 255, 255),
            },
            // Must match CefGlueGtkFlags.GetRecommendedFlags(useTexture) - see Program.
            SetupCefWindowInfo = () =>
            {
                var windowInfo = CefWindowInfo.Create();
                windowInfo.SharedTextureEnabled = _useTexture;
                return windowInfo;
            },
            UseTextureControl = _useTexture,
        });

        /// <summary>Docks an inspector browser on <paramref name="inspectorUrl"/> to the right of the page.</summary>
        public void OpenDevTools(string inspectorUrl)
        {
            if (_devToolsBrowser != null)
            {
                return;
            }

            _devToolsBrowser = CreateBrowser();
            _devToolsBrowser.ShowOverlayInfo = Browser.ShowOverlayInfo;
            _devToolsBrowser.Widget.Vexpand = true;
            _devToolsBrowser.Address = inspectorUrl;

            var title = Label.New("DevTools");
            title.Hexpand = true;
            title.Xalign = 0;
            var closeButton = Button.NewFromIconName("window-close-symbolic");
            closeButton.HasFrame = false;
            closeButton.TooltipText = "Close DevTools (F12)";
            closeButton.OnClicked += (_, _) => CloseDevTools();
            var header = Box.New(Orientation.Horizontal, 4);
            header.Append(title);
            header.Append(closeButton);

            // Same grey as the window chrome (nav bar, status bar) instead of the page's white.
            const uint StyleProviderPriorityApplication = 600;
            var headerCss = CssProvider.New();
            headerCss.LoadFromString(
                "box { background-color: @theme_bg_color; padding-left: 6px;" +
                " border-bottom: 1px solid alpha(currentColor, 0.15); }");
            header.GetStyleContext().AddProvider(headerCss, StyleProviderPriorityApplication);

            _devToolsPanel = Box.New(Orientation.Vertical, 0);
            _devToolsPanel.Append(header);
            _devToolsPanel.Append(_devToolsBrowser.Widget);
            _devToolsPanel.SetSizeRequest(320, -1);
            _splitter.SetEndChild(_devToolsPanel);

            // Page gets ~60% of the width, DevTools the rest.
            var width = _splitter.GetWidth();
            _splitter.SetPosition(width > 0 ? (int)(width * 0.6) : 600);
        }

        public void CloseDevTools()
        {
            if (_devToolsBrowser == null)
            {
                return;
            }

            _splitter.SetEndChild(null);
            _devToolsBrowser.Dispose();
            _devToolsBrowser = null;
            _devToolsPanel = null;
            Focus();
        }

        public bool ShowOverlayInfo
        {
            set
            {
                Browser.ShowOverlayInfo = value;
                if (_devToolsBrowser != null)
                {
                    _devToolsBrowser.ShowOverlayInfo = value;
                }
            }
        }

        private void SetStatus(string status)
        {
            LastStatus = status;
            StatusChanged?.Invoke(status);
        }

        private void Post(Action action) => _uiContext.Post(_ => action(), null);

        public void Dispose()
        {
            _devToolsBrowser?.Dispose();
            Browser.Dispose();
        }

        /// <summary>
        /// target=_blank opens a new tab; a real popup (window.open with features, e.g. "Sign in with Google")
        /// goes to the library, which shows it in its own window and keeps window.opener working.
        /// </summary>
        private sealed class NewTabLifeSpanHandler : LifeSpanHandler
        {
            private readonly CefGlueBrowser _owner;
            private readonly Action<string> _openInNewTab;

            public NewTabLifeSpanHandler(CefGlueBrowser owner, Action<string> openInNewTab)
            {
                _owner = owner;
                _openInNewTab = openInNewTab;
            }

            protected override bool OnBeforePopup(
                CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName,
                CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures,
                CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings,
                ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
            {
                if (targetDisposition != CefWindowOpenDisposition.NewPopup)
                {
                    _openInNewTab?.Invoke(targetUrl);
                    return true;
                }

                // Runs on the GTK main loop itself.
                _owner.CreatePopupBrowser(ref client, ref extraInfo, windowInfo, popupFeatures, targetDisposition);
                return false;
            }
        }
    }
}
