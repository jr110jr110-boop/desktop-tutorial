using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Finance release F3 (completeness). Amounts in rial are decimal(18,0), foreign amounts decimal(18,4), rates and percentages decimal(18,6).
    public static class CompletenessModelBuilderExtensions
    {
        public static void AddCompletenessConfigurations(this ModelBuilder builder)
        {
            builder.Entity<FnLegalEntity>(b =>
            {
                b.ToTable("FnLegalEntities");
                b.Property(x => x.Code).HasMaxLength(20).IsRequired();
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Jurisdiction).HasMaxLength(10).IsRequired();
                b.Property(x => x.BaseCurrency).HasMaxLength(10).IsRequired();
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<FnUserSetting>(b =>
            {
                b.ToTable("FnUserSettings");
                b.Property(x => x.Key).HasMaxLength(50).IsRequired();
                b.Property(x => x.Value).HasMaxLength(500);
                b.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
            });
            builder.Entity<FnIntercompanyPair>(b =>
            {
                b.ToTable("FnIntercompanyPairs");
                b.Property(x => x.Title).HasMaxLength(200);
                b.HasOne(x => x.EntityA).WithMany().HasForeignKey(x => x.EntityAId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.EntityB).WithMany().HasForeignKey(x => x.EntityBId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.AccountA).WithMany().HasForeignKey(x => x.AccountAId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.AccountB).WithMany().HasForeignKey(x => x.AccountBId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnIntercompanyPost>(b =>
            {
                b.ToTable("FnIntercompanyPosts");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.AmountA).HasPrecision(18, 0);
                b.Property(x => x.AmountB).HasPrecision(18, 0);
                b.Property(x => x.Rate).HasPrecision(18, 6);
                b.Property(x => x.Description).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne(x => x.Pair).WithMany().HasForeignKey(x => x.PairId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnAuditLog>(b =>
            {
                b.ToTable("FnAuditLogs");
                b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Action).HasMaxLength(20).IsRequired();
                b.Property(x => x.Summary).HasMaxLength(300);
                b.Property(x => x.Ip).HasMaxLength(60);
                b.HasIndex(x => new { x.EntityType, x.EntityId });
                b.HasIndex(x => x.At);
                b.HasIndex(x => x.UserId);
            });

            builder.Entity<FnCloseChecklist>(b =>
            {
                b.ToTable("FnCloseChecklists");
                b.Property(x => x.OverrideReason).HasMaxLength(1000);
                b.Property(x => x.RedItems).HasMaxLength(2000);
                b.HasIndex(x => x.PeriodId).IsUnique();
                b.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnVoucherAttachment>(b =>
            {
                b.ToTable("FnVoucherAttachments");
                b.Property(x => x.Title).HasMaxLength(300);
                b.HasIndex(x => x.VoucherId);
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnAttachmentRule>(b =>
            {
                b.ToTable("FnAttachmentRules");
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnApprovalThreshold>(b =>
            {
                b.ToTable("FnApprovalThresholds");
                b.Property(x => x.MinAmount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
            });
            builder.Entity<FnVoucherTemplate>(b =>
            {
                b.ToTable("FnVoucherTemplates");
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Description).HasMaxLength(500);
            });
            builder.Entity<FnVoucherTemplateLine>(b =>
            {
                b.ToTable("FnVoucherTemplateLines");
                b.Property(x => x.Debit).HasPrecision(18, 0);
                b.Property(x => x.Credit).HasPrecision(18, 0);
                b.Property(x => x.Description).HasMaxLength(500);
                b.HasOne(x => x.Template).WithMany(x => x.Lines).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnTemplateInstance>(b =>
            {
                b.ToTable("FnTemplateInstances");
                b.HasIndex(x => new { x.TemplateId, x.PeriodId }).IsUnique();
                b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnMigrationAccountMap>(b =>
            {
                b.ToTable("FnMigrationAccountMaps");
                b.Property(x => x.SepidarCode).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.SepidarCode).IsUnique();
                b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnParallelRun>(b =>
            {
                b.ToTable("FnParallelRuns");
                b.Property(x => x.SignOffNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.LegalEntityId, x.PeriodId });
                b.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnParallelDifference>(b =>
            {
                b.ToTable("FnParallelDifferences");
                b.Property(x => x.AccountCode).HasMaxLength(50);
                b.Property(x => x.AccountTitle).HasMaxLength(300);
                b.Property(x => x.DetailCode).HasMaxLength(50);
                b.Property(x => x.PmisDebit).HasPrecision(18, 0);
                b.Property(x => x.PmisCredit).HasPrecision(18, 0);
                b.Property(x => x.SepidarDebit).HasPrecision(18, 0);
                b.Property(x => x.SepidarCredit).HasPrecision(18, 0);
                b.Property(x => x.Explanation).HasMaxLength(1000);
                b.HasIndex(x => x.RunId);
                b.HasOne(x => x.Run).WithMany(x => x.Differences).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FnExportLayoutColumn>(b =>
            {
                b.ToTable("FnExportLayoutColumns");
                b.Property(x => x.Key).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
            });
            builder.Entity<FnDetailSepidarMap>(b =>
            {
                b.ToTable("FnDetailSepidarMaps");
                b.Property(x => x.SepidarCode).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.DetailAccountId).IsUnique();
                b.HasOne(x => x.DetailAccount).WithMany().HasForeignKey(x => x.DetailAccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnVoucherExport>(b =>
            {
                b.ToTable("FnVoucherExports");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.TotalDebit).HasPrecision(18, 0);
                b.Property(x => x.TotalCredit).HasPrecision(18, 0);
                b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                b.Property(x => x.SupersedeReason).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.PeriodId, x.Status });
                b.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
