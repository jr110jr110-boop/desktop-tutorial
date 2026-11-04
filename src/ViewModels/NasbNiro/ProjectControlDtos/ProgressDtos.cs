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

        /// <summary>R1c (H4): the day the percent was reached. Default today; never in the future and never before the last weekly snapshot or the previous approved progress of the activity.</summary>
        public DateTime? ProgressDate { get; set; }
    }

    public class PcManualProgressDto
    {
        public int Id { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public decimal Percent { get; set; }
        public string Comment { get; set; }

        /// <summary>R1c (H4): the day the percent was reached (the day of the approval when none was given).</summary>
        public DateTime? ProgressDate { get; set; }
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

        /// <summary>R1c (M9): the finish forecast of the production scheduler (MES), kept apart from <see cref="ForecastFinish"/> (the project-control forecast).</summary>
        public DateTime? ProductionForecastFinish { get; set; }

        /// <summary>R1c (M4): true when PV was read from the rows frozen at the approval of the baseline; false for a baseline of before R1c (calculated with today's calendar until the nightly job freezes it).</summary>
        public bool PvFrozen { get; set; }

        /// <summary>R1c (H4): true when the percents come from the dated progress events of a past data date.</summary>
        public bool FromEvents { get; set; }
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

        // R1c (M5)
        /// <summary>True for an entry written by hand in project control; it counts as actual cost only after a second FINANCIAL person checked it.</summary>
        public bool NeedsCheck { get; set; }
        public bool Checked { get; set; }
        public int? CheckedByUserId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }

        /// <summary>The person who wrote the entry.</summary>
        public int? CreatedByUserId { get; set; }

        /// <summary>True when the entry is not counted in actual cost: an after-sales cost, or a manual entry nobody checked yet.</summary>
        public bool ExcludedFromActual { get; set; }
    }

    /// <summary>R1c (M5): the second-person check of a manual cost entry.</summary>
    public class PcCostCheckDto
    {
        public int CostEntryId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>R1c (H4): a request to take a weekly snapshot for a data date in the past (the planning unit asks, the PMO deputy approves).</summary>
    public class PcPastSnapshotRequestDto
    {
        public int ContractId { get; set; }
        public DateTime DataDate { get; set; }
        public string Reason { get; set; }
    }

    public class PcPastSnapshotDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public DateTime DataDate { get; set; }
        public string Reason { get; set; }

        /// <summary>0 = requested, 1 = approved (not used yet), 2 = rejected, 3 = used (the snapshot was taken).</summary>
        public PcSnapshotRequestState State { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public int? SnapshotId { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcPastSnapshotDecisionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class PcPastSnapshotQuery : PcContractQuery
    {
        public PcSnapshotRequestState? State { get; set; }
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

        // P-B7a (appendix 5): the extended site report
        public string ReportDateText { get; set; }
        public string ConfirmedByName { get; set; }
        public string ConfirmedAtText { get; set; }
        public string CreatorName { get; set; }
        public string Weather { get; set; }
        public string Notes { get; set; }
        public List<PcDailyManpowerDto> Manpower { get; set; } = new List<PcDailyManpowerDto>();
        public List<PcDailyMaterialDto> Materials { get; set; } = new List<PcDailyMaterialDto>();
        public List<PcDailyMachineryDto> Machinery { get; set; } = new List<PcDailyMachineryDto>();
        public List<PcDailyObstacleDto> Obstacles { get; set; } = new List<PcDailyObstacleDto>();
        public List<PcDailyHseDto> HseEvents { get; set; } = new List<PcDailyHseDto>();

        /// <summary>The caller may confirm this report (a person who wrote or edited it may not).</summary>
        public bool CanConfirm { get; set; }
    }
}
