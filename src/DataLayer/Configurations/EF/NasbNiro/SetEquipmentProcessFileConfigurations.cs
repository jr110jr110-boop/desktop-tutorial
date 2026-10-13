using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetEquipmentProcessFileConfigurations : IEntityTypeConfiguration<EquipmentProcessFile>
    {
        public void Configure(EntityTypeBuilder<EquipmentProcessFile> builder)
        {
            builder
                 .ToTable("EquipmentProcessFiles");

            builder
                .HasKey(epf => new { epf.EquipmentProcessId, epf.DocumentId });

            //builder
            //    .HasOne(epf => epf.Document)
            //    .WithMany()
            //    .HasForeignKey(epf => epf.DocumentId)
            //    .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasOne(epf => epf.EquipmentProcess)
               .WithMany(ep => ep.EquipmentProcessFiles)
               .HasForeignKey(epf => epf.EquipmentProcessId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
