using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.Avalonia
{
    public static class CefGlueBuilderAvaloniaExtensions
    {
        public static CefGlueBuilder UseAvalonia(this CefGlueBuilder builder)
        {
            builder.Services.AddSingleton<ICefGlueBrowserFactory, AvaloniaCefGlueBrowserFactory>();
            // Try: an app's own presenter registered before UseAvalonia() wins.
            builder.Services.TryAddCoordinator<IPopupPresenter<CefGlueBrowser>, WindowPopupPresenter>();
            return builder;
        }

        public static CefGlueBrowser CreateBrowser(this CefGlueHost host, CefGlueBrowserOptions options = null)
            => host.CreateBrowser<CefGlueBrowser>(options);
    }

    internal sealed class AvaloniaCefGlueBrowserFactory : ICefGlueBrowserFactory
    {
        public System.Type BrowserType => typeof(CefGlueBrowser);

        public object CreateBrowser(CefGlueHost host, CefGlueBrowserOptions options)
        {
            return new CefGlueBrowser(
                cefRequestContextFactory: options.RequestContextFactory,
                setupCefWindowInfo: options.SetupCefWindowInfo,
                setupCefBrowserSettings: options.SetupCefBrowserSettings,
                host: host,
                useTextureControl: options.UseTextureControl);
        }
    }
}
