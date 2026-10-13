using System;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Extentions
{
    /// <summary>
    /// Runs a callback with a service resolved from a new DI scope.
    /// Local copy of the helpers that used to come from DNTCommon.Web.Core (same generic argument order).
    /// </summary>
    public static class ServiceProviderExtensions
    {
        public static void RunScopedService<TService>(this IServiceProvider serviceProvider, Action<TService> callback)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            using (var serviceScope = scopeFactory.CreateScope())
            {
                var service = serviceScope.ServiceProvider.GetRequiredService<TService>();
                callback(service);
            }
        }

        public static TResult RunScopedService<TResult, TService>(this IServiceProvider serviceProvider, Func<TService, TResult> callback)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            using (var serviceScope = scopeFactory.CreateScope())
            {
                var service = serviceScope.ServiceProvider.GetRequiredService<TService>();
                return callback(service);
            }
        }
    }
}
