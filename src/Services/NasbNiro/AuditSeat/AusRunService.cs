using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    internal static class AusNames
    {
        public static async Task<Dictionary<int, string>> LoadAsync(IUnitOfWork uow, IEnumerable<int?> ids, CancellationToken ct)
        {
            List<int> list = ids.Where(i => i.HasValue && i.Value > 0).Select(i => i.Value).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = await uow.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.LastName, u.UserName }).ToListAsync(ct);
            return rows.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace((u.Name + " " + u.LastName).Trim()) ? u.UserName : (u.Name + " " + u.LastName).Trim());
        }

        public static string Of(Dictionary<int, string> names, int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : (id.HasValue ? "#" + id.Value : null);
    }

    public interface IAusRunService
    {
        /// <summary>On-demand run: AUDITOR, QA, PMO or the CEO (a person, never an AI seat).</summary>
        Task<ResponseDto<AusRunDto>> RunNowAsync(CancellationToken ct);

        /// <summary>The scheduled run of the AUDIT seat (and the tests): no role check, the caller is the seat.</summary>
        Task<AusRunDto> RunAsync(AusRunKind kind, int requestedByUserId, CancellationToken ct);

        Task<ResponseDto<List<AusRunDto>>> GetRunsAsync(int? limit, CancellationToken ct);
        Task<ResponseDto<AusRunDto>> GetRunAsync(int id, CancellationToken ct);
        Task<ResponseDto<List<AusRuleDto>>> GetRulesAsync(CancellationToken ct);

        /// <summary>Re-runs the rule of a finding over the whole data and tells whether the same object still shows the deviation (the automatic re-check).</summary>
        Task<bool> StillDeviatesAsync(AusFinding finding, CancellationToken ct);
    }

    /// <summary>
    /// The AUDIT seat's run: every enabled rule of the catalogue is executed with a time budget, its deviations become findings (once per rule and object while a finding is not closed), recurrences are linked and escalated,
    /// a major finding is told at once to the CEO, PMO and QA managers and the owner. Nothing is closed, dropped or deleted here; a finding that comes back while it is on its way to closure is reopened.
    /// </summary>
    public class AusRunService : IAusRunService
    {
        // one run at a time in the process; also the finding numbers are allocated here (the unique index is the guard across processes)
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IServiceProvider _services;

        public AusRunService(IUnitOfWork uow, IInventoryUserContext user, IServiceProvider services, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _services = services;
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        private bool CanRead => _user.IsInRole(AusRoles.Readers);

        public async Task<ResponseDto<AusRunDto>> RunNowAsync(CancellationToken ct)
        {
            if (AiActing.IsActing) return Bad<AusRunDto>("اجرای دستی ممیزی با انسان است؛ صندلی هوش مصنوعی آن را روزانه خودش اجرا می‌کند.");
            if (!_user.IsInRole(AusRoles.Runners)) return Bad<AusRunDto>(QualityText.NotAllowed);
            if (!await Gate.WaitAsync(0, ct)) return Bad<AusRunDto>("اجرای دیگری از ممیزی در جریان است؛ پس از پایان آن دوباره تلاش کنید.");
            try
            {
                AusRunDto dto = await RunCoreAsync(AusRunKind.OnDemand, _user.UserId, ct);
                return ResponseDtoHelper.Ok(dto);
            }
            finally { Gate.Release(); }
        }

        public async Task<AusRunDto> RunAsync(AusRunKind kind, int requestedByUserId, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { return await RunCoreAsync(kind, requestedByUserId, ct); }
            finally { Gate.Release(); }
        }

        private sealed class RuleResult { public string Code; public string Status; public int Hits; public int New; public int Ms; }

        private async Task<AusRunDto> RunCoreAsync(AusRunKind kind, int by, CancellationToken ct)
        {
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (st[AusSettingKeys.Master] == 0m) return new AusRunDto { Kind = kind.ToString(), Status = "Disabled", Message = "ممیز داخلی هوشمند خاموش است؛ این تصمیم باید دو نفره و ثبت‌شده باشد." };

            var run = new AusRun { Kind = kind, Status = AusRunStatus.Running, StartedAt = now, RequestedByUserId = by, CreateDate = now, ModifyDate = now };
            _uow.Set<AusRun>().Add(run);
            await _uow.SaveChangesAsync(ct);

            var results = new List<RuleResult>();
            int hits = 0, created = 0, recurrences = 0, failed = 0;
            Dictionary<string, AusRuleState> states = await _uow.Set<AusRuleState>().Where(s => !s.Deleted).ToDictionaryAsync(s => s.RuleCode, ct);
            int budget = (int)st[AusSettingKeys.RuleBudgetSeconds], max = (int)st[AusSettingKeys.MaxHits], fullDays = (int)st[AusSettingKeys.FullPassDays];
            var alerts = new List<AusFinding>();

            foreach (AusRuleDef rule in AusCatalog.All)
            {
                if (ct.IsCancellationRequested) break;
                if (st.TryGetValue(AusSettingKeys.RuleKey(rule.Code), out decimal on) && on == 0m) { results.Add(new RuleResult { Code = rule.Code, Status = "disabled" }); continue; }
                states.TryGetValue(rule.Code, out AusRuleState state);
                bool full = !rule.Incremental || state?.LastFullAt == null || state.LastRunAt == null || now - state.LastFullAt.Value > TimeSpan.FromDays(fullDays);
                var ctx = new AusContext { Uow = _uow, Services = _services, Today = now.LocalDateTime.Date, Now = now, Settings = st, Since = full ? (DateTimeOffset?)null : state.LastRunAt.Value.AddDays(-1), Max = max, Ct = ct };
                var sw = Stopwatch.StartNew();
                string status = "ok"; string error = null; List<AusHit> found = null;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(budget));
                    ctx.Ct = cts.Token;
                    try { found = await rule.Query(ctx) ?? new List<AusHit>(); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { status = "timeout"; error = "زمان مجاز قاعده (" + budget + " ثانیه) تمام شد."; }
                    catch (Exception ex) when (!(ex is OperationCanceledException)) { status = "error"; error = Cut(ex.GetType().Name + ": " + ex.Message, 500); }
                }
                if (status == "ok" && ctx.Truncated) status = "truncated";
                if (found == null) found = new List<AusHit>();
                int newForRule = 0;
                if (found.Count > 0)
                {
                    try { newForRule = await UpsertAsync(rule, found, run, st, now, alerts, ct); }
                    catch (DbUpdateException ex) { status = "error"; error = Cut("ذخیره یافته‌ها: " + ex.GetBaseException().Message, 500); DetachNew(); }
                }
                sw.Stop();
                hits += found.Count; created += newForRule;
                if (status != "ok") failed++;
                if (state == null) { state = new AusRuleState { RuleCode = rule.Code, CreateDate = now }; _uow.Set<AusRuleState>().Add(state); states[rule.Code] = state; }
                state.LastRunAt = now; state.LastDurationMs = (int)sw.ElapsedMilliseconds; state.LastHits = found.Count; state.LastStatus = status; state.LastError = error; state.TotalRuns++; state.ModifyDate = now;
                if (full && status != "timeout" && status != "error") state.LastFullAt = now;
                results.Add(new RuleResult { Code = rule.Code, Status = status, Hits = found.Count, New = newForRule, Ms = state.LastDurationMs });
                await _uow.SaveChangesAsync(ct);
            }

            // findings on their way to closure whose owner proposed the action: the re-check runs again (a failed immediate re-check left them in ActionProposed only when the rule could not run)
            int rechecked = 0, recheckFailed = 0;
            List<AusFinding> waiting = await _uow.Set<AusFinding>().Where(f => !f.Deleted && f.Status == AusFindingStatus.ActionProposed && f.Source == AusFindingSource.Rule).OrderBy(f => f.Id).Take(200).ToListAsync(ct);
            foreach (AusFinding f in waiting)
            {
                bool still;
                try { still = await StillDeviatesAsync(f, ct); } catch (Exception) { continue; }
                AusFindingFlow.ApplyRecheck(f, !still, now, run.Id, 0, still ? "بازبینی خودکار: انحراف هنوز دیده می‌شود." : "بازبینی خودکار: انحراف دیده نشد.", _uow);
                if (still) recheckFailed++; else rechecked++;
            }
            recurrences = await _uow.Set<AusFinding>().CountAsync(f => f.RunId == run.Id && f.RecurrenceOfId != null, ct);
            run.Status = failed > 0 ? AusRunStatus.Partial : AusRunStatus.Ok;
            run.FinishedAt = DateTimeOffset.Now; run.RulesRun = results.Count(r => r.Status != "disabled"); run.RulesFailed = failed; run.Hits = hits; run.NewFindings = created; run.Recurrences = recurrences; run.Rechecked = rechecked; run.RecheckFailed = recheckFailed;
            run.ResultsJson = JsonSerializer.Serialize(results.Select(r => new AusRunRuleDto { Code = r.Code, Status = r.Status, Hits = r.Hits, New = r.New, Ms = r.Ms }));
            run.Message = $"{run.RulesRun} قاعده اجرا شد؛ {hits} انحراف دیده شد، {created} یافته‌ی تازه، {recurrences} تکرار، {failed} قاعده ناقص.";
            run.ModifyDate = run.FinishedAt.Value;
            await _uow.SaveChangesAsync(ct);

            await AlertAsync(alerts, ct);
            return ToDto(run);
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        private void DetachNew()
        {
            foreach (var e in _uow.Set<AusFinding>().Local.Where(f => f.Id == 0).ToList()) _uow.Entry(e).State = EntityState.Detached;
            foreach (var e in _uow.Set<AusFindingEvent>().Local.Where(f => f.Id == 0).ToList()) _uow.Entry(e).State = EntityState.Detached;
        }

        /// <summary>The hits of one rule become findings: an open finding of the same object is only seen again; a new one is created, linked to an earlier closed one when it is a recurrence.</summary>
        private async Task<int> UpsertAsync(AusRuleDef rule, List<AusHit> found, AusRun run, IReadOnlyDictionary<string, decimal> st, DateTimeOffset now, List<AusFinding> alerts, CancellationToken ct)
        {
            // one finding per object key (a rule may report the same key twice)
            List<AusHit> hits = found.GroupBy(h => h.Key).Select(g => g.First()).ToList();
            List<string> keys = hits.Select(h => h.Key).ToList();
            var open = new Dictionary<string, AusFinding>();
            var closedBefore = new Dictionary<string, AusFinding>();
            for (int i = 0; i < keys.Count; i += 400)
            {
                List<string> chunk = keys.Skip(i).Take(400).ToList();
                foreach (AusFinding f in await _uow.Set<AusFinding>().Where(f => !f.Deleted && f.RuleCode == rule.Code && chunk.Contains(f.ObjectKey) && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided).ToListAsync(ct)) open[f.ObjectKey] = f;
                var prev = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && chunk.Contains(f.ObjectKey) && f.Status == AusFindingStatus.Closed).ToListAsync(ct);
                foreach (AusFinding f in prev.OrderBy(x => x.Id)) closedBefore[f.ObjectKey] = f;
            }
            int window = (int)(st.TryGetValue(AusSettingKeys.RecurrenceWindow, out decimal w) ? w : 90);
            AusFinding sameRule = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && f.Status == AusFindingStatus.Closed && f.ClosedAt != null && f.ClosedAt >= now.AddDays(-window)).OrderByDescending(f => f.Id).FirstOrDefaultAsync(ct);
            int year = AusRules.PersianYear(now.LocalDateTime);
            int seq = await _uow.Set<AusFinding>().Where(f => f.Year == year).Select(f => (int?)f.Sequence).MaxAsync(ct) ?? 0;
            int created = 0;
            var fresh = new List<(AusFinding F, string Note)>();
            foreach (AusHit h in hits)
            {
                if (open.TryGetValue(h.Key, out AusFinding ex))
                {
                    ex.LastSeenAt = now; ex.SeenCount++;
                    if (ex.Status == AusFindingStatus.Rechecked || ex.Status == AusFindingStatus.QaProposed || ex.Status == AusFindingStatus.PmoProposed)
                        AusFindingFlow.Reopen(ex, now, 0, "انحراف دوباره دیده شد (اجرای ممیزی)؛ زنجیره‌ی بستن از اول.", _uow);
                    continue;
                }
                AusSeverity baseSev = h.Severity ?? rule.Severity;
                AusSeverity sev = baseSev;
                AusFinding link = null;
                if (closedBefore.TryGetValue(h.Key, out AusFinding same)) link = same;
                else if (sameRule != null) link = sameRule;
                if (link != null) sev = AusRules.Raise((AusSeverity)Math.Max((int)link.Severity, (int)baseSev));
                seq++;
                string evidence = AusEvidence.Snapshot(rule, h, now, run.Id);
                var f = new AusFinding
                {
                    Number = AusRules.Number(year, seq), Year = year, Sequence = seq, Source = AusFindingSource.Rule, RuleCode = rule.Code, Standard = rule.Standard, Clause = rule.Clause, Procedure = rule.Procedure, ProcessCode = rule.ProcessCode,
                    Severity = sev, BaseSeverity = baseSev, Title = rule.Title, Description = h.Text, ObjectType = h.ObjectType, ObjectId = h.ObjectId, ObjectKey = h.Key, ObjectNumber = h.Number, Link = h.Link,
                    EvidenceJson = evidence, EvidenceSha256 = AusEvidence.Sha256Hex(evidence), OwnerUserId = h.OwnerUserId, OwnerRole = h.OwnerRole ?? rule.ResolveOwnerRole(), OwnerUnit = rule.ResolveOwnerUnit(),
                    DueDate = AusRules.DueDate(now.LocalDateTime, sev, st), DetectedAt = now, RunId = run.Id, LastSeenAt = now, SeenCount = 1, Status = AusFindingStatus.Open,
                    RecurrenceOfId = link?.Id, RecurrenceCount = link == null ? 0 : link.RecurrenceCount + 1, CreateDate = now, ModifyDate = now,
                };
                _uow.Set<AusFinding>().Add(f);
                fresh.Add((f, link == null ? "یافته‌ی تازه از قاعده‌ی " + rule.Code : "تکرار؛ پیوند به " + link.Number + " و ارتقای شدت"));
                created++;
                if (sev == AusSeverity.Major) alerts.Add(f);
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var (nf, note) in fresh) AusFindingFlow.Event(_uow, nf, now, "detected", 0, AusFindingStatus.Open, AusFindingStatus.Open, note);
            if (fresh.Count > 0) await _uow.SaveChangesAsync(ct);
            return created;
        }

        /// <summary>The immediate in-app alert of a major finding: CEO, PMO and QA managers and the owner.</summary>
        private async Task AlertAsync(List<AusFinding> majors, CancellationToken ct)
        {
            if (_notify == null) return;
            foreach (AusFinding f in majors)
            {
                var req = new NotifyRequest
                {
                    SourceKey = "aus:" + f.Id, Module = "AUDIT", SourceType = "AusFinding", SourceId = f.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Immediate, DueDate = f.DueDate,
                    Title = $"یافته‌ی عمده‌ی ممیزی {f.Number}: {f.Title}" + (f.ObjectNumber != null ? " (" + f.ObjectNumber + ")" : ""), Link = "/audit/findings/" + f.Id, Roles = AusRoles.Qa + "," + AusRoles.Pmo + "," + AusRoles.Ceo,
                };
                if (f.OwnerUserId.HasValue) req.UserIds.Add(f.OwnerUserId.Value);
                await _notify.NotifyAsync(req, ct);
            }
        }

        public async Task<bool> StillDeviatesAsync(AusFinding finding, CancellationToken ct)
        {
            AusRuleDef rule = AusCatalog.Find(finding.RuleCode);
            if (rule == null) return true;
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var ctx = new AusContext { Uow = _uow, Services = _services, Today = now.LocalDateTime.Date, Now = now, Settings = st, Max = (int)st[AusSettingKeys.MaxHits], Ct = ct };
            List<AusHit> found = await rule.Query(ctx) ?? new List<AusHit>();
            // an incomplete pass cannot prove that the deviation is gone
            if (ctx.Truncated) return true;
            return found.Any(h => h.Key == finding.ObjectKey);
        }

        public static AusRunDto ToDto(AusRun r)
        {
            var d = new AusRunDto
            {
                Id = r.Id, Kind = r.Kind == AusRunKind.Daily ? "روزانه" : "دستی", Status = r.Status == AusRunStatus.Ok ? "موفق" : r.Status == AusRunStatus.Partial ? "ناقص" : r.Status == AusRunStatus.Failed ? "ناموفق" : "در حال اجرا", StatusValue = (int)r.Status,
                StartedAtText = Stamp(r.StartedAt), FinishedAtText = r.FinishedAt.HasValue ? Stamp(r.FinishedAt.Value) : null, RulesRun = r.RulesRun, RulesFailed = r.RulesFailed, Hits = r.Hits, NewFindings = r.NewFindings, Recurrences = r.Recurrences,
                Rechecked = r.Rechecked, RecheckFailed = r.RecheckFailed, Message = r.Message,
            };
            if (!string.IsNullOrEmpty(r.ResultsJson))
                try { d.Rules = JsonSerializer.Deserialize<List<AusRunRuleDto>>(r.ResultsJson) ?? new List<AusRunRuleDto>(); } catch (JsonException) { }
            return d;
        }

        public static string Stamp(DateTimeOffset t) => AusEvidence.Day(t.LocalDateTime) + " " + t.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);

        public async Task<ResponseDto<List<AusRunDto>>> GetRunsAsync(int? limit, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<AusRunDto>>(QualityText.NotAllowed);
            int n = Math.Min(Math.Max(limit ?? 50, 1), 500);
            List<AusRun> rows = await _uow.Set<AusRun>().AsNoTracking().Where(r => !r.Deleted).OrderByDescending(r => r.Id).Take(n).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => { AusRunDto d = ToDto(r); d.Rules = new List<AusRunRuleDto>(); return d; }).ToList());
        }

        public async Task<ResponseDto<AusRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<AusRunDto>(QualityText.NotAllowed);
            AusRun r = await _uow.Set<AusRun>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            return r == null ? QualityText.NotFound<AusRunDto>("اجرا پیدا نشد.") : ResponseDtoHelper.Ok(ToDto(r));
        }

        public async Task<ResponseDto<List<AusRuleDto>>> GetRulesAsync(CancellationToken ct)
        {
            if (!CanRead) return Bad<List<AusRuleDto>>(QualityText.NotAllowed);
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            Dictionary<string, AusRuleState> states = await _uow.Set<AusRuleState>().AsNoTracking().Where(s => !s.Deleted).ToDictionaryAsync(s => s.RuleCode, ct);
            var counts = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided).GroupBy(f => f.RuleCode).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            Dictionary<string, int> open = counts.ToDictionary(c => c.Key, c => c.N);
            var list = new List<AusRuleDto>();
            foreach (AusRuleDef r in AusCatalog.All)
            {
                states.TryGetValue(r.Code, out AusRuleState s);
                list.Add(new AusRuleDto
                {
                    Code = r.Code, Title = r.Title, Standard = r.Standard, Clause = r.Clause, Procedure = r.Procedure, ProcessCode = r.ProcessCode, ProcessTitle = AusProcesses.TitleOf(r.ProcessCode), Severity = AusRules.SeverityText(r.Severity), SeverityValue = (int)r.Severity,
                    Incremental = r.Incremental, Enabled = !(st.TryGetValue(AusSettingKeys.RuleKey(r.Code), out decimal on) && on == 0m) && st[AusSettingKeys.Master] != 0m, SettingKey = AusSettingKeys.RuleKey(r.Code), OwnerRole = r.ResolveOwnerRole(),
                    LastRunAtText = s?.LastRunAt != null ? Stamp(s.LastRunAt.Value) : null, LastStatus = s?.LastStatus, LastHits = s?.LastHits ?? 0, LastDurationMs = s?.LastDurationMs ?? 0, TotalRuns = s?.TotalRuns ?? 0, OpenFindings = open.TryGetValue(r.Code, out int n) ? n : 0,
                });
            }
            return ResponseDtoHelper.Ok(list);
        }
    }
}
