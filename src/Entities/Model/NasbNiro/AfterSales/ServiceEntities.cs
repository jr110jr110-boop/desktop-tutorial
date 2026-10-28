using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.AfterSales
{
    // After-sales service (release S1 part B). Tables start with "Sv"; the satisfaction survey keeps the "Sl" prefix of the brief (SlSurvey).
    // Money is in rials (one company, rial only). No SLA hours, no hourly rate and no warranty period is seeded: they are inputs.

    /// <summary>A number or a text the company can change (the cost centre code of after-sales, the work centre whose standard labour rate is used).</summary>
    public class SvSetting : BaseEntity
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public string Title { get; set; }
    }

    /// <summary>SLA of one priority. A priority without a row has no SLA (nothing is assumed).</summary>
    public class SvSlaRule : BaseEntity
    {
        public SvPriority Priority { get; set; }

        /// <summary>Hours from the receipt of the request to the first response.</summary>
        public decimal ResponseHours { get; set; }

        /// <summary>Hours from the receipt to the resolution; empty when the company has none.</summary>
        public decimal? ResolutionHours { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>The installed base: one delivered unit at a customer site.</summary>
    public class SvInstalledUnit : BaseEntity
    {
        /// <summary>The tag number at the site (service confirms it).</summary>
        public string Tag { get; set; }

        public string Title { get; set; }

        /// <summary>The serial unit of the delivered product (its as-built tree is read through the traceability API).</summary>
        public int? SerialUnitId { get; set; }

        public Sales.SlProductType ProductType { get; set; } = Sales.SlProductType.Other;
        public int? CustomerId { get; set; }
        public int? SiteId { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }

        /// <summary>The shipment whose handover created this unit (empty for a unit entered by hand).</summary>
        public int? ShipmentId { get; set; }

        public DateTime? HandoverDate { get; set; }
        public DateTime? CommissioningDate { get; set; }

        /// <summary>Input: months of warranty counted from the handover. Empty when the guarantee of the contract gives the end.</summary>
        public int? WarrantyMonths { get; set; }

        public DateTime? WarrantyStart { get; set; }
        public DateTime? WarrantyEnd { get; set; }
        public SvWarrantySource WarrantySource { get; set; }

        /// <summary>Operating hours, updated by the meter reading of the service visits.</summary>
        public decimal RunningHours { get; set; }

        public DateTime? RunningHoursAt { get; set; }
        public bool Decommissioned { get; set; }
        public DateTime? DecommissionedAt { get; set; }
        public string Note { get; set; }

        /// <summary>False while the record is the system's draft; service confirms the site, the tag and the warranty.</summary>
        public bool IsConfirmed { get; set; }

        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public string EditorUserIds { get; set; }

        public ICollection<SvUnitSpare> Spares { get; set; } = new List<SvUnitSpare>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A recommended spare part of an installed unit (manual line; the MDR spare-parts list itself is a document linked from the work order).</summary>
    public class SvUnitSpare
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public SvSpareSource Source { get; set; }
        public string Note { get; set; }
    }

    public class SvServiceRequest : BaseEntity
    {
        public string Number { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public int? UnitId { get; set; }
        public string Channel { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string Description { get; set; }
        public SvRequestType Type { get; set; }
        public SvPriority Priority { get; set; }

        /// <summary>The product itself is not as it should be (a nonconformity): the system also opens a quality complaint.</summary>
        public bool ProductNonconformity { get; set; }

        public SvRequestStatus Status { get; set; } = SvRequestStatus.Open;

        /// <summary>Received time + the SLA response hours of the priority; empty when the priority has no SLA.</summary>
        public DateTime? ResponseDue { get; set; }

        public DateTime? ResolutionDue { get; set; }
        public int? AssignedToUserId { get; set; }
        public DateTimeOffset? AssignedAt { get; set; }

        /// <summary>The first time somebody acted on the request (it was assigned or its status changed).</summary>
        public DateTime? FirstResponseAt { get; set; }

        public DateTime? ResolvedAt { get; set; }
        public string Resolution { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        // The quality complaint made for this request points to the request with its own ServiceRequestId (one foreign key, no cycle); the request finds its complaint by that key.

        public string EditorUserIds { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SvServiceOrder : BaseEntity
    {
        public string Number { get; set; }
        public int? RequestId { get; set; }
        public int UnitId { get; set; }
        public int? CustomerId { get; set; }
        public int? ContractId { get; set; }

        /// <summary>The LTSA whose visit this order is (the system drafts it when the visit is due).</summary>
        public int? LtsaContractId { get; set; }

        public SvOrderKind Kind { get; set; } = SvOrderKind.Field;
        public SvOrderStatus Status { get; set; } = SvOrderStatus.Planned;
        public string Title { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public string TravelNote { get; set; }
        public DateTime? StartedAt { get; set; }

        /// <summary>The cost centre of after-sales the order is charged to.</summary>
        public string CostCenterCode { get; set; }

        // ----- warranty decision
        public SvWarrantyDecision Decision { get; set; }

        /// <summary>The cause is misuse by the customer: the order cannot be a warranty order.</summary>
        public bool CauseIsMisuse { get; set; }

        public string DecisionNote { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public DateTimeOffset? GoodwillRequestedAt { get; set; }
        public int? GoodwillRequestedByUserId { get; set; }
        public string GoodwillReason { get; set; }

        /// <summary>The date the warranty was tested against (the day the failure was reported; the planned start when there is no request).</summary>
        public DateTime? WarrantyTestedOn { get; set; }

        // ----- report and findings
        public Guid? ReportDocumentId { get; set; }
        public string Findings { get; set; }
        public string Recommendations { get; set; }

        /// <summary>Meter reading of the unit at the visit; the unit's running hours follow it.</summary>
        public decimal? RunningHoursReading { get; set; }

        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        // ----- cost (posted when the order is closed)
        public decimal LabourHours { get; set; }

        /// <summary>R1p D2 (round 3): hours x the STANDARD rate of the after-sales labour work centre, for every engineer alike (an order closed by the earlier release holds the engineers' own pay here; see SvLegacyLabour).</summary>
        public decimal LabourAmount { get; set; }

        public int LabourEmployeeCount { get; set; }
        public int LabourPayrollEmployeeCount { get; set; }
        public decimal PartsAmount { get; set; }
        public DateTimeOffset? CostPostedAt { get; set; }
        public int? WarrantyEventId { get; set; }

        /// <summary>Set together with <see cref="WarrantyEventId"/> so the close is one save (the id exists only after it).</summary>
        public Finance.AccountingEvent WarrantyEvent { get; set; }

        /// <summary>
        /// R1p D2 (standard rate, round 3): the Persian month (yyyymm) whose variance takes this order at the month-end run; set when the order is closed with hours of payroll-rated engineers (the first month that is not closed
        /// at or after the close). The monthly run moves it to a later month while the month does not pass the publication gate. Empty when no payroll-rated engineer worked on the order.
        /// </summary>
        public int? LabourPendingPeriod { get; set; }

        /// <summary>When the month-end run took this order into the pool of its month (empty while it waits).</summary>
        public DateTimeOffset? LabourAggregatedAt { get; set; }

        /// <summary>R1p D2: hours x the own hourly payroll cost of the payroll-rated engineers of the order, unrounded. It only goes into the cost total of a pool; no API shows it (on its own it is one person's pay).</summary>
        public decimal? LabourPayrollCost { get; set; }

        /// <summary>
        /// R1p D2: the part of <see cref="LabourAmount"/> absorbed for the payroll-rated engineers at the standard rate (the rest is the hours of engineers without a payslip, at the same standard rate). It is the amount the pool takes
        /// off its cost. Positive on every order of this release that has payroll-rated hours; empty or not positive only on an order of the earlier release (see SvLegacyLabour); a waiting order the backfill converted holds minus
        /// the standard-rate part of its labour here, so that the variance of its month carries that part too.
        /// </summary>
        public decimal? LabourPooledAmount { get; set; }

        // ----- billing (Billable)
        public decimal? BillableLabourRateRial { get; set; }
        public int? InvoiceId { get; set; }
        public Finance.FnSalesInvoice Invoice { get; set; }
        public string DraftNote { get; set; }

        public string EditorUserIds { get; set; }

        public ICollection<SvOrderEngineer> Engineers { get; set; } = new List<SvOrderEngineer>();
        public ICollection<SvOrderTask> Tasks { get; set; } = new List<SvOrderTask>();
        public ICollection<SvTimeEntry> TimeEntries { get; set; } = new List<SvTimeEntry>();
        public ICollection<SvMeasurement> Measurements { get; set; } = new List<SvMeasurement>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SvOrderEngineer
    {
        public int Id { get; set; }
        public int OrderId { get; set; }

        /// <summary>The personnel record (payroll file) of the engineer; only the name and the code are shown outside payroll.</summary>
        public int EmployeeId { get; set; }
    }

    public class SvOrderTask
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int RowNumber { get; set; }
        public string Description { get; set; }
        public bool IsDone { get; set; }
    }

    public class SvTimeEntry
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public decimal Hours { get; set; }
        public SvTimeKind Kind { get; set; }
        public string Note { get; set; }

        /// <summary>Set when the order is costed: the hourly rate came from payroll (not from the standard rate of the work centre).</summary>
        public bool? RateFromPayroll { get; set; }

        public int CreatedByUserId { get; set; }
    }

    public class SvMeasurement
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Name { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
    }

    /// <summary>A long-term service agreement (LTSA): units, period, scope, visit interval and price.</summary>
    public class SvContract : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public int CustomerId { get; set; }
        public SvContractStatus Status { get; set; } = SvContractStatus.Draft;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Scope { get; set; }

        /// <summary>Months between two visits.</summary>
        public int VisitIntervalMonths { get; set; }

        /// <summary>Rial; an input.</summary>
        public decimal? PriceRial { get; set; }

        /// <summary>The date of the next visit that has no service order yet (empty when the agreement has no more visits).</summary>
        public DateTime? NextVisitDate { get; set; }

        public int? ActivatedByUserId { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public string EditorUserIds { get; set; }
        public ICollection<SvContractUnit> Units { get; set; } = new List<SvContractUnit>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SvContractUnit
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int UnitId { get; set; }
    }

    /// <summary>Customer satisfaction survey (ISO 9001 9.1.2): the system drafts it, a person sends it and records the customer's scores (1 to 5).</summary>
    public class SlSurvey : BaseEntity
    {
        public string Number { get; set; }
        public SlSurveyKind Kind { get; set; }
        public int CustomerId { get; set; }
        public int? ContractId { get; set; }
        public int? ShipmentId { get; set; }
        public int? ServiceOrderId { get; set; }
        public DateTime DraftedOn { get; set; }
        public SlSurveyStatus Status { get; set; } = SlSurveyStatus.Drafted;
        public DateTimeOffset? SentAt { get; set; }
        public DateTimeOffset? AnsweredAt { get; set; }
        public string Comments { get; set; }

        /// <summary>Average of the scores, two decimals; empty until answered.</summary>
        public decimal? AverageScore { get; set; }

        /// <summary>Why a survey was skipped (R1p D25: skipping without a reason hides a customer who would have scored low).</summary>
        public string SkipReason { get; set; }

        /// <summary>
        /// R1p D25: the role of the person who typed the customer's scores in (SERVICE or SALES, the people the scores are about). Such answers are unverified until QA checks a sample of them with the customer.
        /// </summary>
        public string RecordedByRole { get; set; }

        public int? AnsweredByUserId { get; set; }

        /// <summary>Set by QA only (the checker of the person who recorded the answers).</summary>
        public bool Verified { get; set; }
        public int? VerifiedByUserId { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string VerifyNote { get; set; }

        /// <summary>R1p D25: the customer complaint draft the system opened because a score was below the threshold (one per survey).</summary>
        public int? ComplaintId { get; set; }
        public Quality.CustomerComplaint Complaint { get; set; }

        public ICollection<SlSurveyAnswer> Answers { get; set; } = new List<SlSurveyAnswer>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlSurveyAnswer
    {
        public int Id { get; set; }
        public int SurveyId { get; set; }
        public int RowNumber { get; set; }
        public string Question { get; set; }

        /// <summary>1 to 5; empty until the customer answered.</summary>
        public int? Score { get; set; }
    }
}
