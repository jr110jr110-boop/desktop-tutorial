using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class DistributionDto
    {
        public int Id { get; set; }
        public int? RecipientUserId { get; set; }
        public string RecipientName { get; set; }
        public string RecipientUnit { get; set; }
        public DateTimeOffset SentAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
    }

    public class DocumentRevisionDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Revision { get; set; }
        public string ChangeSummary { get; set; }
        public Guid? FileDocumentId { get; set; }
        public ControlledDocumentStatus Status { get; set; }
        public int PreparedByUserId { get; set; }
        public string PreparedByName { get; set; }
        public string ReviewedByName { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public List<DistributionDto> Distributions { get; set; } = new List<DistributionDto>();
    }

    public class ControlledDocumentListDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public ControlledDocumentType Type { get; set; }
        public string Department { get; set; }
        public string Title { get; set; }
        public string CurrentRevision { get; set; }
        public ControlledDocumentStatus Status { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string RetentionClass { get; set; }
        public string ResponsibleUnit { get; set; }
    }

    public class ControlledDocumentDto : ControlledDocumentListDto
    {
        public List<DocumentRevisionDto> Revisions { get; set; } = new List<DocumentRevisionDto>();
    }

    public class ControlledDocumentQuery : PaginationParams
    {
        public ControlledDocumentType? Type { get; set; }
        public ControlledDocumentStatus? Status { get; set; }
        public string Department { get; set; }
        public string Contains { get; set; }
    }

    public class SaveControlledDocumentDto
    {
        public int? Id { get; set; }
        public ControlledDocumentType Type { get; set; }
        public string Department { get; set; }

        /// <summary>The serial part of the code; when empty the next free number of the type and department is used.</summary>
        public int? Serial { get; set; }
        public string Title { get; set; }
        public string RetentionClass { get; set; }
        public string ResponsibleUnit { get; set; }
    }

    public class CodeCheckDto
    {
        public string Code { get; set; }
        public bool Exists { get; set; }
        public string NextFreeCode { get; set; }
    }

    public class SaveRevisionDto
    {
        public int DocumentId { get; set; }
        public string Revision { get; set; }
        public string ChangeSummary { get; set; }
        public Guid? FileDocumentId { get; set; }
    }

    public class RevisionActionDto
    {
        public int Id { get; set; }

        /// <summary>review, approve, obsolete, distribute, acknowledge</summary>
        public string Action { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public List<int> RecipientUserIds { get; set; } = new List<int>();
        public List<string> RecipientUnits { get; set; } = new List<string>();
        public int? DistributionId { get; set; }
    }

    public class RetentionRuleDto
    {
        public int Id { get; set; }
        public string RecordType { get; set; }
        public int? Years { get; set; }
        public bool Permanent { get; set; }
        public string Source { get; set; }
    }

    public class SaveRetentionRuleDto
    {
        public string RecordType { get; set; }
        public int? Years { get; set; }
        public bool Permanent { get; set; }
        public string Source { get; set; }
    }
}
