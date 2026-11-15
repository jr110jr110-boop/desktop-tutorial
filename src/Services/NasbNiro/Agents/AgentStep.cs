using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;

namespace Services.NasbNiro.Agents
{
    /// <summary>
    /// One step of a process. A step is a small class registered in the container (one <c>IAgentStep</c> per step; the same class may be registered twice with different codes and owners).
    /// It says (1) how to find, from the state of the documents alone, every document for which THIS step is the next thing (<see cref="DiscoverAsync"/>),
    /// (2) for an agent step how to do it by calling the existing service methods as the agent (<see cref="RunAsync"/>), and (3) for the rest who must do it and where.
    /// A step never writes through <c>IUnitOfWork</c>: it calls services, so every rule of the service (and the data-layer guard) applies to the agent like to anyone else.
    /// </summary>
    public interface IAgentStep
    {
        /// <summary>Unique, upper case, dotted, at most 60 characters, for example PROC.INQ.OPEN.</summary>
        string Code { get; }

        /// <summary>The station, 1 to 13.</summary>
        int Station { get; }

        /// <summary>The code of the seat that owns the step (also for a gate: whose chain it is).</summary>
        string AgentCode { get; }

        /// <summary>Persian.</summary>
        string Title { get; }
        AgentStepKind Kind { get; }

        /// <summary>Who does an Agent step by hand when the agent or the step is switched off (upper case roles separated by commas).</summary>
        string FallbackRoles { get; }

        /// <summary>
        /// Every document for which this step is the next thing, read from document states only (no tasks, no agent state). Read only: the reconciler gives a unit of work that must not be written.
        /// A document that stops being discovered closes its task: that is how a passed gate is seen.
        /// </summary>
        Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct);

        /// <summary>
        /// Kind Agent only. Called as the agent (the acting principal is the seat) in a scope of its own; <paramref name="services"/> is that scope. Calls existing service methods only.
        /// A refusal of the service is <see cref="AgentStepResult.Refused"/> with the message of the service; a precondition that is not met yet is <see cref="AgentStepResult.Waiting"/>.
        /// </summary>
        Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct);

        /// <summary>Who must act when the step is not done by an agent (a gate, a physical task, an input task, or the fallback of an agent step) and where.</summary>
        AgentGateInfo GateInfo(AgentWorkItem item);
    }

    /// <summary>
    /// A gate step whose "send back" is an existing return action of the document (for example a purchase request). The inbox calls it AS THE PERSON who returns,
    /// in the scope of the request. The document state then moves the chain: the gate task closes at the next reconcile.
    /// </summary>
    public interface IAgentReturnable
    {
        Task<ResponseDto<bool>> ReturnAsync(AgentWorkItem item, string reason, IServiceProvider services, CancellationToken ct);
    }

    public sealed class AgentWorkItem
    {
        /// <summary>The CLR type name of the document, or a name like ReorderRun.</summary>
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public string TargetNumber { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }

        /// <summary>What the agent prepared or what is waiting, one Persian sentence.</summary>
        public string Summary { get; set; }

        /// <summary>
        /// Optional, for a gate whose roles follow the state of the document (a purchase request that planning approved waits for management only): the roles that must act NOW, read from the
        /// state by <see cref="IAgentStep.DiscoverAsync"/>. The task takes them at birth and again at every reconcile while it waits, so the inbox shows the gate only to the roles that are still missing.
        /// </summary>
        public string GateRoles { get; set; }

        /// <summary>
        /// Optional, for a gate that only one named person may pass (the project manager of the contract authorises a progress statement): read from the state by <see cref="IAgentStep.DiscoverAsync"/> and
        /// returned by <see cref="IAgentStep.GateInfo"/> as <see cref="AgentGateInfo.UserId"/>. The task keeps it up to date while it waits.
        /// </summary>
        public int? GateUserId { get; set; }
    }

    public enum AgentStepOutcome
    {
        Done = 1,
        Waiting = 2,
        Refused = 3,
    }

    public sealed class AgentStepResult
    {
        public AgentStepOutcome Outcome { get; set; }
        public string OutputType { get; set; }
        public int? OutputId { get; set; }
        public string OutputNumber { get; set; }

        /// <summary>The summary of a done step, or the Persian reason of a waiting or refused one.</summary>
        public string Message { get; set; }

        public static AgentStepResult Done(string outputType, int? outputId, string outputNumber, string summary) =>
            new AgentStepResult { Outcome = AgentStepOutcome.Done, OutputType = outputType, OutputId = outputId, OutputNumber = outputNumber, Message = summary };

        /// <summary>A precondition is not met yet. No attempt is counted; the step is tried again later.</summary>
        public static AgentStepResult Waiting(string persianReason) => new AgentStepResult { Outcome = AgentStepOutcome.Waiting, Message = persianReason };

        /// <summary>The service answered Success = false (its message goes to the task as it is). An attempt is counted.</summary>
        public static AgentStepResult Refused(string persianMessage) => new AgentStepResult { Outcome = AgentStepOutcome.Refused, Message = persianMessage };
    }

    public sealed class AgentGateInfo
    {
        /// <summary>Upper case role names separated by commas.</summary>
        public string Roles { get; set; }

        /// <summary>A person instead of (or besides) the roles.</summary>
        public int? UserId { get; set; }

        /// <summary>The route of the page of the document.</summary>
        public string Link { get; set; }

        /// <summary>The title of the task (Persian); the reconciler adds the number of the document when this is empty.</summary>
        public string Title { get; set; }

        /// <summary>Hours until the task is late; empty takes the setting of the step.</summary>
        public int? DueHours { get; set; }

        /// <summary>The gate already has an item in the office inbox (the procurement gates do): the reconciler does not mirror it.</summary>
        public bool HasOfficeInboxItem { get; set; }
    }
}
