using System;
using System.IO;
using System.Linq;
using Common.GuardToolkit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ViewModels.Identity.Settings;

namespace DataLayer.Context
{
    public static class DbContextOptionsExtensions
    {
        /// <summary>
        /// It's added to access services from the dbcontext.
        /// remove it if you are using the normal `AddDbContext` and normal constructor dependency injection.
        /// </summary>
        public static void AddRequiredEfInternalServices(
            this IServiceCollection serviceCollection,
            SiteSettings siteSettings)
        {
            switch (siteSettings.ActiveDatabase)
            {
                case ActiveDatabase.InMemoryDatabase:
                    serviceCollection.AddEntityFrameworkInMemoryDatabase();
                    break;

                case ActiveDatabase.LocalDb:
                case ActiveDatabase.SqlServer:
                    serviceCollection.AddEntityFrameworkSqlServer();
                    break;

                default:
                    throw new NotSupportedException("Please set the ActiveDatabase in appsettings.json file.");
            }
        }

        public static void SetDbContextOptions(
            this DbContextOptionsBuilder optionsBuilder,
            SiteSettings siteSettings)
        {
            switch (siteSettings.ActiveDatabase)
            {
                case ActiveDatabase.InMemoryDatabase:
                    optionsBuilder.UseInMemoryDatabase(siteSettings.ConnectionStrings.LocalDb.InitialCatalog);
                    break;

                case ActiveDatabase.LocalDb:
                case ActiveDatabase.SqlServer:
                    optionsBuilder.UseSqlServer(
                        siteSettings.GetDbConnectionString()
                        , serverDbContextOptionsBuilder =>
                        {
                            var minutes = (int)TimeSpan.FromMinutes(3).TotalSeconds;
                            serverDbContextOptionsBuilder.CommandTimeout(minutes);
                            serverDbContextOptionsBuilder.EnableRetryOnFailure();
                        });
                    break;

                default:
                    throw new NotSupportedException("Please set the ActiveDatabase in appsettings.json file.");
            }

            optionsBuilder.ConfigureWarnings(warnings =>
            {
                // EF Core 2.2's IncludeIgnoredWarning / QueryClientEvaluationWarning no longer exist.
                // EF Core 10 throws for an Include that is redundant because of fix-up; EF Core 2.2 accepted it.
                warnings.Log(CoreEventId.NavigationBaseIncludeIgnored);

                // EF Core 9+ refuses to run Database.Migrate() when the model differs from the last migration
                // snapshot. The snapshot was produced by EF Core 2.2, so log the difference instead of stopping
                // the application; run `dotnet ef migrations add <Name>` to see whether anything really changed.
                warnings.Log(RelationalEventId.PendingModelChangesWarning);
            });
        }

        public static string GetDbConnectionString(this SiteSettings siteSettingsValue)
        {
            siteSettingsValue.ThrowIfNull(nameof(siteSettingsValue));

            switch (siteSettingsValue.ActiveDatabase)
            {
                case ActiveDatabase.InMemoryDatabase:
                    return null;

                case ActiveDatabase.LocalDb:
                    var attachDbFilename = siteSettingsValue.ConnectionStrings.LocalDb.AttachDbFilename;
                    var webRootPath = Path.Combine(AppContext.BaseDirectory.Split(new[] { "bin" }, StringSplitOptions.RemoveEmptyEntries).First(), "wwwroot");
                    var attachDbFilenamePath = Path.Combine(webRootPath, "App_Data", attachDbFilename);
                    var localDbInitialCatalog = siteSettingsValue.ConnectionStrings.LocalDb.InitialCatalog;
                    return $@"Data Source=(LocalDB)\MSSQLLocalDB;Initial Catalog={localDbInitialCatalog};AttachDbFilename={attachDbFilenamePath};Integrated Security=True;MultipleActiveResultSets=True;TrustServerCertificate=True;";

                case ActiveDatabase.SqlServer:
                    return siteSettingsValue.ConnectionStrings.SqlServer.ApplicationDbContextConnection;

                default:
                    throw new NotSupportedException("Please set the ActiveDatabase in appsettings.json file.");
            }
        }
    }
}