using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Agents
{
    public sealed class AgentReconcileResult
    {
        public int Created { get; set; }
        public int Closed { get; set; }
        public int Cancelled { get; set; }

        /// <summary>Tasks handed to a person because the agent or the step is switched off.</summary>
        public int FallbackSet { get; set; }

        /// <summary>Fallback tasks that went back to the agent because it is on again.</summary>
        public int Restored { get; set; }
        public int Escalated { get; set; }

        /// <summary>A task that was not made again because the same step closed the same document a moment ago (the step did not change the state it looks at).</summary>
        public int Suppressed { get; set; }
        public List<string> Errors { get; } = new List<string>();

        public bool Changed => Created + Closed + Cancelled + FallbackSet + Restored + Escalated > 0;

        public void Add(AgentReconcileResult other)
        {
            Created += other.Created; Closed += other.Closed; Cancelled += other.Cancelled; FallbackSet += other.FallbackSet; Restored += other.Restored; Escalated += other.Escalated;
            Suppressed += other.Suppressed; Errors.AddRange(other.Errors);
        }

        public string Describe() => string.Create(CultureInfo.InvariantCulture, $"created={Created}; closed={Closed}; cancelled={Cancelled}; fallback={FallbackSet}; restored={Restored}; escalated={Escalated}; suppressed={Suppressed}; errors={Errors.Count}");
    }

    public interface IAgentReconciler
    {
        /// <summary>Reconciles the steps of one agent, or of every agent when <paramref name="agentCode"/> is empty.</summary>
        Task<AgentReconcileResult> ReconcileAsync(string agentCode, CancellationToken ct);

        /// <summary>Reconciles the given steps only (the tests and a later "run now" nudge of a hook).</summary>
        Task<AgentReconcileResult> ReconcileStepsAsync(IEnumerable<string> stepCodes, CancellationToken ct);
    }

    /// <summary>The notices of the agent layer in the office inbox (the keys keep a task from being told twice and let the close find them).</summary>
    internal static class AgentNotices
    {
        public static string MirrorKey(int taskId) => "agt:" + taskId.ToString(CultureInfo.InvariantCulture);
        public static string AlertKey(int taskId) => "agt-alert:" + taskId.ToString(CultureInfo.InvariantCulture);

        /// <summary>A task a person must do that has no item in the office inbox yet: the person sees it there too.</summary>
        public static Task MirrorAsync(INotificationService notify, AgentTask task, CancellationToken ct) =>
            notify == null || string.IsNullOrWhiteSpace(task.GateRoles) && !task.GateUserId.HasValue ? Task.CompletedTask : notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = MirrorKey(task.Id), Module = "agents", SourceType = "AgentTask", SourceId = task.Id, Title = AgentText.Cut(task.Title, 250), Link = task.Link ?? "/agents/inbox",
                Kind = task.Kind == AgentStepKind.HumanGate ? OfInboxKind.Approval : OfInboxKind.Task, Priority = OfUrgency.Normal, DueDate = task.DueAt?.LocalDateTime,
                Roles = task.GateRoles, UserIds = task.GateUserId.HasValue ? new List<int> { task.GateUserId.Value } : new List<int>(),
            }, ct);

        /// <summary>The PMO is told once: a gate is late or a task failed.</summary>
        public static Task AlertPmoAsync(INotificationService notify, AgentTask task, string title, CancellationToken ct) =>
            notify == null ? Task.CompletedTask : notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = AlertKey(task.Id), Module = "agents", SourceType = "AgentTask", SourceId = task.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent, Roles = AgentRoles.Admins,
                Link = "/agents/board", Title = AgentText.Cut(title, 250),
            }, ct);

        /// <summary>Closes the notices of a task that was closed (<paramref name="wasWaiting"/>: it waited for a person, so it may have been mirrored).</summary>
        public static async Task CloseAsync(INotificationService notify, AgentTask task, bool wasWaiting, string reason, CancellationToken ct)
        {
            if (notify == null) return;
            if (wasWaiting) await notify.CloseAsync(MirrorKey(task.Id), reason, ct);
            if (task.EscalatedAt.HasValue) await notify.CloseAsync(AlertKey(task.Id), reason, ct);
        }
    }

    /// <summary>
    /// Makes the tasks follow the documents. It is idempotent and safe to repeat after a restart: everything is derived from the state of the documents.
    /// For every step it asks which documents the step is next for (Discover), makes a task for each one that has none, closes the task of a document the step no longer finds
    /// (that is how a passed gate is seen: the person used the existing approve action, no parallel approval exists), hands the tasks of a switched-off agent to people and takes them back,
    /// and tells the PMO once about a late gate or a failed task.
    /// </summary>
    public class AgentReconciler : IAgentReconciler
    {
        /// <summary>A task that was just closed as done is not made again for the same document before this time (a step that does not change the state it looks at would loop). Only for the steps an agent runs: a gate, a physical or an input task that comes back (a returned and re-submitted document) is shown to the person at once.</summary>
        public static readonly TimeSpan RepeatGuard = TimeSpan.FromMinutes(30);

        private readonly IUnitOfWork _uow;
        private readonly IAgentStepCatalog _catalog;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly INotificationService _notify;
        private readonly ILogger<AgentReconciler> _logger;

        public AgentReconciler(IUnitOfWork uow, IAgentStepCatalog catalog, IInventoryUserContext user, IAiAudit audit, INotificationService notify = null, ILogger<AgentReconciler> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _notify = notify;
            _logger = logger;
        }

        public Task<AgentReconcileResult> ReconcileAsync(string agentCode, CancellationToken ct) =>
            CoreAsync(_catalog.All.Where(s => string.IsNullOrWhiteSpace(agentCode) || string.Equals(s.AgentCode, agentCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList(), ct);

        public Task<AgentReconcileResult> ReconcileStepsAsync(IEnumerable<string> stepCodes, CancellationToken ct)
        {
            var codes = new HashSet<string>((stepCodes ?? Enumerable.Empty<string>()).Select(c => c?.Trim()), StringComparer.OrdinalIgnoreCase);
            return CoreAsync(_catalog.All.Where(s => codes.Contains(s.Code)).ToList(), ct);
        }

        private async Task<AgentReconcileResult> CoreAsync(List<IAgentStep> steps, CancellationToken ct)
        {
            var result = new AgentReconcileResult();
            if (steps.Count == 0) return result;
            DateTimeOffset now = DateTimeOffset.Now;
            DateTime today = DateTime.Today;
            Dictionary<string, AgentStepSetting> settings = await AgentSettings.EnsureAsync(_uow, _catalog.All, ct);
            HashSet<string> seatsOff = await AgentSettings.DisabledSeatsAsync(_uow, ct);

            foreach (IAgentStep step in steps)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await ReconcileStepAsync(step, settings[step.Code], !seatsOff.Contains(step.AgentCode), now, today, result, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // one step that cannot be read (or a save that lost a race) must not stop the others; its tasks stay as they are
                    AgentDb.Clear(_uow);
                    _logger?.LogWarning(ex, "Agent step {Step} could not be reconciled", step.Code);
                    result.Errors.Add(step.Code + ": " + AgentText.Cut(ex.Message, 300));
                }
            }

            try { await EscalateAsync(steps, now, result, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AgentDb.Clear(_uow);
                _logger?.LogWarning(ex, "Agent escalation failed");
                result.Errors.Add("escalation: " + AgentText.Cut(ex.Message, 300));
            }

            if (result.Changed || result.Errors.Count > 0)
            {
                try
                {
                    _audit.Add(AiAuditKinds.Agent, "reconcile " + result.Describe() + (result.Errors.Count > 0 ? "; " + AgentText.Cut(string.Join(" | ", result.Errors), 800) : string.Empty), seatCode: AiSeatContext.Code);
                    await _uow.SaveChangesAsync(ct);
                }
                catch (DbUpdateException) { AgentDb.Clear(_uow); }
            }
            return result;
        }

        private static string KeyOf(string type, int id) => type + "#" + id.ToString(CultureInfo.InvariantCulture);

        private void AddEvent(AgentTask task, AgentTaskState? from, AgentTaskState to, string note, DateTimeOffset now) =>
            _uow.Set<AgentTaskEvent>().Add(new AgentTaskEvent { Task = task, At = now, FromState = from, ToState = to, UserId = _user.UserId, SeatCode = AiSeatContext.Code, Note = AgentText.Cut(note, 1000) });

        private static bool IsWaiting(AgentTaskState s) => s == AgentTaskState.WaitingHumanGate || s == AgentTaskState.WaitingPhysical || s == AgentTaskState.WaitingInput;

        private void CloseTask(AgentTask task, AgentTaskState to, string reason, DateTimeOffset now)
        {
            AgentTaskState from = task.State;
            task.State = to; task.ClosedAt = now; task.ClosedReason = AgentText.Cut(reason, 300); task.NextAttemptAt = null;
            AddEvent(task, from, to, reason, now);
        }

        private async Task ReconcileStepAsync(IAgentStep step, AgentStepSetting setting, bool seatOn, DateTimeOffset now, DateTime today, AgentReconcileResult result, CancellationToken ct)
        {
            bool agentOn = setting.Enabled && seatOn;
            List<AgentTask> open = await _uow.Set<AgentTask>().Where(t => t.StepCode == step.Code && !t.Deleted && t.State < AgentTaskState.Done).ToListAsync(ct);
            var closedNow = new List<(AgentTask Task, bool WasWaiting)>();
            var restored = new List<AgentTask>();
            var toMirror = new List<AgentTask>();

            // a gate, a physical task or an input task has no "agent" to fall back from: a switched-off step is not tracked, and its open tasks are withdrawn
            if (!setting.Enabled && step.Kind != AgentStepKind.Agent)
            {
                foreach (AgentTask t in open.Where(t => t.State != AgentTaskState.Running))
                {
                    bool wasWaiting = IsWaiting(t.State);
                    CloseTask(t, AgentTaskState.Cancelled, AgentText.StepSwitchedOffCancel, now);
                    closedNow.Add((t, wasWaiting));
                    result.Cancelled++;
                }
                await SaveAsync(ct);
                await CloseNoticesAsync(closedNow, ct);
                return;
            }

            List<AgentWorkItem> discovered = await step.DiscoverAsync(_uow, today, ct) ?? new List<AgentWorkItem>();
            var found = new Dictionary<string, AgentWorkItem>(StringComparer.Ordinal);
            foreach (AgentWorkItem item in discovered)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.TargetType)) continue;
                found.TryAdd(KeyOf(item.TargetType.Trim(), item.TargetId), item);
            }

            var openByKey = new Dictionary<string, AgentTask>(StringComparer.Ordinal);
            foreach (AgentTask task in open)
            {
                string key = KeyOf(task.TargetType, task.TargetId);
                if (openByKey.ContainsKey(key)) continue;   // a duplicate (an earlier race): the first one is kept, the rest closes below
                openByKey[key] = task;
            }

            foreach (AgentTask task in open)
            {
                string key = KeyOf(task.TargetType, task.TargetId);
                bool duplicate = !ReferenceEquals(openByKey[key], task);
                if (duplicate || !found.ContainsKey(key))
                {
                    if (task.State == AgentTaskState.Running) continue;   // the executor closes it; a dead run is written off by the coordinator
                    bool wasWaiting = IsWaiting(task.State);
                    CloseTask(task, AgentTaskState.Done, AgentText.DocumentChanged, now);
                    closedNow.Add((task, wasWaiting));
                    result.Closed++;
                    continue;
                }

                AgentWorkItem item = found[key];
                Refresh(task, item, step.Kind != AgentStepKind.Agent);
                if (step.Kind != AgentStepKind.Agent) continue;

                if (agentOn && task.IsFallback)
                {
                    AgentTaskState from = task.State;
                    restored.Add(task);
                    task.State = AgentTaskState.Queued; task.IsFallback = false; task.GateRoles = null; task.DueAt = null; task.NextAttemptAt = null; task.LastError = null; task.EscalatedAt = null;
                    AddEvent(task, from, AgentTaskState.Queued, AgentText.AgentRestored, now);
                    result.Restored++;
                }
                else if (!agentOn && !task.IsFallback && (task.State == AgentTaskState.Queued || task.State == AgentTaskState.Returned))
                {
                    MakeFallback(task, step, item, setting, now, AgentText.StepSwitchedOff);
                    toMirror.Add(task);
                    result.FallbackSet++;
                }
            }

            // documents that have no open task: make one, unless the step closed the same document a moment ago
            List<KeyValuePair<string, AgentWorkItem>> fresh = found.Where(p => !openByKey.ContainsKey(p.Key)).ToList();
            HashSet<string> recentlyDone = null;
            if (fresh.Count > 0 && step.Kind == AgentStepKind.Agent)
            {
                DateTimeOffset since = now - RepeatGuard;
                var recent = await _uow.Set<AgentTask>().AsNoTracking()
                    .Where(t => t.StepCode == step.Code && !t.Deleted && t.State == AgentTaskState.Done && t.ClosedAt != null && t.ClosedAt > since)
                    .Select(t => new { t.TargetType, t.TargetId }).ToListAsync(ct);
                recentlyDone = recent.Select(r => KeyOf(r.TargetType, r.TargetId)).ToHashSet(StringComparer.Ordinal);
            }
            foreach (KeyValuePair<string, AgentWorkItem> pair in fresh)
            {
                if (recentlyDone != null && recentlyDone.Contains(pair.Key)) { result.Suppressed++; continue; }
                AgentTask task = NewTask(step, pair.Value, setting, agentOn, now);
                _uow.Set<AgentTask>().Add(task);
                AddEvent(task, null, task.State, task.IsFallback ? AgentText.StepSwitchedOff : null, now);
                if (IsWaiting(task.State)) toMirror.Add(task);
                result.Created++;
            }

            await SaveAsync(ct);

            // the notices: a waiting task that has no office-inbox item of its own is mirrored there; a closed task takes its notices with it
            foreach (AgentTask t in toMirror)
            {
                AgentGateInfo info = step.GateInfo(ToItem(t)) ?? new AgentGateInfo();
                if (!info.HasOfficeInboxItem) await AgentNotices.MirrorAsync(_notify, t, ct);
            }
            foreach (AgentTask t in restored)
                if (_notify != null) await _notify.CloseAsync(AgentNotices.MirrorKey(t.Id), AgentText.AgentRestored, ct);
            await CloseNoticesAsync(closedNow, ct);
        }

        private async Task CloseNoticesAsync(List<(AgentTask Task, bool WasWaiting)> closed, CancellationToken ct)
        {
            foreach ((AgentTask task, bool wasWaiting) in closed) await AgentNotices.CloseAsync(_notify, task, wasWaiting, task.ClosedReason, ct);
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            if (!AgentDb.HasChanges(_uow)) return;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // another process made or changed the same tasks at the same moment (the unique index of open tasks, the row version): this pass is dropped, the next one starts clean
                AgentDb.Clear(_uow);
                throw;
            }
        }

        private static AgentWorkItem ToItem(AgentTask t) => new AgentWorkItem
        {
            TargetType = t.TargetType, TargetId = t.TargetId, TargetNumber = t.TargetNumber, ContractId = t.ContractId, WorkOrderId = t.WorkOrderId, Summary = t.Summary, GateUserId = t.GateUserId,
        };

        /// <summary>The document is still waiting at this step: what the person or the agent sees follows the document.</summary>
        private static void Refresh(AgentTask task, AgentWorkItem item, bool gateStep)
        {
            if (task.State == AgentTaskState.Running) return;
            // a gate whose roles follow the state of the document keeps the roles that are still missing (a fallback task keeps the roles of the step)
            if (gateStep && !task.IsFallback && item.GateRoles != null && AgentText.NormalizeRoles(item.GateRoles) is string roles && roles != task.GateRoles) task.GateRoles = AgentText.Cut(roles, 300);
            if (gateStep && !task.IsFallback && item.GateUserId != task.GateUserId && (item.GateUserId.HasValue || task.GateUserId.HasValue)) task.GateUserId = item.GateUserId;
            if (item.TargetNumber != null && item.TargetNumber != task.TargetNumber) task.TargetNumber = AgentText.Cut(item.TargetNumber, 60);
            if (item.ContractId != task.ContractId) task.ContractId = item.ContractId;
            if (item.WorkOrderId != task.WorkOrderId) task.WorkOrderId = item.WorkOrderId;
            if (item.Summary != null && item.Summary != task.Summary) task.Summary = AgentText.Cut(item.Summary, 1000);
        }

        private static string TitleOf(IAgentStep step, AgentWorkItem item, AgentGateInfo info) =>
            AgentText.Cut(AgentText.Clean(info.Title) ?? (step.Title + (string.IsNullOrWhiteSpace(item.TargetNumber) ? string.Empty : " " + item.TargetNumber)), 300);

        private AgentTask NewTask(IAgentStep step, AgentWorkItem item, AgentStepSetting setting, bool agentOn, DateTimeOffset now)
        {
            AgentGateInfo info = step.GateInfo(item) ?? new AgentGateInfo();
            var task = new AgentTask
            {
                StepCode = step.Code, Station = step.Station, AgentCode = step.AgentCode.ToUpperInvariant(), Kind = step.Kind, TargetType = AgentText.Cut(item.TargetType.Trim(), 60), TargetId = item.TargetId,
                TargetNumber = AgentText.Cut(item.TargetNumber, 60), ContractId = item.ContractId, WorkOrderId = item.WorkOrderId, Title = TitleOf(step, item, info), Summary = AgentText.Cut(item.Summary, 1000),
                Link = AgentText.Cut(info.Link, 300), CreateDate = now, ModifyDate = now,
            };
            switch (step.Kind)
            {
                case AgentStepKind.Agent:
                    if (agentOn) task.State = AgentTaskState.Queued;
                    else ApplyFallback(task, step, setting, now);
                    break;
                case AgentStepKind.HumanGate:
                    MakeWaiting(task, AgentTaskState.WaitingHumanGate, info, setting, now);
                    break;
                case AgentStepKind.Physical:
                    MakeWaiting(task, AgentTaskState.WaitingPhysical, info, setting, now);
                    break;
                default:
                    MakeWaiting(task, AgentTaskState.WaitingInput, info, setting, now);
                    break;
            }
            return task;
        }

        private static void MakeWaiting(AgentTask task, AgentTaskState state, AgentGateInfo info, AgentStepSetting setting, DateTimeOffset now)
        {
            task.State = state;
            task.GateRoles = AgentText.Cut(AgentText.NormalizeRoles(info.Roles), 300);
            task.GateUserId = info.UserId;
            task.DueAt = now.AddHours(info.DueHours ?? setting.GateDueHours);
        }

        private static void ApplyFallback(AgentTask task, IAgentStep step, AgentStepSetting setting, DateTimeOffset now)
        {
            task.State = AgentTaskState.WaitingInput;
            task.IsFallback = true;
            task.GateRoles = AgentText.Cut(AgentText.NormalizeRoles(step.FallbackRoles), 300);
            task.DueAt = now.AddHours(setting.GateDueHours);
            task.NextAttemptAt = null;
        }

        private void MakeFallback(AgentTask task, IAgentStep step, AgentWorkItem item, AgentStepSetting setting, DateTimeOffset now, string note)
        {
            AgentTaskState from = task.State;
            ApplyFallback(task, step, setting, now);
            AgentGateInfo info = step.GateInfo(item) ?? new AgentGateInfo();
            task.Link = AgentText.Cut(info.Link ?? task.Link, 300);
            AddEvent(task, from, task.State, note, now);
        }

        /// <summary>A gate (or physical or input task) that is past its time, and a failed task, are told once to the PMO.</summary>
        private async Task EscalateAsync(List<IAgentStep> steps, DateTimeOffset now, AgentReconcileResult result, CancellationToken ct)
        {
            string[] codes = steps.Select(s => s.Code).ToArray();
            List<AgentTask> late = await _uow.Set<AgentTask>()
                .Where(t => !t.Deleted && codes.Contains(t.StepCode) && t.EscalatedAt == null
                    && ((t.State == AgentTaskState.Failed)
                        || ((t.State == AgentTaskState.WaitingHumanGate || t.State == AgentTaskState.WaitingPhysical || t.State == AgentTaskState.WaitingInput) && t.DueAt != null && t.DueAt < now)))
                .ToListAsync(ct);
            if (late.Count == 0) return;
            foreach (AgentTask t in late)
            {
                t.EscalatedAt = now;
                AddEvent(t, t.State, t.State, t.State == AgentTaskState.Failed ? "اطلاع به PMO: کار ناموفق ماند" : "اطلاع به PMO: مهلت کار گذشت", now);
            }
            await _uow.SaveChangesAsync(ct);
            foreach (AgentTask t in late)
                await AgentNotices.AlertPmoAsync(_notify, t, t.State == AgentTaskState.Failed
                    ? $"کار ایجنت «{t.Title}» ناموفق ماند: {t.LastError}"
                    : $"کار «{t.Title}» ({AgentStations.Title(t.Station)}) از مهلت گذشته و منتظر {t.GateRoles} است.", ct);
            result.Escalated += late.Count;
        }
    }
}
