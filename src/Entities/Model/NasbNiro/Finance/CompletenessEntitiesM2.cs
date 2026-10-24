using System;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Finance
{
    // Finance release F3, milestone 2: foreign currency, budgeting, costing completeness, prepaid / accruals / provisions.

    // ------------------------------------------------------------------------------------------ foreign currency (section 3)

    public class FnCurrency
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int Decimals { get; set; } = 2;
        public bool IsBase { get; set; }
    }

    public enum FnRateSource { Manual = 1, CentralBank = 2, Market = 3 }

    /// <summary>Official, negotiated or free-market rate (★ which one is used at period end is a setting).</summary>
    public enum FnRateType { Official = 1, Negotiated = 2, Free = 3 }

    /// <summary>A rate is an input: rial per one unit of the currency. Never seeded.</summary>
    public class FnExchangeRate
    {
        public int Id { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime Date { get; set; }
        public decimal? Buy { get; set; }
        public decimal? Sell { get; set; }
        /// <summary>The rate used by the system; the mean of buy and sell when only those two are entered.</summary>
        public decimal Average { get; set; }
        public FnRateSource Source { get; set; }
        public FnRateType RateType { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>One period-end revaluation of the monetary foreign-currency items of an entity.</summary>
    public class FnFxRevaluation
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        /// <summary>1, 2 ... within the entity and period (later runs post only the new differences); unique, so two concurrent runs cannot both post.</summary>
        public int Sequence { get; set; } = 1;
        public FnRateType RateType { get; set; }
        public bool Reverse { get; set; }
        public decimal TotalGain { get; set; }
        public decimal TotalLoss { get; set; }
        public int VoucherId { get; set; }
        public int? ReversalVoucherId { get; set; }
        public string Warning { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnFxRevaluationLine> Lines { get; set; }
    }

    public class FnFxRevaluationLine
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public FnFxRevaluation Run { get; set; }
        public int AccountId { get; set; }
        public int? Detail1Id { get; set; }
        public string CurrencyCode { get; set; }
        public decimal CurrencyBalance { get; set; }
        public decimal BookBalance { get; set; }
        public decimal Rate { get; set; }
        public decimal NewBalance { get; set; }
        public decimal Difference { get; set; }
    }

    /// <summary>Settlement of a foreign receivable or payable with the realised exchange difference.</summary>
    public class FnFxSettlement
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public DateTime Date { get; set; }
        public string Direction { get; set; }
        public string CurrencyCode { get; set; }
        public decimal CurrencyAmount { get; set; }
        public decimal BookRate { get; set; }
        public decimal SettleRate { get; set; }
        public decimal BookBase { get; set; }
        public decimal SettledBase { get; set; }
        /// <summary>Positive = gain, negative = loss.</summary>
        public decimal Difference { get; set; }
        public int VoucherId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public enum FnLcStatus { Opened = 1, DocumentsReceived = 2, Paid = 3, Expired = 4, Cancelled = 5 }

    public enum FnLcPostingKind { Margin = 1, Fee = 2, Settlement = 3, MarginRelease = 4 }

    public class FnLetterOfCredit
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string LcNumber { get; set; }
        public int ProviderId { get; set; }
        public int BankAccountId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Amount { get; set; }
        public DateTime OpenDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        /// <summary>Input ★: the bank's margin / deposit percentage.</summary>
        public decimal MarginPercent { get; set; }
        public decimal MarginPaid { get; set; }
        public decimal FeesPaid { get; set; }
        public FnLcStatus Status { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string ForeignInvoiceRef { get; set; }
        public decimal? SettlementRate { get; set; }
        public decimal? SettledBase { get; set; }
        public DateTime? PaidDate { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnLcPosting> Postings { get; set; }
    }

    public class FnLcPosting
    {
        public int Id { get; set; }
        public int LcId { get; set; }
        public FnLetterOfCredit Lc { get; set; }
        public FnLcPostingKind Kind { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int VoucherId { get; set; }
        public string Note { get; set; }
        public int CreatedByUserId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ budget (section 4)

    public enum FnBudgetKind { Annual = 1, Revision = 2 }
    public enum FnBudgetStatus { Draft = 0, Approved = 1, Superseded = 2 }
    public enum FnCommitMode { Off = 0, Warn = 1, Block = 2 }

    public class FnBudget
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int FiscalYearId { get; set; }
        public FnFiscalYear FiscalYear { get; set; }
        public int Version { get; set; }
        public FnBudgetKind Kind { get; set; }
        public FnBudgetStatus Status { get; set; }
        public FnCommitMode CommitMode { get; set; } = FnCommitMode.Warn;
        public string Title { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ManagerApprovedByUserId { get; set; }
        public DateTimeOffset? ManagerApprovedAt { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public int? SupersededByBudgetId { get; set; }
        public ICollection<FnBudgetLine> Lines { get; set; }
    }

    public class FnBudgetLine
    {
        public int Id { get; set; }
        public int BudgetId { get; set; }
        public FnBudget Budget { get; set; }
        /// <summary>Any level: a general account budget covers all its subsidiary accounts.</summary>
        public int? AccountId { get; set; }
        public int? CostCenterId { get; set; }
        public int? ContractId { get; set; }
        /// <summary>1..12 of the fiscal year.</summary>
        public int Period { get; set; }
        public decimal Amount { get; set; }
        /// <summary>Capital budget line: linked to an asset class, spent by acquisitions.</summary>
        public bool IsCapital { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ costing (section 5)

    public enum FnOverheadBase { LabourHours = 1, MachineHours = 2, DirectLabourCost = 3, DirectMaterialCost = 4 }

    public class FnOverheadRate
    {
        public int Id { get; set; }
        public int CostCenterId { get; set; }
        public FnOverheadBase Base { get; set; }
        public decimal Rate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; } = true;
    }

    /// <summary>Share of an indirect cost centre that goes to a producing one (percent or a statistical basis such as area, headcount, kWh).</summary>
    public class FnAllocationKey
    {
        public int Id { get; set; }
        public int FromCostCenterId { get; set; }
        public int ToCostCenterId { get; set; }
        /// <summary>Percent, Area, Headcount, KWh or Other; the share is value / sum of the values of the same source.</summary>
        public string Basis { get; set; }
        public decimal Value { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        /// <summary>Step-down order of the source cost centre (lower first).</summary>
        public int Sequence { get; set; }
        public string Source { get; set; }
    }

    /// <summary>Hours or other bases of a work order in a period (inputs of the absorption).</summary>
    public class FnWorkOrderBase
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public int CostCenterId { get; set; }
        public int PeriodId { get; set; }
        public FnOverheadBase Base { get; set; }
        public decimal Quantity { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public class FnOverheadAllocation
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public decimal TotalAllocated { get; set; }
        public decimal TotalAbsorbed { get; set; }
        /// <summary>Actual overhead of producing cost centres minus absorbed; positive = under-absorbed.</summary>
        public decimal Variance { get; set; }
        public int? AllocationVoucherId { get; set; }
        public int? AbsorptionVoucherId { get; set; }
        public int? VarianceVoucherId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class FnStandardCost
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public decimal Cost { get; set; }
        public DateTime ValidFrom { get; set; }
        public string Source { get; set; }
    }

    /// <summary>Close-out of a work order: final cost, transfer to finished goods and the residual variance.</summary>
    public class FnWorkOrderClose
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public decimal Material { get; set; }
        public decimal Labour { get; set; }
        public decimal Overhead { get; set; }
        public decimal Subcontract { get; set; }
        public decimal TotalCost { get; set; }
        public decimal Variance { get; set; }
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        /// <summary>The part of the cost debited to finished goods (10503) by the close-out itself (the rest went through production receipts and ItemCost); relieved to cost of sales per delivered unit.</summary>
        public decimal FgAmount { get; set; }
        public int DeliveredQuantity { get; set; }
        public decimal RelievedAmount { get; set; }
        public bool IsOpen { get; set; }
        public int VoucherId { get; set; }
        public int? ReversalVoucherId { get; set; }
        public string ReopenReason { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public enum FnRevenueMethod { OnInvoice = 0, PercentOfCompletion = 1 }

    /// <summary>Revenue recognition setting of a contract. Contracts without a row recognise revenue on invoice.</summary>
    public class FnContractRevenue
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public FnRevenueMethod Method { get; set; }
        /// <summary>Contract price excluding VAT (rial); taken from the contract when null.</summary>
        public decimal? ContractPrice { get; set; }
        /// <summary>ProjectControl or Manual (the monthly run then needs the progress and EAC inputs).</summary>
        public string ProgressSource { get; set; } = "ProjectControl";
        /// <summary>CostToCost or Physical.</summary>
        public string ProgressMeasure { get; set; } = "Physical";
    }

    /// <summary>The cumulative position of one contract after a revenue-recognition run (reversal-safe cumulative method).</summary>
    public class FnRevenueRecognition
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public int ContractId { get; set; }
        public decimal ProgressPercent { get; set; }
        public decimal ContractPrice { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal EarnedToDate { get; set; }
        public decimal RecognisedBefore { get; set; }
        public decimal RevenueThisRun { get; set; }
        public decimal BilledToDate { get; set; }
        public decimal ContractAsset { get; set; }
        public decimal ContractLiability { get; set; }
        public decimal CostOfSales { get; set; }
        public decimal LossProvision { get; set; }
        public int VoucherId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ prepaid, accruals, provisions (section 5b)

    public enum FnDeferralKind { PrepaidExpense = 1, DeferredIncome = 2 }
    public enum FnDeferralStatus { Active = 1, Completed = 2, Cancelled = 3 }

    public class FnDeferral
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public FnDeferralKind Kind { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        /// <summary>The prepaid or deferred balance account.</summary>
        public int BalanceAccountId { get; set; }
        /// <summary>The expense or income account the amortisation goes to.</summary>
        public int ResultAccountId { get; set; }
        public int? CostCenterId { get; set; }
        public int? ContractId { get; set; }
        public int? GuaranteeId { get; set; }
        public FnDeferralStatus Status { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnDeferralEntry> Entries { get; set; }
    }

    /// <summary>One monthly amortisation: drafted by the run, approved by a second person.</summary>
    public class FnDeferralEntry
    {
        public int Id { get; set; }
        public int DeferralId { get; set; }
        public FnDeferral Deferral { get; set; }
        public int PeriodId { get; set; }
        public int Sequence { get; set; }
        public decimal Amount { get; set; }
        /// <summary>0 draft, 1 approved (voucher created).</summary>
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? VoucherId { get; set; }
    }

    public enum FnProvisionKind { Warranty = 1, Legal = 2, Bonus = 3, Leave = 4, Other = 9 }

    public class FnProvision
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnProvisionKind Kind { get; set; }
        public string Description { get; set; }
        public decimal Balance { get; set; }
        public DateTime? ReviewDate { get; set; }
        public bool IsActive { get; set; } = true;
        public int ExpenseAccountId { get; set; }
        public int LiabilityAccountId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnProvisionMovement> Movements { get; set; }
    }

    public class FnProvisionMovement
    {
        public int Id { get; set; }
        public int ProvisionId { get; set; }
        public FnProvision Provision { get; set; }
        public DateTime Date { get; set; }
        /// <summary>Positive = increase (charge), negative = use or release.</summary>
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public int VoucherId { get; set; }
        public int UserId { get; set; }
    }
}
