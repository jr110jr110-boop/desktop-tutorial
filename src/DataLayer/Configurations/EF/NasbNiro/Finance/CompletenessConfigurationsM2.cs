using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Finance release F3, milestone 2. Rial amounts decimal(18,0), foreign amounts decimal(18,4), rates and percentages decimal(18,6).
    public static class CompletenessM2ModelBuilderExtensions
    {
        public static void AddCompletenessM2Configurations(this ModelBuilder builder)
        {
            builder.Entity<FnCurrency>(b =>
            {
                b.ToTable("FnCurrencies");
                b.Property(x => x.Code).HasMaxLength(10).IsRequired();
                b.Property(x => x.Title).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<FnExchangeRate>(b =>
            {
                b.ToTable("FnExchangeRates");
                b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.Buy).HasPrecision(18, 6);
                b.Property(x => x.Sell).HasPrecision(18, 6);
                b.Property(x => x.Average).HasPrecision(18, 6);
                b.Property(x => x.Note).HasMaxLength(300);
                b.HasIndex(x => new { x.CurrencyCode, x.Date, x.RateType }).IsUnique();
            });
            builder.Entity<FnFxRevaluation>(b =>
            {
                b.ToTable("FnFxRevaluations");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.TotalGain).HasPrecision(18, 0);
                b.Property(x => x.TotalLoss).HasPrecision(18, 0);
                b.Property(x => x.Warning).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.LegalEntityId, x.PeriodId, x.Sequence }).IsUnique();
            });
            builder.Entity<FnFxRevaluationLine>(b =>
            {
                b.ToTable("FnFxRevaluationLines");
                b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.CurrencyBalance).HasPrecision(18, 4);
                b.Property(x => x.BookBalance).HasPrecision(18, 0);
                b.Property(x => x.Rate).HasPrecision(18, 6);
                b.Property(x => x.NewBalance).HasPrecision(18, 0);
                b.Property(x => x.Difference).HasPrecision(18, 0);
                b.HasIndex(x => x.RunId);
                b.HasOne(x => x.Run).WithMany(x => x.Lines).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnFxSettlement>(b =>
            {
                b.ToTable("FnFxSettlements");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Direction).HasMaxLength(20).IsRequired();
                b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.CurrencyAmount).HasPrecision(18, 4);
                b.Property(x => x.BookRate).HasPrecision(18, 6);
                b.Property(x => x.SettleRate).HasPrecision(18, 6);
                b.Property(x => x.BookBase).HasPrecision(18, 0);
                b.Property(x => x.SettledBase).HasPrecision(18, 0);
                b.Property(x => x.Difference).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnLetterOfCredit>(b =>
            {
                b.ToTable("FnLettersOfCredit");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.LcNumber).HasMaxLength(60);
                b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.Amount).HasPrecision(18, 4);
                b.Property(x => x.MarginPercent).HasPrecision(18, 6);
                b.Property(x => x.MarginPaid).HasPrecision(18, 0);
                b.Property(x => x.FeesPaid).HasPrecision(18, 0);
                b.Property(x => x.SettlementRate).HasPrecision(18, 6);
                b.Property(x => x.SettledBase).HasPrecision(18, 0);
                b.Property(x => x.ForeignInvoiceRef).HasMaxLength(100);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnLcPosting>(b =>
            {
                b.ToTable("FnLcPostings");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(300);
                b.HasIndex(x => x.LcId);
                b.HasOne(x => x.Lc).WithMany(x => x.Postings).HasForeignKey(x => x.LcId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FnBudget>(b =>
            {
                b.ToTable("FnBudgets");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.FiscalYearId, x.Version }).IsUnique();
                b.HasOne(x => x.FiscalYear).WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBudgetLine>(b =>
            {
                b.ToTable("FnBudgetLines");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(300);
                b.HasIndex(x => x.BudgetId);
                b.HasOne(x => x.Budget).WithMany(x => x.Lines).HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FnOverheadRate>(b =>
            {
                b.ToTable("FnOverheadRates");
                b.Property(x => x.Rate).HasPrecision(18, 6);
                b.Property(x => x.Source).HasMaxLength(300);
                b.HasIndex(x => new { x.CostCenterId, x.ValidFrom });
            });
            builder.Entity<FnAllocationKey>(b =>
            {
                b.ToTable("FnAllocationKeys");
                b.Property(x => x.Basis).HasMaxLength(30).IsRequired();
                b.Property(x => x.Value).HasPrecision(18, 6);
                b.Property(x => x.Source).HasMaxLength(300);
                b.HasIndex(x => new { x.FromCostCenterId, x.ValidFrom });
            });
            builder.Entity<FnWorkOrderBase>(b =>
            {
                b.ToTable("FnWorkOrderBases");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.HasIndex(x => new { x.PeriodId, x.WorkOrderId, x.CostCenterId, x.Base }).IsUnique();
            });
            builder.Entity<FnOverheadAllocation>(b =>
            {
                b.ToTable("FnOverheadAllocations");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.TotalAllocated).HasPrecision(18, 0);
                b.Property(x => x.TotalAbsorbed).HasPrecision(18, 0);
                b.Property(x => x.Variance).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.LegalEntityId, x.PeriodId }).IsUnique();
            });
            builder.Entity<FnStandardCost>(b =>
            {
                b.ToTable("FnStandardCosts");
                b.Property(x => x.Cost).HasPrecision(18, 4);
                b.Property(x => x.Source).HasMaxLength(300);
                b.HasIndex(x => new { x.ItemId, x.ValidFrom });
            });
            builder.Entity<FnWorkOrderClose>(b =>
            {
                b.ToTable("FnWorkOrderCloses");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Material).HasPrecision(18, 0);
                b.Property(x => x.Labour).HasPrecision(18, 0);
                b.Property(x => x.Overhead).HasPrecision(18, 0);
                b.Property(x => x.Subcontract).HasPrecision(18, 0);
                b.Property(x => x.TotalCost).HasPrecision(18, 0);
                b.Property(x => x.Variance).HasPrecision(18, 0);
                b.Property(x => x.UnitCost).HasPrecision(18, 4);
                b.Property(x => x.FgAmount).HasPrecision(18, 0);
                b.Property(x => x.RelievedAmount).HasPrecision(18, 0);
                b.Property(x => x.ReopenReason).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.WorkOrderId);
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("[IsOpen] = 0").HasDatabaseName("IX_FnWorkOrderCloses_ActiveClose");
            });
            builder.Entity<FnContractRevenue>(b =>
            {
                b.ToTable("FnContractRevenues");
                b.Property(x => x.ContractPrice).HasPrecision(18, 0);
                b.Property(x => x.ProgressSource).HasMaxLength(20).IsRequired();
                b.Property(x => x.ProgressMeasure).HasMaxLength(20).IsRequired();
                b.HasIndex(x => x.ContractId).IsUnique();
            });
            builder.Entity<FnRevenueRecognition>(b =>
            {
                b.ToTable("FnRevenueRecognitions");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.ProgressPercent).HasPrecision(18, 6);
                b.Property(x => x.ContractPrice).HasPrecision(18, 0);
                b.Property(x => x.EstimatedCost).HasPrecision(18, 0);
                b.Property(x => x.EarnedToDate).HasPrecision(18, 0);
                b.Property(x => x.RecognisedBefore).HasPrecision(18, 0);
                b.Property(x => x.RevenueThisRun).HasPrecision(18, 0);
                b.Property(x => x.BilledToDate).HasPrecision(18, 0);
                b.Property(x => x.ContractAsset).HasPrecision(18, 0);
                b.Property(x => x.ContractLiability).HasPrecision(18, 0);
                b.Property(x => x.LossProvision).HasPrecision(18, 0);
                b.Property(x => x.CostOfSales).HasPrecision(18, 0);
                b.HasIndex(x => new { x.ContractId, x.PeriodId });
            });

            builder.Entity<FnDeferral>(b =>
            {
                b.ToTable("FnDeferrals");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne<FnAccount>().WithMany().HasForeignKey(x => x.BalanceAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnAccount>().WithMany().HasForeignKey(x => x.ResultAccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnDeferralEntry>(b =>
            {
                b.ToTable("FnDeferralEntries");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => new { x.DeferralId, x.PeriodId }).IsUnique();
                b.HasOne(x => x.Deferral).WithMany(x => x.Entries).HasForeignKey(x => x.DeferralId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnProvision>(b =>
            {
                b.ToTable("FnProvisions");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.Balance).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne<FnAccount>().WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnAccount>().WithMany().HasForeignKey(x => x.LiabilityAccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnProvisionMovement>(b =>
            {
                b.ToTable("FnProvisionMovements");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Reason).HasMaxLength(500);
                b.HasIndex(x => x.ProvisionId);
                b.HasOne(x => x.Provision).WithMany(x => x.Movements).HasForeignKey(x => x.ProvisionId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
