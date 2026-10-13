using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetTenderConfigurations : IEntityTypeConfiguration<Tender>
    {
        public void Configure(EntityTypeBuilder<Tender> builder)
        {
            builder.ToTable("Tenders");

            builder
                .HasMany(tender => tender.TenderFiles)
                .WithOne(tenderFiles => tenderFiles.Tender)
                .HasForeignKey(tenderFiles => tenderFiles.TenderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
