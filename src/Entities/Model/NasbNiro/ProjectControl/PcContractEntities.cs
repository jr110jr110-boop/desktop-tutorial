using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Quality;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control module, part 1: project/contract terms, initiation, charter, review, milestones, guarantees, penalties, variations.
    // Legacy tables (contracts, work orders, guarantees, statements) are not changed: these are 1:1 side tables.

    /// <summary>A number the company can change (thresholds, alert days...). Defaults live in code with their source.</summary>
    public class PcSetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }

    public class PcCalendar : BaseEntity
    {
        public string Name { get; set; }

        /// <summary>Bit (1 &lt;&lt; DayOfWeek) is set for a working weekday.</summary>
        public int WorkingWeekdays { get; set; }

        public decimal HoursPerDay { get; set; } = 8;

        /// <summary>Also treat the company holidays (HolidaysByDate) as non-working.</summary>
        public bool UseCompanyHolidays { get; set; }

        public ICollection<PcCalendarException> Exceptions { get; set; }
    }

    public class PcCalendarException
    {
        public int Id { get; set; }
        public int CalendarId { get; set; }
        public PcCalendar Calendar { get; set; }
        public DateTime Date { get; set; }
        public bool IsWorking { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Contract terms and project settings (WI-PL-005, WI-IP-002, WI-PL-009).</summary>
    public class PcContractTerms : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>9 characters: year(3) + counter(2) + category(2) + repeat counter(2).</summary>
        public string ProjectCode { get; set; }
        public PcCategory? Category { get; set; }

        public string CurrencyCode { get; set; }
        public decimal? ValueExVat { get; set; }
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public decimal? RetentionPercent { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public decimal? PenaltyCapPercent { get; set; }
        public int GraceDays { get; set; }

        public bool ChangeProcedureAgreed { get; set; }
        public Guid? ChangeProcedureDocumentId { get; set; }

        public int? ProjectManagerUserId { get; set; }
        public int? CalendarId { get; set; }

        /// <summary>The approved baseline in force (set when a baseline is approved).</summary>
        public int? CurrentBaselineId { get; set; }
        public PcBaseline CurrentBaseline { get; set; }

        public int? ClientReportDayOfMonth { get; set; }

        /// <summary>Weekday of the weekly data date and snapshot (DayOfWeek number; default Wednesday = 3).</summary>
        public int DataDateWeekday { get; set; } = 3;

        public DateTime? ScheduleStartDate { get; set; }
        public DateTime? ContractDeliveryDate { get; set; }

        /// <summary>SPI/CPI never go into the client report unless this is true (WI-PL-009 28).</summary>
        public bool ClientReportShowsIndices { get; set; }

        public PcAccrualMode AccrualMode { get; set; } = PcAccrualMode.Linear;

        /// <summary>Days the client has to answer a transmittal (default 14).</summary>
        public int ClientReviewDays { get; set; } = 14;

        /// <summary>Held outside the budget at completion (BAC).</summary>
        public decimal ManagementReserve { get; set; }

        /// <summary>The server takes the weekly snapshot by itself.</summary>
        public bool AutoSnapshot { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Project code of one work order (WI-PL-005): TYPE(2) + SIZE(3) + OPTION(2) + counter(2).</summary>
    public class PcWorkOrderInfo : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }
        public int ContractId { get; set; }

        public ItpProductType ProductType { get; set; }
        public string EquipmentProjectCode { get; set; }
        public string TypeCode { get; set; }
        public string SizeCode { get; set; }
        public string OptionCode { get; set; }
        public int RepeatCounter { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }

    /// <summary>Coding tables as data (WI-PL-005): ORC and valve codes can be added later without a release.</summary>
    public class PcCodingTable : BaseEntity
    {
        /// <summary>TYPE, OPTION, SIZE or NOTE.</summary>
        public string Table { get; set; }

        /// <summary>Product family the row belongs to, for example turbine, lube-oil, pump.</summary>
        public string Family { get; set; }

        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType? ProductType { get; set; }
        public string Note { get; set; }
    }

    public class PcProjectInitiation : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public PcInitiationStep CurrentStep { get; set; }
        public bool Completed { get; set; }
        public ICollection<PcInitiationStepRecord> Steps { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcInitiationStepRecord
    {
        public int Id { get; set; }
        public int InitiationId { get; set; }
        public PcProjectInitiation Initiation { get; set; }
        public PcInitiationStep Step { get; set; }

        /// <summary>Working-day limit from WI-PL-009 (0 = none).</summary>
        public int SlaDays { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Charter (WI-PL-007): sections as text, approved by the CEO.</summary>
    public class PcProjectCharter : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public string Overview { get; set; }
        public string ScopeDescription { get; set; }
        public string Requirements { get; set; }
        public string Deliverables { get; set; }
        public string Boundaries { get; set; }
        public string TimeBaselineEstimate { get; set; }
        public string CostEstimate { get; set; }
        public string AlternativesAnalysis { get; set; }
        public string Assumptions { get; set; }
        public string Constraints { get; set; }
        public string Risks { get; set; }
        public string Stakeholders { get; set; }
        public string PreAssignedResources { get; set; }
        public string ManagerAuthority { get; set; }
        public DateTimeOffset? ManagerAcceptedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Project execution plan, PEP (WI-PL-006).</summary>
    public class PcProjectPlan : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public string Overview { get; set; }
        public string ScopeStatement { get; set; }
        public string InScope { get; set; }
        public string OutOfScope { get; set; }
        public string Constraints { get; set; }
        public string Assumptions { get; set; }
        public string Milestones { get; set; }
        public string LongLeadRules { get; set; }
        public string CommunicationPlan { get; set; }
        public string MakeBuyPartner { get; set; }
        public string QualityPlan { get; set; }
        public string ChangeManagement { get; set; }
        public string OtherSections { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Contract review before commitment (ISO 9001 8.2.3, WI-IP-002).</summary>
    public class PcContractReview : BaseEntity
    {
        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public string TenderCode { get; set; }

        public PcReviewKind Kind { get; set; }
        public bool RequirementsDefined { get; set; }
        public bool DifferencesResolved { get; set; }

        /// <summary>The planning check that the company can meet the need (special orders).</summary>
        public bool CapabilityChecked { get; set; }
        public bool DeliveryFeasible { get; set; }
        public bool StandardsItpAgreed { get; set; }
        public bool GuaranteesReviewed { get; set; }
        public bool PenaltiesReviewed { get; set; }
        public bool PaymentTermsReviewed { get; set; }
        public bool CurrencyReviewed { get; set; }
        public string Notes { get; set; }

        public int? CapabilityCheckedByUserId { get; set; }

        public PcReviewDecision Decision { get; set; }
        public string DecisionNote { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcContractAmendment : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public PcAmendmentType Type { get; set; }
        public PcAmendmentOrigin Origin { get; set; }
        public string Description { get; set; }
        public decimal? PriceDelta { get; set; }
        public int? DaysDelta { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public string CustomerApprovalReference { get; set; }
        public DateTime? CustomerApprovalDate { get; set; }

        public ICollection<PcAmendmentCopy> Copies { get; set; }
    }

    /// <summary>The three copies of an amendment: customer, sales unit, finance.</summary>
    public class PcAmendmentCopy
    {
        public int Id { get; set; }
        public int AmendmentId { get; set; }
        public PcContractAmendment Amendment { get; set; }
        public string Recipient { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class PcPaymentMilestone : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int Seq { get; set; }
        public string Title { get; set; }
        public PcMilestoneKind Kind { get; set; }
        public PcMilestoneTrigger Trigger { get; set; }

        /// <summary>Progress percent for ProgressPercent triggers; ignored by the others.</summary>
        public decimal? TriggerPercent { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ActivityId { get; set; }

        public decimal? PercentOfValue { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PlannedDate { get; set; }
        public PcMilestoneStatus Status { get; set; }
        public DateTimeOffset? DueAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }

        /// <summary>Legacy provisional statement the commercial unit prepared for this milestone.</summary>
        public int? ProvisionalStatementId { get; set; }
        public string FinanceRef { get; set; }
        public string ClientLetterRef { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Typed guarantee data next to a legacy ContractGuaranty/TenderGuaranty row (the old string fields stay).</summary>
    public class PcGuaranteeInfo : BaseEntity
    {
        public PcGuaranteeSource SourceKind { get; set; }
        public int SourceId { get; set; }
        public int? ContractId { get; set; }

        public PcGuaranteeType GuaranteeType { get; set; }
        public decimal? AmountDecimal { get; set; }
        public string CurrencyCode { get; set; }
        public PcGuaranteeIssuer IssuerKind { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public PcGuaranteeRelease ReleaseTrigger { get; set; }
        public PcGuaranteeStatus Status { get; set; }

        /// <summary>The one-time parser could not read the old amount text.</summary>
        public bool ParseFailed { get; set; }
        public string ParseNote { get; set; }

        public ICollection<PcGuaranteeEvent> Events { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcGuaranteeEvent
    {
        public int Id { get; set; }
        public int GuaranteeInfoId { get; set; }
        public PcGuaranteeInfo GuaranteeInfo { get; set; }
        public PcGuaranteeEventKind Kind { get; set; }
        public DateTime EventDate { get; set; }
        public DateTime? NewExpiryDate { get; set; }
        public decimal? Fee { get; set; }
        public Guid? DocumentId { get; set; }
        public string Note { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    /// <summary>Delay penalty exposure (client contracts): computed from the terms, waiver is a human decision.</summary>
    public class PcPenaltyAssessment : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int? MilestoneId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Subject { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ActualDate { get; set; }
        public int LateDays { get; set; }
        public int GraceDays { get; set; }
        public decimal RatePercentPerWeek { get; set; }
        public decimal? CapPercent { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
        public PcPenaltyStatus Status { get; set; }
        public string DecisionReason { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset ComputedAt { get; set; }
    }

    public class PcVariationOrder : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int? ChangeRequestId { get; set; }
        public int? AmendmentId { get; set; }
        public string Description { get; set; }
        public decimal PriceDelta { get; set; }
        public int DaysDelta { get; set; }
        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }
        public PcVariationStatus Status { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
