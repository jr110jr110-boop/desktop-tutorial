using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.OpsData
{
    // PB8: operational data control. Tables start with "Od". Foreign keys to documents are plain ids (the documents are of five different tables) and the rows are history:
    // a finding is never deleted, it is resolved, verified or superseded.

    /// <summary>The unit that owns the data (and whose supervisor decides on exceptions).</summary>
    public enum OpsUnit
    {
        Warehouse = 1,
        Production = 2,
        Quality = 3,
        Finance = 4,
    }

    public enum OpsSeverity
    {
        Warning = 1,
        Block = 2,
    }

    public enum OpsSource
    {
        /// <summary>Found by the real-time gate at entry or confirmation.</summary>
        Gate = 1,

        /// <summary>Found by the daily OPS-DATA seat.</summary>
        Seat = 2,
    }

    public enum OpsFindingStatus
    {
        Open = 1,

        /// <summary>Resolved with a corrective document (cancel + new document).</summary>
        Resolved = 2,

        /// <summary>A supervisor (not the creator) confirmed the data is right.</summary>
        Verified = 3,

        /// <summary>The rule no longer fires (the entry was corrected before confirmation) or the document was cancelled.</summary>
        Superseded = 4,
    }

    public enum OpsExceptionStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,

        /// <summary>The entry was changed after the approval: the exception no longer covers it.</summary>
        Voided = 3,
    }

    /// <summary>One implausible or baseline-violating value of one document (line), found by a rule. One row per (document, line, rule): re-evaluation updates it.</summary>
    public class OpsFinding : BaseEntity
    {
        public OpsUnit Unit { get; set; }

        /// <summary>InventoryDocument, StockCount, MfOperation, QcInspection, SupplierInvoice, QcRecord.</summary>
        public string DocumentType { get; set; }

        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }

        /// <summary>The line (or booking / measurement) of the document; empty for a finding on the document as a whole.</summary>
        public int? LineId { get; set; }

        public int? RowNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemTitle { get; set; }

        /// <summary>PB8 fix round: tells apart findings of the same rule on the same row and item (weight / quantity / price, typed price / history); null otherwise.</summary>
        public string Field { get; set; }

        public string RuleCode { get; set; }
        public OpsSeverity Severity { get; set; }
        public OpsSource Source { get; set; }
        public OpsFindingStatus Status { get; set; }

        /// <summary>Persian text with the numbers (entered, reference, deviation).</summary>
        public string Message { get; set; }

        /// <summary>The same without numbers, for readers who may not see prices.</summary>
        public string MessageMasked { get; set; }

        public decimal? EnteredValue { get; set; }
        public decimal? ReferenceValue { get; set; }
        public decimal? DeviationPercent { get; set; }

        /// <summary>The finding is about a price or an amount in rial (masked for roles without price access).</summary>
        public bool IsMoney { get; set; }

        public DateTimeOffset DetectedAt { get; set; }

        /// <summary>The person who entered, edited or confirmed the document at the time (the exception and verification are never theirs).</summary>
        public int? EnteredByUserId { get; set; }

        public int? ExceptionId { get; set; }

        public DateTimeOffset? ResolvedAt { get; set; }
        public int? ResolvedByUserId { get; set; }
        public string ResolutionNote { get; set; }

        /// <summary>The corrective document of a resolved finding (same document type).</summary>
        public int? CorrectiveDocumentId { get; set; }
    }

    /// <summary>The exception a user asks for when the gate blocks an entry: a justification by the user and a decision by another supervisor.</summary>
    public class OpsException : BaseEntity
    {
        public OpsUnit Unit { get; set; }
        public string DocumentType { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public int? LineId { get; set; }
        public int? RowNumber { get; set; }

        /// <summary>PB8 fix round: an exception is keyed by document + row + item + rule + field + value, not by the id of the line.</summary>
        public int? ItemId { get; set; }
        public string Field { get; set; }
        public string RuleCode { get; set; }

        /// <summary>Concurrency token: two simultaneous decisions cannot both win.</summary>
        [Timestamp]
        public byte[] RowVersion { get; set; }

        public decimal? EnteredValue { get; set; }
        public decimal? ReferenceValue { get; set; }
        public decimal? DeviationPercent { get; set; }
        public bool IsMoney { get; set; }
        public string Message { get; set; }

        public string Justification { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        public OpsExceptionStatus Status { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionReason { get; set; }

        /// <summary>The gate let the confirmation pass on this exception.</summary>
        public DateTimeOffset? UsedAt { get; set; }
    }
}

namespace Entities.Model.NasbNiro.OpsData
{
    public enum OpsReadinessExceptionStatus
    {
        /// <summary>Waiting for the PMO and the CEO (both must approve).</summary>
        Pending = 0,
        Approved = 1,
        Rejected = 2,

        /// <summary>The baseline was created with it.</summary>
        Used = 3,
    }

    public enum OpsObligationStatus
    {
        Open = 0,
        Closed = 1,
    }

    /// <summary>
    /// A request to start a project although engineering information is missing (hard checklist items). It needs a justification and the approval of two other persons: the PMO and the CEO.
    /// </summary>
    public class OpsReadinessException : BaseEntity
    {
        /// <summary>Concurrency token: a simultaneous PMO and CEO approval cannot leave the request pending forever.</summary>
        [Timestamp]
        public byte[] RowVersion { get; set; }

        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }

        /// <summary>The hard items missing when it was requested, codes between commas. It covers these items only; a new gap needs a new request.</summary>
        public string ItemCodes { get; set; }

        public string Justification { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        public OpsReadinessExceptionStatus Status { get; set; }
        public int? PmoUserId { get; set; }
        public DateTimeOffset? PmoAt { get; set; }
        public string PmoNote { get; set; }
        public int? CeoUserId { get; set; }
        public DateTimeOffset? CeoAt { get; set; }
        public string CeoNote { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset? UsedAt { get; set; }
    }

    /// <summary>A checklist item that is still missing after the project started: tracked with an owner unit and a due date until it is complete.</summary>
    public class OpsReadinessObligation : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }
        public string ItemCode { get; set; }
        public string Title { get; set; }
        public string OwnerUnit { get; set; }

        /// <summary>True: a hard item (started on an exception). False: a soft item.</summary>
        public bool Hard { get; set; }

        public int MissingCount { get; set; }
        public string Detail { get; set; }
        public DateTime DueDate { get; set; }
        public int? ExceptionId { get; set; }
        public OpsObligationStatus Status { get; set; }
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public DateTimeOffset? LastNotifiedAt { get; set; }
        public DateTimeOffset? EscalatedAt { get; set; }
    }
}
