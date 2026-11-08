using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Integrity
{
    // Package D: three new tables. No existing table is changed.
    public static class IntegrityModelBuilderExtensions
    {
        public static void AddIntegrityConfigurations(this ModelBuilder builder)
        {
            builder.Entity<AuditChainRun>(b =>
            {
                b.ToTable("IntAuditChainRuns");
                b.Property(x => x.HeadHash).HasMaxLength(80);
                b.Property(x => x.BreakKind).HasMaxLength(30);
                b.Property(x => x.BreakEntityType).HasMaxLength(100);
                b.Property(x => x.Message).HasMaxLength(1000);
                b.HasIndex(x => x.StartedAt);
                b.HasIndex(x => new { x.Status, x.Kind });
            });

            builder.Entity<RestoreTestResult>(b =>
            {
                b.ToTable("IntRestoreTestResults");
                b.Property(x => x.RunId).HasMaxLength(40).IsRequired();
                b.Property(x => x.BackupFile).HasMaxLength(260);
                b.Property(x => x.Message).HasMaxLength(1000);
                b.HasIndex(x => x.RunId).IsUnique();
                b.HasIndex(x => x.StartedAt);
            });

            builder.Entity<SafetySettingChange>(b =>
            {
                b.ToTable("IntSafetySettingChanges");
                b.Property(x => x.Key).HasMaxLength(120).IsRequired();
                b.Property(x => x.OldValue).HasMaxLength(200);
                b.Property(x => x.NewValue).HasMaxLength(200).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => new { x.Store, x.Key, x.Status });
            });
        }
    }
}
