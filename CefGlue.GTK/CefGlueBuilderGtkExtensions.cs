using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.GTK
{
    public static class CefGlueBuilderGtkExtensions
    {
        public static CefGlueBuilder UseGtk(this CefGlueBuilder builder)
        {
            builder.Services.AddSingleton<ICefGlueBrowserFactory, GtkCefGlueBrowserFactory>();
            builder.Services.TryAddCoordinator<IPopupPresenter<CefGlueBrowser>, WindowPopupPresenter>();
            return builder;
        }

        public static CefGlueBrowser CreateBrowser(this CefGlueHost host, CefGlueBrowserOptions options = null)
            => host.CreateBrowser<CefGlueBrowser>(options);
    }

    internal sealed class GtkCefGlueBrowserFactory : ICefGlueBrowserFactory
    {
        public System.Type BrowserType => typeof(CefGlueBrowser);

        public object CreateBrowser(CefGlueHost host, CefGlueBrowserOptions options)
        {
            return new CefGlueBrowser(
                cefRequestContextFactory: options.RequestContextFactory,
                setupCefWindowInfo: options.SetupCefWindowInfo,
                setupCefBrowserSettings: options.SetupCefBrowserSettings,
                useTextureControl: options.UseTextureControl,
                host: host);
        }
    }
}
