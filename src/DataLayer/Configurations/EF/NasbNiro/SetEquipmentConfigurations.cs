using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetEquipmentConfigurations : IEntityTypeConfiguration<Equipment>
    {
        public void Configure(EntityTypeBuilder<Equipment> builder)
        {
            builder
                .ToTable("Equipments");

            builder
               .HasMany(equipment => equipment.ContractEquipments)
               .WithOne(contract => contract.Equipment)
               .HasForeignKey(equipment => equipment.EquipmentId)
               .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(equipment => equipment.EquipmentAssemblies)
                .WithOne(equipmentAssemblie => equipmentAssemblie.Equipment)
                .HasForeignKey(equipmentAssemblie => equipmentAssemblie.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
