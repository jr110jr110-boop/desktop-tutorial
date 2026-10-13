using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetContractGuarantyConfigurations : IEntityTypeConfiguration<ContractGuaranty>
    {
        public void Configure(EntityTypeBuilder<ContractGuaranty> builder)
        {
            builder
                .ToTable("ContractGuaranties");

            builder
                .HasKey(contractGuaranty => new { contractGuaranty.Id });

            builder
               .HasOne(contractGuaranty => contractGuaranty.ContractEntity)
               .WithMany(contract => contract.ContractGuaranties)
               .HasForeignKey(contractGuaranty => contractGuaranty.ContractEntityId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
