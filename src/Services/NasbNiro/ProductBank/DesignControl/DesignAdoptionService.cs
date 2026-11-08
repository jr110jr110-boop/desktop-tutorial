using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IDesignAdoptionService
    {
        Task<ResponseDto<List<DesignAdoptionDto>>> GetListAsync(DesignAdoptionQuery query, CancellationToken ct);
        Task<ResponseDto<DesignAdoptionDto>> RequestAsync(SaveDesignAdoptionDto dto, CancellationToken ct);
        Task<ResponseDto<DesignAdoptionDto>> ActionAsync(DesignAdoptionActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Fix round 1 (H6), the transition to the design control: a BOM revision released before the control started is "legacy, not design-verified" (shown, never silently passed). Engineering asks to adopt the existing design with a reason
    /// and an evidence reference, QA, a second person, accepts: the accepted record stands in for the verification and validation of that exact BOM content. It is a record, never a deletion; it is voided with a reason.
    /// </summary>
    public class DesignAdoptionService : PbServiceBase, IDesignAdoptionService
    {
        public DesignAdoptionService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user) { }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;

        private static string StatusText(DesignAdoptionStatus? s) => s == null ? "پذیرش ثبت نشده" : s == DesignAdoptionStatus.Requested ? "در انتظار پذیرش QA" : s == DesignAdoptionStatus.Accepted ? "پذیرفته شده" : "باطل";

        private DesignAdoptionDto ToDto(PbBomHeader h, string productCode, PbDesignAdoption a, DateTime start, Dictionary<int, string> names)
        {
            bool human = !IsAi;
            var d = new DesignAdoptionDto
            {
                BomHeaderId = h.Id, BomCode = h.Code, BomRevision = h.Revision, ProductId = h.ProductId, ProductCode = productCode, ReleasedAt = h.ReleasedAt, Legacy = DesignGate.IsLegacy(h, start),
                Status = a?.Status, StatusText = StatusText(a?.Status),
            };
            d.CanRequest = human && d.Legacy && InRole("ENGINEERING") && (a == null || a.Status == DesignAdoptionStatus.Void);
            if (a != null)
            {
                d.AdoptionId = a.Id; d.Reason = a.Reason; d.EvidenceRef = a.EvidenceRef; d.RequestedByUserId = a.RequestedByUserId; d.RequestedByName = names.Name(a.RequestedByUserId); d.RequestedAt = a.RequestedAt;
                d.AcceptedByUserId = a.AcceptedByUserId; d.AcceptedByName = names.Name(a.AcceptedByUserId); d.AcceptedAt = a.AcceptedAt; d.AcceptNote = a.AcceptNote; d.VoidReason = a.VoidReason; d.RowVersion = DesignQueries.B64(a.RowVersion);
                d.CanAccept = human && a.Status == DesignAdoptionStatus.Requested && InRole(DesignRoles.QaOnly) && a.RequestedByUserId != Me;
                d.CanVoid = human && (a.Status == DesignAdoptionStatus.Requested ? (a.RequestedByUserId == Me || InRole(DesignRoles.QaOnly)) : a.Status == DesignAdoptionStatus.Accepted && InRole(DesignRoles.QaOnly));
            }
            return d;
        }

        public async Task<ResponseDto<List<DesignAdoptionDto>>> GetListAsync(DesignAdoptionQuery query, CancellationToken ct)
        {
            query ??= new DesignAdoptionQuery();
            DateTime start = await DesignGate.ControlStartAsync(Uow, ct);
            IQueryable<PbBomHeader> q = Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ReleasedAt != null);
            if (query.ProductId.HasValue) q = q.Where(h => h.ProductId == query.ProductId.Value);
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(h => h.Code.Contains(text) || h.Title.Contains(text));
            List<PbBomHeader> headers = (await q.OrderByDescending(h => h.Id).Take(2000).ToListAsync(ct)).Where(h => !query.OnlyLegacy || DesignGate.IsLegacy(h, start)).Take(300).ToList();
            List<int> ids = headers.Select(h => h.Id).ToList();
            List<PbDesignAdoption> adoptions = await Uow.Set<PbDesignAdoption>().AsNoTracking().Where(a => !a.Deleted && ids.Contains(a.BomHeaderId)).OrderBy(a => a.Id).ToListAsync(ct);
            List<int> productIds = headers.Where(h => h.ProductId.HasValue).Select(h => h.ProductId.Value).Distinct().ToList();
            Dictionary<int, string> codes = await Uow.Set<PbProduct>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            var userIds = new List<int?>();
            foreach (PbDesignAdoption a in adoptions) { userIds.Add(a.RequestedByUserId); userIds.Add(a.AcceptedByUserId); }
            Dictionary<int, string> names = await DesignQueries.NamesAsync(Uow, userIds, ct);
            var list = new List<DesignAdoptionDto>();
            foreach (PbBomHeader h in headers)
            {
                PbDesignAdoption a = adoptions.Where(x => x.BomHeaderId == h.Id).OrderByDescending(x => x.Id).FirstOrDefault();
                list.Add(ToDto(h, h.ProductId.HasValue && codes.TryGetValue(h.ProductId.Value, out string c) ? c : null, a, start, names));
            }
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<ResponseDto<DesignAdoptionDto>> OneAsync(int bomId, CancellationToken ct)
        {
            ResponseDto<List<DesignAdoptionDto>> all = await GetListAsync(new DesignAdoptionQuery { OnlyLegacy = false }, ct);
            DesignAdoptionDto one = all.Data?.FirstOrDefault(x => x.BomHeaderId == bomId);
            return one == null ? ResponseDtoHelper.NotFound<DesignAdoptionDto>(PbMessages.NotFound) : ResponseDtoHelper.Ok(one);
        }

        public async Task<ResponseDto<DesignAdoptionDto>> RequestAsync(SaveDesignAdoptionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignAdoptionDto>(DesignRules.AiNever);
            if (!InRole("ENGINEERING")) return Bad<DesignAdoptionDto>(DesignRules.NotAllowed);
            if (dto == null) return Bad<DesignAdoptionDto>(PbMessages.InvalidData);
            PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.BomHeaderId && !x.Deleted, ct);
            if (h == null) return ResponseDtoHelper.NotFound<DesignAdoptionDto>(PbMessages.NotFound);
            DateTime start = await DesignGate.ControlStartAsync(Uow, ct);
            if (h.Status != PbBomStatus.Released && h.Status != PbBomStatus.Superseded) return Bad<DesignAdoptionDto>("پذیرش طراحی موجود برای BOM منتشرشده است.");
            if (!DesignGate.IsLegacy(h, start)) return Bad<DesignAdoptionDto>($"این BOM پس از آغاز کنترل طراحی ({DesignGate.ShamsiText(start)}) منتشر شده و تایید کامل طراحی می‌خواهد؛ پذیرش طراحی موجود فقط برای BOMهای قدیمی است.");
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            string reason = PbText.Clean(dto.Reason), evidence = PbText.Clean(dto.EvidenceRef);
            if (reason == null || reason.Length < reasonMin) return Bad<DesignAdoptionDto>($"دلیل پذیرش طراحی موجود دست‌کم {reasonMin} نویسه باشد.");
            if (evidence == null || evidence.Length < 5) return Bad<DesignAdoptionDto>("مرجع مدرک (گزارش آزمون، سابقه بهره‌برداری، تایید مشتری ...) را بنویسید.");
            if (DesignLimits.Over(reason, DesignLimits.Reason, "دلیل") is string l1) return Bad<DesignAdoptionDto>(l1);
            if (DesignLimits.Over(evidence, DesignLimits.AdoptionEvidence, "مرجع مدرک") is string l2) return Bad<DesignAdoptionDto>(l2);
            if (await Uow.Set<PbDesignAdoption>().AnyAsync(a => !a.Deleted && a.BomHeaderId == h.Id && (a.Status == DesignAdoptionStatus.Requested || a.Status == DesignAdoptionStatus.Accepted), ct))
                return Bad<DesignAdoptionDto>("برای این BOM پذیرش در جریان یا پذیرفته‌شده هست.");
            var a2 = new PbDesignAdoption { BomHeaderId = h.Id, ProductId = h.ProductId, ProductRevision = h.Revision, Status = DesignAdoptionStatus.Requested, Reason = reason, EvidenceRef = evidence, RequestedByUserId = Me, RequestedAt = DateTimeOffset.Now, ContentHash = h.ContentHash };
            Stamp(a2, true);
            Uow.Set<PbDesignAdoption>().Add(a2);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignAdoptionDto>(conflict);
            return await OneAsync(h.Id, ct);
        }

        public async Task<ResponseDto<DesignAdoptionDto>> ActionAsync(DesignAdoptionActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignAdoptionDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignAdoptionDto>(PbMessages.InvalidData);
            PbDesignAdoption a = await Uow.Set<PbDesignAdoption>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return ResponseDtoHelper.NotFound<DesignAdoptionDto>(PbMessages.NotFound);
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { Uow.Entry(a).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<DesignAdoptionDto>(PbMessages.InvalidData); }
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            string note = PbText.Clean(dto.Note);
            if (DesignLimits.Over(note, DesignLimits.Note, "شرح") is string longNote) return Bad<DesignAdoptionDto>(longNote);
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "accept":
                    if (a.Status != DesignAdoptionStatus.Requested) return Bad<DesignAdoptionDto>("فقط پذیرش درخواست‌شده تایید می‌شود.");
                    if (!InRole(DesignRoles.QaOnly)) return Bad<DesignAdoptionDto>(DesignRules.NotAllowed);
                    if (a.RequestedByUserId == Me) return Bad<DesignAdoptionDto>("تفکیک وظایف: پذیرش را QA، غیر از درخواست‌کننده (مهندسی)، تایید می‌کند.");
                    PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstAsync(x => x.Id == a.BomHeaderId, ct);
                    if (!string.Equals(h.ContentHash ?? string.Empty, a.ContentHash ?? string.Empty, StringComparison.Ordinal)) return Bad<DesignAdoptionDto>("محتوای BOM پس از درخواست تغییر کرده است.");
                    a.Status = DesignAdoptionStatus.Accepted; a.AcceptedByUserId = Me; a.AcceptedAt = DateTimeOffset.Now; a.AcceptNote = note;
                    break;
                case "void":
                    if (a.Status == DesignAdoptionStatus.Void) return Bad<DesignAdoptionDto>("پذیرش پیش‌تر باطل شده است.");
                    if (a.Status == DesignAdoptionStatus.Requested ? !(a.RequestedByUserId == Me || InRole(DesignRoles.QaOnly)) : !InRole(DesignRoles.QaOnly)) return Bad<DesignAdoptionDto>(DesignRules.NotAllowed);
                    if (note == null || note.Length < reasonMin) return Bad<DesignAdoptionDto>($"دلیل ابطال دست‌کم {reasonMin} نویسه باشد.");
                    a.Status = DesignAdoptionStatus.Void; a.VoidReason = note;
                    break;
                default: return Bad<DesignAdoptionDto>("کار نامعتبر است.");
            }
            Stamp(a, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignAdoptionDto>(conflict);
            return await OneAsync(a.BomHeaderId, ct);
        }
    }
}
