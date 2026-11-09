using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        /// <summary>On-demand run: AUDITOR, QA, PMO or the CEO (a person, never an AI seat). The run is queued and executed in the background; the answer carries the run id (status "Running") and the caller polls GET runs/{id}.</summary>
        Task<ResponseDto<AusRunDto>> RunNowAsync(CancellationToken ct);

        /// <summary>The scheduled run of the AUDIT seat (and the tests): synchronous, no role check, the caller is the seat.</summary>
        Task<AusRunDto> RunAsync(AusRunKind kind, int requestedByUserId, CancellationToken ct);

        /// <summary>Executes a run row that <see cref="RunNowAsync"/> created (called by the background task).</summary>
        Task ExecuteQueuedAsync(int runId, int requestedByUserId, CancellationToken ct);

        Task<ResponseDto<List<AusRunDto>>> GetRunsAsync(int? limit, CancellationToken ct);
        Task<ResponseDto<AusRunDto>> GetRunAsync(int id, CancellationToken ct);
        Task<ResponseDto<List<AusRuleDto>>> GetRulesAsync(CancellationToken ct);

        /// <summary>
        /// The automatic re-check: a State rule evaluates exactly the finding's object again (the focus is pushed into SQL, so no ceiling can hide it); an Event rule (a past, immutable event) passes when the owner's action is recorded
        /// and no new occurrence of the rule has been found for the same object or owner since the action.
        /// </summary>
        Task<bool> StillDeviatesAsync(AusFinding finding, CancellationToken ct);
    }

    /// <summary>
    /// The AUDIT seat's run: every enabled rule of the catalogue is executed with a time budget, its deviations become findings (once per rule and object while a finding is not closed), recurrences are linked and escalated,
    /// a major finding is told at once (one consolidated alert per run) to the CEO, PMO and QA managers and the owner. Nothing is closed, dropped or deleted here; a finding that comes back while it is on its way to closure is reopened.
    /// A pass that the time budget or the hit ceiling cuts leaves a checkpoint and continues at the next run; closed and voided findings are suppressed by their rule and object key; events before the audit horizon are one backlog finding per rule.
    /// </summary>
    public class AusRunService : IAusRunService
    {
        // one run at a time in the process; also the finding numbers are allocated here (the unique index is the guard across processes)
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        /// <summary>A Running row older than this is a run that died with its process: it is written off as failed.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

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
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            if (st[AusSettingKeys.Master] == 0m) return ResponseDtoHelper.Ok(new AusRunDto { Kind = "دستی", Status = "Disabled", Message = "ممیز داخلی هوشمند خاموش است؛ این تصمیم باید دو نفره و ثبت‌شده باشد." });
            DateTimeOffset now = DateTimeOffset.Now;
            // a run that died with its process is written off; a live one blocks a second run
            List<AusRun> running = await _uow.Set<AusRun>().Where(r => !r.Deleted && r.Status == AusRunStatus.Running).ToListAsync(ct);
            foreach (AusRun old in running.Where(r => now - r.StartedAt > StaleAfter)) { old.Status = AusRunStatus.Failed; old.FinishedAt = now; old.Message = "اجرا پایان نیافت و ناتمام ثبت شد."; old.ModifyDate = now; }
            if (running.Any(r => now - r.StartedAt <= StaleAfter) || Gate.CurrentCount == 0)
            {
                if (_uow.Set<AusRun>().Local.Any(r => r.Status == AusRunStatus.Failed && r.FinishedAt == now)) await _uow.SaveChangesAsync(ct);
                return Bad<AusRunDto>("اجرای دیگری از ممیزی در جریان است؛ پس از پایان آن دوباره تلاش کنید.");
            }
            var run = new AusRun { Kind = AusRunKind.OnDemand, Status = AusRunStatus.Running, StartedAt = now, RequestedByUserId = _user.UserId, Message = "در صف اجرا", CreateDate = now, ModifyDate = now };
            _uow.Set<AusRun>().Add(run);
            await _uow.SaveChangesAsync(ct);

            int runId = run.Id, by = _user.UserId;
            ClaimsPrincipal principal = _services?.GetService<IHttpContextAccessor>()?.HttpContext?.User;
            IServiceScopeFactory scopes = _services?.GetService<IServiceScopeFactory>();
            if (scopes == null) { await ExecuteQueuedAsync(runId, by, ct); return await GetRunAsync(runId, ct); }
            // the background task does not inherit the request: it opens a scope of its own with the same user
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (IServiceScope scope = scopes.CreateScope())
                        {
                            IHttpContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                            accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal ?? new ClaimsPrincipal() };
                            await scope.ServiceProvider.GetRequiredService<IAusRunService>().ExecuteQueuedAsync(runId, by, CancellationToken.None);
                        }
                    }
                    catch (Exception) { /* the run row is closed by ExecuteQueuedAsync itself; nothing may escape a background task */ }
                });
            }
            return ResponseDtoHelper.Ok(ToDto(run));
        }

        public async Task ExecuteQueuedAsync(int runId, int requestedByUserId, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try
            {
                AusRun run = await _uow.Set<AusRun>().FirstOrDefaultAsync(r => r.Id == runId && !r.Deleted, ct);
                if (run == null || run.Status != AusRunStatus.Running) return;
                await RunCoreAsync(run.Kind, requestedByUserId, run, ct);
            }
            finally { Gate.Release(); }
        }

        public async Task<AusRunDto> RunAsync(AusRunKind kind, int requestedByUserId, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { return await RunCoreAsync(kind, requestedByUserId, null, ct); }
            finally { Gate.Release(); }
        }

        private sealed class RuleResult { public string Code; public string Status; public int Hits; public int New; public int Ms; public int Accepted; public int Backlog; public int Suppressed; }

        private sealed class RunTally
        {
            public int Created, Systemic, Recurrences, Accepted, Suppressed, Backlog, Escalated;
            public readonly List<AusFinding> Majors = new List<AusFinding>();
            public readonly List<AusFinding> Escalations = new List<AusFinding>();
        }

        private async Task<AusRunDto> RunCoreAsync(AusRunKind kind, int by, AusRun run, CancellationToken ct)
        {
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (st[AusSettingKeys.Master] == 0m)
            {
                if (run != null) { run.Status = AusRunStatus.Failed; run.FinishedAt = now; run.Message = "ممیز داخلی هوشمند خاموش است."; run.ModifyDate = now; await _uow.SaveChangesAsync(CancellationToken.None); }
                return new AusRunDto { Kind = kind.ToString(), Status = "Disabled", Message = "ممیز داخلی هوشمند خاموش است؛ این تصمیم باید دو نفره و ثبت‌شده باشد." };
            }

            if (run == null)
            {
                run = new AusRun { Kind = kind, Status = AusRunStatus.Running, StartedAt = now, RequestedByUserId = by, CreateDate = now, ModifyDate = now };
                _uow.Set<AusRun>().Add(run);
                await _uow.SaveChangesAsync(ct);
            }

            var results = new List<RuleResult>();
            var tally = new RunTally();
            int hits = 0, failed = 0, rechecked = 0, recheckFailed = 0;
            string fatal = null;
            try
            {
                Dictionary<string, AusRuleState> states = await _uow.Set<AusRuleState>().Where(s => !s.Deleted).ToDictionaryAsync(s => s.RuleCode, ct);
                int budget = (int)st[AusSettingKeys.RuleBudgetSeconds], max = (int)st[AusSettingKeys.MaxHits], fullDays = (int)st[AusSettingKeys.FullPassDays];
                DateTime horizon = await HorizonAsync(st, now, ct);
                HashSet<string> accepted = await ActiveAcceptancesAsync(now, ct);

                foreach (AusRuleDef rule in AusCatalog.All)
                {
                    if (ct.IsCancellationRequested) break;
                    if (st.TryGetValue(AusSettingKeys.RuleKey(rule.Code), out decimal on) && on == 0m) { results.Add(new RuleResult { Code = rule.Code, Status = "disabled" }); continue; }
                    states.TryGetValue(rule.Code, out AusRuleState state);
                    bool resume = state != null && state.CursorId > 0;
                    bool fullPass = !rule.Incremental || state?.LastFullAt == null || state.LastRunAt == null || now - state.LastFullAt.Value > TimeSpan.FromDays(fullDays);
                    DateTimeOffset? since = resume ? state.CursorSince : (fullPass ? (DateTimeOffset?)null : state.LastRunAt.Value.AddDays(-1));
                    var ctx = new AusContext { Uow = _uow, Services = _services, Today = now.LocalDateTime.Date, Now = now, Settings = st, Since = since, Max = max, Cursor = resume ? state.CursorId : 0, Ct = ct, Deadline = DateTimeOffset.Now.AddSeconds(budget * 0.8) };
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
                    if (found == null) found = new List<AusHit>();
                    if (status == "ok" && ctx.Continued) status = ctx.NextCursor > 0 ? "continued" : "truncated";
                    var rr = new RuleResult { Code = rule.Code };
                    int createdBefore = tally.Created;
                    if (found.Count > 0)
                    {
                        try
                        {
                            if (rule.Code == "AUD-SYS-002") await EscalateAsync(found, tally, now, ct);
                            else await UpsertAsync(rule, found, run, st, now, horizon, accepted, tally, rr, ct);
                        }
                        catch (DbUpdateException ex) { status = "error"; error = Cut("ذخیره یافته‌ها: " + ex.GetBaseException().Message, 500); DetachPending(); }
                    }
                    sw.Stop();
                    hits += found.Count;
                    if (status == "error" || status == "timeout" || status == "truncated") failed++;
                    if (state == null) { state = new AusRuleState { RuleCode = rule.Code, CreateDate = now }; _uow.Set<AusRuleState>().Add(state); states[rule.Code] = state; }
                    state.LastRunAt = now; state.LastDurationMs = (int)sw.ElapsedMilliseconds; state.LastHits = found.Count; state.LastStatus = status; state.LastError = error; state.TotalRuns++; state.ModifyDate = now;
                    if (status == "continued") { state.CursorId = ctx.NextCursor; state.CursorSince = since; }
                    else if (status != "error" && status != "timeout") { state.CursorId = 0; state.CursorSince = null; }
                    if (fullPass && !resume && status != "timeout" && status != "error" && status != "continued") state.LastFullAt = now;
                    else if (resume && status == "ok" && since == null) state.LastFullAt = now;
                    rr.Status = status; rr.Hits = found.Count; rr.New = tally.Created - createdBefore; rr.Ms = (int)sw.ElapsedMilliseconds;
                    results.Add(rr);
                    await _uow.SaveChangesAsync(ct);
                }

                // findings whose owner proposed the action and whose automatic re-check could not run then: the re-check runs again
                List<AusFinding> waiting = await _uow.Set<AusFinding>().Where(f => !f.Deleted && f.Status == AusFindingStatus.ActionProposed && f.Source == AusFindingSource.Rule).OrderBy(f => f.Id).Take(200).ToListAsync(ct);
                foreach (AusFinding f in waiting)
                {
                    bool still;
                    try { still = await StillDeviatesAsync(f, ct); } catch (Exception ex) when (!(ex is OperationCanceledException)) { continue; }
                    AusFindingFlow.ApplyRecheck(f, !still, now, run.Id, 0, still ? "بازبینی خودکار: انحراف هنوز دیده می‌شود." : "بازبینی خودکار: انحراف دیده نشد.", _uow);
                    if (still) recheckFailed++; else rechecked++;
                }
                tally.Recurrences = await _uow.Set<AusFinding>().CountAsync(f => f.RunId == run.Id && f.RecurrenceOfId != null && f.ObjectType != "Systemic", ct);   // a systemic finding is linked to its objects but it is no recurrence
                tally.Systemic = await _uow.Set<AusFinding>().CountAsync(f => f.RunId == run.Id && f.ObjectType == "Systemic", ct);
                await _uow.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) { fatal = "اجرا لغو شد."; }
            catch (Exception ex) { fatal = Cut("اجرا با خطا پایان یافت: " + ex.GetType().Name + ": " + ex.Message, 500); DetachPending(); }
            finally
            {
                // the run row always ends, also after a cancellation or an exception (a "Running" row would block the next on-demand run)
                try
                {
                    run.Status = fatal != null ? AusRunStatus.Failed : (failed > 0 ? AusRunStatus.Partial : AusRunStatus.Ok);
                    run.FinishedAt = DateTimeOffset.Now; run.RulesRun = results.Count(r => r.Status != "disabled"); run.RulesFailed = failed; run.Hits = hits; run.NewFindings = tally.Created; run.Recurrences = tally.Recurrences; run.Rechecked = rechecked; run.RecheckFailed = recheckFailed;
                    run.ResultsJson = JsonSerializer.Serialize(results.Select(r => new AusRunRuleDto { Code = r.Code, Status = r.Status, Hits = r.Hits, New = r.New, Ms = r.Ms, Accepted = r.Accepted, Backlog = r.Backlog, Suppressed = r.Suppressed }));
                    run.Message = fatal ?? $"{run.RulesRun} قاعده اجرا شد؛ {hits} انحراف دیده شد، {tally.Created} یافته‌ی تازه (شامل {tally.Systemic} فراگیر)، {tally.Recurrences} تکرار، {tally.Accepted} پذیرفته‌شده‌ی موقت، {tally.Suppressed} بسته/باطل‌شده‌ی نادیده، {failed} قاعده ناقص.";
                    run.ModifyDate = run.FinishedAt.Value;
                    DetachPending();
                    await _uow.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception) { /* the next on-demand run writes off a Running row older than three hours */ }
            }

            await AlertAsync(run, tally, ct);
            return ToDto(run);
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        /// <summary>Forgets every finding or event that was added or changed but not saved (after a failed save), so that the next SaveChanges does not throw again and the run can end.</summary>
        private void DetachPending()
        {
            foreach (AusFinding e in _uow.Set<AusFinding>().Local.ToList()) { var en = _uow.Entry(e); if (en.State == EntityState.Added || en.State == EntityState.Modified || en.State == EntityState.Deleted) en.State = EntityState.Detached; }
            foreach (AusFindingEvent e in _uow.Set<AusFindingEvent>().Local.ToList()) { var en = _uow.Entry(e); if (en.State == EntityState.Added || en.State == EntityState.Modified || en.State == EntityState.Deleted) en.State = EntityState.Detached; }
        }

        /// <summary>The audit horizon: the setting (a Shamsi day), else the day of the first audit run. Events before it are one backlog finding per rule.</summary>
        private async Task<DateTime> HorizonAsync(IReadOnlyDictionary<string, decimal> st, DateTimeOffset now, CancellationToken ct)
        {
            DateTime? set = global::Services.NasbNiro.ProductBank.DesignGate.ShamsiDay(st.TryGetValue(AusSettingKeys.HorizonDate, out decimal h) ? h : 0m);
            if (set.HasValue) return set.Value.Date;
            DateTimeOffset? first = await _uow.Set<AusRun>().AsNoTracking().Where(r => !r.Deleted).OrderBy(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(ct);
            return (first ?? now).LocalDateTime.Date;
        }

        internal static string AcceptanceKey(string rule, string key) => rule + "\u001f" + key;

        private async Task<HashSet<string>> ActiveAcceptancesAsync(DateTimeOffset now, CancellationToken ct)
        {
            DateTime today = now.LocalDateTime.Date;
            var rows = await _uow.Set<AusAcceptance>().AsNoTracking().Where(a => !a.Deleted && a.Status == AusAcceptanceStatus.Active && a.ExpiresOn >= today).Select(a => new { a.RuleCode, a.ObjectKey }).ToListAsync(ct);
            return new HashSet<string>(rows.Select(a => AcceptanceKey(a.RuleCode, a.ObjectKey)), StringComparer.Ordinal);
        }

        /// <summary>AUD-SYS-002 does not open a second finding: the overdue finding itself is escalated once (EscalatedAt, an event) and the managers are told in the consolidated alert of the run.</summary>
        private async Task EscalateAsync(List<AusHit> found, RunTally tally, DateTimeOffset now, CancellationToken ct)
        {
            List<int> ids = found.Where(h => h.ObjectType == "AusFinding").Select(h => h.ObjectId).Distinct().ToList();
            if (ids.Count == 0) return;
            List<AusFinding> rows = await _uow.Set<AusFinding>().Where(f => ids.Contains(f.Id) && !f.Deleted && f.EscalatedAt == null && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided).ToListAsync(ct);
            foreach (AusFinding f in rows)
            {
                f.EscalatedAt = now; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "escalated", 0, f.Status, f.Status, "از مهلت گذشت؛ به QA، PMO و مدیرعامل گزارش شد (" + AusRules.SeverityText(f.Severity) + ").");
                tally.Escalations.Add(f); tally.Escalated++;
            }
            await _uow.SaveChangesAsync(ct);
        }

        /// <summary>The hits of one rule become findings. See the class summary for the rules of suppression, recurrence, backlog and the systemic finding.</summary>
        private async Task UpsertAsync(AusRuleDef rule, List<AusHit> found, AusRun run, IReadOnlyDictionary<string, decimal> st, DateTimeOffset now, DateTime horizon, HashSet<string> accepted, RunTally tally, RuleResult rr, CancellationToken ct)
        {
            // one finding per object key (a rule may report the same key twice)
            List<AusHit> all = found.GroupBy(h => h.Key).Select(g => g.First()).ToList();
            // a time-boxed accepted deviation (three persons) is visible in the reports but is not a finding
            List<AusHit> hits = all.Where(h => !accepted.Contains(AcceptanceKey(rule.Code, h.Key))).ToList();
            rr.Accepted = all.Count - hits.Count; tally.Accepted += rr.Accepted;
            bool eventRule = rule.Mode == AusRecheckMode.Event;
            List<AusHit> backlog = eventRule ? hits.Where(h => h.InBacklog || (h.EventDate.HasValue && h.EventDate.Value.Date < horizon)).ToList() : hits.Where(h => h.InBacklog).ToList();
            if (backlog.Count > 0) hits = hits.Except(backlog).ToList();

            List<string> keys = hits.Select(h => h.Key).ToList();
            var open = new Dictionary<string, AusFinding>();
            var closedBefore = new Dictionary<string, AusFinding>();
            var voided = new HashSet<string>();
            for (int i = 0; i < keys.Count; i += 400)
            {
                List<string> chunk = keys.Skip(i).Take(400).ToList();
                foreach (AusFinding f in await _uow.Set<AusFinding>().Where(f => !f.Deleted && f.RuleCode == rule.Code && chunk.Contains(f.ObjectKey) && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided).ToListAsync(ct)) open[f.ObjectKey] = f;
                var prev = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && chunk.Contains(f.ObjectKey) && (f.Status == AusFindingStatus.Closed || f.Status == AusFindingStatus.Voided)).Select(f => new { f.Id, f.ObjectKey, f.Status, f.Severity, f.RecurrenceCount, f.Number }).ToListAsync(ct);
                foreach (var f in prev.OrderBy(x => x.Id))
                {
                    if (f.Status == AusFindingStatus.Voided) voided.Add(f.ObjectKey);
                    else closedBefore[f.ObjectKey] = new AusFinding { Id = f.Id, ObjectKey = f.ObjectKey, Severity = f.Severity, RecurrenceCount = f.RecurrenceCount, Number = f.Number };
                }
            }
            int window = (int)(st.TryGetValue(AusSettingKeys.RecurrenceWindow, out decimal w) ? w : 90);
            AusFinding sameRule = null;
            if (hits.Count > 0)
            {
                var sr = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && f.Status == AusFindingStatus.Closed && f.ClosedAt != null && f.ClosedAt >= now.AddDays(-window)).OrderByDescending(f => f.Id).Select(f => new { f.Id, f.RecurrenceCount, f.Number }).FirstOrDefaultAsync(ct);
                if (sr != null) sameRule = new AusFinding { Id = sr.Id, RecurrenceCount = sr.RecurrenceCount, Number = sr.Number };
            }
            int year = AusRules.PersianYear(now.LocalDateTime);
            int seq = await _uow.Set<AusFinding>().Where(f => f.Year == year).Select(f => (int?)f.Sequence).MaxAsync(ct) ?? 0;
            var fresh = new List<(AusFinding F, string Note)>();
            foreach (AusHit h in hits)
            {
                if (open.TryGetValue(h.Key, out AusFinding ex))
                {
                    ex.LastSeenAt = now; ex.SeenCount++;
                    // a State deviation that is seen again while the finding is on its way to closure starts the chain over; a past event is always "seen again" and does not
                    if (!eventRule && (ex.Status == AusFindingStatus.Rechecked || ex.Status == AusFindingStatus.QaProposed || ex.Status == AusFindingStatus.PmoProposed))
                        AusFindingFlow.Reopen(ex, now, 0, "انحراف دوباره دیده شد (اجرای ممیزی)؛ زنجیره‌ی بستن از اول.", _uow);
                    continue;
                }
                if (voided.Contains(h.Key)) { tally.Suppressed++; rr.Suppressed++; continue; }
                AusSeverity baseSev = h.Severity ?? rule.Severity;
                AusSeverity sev = baseSev;
                AusFinding link = null;
                if (closedBefore.TryGetValue(h.Key, out AusFinding same))
                {
                    // the same record after its closure: a past event is not detected again; a State deviation that is back is a recurrence, one level higher
                    if (eventRule) { tally.Suppressed++; rr.Suppressed++; continue; }
                    link = same;
                    sev = AusRules.Raise((AusSeverity)Math.Max((int)same.Severity, (int)baseSev));
                }
                else if (sameRule != null) link = sameRule;   // a new object of a rule that was closed lately: linked, not raised (the systemic finding covers the pattern)
                seq++;
                fresh.Add((NewFinding(rule, h, run, now, st, year, seq, sev, baseSev, link), link == null ? "یافته‌ی تازه از قاعده‌ی " + rule.Code : (ReferenceEquals(link, sameRule) && !closedBefore.ContainsKey(h.Key) ? "تکرار در همان قاعده؛ پیوند به " + link.Number : "تکرار همان شیء؛ پیوند به " + link.Number + " و ارتقای شدت")));
            }

            if (backlog.Count > 0) seq = await BacklogAsync(rule, backlog, run, st, now, year, seq, fresh, tally, rr, ct);

            foreach (var (nf, _) in fresh) { _uow.Set<AusFinding>().Add(nf); tally.Created++; if (nf.Severity == AusSeverity.Major) tally.Majors.Add(nf); }
            if (fresh.Count > 0) await _uow.SaveChangesAsync(ct);
            foreach (var (nf, note) in fresh) AusFindingFlow.Event(_uow, nf, now, "detected", 0, AusFindingStatus.Open, AusFindingStatus.Open, note);
            if (fresh.Count > 0)
            {
                await ReopenAfterNewOccurrenceAsync(rule, fresh.Select(x => x.F).ToList(), now, ct);
                await _uow.SaveChangesAsync(ct);
                await SystemicAsync(rule, run, st, now, window, year, tally, ct);
            }
        }

        private AusFinding NewFinding(AusRuleDef rule, AusHit h, AusRun run, DateTimeOffset now, IReadOnlyDictionary<string, decimal> st, int year, int seq, AusSeverity sev, AusSeverity baseSev, AusFinding link)
        {
            string evidence = AusEvidence.Snapshot(rule, h, now, run.Id);
            return new AusFinding
            {
                Number = AusRules.Number(year, seq), Year = year, Sequence = seq, Source = AusFindingSource.Rule, RuleCode = rule.Code, Standard = rule.Standard, Clause = rule.Clause, Procedure = Cut(rule.Procedure, 60), ProcessCode = rule.ProcessCode,
                Severity = sev, BaseSeverity = baseSev, Title = Cut(rule.Title, 300), Description = Cut(h.Text, 2000), ObjectType = h.ObjectType, ObjectId = h.ObjectId, ObjectKey = h.Key, ObjectNumber = Cut(h.Number, 80), Link = Cut(h.Link, 200),
                EvidenceJson = evidence, EvidenceSha256 = AusEvidence.Sha256Hex(evidence), OwnerUserId = h.OwnerUserId, OwnerRole = h.OwnerRole ?? rule.ResolveOwnerRole(), OwnerUnit = rule.ResolveOwnerUnit(),
                DueDate = AusRules.DueDate(now.LocalDateTime, sev, st), DetectedAt = now, RunId = run.Id, LastSeenAt = now, SeenCount = 1, Status = AusFindingStatus.Open,
                RecurrenceOfId = link?.Id, RecurrenceCount = link == null ? 0 : link.RecurrenceCount + 1, CreateDate = now, ModifyDate = now,
            };
        }

        /// <summary>
        /// The events before the audit horizon of one rule: ONE backlog finding (Minor) with the count and a sample as evidence, which still goes through the closure chain. Nothing is dropped. A backlog that is open only grows
        /// (an event note); a closed or voided one is not recreated unless the set of events is a different one.
        /// </summary>
        private async Task<int> BacklogAsync(AusRuleDef rule, List<AusHit> backlog, AusRun run, IReadOnlyDictionary<string, decimal> st, DateTimeOffset now, int year, int seq, List<(AusFinding F, string Note)> fresh, RunTally tally, RuleResult rr, CancellationToken ct)
        {
            rr.Backlog = backlog.Count; tally.Backlog += backlog.Count;
            List<string> sorted = backlog.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            string hash = AusEvidence.Sha256Hex(string.Join("\n", sorted)).Substring(0, 10);
            AusFinding open = await _uow.Set<AusFinding>().FirstOrDefaultAsync(f => !f.Deleted && f.RuleCode == rule.Code && f.ObjectType == "Backlog" && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided, ct);
            if (open != null)
            {
                open.LastSeenAt = now; open.SeenCount++;
                if (!open.ObjectKey.EndsWith("|" + hash, StringComparison.Ordinal)) AusFindingFlow.Event(_uow, open, now, "backlog-grew", 0, open.Status, open.Status, $"فهرست رخدادهای پیش از افق ممیزی اکنون {backlog.Count} مورد است.");
                return seq;
            }
            string key = AusRules.ObjectKey("Backlog", 0, rule.Code + "|" + hash);
            if (await _uow.Set<AusFinding>().AnyAsync(f => !f.Deleted && f.RuleCode == rule.Code && f.ObjectKey == key, ct)) { tally.Suppressed++; rr.Suppressed++; return seq; }
            var hit = new AusHit { ObjectType = "Backlog", ObjectId = 0, Sub = rule.Code + "|" + hash, Number = rule.Code, Link = "/audit/findings", Severity = AusSeverity.Minor };
            hit.Text = AusCatalog.Cut($"{backlog.Count} رخداد از قاعده‌ی {rule.Code} مربوط به پیش از افق ممیزی است؛ همه در شواهد این یافته‌ی پس‌زمینه ثبت شده‌اند و مانند هر یافته‌ی دیگر باید با زنجیره‌ی QA، PMO و مدیرعامل بسته شوند.", 1990);
            hit.Fact("count", backlog.Count).Fact("rule", rule.Code).Fact("firstDate", backlog.Where(b => b.EventDate.HasValue).Select(b => b.EventDate).DefaultIfEmpty(null).Min()).Fact("lastDate", backlog.Where(b => b.EventDate.HasValue).Select(b => b.EventDate).DefaultIfEmpty(null).Max());
            int n = 0;
            foreach (AusHit b in backlog.OrderBy(b => b.Key, StringComparer.Ordinal).Take(40)) { n++; hit.Fact("sample" + n.ToString("00", CultureInfo.InvariantCulture), b.Key + " " + (b.Number ?? "")); }
            seq++;
            fresh.Add((NewFinding(rule, hit, run, now, st, year, seq, AusSeverity.Minor, AusSeverity.Minor, null), "یافته‌ی پس‌زمینه: " + backlog.Count + " رخداد پیش از افق ممیزی"));
            return seq;
        }

        /// <summary>A new occurrence of an Event rule for an owner whose earlier finding already has an action sends that finding's closure chain back to Open: the re-check requires that no new occurrence happened since the action.</summary>
        private async Task ReopenAfterNewOccurrenceAsync(AusRuleDef rule, List<AusFinding> created, DateTimeOffset now, CancellationToken ct)
        {
            if (rule.Mode != AusRecheckMode.Event) return;
            List<int> owners = created.Where(f => f.OwnerUserId.HasValue && f.ObjectType != "Backlog").Select(f => f.OwnerUserId.Value).Distinct().ToList();
            if (owners.Count == 0) return;
            List<AusFinding> chain = await _uow.Set<AusFinding>().Where(f => !f.Deleted && f.RuleCode == rule.Code && f.OwnerUserId != null && owners.Contains(f.OwnerUserId.Value) && f.ActionAt != null
                && (f.Status == AusFindingStatus.Rechecked || f.Status == AusFindingStatus.QaProposed || f.Status == AusFindingStatus.PmoProposed)).ToListAsync(ct);
            foreach (AusFinding f in chain)
                if (created.Any(n => n.Id != f.Id && n.OwnerUserId == f.OwnerUserId && n.DetectedAt > f.ActionAt))
                    AusFindingFlow.Reopen(f, now, 0, "رخداد تازه‌ای از همین قاعده برای همین مسئول پس از اقدام دیده شد؛ زنجیره‌ی بستن از اول.", _uow);
        }

        /// <summary>
        /// The same rule on ★3 or more different objects within the recurrence window is a pattern of the process, not of one record: ONE systemic Major finding (linked to the latest of them) is opened instead of raising every new hit.
        /// </summary>
        private async Task SystemicAsync(AusRuleDef rule, AusRun run, IReadOnlyDictionary<string, decimal> st, DateTimeOffset now, int window, int year, RunTally tally, CancellationToken ct)
        {
            int min = (int)(st.TryGetValue(AusSettingKeys.SystemicMinObjects, out decimal m) ? m : AusSettingKeys.DefaultOf(AusSettingKeys.SystemicMinObjects));
            if (min < 2) return;
            string systemicKey = AusRules.ObjectKey("Systemic", 0, rule.Code);
            if (await _uow.Set<AusFinding>().AnyAsync(f => !f.Deleted && f.RuleCode == rule.Code && f.ObjectKey == systemicKey && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided, ct)) return;
            DateTimeOffset from = now.AddDays(-window);
            DateTimeOffset? lastSystemic = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && f.ObjectKey == systemicKey).OrderByDescending(f => f.Id).Select(f => (DateTimeOffset?)f.DetectedAt).FirstOrDefaultAsync(ct);
            if (lastSystemic.HasValue && lastSystemic.Value > from) from = lastSystemic.Value;
            var objects = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.RuleCode == rule.Code && f.Source == AusFindingSource.Rule && f.DetectedAt > from && f.ObjectType != "Systemic" && f.ObjectType != "Backlog")
                .Select(f => new { f.Id, f.ObjectKey, f.Number }).ToListAsync(ct);
            var distinct = objects.GroupBy(o => o.ObjectKey).Select(g => g.OrderBy(x => x.Id).First()).OrderBy(o => o.Id).ToList();
            if (distinct.Count < min) return;
            var hit = new AusHit { ObjectType = "Systemic", ObjectId = 0, Sub = rule.Code, Number = rule.Code, Link = "/audit/findings", Severity = AusSeverity.Major };
            hit.Text = AusCatalog.Cut($"قاعده‌ی {rule.Code} در {window} روز گذشته روی {distinct.Count} شیء متفاوت دیده شده است؛ این الگوی فرایند است و نه خطای یک رکورد: ریشه‌ی علت فرایندی باید پیدا و اصلاح شود.", 1990);
            hit.Fact("rule", rule.Code).Fact("objects", distinct.Count).Fact("windowDays", window);
            int n = 0;
            foreach (var d in distinct.Take(60)) { n++; hit.Fact("finding" + n.ToString("00", CultureInfo.InvariantCulture), d.Number + " " + d.ObjectKey); }
            int seq = (await _uow.Set<AusFinding>().Where(f => f.Year == year).Select(f => (int?)f.Sequence).MaxAsync(ct) ?? 0) + 1;
            var link = new AusFinding { Id = distinct.Last().Id, RecurrenceCount = distinct.Count - 1 };
            AusFinding sf = NewFinding(rule, hit, run, now, st, year, seq, AusSeverity.Major, AusSeverity.Major, link);
            sf.RecurrenceCount = distinct.Count;
            _uow.Set<AusFinding>().Add(sf);
            await _uow.SaveChangesAsync(ct);
            AusFindingFlow.Event(_uow, sf, now, "detected", 0, AusFindingStatus.Open, AusFindingStatus.Open, $"یافته‌ی سیستمی: {distinct.Count} شیء متفاوت از قاعده‌ی {rule.Code}");
            await _uow.SaveChangesAsync(ct);
            tally.Created++; tally.Majors.Add(sf);
        }

        /// <summary>
        /// The immediate in-app alert of the major findings of a run, ONE per run for the CEO, PMO and QA managers (all new major findings listed, each one still tracked on its own) and one per owner;
        /// the escalations of overdue findings go in a consolidated alert of their own.
        /// </summary>
        private async Task AlertAsync(AusRun run, RunTally tally, CancellationToken ct)
        {
            if (_notify == null) return;
            try
            {
                if (tally.Majors.Count > 0)
                {
                    string list = string.Join("، ", tally.Majors.Take(8).Select(f => f.Number));
                    string title = Cut($"{tally.Majors.Count} یافته‌ی عمده‌ی تازه‌ی ممیزی: {list}" + (tally.Majors.Count > 8 ? " و ..." : ""), 250);
                    DateTime due = tally.Majors.Min(f => f.DueDate);
                    await _notify.NotifyAsync(new NotifyRequest { SourceKey = "aus-run:" + run.Id, Module = "AUDIT", SourceType = "AusRun", SourceId = run.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Immediate, DueDate = due, Title = title, Link = "/audit/findings", Roles = AusRoles.Qa + "," + AusRoles.Pmo + "," + AusRoles.Ceo }, ct);
                    foreach (var g in tally.Majors.Where(f => f.OwnerUserId.HasValue).GroupBy(f => f.OwnerUserId.Value))
                    {
                        var req = new NotifyRequest { SourceKey = "aus-run:" + run.Id + ":u" + g.Key, Module = "AUDIT", SourceType = "AusRun", SourceId = run.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Immediate, DueDate = g.Min(f => f.DueDate), Title = Cut($"{g.Count()} یافته‌ی عمده‌ی ممیزی با مسئولیت شما: " + string.Join("، ", g.Take(8).Select(f => f.Number)), 250), Link = "/audit/findings" };
                        req.UserIds.Add(g.Key);
                        await _notify.NotifyAsync(req, ct);
                    }
                }
                if (tally.Escalations.Count > 0)
                {
                    var req = new NotifyRequest { SourceKey = "aus-esc:" + run.Id, Module = "AUDIT", SourceType = "AusRun", SourceId = run.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Immediate, Title = Cut($"{tally.Escalations.Count} یافته‌ی ممیزی از مهلت گذشته است: " + string.Join("، ", tally.Escalations.Take(8).Select(f => f.Number)), 250), Link = "/audit/findings?overdue=1", Roles = AusRoles.Qa + "," + AusRoles.Pmo + "," + AusRoles.Ceo };
                    foreach (int u in tally.Escalations.Where(f => f.OwnerUserId.HasValue).Select(f => f.OwnerUserId.Value).Distinct()) req.UserIds.Add(u);
                    await _notify.NotifyAsync(req, ct);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { /* an alert never fails a run */ }
        }

        public async Task<bool> StillDeviatesAsync(AusFinding finding, CancellationToken ct)
        {
            AusRuleDef rule = AusCatalog.Find(finding.RuleCode);
            if (rule == null) return true;
            DateTimeOffset now = DateTimeOffset.Now;
            // an event (and the backlog and systemic findings, which are about a pattern of past events): passes when the action is recorded and nothing new of the same kind happened since
            if (rule.Mode == AusRecheckMode.Event || finding.ObjectType == "Backlog" || finding.ObjectType == "Systemic")
            {
                if (finding.ActionAt == null) return true;
                DateTimeOffset after = finding.ActionAt.Value;
                return await _uow.Set<AusFinding>().AsNoTracking().AnyAsync(f => !f.Deleted && f.RuleCode == finding.RuleCode && f.Id != finding.Id && f.DetectedAt > after && f.ObjectType != "Backlog" && f.ObjectType != "Systemic"
                    && (f.ObjectKey == finding.ObjectKey || (finding.OwnerUserId != null && f.OwnerUserId == finding.OwnerUserId)), ct);
            }
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            var ctx = new AusContext { Uow = _uow, Services = _services, Today = now.LocalDateTime.Date, Now = now, Settings = st, Max = (int)st[AusSettingKeys.MaxHits], Ct = ct, FocusId = finding.ObjectId > 0 ? finding.ObjectId : (int?)null };
            List<AusHit> found = await rule.Query(ctx) ?? new List<AusHit>();
            // an incomplete pass cannot prove that the deviation is gone
            if (ctx.Continued) return true;
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
            if (r == null) return QualityText.NotFound<AusRunDto>("اجرا پیدا نشد.");
            AusRunDto d = ToDto(r);
            d.Systemic = await _uow.Set<AusFinding>().CountAsync(f => f.RunId == r.Id && f.ObjectType == "Systemic", ct);
            return ResponseDtoHelper.Ok(d);
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
                    Incremental = r.Incremental, Mode = r.Mode == AusRecheckMode.Event ? "Event" : "State", Enabled = !(st.TryGetValue(AusSettingKeys.RuleKey(r.Code), out decimal on) && on == 0m) && st[AusSettingKeys.Master] != 0m, SettingKey = AusSettingKeys.RuleKey(r.Code), OwnerRole = r.ResolveOwnerRole(),
                    LastRunAtText = s?.LastRunAt != null ? Stamp(s.LastRunAt.Value) : null, LastStatus = s?.LastStatus, LastHits = s?.LastHits ?? 0, LastDurationMs = s?.LastDurationMs ?? 0, TotalRuns = s?.TotalRuns ?? 0, OpenFindings = open.TryGetValue(r.Code, out int n) ? n : 0,
                });
            }
            return ResponseDtoHelper.Ok(list);
        }
    }
}
