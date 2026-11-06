using System;
using System.Linq;
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
            services.AddSingleton(new CoordinatorRegistration(typeof(TInterface), typeof(TImplementation)));
            return services;
        }

        /// <summary>Registers <typeparamref name="TImplementation"/> only if nothing is registered for <typeparamref name="TInterface"/> yet - for platform defaults.</summary>
        public static IServiceCollection TryAddCoordinator<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            if (!services.Any(d => IsRegistrationFor(d, typeof(TInterface)) || d.ServiceType == typeof(TInterface)))
            {
                services.AddCoordinator<TInterface, TImplementation>();
            }
            return services;
        }

        public static IServiceCollection ReplaceCoordinator<TInterface, TImplementation>(this IServiceCollection services)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (IsRegistrationFor(services[i], typeof(TInterface)))
                {
                    services.RemoveAt(i);
                }
            }
            services.RemoveAll<TInterface>();
            services.AddCoordinator<TInterface, TImplementation>();
            return services;
        }

        internal static Type FindCoordinatorImplementation(this IServiceCollection services, Type serviceType)
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var descriptor = services[i];
                if (IsRegistrationFor(descriptor, serviceType))
                {
                    return ((CoordinatorRegistration)descriptor.ImplementationInstance).ImplementationType;
                }
                // A plain AddTransient<T, TImplementation>() still works.
                if (descriptor.ServiceType == serviceType && !descriptor.IsKeyedService && descriptor.ImplementationType != null)
                {
                    return descriptor.ImplementationType;
                }
            }
            return null;
        }

        private static bool IsRegistrationFor(ServiceDescriptor descriptor, Type serviceType) =>
            descriptor.ServiceType == typeof(CoordinatorRegistration)
            && !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is CoordinatorRegistration registration
            && registration.ServiceType == serviceType;
    }

    /// <summary>
    /// A coordinator's interface-to-implementation mapping. Coordinators are built per browser by
    /// <see cref="CefGlueHost"/> with runtime arguments (the browser host, the render target), so they are kept as
    /// this plain instance instead of a transient the container would try to construct, and fail to validate,
    /// itself (e.g. ASP.NET Core's ValidateOnBuild in Development).
    /// </summary>
    public sealed class CoordinatorRegistration
    {
        internal CoordinatorRegistration(Type serviceType, Type implementationType)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
        }

        public Type ServiceType { get; }

        public Type ImplementationType { get; }
    }
}
