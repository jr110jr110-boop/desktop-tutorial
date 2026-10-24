using System;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Finance
{
    // Finance release F3, milestone 3: financing, tax support, statements and receivables/payables completeness.

    // ------------------------------------------------------------------------------------------ loans (section 6)

    public enum FnFacilityType { Loan = 1, Murabaha = 2, Overdraft = 3, Leasing = 4 }
    public enum FnScheduleKind { EqualPrincipal = 1, Annuity = 2, Custom = 3 }
    public enum FnLoanStatus { Draft = 0, Active = 1, Closed = 2, Cancelled = 3 }

    /// <summary>The credit limit a bank gave the company; loans point to it for the utilisation report.</summary>
    public class FnFacilityLimit
    {
        public int Id { get; set; }
        public string BankName { get; set; }
        public decimal LimitAmount { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Note { get; set; }
    }

    public class FnLoan
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public string BankName { get; set; }
        public FnFacilityType FacilityType { get; set; }
        public string ContractNo { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        /// <summary>Annual percentage rate; the monthly rate is rate / 12 / 100 (★ day-count convention to be confirmed with the bank contract).</summary>
        public decimal RatePercent { get; set; }
        public FnScheduleKind ScheduleKind { get; set; }
        public int InstallmentCount { get; set; }
        public decimal FeeAmount { get; set; }
        public string Collateral { get; set; }
        public FnLoanStatus Status { get; set; }
        public int? FacilityLimitId { get; set; }
        /// <summary>The bank account that receives the money and pays the instalments.</summary>
        public int? BankAccountId { get; set; }
        public int? DisbursementVoucherId { get; set; }
        public DateTime? DisbursedDate { get; set; }
        /// <summary>Interest booked as expense so far (accruals and the part booked at payment).</summary>
        public decimal InterestExpensed { get; set; }
        public decimal InterestPaid { get; set; }
        public decimal PrincipalPaid { get; set; }
        /// <summary>The part of the long-term loan already reclassified to the current portion (short-term) by the period-close runs.</summary>
        public decimal CurrentPortionReclassified { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnLoanInstallment> Installments { get; set; }
    }

    public class FnLoanInstallment
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public FnLoan Loan { get; set; }
        public int Sequence { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Principal { get; set; }
        public decimal Interest { get; set; }
        /// <summary>0 due, 1 paid.</summary>
        public int Status { get; set; }
        public DateTime? PaidDate { get; set; }
        public decimal PenaltyPaid { get; set; }
        public int? VoucherId { get; set; }
    }

    /// <summary>Monthly interest accrual: drafted by the run, approved by a second person.</summary>
    public class FnLoanAccrual
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public int PeriodId { get; set; }
        public decimal Amount { get; set; }
        /// <summary>0 draft, 1 approved.</summary>
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? VoucherId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ tax support (section 7)

    public enum FnTaxAdjustmentKind { NonDeductibleExpense = 1, Exemption = 2, LossCarriedForward = 3, Other = 9 }

    /// <summary>A book-tax difference. Positive amounts increase the taxable profit, negative ones reduce it. Inputs with a source; never seeded.</summary>
    public class FnTaxAdjustment
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public FnTaxAdjustmentKind Kind { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; } = true;
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ analysis (section 8)

    public class FnReportView
    {
        public int Id { get; set; }
        public string ReportKey { get; set; }
        public string Title { get; set; }
        public string FiltersJson { get; set; }
        public string ColumnsJson { get; set; }
        public int OwnerUserId { get; set; }
        public bool Shared { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ receivables and payables (section 10)

    public class FnCreditHold
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string Reason { get; set; }
        public bool Active { get; set; }
        public int SetByUserId { get; set; }
        public DateTimeOffset SetAt { get; set; }
    }

    public class FnBadDebtRun
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public DateTime AsOf { get; set; }
        public decimal Required { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal Change { get; set; }
        public string RatesJson { get; set; }
        public int? VoucherId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class FnDunning
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CustomerId { get; set; }
        public DateTime AsOf { get; set; }
        /// <summary>Current, 31-60, 61-90 or Over90: the worst bucket the customer is in.</summary>
        public string Bucket { get; set; }
        public decimal Amount { get; set; }
        public string Text { get; set; }
        /// <summary>0 draft, 1 approved for sending (the letter module sends later).</summary>
        public int Status { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
    }

    public class FnPaymentProposal
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime AsOf { get; set; }
        public decimal CashLimit { get; set; }
        public decimal Total { get; set; }
        /// <summary>0 draft, 1 approved.</summary>
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<FnPaymentProposalLine> Lines { get; set; }
    }

    public class FnPaymentProposalLine
    {
        public int Id { get; set; }
        public int ProposalId { get; set; }
        public FnPaymentProposal Proposal { get; set; }
        public int SupplierInvoiceId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        /// <summary>False when the cash limit did not allow it.</summary>
        public bool Included { get; set; }
    }
}
