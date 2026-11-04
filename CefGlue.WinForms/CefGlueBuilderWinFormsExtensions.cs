using System;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.WinForms
{
    public static class CefGlueBuilderWinFormsExtensions
    {
        public static CefGlueBuilder UseWinForms(this CefGlueBuilder builder)
        {
            builder.Services.AddSingleton<ICefGlueBrowserFactory, WinFormsCefGlueBrowserFactory>();
            // Try: an app's own presenter registered before UseWinForms() wins.
            builder.Services.TryAddCoordinator<IPopupPresenter<CefGlueBrowser>, FormPopupPresenter>();
            return builder;
        }

        public static CefGlueBrowser CreateBrowser(this CefGlueHost host, CefGlueBrowserOptions options = null)
            => host.CreateBrowser<CefGlueBrowser>(options);
    }

    internal sealed class WinFormsCefGlueBrowserFactory : ICefGlueBrowserFactory
    {
        public Type BrowserType => typeof(CefGlueBrowser);

        public object CreateBrowser(CefGlueHost host, CefGlueBrowserOptions options)
        {
            return new CefGlueBrowser(
                cefRequestContextFactory: options.RequestContextFactory,
                setupCefWindowInfo: options.SetupCefWindowInfo,
                setupCefBrowserSettings: options.SetupCefBrowserSettings,
                host: host);
        }
    }
}
