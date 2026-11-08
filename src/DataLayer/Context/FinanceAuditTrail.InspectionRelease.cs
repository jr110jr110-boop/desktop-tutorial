using System;
using System.Linq;
using Q = Entities.Model.NasbNiro.Quality;

namespace DataLayer.Context
{
    /// <summary>
    /// Package B (inspection and release integrity): the characteristic lists, the «بدون نقشه» flags, the first article inspections and the as-built records join the tamper-evident chain (Package D mechanism).
    /// A direct edit of a nominal, a tolerance, a flag status, an approval or an as-built hash in the database is then a row of the chain that nobody wrote.
    /// In its own file so that it merges cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddInspectionRelease()
        {
            AuditPd(typeof(Q.QcCharacteristicList), new[] { "DeliverableId", "RevisionId", "DocumentNumber", "RevisionCode", "Version", "Status", "Reason", "SupersedesListId", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "ReleasedAt", "ContentHash", "ReturnNote", "VoidReason", "EditorUserIds", "Deleted" }, "فهرست مشخصه‌های نقشه");
            AuditPd(typeof(Q.QcCharacteristic), new[] { "ListId", "RowNumber", "No", "Name", "Nominal", "TolPlus", "TolMinus", "Unit", "Class", "InstrumentClass", "Method", "SamplePercent", "OperationSeq", "StageCode2" }, "مشخصه بازرسی نقشه");
            AuditPd(typeof(Q.NoDrawingFlag), new[] { "ItemId", "PartNumber", "Reason", "Status", "DecidedByUserId", "DecidedAt", "DecisionNote", "VoidedByUserId", "VoidedAt", "VoidReason", "Deleted" }, "پرچم «بدون نقشه»");
            AuditPd(typeof(Q.QcFirstArticle), new[] { "Number", "ItemId", "PartNumber", "WorkOrderId", "ProductionOrderId", "Fingerprint", "CharacteristicListId", "RoutingRevisionId", "Trigger", "Status", "QcRecordId", "MeasuredByUserId", "MeasuredAt", "QcAcceptedByUserId", "QcAcceptedAt", "QcAcceptNote", "QaApprovedByUserId", "QaApprovedAt", "QaApproveNote", "RejectedByUserId", "RejectedAt", "RejectReason", "VoidReason", "Note", "Deleted" }, "بازرسی نمونه اول");
            AuditPd(typeof(Q.AsBuiltRecord), new[] { "WorkOrderId", "ReleaseNoteId", "ItemId", "SerialUnitId", "SerialNumber", "Version", "Status", "Reason", "ProductCode", "BomText", "Sha256", "SupersededByVersion", "Deleted" }, "پیکربندی ساخته‌شده (As-Built)");

            // the existing inspection rows now also say where their measurements came from
            ExtendInspection(typeof(Q.QcRecord), "DrawingRevisionId", "CharacteristicListId");
            ExtendInspection(typeof(Q.QcMeasurement), "CharacteristicId", "CharacteristicNo");
        }

        /// <summary>Adds columns to a type that Package D already audits (without listing the type twice in the coverage report).</summary>
        private static void ExtendInspection(Type type, params string[] more)
        {
            if (Audited.TryGetValue(type, out string[] have) && have != null)
                Audited[type] = have.Concat(more.Where(c => !have.Contains(c))).ToArray();
        }
    }
}
