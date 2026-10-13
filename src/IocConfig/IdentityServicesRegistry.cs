using Common.GuardToolkit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;
using ViewModels.Identity.Settings;

namespace IocConfig
{
    public static class IdentityServicesRegistry
    {
        /// <summary>
        /// Adds all of the ASP.NET Core Identity related services and configurations at once.
        /// </summary>
        public static void AddCustomIdentityServices(this IServiceCollection services)
        {
            var siteSettings = GetSiteSettings(services);
            services.AddIdentityOptions(siteSettings);
            services.AddCustomServices();
            services.AddCustomTicketStore(siteSettings);
            services.AddDynamicPermissions();
            services.AddCustomDataProtection(siteSettings);
        }

        /// <summary>
        /// Adds all of the ASP.NET Core Identity related initializations at once
        /// (applies pending migrations and seeds the admin user).
        /// </summary>
        /// <remarks>
        /// With endpoint routing (ASP.NET Core 3+) app.UseAuthentication() must be placed after app.UseRouting(),
        /// so it is now called from Startup.Configure instead of here.
        /// </remarks>
        public static void UseCustomIdentityServices(this IApplicationBuilder app)
        {
            app.callDbInitializer();
        }

        private static void callDbInitializer(this IApplicationBuilder app)
        {
            var scopeFactory = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                var identityDbInitialize = scope.ServiceProvider.GetService<IIdentityDbInitializer>();
                identityDbInitialize.Initialize();
                identityDbInitialize.SeedData();
            }
        }

        public static SiteSettings GetSiteSettings(this IServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            var siteSettingsOptions = provider.GetService<IOptionsSnapshot<SiteSettings>>();
            siteSettingsOptions.ThrowIfNull(nameof(siteSettingsOptions));
            var siteSettings = siteSettingsOptions.Value;
            siteSettings.ThrowIfNull(nameof(siteSettings));
            return siteSettings;
        }
    }
}