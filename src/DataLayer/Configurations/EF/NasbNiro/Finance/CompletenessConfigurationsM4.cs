using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Finance release F3b. Rial amounts decimal(18,0), days / rates decimal(18,4..6).
    public static class CompletenessM4ModelBuilderExtensions
    {
        public static void AddCompletenessM4Configurations(this ModelBuilder builder)
        {
            builder.Entity<FnPaymentLine>(b => { b.Property(x => x.CurrencyAmount).HasPrecision(18, 4); b.Property(x => x.Rate).HasPrecision(18, 6); });
            builder.Entity<FnReceiptLine>(b => { b.Property(x => x.CurrencyAmount).HasPrecision(18, 4); b.Property(x => x.Rate).HasPrecision(18, 6); });
            builder.Entity<FnBankGuarantee>(b =>
            {
                b.ToTable("FnBankGuarantees");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
                b.Property(x => x.GuaranteeNo).HasMaxLength(60);
                b.Property(x => x.PartyName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.FeeAmount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.Direction, x.Status });
                b.HasOne(x => x.RegistrationVoucher).WithMany().HasForeignKey(x => x.RegistrationVoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBankGuaranteeEvent>(b =>
            {
                b.ToTable("FnBankGuaranteeEvents");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.FeeAmount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasOne(x => x.Guarantee).WithMany(x => x.Events).HasForeignKey(x => x.GuaranteeId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBankGuaranteeLimit>(b =>
            {
                b.ToTable("FnBankGuaranteeLimits");
                b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
                b.Property(x => x.Limit).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.BankName).IsUnique();
            });
            builder.Entity<FnInsuranceClearance>(b =>
            {
                b.ToTable("FnInsuranceClearances");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.LetterNo).HasMaxLength(60);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.ProviderId, x.ValidUntil });
            });
            builder.Entity<FnRetentionRelease>(b =>
            {
                b.ToTable("FnRetentionReleases");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.ProviderId);
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnTermination>(b =>
            {
                b.ToTable("FnTerminations");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.EditorUserIds).HasMaxLength(200);
                b.Property(x => x.YearsOfService).HasPrecision(18, 4);
                b.Property(x => x.MonthlyBase).HasPrecision(18, 0);
                b.Property(x => x.DailyWage).HasPrecision(18, 0);
                b.Property(x => x.SeveranceDaysPerYear).HasPrecision(18, 4);
                b.Property(x => x.UnusedLeaveDays).HasPrecision(18, 4);
                b.Property(x => x.NoticeDays).HasPrecision(18, 4);
                b.Property(x => x.SeverancePay).HasPrecision(18, 0);
                b.Property(x => x.LeavePay).HasPrecision(18, 0);
                b.Property(x => x.NoticePay).HasPrecision(18, 0);
                b.Property(x => x.Total).HasPrecision(18, 0);
                b.Property(x => x.TaxAmount).HasPrecision(18, 0);
                b.Property(x => x.InsuranceAmount).HasPrecision(18, 0);
                b.Property(x => x.NetPayable).HasPrecision(18, 0);
                b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[Status] <> 2").HasDatabaseName("IX_FnTerminations_Employee_Active");
                b.Property(x => x.FromSeveranceProvision).HasPrecision(18, 0);
                b.Property(x => x.FromLeaveProvision).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBonusRun>(b =>
            {
                b.ToTable("FnBonusRuns");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.EditorUserIds).HasMaxLength(200);
                b.Property(x => x.BonusDays).HasPrecision(18, 4);
                b.Property(x => x.CapDays).HasPrecision(18, 4);
                b.Property(x => x.MinimumDailyWage).HasPrecision(18, 0);
                b.Property(x => x.TotalGross).HasPrecision(18, 0);
                b.Property(x => x.TotalTax).HasPrecision(18, 0);
                b.Property(x => x.TotalNet).HasPrecision(18, 0);
                b.Property(x => x.FromProvision).HasPrecision(18, 0);
                b.Property(x => x.ExpensedNow).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.Year).IsUnique().HasFilter("[Status] <> 2").HasDatabaseName("IX_FnBonusRuns_Year_Active");
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBonusLine>(b =>
            {
                b.ToTable("FnBonusLines");
                b.Property(x => x.DailyWage).HasPrecision(18, 0);
                b.Property(x => x.Gross).HasPrecision(18, 0);
                b.Property(x => x.MonthlyTaxable).HasPrecision(18, 0);
                b.Property(x => x.Tax).HasPrecision(18, 0);
                b.Property(x => x.Net).HasPrecision(18, 0);
                b.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
                b.HasOne(x => x.Run).WithMany(x => x.Lines).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnWorkOrderEstimate>(b =>
            {
                b.ToTable("FnWorkOrderEstimates");
                b.Property(x => x.Material).HasPrecision(18, 0);
                b.Property(x => x.Labour).HasPrecision(18, 0);
                b.Property(x => x.Overhead).HasPrecision(18, 0);
                b.Property(x => x.Subcontract).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("[IsActive] = 1");
            });
            builder.Entity<FnSupplierNote>(b =>
            {
                b.ToTable("FnSupplierNotes");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.EditorUserIds).HasMaxLength(200);
                b.Property(x => x.NoteNumber).HasMaxLength(60);
                b.Property(x => x.NetAmount).HasPrecision(18, 0);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.Property(x => x.Reason).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.ProviderId, x.Date });
                b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnSupplierStatement>(b =>
            {
                b.ToTable("FnSupplierStatements");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.TheirBalance).HasPrecision(18, 0);
                b.Property(x => x.OurBalance).HasPrecision(18, 0);
                b.Property(x => x.Difference).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.ProviderId);
            });
            builder.Entity<FnSupplierStatementLine>(b =>
            {
                b.ToTable("FnSupplierStatementLines");
                b.Property(x => x.DocumentNumber).HasMaxLength(60);
                b.Property(x => x.Description).HasMaxLength(300);
                b.Property(x => x.Credit).HasPrecision(18, 0);
                b.Property(x => x.Debit).HasPrecision(18, 0);
                b.Property(x => x.MatchedKind).HasMaxLength(20);
                b.Property(x => x.MatchedNumber).HasMaxLength(60);
                b.HasOne(x => x.Statement).WithMany(x => x.Lines).HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnConfirmationLetter>(b =>
            {
                b.ToTable("FnConfirmationLetters");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.LedgerBalance).HasPrecision(18, 0);
                b.Property(x => x.StatementBalance).HasPrecision(18, 0);
                b.Property(x => x.ConfirmedAmount).HasPrecision(18, 0);
                b.Property(x => x.ResponseNote).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.CustomerId);
            });
        }
    }
}
