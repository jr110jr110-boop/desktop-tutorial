using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetWorkOrderConfigurations : IEntityTypeConfiguration<WorkOrder>
    {
        public void Configure(EntityTypeBuilder<WorkOrder> builder)
        {
            builder.ToTable("WorkOrders");

            builder
                .HasOne<ContractEntity>(workOrder => workOrder.Contract)
                .WithMany(contract => contract.WorkOrders)
                .HasForeignKey(workOrder => workOrder.ContractId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany<AssemblyEntity>(workOrder => workOrder.Assemblies)
                .WithOne(assembly => assembly.WorkOrder)
                .HasForeignKey(assembly => assembly.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
               .HasMany(workOrder => workOrder.BuyOrders)
               .WithOne(buyOrder => buyOrder.WorkOrder)
               .HasForeignKey(buyOrder => buyOrder.WorkOrderId)
               .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
