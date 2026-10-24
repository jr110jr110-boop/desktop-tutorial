using System;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Finance
{
    // Finance release F3b (migration FinanceCompleteness4): bank guarantees, contractor insurance clearance and retention release, termination settlement,
    // annual bonus, planned cost per work order, supplier credit/debit notes, supplier statement reconciliation and customer confirmation letters.

    // ------------------------------------------------------------------------------------------ bank-guarantee register

    public enum FnBankGuaranteeDirection { Issued = 1, Received = 2 }
    public enum FnBankGuaranteeKind { Performance = 1, Advance = 2, Bid = 3, Other = 9 }
    public enum FnBankGuaranteeStatus { Active = 1, Released = 2, Expired = 3, Claimed = 4 }
    public enum FnGuaranteeEventKind { Extended = 1, Released = 2, Claimed = 3, Reduced = 4, Expired = 5 }

    /// <summary>
    /// A bank guarantee (ضمانت‌نامه بانکی). Issued = our bank guarantees for us (exposure against the bank's limit, a fee that is deferred over its life);
    /// Received = a supplier's or contractor's bank guarantees to us (memorandum only).
    /// </summary>
    public class FnBankGuarantee
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public FnBankGuaranteeDirection Direction { get; set; }
        public FnBankGuaranteeKind Kind { get; set; }
        /// <summary>The issuing bank (issued guarantees: our bank, counted against its limit).</summary>
        public string BankName { get; set; }
        /// <summary>Our bank account that paid the fee (issued only).</summary>
        public int? BankAccountId { get; set; }
        /// <summary>The bank's own guarantee number.</summary>
        public string GuaranteeNo { get; set; }
        /// <summary>Beneficiary (issued) or applicant (received).</summary>
        public string PartyName { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public decimal FeeAmount { get; set; }
        public int? RegistrationVoucherId { get; set; }
        public FnVoucher RegistrationVoucher { get; set; }
        public FnBankGuaranteeStatus Status { get; set; }
        public DateTime? ClosedDate { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnBankGuaranteeEvent> Events { get; set; }
    }

    public class FnBankGuaranteeEvent
    {
        public int Id { get; set; }
        public int GuaranteeId { get; set; }
        public FnBankGuarantee Guarantee { get; set; }
        public DateTime Date { get; set; }
        public FnGuaranteeEventKind Kind { get; set; }
        public DateTime? OldExpiry { get; set; }
        public DateTime? NewExpiry { get; set; }
        /// <summary>Amount released / claimed / reduced.</summary>
        public decimal Amount { get; set; }
        public decimal FeeAmount { get; set; }
        public int? VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public string Note { get; set; }
        public int UserId { get; set; }
    }

    /// <summary>The guarantee limit a bank gave the company (سقف ضمانت‌نامه); exposure of the active issued guarantees is measured against it.</summary>
    public class FnBankGuaranteeLimit
    {
        public int Id { get; set; }
        public string BankName { get; set; }
        public decimal Limit { get; set; }
        public string Note { get; set; }
        public int UpdatedByUserId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ contractor insurance clearance

    /// <summary>The social-insurance clearance letter of a contractor (مفاصاحساب بیمه): document and dates. Performance retention is released only against a valid one.</summary>
    public class FnInsuranceClearance
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public int? ContractId { get; set; }
        /// <summary>The number on the letter.</summary>
        public string LetterNo { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ValidUntil { get; set; }
        /// <summary>The scanned letter (document store). Without it the clearance does not count.</summary>
        public Guid? DocumentId { get; set; }
        public bool IsRevoked { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class FnRetentionRelease
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public int? ContractId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int ClearanceId { get; set; }
        public int VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ termination settlement and annual bonus

    public enum FnSettlementStatus { Draft = 0, Posted = 1, Cancelled = 2 }

    /// <summary>End-of-service settlement of one employee: severance for the years of service, unused leave and notice. Rates come from the rate table (Source / Verify).</summary>
    public class FnTermination
    {
        public int Id { get; set; }
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public int EmployeeId { get; set; }
        public FnEmployee Employee { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime TerminationDate { get; set; }
        /// <summary>Years of service: whole Persian months between the two dates divided by 12.</summary>
        public decimal YearsOfService { get; set; }
        /// <summary>Last monthly base of the severance items.</summary>
        public decimal MonthlyBase { get; set; }
        public decimal DailyWage { get; set; }
        public decimal SeveranceDaysPerYear { get; set; }
        public decimal UnusedLeaveDays { get; set; }
        public decimal NoticeDays { get; set; }
        public decimal SeverancePay { get; set; }
        public decimal LeavePay { get; set; }
        public decimal NoticePay { get; set; }
        public decimal Total { get; set; }
        /// <summary>Income tax and employee insurance on the leave and notice pay (severance is exempt); the net is what is paid.</summary>
        public decimal TaxAmount { get; set; }
        public decimal InsuranceAmount { get; set; }
        public decimal NetPayable { get; set; }
        /// <summary>Covered by the provisions already booked; the rest is expensed now.</summary>
        public decimal FromSeveranceProvision { get; set; }
        public decimal FromLeaveProvision { get; set; }
        public FnSettlementStatus Status { get; set; }
        public int? VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public string Note { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    /// <summary>The yearly bonus run (عیدی): per employee from the days in the rate table, taxed at the margin of the employee's month, paid from the bonus provision.</summary>
    public class FnBonusRun
    {
        public int Id { get; set; }
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        /// <summary>Persian year the bonus belongs to.</summary>
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
        public FnVoucher Voucher { get; set; }
        public string Note { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<FnBonusLine> Lines { get; set; }
    }

    public class FnBonusLine
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public FnBonusRun Run { get; set; }
        public int EmployeeId { get; set; }
        public decimal DailyWage { get; set; }
        /// <summary>Days of the Persian year the employee was employed, and days in that year.</summary>
        public int EmployedDays { get; set; }
        public int YearDays { get; set; }
        public decimal Gross { get; set; }
        /// <summary>Monthly taxable pay the margin is measured from (the employee's last payslip of the year).</summary>
        public decimal MonthlyTaxable { get; set; }
        public decimal Tax { get; set; }
        public decimal Net { get; set; }
    }

    // ------------------------------------------------------------------------------------------ planned cost per work order

    /// <summary>The estimate of a work order (برآورد), by cost element; the report sets it against the actual cost sheet.</summary>
    public class FnWorkOrderEstimate
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public decimal Material { get; set; }
        public decimal Labour { get; set; }
        public decimal Overhead { get; set; }
        public decimal Subcontract { get; set; }
        public int Quantity { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ supplier credit / debit notes and statement reconciliation

    public enum FnSupplierNoteKind { Credit = 1, Debit = 2 }

    /// <summary>
    /// A supplier credit note (reduces what we owe: price adjustment, discount) or debit note (increases it). Net and VAT; the VAT follows the VAT return of the season.
    /// Goods returns stay on the warehouse return document.
    /// </summary>
    public class FnSupplierNote
    {
        public int Id { get; set; }
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public FnSupplierNoteKind Kind { get; set; }
        public int ProviderId { get; set; }
        public int? SupplierInvoiceId { get; set; }
        /// <summary>The supplier's own number of the note.</summary>
        public string NoteNumber { get; set; }
        public DateTime Date { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        /// <summary>The part of the VAT that follows the original invoice's non-deductible share (it was cost, so the note reverses cost, not input VAT).</summary>
        public decimal NonRecoverableVatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; }
        public FnSettlementStatus Status { get; set; }
        public int? VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    /// <summary>The statement a supplier sent (مغایرت‌گیری): its lines are matched against our documents; what stays unmatched explains the difference.</summary>
    public class FnSupplierStatement
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public DateTime AsOf { get; set; }
        /// <summary>What the supplier says we owe (credit positive).</summary>
        public decimal TheirBalance { get; set; }
        /// <summary>What our ledger says we owe at the same date.</summary>
        public decimal OurBalance { get; set; }
        public decimal Difference { get; set; }
        public bool Reconciled { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnSupplierStatementLine> Lines { get; set; }
    }

    public class FnSupplierStatementLine
    {
        public int Id { get; set; }
        public int StatementId { get; set; }
        public FnSupplierStatement Statement { get; set; }
        public int RowNumber { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; }
        public string Description { get; set; }
        /// <summary>Seen from the supplier: the amount they billed us (our liability grows).</summary>
        public decimal Credit { get; set; }
        /// <summary>Seen from the supplier: payments and credit notes they received or issued (our liability falls).</summary>
        public decimal Debit { get; set; }
        /// <summary>Invoice / Payment / Note, or null when nothing of ours matches.</summary>
        public string MatchedKind { get; set; }
        public string MatchedNumber { get; set; }
    }

    // ------------------------------------------------------------------------------------------ customer confirmation letters

    public enum FnConfirmationStatus { Issued = 1, Confirmed = 2, Disputed = 3 }

    public class FnConfirmationLetter
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CustomerId { get; set; }
        public DateTime AsOf { get; set; }
        /// <summary>The balance of the customer account in the ledger / of the customer statement on the date (they must agree).</summary>
        public decimal LedgerBalance { get; set; }
        public decimal StatementBalance { get; set; }
        public int OpenDocuments { get; set; }
        public FnConfirmationStatus Status { get; set; }
        public decimal? ConfirmedAmount { get; set; }
        public DateTime? ResponseDate { get; set; }
        public string ResponseNote { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
