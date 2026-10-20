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
        Task<ResponseDto<DataBookDto>> GetDataBookAsync(int workOrderId, CancellationToken cancellationToken);

        /// <summary>Null when a Released note exists for the work order, otherwise the Persian reason (customer delivery gate, ISO 8.6).</summary>
        Task<string> CheckDeliveryAsync(int? workOrderId, CancellationToken cancellationToken);
    }

    /// <summary>Product release (ISO 8.6): the checks and the final data book index are computed from the records, never typed.</summary>
    public class ReleaseService : IReleaseService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public ReleaseService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        public async Task<string> CheckDeliveryAsync(int? workOrderId, CancellationToken cancellationToken)
        {
            if (!workOrderId.HasValue) return "تحویل به مشتری باید به دستور کار مرتبط باشد تا برگه ترخیص کیفی بررسی شود.";
            bool released = await _uow.Set<ReleaseNote>().AnyAsync(r => r.WorkOrderId == workOrderId.Value && r.Status == ReleaseNoteStatus.Released && !r.Deleted, cancellationToken);
            return released ? null : "برای این دستور کار «برگه ترخیص کیفی» صادرشده وجود ندارد؛ تحویل به مشتری بدون ترخیص QC مجاز نیست.";
        }

        // -------------------------------------------------------------------------------- data book

        private static bool IsOpenNcr(NonConformance n) => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled;

        private static bool IsConcessionApproved(NonConformance n) =>
            n.Disposition == NcrDisposition.Concession && n.ConcessionApprovedAt.HasValue;

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
                    SourceKind = "WeldLog", SourceId = l.Id, Number = l.JointNo, Title = $"{l.WeldDate:yyyy-MM-dd} — {l.Qualification.Person.Name} ({l.Qualification.Method}) — WPS {l.Wps.Number}/{l.Wps.Revision}",
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
                    SourceKind = "TestReport", SourceId = r.Id, Number = r.Number, Title = $"{r.Type} — {r.ReportDate:yyyy-MM-dd}", Status = $"{r.Status} / {r.Result}",
                    DocumentId = r.DocumentId, Mandatory = true, Missing = r.Status != TestReportStatus.Approved,
                });
            }
            foreach (QcRecord r in await _uow.Set<QcRecord>().Where(r => r.WorkOrderId == wo.Id && !r.Deleted).OrderBy(r => r.Id).ToListAsync(cancellationToken))
                s5.Entries.Add(new DataBookEntryDto { SourceKind = "QcRecord", SourceId = r.Id, Number = r.Number, Title = $"{r.Type} — {r.RecordDate:yyyy-MM-dd}", Status = r.Result.ToString(), DocumentId = r.AttachmentDocumentId });
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
                    SourceKind = "Equipment", SourceId = e.Id, Number = last?.CertificateNumber, Title = $"{e.Code} — {e.Name}", Status = last == null ? "-" : $"{last.EventDate:yyyy-MM-dd}",
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
            book.MissingCount = book.Sections.Where(s => s.No != 10).SelectMany(s => s.Entries).Count(e => e.Mandatory && e.Missing);
            return book;
        }

        public async Task<ResponseDto<DataBookDto>> GetDataBookAsync(int workOrderId, CancellationToken cancellationToken)
        {
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == workOrderId && !w.Deleted, cancellationToken);
            return wo == null ? QualityText.NotFound<DataBookDto>("دستور کار پیدا نشد.") : ResponseDtoHelper.Ok(await BuildBookAsync(wo, cancellationToken));
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
            // out-of-tolerance impact assessments still pending for documents of this work order
            var pending = await _uow.Set<OutOfToleranceItem>().Where(i => i.Assessment == OotAssessment.Pending && i.Case.Status == OotStatus.Open && !i.Case.Deleted)
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
                Detail = ootPending == 0 ? "-" : $"{ootPending} سند در انتظار ارزیابی اثر است.",
            });
            DataBookDto book = await BuildBookAsync(wo, cancellationToken);
            var missing = book.Sections.Where(s => s.No != 10).SelectMany(s => s.Entries.Where(e => e.Mandatory && e.Missing).Select(e => s.No + ": " + (e.Number ?? e.Title))).ToList();
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
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ReleasedByUserId }, cancellationToken);
            List<ReleaseCheckDto> checks;
            if (r.Status == ReleaseNoteStatus.Draft) checks = await ComputeChecksAsync(wo, cancellationToken);
            else checks = string.IsNullOrWhiteSpace(r.ChecksSnapshot) ? new List<ReleaseCheckDto>() : JsonSerializer.Deserialize<List<ReleaseCheckDto>>(r.ChecksSnapshot);
            return new ReleaseNoteDto
            {
                Id = r.Id, Number = r.Number, WorkOrderId = r.WorkOrderId, WorkOrderCode = wo.Code, Status = r.Status, ReleasedAt = r.ReleasedAt,
                CertificateOfConformityNumber = r.CertificateOfConformityNumber, ContractId = r.ContractId, ItemsText = r.ItemsText, Note = r.Note,
                CancelReason = r.CancelReason, ReleasedByName = names.Name(r.ReleasedByUserId), RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                Checks = checks, CanRelease = r.Status == ReleaseNoteStatus.Draft && checks.All(c => c.Passed),
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
                if (await _uow.Set<ReleaseNote>().AnyAsync(x => x.WorkOrderId == wo.Id && !x.Deleted && x.Status != ReleaseNoteStatus.Cancelled, cancellationToken))
                    return Bad<ReleaseNoteDto>("برای این دستور کار برگه ترخیص فعال وجود دارد.");
                r = new ReleaseNote
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.ReleaseNote, "RN", DateTime.Today, cancellationToken),
                    WorkOrderId = wo.Id, ContractId = wo.ContractId, Status = ReleaseNoteStatus.Draft, CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<ReleaseNote>().Add(r);
            }
            r.ItemsText = Clean(dto.ItemsText); r.Note = Clean(dto.Note); r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
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
                    WorkOrder wo = await _uow.Set<WorkOrder>().FirstAsync(w => w.Id == r.WorkOrderId, cancellationToken);
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
                    r.Status = ReleaseNoteStatus.Cancelled; r.CancelReason = Clean(dto.Reason);
                    break;
                default:
                    return Bad<ReleaseNoteDto>("عملیات نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<ReleaseNoteDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, cancellationToken));
        }
    }
}
