
using Entities.Model.NasbNiro.Reports;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetProgressReportsConfigurations : IEntityTypeConfiguration<ProgressReport>
    {
        public void Configure(EntityTypeBuilder<ProgressReport> builder)
        {
            builder.ToTable("ProgressReports");

            // FX1: these two are percent values (0..100, computed as count * 100 / total). The column keeps its 16 integer digits and gains two decimals
            // (was decimal(18,2) by the EF default, which also made EF warn): widening only.
            builder.Property(x => x.WorkOrderLevelPlannedProgress).HasPrecision(20, 4);
            builder.Property(x => x.WorkOrderLevelActualProgress).HasPrecision(20, 4);

            builder
                .HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId);

            builder
                .HasOne(x => x.WorkOrder)
                .WithMany()
                .HasForeignKey(x => x.WorkOrderId);

            builder
             .HasOne(x => x.Process)
             .WithOne()
             .HasForeignKey<ProgressReport>(x => x.ProcessId);
        }
    }
}
