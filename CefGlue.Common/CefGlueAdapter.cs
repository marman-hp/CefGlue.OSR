using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.Common.JavascriptExecution;
using Xilium.CefGlue.Common.ObjectBinding;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.Common.Shared.Helpers;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;
using Xilium.CefGlue.Platform.Windows;

namespace Xilium.CefGlue.Common
{

    internal class CefGlueAdapter : ICefBrowserHost, IDisposable
    {
        private readonly object _eventsEmitter;
        private readonly string _name;
        protected readonly ILogger _logger;
        protected bool _useTexture = false;

        private string _initialUrl;
        private string _title;
        private CefBrowser _browser;

        private CommonCefClient _cefClient;
        private PipeServer _crashServerPipe;
        private string _crashServerPipeName;
        private JavascriptExecutionEngine _javascriptExecutionEngine;
        private NativeObjectMethodDispatcher _objectMethodDispatcher;

        private readonly NativeObjectRegistry _objectRegistry = new NativeObjectRegistry();

        // Live JS contexts per frame id. A context release is reported both by the renderer message
        // and by OnFrameDetached (CEF 154 sends both), so only the first release of a created context fires.
        private readonly Dictionary<string, int> _liveJavascriptContexts = new Dictionary<string, int>();

        private volatile bool _disposed;
        private object _disposeLock = new object();

        private readonly IContextMenuPresenter _contextMenuPresenter;

        protected readonly CefGlueHost _host;

        public CefGlueAdapter(object eventsEmitter, string name, ITarget target, ILogger logger, CefGlueHost host, CefRequestContext cefRequestContext = null)
        {
            _eventsEmitter = eventsEmitter;
            _name = name;
            _logger = logger;
            _host = host;

            Target = target;
            RequestContext = cefRequestContext;

            _contextMenuPresenter = _host.CreateInstance<IContextMenuPresenter>(target);

            if (_logger.IsInfoEnabled)
            {
                _logger.Info($"Browser adapter created (Id:{GetHashCode()}");
            }
        }

        public Func<CefWindowInfo> SetupCefWindowInfo;
        public Func<CefBrowserSettings> SetupCefBrowserSettings;

        internal protected void InternalInitCef()
        {

            Target.GotFocus += HandleGotFocus;
            Target.SizeChanged += HandleControlSizeChanged;
        }

        ~CefGlueAdapter()
        {

            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool disposing)
        {
            var disposeLock = _disposeLock;
            if (disposeLock == null)
            {
                return;
            }

            lock (disposeLock)
            {
                if (_disposeLock == null)
                {
                    return;
                }

                _disposeLock = null;
            }

            if (_logger.IsInfoEnabled)
            {
                _logger.Info($"Browser adapter disposed (Id:{GetHashCode()}");
            }

            var browserHost = BrowserHost;
            if (browserHost != null)
            {
                if (disposing)
                {
                    browserHost.CloseBrowser(true);
                }
            }

            if (disposing)
            {
                InnerDispose();
                GC.SuppressFinalize(this);
            }
        }

        protected virtual void InnerDispose() { }

        public event LoadStartEventHandler LoadStart;
        public event LoadEndEventHandler LoadEnd;
        public event LoadingStateChangeEventHandler LoadingStateChange;
        public event LoadErrorEventHandler LoadError;

        public event Action Initialized;
        public event AddressChangedEventHandler AddressChanged;
        public event TitleChangedEventHandler TitleChanged;
        public event FaviconUrlsChangedEventHandler FaviconUrlsChanged;
        public event FullscreenModeChangedEventHandler FullscreenModeChanged;
        public event ConsoleMessageEventHandler ConsoleMessage;
        public event StatusMessageEventHandler StatusMessage;

        public event JavascriptContextLifetimeEventHandler JavascriptContextCreated;
        public event JavascriptContextLifetimeEventHandler JavascriptContextReleased;
        public event JavascriptUncaughtExceptionEventHandler JavascriptUncaughtException;

        public bool CaptureAudio { get; set; }
        public event Action<int, int> AudioStreamStarted;
        public event Action<float[][], int, long> AudioStreamPacket;
        public event Action AudioStreamStopped;

        public event AsyncUnhandledExceptionEventHandler UnhandledException;

        public CefRequestContext RequestContext { get; }

        public string Address { get => _browser?.GetMainFrame().Url ?? _initialUrl; set => NavigateTo(value); }

        #region Cef Handlers

        public bool UseNativeContextMenu { get; set; }
        public ContextMenuHandler ContextMenuHandler { get; set; }
        public DialogHandler DialogHandler { get; set; }
        public DownloadHandler DownloadHandler { get; set; }
        public DragHandler DragHandler { get; set; }
        public FindHandler FindHandler { get; set; }
        public FocusHandler FocusHandler { get; set; }
        public KeyboardHandler KeyboardHandler { get; set; }
        public RequestHandler RequestHandler { get; set; }
        public LifeSpanHandler LifeSpanHandler { get; set; }
        public DisplayHandler DisplayHandler { get; set; }
        public RenderHandler RenderHandler { get; set; }
        public JSDialogHandler JSDialogHandler { get; set; }
        public PermissionHandler PermissionHandler { get; set; }

        #endregion

        protected virtual ITarget Target { get; }

        protected CefBrowserHost BrowserHost { get;  private set; }

        protected bool IsBrowserCreated { get; private set; }

        private volatile bool _isFullyReady;
        public bool IsInitialized => _browser != null && _isFullyReady;

        public bool IsLoading => _browser?.IsLoading ?? false;

        public string Title => _title;

        public double ZoomLevel
        {
            get => BrowserHost?.GetZoomLevel() ?? 0;
            set => BrowserHost?.SetZoomLevel(value);
        }

        public bool IsJavascriptEngineInitialized { get; private set; }

        public CefBrowser Browser => _browser;

        public double DefaultZoomLevel => BrowserHost?.GetDefaultZoomLevel() ?? 0.0;

        private bool _isPageFullscreen;

        public bool IsFullscreen => _isPageFullscreen;

        private void NavigateTo(string url)
        {
            url = url.TrimStart();

            ActionTask.Run(async () =>
            {
                if (IsInitialized)
                {
                    await Task.Delay(10);
                    _browser?.GetMainFrame()?.LoadUrl(url);
                }
                else
                {
                    _initialUrl = url;
                }
            });
        }

        public bool CanGoBack()
        {
            return _browser?.CanGoBack ?? false;
        }

        public void GoBack()
        {
            _browser?.GoBack();
        }

        public bool CanGoForward()
        {
            return _browser?.CanGoForward ?? false;
        }

        public void GoForward()
        {
            _browser?.GoForward();
        }

        public void Reload(bool ignoreCache)
        {
            if (ignoreCache)
            {
                _browser?.ReloadIgnoreCache();
            }
            else
            {
                _browser?.Reload();
            }
        }

        public void Invalidate()
        {
            BrowserHost?.Invalidate(CefPaintElementType.View);
        }

        public void SendKeyEvent(CefKeyEvent keyEvent)
        {
            BrowserHost?.SendKeyEvent(keyEvent);
        }

        public void ExecuteJavaScript(string code, string url, int line)
        {
            _browser?.GetMainFrame().ExecuteJavaScript(code, url, line);
        }

        public Task<T> EvaluateJavaScript<T>(string code, string url, int line, string frameName = null, TimeSpan? timeout = null)
        {
            var frame = frameName != null ? _browser?.GetFrameByName(frameName) : _browser?.GetMainFrame();
            if (frame != null)
            {
                return EvaluateJavaScript<T>(code, url, line, frame, timeout);
            }

            return Task.FromResult<T>(default);
        }

        public Task<T> EvaluateJavaScript<T>(string code, string url, int line, CefFrame frame, TimeSpan? timeout = null)
        {
            if (frame.IsValid && _javascriptExecutionEngine != null)
            {
                return _javascriptExecutionEngine.Evaluate<T>(code, url, line, frame, timeout);
            }

            return Task.FromResult<T>(default);
        }

        public void ShowDeveloperTools()
        {
            var windowInfo = CefWindowInfo.Create();

            if (CefRuntime.Platform == CefRuntimePlatform.Windows)
            {
                windowInfo.SetAsPopup( IntPtr.Zero, "DevTools");
            }

            BrowserHost?.ShowDevTools(windowInfo, _cefClient, new CefBrowserSettings(), new CefPoint());
        }

        public void CloseDeveloperTools()
        {
            BrowserHost?.CloseDevTools();
        }

        public int ExecuteDevToolsMethod(string method, CefDictionaryValue parameters)
        {
            return BrowserHost?.ExecuteDevToolsMethod(0, method, parameters) ?? 0;
        }

        public CefRegistration AddDevToolsMessageObserver(CefDevToolsMessageObserver observer)
        {
            return BrowserHost?.AddDevToolsMessageObserver(observer);
        }

        public bool SendDevToolsMessage(string message)
        {
            if (BrowserHost == null)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                return BrowserHost.SendDevToolsMessage(buffer, bytes.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private int _nextDevToolsMessageId;

        public bool SendDevToolsMessage(string method, string jsonParams = "{}")
        {
            var id = Interlocked.Increment(ref _nextDevToolsMessageId);
            return SendDevToolsMessage($"{{\"id\":{id},\"method\":\"{method}\",\"params\":{jsonParams}}}");
        }

        public void RegisterJavascriptObject(object targetObject, string name, MethodCallHandler methodHandler = null)
        {
            _objectRegistry.Register(targetObject, name, methodHandler);
        }

        public void UnregisterJavascriptObject(string name)
        {
            _objectRegistry.Unregister(name);
        }

        public bool IsJavascriptObjectRegistered(string name)
        {
            return _objectRegistry.Get(name) != null;
        }

        public bool CreateBrowser(int width, int height)
        {
            if (IsBrowserCreated || width < 0 || height < 0)
            {
                return false;
            }

            var hostViewHandle = Target.GetHostViewHandle(width, height);
            if (hostViewHandle == null)
            {
                return false;
            }

            IsBrowserCreated = true;

            var windowInfo =  SetupCefWindowInfo?.Invoke() ?? CefWindowInfo.Create();
            var settings =   SetupCefBrowserSettings?.Invoke() ?? new CefBrowserSettings();

            if (settings.BackgroundColor.A == 0)
            {
                settings.BackgroundColor = new CefColor(255, 255, 255, 255);
            }

            SetupBrowserView(windowInfo, width, height, hostViewHandle.Value);
            var cefClient = CreateCefClient();
            cefClient.Dispatcher.RegisterMessageHandler(Messages.UnhandledException.Name, OnBrowserProcessUnhandledException);
            _cefClient = cefClient;

            using (var extraInfo = CefDictionaryValue.Create())
            {
                _crashServerPipeName = Guid.NewGuid().ToString();
                extraInfo.SetString(Constants.CrashPipeNameKey, _crashServerPipeName);
                CefBrowserHost.CreateBrowser(windowInfo, cefClient, settings, "", extraInfo, RequestContext);
            }

            return true;
        }

        private  void OnBrowserCreated(CefBrowser browser)
        {
            WithErrorHandling((nameof(OnBrowserCreated)), () =>
            {

                if (_browser != null)
                    return;

                _browser = browser;

                if (browser.IsPopup == false)
                {
                    _crashServerPipe = new PipeServer(_crashServerPipeName);
                    _crashServerPipe.MessageReceived += OnChildProcessCrashed;
                }

                BrowserHost = browser.GetHost();

                var dispatcher = _cefClient?.Dispatcher;

                if (dispatcher != null && browser.IsPopup == false)
                {
                    var javascriptExecutionEngine =
                        new JavascriptExecutionEngine(dispatcher);

                    javascriptExecutionEngine.ContextCreated += HandleJavascriptExecutionEngineContextCreated;
                    javascriptExecutionEngine.ContextReleased += HandleJavascriptExecutionEngineContextReleased;
                    javascriptExecutionEngine.UncaughtException += OnJavascriptExecutionEngineUncaughtException;

                    _javascriptExecutionEngine = javascriptExecutionEngine;

                    _objectRegistry.SetBrowser(browser);

                    _objectMethodDispatcher =
                        new NativeObjectMethodDispatcher(dispatcher, _objectRegistry);
                }

                OnBrowserHostCreated(BrowserHost);

                _isFullyReady = true;
                if (!string.IsNullOrEmpty(_initialUrl))
                {
                    var urlToLoad = _initialUrl;
                    _initialUrl = "";

                    ActionTask.Run(async () =>
                    {
                        await Task.Delay(10);
                        _browser?.GetMainFrame()?.LoadUrl(urlToLoad);
                    });
                }

                Initialized?.Invoke();
            });

        }

        public bool CanZoom(CefZoomCommand command)
        {
            return BrowserHost?.CanZoom(command) ?? false;
        }

        public void Zoom(CefZoomCommand command)
        {
            BrowserHost?.CanZoom(command);
        }

        public void ExitFullscreen(bool willCauseResize)
        {
            BrowserHost?.ExitFullscreen(willCauseResize);
        }

        protected virtual CommonCefClient CreateCefClient()
        {
            return new CommonCefClient(this, null, _logger, _host);
        }

        protected virtual void SetupBrowserView(CefWindowInfo windowInfo, int width, int height, IntPtr hostViewHandle)
        {
            windowInfo.StyleEx |= WindowStyleEx.WS_EX_NOACTIVATE;
            windowInfo.SetAsChild(hostViewHandle, new CefRectangle(0, 0, width, height));
        }

        private void HandleJavascriptExecutionEngineContextCreated(CefFrame frame)
        {
            lock (_liveJavascriptContexts)
            {
                var frameId = frame.Identifier;
                _liveJavascriptContexts.TryGetValue(frameId, out var count);
                _liveJavascriptContexts[frameId] = count + 1;
            }
            if (frame.IsMain)
            {
                IsJavascriptEngineInitialized = true;
            }
            JavascriptContextCreated?.Invoke(_eventsEmitter, new JavascriptContextLifetimeEventArgs(frame));
        }

        private void HandleJavascriptExecutionEngineContextReleased(CefFrame frame)
        {
            lock (_liveJavascriptContexts)
            {
                var frameId = frame.Identifier;
                if (!_liveJavascriptContexts.TryGetValue(frameId, out var count))
                {
                    return; // already released (or never created)
                }
                if (count > 1)
                {
                    _liveJavascriptContexts[frameId] = count - 1;
                }
                else
                {
                    _liveJavascriptContexts.Remove(frameId);
                }
            }
            if (frame.IsMain)
            {
                IsJavascriptEngineInitialized = false;
            }
            JavascriptContextReleased?.Invoke(_eventsEmitter, new JavascriptContextLifetimeEventArgs(frame));
        }

        private void OnJavascriptExecutionEngineUncaughtException(JavascriptUncaughtExceptionEventArgs args)
        {
            JavascriptUncaughtException?.Invoke(_eventsEmitter, args);
        }

        protected void WithErrorHandling(string scopeName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                HandleException(scopeName, ex);
            }
        }

        protected void HandleException(string scopeName, Exception exception)
        {
            _logger.ErrorException($"{_name} : Caught exception in {scopeName}()", exception);
            UnhandledException?.Invoke(_eventsEmitter, new AsyncUnhandledExceptionEventArgs(exception));
        }

        protected virtual void HandleGotFocus()
        {
            WithErrorHandling(nameof(HandleGotFocus), () =>
            {
                Debug.WriteLine($"[CefGlue.DIAG] HandleGotFocus @ {DateTime.Now:HH:mm:ss.fff}");
                BrowserHost?.SetFocus(true);
            });
        }

        protected virtual void HandleControlSizeChanged(CefSize size)
        {
            if (IsBrowserCreated)
                return;

            var created = CreateBrowser(size.Width, size.Height);

            if (created)
            {
                Target.SizeChanged -= HandleControlSizeChanged;
            }
        }

        private void OnBrowserProcessUnhandledException(MessageReceivedEventArgs e)
        {
            var exceptionDetails = Messages.UnhandledException.FromCefMessage(e.Message);
            FireBrowserProcessUnhandledExceptionHandler(exceptionDetails.ExceptionType, exceptionDetails.Message, exceptionDetails.StackTrace);
        }

        private void OnChildProcessCrashed(string message)
        {
            WithErrorHandling(nameof(OnChildProcessCrashed), () =>
            {
                var exception = SerializableException.DeserializeFromString(message);
                FireBrowserProcessUnhandledExceptionHandler(exception.ExceptionType, exception.Message, exception.StackTrace);
            });
        }

        private void FireBrowserProcessUnhandledExceptionHandler(string exceptionType, string message, string stackTrace)
        {
            var exception = new RenderProcessUnhandledException(exceptionType, message, stackTrace);

            _logger.ErrorException("Browser process unhandled exception", exception);

            UnhandledException?.Invoke(_eventsEmitter, new AsyncUnhandledExceptionEventArgs(exception));
        }

        protected virtual void OnBrowserHostCreated(CefBrowserHost browserHost)
        {

            Target.InitializeRender(browserHost.GetWindowHandle());
        }

        protected virtual bool OnBrowserClose(CefBrowser browser)
        {
            if (browser.IsPopup)
            {

                return false;
            }

            Target.DestroyRender();
            Cleanup(browser);

            if (CefRuntime.Platform == CefRuntimePlatform.Linux)
            {
                return false;
            }
            return true;
        }

        protected void Cleanup(CefBrowser browser)
        {
            browser?.GetHost()?.CloseBrowser();
            browser?.Dispose();

            _crashServerPipe?.Dispose();
            _javascriptExecutionEngine?.Dispose();
            _objectRegistry?.Dispose();

            BrowserHost = null;
            _cefClient = null;
            _browser = null;
        }

        #region ICefBrowserHost

        public virtual bool OnBeforeCreatePopup(ref  CefClient popUpClient,
                                                ref CefDictionaryValue popUpDictionary,
                                                CefWindowInfo windowInfo,
                                                CefPopupFeatures popupFeatures)
        {
            Debug.WriteLine("Hashcode CefClient before : {0}" , RuntimeHelpers.GetHashCode(popUpClient));

            if (IsBrowserCreated )
            {
                return false;
            }

            IsBrowserCreated = true;

            var hostViewHandle = Target.GetHostViewHandle(popupFeatures?.Width ?? 0, popupFeatures?.Height ?? 0);

            SetupBrowserView(windowInfo, popupFeatures?.Width ?? 800, popupFeatures?.Height ?? 600, hostViewHandle.Value);

            Target.SizeChanged += HandleControlSizeChanged;

            popUpClient = CreateCefClient();

            _cefClient = popUpClient as CommonCefClient;

            Debug.WriteLine("Hashcode CefClient after : {0}", RuntimeHelpers.GetHashCode(popUpClient));

            popUpDictionary = CefDictionaryValue.Create();
            _crashServerPipeName = Guid.NewGuid().ToString();
            popUpDictionary.SetString(Constants.CrashPipeNameKey, _crashServerPipeName);

            return true;

        }

        void ICefBrowserHost.HandleBrowserCreated(CefBrowser browser)
        {
            WithErrorHandling((nameof(ICefBrowserHost.HandleBrowserDestroyed)), () =>
            {

                OnBrowserCreated(browser);
            });
        }

        void ICefBrowserHost.HandleBrowserDestroyed(CefBrowser browser)
        {
            WithErrorHandling((nameof(ICefBrowserHost.HandleBrowserDestroyed)), () =>
            {
                _objectMethodDispatcher = null;
            });
        }

        bool ICefBrowserHost.HandleBrowserClose(CefBrowser browser)
        {
            var result = false;
            WithErrorHandling((nameof(ICefBrowserHost.HandleBrowserClose)), () =>
            {
                result = OnBrowserClose(browser);
            });

            return result;
        }

        bool ICefBrowserHost.HandleTooltip(CefBrowser browser, string text) => false;

        void ICefBrowserHost.HandleAddressChange(CefBrowser browser, CefFrame frame, string url)
        {
            if (browser.IsPopup || !frame.IsMain)
            {
                return;
            }

            AddressChanged?.Invoke(_eventsEmitter, url);
        }

        void ICefBrowserHost.HandleTitleChange(CefBrowser browser, string title)
        {
            if (browser.IsPopup)
            {
                return;
            }

            _title = title;
            TitleChanged?.Invoke(_eventsEmitter, title);
        }

        void ICefBrowserHost.HandleFaviconUrlChange(CefBrowser browser, string[] iconUrls)
        {
            FaviconUrlsChanged?.Invoke(_eventsEmitter, iconUrls);
        }

        void ICefBrowserHost.HandleStatusMessage(CefBrowser browser, string value)
        {
            StatusMessage?.Invoke(_eventsEmitter, value);
        }

        bool ICefBrowserHost.HandleConsoleMessage(CefBrowser browser, CefLogSeverity level, string message, string source, int line)
        {
            var handler = ConsoleMessage;
            if (handler != null)
            {
                var args = new ConsoleMessageEventArgs(level, message, source, line);
                ConsoleMessage?.Invoke(_eventsEmitter, args);
                return !args.OutputToConsole;
            }
            return false;
        }

        void ICefBrowserHost.HandleAudioStreamStarted(CefBrowser browser, int sampleRate, int channels)
        {
            AudioStreamStarted?.Invoke(sampleRate, channels);
        }

        void ICefBrowserHost.HandleAudioStreamPacket(CefBrowser browser, float[][] channelData, int frames, long pts)
        {
            AudioStreamPacket?.Invoke(channelData, frames, pts);
        }

        void ICefBrowserHost.HandleAudioStreamStopped(CefBrowser browser)
        {
            AudioStreamStopped?.Invoke();
        }

        void ICefBrowserHost.HandleLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            OnandleLoadStart(browser,frame,transitionType);
        }

        protected virtual void OnandleLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            LoadStart?.Invoke(_eventsEmitter, new LoadStartEventArgs(frame));
        }

        void ICefBrowserHost.HandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            OnHandleLoadEnd(browser, frame,httpStatusCode);
        }

        protected virtual void OnHandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            LoadEnd?.Invoke(_eventsEmitter, new LoadEndEventArgs(frame, httpStatusCode));
        }

        private int _dictionaryLoadRetryCount = 0;
        private const int MaxDictionaryLoadRetries = 3;

        void ICefBrowserHost.HandleLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        {
            if (errorCode == CefErrorCode.DictionaryLoadFailed && _dictionaryLoadRetryCount < MaxDictionaryLoadRetries)
            {
                _dictionaryLoadRetryCount++;
                frame.LoadUrl(failedUrl);
                return;
            }

            _dictionaryLoadRetryCount = 0;
            LoadError?.Invoke(_eventsEmitter, new LoadErrorEventArgs(frame, errorCode, errorText, failedUrl));
        }

        void ICefBrowserHost.HandleLoadingStateChange(CefBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
        {
            OnHandleLoadingStateChange(browser,isLoading, canGoBack, canGoForward);
        }

        protected virtual void OnHandleLoadingStateChange(CefBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
        {
            LoadingStateChange?.Invoke(_eventsEmitter, new LoadingStateChangeEventArgs(isLoading, canGoBack, canGoForward));
        }

        void ICefBrowserHost.HandleOpenContextMenu(CefContextMenuParams parameters, CefMenuModel model, CefRunContextMenuCallback callback)
        {
            OnHandleOpenContextMenu(parameters, model, callback);
        }

        protected virtual void OnHandleOpenContextMenu(CefContextMenuParams parameters, CefMenuModel model, CefRunContextMenuCallback callback)
        {
            _contextMenuPresenter.HandleOpenContextMenu(parameters, model, callback);
        }

        void ICefBrowserHost.HandleCloseContextMenu()
        {
            OnHandleCloseContextMenu();
        }
        protected virtual void OnHandleCloseContextMenu()
        {
            _contextMenuPresenter.HandleCloseContextMenu();
        }

        void ICefBrowserHost.HandleException(Exception exception)
        {
            HandleException("Unknown", exception);
        }

        bool ICefBrowserHost.HandleCursorChange(IntPtr cursorHandle, CefCursorType cursorType)
        {
            var result = false;
            WithErrorHandling((nameof(ICefBrowserHost.HandleCursorChange)), () =>
            {
                result = Target.SetCursor(cursorHandle, cursorType);
            });

            return result;
        }

        void ICefBrowserHost.HandleFrameDetached(CefBrowser browser, CefFrame frame)
        {
            HandleJavascriptExecutionEngineContextReleased(frame);
        }

        void ICefBrowserHost.HandleFullscreenModeChange(CefBrowser browser, bool fullscreen)
        {
            WithErrorHandling(nameof(ICefBrowserHost.HandleFullscreenModeChange), () =>
            {
                _isPageFullscreen = fullscreen;
                OnFullscreenModeChange(fullscreen);
                FullscreenModeChanged?.Invoke(_eventsEmitter, fullscreen);
            });
        }

        protected virtual void OnFullscreenModeChange(bool fullscreen) { }

        #endregion
    }
}
