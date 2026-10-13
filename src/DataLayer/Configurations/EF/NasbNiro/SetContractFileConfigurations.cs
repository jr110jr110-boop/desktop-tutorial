using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetContractFileConfigurations : IEntityTypeConfiguration<ContractFile>
    {
        public void Configure(EntityTypeBuilder<ContractFile> builder)
        {
            builder
               .ToTable("ContractFiles");

            builder
               .HasKey(contractFile => new { contractFile.ContractId, contractFile.DocumentId });

            builder
                .HasOne(contractFile => contractFile.Document)
                .WithMany()
                .HasForeignKey(contractFile => contractFile.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasOne(contractFile => contractFile.Contract)
               .WithMany(contract => contract.ContractFiles)
               .HasForeignKey(contractFile => contractFile.ContractId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
