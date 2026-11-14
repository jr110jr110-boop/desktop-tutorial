using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Agents;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.AgentDtos
{
    // Agent layer: the gate inbox, the tasks, the master board and the step switches.

    public class AgentInboxQuery : PaginationParams
    {
        /// <summary>HumanGate: waiting for a decision; Physical: physical work; Input: information a person must give (also the tasks an agent that is switched off left to people); Agent: only those fallback tasks.</summary>
        public AgentStepKind? Kind { get; set; }
        public int? Station { get; set; }
        public int? ContractId { get; set; }
    }

    /// <summary>One line of the gate inbox: what is waiting for a person, what the agent prepared, and where to go.</summary>
    public class AgentInboxItemDto
    {
        public int Id { get; set; }
        public string StepCode { get; set; }
        public int Station { get; set; }
        public string StationTitle { get; set; }
        public string AgentCode { get; set; }

        /// <summary>The Persian title of the seat of the agent (from the seat catalogue).</summary>
        public string AgentTitle { get; set; }
        public AgentStepKind Kind { get; set; }
        public AgentTaskState State { get; set; }
        public string Title { get; set; }
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public string TargetNumber { get; set; }
        public int? ContractId { get; set; }

        /// <summary>What the agent prepared (Persian).</summary>
        public string Summary { get; set; }
        public string OutputType { get; set; }
        public string OutputNumber { get; set; }
        public string GateRoles { get; set; }

        /// <summary>The route of the existing page of the document.</summary>
        public string Link { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public int AgeHours { get; set; }
        public DateTimeOffset? DueAt { get; set; }
        public bool Overdue { get; set; }

        /// <summary>The agent or its step is switched off, so a person does the step by hand.</summary>
        public bool IsFallback { get; set; }
        public string LastError { get; set; }
        public string ReturnReason { get; set; }
    }

    public class AgentInboxCountsDto
    {
        public int HumanGate { get; set; }
        public int Physical { get; set; }
        public int Input { get; set; }
        public int Total { get; set; }
        public int Overdue { get; set; }
    }

    public class AgentReturnDto
    {
        public int Id { get; set; }

        /// <summary>Required.</summary>
        public string Reason { get; set; }
    }

    public class AgentTaskQuery : PaginationParams
    {
        public AgentTaskState? State { get; set; }
        public string Agent { get; set; }
        public int? Station { get; set; }
        public int? ContractId { get; set; }
        public string StepCode { get; set; }

        /// <summary>Only tasks that are not closed.</summary>
        public bool? OpenOnly { get; set; }

        /// <summary>True: only the tasks of no contract (the company row of the board).</summary>
        public bool? NoContract { get; set; }
    }

    public class AgentTaskDto : AgentInboxItemDto
    {
        public int? WorkOrderId { get; set; }
        public int? GateUserId { get; set; }
        public int AttemptCount { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedReason { get; set; }
        public DateTimeOffset? ReturnedAt { get; set; }
        public int? ReturnedByUserId { get; set; }
        public DateTimeOffset? EscalatedAt { get; set; }
        public int? OutputId { get; set; }
        public List<AgentTaskEventDto> Events { get; set; } = new List<AgentTaskEventDto>();
    }

    public class AgentTaskEventDto
    {
        public int Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
        public AgentTaskState? FromState { get; set; }
        public AgentTaskState ToState { get; set; }
        public int UserId { get; set; }
        public string SeatCode { get; set; }

        /// <summary>The display name of the person, or the Persian title of the seat when a seat acted.</summary>
        public string UserName { get; set; }
        public string Note { get; set; }
    }

    public class AgentRetryDto
    {
        public int Id { get; set; }
    }

    // ------------------------------------------------------------------------------------------ the master board

    /// <summary>The state of one station of one contract. Order matters: a higher value is worse, and the cell shows the worst.</summary>
    public enum AgentCellStatus
    {
        /// <summary>No task of this station for this contract (yet).</summary>
        NotStarted = 0,

        /// <summary>Only closed tasks: no open work. It does not say the station is complete.</summary>
        Idle = 1,

        /// <summary>An agent has work queued, running or returned.</summary>
        Agent = 2,
        WaitingInput = 3,
        WaitingPhysical = 4,
        WaitingGate = 5,
        Failed = 6,
    }

    public class AgentBoardCellDto
    {
        public int Station { get; set; }
        public AgentCellStatus Status { get; set; }

        /// <summary>The number of open tasks.</summary>
        public int OpenCount { get; set; }

        /// <summary>The roles the worst open task waits for.</summary>
        public string WaitingOn { get; set; }

        /// <summary>The age of the oldest open task in hours.</summary>
        public int OldestHours { get; set; }
        public int? TaskId { get; set; }
        public string Title { get; set; }
    }

    public class AgentBoardRowDto
    {
        /// <summary>Empty for the row "company wide" (tasks that belong to no contract).</summary>
        public int? ContractId { get; set; }
        public string Label { get; set; }
        public List<AgentBoardCellDto> Cells { get; set; } = new List<AgentBoardCellDto>();
    }

    public enum AgentStuckReason
    {
        Failed = 1,
        GateOverdue = 2,

        /// <summary>The agent or its step is switched off and a person must do the step by hand.</summary>
        AgentOff = 3,
    }

    public class AgentStuckDto
    {
        public int TaskId { get; set; }
        public AgentStuckReason Reason { get; set; }
        public int Station { get; set; }
        public string StationTitle { get; set; }
        public string StepCode { get; set; }
        public string Title { get; set; }
        public int? ContractId { get; set; }
        public string ContractLabel { get; set; }
        public string WaitingOn { get; set; }
        public int AgeHours { get; set; }
        public string Error { get; set; }
        public string Link { get; set; }
    }

    public class AgentStationDto
    {
        public int Number { get; set; }
        public string Title { get; set; }
    }

    public class AgentBoardDto
    {
        public DateTimeOffset GeneratedAt { get; set; }
        public List<AgentStationDto> Stations { get; set; } = new List<AgentStationDto>();
        public List<AgentBoardRowDto> Rows { get; set; } = new List<AgentBoardRowDto>();
        public List<AgentStuckDto> Stuck { get; set; } = new List<AgentStuckDto>();
    }

    // ------------------------------------------------------------------------------------------ steps

    public class AgentStepDto
    {
        public string Code { get; set; }
        public int Station { get; set; }
        public string StationTitle { get; set; }
        public string AgentCode { get; set; }
        public string Title { get; set; }
        public AgentStepKind Kind { get; set; }
        public string FallbackRoles { get; set; }
        public bool Enabled { get; set; }

        /// <summary>The seat of the agent is switched on (a step of a switched-off seat is done by people).</summary>
        public bool SeatEnabled { get; set; }
        public int MaxAttempts { get; set; }
        public int GateDueHours { get; set; }
        public int OpenTasks { get; set; }
    }

    public class AgentStepEnableDto
    {
        public string Code { get; set; }
        public bool Enabled { get; set; }
    }
}
