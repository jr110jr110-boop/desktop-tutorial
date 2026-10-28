using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Sales;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.SalesDtos;

namespace ViewModels.NasbNiro.ServiceDtos
{
    // After-sales service (release S1 part B). Dates for people are Persian (...Text); the JSON dates stay ISO.

    // ------------------------------------------------------------------------------------------ settings and SLA

    public class SvSettingDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }

        /// <summary>The value in force (the default when nothing was saved).</summary>
        public string Effective { get; set; }
    }

    public class SvSaveSettingDto
    {
        public string Key { get; set; }
        public string Text { get; set; }
    }

    public class SvSlaRuleDto
    {
        public int Id { get; set; }
        public SvPriority Priority { get; set; }
        public string PriorityText { get; set; }
        public decimal ResponseHours { get; set; }
        public decimal? ResolutionHours { get; set; }
        public bool IsActive { get; set; }
    }

    public class SvSaveSlaRuleDto
    {
        public SvPriority Priority { get; set; }
        public decimal ResponseHours { get; set; }
        public decimal? ResolutionHours { get; set; }
        public bool? IsActive { get; set; }
    }

    // ------------------------------------------------------------------------------------------ installed units

    public class SvUnitQuery : SlPageQuery
    {
        public int? CustomerId { get; set; }
        public SlProductType? ProductType { get; set; }
        public SvUnitStatus? Status { get; set; }
        public bool? NeedsConfirm { get; set; }
        public DateTime? AsOf { get; set; }
    }

    public class SvSpareDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public decimal Quantity { get; set; }
        public SvSpareSource Source { get; set; }
        public string Note { get; set; }
    }

    public class SvUnitDto
    {
        /// <summary>True when the current user made or edited this record, so maker-checker refuses their approval; the page hides the approve button.</summary>
        public bool TouchedByMe { get; set; }

        public int Id { get; set; }
        public string Tag { get; set; }
        public string Title { get; set; }
        public int? SerialUnitId { get; set; }
        public string SerialNumber { get; set; }
        public SlProductType ProductType { get; set; }
        public string ProductTypeText { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? SiteId { get; set; }
        public string SiteTitle { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ShipmentId { get; set; }
        public string ShipmentNumber { get; set; }
        public DateTime? HandoverDate { get; set; }
        public string HandoverDateText { get; set; }
        public DateTime? CommissioningDate { get; set; }
        public string CommissioningDateText { get; set; }
        public int? WarrantyMonths { get; set; }
        public DateTime? WarrantyStart { get; set; }
        public string WarrantyStartText { get; set; }
        public DateTime? WarrantyEnd { get; set; }
        public string WarrantyEndText { get; set; }
        public SvWarrantySource WarrantySource { get; set; }
        public SvUnitStatus Status { get; set; }
        public string StatusText { get; set; }
        public bool InWarranty { get; set; }
        public decimal RunningHours { get; set; }
        public DateTime? RunningHoursAt { get; set; }
        public string RunningHoursAtText { get; set; }
        public bool Decommissioned { get; set; }
        public bool IsConfirmed { get; set; }
        public string ConfirmedAtText { get; set; }
        public string Note { get; set; }
        public List<SvSpareDto> Spares { get; set; } = new List<SvSpareDto>();

        /// <summary>The "spare parts list" deliverable of the work order (MDR) and its file.</summary>
        public int? SpareListDeliverableId { get; set; }
        public string SpareListDocumentNumber { get; set; }
        public Guid? SpareListDocumentId { get; set; }

        /// <summary>MdrList when the contract's «spare parts list» document of the MDR was found (title match); the manual lines of <see cref="Spares"/> are Manual. Empty when there is no such document.</summary>
        public SvSpareSource? SpareListSource { get; set; }
        public string SpareListSourceText { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvSaveSpareDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    public class SvSaveUnitDto
    {
        public int? Id { get; set; }
        public string Tag { get; set; }
        public string Title { get; set; }
        public int? SerialUnitId { get; set; }
        public SlProductType? ProductType { get; set; }
        public int? CustomerId { get; set; }
        public int? SiteId { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime? HandoverDate { get; set; }
        public DateTime? CommissioningDate { get; set; }

        /// <summary>Months of warranty from the handover. When empty and no manual dates are given, the end date of the warranty guarantee of the contract is used.</summary>
        public int? WarrantyMonths { get; set; }

        public DateTime? WarrantyStart { get; set; }
        public DateTime? WarrantyEnd { get; set; }
        public string Note { get; set; }
        public List<SvSaveSpareDto> Spares { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "confirm" (the draft of the system), "decommission" or "reactivate".</summary>
    public class SvUnitActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvAsBuiltDto
    {
        public int UnitId { get; set; }
        public string Tag { get; set; }

        /// <summary>The as-built tree (assembly links) of the serial unit, read through the traceability service. Empty when the unit has no serial unit.</summary>
        public SerialTraceDto Trace { get; set; }
    }

    // ------------------------------------------------------------------------------------------ service requests

    public class SvRequestQuery : SlPageQuery
    {
        public SvRequestStatus? Status { get; set; }
        public SvPriority? Priority { get; set; }
        public int? CustomerId { get; set; }
        public int? UnitId { get; set; }
        public bool? OpenOnly { get; set; }
        public bool? Breached { get; set; }
        public DateTime? AsOf { get; set; }
    }

    public class SvRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContactId { get; set; }
        public int? UnitId { get; set; }
        public string UnitTag { get; set; }
        public string Channel { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string ReceivedAtText { get; set; }
        public string Description { get; set; }
        public SvRequestType Type { get; set; }
        public string TypeText { get; set; }
        public SvPriority Priority { get; set; }
        public string PriorityText { get; set; }
        public bool ProductNonconformity { get; set; }
        public SvRequestStatus Status { get; set; }
        public string StatusText { get; set; }
        public DateTime? ResponseDue { get; set; }
        public string ResponseDueText { get; set; }
        public DateTime? ResolutionDue { get; set; }
        public string ResolutionDueText { get; set; }

        /// <summary>The priority has no SLA row, so no due time was set.</summary>
        public bool SlaMissing { get; set; }

        public int? AssignedToUserId { get; set; }
        public string AssignedToName { get; set; }
        public DateTime? FirstResponseAt { get; set; }
        public string FirstResponseAtText { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string ResolvedAtText { get; set; }
        public string Resolution { get; set; }
        public bool ResponseBreached { get; set; }
        public bool ResolutionBreached { get; set; }
        public bool Breached { get; set; }
        public int? ComplaintId { get; set; }
        public string ComplaintNumber { get; set; }
        public int OrderCount { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvSaveRequestDto
    {
        public int? Id { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public int? UnitId { get; set; }
        public string Channel { get; set; }

        /// <summary>
        /// When the request was received (the SLA clock starts here). The server time when empty. A person registering a phone call or a letter may enter the real time within the last 24 hours; every other channel
        /// (e-mail, portal) refuses a value. Fixed after the registration.
        /// </summary>
        public DateTime? ReceivedAt { get; set; }

        public string Description { get; set; }
        public SvRequestType Type { get; set; }
        public SvPriority Priority { get; set; }
        public bool ProductNonconformity { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "assign" (UserId), "progress", "wait", "resolve" (Resolution) or "close".</summary>
    public class SvRequestActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public int? UserId { get; set; }
        public string Resolution { get; set; }

        /// <summary>Not accepted any more (R1p D24): the time of an action is the server's. A request that carries a value is refused.</summary>
        public DateTime? At { get; set; }

        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ service orders

    public class SvOrderQuery : SlPageQuery
    {
        public SvOrderStatus? Status { get; set; }
        public int? RequestId { get; set; }
        public int? UnitId { get; set; }
        public int? ContractId { get; set; }
        public SvWarrantyDecision? Decision { get; set; }
    }

    public class SvEngineerDto
    {
        /// <summary>0 and no name for a reader who is not SERVICE, payroll, the finance manager or the CEO (R1p D2), except for the engineer himself.</summary>
        public int EmployeeId { get; set; }
        public string Name { get; set; }

        /// <summary>The person is not shown to this reader.</summary>
        public bool EmployeeHidden { get; set; }
    }

    public class SvTaskDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public string Description { get; set; }
        public bool IsDone { get; set; }
    }

    public class SvTimeEntryDto
    {
        public int Id { get; set; }

        /// <summary>0 and no name for a reader who is not SERVICE, payroll, the finance manager or the CEO (R1p D2), except for his own entries.</summary>
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }

        /// <summary>The person of this entry is not shown to this reader.</summary>
        public bool EmployeeHidden { get; set; }
        public DateTime WorkDate { get; set; }
        public string WorkDateText { get; set; }
        public decimal Hours { get; set; }
        public SvTimeKind Kind { get; set; }
        public string KindText { get; set; }
        public string Note { get; set; }
    }

    public class SvMeasurementDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
    }

    public class SvPartLineDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public decimal Quantity { get; set; }
    }

    /// <summary>A warehouse document of the order: an issue (consumed parts) or a customer delivery (parts left on site).</summary>
    public class SvPartDocumentDto
    {
        public int DocumentId { get; set; }
        public string Number { get; set; }
        public InventoryDocumentType Type { get; set; }
        public InventoryDocumentStatus Status { get; set; }
        public List<SvPartLineDto> Lines { get; set; } = new List<SvPartLineDto>();
    }

    public class SvOrderDto
    {
        /// <summary>True when the current user made or edited this record, so maker-checker refuses their approval; the page hides the approve button.</summary>
        public bool TouchedByMe { get; set; }

        public int Id { get; set; }
        public string Number { get; set; }
        public int? RequestId { get; set; }
        public string RequestNumber { get; set; }
        public int UnitId { get; set; }
        public string UnitTag { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContractId { get; set; }
        public int? LtsaContractId { get; set; }
        public string LtsaNumber { get; set; }
        public SvOrderKind Kind { get; set; }
        public SvOrderStatus Status { get; set; }
        public string StatusText { get; set; }
        public string Title { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public string PlannedEndText { get; set; }
        public string TravelNote { get; set; }
        public DateTime? StartedAt { get; set; }
        public string StartedAtText { get; set; }
        public string CostCenterCode { get; set; }
        public SvWarrantyDecision Decision { get; set; }
        public string DecisionText { get; set; }
        public bool CauseIsMisuse { get; set; }
        public string DecisionNote { get; set; }
        public string DecidedAtText { get; set; }
        public int? DecidedByUserId { get; set; }
        public bool GoodwillRequested { get; set; }
        public string GoodwillReason { get; set; }
        public DateTime? WarrantyTestedOn { get; set; }
        public string WarrantyTestedOnText { get; set; }
        public Guid? ReportDocumentId { get; set; }
        public string Findings { get; set; }
        public string Recommendations { get; set; }
        public decimal? RunningHoursReading { get; set; }
        public string ClosedAtText { get; set; }
        public List<SvEngineerDto> Engineers { get; set; } = new List<SvEngineerDto>();
        public List<SvTaskDto> Tasks { get; set; } = new List<SvTaskDto>();
        public List<SvTimeEntryDto> TimeEntries { get; set; } = new List<SvTimeEntryDto>();
        public List<SvMeasurementDto> Measurements { get; set; } = new List<SvMeasurementDto>();
        public List<SvPartDocumentDto> Parts { get; set; } = new List<SvPartDocumentDto>();
        public decimal TravelHours { get; set; }
        public decimal WorkHours { get; set; }
        public decimal WaitingHours { get; set; }
        public decimal TotalHours { get; set; }

        // ----- cost: the labour is absorbed at the pooled average rate of the service month pool (R1p D2), so it is never one person's pay
        public decimal LabourHours { get; set; }
        public decimal LabourAmount { get; set; }
        public decimal PartsAmount { get; set; }
        public decimal TotalCost { get; set; }
        public bool CostPosted { get; set; }
        public decimal? BillableLabourRateRial { get; set; }
        public int? InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string DraftNote { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvSaveTaskDto
    {
        public string Description { get; set; }
        public bool IsDone { get; set; }
    }

    public class SvSaveOrderDto
    {
        public int? Id { get; set; }
        public int? RequestId { get; set; }
        public int UnitId { get; set; }
        public SvOrderKind Kind { get; set; } = SvOrderKind.Field;
        public string Title { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public string TravelNote { get; set; }
        public List<int> EngineerEmployeeIds { get; set; }
        public List<SvSaveTaskDto> Tasks { get; set; }

        /// <summary>Rial per billable hour (work and travel hours) when the order is billable; an input.</summary>
        public decimal? BillableLabourRateRial { get; set; }

        public string Findings { get; set; }
        public string Recommendations { get; set; }
        public decimal? RunningHoursReading { get; set; }
        public Guid? ReportDocumentId { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "confirm" (a drafted LTSA visit becomes planned), "start", "cancel" (Reason) or "close".</summary>
    public class SvOrderActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvSaveTimeEntryDto
    {
        public int OrderId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime? WorkDate { get; set; }
        public decimal Hours { get; set; }
        public SvTimeKind Kind { get; set; }
        public string Note { get; set; }
    }

    public class SvSaveMeasurementDto
    {
        public int OrderId { get; set; }
        public string Name { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
    }

    public class SvRemoveRowDto
    {
        public int OrderId { get; set; }
        public int RowId { get; set; }
    }

    public class SvDecisionDto
    {
        public int OrderId { get; set; }
        public SvWarrantyDecision Decision { get; set; }
        public bool CauseIsMisuse { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvGoodwillRequestDto
    {
        public int OrderId { get; set; }
        public string Reason { get; set; }
    }

    public class SvDraftInvoiceDto
    {
        public int Id { get; set; }

        /// <summary>Rial per billable hour; needed when it was not entered on the order before it was closed.</summary>
        public decimal? BillableLabourRateRial { get; set; }
    }

    // ------------------------------------------------------------------------------------------ LTSA

    public class SvContractQuery : SlPageQuery
    {
        public SvContractStatus? Status { get; set; }
        public int? CustomerId { get; set; }
    }

    public class SvContractDto
    {
        /// <summary>True when the current user made or edited this record, so maker-checker refuses their approval; the page hides the approve button.</summary>
        public bool TouchedByMe { get; set; }

        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public SvContractStatus Status { get; set; }
        public string StatusText { get; set; }
        public DateTime StartDate { get; set; }
        public string StartDateText { get; set; }
        public DateTime EndDate { get; set; }
        public string EndDateText { get; set; }
        public string Scope { get; set; }
        public int VisitIntervalMonths { get; set; }
        public decimal? PriceRial { get; set; }
        public DateTime? NextVisitDate { get; set; }
        public string NextVisitDateText { get; set; }
        public List<int> UnitIds { get; set; } = new List<int>();
        public List<string> UnitTags { get; set; } = new List<string>();
        public int DraftedVisits { get; set; }
        public string RowVersion { get; set; }
    }

    public class SvSaveContractDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int CustomerId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Scope { get; set; }
        public int VisitIntervalMonths { get; set; }
        public decimal? PriceRial { get; set; }
        public List<int> UnitIds { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "activate" (by somebody who did not write it) or "cancel".</summary>
    public class SvContractActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ jobs

    public class SvJobsRunDto
    {
        /// <summary>The day the jobs count from; today when empty (tests and what-if runs).</summary>
        public DateTime? AsOf { get; set; }
    }

    /// <summary>The month-end run of the service labour pool (R1p D2): closes the ended months and posts the true-up of the labour absorbed at the pooled average rate.</summary>
    public class SvLabourAggregateResultDto
    {
        public DateTime AsOf { get; set; }
        public List<SvLabourPeriodDto> Periods { get; set; } = new List<SvLabourPeriodDto>();

        /// <summary>Orders whose month has fewer than three payroll-rated engineers behind it: nothing is published, they wait for the next month and join its pool (up to the end of the quarter).</summary>
        public int OrdersCarried { get; set; }
    }

    public class SvLabourPeriodDto
    {
        /// <summary>The Persian month as yyyymm.</summary>
        public int Period { get; set; }
        public string PeriodText { get; set; }

        /// <summary>Orders the true-up took into the pool of the month.</summary>
        public int Orders { get; set; }

        /// <summary>Distinct payroll-rated engineers in the pool.</summary>
        public int People { get; set; }

        public decimal Hours { get; set; }

        /// <summary>The pooled average rate (pool cost / pool hours) the orders closed from now on are absorbed at. Null for a protected pool.</summary>
        public decimal? Rate { get; set; }

        /// <summary>What the closed orders absorbed. Null for a protected pool and a reader outside PAYROLL, FINANCEMANAGER and CEO.</summary>
        public decimal? Absorbed { get; set; }

        /// <summary>Pool cost less absorbed: the one company-level true-up line of the month. Null like <see cref="Absorbed"/>.</summary>
        public decimal? Variance { get; set; }

        public int? EventId { get; set; }

        /// <summary>When the month was closed.</summary>
        public DateTimeOffset? PostedAt { get; set; }

        /// <summary>The pool had fewer than three payroll-rated engineers and was closed by the quarter-end cap: every reader but payroll, the finance manager and the CEO sees no amount in it.</summary>
        public bool Protected { get; set; }
    }

    public class SvJobsResultDto
    {
        public DateTime AsOf { get; set; }
        public int VisitsDrafted { get; set; }
        public int ContractsExpired { get; set; }
        public int BreachAlerts { get; set; }
        public int SurveysDrafted { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ surveys

    public class SlSurveyQuery : SlPageQuery
    {
        public SlSurveyStatus? Status { get; set; }
        public SlSurveyKind? Kind { get; set; }
        public int? CustomerId { get; set; }

        /// <summary>R1p D25: QA picks its sample among the answered surveys that are still unverified (false) or lists the verified ones (true).</summary>
        public bool? Verified { get; set; }
    }

    public class SlSurveyAnswerDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public string Question { get; set; }
        public int? Score { get; set; }
    }

    public class SlSurveyDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public SlSurveyKind Kind { get; set; }
        public string KindText { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContractId { get; set; }
        public int? ShipmentId { get; set; }
        public int? ServiceOrderId { get; set; }
        public DateTime DraftedOn { get; set; }
        public string DraftedOnText { get; set; }
        public SlSurveyStatus Status { get; set; }
        public string StatusText { get; set; }
        public string SentAtText { get; set; }
        public string AnsweredAtText { get; set; }
        public string Comments { get; set; }
        public decimal? AverageScore { get; set; }

        /// <summary>R1p D25: why the survey was skipped.</summary>
        public string SkipReason { get; set; }

        /// <summary>R1p D25: SERVICE or SALES, whoever typed the customer's scores in (empty until answered).</summary>
        public string RecordedByRole { get; set; }

        /// <summary>R1p D25: QA checked these answers with the customer.</summary>
        public bool Verified { get; set; }
        public string VerifiedAtText { get; set; }
        public string VerifyNote { get; set; }

        /// <summary>R1p D25: the complaint draft opened because a score was below the threshold.</summary>
        public int? ComplaintId { get; set; }
        public string ComplaintNumber { get; set; }

        public List<SlSurveyAnswerDto> Answers { get; set; } = new List<SlSurveyAnswerDto>();
        public string RowVersion { get; set; }
    }

    public class SlSurveyScoreDto
    {
        public int RowNumber { get; set; }
        public int Score { get; set; }
    }

    public class SlSurveyAnswerSaveDto
    {
        public int Id { get; set; }
        public List<SlSurveyScoreDto> Scores { get; set; } = new List<SlSurveyScoreDto>();
        public string Comments { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "send", "skip" (Reason is mandatory) or "verify" (QA only; Note says what was checked with the customer).</summary>
    public class SlSurveyActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }

        /// <summary>The reason of a skip.</summary>
        public string Reason { get; set; }

        /// <summary>What QA checked with the customer (verify).</summary>
        public string Note { get; set; }

        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ dashboard and KPIs

    public class SvPriorityCountDto
    {
        public SvPriority Priority { get; set; }
        public string PriorityText { get; set; }
        public int Count { get; set; }
    }

    public class SvDashboardDto
    {
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }
        public int OpenRequests { get; set; }
        public List<SvPriorityCountDto> OpenByPriority { get; set; } = new List<SvPriorityCountDto>();

        /// <summary>Open requests whose response or resolution time is past.</summary>
        public int SlaBreaches { get; set; }

        public int UnitsInWarranty { get; set; }
        public int UnitsUnderLtsa { get; set; }
        public int UnitsOutOfWarranty { get; set; }
        public int UnitsToConfirm { get; set; }

        /// <summary>Cost of the warranty orders closed since the first day of the Persian year (labour at the pooled average rate + parts).</summary>
        public decimal WarrantyCostYtd { get; set; }

        public int WarrantyOrdersYtd { get; set; }
        public int OpenOrders { get; set; }
        public int OrdersWaitingClose { get; set; }
        public int GoodwillWaiting { get; set; }
        public int SurveysDrafted { get; set; }

        /// <summary>Average score of the VERIFIED satisfaction surveys (R1p D25); empty when none is verified yet.</summary>
        public decimal? SurveyAverageScore { get; set; }

        /// <summary>How many verified surveys the average is made of.</summary>
        public int SurveysVerified { get; set; }

        /// <summary>Answered surveys QA has not verified yet: they are in no average.</summary>
        public int SurveysUnverified { get; set; }
    }

    public class SvKpiQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public DateTime? AsOf { get; set; }
    }

    public class SvRepeatFailureDto
    {
        public int UnitId { get; set; }
        public string UnitTag { get; set; }
        public int Failures { get; set; }

        /// <summary>Failures after the first one.</summary>
        public int Repeats { get; set; }
    }

    public class SvProductKpiDto
    {
        public SlProductType ProductType { get; set; }
        public string ProductTypeText { get; set; }
        public int Units { get; set; }
        public decimal RunningHours { get; set; }
        public int Failures { get; set; }

        /// <summary>Operating hours of the units divided by their breakdown requests; empty without a failure. METER hours (the operating time the units' meters counted), not clock hours.</summary>
        public decimal? MtbfHours { get; set; }

        public int WarrantyOrders { get; set; }
        public decimal WarrantyCost { get; set; }
    }

    public class SvKpiDto
    {
        public DateTime AsOf { get; set; }
        public int RequestsCounted { get; set; }

        /// <summary>
        /// Average CLOCK hours from the receipt to the first response; empty without a response. ★ design note: the clock does not stop while the request waits for the customer (the SLA, the list, the dashboard and
        /// the CEO dashboard all measure it the same way); the company decides whether a "waiting for customer" status should pause it.
        /// </summary>
        public decimal? FirstResponseHours { get; set; }

        /// <summary>Average CLOCK hours from the receipt to the resolution; empty without a resolved request (same note as <see cref="FirstResponseHours"/>).</summary>
        public decimal? ResolutionHours { get; set; }

        public List<SvRepeatFailureDto> RepeatFailures { get; set; } = new List<SvRepeatFailureDto>();
        public List<SvProductKpiDto> ByProduct { get; set; } = new List<SvProductKpiDto>();
        public int TotalFailures { get; set; }

        /// <summary>R1p D36: the meter hours the ACTIVE units ran inside the period (the increase of the meter, not the whole life of the units).</summary>
        public decimal TotalRunningHours { get; set; }

        /// <summary>The failures of the same units in the same period divided into <see cref="TotalRunningHours"/>: METER hours between failures (the repair time is clock time and is not part of it).</summary>
        public decimal? MtbfHours { get; set; }

        /// <summary>The breakdown requests of the units that went into the MTBF.</summary>
        public int MtbfFailures { get; set; }

        /// <summary>The active units whose hours of the period are known, and the active units left out because the period cannot be measured for them (their failures are left out too).</summary>
        public int MtbfUnits { get; set; }
        public int MtbfUnitsExcluded { get; set; }
    }
}
