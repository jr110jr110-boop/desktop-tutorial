using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public interface IRequestService
    {
        /// <summary>The request types. The three starting types are created the first time the list is read.</summary>
        Task<ResponseDto<List<RequestTypeDto>>> GetTypesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<RequestTypeDto>> SaveTypeAsync(SaveRequestTypeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<RequestDto>>> GetRequestsAsync(RequestQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<RequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<RequestDto>> SaveRequestAsync(SaveRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<RequestDto>> RequestActionAsync(RequestActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>General requests (vehicle, IT support, training, document copy ...): a form defined as data, an approval chain of positions and a final carry-out step.</summary>
    public class RequestService : IRequestService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public RequestService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private bool IsAdmin => _user.IsInRole(OfficeRoles.OrgAdmins);
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly Regex FieldName = new Regex("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.Compiled);
        private static readonly string[] FieldTypes = { "text", "number", "date", "select" };

        // -------------------------------------------------------------------------------- types

        private static string Fields(params RequestFieldDto[] fields) => JsonSerializer.Serialize(fields.ToList(), Json);

        private async Task EnsureSeedAsync(CancellationToken ct)
        {
            if (await _uow.Set<OfRequestType>().AnyAsync(ct)) return;
            DateTimeOffset now = DateTimeOffset.Now;
            OfRequestType Make(string title, string fields, string target) => new OfRequestType { Title = title, FieldsJson = fields, ApprovalChain = "", TargetNote = target, IsActive = true, CreateDate = now, ModifyDate = now };
            var seeds = new List<OfRequestType>
            {
                Make("درخواست خودرو", Fields(
                    new RequestFieldDto { Name = "destination", Label = "مقصد", Type = "text", Required = true },
                    new RequestFieldDto { Name = "date", Label = "تاریخ استفاده", Type = "date", Required = true },
                    new RequestFieldDto { Name = "passengers", Label = "تعداد سرنشین", Type = "number" },
                    new RequestFieldDto { Name = "purpose", Label = "هدف سفر", Type = "text", Required = true }), "واحد اداری"),
                Make("درخواست پشتیبانی فناوری اطلاعات", Fields(
                    new RequestFieldDto { Name = "category", Label = "دسته", Type = "select", Required = true, Options = new List<string> { "سخت‌افزار", "نرم‌افزار", "شبکه", "دسترسی", "سایر" } },
                    new RequestFieldDto { Name = "subject", Label = "موضوع", Type = "text", Required = true },
                    new RequestFieldDto { Name = "description", Label = "شرح مشکل", Type = "text" }), "واحد فناوری اطلاعات"),
                Make("درخواست آموزش", Fields(
                    new RequestFieldDto { Name = "course", Label = "عنوان دوره", Type = "text", Required = true },
                    new RequestFieldDto { Name = "participants", Label = "تعداد شرکت‌کنندگان", Type = "number" },
                    new RequestFieldDto { Name = "preferredDate", Label = "تاریخ مناسب", Type = "date" },
                    new RequestFieldDto { Name = "justification", Label = "ضرورت آموزش", Type = "text", Required = true }), "واحد منابع انسانی"),
            };
            foreach (OfRequestType t in seeds) _uow.Set<OfRequestType>().Add(t);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // another request seeded at the same moment
                foreach (OfRequestType t in seeds) _uow.Entry(t).State = EntityState.Detached;
            }
        }

        private static List<RequestFieldDto> ParseFields(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<RequestFieldDto>();
            try { return JsonSerializer.Deserialize<List<RequestFieldDto>>(json, Json) ?? new List<RequestFieldDto>(); }
            catch (JsonException) { return new List<RequestFieldDto>(); }
        }

        private async Task<RequestTypeDto> ToDtoAsync(OfRequestType t, CancellationToken ct)
        {
            List<int> chain = OfficeText.Ids(t.ApprovalChain);
            Dictionary<int, string> titles = await OfficeActors.PositionTitlesAsync(_uow, chain.Select(i => (int?)i), ct);
            return new RequestTypeDto
            {
                Id = t.Id, Title = t.Title, Fields = ParseFields(t.FieldsJson), ApprovalChain = chain, ApprovalChainTitles = chain.Select(i => titles.Title(i) ?? "").ToList(),
                TargetNote = t.TargetNote, IsActive = t.IsActive,
            };
        }

        public async Task<ResponseDto<List<RequestTypeDto>>> GetTypesAsync(CancellationToken ct)
        {
            await EnsureSeedAsync(ct);
            IQueryable<OfRequestType> q = _uow.Set<OfRequestType>().AsNoTracking().Where(t => !t.Deleted);
            if (!IsAdmin) q = q.Where(t => t.IsActive);
            List<OfRequestType> rows = await q.OrderBy(t => t.Id).ToListAsync(ct);
            var result = new List<RequestTypeDto>();
            foreach (OfRequestType t in rows) result.Add(await ToDtoAsync(t, ct));
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<RequestTypeDto>> SaveTypeAsync(SaveRequestTypeDto dto, CancellationToken ct)
        {
            if (!IsAdmin) return Bad<RequestTypeDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<RequestTypeDto>(OfficeText.InvalidData);
            string title = Clean(dto.Title);
            if (title == null) return Bad<RequestTypeDto>("عنوان نوع درخواست را وارد کنید.");
            List<RequestFieldDto> fields = dto.Fields ?? new List<RequestFieldDto>();
            if (fields.Count == 0 || fields.Count > 40) return Bad<RequestTypeDto>("فرم باید بین ۱ تا ۴۰ فیلد داشته باشد.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RequestFieldDto f in fields)
            {
                f.Name = Clean(f.Name); f.Label = Clean(f.Label); f.Type = (Clean(f.Type) ?? "text").ToLowerInvariant();
                if (f.Name == null || !FieldName.IsMatch(f.Name) || !names.Add(f.Name)) return Bad<RequestTypeDto>("نام فیلدها باید لاتین، یکتا و بدون فاصله باشد.");
                if (f.Label == null) return Bad<RequestTypeDto>("برچسب هر فیلد را وارد کنید.");
                if (!FieldTypes.Contains(f.Type)) return Bad<RequestTypeDto>("نوع فیلد باید text، number، date یا select باشد.");
                f.Options = (f.Options ?? new List<string>()).Select(o => Clean(o)).Where(o => o != null).Distinct().ToList();
                if (f.Type == "select" && f.Options.Count == 0) return Bad<RequestTypeDto>($"برای فیلد «{f.Label}» گزینه‌ها را وارد کنید.");
                if (f.Type != "select") f.Options = new List<string>();
            }
            List<int> chain = (dto.ApprovalChain ?? new List<int>()).Where(i => i > 0).ToList();
            if (chain.Count > 0)
            {
                List<int> distinct = chain.Distinct().ToList();
                int found = await _uow.Set<OfPosition>().CountAsync(p => !p.Deleted && p.IsActive && distinct.Contains(p.Id), ct);
                if (found != distinct.Count) return Bad<RequestTypeDto>("یکی از جایگاه‌های زنجیره تایید پیدا نشد یا غیرفعال است.");
            }
            await EnsureSeedAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            OfRequestType t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<OfRequestType>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (t == null) return OfficeText.NotFound<RequestTypeDto>("نوع درخواست پیدا نشد.");
            }
            else
            {
                t = new OfRequestType { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfRequestType>().Add(t);
            }
            if (await _uow.Set<OfRequestType>().AnyAsync(x => !x.Deleted && x.Title == title && x.Id != t.Id, ct)) return Bad<RequestTypeDto>("نوع درخواست دیگری با همین عنوان وجود دارد.");
            t.Title = title; t.FieldsJson = JsonSerializer.Serialize(fields, Json); t.ApprovalChain = string.Join(",", chain); t.TargetNote = Clean(dto.TargetNote); t.IsActive = dto.IsActive;
            t.ModifyDate = now; t.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(t, ct));
        }

        // -------------------------------------------------------------------------------- reading

        private sealed class Ctx
        {
            public List<int> Pos = new List<int>();
            public bool IsAdmin;
        }

        private async Task<Ctx> ContextAsync(CancellationToken ct)
        {
            List<ActingPosition> acting = await OfficeActors.ActingAsync(_uow, Me, ct);
            return new Ctx { Pos = acting.Select(a => a.PositionId).ToList(), IsAdmin = IsAdmin };
        }

        private static OfRequestApproval Current(OfRequest r) =>
            (r.Approvals ?? new List<OfRequestApproval>()).Where(a => a.Status == OfApprovalStatus.Pending).OrderBy(a => a.Step).FirstOrDefault();

        private bool CanSee(OfRequest r, Ctx c) =>
            r.RequesterUserId == Me || r.CreatorUserId == Me || c.IsAdmin || (r.Approvals ?? new List<OfRequestApproval>()).Any(a => c.Pos.Contains(a.PositionId));

        private List<string> Actions(OfRequest r, Ctx c)
        {
            var a = new List<string>();
            bool owner = r.RequesterUserId == Me || r.CreatorUserId == Me;
            OfRequestApproval current = Current(r);
            switch (r.Status)
            {
                case OfRequestStatus.Draft:
                    if (owner) { a.Add("save"); a.Add("submit"); a.Add("cancel"); }
                    break;
                case OfRequestStatus.InApproval:
                    if (current != null && c.Pos.Contains(current.PositionId) && r.RequesterUserId != Me) { a.Add("approve"); a.Add("reject"); }
                    if (owner || c.IsAdmin) a.Add("cancel");
                    break;
                case OfRequestStatus.Approved:
                    if (c.IsAdmin) a.Add("complete");
                    if (owner || c.IsAdmin) a.Add("cancel");
                    break;
            }
            return a;
        }

        private async Task<List<RequestDto>> DtosAsync(List<OfRequest> rows, Ctx c, CancellationToken ct)
        {
            List<int> typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
            Dictionary<int, OfRequestType> types = typeIds.Count == 0 ? new Dictionary<int, OfRequestType>() : await _uow.Set<OfRequestType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.SelectMany(r => (r.Approvals ?? new List<OfRequestApproval>()).Select(a => (int?)a.PositionId)), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new[] { (int?)r.RequesterUserId }.Concat((r.Approvals ?? new List<OfRequestApproval>()).Select(a => a.ActedByUserId))), ct);
            return rows.Select(r =>
            {
                types.TryGetValue(r.TypeId, out OfRequestType t);
                Dictionary<string, string> values = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(r.ValuesJson))
                {
                    try { values = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ValuesJson, Json) ?? values; }
                    catch (JsonException) { /* an unreadable value is shown empty */ }
                }
                return new RequestDto
                {
                    Id = r.Id, Number = r.Number, TypeId = r.TypeId, TypeTitle = t?.Title, TargetNote = t?.TargetNote, RequesterUserId = r.RequesterUserId, RequesterName = users.Name(r.RequesterUserId),
                    Values = values, Fields = t == null ? new List<RequestFieldDto>() : ParseFields(t.FieldsJson), Status = r.Status, SubmittedAt = r.SubmittedAt, SubmittedAtText = OfficeText.Stamp(r.SubmittedAt),
                    CompletedAt = r.CompletedAt, CompletedAtText = OfficeText.Stamp(r.CompletedAt), Note = r.Note,
                    Approvals = (r.Approvals ?? new List<OfRequestApproval>()).OrderBy(a => a.Step).Select(a => new RequestApprovalDto
                    {
                        Step = a.Step, PositionId = a.PositionId, PositionTitle = positions.Title(a.PositionId), Status = a.Status, ActedByName = users.Name(a.ActedByUserId), OnBehalf = a.OnBehalf,
                        Comment = a.Comment, ActedAt = a.ActedAt, ActedAtText = OfficeText.Stamp(a.ActedAt),
                    }).ToList(),
                    AllowedActions = Actions(r, c), RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<RequestDto>>> GetRequestsAsync(RequestQuery query, CancellationToken ct)
        {
            query ??= new RequestQuery();
            Ctx c = await ContextAsync(ct);
            int me = Me;
            List<int> pos = c.Pos;
            IQueryable<OfRequest> q = _uow.Set<OfRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (!c.IsAdmin) q = q.Where(r => r.RequesterUserId == me || r.CreatorUserId == me || r.Approvals.Any(a => pos.Contains(a.PositionId)));
            if (query.MineOnly == true) q = q.Where(r => r.RequesterUserId == me || r.CreatorUserId == me);
            if (query.WaitingForMe == true)
                q = q.Where(r => r.Status == OfRequestStatus.InApproval && r.Approvals.Any(a => a.Status == OfApprovalStatus.Pending && pos.Contains(a.PositionId)
                    && !r.Approvals.Any(b => b.Status == OfApprovalStatus.Pending && b.Step < a.Step)));
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.TypeId.HasValue) q = q.Where(r => r.TypeId == query.TypeId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || (r.ValuesJson != null && r.ValuesJson.Contains(text)));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfRequest> rows = await q.Include(r => r.Approvals).OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).AsSplitQuery().ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await DtosAsync(rows, c, ct), total, number, size));
        }

        private async Task<OfRequest> LoadAsync(int id, bool tracking, CancellationToken ct)
        {
            IQueryable<OfRequest> q = _uow.Set<OfRequest>().Where(r => r.Id == id && !r.Deleted).Include(r => r.Approvals);
            if (!tracking) q = q.AsNoTracking();
            return await q.FirstOrDefaultAsync(ct);
        }

        public async Task<ResponseDto<RequestDto>> GetRequestAsync(int id, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfRequest r = await LoadAsync(id, false, ct);
            if (r == null) return OfficeText.NotFound<RequestDto>("درخواست پیدا نشد.");
            if (!CanSee(r, c)) return Bad<RequestDto>(OfficeText.NotAllowed);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<OfRequest> { r }, c, ct))[0]);
        }

        // -------------------------------------------------------------------------------- save

        /// <summary>Checks the values against the form; returns the cleaned values or an error text.</summary>
        private static (Dictionary<string, string> Values, string Error) CheckValues(List<RequestFieldDto> fields, Dictionary<string, string> given, bool forSubmit)
        {
            var values = new Dictionary<string, string>();
            given ??= new Dictionary<string, string>();
            foreach (string key in given.Keys)
                if (!fields.Any(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase))) return (null, $"فیلد «{key}» در این فرم وجود ندارد.");
            foreach (RequestFieldDto f in fields)
            {
                string raw = null;
                foreach (KeyValuePair<string, string> kv in given) if (string.Equals(kv.Key, f.Name, StringComparison.OrdinalIgnoreCase)) raw = Clean(kv.Value);
                if (raw == null)
                {
                    if (f.Required && forSubmit) return (null, $"فیلد «{f.Label}» را پر کنید.");
                    continue;
                }
                if (raw.Length > 2000) return (null, $"متن فیلد «{f.Label}» بیش از حد طولانی است.");
                switch (f.Type)
                {
                    case "number":
                        if (!decimal.TryParse(OfficeText.Digits(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n)) return (null, $"فیلد «{f.Label}» باید عدد باشد.");
                        raw = n.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "date":
                        if (!AttendanceService.TryParseDate(raw, out DateTime d)) return (null, $"فیلد «{f.Label}» باید تاریخ معتبر باشد.");
                        raw = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        break;
                    case "select":
                        if (!f.Options.Contains(raw)) return (null, $"مقدار فیلد «{f.Label}» جزو گزینه‌ها نیست.");
                        break;
                }
                values[f.Name] = raw;
            }
            return (values, null);
        }

        public async Task<ResponseDto<RequestDto>> SaveRequestAsync(SaveRequestDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<RequestDto>(OfficeText.InvalidData);
            await EnsureSeedAsync(ct);
            Ctx c = await ContextAsync(ct);
            OfRequest r;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                r = await LoadAsync(dto.Id.Value, true, ct);
                if (r == null) return OfficeText.NotFound<RequestDto>("درخواست پیدا نشد.");
                if (!(r.RequesterUserId == Me || r.CreatorUserId == Me)) return Bad<RequestDto>(OfficeText.NotAllowed);
                if (r.Status != OfRequestStatus.Draft) return Bad<RequestDto>("فقط پیش‌نویس قابل ویرایش است.");
                if (r.TypeId != dto.TypeId) return Bad<RequestDto>("نوع درخواست قابل تغییر نیست.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                {
                    try { _uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<RequestDto>(OfficeText.InvalidData); }
                }
            }
            else
            {
                r = new OfRequest { TypeId = dto.TypeId, RequesterUserId = Me, Status = OfRequestStatus.Draft, CreateDate = now, CreatorUserId = Me, Approvals = new List<OfRequestApproval>() };
            }
            OfRequestType type = await _uow.Set<OfRequestType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TypeId && !t.Deleted && t.IsActive, ct);
            if (type == null) return OfficeText.NotFound<RequestDto>("نوع درخواست پیدا نشد.");
            var (values, error) = CheckValues(ParseFields(type.FieldsJson), dto.Values, false);
            if (error != null) return Bad<RequestDto>(error);
            r.ValuesJson = JsonSerializer.Serialize(values, Json);
            r.ModifyDate = now; r.ModifierUserId = Me;
            if (r.Id == 0)
            {
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, OfficeNumbers.Request, "RQ", DateTime.Today, ct);
                _uow.Set<OfRequest>().Add(r);
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<RequestDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<RequestDto>("ذخیره درخواست انجام نشد؛ دوباره تلاش کنید."); }
            if (dto.Submit) return await RequestActionAsync(new RequestActionDto { Id = r.Id, Action = "submit" }, ct);
            return await GetRequestAsync(r.Id, ct);
        }

        // -------------------------------------------------------------------------------- actions

        private void AddInbox(string key, OfInboxKind kind, string title, OfRequest r, int? positionId, int? userId, string role)
        {
            _uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                RecipientPositionId = positionId, RecipientUserId = userId, RecipientRole = role, Kind = kind, Title = title.Length > 300 ? title.Substring(0, 300) : title,
                SourceModule = "office", SourceType = "request", SourceId = r.Id, SourceKey = key, Link = "/office/requests/" + r.Id, Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
            });
        }

        private async Task CloseKeysAsync(string reason, CancellationToken ct, params string[] keys)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey != null && keys.Contains(i.SourceKey) && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        /// <summary>The unit manager position above the requester's position.</summary>
        private async Task<int?> ManagerOfAsync(int userId, CancellationToken ct)
        {
            OfPosition own = await _uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && p.UserId == userId).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            if (own == null) return null;
            int? unitId = own.UnitId;
            for (int i = 0; i < 12 && unitId.HasValue; i++)
            {
                int current = unitId.Value;
                OfUnit unit = await _uow.Set<OfUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == current && !u.Deleted, ct);
                if (unit == null) return null;
                if (unit.ManagerPositionId.HasValue && unit.ManagerPositionId.Value != own.Id) return unit.ManagerPositionId;
                unitId = unit.ParentId;
            }
            return null;
        }

        public async Task<ResponseDto<RequestDto>> RequestActionAsync(RequestActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<RequestDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfRequest r = await LoadAsync(dto.Id, true, ct);
            if (r == null) return OfficeText.NotFound<RequestDto>("درخواست پیدا نشد.");
            if (!CanSee(r, c)) return Bad<RequestDto>(OfficeText.NotAllowed);
            if (!string.IsNullOrEmpty(dto.RowVersion))
            {
                try { _uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<RequestDto>(OfficeText.InvalidData); }
            }
            string action = dto.Action.Trim().ToLowerInvariant();
            if (!Actions(r, c).Contains(action)) return Bad<RequestDto>("این کار در وضعیت فعلی درخواست یا برای شما مجاز نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            string comment = Clean(dto.Comment);
            OfRequestType type = await _uow.Set<OfRequestType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TypeId, ct);
            string label = $"{type?.Title} {r.Number}";
            string key = "req:" + r.Id;

            switch (action)
            {
                case "submit":
                    {
                        var (_, error) = CheckValues(ParseFields(type?.FieldsJson), JsonSerializer.Deserialize<Dictionary<string, string>>(r.ValuesJson ?? "{}", Json), true);
                        if (error != null) return Bad<RequestDto>(error);
                        List<int> chain = OfficeText.Ids(type?.ApprovalChain);
                        if (chain.Count == 0)
                        {
                            int? manager = await ManagerOfAsync(r.RequesterUserId, ct);
                            if (!manager.HasValue) return Bad<RequestDto>("مدیر واحد شما در چارت سازمانی مشخص نیست؛ با واحد اداری تماس بگیرید.");
                            chain.Add(manager.Value);
                        }
                        r.Approvals.Clear();
                        int step = 0;
                        foreach (int positionId in chain) r.Approvals.Add(new OfRequestApproval { Step = ++step, PositionId = positionId, Status = OfApprovalStatus.Pending });
                        r.Status = OfRequestStatus.InApproval; r.SubmittedAt = now;
                        AddInbox(key, OfInboxKind.Approval, $"تایید {label}", r, chain[0], null, null);
                        break;
                    }
                case "approve":
                    {
                        OfRequestApproval current = Current(r);
                        ActingPosition acting = (await OfficeActors.ActingAsync(_uow, Me, ct)).First(a => a.PositionId == current.PositionId);
                        current.Status = OfApprovalStatus.Approved; current.ActedByUserId = Me; current.OnBehalf = acting.OnBehalf; current.Comment = comment; current.ActedAt = now;
                        await CloseKeysAsync("تایید شد", ct, key);
                        OfRequestApproval next = Current(r);
                        if (next != null) AddInbox(key, OfInboxKind.Approval, $"تایید {label}", r, next.PositionId, null, null);
                        else
                        {
                            r.Status = OfRequestStatus.Approved;
                            AddInbox("req-complete:" + r.Id, OfInboxKind.Task, $"انجام {label}" + (string.IsNullOrEmpty(type?.TargetNote) ? "" : " - " + type.TargetNote), r, null, null, OfficeRoles.Hr);
                            AddInbox("req-result:" + r.Id, OfInboxKind.Alert, $"{label} تایید شد.", r, null, r.RequesterUserId, null);
                        }
                        break;
                    }
                case "reject":
                    {
                        if (comment == null) return Bad<RequestDto>("دلیل رد درخواست را بنویسید.");
                        OfRequestApproval current = Current(r);
                        ActingPosition acting = (await OfficeActors.ActingAsync(_uow, Me, ct)).First(a => a.PositionId == current.PositionId);
                        current.Status = OfApprovalStatus.Returned; current.ActedByUserId = Me; current.OnBehalf = acting.OnBehalf; current.Comment = comment; current.ActedAt = now;
                        r.Status = OfRequestStatus.Rejected;
                        await CloseKeysAsync("رد شد", ct, key);
                        AddInbox("req-result:" + r.Id, OfInboxKind.Alert, $"{label} رد شد: {comment}", r, null, r.RequesterUserId, null);
                        break;
                    }
                case "cancel":
                    r.Status = OfRequestStatus.Cancelled; r.Note = comment ?? r.Note;
                    await CloseKeysAsync("باطل شد", ct, key, "req-complete:" + r.Id);
                    break;
                case "complete":
                    r.Status = OfRequestStatus.Completed; r.CompletedAt = now; r.Note = comment ?? r.Note;
                    await CloseKeysAsync("انجام شد", ct, "req-complete:" + r.Id);
                    AddInbox("req-result:" + r.Id + ":done", OfInboxKind.Alert, $"{label} انجام شد.", r, null, r.RequesterUserId, null);
                    break;
                default:
                    return Bad<RequestDto>(OfficeText.InvalidData);
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<RequestDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<RequestDto>("ذخیره انجام نشد؛ دوباره تلاش کنید."); }
            return await GetRequestAsync(r.Id, ct);
        }
    }
}
