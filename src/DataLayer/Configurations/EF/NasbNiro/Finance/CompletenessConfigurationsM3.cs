using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Finance release F3, milestone 3. Rial amounts decimal(18,0), rates decimal(18,6).
    public static class CompletenessM3ModelBuilderExtensions
    {
        public static void AddCompletenessM3Configurations(this ModelBuilder builder)
        {
            builder.Entity<FnFacilityLimit>(b =>
            {
                b.ToTable("FnFacilityLimits");
                b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
                b.Property(x => x.LimitAmount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(300);
            });
            builder.Entity<FnLoan>(b =>
            {
                b.ToTable("FnLoans");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
                b.Property(x => x.ContractNo).HasMaxLength(60);
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.RatePercent).HasPrecision(18, 6);
                b.Property(x => x.FeeAmount).HasPrecision(18, 0);
                b.Property(x => x.Collateral).HasMaxLength(500);
                b.Property(x => x.InterestExpensed).HasPrecision(18, 0);
                b.Property(x => x.InterestPaid).HasPrecision(18, 0);
                b.Property(x => x.PrincipalPaid).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnLoanInstallment>(b =>
            {
                b.ToTable("FnLoanInstallments");
                b.Property(x => x.Principal).HasPrecision(18, 0);
                b.Property(x => x.Interest).HasPrecision(18, 0);
                b.Property(x => x.PenaltyPaid).HasPrecision(18, 0);
                b.HasIndex(x => new { x.LoanId, x.Sequence }).IsUnique();
                b.HasOne(x => x.Loan).WithMany(x => x.Installments).HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnLoanAccrual>(b =>
            {
                b.ToTable("FnLoanAccruals");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => new { x.LoanId, x.PeriodId }).IsUnique();
            });
            builder.Entity<FnTaxAdjustment>(b =>
            {
                b.ToTable("FnTaxAdjustments");
                b.Property(x => x.Description).HasMaxLength(500).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Source).HasMaxLength(300);
                b.HasIndex(x => x.FiscalYearId);
            });
            builder.Entity<FnReportView>(b =>
            {
                b.ToTable("FnReportViews");
                b.Property(x => x.ReportKey).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.FiltersJson).HasMaxLength(2000);
                b.Property(x => x.ColumnsJson).HasMaxLength(2000);
            });
            builder.Entity<FnCreditHold>(b =>
            {
                b.ToTable("FnCreditHolds");
                b.Property(x => x.Reason).HasMaxLength(500);
                b.HasIndex(x => x.CustomerId).IsUnique();
            });
            builder.Entity<FnBadDebtRun>(b =>
            {
                b.ToTable("FnBadDebtRuns");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Required).HasPrecision(18, 0);
                b.Property(x => x.PreviousBalance).HasPrecision(18, 0);
                b.Property(x => x.Change).HasPrecision(18, 0);
                b.Property(x => x.RatesJson).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnDunning>(b =>
            {
                b.ToTable("FnDunnings");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Bucket).HasMaxLength(20).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Text).HasMaxLength(2000);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnPaymentProposal>(b =>
            {
                b.ToTable("FnPaymentProposals");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.CashLimit).HasPrecision(18, 0);
                b.Property(x => x.Total).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnPaymentProposalLine>(b =>
            {
                b.ToTable("FnPaymentProposalLines");
                b.Property(x => x.ProviderName).HasMaxLength(200);
                b.Property(x => x.InvoiceNumber).HasMaxLength(60);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasOne(x => x.Proposal).WithMany(x => x.Lines).HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
