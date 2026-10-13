using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetProviderContactConfigurations : IEntityTypeConfiguration<ProviderContact>
    {
        public void Configure(EntityTypeBuilder<ProviderContact> builder)
        {
            builder
                .ToTable("ProviderContacts");

            builder
                .HasKey(providerContact => providerContact.Id);
        }
    }
}
