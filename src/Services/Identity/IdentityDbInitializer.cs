using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;
using Services.Identity.AccountServices;
using ViewModels.Identity.Settings;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2577
    /// And http://www.dotnettips.info/post/2578
    /// </summary>
    public class IdentityDbInitializer : IIdentityDbInitializer
    {
        private readonly IOptionsSnapshot<SiteSettings> _adminUserSeedOptions;
        private readonly IApplicationUserManager _applicationUserManager;
        private readonly ILogger<IdentityDbInitializer> _logger;
        private readonly IApplicationRoleManager _roleManager;
        private readonly IServiceScopeFactory _scopeFactory;

        public IdentityDbInitializer(
            IApplicationUserManager applicationUserManager,
            IServiceScopeFactory scopeFactory,
            IApplicationRoleManager roleManager,
            IOptionsSnapshot<SiteSettings> adminUserSeedOptions,
            ILogger<IdentityDbInitializer> logger
            )
        {
            _applicationUserManager = applicationUserManager;
            _applicationUserManager.ThrowIfNull(nameof(_applicationUserManager));

            _scopeFactory = scopeFactory;
            _scopeFactory.ThrowIfNull(nameof(_scopeFactory));

            _roleManager = roleManager;
            _roleManager.ThrowIfNull(nameof(_roleManager));

            _adminUserSeedOptions = adminUserSeedOptions;
            _adminUserSeedOptions.ThrowIfNull(nameof(_adminUserSeedOptions));

            _logger = logger;
            _logger.ThrowIfNull(nameof(_logger));
        }

        /// <summary>
        /// Applies any pending migrations for the context to the database.
        /// Will create the database if it does not already exist.
        /// </summary>
        public void Initialize()
        {
            using (var serviceScope = _scopeFactory.CreateScope())
            {
                using (var context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>())
                {
                    context.Database.Migrate();
                }
            }
        }

        /// <summary>
        /// Adds some default values to the IdentityDb
        /// </summary>
        public void SeedData()
        {
            using (var serviceScope = _scopeFactory.CreateScope())
            using (var context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>())
            {
                // R1a2 (M7): a database without a single user is a new installation (nothing to force a password change on)
                bool freshInstall = !context.Users.Any();

                var identityDbSeedData = serviceScope.ServiceProvider.GetService<IIdentityDbInitializer>();
                var result = identityDbSeedData.SeedDatabaseWithAdminUserAsync().Result;
                if (result == IdentityResult.Failed())
                {
                    throw new InvalidOperationException(result.DumpErrors());
                }

                // How to add initial data to the DB directly
                foreach (Role role in RoleConstants.GetRoles())
                {
                    if (!context.Roles.Any(r => r.Name == role.Name))
                    {
                        context.Add(role.Name);
                        context.SaveChanges();
                    }
                }

                // R1a: the sign-in lock-out applies to every account, the administrator included. An installation made before this
                // release has the Admin account without it (and the seat users are locked out for good by their own setup), so it is switched on here.
                // The list is tiny (normally only Admin), and the filter keeps this safe to run at every start.
                var withoutLockout = context.Users.Where(u => !u.LockoutEnabled).ToList();
                foreach (User u in withoutLockout) u.LockoutEnabled = true;
                if (withoutLockout.Count > 0) context.SaveChanges();

                ForcePasswordChangeOnce(context, freshInstall);
            }
        }

        /// <summary>
        /// R1a2 (M7): the first start after the upgrade puts the "must change the password" flag on every user who has a password (not the AI seats), once, because the
        /// passwords chosen before the password rule are unknown to us. The audit row "UpgradeForced" is the marker that this ran; it is also written on a new installation
        /// (where there is nothing to force), so a later start never flags anybody. <c>Identity:ForcePasswordChangeAtUpgrade</c> = false skips the whole step.
        /// </summary>
        private void ForcePasswordChangeOnce(ApplicationDbContext context, bool freshInstall)
        {
            IdentitySecurityOptions options = _adminUserSeedOptions.Value.Identity ?? new IdentitySecurityOptions();
            if (!options.ForcePasswordChangeAtUpgrade) return;
            if (context.Set<Entities.Model.NasbNiro.Finance.FnAuditLog>().Any(a => a.Action == IdentityAudit.UpgradeForced)) return;

            int flagged = 0;
            if (!freshInstall)
            {
                int[] alreadyFlagged = context.Set<UserClaim>().Where(c => c.ClaimType == IdentityRules.MustChangePasswordClaimType).Select(c => c.UserId).ToArray();
                var candidates = context.Users
                    .Where(u => !u.IsDeleted && u.PasswordHash != null && !alreadyFlagged.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName })
                    .ToList();
                foreach (var candidate in candidates.Where(c => !global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(c.UserName)))
                {
                    context.Set<UserClaim>().Add(new UserClaim { UserId = candidate.Id, ClaimType = IdentityRules.MustChangePasswordClaimType, ClaimValue = MustChangeFlag.Upgrade });
                    flagged++;
                }
            }

            IdentityAudit.Add(context, null, 0, IdentityAudit.User, null, IdentityAudit.UpgradeForced,
                freshInstall
                    ? "نصب تازه: تغییر رمز اجباری پس از ارتقا لازم نیست (نشانگر ثبت شد)"
                    : $"ارتقا: تغییر رمز اجباری برای {flagged} کاربر تنظیم شد (یک‌بار)");
            context.SaveChanges();
            _logger.LogInformation("Forced password change at upgrade: {Flagged} user(s) flagged (fresh installation: {Fresh}).", flagged, freshInstall);
        }

        public async Task<IdentityResult> SeedDatabaseWithAdminUserAsync()
        {
            var adminUserSeed = _adminUserSeedOptions.Value.AdminUserSeed;
            adminUserSeed.ThrowIfNull(nameof(adminUserSeed));

            var name = adminUserSeed.Username;
            var password = adminUserSeed.Password;
            var email = adminUserSeed.Email;
            var roleName = adminUserSeed.RoleName;

            var thisMethodName = nameof(SeedDatabaseWithAdminUserAsync);

            var adminUser = await _applicationUserManager.FindByNameAsync(name);
            if (adminUser != null)
            {
                _logger.LogInformation($"{thisMethodName}: adminUser already exists.");
                return IdentityResult.Success;
            }

            // R1a2 (M7): the settings file that ships with the source code holds a placeholder password; outside Development the administrator is not made with it
            // (install.ps1 puts the real password into appsettings.Production.json). The placeholder is public, so an account made with it would be open to everybody.
            using (IServiceScope environmentScope = _scopeFactory.CreateScope())
            {
                IHostEnvironment environment = environmentScope.ServiceProvider.GetService<IHostEnvironment>();
                if (environment != null && !environment.IsDevelopment() && string.Equals(password, AdminUserSeed.PlaceholderPassword, StringComparison.Ordinal))
                {
                    _logger.LogError($"{thisMethodName}: the administrator is not made because AdminUserSeed:Password is still the placeholder. Run install.ps1 or set AdminUserSeed__Password.");
                    return IdentityResult.Failed(new IdentityError { Code = "PlaceholderPassword", Description = "AdminUserSeed:Password is the placeholder." });
                }
            }

            //Create the `Admin` Role if it does not exist
            var adminRole = await _roleManager.FindByNameAsync(roleName);
            if (adminRole == null)
            {
                adminRole = new Role(roleName);
                var adminRoleResult = await _roleManager.CreateAsync(adminRole);
                if (adminRoleResult == IdentityResult.Failed())
                {
                    _logger.LogError($"{thisMethodName}: adminRole CreateAsync failed. {adminRoleResult.DumpErrors()}");
                    return IdentityResult.Failed();
                }
            }
            else
            {
                _logger.LogInformation($"{thisMethodName}: adminRole already exists.");
            }

            adminUser = new User
            {
                UserName = name,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true
            };
            var adminUserResult = await _applicationUserManager.CreateAsync(adminUser, password);
            if (!adminUserResult.Succeeded)
            {
                _logger.LogError($"{thisMethodName}: adminUser CreateAsync failed. {adminUserResult.DumpErrors()}");
                return adminUserResult;
            }

            // R1a: the administrator is no longer exempt from the sign-in lock-out (it used to be switched off here); five wrong passwords lock the account for a few minutes.
            // LockoutEnabled = true above is enough.

            var addToRoleResult = await _applicationUserManager.AddToRoleAsync(adminUser, adminRole.Name);
            if (!addToRoleResult.Succeeded)
            {
                _logger.LogError($"{thisMethodName}: adminUser AddToRoleAsync failed. {addToRoleResult.DumpErrors()}");
                return addToRoleResult;
            }

            // R1a2 (M7): the password the installer typed is known to the person who installed; the administrator must choose his own at the first sign-in
            if (adminUserSeed.MustChangePassword)
            {
                var flagResult = await _applicationUserManager.AddClaimAsync(adminUser, new Claim(IdentityRules.MustChangePasswordClaimType, MustChangeFlag.Seed));
                if (!flagResult.Succeeded)
                    _logger.LogError($"{thisMethodName}: the must-change flag of the administrator could not be set. {flagResult.DumpErrors()}");
            }

            return IdentityResult.Success;
        }
    }
}