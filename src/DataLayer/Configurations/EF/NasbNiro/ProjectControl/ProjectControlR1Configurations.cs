using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.ProjectControl
{
    // Project control remediation R1c (migration ProjectControlR1): the new tables and the new columns of the older ones.
    public static class ProjectControlR1ModelBuilderExtensions
    {
        public static void AddProjectControlR1Configurations(this ModelBuilder builder)
        {
            builder.Entity<PcProgressEvent>(b =>
            {
                b.ToTable("PcProgressEvents");
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.Source).HasMaxLength(30);
                b.HasIndex(x => new { x.ActivityId, x.EventDate, x.Id });
                b.HasIndex(x => new { x.ContractId, x.EventDate });
            });
            builder.Entity<PcSnapshotRequest>(b =>
            {
                b.ToTable("PcSnapshotRequests");
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.DataDate }).IsUnique().HasFilter("[Deleted] = 0 AND [State] IN (0, 1)");
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcShipmentLine>(b =>
            {
                b.ToTable("PcShipmentLines");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Description).HasMaxLength(300);
                b.HasIndex(x => x.ShipmentId);
                b.HasOne(x => x.Shipment).WithMany(x => x.Lines).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcCostCheck>(b =>
            {
                b.ToTable("PcCostChecks");
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.CostEntryId).IsUnique();
            });

            // new columns of the older tables
            builder.Entity<PcVariationOrder>(b =>
            {
                b.Property(x => x.ApprovedByRole).HasMaxLength(20);
            });
            builder.Entity<PcBaselineActivity>(b =>
            {
                b.Property(x => x.EffectiveWeight).HasPrecision(12, 8);
            });
            builder.Entity<PcBaselineCostPeriod>(b =>
            {
                b.HasIndex(x => new { x.BaselineId, x.CalendarId, x.PeriodStart }).IsUnique();
            });
            builder.Entity<PcContractReview>(b =>
            {
                b.HasIndex(x => new { x.ContractId, x.Stage });
            });
        }
    }
}
