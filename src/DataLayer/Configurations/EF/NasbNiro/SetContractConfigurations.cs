using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetContractConfigurations : IEntityTypeConfiguration<ContractEntity>
    {
        public void Configure(EntityTypeBuilder<ContractEntity> builder)
        {
            builder
                .ToTable("Contracts");

            builder
                .HasMany(contract => contract.ContractFiles)
                .WithOne(contractFile => contractFile.Contract)
                .HasForeignKey(contractFile => contractFile.ContractId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(contract => contract.ContractEquipments)
                .WithOne(equipment => equipment.Contract)
                .HasForeignKey(equipment => equipment.ContractId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasMany(contract => contract.WorkOrders)
               .WithOne(workOrder => workOrder.Contract)
               .HasForeignKey(workOrder => workOrder.ContractId)
               .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne<Document>(x => x.ProjectCharterFile)
                .WithOne()
                .HasForeignKey<ContractEntity>(x => x.ProjectCharterFileId);

            builder
                .HasMany(c => c.ProvisionalStatements)
                .WithOne()
                .HasForeignKey(ps => ps.ContractId);
        }
    }
}
