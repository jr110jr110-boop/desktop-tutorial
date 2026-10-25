using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Common.Extentions;
using Entities.Identity;
using IocConfig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts.Identity;
using ViewModels.Identity.Settings;

namespace MsTests
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2510
    /// </summary>
    [TestClass]
    public class CoreTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsSnapshot<SiteSettings> _siteSettings;


        public CoreTests()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            // The web host registers logging in the real application; the managers need ILogger<T>.
            services.AddLogging();
            services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();

            //var configuration = new ConfigurationBuilder()
            //                    .AddJsonFile("appsettings.json", reloadOnChange: true, optional: false)
            //                    .Build();

            var configuration = new ConfigurationBuilder()
                                .AddJsonFile("appsettings.json", reloadOnChange: true, optional: false)
                                .AddInMemoryCollection(new[]
                                  {
                                      new KeyValuePair<string,string>("UseInMemoryDatabase", "true"),
                                  })
                                .Build();
            services.AddSingleton<IConfigurationRoot>(provider => configuration);
            services.Configure<SiteSettings>(options => configuration.Bind(options));
            //services.AddEntityFrameworkInMemoryDatabase().AddDbContext<ApplicationDbContext>(ServiceLifetime.Scoped);


            services.AddScoped<IUnitOfWork, ApplicationDbContext>();

            // Adds all of the ASP.NET Core Identity related services and configurations at once.
            services.AddCustomIdentityServices();


            var siteSettings = services.GetSiteSettings();
            siteSettings.ActiveDatabase = ActiveDatabase.InMemoryDatabase;
            // appsettings.json has no LocalDb section; the in-memory database only needs a name.
            siteSettings.ConnectionStrings.LocalDb ??= new Localdb { InitialCatalog = "IRNNCO_Tests" };

            services.AddRequiredEfInternalServices(siteSettings); // It's added to access services from the dbcontext, remove it if you are using the normal `AddDbContext` and normal constructor dependency injection.
            services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(siteSettings);
                optionsBuilder.UseInternalServiceProvider(serviceProvider); // It's added to access services from the dbcontext, remove it if you are using the normal `AddDbContext` and normal constructor dependency injection.
            });

            _serviceProvider = services.BuildServiceProvider();

            // An in-memory database receives the HasData seed rows (roles, role claims) only through EnsureCreated.
            _serviceProvider.RunScopedService<ApplicationDbContext>(context => context.Database.EnsureCreated());

            _siteSettings = _serviceProvider.GetRequiredService<IOptionsSnapshot<SiteSettings>>();
            //_siteSettings.Value.ActiveDatabase = ActiveDatabase.InMemoryDatabase;

            var identityDbInitialize = _serviceProvider.GetRequiredService<IIdentityDbInitializer>();
            identityDbInitialize.SeedData();

        }

        [TestMethod]
        public void Test_UserAdmin_Exists()
        {

            _serviceProvider.RunScopedService<IUnitOfWork>(context =>
              {
                  var users = context.Set<User>();
                  Assert.IsTrue(users.Any(x => x.UserName == "Admin"));
              });
        }


        [TestMethod]
        public void Sales_Temporary_Keys_Are_Fixed_Up_In_One_Save()
        {
            _serviceProvider.RunScopedService<IUnitOfWork>(uow =>
            {
                var customer = new Entities.Model.NasbNiro.Finance.FnCustomer { Name = "مشتری آزمون" };
                uow.Set<Entities.Model.NasbNiro.Finance.FnCustomer>().Add(customer);
                var profile = new Entities.Model.NasbNiro.Sales.SlCustomerProfile { CustomerId = customer.Id };
                uow.Set<Entities.Model.NasbNiro.Sales.SlCustomerProfile>().Add(profile);
                uow.SaveChanges();
                Assert.IsTrue(customer.Id > 0);
                Assert.AreEqual(customer.Id, profile.CustomerId);
            });
        }
    }
}