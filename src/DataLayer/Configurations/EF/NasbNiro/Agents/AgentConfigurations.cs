using Entities.Model.NasbNiro.Agents;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Agents
{
    // Agent layer. Tables start with "Agent". Nothing cascades. Documents are referenced by plain ids (no foreign keys): the documents are of many tables.
    public static class AgentModelBuilderExtensions
    {
        private const int Code = 50;
        private const int StepCode = 60;
        private const int Title = 300;
        private const int Text = 1000;

        public static void AddAgentConfigurations(this ModelBuilder builder)
        {
            builder.Entity<AgentTask>(b =>
            {
                b.ToTable("AgentTasks");
                b.Property(x => x.StepCode).HasMaxLength(StepCode).IsRequired();
                b.Property(x => x.AgentCode).HasMaxLength(Code).IsRequired();
                b.Property(x => x.State).HasDefaultValue(AgentTaskState.Queued).HasSentinel((AgentTaskState)0);
                b.Property(x => x.TargetType).HasMaxLength(StepCode).IsRequired();
                b.Property(x => x.TargetNumber).HasMaxLength(StepCode);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Summary).HasMaxLength(Text);
                b.Property(x => x.GateRoles).HasMaxLength(Title);
                b.Property(x => x.Link).HasMaxLength(Title);
                b.Property(x => x.IsFallback).HasDefaultValue(false);
                b.Property(x => x.AttemptCount).HasDefaultValue(0);
                b.Property(x => x.LastError).HasMaxLength(Text);
                b.Property(x => x.OutputType).HasMaxLength(StepCode);
                b.Property(x => x.OutputNumber).HasMaxLength(StepCode);
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.ClosedReason).HasMaxLength(Title);
                b.Property(x => x.RowVersion).IsRowVersion();
                // one open task per step and document: the database keeps two apart (the in-memory test database does not enforce it, so the reconciler also looks before it inserts)
                b.HasIndex(x => new { x.StepCode, x.TargetType, x.TargetId }).IsUnique().HasFilter("[State] < 10 AND [Deleted] = 0").HasDatabaseName("IX_AgentTasks_Open");
                b.HasIndex(x => new { x.State, x.AgentCode, x.NextAttemptAt });
                b.HasIndex(x => new { x.ContractId, x.Station, x.State });
                b.HasIndex(x => new { x.TargetType, x.TargetId });
            });
            builder.Entity<AgentTaskEvent>(b =>
            {
                b.ToTable("AgentTaskEvents");
                b.Property(x => x.SeatCode).HasMaxLength(Code);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.TaskId, x.At });
                b.HasOne(x => x.Task).WithMany(t => t.Events).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<AgentStepSetting>(b =>
            {
                b.ToTable("AgentStepSettings");
                b.Property(x => x.StepCode).HasMaxLength(StepCode).IsRequired();
                b.Property(x => x.Enabled).HasDefaultValue(true).HasSentinel(true);
                b.Property(x => x.MaxAttempts).HasDefaultValue(3).ValueGeneratedNever();
                b.Property(x => x.GateDueHours).HasDefaultValue(48).ValueGeneratedNever();
                b.HasIndex(x => x.StepCode).IsUnique();
            });
        }
    }
}
