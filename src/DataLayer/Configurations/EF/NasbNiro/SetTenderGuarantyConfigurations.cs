using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetTenderGuarantyConfigurations : IEntityTypeConfiguration<TenderGuaranty>
    {
        public void Configure(EntityTypeBuilder<TenderGuaranty> builder)
        {
            builder
                .ToTable("TenderGuaranties");

            builder
                .HasKey(tenderGuaranty => new { tenderGuaranty.Id });

            builder
               .HasOne(tenderGuaranty => tenderGuaranty.Tender)
               .WithMany(tender => tender.TenderGuaranties)
               .HasForeignKey(tenderGuaranty => tenderGuaranty.TenderId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
