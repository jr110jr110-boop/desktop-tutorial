using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Entities.Model.NasbNiro.Finance;
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

        // ------------------------------------------------------------------------------------------ Package D (H1): the entry of a NEW record carries the record's real key

        /// <summary>An entry written for a new record whose key the database generates: the row waits here until the save has given the record its key.</summary>
        internal sealed class LateAudit
        {
            public FnAuditLog Row { get; set; }
            public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry { get; set; }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<LateAudit>> LateTable = new System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<LateAudit>>();

        private static List<LateAudit> LateRows(DbContext context) => LateTable.GetOrCreateValue(context);

        /// <summary>The waiting entries of this context (and forgets them).</summary>
        internal static List<LateAudit> TakeLate(DbContext context)
        {
            List<LateAudit> list = LateRows(context);
            var taken = list.ToList();
            list.Clear();
            return taken;
        }

        /// <summary>Gives the waiting entries the key the save generated; returns the rows to seal.</summary>
        internal static List<FnAuditLog> Resolve(List<LateAudit> late)
        {
            foreach (LateAudit item in late)
            {
                object id = item.Entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue;
                if (id is int i && i > 0) item.Row.EntityId = i;
                else if (id is long l && l > 0 && l <= int.MaxValue) item.Row.EntityId = (int)l;
            }
            return late.Select(l => l.Row).ToList();
        }

        /// <summary>True for the inserts that are listed as one summary entry per document or item (a receipt of a thousand serial numbers must not write a thousand chain entries under the global lock of the chain).</summary>
        private static bool PdSummarisesInsert(object entity) => entity is Iv.InventoryDocumentLineSerial || entity is Iv.SerialUnit;

        /// <summary>One entry for the serial numbers added to a document (and one for the serial units added for an item): the count, the first and last number and a SHA-256 of the sorted list, so that a changed list is detectable.</summary>
        private static void AddSerialSummaries(DbContext context, List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> audited, int userId, string ip, DateTimeOffset now)
        {
            var lineSerials = audited.Where(e => e.State == EntityState.Added && e.Entity is Iv.InventoryDocumentLineSerial).Select(e => (Iv.InventoryDocumentLineSerial)e.Entity).ToList();
            foreach (var group in lineSerials.GroupBy(x => x.Line?.DocumentId ?? context.Set<Iv.InventoryDocumentLine>().Local.Where(l => l.Id == x.LineId).Select(l => (int?)l.DocumentId).FirstOrDefault() ?? -x.LineId))
                AddSummary(context, typeof(Iv.InventoryDocumentLineSerial), group.Key > 0 ? group.Key : (int?)null, "سریال‌های سند انبار", group.Select(x => x.SerialNumber).ToList(), userId, ip, now);
            var units = audited.Where(e => e.State == EntityState.Added && e.Entity is Iv.SerialUnit).Select(e => (Iv.SerialUnit)e.Entity).ToList();
            foreach (var group in units.GroupBy(x => x.ItemId))
                AddSummary(context, typeof(Iv.SerialUnit), null, "واحدهای سریال‌دار کالا " + group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), group.Select(x => x.SerialNumber).ToList(), userId, ip, now);
        }

        private static void AddSummary(DbContext context, Type type, int? documentId, string text, List<string> serials, int userId, string ip, DateTimeOffset now)
        {
            List<string> sorted = serials.Where(x => x != null).OrderBy(x => x, StringComparer.Ordinal).ToList();
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", sorted)))).Replace("-", "").ToLowerInvariant();
            context.Set<FnAuditLog>().Add(new FnAuditLog
            {
                EntityType = type.Name, EntityId = null, Action = "Added",
                Summary = text + (documentId.HasValue ? " " + documentId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") + ": " + sorted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " سریال",
                AfterJson = System.Text.Json.JsonSerializer.Serialize(new { documentId, count = sorted.Count, first = sorted.FirstOrDefault(), last = sorted.LastOrDefault(), sha256 = hash }),
                UserId = userId, At = now, Ip = ip,
            });
        }

        /// <summary>The columns of a record type the trail lists (null = every column).</summary>
        internal static string[] AuditedColumns(Type type) => Audited.TryGetValue(type, out string[] only) ? only : null;

        /// <summary>True for a column the trail writes as a marker or a masked value: the record check does not compare it.</summary>
        internal static bool IsMarkedColumn(Type type, string name) => SpecialProperties.ContainsKey((type, name)) || Masked.Contains(name);

        /// <summary>The value as the trail writes it (the record check compares the current row with the trail in the same shape).</summary>
        internal static object ShapeOf(string name, object value) => Shape(name, value);

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

            // ---- Fix round (M6): assembly links, stock counts, material requests, customer complaints, retention rules, part-operation files
            AuditPd(typeof(Iv.AssemblyLink), new[] { "ParentSerialUnitId", "ChildSerialUnitId", "ChildLotId", "Quantity", "InstalledAt", "InstalledByUserId", "RemovedAt", "RemovedByUserId", "WorkOrderId", "Notes" }, "پیوند مونتاژ");
            AuditPd(typeof(Iv.StockCount), new[] { "Number", "Kind", "Status", "CountDate", "WarehouseId", "GroupCode", "LocationId", "SnapshotAt", "SubmittedAt", "WarehouseApprovedAt", "WarehouseApprovedByUserId", "PlanningApprovedAt", "PlanningApprovedByUserId", "QualityApprovedAt", "QualityApprovedByUserId", "PostedAt", "PostedByUserId", "IncreaseDocumentId", "DecreaseDocumentId" }, "انبارگردانی");
            AuditPd(typeof(Iv.StockCountLine), new[] { "StockCountId", "ItemId", "LocationId", "LotId", "SystemQuantity", "CountedQuantity", "RecountedQuantity", "CountedByUserId" }, "سطر انبارگردانی");
            AuditPd(typeof(Iv.MaterialRequest), new[] { "Number", "RequestDate", "Status", "WorkOrderId", "WarehouseId", "ActivityId", "SubmittedAt", "PlanningApprovedAt", "PlanningApprovedByUserId", "ManufacturingApprovedAt", "ManufacturingApprovedByUserId", "RejectedAt", "RejectedByUserId", "ClosedAt", "ClosedByUserId" }, "درخواست مواد");
            AuditPd(typeof(Iv.MaterialRequestLine), new[] { "MaterialRequestId", "RowNumber", "ItemId", "Quantity", "IssuedQuantity" }, "سطر درخواست مواد");
            AuditPd(typeof(Q.CustomerComplaint), new[] { "Number", "ContractId", "WorkOrderId", "ReceivedDate", "Channel", "Severity", "ResponseDue", "Status", "NcrId", "CapaId", "RespondedAt", "ClosedAt", "ClosedByUserId", "ServiceRequestId" }, "شکایت مشتری");
            AuditPd(typeof(Q.RetentionRule), new[] { "RecordType", "Years", "Permanent", "Source" }, "قاعده نگهداری سوابق");
            AuditPd(typeof(Iv.PartOperationFile), new[] { "PartOperationId", "DocumentId", "Title", "CreatedAt", "CreatedByUserId" }, "پرونده عملیات قطعه");

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
            // Fix round (Low): the history of the verification and the acceptances of known breaks are in the chain too
            AuditPd(typeof(Entities.Model.NasbNiro.Integrity.AuditChainRun), new[] { "Kind", "Manual", "RequestedByUserId", "StartedAt", "FinishedAt", "Status", "FromSeq", "ToSeq", "EntriesChecked", "HeadSeq", "HeadHash", "BreakKind", "BreakSeq", "BreakRowId", "BreakEntityType", "BreakColumn" }, "بررسی زنجیره سوابق");
            AuditPd(typeof(Entities.Model.NasbNiro.Integrity.ChainBreakAcceptance), null, "پذیرش شکست شناخته‌شده");
        }
    }
}

namespace DataLayer.Context
{
    /// <summary>The Persian name of a record type of the audit trail (the table a chain entry describes), for the alert and the page «سلامت سوابق».</summary>
    public static class AuditEntityLabels
    {
        public static string Of(string entityType) => FinanceAuditTrail.LabelOfName(entityType);

        /// <summary>The record type of the package with this CLR name (null when it is not one).</summary>
        public static Type TypeOf(string entityType) => FinanceAuditTrail.RecordsIntegrityTypes.FirstOrDefault(t => t.Name == entityType);
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
