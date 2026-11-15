using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Sepidar transfer (per document): the kinds that can be transferred, the queue, the decisions and the summary.

    public class SpTransferTypeDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public bool IsEnabled { get; set; }
        public bool RequiresApproval { get; set; }
        public string Note { get; set; }

        /// <summary>Only documents finalised on or after this date are queued.</summary>
        public DateTime? FromDate { get; set; }

        /// <summary>The old monthly package kinds that carry the same data (the kind cannot be switched on while one of them covers the period).</summary>
        public string OverlapNote { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public string ChangedByName { get; set; }
        public string RowVersion { get; set; }
    }

    public class SpTypeSwitchDto
    {
        public string Code { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>Must stay true: switching the approval off is refused for now.</summary>
        public bool? RequiresApproval { get; set; }
        public string Note { get; set; }

        /// <summary>Required when switching on: the first day of the documents that are transferred.</summary>
        public DateTime? FromDate { get; set; }
        public string RowVersion { get; set; }
    }

    public class SpOutboxQuery : PaginationParams
    {
        public SpOutboxStatus? Status { get; set; }
        public string TypeCode { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>Part of the document number.</summary>
        public string Search { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public int? Id { get; set; }
    }

    public class SpOutboxItemDto
    {
        public int Id { get; set; }
        public string TransferTypeCode { get; set; }
        public string TransferTypeTitle { get; set; }
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public string Title { get; set; }
        public SpOutboxStatus Status { get; set; }
        public string StatusTitle { get; set; }
        public bool IsCorrection { get; set; }
        public int? CorrectsItemId { get; set; }
        public int? SupersededByItemId { get; set; }
        public string PayloadHash { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public string PreparedByAgent { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectedByName { get; set; }
        public string RejectReason { get; set; }
        public string CancelledByName { get; set; }
        public string CancelReason { get; set; }
        public int AttemptCount { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public string LastError { get; set; }
        public string ExternalReference { get; set; }
        public DateTimeOffset? SentAt { get; set; }
        public string RowVersion { get; set; }

        /// <summary>For the viewer: may approve or reject this item now (role, status, maker rule), and the server's reason when not.</summary>
        public bool CanDecide { get; set; }
        public string CannotDecideReason { get; set; }
        public bool CanFix { get; set; }
        public string CannotFixReason { get; set; }
    }

    public class SpOutboxAttemptDto
    {
        public int AttemptNumber { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public SpAttemptOutcome Outcome { get; set; }
        public string Error { get; set; }
        public string SenderName { get; set; }
        public string ExternalReference { get; set; }
    }

    public class SpOutboxItemDetailDto : SpOutboxItemDto
    {
        /// <summary>The payload, pretty printed.</summary>
        public string PayloadPretty { get; set; }
        public int SchemaVersion { get; set; }
        public List<SpOutboxAttemptDto> Attempts { get; set; } = new List<SpOutboxAttemptDto>();
    }

    public class SpDecideDto
    {
        public int Id { get; set; }

        /// <summary>"approve" or "reject".</summary>
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class SpBulkRowDto
    {
        public int Id { get; set; }
        public string RowVersion { get; set; }
    }

    public class SpBulkApproveDto
    {
        public List<SpBulkRowDto> Items { get; set; } = new List<SpBulkRowDto>();
    }

    public class SpBulkRowResultDto
    {
        public int Id { get; set; }
        public bool Approved { get; set; }
        public string Message { get; set; }
    }

    public class SpBulkResultDto
    {
        public int Requested { get; set; }
        public int Approved { get; set; }
        public int Refused { get; set; }
        public List<SpBulkRowResultDto> Rows { get; set; } = new List<SpBulkRowResultDto>();
    }

    public class SpFixDto
    {
        public int Id { get; set; }

        /// <summary>"retry" or "cancel".</summary>
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class SpStatusCountDto
    {
        public SpOutboxStatus Status { get; set; }
        public string StatusTitle { get; set; }
        public int Count { get; set; }
    }

    public class SpOutboxSummaryDto
    {
        public List<SpStatusCountDto> Counts { get; set; } = new List<SpStatusCountDto>();
        public int Total { get; set; }
        public string SenderName { get; set; }
        public bool IsConfigured { get; set; }
        public int MaxAttempts { get; set; }
        public DateTimeOffset? LastSuccessfulSendAt { get; set; }
        public int EnabledTypes { get; set; }
    }
}
