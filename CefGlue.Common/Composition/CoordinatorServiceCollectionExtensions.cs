using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xilium.CefGlue.Common.Composition
{
    public static class CoordinatorServiceCollectionExtensions
    {
        public static IServiceCollection AddCoordinator<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            services.AddTransient<TInterface, TImplementation>();
            return services;
        }

        public static IServiceCollection ReplaceCoordinator<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            services.RemoveAll<TInterface>();
            services.AddTransient<TInterface, TImplementation>();
            return services;
        }
    }
}
