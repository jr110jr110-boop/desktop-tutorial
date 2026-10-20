using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IMaterialService
    {
        Task<ResponseDto<PagedList<MaterialCertificateDto>>> GetCertificatesAsync(MaterialCertificateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<MaterialCertificateDto>> SaveCertificateAsync(SaveMaterialCertificateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MaterialCertificateDto>> VerifyCertificateAsync(VerifyCertificateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TraceabilityDto>> TraceAsync(TraceabilityQuery query, CancellationToken cancellationToken);

        /// <summary>Null when the lot may be accepted at incoming inspection, otherwise the Persian reason (EN 10204 type 2.2 &lt; 3.1 &lt; 3.2).</summary>
        Task<string> CheckRequiredCertificateAsync(int itemId, int? lotId, CancellationToken cancellationToken);
    }

    /// <summary>EN 10204 material certificates of lots and the traceability query by serial or lot.</summary>
    public class MaterialService : IMaterialService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public MaterialService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        public static string TypeText(CertificateType type) => type == CertificateType.T22 ? "2.2" : type == CertificateType.T31 ? "3.1" : "3.2";

        private async Task<List<MaterialCertificateDto>> LoadAsync(IQueryable<MaterialCertificate> rows, CancellationToken cancellationToken)
        {
            List<MaterialCertificate> list = await rows.Include(c => c.Lot).ThenInclude(l => l.Item).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, list.SelectMany(c => new int?[] { c.CreatorUserId, c.VerifiedByUserId }), cancellationToken);
            return list.Select(c => new MaterialCertificateDto
            {
                Id = c.Id, LotId = c.LotId, LotNumber = c.Lot?.LotNumber, HeatNumber = c.Lot?.HeatNumber, ItemId = c.Lot?.ItemId ?? 0, ItemCode = c.Lot?.Item?.Code,
                ItemTitle = c.Lot?.Item?.Title, Type = c.Type, Number = c.Number, Issuer = c.Issuer, DocumentId = c.DocumentId, Summary = c.Summary, Result = c.Result,
                CreatorName = names.Name(c.CreatorUserId), VerifiedByName = names.Name(c.VerifiedByUserId), VerifiedAt = c.VerifiedAt, VerificationNote = c.VerificationNote,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<MaterialCertificateDto>>> GetCertificatesAsync(MaterialCertificateQuery query, CancellationToken cancellationToken)
        {
            query ??= new MaterialCertificateQuery();
            IQueryable<MaterialCertificate> rows = _uow.Set<MaterialCertificate>().Where(c => !c.Deleted);
            if (query.LotId.HasValue) rows = rows.Where(c => c.LotId == query.LotId.Value);
            if (query.ItemId.HasValue) rows = rows.Where(c => c.Lot.ItemId == query.ItemId.Value);
            if (query.Result.HasValue) rows = rows.Where(c => c.Result == query.Result.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(c => c.Number.Contains(text) || c.Lot.LotNumber.Contains(text) || c.Lot.HeatNumber.Contains(text) || c.Lot.Item.Code.Contains(text));
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MaterialCertificateDto> list = await LoadAsync(rows.OrderByDescending(c => c.Id).Skip((number - 1) * size).Take(size), cancellationToken);
            list = list.OrderByDescending(c => c.Id).ToList();
            PagedList<MaterialCertificateDto> page = await PagedList<MaterialCertificateDto>.CreateAsync(list, 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<MaterialCertificateDto>> SaveCertificateAsync(SaveMaterialCertificateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(CertificateType), dto.Type) || Clean(dto.Number) == null) return Bad<MaterialCertificateDto>("نوع و شماره گواهی را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.CertificateWriters)) return Bad<MaterialCertificateDto>(QualityText.NotAllowed);
            InventoryLot lot = await _uow.Set<InventoryLot>().FirstOrDefaultAsync(l => l.Id == dto.LotId && !l.Deleted, cancellationToken);
            if (lot == null) return Bad<MaterialCertificateDto>("بچ پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            MaterialCertificate cert;
            if (dto.Id.HasValue)
            {
                cert = await _uow.Set<MaterialCertificate>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, cancellationToken);
                if (cert == null) return QualityText.NotFound<MaterialCertificateDto>("گواهی پیدا نشد.");
                if (cert.Result != CertificateResult.Pending) return Bad<MaterialCertificateDto>("گواهی بررسی‌شده ویرایش نمی‌شود؛ گواهی جدید ثبت کنید.");
            }
            else
            {
                cert = new MaterialCertificate { CreateDate = now, CreatorUserId = Me, Result = CertificateResult.Pending };
                _uow.Set<MaterialCertificate>().Add(cert);
            }
            string number = dto.Number.Trim();
            if (await _uow.Set<MaterialCertificate>().AnyAsync(c => c.LotId == lot.Id && c.Number == number && c.Id != cert.Id && !c.Deleted, cancellationToken))
                return Bad<MaterialCertificateDto>("این شماره گواهی برای این بچ قبلاً ثبت شده است.");
            cert.LotId = lot.Id; cert.Type = dto.Type; cert.Number = number; cert.Issuer = Clean(dto.Issuer); cert.DocumentId = dto.DocumentId; cert.Summary = Clean(dto.Summary);
            cert.ModifyDate = now; cert.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadAsync(_uow.Set<MaterialCertificate>().Where(c => c.Id == cert.Id), cancellationToken))[0]);
        }

        public async Task<ResponseDto<MaterialCertificateDto>> VerifyCertificateAsync(VerifyCertificateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<MaterialCertificateDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<MaterialCertificateDto>(QualityText.NotAllowed + " (گواهی را فقط QC تایید می‌کند)");
            MaterialCertificate cert = await _uow.Set<MaterialCertificate>().Include(c => c.Lot).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, cancellationToken);
            if (cert == null) return QualityText.NotFound<MaterialCertificateDto>("گواهی پیدا نشد.");
            if (cert.Result != CertificateResult.Pending) return Bad<MaterialCertificateDto>("این گواهی قبلاً بررسی شده است.");
            if (cert.CreatorUserId == Me) return Bad<MaterialCertificateDto>(QualityText.SameUser);
            if (dto.Accept && cert.DocumentId == null) return Bad<MaterialCertificateDto>("برای تایید، تصویر یا فایل گواهی را پیوست کنید.");
            if (!dto.Accept && Clean(dto.Note) == null) return Bad<MaterialCertificateDto>("دلیل رد گواهی را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;
            cert.Result = dto.Accept ? CertificateResult.Accepted : CertificateResult.Rejected;
            cert.VerifiedByUserId = Me; cert.VerifiedAt = now; cert.VerificationNote = Clean(dto.Note);
            cert.ModifyDate = now; cert.ModifierUserId = Me;
            if (dto.Accept)
            {
                // the lot keeps the strongest verified certificate (existing warehouse fields stay in sync)
                CertificateType best = await _uow.Set<MaterialCertificate>()
                    .Where(c => c.LotId == cert.LotId && c.Result == CertificateResult.Accepted && !c.Deleted && c.Id != cert.Id)
                    .Select(c => (CertificateType?)c.Type).MaxAsync(cancellationToken) ?? cert.Type;
                if (cert.Type >= best)
                {
                    cert.Lot.CertificateNumber = cert.Number; cert.Lot.CertificateType = TypeText(cert.Type); cert.Lot.CertificateDocumentId = cert.DocumentId;
                }
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadAsync(_uow.Set<MaterialCertificate>().Where(c => c.Id == cert.Id), cancellationToken))[0]);
        }

        public async Task<string> CheckRequiredCertificateAsync(int itemId, int? lotId, CancellationToken cancellationToken)
        {
            CertificateType? required = await _uow.Set<InventoryItem>().Where(i => i.Id == itemId).Select(i => i.RequiredCertificateType).FirstOrDefaultAsync(cancellationToken);
            if (!required.HasValue) return null;
            string need = TypeText(required.Value);
            if (!lotId.HasValue) return $"این کالا گواهی EN 10204 نوع {need} (یا بالاتر) می‌خواهد؛ بچ مشخص نیست.";
            bool ok = await _uow.Set<MaterialCertificate>().AnyAsync(c => c.LotId == lotId.Value && !c.Deleted && c.Result == CertificateResult.Accepted && c.Type >= required.Value, cancellationToken);
            return ok ? null : $"این کالا گواهی EN 10204 نوع {need} (یا بالاتر) می‌خواهد و برای این بچ گواهی تاییدشده توسط QC ثبت نشده است.";
        }

        // -------------------------------------------------------------------------------- traceability

        public async Task<ResponseDto<TraceabilityDto>> TraceAsync(TraceabilityQuery query, CancellationToken cancellationToken)
        {
            if (query == null) return Bad<TraceabilityDto>(QualityText.InvalidData);
            string serial = Clean(query.Serial);
            string lotNumber = Clean(query?.LotNumber);
            if (serial == null && !query.LotId.HasValue && lotNumber == null) return Bad<TraceabilityDto>("سریال یا بچ را مشخص کنید.");
            var result = new TraceabilityDto { Serial = serial };
            var lotRelations = new Dictionary<int, string>();
            var serialIds = new List<int>();

            if (serial != null)
            {
                List<SerialUnit> units = await _uow.Set<SerialUnit>().Where(s => s.SerialNumber == serial && !s.Deleted).ToListAsync(cancellationToken);
                if (units.Count == 0 && query.LotId == null && lotNumber == null)
                    return QualityText.NotFound<TraceabilityDto>("سریال پیدا نشد.");
                foreach (SerialUnit unit in units)
                {
                    serialIds.Add(unit.Id);
                    if (unit.LotId.HasValue) lotRelations[unit.LotId.Value] = "بچ همین سریال";
                    result.StageHistory.Add(new TraceStageDto { When = unit.CreateDate, Label = "ثبت سریال", Detail = $"{unit.SerialNumber} — {unit.Status}" });
                }
                // components installed into this assembly (genealogy), up to three levels
                List<int> frontier = serialIds.ToList();
                for (int depth = 0; depth < 3 && frontier.Count > 0; depth++)
                {
                    var links = await _uow.Set<AssemblyLink>().Where(a => frontier.Contains(a.ParentSerialUnitId) && !a.Deleted)
                        .Select(a => new { a.ChildSerialUnitId, a.ChildLotId, ChildLotOfSerial = a.ChildSerialUnit.LotId, a.InstalledAt, ChildSerial = a.ChildSerialUnit.SerialNumber })
                        .ToListAsync(cancellationToken);
                    frontier = new List<int>();
                    foreach (var link in links)
                    {
                        int? lotId = link.ChildLotId ?? link.ChildLotOfSerial;
                        if (lotId.HasValue && !lotRelations.ContainsKey(lotId.Value)) lotRelations[lotId.Value] = "قطعه نصب‌شده " + (link.ChildSerial ?? "(بچ)");
                        result.StageHistory.Add(new TraceStageDto { When = link.InstalledAt, Label = "نصب قطعه", Detail = link.ChildSerial ?? "قطعه بچی" });
                        if (link.ChildSerialUnitId.HasValue && !serialIds.Contains(link.ChildSerialUnitId.Value)) { serialIds.Add(link.ChildSerialUnitId.Value); frontier.Add(link.ChildSerialUnitId.Value); }
                    }
                }
            }
            if (query.LotId.HasValue) lotRelations[query.LotId.Value] = "بچ درخواستی";
            if (lotNumber != null)
                foreach (int id in await _uow.Set<InventoryLot>().Where(l => l.LotNumber == lotNumber && !l.Deleted).Select(l => l.Id).ToListAsync(cancellationToken))
                    lotRelations[id] = "بچ درخواستی";

            List<int> lotIds = lotRelations.Keys.ToList();
            List<InventoryLot> lots = await _uow.Set<InventoryLot>().Include(l => l.Item).Where(l => lotIds.Contains(l.Id)).ToListAsync(cancellationToken);
            if (serial == null && lots.Count == 0) return QualityText.NotFound<TraceabilityDto>("بچ پیدا نشد.");
            List<MaterialCertificateDto> certificates = await LoadAsync(_uow.Set<MaterialCertificate>().Where(c => lotIds.Contains(c.LotId) && !c.Deleted), cancellationToken);
            var receipts = await _uow.Set<InventoryDocumentLine>()
                .Where(l => l.LotId.HasValue && lotIds.Contains(l.LotId.Value) && l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted &&
                            (l.Document.Type == InventoryDocumentType.PurchaseReceipt || l.Document.Type == InventoryDocumentType.ProductionReceipt || l.Document.Type == InventoryDocumentType.OpeningBalance))
                .Select(l => new { LotId = l.LotId.Value, l.Document.Number, l.Document.DocumentDate, Provider = l.Document.Provider.Name, l.Quantity }).ToListAsync(cancellationToken);
            foreach (InventoryLot lot in lots)
                result.Lots.Add(new TraceLotDto
                {
                    LotId = lot.Id, LotNumber = lot.LotNumber, HeatNumber = lot.HeatNumber, ItemCode = lot.Item?.Code, ItemTitle = lot.Item?.Title,
                    CertificateType = lot.CertificateType, CertificateNumber = lot.CertificateNumber, Relation = lotRelations[lot.Id],
                    Certificates = certificates.Where(c => c.LotId == lot.Id).ToList(),
                    Receipts = receipts.Where(r => r.LotId == lot.Id).Select(r => new TraceReceiptDto { DocumentNumber = r.Number, DocumentDate = r.DocumentDate, Provider = r.Provider, Quantity = r.Quantity }).ToList(),
                });

            // nonconformances, test reports and weld logs that name the serial or the lot
            IQueryable<NonConformance> ncrs = _uow.Set<NonConformance>().Where(n => !n.Deleted && ((n.LotId.HasValue && lotIds.Contains(n.LotId.Value)) || (serial != null && n.SerialNumbers.Contains(serial))));
            result.Ncrs = (await ncrs.OrderBy(n => n.Id).Select(n => new { n.Id, n.Number, n.Status, n.Severity, n.Description }).ToListAsync(cancellationToken))
                .Select(n => new TraceNcrDto { Id = n.Id, Number = n.Number, Status = n.Status.ToString(), Severity = n.Severity.ToString(), Description = n.Description }).ToList();
            if (serial != null)
            {
                result.TestReports = await _uow.Set<TestReport>().Where(r => !r.Deleted && r.SerialNumber == serial).OrderBy(r => r.Id).Select(r => new TestReportListDto
                {
                    Id = r.Id, Number = r.Number, Type = r.Type, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder.Code, ItpActivityId = r.ItpActivityId,
                    ReportDate = r.ReportDate, Status = r.Status, Result = r.Result, NcrId = r.NcrId,
                }).ToListAsync(cancellationToken);
                var logs = await _uow.Set<WeldLog>().Where(l => !l.Deleted && l.SerialNumber == serial).OrderBy(l => l.WeldDate).Select(l => new
                {
                    l.Id, l.WeldDate, l.WorkOrderId, WorkOrderCode = l.WorkOrder.Code, l.SerialNumber, l.JointNo, l.WpsId, Wps = l.Wps.Number + "/" + l.Wps.Revision,
                    l.QualificationId, Welder = l.Qualification.Person.Name, Stamp = l.Qualification.Person.StampId, l.Qualification.Method, l.FillerBatch, l.Vt,
                }).ToListAsync(cancellationToken);
                result.WeldLogs = logs.Select(l => new WeldLogDto
                {
                    Id = l.Id, WeldDate = l.WeldDate, WorkOrderId = l.WorkOrderId, WorkOrderCode = l.WorkOrderCode, SerialNumber = l.SerialNumber, JointNo = l.JointNo,
                    WpsId = l.WpsId, WpsNumber = l.Wps, QualificationId = l.QualificationId, WelderName = l.Welder, WelderStamp = l.Stamp, Process = l.Method,
                    FillerBatch = l.FillerBatch, Vt = l.Vt,
                }).ToList();
                foreach (var op in await _uow.Set<PartOperation>().Where(p => p.SerialUnitId.HasValue && serialIds.Contains(p.SerialUnitId.Value) && !p.Deleted).OrderBy(p => p.Sequence)
                    .Select(p => new { p.Title, p.Status, p.FinishedAt, p.StartedAt, p.OperatorName, p.QcStatus }).ToListAsync(cancellationToken))
                    result.StageHistory.Add(new TraceStageDto { When = op.FinishedAt ?? op.StartedAt, Label = op.Title, Detail = $"{op.Status}; QC: {op.QcStatus}; {op.OperatorName}" });
            }
            result.StageHistory = result.StageHistory.OrderBy(s => s.When).ToList();
            return ResponseDtoHelper.Ok(result);
        }
    }
}
