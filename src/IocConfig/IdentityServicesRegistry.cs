using System;
using System.Collections.Generic;
using System.Linq;
using Common.GuardToolkit;
using DataLayer.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
                // R1t (D20): on a server installed with install.ps1 -HardenAudit the installer applies the database updates under its own account (WebApi.dll --migrate-only)
                // and the account of the program cannot change the structure. The program then only checks that nothing is waiting (Database:MigrateAtStart false).
                if (MigrateAtStart(scope.ServiceProvider)) identityDbInitialize.Initialize();
                else EnsureNoPendingMigrations(scope.ServiceProvider);
                identityDbInitialize.SeedData();
            }
        }

        /// <summary>Database:MigrateAtStart (default true: the program applies the pending database updates when it starts, as it always did). Only "false" or "0" switch it off.</summary>
        private static bool MigrateAtStart(IServiceProvider services)
        {
            string value = services.GetService<IConfiguration>()?["Database:MigrateAtStart"]?.Trim();
            return !(string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0");
        }

        private static void EnsureNoPendingMigrations(IServiceProvider services)
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            List<string> pending = context.Database.GetPendingMigrations().ToList();
            if (pending.Count > 0)
                throw new InvalidOperationException("The database is not up to date: " + pending.Count + " update(s) are waiting (" + string.Join(", ", pending) +
                    "). This server applies them with install.ps1 (it runs WebApi.dll --migrate-only under the administrator's account); run the installer of this version, or set Database:MigrateAtStart to true to let the program update the database itself.");
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