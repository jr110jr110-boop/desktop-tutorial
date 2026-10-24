using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public interface IOrgService
    {
        Task<ResponseDto<PagedList<UnitDto>>> GetUnitsAsync(OfficePageQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<UnitDto>> SaveUnitAsync(SaveUnitDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PositionDto>>> GetPositionsAsync(PositionQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PositionDto>> SavePositionAsync(SavePositionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<DelegationDto>>> GetDelegationsAsync(DelegationQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DelegationDto>> SaveDelegationAsync(SaveDelegationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<DelegationDto>> CancelDelegationAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DelegationDto>> DecideDelegationAsync(int id, bool approve, CancellationToken cancellationToken);
        Task<ResponseDto<OrgNodeDto>> GetTreeAsync(CancellationToken cancellationToken);
        Task<ResponseDto<MyOfficeDto>> GetMineAsync(CancellationToken cancellationToken);
        Task<ResponseDto<MyOfficeDto>> SaveSignatureAsync(SaveSignatureDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Organisation chart: units, positions (letters go to positions, not people), deputies and the signature image of a user.</summary>
    public class OrgService : IOrgService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public OrgService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);

        /// <summary>An alert in the inbox of a user (or of a role), saved with the change that caused it.</summary>
        private void Alert(string key, string title, int? userId, string role, string link, int? sourceId)
        {
            if (userId == null && role == null) return;
            _uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                RecipientUserId = userId, RecipientRole = role, Kind = OfInboxKind.Alert, Title = title.Length > 300 ? title.Substring(0, 300) : title, SourceModule = "office", SourceType = "org",
                SourceId = sourceId, SourceKey = key, Link = link, Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
            });
        }

        private async Task CloseAlertsAsync(string key, string reason, CancellationToken ct)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey == key && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        private async Task<PagedList<T>> PageAsync<T>(List<T> rows, int total, int pageNumber, int pageSize)
        {
            F2Page.Window(pageNumber, pageSize, total, out int number, out int size);
            await Task.CompletedTask;
            return F2Page.Make(rows, total, number, size);
        }

        // -------------------------------------------------------------------------------- units

        private static UnitDto ToDto(OfUnit u, Dictionary<int, string> unitTitles, Dictionary<int, string> positionTitles, Dictionary<int, int> counts) => new UnitDto
        {
            Id = u.Id, Code = u.Code, Title = u.Title, ParentId = u.ParentId, ParentTitle = u.ParentId.HasValue && unitTitles.TryGetValue(u.ParentId.Value, out string pt) ? pt : null,
            ManagerPositionId = u.ManagerPositionId, ManagerPositionTitle = positionTitles.Title(u.ManagerPositionId), IsActive = u.IsActive,
            PositionCount = counts.TryGetValue(u.Id, out int c) ? c : 0,
        };

        private async Task<List<UnitDto>> MapUnitsAsync(List<OfUnit> units, CancellationToken ct)
        {
            List<int> parentIds = units.Where(u => u.ParentId.HasValue).Select(u => u.ParentId.Value).Distinct().ToList();
            Dictionary<int, string> unitTitles = parentIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<OfUnit>().AsNoTracking().Where(u => parentIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Title, ct);
            Dictionary<int, string> positionTitles = await OfficeActors.PositionTitlesAsync(_uow, units.Select(u => u.ManagerPositionId), ct);
            List<int> ids = units.Select(u => u.Id).ToList();
            Dictionary<int, int> counts = await _uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && ids.Contains(p.UnitId))
                .GroupBy(p => p.UnitId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
            return units.Select(u => ToDto(u, unitTitles, positionTitles, counts)).ToList();
        }

        public async Task<ResponseDto<PagedList<UnitDto>>> GetUnitsAsync(OfficePageQuery query, CancellationToken ct)
        {
            query ??= new OfficePageQuery();
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            IQueryable<OfUnit> q = _uow.Set<OfUnit>().AsNoTracking().Where(u => !u.Deleted);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(u => u.Code.Contains(text) || u.Title.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfUnit> rows = await q.OrderBy(u => u.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await MapUnitsAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<UnitDto>> SaveUnitAsync(SaveUnitDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.OrgAdmins)) return Bad<UnitDto>(OfficeText.NotAllowed);
            if (dto == null || Clean(dto.Code) == null || Clean(dto.Title) == null) return Bad<UnitDto>("کد و عنوان واحد را وارد کنید.");
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            string code = Clean(dto.Code).ToUpperInvariant();
            if (await _uow.Set<OfUnit>().AnyAsync(u => !u.Deleted && u.Code == code && u.Id != (dto.Id ?? 0), ct)) return Bad<UnitDto>("این کد واحد قبلاً ثبت شده است.");
            OfUnit unit = null;
            if (dto.Id.HasValue)
            {
                unit = await _uow.Set<OfUnit>().FirstOrDefaultAsync(u => u.Id == dto.Id.Value && !u.Deleted, ct);
                if (unit == null) return OfficeText.NotFound<UnitDto>("واحد پیدا نشد.");
            }
            int? parentId = dto.ParentId;
            if (unit != null && unit.Code == "CO" && unit.ParentId == null) parentId = null;
            else if (!parentId.HasValue) parentId = (await OfficeActors.EnsureRootUnitAsync(_uow, ct)).Id;
            if (parentId.HasValue)
            {
                if (!await _uow.Set<OfUnit>().AnyAsync(u => u.Id == parentId.Value && !u.Deleted, ct)) return Bad<UnitDto>("واحد بالادستی پیدا نشد.");
                if (unit != null)
                {
                    int? walk = parentId;
                    for (int guard = 0; walk.HasValue && guard < 100; guard++)
                    {
                        if (walk.Value == unit.Id) return Bad<UnitDto>("واحد بالادستی نمی‌تواند خود واحد یا زیرمجموعه آن باشد.");
                        int current = walk.Value;
                        walk = await _uow.Set<OfUnit>().AsNoTracking().Where(u => u.Id == current).Select(u => u.ParentId).FirstOrDefaultAsync(ct);
                    }
                }
            }
            DateTimeOffset now = DateTimeOffset.Now;
            if (unit == null)
            {
                unit = new OfUnit { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfUnit>().Add(unit);
            }
            unit.Code = code; unit.Title = Clean(dto.Title); unit.ParentId = parentId; unit.IsActive = dto.IsActive;
            unit.ModifyDate = now; unit.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapUnitsAsync(new List<OfUnit> { unit }, ct))[0]);
        }

        // -------------------------------------------------------------------------------- positions

        private async Task<List<PositionDto>> MapPositionsAsync(List<OfPosition> rows, CancellationToken ct)
        {
            List<int> unitIds = rows.Select(p => p.UnitId).Distinct().ToList();
            Dictionary<int, string> units = await _uow.Set<OfUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Title, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(p => p.UserId), ct);
            List<int> holders = rows.Where(p => p.UserId.HasValue).Select(p => p.UserId.Value).Distinct().ToList();
            HashSet<int> signed = (await _uow.Set<OfUserSignature>().AsNoTracking().Where(s => holders.Contains(s.UserId)).Select(s => s.UserId).ToListAsync(ct)).ToHashSet();
            return rows.Select(p => new PositionDto
            {
                Id = p.Id, Title = p.Title, UnitId = p.UnitId, UnitTitle = units.TryGetValue(p.UnitId, out string ut) ? ut : null, UserId = p.UserId, UserName = names.Name(p.UserId),
                EmployeeId = p.EmployeeId, IsManager = p.IsManager, CanSign = p.CanSign, IsActive = p.IsActive, HolderHasSignature = p.UserId.HasValue && signed.Contains(p.UserId.Value),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PositionDto>>> GetPositionsAsync(PositionQuery query, CancellationToken ct)
        {
            query ??= new PositionQuery();
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            IQueryable<OfPosition> q = _uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted);
            if (query.UnitId.HasValue) q = q.Where(p => p.UnitId == query.UnitId.Value);
            if (query.UserId.HasValue) q = q.Where(p => p.UserId == query.UserId.Value);
            if (query.SignersOnly == true) q = q.Where(p => p.CanSign);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Title.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfPosition> rows = await q.OrderBy(p => p.UnitId).ThenBy(p => p.Title).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await MapPositionsAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<PositionDto>> SavePositionAsync(SavePositionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.OrgAdmins)) return Bad<PositionDto>(OfficeText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<PositionDto>("عنوان سمت را وارد کنید.");
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            OfUnit unit = await _uow.Set<OfUnit>().FirstOrDefaultAsync(u => u.Id == dto.UnitId && !u.Deleted, ct);
            if (unit == null) return Bad<PositionDto>("واحد سازمانی پیدا نشد.");
            if (dto.UserId.HasValue && !await _uow.Set<User>().AnyAsync(u => u.Id == dto.UserId.Value, ct)) return Bad<PositionDto>("کاربر انتخاب‌شده پیدا نشد.");
            int? employeeId = dto.EmployeeId;
            if (employeeId.HasValue && !await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == employeeId.Value, ct)) return Bad<PositionDto>("کارمند انتخاب‌شده پیدا نشد.");
            if (!employeeId.HasValue && dto.UserId.HasValue)
                employeeId = await _uow.Set<FnEmployee>().Where(e => e.UserId == dto.UserId.Value && e.IsActive).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            OfPosition position = null;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                position = await _uow.Set<OfPosition>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct);
                if (position == null) return OfficeText.NotFound<PositionDto>("سمت پیدا نشد.");
            }
            // who sits in a position decides who reads its letters: only HR changes the holder (the CEO only for his own position or a vacant one), and never for himself
            int? previousHolder = position?.UserId;
            bool holderChanged = dto.UserId != previousHolder;
            if (holderChanged && dto.UserId.HasValue)
            {
                if (dto.UserId.Value == Me) return Bad<PositionDto>("تعیین خود به‌عنوان دارنده یک سمت مجاز نیست؛ این کار را شخص دیگری انجام می‌دهد.");
                bool hr = _user.IsInRole(OfficeRoles.Hr);
                bool ceoRule = _user.IsInRole(OfficeRoles.Ceo) && (previousHolder == null || previousHolder == Me);
                if (!hr && !ceoRule) return Bad<PositionDto>("فقط مسئول منابع انسانی دارنده سمت را تعیین یا تغییر می‌دهد (مدیرعامل برای سمت خودش یا سمت خالی).");
            }
            else if (holderChanged && previousHolder.HasValue && !_user.IsInRole(OfficeRoles.Hr) && !(_user.IsInRole(OfficeRoles.Ceo) && previousHolder == Me))
                return Bad<PositionDto>("فقط مسئول منابع انسانی دارنده سمت را تعیین یا تغییر می‌دهد (مدیرعامل برای سمت خودش یا سمت خالی).");
            if (position == null)
            {
                position = new OfPosition { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfPosition>().Add(position);
            }
            position.Title = Clean(dto.Title); position.UnitId = unit.Id; position.UserId = dto.UserId; position.EmployeeId = employeeId;
            position.IsManager = dto.IsManager; position.CanSign = dto.CanSign; position.IsActive = dto.IsActive;
            position.ModifyDate = now; position.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            if (holderChanged && position.UserId.HasValue)
            {
                Alert("position-holder:" + position.Id + ":" + position.UserId.Value, "شما به‌عنوان دارنده سمت «" + position.Title + "» ثبت شدید؛ نامه‌ها و کارتابل این سمت به شما می‌رسد.", position.UserId.Value, null, "/office/me", position.Id);
                await _uow.SaveChangesAsync(ct);
            }
            // one manager per unit: the unit points at the manager position
            if (position.IsManager && position.IsActive && unit.ManagerPositionId != position.Id)
            {
                if (unit.ManagerPositionId.HasValue)
                {
                    OfPosition old = await _uow.Set<OfPosition>().FirstOrDefaultAsync(p => p.Id == unit.ManagerPositionId.Value, ct);
                    if (old != null) old.IsManager = false;
                }
                unit.ManagerPositionId = position.Id;
                unit.ModifyDate = now; unit.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
            }
            else if ((!position.IsManager || !position.IsActive) && unit.ManagerPositionId == position.Id)
            {
                unit.ManagerPositionId = null;
                unit.ModifyDate = now; unit.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
            }
            return ResponseDtoHelper.Ok((await MapPositionsAsync(new List<OfPosition> { position }, ct))[0]);
        }

        // -------------------------------------------------------------------------------- delegations

        private async Task<List<DelegationDto>> MapDelegationsAsync(List<OfDelegation> rows, CancellationToken ct)
        {
            Dictionary<int, string> titles = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(d => (int?)d.PositionId), ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(d => new int?[] { d.DeputyUserId, d.CreatorUserId, d.DecidedByUserId }), ct);
            DateTime today = DateTime.Today;
            return rows.Select(d => new DelegationDto
            {
                Id = d.Id, PositionId = d.PositionId, PositionTitle = titles.Title(d.PositionId), DeputyUserId = d.DeputyUserId, DeputyName = names.Name(d.DeputyUserId),
                FromDate = d.FromDate, ToDate = d.ToDate, FromDateText = OfficeText.Date(d.FromDate), ToDateText = OfficeText.Date(d.ToDate), IsActive = d.IsActive,
                Current = d.IsActive && d.Status == OfDelegationStatus.Approved && d.FromDate <= today && d.ToDate >= today, Reason = d.Reason,
                Status = d.Status, CreatedByName = names.Name(d.CreatorUserId), DecidedByName = names.Name(d.DecidedByUserId), DecidedAtText = OfficeText.Stamp(d.DecidedAt),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<DelegationDto>>> GetDelegationsAsync(DelegationQuery query, CancellationToken ct)
        {
            query ??= new DelegationQuery();
            IQueryable<OfDelegation> q = _uow.Set<OfDelegation>().AsNoTracking().Where(d => !d.Deleted);
            if (query.PositionId.HasValue) q = q.Where(d => d.PositionId == query.PositionId.Value);
            if (query.DeputyUserId.HasValue) q = q.Where(d => d.DeputyUserId == query.DeputyUserId.Value);
            if (query.CurrentOnly == true)
            {
                DateTime today = DateTime.Today;
                q = q.Where(d => d.IsActive && d.Status == OfDelegationStatus.Approved && d.FromDate <= today && d.ToDate >= today);
            }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfDelegation> rows = await q.OrderByDescending(d => d.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await MapDelegationsAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<DelegationDto>> SaveDelegationAsync(SaveDelegationDto dto, CancellationToken ct)
        {
            if (dto == null || dto.PositionId <= 0 || dto.DeputyUserId <= 0) return Bad<DelegationDto>(OfficeText.InvalidData);
            if (Clean(dto.Reason) == null) return Bad<DelegationDto>("علت نیابت را بنویسید.");
            OfPosition position = await _uow.Set<OfPosition>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PositionId && !p.Deleted && p.IsActive, ct);
            if (position == null) return Bad<DelegationDto>("سمت پیدا نشد.");
            bool holder = position.UserId == Me;
            if (!holder && !_user.IsInRole(OfficeRoles.OrgAdmins)) return Bad<DelegationDto>(OfficeText.NotAllowed);
            if (dto.DeputyUserId == Me) return Bad<DelegationDto>("تعیین خود به‌عنوان جانشین یک سمت مجاز نیست.");
            if (dto.ToDate.Date < dto.FromDate.Date) return Bad<DelegationDto>("تاریخ پایان نیابت از تاریخ شروع زودتر است.");
            if (position.UserId == dto.DeputyUserId) return Bad<DelegationDto>("جانشین نمی‌تواند خود دارنده سمت باشد.");
            if (!await _uow.Set<User>().AnyAsync(u => u.Id == dto.DeputyUserId, ct)) return Bad<DelegationDto>("کاربر جانشین پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            // the holder (or the CEO) decides; somebody else's delegation waits for that decision and gives no access until then
            bool approvedNow = holder || _user.IsInRole(OfficeRoles.Ceo);
            var d = new OfDelegation
            {
                PositionId = position.Id, DeputyUserId = dto.DeputyUserId, FromDate = dto.FromDate.Date, ToDate = dto.ToDate.Date, IsActive = true, Reason = Clean(dto.Reason),
                Status = approvedNow ? OfDelegationStatus.Approved : OfDelegationStatus.Pending, DecidedByUserId = approvedNow ? Me : (int?)null, DecidedAt = approvedNow ? now : (DateTimeOffset?)null,
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<OfDelegation>().Add(d);
            await _uow.SaveChangesAsync(ct);
            string range = OfficeText.Date(d.FromDate) + " تا " + OfficeText.Date(d.ToDate);
            if (d.Status == OfDelegationStatus.Pending)
                Alert("delegation:" + d.Id, "درخواست نیابت سمت «" + position.Title + "» در بازه " + range + " منتظر تایید شماست.", position.UserId, position.UserId.HasValue ? null : OfficeRoles.Ceo, "/office/delegations", d.Id);
            else
            {
                Alert("delegation-deputy:" + d.Id, "شما در بازه " + range + " جانشین سمت «" + position.Title + "» هستید.", d.DeputyUserId, null, "/office/me", d.Id);
                if (position.UserId.HasValue && position.UserId != Me) Alert("delegation:" + d.Id, "برای سمت «" + position.Title + "» جانشین تعیین شد (" + range + ").", position.UserId, null, "/office/delegations", d.Id);
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapDelegationsAsync(new List<OfDelegation> { d }, ct))[0]);
        }

        public async Task<ResponseDto<DelegationDto>> DecideDelegationAsync(int id, bool approve, CancellationToken ct)
        {
            OfDelegation d = await _uow.Set<OfDelegation>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (d == null) return OfficeText.NotFound<DelegationDto>("نیابت پیدا نشد.");
            if (d.Status != OfDelegationStatus.Pending) return Bad<DelegationDto>("این نیابت در انتظار تایید نیست.");
            OfPosition position = await _uow.Set<OfPosition>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == d.PositionId, ct);
            if (d.CreatorUserId == Me) return Bad<DelegationDto>("نیابت را کسی که ثبت کرده نمی‌تواند تایید کند؛ دارنده سمت یا مدیرعامل تایید می‌کند.");
            bool holder = position != null && position.UserId == Me;
            if (!holder && !_user.IsInRole(OfficeRoles.Ceo)) return Bad<DelegationDto>("فقط دارنده سمت یا مدیرعامل نیابت را تایید می‌کند.");
            d.Status = approve ? OfDelegationStatus.Approved : OfDelegationStatus.Rejected;
            if (!approve) d.IsActive = false;
            d.DecidedByUserId = Me; d.DecidedAt = DateTimeOffset.Now; d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            await CloseAlertsAsync("delegation:" + d.Id, approve ? "نیابت تایید شد" : "نیابت رد شد", ct);
            string title = position?.Title ?? "";
            string range = OfficeText.Date(d.FromDate) + " تا " + OfficeText.Date(d.ToDate);
            if (approve) Alert("delegation-deputy:" + d.Id, "شما در بازه " + range + " جانشین سمت «" + title + "» هستید.", d.DeputyUserId, null, "/office/me", d.Id);
            Alert("delegation-result:" + d.Id, "نیابت سمت «" + title + "» " + (approve ? "تایید شد." : "رد شد."), d.CreatorUserId, null, "/office/delegations", d.Id);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapDelegationsAsync(new List<OfDelegation> { d }, ct))[0]);
        }

        public async Task<ResponseDto<DelegationDto>> CancelDelegationAsync(int id, CancellationToken ct)
        {
            OfDelegation d = await _uow.Set<OfDelegation>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (d == null) return OfficeText.NotFound<DelegationDto>("نیابت پیدا نشد.");
            bool holder = await _uow.Set<OfPosition>().AnyAsync(p => p.Id == d.PositionId && p.UserId == Me, ct);
            if (!holder && d.CreatorUserId != Me && !_user.IsInRole(OfficeRoles.OrgAdmins)) return Bad<DelegationDto>(OfficeText.NotAllowed);
            d.IsActive = false; d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            await CloseAlertsAsync("delegation:" + d.Id, "نیابت لغو شد", ct);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapDelegationsAsync(new List<OfDelegation> { d }, ct))[0]);
        }

        // -------------------------------------------------------------------------------- chart and me

        public async Task<ResponseDto<OrgNodeDto>> GetTreeAsync(CancellationToken ct)
        {
            OfUnit root = await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            List<OfUnit> units = await _uow.Set<OfUnit>().AsNoTracking().Where(u => !u.Deleted && u.IsActive).ToListAsync(ct);
            List<OfPosition> positions = await _uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive).OrderBy(p => p.Title).ToListAsync(ct);
            List<PositionDto> mapped = await MapPositionsAsync(positions, ct);
            OrgNodeDto Build(OfUnit u, int depth)
            {
                var node = new OrgNodeDto { Id = u.Id, Code = u.Code, Title = u.Title, ManagerPositionId = u.ManagerPositionId };
                node.Positions = mapped.Where(p => p.UnitId == u.Id).ToList();
                if (depth < 30)
                    foreach (OfUnit child in units.Where(x => x.ParentId == u.Id).OrderBy(x => x.Code)) node.Children.Add(Build(child, depth + 1));
                return node;
            }
            return ResponseDtoHelper.Ok(Build(root, 0));
        }

        private async Task<MyOfficeDto> BuildMineAsync(CancellationToken ct)
        {
            List<ActingPosition> acting = await OfficeActors.ActingAsync(_uow, Me, ct);
            List<int> ids = acting.Select(a => a.PositionId).ToList();
            List<OfPosition> positions = ids.Count == 0 ? new List<OfPosition>() : await _uow.Set<OfPosition>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            List<int> unitIds = positions.Select(p => p.UnitId).Distinct().ToList();
            Dictionary<int, string> units = await _uow.Set<OfUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Title, ct);
            OfUserSignature signature = await _uow.Set<OfUserSignature>().AsNoTracking().FirstOrDefaultAsync(s => s.UserId == Me, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { Me }, ct);
            int? employeeId = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.UserId == Me && e.IsActive).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            return new MyOfficeDto
            {
                UserId = Me, UserName = names.Name(Me), HasSignature = signature != null, SignatureDocumentId = signature?.ImageDocumentId, EmployeeId = employeeId,
                Positions = positions.Select(p => new MyPositionDto
                {
                    PositionId = p.Id, Title = p.Title, UnitTitle = units.TryGetValue(p.UnitId, out string t) ? t : null, CanSign = p.CanSign, IsManager = p.IsManager,
                    OnBehalf = acting.First(a => a.PositionId == p.Id).OnBehalf,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<MyOfficeDto>> GetMineAsync(CancellationToken ct) => ResponseDtoHelper.Ok(await BuildMineAsync(ct));

        public async Task<ResponseDto<MyOfficeDto>> SaveSignatureAsync(SaveSignatureDto dto, CancellationToken ct)
        {
            if (dto == null || dto.DocumentId == Guid.Empty) return Bad<MyOfficeDto>("تصویر امضا را بارگذاری و شناسه آن را بفرستید.");
            if (!await _uow.Set<Entities.Model.Document>().AnyAsync(d => d.Id == dto.DocumentId, ct)) return Bad<MyOfficeDto>("فایل بارگذاری‌شده پیدا نشد.");
            OfUserSignature s = await _uow.Set<OfUserSignature>().FirstOrDefaultAsync(x => x.UserId == Me, ct);
            if (s == null)
            {
                s = new OfUserSignature { UserId = Me };
                _uow.Set<OfUserSignature>().Add(s);
            }
            s.ImageDocumentId = dto.DocumentId; s.UpdatedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BuildMineAsync(ct));
        }
    }

    public interface IInboxService
    {
        Task<ResponseDto<PagedList<InboxItemDto>>> GetInboxAsync(InboxQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<InboxCountsDto>> GetCountsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<InboxItemDto>> ActAsync(InboxActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>The unified inbox: letters, referrals, approvals, tasks, alerts and meeting actions for a person, the positions they hold or act for, and their roles.</summary>
    public class InboxService : IInboxService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public InboxService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;

        private List<string> MyRoles() =>
            RoleConstants.GetResponseRoles().Select(r => r.Name.ToUpperInvariant()).Where(r => _user.IsInRole(r)).ToList();

        private async Task<IQueryable<OfInboxItem>> MineAsync(CancellationToken ct)
        {
            int me = Me;
            List<int> positions = (await OfficeActors.ActingAsync(_uow, me, ct)).Select(a => a.PositionId).ToList();
            List<string> roles = MyRoles();
            return _uow.Set<OfInboxItem>().Where(i => i.RecipientUserId == me
                || (i.RecipientPositionId != null && positions.Contains(i.RecipientPositionId.Value))
                || (i.RecipientRole != null && roles.Contains(i.RecipientRole)));
        }

        private async Task<List<InboxItemDto>> MapAsync(List<OfInboxItem> rows, CancellationToken ct)
        {
            Dictionary<int, string> titles = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.RecipientPositionId), ct);
            DateTime today = DateTime.Today;
            return rows.Select(i => new InboxItemDto
            {
                Id = i.Id, Kind = i.Kind, Title = i.Title, SourceModule = i.SourceModule, SourceType = i.SourceType, SourceId = i.SourceId, SourceKey = i.SourceKey, Link = i.Link,
                DueDate = i.DueDate, DueDateText = OfficeText.Date(i.DueDate), Overdue = i.DueDate.HasValue && i.DueDate.Value.Date < today && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read),
                Priority = i.Priority, Status = i.Status, CreatedAt = i.CreatedAt, CreatedAtText = OfficeText.Stamp(i.CreatedAt), ReadAt = i.ReadAt, ClosedAt = i.ClosedAt,
                RecipientPositionId = i.RecipientPositionId, RecipientPositionTitle = titles.Title(i.RecipientPositionId), RecipientRole = i.RecipientRole, ClosedReason = i.ClosedReason,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<InboxItemDto>>> GetInboxAsync(InboxQuery query, CancellationToken ct)
        {
            query ??= new InboxQuery();
            IQueryable<OfInboxItem> q = (await MineAsync(ct)).AsNoTracking();
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            else if (!query.IncludeClosed) q = q.Where(i => i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read);
            if (query.Kind.HasValue) q = q.Where(i => i.Kind == query.Kind.Value);
            if (!string.IsNullOrWhiteSpace(query.SourceModule)) { string m = query.SourceModule.Trim(); q = q.Where(i => i.SourceModule == m); }
            if (!string.IsNullOrWhiteSpace(query.SourceKey)) { string k = query.SourceKey.Trim(); q = q.Where(i => i.SourceKey == k); }
            string text = OfficeText.Clean(query.Contains);
            if (text != null) q = q.Where(i => i.Title.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfInboxItem> rows = await q.OrderByDescending(i => i.Priority).ThenByDescending(i => i.CreatedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await MapAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<InboxCountsDto>> GetCountsAsync(CancellationToken ct)
        {
            IQueryable<OfInboxItem> open = (await MineAsync(ct)).AsNoTracking().Where(i => i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read);
            DateTime today = DateTime.Today;
            var counts = new InboxCountsDto
            {
                Unread = await open.CountAsync(i => i.Status == OfInboxStatus.Unread, ct),
                Open = await open.CountAsync(ct),
                Overdue = await open.CountAsync(i => i.DueDate != null && i.DueDate < today, ct),
            };
            var byKind = await open.GroupBy(i => i.Kind).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            foreach (var k in byKind) counts.ByKind[k.Key.ToString()] = k.N;
            return ResponseDtoHelper.Ok(counts);
        }

        /// <summary>
        /// Items that wait for a step (dispatch of a signed letter, recording a leave, completing a request, finalising vouchers, the gates of the other modules) are role tasks:
        /// one person closing them by hand would leave the step waiting in nobody's inbox. Only free to-dos and alerts can be closed by hand.
        /// </summary>
        private static bool IsGateItem(OfInboxItem i)
        {
            string key = i.SourceKey ?? "";
            if (key.EndsWith(":dispatch", StringComparison.Ordinal) || key.StartsWith("leave-record:", StringComparison.Ordinal) || key.StartsWith("req-complete:", StringComparison.Ordinal)) return true;
            return i.SourceModule != null && i.SourceModule != "office" && i.Kind != OfInboxKind.Alert;
        }

        public async Task<ResponseDto<InboxItemDto>> ActAsync(InboxActionDto dto, CancellationToken ct)
        {
            if (dto == null) return OfficeText.Bad<InboxItemDto>(OfficeText.InvalidData);
            OfInboxItem item = await (await MineAsync(ct)).FirstOrDefaultAsync(i => i.Id == dto.Id, ct);
            if (item == null) return OfficeText.NotFound<InboxItemDto>("مورد پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "read":
                    if (item.Status == OfInboxStatus.Unread) { item.Status = OfInboxStatus.Read; item.ReadAt = now; }
                    break;
                case "unread":
                    if (item.Status == OfInboxStatus.Read) { item.Status = OfInboxStatus.Unread; item.ReadAt = null; }
                    break;
                case "done":
                    // approvals, referrals and meeting actions are closed by the gate itself, never by hand
                    if (item.Kind == OfInboxKind.Approval || item.Kind == OfInboxKind.Referral || item.Kind == OfInboxKind.MeetingAction || IsGateItem(item))
                        return OfficeText.Bad<InboxItemDto>("این مورد با انجام کار مربوطه بسته می‌شود.");
                    if (item.Status == OfInboxStatus.Unread || item.Status == OfInboxStatus.Read) { item.Status = OfInboxStatus.Done; item.ClosedAt = now; item.ClosedReason = "توسط کاربر بسته شد"; }
                    break;
                default:
                    return OfficeText.Bad<InboxItemDto>("عمل نامعتبر است.");
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapAsync(new List<OfInboxItem> { item }, ct))[0]);
        }
    }
}
