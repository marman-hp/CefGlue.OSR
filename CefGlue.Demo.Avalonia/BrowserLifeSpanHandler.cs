using System;
using Avalonia.Controls;
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

        protected override void OnAfterCreated(CefBrowser cefBrowser)
        {
            var popup = _owner.GetPopup(cefBrowser);
            if (popup == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                var window = new Window
                {
                    Title = "Popup",
                    Width = popup.PopupSize.Width <= 0 ? 808 : popup.PopupSize.Width,
                    Height = popup.PopupSize.Height <= 0 ? 600 : popup.PopupSize.Height,
                    Content = popup.Browser.Control,
                };

                window.Closing += (s, e) => popup.Browser.CloseBrowser();

                var ownerWindow = TopLevel.GetTopLevel(_owner.Control) as Window;
                if (ownerWindow != null)
                {
                    window.Show(ownerWindow);
                }
                else
                {
                    window.Show();
                }

                window.Activate();
            });
        }

        protected override void OnBeforeClose(CefBrowser cefBrowser)
        {
            base.OnBeforeClose(cefBrowser);
            _owner.RemovePopup(cefBrowser);
        }
    }
}
