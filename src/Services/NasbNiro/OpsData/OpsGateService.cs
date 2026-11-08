using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OpsDataDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.OpsData
{
    public sealed class OpsGateResult
    {
        /// <summary>The Persian refusal when an uncovered Block remains; null when the entry may go on.</summary>
        public string Refusal { get; set; }
        public List<OpsFindingDto> Findings { get; set; } = new List<OpsFindingDto>();
        public bool Blocked => Refusal != null;
    }

    public interface IOpsGateService
    {
        /// <summary>
        /// The gate of one entry: stores the findings (a warning is kept with the document), looks for approved exceptions and answers whether the entry may go on. Pending changes stay in the unit of work
        /// (the confirmation saves them); a refusal saves at once, so the supervisor and the user can see what blocked.
        /// </summary>
        Task<OpsGateResult> CheckAsync(OpsEvaluation evaluation, CancellationToken ct);

        /// <summary>The findings as the form shows them while the user types: nothing is written.</summary>
        Task<List<OpsFindingDto>> PreviewAsync(OpsEvaluation evaluation, CancellationToken ct);

        /// <summary>The stored findings of one document (a confirmed document shows these).</summary>
        Task<List<OpsFindingDto>> StoredAsync(string documentType, int documentId, CancellationToken ct);
    }

    public interface IOpsExceptionService
    {
        Task<ResponseDto<OpsExceptionDto>> RequestAsync(RequestOpsExceptionDto dto, CancellationToken ct);
        Task<ResponseDto<OpsExceptionDto>> DecideAsync(DecideOpsExceptionDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<OpsExceptionDto>>> GetAsync(OpsExceptionQuery query, CancellationToken ct);
    }

    internal static class OpsMap
    {
        public static bool Same(decimal? a, decimal? b) => (!a.HasValue && !b.HasValue) || (a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) < 0.0001m);

        public static string Stamp(DateTimeOffset at)
        {
            DateTime local = at.LocalDateTime;
            return InventoryText.PersianDate(local) + " " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static OpsFindingDto ToDto(OpsFinding f, bool masked)
        {
            bool hide = masked && f.IsMoney;
            return new OpsFindingDto
            {
                Id = f.Id == 0 ? (int?)null : f.Id, Unit = f.Unit, UnitTitle = OpsRoles.UnitTitle(f.Unit), DocumentType = f.DocumentType, DocumentId = f.DocumentId, DocumentNumber = f.DocumentNumber,
                LineId = f.LineId, RowNumber = f.RowNumber, ItemId = f.ItemId, ItemTitle = f.ItemTitle, RuleCode = f.RuleCode, RuleTitle = OpsRuleCatalog.TitleOf(f.RuleCode), Severity = f.Severity,
                Source = f.Source, Status = f.Status, StatusTitle = OpsRoles.StatusTitle(f.Status), Message = hide ? (f.MessageMasked ?? f.Message) : f.Message,
                EnteredValue = hide ? null : f.EnteredValue, ReferenceValue = hide ? null : f.ReferenceValue, DeviationPercent = f.DeviationPercent, IsMoney = f.IsMoney, Masked = hide,
                DetectedAt = f.DetectedAt, DetectedAtText = OpsMap.Stamp(f.DetectedAt), ExceptionId = f.ExceptionId, ResolvedAt = f.ResolvedAt,
                ResolvedAtText = f.ResolvedAt.HasValue ? OpsMap.Stamp(f.ResolvedAt.Value) : null, ResolutionNote = f.ResolutionNote, CorrectiveDocumentId = f.CorrectiveDocumentId,
                Link = OpsDocTypes.Link(f.DocumentType, f.DocumentId),
            };
        }
    }

    public class OpsGateService : IOpsGateService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public OpsGateService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private bool Masked => !InventoryPriceAccess.SeesDocumentPrices(_user);

        /// <summary>A document the system makes can only be warned about: a Block is a Warning there.</summary>
        private static List<RuleFinding> Effective(OpsEvaluation ev)
        {
            var list = new List<RuleFinding>();
            foreach (RuleFinding f in ev.Findings)
            {
                if (!ev.Doc.Blockable && f.Severity == OpsSeverity.Block) f.Severity = OpsSeverity.Warning;
                list.Add(f);
            }
            return list;
        }

        public async Task<List<OpsFindingDto>> PreviewAsync(OpsEvaluation evaluation, CancellationToken ct)
        {
            var result = new List<OpsFindingDto>();
            if (evaluation == null) return result;
            List<RuleFinding> findings = Effective(evaluation);
            OpsDocRef doc = evaluation.Doc;
            // the stored finding of the same key gives the draft view its id (the form asks for the exception with it) and its exception
            List<OpsFinding> stored = findings.Count == 0 ? new List<OpsFinding>()
                : await _uow.Set<OpsFinding>().AsNoTracking().Where(f => !f.Deleted && f.DocumentType == doc.Type && f.DocumentId == doc.Id && f.Status != OpsFindingStatus.Superseded).ToListAsync(ct);
            List<OpsException> exceptions = findings.Any(f => f.Severity == OpsSeverity.Block)
                ? await _uow.Set<OpsException>().AsNoTracking().Where(e => !e.Deleted && e.DocumentType == doc.Type && e.DocumentId == doc.Id && e.Status != OpsExceptionStatus.Rejected && e.Status != OpsExceptionStatus.Voided).ToListAsync(ct)
                : new List<OpsException>();
            decimal margin = exceptions.Count == 0 ? 0m : await MarginAsync(ct);
            foreach (RuleFinding f in findings)
            {
                OpsFinding row = ToRow(doc, f, OpsSource.Gate);
                OpsFindingDto dto = OpsMap.ToDto(row, Masked);
                OpsFinding old = stored.FirstOrDefault(x => KeyOf(x) == f.Key);
                if (old != null) dto.Id = old.Id;
                OpsException ex = exceptions.FirstOrDefault(e => KeyOf(e) == f.Key && Covers(e, f, margin));
                if (ex != null) dto.ExceptionId = ex.Id;
                dto.ExceptionStatus = ex?.Status;
                result.Add(dto);
            }
            return result;
        }

        internal static string KeyOf(OpsFinding r) => OpsKey.Of(r.RowNumber, r.RowNumber.HasValue ? null : r.LineId, r.ItemId, r.RuleCode, r.Field);
        internal static string KeyOf(OpsException e) => OpsKey.Of(e.RowNumber, e.RowNumber.HasValue ? null : e.LineId, e.ItemId, e.RuleCode, e.Field);

        /// <summary>The exception covers the entry: the same value, or (time that runs on, like the hours of a running operation) not beyond the approved value plus the margin.</summary>
        internal static bool Covers(OpsException e, RuleFinding f, decimal marginPercent)
        {
            if (OpsMap.Same(e.EnteredValue, f.Entered)) return true;
            if (f.RuleCode == OpsRuleCodes.PrdTime && e.EnteredValue.HasValue && f.Entered.HasValue)
                return f.Entered.Value <= e.EnteredValue.Value * (1m + Math.Max(0m, marginPercent) / 100m);
            return false;
        }

        private async Task<decimal> MarginAsync(CancellationToken ct)
        {
            IReadOnlyDictionary<string, decimal> all = await global::Services.NasbNiro.Quality.QualitySettingKeys.LoadAsync(_uow, ct);
            return all != null && all.TryGetValue(OpsRuleCatalog.ExceptionMargin, out decimal m) ? m : 25m;
        }

        public async Task<List<OpsFindingDto>> StoredAsync(string documentType, int documentId, CancellationToken ct)
        {
            List<OpsFinding> rows = await _uow.Set<OpsFinding>().AsNoTracking().Where(f => !f.Deleted && f.DocumentType == documentType && f.DocumentId == documentId && f.Status != OpsFindingStatus.Superseded)
                .OrderBy(f => f.RowNumber).ThenBy(f => f.RuleCode).ToListAsync(ct);
            var statuses = await _uow.Set<OpsException>().AsNoTracking().Where(e => !e.Deleted && e.DocumentType == documentType && e.DocumentId == documentId).Select(e => new { e.Id, e.Status }).ToListAsync(ct);
            return rows.Select(r =>
            {
                OpsFindingDto d = OpsMap.ToDto(r, Masked);
                if (r.ExceptionId.HasValue) d.ExceptionStatus = statuses.FirstOrDefault(s => s.Id == r.ExceptionId.Value)?.Status;
                return d;
            }).ToList();
        }

        private OpsFinding ToRow(OpsDocRef doc, RuleFinding f, OpsSource source) => new OpsFinding
        {
            Unit = doc.Unit, DocumentType = doc.Type, DocumentId = doc.Id, DocumentNumber = doc.Number, LineId = f.RowNumber.HasValue ? null : f.LineId, RowNumber = f.RowNumber, ItemId = f.ItemId, ItemTitle = f.ItemTitle,
            Field = f.Field, RuleCode = f.RuleCode, Severity = f.Severity, Source = source, Status = OpsFindingStatus.Open, Message = Cut(f.Message, 1000), MessageMasked = Cut(f.MessageMasked, 1000),
            EnteredValue = f.Entered, ReferenceValue = f.Reference, DeviationPercent = f.DeviationPercent, IsMoney = f.IsMoney, DetectedAt = DateTimeOffset.Now,
        };

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        public async Task<OpsGateResult> CheckAsync(OpsEvaluation evaluation, CancellationToken ct)
        {
            var result = new OpsGateResult();
            if (evaluation == null) return result;
            OpsDocRef doc = evaluation.Doc;
            List<RuleFinding> findings = Effective(evaluation);
            DateTimeOffset now = DateTimeOffset.Now;
            int me = _user.UserId;

            List<OpsFinding> existing = await _uow.Set<OpsFinding>().Where(f => !f.Deleted && f.DocumentType == doc.Type && f.DocumentId == doc.Id).ToListAsync(ct);
            List<OpsException> exceptions = await _uow.Set<OpsException>().Where(e => !e.Deleted && e.DocumentType == doc.Type && e.DocumentId == doc.Id && (e.Status == OpsExceptionStatus.Approved || e.Status == OpsExceptionStatus.Pending)).ToListAsync(ct);

            var seen = new HashSet<OpsFinding>();
            var uncovered = new List<RuleFinding>();
            var selfApproved = new List<RuleFinding>();
            var rows = new List<(RuleFinding Rule, OpsFinding Row)>();
            decimal margin = exceptions.Count == 0 ? 0m : await MarginAsync(ct);
            foreach (RuleFinding f in findings)
            {
                OpsFinding row = existing.FirstOrDefault(e => KeyOf(e) == f.Key);
                if (row == null)
                {
                    row = ToRow(doc, f, OpsSource.Gate);
                    row.EnteredByUserId = me == 0 ? (int?)null : me;
                    row.CreatorUserId = me; row.ModifierUserId = me; row.CreateDate = now; row.ModifyDate = now;
                    _uow.Set<OpsFinding>().Add(row);
                    existing.Add(row);
                }
                else if (row.Status == OpsFindingStatus.Open || row.Status == OpsFindingStatus.Superseded)
                {
                    row.Status = OpsFindingStatus.Open; row.Severity = f.Severity; row.Message = Cut(f.Message, 1000); row.MessageMasked = Cut(f.MessageMasked, 1000);
                    row.EnteredValue = f.Entered; row.ReferenceValue = f.Reference; row.DeviationPercent = f.DeviationPercent; row.IsMoney = f.IsMoney; row.ItemId = f.ItemId; row.ItemTitle = f.ItemTitle;
                    row.RowNumber = f.RowNumber; row.DocumentNumber = doc.Number; row.EnteredByUserId = me == 0 ? row.EnteredByUserId : me; row.ModifierUserId = me; row.ModifyDate = now;
                }
                seen.Add(row);
                rows.Add((f, row));

                if (f.Severity != OpsSeverity.Block) continue;
                OpsException approved = exceptions.FirstOrDefault(e => e.Status == OpsExceptionStatus.Approved && KeyOf(e) == f.Key && Covers(e, f, margin));
                if (approved != null)
                {
                    // the supervisor who approved the exception is a second pair of eyes; he cannot also be the one who passes the entry
                    if (me != 0 && approved.DecidedByUserId == me) { selfApproved.Add(f); uncovered.Add(f); continue; }
                    row.ExceptionId = approved.Id;
                    if (approved.UsedAt == null) approved.UsedAt = now;
                    continue;
                }
                OpsException pending = exceptions.FirstOrDefault(e => e.Status == OpsExceptionStatus.Pending && KeyOf(e) == f.Key && Covers(e, f, margin));
                if (pending != null) row.ExceptionId = pending.Id;
                uncovered.Add(f);
            }
            // an approved exception whose value changed no longer covers the entry
            foreach (OpsException e in exceptions.Where(e => e.Status == OpsExceptionStatus.Approved && !findings.Any(f => f.Key == KeyOf(e) && Covers(e, f, margin))))
                if (findings.Any(f => f.Key == KeyOf(e))) e.Status = OpsExceptionStatus.Voided;
            // findings of this gate that no longer fire are superseded (only the rules this evaluation looks at: the production gate and the QC decision share one document)
            foreach (OpsFinding old in existing.Where(e => !seen.Contains(e) && e.Status == OpsFindingStatus.Open && e.Source == OpsSource.Gate && (evaluation.Scope == null || evaluation.Scope.Contains(e.RuleCode))))
            {
                old.Status = OpsFindingStatus.Superseded; old.ModifierUserId = me; old.ModifyDate = now;
            }

            if (uncovered.Count > 0)
            {
                var text = new StringBuilder();
                text.Append("کنترل بهنگام داده، ثبت را متوقف کرد (").Append(OpsDocTypes.Title(doc.Type)).Append(' ').Append(doc.Number).Append("):");
                foreach (RuleFinding f in uncovered.Take(8))
                    text.Append("\n• [").Append(f.RuleCode).Append("] ").Append(Masked && f.IsMoney ? f.MessageMasked ?? f.Message : f.Message);
                if (uncovered.Count > 8) text.Append("\n• و ").Append((uncovered.Count - 8).ToString(CultureInfo.InvariantCulture)).Append(" مورد دیگر");
                if (selfApproved.Count > 0) text.Append("\nتوجه: استثنای تأییدشده را شخص دیگری باید ثبت نهایی کند؛ تأییدکننده‌ی استثنا نمی‌تواند خودش سند را تأیید کند.");
                text.Append("\nاگر داده درست است، برای هر مورد «درخواست استثنا» با توجیه ثبت کنید و سرپرست واحد (غیر از ثبت‌کننده) آن را تأیید کند؛ وگرنه داده را اصلاح کنید.");
                result.Refusal = text.ToString();
                await _uow.SaveChangesAsync(ct);   // the blocking findings stay visible for the supervisor and for the exception request
            }
            bool masked = Masked;
            await StatusesAsync(rows, result, masked, ct);
            return result;
        }

        private async Task StatusesAsync(List<(RuleFinding Rule, OpsFinding Row)> rows, OpsGateResult result, bool masked, CancellationToken ct)
        {
            List<int> exIds = rows.Where(r => r.Row.ExceptionId.HasValue).Select(r => r.Row.ExceptionId.Value).Distinct().ToList();
            Dictionary<int, OpsExceptionStatus> st = exIds.Count == 0 ? new Dictionary<int, OpsExceptionStatus>()
                : await _uow.Set<OpsException>().AsNoTracking().Where(e => exIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Status, ct);
            foreach (var r in rows)
            {
                OpsFindingDto d = OpsMap.ToDto(r.Row, masked);
                if (r.Row.ExceptionId.HasValue && st.TryGetValue(r.Row.ExceptionId.Value, out OpsExceptionStatus s)) d.ExceptionStatus = s;
                result.Findings.Add(d);
            }
        }
    }

    // ====================================================================================================== exceptions

    public class OpsExceptionService : IOpsExceptionService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IOpsDataEvaluator _evaluator;
        private readonly INotificationService _notify;
        private readonly IHttpContextAccessor _http;

        public OpsExceptionService(IUnitOfWork uow, IInventoryUserContext user, IOpsDataEvaluator evaluator, INotificationService notify, IHttpContextAccessor http)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
            _http = http;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).";
        private int Me => _user.UserId;
        private bool IsSeat => AiActing.IsActing || AiSeatNames.IsSeatUser(_http?.HttpContext?.User?.Identity?.Name);

        public async Task<ResponseDto<OpsExceptionDto>> RequestAsync(RequestOpsExceptionDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeat) return Bad<OpsExceptionDto>(NotAllowed);
            if (dto == null || dto.FindingId <= 0) return Bad<OpsExceptionDto>("اطلاعات ارسال‌شده کامل نیست.");
            string why = (dto.Justification ?? string.Empty).Trim();
            if (why.Length < 10) return Bad<OpsExceptionDto>("توجیه استثنا را بنویسید (دست‌کم ۱۰ نویسه): چرا این داده با وجود اختلاف درست است؟");
            if (why.Length > 1000) return Bad<OpsExceptionDto>("توجیه نباید از ۱۰۰۰ نویسه بیشتر باشد.");
            OpsFinding f = await _uow.Set<OpsFinding>().FirstOrDefaultAsync(x => x.Id == dto.FindingId && !x.Deleted, ct);
            if (f == null) return ResponseDtoHelper.NotFound<OpsExceptionDto>("یافته پیدا نشد.");
            if (f.Severity != OpsSeverity.Block || f.Status != OpsFindingStatus.Open) return Bad<OpsExceptionDto>("استثنا فقط برای یافته‌ی مسدودکننده‌ی باز ثبت می‌شود.");
            if (!_user.IsInRole(OpsRoles.Makers(f.Unit) + "," + OpsRoles.Supervisors(f.Unit))) return Bad<OpsExceptionDto>(NotAllowed);
            List<OpsException> open = await _uow.Set<OpsException>().AsNoTracking().Where(e => !e.Deleted && e.DocumentType == f.DocumentType && e.DocumentId == f.DocumentId && e.Status == OpsExceptionStatus.Pending).ToListAsync(ct);
            if (open.Any(e => OpsGateService.KeyOf(e) == OpsGateService.KeyOf(f)))
                return Bad<OpsExceptionDto>("برای این مورد درخواست استثنای بازی وجود دارد؛ منتظر تصمیم سرپرست بمانید.");

            DateTimeOffset now = DateTimeOffset.Now;
            var ex = new OpsException
            {
                Unit = f.Unit, DocumentType = f.DocumentType, DocumentId = f.DocumentId, DocumentNumber = f.DocumentNumber, LineId = f.LineId, RowNumber = f.RowNumber, ItemId = f.ItemId, Field = f.Field, RuleCode = f.RuleCode,
                EnteredValue = f.EnteredValue, ReferenceValue = f.ReferenceValue, DeviationPercent = f.DeviationPercent, IsMoney = f.IsMoney, Message = f.Message, Justification = why,
                RequestedByUserId = Me, RequestedAt = now, Status = OpsExceptionStatus.Pending, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<OpsException>().Add(ex);
            try
            {
                await _uow.SaveChangesAsync(ct);
                f.ExceptionId = ex.Id;
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // a double click: the unique index of pending requests refused the second one
                return Bad<OpsExceptionDto>("برای این مورد درخواست استثنای بازی وجود دارد؛ منتظر تصمیم سرپرست بمانید.");
            }
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "ods:" + ex.Id, Module = "opsdata", SourceType = "ops-exception", SourceId = ex.Id, Kind = OfInboxKind.Approval, Priority = OfUrgency.Normal,
                Title = $"درخواست استثنا برای {OpsDocTypes.Title(f.DocumentType)} {f.DocumentNumber}: {OpsRuleCatalog.TitleOf(f.RuleCode)}", Link = "/ops-data/quality?tab=exceptions", Roles = OpsRoles.Supervisors(f.Unit),
            }, ct);
            return ResponseDtoHelper.Ok(await MapAsync(ex, ct));
        }

        public async Task<ResponseDto<OpsExceptionDto>> DecideAsync(DecideOpsExceptionDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeat) return Bad<OpsExceptionDto>(NotAllowed);
            if (dto == null || dto.Id <= 0) return Bad<OpsExceptionDto>("اطلاعات ارسال‌شده کامل نیست.");
            OpsException ex = await _uow.Set<OpsException>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (ex == null) return ResponseDtoHelper.NotFound<OpsExceptionDto>("درخواست استثنا پیدا نشد.");
            if (ex.Status != OpsExceptionStatus.Pending) return Bad<OpsExceptionDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            if (!_user.IsInRole(OpsRoles.Supervisors(ex.Unit))) return Bad<OpsExceptionDto>(NotAllowed + " (سرپرست واحد)");
            if (ex.RequestedByUserId == Me) return Bad<OpsExceptionDto>(SameUser);
            OpsEvaluation ev = await _evaluator.EvaluateAsync(ex.DocumentType, ex.DocumentId, ct);
            if (ev != null && ev.Doc.Makers.Contains(Me)) return Bad<OpsExceptionDto>(SameUser + " (ثبت‌کننده، ویرایشگر یا تأییدکننده‌ی سند نمی‌تواند استثنای آن را تأیید کند)");
            string reason = (dto.Reason ?? string.Empty).Trim();
            if (!dto.Approve && reason.Length == 0) return Bad<OpsExceptionDto>("دلیل رد را بنویسید.");
            if (reason.Length > 500) return Bad<OpsExceptionDto>("توضیح نباید از ۵۰۰ نویسه بیشتر باشد.");
            ex.Status = dto.Approve ? OpsExceptionStatus.Approved : OpsExceptionStatus.Rejected;
            ex.DecidedByUserId = Me; ex.DecidedAt = DateTimeOffset.Now; ex.DecisionReason = reason.Length == 0 ? null : reason; ex.ModifierUserId = Me; ex.ModifyDate = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<OpsExceptionDto>("همزمان شخص دیگری درباره این درخواست تصمیم گرفت؛ صفحه را تازه کنید."); }
            await _notify.CloseAsync("ods:" + ex.Id, dto.Approve ? "تأیید شد" : "رد شد", ct);
            return ResponseDtoHelper.Ok(await MapAsync(ex, ct));
        }

        public async Task<ResponseDto<PagedList<OpsExceptionDto>>> GetAsync(OpsExceptionQuery query, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(OpsRoles.Readers)) return Bad<PagedList<OpsExceptionDto>>(NotAllowed);
            query ??= new OpsExceptionQuery();
            IQueryable<OpsException> q = _uow.Set<OpsException>().AsNoTracking().Where(e => !e.Deleted);
            List<OpsUnit> units = VisibleUnits(_user);
            q = q.Where(e => units.Contains(e.Unit));
            if (query.Unit.HasValue) q = q.Where(e => e.Unit == query.Unit.Value);
            if (query.Status.HasValue) q = q.Where(e => e.Status == query.Status.Value);
            if (!string.IsNullOrWhiteSpace(query.DocumentType)) q = q.Where(e => e.DocumentType == query.DocumentType);
            if (query.DocumentId.HasValue) q = q.Where(e => e.DocumentId == query.DocumentId.Value);
            if (query.WaitingForMe)
            {
                List<OpsUnit> mine = Enum.GetValues(typeof(OpsUnit)).Cast<OpsUnit>().Where(u => _user.IsInRole(OpsRoles.Supervisors(u))).ToList();
                int me = Me;
                q = q.Where(e => e.Status == OpsExceptionStatus.Pending && e.RequestedByUserId != me && mine.Contains(e.Unit));
            }
            q = q.OrderByDescending(e => e.Id);
            int size = query.PageSize <= 0 ? 50 : query.PageSize;
            int page = Math.Max(1, query.PageNumber);
            int total = await q.CountAsync(ct);
            List<OpsException> rows = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
            var items = new List<OpsExceptionDto>();
            foreach (OpsException e in rows) items.Add(await MapAsync(e, ct));
            PagedList<OpsExceptionDto> list = await PagedList<OpsExceptionDto>.CreateAsync(items, 1, 0, ct);
            list.TotalCount = total; list.CurrentPage = page; list.PageSize = size; list.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(list);
        }

        /// <summary>The units whose findings the caller reads: his own unit (maker or supervisor) and, for planning and the management, all.</summary>
        public static List<OpsUnit> VisibleUnits(IInventoryUserContext user) =>
            Enum.GetValues(typeof(OpsUnit)).Cast<OpsUnit>().Where(u => user.IsInRole(OpsRoles.UnitReaders(u))).ToList();

        private async Task<OpsExceptionDto> MapAsync(OpsException e, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { e.RequestedByUserId, e.DecidedByUserId }, ct);
            bool masked = !InventoryPriceAccess.SeesDocumentPrices(_user) && e.IsMoney;
            return new OpsExceptionDto
            {
                Id = e.Id, Unit = e.Unit, UnitTitle = OpsRoles.UnitTitle(e.Unit), DocumentType = e.DocumentType, DocumentId = e.DocumentId, DocumentNumber = e.DocumentNumber, LineId = e.LineId, RowNumber = e.RowNumber,
                RuleCode = e.RuleCode, RuleTitle = OpsRuleCatalog.TitleOf(e.RuleCode), EnteredValue = masked ? null : e.EnteredValue, ReferenceValue = masked ? null : e.ReferenceValue, DeviationPercent = e.DeviationPercent,
                IsMoney = e.IsMoney, Masked = masked, Message = masked ? "مقدار مالی برای نقش شما محرمانه است." : e.Message, Justification = e.Justification, RequestedByUserId = e.RequestedByUserId,
                RequestedByName = names.Name(e.RequestedByUserId), RequestedAt = e.RequestedAt, RequestedAtText = OpsMap.Stamp(e.RequestedAt), Status = e.Status, StatusTitle = OpsRoles.ExceptionStatusTitle(e.Status),
                DecidedByName = names.Name(e.DecidedByUserId), DecidedAt = e.DecidedAt, DecidedAtText = e.DecidedAt.HasValue ? OpsMap.Stamp(e.DecidedAt.Value) : null, DecisionReason = e.DecisionReason, UsedAt = e.UsedAt,
                Link = OpsDocTypes.Link(e.DocumentType, e.DocumentId),
                CanDecide = e.Status == OpsExceptionStatus.Pending && e.RequestedByUserId != Me && !IsSeat && _user.IsInRole(OpsRoles.Supervisors(e.Unit)),
            };
        }
    }
}
