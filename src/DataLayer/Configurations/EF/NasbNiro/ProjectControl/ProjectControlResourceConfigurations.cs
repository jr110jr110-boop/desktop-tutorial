using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.ProjectControl
{
    // Project control priority 4 (phases 4a and 4b): the resource pool, the standard rates, the resource plan of a contract, its assignments and their copy in a baseline.
    public static class ProjectControlResourceModelBuilderExtensions
    {
        public static void AddProjectControlResourceConfigurations(this ModelBuilder builder)
        {
            builder.Entity<PcResource>(b =>
            {
                b.ToTable("PcResources");
                b.Property(x => x.Code).HasMaxLength(50).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.Capacity).HasPrecision(9, 3);
                b.Property(x => x.ResponsibleUnit).HasMaxLength(30);
                b.Property(x => x.Notes).HasMaxLength(1000);
                b.HasIndex(x => x.Code).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => new { x.Kind, x.IsActive });
                b.HasIndex(x => x.ResponsibleUnit);
                b.HasOne(x => x.Calendar).WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Provider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcResource>().WithMany().HasForeignKey(x => x.RateResourceId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcResourceRate>(b =>
            {
                b.ToTable("PcResourceRates");
                b.Property(x => x.Rate).HasPrecision(22, 2);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.ResourceId, x.Status, x.EffectiveFrom });
                // one submitted or approved rate per resource and date (a draft or a rejected one may repeat the date)
                b.HasIndex(x => new { x.ResourceId, x.EffectiveFrom }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (1, 2)").HasDatabaseName("IX_PcResourceRates_OneLivePerDate");
                b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcResourcePlan>(b =>
            {
                b.ToTable("PcResourcePlans");
                b.Property(x => x.ReturnReason).HasMaxLength(1000);
                b.Property(x => x.EditorUserIds); // nvarchar(max): the list of people who edited the plan has no useful bound
                b.HasIndex(x => new { x.ContractId, x.Version }).IsUnique().HasFilter("[Deleted] = 0");
                // one open plan (draft, submitted or returned) per contract
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1, 3)").HasDatabaseName("IX_PcResourcePlans_OneOpenPerContract");
                b.HasIndex(x => x.ChangeRequestId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcActivityResource>(b =>
            {
                b.ToTable("PcActivityResources");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.ResponsibleRole).HasMaxLength(100);
                b.HasIndex(x => new { x.PlanId, x.ActivityId, x.ResourceId }).IsUnique();
                b.HasIndex(x => x.ResourceId);
                b.HasOne(x => x.Plan).WithMany(x => x.Assignments).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcResourcePlanSkip>(b =>
            {
                b.ToTable("PcResourcePlanSkips");
                b.HasIndex(x => new { x.PlanId, x.ActivityId }).IsUnique();
                b.HasOne(x => x.Plan).WithMany(x => x.Skips).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcResourceOverload>(b =>
            {
                b.ToTable("PcResourceOverloads");
                b.Property(x => x.PeakLoad).HasPrecision(18, 4);
                b.Property(x => x.PeakCapacity).HasPrecision(18, 4);
                b.Property(x => x.AcceptReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.Status, x.FromDate });
                // one open over-allocation per resource and first day
                b.HasIndex(x => new { x.ResourceId, x.FromDate }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] = 1").HasDatabaseName("IX_PcResourceOverloads_OneOpenPerStart");
                b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcLevelingProposal>(b =>
            {
                b.ToTable("PcLevelingProposals");
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.OverloadId, x.Status });
                b.HasIndex(x => new { x.Status, x.ContractId });
                b.HasIndex(x => x.ActivityId);
                b.HasOne(x => x.Overload).WithMany().HasForeignKey(x => x.OverloadId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcBaselineResource>(b =>
            {
                b.ToTable("PcBaselineResources");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.RateAtBaseline).HasPrecision(22, 2);
                b.HasIndex(x => new { x.BaselineId, x.ActivityId, x.ResourceId }).IsUnique();
                b.HasOne(x => x.Baseline).WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
