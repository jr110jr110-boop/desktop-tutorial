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
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface INoDrawingService
    {
        Task<ResponseDto<PagedList<NoDrawingFlagDto>>> GetAsync(NoDrawingQuery query, CancellationToken ct);
        Task<ResponseDto<NoDrawingFlagDto>> RequestAsync(RequestNoDrawingDto dto, CancellationToken ct);
        Task<ResponseDto<NoDrawingFlagDto>> ActionAsync(NoDrawingActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Package B (H6): a make part reaches the shop only with a released drawing, unless engineering flagged it «بدون نقشه». The flag is requested by engineering with a reason, activated by a second person
    /// (never an AI seat), audited, and voided with a reason (never deleted).
    /// </summary>
    public class NoDrawingService : INoDrawingService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public NoDrawingService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        public const string Requesters = "ENGINEERING,ENGINEER";
        public const string Deciders = "PMO,QA,ENGINEERING,ENGINEER";
        public const string Voiders = "PMO,QA,ENGINEERING,ENGINEER";

        private async Task<NoDrawingFlagDto> ToDtoAsync(NoDrawingFlag f, CancellationToken ct)
        {
            var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == f.ItemId).Select(i => new { i.Code, i.Title }).FirstOrDefaultAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { f.CreatorUserId, f.DecidedByUserId, f.VoidedByUserId }, ct);
            var dto = new NoDrawingFlagDto
            {
                Id = f.Id, ItemId = f.ItemId, ItemCode = item?.Code, ItemTitle = item?.Title, PartNumber = f.PartNumber, Reason = f.Reason, Status = f.Status, CreatorUserId = f.CreatorUserId,
                RequestedByName = names.Name(f.CreatorUserId), RequestedAt = f.CreateDate, DecidedByName = names.Name(f.DecidedByUserId), DecidedAt = f.DecidedAt, DecisionNote = f.DecisionNote,
                VoidedByName = names.Name(f.VoidedByUserId), VoidedAt = f.VoidedAt, VoidReason = f.VoidReason, RowVersion = f.RowVersion == null ? null : Convert.ToBase64String(f.RowVersion),
            };
            if (f.Status == NoDrawingStatus.Pending && _user.IsInRole(Deciders) && f.CreatorUserId != Me && !Ai.AiActing.IsActing) { dto.Actions.Add("approve"); dto.Actions.Add("reject"); }
            if ((f.Status == NoDrawingStatus.Pending || f.Status == NoDrawingStatus.Active) && _user.IsInRole(Voiders)) dto.Actions.Add("void");
            return dto;
        }

        public async Task<ResponseDto<PagedList<NoDrawingFlagDto>>> GetAsync(NoDrawingQuery query, CancellationToken ct)
        {
            query ??= new NoDrawingQuery();
            IQueryable<NoDrawingFlag> rows = _uow.Set<NoDrawingFlag>().AsNoTracking().Where(f => !f.Deleted);
            if (query.ItemId.HasValue) rows = rows.Where(f => f.ItemId == query.ItemId.Value);
            if (query.Status.HasValue) rows = rows.Where(f => f.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(f => f.PartNumber.Contains(text) || f.Reason.Contains(text));
            var projection = rows.OrderByDescending(f => f.Id).Select(f => new NoDrawingFlagDto
            {
                Id = f.Id, ItemId = f.ItemId, PartNumber = f.PartNumber, Reason = f.Reason, Status = f.Status, CreatorUserId = f.CreatorUserId, RequestedAt = f.CreateDate, DecidedAt = f.DecidedAt,
                DecisionNote = f.DecisionNote, VoidedAt = f.VoidedAt, VoidReason = f.VoidReason,
            });
            PagedList<NoDrawingFlagDto> page = await PagedList<NoDrawingFlagDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct);
            List<int> ids = page.Select(p => p.Id).ToList();
            List<NoDrawingFlag> full = await _uow.Set<NoDrawingFlag>().AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync(ct);
            for (int i = 0; i < page.Count; i++)
            {
                NoDrawingFlag f = full.First(x => x.Id == page[i].Id);
                page[i] = await ToDtoAsync(f, ct);
            }
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<NoDrawingFlagDto>> RequestAsync(RequestNoDrawingDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<NoDrawingFlagDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Requesters)) return Bad<NoDrawingFlagDto>(QualityText.NotAllowed);
            InventoryItem item = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.ItemId && !i.Deleted, ct);
            if (item == null) return QualityText.NotFound<NoDrawingFlagDto>("کالا پیدا نشد.");
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            int min = (int)st[QualitySettingKeys.PkbNoDrawingReasonMin];
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < min) return Bad<NoDrawingFlagDto>($"دلیل «بدون نقشه» را دست‌کم {min} نویسه بنویسید (چرا این قطعه نقشه ندارد یا لازم ندارد).");
            if (reason.Length > 1000) return Bad<NoDrawingFlagDto>("دلیل بیش از ۱۰۰۰ نویسه است.");
            if (await EdmsResolver.HasReleasedDrawingAsync(_uow, item.Id, ct)) return Bad<NoDrawingFlagDto>("این قطعه نقشه منتشرشده دارد؛ پرچم «بدون نقشه» لازم نیست.");
            if (await _uow.Set<NoDrawingFlag>().AnyAsync(f => !f.Deleted && f.ItemId == item.Id && (f.Status == NoDrawingStatus.Pending || f.Status == NoDrawingStatus.Active), ct))
                return Bad<NoDrawingFlagDto>("برای این قطعه پرچم «بدون نقشه» در جریان یا فعال است.");
            DateTimeOffset now = DateTimeOffset.Now;
            var flag = new NoDrawingFlag { ItemId = item.Id, PartNumber = item.PartNumber ?? item.Code, Reason = reason, Status = NoDrawingStatus.Pending, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me };
            _uow.Set<NoDrawingFlag>().Add(flag);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<NoDrawingFlagDto>("برای این قطعه هم‌زمان پرچم دیگری ثبت شد."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(flag, ct));
        }

        public async Task<ResponseDto<NoDrawingFlagDto>> ActionAsync(NoDrawingActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<NoDrawingFlagDto>(QualityText.InvalidData);
            NoDrawingFlag f = await _uow.Set<NoDrawingFlag>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<NoDrawingFlagDto>("پرچم پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(f).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<NoDrawingFlagDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                case "reject":
                    bool approve = dto.Action.Trim().Equals("approve", StringComparison.OrdinalIgnoreCase);
                    if (!_user.IsInRole(Deciders)) return Bad<NoDrawingFlagDto>(QualityText.NotAllowed);
                    if (Ai.AiActing.IsActing) return Bad<NoDrawingFlagDto>("فعال‌سازی «بدون نقشه» با انسان است؛ صندلی هوش مصنوعی آن را تصمیم نمی‌گیرد.");
                    if (f.Status != NoDrawingStatus.Pending) return Bad<NoDrawingFlagDto>("فقط درخواست در انتظار تصمیم‌گیری می‌شود.");
                    if (f.CreatorUserId == Me) return Bad<NoDrawingFlagDto>(QualityText.SameUser + " (درخواست‌کننده نمی‌تواند پرچم را فعال کند)");
                    if (!approve && note == null) return Bad<NoDrawingFlagDto>("دلیل رد را بنویسید.");
                    f.Status = approve ? NoDrawingStatus.Active : NoDrawingStatus.Rejected; f.DecidedByUserId = Me; f.DecidedAt = now; f.DecisionNote = note;
                    break;
                case "void":
                    if (!_user.IsInRole(Voiders)) return Bad<NoDrawingFlagDto>(QualityText.NotAllowed);
                    if (f.Status != NoDrawingStatus.Pending && f.Status != NoDrawingStatus.Active) return Bad<NoDrawingFlagDto>("فقط پرچم در جریان یا فعال باطل می‌شود.");
                    if (note == null) return Bad<NoDrawingFlagDto>("دلیل ابطال را بنویسید.");
                    f.Status = NoDrawingStatus.Voided; f.VoidedByUserId = Me; f.VoidedAt = now; f.VoidReason = note;
                    break;
                default:
                    return Bad<NoDrawingFlagDto>("عمل نامعتبر است (approve، reject یا void).");
            }
            f.ModifyDate = now; f.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NoDrawingFlagDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, ct));
        }
    }
}
