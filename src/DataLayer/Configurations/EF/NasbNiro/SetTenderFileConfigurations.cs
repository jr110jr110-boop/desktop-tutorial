using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetTenderFileConfigurations : IEntityTypeConfiguration<TenderFile>
    {
        public void Configure(EntityTypeBuilder<TenderFile> builder)
        {
            builder
                .ToTable("TenderFiles");

            builder
                .HasKey(tenderFile => new { tenderFile.TenderId, tenderFile.DocumentId });

            builder
                .HasOne(tenderFile => tenderFile.Document)
                .WithMany()
                .HasForeignKey(tenderFile => tenderFile.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasOne(tenderFile => tenderFile.Tender)
               .WithMany(tender => tender.TenderFiles)
               .HasForeignKey(tenderFile => tenderFile.TenderId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
