using Entities.Model.NasbNiro.AuditSeat;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.AuditSeat
{
    // Package 9: the AI internal auditor. Tables start with "Aus". Object references are plain ids (the audited objects are of many tables); the rows are history.
    public static class AuditSeatModelBuilderExtensions
    {
        public static void AddAuditSeatConfigurations(this ModelBuilder builder)
        {
            builder.Entity<AusProgram>(b =>
            {
                b.ToTable("AusPrograms");
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Scope).HasMaxLength(2000);
                b.Property(x => x.Criteria).HasMaxLength(2000);
                b.Property(x => x.Objectives).HasMaxLength(2000);
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.Property(x => x.ContentSha256).HasMaxLength(80);
                b.HasIndex(x => new { x.Year, x.Version });
                b.HasIndex(x => x.Status);
            });

            builder.Entity<AusProgramItem>(b =>
            {
                b.ToTable("AusProgramItems");
                b.Property(x => x.ProcessCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Criteria).HasMaxLength(1000);
                b.HasOne(x => x.Program).WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.ProgramId, x.ProcessCode });
            });

            builder.Entity<AusRuleState>(b =>
            {
                b.ToTable("AusRuleStates");
                b.Property(x => x.RuleCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.LastStatus).HasMaxLength(20);
                b.Property(x => x.LastError).HasMaxLength(500);
                b.HasIndex(x => x.RuleCode).IsUnique().HasFilter("[Deleted] = 0");
            });

            builder.Entity<AusRun>(b =>
            {
                b.ToTable("AusRuns");
                b.Property(x => x.Message).HasMaxLength(1000);
                b.HasIndex(x => x.StartedAt);
                b.HasIndex(x => x.Status);
            });

            builder.Entity<AusFinding>(b =>
            {
                b.ToTable("AusFindings");
                b.Property(x => x.Number).HasMaxLength(20).IsRequired();
                b.Property(x => x.RuleCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Standard).HasMaxLength(60);
                b.Property(x => x.Clause).HasMaxLength(60);
                b.Property(x => x.Procedure).HasMaxLength(60);
                b.Property(x => x.ProcessCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Description).HasMaxLength(2000);
                b.Property(x => x.ObjectType).HasMaxLength(60).IsRequired();
                b.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
                b.Property(x => x.ObjectNumber).HasMaxLength(80);
                b.Property(x => x.Link).HasMaxLength(200);
                b.Property(x => x.EvidenceSha256).HasMaxLength(80);
                b.Property(x => x.OwnerRole).HasMaxLength(60);
                b.Property(x => x.OwnerUnit).HasMaxLength(100);
                b.Property(x => x.RootCause).HasMaxLength(2000);
                b.Property(x => x.Correction).HasMaxLength(2000);
                b.Property(x => x.CorrectiveAction).HasMaxLength(2000);
                b.Property(x => x.RecheckSha256).HasMaxLength(80);
                b.Property(x => x.QaNote).HasMaxLength(1000);
                b.Property(x => x.PmoNote).HasMaxLength(1000);
                b.Property(x => x.CeoNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.Year, x.Sequence }).IsUnique();
                b.HasIndex(x => x.Number).IsUnique();
                // one open finding per rule and object (Closed = 6 and Voided = 7 are history); a recurrence is a new row that refers to the closed one
                b.HasIndex(x => new { x.RuleCode, x.ObjectKey }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] NOT IN (6, 7)");
                b.HasIndex(x => new { x.Status, x.Severity });
                b.HasIndex(x => x.DueDate);
                b.HasIndex(x => x.ProcessCode);
                b.HasIndex(x => x.RecurrenceOfId);
            });

            builder.Entity<AusFindingEvent>(b =>
            {
                b.ToTable("AusFindingEvents");
                b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.FindingId, x.At });
            });

            builder.Entity<AusReport>(b =>
            {
                b.ToTable("AusReports");
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Recipients).HasMaxLength(200);
                b.Property(x => x.NarrativeSource).HasMaxLength(20);
                b.Property(x => x.Sha256).HasMaxLength(80);
                b.HasIndex(x => new { x.Kind, x.PeriodTo });
                // one report per kind and period (a missed day is caught up once, a second call finds the first)
                b.HasIndex(x => new { x.Kind, x.PeriodFrom }).IsUnique().HasFilter("[Deleted] = 0");
            });

            builder.Entity<AusAcceptance>(b =>
            {
                b.ToTable("AusAcceptances");
                b.Property(x => x.RuleCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
                b.Property(x => x.ObjectNumber).HasMaxLength(80);
                b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                b.Property(x => x.RefusalReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.RuleCode, x.ObjectKey });
                b.HasIndex(x => new { x.Status, x.ExpiresOn });
            });

            builder.Entity<AusAudit>(b =>
            {
                b.ToTable("AusAudits");
                b.Property(x => x.Number).HasMaxLength(20).IsRequired();
                b.Property(x => x.ProcessCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.AuditeeUnit).HasMaxLength(100);
                b.Property(x => x.Summary).HasMaxLength(2000);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.Status, x.PlannedDate });
            });

            builder.Entity<AusAuditCheck>(b =>
            {
                b.ToTable("AusAuditChecks");
                b.Property(x => x.RuleCode).HasMaxLength(30);
                b.Property(x => x.Standard).HasMaxLength(60);
                b.Property(x => x.Clause).HasMaxLength(60);
                b.Property(x => x.Question).HasMaxLength(500).IsRequired();
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasOne(x => x.Audit).WithMany().HasForeignKey(x => x.AuditId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.AuditId, x.Sort });
            });
        }
    }
}
