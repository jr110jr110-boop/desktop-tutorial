using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Quality
{
    /// <summary>What kind of record is the accepted re-inspection of an NCR (<see cref="NonConformance.ReinspectionKind"/>).</summary>
    public static class NcrReinspection
    {
        public const string Inspection = "QcInspection";
        public const string Record = "QcRecord";
        public const string Report = "TestReport";

        public static bool IsKnown(string kind) => kind == Inspection || kind == Record || kind == Report;
    }

    /// <summary>
    /// The system drafts the NCR when an inspection or a test fails; people classify it, decide and approve it
    /// through the existing NCR flow of the inventory module (PR-QC-002).
    /// </summary>
    internal static class QualityNcrFactory
    {
        public static async Task<NonConformance> DraftAsync(
            IUnitOfWork uow, int userId, NcrSource source, int itemId, int? lotId, decimal quantity, string serial, int? workOrderId,
            string requirement, string description, string sourceRecordNumber, int? equipmentId, CancellationToken cancellationToken)
        {
            int? contractId = workOrderId.HasValue
                ? await uow.Set<WorkOrder>().Where(w => w.Id == workOrderId.Value).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(cancellationToken)
                : null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var ncr = new NonConformance
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, InventoryNumbers.Ncr, "NCR", DateTime.Today, cancellationToken),
                ReportDate = DateTime.Today,
                Source = source,
                Status = NcrStatus.Open,
                Severity = NcrSeverity.NotClassified,
                ItemId = itemId,
                LotId = lotId,
                Quantity = quantity > 0 ? quantity : 1,
                SerialNumbers = QualityText.Clean(serial),
                WorkOrderId = workOrderId,
                ContractId = contractId,
                Requirement = QualityText.Clean(requirement),
                Description = QualityText.Clean(description),
                SourceRecordNumber = sourceRecordNumber,
                AffectedEquipmentId = equipmentId,
                DueDate = DateTime.Today.AddDays((int)settings[QualitySettingKeys.NcrMinorDays]),
                // R1q D3: the person who drafted it (inspector, examiner, assessor) cannot give the QC signature or close it
                EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(null, userId),
                CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
            };
            uow.Set<NonConformance>().Add(ncr);
            return ncr;
        }

        /// <summary>
        /// Why the NCR cannot be cancelled (null = it can). A report from an incoming inspection is decided, never cancelled; an NCR drafted from a failed
        /// inspection record or test report is cancelled only when a later accepted re-test of the same product exists (R1q D3 ★).
        /// </summary>
        public static async Task<string> CancelBlockAsync(IUnitOfWork uow, NonConformance ncr, CancellationToken cancellationToken)
        {
            if (ncr.InspectionId.HasValue)
                return "گزارش حاصل از بازرسی ورودی ابطال نمی‌شود؛ تصمیم «مجوز ارفاقی» یا «مرجوعی» بگیرید.";

            QcRecord record = await uow.Set<QcRecord>().FirstOrDefaultAsync(r => r.NcrId == ncr.Id && !r.Deleted, cancellationToken);
            if (record != null)
            {
                bool retest = await uow.Set<QcRecord>().AnyAsync(r => !r.Deleted && r.Id > record.Id && r.Result == QcResult.Accept &&
                    r.ItemId == record.ItemId && r.WorkOrderId == record.WorkOrderId && r.SerialNumber == record.SerialNumber &&
                    r.LotId == record.LotId && r.ItpActivityId == record.ItpActivityId, cancellationToken);
                return retest ? null : $"این گزارش از بازرسی مردودشده {record.Number} ساخته شده است؛ تا بازرسی مجدد قبول‌شده‌ای از همان قطعه ثبت نشود ابطال نمی‌شود.";
            }

            TestReport report = await uow.Set<TestReport>().FirstOrDefaultAsync(r => r.NcrId == ncr.Id && !r.Deleted, cancellationToken);
            if (report != null)
            {
                bool retest = await uow.Set<TestReport>().AnyAsync(r => !r.Deleted && r.Id > report.Id && r.Status == TestReportStatus.Approved && r.Result == ReportResult.Pass &&
                    r.Type == report.Type && r.ItemId == report.ItemId && r.WorkOrderId == report.WorkOrderId && r.SerialNumber == report.SerialNumber &&
                    r.ItpActivityId == report.ItpActivityId, cancellationToken);
                return retest ? null : $"این گزارش از آزمون مردودشده {report.Number} ساخته شده است؛ تا آزمون مجدد قبول‌شده‌ای از همان قطعه ثبت نشود ابطال نمی‌شود.";
            }
            return null;
        }

        /// <summary>Null when <paramref name="recordId"/> of that kind is an accepted re-inspection of the NCR's product, otherwise the Persian reason.</summary>
        public static async Task<string> CheckReinspectionAsync(IUnitOfWork uow, NonConformance ncr, string kind, int recordId, CancellationToken cancellationToken)
        {
            List<string> serials = string.IsNullOrWhiteSpace(ncr.SerialNumbers)
                ? new List<string>()
                : ncr.SerialNumbers.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            switch (kind)
            {
                case NcrReinspection.Inspection:
                    {
                        QcInspection inspection = await uow.Set<QcInspection>().FirstOrDefaultAsync(q => q.Id == recordId && !q.Deleted, cancellationToken);
                        if (inspection == null) return "بازرسی مجدد پیدا نشد.";
                        if (inspection.ItemId != ncr.ItemId) return "بازرسی مجدد مربوط به کالای این گزارش نیست.";
                        if (inspection.Status != InspectionStatus.Done || inspection.AcceptedQuantity <= 0) return "بازرسی مجدد هنوز با نتیجه «قبول» ثبت نشده است.";
                        if (inspection.ReinspectionOfNcrId.HasValue && inspection.ReinspectionOfNcrId.Value != ncr.Id) return "این بازرسی مربوط به گزارش عدم انطباق دیگری است.";
                        return null;
                    }
                case NcrReinspection.Record:
                    {
                        QcRecord record = await uow.Set<QcRecord>().FirstOrDefaultAsync(r => r.Id == recordId && !r.Deleted, cancellationToken);
                        if (record == null) return "گزارش بازرسی مجدد پیدا نشد.";
                        if (record.ItemId != ncr.ItemId) return "گزارش بازرسی مربوط به کالای این گزارش عدم انطباق نیست.";
                        if (ncr.WorkOrderId.HasValue && record.WorkOrderId != ncr.WorkOrderId) return "گزارش بازرسی مربوط به دستور کار این گزارش عدم انطباق نیست.";
                        if (serials.Count > 0 && !serials.Contains(record.SerialNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                            return "سریال گزارش بازرسی با سریال‌های این گزارش عدم انطباق نمی‌خواند.";
                        if (record.Result != QcResult.Accept) return "گزارش بازرسی مجدد با نتیجه «قبول» نیست.";
                        if (record.NcrId == ncr.Id || record.Number == ncr.SourceRecordNumber) return "گزارش بازرسی مردودشده‌ای که این عدم انطباق را ساخته، بازرسی مجدد نیست.";
                        if (record.RecordDate.Date < ncr.ReportDate.Date) return "بازرسی مجدد باید بعد از ثبت گزارش عدم انطباق انجام شده باشد.";
                        return null;
                    }
                case NcrReinspection.Report:
                    {
                        TestReport report = await uow.Set<TestReport>().FirstOrDefaultAsync(r => r.Id == recordId && !r.Deleted, cancellationToken);
                        if (report == null) return "گزارش آزمون مجدد پیدا نشد.";
                        if (report.ItemId != ncr.ItemId) return "گزارش آزمون مربوط به کالای این گزارش عدم انطباق نیست.";
                        if (ncr.WorkOrderId.HasValue && report.WorkOrderId != ncr.WorkOrderId) return "گزارش آزمون مربوط به دستور کار این گزارش عدم انطباق نیست.";
                        if (serials.Count > 0 && !serials.Contains(report.SerialNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                            return "سریال گزارش آزمون با سریال‌های این گزارش عدم انطباق نمی‌خواند.";
                        if (report.Status != TestReportStatus.Approved || report.Result != ReportResult.Pass) return "گزارش آزمون مجدد باید تاییدشده و با نتیجه «قبول» باشد.";
                        if (report.NcrId == ncr.Id) return "گزارش آزمون مردودشده‌ای که این عدم انطباق را ساخته، آزمون مجدد نیست.";
                        if (report.ReportDate.Date < ncr.ReportDate.Date) return "آزمون مجدد باید بعد از ثبت گزارش عدم انطباق انجام شده باشد.";
                        return null;
                    }
                default:
                    return "نوع بازرسی مجدد نامعتبر است (بازرسی ورودی، گزارش بازرسی یا گزارش آزمون).";
            }
        }
    }

    /// <summary>
    /// The inbox items of an NCR (R1q D34): while it waits for QC to classify it and decide, the QC people have an item with the due date;
    /// when the decision is made, the roles that have not signed yet. One item per NCR, closed when the NCR is executed, closed or cancelled.
    /// Call after the save. It never blocks the NCR.
    /// </summary>
    internal static class NcrGates
    {
        public static bool NeedsConcession(NcrDisposition disposition) =>
            disposition == NcrDisposition.Concession || disposition == NcrDisposition.Repair;

        public static async Task SyncAsync(INotificationService notify, NonConformance ncr, CancellationToken cancellationToken)
        {
            if (notify == null || ncr == null || ncr.Id == 0) return;
            var roles = new List<string>();
            string title = null;
            if (ncr.Status == NcrStatus.Open)
            {
                if (ncr.Severity == NcrSeverity.NotClassified || ncr.Disposition == NcrDisposition.None)
                {
                    roles.Add("QC");
                    title = "طبقه‌بندی و تصمیم‌گیری درباره عدم انطباق " + ncr.Number;
                }
                else
                {
                    if (!ncr.QualityApprovedAt.HasValue) roles.Add(InventoryRoles.RoleQualitySigner);
                    if (!ncr.EngineeringApprovedAt.HasValue || (NeedsConcession(ncr.Disposition) && !ncr.ConcessionApprovedAt.HasValue)) roles.Add(InventoryRoles.RoleEngineering);
                    if (!ncr.ManufacturingApprovedAt.HasValue) roles.Add(InventoryRoles.RoleManufacturing);
                    title = (ncr.Severity == NcrSeverity.Major ? "تصمیم کمیته فنی برای عدم انطباق عمده " : "تایید تصمیم عدم انطباق ") + ncr.Number;
                }
            }
            await OfficeGates.SyncAsync(notify, "ncr:" + ncr.Id, roles.Count > 0, "quality", "ncr", ncr.Id, title ?? ncr.Number, "/inventory/ncr/" + ncr.Id,
                string.Join(",", roles), cancellationToken, ncr.DueDate);
        }
    }
}
