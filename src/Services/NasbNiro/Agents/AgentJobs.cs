using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;

namespace Services.NasbNiro.Agents
{
    /// <summary>
    /// The job of a process agent (one registration per agent code). It reconciles the steps of the agent and does what is queued for it, as the service user of its seat (see AiActing),
    /// so every role check of the existing services applies to it. A refusal of a service is kept on the task; the run itself succeeds. Rule-based: no language model.
    /// </summary>
    public sealed class AgentWorkJob : IAiSeatJob
    {
        private readonly IAgentExecutor _executor;

        public AgentWorkJob(string agentCode, IAgentExecutor executor)
        {
            Code = agentCode ?? throw new ArgumentNullException(nameof(agentCode));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public string Code { get; }

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            AgentRunSummary summary = await _executor.RunAgentAsync(Code, ct);
            return AiSeatJobResult.Done(summary.Describe());
        }
    }

    /// <summary>
    /// The coordinator (AG-MASTER): reconciles the steps of every agent (so a gate that was passed closes, a late gate is told to the PMO, a switched-off agent hands its work to people),
    /// and puts the tasks of a dead run back in the queue. It writes only the tables of the agent layer, the AI module and the inbox, and the three tables of the standard-gap report (EnStandardGap, EnStandardGapLine, EnCaseEvent) for the one step it owns (the data layer enforces it).
    /// </summary>
    public sealed class AgentMasterJob : IAiSeatJob
    {
        /// <summary>The same time after which a seat run is taken as dead (AiSeatRunner.StaleAfter).</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

        private readonly IAgentReconciler _reconciler;
        private readonly IAgentExecutor _executor;

        public AgentMasterJob(IAgentReconciler reconciler, IAgentExecutor executor)
        {
            _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public string Code => AgentCodes.Master;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            int requeued = await _executor.RequeueStaleAsync(StaleAfter, ct);
            AgentReconcileResult reconcile = await _reconciler.ReconcileAsync(null, ct);
            AgentRunSummary own = await _executor.RunAgentAsync(Code, ct);   // the steps the coordinator owns itself (the standard-gap report): it does what is queued for it, as its own seat
            AiSeatJobResult result = AiSeatJobResult.Done(string.Create(CultureInfo.InvariantCulture, $"requeued={requeued}; {reconcile.Describe()}") + (own.Executed > 0 ? "; " + own.Describe() : string.Empty));
            result.Quiet = requeued == 0 && !reconcile.Changed && reconcile.Errors.Count == 0 && own.Executed == 0;
            return result;
        }
    }

    /// <summary>The cheap question the scheduler asks before it opens a run row for a process agent.</summary>
    public static class AgentWorkProbe
    {
        /// <summary>True when the agent has a task that it can take now: Queued or Returned, and not waiting for a later attempt.</summary>
        public static Task<bool> HasWorkAsync(IUnitOfWork uow, string agentCode, DateTimeOffset now, CancellationToken ct)
        {
            string code = agentCode?.Trim().ToUpperInvariant();
            return uow.Set<AgentTask>().AsNoTracking().AnyAsync(t => !t.Deleted && t.AgentCode == code && (t.State == AgentTaskState.Queued || t.State == AgentTaskState.Returned) && (t.NextAttemptAt == null || t.NextAttemptAt <= now), ct);
        }
    }
}
