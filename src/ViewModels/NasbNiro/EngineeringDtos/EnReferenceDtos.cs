using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnReferenceQuery
    {
        public int? Status { get; set; }
        public string Contains { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnReferenceLineDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; } = true;
        public bool TenderStage { get; set; }
        public bool ClientSubmittal { get; set; }
        public string StandardClause { get; set; }
        public string Note { get; set; }

        /// <summary>The units that need the document (comma-separated: Qa, Qc, Production, Procurement, Warehouse, Planning); empty = all units. Editable while the list is a draft.</summary>
        public string Units { get; set; }
    }

    public class EnReferenceListDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string StandardSource { get; set; }
        public int Revision { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public bool IsCurrent { get; set; }
        public int PreparedByUserId { get; set; }
        public string PreparedByName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string SubmittedAtText { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedAtText { get; set; }
        public string Note { get; set; }
        public string VoidReason { get; set; }
        public int LineCount { get; set; }
        public int RequiredCount { get; set; }
        public int TenderStageCount { get; set; }
        public bool TouchedByMe { get; set; }
        public List<EnReferenceLineDto> Lines { get; set; } = new List<EnReferenceLineDto>();
        public string RowVersion { get; set; }

        public bool CanEdit { get; set; }
        public string EditBlockedReason { get; set; }
        public bool CanSubmit { get; set; }
        public string SubmitBlockedReason { get; set; }
        public bool CanApprove { get; set; }
        public string ApproveBlockedReason { get; set; }
        public bool CanReturn { get; set; }
        public string ReturnBlockedReason { get; set; }
        public bool CanRevise { get; set; }
        public string ReviseBlockedReason { get; set; }
        public bool CanVoid { get; set; }
        public string VoidBlockedReason { get; set; }
    }

    public class EnSaveReferenceDto
    {
        /// <summary>Empty = a new list (revision 1, or the next revision when the code exists).</summary>
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string StandardSource { get; set; }
        public string Note { get; set; }
        /// <summary>The lines (JSON API). Null on an existing list = a header save: the lines stay as they are. A new list needs at least one line.</summary>
        public List<EnReferenceLineDto> Lines { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action: submit, approve, return, revise or void.</summary>
    public class EnReferenceActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>The code and title of a standard that has an approved reference list.</summary>
    public class EnReferenceCodeDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
    }
}
