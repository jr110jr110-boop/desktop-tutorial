using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production / shop floor (MES), release P1 part B: daily cost posting, scheduling and load, maintenance, KPIs and dashboards.
    // Dates are ISO in JSON; every ...Text field is the Persian (Shamsi) date for people.

    // ------------------------------------------------------------------------------------------ daily cost posting

    public class RunCostDto
    {
        /// <summary>The booking day to post (the closed bookings that started that day). Empty: every unposted closed booking of the days before today.</summary>
        public DateTime? Date { get; set; }
    }

    public class CostRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime PostingDate { get; set; }
        public string PostingDateText { get; set; }
        public string RunAtText { get; set; }
        public int BookingCount { get; set; }
        public decimal LabourHours { get; set; }
        public decimal MachineHours { get; set; }

        /// <summary>Null (with <see cref="LabourHidden"/>) when the amount would reveal one person's payroll cost to a role that may not see it.</summary>
        public decimal? LabourAmount { get; set; }
        public bool LabourHidden { get; set; }
        public decimal OverheadAmount { get; set; }
        public int? AccountingEventId { get; set; }
    }

    public class CostRunResultDto
    {
        public List<CostRunDto> Runs { get; set; } = new List<CostRunDto>();
        public int BookingsPosted { get; set; }

        /// <summary>Bookings left unposted because a rate is missing (see Errors).</summary>
        public int BookingsSkipped { get; set; }

        /// <summary>Bookings without hours: nothing to cost, marked as handled.</summary>
        public int ZeroHourBookings { get; set; }

        /// <summary>Bookings whose labour rate came from the last approved payroll month of the employee.</summary>
        public int LabourRateFromPayroll { get; set; }

        /// <summary>Bookings whose labour rate came from the standard labour rate of the work centre (no payroll exists for the employee yet).</summary>
        public int LabourRateFromWorkCenter { get; set; }

        /// <summary>Null (with <see cref="LabourHidden"/>) when a posting is too small to show without revealing one person's payroll cost to this role.</summary>
        public decimal? LabourAmount { get; set; }
        public bool LabourHidden { get; set; }
        public decimal OverheadAmount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string Message { get; set; }
    }

    public class CostRunQuery : MfPageQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class CostPostingQuery : MfPageQuery
    {
        public int? RunId { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class CostPostingDto
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public string RunNumber { get; set; }
        public DateTime PostingDate { get; set; }
        public string PostingDateText { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsCode { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public int BookingCount { get; set; }
        public decimal LabourHours { get; set; }
        public decimal MachineHours { get; set; }

        /// <summary>Null (with <see cref="LabourHidden"/>) when fewer than three employees paid by payroll are in the row and the caller is not PAYROLL, FINANCEMANAGER or CEO.</summary>
        public decimal? LabourAmount { get; set; }
        public bool LabourHidden { get; set; }
        public decimal OverheadAmount { get; set; }
    }

    // ------------------------------------------------------------------------------------------ scheduling

    public class RunScheduleDto
    {
        /// <summary>"Today" of the run (default: the server date). Used by the tests and to plan from a chosen day.</summary>
        public DateTime? AsOf { get; set; }
    }

    public class ScheduledOperationDto
    {
        public int OperationId { get; set; }
        public string JobCard { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public MfOperationStatus Status { get; set; }

        /// <summary>The hours the scheduler placed (what is left of the planned hours).</summary>
        public decimal Hours { get; set; }

        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public string Note { get; set; }
    }

    public class ScheduledOrderDto
    {
        public int OrderId { get; set; }
        public string Number { get; set; }
        public int? Priority { get; set; }
        public DateTime? NeedDate { get; set; }
        public string NeedDateText { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public bool IsLate { get; set; }
        public int DaysLate { get; set; }
        public DateTime? KitReadyDate { get; set; }
        public string KitReadyDateText { get; set; }
        public bool KitShortage { get; set; }
        public string Note { get; set; }
        public List<ScheduledOperationDto> Operations { get; set; } = new List<ScheduledOperationDto>();
    }

    public class ScheduleResultDto
    {
        public int Id { get; set; }
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }
        public int Orders { get; set; }
        public int Operations { get; set; }
        public int LateOrders { get; set; }
        public int Unscheduled { get; set; }
        public int ForecastsPushed { get; set; }
        public List<ScheduledOrderDto> Items { get; set; } = new List<ScheduledOrderDto>();
    }

    public class LoadQuery
    {
        /// <summary>The first day (default: today). The weeks start on Saturday.</summary>
        public DateTime? From { get; set; }

        public int Weeks { get; set; } = 12;
        public int? WorkCenterId { get; set; }

        /// <summary>Percent above which a week counts as overloaded (default 100).</summary>
        public decimal Threshold { get; set; } = 100;
    }

    public class LoadCellDto
    {
        public int WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public DateTime WeekStart { get; set; }
        public string WeekStartText { get; set; }

        /// <summary>Available hours of the week from the first day counted: shifts x hours x resources x efficiency x utilisation, less maintenance downtime, on working days.</summary>
        public decimal CapacityHours { get; set; }

        /// <summary>Hours the open orders need in the week when every operation starts at its earliest date (no capacity limit): the demand.</summary>
        public decimal LoadHours { get; set; }

        /// <summary>Hours the finite-capacity plan actually put into the week.</summary>
        public decimal ScheduledHours { get; set; }

        /// <summary>Load / capacity in percent; empty when there is no capacity but there is load.</summary>
        public decimal? LoadPercent { get; set; }

        public bool Overloaded { get; set; }
    }

    public class LoadRowDto
    {
        public int WorkCenterId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public List<LoadCellDto> Cells { get; set; } = new List<LoadCellDto>();
    }

    public class LoadReportDto
    {
        public DateTime From { get; set; }
        public string FromText { get; set; }
        public int Weeks { get; set; }
        public decimal Threshold { get; set; }
        public List<LoadRowDto> Rows { get; set; } = new List<LoadRowDto>();

        /// <summary>The cells above the threshold, worst first.</summary>
        public List<LoadCellDto> Overloads { get; set; } = new List<LoadCellDto>();
    }

    public class DispatchQuery : MfPageQuery
    {
        public int WorkCenterId { get; set; }
    }

    public class DispatchRowDto
    {
        public int OperationId { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public string JobCard { get; set; }
        public int? Priority { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public MfOperationStatus Status { get; set; }

        /// <summary>Ready (can be started now), Running (started) or Next (waits for the operation before it).</summary>
        public string Stage { get; set; }

        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal SetupHoursPlanned { get; set; }
        public decimal RunHoursPlanned { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public string NeedDateText { get; set; }
        public bool OrderLate { get; set; }
    }

    // ------------------------------------------------------------------------------------------ maintenance

    public class MaintenancePlanQuery : MfPageQuery
    {
        public int? MachineId { get; set; }
        public bool? IsActive { get; set; }
    }

    public class MaintenancePlanDto
    {
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string Task { get; set; }
        public MfMaintenanceIntervalKind IntervalKind { get; set; }
        public decimal IntervalValue { get; set; }
        public DateTime StartDate { get; set; }
        public string StartDateText { get; set; }
        public DateTime? LastDoneAt { get; set; }
        public string LastDoneAtText { get; set; }
        public DateTime? NextDueDate { get; set; }
        public string NextDueDateText { get; set; }

        /// <summary>Running hours booked on the machine since the last service (running-hours plans).</summary>
        public decimal? RunningHoursSince { get; set; }

        public string Checklist { get; set; }
        public decimal EstimatedHours { get; set; }
        public bool IsActive { get; set; }

        /// <summary>An order drafted from this plan is still open.</summary>
        public int? OpenOrderId { get; set; }

        public string RowVersion { get; set; }
    }

    public class SaveMaintenancePlanDto
    {
        public int? Id { get; set; }
        public int MachineId { get; set; }
        public string Task { get; set; }
        public MfMaintenanceIntervalKind IntervalKind { get; set; } = MfMaintenanceIntervalKind.Days;
        public decimal IntervalValue { get; set; }

        /// <summary>The day the plan counts from until the first service is done (default: today).</summary>
        public DateTime? StartDate { get; set; }

        public string Checklist { get; set; }
        public decimal EstimatedHours { get; set; }
        public bool IsActive { get; set; } = true;
        public string RowVersion { get; set; }
    }

    public class DraftDueDto
    {
        /// <summary>"Today" of the check (default: the server date).</summary>
        public DateTime? AsOf { get; set; }
    }

    public class DraftDueResultDto
    {
        public DateTime AsOf { get; set; }
        public int PlansChecked { get; set; }
        public int Drafted { get; set; }
        public List<MaintenanceOrderListDto> Orders { get; set; } = new List<MaintenanceOrderListDto>();
    }

    public class MaintenanceOrderQuery : MfPageQuery
    {
        public int? MachineId { get; set; }
        public MfMaintenanceKind? Kind { get; set; }
        public MfMaintenanceOrderStatus? Status { get; set; }
        public bool? OpenOnly { get; set; }
    }

    public class MaintenanceOrderListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? WorkCenterId { get; set; }
        public MfMaintenanceKind Kind { get; set; }
        public MfMaintenanceOrderStatus Status { get; set; }
        public int? PlanId { get; set; }
        public int? RequestId { get; set; }
        public string Task { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public decimal EstimatedHours { get; set; }
        public DateTime? DowntimeStart { get; set; }
        public string DowntimeStartText { get; set; }
        public DateTime? DowntimeEnd { get; set; }
        public string DowntimeEndText { get; set; }
        public string CostCenter { get; set; }
    }

    public class MaintenanceLabourDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime WorkDate { get; set; }
        public string WorkDateText { get; set; }
        public decimal Hours { get; set; }
        public string Note { get; set; }
    }

    public class MaintenancePartDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    public class MaintenanceOrderDto : MaintenanceOrderListDto
    {
        public string Checklist { get; set; }
        public string Cause { get; set; }
        public string ActionTaken { get; set; }
        public int? MaterialRequestId { get; set; }
        public string MaterialRequestNumber { get; set; }
        public int? IssueDocumentId { get; set; }
        public string IssueDocumentNumber { get; set; }
        public string StartedAtText { get; set; }
        public string DoneAtText { get; set; }
        public string CancelReason { get; set; }

        /// <summary>Hours of downtime (end less start, in hours) when both are known.</summary>
        public decimal? DowntimeHours { get; set; }

        public decimal LabourHours { get; set; }
        public List<MaintenanceLabourDto> Labour { get; set; } = new List<MaintenanceLabourDto>();
        public List<MaintenancePartDto> Parts { get; set; } = new List<MaintenancePartDto>();
        public string RowVersion { get; set; }
    }

    public class SaveMaintenancePartDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Creates a maintenance order by hand (the system drafts planned orders from the plans and breakdown orders from the machine-down reports).</summary>
    public class CreateMaintenanceOrderDto
    {
        public int MachineId { get; set; }
        public MfMaintenanceKind Kind { get; set; } = MfMaintenanceKind.Planned;
        public string Task { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal EstimatedHours { get; set; }
        public string Checklist { get; set; }
        public string CostCenter { get; set; }

        /// <summary>Make the order from a breakdown report (kind breakdown).</summary>
        public int? RequestId { get; set; }

        /// <summary>Breakdown: when the machine stopped (default: the report time or now).</summary>
        public DateTime? DowntimeStart { get; set; }
    }

    public class SaveMaintenanceOrderDto
    {
        public int Id { get; set; }
        public string Task { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal? EstimatedHours { get; set; }
        public string Checklist { get; set; }
        public string CostCenter { get; set; }
        public DateTime? DowntimeStart { get; set; }
        public DateTime? DowntimeEnd { get; set; }
        public string Cause { get; set; }
        public string ActionTaken { get; set; }

        /// <summary>The spare parts needed (replaces the list while the order is open).</summary>
        public List<SaveMaintenancePartDto> Parts { get; set; }

        public string RowVersion { get; set; }
    }

    /// <summary>Actions: start, complete, cancel, request-parts (draft the material request of the spare parts), draft-issue (draft the issue against the approved request).</summary>
    public class MaintenanceOrderActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public DateTime? DowntimeEnd { get; set; }
        public string RowVersion { get; set; }
    }

    public class AddMaintenanceLabourDto
    {
        public int OrderId { get; set; }

        /// <summary>The personnel record (default: the record linked to the signed-in user).</summary>
        public int? EmployeeId { get; set; }

        public DateTime? WorkDate { get; set; }
        public decimal Hours { get; set; }
        public string Note { get; set; }
    }

    public class KpiQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int? WorkCenterId { get; set; }
        public int? MachineId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class MachineKpiDto
    {
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal PlannedHours { get; set; }
        public decimal DowntimeHours { get; set; }
        public decimal? AvailabilityPercent { get; set; }
        public int Breakdowns { get; set; }

        /// <summary>Mean time between failures: operating hours / breakdowns (empty without a breakdown).</summary>
        public decimal? MtbfHours { get; set; }

        /// <summary>Mean time to repair: repair hours / finished breakdowns.</summary>
        public decimal? MttrHours { get; set; }
    }

    public class OeeDto
    {
        public int WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public string WorkCenterTitle { get; set; }
        public decimal PlannedHours { get; set; }
        public decimal DowntimeHours { get; set; }
        public decimal BookedRunHours { get; set; }

        /// <summary>Standard hours earned by the pieces booked (standard run hours per piece x good and scrap pieces).</summary>
        public decimal StandardHours { get; set; }

        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public decimal AvailabilityPercent { get; set; }
        public decimal? PerformancePercent { get; set; }
        public decimal? QualityPercent { get; set; }

        /// <summary>Availability x performance x quality (empty when there is no run time or no piece).</summary>
        public decimal? OeePercent { get; set; }
    }

    // ------------------------------------------------------------------------------------------ dashboard

    public class DashboardQuery
    {
        /// <summary>"Today" of the figures (default: the server date).</summary>
        public DateTime? AsOf { get; set; }
    }

    public class DashboardOrderDto
    {
        public int OrderId { get; set; }
        public string Number { get; set; }
        public string ItemCode { get; set; }
        public int DaysLate { get; set; }
        public string NeedDateText { get; set; }
        public string PlannedFinishText { get; set; }
    }

    public class DashboardOperationDto
    {
        public int OperationId { get; set; }
        public string JobCard { get; set; }
        public string Title { get; set; }
        public int DaysOnHold { get; set; }
        public string RequestedAtText { get; set; }
    }

    public class DashboardMachineDto
    {
        public int MachineId { get; set; }
        public string Title { get; set; }
        public string WorkCenterCode { get; set; }
        public string Status { get; set; }
    }

    public class ProductionDashboardDto
    {
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }

        /// <summary>Open orders whose planned finish is after their need date (from the last scheduler run).</summary>
        public int LateOrders { get; set; }

        public List<DashboardOrderDto> LateOrderList { get; set; } = new List<DashboardOrderDto>();

        /// <summary>Operations waiting for inspection for more than two days.</summary>
        public int QcHoldOverTwoDays { get; set; }

        public List<DashboardOperationDto> QcHoldList { get; set; } = new List<DashboardOperationDto>();

        /// <summary>Work-centre weeks above 100 % in the next 12 weeks.</summary>
        public int OverloadWeeks { get; set; }

        public List<LoadCellDto> OverloadList { get; set; } = new List<LoadCellDto>();

        /// <summary>Released orders whose kit is not issued and the stock does not cover it.</summary>
        public int KitShortages { get; set; }

        public List<DashboardOrderDto> KitShortageList { get; set; } = new List<DashboardOrderDto>();
        public int MachinesDown { get; set; }
        public List<DashboardMachineDto> MachinesDownList { get; set; } = new List<DashboardMachineDto>();

        /// <summary>Open maintenance orders past their due date.</summary>
        public int MaintenanceOverdue { get; set; }

        public decimal MonthGoodQty { get; set; }
        public decimal MonthScrapQty { get; set; }

        /// <summary>Scrap / (good + scrap) of the bookings of the current Persian month, in percent.</summary>
        public decimal? ScrapRatePercent { get; set; }

        public DateTime? LastScheduleRun { get; set; }
        public string LastScheduleRunText { get; set; }

        /// <summary>OEE per work centre for the current Persian month up to today.</summary>
        public List<OeeDto> Oee { get; set; } = new List<OeeDto>();

        /// <summary>OEE of the whole shop floor: the sums of the work centres put through the same formula.</summary>
        public decimal? OeePercent { get; set; }
    }

    /// <summary>The figures the CEO dashboard adds for production.</summary>
    public class ProductionCeoCounts
    {
        public int LateOrders { get; set; }
        public int MachinesDown { get; set; }

        /// <summary>Work-centre weeks above 110 % in the next four weeks.</summary>
        public int OverloadWeeks { get; set; }

        public int MaintenanceOverdue { get; set; }

        /// <summary>The load report of the next four weeks that the overload count was made from; the CEO overview draws its chart from it instead of loading the schedule a second time.</summary>
        public LoadReportDto Load { get; set; }
    }
}
