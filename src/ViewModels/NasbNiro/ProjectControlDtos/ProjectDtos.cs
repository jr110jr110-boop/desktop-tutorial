using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    /// <summary>Contract terms: used for reading and for saving (the project code is generated, never typed).</summary>
    public class PcContractTermsDto
    {
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public string ContractTitle { get; set; }
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
        public string ProjectManagerName { get; set; }
        public int? CalendarId { get; set; }
        public int? CurrentBaselineId { get; set; }
        public int? ClientReportDayOfMonth { get; set; }
        public int DataDateWeekday { get; set; } = 3;
        public DateTime? ScheduleStartDate { get; set; }
        public DateTime? ContractDeliveryDate { get; set; }
        public bool ClientReportShowsIndices { get; set; }
        public PcAccrualMode AccrualMode { get; set; } = PcAccrualMode.Linear;
        public int ClientReviewDays { get; set; } = 14;
        public decimal ManagementReserve { get; set; }
        public bool AutoSnapshot { get; set; } = true;

        /// <summary>Value plus the approved variation orders (read only).</summary>
        public decimal? CurrentValue { get; set; }
        public decimal ApprovedVariationPrice { get; set; }
        public int ApprovedVariationDays { get; set; }
        public string RowVersion { get; set; }
    }

    public class GenerateProjectCodeDto
    {
        public int ContractId { get; set; }
        public PcCategory Category { get; set; }
    }

    public class PcWorkOrderInfoDto
    {
        public int? Id { get; set; }
        public int WorkOrderId { get; set; }
        public int ContractId { get; set; }
        public string WorkOrderCode { get; set; }
        public ItpProductType ProductType { get; set; }
        public string TypeCode { get; set; }
        public string SizeCode { get; set; }
        public string OptionCode { get; set; }
        public string EquipmentProjectCode { get; set; }
        public int RepeatCounter { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }

    public class PcCodingRowDto
    {
        public int? Id { get; set; }
        public string Table { get; set; }
        public string Family { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType? ProductType { get; set; }
        public string Note { get; set; }
    }

    public class PcInitiationStepDto
    {
        public PcInitiationStep Step { get; set; }
        public string Title { get; set; }
        public string GateRole { get; set; }
        public int SlaDays { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneByName { get; set; }
        public string Note { get; set; }
        public bool Overdue { get; set; }
    }

    public class PcInitiationDto
    {
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public PcInitiationStep CurrentStep { get; set; }
        public bool Completed { get; set; }
        public List<PcInitiationStepDto> Steps { get; set; } = new List<PcInitiationStepDto>();

        /// <summary>FO-PL-001 draft: the system writes the notice to the units when the code is assigned.</summary>
        public string UnitsNoticeDraft { get; set; }
    }

    public class PcInitiationActionDto
    {
        public int ContractId { get; set; }
        public PcInitiationStep Step { get; set; }
        public string Note { get; set; }

        /// <summary>ManagerSelected: the CEO names the project manager.</summary>
        public int? ProjectManagerUserId { get; set; }
    }

    public class PcCharterDto
    {
        public int ContractId { get; set; }
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
        public string ApprovedByName { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcPlanDto
    {
        public int ContractId { get; set; }
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
        public string ApprovedByName { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcApproveDto
    {
        public int ContractId { get; set; }

        /// <summary>Charter: the project manager accepted the authority (true) before the CEO approves.</summary>
        public bool ManagerAccepted { get; set; }
        public string Note { get; set; }
    }

    public class PcReviewDto
    {
        public int? Id { get; set; }
        public int? ContractId { get; set; }
        public string TenderCode { get; set; }
        public PcReviewKind Kind { get; set; }
        public bool RequirementsDefined { get; set; }
        public bool DifferencesResolved { get; set; }
        public bool CapabilityChecked { get; set; }
        public bool DeliveryFeasible { get; set; }
        public bool StandardsItpAgreed { get; set; }
        public bool GuaranteesReviewed { get; set; }
        public bool PenaltiesReviewed { get; set; }
        public bool PaymentTermsReviewed { get; set; }
        public bool CurrencyReviewed { get; set; }
        public string Notes { get; set; }
        public PcReviewDecision Decision { get; set; }
        public string DecisionNote { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcReviewActionDto
    {
        public int Id { get; set; }

        /// <summary>capability-check (planning), decide (CEO)</summary>
        public string Action { get; set; }
        public PcReviewDecision Decision { get; set; }
        public string Note { get; set; }
    }

    public class PcAmendmentDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public PcAmendmentType Type { get; set; }
        public PcAmendmentOrigin Origin { get; set; }
        public string Description { get; set; }
        public decimal? PriceDelta { get; set; }
        public int? DaysDelta { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public string CustomerApprovalReference { get; set; }
        public DateTime? CustomerApprovalDate { get; set; }
        public List<PcAmendmentCopyDto> Copies { get; set; } = new List<PcAmendmentCopyDto>();
    }

    public class PcAmendmentCopyDto
    {
        public int Id { get; set; }
        public string Recipient { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class PcAmendmentActionDto
    {
        public int Id { get; set; }

        /// <summary>ceo-approve, customer-approve, copy-sent</summary>
        public string Action { get; set; }
        public string Reference { get; set; }
        public DateTime? Date { get; set; }
        public int? CopyId { get; set; }
    }

    public class PcContractQuery : PaginationParams
    {
        public int? ContractId { get; set; }
        public string Contains { get; set; }
    }
}
