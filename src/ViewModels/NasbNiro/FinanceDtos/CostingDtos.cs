using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnOverheadRateDto
    {
        public int? Id { get; set; }
        public string CostCenterCode { get; set; }
        public FnOverheadBase Base { get; set; }
        public decimal Rate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; } = true;
    }

    public class FnAllocationKeyDto
    {
        public int? Id { get; set; }
        public string FromCostCenterCode { get; set; }
        public string ToCostCenterCode { get; set; }
        public string Basis { get; set; }
        public decimal Value { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public int Sequence { get; set; }
        public string Source { get; set; }
    }

    public class FnWorkOrderBaseDto
    {
        public int? Id { get; set; }
        public int WorkOrderId { get; set; }
        public string CostCenterCode { get; set; }
        public int PeriodId { get; set; }
        public FnOverheadBase Base { get; set; }
        public decimal Quantity { get; set; }
    }

    public class FnAllocateRequest { public bool DryRun { get; set; } }

    public class FnAllocationStepDto { public string From { get; set; } public string To { get; set; } public decimal Amount { get; set; } }

    public class FnAbsorptionDto
    {
        public int WorkOrderId { get; set; }
        public string CostCenterCode { get; set; }
        public FnOverheadBase Base { get; set; }
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
    }

    public class FnCenterResultDto
    {
        public string CostCenterCode { get; set; }
        public bool IsProduction { get; set; }
        public decimal OwnCost { get; set; }
        public decimal Received { get; set; }
        public decimal Allocated { get; set; }
        public decimal Actual { get; set; }
        public decimal Absorbed { get; set; }
        public decimal Variance { get; set; }
    }

    public class FnAllocationResultDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int PeriodId { get; set; }
        public bool DryRun { get; set; }
        public decimal TotalAllocated { get; set; }
        public decimal TotalAbsorbed { get; set; }
        /// <summary>Actual overhead of the producing cost centres minus absorbed; positive = under-absorbed.</summary>
        public decimal Variance { get; set; }
        public int? AllocationVoucherId { get; set; }
        public int? AbsorptionVoucherId { get; set; }
        public int? VarianceVoucherId { get; set; }
        public List<FnAllocationStepDto> Steps { get; set; } = new List<FnAllocationStepDto>();
        public List<FnAbsorptionDto> Absorptions { get; set; } = new List<FnAbsorptionDto>();
        public List<FnCenterResultDto> Centers { get; set; } = new List<FnCenterResultDto>();
    }

    public class FnWorkOrderCloseRequest
    {
        public int? Quantity { get; set; }
        public DateTime? Date { get; set; }
        public string Reason { get; set; }
    }

    public class FnWorkOrderCostDto
    {
        public int WorkOrderId { get; set; }
        public string Code { get; set; }
        public int Quantity { get; set; }
        public decimal Material { get; set; }

        /// <summary>
        /// R1p D1: null (with <see cref="LabourHidden"/>) when a labour entry of the work order is a protected amount (a row of the earlier release, costed at a person's own rate with fewer than three payroll-rated employees
        /// behind it) and the reader is not PAYROLL, FINANCEMANAGER or CEO. <see cref="TotalCost"/>, <see cref="UnitCost"/>, <see cref="WipBalance"/>, <see cref="LabourInLedger"/> and <see cref="TransferredOut"/> carry the labour
        /// as well (total = WIP + transferred) and are hidden with it.
        /// </summary>
        public decimal? Labour { get; set; }

        public decimal Overhead { get; set; }
        public decimal Subcontract { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? UnitCost { get; set; }
        public decimal? WipBalance { get; set; }
        public decimal? LabourInLedger { get; set; }
        public decimal? TransferredOut { get; set; }
        public bool IsClosed { get; set; }
        public bool LabourHidden { get; set; }
    }

    public class FnWorkOrderCloseDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public decimal Material { get; set; }
        public decimal Labour { get; set; }
        public decimal Overhead { get; set; }
        public decimal Subcontract { get; set; }
        public decimal TotalCost { get; set; }
        public decimal ToFinishedGoods { get; set; }
        public decimal Variance { get; set; }
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public bool IsOpen { get; set; }
        public int VoucherId { get; set; }
        public int? ReversalVoucherId { get; set; }
    }

    public class FnStandardCostDto
    {
        public int? Id { get; set; }
        public int ItemId { get; set; }
        public decimal Cost { get; set; }
        public DateTime ValidFrom { get; set; }
        public string Source { get; set; }
    }

    public class FnContractRevenueDto
    {
        public int ContractId { get; set; }
        public FnRevenueMethod Method { get; set; }
        public decimal? ContractPrice { get; set; }
        public string ProgressSource { get; set; } = "ProjectControl";
        public string ProgressMeasure { get; set; } = "Physical";
    }

    public class FnRevenueInputDto
    {
        public int ContractId { get; set; }
        public decimal? ProgressPercent { get; set; }
        public decimal? EstimatedCost { get; set; }
    }

    public class FnRevenueRunRequest
    {
        public bool DryRun { get; set; }
        public List<FnRevenueInputDto> Inputs { get; set; } = new List<FnRevenueInputDto>();
    }

    public class FnRevenueRowDto
    {
        public int ContractId { get; set; }
        public decimal CostOfSalesThisRun { get; set; }
        public decimal ProgressPercent { get; set; }
        public decimal ContractPrice { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal EarnedToDate { get; set; }
        public decimal RevenueInLedger { get; set; }
        public decimal RevenueThisRun { get; set; }
        public decimal ContractAsset { get; set; }
        public decimal ContractLiability { get; set; }
        public decimal LossProvision { get; set; }
        public decimal LossProvisionChange { get; set; }
    }

    public class FnRevenueRunDto
    {
        public int PeriodId { get; set; }
        public bool DryRun { get; set; }
        public string Number { get; set; }
        public int? VoucherId { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCostOfSales { get; set; }
        public List<FnRevenueRowDto> Rows { get; set; } = new List<FnRevenueRowDto>();
    }
}
