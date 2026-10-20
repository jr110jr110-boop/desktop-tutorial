using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class AuditProgramDto
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public string Scope { get; set; }
        public string Note { get; set; }
        public int AuditCount { get; set; }
    }

    public class SaveAuditProgramDto
    {
        public int? Id { get; set; }
        public int Year { get; set; }
        public string Scope { get; set; }
        public string Note { get; set; }
    }

    public class AuditFindingDto
    {
        public int Id { get; set; }
        public int AuditId { get; set; }
        public string Clause { get; set; }
        public AuditFindingType Type { get; set; }
        public string Evidence { get; set; }
        public int? CapaId { get; set; }
        public string CapaNumber { get; set; }
        public CapaStatus? CapaStatus { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class AuditListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int? ProgramId { get; set; }
        public string AuditedUnit { get; set; }
        public string Criteria { get; set; }
        public DateTime PlannedDate { get; set; }
        public string LeadAuditorName { get; set; }
        public AuditStatus Status { get; set; }
        public int FindingCount { get; set; }
        public bool Overdue { get; set; }
    }

    public class AuditDto : AuditListDto
    {
        public int LeadAuditorUserId { get; set; }
        public List<int> TeamUserIds { get; set; } = new List<int>();
        public List<string> TeamNames { get; set; } = new List<string>();
        public Guid? ReportDocumentId { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public List<AuditFindingDto> Findings { get; set; } = new List<AuditFindingDto>();
    }

    public class AuditQuery : PaginationParams
    {
        public AuditStatus? Status { get; set; }
        public int? ProgramId { get; set; }
        public string Contains { get; set; }
    }

    public class SaveAuditDto
    {
        public int? Id { get; set; }
        public int? ProgramId { get; set; }
        public string AuditedUnit { get; set; }
        public string Criteria { get; set; }
        public DateTime PlannedDate { get; set; }
        public int LeadAuditorUserId { get; set; }
        public List<int> TeamUserIds { get; set; } = new List<int>();
    }

    public class AuditActionDto
    {
        public int Id { get; set; }

        /// <summary>done, close</summary>
        public string Action { get; set; }
        public Guid? ReportDocumentId { get; set; }
    }

    public class SaveAuditFindingDto
    {
        public int AuditId { get; set; }
        public int? Id { get; set; }
        public string Clause { get; set; }
        public AuditFindingType Type { get; set; }
        public string Evidence { get; set; }
        public DateTime? DueDate { get; set; }

        /// <summary>For a nonconformance an existing CAPA may be named; otherwise the system drafts one.</summary>
        public int? CapaId { get; set; }
    }

    // ------------------------------------------------------------------ management review

    public class ReviewInputDto
    {
        public int Id { get; set; }
        public ReviewInputCategory Category { get; set; }
        public string CategoryName { get; set; }
        public string Text { get; set; }
        public bool AutoDrafted { get; set; }
    }

    public class ReviewDecisionDto
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public int? ResponsibleUserId { get; set; }
        public string ResponsibleName { get; set; }
        public DateTime? DueDate { get; set; }
        public int? CapaId { get; set; }
        public string CapaNumber { get; set; }
    }

    public class ReviewListDto
    {
        public int Id { get; set; }
        public DateTime ReviewDate { get; set; }
        public ReviewStatus Status { get; set; }
        public int DecisionCount { get; set; }
        public Guid? MinutesDocumentId { get; set; }
    }

    public class ReviewDto : ReviewListDto
    {
        public string Attendees { get; set; }
        public List<ReviewInputDto> Inputs { get; set; } = new List<ReviewInputDto>();
        public List<ReviewDecisionDto> Decisions { get; set; } = new List<ReviewDecisionDto>();
    }

    public class ReviewQuery : PaginationParams
    {
        public ReviewStatus? Status { get; set; }
    }

    public class CreateReviewDto
    {
        public DateTime? ReviewDate { get; set; }
        public string Attendees { get; set; }
    }

    public class SaveReviewInputDto
    {
        public int ReviewId { get; set; }
        public ReviewInputCategory Category { get; set; }
        public string Text { get; set; }
        public string Attendees { get; set; }
    }

    public class SaveReviewDecisionDto
    {
        public int ReviewId { get; set; }
        public int? Id { get; set; }
        public string Text { get; set; }
        public int? ResponsibleUserId { get; set; }
        public DateTime? DueDate { get; set; }

        /// <summary>The system drafts a CAPA (source: management review) for QA to assign.</summary>
        public bool CreateCapa { get; set; }
    }

    public class ReviewActionDto
    {
        public int Id { get; set; }

        /// <summary>hold, close, redraft</summary>
        public string Action { get; set; }
        public Guid? MinutesDocumentId { get; set; }
    }
}
