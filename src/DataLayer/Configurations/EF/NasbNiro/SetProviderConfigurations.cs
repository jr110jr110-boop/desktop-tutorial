using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetProviderConfigurations : IEntityTypeConfiguration<Provider>
    {
        public void Configure(EntityTypeBuilder<Provider> builder)
        {
            builder
                .ToTable("Providers");

            builder
                .HasKey(provider => provider.Id);

            builder
                .HasMany(provider => provider.ProviderContacts)
                .WithOne(providerContact => providerContact.Provider)
                .HasForeignKey(providerContact => providerContact.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(provider => provider.ProviderAccounts)
                .WithOne(providerAccount => providerAccount.Provider)
                .HasForeignKey(providerAccount => providerAccount.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
