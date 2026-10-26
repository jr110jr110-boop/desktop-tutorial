using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Common.Extentions;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using IocConfig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Contracts;
using Services.Token;
using ViewModels.Identity.Settings;
using ViewModels.Mapping;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>
    /// The application services on the in-memory database (R1a tests): the real services, the real audit trail, a signed-in user set the way a
    /// request would set it. Every call gets a new scope, like a request.
    /// </summary>
    internal sealed class AppHost
    {
        private static readonly IPAddress ClientIp = IPAddress.Parse("10.9.8.7");

        private sealed class FakeDocuments : IDocumentService
        {
            public Task<Document> GetDocumentByIdAsync(Guid documentId) => throw new NotSupportedException();
            public Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto) => Task.FromResult(Guid.NewGuid());
            public Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId) => throw new NotSupportedException();
            public Task<bool> DeleteDocumentAsync(Guid image) => Task.FromResult(true);
            public Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto) => throw new NotSupportedException();
            public Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => Task.FromResult(false);
            public Task<string> GetFileHashAsync(Guid documentId) => Task.FromResult<string>(null);
        }

        public IServiceProvider Provider { get; }
        public IHttpContextAccessor Http { get; }

        public AppHost(string databaseName)
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", reloadOnChange: false, optional: false)
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("UseInMemoryDatabase", "true") })
                .Build();
            services.AddSingleton<IConfigurationRoot>(provider => configuration);
            services.AddSingleton<IConfiguration>(provider => configuration);
            services.Configure<SiteSettings>(options => configuration.Bind(options));
            services.Configure<BearerTokensOptions>(options =>
            {
                options.Key = "R1A-TESTS-KEY-0123456789-abcdefghijklmnopqrstuvwxyz";
                options.Issuer = "http://tests.local/";
                options.Audience = "Any";
                options.AccessTokenExpirationMinutes = 60;
                options.RefreshTokenExpirationMinutes = 10080;
            });

            services.AddScoped<IUnitOfWork, ApplicationDbContext>();
            services.AddCustomIdentityServices();
            services.AddScoped<IDocumentService, FakeDocuments>();   // the real one needs the file system

            // the same profiles as WebApi/Startup.cs (the legacy services map their entities with them)
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.ShouldMapMethod = method => method.IsPublic;
                cfg.AddProfile(new ProStatMappingProfile());
                cfg.AddProfile(new EquipmentAssemblyMappingProfile());
                cfg.AddProfile(new EquipmentBomMappingProfile());
                cfg.AddProfile(new ProgressLevelMappingProfile());
                cfg.AddProfile(new AssemblyMappingProfile());
                cfg.AddProfile(new BuyOrderProcessesMappingProfile());
                cfg.AddProfile(new InquiryMappingProfile());
                cfg.AddProfile(new BuyOrderMappingProfile());
                cfg.AddProfile(new ProviderMappingProfile());
                cfg.AddProfile(new TenderMappingProfile());
                cfg.AddProfile(new BomMappingProfile());
                cfg.AddProfile(new ContractMappingProfile());
                cfg.AddProfile(new ProcessMappingProfile());
                cfg.AddProfile(new MappingProfile());
                cfg.AddProfile(new IdentityMappingProfile());
                cfg.AddProfile(new EquipmentProcessMappingProfile());
                cfg.AddProfile(new WorkOrderMappingProfile());
            }).CreateMapper();
            services.AddSingleton(mapper);

            var siteSettings = services.GetSiteSettings();
            siteSettings.ActiveDatabase = ActiveDatabase.InMemoryDatabase;
            siteSettings.ConnectionStrings.LocalDb = new Localdb { InitialCatalog = databaseName };
            services.AddRequiredEfInternalServices(siteSettings);
            services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(siteSettings);
                optionsBuilder.UseInternalServiceProvider(serviceProvider);
            });

            Provider = services.BuildServiceProvider();
            Provider.RunScopedService<ApplicationDbContext>(context => context.Database.EnsureCreated());
            Http = Provider.GetRequiredService<IHttpContextAccessor>();
        }

        /// <summary>The EF model of the application database (which entity types and scalar properties exist).</summary>
        public IModel Model
        {
            get
            {
                using (IServiceScope scope = Provider.CreateScope())
                    return ((DbContext)scope.ServiceProvider.GetRequiredService<IUnitOfWork>()).Model;
            }
        }

        public static ClaimsPrincipal Principal(int id, string name, params string[] roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, name) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        private void SignIn(ClaimsPrincipal who)
        {
            var context = new DefaultHttpContext { User = who ?? new ClaimsPrincipal(new ClaimsIdentity()) };
            context.Connection.RemoteIpAddress = ClientIp;
            Http.HttpContext = context;
        }

        /// <summary>Runs a call the way a request would: a new scope and a signed-in (or anonymous) user.</summary>
        public async Task<T> As<T>(ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                SignIn(who);
                try { return await call(scope.ServiceProvider); }
                finally { Http.HttpContext = null; }
            }
        }

        /// <summary>The job runs without a user: no HTTP context at all.</summary>
        public async Task<T> AsSystem<T>(Func<IServiceProvider, Task<T>> call)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                Http.HttpContext = null;
                return await call(scope.ServiceProvider);
            }
        }

        /// <summary>Reads with a new scope and no user.</summary>
        public T Read<T>(Func<IUnitOfWork, T> read)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                Http.HttpContext = null;
                return read(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
            }
        }

        /// <summary>Writes with a new scope: the user (when given) is the one the audit trail records. The action does its own SaveChanges.</summary>
        public void Write(Action<IUnitOfWork> write, ClaimsPrincipal who = null)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                SignIn(who);
                try { write(scope.ServiceProvider.GetRequiredService<IUnitOfWork>()); }
                finally { Http.HttpContext = null; }
            }
        }

        /// <summary>Adds an entity and gives every required text column a value (the in-memory database checks them, and the tests only care about a few fields).</summary>
        public static T Add<T>(IUnitOfWork uow, T entity) where T : class
        {
            uow.Set<T>().Add(entity);
            foreach (PropertyEntry p in uow.Entry(entity).Properties)
                if (!p.Metadata.IsNullable && p.Metadata.ClrType == typeof(string) && p.CurrentValue == null)
                    p.CurrentValue = "x";
            return entity;
        }
    }
}
