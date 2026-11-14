using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Agents
{
    // Agent layer: the business processes are done by AI agents (one per specialty, each an AI seat) as the maker; people only decide at the gates and do the physical work.
    // Tables start with "Agent". A task is "what is next for one document at one step"; it is derived from the state of the documents, never the other way round.
    // Document references are plain ids and numbers (the documents are of many tables), the same way as the audit seat does it.

    /// <summary>Who does a step.</summary>
    public enum AgentStepKind
    {
        /// <summary>The agent makes the document (a draft, a request, an inquiry).</summary>
        Agent = 1,

        /// <summary>A person decides (approve, sign, release).</summary>
        HumanGate = 2,

        /// <summary>A person does physical work (receive goods, ship, pay at the bank).</summary>
        Physical = 3,

        /// <summary>A person gives content that only a person has (supplier quotes, a missing price).</summary>
        Input = 4,
    }

    public enum AgentTaskState
    {
        /// <summary>The agent will do it at its next run.</summary>
        Queued = 1,
        Running = 2,

        /// <summary>A person sent it back to the agent with a reason.</summary>
        Returned = 3,
        WaitingHumanGate = 4,
        WaitingPhysical = 5,
        WaitingInput = 6,

        /// <summary>The agent refused or failed as often as the step allows: a person looks at it.</summary>
        Failed = 7,

        /// <summary>Closed: the step was done, or the document left the step (a gate was passed).</summary>
        Done = 10,
        Cancelled = 11,
    }

    /// <summary>What is next for one document (target) at one step. Open while State is below 10.</summary>
    public class AgentTask : BaseEntity
    {
        /// <summary>The code of the step, for example PROC.INQ.OPEN.</summary>
        public string StepCode { get; set; }

        /// <summary>The station of the process, 1 to 13.</summary>
        public int Station { get; set; }

        /// <summary>The code of the seat that owns the step (also for a gate: whose chain it is).</summary>
        public string AgentCode { get; set; }
        public AgentStepKind Kind { get; set; }
        public AgentTaskState State { get; set; } = AgentTaskState.Queued;

        /// <summary>The CLR type name of the document, or a name like ReorderRun for work that has no document yet.</summary>
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public string TargetNumber { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }

        /// <summary>Persian.</summary>
        public string Title { get; set; }

        /// <summary>What the agent prepared (Persian).</summary>
        public string Summary { get; set; }

        /// <summary>Upper case role names separated by commas: who sees it in the gate inbox.</summary>
        public string GateRoles { get; set; }
        public int? GateUserId { get; set; }

        /// <summary>The route of the page of the document in the front end.</summary>
        public string Link { get; set; }

        /// <summary>The agent (or its step) is switched off: the task waits for a person who does the step by hand.</summary>
        public bool IsFallback { get; set; }

        public DateTimeOffset? DueAt { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public DateTimeOffset? ReturnedAt { get; set; }

        /// <summary>The PMO was told once that a gate is late or a task failed.</summary>
        public DateTimeOffset? EscalatedAt { get; set; }
        public int AttemptCount { get; set; }

        /// <summary>The last refusal or waiting reason (Persian).</summary>
        public string LastError { get; set; }
        public string OutputType { get; set; }
        public int? OutputId { get; set; }
        public string OutputNumber { get; set; }
        public string ReturnReason { get; set; }
        public int? ReturnedByUserId { get; set; }
        public string ClosedReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        public virtual ICollection<AgentTaskEvent> Events { get; set; } = new List<AgentTaskEvent>();
    }

    /// <summary>Append-only history of a task: every change of state, by the agent or by a person.</summary>
    public class AgentTaskEvent
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public virtual AgentTask Task { get; set; }
        public DateTimeOffset At { get; set; }
        public AgentTaskState? FromState { get; set; }
        public AgentTaskState ToState { get; set; }

        /// <summary>The user the change ran as: a person, or the service user of the seat.</summary>
        public int UserId { get; set; }

        /// <summary>The seat code when an agent made the change.</summary>
        public string SeatCode { get; set; }
        public string Note { get; set; }
    }

    /// <summary>The switch of one step. A row is made on the first read from the step catalogue.</summary>
    public class AgentStepSetting : BaseEntity
    {
        public string StepCode { get; set; }

        /// <summary>On by default: the agents work as soon as they are installed. A step that is switched off becomes a task for a person (the fallback).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>A refusal is tried again up to this many times, then the task fails.</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>A gate, a physical task or an input task is late after this many hours (the PMO is told once).</summary>
        public int GateDueHours { get; set; } = 48;
    }
}
