using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.WinUI;

namespace Xilium.CefGlue.Demo.WinUI
{
    internal sealed class BrowserLifeSpanHandler : LifeSpanHandler
    {
        private readonly CefGlueBrowser _owner;
        private readonly Action<string> _openInNewTab;

        public BrowserLifeSpanHandler(CefGlueBrowser owner, Action<string> openInNewTab = null)
        {
            _owner = owner;
            _openInNewTab = openInNewTab;
        }

        protected override bool OnBeforePopup(
            CefBrowser browser,
            CefFrame frame,
            int popupId,
            string targetUrl,
            string targetFrameName,
            CefWindowOpenDisposition targetDisposition,
            bool userGesture,
            CefPopupFeatures popupFeatures,
            CefWindowInfo windowInfo,
            ref CefClient client,
            CefBrowserSettings settings,
            ref CefDictionaryValue extraInfo,
            ref bool noJavascriptAccess)
        {
            if (targetDisposition != CefWindowOpenDisposition.NewPopup)
            {
                if (_openInNewTab != null)
                {
                    _openInNewTab(targetUrl);
                }
                else
                {
                    _owner.Control.DispatcherQueue.TryEnqueue(() => _owner.Address = targetUrl);
                }
                return true;
            }

            CefClient outClient = null;
            CefDictionaryValue outExtra = null;

            InvokeSync(_owner.Control.DispatcherQueue, () =>
            {
                _owner.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });

            client = outClient;
            extraInfo = outExtra;
            return false;
        }

        private static void InvokeSync(DispatcherQueue dispatcherQueue, Action action)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            using var completed = new ManualResetEventSlim(false);
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
                throw new InvalidOperationException("Failed to enqueue to the WinUI DispatcherQueue.");
            }

            completed.Wait();

            if (exception != null)
            {
                throw exception;
            }
        }
    }
}
