using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetEquipmentAssemblyConfigurations : IEntityTypeConfiguration<EquipmentAssembly>
    {
        public void Configure(EntityTypeBuilder<EquipmentAssembly> builder)
        {
            builder.ToTable("EquipmentAssemblys");

            builder
                .HasOne(equipmentAssembly => equipmentAssembly.Equipment)
                .WithMany(equipment => equipment.EquipmentAssemblies)
                .HasForeignKey(equipmentAssembly => equipmentAssembly.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasMany(equipmentAssembly => equipmentAssembly.EquipmentBoms)
               .WithOne(equipmentBom => equipmentBom.EquipmentAssembly)
               .HasForeignKey(equipmentBom => equipmentBom.EquipmentAssemblyId)
               .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(x => x.Predecessor)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.PredecessorId);
        }
    }
}
