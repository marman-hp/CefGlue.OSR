using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.Common.InternalHandlers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.Common.Shared.Helpers;

namespace Xilium.CefGlue.Common
{
    internal class CefGlueOffScreenAdapter : CefGlueAdapter
    {
        private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(50);

        private bool _isVisible = true;

        private IResizeCoordinator _resizeCoordinator;

        private readonly IMouseCoordinator _mouseCoordinator;
        private readonly IKeyboardCoordinator _keyboardCoordinator;

        private readonly IDragDropCoordinator _dragDropCoordinator;

        private readonly IPaintDispatcher _paintDispatcher;

        private readonly IOffscreenRenderCallback _renderCallback;

        private readonly CefBrowserContext _context;

        public CefGlueOffScreenAdapter(object eventsEmitter, string name, IOffScreenTargetHost target, IOffScreenPopupHost popup, ILogger logger, CefGlueHost host, CefRequestContext cefRequestContext = null)
            : base(eventsEmitter, name, target, logger, host, cefRequestContext)
        {
            Popup = popup;

            _context = new CefBrowserContext(
                () => BrowserHost,
                target,
                popup,
                WithErrorHandling,
                HandleException,
                () => IsFullscreen,
                logger);

            _mouseCoordinator = _host.CreateInstance<IMouseCoordinator>();
            _mouseCoordinator.Attach(_context);

            _keyboardCoordinator = _host.CreateInstance<IKeyboardCoordinator>();
            _keyboardCoordinator.Attach(_context);

            _dragDropCoordinator = _host.CreateInstance<IDragDropCoordinator>();
            _dragDropCoordinator.Attach(_context);

            _paintDispatcher = _host.CreateInstance<IPaintDispatcher>();
            _paintDispatcher.Attach(_context);

            _renderCallback = _host.CreateInstance<IOffscreenRenderCallback>(
                _paintDispatcher,
                _dragDropCoordinator);
            _renderCallback.Attach(_context);
        }

        protected new IOffScreenTargetHost Target => (IOffScreenTargetHost)base.Target;

        private IOffScreenPopupHost Popup { get; }

        protected override void InnerDispose()
        {
            (_resizeCoordinator as IDisposable)?.Dispose();
            Target.RenderSurface.Dispose();

            Popup.RenderSurface?.Dispose();
        }

        private int Width => Target.RenderSurface.Width;
        private int Height => Target.RenderSurface.Height;

        private void HandleVisibilityChanged(bool isVisible)
        {
            if (isVisible == _isVisible)
            {
                return;
            }

            WithErrorHandling(nameof(HandleVisibilityChanged), () =>
            {
                if (BrowserHost != null)
                {
                    _isVisible = isVisible;
                    if (isVisible)
                    {
                        BrowserHost.WasHidden(false);
                        BrowserHost.WasResized();

                        _resizeCoordinator.ArmStallWatchdog();
                    }
                    else
                    {
                        BrowserHost.WasHidden(true);
                    }
                }
            });
        }

        private void HandleReattached()
        {
            if (!_isVisible)
            {
                return;
            }

            WithErrorHandling(nameof(HandleReattached), () =>
            {
                if (BrowserHost != null)
                {
                    BrowserHost.WasResized();
                    _resizeCoordinator.ArmStallWatchdog();
                }
            });
        }

        private void HandleScreenInfoChanged(float deviceScaleFactor)
        {
            WithErrorHandling(nameof(HandleScreenInfoChanged), () =>
            {
                Target.RenderSurface.DeviceScaleFactor = deviceScaleFactor;
                Popup.RenderSurface.DeviceScaleFactor = deviceScaleFactor;

                BrowserHost?.WasResized();
            });
        }

        public void OverrideDeviceScaleFactor(float deviceScaleFactor)
        {
            HandleScreenInfoChanged(deviceScaleFactor);
        }

        protected override void HandleControlSizeChanged(CefSize size)
        {
            if (IsBrowserCreated)
            {
                _resizeCoordinator?.RequestResize(size.Width, size.Height);
            }
            else
            {
                CreateBrowser(size.Width, size.Height);
            }
        }

        private void AttachEventHandlers(IOffScreenTargetHost target)
        {
            target.LostFocus += _keyboardCoordinator.HandleLostFocus;

            target.MouseMoved += _mouseCoordinator.HandleMouseMove;
            target.MouseLeave += _mouseCoordinator.HandleMouseLeave;
            target.MouseButtonPressed += (pressedOn, mouseEvent, mouseButton, clickCount) =>
                _mouseCoordinator.HandleMouseButtonDown(pressedOn.Focus, mouseEvent, mouseButton, clickCount);
            target.MouseButtonReleased += _mouseCoordinator.HandleMouseButtonUp;
            target.MouseWheelChanged += _mouseCoordinator.HandleMouseWheel;

            target.KeyDown += _keyboardCoordinator.HandleKeyPress;
            target.KeyUp += _keyboardCoordinator.HandleKeyPress;

            target.TextInput += _keyboardCoordinator.HandleTextInput;

            target.CompositionUpdated += _keyboardCoordinator.HandleImeComposition;
            target.CompositionUpdatedWithClauses += _keyboardCoordinator.HandleImeComposition;
            target.CompositionCommitted += _keyboardCoordinator.HandleImeCommitText;

            target.DragEnter += _dragDropCoordinator.HandleDragEnter;
            target.DragOver += _dragDropCoordinator.HandleDragOver;
            target.DragLeave += _dragDropCoordinator.HandleDragLeave;
            target.Drop += _dragDropCoordinator.HandleDrop;
        }

        protected override CommonCefClient CreateCefClient()
        {
            var renderHandler = new CommonCefRenderHandler(_renderCallback, () => RenderHandler, _logger)
            {
                ImeBoundsChanged = (range, bounds) => Target?.UpdateImeCompositionBounds(range, bounds),
                TextInputActiveChanged = active => Target?.SetTextInputActive(active),
            };
            return new CommonCefClient(this, renderHandler, _logger, _host);
        }

        protected override void SetupBrowserView(CefWindowInfo windowInfo, int width, int height, IntPtr hostViewHandle)
        {
            AttachEventHandlers(Target);
            AttachEventHandlers(Popup);

            Target.ScreenInfoChanged += HandleScreenInfoChanged;
            Target.VisibilityChanged += HandleVisibilityChanged;
            Target.Reattached += HandleReattached;

            _resizeCoordinator = _host.CreateInstance<IResizeCoordinator>();
            _resizeCoordinator.Attach(_context);

            Target.RenderSurface.Resize(width, height);

            windowInfo.SetAsWindowless(IntPtr.Zero, Target.RenderSurface.AllowsTransparency);
        }

        protected override void OnBrowserHostCreated(CefBrowserHost browserHost)
        {
            if (Width > 0 && Height > 0)
            {
                browserHost.WasResized();
            }
        }

        protected override bool OnBrowserClose(CefBrowser browser)
        {
            if (browser.Identifier != Browser?.Identifier)
            {
                return false;
            }

            Cleanup(browser);
            return false;
        }

        protected override void OnHandleLoadingStateChange(CefBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
        {

            base.OnHandleLoadingStateChange(browser, isLoading, canGoBack, canGoForward);
        }

        protected override void OnandleLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            base.OnandleLoadStart(browser, frame, transitionType);
        }

        protected override void OnHandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            base.OnHandleLoadEnd(browser, frame, httpStatusCode);

        }

        protected override void OnFullscreenModeChange(bool fullscreen)
        {
            Target.SetFullscreen(fullscreen);
        }
    }
}
