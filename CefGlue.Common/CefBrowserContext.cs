using System;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Common
{
    public sealed class CefBrowserContext
    {
        public CefBrowserContext(
            Func<CefBrowserHost> getBrowserHost,
            IOffScreenTargetHost target,
            IOffScreenPopupHost popup,
            Action<string, Action> withErrorHandling,
            Action<string, Exception> handleException,
            Func<bool> isFullscreen,
            ILogger logger)
        {
            GetBrowserHost = getBrowserHost;
            Target = target;
            Popup = popup;
            WithErrorHandling = withErrorHandling;
            HandleException = handleException;
            IsFullscreen = isFullscreen;
            Logger = logger;
        }

        public Func<CefBrowserHost> GetBrowserHost { get; }
        public IOffScreenTargetHost Target { get; }
        public IOffScreenPopupHost Popup { get; }
        public Action<string, Action> WithErrorHandling { get; }
        public Action<string, Exception> HandleException { get; }
        public Func<bool> IsFullscreen { get; }
        public ILogger Logger { get; }
    }
}
