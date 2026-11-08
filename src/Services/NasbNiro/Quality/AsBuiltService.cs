using System;
using System.Collections.Generic;
using System.Linq;
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
    public interface IAsBuiltService
    {
        Task<ResponseDto<PagedList<AsBuiltListDto>>> GetAsync(AsBuiltQuery query, CancellationToken ct);
        Task<ResponseDto<AsBuiltDto>> GetByIdAsync(int id, CancellationToken ct);
        Task<ResponseDto<AsBuiltDto>> GetBySerialAsync(string serial, int? itemId, CancellationToken ct);
        Task<ResponseDto<AsBuiltDto>> ReissueAsync(ReissueAsBuiltDto dto, CancellationToken ct);
        Task<ResponseDto<AsBuiltDto>> DecideReissueAsync(DecideAsBuiltDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Package B (ISO 10007, H10): the as-built record of a released unit. Read-only: the snapshot and its SHA-256 never change; the only write is the re-issue, which makes a new version with a reason and supersedes the old one.
    /// </summary>
    public class AsBuiltService : IAsBuiltService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public AsBuiltService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);

        public const string Reissuers = "QA";
        public const string Approvers = "PMO,CEO";

        public async Task<ResponseDto<PagedList<AsBuiltListDto>>> GetAsync(AsBuiltQuery query, CancellationToken ct)
        {
            query ??= new AsBuiltQuery();
            IQueryable<AsBuiltRecord> rows = _uow.Set<AsBuiltRecord>().AsNoTracking().Where(a => !a.Deleted);
            if (!query.IncludeSuperseded) rows = rows.Where(a => a.Status == AsBuiltStatus.Current);
            if (query.WorkOrderId.HasValue) rows = rows.Where(a => a.WorkOrderId == query.WorkOrderId.Value);
            if (query.ItemId.HasValue) rows = rows.Where(a => a.ItemId == query.ItemId.Value);
            string serial = Clean(query.Serial);
            if (serial != null) rows = rows.Where(a => a.SerialNumber.Contains(serial));
            var projection = from a in rows
                             join w in _uow.Set<WorkOrder>().AsNoTracking() on a.WorkOrderId equals w.Id
                             join n in _uow.Set<ReleaseNote>().AsNoTracking() on a.ReleaseNoteId equals n.Id
                             join i in _uow.Set<InventoryItem>().AsNoTracking() on a.ItemId equals i.Id
                             orderby a.Id descending
                             select new AsBuiltListDto
                             {
                                 Id = a.Id, WorkOrderId = a.WorkOrderId, WorkOrderCode = w.Code, ReleaseNoteId = a.ReleaseNoteId, ReleaseNoteNumber = n.Number, ItemId = a.ItemId, ItemCode = i.Code, ItemTitle = i.Title,
                                 SerialNumber = a.SerialNumber, Version = a.Version, Status = a.Status, Sha256 = a.Sha256, ProductCode = a.ProductCode, BomText = a.BomText, Reason = a.Reason, CreateDate = a.CreateDate,
                             };
            return ResponseDtoHelper.Ok(await PagedList<AsBuiltListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct));
        }

        private async Task<AsBuiltDto> ToDtoAsync(AsBuiltRecord a, CancellationToken ct)
        {
            var wo = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == a.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            var note = await _uow.Set<ReleaseNote>().AsNoTracking().Where(n => n.Id == a.ReleaseNoteId).Select(n => n.Number).FirstOrDefaultAsync(ct);
            var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == a.ItemId).Select(i => new { i.Code, i.Title }).FirstOrDefaultAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { a.CreatorUserId }, ct);
            string recomputed = CharacteristicRules.Sha256Hex(a.SnapshotJson);
            var dto = new AsBuiltDto
            {
                Id = a.Id, WorkOrderId = a.WorkOrderId, WorkOrderCode = wo, ReleaseNoteId = a.ReleaseNoteId, ReleaseNoteNumber = note, ItemId = a.ItemId, ItemCode = item?.Code, ItemTitle = item?.Title,
                SerialNumber = a.SerialNumber, Version = a.Version, Status = a.Status, Sha256 = a.Sha256, ProductCode = a.ProductCode, BomText = a.BomText, Reason = a.Reason, CreateDate = a.CreateDate,
                IssuedByName = names.Name(a.CreatorUserId), SnapshotJson = a.SnapshotJson, Snapshot = AsBuiltBuilder.Parse(a.SnapshotJson), RecomputedSha256 = recomputed, HashOk = string.Equals(recomputed, a.Sha256, StringComparison.Ordinal),
            };
            dto.Versions = await _uow.Set<AsBuiltRecord>().AsNoTracking().Where(r => !r.Deleted && r.WorkOrderId == a.WorkOrderId && r.ItemId == a.ItemId && r.SerialNumber == a.SerialNumber).OrderBy(r => r.Version)
                .Select(r => new AsBuiltListDto { Id = r.Id, WorkOrderId = r.WorkOrderId, ItemId = r.ItemId, SerialNumber = r.SerialNumber, Version = r.Version, Status = r.Status, Sha256 = r.Sha256, Reason = r.Reason, CreateDate = r.CreateDate }).ToListAsync(ct);
            return dto;
        }

        public async Task<ResponseDto<AsBuiltDto>> GetByIdAsync(int id, CancellationToken ct)
        {
            AsBuiltRecord a = await _uow.Set<AsBuiltRecord>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (a == null) return QualityText.NotFound<AsBuiltDto>("رکورد پیکربندی ساخته‌شده پیدا نشد.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }

        public async Task<ResponseDto<AsBuiltDto>> GetBySerialAsync(string serial, int? itemId, CancellationToken ct)
        {
            string s = Clean(serial);
            if (s == null) return QualityText.Bad<AsBuiltDto>("شماره سریال را وارد کنید.");
            IQueryable<AsBuiltRecord> rows = _uow.Set<AsBuiltRecord>().AsNoTracking().Where(a => !a.Deleted && a.Status == AsBuiltStatus.Current && a.SerialNumber == s);
            if (itemId.HasValue) rows = rows.Where(a => a.ItemId == itemId.Value);
            List<AsBuiltRecord> hits = await rows.OrderByDescending(a => a.Id).Take(2).ToListAsync(ct);
            if (hits.Count == 0) return QualityText.NotFound<AsBuiltDto>("برای این سریال پیکربندی ساخته‌شده‌ای صادر نشده است (قطعه هنوز ترخیص نشده).");
            if (hits.Count > 1) return QualityText.Bad<AsBuiltDto>("این سریال برای بیش از یک کالا ثبت شده است؛ کالا را مشخص کنید.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(hits[0], ct));
        }

        public async Task<ResponseDto<AsBuiltDto>> ReissueAsync(ReissueAsBuiltDto dto, CancellationToken ct)
        {
            if (dto == null) return QualityText.Bad<AsBuiltDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Reissuers)) return QualityText.Bad<AsBuiltDto>(QualityText.NotAllowed);
            if (Ai.AiActing.IsActing) return QualityText.Bad<AsBuiltDto>("صدور مجدد پیکربندی ساخته‌شده با انسان است؛ صندلی هوش مصنوعی آن را صادر نمی‌کند.");
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < 5) return QualityText.Bad<AsBuiltDto>("دلیل صدور نسخه جدید را بنویسید.");
            if (reason.Length > 1000) return QualityText.Bad<AsBuiltDto>("دلیل بیش از ۱۰۰۰ نویسه است.");
            AsBuiltRecord a = await _uow.Set<AsBuiltRecord>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return QualityText.NotFound<AsBuiltDto>("رکورد پیکربندی ساخته‌شده پیدا نشد.");
            if (a.Status != AsBuiltStatus.Current) return QualityText.Bad<AsBuiltDto>("فقط نسخه جاری دوباره صادر می‌شود.");
            ReleaseNote note = await _uow.Set<ReleaseNote>().AsNoTracking().FirstAsync(n => n.Id == a.ReleaseNoteId, ct);
            if (note.Status != ReleaseNoteStatus.Released) return QualityText.Bad<AsBuiltDto>("برگه ترخیص این واحد دیگر در وضعیت ترخیص‌شده نیست.");
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstAsync(w => w.Id == a.WorkOrderId, ct);
            if (await _uow.Set<AsBuiltRecord>().AnyAsync(x => !x.Deleted && x.WorkOrderId == a.WorkOrderId && x.ItemId == a.ItemId && x.SerialNumber == a.SerialNumber && x.Status == AsBuiltStatus.Pending, ct))
                return QualityText.Bad<AsBuiltDto>("برای این واحد یک درخواست صدور مجدد در انتظار تایید است.");
            // M6: QA asks (maker); PMO or the CEO decides (checker). The new version stays Pending and the current one stays in force until the approval
            List<AsBuiltRecord> created = await AsBuiltBuilder.IssueAsync(_uow, note, wo, reason, DateTimeOffset.Now, Me, ct, a.ItemId, a.SerialNumber, pending: true);
            if (created.Count == 0) return QualityText.Bad<AsBuiltDto>("این واحد در برگه ترخیص پیدا نشد.");
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return QualityText.Bad<AsBuiltDto>("هم‌زمان نسخه دیگری صادر شد؛ صفحه را تازه کنید."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(created[0], ct));
        }

        public async Task<ResponseDto<AsBuiltDto>> DecideReissueAsync(DecideAsBuiltDto dto, CancellationToken ct)
        {
            if (dto == null) return QualityText.Bad<AsBuiltDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Approvers)) return QualityText.Bad<AsBuiltDto>(QualityText.NotAllowed);
            if (Ai.AiActing.IsActing) return QualityText.Bad<AsBuiltDto>("تایید صدور مجدد پیکربندی ساخته‌شده با انسان است؛ صندلی هوش مصنوعی آن را تصمیم نمی‌گیرد.");
            AsBuiltRecord p = await _uow.Set<AsBuiltRecord>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (p == null) return QualityText.NotFound<AsBuiltDto>("رکورد پیکربندی ساخته‌شده پیدا نشد.");
            if (p.Status != AsBuiltStatus.Pending) return QualityText.Bad<AsBuiltDto>("فقط درخواست در انتظار تایید تصمیم‌گیری می‌شود.");
            if (p.CreatorUserId == Me) return QualityText.Bad<AsBuiltDto>(QualityText.SameUser + " (درخواست‌کننده صدور مجدد نمی‌تواند آن را تایید کند)");
            string note = Clean(dto.Note);
            if (!dto.Approve && note == null) return QualityText.Bad<AsBuiltDto>("دلیل رد را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Approve)
            {
                AsBuiltRecord current = await _uow.Set<AsBuiltRecord>().FirstOrDefaultAsync(x => !x.Deleted && x.WorkOrderId == p.WorkOrderId && x.ItemId == p.ItemId && x.SerialNumber == p.SerialNumber && x.Status == AsBuiltStatus.Current, ct);
                if (current != null) { current.Status = AsBuiltStatus.Superseded; current.SupersededByVersion = p.Version; current.ModifyDate = now; current.ModifierUserId = Me; }
                p.Status = AsBuiltStatus.Current;
            }
            else { p.Status = AsBuiltStatus.Rejected; p.Reason = ((p.Reason ?? string.Empty) + " | رد شد: " + note).Trim(' ', '|'); if (p.Reason.Length > 1000) p.Reason = p.Reason.Substring(0, 1000); }
            p.ModifyDate = now; p.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return QualityText.Bad<AsBuiltDto>("هم‌زمان نسخه دیگری جاری شد؛ صفحه را تازه کنید."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(p, ct));
        }
    }
}
