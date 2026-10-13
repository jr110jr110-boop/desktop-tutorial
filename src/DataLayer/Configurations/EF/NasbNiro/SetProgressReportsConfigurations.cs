
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
