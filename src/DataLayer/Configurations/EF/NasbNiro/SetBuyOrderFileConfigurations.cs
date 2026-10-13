using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetBuyOrderFileConfigurations : IEntityTypeConfiguration<BuyOrderFile>
    {
        public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<BuyOrderFile> builder)
        {
            builder
              .ToTable("BuyOrderFiles");

            builder
               .HasKey(x => new { x.BuyOrderId, x.DocumentId });

            builder
                .HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(contractFile => contractFile.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasOne(x => x.BuyOrder)
               .WithMany(x => x.BuyOrderFiles)
               .HasForeignKey(c => c.BuyOrderId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
