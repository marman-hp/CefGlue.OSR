using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common.Helpers.Logger;

namespace Xilium.CefGlue.Common.Composition
{
    public sealed class CefGlueHost
    {
        private readonly IServiceCollection _services;
        private readonly IServiceProvider _provider;

        internal CefGlueHost(IServiceCollection services, IServiceProvider provider)
        {
            _services = services;
            _provider = provider;
        }

        public static CefGlueBuilder CreateBuilder() => new CefGlueBuilder(new ServiceCollection());

        public static CefGlueBuilder CreateBuilder(IServiceCollection services) => new CefGlueBuilder(services);

        private static readonly Lazy<CefGlueHost> _default = new Lazy<CefGlueHost>(() => CreateBuilder().Build());
        internal static CefGlueHost Default => _default.Value;

        internal ILogger CreateLogger(string name) => _provider.GetService<ILogger>() ?? new NullLogger(name);

        internal T CreateInstance<T>(params object[] parameters)
        {
            var implementationType = _services
                .LastOrDefault(descriptor => descriptor.ServiceType == typeof(T))
                ?.ImplementationType;

            if (implementationType == null)
            {
                throw new InvalidOperationException(
                    $"CefGlueHost has no type-to-type registration for {typeof(T)}. Register one via " +
                    $"builder.Services.AddCoordinator<{typeof(T).Name}, YourImplementation>() (or a plain " +
                    "AddTransient<T, TImplementation>()) before calling Build().");
            }

            return (T)ActivatorUtilities.CreateInstance(_provider, implementationType, parameters);
        }

        public TBrowser CreateBrowser<TBrowser>(CefGlueBrowserOptions options = null) where TBrowser : class
        {
            var factory = _provider.GetService<ICefGlueBrowserFactory>();
            if (factory == null)
            {
                throw new InvalidOperationException(
                    "CefGlueHost has no platform provider registered. Call builder.UseWinForms() " +
                    "(or another platform head's own UseX()) before Build().");
            }

            var resolvedOptions = options ?? new CefGlueBrowserOptions();

            if (typeof(TBrowser) == factory.BrowserType)
            {
                var browser = factory.CreateBrowser(this, resolvedOptions);

                if (browser is not TBrowser typed)
                {
                    throw new InvalidOperationException(
                        $"The registered platform provider produced a {browser.GetType()}, which isn't " +
                        $"assignable to {typeof(TBrowser)}. Did you call the UseX() for the browser type " +
                        "you're requesting?");
                }

                return typed;
            }

            var runtimeArgs = new System.Collections.Generic.List<object>(4) { this };

            if (resolvedOptions.RequestContextFactory != null)
            {
                runtimeArgs.Add(resolvedOptions.RequestContextFactory);
            }

            if (resolvedOptions.SetupCefWindowInfo != null)
            {
                runtimeArgs.Add(resolvedOptions.SetupCefWindowInfo);
            }

            if (resolvedOptions.SetupCefBrowserSettings != null)
            {
                runtimeArgs.Add(resolvedOptions.SetupCefBrowserSettings);
            }

            return ActivatorUtilities.CreateInstance<TBrowser>(_provider, runtimeArgs.ToArray());
        }
    }
}
