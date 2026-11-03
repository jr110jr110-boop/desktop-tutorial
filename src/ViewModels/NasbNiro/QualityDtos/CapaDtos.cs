using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class CapaActionDto
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public int? OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? DoneAt { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public string EvidenceNote { get; set; }
        public bool Overdue { get; set; }

        /// <summary>The round of the CAPA this action belongs to; a failed effectiveness check starts the next round.</summary>
        public int Iteration { get; set; } = 1;
    }

    public class CapaListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public CapaSource Source { get; set; }
        public int? SourceId { get; set; }
        public string Problem { get; set; }
        public CapaStatus Status { get; set; }
        public int? OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? EffectivenessDueDate { get; set; }

        /// <summary>The due date has passed, or the effectiveness check is late.</summary>
        public bool Overdue { get; set; }
        public bool AutoDrafted { get; set; }

        /// <summary>Required by a rule (system-drafted, non-conformance or audit finding source): only the QA manager can cancel it.</summary>
        public bool Mandatory { get; set; }

        /// <summary>Which round of actions is current (1 at first; +1 every time a failed effectiveness check reopens the CAPA). On the list since the front R1q review.</summary>
        public int Iteration { get; set; } = 1;

        /// <summary>0 none, 1 QA alerted (late), 2 CEO alerted (CapaCeoEscalationDays late).</summary>
        public int EscalationLevel { get; set; }

        public string CancelReason { get; set; }
    }

    public class CapaDto : CapaListDto
    {

        public string SourceNumber { get; set; }
        public string Containment { get; set; }
        public RootCauseMethod RootCauseMethod { get; set; }
        public string RootCauseAnalysis { get; set; }
        public string EffectivenessCriteria { get; set; }
        public string EffectivenessResult { get; set; }
        public DateTime? ImplementedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedByName { get; set; }
        public string CloseNote { get; set; }
        public string RowVersion { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }

        /// <summary>The first day the effectiveness may be judged (implementation date + the waiting days).</summary>
        public DateTime? EffectivenessEarliest { get; set; }

        public List<CapaActionDto> Actions { get; set; } = new List<CapaActionDto>();
    }

    public class CapaEscalationResultDto
    {
        /// <summary>CAPAs that moved up a level now.</summary>
        public int Escalated { get; set; }

        /// <summary>Of them, those the CEO was told about.</summary>
        public int CeoNotified { get; set; }

        /// <summary>
        /// True for the very first run: the levels of the CAPAs that were already late were only recorded (<see cref="Recorded"/>) and nobody was told, so that the first night
        /// after the upgrade does not flood QA and the CEO with the whole backlog.
        /// </summary>
        public bool FirstRun { get; set; }

        /// <summary>CAPAs whose level was recorded without telling anybody (first run only).</summary>
        public int Recorded { get; set; }

        /// <summary>CAPAs that could not be handled now (somebody changed them at the same moment, or the notice failed); the next run tries again.</summary>
        public int Skipped { get; set; }
    }

    public class CapaQuery : PaginationParams
    {
        public CapaStatus? Status { get; set; }
        public CapaSource? Source { get; set; }
        public int? OwnerUserId { get; set; }
        public string Contains { get; set; }
    }

    public class SaveCapaActionDto
    {
        public int? Id { get; set; }
        public string Description { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class SaveCapaDto
    {
        public int? Id { get; set; }
        public string RowVersion { get; set; }
        public CapaSource Source { get; set; } = CapaSource.Other;
        public int? SourceId { get; set; }
        public string Problem { get; set; }
        public string Containment { get; set; }
        public RootCauseMethod RootCauseMethod { get; set; }
        public string RootCauseAnalysis { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
        public string EffectivenessCriteria { get; set; }
        public List<SaveCapaActionDto> Actions { get; set; } = new List<SaveCapaActionDto>();
    }

    public class CapaActionRequestDto
    {
        public int Id { get; set; }

        /// <summary>complete-action, implemented, effectiveness, close, cancel</summary>
        public string Action { get; set; }
        public int? ActionId { get; set; }
        public string Note { get; set; }
        public Guid? EvidenceDocumentId { get; set; }

        /// <summary>For effectiveness: the check passed.</summary>
        public bool Passed { get; set; }

        /// <summary>
        /// For complete-action: the day the action was really done (today when empty). Not in the future and not older than the back-date window (setting MaxBackdateDays); the
        /// CAPA counts as implemented on the day of its last action of the round, and the waiting period of the effectiveness check runs from that day.
        /// </summary>
        public DateTime? Date { get; set; }
    }

    // ------------------------------------------------------------------ customer complaints

    public class ComplaintListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public DateTime ReceivedDate { get; set; }
        public NcrSeverity Severity { get; set; }
        public ComplaintStatus Status { get; set; }
        public DateTime ResponseDue { get; set; }
        public bool Overdue { get; set; }
        public int? NcrId { get; set; }
        public int? CapaId { get; set; }
    }

    public class ComplaintDto : ComplaintListDto
    {
        public string Channel { get; set; }
        public string Description { get; set; }
        public string Response { get; set; }
        public DateTime? RespondedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string NcrNumber { get; set; }
        public string CapaNumber { get; set; }

        /// <summary>The after-sales service request this complaint was opened from (release S1 part B).</summary>
        public int? ServiceRequestId { get; set; }

        public string ServiceRequestNumber { get; set; }
    }

    public class ComplaintQuery : PaginationParams
    {
        public ComplaintStatus? Status { get; set; }
        public bool? Overdue { get; set; }
        public string Contains { get; set; }
    }

    public class SaveComplaintDto
    {
        public int? Id { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string Channel { get; set; }
        public string Description { get; set; }
        public NcrSeverity Severity { get; set; } = NcrSeverity.NotClassified;
    }

    public class ComplaintActionDto
    {
        public int Id { get; set; }

        /// <summary>review, respond, link-ncr, create-capa, close</summary>
        public string Action { get; set; }
        public string Response { get; set; }
        public int? NcrId { get; set; }
        public NcrSeverity Severity { get; set; }
    }
}
