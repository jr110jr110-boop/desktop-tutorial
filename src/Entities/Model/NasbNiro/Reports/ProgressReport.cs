using System;

using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.Reports
{
    public class ProgressReport : BaseEntity
    {
        public int ProcessId { get; set; }
        public ProcessEntity Process { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }
        public DateTimeOffset? PlannedStartDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? PlannedEndDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public DateTimeOffset ReportDate { get; set; }
        public decimal PlannedPrice { get; set; }
        public decimal? ActualPrice { get; set; }
        public decimal ContractLevelPlannedProgress { get; set; }
        public decimal ContractLevelActualProgress { get; set; }
        public decimal WorkOrderLevelPlannedProgress { get; set; }
        public decimal WorkOrderLevelActualProgress { get; set; }
        public decimal PlannedWeightPercent { get; set; }
        public decimal ActualWeightPercent { get; set; }
        public string ProcessTitle { get; set; }
        public string WorkOrderTitle { get; set; }
        public int WorkOrderRemainingDays { get; set; }

        public int ContractRemainingDays { get; set; }
        public DateTimeOffset? ContractPlannedStartDate { get; set; }
        public DateTimeOffset? ContractActualStartDate { get; set; }
        public DateTimeOffset? ContractPlannedEndDate { get; set; }
        public DateTimeOffset? ContractActualEndtDate { get; set; }
        public decimal? ContractPlannedWeightPercent { get; set; }
        public decimal? ContractActualWeightPercent { get; set; }
        public decimal? ContractPlannedPrice { get; set; }
        public decimal? ContractActualPrice { get; set; }
    }
}
