using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    public class PcCalendarExceptionDto
    {
        public DateTime Date { get; set; }
        public bool IsWorking { get; set; }
        public string Note { get; set; }
    }

    public class PcCalendarDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }

        /// <summary>Bit (1 &lt;&lt; DayOfWeek) is set for a working weekday (Sunday = bit 0).</summary>
        public int WorkingWeekdays { get; set; }
        public decimal HoursPerDay { get; set; } = 8;
        public bool UseCompanyHolidays { get; set; }
        public List<PcCalendarExceptionDto> Exceptions { get; set; } = new List<PcCalendarExceptionDto>();
    }

    public class PcWbsNodeDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int? ParentId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcWbsNodeType NodeType { get; set; }
        public PcPhase? Phase { get; set; }
        public int? WorkOrderId { get; set; }
        public int? AssemblyId { get; set; }
        public int? BomId { get; set; }
        public int? ProcessId { get; set; }
        public decimal Weight { get; set; }
        public decimal BudgetAmount { get; set; }
        public string Dictionary { get; set; }
        public string ResponsibleUnit { get; set; }
        public int Sort { get; set; }
        public bool IsGenerated { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcWbsQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }
    }

    public class PcWbsGenerateDto
    {
        public int ContractId { get; set; }

        /// <summary>Empty: all work orders of the contract.</summary>
        public List<int> WorkOrderIds { get; set; }
    }

    public class PcWbsGenerateResultDto
    {
        public int ContractId { get; set; }
        public int NodesCreated { get; set; }
        public int ActivitiesCreated { get; set; }
        public int DependenciesCreated { get; set; }
        public int MdrLinesDrafted { get; set; }
        public int WorkOrdersSkipped { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class PcActivityDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsCode { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcSourceType SourceType { get; set; } = PcSourceType.Manual;
        public int? SourceId { get; set; }
        public int? WorkOrderId { get; set; }
        public int DurationDays { get; set; }
        public int? CalendarId { get; set; }
        public PcPhase? Phase { get; set; }
        public string ResponsibleUnit { get; set; }
        public PcConstraint ConstraintType { get; set; }
        public DateTime? ConstraintDate { get; set; }

        public DateTime? EarlyStart { get; set; }
        public DateTime? EarlyFinish { get; set; }
        public DateTime? LateStart { get; set; }
        public DateTime? LateFinish { get; set; }
        public int? TotalFloat { get; set; }
        public int? FreeFloat { get; set; }
        public bool IsCritical { get; set; }
        public DateTime? ForecastStart { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }

        public PcProgressMethod ProgressMethod { get; set; }
        public decimal Weight { get; set; }
        public decimal BudgetAtCompletion { get; set; }
        public PcActivityStatus Status { get; set; }
        public string OnHoldReason { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public int? PurchaseRequestLineId { get; set; }
        public int? ItpActivityId { get; set; }
        public PcMilestoneEvent MilestoneEvent { get; set; }
        public decimal? PlannedQuantity { get; set; }
        public int? SerialItemId { get; set; }
        public DateTime? NeedByDate { get; set; }
        public int LeadTimeDays { get; set; }
        public bool IsPackaging { get; set; }
        public bool IsLate { get; set; }
        public string LateKind { get; set; }
        public decimal PercentCache { get; set; }
        public int Sort { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcActivityQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public PcActivityStatus? Status { get; set; }
        public PcPhase? Phase { get; set; }
        public string ResponsibleUnit { get; set; }
        public bool OnlyCritical { get; set; }
        public bool OnlyLate { get; set; }
    }

    public class PcActivityActionDto
    {
        public int Id { get; set; }

        /// <summary>start, finish, hold, resume</summary>
        public string Action { get; set; }
        public DateTime? Date { get; set; }
        public string Reason { get; set; }
    }

    public class PcDependencyDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int PredecessorId { get; set; }
        public string PredecessorCode { get; set; }
        public int SuccessorId { get; set; }
        public string SuccessorCode { get; set; }
        public PcDependencyType Type { get; set; } = PcDependencyType.FS;
        public int LagDays { get; set; }
        public PcDependencyOrigin Origin { get; set; } = PcDependencyOrigin.Manual;
    }

    public class PcCpmRunRequestDto
    {
        public int ContractId { get; set; }

        /// <summary>false: plan run (ignores progress); true: forecast with actuals, remaining durations and PO delivery dates.</summary>
        public bool Forecast { get; set; }

        /// <summary>Plan run only: write the computed dates to the legacy process / assembly / work order dates.</summary>
        public bool Mirror { get; set; } = true;
        public DateTime? DataDate { get; set; }
    }

    public class PcCpmItemDto
    {
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public DateTime EarlyStart { get; set; }
        public DateTime EarlyFinish { get; set; }
        public DateTime LateStart { get; set; }
        public DateTime LateFinish { get; set; }
        public int TotalFloat { get; set; }
        public int FreeFloat { get; set; }
        public bool Critical { get; set; }
    }

    public class PcCpmRunDto
    {
        public int ContractId { get; set; }
        public bool Forecast { get; set; }
        public bool HasCycle { get; set; }
        public List<string> CycleCodes { get; set; } = new List<string>();
        public DateTime? ProjectStart { get; set; }
        public DateTime? ProjectFinish { get; set; }
        public int ProjectFinishOffset { get; set; }
        public List<string> CriticalPath { get; set; } = new List<string>();
        public List<PcCpmItemDto> Items { get; set; } = new List<PcCpmItemDto>();
        public int Mirrored { get; set; }
        public string Message { get; set; }
    }

    public class PcExecutivePlanQuery : PcContractQuery
    {
        /// <summary>First day of the window (default: today).</summary>
        public DateTime? From { get; set; }

        /// <summary>Window length in days (default 14).</summary>
        public int Days { get; set; } = 14;
        public string ResponsibleUnit { get; set; }
    }

    public class PcExecutivePlanRowDto
    {
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string ResponsibleUnit { get; set; }
        public PcPhase? Phase { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? Finish { get; set; }
        public decimal Percent { get; set; }
        public PcActivityStatus Status { get; set; }
        public bool IsCritical { get; set; }
        public bool IsLate { get; set; }
        public int? TotalFloat { get; set; }
    }

    public class PcNeedByRowDto
    {
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public DateTime? LateStart { get; set; }
        public int LeadTimeDays { get; set; }
        public DateTime? NeedByDate { get; set; }
        public int? PurchaseRequestLineId { get; set; }
        public bool AppliedToRequest { get; set; }
        public bool LongLead { get; set; }
    }

    public class PcNeedByApplyDto
    {
        public int ContractId { get; set; }
    }

    public class PcItpGenerateDto
    {
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }
    }

    public class PcNoticeDraftDto
    {
        public int ItpId { get; set; }
        public string ItpNumber { get; set; }
        public int ItpActivityId { get; set; }
        public int? ActivityId { get; set; }
        public int WorkOrderId { get; set; }
        public string Stage { get; set; }
        public string Activity { get; set; }
        public string Party { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? NoticeDeadline { get; set; }
        public bool NoticeLate { get; set; }
        public string Text { get; set; }
    }

    public class PcNoticeQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }
        public int? LookAheadDays { get; set; }
    }
}
