using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetBuyOrderProcessesConfigurations : IEntityTypeConfiguration<BuyOrderProcesses>
    {
        public void Configure(EntityTypeBuilder<BuyOrderProcesses> builder)
        {
            builder.ToTable("OrderProcesses");
            builder.HasKey(bop => new { bop.ProcessId, bop.BuyOrderId });

            builder
                .HasOne<BuyOrder>(x => x.BuyOrder)
                .WithMany(bo => bo.BuyOrderProcesses)
                .HasForeignKey(bop => bop.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne<ProcessEntity>(x => x.Process)
                .WithMany()
                .HasForeignKey(bop => bop.ProcessId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
