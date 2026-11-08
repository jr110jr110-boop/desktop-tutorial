using System;
using System.Collections.Generic;
using System.Linq;
using Q = Entities.Model.NasbNiro.Quality;
using Iv = Entities.Model.NasbNiro.Inventory;

namespace DataLayer.Context
{
    /// <summary>
    /// Package D (records integrity): the quality, manufacturing and warehouse evidence joins the tamper-evident chain. Kept in its own file (one block of entries, one block of labels) so that it merges
    /// cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>. A direct edit of a measured value, a heat number, a certificate, a calibration date or a qualification in the database
    /// is then a row of the chain that nobody wrote, and the nightly verification (<c>AuditIntegrityService</c>) reports it.
    /// Left out on purpose: free texts that hold a person's name (driver, receiver, buyer, operator names), the weighing and price quotes of a scrap sale, the snapshot of an ITP revision (a copy of rows that
    /// are audited one by one) and the unit price of a warehouse line (written as a "changed" marker, like every amount that most roles may not see).
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        /// <summary>Record types of this package, as they are written to <c>FnAuditLog.EntityType</c> (the CLR type name); used by the tests and by the coverage report.</summary>
        internal static IReadOnlyCollection<Type> RecordsIntegrityTypes => PdTypes;

        private static readonly List<Type> PdTypes = new List<Type>();

        private static void AuditPd(Type type, string[] fields, string label)
        {
            Audited[type] = fields;
            Labels[type] = label;
            PdTypes.Add(type);
        }

        /// <summary>Adds columns to an entry that an earlier release already made (kept apart so the old entry stays as it was).</summary>
        private static void ExtendPd(Type type, params string[] more)
        {
            if (Audited.TryGetValue(type, out string[] have) && have != null)
                Audited[type] = have.Concat(more.Where(c => !have.Contains(c))).ToArray();
            PdTypes.Add(type);
        }

        /// <summary>True for the inserts that are made in bulk by a copy from a template (their later changes are audited one by one).</summary>
        private static bool PdSkipsInsert(object entity) => entity is Q.ItpActivityParameter;

        private static void AddRecordsIntegrity()
        {
            // ---- quality records: inspections, measured values, test reports, certificates
            AuditPd(typeof(Q.QcRecord), new[] { "Number", "Type", "WorkOrderId", "ItpActivityId", "ItemId", "LotId", "SerialNumber", "Quantity", "Operation", "DrawingNumber", "DrawingRevision", "InspectorUserId", "SelfInspection", "RecordDate", "Result", "NcrId", "AttachmentDocumentId", "Note", "Deleted" }, "سابقه بازرسی کنترل کیفیت");
            AuditPd(typeof(Q.QcMeasurement), new[] { "RecordId", "Characteristic", "Nominal", "TolPlus", "TolMinus", "Measured", "Unit", "EquipmentId", "Pass" }, "اندازه‌گیری بازرسی");
            AuditPd(typeof(Q.TestReport), new[] { "Number", "Type", "WorkOrderId", "ItpActivityId", "ItemId", "SerialNumber", "ProcedureRef", "ProcedureRevision", "CodeEdition", "ExaminerUserId", "ExaminerQualificationId", "ReportDate", "Environment", "Status", "Result", "SubmittedAt", "ApprovedAt", "ApprovedByUserId", "RejectReason", "NcrId", "DocumentId", "Note", "Deleted" }, "گزارش آزمون");
            AuditPd(typeof(Q.TestReportValue), new[] { "ReportId", "Parameter", "Unit", "Min", "Max", "Measured", "Pass", "Source", "RequiresLimits" }, "مقدار گزارش آزمون");
            AuditPd(typeof(Q.TestReportIndication), new[] { "ReportId", "Location", "Type", "Size", "Orientation", "Accepted" }, "اندیکاسیون گزارش آزمون");
            AuditPd(typeof(Q.TestReportEquipment), new[] { "ReportId", "EquipmentId" }, "تجهیز گزارش آزمون");
            AuditPd(typeof(Q.TestReportWitness), new[] { "ReportId", "Party", "Name", "WitnessDate" }, "شاهد گزارش آزمون");
            AuditPd(typeof(Q.MaterialCertificate), new[] { "LotId", "Type", "Number", "Issuer", "DocumentId", "Summary", "Result", "VerifiedByUserId", "VerifiedAt", "VerificationNote", "Deleted" }, "گواهی مواد");
            AuditPd(typeof(Iv.QcInspection), new[] { "Number", "ReceiptDocumentId", "ReceiptLineId", "ItemId", "LotId", "WarehouseId", "LocationId", "Quantity", "SerialNumbers", "TargetWarehouseId", "TargetLocationId", "Status", "AcceptedQuantity", "RejectedQuantity", "RejectedSerialNumbers", "ReportNumber", "ReportDocumentId", "Note", "InspectedAt", "InspectedByUserId", "ReleaseDocumentId", "NcrId", "ReinspectionOfNcrId", "EquipmentIds", "MeasurementsJson", "Deleted" }, "بازرسی ورودی کالا");

            // ---- calibration: the gauge master is in the chain since R1q for its gates; here the rest of the master and the events
            ExtendPd(typeof(Q.MeasuringEquipment), "Name", "Type", "Maker", "Model", "SerialNumber", "OwnerUnit", "Location", "Range", "Accuracy", "IntervalMonths", "LastCalibrated", "NextDue", "CorrectionTableDocumentId", "Note", "CalibrationWaivedAt", "CalibrationWaiverRequestedAt", "Deleted");
            Labels[typeof(Q.MeasuringEquipment)] = "تجهیز اندازه‌گیری";
            AuditPd(typeof(Q.CalibrationEvent), null, "رویداد کالیبراسیون");
            AuditPd(typeof(Q.CalibrationIntervalDefault), null, "فاصله پیش‌فرض کالیبراسیون");
            AuditPd(typeof(Q.OutOfToleranceCase), null, "پرونده خارج از تلورانس");
            AuditPd(typeof(Q.OutOfToleranceItem), null, "قلم پرونده خارج از تلورانس");

            // ---- qualified people and welding (the name of a person is not copied: code, stamp and user link identify him)
            AuditPd(typeof(Q.QualifiedPerson), new[] { "PersonnelCode", "Employer", "ProviderId", "StampId", "UserId", "IsActive", "Deleted" }, "فرد صلاحیت‌دار");
            AuditPd(typeof(Q.Qualification), null, "صلاحیت (جوشکار، بازرس یا NDT)");
            AuditPd(typeof(Q.Wps), null, "دستورالعمل جوشکاری (WPS)");
            AuditPd(typeof(Q.Pqr), null, "گزارش تایید روش جوشکاری (PQR)");
            AuditPd(typeof(Q.WpsPqr), null, "پیوند WPS و PQR");
            AuditPd(typeof(Q.WeldLog), null, "گزارش جوشکاری");
            AuditPd(typeof(Q.WeldLogReport), null, "پیوند گزارش جوشکاری به گزارش آزمون");

            // ---- audits, management review, controlled documents (the revisions are in the chain since R1q), CAPA actions
            AuditPd(typeof(Q.AuditProgram), null, "برنامه ممیزی");
            AuditPd(typeof(Q.Audit), new[] { "Number", "ProgramId", "AuditedUnit", "Criteria", "PlannedDate", "LeadAuditorUserId", "TeamUserIds", "Status", "ReportDocumentId", "DoneAt", "ClosedAt", "ClosedByUserId", "Deleted" }, "ممیزی");
            AuditPd(typeof(Q.AuditFinding), new[] { "AuditId", "Clause", "Type", "Evidence", "CapaId", "DueDate", "Deleted" }, "یافته ممیزی");
            AuditPd(typeof(Q.ManagementReview), new[] { "ReviewDate", "Attendees", "Status", "MinutesDocumentId", "OfMeetingId", "Deleted" }, "بازنگری مدیریت");
            AuditPd(typeof(Q.ManagementReviewInput), new[] { "ReviewId", "Category", "Text", "AutoDrafted" }, "ورودی بازنگری مدیریت");
            AuditPd(typeof(Q.ControlledDocument), null, "سند کنترل‌شده");
            AuditPd(typeof(Q.DocumentDistribution), new[] { "RevisionId", "RecipientUserId", "RecipientUnit", "SentAt", "AcknowledgedAt", "AcknowledgedByUserId" }, "توزیع سند کنترل‌شده");
            AuditPd(typeof(Q.CapaAction), new[] { "CapaId", "Description", "OwnerUserId", "DueDate", "DoneAt", "EvidenceDocumentId", "EvidenceNote", "Iteration" }, "اقدام CAPA");
            AuditPd(typeof(Entities.Model.NasbNiro.Procurement.SupplierEvaluation), new[] { "ProviderId", "Kind", "EvaluationDate", "PurchaseOrderId", "Score", "Grade", "Notes", "Scored", "Deleted" }, "ارزیابی تامین‌کننده");
            AuditPd(typeof(Entities.Model.NasbNiro.Procurement.SupplierEvaluationLine), new[] { "EvaluationId", "Question", "Weight", "Score", "Basis" }, "ردیف ارزیابی تامین‌کننده");

            // ---- inspection and test plans: the templates and the characteristics (limits), the point releases and the revisions of a plan, the release note lines
            AuditPd(typeof(Q.ItpTemplate), null, "قالب ITP");
            AuditPd(typeof(Q.ItpTemplateActivity), null, "فعالیت قالب ITP");
            AuditPd(typeof(Q.ItpTemplateParameter), null, "مشخصه فعالیت قالب ITP");
            AuditPd(typeof(Q.ItpActivityParameter), null, "مشخصه فعالیت ITP");
            AuditPd(typeof(Q.ItpPointRelease), null, "آزادسازی نقطه بازرسی");
            AuditPd(typeof(Q.ItpRevision), new[] { "ItpId", "Revision", "Reason", "ClientApprovalReference", "ClientApprovalDate", "ChangedAt", "ChangedByUserId" }, "بازنگری ITP");
            AuditPd(typeof(Q.ReleaseNoteLine), new[] { "ReleaseNoteId", "RowNumber", "ItemId", "Quantity", "SerialNumbers" }, "سطر برگه آزادسازی");

            // ---- warehouse and traceability: lots (heat number, certificate links), serial units, genealogy, documents and lines, operation templates, part operations
            AuditPd(typeof(Iv.InventoryLot), new[] { "ItemId", "LotNumber", "HeatNumber", "CertificateNumber", "CertificateType", "CertificateDocumentId", "ProviderId", "ProductionDate", "ExpiryDate", "Description", "ParentLotId", "IsCustomerProperty", "Deleted" }, "بچ و ذوب کالا");
            AuditPd(typeof(Iv.SerialUnit), new[] { "ItemId", "SerialNumber", "LotId", "Status", "WarehouseId", "LocationId", "ParentSerialUnitId", "WorkOrderId", "Description", "SourceSerialUnitId", "SourceLotId", "Deleted" }, "سریال کالا");
            AuditPd(typeof(Iv.GenealogyLink), null, "پیوند شجره‌نامه قطعه");
            AuditPd(typeof(Iv.InventoryDocument), new[] { "Type", "Number", "DocumentDate", "Status", "WarehouseId", "DestinationWarehouseId", "ProviderId", "BuyOrderId", "PurchaseOrderId", "WorkOrderId", "ContractId", "SalesOrderId", "ServiceOrderId", "ReferenceNumber", "ConfirmedAt", "ConfirmedByUserId", "CancelledAt", "CancelledByUserId", "CancelReason", "SepidarStatus", "SepidarReference", "SepidarExportedAt", "MaterialRequestId", "NcrId", "InspectionId", "StockCountId", "ExitCheckedAt", "ExitCheckedByUserId", "QcLoadingApprovedAt", "QcLoadingApprovedByUserId", "FinanceApprovedAt", "FinanceApprovedByUserId", "WarehouseApprovedAt", "WarehouseApprovedByUserId", "PlanningApprovedAt", "PlanningApprovedByUserId", "ZeroPriceApprovedAt", "ZeroPriceApprovedByUserId", "WriteOffNcrId", "ConsignmentDueDate", "ConsignmentOfDocumentId", "CorrectsDocumentId", "RejectedAt", "RejectedByUserId", "RejectReason", "PriceApprovedAt", "PriceApprovedByUserId", "GoodsType", "BillOfLadingNumber", "Deleted" }, "سند انبار");
            AuditPd(typeof(Iv.InventoryDocumentLine), new[] { "DocumentId", "RowNumber", "ItemId", "Quantity", "LotId", "LotNumber", "HeatNumber", "CertificateNumber", "LocationId", "DestinationLocationId", "UnitPrice", "SalesOrderLineId", "PurchaseOrderLineId", "Direction", "SourceRowNumber" }, "سطر سند انبار");
            SpecialProperties[(typeof(Iv.InventoryDocumentLine), "UnitPrice")] = Special.Marker;   // the price is hidden from most roles: the trail shows that it changed, never what it was
            AuditPd(typeof(Iv.InventoryDocumentLineSerial), new[] { "LineId", "SerialNumber", "SerialUnitId" }, "سریال سطر سند انبار");
            AuditPd(typeof(Iv.OperationTemplate), null, "قالب عملیات ساخت");
            AuditPd(typeof(Iv.PartOperation), new[] { "SerialUnitId", "LotId", "OperationTemplateId", "Sequence", "Title", "Status", "StartedAt", "FinishedAt", "OperatorUserId", "MachineId", "WorkOrderId", "Result", "QcStatus", "QcByUserId", "QcAt", "QcNote", "Notes", "Deleted" }, "عملیات ساخت قطعه");

            // ---- the control settings of this package and the AI seats (a seat switched on or off is a decision)
            AuditPd(typeof(Entities.Model.NasbNiro.Ai.AiSeat), new[] { "Code", "Title", "ServiceUserName", "Roles", "Schedule", "PromptVersionCode", "Enabled", "OutputKind" }, "صندلی هوش مصنوعی");
            AuditPd(typeof(Entities.Model.NasbNiro.Integrity.SafetySettingChange), null, "درخواست تغییر تنظیم امنیتی");
            AuditPd(typeof(Entities.Model.NasbNiro.Integrity.RestoreTestResult), null, "نتیجه آزمون بازیابی پشتیبان");
        }
    }
}

namespace DataLayer.Context
{
    /// <summary>The Persian name of a record type of the audit trail (the table a chain entry describes), for the alert and the page «سلامت سوابق».</summary>
    public static class AuditEntityLabels
    {
        public static string Of(string entityType) => FinanceAuditTrail.LabelOfName(entityType);
    }

    internal static partial class FinanceAuditTrail
    {
        internal static string LabelOfName(string entityType)
        {
            if (string.IsNullOrWhiteSpace(entityType)) return null;
            foreach (KeyValuePair<Type, string> pair in Labels)
                if (pair.Key.Name == entityType) return pair.Value;
            return null;
        }
    }
}
