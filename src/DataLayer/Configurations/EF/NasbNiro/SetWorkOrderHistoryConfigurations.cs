using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    internal class SetWorkOrderHistoryConfigurations : IEntityTypeConfiguration<WorkOrderHistory>
    {
        public void Configure(EntityTypeBuilder<WorkOrderHistory> builder)
        {
            builder.HasKey(x => x.Id);

            builder.HasOne(x => x.WorkOrder)
                .WithMany()
                .HasForeignKey(x => x.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
