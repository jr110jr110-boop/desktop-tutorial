using Entities.Model.NasbNiro.ContractEquipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetContractEquipmentConfigurations : IEntityTypeConfiguration<ContractEquipment>
    {
        public void Configure(EntityTypeBuilder<ContractEquipment> builder)
        {
            builder.ToTable("ContractEquipments");

            builder.HasKey(contractEquipment =>
                new { contractEquipment.ContractId, contractEquipment.EquipmentId });

            builder
              .HasOne(contractEquipment => contractEquipment.Contract)
              .WithMany(contract => contract.ContractEquipments)
              .HasForeignKey(contractEquipment => contractEquipment.ContractId)
              .OnDelete(DeleteBehavior.Restrict);

            builder
              .HasOne(contractEquipment => contractEquipment.Equipment)
              .WithMany(equipment => equipment.ContractEquipments)
              .HasForeignKey(contractEquipment => contractEquipment.EquipmentId)
              .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
