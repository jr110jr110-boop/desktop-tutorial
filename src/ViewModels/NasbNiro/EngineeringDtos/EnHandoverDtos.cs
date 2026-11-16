using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnHandoverQuery
    {
        public int? CaseId { get; set; }
        public int? ContractId { get; set; }
        public int? Status { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnHandoverLineDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; }
        public int State { get; set; }
        public string StateText { get; set; }
        public Guid? DocumentId { get; set; }
        public string Revision { get; set; }
        public int? PlatformDocumentId { get; set; }
        public int? DeliverableId { get; set; }
        public string Note { get; set; }
    }

    public class EnDistributionDto
    {
        public int Id { get; set; }
        public int HandoverId { get; set; }
        public string HandoverNumber { get; set; }
        public int ContractId { get; set; }
        public int Unit { get; set; }
        public string UnitText { get; set; }
        public string Roles { get; set; }
        public string NotifiedAtText { get; set; }
        public string AcknowledgedByName { get; set; }
        public string AcknowledgedAtText { get; set; }
        public bool Acknowledged { get; set; }
        public string RowVersion { get; set; }
        public bool CanAcknowledge { get; set; }
        public string AcknowledgeBlockedReason { get; set; }
    }

    public class EnDebtDto
    {
        public int Id { get; set; }
        public int WaiverId { get; set; }
        public string WaiverNumber { get; set; }
        public int HandoverLineId { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int ContractId { get; set; }
        public int? ProductId { get; set; }
        public string DocumentType { get; set; }
        public string Title { get; set; }
        public string DueDateText { get; set; }
        public DateTime DueDate { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public bool Overdue { get; set; }
        public int OverdueDays { get; set; }
        public string SettledAtText { get; set; }
        public string SettledByName { get; set; }
        public Guid? SettledDocumentId { get; set; }
    }

    public class EnDebtQuery
    {
        public int? CaseId { get; set; }
        public int? ContractId { get; set; }
        public int? Status { get; set; }
        public bool OnlyOverdue { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnWaiverDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int HandoverId { get; set; }
        public string HandoverNumber { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public string Reason { get; set; }
        public List<string> MissingLines { get; set; } = new List<string>();
        public bool DossierUnconfirmed { get; set; }
        public DateTime DebtDueDate { get; set; }
        public string DebtDueDateText { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        /// <summary>Whole hours a request has waited for the CEO; a request never expires, it stays an alarm until he decides.</summary>
        public int WaitingHours { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
        public bool Used { get; set; }
        public bool Covers { get; set; }
        public string CoverageNote { get; set; }
        public string RowVersion { get; set; }
        public bool CanDecide { get; set; }
        public string DecideBlockedReason { get; set; }
    }

    public class EnWaiverQuery
    {
        public int? HandoverId { get; set; }
        public int? Status { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnWaiverRequestDto
    {
        public int HandoverId { get; set; }
        public string Reason { get; set; }
        public DateTime DebtDueDate { get; set; }
    }

    /// <summary>Decision: approve or reject (the CEO only, and not the one who requested it).</summary>
    public class EnWaiverDecisionDto
    {
        public int Id { get; set; }
        public string Decision { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class EnHandoverDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int Classification { get; set; }
        public string ClassificationText { get; set; }
        public int Round { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string PreparedByName { get; set; }
        public bool PreparedByAgent { get; set; }
        public bool Complete { get; set; }
        public int MissingRequiredCount { get; set; }
        public int RequiredCount { get; set; }
        public bool PlatformDossierConfirmed { get; set; }

        /// <summary>The one Persian message of the lock: how many documents are missing and that only a CEO emergency waiver opens it. Empty when the lock is open.</summary>
        public string LockMessage { get; set; }
        public int? WaiverId { get; set; }
        public string WaiverNumber { get; set; }
        public string SubmittedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string AcceptedByName { get; set; }
        public string AcceptedAtText { get; set; }
        public string ReturnedByName { get; set; }
        public string ReturnedAtText { get; set; }
        public string ReturnReason { get; set; }
        public string ReleasedByName { get; set; }
        public string ReleasedAtText { get; set; }
        public string Note { get; set; }
        public string VoidReason { get; set; }
        public bool TouchedByMe { get; set; }
        public List<EnHandoverLineDto> Lines { get; set; } = new List<EnHandoverLineDto>();
        public List<EnDistributionDto> Distribution { get; set; } = new List<EnDistributionDto>();
        public List<EnDebtDto> Debts { get; set; } = new List<EnDebtDto>();
        public EnWaiverDto Waiver { get; set; }
        public string RowVersion { get; set; }

        public bool CanSubmit { get; set; }
        public string SubmitBlockedReason { get; set; }
        public bool CanRequestWaiver { get; set; }
        public string RequestWaiverBlockedReason { get; set; }
        public bool CanAccept { get; set; }
        public string AcceptBlockedReason { get; set; }
        public bool CanReturn { get; set; }
        public string ReturnBlockedReason { get; set; }
        public bool CanRelease { get; set; }
        public string ReleaseBlockedReason { get; set; }
        public bool CanVoid { get; set; }
        public string VoidBlockedReason { get; set; }
    }

    /// <summary>Action: draft (CaseId: the next round), refresh, submit, accept, return, release, void, distribute (agent), acknowledge (DistributionId), settle (agent: settles the debts whose documents now exist).</summary>
    public class EnHandoverActionDto
    {
        public int? Id { get; set; }
        public int? CaseId { get; set; }
        public int? DistributionId { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }
}
