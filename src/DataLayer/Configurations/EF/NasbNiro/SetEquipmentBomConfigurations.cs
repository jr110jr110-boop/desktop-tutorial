using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetEquipmentBomConfigurations : IEntityTypeConfiguration<EquipmentBom>
    {
        public void Configure(EntityTypeBuilder<EquipmentBom> builder)
        {
            builder.ToTable("EquipmentBoms");

            builder
                .HasOne(equipmentBom => equipmentBom.EquipmentAssembly)
                .WithMany(equipmentBom => equipmentBom.EquipmentBoms)
                .HasForeignKey(equipmentBom => equipmentBom.EquipmentAssemblyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(equipmentBom => equipmentBom.EquipmentProcesses)
                .WithOne(equipmentProcesses => equipmentProcesses.EquipmentBom)
                .HasForeignKey(equipmentProcesses => equipmentProcesses.EquipmentBomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(x => x.Predecessor)
                .WithMany()
                .HasForeignKey(x => x.PredecessorId);
        }
    }
}
