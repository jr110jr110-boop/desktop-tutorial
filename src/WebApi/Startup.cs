using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using Common.WebToolkit;
using DataLayer.Context;
using IocConfig;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;
using Services.Identity.Logger;
using Services.Token;
using ViewModels.Identity.Settings;
using ViewModels.Mapping;
using WebApi.Extensions;

namespace WebApi
{
    public class Startup
    {
        public const string CorsPolicyName = "CorsPolicy";

        // The generic host (ASP.NET Core 3+) only injects IConfiguration/IWebHostEnvironment here,
        // so the logger is now taken as a parameter of Configure().
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCustomAuthorization();

            services.Configure<BearerTokensOptions>(options => Configuration.GetSection("BearerTokens").Bind(options));
            services.Configure<ApiSettings>(options => Configuration.GetSection("ApiSettings").Bind(options));
            services.Configure<SiteSettings>(options => Configuration.Bind(options));

            services.AddScoped<IAntiForgeryCookieService, AntiForgeryCookieService>();

            // Adds all of the ASP.NET Core Identity related services and configurations at once.
            services.AddCustomIdentityServices();

            var siteSettings = services.GetSiteSettings();
            services.AddRequiredEfInternalServices(siteSettings); // It's added to access services from the dbcontext, remove it if you are using the normal `AddDbContext` and normal constructor dependency injection.
            services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(siteSettings);
                optionsBuilder.UseInternalServiceProvider(serviceProvider); // It's added to access services from the dbcontext, remove it if you are using the normal `AddDbContext` and normal constructor dependency injection.
            });

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
                .AddJwtBearer(cfg =>
                {
                    cfg.RequireHttpsMetadata = false;
                    cfg.SaveToken = true;
                    // Keep the claim types exactly as they are written in the token (same as the old
                    // `InboundClaimTypeMap = new Dictionary<string, string>()` setting in ASP.NET Core 2.2).
                    cfg.MapInboundClaims = false;
                    cfg.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = Configuration["BearerTokens:Issuer"], // site that makes the token
                        ValidateIssuer = false, // TODO: change this to avoid forwarding attacks
                        ValidAudience = Configuration["BearerTokens:Audience"], // site that consumes the token
                        ValidateAudience = false, // TODO: change this to avoid forwarding attacks
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["BearerTokens:Key"])),
                        ValidateIssuerSigningKey = true, // verify signature to avoid tampering
                        ValidateLifetime = true, // validate the expiration
                        ClockSkew = TimeSpan.Zero // tolerance for the expiration date
                    };
                });

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Home/Login";
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Response.StatusCode == 200)
                        {
                            ctx.Response.StatusCode = 401;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            });

            services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicyName, builder =>
                {
                    var allowedOrigins = Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                    if (allowedOrigins != null && allowedOrigins.Length > 0)
                    {
                        builder.WithOrigins(allowedOrigins)
                               .AllowAnyMethod()
                               .AllowAnyHeader()
                               .AllowCredentials();
                    }
                    else
                    {
                        // ASP.NET Core 3+ refuses "*" together with AllowCredentials() at startup.
                        // The front-end sends the JWT in the Authorization header (no cookies),
                        // so an open policy without credentials behaves the same for it.
                        builder.AllowAnyOrigin()
                               .AllowAnyMethod()
                               .AllowAnyHeader();
                    }
                });
            });

            var mappingConfig = new MapperConfiguration(cfg =>
            {
                // AutoMapper 10 scans every extension method of System.Linq.Enumerable, including private ones.
                // .NET 7+ added private generic-math helpers (for example MaxFloat<T>) whose type constraints make
                // that scan throw at startup ("violates the constraint of type 'T'"). Only public methods are
                // meant to be used for mapping, so limit the scan to them (AutoMapper 12+ handles this itself).
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
            });

            IMapper mapper = mappingConfig.CreateMapper();

            services.AddSingleton(mapper);

            services.AddMvc(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                // Same Persian ی/ک model binder as before (it used to come from DNTCommon.Web.Core).
                options.UseYeKeModelBinder();
                options.AllowEmptyInputInBodyModelBinding = true;
            })
            // ASP.NET Core 3+ uses System.Text.Json by default; keep Newtonsoft.Json with the same settings.
            .AddNewtonsoftJson(jsonOptions =>
            {
                jsonOptions.SerializerSettings.ReferenceLoopHandling =
                    Newtonsoft.Json.ReferenceLoopHandling.Ignore;

                jsonOptions.SerializerSettings.NullValueHandling =
                    Newtonsoft.Json.NullValueHandling.Include;

                jsonOptions.SerializerSettings.ContractResolver = new DefaultContractResolver()
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    (true, true, true)
                };
            });

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Nasb Niro HTTP API",
                    Version = "v1",
                    Description = "Nasb Niro Service HTTP API",
                    TermsOfService = new Uri("https://example.com/terms"),
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description =
                    "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 12345abcdef\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement()
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            },
                            Scheme = "oauth2",
                            Name = "Bearer",
                            In = ParameterLocation.Header,
                        },
                        new List<string>()
                    }
                });
            });

            services.AddResponseCompression(options =>
            {
                options.Providers.Add<GzipCompressionProvider>();
                options.EnableForHttps = true;
            });

            services.AddResponseCaching();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
        }

        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory,
            ILogger<Startup> logger)
        {
            app.UseExceptionHandler(appBuilder =>
            {
                appBuilder.Use(async (context, next) =>
                {
                    var error = context.Features[typeof(IExceptionHandlerFeature)] as IExceptionHandlerFeature;
                    if (error?.Error is SecurityTokenExpiredException)
                    {
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            State = 401,
                            Msg = "token expired"
                        }));
                    }
                    else if (error?.Error != null)
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            State = 500,
                            Msg = error.Error.Message
                        }));

                        logger.LogCritical(error.Error, error.Error.Message);
                    }
                    else
                    {
                        await next();
                    }
                });
            });

            loggerFactory
                .AddDbLogger(serviceProvider: app.ApplicationServices, minLevel: LogLevel.Warning);

            if (!env.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStatusCodePages();
            app.UseResponseCompression();

            // Applies pending database migrations and seeds the admin user/roles.
            app.UseCustomIdentityServices();

            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("fa-IR"),
            };

            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("fa-IR"),
                // Formatting numbers, dates, etc.
                SupportedCultures = supportedCultures,
                // UI strings that we have localized.
                SupportedUICultures = supportedCultures,

                RequestCultureProviders = new List<IRequestCultureProvider>()
                {
                    new QueryStringRequestCultureProvider(),
                    new CookieRequestCultureProvider()
                }
            });

            var pathBase = this.Configuration["PATH_BASE"];

            app.UseSwagger()
               .UseSwaggerUI(c =>
               {
                   c.SwaggerEndpoint($"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json", "Nasb Niro.API V1");
                   c.OAuthClientId("Nasb Niro");
                   c.OAuthAppName("Nasb Niro Swagger UI");
               });

            // Endpoint routing (replaces app.UseMvc of ASP.NET Core 2.2).
            app.UseRouting();
            // With endpoint routing, CORS must sit between UseRouting and UseEndpoints; otherwise controllers
            // with [EnableCors] (AdminSettings, ApiSettings) fail with "a middleware was not found that supports CORS".
            app.UseCors(CorsPolicyName);
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapControllerRoute(
                    name: "areas",
                    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                // The Vue front-end can be published into wwwroot (deploy/install.ps1 does this), so the whole
                // system runs as one IIS site. Browser addresses such as /contracts/12 are neither files nor API
                // endpoints: they get index.html and the Vue router shows the page. Unknown /api addresses keep
                // their 404, and without a front-end in wwwroot nothing changes.
                endpoints.MapFallback(async context =>
                {
                    var indexFile = env.WebRootFileProvider.GetFileInfo("index.html");
                    var isPageRequest = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
                    if (!isPageRequest || context.Request.Path.StartsWithSegments("/api") || !indexFile.Exists)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.SendFileAsync(indexFile);
                }).AllowAnonymous();
            });

            // As before, the middlewares below only run for requests that are not handled by a controller.
            app.UseResponseCaching();

            app.Use(async (context, next) =>
            {
                string path = context.Request.Path;

                if (
                    path.EndsWith(".css") ||
                    path.EndsWith(".js") ||
                    path.EndsWith(".webp") ||
                    path.EndsWith(".jpg") ||
                    path.EndsWith(".png"))
                {
                    // Cache static assets for 1 day
                    TimeSpan maxAge = new TimeSpan(1, 0, 0, 0);
                    context.Response.Headers.Append("Cache-Control", "max-age=" + maxAge.TotalSeconds.ToString("0"));
                }
                else
                {
                    context.Response.Headers.Append("Cache-Control", "no-cache");
                    context.Response.Headers.Append("Cache-Control", "private, no-store");
                }
                await next();
            });

            app.UseStaticFiles();
        }
    }
}
