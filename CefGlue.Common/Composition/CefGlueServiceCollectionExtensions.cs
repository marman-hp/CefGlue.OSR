using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xilium.CefGlue.Common.Composition
{
    public static class CefGlueServiceCollectionExtensions
    {
        /// <summary>
        /// Registers CefGlue in an application's own container, e.g. an ASP.NET Core or Generic Host
        /// <see cref="IServiceCollection"/>. <see cref="CefGlueHost"/> is then resolved from that container and uses
        /// the app's provider, so coordinators and the app share the same singletons.
        /// Use <see cref="CefGlueHost.CreateBuilder()"/> instead when CefGlue should have a container of its own.
        /// </summary>
        /// <param name="configure">Platform head and coordinator overrides, e.g. <c>b => b.UseAvalonia()</c>.</param>
        public static IServiceCollection AddCefGlue(this IServiceCollection services, Action<CefGlueBuilder> configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var builder = new CefGlueBuilder(services);
            configure?.Invoke(builder);

            services.TryAddSingleton(provider => new CefGlueHost(services, provider));
            return services;
        }
    }
}
