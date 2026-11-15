using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Finance
{
    // Sepidar transfer (per document). All finance work is done inside Orchestra under human supervision; only the data kinds that the owner enables are passed to the external accounting software.
    // The sender is pluggable (none, a file drop, later the API): until one is configured, approved items wait in the queue. Tables start with "Sp".
    // The older package outbox (FnOutboxPackage: monthly tax and insurance lists) is a different thing and stays as it is.

    public enum SpOutboxStatus
    {
        /// <summary>Prepared, not yet put in front of the approver.</summary>
        Draft = 1,
        AwaitingApproval = 2,
        /// <summary>Approved by a person: ready for the sender.</summary>
        Approved = 3,
        Sending = 4,
        Sent = 5,
        Failed = 6,
        Rejected = 7,
        Cancelled = 8,
        /// <summary>The source document changed before it was sent: a newer item took its place.</summary>
        Superseded = 9,
    }

    public enum SpAttemptOutcome
    {
        Started = 0,
        Succeeded = 1,
        Failed = 2,
    }

    /// <summary>One kind of document that can be transferred. Seeded; every kind is OFF until the owner enables it.</summary>
    public class SpTransferType
    {
        public int Id { get; set; }

        /// <summary>VOUCHER, SALES_INVOICE, PURCHASE_INVOICE, RECEIPT, PAYMENT or PAYROLL_SUMMARY (unique).</summary>
        public string Code { get; set; }

        public string Title { get; set; }

        /// <summary>Default false: the owner has not delivered the list of what goes to Sepidar yet. Nothing is ever queued for a disabled kind.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Always true for now (a person approves every item); switching it off is refused.</summary>
        public bool RequiresApproval { get; set; } = true;

        public string Note { get; set; }

        /// <summary>Only documents finalised on or after this date are queued (required when the kind is switched on): the history before it is not sent by this channel.</summary>
        public DateTime? FromDate { get; set; }

        public int? ChangedByUserId { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>The snapshot of one document, prepared for the external software, and where it stands.</summary>
    public class SpOutboxItem
    {
        public int Id { get; set; }

        public string TransferTypeCode { get; set; }

        /// <summary>The CLR type name of the Orchestra document (FnVoucher, FnSalesInvoice, ...) and its id.</summary>
        public string SourceType { get; set; }
        public int SourceId { get; set; }

        /// <summary>The number of the document, for people.</summary>
        public string SourceNumber { get; set; }
        public string Title { get; set; }

        /// <summary>The neutral, versioned snapshot (a JSON object with schemaVersion).</summary>
        public string PayloadJson { get; set; }
        public int SchemaVersion { get; set; }

        /// <summary>SHA-256 (hex) of the canonical payload.</summary>
        public string PayloadHash { get; set; }

        /// <summary>The row version of the source when the item was prepared: the cheap way to see that the source changed.</summary>
        public byte[] SourceRowVersion { get; set; }

        /// <summary>The source document was already sent once and changed after that: this item corrects it.</summary>
        public bool IsCorrection { get; set; }
        public int? CorrectsItemId { get; set; }
        public int? SupersededByItemId { get; set; }

        public SpOutboxStatus Status { get; set; }

        /// <summary>The seat that prepared the item (AG-FIN) and its service user.</summary>
        public string PreparedByAgent { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }

        /// <summary>The people who made or edited the source document (",5,7,"): none of them approves the item.</summary>
        public string SourceMakerUserIds { get; set; }

        /// <summary>A payroll total that covers fewer people than the group minimum: only the readers of payroll see the item.</summary>
        public bool PayrollRestricted { get; set; }

        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public int? CancelledByUserId { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelReason { get; set; }

        public int AttemptCount { get; set; }

        /// <summary>The attempt number at the last human retry: the automatic attempts are counted from here (so a retry gets a full set of attempts again).</summary>
        public int RetryBaseline { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }

        /// <summary>A short Persian text (never a stack trace or a secret).</summary>
        public string LastError { get; set; }

        /// <summary>What the external software returned (or the file name of the file drop).</summary>
        public string ExternalReference { get; set; }
        public DateTimeOffset? SentAt { get; set; }

        public ICollection<SpOutboxAttempt> Attempts { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One try to send an item.</summary>
    public class SpOutboxAttempt
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public SpOutboxItem Item { get; set; }
        public int AttemptNumber { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public SpAttemptOutcome Outcome { get; set; }
        public string Error { get; set; }
        public string SenderName { get; set; }
        public string ExternalReference { get; set; }
    }
}
