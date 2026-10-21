using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // ============================================================================================ fiscal years and chart

    public class PeriodDto
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FnPeriodStatus Status { get; set; }
        public int Vouchers { get; set; }
        public int NonFinalVouchers { get; set; }
    }

    public class FiscalYearDto
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public string Title { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FnYearStatus Status { get; set; }
        public int? OpeningVoucherId { get; set; }
        public int? ClosingVoucherId { get; set; }
        public int LegalEntityId { get; set; }
        public bool IsImported { get; set; }
        public List<PeriodDto> Periods { get; set; } = new List<PeriodDto>();
    }

    public class EnsureYearDto
    {
        public DateTime Date { get; set; }
        /// <summary>Default: the user's default legal entity.</summary>
        public int? LegalEntityId { get; set; }
    }

    public class PeriodActionDto
    {
        public int Id { get; set; }
        /// <summary>lock, reopen, close</summary>
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class CloseYearDto
    {
        public bool OverrideReconciliation { get; set; }
        public string Reason { get; set; }
    }

    public class AccountQuery : PaginationParams
    {
        public FnAccountLevel? Level { get; set; }
        public FnAccountType? Type { get; set; }
        public string Contains { get; set; }
        public bool IncludeInactive { get; set; }
    }

    public class AccountDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnAccountLevel Level { get; set; }
        public int? ParentId { get; set; }
        public string ParentCode { get; set; }
        public FnAccountNature Nature { get; set; }
        public FnAccountType Type { get; set; }
        public bool Temporary { get; set; }
        public string DetailTypes { get; set; }
        public string Detail2Types { get; set; }
        public bool RequiresDetail { get; set; }
        public bool CurrencyAllowed { get; set; }
        public string StatementLine { get; set; }
        public bool IsActive { get; set; }
        public string SepidarCode { get; set; }
        public bool HasPostings { get; set; }
    }

    public class SaveAccountDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnAccountNature Nature { get; set; } = FnAccountNature.Either;
        public FnAccountType? Type { get; set; }
        public bool Temporary { get; set; }
        public string DetailTypes { get; set; }
        public string Detail2Types { get; set; }
        public bool RequiresDetail { get; set; }
        public bool CurrencyAllowed { get; set; }
        public string StatementLine { get; set; }
        public bool IsActive { get; set; } = true;
        public string SepidarCode { get; set; }
    }

    public class DetailAccountQuery : PaginationParams
    {
        public FnDetailType? Type { get; set; }
        public string Contains { get; set; }
    }

    public class DetailAccountDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnDetailType Type { get; set; }
        public int? LinkedEntityId { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveDetailAccountDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnDetailType Type { get; set; } = FnDetailType.Person;
        public bool IsActive { get; set; } = true;
    }

    // ============================================================================================ vouchers

    public class VoucherLineDto
    {
        public int RowNumber { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; }
        public string AccountTitle { get; set; }
        public int? Detail1Id { get; set; }
        public string Detail1Code { get; set; }
        public string Detail1Title { get; set; }
        public int? Detail2Id { get; set; }
        public string Detail2Code { get; set; }
        public string Detail2Title { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? CurrencyAmount { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Quantity { get; set; }
        public string Description { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? CostCenterId { get; set; }
        public string SourceRole { get; set; }
    }

    public class VoucherDto
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public string FiscalYearTitle { get; set; }
        public int? Number { get; set; }
        public int TemporaryNumber { get; set; }
        public DateTime Date { get; set; }
        public FnVoucherType Type { get; set; }
        public FnVoucherStatus Status { get; set; }
        public string Description { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public string SourceNumber { get; set; }
        public int? AccountingEventId { get; set; }
        public int? ReversesVoucherId { get; set; }
        public int? ReversedByVoucherId { get; set; }
        public bool LatePosting { get; set; }
        public decimal TotalAmount { get; set; }
        public int LegalEntityId { get; set; }
        public bool IsImported { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public int AttachmentCount { get; set; }
        public int CreatedByUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CheckedByName { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public string FinalizedByName { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }
        public List<VoucherLineDto> Lines { get; set; } = new List<VoucherLineDto>();
    }

    public class SaveVoucherLineDto
    {
        public int? AccountId { get; set; }
        public string AccountCode { get; set; }
        public int? Detail1Id { get; set; }
        public string Detail1Code { get; set; }
        public int? Detail2Id { get; set; }
        public string Detail2Code { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? CurrencyAmount { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Quantity { get; set; }
        public string Description { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? CostCenterId { get; set; }
    }

    public class SaveVoucherDto
    {
        public int? Id { get; set; }
        /// <summary>Default: the user's default legal entity (an existing voucher keeps its entity).</summary>
        public int? LegalEntityId { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public List<SaveVoucherLineDto> Lines { get; set; } = new List<SaveVoucherLineDto>();
    }

    public class VoucherQuery : PaginationParams
    {
        public int? FiscalYearId { get; set; }
        /// <summary>Default: the user's default legal entity (ignored when a fiscal year is given).</summary>
        public int? LegalEntityId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public FnVoucherStatus? Status { get; set; }
        public FnVoucherType? Type { get; set; }
        public string Contains { get; set; }
        public int? AccountId { get; set; }
        public int? Detail1Id { get; set; }
        public bool WithLines { get; set; }
    }

    public class VoucherActionDto
    {
        public int Id { get; set; }
        /// <summary>check, uncheck, finalize, reverse</summary>
        public string Action { get; set; }
        /// <summary>Date of the reversal voucher (default: the original date, or today when that period is closed).</summary>
        public DateTime? Date { get; set; }
        public string Note { get; set; }
    }

    public class FinalizeUpToDto
    {
        public int? FiscalYearId { get; set; }
        public int? LegalEntityId { get; set; }
        public DateTime UpTo { get; set; }
    }

    public class FinalizeResultDto
    {
        public int Finalized { get; set; }
        public int? FirstNumber { get; set; }
        public int? LastNumber { get; set; }
        public List<string> Skipped { get; set; } = new List<string>();
    }

    public class PostEventsResultDto
    {
        public int Posted { get; set; }
        public int LatePosted { get; set; }
        public int NotRequired { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class OpeningRowDto
    {
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class OpeningImportDto
    {
        public int? FiscalYearId { get; set; }
        public string Description { get; set; }
        public List<OpeningRowDto> Rows { get; set; } = new List<OpeningRowDto>();
    }

    // ============================================================================================ reports

    public class LedgerReportQuery : PaginationParams
    {
        public int? FiscalYearId { get; set; }
        /// <summary>Default: the user's default legal entity (ignored when a fiscal year is given).</summary>
        public int? LegalEntityId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        /// <summary>Include Draft and Checked vouchers (default: final vouchers only).</summary>
        public bool IncludeDraft { get; set; }
        /// <summary>1 = General, 2 = Subsidiary, 3 = Detail (trial balance).</summary>
        public int Level { get; set; } = 1;
        /// <summary>2, 4 or 6 columns (trial balance).</summary>
        public int Columns { get; set; } = 4;
        public string AccountCode { get; set; }
        public int? AccountId { get; set; }
        public int? DetailId { get; set; }
        public bool ExcludeClosing { get; set; }
        public string Format { get; set; }
    }

    public class JournalAccountDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class JournalVoucherDto
    {
        public int? Number { get; set; }
        public int VoucherId { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class JournalDayDto
    {
        public DateTime Date { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public List<JournalAccountDto> Accounts { get; set; } = new List<JournalAccountDto>();
        public List<JournalVoucherDto> Vouchers { get; set; } = new List<JournalVoucherDto>();
    }

    public class LedgerRowDto
    {
        public DateTime Date { get; set; }
        public int VoucherId { get; set; }
        public int? VoucherNumber { get; set; }
        public string AccountCode { get; set; }
        public string DetailTitle { get; set; }
        public string Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    public class LedgerStatementDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Opening { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Closing { get; set; }
        public int TotalRows { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public List<LedgerRowDto> Rows { get; set; } = new List<LedgerRowDto>();
    }

    public class TrialBalanceRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public int Level { get; set; }
        public FnAccountType? Type { get; set; }
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
    }

    public class TrialBalanceDto
    {
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool IncludesDraft { get; set; }
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
        public bool Balanced { get; set; }
        public List<TrialBalanceRowDto> Rows { get; set; } = new List<TrialBalanceRowDto>();
    }

    public class StatementLineDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Section { get; set; }
        public decimal Amount { get; set; }
        public bool IsTotal { get; set; }
    }

    public class FinancialStatementDto
    {
        public string Kind { get; set; }
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<StatementLineDto> Lines { get; set; } = new List<StatementLineDto>();
        // balance sheet
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }
        public bool Balanced { get; set; }
        // income statement
        public decimal GrossProfit { get; set; }
        public decimal OperatingProfit { get; set; }
        public decimal ProfitBeforeTax { get; set; }
        public decimal NetProfit { get; set; }
    }

    public class ProjectProfitabilityRowDto
    {
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string ContractTitle { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Margin { get; set; }
        public decimal? MarginPercent { get; set; }
        /// <summary>Material, labour and service cost in the cost sub-ledger (CostEntry).</summary>
        public decimal CostEntries { get; set; }
        public decimal CostDifference { get; set; }
    }

    public class ReconRowDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Ledger { get; set; }
        public decimal SubLedger { get; set; }
        public decimal Difference { get; set; }
        public bool Ok { get; set; }
        public string Note { get; set; }
    }

    public class LedgerReconciliationDto
    {
        public bool Clean { get; set; }
        public int UnpostedEvents { get; set; }
        public int OldNonFinalVouchers { get; set; }
        public int OldVoucherDays { get; set; }
        public List<ReconRowDto> Rows { get; set; } = new List<ReconRowDto>();
    }

    public class SetupProblemDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }

    public class SetupCheckDto
    {
        public bool Ok { get; set; }
        public List<SetupProblemDto> Problems { get; set; } = new List<SetupProblemDto>();
        /// <summary>Seeded ratios that finance must confirm once (not problems).</summary>
        public List<string> ToVerify { get; set; } = new List<string>();
        /// <summary>Release F2: what the optional modules (assets, payroll) still need; these do not count as problems of the ledger.</summary>
        public List<SetupModuleDto> Modules { get; set; } = new List<SetupModuleDto>();
    }

    // ============================================================================================ tax

    public class TaxRateDto
    {
        public int Id { get; set; }
        public FnTaxKind Kind { get; set; }
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public string Note { get; set; }
    }

    public class SaveTaxRateDto
    {
        public int? Id { get; set; }
        public FnTaxKind Kind { get; set; }
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public string Note { get; set; }
    }

    public class VatSeasonRowDto
    {
        public string Side { get; set; }
        public string PartyName { get; set; }
        public string EconomicCode { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public int Documents { get; set; }
    }

    public class VatSeasonDto
    {
        public int Year { get; set; }
        public int Season { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal SalesNet { get; set; }
        public decimal SalesVat { get; set; }
        public decimal PurchasesNet { get; set; }
        public decimal PurchasesVat { get; set; }
        public decimal VatPayable { get; set; }
        public List<VatSeasonRowDto> Rows { get; set; } = new List<VatSeasonRowDto>();
    }

    public class TaxpayerSaleDto
    {
        public int InvoiceId { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public string CustomerName { get; set; }
        public string EconomicCode { get; set; }
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossAmount { get; set; }
        public string TaxId { get; set; }
        public int TaxSentStatus { get; set; }
        public FnInvoiceType Type { get; set; }
    }

    // ============================================================================================ treasury

    public class BankAccountDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string BankName { get; set; }
        public string Branch { get; set; }
        public string AccountNumber { get; set; }
        public string Iban { get; set; }
        public string CurrencyCode { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; }
        public int? DetailAccountId { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Balance from the accounting events of this account.</summary>
        public decimal Balance { get; set; }
    }

    public class SaveBankAccountDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string BankName { get; set; }
        public string Branch { get; set; }
        public string AccountNumber { get; set; }
        public string Iban { get; set; }
        public string CurrencyCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CashBoxDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string CustodianTitle { get; set; }
        public bool IsActive { get; set; }
        public decimal Balance { get; set; }
    }

    public class SaveCashBoxDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string CustodianTitle { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PettyCashDto
    {
        public int Id { get; set; }
        public int HolderUserId { get; set; }
        public string HolderName { get; set; }
        public string HolderTitle { get; set; }
        public decimal Ceiling { get; set; }
        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
    }

    public class SavePettyCashDto
    {
        public int? Id { get; set; }
        public int HolderUserId { get; set; }
        public string HolderTitle { get; set; }
        public decimal Ceiling { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class TreasuryQuery : PaginationParams
    {
        public FnDocStatus? Status { get; set; }
        public int? Kind { get; set; }
        public int? BankAccountId { get; set; }
        public int? CustomerId { get; set; }
        public int? ProviderId { get; set; }
        public int? PettyCashId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Contains { get; set; }
    }

    public class PaymentLineDto
    {
        public int RowNumber { get; set; }
        public FnInstrument Instrument { get; set; }
        public decimal Amount { get; set; }
        public int? BankAccountId { get; set; }
        public int? CashBoxId { get; set; }
        public string Reference { get; set; }
        public string PayeeIban { get; set; }
        public int? IssuedChequeId { get; set; }
        public long? ChequeSerial { get; set; }
        public DateTime? ChequeDueDate { get; set; }
        public string ChequeSayadId { get; set; }
    }

    public class PaymentRequestLinkDto
    {
        public int PaymentRequestId { get; set; }
        public string PaymentRequestNumber { get; set; }
        public decimal Amount { get; set; }
        public decimal Deductions { get; set; }
    }

    public class PaymentDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public FnPaymentPurpose Purpose { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? PettyCashId { get; set; }
        public int? PayrollRunId { get; set; }
        public string PayeeName { get; set; }
        public string Description { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal DeductionAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string CreatorName { get; set; }
        public string PostedByName { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public int? IbanApprovedByUserId { get; set; }
        public List<PaymentLineDto> Lines { get; set; } = new List<PaymentLineDto>();
        public List<PaymentRequestLinkDto> Requests { get; set; } = new List<PaymentRequestLinkDto>();
    }

    public class SavePaymentDto
    {
        public int? Id { get; set; }
        public DateTime Date { get; set; }
        public FnPaymentPurpose Purpose { get; set; } = FnPaymentPurpose.Supplier;
        /// <summary>Approved payment requests (of one supplier) paid by this payment.</summary>
        public List<int> PaymentRequestIds { get; set; } = new List<int>();
        public int? PettyCashId { get; set; }
        /// <summary>Payroll purpose: the Posted payroll run that is paid (the amount is the run's net total).</summary>
        public int? PayrollRunId { get; set; }
        /// <summary>Amount of a petty-cash top-up.</summary>
        public decimal? TopUpAmount { get; set; }
        public string Description { get; set; }
        public List<PaymentLineDto> Lines { get; set; } = new List<PaymentLineDto>();
    }

    /// <summary>An approved supplier payment request waiting for treasury.</summary>
    public class PayableRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int Type { get; set; }
        public DateTime RequestDate { get; set; }
        public string InvoiceNumber { get; set; }
        public decimal Amount { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetAmount { get; set; }
        public string SupplierIban { get; set; }
        public int? PaymentId { get; set; }
    }

    public class DocActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public DateTime? Date { get; set; }
        public int? BankAccountId { get; set; }
        public int? ProviderId { get; set; }
        public string Reference { get; set; }
    }

    public class ReceiptLineDto
    {
        public int RowNumber { get; set; }
        public FnInstrument Instrument { get; set; }
        public decimal Amount { get; set; }
        public int? BankAccountId { get; set; }
        public int? CashBoxId { get; set; }
        public string Reference { get; set; }
        public int? ReceivedChequeId { get; set; }
        public string ChequeSayadId { get; set; }
        public string ChequeSerial { get; set; }
        public string ChequeBank { get; set; }
        public string ChequeBranch { get; set; }
        public string ChequeHolder { get; set; }
        public DateTime? ChequeDueDate { get; set; }
    }

    public class ReceiptAllocationDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public decimal Amount { get; set; }
    }

    public class ReceiptDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string PayerName { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AllocatedAmount { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public List<ReceiptLineDto> Lines { get; set; } = new List<ReceiptLineDto>();
        public List<ReceiptAllocationDto> Allocations { get; set; } = new List<ReceiptAllocationDto>();
    }

    public class SaveReceiptDto
    {
        public int? Id { get; set; }
        public DateTime Date { get; set; }
        public int? CustomerId { get; set; }
        public string PayerName { get; set; }
        public string Description { get; set; }
        public List<ReceiptLineDto> Lines { get; set; } = new List<ReceiptLineDto>();
        /// <summary>Manual allocation to invoices; when empty the oldest open invoices of the customer are settled first (FIFO).</summary>
        public List<ReceiptAllocationDto> Allocations { get; set; } = new List<ReceiptAllocationDto>();
        public bool AllocateFifo { get; set; } = true;
    }

    public class ChequeHistoryDto
    {
        public DateTime Date { get; set; }
        public FnReceivedChequeStatus? FromStatus { get; set; }
        public FnReceivedChequeStatus ToStatus { get; set; }
        public string Note { get; set; }
        public int? AccountingEventId { get; set; }
        public int? VoucherId { get; set; }
        public int? VoucherNumber { get; set; }
    }

    public class ReceivedChequeDto
    {
        public int Id { get; set; }
        public string SayadId { get; set; }
        public string Serial { get; set; }
        public string BankName { get; set; }
        public string Branch { get; set; }
        public string AccountHolder { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public FnReceivedChequeStatus Status { get; set; }
        public int? DepositBankAccountId { get; set; }
        public DateTime ReceivedDate { get; set; }
        public List<ChequeHistoryDto> History { get; set; } = new List<ChequeHistoryDto>();
    }

    public class ChequeBookDto
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public string BankAccountTitle { get; set; }
        public string Series { get; set; }
        public long FromSerial { get; set; }
        public long ToSerial { get; set; }
        public bool IsActive { get; set; }
        public int Leaves { get; set; }
        public int Blank { get; set; }
    }

    public class SaveChequeBookDto
    {
        public int BankAccountId { get; set; }
        public string Series { get; set; }
        public long FromSerial { get; set; }
        public long ToSerial { get; set; }
    }

    public class IssuedChequeQuery : PaginationParams
    {
        public int? ChequeBookId { get; set; }
        public FnIssuedChequeStatus? Status { get; set; }
        public int? BankAccountId { get; set; }
    }

    public class IssuedChequeDto
    {
        public int Id { get; set; }
        public int ChequeBookId { get; set; }
        public int BankAccountId { get; set; }
        public string BankAccountTitle { get; set; }
        public string Series { get; set; }
        public long Serial { get; set; }
        public string SayadId { get; set; }
        public FnIssuedChequeStatus Status { get; set; }
        public string PayeeName { get; set; }
        public int? ProviderId { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal Amount { get; set; }
        public int? PaymentId { get; set; }
        public DateTimeOffset? IssuedAt { get; set; }
        public DateTimeOffset? ClearedAt { get; set; }
        public string VoidReason { get; set; }
    }

    public class GuaranteeDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Kind { get; set; }
        public int Direction { get; set; }
        public string PartyName { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public string Reference { get; set; }
        public FnGuaranteeStatus Status { get; set; }
        public DateTime? ClosedDate { get; set; }
    }

    public class SaveGuaranteeDto
    {
        public int Kind { get; set; } = 1;
        public int Direction { get; set; } = 1;
        public string PartyName { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public string Reference { get; set; }
    }

    public class SettlementLineDto
    {
        public int RowNumber { get; set; }
        public string Description { get; set; }
        public int? AccountId { get; set; }
        public string AccountCode { get; set; }
        public int? CostCenterId { get; set; }
        public int? WorkOrderId { get; set; }
        public decimal Amount { get; set; }
        public decimal VatAmount { get; set; }
        public string SupplierName { get; set; }
        public Guid? DocumentId { get; set; }
        public int? InventoryDocumentId { get; set; }
    }

    public class SettlementDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public string HolderName { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal VatAmount { get; set; }
        public string CreatorName { get; set; }
        public string ApprovedByName { get; set; }
        public List<SettlementLineDto> Lines { get; set; } = new List<SettlementLineDto>();
    }

    public class SaveSettlementDto
    {
        public int? Id { get; set; }
        public int PettyCashId { get; set; }
        public DateTime Date { get; set; }
        public List<SettlementLineDto> Lines { get; set; } = new List<SettlementLineDto>();
    }

    public class PettyCandidateDto
    {
        public int InventoryDocumentId { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Reference { get; set; }
    }

    public class StatementRowDto
    {
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public decimal Withdrawal { get; set; }
        public decimal Deposit { get; set; }
        public string Reference { get; set; }
        public decimal? Balance { get; set; }
    }

    public class ImportStatementDto
    {
        public int BankAccountId { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<StatementRowDto> Rows { get; set; } = new List<StatementRowDto>();
    }

    public class StatementLineViewDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public decimal Withdrawal { get; set; }
        public decimal Deposit { get; set; }
        public string Reference { get; set; }
        public decimal? Balance { get; set; }
        public FnStatementMatch MatchKind { get; set; }
        public int? MatchedId { get; set; }
    }

    public class BankStatementDto
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public string BankAccountTitle { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public int Matched { get; set; }
        public int Unmatched { get; set; }
        public List<StatementLineViewDto> Lines { get; set; } = new List<StatementLineViewDto>();
    }

    public class StatementMatchDto
    {
        public int StatementId { get; set; }
        public int LineId { get; set; }
        /// <summary>cheque, payment, receipt, fee, interest</summary>
        public string Kind { get; set; }
        public int? TargetId { get; set; }
    }

    public class ReconciliationDto
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public string BankAccountTitle { get; set; }
        public int StatementId { get; set; }
        public DateTime AsOfDate { get; set; }
        public decimal BookBalance { get; set; }
        public decimal BankBalance { get; set; }
        public decimal DepositsInTransit { get; set; }
        public decimal OutstandingPayments { get; set; }
        public decimal OutstandingCheques { get; set; }
        public decimal Difference { get; set; }
        public int Status { get; set; }
        public string PreparedByName { get; set; }
        public string ApprovedByName { get; set; }
        public int UnmatchedLines { get; set; }
    }

    public class CreateReconciliationDto
    {
        public int StatementId { get; set; }
        public DateTime? AsOfDate { get; set; }
    }

    public class CashPositionAccountDto
    {
        public string Kind { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public decimal Balance { get; set; }
    }

    public class CashWeekDto
    {
        public int Week { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal Inflows { get; set; }
        public decimal Outflows { get; set; }
        public decimal Net { get; set; }
        public decimal Cumulative { get; set; }
    }

    public class CashPositionDto
    {
        public decimal Cash { get; set; }
        public decimal Banks { get; set; }
        public decimal PettyCash { get; set; }
        public decimal Total { get; set; }
        public List<CashPositionAccountDto> Accounts { get; set; } = new List<CashPositionAccountDto>();
        public decimal ReceivedChequesInBox { get; set; }
        public decimal ReceivedChequesDeposited { get; set; }
        public decimal IssuedChequesOutstanding { get; set; }
        public decimal ApprovedPaymentRequests { get; set; }
        public List<CashWeekDto> Forecast { get; set; } = new List<CashWeekDto>();
        public List<string> Alerts { get; set; } = new List<string>();
    }

    // ============================================================================================ receivables

    public class CustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string EconomicCode { get; set; }
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string EmployerName { get; set; }
        public bool IsActive { get; set; }
        public decimal OpenAmount { get; set; }
    }

    public class SaveCustomerDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string EconomicCode { get; set; }
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string EmployerName { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class InvoiceLineDto
    {
        public int RowNumber { get; set; }
        public string Description { get; set; }
        public string ItemCode { get; set; }
        public decimal Quantity { get; set; } = 1;
        public string Unit { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public bool VatExempt { get; set; }
        public decimal Amount { get; set; }
        public decimal VatRatePercent { get; set; }
        public decimal VatAmount { get; set; }
        public decimal Total { get; set; }
    }

    public class SalesInvoiceDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContractId { get; set; }
        public int? MilestoneId { get; set; }
        public FnInvoiceType Type { get; set; }
        public FnDocStatus Status { get; set; }
        public int? CreditedInvoiceId { get; set; }
        public string Description { get; set; }
        public decimal NetAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal AdvanceRecovery { get; set; }
        public decimal PerformanceRetentionPercent { get; set; }
        public decimal PerformanceRetention { get; set; }
        public decimal InsuranceRetentionPercent { get; set; }
        public decimal InsuranceRetention { get; set; }
        public decimal WithholdingPercent { get; set; }
        public decimal Withholding { get; set; }
        public decimal ReceivableAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OpenAmount { get; set; }
        public string TaxId { get; set; }
        public string CreatorName { get; set; }
        public string ApprovedByName { get; set; }
        public List<InvoiceLineDto> Lines { get; set; } = new List<InvoiceLineDto>();
    }

    public class SaveInvoiceDto
    {
        public int? Id { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContractId { get; set; }
        public int? MilestoneId { get; set; }
        public FnInvoiceType Type { get; set; } = FnInvoiceType.Progress;
        public int? CreditedInvoiceId { get; set; }
        public string Description { get; set; }
        public decimal AdvanceRecovery { get; set; }
        public decimal? PerformanceRetentionPercent { get; set; }
        public decimal? InsuranceRetentionPercent { get; set; }
        public decimal? WithholdingPercent { get; set; }
        /// <summary>Take the three percentages from the rate table when they are not given.</summary>
        public bool UseDefaultDeductionRates { get; set; }
        public List<InvoiceLineDto> Lines { get; set; } = new List<InvoiceLineDto>();
    }

    public class InvoiceQuery : PaginationParams
    {
        public FnDocStatus? Status { get; set; }
        public int? CustomerId { get; set; }
        public int? ContractId { get; set; }
        public FnInvoiceType? Type { get; set; }
        public bool OpenOnly { get; set; }
        public string Contains { get; set; }
    }

    public class DraftFromMilestonesResultDto
    {
        public int Drafted { get; set; }
        public List<string> Skipped { get; set; } = new List<string>();
    }

    public class AgeingRowDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }
        public decimal Total { get; set; }
    }

    public class AgeingDto
    {
        public DateTime AsOf { get; set; }
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }
        public decimal Total { get; set; }
        public List<AgeingRowDto> Rows { get; set; } = new List<AgeingRowDto>();
    }

    public class CustomerStatementRowDto
    {
        public DateTime Date { get; set; }
        public string Kind { get; set; }
        public string Number { get; set; }
        public string Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    public class CustomerStatementDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal Balance { get; set; }
        public List<CustomerStatementRowDto> Rows { get; set; } = new List<CustomerStatementRowDto>();
    }

    // ============================================================================================ dashboard

    public class GrossMarginRowDto
    {
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal MarginPercent { get; set; }
    }

    public class FinanceDashboardDto
    {
        public DateTimeOffset GeneratedAt { get; set; }
        public decimal CashTotal { get; set; }
        public decimal BankTotal { get; set; }
        public decimal PettyCashTotal { get; set; }
        public int ReceivedChequesDue3Days { get; set; }
        public decimal ReceivedChequesDue3DaysAmount { get; set; }
        public int IssuedChequesDue7Days { get; set; }
        public decimal IssuedChequesDue7DaysAmount { get; set; }
        public int VouchersWaitingCheck { get; set; }
        public int VouchersWaitingFinalize { get; set; }
        public int UnpostedEvents { get; set; }
        public int OpenPeriods { get; set; }
        public int UnapprovedReconciliations { get; set; }
        public decimal ReconciliationDifference { get; set; }
        public decimal ReceivablesOpen { get; set; }
        public decimal ReceivablesOverdue { get; set; }
        public decimal PayablesOpen { get; set; }
        public decimal PayablesDue30 { get; set; }
        public decimal VatPayableEstimate { get; set; }
        public decimal NetProfitYtd { get; set; }
        public int SetupProblems { get; set; }
        public List<string> Alerts { get; set; } = new List<string>();
    }

    /// <summary>Figures added to the CEO dashboard by the finance module (all read from the ledger and the treasury records).</summary>
    public class CeoFinanceDto
    {
        public decimal CashAndBank { get; set; }
        public decimal ReceivablesOpen { get; set; }
        public decimal ReceivablesOverdue { get; set; }
        public decimal PayablesDue30 { get; set; }
        public decimal IssuedChequesDue7Amount { get; set; }
        public int IssuedChequesDue7Count { get; set; }
        public decimal NetProfitYtd { get; set; }
        public int FinanceSetupProblems { get; set; }
        // release F2: Sepidar outbox (tax and insurance packages), fixed assets, payroll progress (counts only, never salary figures)
        public int SepidarPendingPackages { get; set; }
        public int SepidarFailedPackages { get; set; }
        public DateTimeOffset? SepidarLastSentAt { get; set; }
        public int AssetsCount { get; set; }
        public decimal AssetsNetBookValue { get; set; }
        public int DepreciationRunsWaiting { get; set; }
        public int PayrollRunsInProgress { get; set; }
        public List<GrossMarginRowDto> GrossMargins { get; set; } = new List<GrossMarginRowDto>();
        public List<DashboardAlertDto> Alerts { get; set; } = new List<DashboardAlertDto>();
    }
}
