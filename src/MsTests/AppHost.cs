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
            public Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto) => Task.FromResult(true);
            public Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto) => throw new NotSupportedException();
            public Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => Task.FromResult(false);
            public Task<string> GetFileHashAsync(Guid documentId) => Task.FromResult<string>(null);
        }

        public IServiceProvider Provider { get; }
        public IHttpContextAccessor Http { get; }

        public AppHost(string databaseName) : this(databaseName, null)
        {
        }

        /// <summary>The name of the in-memory database of this host.</summary>
        public string DatabaseName { get; }

        private readonly SiteSettings _siteSettings;

        /// <summary>
        /// Options for a context of another type on the same in-memory database and the same internal service provider, built the way the application's own context is. For tests that must change
        /// what the application's context refuses to change (a row of the append-only audit trail).
        /// </summary>
        public DbContextOptions<T> RawOptions<T>() where T : DbContext
        {
            var builder = new DbContextOptionsBuilder<T>();
            builder.SetDbContextOptions(_siteSettings);
            builder.UseInternalServiceProvider(Provider);
            return builder.Options;
        }

        /// <param name="settings">Extra configuration values (for example <c>Identity:AuditHmacKey</c>) that win over appsettings.json.</param>
        public AppHost(string databaseName, IDictionary<string, string> settings)
        {
            DatabaseName = databaseName;
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", reloadOnChange: false, optional: false)
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("UseInMemoryDatabase", "true") })
                .AddInMemoryCollection(settings ?? new Dictionary<string, string>())
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
            _siteSettings = siteSettings;
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

        /// <summary>
        /// Package A1: the design verification gates (BOM and drawing release, readiness item, FCA) are ON by default (the strict production default). The legacy tests release BOMs and drawings without a design plan,
        /// so they switch the gates off with this call (the same way WithoutReadinessGate switches the readiness gate off); the gates themselves are tested in PackageA1DesignControlTests.
        /// </summary>
        public AppHost WithoutDesignControl()
        {
            SetDesignControl(false);
            return this;
        }

        /// <summary>Package A1: writes the two design control settings at their strict value (they are on by default; this call is only needed after WithoutDesignControl).</summary>
        public AppHost WithDesignControl()
        {
            SetDesignControl(true);
            return this;
        }

        private void SetDesignControl(bool on)
        {
            Write(uow =>
            {
                foreach (string key in new[] { Services.NasbNiro.ProductBank.DesignSettingKeys.VerificationRequired, Services.NasbNiro.ProductBank.DesignSettingKeys.ValidationRequired })
                {
                    Entities.Model.NasbNiro.Quality.QualitySetting row = uow.Set<Entities.Model.NasbNiro.Quality.QualitySetting>().FirstOrDefault(x => x.Key == key);
                    if (row == null) Add(uow, new Entities.Model.NasbNiro.Quality.QualitySetting { Key = key, Value = on ? 1 : 0, Title = "design control (test)", Source = "test" });
                    else row.Value = on ? 1 : 0;
                }
                uow.SaveChanges();
            });
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

        /// <summary>PB8: the legacy product-bank and balance tests do not build the engineering data of a project (WBS template, OPC, drawings); they switch the readiness gate off by its master setting. The gate itself is tested in OpsDataTests.</summary>
        public AppHost WithoutReadinessGate()
        {
            Write(uow =>
            {
                Add(uow, new Entities.Model.NasbNiro.Quality.QualitySetting { Key = Services.NasbNiro.OpsData.OpsReadinessCatalog.MasterSwitch, Value = 0, Title = "readiness gate off (test)", Source = "test" });
                uow.SaveChanges();
            });
            return this;   // fix round 1 (M4): the design control stays ON; a test that releases a BOM gives it design data (DesignTestData)
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
