using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OpsDataDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.OpsData
{
    public interface IOpsDataQualityService
    {
        Task<ResponseDto<PagedList<OpsFindingDto>>> GetFindingsAsync(OpsFindingQuery query, CancellationToken ct);
        Task<ResponseDto<OpsFindingDto>> ResolveAsync(ResolveOpsFindingDto dto, CancellationToken ct);
        Task<ResponseDto<OpsSummaryDto>> SummaryAsync(OpsUnit? unit, CancellationToken ct);
        Task<ResponseDto<List<OpsRuleDto>>> RulesAsync(CancellationToken ct);

        /// <summary>The findings of one document as of now (live) and as stored.</summary>
        Task<ResponseDto<List<OpsFindingDto>>> EvaluateAsync(OpsEvaluateQuery query, CancellationToken ct);
    }

    public class OpsDataQualityService : IOpsDataQualityService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IOpsDataEvaluator _evaluator;
        private readonly IOpsGateService _gate;
        private readonly IHttpContextAccessor _http;

        public OpsDataQualityService(IUnitOfWork uow, IInventoryUserContext user, IOpsDataEvaluator evaluator, IOpsGateService gate, IHttpContextAccessor http)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _http = http;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => _user.UserId;
        private bool IsSeat => AiActing.IsActing || AiSeatNames.IsSeatUser(_http?.HttpContext?.User?.Identity?.Name);
        private bool Masked => !InventoryPriceAccess.SeesDocumentPrices(_user);

        public async Task<ResponseDto<PagedList<OpsFindingDto>>> GetFindingsAsync(OpsFindingQuery query, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(OpsRoles.Readers)) return Bad<PagedList<OpsFindingDto>>(OpsExceptionService.NotAllowed);
            query ??= new OpsFindingQuery();
            List<OpsUnit> units = OpsExceptionService.VisibleUnits(_user);
            IQueryable<OpsFinding> q = _uow.Set<OpsFinding>().AsNoTracking().Where(f => !f.Deleted && units.Contains(f.Unit));
            if (query.Unit.HasValue) q = q.Where(f => f.Unit == query.Unit.Value);
            if (!string.IsNullOrWhiteSpace(query.Rule)) { string rule = query.Rule.Trim(); q = q.Where(f => f.RuleCode == rule); }
            if (query.Severity.HasValue) q = q.Where(f => f.Severity == query.Severity.Value);
            if (query.Source.HasValue) q = q.Where(f => f.Source == query.Source.Value);
            if (!string.IsNullOrWhiteSpace(query.DocumentType)) { string t = query.DocumentType.Trim(); q = q.Where(f => f.DocumentType == t); }
            if (query.DocumentId.HasValue) q = q.Where(f => f.DocumentId == query.DocumentId.Value);
            if (query.From.HasValue) { DateTimeOffset from = new DateTimeOffset(query.From.Value.Date, TimeZoneInfo.Local.GetUtcOffset(query.From.Value.Date)); q = q.Where(f => f.DetectedAt >= from); }
            if (query.To.HasValue) { DateTime end = query.To.Value.Date.AddDays(1); DateTimeOffset to = new DateTimeOffset(end, TimeZoneInfo.Local.GetUtcOffset(end)); q = q.Where(f => f.DetectedAt < to); }
            switch ((query.State ?? "open").Trim().ToLowerInvariant())
            {
                case "all": break;
                case "resolved": q = q.Where(f => f.Status == OpsFindingStatus.Resolved || f.Status == OpsFindingStatus.Verified); break;
                case "superseded": q = q.Where(f => f.Status == OpsFindingStatus.Superseded); break;
                default: q = q.Where(f => f.Status == OpsFindingStatus.Open); break;
            }
            q = q.OrderByDescending(f => f.Id);
            int size = query.PageSize <= 0 ? 50 : query.PageSize;
            int page = Math.Max(1, query.PageNumber);
            int total = await q.CountAsync(ct);
            List<OpsFinding> rows = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.EnteredByUserId, r.ResolvedByUserId }), ct);
            List<int> exIds = rows.Where(r => r.ExceptionId.HasValue).Select(r => r.ExceptionId.Value).Distinct().ToList();
            Dictionary<int, OpsExceptionStatus> exStatus = exIds.Count == 0 ? new Dictionary<int, OpsExceptionStatus>()
                : await _uow.Set<OpsException>().AsNoTracking().Where(e => exIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Status, ct);
            bool masked = Masked;
            var dtos = rows.Select(r =>
            {
                OpsFindingDto d = OpsMap.ToDto(r, masked);
                d.EnteredByName = names.Name(r.EnteredByUserId);
                d.ResolvedByName = names.Name(r.ResolvedByUserId);
                if (r.ExceptionId.HasValue && exStatus.TryGetValue(r.ExceptionId.Value, out OpsExceptionStatus s)) d.ExceptionStatus = s;
                return d;
            }).ToList();
            PagedList<OpsFindingDto> list = await PagedList<OpsFindingDto>.CreateAsync(dtos, 1, 0, ct);
            list.TotalCount = total; list.CurrentPage = page; list.PageSize = size; list.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<OpsFindingDto>> ResolveAsync(ResolveOpsFindingDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeat) return Bad<OpsFindingDto>(OpsExceptionService.NotAllowed);
            if (dto == null || dto.Id <= 0) return Bad<OpsFindingDto>("اطلاعات ارسال‌شده کامل نیست.");
            OpsFinding f = await _uow.Set<OpsFinding>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (f == null) return ResponseDtoHelper.NotFound<OpsFindingDto>("یافته پیدا نشد.");
            if (f.Status != OpsFindingStatus.Open) return Bad<OpsFindingDto>("این یافته قبلاً بسته شده است.");
            string reason = (dto.Reason ?? string.Empty).Trim();
            if (reason.Length > 500) return Bad<OpsFindingDto>("توضیح نباید از ۵۰۰ نویسه بیشتر باشد.");
            string kind = (dto.Resolution ?? string.Empty).Trim().ToLowerInvariant();
            if (kind == "corrected")
            {
                if (!_user.IsInRole(OpsRoles.Makers(f.Unit) + "," + OpsRoles.Supervisors(f.Unit))) return Bad<OpsFindingDto>(OpsExceptionService.NotAllowed);
                if (!dto.CorrectiveDocumentId.HasValue || dto.CorrectiveDocumentId.Value <= 0) return Bad<OpsFindingDto>("سند اصلاحی را مشخص کنید.");
                if (f.DocumentType == OpsDocTypes.InventoryDocument)
                {
                    // the corrective document of the warehouse is the draft made from the cancelled one (R31)
                    bool linked = await _uow.Set<InventoryDocument>().AsNoTracking().AnyAsync(d => d.Id == dto.CorrectiveDocumentId.Value && !d.Deleted && d.CorrectsDocumentId == f.DocumentId && d.Status != InventoryDocumentStatus.Cancelled, ct);
                    if (!linked) return Bad<OpsFindingDto>("سند اصلاحی باید از روی همین سند (ابطال و صدور سند اصلاحی) ساخته شده باشد.");
                    InventoryDocumentStatus original = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == f.DocumentId).Select(d => d.Status).FirstOrDefaultAsync(ct);
                    if (original != InventoryDocumentStatus.Cancelled) return Bad<OpsFindingDto>("سند اصلی هنوز باطل نشده است.");
                }
                f.Status = OpsFindingStatus.Resolved;
                f.CorrectiveDocumentId = dto.CorrectiveDocumentId;
            }
            else if (kind == "verified")
            {
                if (!_user.IsInRole(OpsRoles.Supervisors(f.Unit))) return Bad<OpsFindingDto>(OpsExceptionService.NotAllowed + " (سرپرست واحد)");
                if (reason.Length < 5) return Bad<OpsFindingDto>("دلیل تأیید درستی داده را بنویسید.");
                OpsEvaluation ev = await _evaluator.EvaluateAsync(f.DocumentType, f.DocumentId, ct);
                HashSet<int> makers = ev?.Doc.Makers ?? new HashSet<int>();
                if (f.EnteredByUserId.HasValue) makers.Add(f.EnteredByUserId.Value);
                if (makers.Contains(Me)) return Bad<OpsFindingDto>(OpsExceptionService.SameUser + " (ثبت‌کننده‌ی سند درستی داده‌ی خودش را تأیید نمی‌کند)");
                f.Status = OpsFindingStatus.Verified;
            }
            else return Bad<OpsFindingDto>("نوع بستن باید «سند اصلاحی» (corrected) یا «تأیید درستی داده» (verified) باشد.");
            f.ResolvedAt = DateTimeOffset.Now; f.ResolvedByUserId = Me; f.ResolutionNote = reason.Length == 0 ? null : reason; f.ModifierUserId = Me; f.ModifyDate = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(OpsMap.ToDto(f, Masked));
        }

        public async Task<ResponseDto<OpsSummaryDto>> SummaryAsync(OpsUnit? unit, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(OpsRoles.Readers)) return Bad<OpsSummaryDto>(OpsExceptionService.NotAllowed);
            List<OpsUnit> units = OpsExceptionService.VisibleUnits(_user);
            if (unit.HasValue) units = units.Where(u => u == unit.Value).ToList();
            DateTimeOffset week = DateTimeOffset.Now.AddDays(-7);
            var open = await _uow.Set<OpsFinding>().AsNoTracking().Where(f => !f.Deleted && f.Status == OpsFindingStatus.Open && units.Contains(f.Unit))
                .GroupBy(f => new { f.Unit, f.RuleCode, f.Severity }).Select(g => new { g.Key.Unit, g.Key.RuleCode, g.Key.Severity, N = g.Count() }).ToListAsync(ct);
            var s = new OpsSummaryDto { AsOf = DateTime.Today, Link = "/ops-data/quality" };
            s.OpenFindings = open.Sum(o => o.N);
            s.OpenBlocks = open.Where(o => o.Severity == OpsSeverity.Block).Sum(o => o.N);
            s.OpenWarnings = s.OpenFindings - s.OpenBlocks;
            s.ByUnit = open.GroupBy(o => o.Unit).OrderBy(g => g.Key).Select(g => new OpsCountDto { Key = g.Key.ToString(), Title = OpsRoles.UnitTitle(g.Key), Count = g.Sum(x => x.N) }).ToList();
            s.ByRule = open.GroupBy(o => o.RuleCode).OrderByDescending(g => g.Sum(x => x.N)).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => new OpsCountDto { Key = g.Key, Title = OpsRuleCatalog.TitleOf(g.Key), Count = g.Sum(x => x.N) }).ToList();
            s.TopRules = s.ByRule.Take(3).ToList();
            s.ExceptionsThisWeek = await _uow.Set<OpsException>().AsNoTracking().CountAsync(e => !e.Deleted && units.Contains(e.Unit) && e.RequestedAt >= week, ct);
            s.PendingExceptions = await _uow.Set<OpsException>().AsNoTracking().CountAsync(e => !e.Deleted && units.Contains(e.Unit) && e.Status == OpsExceptionStatus.Pending, ct);
            s.ResolvedThisWeek = await _uow.Set<OpsFinding>().AsNoTracking().CountAsync(f => !f.Deleted && units.Contains(f.Unit) && f.ResolvedAt != null && f.ResolvedAt >= week, ct);
            return ResponseDtoHelper.Ok(s);
        }

        public async Task<ResponseDto<List<OpsRuleDto>>> RulesAsync(CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(OpsRoles.Readers)) return Bad<List<OpsRuleDto>>(OpsExceptionService.NotAllowed);
            IReadOnlyDictionary<string, decimal> st = await _evaluator.SettingsAsync(ct);
            return ResponseDtoHelper.Ok(OpsRuleCatalog.All.Select(r =>
            {
                OpsRuleSetting s = OpsRuleCatalog.SettingOf(st, r.Code);
                return new OpsRuleDto { Code = r.Code, Title = r.Title, Family = r.Family, Unit = r.Unit, Enabled = s.Enabled, Warn = s.Warn, Block = s.Block, MinHistory = s.MinHistory, Basis = r.Basis };
            }).ToList());
        }

        public async Task<ResponseDto<List<OpsFindingDto>>> EvaluateAsync(OpsEvaluateQuery query, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(OpsRoles.Readers + ",WAREHOUSE,WHSUPERVISOR")) return Bad<List<OpsFindingDto>>(OpsExceptionService.NotAllowed);
            if (query == null || string.IsNullOrWhiteSpace(query.DocumentType) || query.DocumentId <= 0) return Bad<List<OpsFindingDto>>("اطلاعات ارسال‌شده کامل نیست.");
            OpsEvaluation ev = await _evaluator.EvaluateAsync(query.DocumentType.Trim(), query.DocumentId, ct);
            if (ev == null) return ResponseDtoHelper.NotFound<List<OpsFindingDto>>("سند پیدا نشد.");
            return ResponseDtoHelper.Ok(ev.Doc.Confirmed ? await _gate.StoredAsync(ev.Doc.Type, ev.Doc.Id, ct) : await _gate.PreviewAsync(ev, ct));
        }
    }
}
