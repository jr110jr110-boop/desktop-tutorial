using System;
using System.Collections.Generic;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Common.Extentions;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model;
using Entities.Model.NasbNiro.Finance;
using IocConfig;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.Identity.AccountServices;
using Services.Resources;
using Services.Token;
using ViewModels.Identity.Settings;
using ViewModels.Identity.UserDtos;
using ViewModels.Mapping;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;

namespace MsTests
{
    /// <summary>
    /// Security review R1a: user administration is for the PMO only, passwords are changed by their owner with the current password,
    /// an administrator can only set a temporary password, nobody changes his or her own roles, a sensitive role needs a second PMO user,
    /// the sign-in lock-out applies to everybody, and the events are written to the audit trail.
    /// The tests run the real AccountService with the real Identity services on the in-memory database.
    /// </summary>
    [TestClass]
    public partial class IdentityTests
    {
        private const string AdminPassword = "SET-BY-INSTALL-ps1"; // the placeholder of appsettings.json (the seed user's password in the tests)
        private const string GoodPassword = "Zx-Secret-2026k";

        private static readonly IPAddress ClientIp = IPAddress.Parse("10.1.2.3");
        private static readonly Lazy<Host> LazyHost = new Lazy<Host>(() => new Host());
        private static Host H => LazyHost.Value;
        private static int _counter;
        private static string Unique(string prefix) => prefix + System.Threading.Interlocked.Increment(ref _counter);

        // ---------------------------------------------------------------------------------------------------- test host

        private sealed class FakeDocuments : IDocumentService
        {
            public Task<Document> GetDocumentByIdAsync(Guid documentId) => throw new NotSupportedException();
            public Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto) => Task.FromResult(Guid.NewGuid());
            public Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId) => throw new NotSupportedException();
            public Task<bool> DeleteDocumentAsync(Guid image) => Task.FromResult(true);
            public Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto) => throw new NotSupportedException();
            public Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => Task.FromResult(false);
            public Task<string> GetFileHashAsync(Guid documentId) => Task.FromResult<string>(null);
        }

        /// <summary>Keeps what the services write to the log, so a test can look at the level of a message.</summary>
        private sealed class LogCollector : ILoggerProvider
        {
            private readonly List<(LogLevel Level, string Category, string Text)> _entries = new List<(LogLevel, string, string)>();

            public IReadOnlyList<(LogLevel Level, string Category, string Text)> Entries
            {
                get { lock (_entries) return _entries.ToList(); }
            }

            public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

            public void Dispose() { }

            private sealed class Sink : ILogger
            {
                private readonly LogCollector _owner;
                private readonly string _category;
                public Sink(LogCollector owner, string category) { _owner = owner; _category = category; }
                public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                {
                    lock (_owner._entries) _owner._entries.Add((logLevel, _category, formatter(state, exception)));
                }
            }
        }

        private sealed class Host
        {
            public IServiceProvider Provider { get; }
            public IHttpContextAccessor Http { get; }
            public int AdminId { get; }

            /// <summary>The settings of this host; a test may change a value (the options are read again for every scope).</summary>
            public IConfigurationRoot Configuration { get; }

            /// <summary>What the services wrote to the log (level and text), for the tests that look at the level of a message.</summary>
            public LogCollector Logs { get; } = new LogCollector();

            /// <summary>A host with its own in-memory database: the seeded administrator is the only PMO user, so tests that depend on the number of PMO users start from a known state.</summary>
            public Host(string databaseName = null, IDictionary<string, string> settings = null)
            {
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddLogging(logging => logging.AddProvider(Logs));
                services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();

                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", reloadOnChange: false, optional: false)
                    .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("UseInMemoryDatabase", "true") }.Concat(settings ?? new Dictionary<string, string>()))
                    .Build();
                Configuration = configuration;
                services.AddSingleton<IConfigurationRoot>(provider => configuration);
                services.Configure<SiteSettings>(options => configuration.Bind(options));
                services.Configure<BearerTokensOptions>(options =>
                {
                    options.Key = "IDENTITY-TESTS-KEY-0123456789-abcdefghijklmnopqrstuvwxyz";
                    options.Issuer = "http://tests.local/";
                    options.Audience = "Any";
                    options.AccessTokenExpirationMinutes = 60;
                    options.RefreshTokenExpirationMinutes = 10080;
                    options.AllowMultipleLoginsFromTheSameUser = false;
                    options.AllowSignoutAllUserActiveClients = true;
                });

                services.AddScoped<IUnitOfWork, ApplicationDbContext>();
                services.AddCustomIdentityServices();
                // the real document service needs the file system; the account service only calls it for profile photos
                services.AddScoped<IDocumentService, FakeDocuments>();

                var mapper = new MapperConfiguration(cfg =>
                {
                    cfg.ShouldMapMethod = method => method.IsPublic;
                    cfg.AddProfile(new MappingProfile());
                    cfg.AddProfile(new IdentityMappingProfile());
                }).CreateMapper();
                services.AddSingleton(mapper);

                var siteSettings = services.GetSiteSettings();
                siteSettings.ActiveDatabase = ActiveDatabase.InMemoryDatabase;
                if (databaseName != null || siteSettings.ConnectionStrings.LocalDb == null)
                    siteSettings.ConnectionStrings.LocalDb = new Localdb { InitialCatalog = databaseName ?? "IRNNCO_IdentityTests" };
                services.AddRequiredEfInternalServices(siteSettings);
                services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
                {
                    optionsBuilder.SetDbContextOptions(siteSettings);
                    optionsBuilder.UseInternalServiceProvider(serviceProvider);
                });

                Provider = services.BuildServiceProvider();
                Provider.RunScopedService<ApplicationDbContext>(context => context.Database.EnsureCreated());
                Provider.GetRequiredService<IIdentityDbInitializer>().SeedData();
                Http = Provider.GetRequiredService<IHttpContextAccessor>();
                AdminId = Provider.RunScopedService<int, IUnitOfWork>(uow => uow.Set<User>().Single(u => u.UserName == "Admin").Id);
            }

            public ClaimsPrincipal Admin => Principal(AdminId, "Admin", "PMO");

            public static ClaimsPrincipal Principal(int id, string name, params string[] roles)
            {
                var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, name) };
                claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
                return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            }

            /// <summary>Calls the account service the way a request would: a new scope and a signed-in (or anonymous) user.</summary>
            public Task<T> As<T>(ClaimsPrincipal who, Func<IAccountService, Task<T>> call) => From(ClientIp, who, call);

            /// <summary>The same call from another client address; <paramref name="forwardedFor"/> adds the header a reverse proxy would add.</summary>
            public async Task<T> From<T>(IPAddress address, ClaimsPrincipal who, Func<IAccountService, Task<T>> call, string forwardedFor = null)
            {
                using (IServiceScope scope = Provider.CreateScope())
                {
                    var context = new DefaultHttpContext { User = who ?? new ClaimsPrincipal(new ClaimsIdentity()) };
                    context.Connection.RemoteIpAddress = address;
                    if (forwardedFor != null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
                    Http.HttpContext = context;
                    var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
                    return await call(service);
                }
            }

            /// <summary>
            /// A PMO user made the way the seeded administrator was: the role is put in directly (no request, no bootstrap, no second approval), so a test can have exactly the PMO users it needs.
            /// With <paramref name="grantedBy"/> the record of who gave the role is kept as well (and how long ago, for the minimum-days rule).
            /// </summary>
            public async Task<int> AddPmoAsync(string userName, int grantedBy = 0, bool bootstrap = false, int ageDays = 30)
            {
                int id = await RegisterAsync(Admin, userName, new[] { WarehouseRole });
                using (IServiceScope scope = Provider.CreateScope())
                {
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    // the registration needs a role; the user is to hold the PMO role alone
                    uow.Set<UserRole>().RemoveRange(uow.Set<UserRole>().Where(r => r.UserId == id).ToList());
                    uow.Set<UserRole>().Add(new UserRole { UserId = id, RoleId = PmoRole });
                    if (grantedBy > 0)
                        uow.Set<UserClaim>().Add(new UserClaim
                        {
                            UserId = id, ClaimType = IdentityRules.RoleGrantedClaimType,
                            ClaimValue = new RoleGrantRecord { Role = "PMO", GrantedBy = grantedBy, ApprovedBy = bootstrap ? 0 : grantedBy + 1000, At = DateTimeOffset.Now.AddDays(-ageDays), Bootstrap = bootstrap }.ToClaimValue(),
                        });
                    uow.SaveChanges();
                }
                return id;
            }

            /// <summary>Makes the record of the PMO role of a user <paramref name="days"/> days older (the approver rule asks how long the role has been held).</summary>
            public void AgeGrantRecord(int userId, int days) => Write(uow =>
            {
                foreach (UserClaim row in uow.Set<UserClaim>().Where(c => c.UserId == userId && c.ClaimType == IdentityRules.RoleGrantedClaimType).ToList())
                    if (RoleGrantRecord.TryParse(row.ClaimValue, out RoleGrantRecord record))
                    {
                        record.At = record.At.AddDays(-days);
                        row.ClaimValue = record.ToClaimValue();
                    }
            });

            public string[] RolesOf(int userId) => Provider.RunScopedService<string[], IApplicationUserManager>(m =>
                m.GetRolesAsync(m.Users.First(u => u.Id == userId)).GetAwaiter().GetResult().OrderBy(x => x).ToArray());

            /// <summary>Changes a row of the database directly (to make a request or a record old, to deactivate a user ...).</summary>
            public void Write(Action<IUnitOfWork> change)
            {
                using (IServiceScope scope = Provider.CreateScope())
                {
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    change(uow);
                    uow.SaveChanges();
                }
            }

            /// <summary>
            /// The account check that every request with a bearer token goes through (the code Startup assigns to OnTokenValidated), run for this access token against the database.
            /// Returns the reason the token is refused, or null when it is accepted.
            /// </summary>
            public async Task<string> TokenProblemAsync(string accessToken, string path = "/api/v1/Contracts")
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
                var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test"));
                using (IServiceScope scope = Provider.CreateScope())
                {
                    var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal };
                    http.Request.Path = path;
                    var context = new TokenValidatedContext(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions()) { Principal = principal };
                    await JwtAccountStatus.ValidateAsync(context);
                    return context.Result?.Failure?.Message;
                }
            }

            public T Query<T>(Func<IUnitOfWork, T> read)
            {
                using (IServiceScope scope = Provider.CreateScope())
                    return read(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
            }

            public async Task<int> RegisterAsync(ClaimsPrincipal by, string userName, int[] roleIds, string password = GoodPassword)
            {
                ResponseDto<ResponseUserDto> r = await As(by, s => s.RegisterAsync(NewUser(userName, roleIds, password), default).AsTask());
                Assert.IsTrue(r.Success, "register " + userName + ": " + r.Message);
                return r.Data.Id;
            }
        }

        private static RegisterUserDto NewUser(string userName, int[] roleIds, string password) => new RegisterUserDto
        {
            UserName = userName, Name = userName, LastName = "آزمون", Email = userName + "@example.com", RoleIds = roleIds, Password = password,
            PhoneNumber = "09120000000", BirthDate = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero), NationalCode = "0012345678",
        };

        private static UpdateUserDto Edit(int id, string email, int[] roleIds) => new UpdateUserDto
        {
            Id = id, Email = email, EmailConfirmed = true, PhoneNumber = "09120000000", PhoneNumberConfirmed = true,
            Name = "edited", LastName = "آزمون", NationalCode = "0012345678", BirthDate = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero), RoleIds = roleIds,
        };

        private static readonly int PmoRole = RoleConstants.PMOResponseRole.Id;
        private static readonly int WarehouseRole = RoleConstants.WareHouseResponseRole.Id;
        private static readonly int FinanceManagerRole = RoleConstants.FinanceManagerResponseRole.Id;
        private static readonly int CeoRole = RoleConstants.CeoResponseRole.Id;

        private static string[] RolesOf(int userId) => H.RolesOf(userId);

        private static ClaimsPrincipal Signed(int id, string name, params string[] roles) => Host.Principal(id, name, roles);

        // ---------------------------------------------------------------------------------------------------- pure rules

        [TestMethod]
        public void Sensitive_Roles_Are_The_Seven_Of_The_Review_Pmo_Included()
        {
            // the six of the first review and (R1a2, H1) the PMO role itself
            foreach (string role in new[] { "PMO", "pmo", "FINANCEMANAGER", "PAYROLL", "CEO", "AUDITOR", "HR", "SECRETARIAT", "ceo", " Hr " })
                Assert.IsTrue(IdentityRules.IsSensitiveRole(role), role);
            foreach (string role in new[] { "FINANCIAL", "WAREHOUSE", "ENGINEERING", "", null })
                Assert.IsFalse(IdentityRules.IsSensitiveRole(role), role ?? "null");
            Assert.AreEqual(7, IdentityRules.SensitiveRoles.Length);
        }

        [TestMethod]
        public void Role_Plan_Gives_Ordinary_Roles_At_Once_And_Holds_Back_Sensitive_Ones()
        {
            IdentityRules.RolePlan plan = IdentityRules.PlanRoles(new[] { "WAREHOUSE", "PAYROLL" }, new[] { "warehouse", "ENGINEERING", "CEO", "FINANCEMANAGER" }, new[] { "FINANCEMANAGER" });
            CollectionAssert.AreEqual(new[] { "ENGINEERING" }, plan.AddNow);
            CollectionAssert.AreEqual(new[] { "CEO" }, plan.NeedApproval);
            CollectionAssert.AreEqual(new[] { "FINANCEMANAGER" }, plan.AlreadyPending);
            CollectionAssert.AreEqual(new[] { "PAYROLL" }, plan.Remove); // taking a role away is immediate, sensitive or not
            Assert.IsFalse(plan.IsEmpty);
            Assert.IsTrue(IdentityRules.PlanRoles(new[] { "CEO" }, new[] { "ceo" }, null).IsEmpty);
        }

        [TestMethod]
        public void Approval_Needs_Somebody_Who_Is_Neither_The_Requester_Nor_The_Receiver()
        {
            Assert.IsNull(IdentityRules.ApprovalProblem(requestedBy: 1, approver: 2, receiver: 3));
            Assert.AreEqual("SameApprover", IdentityRules.ApprovalProblem(1, 1, 3));
            Assert.AreEqual("ApproverIsReceiver", IdentityRules.ApprovalProblem(1, 3, 3));
            Assert.AreEqual("NotSignedIn", IdentityRules.ApprovalProblem(1, 0, 3));
        }

        [TestMethod]
        public void Pending_Grant_Round_Trips_And_Expires_After_Seven_Days()
        {
            var now = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(3.5));
            var grant = new PendingRoleGrant { Role = "ceo", RequestedBy = 5, RequestedAt = now };
            Assert.IsTrue(PendingRoleGrant.TryParse(grant.ToClaimValue(), out PendingRoleGrant back));
            Assert.AreEqual("CEO", back.Role);
            Assert.AreEqual(5, back.RequestedBy);
            Assert.AreEqual(now, back.RequestedAt);
            Assert.IsFalse(back.IsExpired(now.AddDays(6).AddHours(23)));
            Assert.IsTrue(back.IsExpired(now.AddDays(7)));
            foreach (string bad in new[] { null, "", "x", "{}", "{\"role\":\"CEO\"}", "{\"role\":\"CEO\",\"by\":0,\"at\":\"2026-10-26T00:00:00Z\"}", "{\"role\":\"CEO\",\"by\":1,\"at\":\"nope\"}" })
                Assert.IsFalse(PendingRoleGrant.TryParse(bad, out _), bad ?? "null");
        }

        [TestMethod]
        public void Password_Policy_Wants_Ten_Characters_A_Letter_And_A_Digit()
        {
            Assert.IsNull(PasswordPolicy.Problem("abcdefghi1"));
            Assert.IsNull(PasswordPolicy.Problem("Ci#Adm1n-Build-2026"));
            Assert.IsNotNull(PasswordPolicy.Problem("abcdefgh1"), "9 characters");
            Assert.IsNotNull(PasswordPolicy.Problem("abcdefghijk"), "no digit");
            Assert.IsNotNull(PasswordPolicy.Problem("12345678901"), "no letter");
            Assert.IsNotNull(PasswordPolicy.Problem(null));
        }

        [TestMethod]
        public void Made_Up_Temporary_Passwords_Follow_The_Policy_And_Differ()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                string p = IdentityRules.GenerateTemporaryPassword();
                Assert.AreEqual(14, p.Length);
                Assert.IsNull(PasswordPolicy.Problem(p), p);
                Assert.IsTrue(p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit) && p.Any(c => "#-_.".IndexOf(c) >= 0), p);
                Assert.IsTrue(char.IsLetterOrDigit(p[0]), "a symbol is never first: " + p);
                seen.Add(p);
            }
            Assert.IsTrue(seen.Count > 195);
        }

        // ---------------------------------------------------------------------------------------------------- user administration is for the PMO

        [TestMethod]
        public async Task Only_The_Pmo_Administers_Users()
        {
            int wh = await H.RegisterAsync(H.Admin, Unique("wh"), new[] { WarehouseRole });
            ClaimsPrincipal warehouse = Signed(wh, "wh", "WAREHOUSE");

            ResponseDto<ResponseUserDto> reg = await H.As(warehouse, s => s.RegisterAsync(NewUser(Unique("x"), new[] { WarehouseRole }, GoodPassword), default).AsTask());
            Assert.IsFalse(reg.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, reg.HttpStatusCode);

            Assert.IsFalse((await H.As(warehouse, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = wh, RoleIds = new[] { PmoRole } }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.GetAllUsersAsync(new UserPaginationParams(), default).AsTask())).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.DeleteUserAsync(wh, default))).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.DeleteUserCollectionAsync(new[] { wh }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = wh }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.GetRoleGrantsAsync(default).AsTask())).Success);
            Assert.IsFalse((await H.As(warehouse, s => s.ApproveRoleGrantAsync(1, default).AsTask())).Success);
            UpdateUserDto update = Edit(wh, "wh-edit@example.com", new[] { PmoRole });
            Assert.IsFalse((await H.As(warehouse, s => s.UpdateUserAsync(update, default).AsTask())).Success);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, RolesOf(wh), "nothing was changed");

            // not even a signed-out caller
            Assert.IsFalse((await H.As(null, s => s.GetAllUsersAsync(new UserPaginationParams(), default).AsTask())).Success);
        }

        [TestMethod]
        public async Task The_Full_User_List_Is_Pmo_Only_But_The_Lookup_Is_Slim_And_Open()
        {
            string name = Unique("pick");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ClaimsPrincipal warehouse = Signed(id, name, "WAREHOUSE");

            var lookup = await H.As(warehouse, s => s.GetUserLookupAsync(new UserPaginationParams { Contains = name }, default).AsTask());
            Assert.IsTrue(lookup.Success);
            ResponseUserLookupDto row = lookup.Data.Single(x => x.Id == id);
            Assert.AreEqual(name, row.UserName);
            Assert.AreEqual(name + " آزمون", row.DisplayName);
            // the lookup type has no personal data at all
            CollectionAssert.AreEquivalent(new[] { "Id", "UserName", "Name", "LastName", "DisplayName" }, typeof(ResponseUserLookupDto).GetProperties().Select(p => p.Name).ToArray());

            var full = await H.As(H.Admin, s => s.GetAllUsersAsync(new UserPaginationParams { Id = id }, default).AsTask());
            Assert.IsTrue(full.Success);
            Assert.AreEqual("0012345678", full.Data.Single(x => x.Id == id).NationalCode);
            Assert.IsNull(full.Data.Single(x => x.Id == id).SerialNumber);
            Assert.IsNull(full.Data.Single(x => x.Id == id).MobileConfirmCode);

            // the roles that matter are not for the pickers to enumerate
            var pmoFilter = await H.As(warehouse, s => s.GetUserLookupAsync(new UserPaginationParams { Role = "PMO" }, default).AsTask());
            Assert.AreEqual(0, pmoFilter.Data.Count);
            var whFilter = await H.As(warehouse, s => s.GetUserLookupAsync(new UserPaginationParams { Role = "warehouse", Contains = name }, default).AsTask());
            Assert.AreEqual(1, whFilter.Data.Count);
        }

        [TestMethod]
        public async Task A_User_Reads_Only_His_Own_Record_And_Photo()
        {
            int a = await H.RegisterAsync(H.Admin, Unique("ra"), new[] { WarehouseRole });
            int b = await H.RegisterAsync(H.Admin, Unique("rb"), new[] { WarehouseRole });
            ClaimsPrincipal userA = Signed(a, "a", "WAREHOUSE");
            Assert.IsTrue((await H.As(userA, s => s.GetResponseUserDtoByIdAsync(new GetUserDto { Id = a }, default).AsTask())).Success);
            ResponseDto<ResponseUserDto> other = await H.As(userA, s => s.GetResponseUserDtoByIdAsync(new GetUserDto { Id = b }, default).AsTask());
            Assert.IsFalse(other.Success);
            Assert.IsNull(other.Data);
            Assert.IsTrue((await H.As(H.Admin, s => s.GetResponseUserDtoByIdAsync(new GetUserDto { Id = b }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(userA, s => s.DetachUserProfilePhotoAsync(b, default).AsTask())).Success);
            Assert.IsFalse((await H.As(userA, s => s.AttachProfilePhotoAsync(new AttachProfilePhotoDto { UserId = b }, default).AsTask())).Success);
        }

        // ---------------------------------------------------------------------------------------------------- roles

        [TestMethod]
        public async Task Nobody_Changes_His_Own_Roles()
        {
            // a host of its own: how many PMO users there are decides whether the PMO role is given at once (bootstrap) or waits for a second user
            var h = new Host(Unique("roles"));
            string pmoName = Unique("pmo");
            int second = await h.AddPmoAsync(pmoName);
            ClaimsPrincipal pmo = Signed(second, pmoName, "PMO");
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(second));

            ResponseDto<ChangeRoleResultDto> own = await h.As(pmo, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = second, RoleIds = new[] { PmoRole, WarehouseRole } }, default).AsTask());
            Assert.IsFalse(own.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, own.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.CannotChangeOwnRoles, own.Message);

            // R1a2 (M-1): the one thing he may do to himself is to step down; a step-down that also gives him a role is something else and is refused
            ResponseDto<ChangeRoleResultDto> mixed = await h.As(pmo, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = second, RoleIds = new[] { WarehouseRole } }, default).AsTask());
            Assert.IsFalse(mixed.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, mixed.HttpStatusCode);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(second));

            // the same through the edit-user screen: other fields yes, the list of roles no
            UpdateUserDto edit = Edit(second, pmoName + "@example.com", new[] { PmoRole, WarehouseRole });
            Assert.IsFalse((await h.As(pmo, s => s.UpdateUserAsync(edit, default).AsTask())).Success);
            edit.RoleIds = new[] { PmoRole };
            edit.Name = "changed";
            ResponseDto<ResponseUserDto> edited = await h.As(pmo, s => s.UpdateUserAsync(edit, default).AsTask());
            Assert.IsTrue(edited.Success, edited.Message);
            Assert.AreEqual("changed", edited.Data.Name);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(second));

            // another PMO user can change them (ordinary role: at once)
            Assert.IsTrue((await h.As(h.Admin, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = second, RoleIds = new[] { PmoRole, WarehouseRole } }, default).AsTask())).Success);
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(second));
        }

        [TestMethod]
        public async Task A_Sensitive_Role_Waits_For_A_Second_Pmo_User()
        {
            // a host of its own: how many PMO users there are decides whether the PMO role is given at once (bootstrap) or waits for a second user
            var h = new Host(Unique("roles"));
            string name = Unique("fin");
            int fin = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, FinanceManagerRole });
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(fin), "FINANCEMANAGER is not given at registration");

            var list = (await h.As(h.Admin, s => s.GetRoleGrantsAsync(default).AsTask())).Data;
            RoleGrantDto grant = list.Single(g => g.UserId == fin);
            Assert.AreEqual("FINANCEMANAGER", grant.Role);
            Assert.AreEqual(h.AdminId, grant.RequestedByUserId);
            Assert.IsFalse(grant.CanApprove, "the one who asked cannot approve");

            // asking again does not make a second request
            Assert.IsTrue((await h.As(h.Admin, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = fin, RoleIds = new[] { WarehouseRole, FinanceManagerRole } }, default).AsTask())).Success);
            Assert.AreEqual(1, (await h.As(h.Admin, s => s.GetRoleGrantsAsync(default).AsTask())).Data.Count(g => g.UserId == fin));

            // the requester cannot approve it
            ResponseDto<RoleGrantDto> self = await h.As(h.Admin, s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask());
            Assert.IsFalse(self.Success);
            Assert.AreEqual(AccountServiceMessages.GrantSameApprover, self.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(fin));

            // a user who is not a PMO user cannot approve it
            Assert.IsFalse((await h.As(Signed(fin, name, "WAREHOUSE"), s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask())).Success);

            // a second PMO user can, once the receiver has chosen a password of his own (R1a2, M-2: the PMO user who registered him chose the first one; see the tests of M-2)
            await ChoosePasswordAsync(h, fin, name);
            int second = await h.AddPmoAsync(Unique("pmo"));
            ClaimsPrincipal pmo2 = Signed(second, "pmo2", "PMO");
            Assert.IsTrue((await h.As(pmo2, s => s.GetRoleGrantsAsync(default).AsTask())).Data.Single(g => g.Id == grant.Id).CanApprove);
            ResponseDto<RoleGrantDto> approved = await h.As(pmo2, s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask());
            Assert.IsTrue(approved.Success, approved.Message);
            CollectionAssert.AreEqual(new[] { "FINANCEMANAGER", "WAREHOUSE" }, h.RolesOf(fin));
            Assert.IsFalse((await h.As(h.Admin, s => s.GetRoleGrantsAsync(default).AsTask())).Data.Any(g => g.UserId == fin));
            Assert.IsFalse((await h.As(pmo2, s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask())).Success, "an approved request is gone");

            // taking a sensitive role away is immediate
            Assert.IsTrue((await h.As(pmo2, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = fin, RoleIds = new[] { WarehouseRole } }, default).AsTask())).Success);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(fin));
        }

        [TestMethod]
        public async Task The_Receiver_Of_A_Sensitive_Role_Cannot_Approve_It_And_A_Request_Can_Be_Rejected()
        {
            // a host of its own: how many PMO users there are decides whether the PMO role is given at once (bootstrap) or waits for a second user
            var h = new Host(Unique("roles"));
            // a PMO user who is to receive the CEO role asked for by another PMO user cannot approve it himself
            int receiver = await h.AddPmoAsync(Unique("pmo"));
            int third = await h.AddPmoAsync(Unique("pmo"));
            Assert.IsTrue((await h.As(h.Admin, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = receiver, RoleIds = new[] { PmoRole, CeoRole } }, default).AsTask())).Success);
            RoleGrantDto grant = (await h.As(h.Admin, s => s.GetRoleGrantsAsync(default).AsTask())).Data.Single(g => g.UserId == receiver);

            ResponseDto<RoleGrantDto> byReceiver = await h.As(Signed(receiver, "r", "PMO"), s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask());
            Assert.IsFalse(byReceiver.Success);
            Assert.AreEqual(AccountServiceMessages.GrantApproverIsTarget, byReceiver.Message);
            Assert.IsFalse((await h.As(Signed(receiver, "r", "PMO"), s => s.GetRoleGrantsAsync(default).AsTask())).Data.Single(g => g.Id == grant.Id).CanApprove);

            // any PMO user can reject it (the requester takes it back)
            Assert.IsTrue((await h.As(h.Admin, s => s.RejectRoleGrantAsync(grant.Id, default).AsTask())).Success);
            Assert.IsFalse((await h.As(Signed(third, "t", "PMO"), s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask())).Success);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(receiver));
        }

        [TestMethod]
        public async Task A_Request_Nobody_Approves_Within_Seven_Days_Is_Void()
        {
            // a host of its own: how many PMO users there are decides whether the PMO role is given at once (bootstrap) or waits for a second user
            var h = new Host(Unique("roles"));
            int user = await h.RegisterAsync(h.Admin, Unique("old"), new[] { WarehouseRole, CeoRole });
            int second = await h.AddPmoAsync(Unique("pmo"));
            RoleGrantDto grant = (await h.As(h.Admin, s => s.GetRoleGrantsAsync(default).AsTask())).Data.Single(g => g.UserId == user);

            // make the request eight days old
            using (IServiceScope scope = h.Provider.CreateScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                UserClaim row = uow.Set<UserClaim>().Single(c => c.Id == grant.Id);
                var old = new PendingRoleGrant { Role = "CEO", RequestedBy = h.AdminId, RequestedAt = DateTimeOffset.Now.AddDays(-8) };
                row.ClaimValue = old.ToClaimValue();
                uow.SaveChanges();
            }

            Assert.IsFalse((await h.As(Signed(second, "p2", "PMO"), s => s.GetRoleGrantsAsync(default).AsTask())).Data.Any(g => g.Id == grant.Id), "an expired request is not listed");
            ResponseDto<RoleGrantDto> late = await h.As(Signed(second, "p2", "PMO"), s => s.ApproveRoleGrantAsync(grant.Id, default).AsTask());
            Assert.IsFalse(late.Success);
            Assert.AreEqual(AccountServiceMessages.GrantExpired, late.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(user));
        }

        // ---------------------------------------------------------------------------------------------------- passwords

        [TestMethod]
        public async Task Registration_Refuses_A_Weak_Password()
        {
            foreach (string weak in new[] { "Ab1-short", "abcdefghijkl", "1234567890123", "Qw3rty-" })
            {
                ResponseDto<ResponseUserDto> r = await H.As(H.Admin, s => s.RegisterAsync(NewUser(Unique("weak"), new[] { WarehouseRole }, weak), default).AsTask());
                Assert.IsFalse(r.Success, weak);
            }
            // a letter and a digit and ten characters is enough
            Assert.IsTrue((await H.As(H.Admin, s => s.RegisterAsync(NewUser(Unique("ok"), new[] { WarehouseRole }, "kitchen2026x"), default).AsTask())).Success);
        }

        [TestMethod]
        public async Task A_User_Changes_Only_His_Own_Password_With_The_Current_One()
        {
            string name = Unique("pw");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            string other = Unique("pw");
            int otherId = await H.RegisterAsync(H.Admin, other, new[] { WarehouseRole });
            ClaimsPrincipal me = Signed(id, name, "WAREHOUSE");
            const string next = "Next-Secret-2027z";

            // another user's id: refused, even for the PMO
            ResponseDto<ResponseLoginUserDto> theirs = await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { UserId = otherId, CurrentPassword = GoodPassword, NewPassword = next }, default).AsTask());
            Assert.IsFalse(theirs.Success);
            Assert.AreEqual(AccountServiceMessages.OnlyOwnPassword, theirs.Message);
            Assert.IsNull(theirs.Data);
            Assert.IsFalse((await H.As(H.Admin, s => s.ChangePasswordAsync(new ChangePasswordDto { UserId = otherId, CurrentPassword = GoodPassword, NewPassword = next }, default).AsTask())).Success);
            Assert.IsTrue((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = other, Password = GoodPassword }, default).AsTask())).Success, "their password is unchanged");

            // no current password, a wrong one, the same one, a weak new one
            Assert.AreEqual(AccountServiceMessages.CurrentPasswordRequired, (await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { NewPassword = next }, default).AsTask())).Message);
            Assert.AreEqual(AccountServiceMessages.CurrentPasswordWrong, (await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = "Wrong-Pass-2026", NewPassword = next }, default).AsTask())).Message);
            Assert.AreEqual(AccountServiceMessages.NewPasswordSameAsCurrent, (await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = GoodPassword, NewPassword = GoodPassword }, default).AsTask())).Message);
            Assert.IsFalse((await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = GoodPassword, NewPassword = "short1" }, default).AsTask())).Success);
            Assert.IsTrue((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Success, "still the old password");

            // the right way: own id (or none) with the current password; the answer carries the new tokens of this user
            ResponseDto<ResponseLoginUserDto> done = await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { UserId = id, CurrentPassword = GoodPassword, NewPassword = next }, default).AsTask());
            Assert.IsTrue(done.Success, done.Message);
            Assert.IsFalse(string.IsNullOrEmpty(done.Data.AccessToken));
            Assert.IsFalse((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Success);
            Assert.IsTrue((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = next }, default).AsTask())).Success);
        }

        [TestMethod]
        public async Task Changing_The_Password_Ends_The_Old_Sessions()
        {
            string name = Unique("sess");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto first = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Data;
            Assert.IsTrue((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = first.RefreshToken }, default).AsTask())).Success, "refresh works while the session is alive");

            ResponseLoginUserDto fresh = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Data;
            ResponseDto<ResponseLoginUserDto> changed = await H.As(Signed(id, name, "WAREHOUSE"), s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = GoodPassword, NewPassword = "Another-Pass-2028q" }, default).AsTask());
            Assert.IsTrue(changed.Success, changed.Message);
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = fresh.RefreshToken }, default).AsTask())).Success, "the refresh token from before the change is dead");
            Assert.IsTrue((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = changed.Data.RefreshToken }, default).AsTask())).Success);
        }

        [TestMethod]
        public async Task The_Administrator_Sets_A_Temporary_Password_That_Must_Be_Changed()
        {
            string name = Unique("tmp");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto before = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Data;

            // not for anybody but a PMO user, and not for the own account
            Assert.IsFalse((await H.As(Signed(id, name, "WAREHOUSE"), s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask())).Success);
            ResponseDto<AdminResetPasswordResultDto> ownReset = await H.As(H.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = H.AdminId }, default).AsTask());
            Assert.IsFalse(ownReset.Success);
            Assert.AreEqual(AccountServiceMessages.CannotResetOwnPassword, ownReset.Message);

            ResponseDto<AdminResetPasswordResultDto> reset = await H.As(H.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask());
            Assert.IsTrue(reset.Success, reset.Message);
            Assert.IsTrue(reset.Data.MustChangePassword);
            string temporary = reset.Data.TemporaryPassword;
            Assert.IsNull(PasswordPolicy.Problem(temporary));
            Assert.IsFalse(reset.Data.GetType().GetProperties().Any(p => p.Name.Contains("Token")), "the answer holds no tokens");

            // the old password and the old sessions are gone
            Assert.IsFalse((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = before.RefreshToken }, default).AsTask())).Success);

            // signing in with the temporary password gives a code and no tokens
            ResponseDto<ResponseLoginUserDto> login = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = temporary }, default).AsTask());
            Assert.IsFalse(login.Success);
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, login.Data.Code);
            Assert.IsTrue(login.Data.MustChangePassword);
            Assert.IsNull(login.Data.AccessToken);
            Assert.IsNull(login.Data.RefreshToken);
            Assert.AreEqual(HttpStatusCode.PreconditionRequired, login.HttpStatusCode);

            // the user cannot go around it with change-password (no session) and a wrong temporary password does not help
            Assert.IsFalse((await H.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = "Wrong-Temp-2026", NewPassword = "Brand-New-2029w" }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = temporary, NewPassword = temporary }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = temporary, NewPassword = "weak1" }, default).AsTask())).Success);

            ResponseDto<ResponseLoginUserDto> chosen = await H.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = temporary, NewPassword = "Brand-New-2029w" }, default).AsTask());
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.IsFalse(string.IsNullOrEmpty(chosen.Data.AccessToken));

            // the flag is gone: normal sign-in with the new password, and the temporary one is dead
            Assert.IsTrue((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = "Brand-New-2029w" }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = temporary }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = "Brand-New-2029w", NewPassword = "Yet-Another-2030v" }, default).AsTask())).Success, "no temporary password any more");
        }

        [TestMethod]
        public async Task The_Administrator_Can_Choose_The_Temporary_Password_And_It_Must_Follow_The_Policy()
        {
            string name = Unique("tmp");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            Assert.IsFalse((await H.As(H.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id, TemporaryPassword = "weak" }, default).AsTask())).Success);
            ResponseDto<AdminResetPasswordResultDto> ok = await H.As(H.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id, TemporaryPassword = "Chosen-By-Boss-77" }, default).AsTask());
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.IsNull(ok.Data.TemporaryPassword, "a password the administrator typed is not echoed");
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = "Chosen-By-Boss-77" }, default).AsTask())).Data.Code);
        }

        // ---------------------------------------------------------------------------------------------------- lock-out

        [TestMethod]
        public async Task Five_Wrong_Passwords_Lock_The_Account()
        {
            string name = Unique("lock");
            await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            Assert.IsTrue(H.Query(uow => uow.Set<User>().Single(u => u.UserName == name).LockoutEnabled), "new users have the lock-out switched on");

            ResponseDto<ResponseLoginUserDto> last = null;
            for (int i = 0; i < 5; i++)
            {
                last = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = "Wrong-Pass-" + i + "xx" }, default).AsTask());
                Assert.IsFalse(last.Success);
            }
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, last.Message, "the fifth wrong password locks the account");

            // even the right password is refused while the account is locked
            ResponseDto<ResponseLoginUserDto> right = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask());
            Assert.IsFalse(right.Success);
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, right.Message);
            Assert.IsNull(right.Data);
        }

        [TestMethod]
        public async Task Wrong_Current_Passwords_In_Change_Password_Count_Toward_The_Lock_Out()
        {
            string name = Unique("lockpw");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ClaimsPrincipal me = Signed(id, name, "WAREHOUSE");
            for (int i = 0; i < 5; i++)
                await H.As(me, s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = "Wrong-Pass-" + i + "xx", NewPassword = "Brand-New-2029w" }, default).AsTask());
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut,
                (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Message);
        }

        [TestMethod]
        public async Task The_Administrator_Account_Is_Not_Exempt_From_The_Lock_Out()
        {
            Assert.IsTrue(H.Query(uow => uow.Set<User>().Single(u => u.UserName == "Admin").LockoutEnabled));
            // a start of an installation made before R1a: the exemption is lifted again at the next seed
            H.Provider.RunScopedService<IUnitOfWork>(uow => { uow.Set<User>().Single(u => u.UserName == "Admin").LockoutEnabled = false; uow.SaveChanges(); });
            Assert.IsFalse(H.Query(uow => uow.Set<User>().Single(u => u.UserName == "Admin").LockoutEnabled));
            H.Provider.GetRequiredService<IIdentityDbInitializer>().SeedData();
            Assert.IsTrue(H.Query(uow => uow.Set<User>().Single(u => u.UserName == "Admin").LockoutEnabled));
            // the right password is still accepted (nothing was locked); the seeded administrator has to choose a password of his own first (R1a2, M7)
            ResponseDto<ResponseLoginUserDto> first = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = "Admin", Password = AdminPassword }, default).AsTask());
            Assert.IsFalse(first.Success);
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, first.Data.Code);
        }

        // ---------------------------------------------------------------------------------------------------- sign-in details

        [TestMethod]
        public async Task Unknown_Users_Get_The_Same_Answer_As_A_Wrong_Password()
        {
            string name = Unique("known");
            await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ResponseDto<ResponseLoginUserDto> unknown = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = Unique("nobody"), Password = GoodPassword }, default).AsTask());
            ResponseDto<ResponseLoginUserDto> wrong = await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = "Wrong-Pass-2026" }, default).AsTask());
            Assert.AreEqual(wrong.Message, unknown.Message);
            Assert.AreEqual(AccountServiceMessages.InvalidLoginData, unknown.Message);
        }

        [TestMethod]
        public async Task A_Deleted_Or_Deactivated_User_Is_Cut_Off()
        {
            string name = Unique("gone");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto session = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Data;

            // the PMO user cannot delete his or her own account
            Assert.IsFalse((await H.As(H.Admin, s => s.DeleteUserAsync(H.AdminId, default))).Success);
            Assert.IsTrue(H.Query(uow => !uow.Set<User>().Single(u => u.Id == H.AdminId).IsDeleted));

            Assert.IsTrue((await H.As(H.Admin, s => s.DeleteUserAsync(id, default))).Success);
            Assert.IsFalse((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = session.RefreshToken }, default).AsTask())).Success, "the deleted user's refresh token is dead");

            string quiet = Unique("quiet");
            int quietId = await H.RegisterAsync(H.Admin, quiet, new[] { WarehouseRole });
            ResponseLoginUserDto quietSession = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = quiet, Password = GoodPassword }, default).AsTask())).Data;
            H.Provider.RunScopedService<IUnitOfWork>(uow => { uow.Set<User>().Single(u => u.Id == quietId).IsActive = false; uow.SaveChanges(); });
            Assert.IsFalse((await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = quiet, Password = GoodPassword }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = quietSession.RefreshToken }, default).AsTask())).Success);
        }

        [TestMethod]
        public async Task Refresh_Does_Not_Need_The_Old_Access_Token_And_Gives_Fresh_Roles()
        {
            string name = Unique("ref");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto login = (await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask())).Data;
            Assert.IsTrue((await H.As(H.Admin, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = id, RoleIds = new[] { WarehouseRole, RoleConstants.EngineerResponseRole.Id } }, default).AsTask())).Success);

            // an anonymous caller (the access token has expired) presenting only the refresh token
            ResponseDto<ResponseLoginUserDto> refreshed = await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = login.RefreshToken }, default).AsTask());
            Assert.IsTrue(refreshed.Success, refreshed.Message);
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(refreshed.Data.AccessToken);
            CollectionAssert.AreEquivalent(new[] { "ENGINEER", "WAREHOUSE" }, jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray());
            Assert.IsTrue(jwt.ValidTo > DateTime.UtcNow.AddMinutes(55) && jwt.ValidTo < DateTime.UtcNow.AddMinutes(65), "an access token is good for about an hour");
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = "not-a-token" }, default).AsTask())).Success);
            Assert.IsFalse((await H.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto(), default).AsTask())).Success);
        }

        // ---------------------------------------------------------------------------------------------------- audit trail

        [TestMethod]
        public async Task Sign_In_Password_And_Role_Events_Are_In_The_Audit_Trail_With_The_Address()
        {
            UnknownUserTally.Reset();
            string ghost = Unique("ghost");
            string name = Unique("aud");
            int id = await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole, CeoRole });
            await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = "Wrong-Pass-2026" }, default).AsTask());
            await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask());
            await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = ghost, Password = GoodPassword }, default).AsTask());
            await H.As(Signed(id, name, "WAREHOUSE"), s => s.ChangePasswordAsync(new ChangePasswordDto { UserId = H.AdminId, CurrentPassword = GoodPassword, NewPassword = "Next-Secret-2027z" }, default).AsTask());
            await H.As(Signed(id, name, "WAREHOUSE"), s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = GoodPassword, NewPassword = "Next-Secret-2027z" }, default).AsTask());
            await H.As(H.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask());
            await H.As(H.Admin, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = H.AdminId, RoleIds = new[] { PmoRole, WarehouseRole } }, default).AsTask());
            await H.As(H.Admin, s => s.DeleteUserAsync(id, default));

            List<FnAuditLog> log = H.Query(uow => uow.Set<FnAuditLog>().AsNoTracking().OrderBy(a => a.Id).ToList());
            bool Has(string type, string action, Func<FnAuditLog, bool> where = null) => log.Any(a => a.EntityType == type && a.Action == action && (where == null || where(a)));

            Assert.IsTrue(Has("Identity.SignIn", "Failure", a => a.EntityId == id && a.UserId == id && a.Ip == "10.1.2.3"), "failed sign-in: user, time and address");
            Assert.IsTrue(Has("Identity.SignIn", "Success", a => a.EntityId == id && a.UserId == id && a.Ip == "10.1.2.3" && a.At > DateTimeOffset.Now.AddMinutes(-5)), "successful sign-in");
            // an unknown name is kept as its length and first two characters only (R1a2, M5): a password typed into the name field never reaches the trail
            Assert.IsTrue(Has("Identity.SignIn", "Failure", a => a.EntityId == null && a.UserId == 0 && a.Summary.Contains("«gh»") && a.Summary.Contains(ghost.Length + " ")), "unknown user name");
            Assert.IsFalse(log.Any(a => (a.Summary ?? "").Contains(ghost) || (a.AfterJson ?? "").Contains(ghost) || (a.BeforeJson ?? "").Contains(ghost)), "the whole unknown name is nowhere in the trail");
            Assert.IsTrue(Has("Identity.Password", "ChangeRefused", a => a.UserId == id && a.EntityId == H.AdminId), "an attempt on another user's password");
            Assert.IsTrue(Has("Identity.Password", "Changed", a => a.UserId == id && a.EntityId == id), "password change by the owner");
            Assert.IsTrue(Has("Identity.Password", "AdminReset", a => a.UserId == H.AdminId && a.EntityId == id), "administrator's reset: who and for whom");
            Assert.IsTrue(Has("Identity.Roles", "GrantRequested", a => a.EntityId == id && a.UserId == H.AdminId && a.Summary.Contains("CEO")), "request of a sensitive role");
            Assert.IsTrue(Has("Identity.Access", "Refused", a => a.UserId == H.AdminId && a.EntityId == H.AdminId), "an attempt to change the own roles");
            Assert.IsTrue(Has("Identity.User", "Deleted", a => a.UserId == H.AdminId && a.EntityId == id), "deletion");
            Assert.IsTrue(Has("Identity.User", "Registered", a => a.EntityId == id), "registration");

            // the generic trail of the database context: the user row and the role row, the password hash is never in it
            Assert.IsTrue(Has("User", "Added", a => a.AfterJson != null && a.AfterJson.Contains(name)), "user created");
            Assert.IsTrue(Has("UserRole", "Added", a => a.AfterJson != null && a.AfterJson.Contains("\"UserId\"")), "role given");
            Assert.IsTrue(Has("User", "Modified", a => a.EntityId == id && a.AfterJson != null && a.AfterJson.Contains("IsDeleted")), "user deleted: the flag");
            Assert.IsFalse(log.Any(a => (a.BeforeJson ?? "").Contains("PasswordHash") || (a.AfterJson ?? "").Contains("PasswordHash") || (a.AfterJson ?? "").Contains("SecurityStamp")), "no secret in the trail");
            Assert.IsFalse(log.Any(a => a.EntityType == "User" && ((a.AfterJson ?? "").Contains("0012345678") || (a.BeforeJson ?? "").Contains("0012345678"))), "the national code is masked");
            Assert.IsFalse(log.Any(a => a.EntityType == "User" && ((a.AfterJson ?? "").Contains("09120000000") || (a.BeforeJson ?? "").Contains("09120000000"))), "the phone number is masked");
            Assert.IsTrue(log.Any(a => a.EntityType == "User" && (a.AfterJson ?? "").Contains("\"NationalCode\":\"****5678\"")), "the national code shows its last four digits only");
        }

        [TestMethod]
        public async Task The_Audit_Trail_Of_Identity_Is_Append_Only_Like_The_Rest()
        {
            string name = Unique("ro");
            await H.RegisterAsync(H.Admin, name, new[] { WarehouseRole });
            await H.As(null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = GoodPassword }, default).AsTask());
            Assert.ThrowsExactly<InvalidOperationException>(() => H.Provider.RunScopedService<IUnitOfWork>(uow =>
            {
                FnAuditLog row = uow.Set<FnAuditLog>().First(a => a.EntityType == "Identity.SignIn");
                row.Summary = "tampered";
                uow.SaveChanges();
            }));
        }
    }
}
