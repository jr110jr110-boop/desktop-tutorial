using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.Contracts.Identity;

namespace WebApi
{
    public class Program
    {
        public static int Main(string[] args)
        {
            IHost host = CreateHostBuilder(args).Build();
            if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
            {
                // R1t (D20): install.ps1 applies the database updates with its own (administrator) account and then starts the site, so the account of the program does not need
                // the right to change the database structure. Nothing is served: the host is built, the updates are applied and the process ends (exit code 0, else an exception).
                using (IServiceScope scope = host.Services.CreateScope())
                    scope.ServiceProvider.GetRequiredService<IIdentityDbInitializer>().Initialize();
                return 0;
            }

            host.Run();
            return 0;
        }

        // .NET 10: the generic host replaces the obsolete WebHost.CreateDefaultBuilder; Startup.cs is still used.
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                    // ASP.NET Core 2.2 did not validate every registration at startup. Keep that behaviour
                    // until all registrations have been reviewed; set to true to surface DI problems early.
                    options.ValidateOnBuild = false;
                })
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}
