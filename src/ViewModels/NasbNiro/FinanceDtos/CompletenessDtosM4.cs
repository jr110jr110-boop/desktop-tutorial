using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Finance release F3b (migration FinanceCompleteness4). New DTOs carry the Fn prefix.

    // ------------------------------------------------------------------------------------------ direct-method cash flow

    public class FnDirectRowDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        /// <summary>Operating, Investing or Financing.</summary>
        public string Section { get; set; }
        public decimal Receipts { get; set; }
        public decimal Payments { get; set; }
        public decimal Net { get; set; }
        public List<string> Accounts { get; set; } = new List<string>();
    }

    public class FnCashReconRowDto
    {
        public string Section { get; set; }
        public decimal Direct { get; set; }
        public decimal Indirect { get; set; }
        public decimal Difference { get; set; }
    }

    public class FnDirectCashFlowDto
    {
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal ClosingCash { get; set; }
        public List<FnDirectRowDto> Rows { get; set; } = new List<FnDirectRowDto>();
        public decimal Operating { get; set; }
        public decimal Investing { get; set; }
        public decimal Financing { get; set; }
        public decimal NetChange { get; set; }
        /// <summary>The same total from the indirect method: they must be equal.</summary>
        public decimal IndirectNetChange { get; set; }
        public bool Ties { get; set; }
        public List<FnCashReconRowDto> Reconciliation { get; set; } = new List<FnCashReconRowDto>();
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ bank guarantees

    public class FnSaveBankGuaranteeDto
    {
        public FnBankGuaranteeDirection Direction { get; set; }
        public FnBankGuaranteeKind Kind { get; set; }
        public string BankName { get; set; }
        public int? BankAccountId { get; set; }
        public string GuaranteeNo { get; set; }
        public string PartyName { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        /// <summary>The bank's fee for an issued guarantee: paid from BankAccountId and spread over the life of the guarantee.</summary>
        public decimal FeeAmount { get; set; }
        public string Note { get; set; }
    }

    public class FnGuaranteeEventDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public FnGuaranteeEventKind Kind { get; set; }
        public DateTime? OldExpiry { get; set; }
        public DateTime? NewExpiry { get; set; }
        public decimal Amount { get; set; }
        public decimal FeeAmount { get; set; }
        public string Note { get; set; }
    }

    public class FnBankGuaranteeDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnBankGuaranteeDirection Direction { get; set; }
        public FnBankGuaranteeKind Kind { get; set; }
        public string BankName { get; set; }
        public string GuaranteeNo { get; set; }
        public string PartyName { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public decimal FeeAmount { get; set; }
        public decimal FeeAmortised { get; set; }
        public decimal FeeRemaining { get; set; }
        public FnBankGuaranteeStatus Status { get; set; }
        public DateTime? ClosedDate { get; set; }
        public int DaysToExpiry { get; set; }
        public string Note { get; set; }
        public List<FnGuaranteeEventDto> Events { get; set; } = new List<FnGuaranteeEventDto>();
    }

    public class FnGuaranteeActionDto
    {
        public int Id { get; set; }
        /// <summary>extend, release, claim, reduce.</summary>
        public string Action { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? NewExpiry { get; set; }
        /// <summary>Reduce / claim amount (release closes the whole guarantee).</summary>
        public decimal Amount { get; set; }
        /// <summary>Fee of an extension, paid from the guarantee's bank account.</summary>
        public decimal FeeAmount { get; set; }
        public string Note { get; set; }
    }

    public class FnGuaranteeLimitDto
    {
        public string BankName { get; set; }
        public decimal Limit { get; set; }
        public string Note { get; set; }
    }

    public class FnGuaranteeBankExposureDto
    {
        public string BankName { get; set; }
        public int ActiveCount { get; set; }
        public decimal Exposure { get; set; }
        public decimal Limit { get; set; }
        /// <summary>Limit minus exposure (empty when the bank has no limit on file).</summary>
        public decimal? Available { get; set; }
    }

    public class FnGuaranteeExposureDto
    {
        public DateTime AsOf { get; set; }
        public List<FnGuaranteeBankExposureDto> Banks { get; set; } = new List<FnGuaranteeBankExposureDto>();
        public decimal TotalIssued { get; set; }
        public decimal TotalReceived { get; set; }
        public List<FnBankGuaranteeDto> ExpiringWithin30Days { get; set; } = new List<FnBankGuaranteeDto>();
    }

    // ------------------------------------------------------------------------------------------ insurance clearance and retention release

    public class FnSaveClearanceDto
    {
        public int? Id { get; set; }
        public int ProviderId { get; set; }
        public int? ContractId { get; set; }
        public string LetterNo { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ValidUntil { get; set; }
        public Guid? DocumentId { get; set; }
        public bool IsRevoked { get; set; }
        public string Note { get; set; }
    }

    public class FnClearanceDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? ContractId { get; set; }
        public string LetterNo { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ValidUntil { get; set; }
        public Guid? DocumentId { get; set; }
        public bool HasDocument { get; set; }
        public bool IsRevoked { get; set; }
        /// <summary>Document attached, not revoked and not expired on the date asked.</summary>
        public bool IsValid { get; set; }
        public string Note { get; set; }
    }

    public class FnRetentionStatusDto
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? ContractId { get; set; }
        public DateTime AsOf { get; set; }
        /// <summary>Performance retention deducted from the paid payment requests.</summary>
        public decimal Held { get; set; }
        public decimal Released { get; set; }
        public decimal Releasable { get; set; }
        public int? ValidClearanceId { get; set; }
        public bool CanRelease { get; set; }
        public string Message { get; set; }
    }

    public class FnRetentionReleaseRequestDto
    {
        public int ProviderId { get; set; }
        public int? ContractId { get; set; }
        public DateTime? Date { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class FnRetentionReleaseDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public int? ContractId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int ClearanceId { get; set; }
        public int VoucherId { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ termination settlement and annual bonus

    public class FnTerminationInputDto
    {
        public int? Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime TerminationDate { get; set; }
        /// <summary>Empty = entitlement minus used leave of the Persian year of the termination.</summary>
        public decimal? UnusedLeaveDays { get; set; }
        /// <summary>Notice days owed (decided by finance / HR); paid at the daily wage.</summary>
        public decimal NoticeDays { get; set; }
        /// <summary>Empty = the severance items of the last payslip (else daily wage x 30).</summary>
        public decimal? MonthlyBase { get; set; }
        public string Note { get; set; }
    }

    public class FnTerminationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime TerminationDate { get; set; }
        public decimal YearsOfService { get; set; }
        public decimal MonthlyBase { get; set; }
        public decimal DailyWage { get; set; }
        public decimal SeveranceDaysPerYear { get; set; }
        public decimal UnusedLeaveDays { get; set; }
        public decimal NoticeDays { get; set; }
        public decimal SeverancePay { get; set; }
        public decimal LeavePay { get; set; }
        public decimal NoticePay { get; set; }
        public decimal Total { get; set; }
        public decimal FromSeveranceProvision { get; set; }
        public decimal FromLeaveProvision { get; set; }
        public FnSettlementStatus Status { get; set; }
        public int? VoucherId { get; set; }
        public string Note { get; set; }
        public List<string> Sources { get; set; } = new List<string>();
    }

    public class FnBonusInputDto
    {
        public int? Id { get; set; }
        public int Year { get; set; }
        public DateTime PayDate { get; set; }
        /// <summary>Empty = every employee who was employed in the year.</summary>
        public List<int> EmployeeIds { get; set; }
        public string Note { get; set; }
    }

    public class FnBonusLineDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public decimal DailyWage { get; set; }
        public int EmployedDays { get; set; }
        public int YearDays { get; set; }
        public decimal Gross { get; set; }
        public decimal MonthlyTaxable { get; set; }
        public decimal Tax { get; set; }
        public decimal Net { get; set; }
    }

    public class FnBonusRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Year { get; set; }
        public DateTime PayDate { get; set; }
        public FnSettlementStatus Status { get; set; }
        public decimal BonusDays { get; set; }
        public decimal CapDays { get; set; }
        public decimal MinimumDailyWage { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalNet { get; set; }
        public decimal FromProvision { get; set; }
        public decimal ExpensedNow { get; set; }
        public int? VoucherId { get; set; }
        public string Note { get; set; }
        public List<FnBonusLineDto> Lines { get; set; } = new List<FnBonusLineDto>();
        public List<string> Sources { get; set; } = new List<string>();
    }

    public class FnTaxReconRowDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public int Payslips { get; set; }
        public decimal Taxable { get; set; }
        public decimal TaxWithheld { get; set; }
        /// <summary>The annual tax of the whole year's taxable pay on the year's brackets.</summary>
        public decimal TaxRecomputed { get; set; }
        /// <summary>Recomputed minus withheld: positive = more tax is due, negative = overpaid.</summary>
        public decimal Difference { get; set; }
    }

    public class FnTaxReconciliationDto
    {
        public int Year { get; set; }
        public decimal TotalTaxable { get; set; }
        public decimal TotalWithheld { get; set; }
        public decimal TotalRecomputed { get; set; }
        public decimal TotalDifference { get; set; }
        /// <summary>Credits to the payroll tax payable account in the year by posted payroll runs.</summary>
        public decimal LedgerWithheld { get; set; }
        public bool TiesToLedger { get; set; }
        public string Note { get; set; }
        public List<FnTaxReconRowDto> Rows { get; set; } = new List<FnTaxReconRowDto>();
    }

    // ------------------------------------------------------------------------------------------ planned vs actual

    public class FnSaveEstimateDto
    {
        public int WorkOrderId { get; set; }
        public decimal Material { get; set; }
        public decimal Labour { get; set; }
        public decimal Overhead { get; set; }
        public decimal Subcontract { get; set; }
        public int Quantity { get; set; }
        public string Note { get; set; }
    }

    public class FnPlannedActualRowDto
    {
        public string Element { get; set; }
        public decimal Planned { get; set; }
        public decimal Actual { get; set; }
        /// <summary>Actual minus planned: positive = over budget.</summary>
        public decimal Variance { get; set; }
        public decimal? VariancePercent { get; set; }
    }

    public class FnPlannedActualDto
    {
        public int WorkOrderId { get; set; }
        public string Code { get; set; }
        public bool HasEstimate { get; set; }
        public bool IsClosed { get; set; }
        public int PlannedQuantity { get; set; }
        public int ActualQuantity { get; set; }
        public List<FnPlannedActualRowDto> Rows { get; set; } = new List<FnPlannedActualRowDto>();
        public decimal PlannedTotal { get; set; }
        public decimal ActualTotal { get; set; }
        public decimal Variance { get; set; }
        public decimal? VariancePercent { get; set; }
        public decimal PlannedUnitCost { get; set; }
        public decimal ActualUnitCost { get; set; }
    }

    // ------------------------------------------------------------------------------------------ supplier notes and statements

    public class FnSupplierNoteInputDto
    {
        public int? Id { get; set; }
        public FnSupplierNoteKind Kind { get; set; }
        public int ProviderId { get; set; }
        public int? SupplierInvoiceId { get; set; }
        public string NoteNumber { get; set; }
        public DateTime Date { get; set; }
        public decimal NetAmount { get; set; }
        /// <summary>Empty = the VAT percent of the date on the net amount (0 when the invoice is not official).</summary>
        public decimal? VatAmount { get; set; }
        public string Reason { get; set; }
    }

    public class FnSupplierNoteDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnSupplierNoteKind Kind { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? SupplierInvoiceId { get; set; }
        public string NoteNumber { get; set; }
        public DateTime Date { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; }
        public FnSettlementStatus Status { get; set; }
        public int? VoucherId { get; set; }
    }

    public class FnStatementLineInputDto
    {
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; }
        public string Description { get; set; }
        public decimal Credit { get; set; }
        public decimal Debit { get; set; }
    }

    public class FnSupplierStatementInputDto
    {
        public int ProviderId { get; set; }
        public DateTime AsOf { get; set; }
        public decimal TheirBalance { get; set; }
        public string Note { get; set; }
        public List<FnStatementLineInputDto> Lines { get; set; } = new List<FnStatementLineInputDto>();
    }

    public class FnStatementLineDto
    {
        public int RowNumber { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; }
        public string Description { get; set; }
        public decimal Credit { get; set; }
        public decimal Debit { get; set; }
        public string MatchedKind { get; set; }
        public string MatchedNumber { get; set; }
        /// <summary>Set when the document number matched but the amount did not: their net (credit minus debit) minus ours.</summary>
        public decimal? AmountDifference { get; set; }
    }

    public class FnOurItemDto
    {
        public DateTime Date { get; set; }
        public string Kind { get; set; }
        public string Number { get; set; }
        public decimal Credit { get; set; }
        public decimal Debit { get; set; }
    }

    public class FnSupplierStatementDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateTime AsOf { get; set; }
        public decimal TheirBalance { get; set; }
        public decimal OurBalance { get; set; }
        public decimal Difference { get; set; }
        public List<FnStatementLineDto> Lines { get; set; } = new List<FnStatementLineDto>();
        public List<FnStatementLineDto> UnmatchedTheirs { get; set; } = new List<FnStatementLineDto>();
        public List<FnOurItemDto> UnmatchedOurs { get; set; } = new List<FnOurItemDto>();
        /// <summary>Their unmatched (credit - debit) minus our unmatched (credit - debit): the part of the difference the unmatched items explain.</summary>
        public decimal Explained { get; set; }
        public decimal Unexplained { get; set; }
        public bool Reconciled { get; set; }
    }

    // ------------------------------------------------------------------------------------------ customer confirmation letters

    public class FnConfirmationInputDto
    {
        public int CustomerId { get; set; }
        public DateTime AsOf { get; set; }
    }

    public class FnConfirmationRespondDto
    {
        public int Id { get; set; }
        /// <summary>confirm or dispute.</summary>
        public string Action { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? Date { get; set; }
        public string Note { get; set; }
    }

    public class FnConfirmationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string CompanyName { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string EconomicCode { get; set; }
        public DateTime AsOf { get; set; }
        public decimal LedgerBalance { get; set; }
        public decimal StatementBalance { get; set; }
        public bool Agrees { get; set; }
        public int OpenDocuments { get; set; }
        public FnConfirmationStatus Status { get; set; }
        public decimal? ConfirmedAmount { get; set; }
        public DateTime? ResponseDate { get; set; }
        public string ResponseNote { get; set; }
        public List<CustomerStatementRowDto> Rows { get; set; } = new List<CustomerStatementRowDto>();
    }

    // ------------------------------------------------------------------------------------------ comparative statements and notes

    public class FnComparativeRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Section { get; set; }
        public bool IsTotal { get; set; }
        public decimal Current { get; set; }
        public decimal Prior { get; set; }
        public decimal Change { get; set; }
        public decimal? ChangePercent { get; set; }
    }

    public class FnComparativeDto
    {
        /// <summary>BalanceSheet or IncomeStatement.</summary>
        public string Kind { get; set; }
        public int FiscalYearId { get; set; }
        public int? PriorFiscalYearId { get; set; }
        public int Year { get; set; }
        public int? PriorYear { get; set; }
        public DateTime AsOf { get; set; }
        public DateTime? PriorAsOf { get; set; }
        public List<FnComparativeRowDto> Rows { get; set; } = new List<FnComparativeRowDto>();
        public string Note { get; set; }
    }

    public class FnNoteAgeingDto
    {
        public string Name { get; set; }
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }
        public decimal Total { get; set; }
    }

    public class FnNoteLoanDto
    {
        public string Number { get; set; }
        public string BankName { get; set; }
        public decimal Original { get; set; }
        public decimal Outstanding { get; set; }
        public decimal CurrentPortion { get; set; }
        public decimal LongTerm { get; set; }
        public decimal PriorOutstanding { get; set; }
    }

    public class FnNoteProvisionDto
    {
        public string Number { get; set; }
        public string Description { get; set; }
        public decimal Balance { get; set; }
        public decimal PriorBalance { get; set; }
    }

    public class FnNoteInventoryDto
    {
        public string AccountCode { get; set; }
        public string Title { get; set; }
        public decimal Balance { get; set; }
        public decimal PriorBalance { get; set; }
    }

    public class FnNotesDto
    {
        public int FiscalYearId { get; set; }
        public DateTime AsOf { get; set; }
        public DateTime? PriorAsOf { get; set; }
        public AgeingDto ReceivablesAgeing { get; set; }
        public AgeingDto PriorReceivablesAgeing { get; set; }
        public List<FnNoteAgeingDto> Payables { get; set; } = new List<FnNoteAgeingDto>();
        public decimal PayablesTotal { get; set; }
        public decimal PriorPayablesTotal { get; set; }
        public List<FnNoteLoanDto> Loans { get; set; } = new List<FnNoteLoanDto>();
        public List<FnNoteProvisionDto> Provisions { get; set; } = new List<FnNoteProvisionDto>();
        public List<FnNoteInventoryDto> Inventory { get; set; } = new List<FnNoteInventoryDto>();
        public decimal InventoryTotal { get; set; }
        public decimal PriorInventoryTotal { get; set; }
    }
}
