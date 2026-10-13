using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetEquipmentProcessConfigurations : IEntityTypeConfiguration<EquipmentProcess>
    {
        public void Configure(EntityTypeBuilder<EquipmentProcess> builder)
        {
            builder.ToTable("EquipmentProcesses");

            builder
               .HasOne(equipmentProcess => equipmentProcess.EquipmentBom)
               .WithMany(equipmentBom => equipmentBom.EquipmentProcesses)
               .HasForeignKey(equipmentProcess => equipmentProcess.EquipmentBomId)
               .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(equipmentProcess => equipmentProcess.EquipmentProcessFiles)
                .WithOne(equipmentProcessFile => equipmentProcessFile.EquipmentProcess)
                .HasForeignKey(equipmentProcessFile => equipmentProcessFile.EquipmentProcessId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
