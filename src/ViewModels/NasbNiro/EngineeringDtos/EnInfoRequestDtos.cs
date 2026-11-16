using System;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnInfoQuery
    {
        public int? CaseId { get; set; }
        public int? HandoverId { get; set; }
        public int? Status { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>A unit asks the PMO for more than its notice carries.</summary>
    public class EnInfoAskDto
    {
        public int? HandoverId { get; set; }

        /// <summary>Required when no handover is given.</summary>
        public int? CaseId { get; set; }

        /// <summary>The distribution unit of the asker: 1 QA, 2 QC, 3 production, 4 procurement, 5 warehouse, 6 planning.</summary>
        public int Unit { get; set; }

        /// <summary>At least 15 characters.</summary>
        public string Question { get; set; }
    }

    /// <summary>answer (the PMO, with Answer), close (the asking unit), void (the asker, while the question is open, with Answer as the reason).</summary>
    public class EnInfoActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Answer { get; set; }
        public string RowVersion { get; set; }
    }

    public class EnInfoRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int? HandoverId { get; set; }
        public string HandoverNumber { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int Unit { get; set; }
        public string UnitText { get; set; }
        public string Question { get; set; }
        public string AskedByName { get; set; }
        public string AskedAtText { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string Answer { get; set; }
        public string AnsweredByName { get; set; }
        public string AnsweredAtText { get; set; }
        public string ClosedByName { get; set; }
        public string ClosedAtText { get; set; }

        /// <summary>A Persian reminder for the PMO: the answer must not contain a price (no automatic check is possible).</summary>
        public string AnswerHint { get; set; }
        public string RowVersion { get; set; }
        public bool CanAnswer { get; set; }
        public string AnswerBlockedReason { get; set; }
        public bool CanClose { get; set; }
        public string CloseBlockedReason { get; set; }
        public bool CanVoid { get; set; }
        public string VoidBlockedReason { get; set; }
    }
}
