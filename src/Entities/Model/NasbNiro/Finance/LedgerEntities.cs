using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Finance
{
    // General ledger, treasury, receivables and tax of PMIS (release F1). Tables start with "Fn".
    // The ledger is append-only: a final voucher is never edited; a reversal voucher corrects it.
    // Everything country specific (chart, tax codes, calendars) is data keyed by Jurisdiction ("IR" now, "DE" later).

    /// <summary>Company-wide finance settings (base currency, jurisdiction, ...).</summary>
    public class FnSetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public string Title { get; set; }
    }

    // ------------------------------------------------------------------------------------------ fiscal years

    public enum FnYearStatus { Open = 0, Closing = 1, Closed = 2 }
    public enum FnPeriodStatus { Open = 0, Locked = 1, Closed = 2 }

    public class FnFiscalYear
    {
        public int Id { get; set; }
        /// <summary>Persian year number, for example 1405.</summary>
        public int Year { get; set; }
        public string Title { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FnYearStatus Status { get; set; }
        public int? OpeningVoucherId { get; set; }
        public FnVoucher OpeningVoucher { get; set; }
        public int? ClosingVoucherId { get; set; }
        public FnVoucher ClosingVoucher { get; set; }
        public string Jurisdiction { get; set; }
        /// <summary>The legal entity of the year (F3); existing years belong to the main company (entity 1). No foreign key: the column defaults to 1.</summary>
        public int LegalEntityId { get; set; }
        /// <summary>A historical year imported from Sepidar: read-only, never mixed with PMIS vouchers.</summary>
        public bool IsImported { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public ICollection<FnPeriod> Periods { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnPeriod
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public FnFiscalYear FiscalYear { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FnPeriodStatus Status { get; set; }
        public DateTimeOffset? LockedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnPeriodLog
    {
        public int Id { get; set; }
        public int PeriodId { get; set; }
        public FnPeriod Period { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
    }

    // ------------------------------------------------------------------------------------------ chart of accounts

    public enum FnAccountLevel { Group = 1, General = 2, Subsidiary = 3 }
    public enum FnAccountNature { Debit = 1, Credit = 2, Either = 3 }
    public enum FnAccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5, Memorandum = 6 }

    public enum FnDetailType
    {
        Person = 1, Supplier = 2, Customer = 3, Employee = 4, Bank = 5, CashBox = 6, PettyCash = 7,
        Project = 8, WorkOrder = 9, CostCenter = 10, Asset = 11, Other = 99,
    }

    public class FnAccount
    {
        public int Id { get; set; }
        /// <summary>Group 1 digit, General 3 digits, Subsidiary 5 digits.</summary>
        public string Code { get; set; }
        public string Title { get; set; }
        public FnAccountLevel Level { get; set; }
        public int? ParentId { get; set; }
        public FnAccount Parent { get; set; }
        public FnAccountNature Nature { get; set; }
        public FnAccountType Type { get; set; }

        /// <summary>Closed at year end (revenue, expense, cost of sales, summary).</summary>
        public bool Temporary { get; set; }

        /// <summary>Detail types allowed as the first detail (comma separated FnDetailType names); empty = none.</summary>
        public string DetailTypes { get; set; }
        /// <summary>Detail types allowed as the second detail (project, work order, cost centre).</summary>
        public string Detail2Types { get; set; }
        public bool RequiresDetail { get; set; }
        public bool CurrencyAllowed { get; set; }
        /// <summary>Code of the financial-statement line this account rolls into.</summary>
        public string StatementLine { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Only for the tax and insurance exports.</summary>
        public string SepidarCode { get; set; }
        public string Jurisdiction { get; set; }
        /// <summary>Monetary item in foreign currency (cash/bank, receivable, payable, advance, loan): revalued at period end (F3).</summary>
        public bool IsMonetaryForeign { get; set; }
    }

    public class FnDetailAccount
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnDetailType Type { get; set; }
        /// <summary>ProviderId / CustomerId / ContractId / WorkOrderId / CostCenterId / EmployeeId / BankAccountId / AssetId.</summary>
        public int? LinkedEntityId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ------------------------------------------------------------------------------------------ vouchers

    public enum FnVoucherType { Opening = 1, Normal = 2, Auto = 3, Closing = 4, Reversal = 5, YearEndTemporaryClose = 6 }
    public enum FnVoucherStatus { Draft = 0, Checked = 1, Final = 2 }

    public class FnVoucher
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public int FiscalYearId { get; set; }
        public FnFiscalYear FiscalYear { get; set; }
        /// <summary>Final number: gapless inside the year, assigned at finalisation.</summary>
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
        public FnVoucher ReversesVoucher { get; set; }
        public bool LatePosting { get; set; }
        public decimal TotalAmount { get; set; }
        /// <summary>Imported from Sepidar (read-only history; excluded from the segregation checks).</summary>
        public bool IsImported { get; set; }
        /// <summary>CEO approval, required when the amount reaches an approval threshold.</summary>
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        /// <summary>Accrual-type manual voucher: the system drafts its reversal on day 1 of the next period (F3).</summary>
        public bool ReverseNextPeriod { get; set; }
        public int? AutoReversalVoucherId { get; set; }

        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? CheckedByUserId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public int? FinalizedByUserId { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }

        public ICollection<FnVoucherLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnVoucherLine
    {
        public int Id { get; set; }
        public int VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public int RowNumber { get; set; }
        public int AccountId { get; set; }
        public FnAccount Account { get; set; }
        public int? Detail1Id { get; set; }
        public FnDetailAccount Detail1 { get; set; }
        public int? Detail2Id { get; set; }
        public FnDetailAccount Detail2 { get; set; }
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
        /// <summary>The sub-ledger account role this line came from (auto vouchers); used for reconciliation.</summary>
        public string SourceRole { get; set; }
    }

    // ------------------------------------------------------------------------------------------ tax data

    public enum FnTaxKind { Vat = 1, WithholdingContract = 2, InsuranceRetention = 3, PerformanceRetention = 4, Other = 9 }

    public class FnTaxRate
    {
        public int Id { get; set; }
        public FnTaxKind Kind { get; set; }
        /// <summary>Percent.</summary>
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string Source { get; set; }
        /// <summary>Finance must confirm the value once.</summary>
        public bool Verify { get; set; }
        public string Note { get; set; }
        public string Jurisdiction { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public enum FnDeductionKind { Withholding = 1, InsuranceRetention = 2, PerformanceRetention = 3, LatePenalty = 4, AdvanceRecovery = 5, Other = 9 }

    /// <summary>A typed deduction of a supplier payment request (the old single Deductions number stays as the sum).</summary>
    public class FnPaymentDeduction
    {
        public int Id { get; set; }
        public int PaymentRequestId { get; set; }
        public Entities.Model.NasbNiro.Procurement.PaymentRequest PaymentRequest { get; set; }
        public FnDeductionKind Kind { get; set; }
        public decimal Base { get; set; }
        public decimal RatePercent { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ treasury masters

    public class FnBankAccount
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string BankName { get; set; }
        public string Branch { get; set; }
        public string AccountNumber { get; set; }
        public string Iban { get; set; }
        public string CurrencyCode { get; set; }
        /// <summary>10102 (rial) or 10103 (foreign currency).</summary>
        public int AccountId { get; set; }
        public FnAccount Account { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Legal entity of the account (F3 seam; defaults to the main company).</summary>
        public int LegalEntityId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public class FnCashBox
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string CustodianTitle { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class FnPettyCash
    {
        public int Id { get; set; }
        public int HolderUserId { get; set; }
        public string HolderTitle { get; set; }
        public decimal Ceiling { get; set; }
        /// <summary>Current balance (top-ups minus approved settlements).</summary>
        public decimal Balance { get; set; }
        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ receipts and payments

    public enum FnDocStatus { Draft = 0, Approved = 1, Posted = 2, Cancelled = 3 }
    public enum FnInstrument { Cash = 1, BankTransfer = 2, Cheque = 3, Card = 4, Offset = 5 }

    public class FnCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>کد اقتصادی</summary>
        public string EconomicCode { get; set; }
        /// <summary>شناسه ملی (11 digits) or national code (10 digits).</summary>
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        /// <summary>The text used in ContractEntity.Employer; links contracts to this customer.</summary>
        public string EmployerName { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public class FnReceipt
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public int? CustomerId { get; set; }
        public FnCustomer Customer { get; set; }
        public string PayerName { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AllocatedAmount { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? PostedByUserId { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public ICollection<FnReceiptLine> Lines { get; set; }
        public ICollection<FnReceiptAllocation> Allocations { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnReceiptLine
    {
        public int Id { get; set; }
        public int ReceiptId { get; set; }
        public FnReceipt Receipt { get; set; }
        public int RowNumber { get; set; }
        public FnInstrument Instrument { get; set; }
        public decimal Amount { get; set; }
        public int? BankAccountId { get; set; }
        public int? CashBoxId { get; set; }
        /// <summary>Transfer tracking number.</summary>
        public string Reference { get; set; }
        public int? ReceivedChequeId { get; set; }
        public FnReceivedCheque ReceivedCheque { get; set; }
        public string ChequeSayadId { get; set; }
        public string ChequeSerial { get; set; }
        /// <summary>Line on a foreign-currency bank account: the amount in that currency and the rate to rials at the receipt date (Amount = round(CurrencyAmount x Rate)).</summary>
        public decimal? CurrencyAmount { get; set; }
        public decimal? Rate { get; set; }
        public string ChequeBank { get; set; }
        public string ChequeBranch { get; set; }
        public string ChequeHolder { get; set; }
        public DateTime? ChequeDueDate { get; set; }
    }

    public class FnReceiptAllocation
    {
        public int Id { get; set; }
        public int ReceiptId { get; set; }
        public FnReceipt Receipt { get; set; }
        public int InvoiceId { get; set; }
        public FnSalesInvoice Invoice { get; set; }
        public decimal Amount { get; set; }
    }

    public enum FnPaymentPurpose { Supplier = 1, PettyCashTopUp = 2, Payroll = 3, EmployeeLoan = 4, BonusRun = 5, Termination = 6 }

    public class FnPayment
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public FnPaymentPurpose Purpose { get; set; }
        public int? ProviderId { get; set; }
        public int? PettyCashId { get; set; }
        /// <summary>Payroll payment: the posted payroll run (totals only; person level stays in payroll).</summary>
        public int? PayrollRunId { get; set; }
        /// <summary>The posted yearly bonus run that is paid (review 3, H6).</summary>
        public int? BonusRunId { get; set; }
        /// <summary>The posted end-of-service settlement that is paid (its net after tax and insurance).</summary>
        public int? TerminationId { get; set; }
        /// <summary>Employee loan or advance paid out (Dr 10401 with the employee detail / Cr bank).</summary>
        public int? EmployeeLoanId { get; set; }
        public string PayeeName { get; set; }
        public string Description { get; set; }
        /// <summary>Total of the payment requests (before deductions) or the top-up.</summary>
        public decimal GrossAmount { get; set; }
        public decimal DeductionAmount { get; set; }
        public decimal NetAmount { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? PostedByUserId { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        /// <summary>FINANCEMANAGER who approved a transfer to an IBAN other than the supplier's registered one.</summary>
        public int? IbanApprovedByUserId { get; set; }
        public ICollection<FnPaymentLine> Lines { get; set; }
        public ICollection<FnPaymentRequestLink> Requests { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnPaymentLine
    {
        public int Id { get; set; }
        public int PaymentId { get; set; }
        public FnPayment Payment { get; set; }
        public int RowNumber { get; set; }
        public FnInstrument Instrument { get; set; }
        public decimal Amount { get; set; }
        public int? BankAccountId { get; set; }
        public int? CashBoxId { get; set; }
        public string Reference { get; set; }
        public string PayeeIban { get; set; }
        public int? IssuedChequeId { get; set; }
        /// <summary>Cheque lines: the due date and Sayad id written on the leaf when the payment is posted.</summary>
        public DateTime? ChequeDueDate { get; set; }
        public string ChequeSayadId { get; set; }
        /// <summary>Line on a foreign-currency bank account: the amount in that currency and the rate to rials at the payment date (Amount = round(CurrencyAmount x Rate)).</summary>
        public decimal? CurrencyAmount { get; set; }
        public decimal? Rate { get; set; }
    }

    public class FnPaymentRequestLink
    {
        public int Id { get; set; }
        public int PaymentId { get; set; }
        public FnPayment Payment { get; set; }
        public int PaymentRequestId { get; set; }
        public Entities.Model.NasbNiro.Procurement.PaymentRequest PaymentRequest { get; set; }
        public decimal Amount { get; set; }
        public decimal Deductions { get; set; }
    }

    // ------------------------------------------------------------------------------------------ cheques

    public enum FnReceivedChequeStatus { InBox = 1, Deposited = 2, Cleared = 3, Bounced = 4, ReturnedToDrawer = 5, Endorsed = 6 }

    public class FnReceivedCheque
    {
        public int Id { get; set; }
        /// <summary>شناسه صیادی, 16 digits.</summary>
        public string SayadId { get; set; }
        public string Serial { get; set; }
        public string BankName { get; set; }
        public string Branch { get; set; }
        public string AccountHolder { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public int? CustomerId { get; set; }
        public FnCustomer Customer { get; set; }
        public int? ReceiptId { get; set; }
        public FnReceivedChequeStatus Status { get; set; }
        public int? DepositBankAccountId { get; set; }
        public int? EndorsedProviderId { get; set; }
        public DateTime ReceivedDate { get; set; }
        public ICollection<FnReceivedChequeHistory> History { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnReceivedChequeHistory
    {
        public int Id { get; set; }
        public int ChequeId { get; set; }
        public FnReceivedCheque Cheque { get; set; }
        public DateTime Date { get; set; }
        public FnReceivedChequeStatus? FromStatus { get; set; }
        public FnReceivedChequeStatus ToStatus { get; set; }
        public string Note { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
        /// <summary>The accounting event (and through it the voucher) of this step.</summary>
        public int? AccountingEventId { get; set; }
        public AccountingEvent AccountingEvent { get; set; }
    }

    public class FnChequeBook
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public FnBankAccount BankAccount { get; set; }
        public string Series { get; set; }
        public long FromSerial { get; set; }
        public long ToSerial { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public ICollection<FnIssuedCheque> Leaves { get; set; }
    }

    public enum FnIssuedChequeStatus { Blank = 0, Issued = 1, Cleared = 2, Cancelled = 3, Voided = 4 }

    public class FnIssuedCheque
    {
        public int Id { get; set; }
        public int ChequeBookId { get; set; }
        public FnChequeBook ChequeBook { get; set; }
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

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnGuaranteeStatus { Held = 1, Returned = 2, Collected = 3 }

    /// <summary>Guarantee cheque or promissory note (چک و سفته تضمینی): memorandum accounts only.</summary>
    public class FnGuarantee
    {
        public int Id { get; set; }
        public string Number { get; set; }
        /// <summary>1 = cheque, 2 = promissory note.</summary>
        public int Kind { get; set; }
        /// <summary>1 = received from others, 2 = given by the company.</summary>
        public int Direction { get; set; }
        public string PartyName { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public string Reference { get; set; }
        public FnGuaranteeStatus Status { get; set; }
        public DateTime? ClosedDate { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ petty cash settlement

    public class FnPettyCashSettlement
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public FnPettyCash PettyCash { get; set; }
        public DateTime Date { get; set; }
        public FnDocStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal VatAmount { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<FnPettyCashSettlementLine> Lines { get; set; }
    }

    public class FnPettyCashSettlementLine
    {
        public int Id { get; set; }
        public int SettlementId { get; set; }
        public FnPettyCashSettlement Settlement { get; set; }
        public int RowNumber { get; set; }
        public string Description { get; set; }
        /// <summary>Expense account (subsidiary).</summary>
        public int AccountId { get; set; }
        public int? CostCenterId { get; set; }
        public int? WorkOrderId { get; set; }
        /// <summary>Amount before VAT.</summary>
        public decimal Amount { get; set; }
        public decimal VatAmount { get; set; }
        public string SupplierName { get; set; }
        public Guid? DocumentId { get; set; }
        /// <summary>The petty-cash purchase receipt (warehouse document) this line settles.</summary>
        public int? InventoryDocumentId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ bank statements and reconciliation

    public class FnBankStatement
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public FnBankAccount BankAccount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public DateTimeOffset ImportedAt { get; set; }
        public int ImportedByUserId { get; set; }
        public ICollection<FnBankStatementLine> Lines { get; set; }
    }

    public enum FnStatementMatch { None = 0, Cheque = 1, Payment = 2, Receipt = 3, Voucher = 4, Manual = 5 }

    public class FnBankStatementLine
    {
        public int Id { get; set; }
        public int StatementId { get; set; }
        public FnBankStatement Statement { get; set; }
        public int RowNumber { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        /// <summary>Money out of the account (برداشت).</summary>
        public decimal Withdrawal { get; set; }
        /// <summary>Money into the account (واریز).</summary>
        public decimal Deposit { get; set; }
        public string Reference { get; set; }
        public decimal? Balance { get; set; }
        public FnStatementMatch MatchKind { get; set; }
        public int? MatchedId { get; set; }
        public int? AccountingEventId { get; set; }
        public AccountingEvent AccountingEvent { get; set; }
    }

    public class FnBankReconciliation
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public FnBankAccount BankAccount { get; set; }
        public int StatementId { get; set; }
        public DateTime AsOfDate { get; set; }
        public decimal BookBalance { get; set; }
        public decimal BankBalance { get; set; }
        public decimal DepositsInTransit { get; set; }
        public decimal OutstandingPayments { get; set; }
        public decimal OutstandingCheques { get; set; }
        public decimal Difference { get; set; }
        /// <summary>0 = draft, 1 = approved.</summary>
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ sales invoices

    public enum FnInvoiceType { Advance = 1, Progress = 2, Final = 3, Goods = 4, Service = 5, CreditNote = 6, RetentionRelease = 7 }

    public class FnSalesInvoice
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public DateTime? DueDate { get; set; }
        public int CustomerId { get; set; }
        public FnCustomer Customer { get; set; }
        public int? ContractId { get; set; }
        public int? MilestoneId { get; set; }
        public int? ProvisionalStatementId { get; set; }
        public FnInvoiceType Type { get; set; }
        public FnDocStatus Status { get; set; }
        public int? CreditedInvoiceId { get; set; }
        public string Description { get; set; }

        /// <summary>Net of discounts, before VAT.</summary>
        public decimal NetAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossAmount { get; set; }

        // deductions requested by the contract
        public decimal AdvanceRecovery { get; set; }
        public decimal PerformanceRetentionPercent { get; set; }
        public decimal PerformanceRetention { get; set; }
        public decimal InsuranceRetentionPercent { get; set; }
        public decimal InsuranceRetention { get; set; }
        public decimal WithholdingPercent { get; set; }
        public decimal Withholding { get; set; }
        /// <summary>Gross minus all deductions: what the customer owes in cash.</summary>
        public decimal ReceivableAmount { get; set; }

        // taxpayer system placeholders (filled from Sepidar later)
        public string TaxId { get; set; }
        public int TaxSentStatus { get; set; }

        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<FnSalesInvoiceLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnSalesInvoiceLine
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public FnSalesInvoice Invoice { get; set; }
        public int RowNumber { get; set; }
        public string Description { get; set; }
        public string ItemCode { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public bool VatExempt { get; set; }
        /// <summary>Net of discount, before VAT.</summary>
        public decimal Amount { get; set; }
        public decimal VatRatePercent { get; set; }
        public decimal VatAmount { get; set; }
        public decimal Total { get; set; }
    }
}
