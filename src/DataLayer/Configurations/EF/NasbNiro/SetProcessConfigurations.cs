using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetProcessConfigurations : IEntityTypeConfiguration<ProcessEntity>
    {
        public void Configure(EntityTypeBuilder<ProcessEntity> builder)
        {
            builder.ToTable("Processes");

            builder
                .HasOne<Bom>(x => x.Bom)
                .WithMany(bom => bom.Processes)
                .HasForeignKey(process => process.BomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasMany<BuyOrderProcesses>(p => p.BuyOrderProcesses)
               .WithOne()
               .HasForeignKey(bop => bop.ProcessId)
               .OnDelete(DeleteBehavior.Restrict);

            //builder
            //    .HasOne<User>(p => p.User)
            //    .WithMany(user => user.Processes)
            //    .HasForeignKey(p => p.UserId)
            //    .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
