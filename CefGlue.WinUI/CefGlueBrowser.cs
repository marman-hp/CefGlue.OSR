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
using Xilium.CefGlue.Common.Handlers;
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
            if (!CefRuntimeLoader.IsOSREnabled)
            {
                throw new NotSupportedException(
                    "CefGlue.WinUI only supports off-screen (windowless) rendering today - " +
                    "windowed rendering isn't implemented yet (see CefGlueBrowser.CreateControl).");
            }

            _setupCefWindowInfo = setupCefWindowInfo;
            _setupCefBrowserSettings = setupCefBrowserSettings;

            _rootGrid = new Grid();

            var windowInfo = setupCefWindowInfo?.Invoke();
            AttachTarget(ResolveControl(windowInfo));
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

            if (CefRuntimeLoader.IsOSREnabled)
            {
                base.AttachTarget(CreateOffScreenControlHost());
            }
            else
            {
                BuildAdapter();
            }

            Adapter.SetupCefWindowInfo = _setupCefWindowInfo;
            Adapter.SetupCefBrowserSettings = _setupCefBrowserSettings;
            Adapter.Initialized += () => target.AttachBrowser(UnderlyingBrowser);

            return target;
        }

        protected virtual BaseCefControl ResolveControl(CefWindowInfo windowInfo)
        {
            if (windowInfo?.SharedTextureEnabled == true)
            {
                return new TextureControl { UseSafeResize = true };
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

        protected internal override ITarget CreateControl() =>
            throw new NotSupportedException(
                "Windowed rendering isn't implemented for CefGlue.WinUI yet - off-screen only.");

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
                _textureSurface = new WinUITextureRenderSurface { UseOSRDirtyRect = UseOSRDirtyRect, ShowOverlayInfo = _showOverlayInfo };
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
            var popupChild = new CefGlueBrowser();

            popupChild.Adapter.OnBeforeCreatePopup(ref popUpClient, ref popUpDictionary, windowInfo, popupFeatures);
            popupChild.DisplayHandler = new PopupDisplayHandler(popupChild);
            popupChild.LifeSpanHandler = LifeSpanHandler;

            var size = (popupFeatures.Width == null || popupFeatures.Height == null)
                ? new Windows.Foundation.Size()
                : new Windows.Foundation.Size(popupFeatures.Width.Value, popupFeatures.Height.Value);

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
            if (_target.HostWindow != null)
            {
                _target.HostWindow.DispatcherQueue.TryEnqueue(() => _target.HostWindow.Title = title);
            }
        }
    }
}
