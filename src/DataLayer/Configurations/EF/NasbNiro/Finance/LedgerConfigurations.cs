using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // General ledger, treasury, receivables and tax (release F1). All tables start with "Fn".
    // Amounts in rial are decimal(18,0), foreign amounts decimal(18,4), rates and percentages decimal(18,6).
    public static class LedgerModelBuilderExtensions
    {
        public static void AddLedgerConfigurations(this ModelBuilder builder)
        {
            builder.Entity<FnSetting>(b =>
            {
                b.ToTable("FnSettings");
                b.Property(x => x.Key).HasMaxLength(50).IsRequired();
                b.Property(x => x.Value).HasMaxLength(500);
                b.Property(x => x.Title).HasMaxLength(200);
                b.HasIndex(x => x.Key).IsUnique();
            });

            builder.Entity<FnFiscalYear>(b =>
            {
                b.ToTable("FnFiscalYears");
                b.Property(x => x.Title).HasMaxLength(20).IsRequired();
                b.Property(x => x.Jurisdiction).HasMaxLength(10);
                b.Property(x => x.LegalEntityId).HasDefaultValue(1);
                b.HasIndex(x => new { x.LegalEntityId, x.Year }).IsUnique();
                b.HasOne(x => x.OpeningVoucher).WithMany().HasForeignKey(x => x.OpeningVoucherId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ClosingVoucher).WithMany().HasForeignKey(x => x.ClosingVoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPeriod>(b =>
            {
                b.ToTable("FnPeriods");
                b.Property(x => x.Title).HasMaxLength(100);
                b.HasIndex(x => new { x.FiscalYearId, x.Number }).IsUnique();
                b.HasOne(x => x.FiscalYear).WithMany(x => x.Periods).HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnPeriodLog>(b =>
            {
                b.ToTable("FnPeriodLogs");
                b.Property(x => x.Action).HasMaxLength(30);
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnAccount>(b =>
            {
                b.ToTable("FnAccounts");
                b.Property(x => x.Code).HasMaxLength(10).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.DetailTypes).HasMaxLength(200);
                b.Property(x => x.Detail2Types).HasMaxLength(200);
                b.Property(x => x.StatementLine).HasMaxLength(30);
                b.Property(x => x.SepidarCode).HasMaxLength(50);
                b.Property(x => x.Jurisdiction).HasMaxLength(10);
                b.HasIndex(x => x.Code).IsUnique();
                b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnDetailAccount>(b =>
            {
                b.ToTable("FnDetailAccounts");
                b.Property(x => x.Code).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.HasIndex(x => x.Code).IsUnique();
                b.HasIndex(x => new { x.Type, x.LinkedEntityId }).IsUnique().HasFilter("[LinkedEntityId] IS NOT NULL");
            });

            builder.Entity<FnVoucher>(b =>
            {
                b.ToTable("FnVouchers");
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.SourceType).HasMaxLength(50);
                b.Property(x => x.SourceNumber).HasMaxLength(50);
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.HasIndex(x => new { x.FiscalYearId, x.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
                b.HasIndex(x => x.AccountingEventId).IsUnique().HasFilter("[AccountingEventId] IS NOT NULL");
                b.HasIndex(x => x.ReversesVoucherId).IsUnique().HasFilter("[ReversesVoucherId] IS NOT NULL");
                b.HasIndex(x => x.Date);
                b.HasIndex(x => new { x.FiscalYearId, x.Status });
                b.HasOne(x => x.FiscalYear).WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ReversesVoucher).WithMany().HasForeignKey(x => x.ReversesVoucherId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnVoucherLine>(b =>
            {
                b.ToTable("FnVoucherLines");
                b.Property(x => x.Debit).HasPrecision(18, 0);
                b.Property(x => x.Credit).HasPrecision(18, 0);
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.CurrencyAmount).HasPrecision(18, 4);
                b.Property(x => x.Rate).HasPrecision(18, 6);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.SourceRole).HasMaxLength(50);
                b.HasIndex(x => x.AccountId);
                b.HasIndex(x => x.Detail1Id);
                b.HasIndex(x => x.ContractId);
                b.HasOne(x => x.Voucher).WithMany(x => x.Lines).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Detail1).WithMany().HasForeignKey(x => x.Detail1Id).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Detail2).WithMany().HasForeignKey(x => x.Detail2Id).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnTaxRate>(b =>
            {
                b.ToTable("FnTaxRates");
                b.Property(x => x.Rate).HasPrecision(18, 6);
                b.Property(x => x.Source).HasMaxLength(500);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.Jurisdiction).HasMaxLength(10);
                b.HasIndex(x => new { x.Kind, x.EffectiveFrom });
            });
            builder.Entity<FnPaymentDeduction>(b =>
            {
                b.ToTable("FnPaymentDeductions");
                b.Property(x => x.Base).HasPrecision(18, 0);
                b.Property(x => x.RatePercent).HasPrecision(18, 6);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasOne(x => x.PaymentRequest).WithMany().HasForeignKey(x => x.PaymentRequestId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ treasury
            builder.Entity<FnBankAccount>(b =>
            {
                b.ToTable("FnBankAccounts");
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.BankName).HasMaxLength(100);
                b.Property(x => x.Branch).HasMaxLength(100);
                b.Property(x => x.AccountNumber).HasMaxLength(50);
                b.Property(x => x.Iban).HasMaxLength(34);
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.LegalEntityId).HasDefaultValue(1);
                b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnCashBox>(b =>
            {
                b.ToTable("FnCashBoxes");
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.CustodianTitle).HasMaxLength(200);
            });
            builder.Entity<FnPettyCash>(b =>
            {
                b.ToTable("FnPettyCashes");
                b.Property(x => x.HolderTitle).HasMaxLength(200);
                b.Property(x => x.Ceiling).HasPrecision(18, 0);
                b.Property(x => x.Balance).HasPrecision(18, 0);
                b.HasIndex(x => x.HolderUserId).IsUnique();
            });

            builder.Entity<FnCustomer>(b =>
            {
                b.ToTable("FnCustomers");
                b.Property(x => x.Name).HasMaxLength(300).IsRequired();
                b.Property(x => x.EconomicCode).HasMaxLength(20);
                b.Property(x => x.NationalId).HasMaxLength(20);
                b.Property(x => x.PostalCode).HasMaxLength(20);
                b.Property(x => x.Address).HasMaxLength(500);
                b.Property(x => x.Phone).HasMaxLength(50);
                b.Property(x => x.EmployerName).HasMaxLength(300);
                b.HasIndex(x => x.EmployerName);
            });

            builder.Entity<FnReceipt>(b =>
            {
                b.ToTable("FnReceipts");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.PayerName).HasMaxLength(300);
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.Property(x => x.AllocatedAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnReceiptLine>(b =>
            {
                b.ToTable("FnReceiptLines");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Reference).HasMaxLength(100);
                b.Property(x => x.ChequeSayadId).HasMaxLength(20);
                b.Property(x => x.ChequeSerial).HasMaxLength(50);
                b.Property(x => x.ChequeBank).HasMaxLength(100);
                b.Property(x => x.ChequeBranch).HasMaxLength(100);
                b.Property(x => x.ChequeHolder).HasMaxLength(200);
                b.HasOne(x => x.Receipt).WithMany(x => x.Lines).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.ReceivedCheque).WithMany().HasForeignKey(x => x.ReceivedChequeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnReceiptAllocation>(b =>
            {
                b.ToTable("FnReceiptAllocations");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => x.InvoiceId);
                b.HasOne(x => x.Receipt).WithMany(x => x.Allocations).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnPayment>(b =>
            {
                b.ToTable("FnPayments");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.PayeeName).HasMaxLength(300);
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.GrossAmount).HasPrecision(18, 0);
                b.Property(x => x.DeductionAmount).HasPrecision(18, 0);
                b.Property(x => x.NetAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
            });
            builder.Entity<FnPaymentLine>(b =>
            {
                b.ToTable("FnPaymentLines");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Reference).HasMaxLength(100);
                b.Property(x => x.PayeeIban).HasMaxLength(34);
                b.Property(x => x.ChequeSayadId).HasMaxLength(16);
                b.HasOne(x => x.Payment).WithMany(x => x.Lines).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnPaymentRequestLink>(b =>
            {
                b.ToTable("FnPaymentRequestLinks");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Deductions).HasPrecision(18, 0);
                b.HasIndex(x => x.PaymentRequestId);
                b.HasOne(x => x.Payment).WithMany(x => x.Requests).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.PaymentRequest).WithMany().HasForeignKey(x => x.PaymentRequestId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FnReceivedCheque>(b =>
            {
                b.ToTable("FnReceivedCheques");
                b.Property(x => x.SayadId).HasMaxLength(16);
                b.Property(x => x.Serial).HasMaxLength(50);
                b.Property(x => x.BankName).HasMaxLength(100);
                b.Property(x => x.Branch).HasMaxLength(100);
                b.Property(x => x.AccountHolder).HasMaxLength(200);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => x.SayadId).IsUnique().HasFilter("[SayadId] IS NOT NULL");
                b.HasIndex(x => new { x.Status, x.DueDate });
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnReceivedChequeHistory>(b =>
            {
                b.ToTable("FnReceivedChequeHistories");
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasOne(x => x.Cheque).WithMany(x => x.History).HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.AccountingEvent).WithMany().HasForeignKey(x => x.AccountingEventId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnChequeBook>(b =>
            {
                b.ToTable("FnChequeBooks");
                b.Property(x => x.Series).HasMaxLength(50);
                b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnIssuedCheque>(b =>
            {
                b.ToTable("FnIssuedCheques");
                b.Property(x => x.SayadId).HasMaxLength(16);
                b.Property(x => x.PayeeName).HasMaxLength(300);
                b.Property(x => x.VoidReason).HasMaxLength(500);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => new { x.ChequeBookId, x.Serial }).IsUnique();
                b.HasIndex(x => new { x.Status, x.DueDate });
                b.HasOne(x => x.ChequeBook).WithMany(x => x.Leaves).HasForeignKey(x => x.ChequeBookId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnGuarantee>(b =>
            {
                b.ToTable("FnGuarantees");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.PartyName).HasMaxLength(300);
                b.Property(x => x.Reference).HasMaxLength(200);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
            });

            builder.Entity<FnPettyCashSettlement>(b =>
            {
                b.ToTable("FnPettyCashSettlements");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasOne(x => x.PettyCash).WithMany().HasForeignKey(x => x.PettyCashId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPettyCashSettlementLine>(b =>
            {
                b.ToTable("FnPettyCashSettlementLines");
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.SupplierName).HasMaxLength(300);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.InventoryDocumentId).IsUnique().HasFilter("[InventoryDocumentId] IS NOT NULL");
                b.HasOne(x => x.Settlement).WithMany(x => x.Lines).HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FnBankStatement>(b =>
            {
                b.ToTable("FnBankStatements");
                b.Property(x => x.OpeningBalance).HasPrecision(18, 0);
                b.Property(x => x.ClosingBalance).HasPrecision(18, 0);
                b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBankStatementLine>(b =>
            {
                b.ToTable("FnBankStatementLines");
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.Reference).HasMaxLength(100);
                b.Property(x => x.Withdrawal).HasPrecision(18, 0);
                b.Property(x => x.Deposit).HasPrecision(18, 0);
                b.Property(x => x.Balance).HasPrecision(18, 0);
                b.HasOne(x => x.Statement).WithMany(x => x.Lines).HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.AccountingEvent).WithMany().HasForeignKey(x => x.AccountingEventId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnBankReconciliation>(b =>
            {
                b.ToTable("FnBankReconciliations");
                b.Property(x => x.BookBalance).HasPrecision(18, 0);
                b.Property(x => x.BankBalance).HasPrecision(18, 0);
                b.Property(x => x.DepositsInTransit).HasPrecision(18, 0);
                b.Property(x => x.OutstandingPayments).HasPrecision(18, 0);
                b.Property(x => x.OutstandingCheques).HasPrecision(18, 0);
                b.Property(x => x.Difference).HasPrecision(18, 0);
                b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ receivables
            builder.Entity<FnSalesInvoice>(b =>
            {
                b.ToTable("FnSalesInvoices");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.TaxId).HasMaxLength(50);
                b.Property(x => x.NetAmount).HasPrecision(18, 0);
                b.Property(x => x.DiscountAmount).HasPrecision(18, 0);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.Property(x => x.GrossAmount).HasPrecision(18, 0);
                b.Property(x => x.AdvanceRecovery).HasPrecision(18, 0);
                b.Property(x => x.PerformanceRetentionPercent).HasPrecision(18, 6);
                b.Property(x => x.PerformanceRetention).HasPrecision(18, 0);
                b.Property(x => x.InsuranceRetentionPercent).HasPrecision(18, 6);
                b.Property(x => x.InsuranceRetention).HasPrecision(18, 0);
                b.Property(x => x.WithholdingPercent).HasPrecision(18, 6);
                b.Property(x => x.Withholding).HasPrecision(18, 0);
                b.Property(x => x.ReceivableAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.MilestoneId).IsUnique().HasFilter("[MilestoneId] IS NOT NULL AND [Status] <> 3");
                b.HasIndex(x => new { x.CustomerId, x.Status });
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnSalesInvoiceLine>(b =>
            {
                b.ToTable("FnSalesInvoiceLines");
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.ItemCode).HasMaxLength(50);
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(18, 4);
                b.Property(x => x.Discount).HasPrecision(18, 0);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.VatRatePercent).HasPrecision(18, 6);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.Property(x => x.Total).HasPrecision(18, 0);
                b.HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ changes to existing finance tables
            builder.Entity<AccountingEventLine>(b =>
            {
                b.Property(x => x.PartyType).HasMaxLength(30);
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.CurrencyAmount).HasPrecision(18, 4);
                b.Property(x => x.Rate).HasPrecision(18, 6);
            });
            builder.Entity<AccountMapping>(b =>
            {
                b.HasOne<FnAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierProfile>(b =>
            {
                b.Property(x => x.Iban).HasMaxLength(34);
            });
        }
    }
}
