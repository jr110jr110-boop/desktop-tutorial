using System;
using E = Entities.Model.NasbNiro.EngChange;
using P = Entities.Model.NasbNiro.Production;

namespace DataLayer.Context
{
    /// <summary>
    /// Package A2 (engineering change): the change requests, change orders (with the impact snapshot and its SHA-256), their changes, dispositions and tasks join the tamper-evident chain (Package D mechanism).
    /// A direct edit of a disposition, an effectivity point, an approval or the impact snapshot in the database is then a row of the chain that nobody wrote.
    /// In its own file so that it merges cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddEngChange()
        {
            AuditPd(typeof(E.EcRequest), new[] { "Number", "Title", "Reason", "Description", "Classification", "Urgency", "Status", "RequestedByUserId", "SubmittedAt", "DecidedByUserId", "DecidedAt", "DecisionNote", "Deleted" }, "درخواست تغییر مهندسی (ECR)");
            AuditPd(typeof(E.EcRequestTarget), new[] { "RequestId", "Kind", "PartNumber", "ProductId", "BomHeaderId", "OpcRoutingId", "DeliverableId", "RoutingRevisionId", "Reference", "Note" }, "مورد متاثر از درخواست تغییر");
            AuditPd(typeof(E.EcOrder), new[] { "Number", "RequestId", "Title", "Summary", "Status", "EffectiveFromOrderNumber", "EffectiveFromSerial", "EffectiveFromDate", "EffectivityNote", "AffectedParts", "ImpactJson", "ImpactSha256", "ImpactAt", "ImpactByUserId", "ContentSha256", "SubmittedAt", "EngineeringApprovedByUserId", "EngineeringApprovedAt", "EngineeringNote", "QaApprovedByUserId", "QaApprovedAt", "QaNote", "ApprovedAt", "RejectedByUserId", "RejectedAt", "RejectReason", "CancelReason", "ClosedAt", "ClosedByUserId", "EditorUserIds", "Deleted" }, "دستور تغییر مهندسی (ECO)");
            AuditPd(typeof(E.EcOrderChange), new[] { "OrderId", "Kind", "Action", "TargetId", "OldTargetId", "Code", "PartNumber", "OldRevision", "NewRevision", "Reference", "Note" }, "بازنگری نام‌برده در دستور تغییر");
            AuditPd(typeof(E.EcDispositionItem), new[] { "OrderId", "Category", "ImpactKey", "Text", "PartNumber", "Quantity", "Disposition", "Note", "OwnerUserId", "DueDate", "DecidedByUserId", "DecidedAt", "Stale" }, "تکلیف مورد متاثر از تغییر");
            AuditPd(typeof(E.EcTask), new[] { "OrderId", "DispositionItemId", "Title", "Disposition", "OwnerUserId", "DueDate", "Status", "DoneAt", "DoneByUserId", "DoneNote", "VerifiedAt", "VerifiedByUserId", "VerifyNote", "Deleted" }, "وظیفه اجرای تغییر مهندسی");

            // the production order says where its components come from (baseline or the legacy fallback)
            ExtendInspection(typeof(P.MfProductionOrder), "ComponentSource", "ComponentNote");
        }
    }
}
