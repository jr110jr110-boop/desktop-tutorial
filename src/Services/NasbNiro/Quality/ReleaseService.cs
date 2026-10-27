using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IReleaseService
    {
        Task<ResponseDto<PagedList<ReleaseNoteListDto>>> GetNotesAsync(ReleaseNoteQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ReleaseNoteDto>> GetNoteAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ReleaseNoteDto>> SaveNoteAsync(SaveReleaseNoteDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReleaseNoteDto>> NoteActionAsync(ReleaseNoteActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReleaseChecksDto>> GetChecksAsync(int workOrderId, CancellationToken cancellationToken);
        Task<ResponseDto<DataBookDto>> GetDataBookAsync(int workOrderId, string variant, CancellationToken cancellationToken);

        /// <summary>
        /// The customer delivery gate (ISO 8.6, R1q D10): null when a Released note of the work order covers every line of the delivery (item, serial numbers,
        /// quantity left) and no non-conformance is open on the work order or on the delivered serial numbers; otherwise the Persian reason.
        /// </summary>
        Task<string> CheckDeliveryAsync(InventoryDocument document, CancellationToken cancellationToken);
    }

    /// <summary>Product release (ISO 8.6): the checks and the final data book index are computed from the records, never typed.</summary>
    public class ReleaseService : IReleaseService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public ReleaseService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _notify = notify;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        public async Task<string> CheckDeliveryAsync(InventoryDocument document, CancellationToken cancellationToken)
        {
            if (document == null) return "سند تحویل پیدا نشد.";
            List<InventoryDocumentLine> lines = document.Lines?.ToList() ?? new List<InventoryDocumentLine>();
            List<int> itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
            var delivery = lines.Select(l =>
            {
                items.TryGetValue(l.ItemId, out InventoryItem item);
                return new CoverageLine
                {
                    ItemId = l.ItemId, ItemCode = item?.Code, Quantity = l.Quantity, IsSerial = item != null && item.TrackingMode == ItemTrackingMode.Serial,
                    Serials = (l.Serials ?? new List<InventoryDocumentLineSerial>()).Select(x => x.SerialNumber).ToList(),
                };
            }).ToList();
            List<string> deliveredSerials = delivery.SelectMany(d => d.Serials).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

            // the pieces that leave and the work orders that made them (review H4): a sales order or a service order does not hide a work order
            List<SerialUnit> units = deliveredSerials.Count == 0
                ? new List<SerialUnit>()
                : await _uow.Set<SerialUnit>().Where(u => itemIds.Contains(u.ItemId) && deliveredSerials.Contains(u.SerialNumber) && !u.Deleted).ToListAsync(cancellationToken);
            List<int> unitOrders = units.Where(u => u.WorkOrderId.HasValue).Select(u => u.WorkOrderId.Value).Distinct().ToList();
            bool gate = ReleaseCoverage.GateApplies(document.SalesOrderId.HasValue, document.ServiceOrderId.HasValue, document.WorkOrderId.HasValue, unitOrders.Count > 0);

            int? workOrderId = document.WorkOrderId ?? (unitOrders.Count == 1 ? unitOrders[0] : (int?)null);
            List<ReleaseNote> released = new List<ReleaseNote>();
            if (gate)
            {
                if (!workOrderId.HasValue)
                    return unitOrders.Count > 1
                        ? "سریال‌های این سند ساخته چند دستور کار است؛ برای هر دستور کار یک سند تحویل جدا ثبت کنید تا برگه ترخیص هر کدام بررسی شود."
                        : "تحویل به مشتری باید به دستور کار مرتبط باشد تا برگه ترخیص کیفی بررسی شود.";
                released = await _uow.Set<ReleaseNote>().Include(r => r.Lines)
                    .Where(r => r.WorkOrderId == workOrderId.Value && r.Status == ReleaseNoteStatus.Released && !r.Deleted).ToListAsync(cancellationToken);
                if (released.Count == 0) return "برای این دستور کار «برگه ترخیص کیفی» صادرشده وجود ندارد؛ تحویل به مشتری بدون ترخیص QC مجاز نیست.";
            }

            // the NCRs are looked at again now (every customer delivery, review H4): one may have been raised after the release, on the work order, on a lot or on a delivered serial number
            var orderIds = new List<int>(unitOrders);
            if (document.WorkOrderId.HasValue) orderIds.Add(document.WorkOrderId.Value);
            List<int> lotIds = lines.Where(l => l.LotId.HasValue).Select(l => l.LotId.Value).Concat(units.Where(u => u.LotId.HasValue).Select(u => u.LotId.Value)).Distinct().ToList();
            List<NonConformance> open = await _uow.Set<NonConformance>()
                .Where(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled &&
                            ((n.WorkOrderId != null && orderIds.Contains(n.WorkOrderId.Value)) || (n.LotId != null && lotIds.Contains(n.LotId.Value)) ||
                             (n.SerialNumbers != null && itemIds.Contains(n.ItemId)))).ToListAsync(cancellationToken);
            List<string> blocking = open.Where(n => !IsConcessionApproved(n) &&
                    ((n.WorkOrderId.HasValue && orderIds.Contains(n.WorkOrderId.Value)) || (n.LotId.HasValue && lotIds.Contains(n.LotId.Value)) ||
                     (n.SerialNumbers ?? string.Empty).Split('\n').Select(x => x.Trim()).Any(x => deliveredSerials.Contains(x, StringComparer.OrdinalIgnoreCase))))
                .Select(n => n.Number).ToList();
            if (blocking.Count > 0)
                return "تا بسته‌شدن عدم انطباق‌های باز (یا صدور مجوز ارفاقی) تحویل به مشتری مجاز نیست: " + string.Join("، ", blocking.Take(8)) + ".";
            if (!gate) return null;

            var releasedLines = released.SelectMany(r => r.Lines ?? new List<ReleaseNoteLine>()).Select(l => new CoverageLine
            {
                ItemId = l.ItemId, Quantity = l.Quantity, IsSerial = !string.IsNullOrWhiteSpace(l.SerialNumbers),
                Serials = (l.SerialNumbers ?? string.Empty).Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
            }).ToList();
            Dictionary<int, decimal> already = (await _uow.Set<InventoryDocumentLine>()
                .Where(l => itemIds.Contains(l.ItemId) && l.Document.Id != document.Id && l.Document.Type == InventoryDocumentType.CustomerDelivery &&
                            l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted && l.Document.WorkOrderId == workOrderId.Value)
                .Select(l => new { l.ItemId, l.Quantity }).ToListAsync(cancellationToken))
                .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            List<string> uncovered = ReleaseCoverage.Uncovered(delivery, releasedLines, already);
            return uncovered.Count == 0
                ? null
                : "تحویل با برگه ترخیص کیفی این دستور کار نمی‌خواند: " + string.Join(" | ", uncovered.Take(8)) + (uncovered.Count > 8 ? "…" : string.Empty);
        }

        // -------------------------------------------------------------------------------- data book

        private static string ResultText(QcResult result)
        {
            switch (result)
            {
                case QcResult.Accept: return "قبول";
                case QcResult.Reject: return "رد";
                case QcResult.Rework: return "دوباره‌کاری";
                case QcResult.Hold: return "نگهداری (در انتظار تصمیم)";
                default: return result.ToString();
            }
        }

        private static bool IsOpenNcr(NonConformance n) => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled;

        /// <summary>A concession lets the work go on only when its permit counts (review H4): approved or executed, all three signatures, the permit given after the QC signature.</summary>
        private static bool IsConcessionApproved(NonConformance n) => ConcessionRules.IsValid(n, false);

        private static string SectionOf(TestReportType type)
        {
            switch (type)
            {
                case TestReportType.PT: case TestReportType.MT: case TestReportType.UT: case TestReportType.RT:
                case TestReportType.Hardness: case TestReportType.PMI:
                    return "NDT";
                case TestReportType.Dimensional: return "Dimensional";
                case TestReportType.Painting: return "Painting";
                default: return "Tests";
            }
        }

        private async Task<DataBookDto> BuildBookAsync(WorkOrder wo, CancellationToken cancellationToken)
        {
            var book = new DataBookDto { WorkOrderId = wo.Id, WorkOrderCode = wo.Code };
            var s1 = new DataBookSectionDto { No = 1, Name = "برنامه بازرسی و آزمون (ITP) و تاییدها" };
            var s2 = new DataBookSectionDto { No = 2, Name = "گواهی مواد (EN 10204)" };
            var s3 = new DataBookSectionDto { No = 3, Name = "جوشکاری: WPS، PQR، گواهی جوشکاران و گزارش جوش" };
            var s4 = new DataBookSectionDto { No = 4, Name = "گزارش‌های آزمون غیرمخرب و مواد" };
            var s5 = new DataBookSectionDto { No = 5, Name = "گزارش‌های ابعادی و بازرسی" };
            var s6 = new DataBookSectionDto { No = 6, Name = "گزارش‌های آزمون (هیدرو، تست کارکرد، بالانس، عملکرد)" };
            var s7 = new DataBookSectionDto { No = 7, Name = "رنگ و پوشش" };
            var s8 = new DataBookSectionDto { No = 8, Name = "فهرست عدم انطباق و مجوز ارفاقی" };
            var s9 = new DataBookSectionDto { No = 9, Name = "گواهی کالیبراسیون تجهیزات به‌کاررفته" };
            var s10 = new DataBookSectionDto { No = 10, Name = "برگه ترخیص و گواهی انطباق" };
            var s11 = new DataBookSectionDto { No = 11, Name = "فهرست مدارک مهندسی (MDR / MDL)" };
            book.Sections.AddRange(new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10 });

            // 1: ITP
            Itp itp = await _uow.Set<Itp>().Include(i => i.Activities).FirstOrDefaultAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, cancellationToken);
            if (itp == null)
                s1.Entries.Add(new DataBookEntryDto { SourceKind = "Itp", Title = "برنامه بازرسی و آزمون ساخته نشده است", Status = "-", Mandatory = true, Missing = true });
            else
            {
                bool approved = itp.Status == ItpStatus.ClientApproved || itp.Status == ItpStatus.InUse || itp.Status == ItpStatus.Closed;
                s1.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "Itp", SourceId = itp.Id, Number = $"{itp.Number} rev {itp.Revision}", Title = "برنامه بازرسی و آزمون",
                    Status = itp.Status + (itp.ClientApprovalReference == null ? string.Empty : " — " + itp.ClientApprovalReference), Mandatory = true, Missing = !approved,
                });
            }

            // 2: material certificates of the lots issued to the work order
            var lots = await _uow.Set<InventoryDocumentLine>()
                .Where(l => l.LotId.HasValue && l.Document.WorkOrderId == wo.Id && l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted &&
                            (l.Document.Type == InventoryDocumentType.Issue || l.Document.Type == InventoryDocumentType.SubcontractIssue))
                .Select(l => new { LotId = l.LotId.Value, LotNumber = l.Lot.LotNumber, ItemCode = l.Item.Code, Required = l.Item.RequiredCertificateType }).Distinct().ToListAsync(cancellationToken);
            List<int> lotIds = lots.Select(l => l.LotId).ToList();
            List<MaterialCertificate> certs = await _uow.Set<MaterialCertificate>().Where(c => lotIds.Contains(c.LotId) && !c.Deleted && c.Result == CertificateResult.Accepted).ToListAsync(cancellationToken);
            foreach (var lot in lots)
            {
                List<MaterialCertificate> mine = certs.Where(c => c.LotId == lot.LotId).ToList();
                foreach (MaterialCertificate c in mine)
                    s2.Entries.Add(new DataBookEntryDto { SourceKind = "MaterialCertificate", SourceId = c.Id, Number = c.Number, Title = $"{lot.ItemCode} — بچ {lot.LotNumber}", Status = "EN 10204 " + MaterialService.TypeText(c.Type), DocumentId = c.DocumentId });
                if (lot.Required.HasValue && !mine.Any(c => c.Type >= lot.Required.Value))
                    s2.Entries.Add(new DataBookEntryDto { SourceKind = "MaterialCertificate", Title = $"{lot.ItemCode} — بچ {lot.LotNumber}: گواهی نوع {MaterialService.TypeText(lot.Required.Value)} ثبت نشده", Status = "-", Mandatory = true, Missing = true });
            }

            // 3: welding
            List<WeldLog> logs = await _uow.Set<WeldLog>().Include(l => l.Wps).Include(l => l.Qualification).ThenInclude(q => q.Person)
                .Where(l => l.WorkOrderId == wo.Id && !l.Deleted).OrderBy(l => l.WeldDate).ToListAsync(cancellationToken);
            foreach (WeldLog l in logs)
                s3.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "WeldLog", SourceId = l.Id, Number = l.JointNo, Title = $"{(global::Services.NasbNiro.Finance.FiscalCalendars.Text(l.WeldDate))} — {l.Qualification.Person.Name} ({l.Qualification.Method}) — WPS {l.Wps.Number}/{l.Wps.Revision}",
                    Status = "VT: " + l.Vt, Mandatory = true, Missing = l.Vt != VtResult.Accept,
                });
            foreach (Wps w in logs.Select(l => l.Wps).GroupBy(w => w.Id).Select(g => g.First()))
            {
                s3.Entries.Add(new DataBookEntryDto { SourceKind = "Wps", SourceId = w.Id, Number = $"{w.Number}/{w.Revision}", Title = "WPS " + w.Process, Status = w.Status.ToString() });
                foreach (Pqr p in await _uow.Set<WpsPqr>().Where(x => x.WpsId == w.Id).Select(x => x.Pqr).ToListAsync(cancellationToken))
                    s3.Entries.Add(new DataBookEntryDto { SourceKind = "Pqr", SourceId = p.Id, Number = p.Number, Title = "PQR " + p.Process, Status = p.Approved ? "Approved" : "Not approved", DocumentId = p.ResultsDocumentId, Mandatory = true, Missing = !p.Approved });
            }
            foreach (Qualification q in logs.Select(l => l.Qualification).GroupBy(q => q.Id).Select(g => g.First()))
                s3.Entries.Add(new DataBookEntryDto { SourceKind = "Qualification", SourceId = q.Id, Number = q.CertificateNumber, Title = $"{q.Person.Name} ({q.Person.StampId}) — {q.Method}", Status = "Welder qualification", DocumentId = q.CertificateDocumentId });

            // 4-7: reports
            List<TestReport> reports = await _uow.Set<TestReport>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted && r.Status != TestReportStatus.Rejected).OrderBy(r => r.Id).ToListAsync(cancellationToken);
            foreach (TestReport r in reports)
            {
                DataBookSectionDto target = SectionOf(r.Type) == "NDT" ? s4 : SectionOf(r.Type) == "Dimensional" ? s5 : SectionOf(r.Type) == "Painting" ? s7 : s6;
                target.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "TestReport", SourceId = r.Id, Number = r.Number, Title = $"{r.Type} — {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(r.ReportDate))}", Status = $"{r.Status} / {r.Result}",
                    DocumentId = r.DocumentId, Mandatory = true, Missing = r.Status != TestReportStatus.Approved,
                });
            }
            foreach (QcRecord r in await _uow.Set<QcRecord>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted).OrderBy(r => r.Id).ToListAsync(cancellationToken))
                s5.Entries.Add(new DataBookEntryDto { SourceKind = "QcRecord", SourceId = r.Id, Number = r.Number, Title = $"{r.Type} — {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(r.RecordDate))}", Status = r.Result.ToString(), DocumentId = r.AttachmentDocumentId });
            if (itp != null)
                foreach (ItpActivity a in itp.Activities.Where(a => a.RequiredReportType.HasValue && a.Status != ItpActivityStatus.NotApplicable).OrderBy(a => a.Seq))
                {
                    if (reports.Any(r => r.ItpActivityId == a.Id && r.Status == TestReportStatus.Approved && r.Result == ReportResult.Pass)) continue;
                    DataBookSectionDto target = SectionOf(a.RequiredReportType.Value) == "NDT" ? s4 : SectionOf(a.RequiredReportType.Value) == "Dimensional" ? s5 : SectionOf(a.RequiredReportType.Value) == "Painting" ? s7 : s6;
                    target.Entries.Add(new DataBookEntryDto
                    {
                        SourceKind = "ItpActivity", SourceId = a.Id, Number = a.ItemNo, Title = $"{a.Activity}: گزارش {a.RequiredReportType} تاییدشده وجود ندارد", Status = "-", Mandatory = true, Missing = true,
                    });
                }

            // 8: NCRs
            foreach (NonConformance n in await _uow.Set<NonConformance>().Where(n => n.WorkOrderId == wo.Id && !n.Deleted && n.Status != NcrStatus.Cancelled).OrderBy(n => n.Id).ToListAsync(cancellationToken))
                s8.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "Ncr", SourceId = n.Id, Number = n.Number, Title = n.Description, Status = $"{n.Status} / {n.Disposition}" + (n.ConcessionNumber == null ? string.Empty : " / " + n.ConcessionNumber),
                    Mandatory = true, Missing = IsOpenNcr(n) && !IsConcessionApproved(n),
                });

            // 9: calibration certificates of the equipment used on this work order
            List<int> equipmentIds = (await _uow.Set<QcMeasurement>().Where(m => m.EquipmentId.HasValue && m.Record.WorkOrderId == wo.Id && !m.Record.Deleted).Select(m => m.EquipmentId.Value).Distinct().ToListAsync(cancellationToken))
                .Union(await _uow.Set<TestReportEquipment>().Where(e => e.Report.WorkOrderId == wo.Id && !e.Report.Deleted && e.Report.Status != TestReportStatus.Rejected).Select(e => e.EquipmentId).Distinct().ToListAsync(cancellationToken))
                .Distinct().ToList();
            foreach (MeasuringEquipment e in await _uow.Set<MeasuringEquipment>().Where(e => equipmentIds.Contains(e.Id)).OrderBy(e => e.Code).ToListAsync(cancellationToken))
            {
                CalibrationEvent last = await _uow.Set<CalibrationEvent>().Where(c => c.EquipmentId == e.Id && !c.Deleted).OrderByDescending(c => c.EventDate).ThenByDescending(c => c.Id).FirstOrDefaultAsync(cancellationToken);
                s9.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "Equipment", SourceId = e.Id, Number = last?.CertificateNumber, Title = $"{e.Code} — {e.Name}", Status = last == null ? "-" : $"{(global::Services.NasbNiro.Finance.FiscalCalendars.Text(last.EventDate))}",
                    DocumentId = last?.CertificateDocumentId, Mandatory = e.RequiresCalibration, Missing = e.RequiresCalibration && (last == null || last.CertificateDocumentId == null),
                });
            }

            // 10: release note
            ReleaseNote note = await _uow.Set<ReleaseNote>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted && r.Status != ReleaseNoteStatus.Cancelled).OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            s10.Entries.Add(new DataBookEntryDto
            {
                SourceKind = "ReleaseNote", SourceId = note?.Id, Number = note?.Number, Title = "برگه ترخیص کیفی و گواهی انطباق با MR و SPEC",
                Status = note == null ? "-" : note.Status.ToString(), Mandatory = false, Missing = note == null || note.Status != ReleaseNoteStatus.Released,
            });
            // 11: the master document register of project control; the lines marked ClientSubmittal are the MDL (the client list)
            foreach (Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable d in await _uow.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().Where(d => d.WorkOrderId == wo.Id && !d.Deleted).OrderBy(d => d.DocumentNumber).ThenBy(d => d.Id).ToListAsync(cancellationToken))
                s11.Entries.Add(new DataBookEntryDto
                {
                    SourceKind = "Mdr", SourceId = d.Id, Number = d.DocumentNumber + (d.Revision == null ? string.Empty : " rev " + d.Revision), Title = d.Title + (d.ClientSubmittal ? " [MDL]" : string.Empty),
                    Status = d.IssueState + (d.ClientResponseCode == null ? string.Empty : " — " + d.ClientResponseCode), DocumentId = d.DocumentId, Mandatory = d.Required,
                    Missing = d.Required && d.IssueState != Entities.Model.NasbNiro.ProjectControl.PcIssueState.Approved, ClientRelevant = d.ClientSubmittal,
                });
            if (s11.Entries.Count > 0) book.Sections.Add(s11);
            // client relevance: until the MDL table exists, an entry is for the client when its ITP activity has a client point
            var clientActivities = new HashSet<int>(itp == null ? new List<int>() : itp.Activities.Where(x => x.PointClient != ItpPoint.None).Select(x => x.Id));
            var reportActivity = reports.Where(r => r.ItpActivityId.HasValue).ToDictionary(r => r.Id, r => r.ItpActivityId.Value);
            var recordActivity = await _uow.Set<QcRecord>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted && r.ItpActivityId.HasValue).ToDictionaryAsync(r => r.Id, r => r.ItpActivityId.Value, cancellationToken);
            foreach (DataBookEntryDto e in book.Sections.SelectMany(x => x.Entries))
            {
                switch (e.SourceKind)
                {
                    case "Itp": case "ReleaseNote": case "Ncr": e.ClientRelevant = true; break;
                    case "ItpActivity": e.ClientRelevant = e.SourceId.HasValue && clientActivities.Contains(e.SourceId.Value); break;
                    case "TestReport": e.ClientRelevant = e.SourceId.HasValue && reportActivity.TryGetValue(e.SourceId.Value, out int ra) && clientActivities.Contains(ra); break;
                    case "QcRecord": e.ClientRelevant = e.SourceId.HasValue && recordActivity.TryGetValue(e.SourceId.Value, out int qa) && clientActivities.Contains(qa); break;
                }
            }
            book.MissingCount = book.Sections.Where(s => s.No != 10).SelectMany(s => s.Entries).Count(e => e.Mandatory && e.Missing);
            return book;
        }

        public async Task<ResponseDto<DataBookDto>> GetDataBookAsync(int workOrderId, string variant, CancellationToken cancellationToken)
        {
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == workOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return QualityText.NotFound<DataBookDto>("دستور کار پیدا نشد.");
            string v = Clean(variant)?.ToLowerInvariant() ?? "company";
            if (v != "company" && v != "client") return Bad<DataBookDto>("نوع کتاب نهایی باید company یا client باشد.");
            DataBookDto book = await BuildBookAsync(wo, cancellationToken);
            book.Variant = v;
            if (v == "client")
            {
                foreach (DataBookSectionDto sec in book.Sections) sec.Entries = sec.Entries.Where(e => e.ClientRelevant).ToList();
                book.MissingCount = book.Sections.Where(x => x.No != 10).SelectMany(x => x.Entries).Count(e => e.Mandatory && e.Missing);
            }
            return ResponseDtoHelper.Ok(book);
        }

        // -------------------------------------------------------------------------------- release checks

        private async Task<List<ReleaseCheckDto>> ComputeChecksAsync(WorkOrder wo, CancellationToken cancellationToken)
        {
            var checks = new List<ReleaseCheckDto>();
            Itp itp = await _uow.Set<Itp>().Include(i => i.Activities).FirstOrDefaultAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, cancellationToken);
            bool approved = itp != null && (itp.Status == ItpStatus.ClientApproved || itp.Status == ItpStatus.InUse || itp.Status == ItpStatus.Closed);
            checks.Add(new ReleaseCheckDto
            {
                Code = "ITP_APPROVED", Title = "برنامه بازرسی و آزمون توسط کارفرما تایید شده است", Passed = approved,
                Detail = itp == null ? "برنامه بازرسی و آزمون ساخته نشده است." : approved ? itp.Number + " rev " + itp.Revision : "وضعیت برنامه: " + itp.Status,
            });
            List<ItpActivity> open = itp == null ? new List<ItpActivity>() : itp.Activities
                .Where(a => a.GatesRelease && (a.PointVendor == ItpPoint.H || a.PointTpi == ItpPoint.H || a.PointClient == ItpPoint.H ||
                                               a.PointVendor == ItpPoint.W || a.PointTpi == ItpPoint.W || a.PointClient == ItpPoint.W) && !ItpService.IsTerminal(a.Status))
                .OrderBy(a => a.Seq).ToList();
            checks.Add(new ReleaseCheckDto
            {
                Code = "POINTS_DONE", Title = "همه نقاط توقف (H) و شاهد (W) انجام یا معاف شده‌اند", Passed = itp != null && open.Count == 0,
                Detail = itp == null ? "-" : open.Count == 0 ? "همه انجام شده‌اند." : $"{open.Count} مورد باز: " + string.Join("، ", open.Take(8).Select(a => a.ItemNo)) + (open.Count > 8 ? "…" : string.Empty),
            });
            List<NonConformance> ncrs = await _uow.Set<NonConformance>().Where(n => n.WorkOrderId == wo.Id && !n.Deleted).ToListAsync(cancellationToken);
            List<NonConformance> blocking = ncrs.Where(n => IsOpenNcr(n) && !IsConcessionApproved(n)).ToList();
            checks.Add(new ReleaseCheckDto
            {
                Code = "NO_OPEN_NCR", Title = "عدم انطباق باز (بجز دارای مجوز ارفاقی) وجود ندارد", Passed = blocking.Count == 0,
                Detail = blocking.Count == 0 ? "-" : string.Join("، ", blocking.Select(n => n.Number)),
            });
            List<string> noReport = new List<string>();
            if (itp != null)
            {
                List<TestReport> reports = await _uow.Set<TestReport>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted).ToListAsync(cancellationToken);
                foreach (ItpActivity a in itp.Activities.Where(a => a.RequiredReportType.HasValue && a.Status != ItpActivityStatus.NotApplicable))
                    if (!reports.Any(r => r.ItpActivityId == a.Id && r.Status == TestReportStatus.Approved && r.Result == ReportResult.Pass)) noReport.Add(a.ItemNo);
            }
            checks.Add(new ReleaseCheckDto
            {
                Code = "REPORTS_APPROVED", Title = "برای هر فعالیت دارای گزارش الزامی، گزارش تاییدشده و قبول موجود است", Passed = itp != null && noReport.Count == 0,
                Detail = itp == null ? "-" : noReport.Count == 0 ? "-" : "بدون گزارش: " + string.Join("، ", noReport.Take(10)),
            });
            // out-of-tolerance impact assessments still pending for documents of this work order; R1q D40: a document assessed as "re-inspect" stays pending until the
            // accepted new inspection is linked to it
            var pending = await _uow.Set<OutOfToleranceItem>().Where(i => (i.Assessment == OotAssessment.Pending || (i.Assessment == OotAssessment.Reinspect && i.ReinspectionRecordId == null)) &&
                    i.Case.Status == OotStatus.Open && !i.Case.Deleted)
                .Select(i => new { i.SourceKind, i.SourceId }).ToListAsync(cancellationToken);
            int ootPending = 0;
            if (pending.Count > 0)
            {
                List<int> recordIds = pending.Where(p => p.SourceKind == "QcRecord").Select(p => p.SourceId).ToList();
                List<int> reportIds = pending.Where(p => p.SourceKind == "TestReport").Select(p => p.SourceId).ToList();
                ootPending = await _uow.Set<QcRecord>().CountAsync(r => recordIds.Contains(r.Id) && r.WorkOrderId == wo.Id, cancellationToken) +
                             await _uow.Set<TestReport>().CountAsync(r => reportIds.Contains(r.Id) && r.WorkOrderId == wo.Id, cancellationToken);
            }
            checks.Add(new ReleaseCheckDto
            {
                Code = "OOT_ASSESSED", Title = "اثر تجهیز خارج از تلرانس روی اسناد این دستور کار ارزیابی شده است", Passed = ootPending == 0,
                Detail = ootPending == 0 ? "-" : $"{ootPending} سند در انتظار ارزیابی اثر یا بازرسی مجدد است.",
            });
            // R1q D22: the production orders of the work order are finished and the last final inspection accepted the product
            List<string> unfinished = await _uow.Set<MfProductionOrder>()
                .Where(o => o.WorkOrderId == wo.Id && !o.Deleted && o.Status != MfOrderStatus.Cancelled && o.Status != MfOrderStatus.Completed && o.Status != MfOrderStatus.Closed)
                .OrderBy(o => o.Id).Select(o => o.Number).ToListAsync(cancellationToken);
            checks.Add(new ReleaseCheckDto
            {
                Code = "PRODUCTION_COMPLETE", Title = "همه دستورهای تولید این دستور کار تکمیل شده‌اند", Passed = unfinished.Count == 0,
                Detail = unfinished.Count == 0 ? "-" : "ناتمام: " + string.Join("، ", unfinished.Take(8)),
            });
            QcResult? lastFinal = await _uow.Set<QcRecord>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted && r.Type == QcRecordType.Final)
                .OrderByDescending(r => r.Id).Select(r => (QcResult?)r.Result).FirstOrDefaultAsync(cancellationToken);
            checks.Add(new ReleaseCheckDto
            {
                Code = "FINAL_INSPECTION", Title = "آخرین بازرسی نهایی محصول با نتیجه «قبول» ثبت شده است", Passed = lastFinal == QcResult.Accept,
                Detail = lastFinal == null ? "بازرسی نهایی ثبت نشده است." : lastFinal == QcResult.Accept ? "-" : "نتیجه آخرین بازرسی نهایی: " + ResultText(lastFinal.Value),
            });
            DataBookDto book = await BuildBookAsync(wo, cancellationToken);
            var missing = book.Sections.Where(s => s.No != 10 && s.No != 11).SelectMany(s => s.Entries.Where(e => e.Mandatory && e.Missing).Select(e => s.No + ": " + (e.Number ?? e.Title))).ToList();
            checks.Add(new ReleaseCheckDto
            {
                Code = "DATABOOK_COMPLETE", Title = "فهرست کتاب نهایی (Final Data Book) مورد الزامی ناقص ندارد", Passed = missing.Count == 0,
                Detail = missing.Count == 0 ? "-" : $"{missing.Count} مورد ناقص: " + string.Join("، ", missing.Take(8)) + (missing.Count > 8 ? "…" : string.Empty),
            });
            return checks;
        }

        public async Task<ResponseDto<ReleaseChecksDto>> GetChecksAsync(int workOrderId, CancellationToken cancellationToken)
        {
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == workOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return QualityText.NotFound<ReleaseChecksDto>("دستور کار پیدا نشد.");
            List<ReleaseCheckDto> checks = await ComputeChecksAsync(wo, cancellationToken);
            return ResponseDtoHelper.Ok(new ReleaseChecksDto { WorkOrderId = wo.Id, WorkOrderCode = wo.Code, Checks = checks, CanRelease = checks.All(c => c.Passed) });
        }

        // -------------------------------------------------------------------------------- release notes

        private async Task<ReleaseNoteDto> ToDtoAsync(ReleaseNote r, CancellationToken cancellationToken)
        {
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstAsync(w => w.Id == r.WorkOrderId, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ReleasedByUserId, r.CreatorUserId }, cancellationToken);
            List<ReleaseNoteLineDto> lines = (await _uow.Set<ReleaseNoteLine>().Where(l => l.ReleaseNoteId == r.Id).OrderBy(l => l.RowNumber)
                .Select(l => new { l.RowNumber, l.ItemId, l.Item.Code, l.Item.Title, l.Item.Unit, l.Quantity, l.SerialNumbers }).ToListAsync(cancellationToken))
                .Select(l => new ReleaseNoteLineDto
                {
                    RowNumber = l.RowNumber, ItemId = l.ItemId, ItemCode = l.Code, ItemTitle = l.Title, Unit = l.Unit, Quantity = l.Quantity,
                    SerialNumbers = (l.SerialNumbers ?? string.Empty).Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                }).ToList();
            List<ReleaseCheckDto> checks;
            if (r.Status == ReleaseNoteStatus.Draft) checks = await ComputeChecksAsync(wo, cancellationToken);
            else checks = string.IsNullOrWhiteSpace(r.ChecksSnapshot) ? new List<ReleaseCheckDto>() : JsonSerializer.Deserialize<List<ReleaseCheckDto>>(r.ChecksSnapshot);
            return new ReleaseNoteDto
            {
                Id = r.Id, Number = r.Number, WorkOrderId = r.WorkOrderId, WorkOrderCode = wo.Code, Status = r.Status, ReleasedAt = r.ReleasedAt,
                CertificateOfConformityNumber = r.CertificateOfConformityNumber, ContractId = r.ContractId, ItemsText = r.ItemsText, Note = r.Note,
                CancelReason = r.CancelReason, ReleasedByName = names.Name(r.ReleasedByUserId), DraftedByName = names.Name(r.CreatorUserId),
                RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion), Lines = lines,
                Checks = checks, CanRelease = r.Status == ReleaseNoteStatus.Draft && lines.Count > 0 && checks.All(c => c.Passed),
            };
        }

        public async Task<ResponseDto<PagedList<ReleaseNoteListDto>>> GetNotesAsync(ReleaseNoteQuery query, CancellationToken cancellationToken)
        {
            query ??= new ReleaseNoteQuery();
            IQueryable<ReleaseNote> rows = _uow.Set<ReleaseNote>().Where(r => !r.Deleted);
            if (query.WorkOrderId.HasValue) rows = rows.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            if (query.Status.HasValue) rows = rows.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(r => r.Number.Contains(text) || r.WorkOrder.Code.Contains(text));
            IQueryable<ReleaseNoteListDto> projection = rows.OrderByDescending(r => r.Id).Select(r => new ReleaseNoteListDto
            {
                Id = r.Id, Number = r.Number, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder.Code, Status = r.Status, ReleasedAt = r.ReleasedAt,
                CertificateOfConformityNumber = r.CertificateOfConformityNumber,
            });
            return ResponseDtoHelper.Ok(await PagedList<ReleaseNoteListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<ReleaseNoteDto>> GetNoteAsync(int id, CancellationToken cancellationToken)
        {
            ReleaseNote r = await _uow.Set<ReleaseNote>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return r == null ? QualityText.NotFound<ReleaseNoteDto>("برگه ترخیص پیدا نشد.") : ResponseDtoHelper.Ok(await ToDtoAsync(r, cancellationToken));
        }

        /// <summary>
        /// The lines of a release note: existing items, a positive quantity and, for a serial item, one serial number per piece. Review H4: the lines are checked against the
        /// work order they release: a serial number must be a piece this work order made, and a quantity cannot be more than the work order produced or received.
        /// </summary>
        private async Task<(List<ReleaseNoteLine> Lines, string Error)> BuildLinesAsync(List<SaveReleaseNoteLineDto> input, WorkOrder wo, CancellationToken cancellationToken)
        {
            if (input == null || input.Count == 0)
                return (null, "فهرست اقلام برگه ترخیص (کالا، مقدار و سریال‌ها) را وارد کنید؛ تحویل به مشتری فقط تا سقف همین فهرست مجاز است.");
            List<int> ids = input.Where(l => l != null).Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => ids.Contains(i.Id) && !i.Deleted).ToDictionaryAsync(i => i.Id, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ReleaseNoteLine>();
            int number = 1;
            foreach (SaveReleaseNoteLineDto line in input)
            {
                string row = $"ردیف {number}: ";
                if (line == null || !items.TryGetValue(line.ItemId, out InventoryItem item)) return (null, row + "کالا پیدا نشد.");
                if (line.Quantity <= 0) return (null, row + "مقدار باید بیشتر از صفر باشد.");
                List<string> serials = (line.SerialNumbers ?? new List<string>()).Select(Clean).Where(x => x != null).ToList();
                if (item.TrackingMode == ItemTrackingMode.Serial)
                {
                    if (line.Quantity != decimal.Truncate(line.Quantity)) return (null, row + "کالای سریالی با عدد صحیح ترخیص می‌شود.");
                    if (serials.Count != (int)line.Quantity) return (null, row + $"سریال قطعات {item.Code} را وارد کنید (تعداد سریال‌ها برابر مقدار).");
                    foreach (string serial in serials)
                        if (!seen.Add(item.Id + "|" + serial)) return (null, row + $"سریال {serial} تکراری است.");
                }
                else
                {
                    if (!seen.Add(item.Id + "|")) return (null, row + $"کالای {item.Code} دو بار آمده است؛ مقدارها را در یک ردیف جمع کنید.");
                    serials.Clear();
                }
                // the serial numbers are kept in a column of 4000 characters: a longer list is refused here, not by the database (review L21)
                if (string.Join("\n", serials).Length > 4000) return (null, row + "فهرست سریال‌ها بیش از ۴۰۰۰ نویسه است؛ ترخیص را در چند برگه یا چند ردیف ثبت کنید.");
                rows.Add(new ReleaseNoteLine { RowNumber = number++, ItemId = item.Id, Quantity = line.Quantity, SerialNumbers = serials.Count == 0 ? null : string.Join("\n", serials) });
            }
            string workOrderProblem = await LinesProblemAsync(wo, rows, items, cancellationToken);
            return workOrderProblem == null ? (rows, null) : (null, workOrderProblem);
        }

        /// <summary>
        /// Null when every line of a release note is something the work order really has: a serial number of a piece made under it (not scrapped, returned or converted), and a
        /// quantity within what its confirmed production receipts and purchase receipts brought in, less supplier returns.
        /// </summary>
        private async Task<string> LinesProblemAsync(WorkOrder wo, List<ReleaseNoteLine> lines, Dictionary<int, InventoryItem> items, CancellationToken cancellationToken)
        {
            List<int> itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var serialPairs = lines.Where(l => !string.IsNullOrWhiteSpace(l.SerialNumbers))
                .SelectMany(l => l.SerialNumbers.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => new { l.ItemId, Serial = x })).ToList();
            List<string> serialNames = serialPairs.Select(x => x.Serial).Distinct().ToList();
            List<SerialUnit> units = serialNames.Count == 0
                ? new List<SerialUnit>()
                : await _uow.Set<SerialUnit>().Where(u => itemIds.Contains(u.ItemId) && serialNames.Contains(u.SerialNumber) && !u.Deleted).ToListAsync(cancellationToken);
            foreach (var pair in serialPairs)
            {
                SerialUnit unit = units.FirstOrDefault(u => u.ItemId == pair.ItemId && string.Equals(u.SerialNumber, pair.Serial, StringComparison.OrdinalIgnoreCase));
                string code = items.TryGetValue(pair.ItemId, out InventoryItem it) ? it.Code : pair.ItemId.ToString();
                if (unit == null) return $"سریال {pair.Serial} ({code}) در سامانه ثبت نشده است.";
                if (unit.WorkOrderId != wo.Id) return $"سریال {pair.Serial} ({code}) در دستور کار {wo.Code} ساخته یا ثبت نشده است؛ فقط قطعات همین دستور کار ترخیص می‌شوند.";
                if (unit.Status == SerialUnitStatus.Scrapped || unit.Status == SerialUnitStatus.ReturnedToSupplier || unit.Status == SerialUnitStatus.Converted)
                    return $"سریال {pair.Serial} ({code}) به‌دلیل وضعیت فعلی (ضایعات، برگشتی یا تبدیل‌شده) ترخیص نمی‌شود.";
            }
            List<int> quantityItems = lines.Where(l => string.IsNullOrWhiteSpace(l.SerialNumbers)).Select(l => l.ItemId).Distinct().ToList();
            if (quantityItems.Count > 0)
            {
                var moved = await _uow.Set<InventoryDocumentLine>()
                    .Where(l => quantityItems.Contains(l.ItemId) && l.Document.WorkOrderId == wo.Id && l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted &&
                                (l.Document.Type == InventoryDocumentType.ProductionReceipt || l.Document.Type == InventoryDocumentType.PurchaseReceipt ||
                                 l.Document.Type == InventoryDocumentType.CustomerPropertyReceipt || l.Document.Type == InventoryDocumentType.SupplierReturn))
                    .Select(l => new { l.ItemId, l.Quantity, l.Document.Type }).ToListAsync(cancellationToken);
                foreach (ReleaseNoteLine line in lines.Where(l => string.IsNullOrWhiteSpace(l.SerialNumbers)))
                {
                    decimal made = moved.Where(m => m.ItemId == line.ItemId).Sum(m => m.Type == InventoryDocumentType.SupplierReturn ? -m.Quantity : m.Quantity);
                    string code = items.TryGetValue(line.ItemId, out InventoryItem it) ? it.Code : line.ItemId.ToString();
                    if (made <= 0) return $"برای کالای {code} در دستور کار {wo.Code} رسید ساخت یا رسید خریدی ثبت نشده است؛ چیزی که ساخته یا دریافت نشده ترخیص نمی‌شود.";
                    if (line.Quantity > made)
                        return $"مقدار ترخیص کالای {code} ({line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}) از مقدار ساخته یا دریافت‌شده برای دستور کار ({made.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}) بیشتر است.";
                }
            }
            return null;
        }

        public async Task<ResponseDto<ReleaseNoteDto>> SaveNoteAsync(SaveReleaseNoteDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ReleaseNoteDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ReleaseNoteDto>(QualityText.NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return Bad<ReleaseNoteDto>("دستور کار پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            ReleaseNote r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<ReleaseNote>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (r == null) return QualityText.NotFound<ReleaseNoteDto>("برگه ترخیص پیدا نشد.");
                if (r.Status != ReleaseNoteStatus.Draft) return Bad<ReleaseNoteDto>("برگه صادرشده یا لغوشده ویرایش نمی‌شود.");
            }
            else
            {
                // a released note that lists no items (made before the lists existed) covers nothing and does not stop a new, complete note
                if (await _uow.Set<ReleaseNote>().AnyAsync(x => x.WorkOrderId == wo.Id && !x.Deleted && x.Status != ReleaseNoteStatus.Cancelled &&
                                                                 (x.Status != ReleaseNoteStatus.Released || x.Lines.Any()), cancellationToken))
                    return Bad<ReleaseNoteDto>("برای این دستور کار برگه ترخیص فعال وجود دارد.");
                r = null;
            }
            (List<ReleaseNoteLine> lines, string lineError) = await BuildLinesAsync(dto.Lines, wo, cancellationToken);
            if (lineError != null) return Bad<ReleaseNoteDto>(lineError);
            if (r == null)
            {
                r = new ReleaseNote
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.ReleaseNote, "RN", DateTime.Today, cancellationToken),
                    WorkOrderId = wo.Id, ContractId = wo.ContractId, Status = ReleaseNoteStatus.Draft, CreateDate = now, CreatorUserId = Me,
                    Lines = new List<ReleaseNoteLine>(),
                };
                _uow.Set<ReleaseNote>().Add(r);
            }
            else
            {
                _uow.Set<ReleaseNoteLine>().RemoveRange(await _uow.Set<ReleaseNoteLine>().Where(l => l.ReleaseNoteId == r.Id).ToListAsync(cancellationToken));
                r.Lines = new List<ReleaseNoteLine>();
            }
            foreach (ReleaseNoteLine line in lines) r.Lines.Add(line);
            // R1q D22: everybody who saved the draft is remembered; none of them releases it
            r.EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(r.EditorUserIds, Me);
            r.ItemsText = Clean(dto.ItemsText); r.Note = Clean(dto.Note); r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "release:" + r.Id, r.Status == ReleaseNoteStatus.Draft, "quality", "release-note", r.Id,
                "صدور برگه ترخیص " + r.Number, "/quality/release-notes/" + r.Id, QualityRoles.Inspectors, cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<ReleaseNoteDto>> NoteActionAsync(ReleaseNoteActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ReleaseNoteDto>(QualityText.InvalidData);
            ReleaseNote r = await _uow.Set<ReleaseNote>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<ReleaseNoteDto>("برگه ترخیص پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "release":
                    if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<ReleaseNoteDto>(QualityText.NotAllowed + " (برگه ترخیص و گواهی انطباق را مدیر QC امضا می‌کند)");
                    if (r.Status != ReleaseNoteStatus.Draft) return Bad<ReleaseNoteDto>("فقط برگه پیش‌نویس صادر می‌شود.");
                    // R1q D22: the person who drafted or edited the note cannot release it (maker-checker)
                    if (global::Services.NasbNiro.Finance.LedgerMath.Touched(r.CreatorUserId, r.EditorUserIds, Me))
                        return Bad<ReleaseNoteDto>(QualityText.SameUser + " (برگه را کسی که آن را تنظیم یا ویرایش کرده نمی‌تواند صادر کند؛ مدیر QC دیگری امضا کند)");
                    if (!await _uow.Set<ReleaseNoteLine>().AnyAsync(l => l.ReleaseNoteId == r.Id, cancellationToken))
                        return Bad<ReleaseNoteDto>("فهرست اقلام برگه ترخیص خالی است؛ کالا، مقدار و سریال‌های ترخیص‌شده را ثبت کنید.");
                    WorkOrder wo = await _uow.Set<WorkOrder>().FirstAsync(w => w.Id == r.WorkOrderId, cancellationToken);
                    // the lines are looked at again against the work order now (a receipt may have been returned since the draft was made)
                    List<ReleaseNoteLine> savedLines = await _uow.Set<ReleaseNoteLine>().Where(l => l.ReleaseNoteId == r.Id).ToListAsync(cancellationToken);
                    Dictionary<int, InventoryItem> lineItems = await _uow.Set<InventoryItem>().Where(i => savedLines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
                    string linesProblem = await LinesProblemAsync(wo, savedLines, lineItems, cancellationToken);
                    if (linesProblem != null) return Bad<ReleaseNoteDto>("ترخیص ممکن نیست: " + linesProblem);
                    List<ReleaseCheckDto> checks = await ComputeChecksAsync(wo, cancellationToken);
                    List<ReleaseCheckDto> failed = checks.Where(c => !c.Passed).ToList();
                    if (failed.Count > 0) return Bad<ReleaseNoteDto>("ترخیص ممکن نیست: " + string.Join(" | ", failed.Select(c => c.Title + (c.Detail == "-" ? string.Empty : " (" + c.Detail + ")"))));
                    r.Status = ReleaseNoteStatus.Released; r.ReleasedAt = now; r.ReleasedByUserId = Me;
                    r.CertificateOfConformityNumber = "CoC-" + r.Number;
                    r.ChecksSnapshot = JsonSerializer.Serialize(checks);
                    break;
                case "cancel":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ReleaseNoteDto>(QualityText.NotAllowed);
                    if (r.Status == ReleaseNoteStatus.Cancelled) return Bad<ReleaseNoteDto>("برگه قبلاً لغو شده است.");
                    if (Clean(dto.Reason) == null) return Bad<ReleaseNoteDto>("دلیل لغو را بنویسید.");
                    if (Clean(dto.Reason).Length > 1000) return Bad<ReleaseNoteDto>("دلیل لغو نباید بیش از ۱۰۰۰ نویسه باشد.");
                    r.Status = ReleaseNoteStatus.Cancelled; r.CancelReason = Clean(dto.Reason);
                    break;
                default:
                    return Bad<ReleaseNoteDto>("عملیات نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<ReleaseNoteDto>(QualityText.Concurrency); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "release:" + r.Id, r.Status == ReleaseNoteStatus.Draft, "quality", "release-note", r.Id,
                "صدور برگه ترخیص " + r.Number, "/quality/release-notes/" + r.Id, QualityRoles.Inspectors, cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, cancellationToken));
        }
    }
}
