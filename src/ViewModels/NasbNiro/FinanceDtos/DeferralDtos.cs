using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnSaveDeferralDto
    {
        public FnDeferralKind Kind { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        /// <summary>The prepaid-expense or deferred-income account.</summary>
        public string BalanceAccountCode { get; set; }
        /// <summary>The expense or income account the monthly amount goes to.</summary>
        public string ResultAccountCode { get; set; }
        public string CostCenterCode { get; set; }
        public int? ContractId { get; set; }
    }

    public class FnDeferralEntryDto
    {
        public int Id { get; set; }
        public int DeferralId { get; set; }
        public int PeriodId { get; set; }
        public int Sequence { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? VoucherId { get; set; }
    }

    public class FnDeferralDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnDeferralKind Kind { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Months { get; set; }
        public string BalanceAccountCode { get; set; }
        public string ResultAccountCode { get; set; }
        public FnDeferralStatus Status { get; set; }
        public decimal Amortised { get; set; }
        public decimal Remaining { get; set; }
        public List<FnDeferralEntryDto> Entries { get; set; } = new List<FnDeferralEntryDto>();
    }

    public class FnSaveProvisionDto
    {
        public int? Id { get; set; }
        public FnProvisionKind Kind { get; set; }
        public string Description { get; set; }
        public DateTime? ReviewDate { get; set; }
        public string ExpenseAccountCode { get; set; }
        public string LiabilityAccountCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class FnProvisionMovementDto
    {
        public int? ProvisionId { get; set; }
        public DateTime Date { get; set; }
        /// <summary>Positive = increase (charge to expense), negative = use or release.</summary>
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        /// <summary>For a use: the account that is credited instead of the expense (for example the bank). Empty = release to the expense account.</summary>
        public string CounterAccountCode { get; set; }
        public int? VoucherId { get; set; }
    }

    public class FnProvisionDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnProvisionKind Kind { get; set; }
        public string Description { get; set; }
        public decimal Balance { get; set; }
        public DateTime? ReviewDate { get; set; }
        public bool ReviewDue { get; set; }
        public bool IsActive { get; set; }
        public string ExpenseAccountCode { get; set; }
        public string LiabilityAccountCode { get; set; }
        public List<FnProvisionMovementDto> Movements { get; set; } = new List<FnProvisionMovementDto>();
    }

    public class FnReversingRunDto
    {
        public int PeriodId { get; set; }
        public List<int> CreatedVoucherIds { get; set; } = new List<int>();
        public List<string> Skipped { get; set; } = new List<string>();
    }
}
