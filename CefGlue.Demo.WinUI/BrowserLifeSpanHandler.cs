using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT.Interop;
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

        protected override void OnAfterCreated(CefBrowser cefBrowser)
        {
            var popup = _owner.GetPopup(cefBrowser);
            if (popup == null)
            {
                return;
            }

            _owner.Control.DispatcherQueue.TryEnqueue(() =>
            {
                var window = new Window
                {
                    Title = "Popup",
                };

                popup.Browser.HostWindow = window;

                var width = popup.PopupSize.Width <= 0 ? 808 : popup.PopupSize.Width;
                var height = popup.PopupSize.Height <= 0 ? 600 : popup.PopupSize.Height;

                var root = new Microsoft.UI.Xaml.Controls.Grid();
                root.Children.Add(popup.Browser.Control);
                window.Content = root;

                window.Closed += (s, e) =>
                {
                    popup.Browser.CloseBrowser();

                    if (_owner.HostWindow != null)
                    {
                        void OnOwnerActivated(object activatedSender, WindowActivatedEventArgs activatedArgs)
                        {
                            _owner.HostWindow.Activated -= OnOwnerActivated;

                            if (activatedArgs.WindowActivationState != WindowActivationState.Deactivated)
                            {
                                _owner.Focus();
                            }
                        }

                        _owner.HostWindow.Activated += OnOwnerActivated;
                    }

                    _owner.Control.DispatcherQueue.TryEnqueue(() => _owner.Focus());
                };

                if (_owner.HostWindow != null)
                {
                    var popupHwnd = WindowNative.GetWindowHandle(window);
                    var ownerHwnd = WindowNative.GetWindowHandle(_owner.HostWindow);
                    SetWindowLongPtr(popupHwnd, GWLP_HWNDPARENT, ownerHwnd);
                }

                window.Activate();

                var appWindow = window.AppWindow;
                if (appWindow != null)
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32((int)width, (int)height));
                }
            });
        }

        protected override void OnBeforeClose(CefBrowser cefBrowser)
        {
            base.OnBeforeClose(cefBrowser);
            _owner.RemovePopup(cefBrowser);
        }

        private const int GWLP_HWNDPARENT = -8;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
