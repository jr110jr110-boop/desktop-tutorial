using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    public class PcManualProgressSaveDto
    {
        public int ActivityId { get; set; }
        public decimal Percent { get; set; }
        public string Comment { get; set; }
    }

    public class PcManualProgressDto
    {
        public int Id { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public decimal Percent { get; set; }
        public string Comment { get; set; }
        public int ProposedByUserId { get; set; }
        public string ProposedByName { get; set; }
        public DateTimeOffset ProposedAt { get; set; }

        /// <summary>0 = proposed, 1 = approved, 2 = rejected.</summary>
        public int State { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    public class PcManualDecisionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class PcManualQuery : PcContractQuery
    {
        public int? ActivityId { get; set; }
        public int? State { get; set; }
    }

    public class PcPhaseProgressDto
    {
        public PcPhase? Phase { get; set; }
        public decimal Percent { get; set; }
        public decimal PlannedPercent { get; set; }
        public int Activities { get; set; }
    }

    public class PcWorkOrderProgressDto
    {
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public decimal Percent { get; set; }
        public int Activities { get; set; }
    }

    public class PcProgressDto
    {
        public int ContractId { get; set; }
        public DateTime AsOf { get; set; }
        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public int Activities { get; set; }
        public int DoneActivities { get; set; }
        public int LateActivities { get; set; }
        public List<PcPhaseProgressDto> ByPhase { get; set; } = new List<PcPhaseProgressDto>();
        public List<PcWorkOrderProgressDto> ByWorkOrder { get; set; } = new List<PcWorkOrderProgressDto>();
    }

    public class PcEvmDto
    {
        public int ContractId { get; set; }
        public DateTime AsOf { get; set; }
        public int? BaselineId { get; set; }
        public bool FromBaseline { get; set; }
        public decimal Bac { get; set; }
        public decimal ManagementReserve { get; set; }
        public decimal Pv { get; set; }
        public decimal Ev { get; set; }
        public decimal Ac { get; set; }
        public decimal Cv { get; set; }
        public decimal Sv { get; set; }
        public decimal? Cpi { get; set; }
        public decimal? Spi { get; set; }

        // earned schedule (working days)
        public decimal? EarnedScheduleDays { get; set; }
        public int ActualTimeDays { get; set; }
        public decimal? ScheduleVarianceDays { get; set; }
        public decimal? SpiTime { get; set; }

        public decimal? EacTypical { get; set; }
        public decimal? EacAtypical { get; set; }
        public decimal? EacCpiSpi { get; set; }
        public decimal? EtcTypical { get; set; }
        public decimal? VacTypical { get; set; }
        public decimal? Tcpi { get; set; }
        public decimal? TcpiEac { get; set; }

        /// <summary>green, amber, red, none</summary>
        public string SpiStatus { get; set; }
        public string CpiStatus { get; set; }
        public bool CorrectiveActionRequired { get; set; }
        public bool RescheduleRequired { get; set; }
        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public DateTime? BaselineFinish { get; set; }
        public string Note { get; set; }
    }

    public class PcCashFlowPointDto
    {
        public DateTime Date { get; set; }
        public decimal PlannedCumulative { get; set; }
        public decimal ActualCostCumulative { get; set; }
        public decimal? EarnedValue { get; set; }
    }

    public class PcCostEntrySaveDto
    {
        public int ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public CostKind Kind { get; set; }
        public DateTime EntryDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
    }

    public class PcCostEntryDto
    {
        public int Id { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public CostKind Kind { get; set; }
        public DateTime EntryDate { get; set; }

        /// <summary>Null (with <see cref="LabourHidden"/>) for a protected labour amount seen by a reader who is not PAYROLL, FINANCEMANAGER or CEO (R1p D1).</summary>
        public decimal? Amount { get; set; }

        /// <summary>R1p D1: the entry is a labour amount that could show one person's pay (a quarter-end variance line whose pool did not pass the publication gate, or a row of the earlier release with one or two payroll-rated people behind it).</summary>
        public bool LabourHidden { get; set; }

        public string SourceType { get; set; }
        public string SourceNumber { get; set; }
        public string Description { get; set; }
    }

    public class PcSnapshotRequestDto
    {
        public int ContractId { get; set; }

        /// <summary>Default: the latest data-date weekday on or before today.</summary>
        public DateTime? DataDate { get; set; }
    }

    public class PcSnapshotRunDto
    {
        /// <summary>Test and catch-up: act as if this were the current time.</summary>
        public DateTime? AsOf { get; set; }
    }

    public class PcSnapshotRunResultDto
    {
        public int ContractsChecked { get; set; }
        public int SnapshotsTaken { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class PcSnapshotLineDto
    {
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public decimal Percent { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal Budget { get; set; }
        public decimal Ev { get; set; }
        public decimal Pv { get; set; }
        public DateTime? ForecastFinish { get; set; }
    }

    public class PcSnapshotDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public DateTime DataDate { get; set; }
        public DateTimeOffset TakenAt { get; set; }
        public int TakenByUserId { get; set; }
        public int? BaselineId { get; set; }
        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal Bac { get; set; }
        public decimal Pv { get; set; }
        public decimal Ev { get; set; }
        public decimal Ac { get; set; }
        public decimal? Cpi { get; set; }
        public decimal? Spi { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public string Hash { get; set; }
        public List<PcSnapshotLineDto> Lines { get; set; } = new List<PcSnapshotLineDto>();
    }

    public class PcSnapshotQuery : PcContractQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class PcDailyQuery
    {
        public int ContractId { get; set; }
        public string Unit { get; set; }
        public DateTime? Date { get; set; }
    }

    public class PcDailyLineDto
    {
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public string EventText { get; set; }
        public decimal Percent { get; set; }
        public bool IsLate { get; set; }
        public string DelayReasonCode { get; set; }
        public string DelayReason { get; set; }
        public string PreventiveAction { get; set; }
        public string CorrectiveAction { get; set; }
    }

    public class PcDailyReportDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public string Unit { get; set; }
        public DateTime ReportDate { get; set; }

        /// <summary>0 = draft (pre-filled), 1 = confirmed.</summary>
        public int State { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public List<PcDailyLineDto> Lines { get; set; } = new List<PcDailyLineDto>();
    }
}
