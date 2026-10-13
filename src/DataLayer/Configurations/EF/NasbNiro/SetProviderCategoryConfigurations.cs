using System;
using System.Collections.Generic;
using System.Text;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetProviderCategoryConfigurations : IEntityTypeConfiguration<ProviderCategory>
    {
        public void Configure(EntityTypeBuilder<ProviderCategory> builder)
        {
            builder.ToTable("ProviderCategories");

            builder
                .HasMany<Provider>()
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
