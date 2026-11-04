using System;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Demo.Avalonia
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
                    Dispatcher.UIThread.Invoke(() => _owner.Address = targetUrl);
                }
                return true;
            }

            CefClient outClient = null;
            CefDictionaryValue outExtra = null;

            Dispatcher.UIThread.Invoke(() =>
            {
                _owner.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });

            client = outClient;
            extraInfo = outExtra;
            return false;
        }
    }
}
