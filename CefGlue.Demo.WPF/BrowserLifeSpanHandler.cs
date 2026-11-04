using System;
using System.Windows;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.WPF;

namespace Xilium.CefGlue.Demo.WPF
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
                    _owner.Control.Dispatcher.Invoke(() => _owner.Address = targetUrl);
                }
                return true;
            }

            CefClient outClient = null;
            CefDictionaryValue outExtra = null;

            _owner.Control.Dispatcher.Invoke(() =>
            {
                _owner.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });

            client = outClient;
            extraInfo = outExtra;
            return false;
        }

        protected override void OnBeforeDevToolsPopup(
            CefBrowser browser,
            CefWindowInfo windowInfo,
            ref CefClient client,
            CefBrowserSettings settings,
            ref CefDictionaryValue extraInfo,
            ref bool useDefaultWindow)
        {
            CefClient outClient = null;

            _owner.Control.Dispatcher.Invoke(() =>
            {
                _owner.SetupDevToolsWindowed(windowInfo, ref outClient);
            });

            client = outClient;
            useDefaultWindow = false;
        }
    }
}
