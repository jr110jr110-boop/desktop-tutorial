using Entities.Identity;
using Entities.Identity.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.Identity
{
    internal class SetAppRoleClaimConfigurations :
        IEntityTypeConfiguration<RoleClaim>
    {
        public void Configure(EntityTypeBuilder<RoleClaim> builder)
        {
            builder.ToTable("AppRoleClaims");

            builder
                .HasData(PermissionHelper.GetRoleClaims());

            builder
                .HasOne(roleClaim => roleClaim.Role)
                .WithMany(role => role.Claims)
                .HasForeignKey(roleClaim => roleClaim.RoleId);
        }
    }
}
