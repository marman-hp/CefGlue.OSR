using System;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;

namespace Xilium.CefGlue.WPF
{
    public static class CefGlueBuilderWpfExtensions
    {
        public static CefGlueBuilder UseWpf(this CefGlueBuilder builder)
        {
            builder.Services.AddSingleton<ICefGlueBrowserFactory, WpfCefGlueBrowserFactory>();
            return builder;
        }

        public static CefGlueBrowser CreateBrowser(this CefGlueHost host, CefGlueBrowserOptions options = null)
            => host.CreateBrowser<CefGlueBrowser>(options);
    }

    internal sealed class WpfCefGlueBrowserFactory : ICefGlueBrowserFactory
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
