using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Extensions
{
    public static class IdentityExtensions
    {
        public static IServiceCollection AddCustomAuthorization(this IServiceCollection services)
        {
            services.AddCustomAuthorizationPolicies();

            return services;
        }


        private static IServiceCollection AddCustomAuthorizationPolicies(this IServiceCollection services)
        {
            services.AddAuthorization(
                options =>
                {
                });

            return services;
        }
    }
}
