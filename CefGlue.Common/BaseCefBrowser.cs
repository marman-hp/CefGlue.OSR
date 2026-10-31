using System;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Common
{
    public abstract partial class BaseCefBrowser : IDisposable
    {
        protected readonly ILogger _logger;

        internal CefGlueAdapter _adapter;
        private readonly Func<CefRequestContext> _requestContextFactory;
        private readonly CefGlueHost _host;

        private IOffScreenTargetHost _attachedTarget;

        #region Disposable

        public BaseCefBrowser(Func<CefRequestContext> cefRequestContextFactory = null, CefGlueHost host = null)
        {
            if (!CefRuntimeLoader.IsLoaded)
            {
                CefRuntimeLoader.Load();
            }

            _requestContextFactory = cefRequestContextFactory;
            _host = host ?? CefGlueHost.Default;

            _logger = _host.CreateLogger(nameof(BaseCefBrowser));

        }

        protected void BuildAdapter()
        {
            if (_adapter != null  )
                return;

            _adapter = CreateBrowserAdapter(_requestContextFactory);
            _adapter.InternalInitCef();
        }

        public T AttachTarget<T>(T target) where T : IOffScreenTargetHost
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (_adapter != null)
            {
                throw new InvalidOperationException("AttachTarget can only be called before the browser has been created - this instance is already attached and initialized.");
            }

            _attachedTarget = target;
            BuildAdapter();

            return target;
        }

        internal bool EnableTexture { get; set; }

        internal virtual CefGlueAdapter CreateBrowserAdapter(Func<CefRequestContext> cefRequestContextFactory)
        {
            if (CefRuntimeLoader.IsOSREnabled)
            {
                var target = _attachedTarget ?? CreateOffScreenControlHost();

                return new CefGlueOffScreenAdapter(this, nameof(BaseCefBrowser), target, CreatePopupHost(), _logger, _host, cefRequestContextFactory?.Invoke());
            }
            else
            {
                var target = _attachedTarget ?? CreateControl();
                return new CefGlueAdapter(this, nameof(BaseCefBrowser), target, _logger, _host, cefRequestContextFactory?.Invoke());
            }
        }

        ~BaseCefBrowser()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _adapter?.Dispose(disposing);
        }

        #endregion

        internal CefGlueAdapter Adapter => _adapter;

        protected internal abstract IOffScreenPopupHost CreatePopupHost();

        protected internal abstract ITarget CreateControl();

        protected internal abstract IOffScreenTargetHost CreateOffScreenControlHost();

        public event Action BrowserInitialized { add => _adapter.Initialized += value; remove => _adapter.Initialized -= value; }

        public event LoadStartEventHandler LoadStart { add => _adapter.LoadStart += value; remove => _adapter.LoadStart -= value; }

        public event LoadEndEventHandler LoadEnd { add => _adapter.LoadEnd += value; remove => _adapter.LoadEnd -= value; }

        public event LoadingStateChangeEventHandler LoadingStateChange { add => _adapter.LoadingStateChange += value; remove => _adapter.LoadingStateChange -= value; }

        public event LoadErrorEventHandler LoadError { add => _adapter.LoadError += value; remove => _adapter.LoadError -= value; }

        public event AddressChangedEventHandler AddressChanged { add => _adapter.AddressChanged += value; remove => _adapter.AddressChanged -= value; }

        public event ConsoleMessageEventHandler ConsoleMessage { add => _adapter.ConsoleMessage += value; remove => _adapter.ConsoleMessage -= value; }

        public bool CaptureAudio { get => _adapter.CaptureAudio; set => _adapter.CaptureAudio = value; }

        public event Action<int, int> AudioStreamStarted { add => _adapter.AudioStreamStarted += value; remove => _adapter.AudioStreamStarted -= value; }

        public event Action<float[][], int, long> AudioStreamPacket { add => _adapter.AudioStreamPacket += value; remove => _adapter.AudioStreamPacket -= value; }

        public event Action AudioStreamStopped { add => _adapter.AudioStreamStopped += value; remove => _adapter.AudioStreamStopped -= value; }

        public event StatusMessageEventHandler StatusMessage { add => _adapter.StatusMessage += value; remove => _adapter.StatusMessage -= value; }

        public event TitleChangedEventHandler TitleChanged { add => _adapter.TitleChanged += value; remove => _adapter.TitleChanged -= value; }

        public event FaviconUrlsChangedEventHandler FaviconUrlsChanged { add => _adapter.FaviconUrlsChanged += value; remove => _adapter.FaviconUrlsChanged -= value; }

        public event FullscreenModeChangedEventHandler FullscreenModeChanged { add => _adapter.FullscreenModeChanged += value; remove => _adapter.FullscreenModeChanged -= value; }

        public event JavascriptContextLifetimeEventHandler JavascriptContextCreated { add => _adapter.JavascriptContextCreated += value; remove => _adapter.JavascriptContextCreated -= value; }

        public event JavascriptContextLifetimeEventHandler JavascriptContextReleased { add => _adapter.JavascriptContextReleased += value; remove => _adapter.JavascriptContextReleased -= value; }

        public event JavascriptUncaughtExceptionEventHandler JavascriptUncaughException { add => _adapter.JavascriptUncaughtException += value; remove => _adapter.JavascriptUncaughtException -= value; }

        public event AsyncUnhandledExceptionEventHandler UnhandledException { add => _adapter.UnhandledException += value; remove => _adapter.UnhandledException -= value; }

        public bool UseNativeContextMenu { get => _adapter.UseNativeContextMenu; set => _adapter.UseNativeContextMenu = value; }

        public ContextMenuHandler ContextMenuHandler { get => _adapter.ContextMenuHandler; set => _adapter.ContextMenuHandler = value; }

        public DialogHandler DialogHandler { get => _adapter.DialogHandler; set => _adapter.DialogHandler = value; }

        public DownloadHandler DownloadHandler { get => _adapter.DownloadHandler; set => _adapter.DownloadHandler = value; }

        public DragHandler DragHandler { get => _adapter.DragHandler; set => _adapter.DragHandler = value; }

        public FindHandler FindHandler { get => _adapter.FindHandler; set => _adapter.FindHandler = value; }

        public FocusHandler FocusHandler { get => _adapter.FocusHandler; set => _adapter.FocusHandler = value; }

        public KeyboardHandler KeyboardHandler { get => _adapter.KeyboardHandler; set => _adapter.KeyboardHandler = value; }

        public RequestHandler RequestHandler { get => _adapter.RequestHandler; set => _adapter.RequestHandler = value; }

        public LifeSpanHandler LifeSpanHandler { get => _adapter.LifeSpanHandler; set => _adapter.LifeSpanHandler = value; }

        public DisplayHandler DisplayHandler { get => _adapter.DisplayHandler; set => _adapter.DisplayHandler = value; }

        public RenderHandler RenderHandler { get => _adapter.RenderHandler; set => _adapter.RenderHandler = value; }

        public JSDialogHandler JSDialogHandler { get => _adapter.JSDialogHandler; set => _adapter.JSDialogHandler = value; }

        public string Address { get => _adapter.Address; set => _adapter.Address = value; }

        public bool IsBrowserInitialized => _adapter.IsInitialized;

        public bool IsJavascriptEngineInitialized => _adapter.IsJavascriptEngineInitialized;

        public bool IsLoading => _adapter.IsLoading;

        public string Title => _adapter.Title;

        public double ZoomLevel { get => _adapter.ZoomLevel; set => _adapter.ZoomLevel = value; }

        public CefRequestContext RequestContext { get => _adapter.RequestContext; }

        protected CefBrowser UnderlyingBrowser => _adapter.Browser;

        public CefFrame GetMainFrame() => UnderlyingBrowser?.GetMainFrame();

        public CefFrame GetFocusedFrame() => UnderlyingBrowser?.GetFocusedFrame();

        public CefBrowserHost GetBrowserHost() => UnderlyingBrowser?.GetHost();

        public bool CanGoBack => _adapter.CanGoBack();

        public void GoBack()
        {
            _adapter.GoBack();
        }

        public bool CanGoForward => _adapter.CanGoForward();

        public void GoForward()
        {
            _adapter.GoForward();
        }

        public void Reload(bool ignoreCache = false)
        {
            _adapter.Reload(ignoreCache);
        }

        public void Invalidate()
        {
            _adapter.Invalidate();
        }

        public void SendKeyEvent(CefKeyEvent keyEvent)
        {
            _adapter.SendKeyEvent(keyEvent);
        }

        public void ExecuteJavaScript(string code, string url = null, int line = 1)
        {
            _adapter.ExecuteJavaScript(code, url ?? "about:blank", line);
        }

        public Task<T> EvaluateJavaScript<T>(string code, string frameName = null, string url = null, int line = 1, TimeSpan? timeout = null)
        {
            return _adapter.EvaluateJavaScript<T>(code, url ?? "about:blank", line, frameName, timeout);
        }

        public Task<T> EvaluateJavaScript<T>(string code, CefFrame frame, string url = null, int line = 1, TimeSpan? timeout = null)
        {
            return _adapter.EvaluateJavaScript<T>(code, url ?? "about:blank", line, frame, timeout);
        }

        public void ShowDeveloperTools()
        {
            _adapter.ShowDeveloperTools();
        }

        public void CloseDeveloperTools()
        {
            _adapter.CloseDeveloperTools();
        }

        public int ExecuteDevToolsMethod(string method, CefDictionaryValue parameters)
        {
            return _adapter.ExecuteDevToolsMethod(method, parameters);
        }

        public CefRegistration AddDevToolsMessageObserver(CefDevToolsMessageObserver observer)
        {
            return _adapter.AddDevToolsMessageObserver(observer);
        }

        public bool SendDevToolsMessage(string message)
        {
            return _adapter.SendDevToolsMessage(message);
        }

        public bool SendDevToolsMessage(string method, string jsonParams = "{}")
        {
            return _adapter.SendDevToolsMessage(method, jsonParams);
        }

        public void RegisterJavascriptObject(object targetObject, string name, MethodCallHandler methodHandler = null)
        {
            _adapter.RegisterJavascriptObject(targetObject, name, methodHandler);
        }

        public void UnregisterJavascriptObject(string name)
        {
            _adapter.UnregisterJavascriptObject(name);
        }

        public bool IsJavascriptObjectRegistered(string name)
        {
            return _adapter.IsJavascriptObjectRegistered(name);
        }

        public void Zoom(CefZoomCommand command)
        {
            _adapter.Zoom(command);
        }

        protected bool CreateBrowser(int width, int height)
        {
            return _adapter.CreateBrowser(width, height);
        }
    }
}
