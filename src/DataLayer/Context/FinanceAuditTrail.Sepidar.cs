using System;
using F = Entities.Model.NasbNiro.Finance;

namespace DataLayer.Context
{
    /// <summary>
    /// Sepidar transfer (per document): the switches of the transferable kinds, the queue items and the attempts join the tamper-evident chain (Package D mechanism): who switched a kind on or off, who approved, rejected,
    /// cancelled or retried an item, and what the sender answered. The payload is not written to the chain (its SHA-256 is): a payroll total is as private as the payroll itself and the trail is read by the auditor too.
    /// The error text and the retry clock are left out to keep the volume down. The tables are new, so they are not part of the go-live baseline. In its own file so that it merges cleanly with the other packages.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddSepidarTransfer()
        {
            AuditPd(typeof(F.SpTransferType), new[] { "Code", "Title", "IsEnabled", "RequiresApproval", "ChangedByUserId", "ChangedAt" }, "نوع قابل انتقال به سپیدار");
            AuditPd(typeof(F.SpOutboxItem), new[]
            {
                "TransferTypeCode", "SourceType", "SourceId", "SourceNumber", "SchemaVersion", "PayloadHash", "IsCorrection", "CorrectsItemId", "SupersededByItemId", "Status", "PreparedByAgent", "PreparedByUserId", "PreparedAt",
                "PayrollRestricted", "ApprovedByUserId", "ApprovedAt", "RejectedByUserId", "RejectedAt", "RejectReason", "CancelledByUserId", "CancelledAt", "CancelReason", "AttemptCount", "ExternalReference", "SentAt",
            }, "قلم صف انتقال به سپیدار");
            AuditPd(typeof(F.SpOutboxAttempt), new[] { "ItemId", "AttemptNumber", "StartedAt", "FinishedAt", "Outcome", "SenderName", "ExternalReference" }, "تلاش ارسال به سپیدار");
        }
    }
}
