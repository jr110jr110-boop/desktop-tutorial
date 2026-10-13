using DataLayer.Configurations.EF.Identity;
using Entities.Identity;
using Entities.Model;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Workflow;
using Microsoft.EntityFrameworkCore;
using ViewModels.Identity.Settings;

namespace DataLayer.Mappings
{
    public static class IdentityMappings
    {
        /// <summary>
        /// Adds all of the ASP.NET Core Identity related mappings at once.
        /// More info: http://www.dotnettips.info/post/2577
        /// and http://www.dotnettips.info/post/2578
        /// </summary>
        public static void AddCustomIdentityMappings(this ModelBuilder modelBuilder, SiteSettings siteSettings)
        {
            modelBuilder.ApplyConfiguration(new SetRoleConfigurations());

            modelBuilder.Entity<UserClaim>(builder =>
            {
                builder
                    .HasOne(userClaim => userClaim.User)
                    .WithMany(user => user.Claims)
                    .HasForeignKey(userClaim => userClaim.UserId);

                builder.ToTable("AppUserClaims");
            });

            modelBuilder.Entity<UserLogin>(builder =>
            {
                builder
                    .HasOne(userLogin => userLogin.User)
                    .WithMany(user => user.Logins)
                    .HasForeignKey(userLogin => userLogin.UserId);

                builder.ToTable("AppUserLogins");
            });

            //@
            modelBuilder.Entity<User>(builder =>
            {
                builder.ToTable("AppUsers");

                builder
                    .HasOne(x => x.ProfilePhoto)
                    .WithOne()
                    .HasForeignKey<User>(x => x.ProfilePhotoId);
            });

            modelBuilder.Entity<UserToken>(builder =>
            {
                builder
                    .HasOne(userToken => userToken.User)
                    .WithMany(user => user.UserTokens)
                    .HasForeignKey(userToken => userToken.UserId);

                builder.ToTable("AppUserTokens");
            });

            modelBuilder.Entity<UserUsedPassword>(builder =>
            {
                builder.ToTable("AppUserUsedPasswords");

                builder
                    .Property(applicationUserUsedPassword => applicationUserUsedPassword.HashedPassword)
                    .HasMaxLength(450)
                    .IsRequired();

                builder
                    .HasOne(applicationUserUsedPassword => applicationUserUsedPassword.User)
                    .WithMany(applicationUser => applicationUser.UserUsedPasswords);
            });

            modelBuilder.Entity<AppSqlCache>(builder =>
            {
                // For Microsoft.Extensions.Caching.SqlServer
                var cacheOptions = siteSettings.CookieOptions.DistributedSqlServerCacheOptions;
                builder.ToTable(cacheOptions.TableName, cacheOptions.SchemaName);

                builder
                    .HasIndex(e => e.ExpiresAtTime)
                    .HasDatabaseName("Index_ExpiresAtTime");

                builder
                    .Property(e => e.Id)
                    .HasMaxLength(449);

                builder
                    .Property(e => e.Value)
                    .IsRequired();
            });

            modelBuilder.Entity<AppDataProtectionKey>(builder =>
            {
                builder.ToTable("AppDataProtectionKeys");

                builder
                    .HasIndex(e => e.FriendlyName)
                    .IsUnique();
            });

            modelBuilder.Entity<UserProfile>(builder =>
            {
                builder.ToTable("UserProfiles");

                builder
                    .HasOne(p => p.User)
                    .WithOne(d => d.UserProfile);
            });

            modelBuilder.Entity<Address>(builder =>
            {
                builder.ToTable("Addresses");

                builder.HasOne(address => address.UserProfile).WithMany(userProfile => userProfile.Addresses).HasForeignKey(address => address.UserProfileId);


            });

            modelBuilder.Entity<City>(builder =>
            {
                builder.ToTable("Cities");
                builder.HasOne(p => p.Address).WithOne(d => d.City);
            });

            modelBuilder.Entity<Country>(builder =>
            {
                builder.ToTable("Countries");
                builder.HasOne(p => p.Address).WithOne(d => d.Country);
            });

            modelBuilder.Entity<Province>(builder =>
            {
                builder.ToTable("Provinces");
                builder.HasOne(p => p.Address).WithOne(d => d.Province);
            });

            modelBuilder.Entity<Section>(builder =>
            {
                builder.ToTable("Sections");
                builder.HasOne(p => p.Address).WithOne(d => d.Section);
            });

            modelBuilder.Entity<WorkflowGroup>(builder =>
            {
                builder.ToTable("WorkflowGroups");
            });

            modelBuilder.Entity<WorkflowStation>(builder =>
            {
                builder.ToTable("WorkflowStations");
            });

            modelBuilder.Entity<Machine>(builder =>
            {
                builder.ToTable("Machines");
            });
        }
    }
}