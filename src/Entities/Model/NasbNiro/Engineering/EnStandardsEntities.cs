using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Engineering
{
    // ENG-E1 (standards deviation, standard-gap report): an instruction of a person that contradicts a requirement of a standard is not executed until the head of engineering AND the CEO both approve (two distinct persons).

    public enum EnDeviationKind { NotApplicable = 1, ReferenceLoosening = 2, Waiver = 3 }

    public enum EnDeviationStatus { Requested = 0, ManagerApproved = 1, Approved = 2, Rejected = 3, Void = 4 }

    /// <summary>A request to deviate from a requirement of a standard (a Required line of an Approved reference list). Kind Waiver is the read model of the emergency waiver of the handover lock (never stored here).</summary>
    public class EnDeviation : BaseEntity
    {
        public string Number { get; set; }
        public EnDeviationKind Kind { get; set; }

        /// <summary>The record the deviation is about: EnPlatformDocument (a dossier line) or EnReferenceList (a revision that removes or loosens a requirement).</summary>
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public int? CaseId { get; set; }
        public int? ProductId { get; set; }
        public string StandardCode { get; set; }
        public string StandardClause { get; set; }

        /// <summary>The requirement (line title) that is deviated from.</summary>
        public string Requirement { get; set; }

        /// <summary>The reason of the person who instructs it (at least 30 characters).</summary>
        public string Instruction { get; set; }

        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public int? ManagerApprovedByUserId { get; set; }
        public DateTimeOffset? ManagerApprovedAt { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public EnDeviationStatus Status { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>SHA-256 of the content the approvals bind to; a change of the line (or the list) voids the request.</summary>
        public string ContentHash { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
