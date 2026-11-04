using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Tasks
{
    // Task management (package P-B7a). All tables start with "Tm". Deletes are restricted except for child rows, which go with their task.
    public static class TaskModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddTaskConfigurations(this ModelBuilder builder)
        {
            builder.Entity<TmTask>(b =>
            {
                b.ToTable("TmTasks");
                b.Property(x => x.Number).HasMaxLength(40);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Description).HasMaxLength(4000);
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.RequestType).HasMaxLength(100);
                b.Property(x => x.Requester).HasMaxLength(200);
                b.Property(x => x.ReferringOrganisation).HasMaxLength(200);
                b.Property(x => x.StatusReason).HasMaxLength(1000);
                b.Property(x => x.ReminderDays).HasMaxLength(60);
                b.Property(x => x.EstimatedHours).HasPrecision(12, 2);
                b.Property(x => x.ActualHours).HasPrecision(12, 2);
                b.Property(x => x.EstimatedCost).HasPrecision(22, 2);
                b.Property(x => x.ActualCost).HasPrecision(22, 2);
                b.Property(x => x.PercentComplete).HasPrecision(9, 4);
                b.Property(x => x.ApprovedPercent).HasPrecision(9, 4);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.DueDate);
                b.HasIndex(x => x.ContractId);
                b.HasIndex(x => x.ActivityId);
                b.HasIndex(x => x.ParentTaskId);
                b.HasIndex(x => x.DecisionId);
                b.HasIndex(x => x.RiskId);
                b.HasIndex(x => x.IssueId);
                b.HasOne<TmTask>().WithMany().HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfDecision>().WithMany().HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfLetter>().WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcRisk>().WithMany().HasForeignKey(x => x.RiskId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcIssue>().WithMany().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TmTaskAssignee>(b =>
            {
                b.ToTable("TmTaskAssignees");
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.EndReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.TaskId, x.State });
                b.HasIndex(x => new { x.UserId, x.State });
                b.HasIndex(x => new { x.PositionId, x.State });
                b.HasOne(x => x.Task).WithMany(x => x.Assignees).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<TmTaskAssignee>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TmTaskChecklistItem>(b =>
            {
                b.ToTable("TmTaskChecklistItems");
                b.Property(x => x.Text).HasMaxLength(500).IsRequired();
                b.HasIndex(x => x.TaskId);
                b.HasOne(x => x.Task).WithMany(x => x.Checklist).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<TmTaskComment>(b =>
            {
                b.ToTable("TmTaskComments");
                b.Property(x => x.Text).HasMaxLength(4000).IsRequired();
                b.HasIndex(x => x.TaskId);
                b.HasOne(x => x.Task).WithMany(x => x.Comments).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<TmTaskComment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TmTaskWatcher>(b =>
            {
                b.ToTable("TmTaskWatchers");
                b.HasIndex(x => new { x.TaskId, x.UserId }).IsUnique();
                b.HasIndex(x => x.UserId);
                b.HasOne(x => x.Task).WithMany(x => x.Watchers).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<TmTaskHistory>(b =>
            {
                b.ToTable("TmTaskHistory");
                b.Property(x => x.FromValue).HasMaxLength(500);
                b.Property(x => x.ToValue).HasMaxLength(500);
                b.Property(x => x.Text).HasMaxLength(2000);
                b.HasIndex(x => new { x.TaskId, x.At });
                b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
