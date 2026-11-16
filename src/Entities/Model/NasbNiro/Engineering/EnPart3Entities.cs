using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Engineering
{
    // ENG-E1 part 3 (S9, S10): the questions of the units to the PMO, and the registration of a new design as a platform. Records are never deleted physically.

    public enum EnRegStatus { Proposed = 0, Approved = 1, Rejected = 2, Void = 3 }

    public enum EnInfoStatus { Open = 0, Answered = 1, Closed = 2, Void = 3 }

    /// <summary>A unit that needs more than its notice asks the PMO; the PMO answers (never with a price), the asking unit closes. A question waits for its answer without expiring.</summary>
    public class EnInfoRequest : BaseEntity
    {
        public string Number { get; set; }
        public int? HandoverId { get; set; }
        public int CaseId { get; set; }
        public int ContractId { get; set; }
        public EnUnit Unit { get; set; }
        public string Question { get; set; }
        public int AskedByUserId { get; set; }
        public DateTimeOffset AskedAt { get; set; }
        public EnInfoStatus Status { get; set; }
        public string Answer { get; set; }
        public int? AnsweredByUserId { get; set; }
        public DateTimeOffset? AnsweredAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A product of a New-design case becomes a platform (a dossier against a standard) only on the proposal of the head of engineering and the approval of the CEO. The approval binds to the content hash.</summary>
    public class EnPlatformRegistration : BaseEntity
    {
        public string Number { get; set; }
        public int CaseId { get; set; }
        public int ProductId { get; set; }
        public string ReferenceCode { get; set; }
        public string StandardBasis { get; set; }
        public string Rationale { get; set; }
        public string ContentHash { get; set; }
        public EnRegStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public DateTimeOffset ProposedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string DecisionNote { get; set; }
        public int? DossierId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
