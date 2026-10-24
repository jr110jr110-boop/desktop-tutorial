using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnBudgetLineDto
    {
        public int? Id { get; set; }
        public string AccountCode { get; set; }
        public string CostCenterCode { get; set; }
        public int? ContractId { get; set; }
        public int Period { get; set; }
        public decimal Amount { get; set; }
        public bool IsCapital { get; set; }
        public string Note { get; set; }
    }

    public class FnBudgetDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int FiscalYearId { get; set; }
        public int Version { get; set; }
        public FnBudgetKind Kind { get; set; }
        public FnBudgetStatus Status { get; set; }
        public FnCommitMode CommitMode { get; set; }
        public string Title { get; set; }
        public decimal Total { get; set; }
        public int? ManagerApprovedByUserId { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public List<FnBudgetLineDto> Lines { get; set; } = new List<FnBudgetLineDto>();
    }

    public class FnSaveBudgetDto
    {
        public int? Id { get; set; }
        public int FiscalYearId { get; set; }
        public string Title { get; set; }
        public FnBudgetKind Kind { get; set; } = FnBudgetKind.Annual;
        public FnCommitMode CommitMode { get; set; } = FnCommitMode.Warn;
        public List<FnBudgetLineDto> Lines { get; set; } = new List<FnBudgetLineDto>();
    }

    public class FnBudgetImportDto
    {
        public int BudgetId { get; set; }
        /// <summary>Header: accountCode,costCenterCode,contractId,period,amount,isCapital,note. The existing lines are replaced.</summary>
        public string Csv { get; set; }
    }

    public class FnBudgetIdDto { public int Id { get; set; } }

    public class FnBudgetReportQuery
    {
        public int? BudgetId { get; set; }
        public int? FiscalYearId { get; set; }
        public int? FromPeriod { get; set; }
        public int? ToPeriod { get; set; }
        /// <summary>account (default), costcenter or contract.</summary>
        public string GroupBy { get; set; }
        /// <summary>Remaining (actual YTD plus the remaining budget) or RunRate.</summary>
        public string Forecast { get; set; }
    }

    public class FnBudgetReportRowDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Budget { get; set; }
        public decimal Actual { get; set; }
        public decimal Variance { get; set; }
        public decimal VariancePercent { get; set; }
        public decimal FullYearBudget { get; set; }
        public decimal Forecast { get; set; }
    }

    public class FnBudgetReportDto
    {
        public int BudgetId { get; set; }
        public int FromPeriod { get; set; }
        public int ToPeriod { get; set; }
        public string GroupBy { get; set; }
        public string ForecastMethod { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal TotalActual { get; set; }
        public decimal TotalVariance { get; set; }
        public decimal UnbudgetedActual { get; set; }
        public List<FnBudgetReportRowDto> Rows { get; set; } = new List<FnBudgetReportRowDto>();
    }

    public class FnCommitmentCheckRequest
    {
        public string AccountCode { get; set; }
        public string CostCenterCode { get; set; }
        public int? ContractId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? Date { get; set; }
        public string OverrideReason { get; set; }
        /// <summary>The document being approved is not counted among the open commitments.</summary>
        public int? ExcludePurchaseRequestId { get; set; }
        public int? ExcludePurchaseOrderId { get; set; }
    }

    public class FnCommitmentCheckDto
    {
        public FnCommitMode Mode { get; set; }
        public bool HasBudget { get; set; }
        public bool Allowed { get; set; }
        public bool Warning { get; set; }
        public bool Overridden { get; set; }
        public decimal BudgetToDate { get; set; }
        public decimal ActualToDate { get; set; }
        /// <summary>Approved purchase requests not yet ordered plus the not-yet-invoiced part of open purchase orders on the same cost centre / contract.</summary>
        public decimal OpenCommitments { get; set; }
        public decimal Remaining { get; set; }
        public decimal Amount { get; set; }
        public string Message { get; set; }
    }
}
