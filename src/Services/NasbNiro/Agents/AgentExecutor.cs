using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Agents
{
    public enum AgentRunOutcome
    {
        /// <summary>The step did its work; the task is closed.</summary>
        Done = 1,

        /// <summary>A precondition is not met yet; the task is queued again without an attempt.</summary>
        Waiting = 2,

        /// <summary>The service refused (or the step threw); the attempt is counted.</summary>
        Refused = 3,

        /// <summary>That was the last attempt: the task failed and the PMO is told.</summary>
        Failed = 4,

        /// <summary>The agent may not run it (another agent's step, a gate, a task that is not queued): nothing changed.</summary>
        NotAllowed = 5,

        /// <summary>The step or the agent is switched off: the task went to a person.</summary>
        HandedToPerson = 6,

        /// <summary>Another process changed the task first.</summary>
        Skipped = 7,
    }

    public sealed class AgentRunSummary
    {
        public int Rounds { get; set; }
        public int Executed { get; set; }
        public int Done { get; set; }
        public int Waiting { get; set; }
        public int Refused { get; set; }
        public int Failed { get; set; }
        public AgentReconcileResult Reconcile { get; } = new AgentReconcileResult();

        public string Describe() => string.Create(CultureInfo.InvariantCulture, $"rounds={Rounds}; executed={Executed}; done={Done}; waiting={Waiting}; refused={Refused}; failed={Failed}; {Reconcile.Describe()}");
    }

    public interface IAgentExecutor
    {
        /// <summary>
        /// One run of an agent: up to <see cref="AgentExecutor.MaxRounds"/> rounds of "reconcile my steps, then do up to <see cref="AgentExecutor.MaxTasksPerRound"/> queued tasks",
        /// so that a chain such as draft then submit completes in one run.
        /// </summary>
        Task<AgentRunSummary> RunAgentAsync(string agentCode, CancellationToken ct);

        /// <summary>Does one task as the agent. The agent may run only a step that is its own (checked here, before every run).</summary>
        Task<AgentRunOutcome> ExecuteTaskAsync(string actingAgentCode, int taskId, CancellationToken ct);

        /// <summary>A task that has been Running for longer than <paramref name="staleAfter"/> belongs to a run that died: it goes back to the queue.</summary>
        Task<int> RequeueStaleAsync(TimeSpan staleAfter, CancellationToken ct);
    }

    /// <summary>
    /// Runs the tasks of an agent. The task rows are kept by the scope of the seat run; every step runs in a scope of its own (inside the same acting flow, so the principal is the seat), so a failed step
    /// drops its tracked changes with its scope and cannot spoil the bookkeeping. Every execution writes one audit row (kind "agent") and one event of the task.
    /// </summary>
    public class AgentExecutor : IAgentExecutor
    {
        public const int MaxRounds = 3;
        public const int MaxTasksPerRound = 20;

        /// <summary>A step that is not ready waits this long before it is looked at again.</summary>
        public static readonly TimeSpan WaitingDelay = TimeSpan.FromMinutes(10);

        /// <summary>The delay after the first, second and third refusal.</summary>
        public static readonly TimeSpan[] Backoff = { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };

        private readonly IUnitOfWork _uow;
        private readonly IAgentStepCatalog _catalog;
        private readonly IAgentReconciler _reconciler;
        private readonly IServiceScopeFactory _scopes;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly INotificationService _notify;
        private readonly ILogger<AgentExecutor> _logger;

        public AgentExecutor(IUnitOfWork uow, IAgentStepCatalog catalog, IAgentReconciler reconciler, IServiceScopeFactory scopes, IInventoryUserContext user, IAiAudit audit,
            INotificationService notify = null, ILogger<AgentExecutor> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _notify = notify;
            _logger = logger;
        }

        public async Task<AgentRunSummary> RunAgentAsync(string agentCode, CancellationToken ct)
        {
            var summary = new AgentRunSummary();
            string code = agentCode?.Trim().ToUpperInvariant();
            for (int round = 1; round <= MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                summary.Rounds = round;
                summary.Reconcile.Add(await _reconciler.ReconcileAsync(code, ct));

                DateTimeOffset now = DateTimeOffset.Now;
                List<int> ids = await _uow.Set<AgentTask>().AsNoTracking()
                    .Where(t => !t.Deleted && t.AgentCode == code && (t.State == AgentTaskState.Queued || t.State == AgentTaskState.Returned) && (t.NextAttemptAt == null || t.NextAttemptAt <= now))
                    .OrderBy(t => t.Id).Select(t => t.Id).Take(MaxTasksPerRound).ToListAsync(ct);
                if (ids.Count == 0) break;

                foreach (int id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    AgentRunOutcome outcome = await ExecuteTaskAsync(code, id, ct);
                    summary.Executed++;
                    switch (outcome)
                    {
                        case AgentRunOutcome.Done: summary.Done++; break;
                        case AgentRunOutcome.Waiting: summary.Waiting++; break;
                        case AgentRunOutcome.Refused: summary.Refused++; break;
                        case AgentRunOutcome.Failed: summary.Failed++; break;
                    }
                }
            }
            return summary;
        }

        private async Task<bool> SeatOnAsync(string agentCode, CancellationToken ct) => !(await AgentSettings.DisabledSeatsAsync(_uow, ct)).Contains(agentCode);

        public async Task<AgentRunOutcome> ExecuteTaskAsync(string actingAgentCode, int taskId, CancellationToken ct)
        {
            string acting = actingAgentCode?.Trim().ToUpperInvariant();
            AgentTask task = await _uow.Set<AgentTask>().FirstOrDefaultAsync(t => t.Id == taskId && !t.Deleted, ct);
            if (task == null) return AgentRunOutcome.Skipped;
            IAgentStep step = _catalog.Find(task.StepCode);

            // the allow-list: an agent runs only a step that is its own, and only a step that an agent does
            if (step != null && (!string.Equals(step.AgentCode, acting, StringComparison.OrdinalIgnoreCase) || !string.Equals(task.AgentCode, acting, StringComparison.OrdinalIgnoreCase) || step.Kind != AgentStepKind.Agent))
            {
                _audit.Add(AiAuditKinds.Agent, $"step {task.StepCode} task {task.Id}: refused to run for {acting}: {AgentText.WrongAgent}", seatCode: acting);
                await SaveQuietlyAsync(ct);
                return AgentRunOutcome.NotAllowed;
            }
            if (task.State != AgentTaskState.Queued && task.State != AgentTaskState.Returned) return AgentRunOutcome.NotAllowed;

            DateTimeOffset now = DateTimeOffset.Now;
            AgentTaskState from = task.State;
            if (step == null)
            {
                Close(task, from, AgentTaskState.Failed, AgentText.StepUnknown, now);
                task.LastError = AgentText.StepUnknown;
                _audit.Add(AiAuditKinds.Agent, $"step {task.StepCode} task {task.Id} -> failed; {AgentText.StepUnknown}", seatCode: acting);
                await SaveQuietlyAsync(ct);
                return AgentRunOutcome.Failed;
            }

            // a step or a seat that was switched off after the task was queued: a person takes it
            Dictionary<string, AgentStepSetting> settings = await AgentSettings.EnsureAsync(_uow, _catalog.All, ct);
            AgentStepSetting setting = settings[step.Code];
            if (!setting.Enabled || !await SeatOnAsync(acting, ct))
            {
                task.State = AgentTaskState.WaitingInput; task.IsFallback = true; task.GateRoles = AgentText.NormalizeRoles(step.FallbackRoles); task.DueAt = now.AddHours(setting.GateDueHours); task.NextAttemptAt = null;
                AddEvent(task, from, task.State, AgentText.StepSwitchedOff, now);
                _audit.Add(AiAuditKinds.Agent, $"step {task.StepCode} task {task.Id} -> handed to a person; {AgentText.StepSwitchedOff}", seatCode: acting);
                if (!await SaveQuietlyAsync(ct)) return AgentRunOutcome.Skipped;
                await AgentNotices.MirrorAsync(_notify, task, ct);
                return AgentRunOutcome.HandedToPerson;
            }

            // Running: the row version keeps two runners of the same task apart
            task.State = AgentTaskState.Running; task.StartedAt = now;
            if (!await SaveQuietlyAsync(ct)) return AgentRunOutcome.Skipped;

            AgentWorkItem item = new AgentWorkItem
            {
                TargetType = task.TargetType, TargetId = task.TargetId, TargetNumber = task.TargetNumber, ContractId = task.ContractId, WorkOrderId = task.WorkOrderId, Summary = task.Summary,
            };
            AgentStepResult result;
            string failure = null;
            try
            {
                using (AgentStepContext.Enter(step.Code))
                using (IServiceScope scope = _scopes.CreateScope())
                    result = await step.RunAsync(item, scope.ServiceProvider, ct) ?? AgentStepResult.Refused("گام ایجنت نتیجه‌ای نداد.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // the service is stopping: the task goes back to the queue, no attempt is counted
                task.State = from; task.StartedAt = null;
                await SaveQuietlyAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                // a refusal of the data layer (the guard), a database error, a bug of the step: the attempt is counted and the message is kept
                _logger?.LogWarning(ex, "Agent step {Step} failed on task {Task}", step.Code, task.Id);
                failure = ex.GetType().Name;
                result = AgentStepResult.Refused(AgentText.Cut(ex.Message, 500));
            }

            now = DateTimeOffset.Now;
            string outcomeText;
            AgentRunOutcome outcome;
            bool alert = false;
            switch (result.Outcome)
            {
                case AgentStepOutcome.Done:
                    task.State = AgentTaskState.Done; task.ClosedAt = now; task.ClosedReason = AgentText.DoneByAgent; task.NextAttemptAt = null; task.LastError = null;
                    task.OutputType = AgentText.Cut(result.OutputType, 60); task.OutputId = result.OutputId; task.OutputNumber = AgentText.Cut(result.OutputNumber, 60);
                    if (!string.IsNullOrWhiteSpace(result.Message)) task.Summary = AgentText.Cut(result.Message, 1000);
                    outcomeText = "done"; outcome = AgentRunOutcome.Done;
                    break;
                case AgentStepOutcome.Waiting:
                    task.State = AgentTaskState.Queued; task.NextAttemptAt = now + WaitingDelay; task.LastError = AgentText.Cut(result.Message, 1000);
                    outcomeText = "waiting"; outcome = AgentRunOutcome.Waiting;
                    break;
                default:
                    task.AttemptCount++;
                    task.LastError = AgentText.Cut(result.Message, 1000);
                    if (task.AttemptCount >= Math.Max(1, setting.MaxAttempts))
                    {
                        task.State = AgentTaskState.Failed; task.NextAttemptAt = null; task.EscalatedAt = now; alert = true;
                        outcomeText = "failed"; outcome = AgentRunOutcome.Failed;
                    }
                    else
                    {
                        task.State = AgentTaskState.Queued; task.NextAttemptAt = now + Backoff[Math.Min(task.AttemptCount, Backoff.Length) - 1];
                        outcomeText = "refused"; outcome = AgentRunOutcome.Refused;
                    }
                    break;
            }
            AddEvent(task, from, task.State, AgentText.Cut(result.Message, 1000), now);
            _audit.Add(AiAuditKinds.Agent,
                $"step {step.Code} task {task.Id} target {task.TargetType}#{task.TargetId.ToString(CultureInfo.InvariantCulture)} -> {outcomeText}; {AgentText.Cut(result.Message, 600)}{(failure == null ? string.Empty : "; " + failure)}",
                seatCode: acting);
            if (!await SaveQuietlyAsync(ct)) return AgentRunOutcome.Skipped;
            if (alert) await AgentNotices.AlertPmoAsync(_notify, task, $"کار ایجنت «{task.Title}» ناموفق ماند: {task.LastError}", ct);
            if (outcome == AgentRunOutcome.Done && _notify != null && task.EscalatedAt.HasValue) await _notify.CloseAsync(AgentNotices.AlertKey(task.Id), AgentText.DoneByAgent, ct);
            return outcome;
        }

        private void Close(AgentTask task, AgentTaskState from, AgentTaskState to, string reason, DateTimeOffset now)
        {
            task.State = to; task.ClosedAt = to == AgentTaskState.Failed ? (DateTimeOffset?)null : now; task.ClosedReason = AgentText.Cut(reason, 300);
            AddEvent(task, from, to, reason, now);
        }

        private void AddEvent(AgentTask task, AgentTaskState? from, AgentTaskState to, string note, DateTimeOffset now) =>
            _uow.Set<AgentTaskEvent>().Add(new AgentTaskEvent { Task = task, At = now, FromState = from, ToState = to, UserId = _user.UserId, SeatCode = AiSeatContext.Code, Note = AgentText.Cut(note, 1000) });

        /// <summary>Saves the bookkeeping. A lost race (the row version, the unique index) drops the changes of this scope and answers false.</summary>
        private async Task<bool> SaveQuietlyAsync(CancellationToken ct)
        {
            try
            {
                await _uow.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex)
            {
                _logger?.LogInformation(ex, "Agent task changed by another process; this change is dropped");
                AgentDb.Clear(_uow);
                return false;
            }
        }

        public async Task<int> RequeueStaleAsync(TimeSpan staleAfter, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset limit = now - staleAfter;
            List<AgentTask> stale = await _uow.Set<AgentTask>().Where(t => !t.Deleted && t.State == AgentTaskState.Running && t.StartedAt != null && t.StartedAt < limit).ToListAsync(ct);
            if (stale.Count == 0) return 0;
            foreach (AgentTask t in stale)
            {
                t.State = AgentTaskState.Queued; t.StartedAt = null; t.NextAttemptAt = null; t.LastError = AgentText.StaleRunning;
                AddEvent(t, AgentTaskState.Running, AgentTaskState.Queued, AgentText.StaleRunning, now);
            }
            _audit.Add(AiAuditKinds.Agent, $"{stale.Count.ToString(CultureInfo.InvariantCulture)} stale running task(s) put back in the queue", seatCode: AiSeatContext.Code);
            return await SaveQuietlyAsync(ct) ? stale.Count : 0;
        }
    }
}
