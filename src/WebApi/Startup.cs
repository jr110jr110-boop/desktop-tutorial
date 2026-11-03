using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

        /// <summary>R1a2 (M1): the rate-limit policy of the calls anybody can make without a token (sign in, refresh, temporary password, sign out). Put on the actions with [EnableRateLimiting].</summary>
        public const string IdentityRateLimitPolicy = "identity";

        /// <summary>What a caller reads when the server fails (the details are in the log only).</summary>
        public const string GenericServerErrorText = "خطای داخلی سرور رخ داد و در گزارش‌های سامانه ثبت شد. اگر تکرار شد، به مدیر سامانه اطلاع دهید.";

        // The generic host (ASP.NET Core 3+) only injects IConfiguration/IWebHostEnvironment here,
        // so the logger is now taken as a parameter of Configure().
        public Startup(IConfiguration configuration)
        {
            Services.NasbNiro.Finance.FinanceFeatures.Configure(configuration);
            // R1a2 (M4): the secret of the keyed hash that the audit trail writes next to a masked national code or phone number (absent: only the masked text is written)
            AuditHmac.Configure(configuration["Identity:AuditHmacKey"]);
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

            // R1a: the issuer and the audience of a token are checked against the same settings the token factory writes into the token.
            // (Empty settings would make every token invalid, so a missing value only switches that one check off; Configure() logs a warning for it.)
            string tokenIssuer = Configuration["BearerTokens:Issuer"];
            string tokenAudience = Configuration["BearerTokens:Audience"];

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
                        ValidIssuer = tokenIssuer, // site that makes the token
                        ValidateIssuer = !string.IsNullOrWhiteSpace(tokenIssuer), // avoids forwarding attacks
                        ValidAudience = tokenAudience, // site that consumes the token
                        ValidateAudience = !string.IsNullOrWhiteSpace(tokenAudience), // avoids forwarding attacks
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["BearerTokens:Key"])),
                        ValidateIssuerSigningKey = true, // verify signature to avoid tampering
                        ValidateLifetime = true, // validate the expiration
                        ClockSkew = TimeSpan.Zero // tolerance for the expiration date
                    };
                    cfg.Events = new JwtBearerEvents
                    {
                        // R1a: a token is valid only for an account that still exists and is active (a deleted or deactivated user is cut off at once,
                        // not when the access token expires) and only an access token counts (a refresh token carries no user id).
                        OnTokenValidated = JwtAccountStatus.ValidateAsync,
                        // R1a: a refusal of the bearer handler (no valid token: 401; the role attribute of the action does not allow the user: 403)
                        // answers the same ResponseDto JSON as the services do, instead of an empty body that UseStatusCodePages turns into plain text.
                        OnChallenge = JwtBearerRefusals.OnChallengeAsync,
                        OnForbidden = JwtBearerRefusals.OnForbiddenAsync,
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
                        // R1a2 (M1): no origin is listed, so no other origin is allowed (it used to be every origin). The front-end is served from the same
                        // address as the API, and a call from the same origin is not touched by CORS at all.
                        builder.SetIsOriginAllowed(origin => false);
                    }
                });
            });

            // R1a2 (M1): a fixed window per client address on the calls that anybody can make without a token. The limit is Identity:RateLimitPerMinute (default 20);
            // the CI smoke raises it because it signs in many test users from one address.
            IdentitySecurityOptions identityRules = Configuration.GetSection("Identity").Get<IdentitySecurityOptions>() ?? new IdentitySecurityOptions();
            int permitsPerMinute = Math.Max(1, identityRules.RateLimitPerMinute);
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(IdentityRateLimitPolicy, httpContext => IdentityRateLimit.Partition(httpContext, permitsPerMinute));
                options.OnRejected = (context, cancellationToken) =>
                {
                    TimeSpan? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) ? wait : (TimeSpan?)null;
                    return new ValueTask(JwtBearerRefusals.WriteTooManyRequestsAsync(context.HttpContext.Response, retryAfter));
                };
            });

            // R1a2 (M6): the browser is told to use HTTPS only (sent on HTTPS answers; the loopback host names are skipped by default)
            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
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
                // camelCase, nulls written, reference loops ignored (R1a: the same code writes the 401 / 403 of the bearer handler, see JwtBearerRefusals)
                ApiJson.Apply(jsonOptions.SerializerSettings);
            });

            // R1a2 (M8): the prices and the bank accounts of the legacy DTOs are written only for the roles that work with them (see LegacyFieldAccess); the user is the one of the current request
            services.AddOptions<Microsoft.AspNetCore.Mvc.MvcNewtonsoftJsonOptions>().Configure<IHttpContextAccessor>((jsonOptions, accessor) =>
                ApiJson.HideSensitiveFields(jsonOptions.SerializerSettings, () => accessor.HttpContext?.User));

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

        /// <summary>Start-up warnings for settings that leave the API more open than intended (nothing is changed here).</summary>
        private void WarnAboutOpenSettings(ILogger<Startup> logger)
        {
            string[] origins = Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (origins == null || origins.Length == 0)
                logger.LogInformation("CORS: Cors:AllowedOrigins is empty, so browsers of other origins are refused (the front-end must be served from the same address as the API). List the front-end address(es) in the settings file to allow another origin.");
            if (string.IsNullOrWhiteSpace(Configuration["BearerTokens:Issuer"]))
                logger.LogWarning("BearerTokens:Issuer is empty: the issuer of a token is not checked.");
            if (string.IsNullOrWhiteSpace(Configuration["BearerTokens:Audience"]))
                logger.LogWarning("BearerTokens:Audience is empty: the audience of a token is not checked.");
        }

        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory,
            ILogger<Startup> logger)
        {
            WarnAboutOpenSettings(logger);

            // R1a2 (L7): outside Development the server does not start with the signing key that ships with the source code
            IReadOnlyList<string> fatal = StartupSecurityChecks.Problems(Configuration, env.IsDevelopment());
            if (fatal.Count > 0)
                throw new InvalidOperationException("The server does not start with these settings: " + string.Join(" ", fatal));

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
                        // R1a: the caller gets a general text only; the exception (message, stack, inner exceptions) goes to the log
                        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            State = 500,
                            Msg = GenericServerErrorText
                        }));

                        logger.LogCritical(error.Error, "Unhandled exception on {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, error.Error.Message);
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

            // R1a2 (M6): the redirect to HTTPS does not apply to the server itself (install checks and the recovery sign-in use the plain loopback address)
            app.UseWhen(
                context => context.Connection.RemoteIpAddress != null && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress),
                branch => branch.UseHttpsRedirection());

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
            app.UseRateLimiter();
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
