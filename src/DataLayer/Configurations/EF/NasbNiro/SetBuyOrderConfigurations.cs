using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetBuyOrderConfigurations : IEntityTypeConfiguration<BuyOrder>
    {
        public void Configure(EntityTypeBuilder<BuyOrder> builder)
        {
            builder.ToTable("BuyOrders");

            builder
                .HasOne(buyOrder => buyOrder.WorkOrder)
                .WithMany(workOrder => workOrder.BuyOrders)
                .HasForeignKey(buyOrder => buyOrder.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(buyOrder => buyOrder.BuyOrderProcesses)
                .WithOne()
                .HasForeignKey(bop => bop.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(buyOrder => buyOrder.Inquiries)
                .WithOne(Inquiry => Inquiry.BuyOrder)
                .HasForeignKey(Inquiry => Inquiry.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
