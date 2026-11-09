using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.GoLive
{
    public interface IGlCharAdoptionService
    {
        Task<ResponseDto<List<GlCharAdoptionCandidateDto>>> CandidatesAsync(CancellationToken ct);
        Task<ResponseDto<List<GlCharAdoptionDto>>> GetListAsync(CancellationToken ct);
        Task<ResponseDto<GlCharAdoptionDto>> RequestAsync(GlSaveCharAdoptionDto dto, CancellationToken ct);
        Task<ResponseDto<GlCharAdoptionDto>> ActionAsync(GlAdoptionActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// «پذیرش مشخصه‌های نقشه موجود»: engineering writes the characteristic list of a drawing revision released before the cut-over (no ECO is needed for the first list of a revision), QA, another person, approves it.
    /// The list is made and approved by the ordinary list service (so its own maker-checker rules hold too); this record keeps the reason, the evidence and the two persons. Never a deletion: voided with a reason.
    /// </summary>
    public class GlCharAdoptionService : PbServiceBase, IGlCharAdoptionService
    {
        private readonly ICharacteristicService _lists;
        public GlCharAdoptionService(IUnitOfWork uow, IInventoryUserContext user, ICharacteristicService lists) : base(uow, user) { _lists = lists; }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;
        private static bool InForce(PcDocStatus s) => s == PcDocStatus.Released || s == PcDocStatus.AsBuilt;
        private static string StatusText(GlAdoptionStatus s) => s == GlAdoptionStatus.Requested ? "در انتظار تایید QA" : s == GlAdoptionStatus.Accepted ? "پذیرفته شده" : s == GlAdoptionStatus.Void ? "باطل" : "در جریان";

        public async Task<ResponseDto<List<GlCharAdoptionCandidateDto>>> CandidatesAsync(CancellationToken ct)
        {
            DateTimeOffset start = await CutOver.StartAsync(Uow, ct);
            List<PcDocumentRevision> revs = await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.AsBuilt)).OrderByDescending(r => r.Id).Take(500).ToListAsync(ct);
            revs = revs.Where(r => start > DateTimeOffset.MinValue && (!r.ReleasedAt.HasValue || r.ReleasedAt.Value < start)).Take(300).ToList();
            List<int> revIds = revs.Select(r => r.Id).ToList(), docIds = revs.Select(r => r.DeliverableId).Distinct().ToList();
            Dictionary<int, PcEngineeringDeliverable> docs = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
            HashSet<int> withList = (await Uow.Set<QcCharacteristicList>().AsNoTracking().Where(l => !l.Deleted && revIds.Contains(l.RevisionId) && l.Status == CharListStatus.Released).Select(l => l.RevisionId).ToListAsync(ct)).ToHashSet();
            Dictionary<int, int> adoptions = (await Uow.Set<GlCharAdoption>().AsNoTracking().Where(a => !a.Deleted && a.Status != GlAdoptionStatus.Void && revIds.Contains(a.RevisionId)).Select(a => new { a.RevisionId, a.Id }).ToListAsync(ct)).GroupBy(x => x.RevisionId).ToDictionary(g => g.Key, g => g.First().Id);
            var list = revs.Select(r => new GlCharAdoptionCandidateDto
            {
                DeliverableId = r.DeliverableId, RevisionId = r.Id, DocumentNumber = docs.TryGetValue(r.DeliverableId, out var d) ? d.DocumentNumber : null, Title = d?.Title, RevisionCode = r.Code, ReleasedAt = r.ReleasedAt,
                Legacy = true, HasList = withList.Contains(r.Id), AdoptionId = adoptions.TryGetValue(r.Id, out int aid) ? aid : (int?)null,
            }).Where(c => !c.HasList).ToList();
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<GlCharAdoptionDto> ToDtoAsync(GlCharAdoption a, CancellationToken ct)
        {
            PcDocumentRevision rev = await Uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == a.RevisionId, ct);
            PcEngineeringDeliverable doc = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == a.DeliverableId, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, new int?[] { a.RequestedByUserId, a.AcceptedByUserId }, ct);
            bool human = !IsAi;
            return new GlCharAdoptionDto
            {
                Id = a.Id, DeliverableId = a.DeliverableId, RevisionId = a.RevisionId, DocumentNumber = doc?.DocumentNumber, RevisionCode = rev?.Code, ReleasedAt = rev?.ReleasedAt, ListId = a.ListId, Status = (int)a.Status, StatusText = StatusText(a.Status),
                Reason = a.Reason, EvidenceRef = a.EvidenceRef, RequestedByUserId = a.RequestedByUserId, RequestedByName = names.Name(a.RequestedByUserId), RequestedAt = a.RequestedAt,
                AcceptedByName = names.Name(a.AcceptedByUserId), AcceptedAt = a.AcceptedAt, AcceptNote = a.AcceptNote, VoidReason = a.VoidReason,
                CanAccept = human && a.Status == GlAdoptionStatus.Requested && InRole("QA,QC") && a.RequestedByUserId != Me,
                CanVoid = human && a.Status == GlAdoptionStatus.Requested && (a.RequestedByUserId == Me || InRole("QA,QC")),
                RowVersion = a.RowVersion == null ? null : Convert.ToBase64String(a.RowVersion),
            };
        }

        public async Task<ResponseDto<List<GlCharAdoptionDto>>> GetListAsync(CancellationToken ct)
        {
            List<GlCharAdoption> rows = await Uow.Set<GlCharAdoption>().AsNoTracking().Where(a => !a.Deleted).OrderByDescending(a => a.Id).Take(300).ToListAsync(ct);
            var list = new List<GlCharAdoptionDto>();
            foreach (GlCharAdoption a in rows) list.Add(await ToDtoAsync(a, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<GlCharAdoptionDto>> RequestAsync(GlSaveCharAdoptionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<GlCharAdoptionDto>(GlText.AiNever);
            if (!InRole("ENGINEERING,ENGINEER")) return Bad<GlCharAdoptionDto>(GlText.NotAllowed);
            if (dto == null || dto.RevisionId <= 0) return Bad<GlCharAdoptionDto>(GlText.InvalidData);
            PcDocumentRevision rev = await Uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RevisionId && !r.Deleted, ct);
            if (rev == null) return ResponseDtoHelper.NotFound<GlCharAdoptionDto>("بازنگری نقشه پیدا نشد.");
            if (!InForce(rev.Status)) return Bad<GlCharAdoptionDto>("پذیرش مشخصه‌های نقشه موجود برای بازنگری منتشرشده (در اجرا) است.");
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(Uow, ct);
            DateTimeOffset start = await CutOver.StartAsync(Uow, st, ct);
            if (start <= DateTimeOffset.MinValue || (rev.ReleasedAt.HasValue && rev.ReleasedAt.Value >= start))
                return Bad<GlCharAdoptionDto>($"این بازنگری پس از آغاز کنترل ({CutOver.Text(start)}) منتشر شده؛ فهرست مشخصه آن از مسیر عادی (با تغییر مهندسی) ثبت می‌شود.");
            int min = (int)st[GlSettingKeys.LegacyReasonMinChars];
            string reason = PbText.Clean(dto.Reason), evidence = PbText.Clean(dto.EvidenceRef);
            if (reason == null || reason.Length < min) return Bad<GlCharAdoptionDto>($"دلیل پذیرش دست‌کم {min} نویسه باشد.");
            if (evidence == null || evidence.Length < 5) return Bad<GlCharAdoptionDto>("مرجع مدرک (برگه بازرسی، گزارش اندازه‌گیری، سابقه ساخت) را بنویسید.");
            if (GlText.Over(reason, 500, "دلیل") is string l1) return Bad<GlCharAdoptionDto>(l1);
            if (GlText.Over(evidence, 300, "مرجع مدرک") is string l2) return Bad<GlCharAdoptionDto>(l2);
            if (await Uow.Set<GlCharAdoption>().AnyAsync(a => !a.Deleted && a.RevisionId == rev.Id && (a.Status == GlAdoptionStatus.Requested || a.Status == GlAdoptionStatus.Accepted), ct))
                return Bad<GlCharAdoptionDto>("برای این بازنگری پذیرش در جریان یا پذیرفته‌شده هست.");
            var items = dto.Items ?? new List<SaveCharLineDto>();
            var save = await _lists.SaveAsync(new SaveCharListDto { RevisionId = rev.Id, Reason = items.Count == 0 ? (PbText.Clean(dto.EmptyListReason) ?? reason) : reason, Items = items }, ct);
            if (!save.Success) return Bad<GlCharAdoptionDto>(save.Message);
            var submit = await _lists.ActionAsync(new CharListActionDto { Id = save.Data.Id, Action = "submit", RowVersion = save.Data.RowVersion }, ct);
            if (!submit.Success)
            {
                await _lists.ActionAsync(new CharListActionDto { Id = save.Data.Id, Action = "void", Note = "ارسال ناموفق در پذیرش مشخصه‌های نقشه موجود", RowVersion = save.Data.RowVersion }, ct);
                return Bad<GlCharAdoptionDto>(submit.Message);
            }
            var a2 = new GlCharAdoption { DeliverableId = rev.DeliverableId, RevisionId = rev.Id, ListId = save.Data.Id, Status = GlAdoptionStatus.Requested, Reason = reason, EvidenceRef = evidence, RequestedByUserId = Me, RequestedAt = DateTimeOffset.Now };
            Stamp(a2, true);
            Uow.Set<GlCharAdoption>().Add(a2);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<GlCharAdoptionDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a2, ct));
        }

        public async Task<ResponseDto<GlCharAdoptionDto>> ActionAsync(GlAdoptionActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<GlCharAdoptionDto>(GlText.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<GlCharAdoptionDto>(GlText.InvalidData);
            GlCharAdoption a = await Uow.Set<GlCharAdoption>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return ResponseDtoHelper.NotFound<GlCharAdoptionDto>(PbMessages.NotFound);
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { Uow.Entry(a).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<GlCharAdoptionDto>(GlText.InvalidData); }
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(Uow, ct);
            int min = (int)st[GlSettingKeys.LegacyReasonMinChars];
            string note = PbText.Clean(dto.Note);
            if (GlText.Over(note, 500, "شرح") is string ln) return Bad<GlCharAdoptionDto>(ln);
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "accept":
                    if (a.Status != GlAdoptionStatus.Requested) return Bad<GlCharAdoptionDto>("فقط پذیرش درخواست‌شده تایید می‌شود.");
                    if (!InRole("QA,QC")) return Bad<GlCharAdoptionDto>(GlText.NotAllowed);
                    if (a.RequestedByUserId == Me) return Bad<GlCharAdoptionDto>(GlText.SameUser);
                    if (!a.ListId.HasValue) return Bad<GlCharAdoptionDto>(GlText.InvalidData);
                    var ap = await _lists.ActionAsync(new CharListActionDto { Id = a.ListId.Value, Action = "approve", Note = note }, ct);
                    if (!ap.Success) return Bad<GlCharAdoptionDto>(ap.Message);
                    a.Status = GlAdoptionStatus.Accepted; a.AcceptedByUserId = Me; a.AcceptedAt = DateTimeOffset.Now; a.AcceptNote = note;
                    break;
                case "void":
                    if (a.Status != GlAdoptionStatus.Requested) return Bad<GlCharAdoptionDto>("فقط پذیرش در انتظار تایید باطل می‌شود؛ فهرست تاییدشده با نسخه جدید (تغییر مهندسی) اصلاح می‌شود.");
                    if (!(a.RequestedByUserId == Me || InRole("QA,QC"))) return Bad<GlCharAdoptionDto>(GlText.NotAllowed);
                    if (note == null || note.Length < min) return Bad<GlCharAdoptionDto>($"دلیل ابطال دست‌کم {min} نویسه باشد.");
                    if (a.ListId.HasValue)
                    {
                        var vr = await _lists.ActionAsync(new CharListActionDto { Id = a.ListId.Value, Action = "void", Note = note }, ct);
                        if (!vr.Success) return Bad<GlCharAdoptionDto>(vr.Message);
                    }
                    a.Status = GlAdoptionStatus.Void; a.VoidReason = note;
                    break;
                default: return Bad<GlCharAdoptionDto>("کار نامعتبر است.");
            }
            Stamp(a, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<GlCharAdoptionDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }
    }

    public interface IGlConfigAdoptionService
    {
        Task<ResponseDto<List<GlConfigCandidateDto>>> CandidatesAsync(CancellationToken ct);
        Task<ResponseDto<List<GlConfigAdoptionDto>>> GetListAsync(CancellationToken ct);
        Task<ResponseDto<GlConfigAdoptionDto>> RequestAsync(GlSaveConfigAdoptionDto dto, CancellationToken ct);
        Task<ResponseDto<GlConfigAdoptionDto>> ActionAsync(GlAdoptionActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// «پذیرش پیکربندی پروژه موجود»: a running project (work order created before the cut-over) with no product-bank baseline gets one, built from a released product-bank BOM that stands for its legacy BOM. Three distinct persons:
    /// the preparer (engineering, with the comparison against the legacy BOM), one more engineering person and one PMO person. The baseline is created by the last review. Voided with a reason; never deleted.
    /// </summary>
    public class GlConfigAdoptionService : PbServiceBase, IGlConfigAdoptionService
    {
        private readonly IPbProductService _products;
        public GlConfigAdoptionService(IUnitOfWork uow, IInventoryUserContext user, IPbProductService products) : base(uow, user) { _products = products; }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;
        private static string StatusText(GlAdoptionStatus s) => s == GlAdoptionStatus.Requested ? "در انتظار بازبینی مهندسی و PMO" : s == GlAdoptionStatus.PartlyReviewed ? "یک بازبینی انجام شده" : s == GlAdoptionStatus.Accepted ? "پذیرفته شده (پایه‌ریزی انجام شد)" : "باطل";

        private Task<int> LegacyLinesAsync(int workOrderId, CancellationToken ct) => Uow.Set<Bom>().AsNoTracking().CountAsync(b => !b.Deleted && b.Assembly.WorkOrderId == workOrderId, ct);

        public async Task<ResponseDto<List<GlConfigCandidateDto>>> CandidatesAsync(CancellationToken ct)
        {
            DateTimeOffset start = await CutOver.StartAsync(Uow, ct);
            if (start <= DateTimeOffset.MinValue) return ResponseDtoHelper.Ok(new List<GlConfigCandidateDto>());
            HashSet<int> withBaseline = (await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => b.WorkOrderId).ToListAsync(ct)).ToHashSet();
            List<WorkOrder> wos = await Uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && w.CreateDate < start).OrderByDescending(w => w.Id).Take(500).ToListAsync(ct);
            Dictionary<int, int> adoptions = (await Uow.Set<GlConfigAdoption>().AsNoTracking().Where(a => !a.Deleted && a.Status != GlAdoptionStatus.Void).Select(a => new { a.WorkOrderId, a.Id }).ToListAsync(ct)).GroupBy(x => x.WorkOrderId).ToDictionary(g => g.Key, g => g.First().Id);
            var list = new List<GlConfigCandidateDto>();
            foreach (WorkOrder w in wos.Where(w => !withBaseline.Contains(w.Id)).Take(200))
                list.Add(new GlConfigCandidateDto { WorkOrderId = w.Id, Title = w.Title, CreatedAt = w.CreateDate, Legacy = true, LegacyLineCount = await LegacyLinesAsync(w.Id, ct), AdoptionId = adoptions.TryGetValue(w.Id, out int id) ? id : (int?)null });
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<GlConfigAdoptionDto> ToDtoAsync(GlConfigAdoption a, CancellationToken ct)
        {
            PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.BomHeaderId, ct);
            string productCode = await Uow.Set<PbProduct>().AsNoTracking().Where(p => p.Id == a.ProductId).Select(p => p.Code).FirstOrDefaultAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, new int?[] { a.RequestedByUserId, a.EngReviewerUserId, a.PmoReviewerUserId }, ct);
            bool human = !IsAi, open = a.Status == GlAdoptionStatus.Requested || a.Status == GlAdoptionStatus.PartlyReviewed;
            bool Distinct(int u) => u != a.RequestedByUserId && u != (a.EngReviewerUserId ?? -1) && u != (a.PmoReviewerUserId ?? -1);
            return new GlConfigAdoptionDto
            {
                Id = a.Id, WorkOrderId = a.WorkOrderId, ProductId = a.ProductId, ProductCode = productCode, BomHeaderId = a.BomHeaderId, BomCode = h?.Code, BomRevision = h?.Revision ?? 0, BaselineId = a.BaselineId,
                Status = (int)a.Status, StatusText = StatusText(a.Status), Reason = a.Reason, EvidenceRef = a.EvidenceRef, ComparisonNote = a.ComparisonNote, LegacyLineCount = a.LegacyLineCount, BankLineCount = a.BankLineCount,
                RequestedByName = names.Name(a.RequestedByUserId), RequestedAt = a.RequestedAt, EngReviewerName = names.Name(a.EngReviewerUserId), EngReviewedAt = a.EngReviewedAt, EngNote = a.EngNote,
                PmoReviewerName = names.Name(a.PmoReviewerUserId), PmoReviewedAt = a.PmoReviewedAt, PmoNote = a.PmoNote, VoidReason = a.VoidReason,
                CanEngReview = human && open && a.EngReviewerUserId == null && InRole("ENGINEERING") && Distinct(Me),
                CanPmoReview = human && open && a.PmoReviewerUserId == null && InRole("PMO") && Distinct(Me),
                CanVoid = human && open && (a.RequestedByUserId == Me || InRole("PMO")),
                RowVersion = a.RowVersion == null ? null : Convert.ToBase64String(a.RowVersion),
            };
        }

        public async Task<ResponseDto<List<GlConfigAdoptionDto>>> GetListAsync(CancellationToken ct)
        {
            List<GlConfigAdoption> rows = await Uow.Set<GlConfigAdoption>().AsNoTracking().Where(a => !a.Deleted).OrderByDescending(a => a.Id).Take(300).ToListAsync(ct);
            var list = new List<GlConfigAdoptionDto>();
            foreach (GlConfigAdoption a in rows) list.Add(await ToDtoAsync(a, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<GlConfigAdoptionDto>> RequestAsync(GlSaveConfigAdoptionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<GlConfigAdoptionDto>(GlText.AiNever);
            if (!InRole("ENGINEERING")) return Bad<GlConfigAdoptionDto>(GlText.NotAllowed);
            if (dto == null || dto.WorkOrderId <= 0 || dto.ProductId <= 0 || dto.BomHeaderId <= 0) return Bad<GlConfigAdoptionDto>(GlText.InvalidData);
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(Uow, ct);
            DateTimeOffset start = await CutOver.StartAsync(Uow, st, ct);
            WorkOrder wo = await Uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, ct);
            if (wo == null) return ResponseDtoHelper.NotFound<GlConfigAdoptionDto>("دستور کار پیدا نشد.");
            if (!CutOver.IsLegacy(wo.CreateDate, start)) return Bad<GlConfigAdoptionDto>($"این دستور کار پس از آغاز کنترل ({CutOver.Text(start)}) ساخته شده و پایه‌ریزی از بانک محصول می‌خواهد؛ پذیرش پیکربندی موجود فقط برای دستور کارهای قدیمی است.");
            if (await Uow.Set<PbProjectBaseline>().AnyAsync(b => b.WorkOrderId == wo.Id && !b.Deleted && b.IsCurrent, ct)) return Bad<GlConfigAdoptionDto>("این دستور کار پایه‌ریزی (baseline) دارد.");
            PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.BomHeaderId && !x.Deleted, ct);
            if (h == null || h.ProductId != dto.ProductId || h.Status != PbBomStatus.Released) return Bad<GlConfigAdoptionDto>("BOM منتشرشده (Released) این محصول پیدا نشد.");
            int min = (int)st[GlSettingKeys.LegacyReasonMinChars];
            string reason = PbText.Clean(dto.Reason), evidence = PbText.Clean(dto.EvidenceRef), cmp = PbText.Clean(dto.ComparisonNote);
            if (reason == null || reason.Length < min) return Bad<GlConfigAdoptionDto>($"دلیل پذیرش دست‌کم {min} نویسه باشد.");
            if (evidence == null || evidence.Length < 5) return Bad<GlConfigAdoptionDto>("مرجع مدرک را بنویسید.");
            if (cmp == null || cmp.Length < min) return Bad<GlConfigAdoptionDto>($"شرح مقایسه BOM قدیمی با BOM بانک محصول دست‌کم {min} نویسه باشد.");
            if (GlText.Over(reason, 500, "دلیل") is string l1) return Bad<GlConfigAdoptionDto>(l1);
            if (GlText.Over(evidence, 300, "مرجع مدرک") is string l2) return Bad<GlConfigAdoptionDto>(l2);
            if (GlText.Over(cmp, 1000, "شرح مقایسه") is string l3) return Bad<GlConfigAdoptionDto>(l3);
            if (await Uow.Set<GlConfigAdoption>().AnyAsync(x => !x.Deleted && x.WorkOrderId == wo.Id && x.Status != GlAdoptionStatus.Void, ct)) return Bad<GlConfigAdoptionDto>("برای این دستور کار پذیرش در جریان یا پذیرفته‌شده هست.");
            int bankLines = await Uow.Set<PbBomLine>().AsNoTracking().CountAsync(l => !l.Deleted && l.BomHeaderId == h.Id && !l.IsHeading, ct);
            var a2 = new GlConfigAdoption
            {
                WorkOrderId = wo.Id, ProductId = dto.ProductId, BomHeaderId = h.Id, Status = GlAdoptionStatus.Requested, Reason = reason, EvidenceRef = evidence, ComparisonNote = cmp,
                LegacyLineCount = await LegacyLinesAsync(wo.Id, ct), BankLineCount = bankLines, RequestedByUserId = Me, RequestedAt = DateTimeOffset.Now,
            };
            Stamp(a2, true);
            Uow.Set<GlConfigAdoption>().Add(a2);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<GlConfigAdoptionDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a2, ct));
        }

        public async Task<ResponseDto<GlConfigAdoptionDto>> ActionAsync(GlAdoptionActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<GlConfigAdoptionDto>(GlText.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<GlConfigAdoptionDto>(GlText.InvalidData);
            GlConfigAdoption a = await Uow.Set<GlConfigAdoption>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return ResponseDtoHelper.NotFound<GlConfigAdoptionDto>(PbMessages.NotFound);
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { Uow.Entry(a).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<GlConfigAdoptionDto>(GlText.InvalidData); }
            int min = (int)(await QualitySettingKeys.LoadAsync(Uow, ct))[GlSettingKeys.LegacyReasonMinChars];
            string note = PbText.Clean(dto.Note);
            if (GlText.Over(note, 500, "شرح") is string ln) return Bad<GlConfigAdoptionDto>(ln);
            bool open = a.Status == GlAdoptionStatus.Requested || a.Status == GlAdoptionStatus.PartlyReviewed;
            string action = dto.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "eng-review":
                case "pmo-review":
                {
                    if (!open) return Bad<GlConfigAdoptionDto>("این پذیرش بازبینی نمی‌شود.");
                    bool eng = action == "eng-review";
                    if (!InRole(eng ? "ENGINEERING" : "PMO")) return Bad<GlConfigAdoptionDto>(GlText.NotAllowed);
                    if (a.RequestedByUserId == Me || a.EngReviewerUserId == Me || a.PmoReviewerUserId == Me) return Bad<GlConfigAdoptionDto>("تفکیک وظایف: هر سه نفر (تهیه‌کننده، بازبین مهندسی و بازبین PMO) باید متفاوت باشند.");
                    if (eng ? a.EngReviewerUserId != null : a.PmoReviewerUserId != null) return Bad<GlConfigAdoptionDto>("این بازبینی پیش‌تر انجام شده است.");
                    if (eng) { a.EngReviewerUserId = Me; a.EngReviewedAt = DateTimeOffset.Now; a.EngNote = note; } else { a.PmoReviewerUserId = Me; a.PmoReviewedAt = DateTimeOffset.Now; a.PmoNote = note; }
                    if (a.EngReviewerUserId != null && a.PmoReviewerUserId != null)
                    {
                        var r = await _products.InstantiateAdoptedAsync(new PbInstantiateDto { WorkOrderId = a.WorkOrderId, ProductId = a.ProductId }, a.BomHeaderId, ct);
                        if (!r.Success) return Bad<GlConfigAdoptionDto>(r.Message);
                        a.BaselineId = r.Data?.Id; a.Status = GlAdoptionStatus.Accepted;
                    }
                    else a.Status = GlAdoptionStatus.PartlyReviewed;
                    break;
                }
                case "void":
                    if (!open) return Bad<GlConfigAdoptionDto>("فقط پذیرش در جریان باطل می‌شود.");
                    if (!(a.RequestedByUserId == Me || InRole("PMO"))) return Bad<GlConfigAdoptionDto>(GlText.NotAllowed);
                    if (note == null || note.Length < min) return Bad<GlConfigAdoptionDto>($"دلیل ابطال دست‌کم {min} نویسه باشد.");
                    a.Status = GlAdoptionStatus.Void; a.VoidReason = note;
                    break;
                default: return Bad<GlConfigAdoptionDto>("کار نامعتبر است.");
            }
            Stamp(a, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<GlConfigAdoptionDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }
    }
}
