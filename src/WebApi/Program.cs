using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
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
