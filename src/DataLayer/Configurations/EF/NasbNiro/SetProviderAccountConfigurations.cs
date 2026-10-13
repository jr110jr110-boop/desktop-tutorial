using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetProviderAccountConfigurations : IEntityTypeConfiguration<ProviderAccount>
    {
        public void Configure(EntityTypeBuilder<ProviderAccount> builder)
        {
            builder
                .ToTable("ProviderAccounts");

            builder
                .HasKey(providerAccount => providerAccount.Id);
        }
    }
}
