using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control, priority 4, phase 4c: resource over-allocation alerts and the levelling proposals for them.
    // The planning agent (RES.LOAD.CHECK) finds the over-allocations and (RES.LEVEL.PROPOSE) prepares a proposal; people decide: the planner applies or rejects it, a second person (PMO) confirms it.
    // The AI never applies a schedule change and never accepts an over-allocation.

    /// <summary>Lapsed: the run ended before today and nobody fixed or accepted it (the days simply passed); it is counted apart from Resolved.</summary>
    public enum PcOverloadStatus { Open = 1, Resolved = 2, Accepted = 3, Lapsed = 4 }

    public enum PcLevelingKind { ShiftWithinFloat = 1, DelayBeyondFloat = 2 }

    /// <summary>ClosedUnreverted: the second person closed an applied change whose automatic revert is impossible (the constraint was changed again); the dates stay as they are.</summary>
    public enum PcLevelingStatus { Proposed = 1, Applied = 2, Rejected = 3, Superseded = 4, ClosedUnreverted = 5 }

    /// <summary>
    /// A maximal run of consecutive working days on which the approved resource plans of all projects load a resource beyond its capacity. Open until the plan or the schedule changes (Resolved)
    /// or the PMO accepts it on purpose with a reason (Accepted). At most one open row per resource and first day.
    /// </summary>
    public class PcResourceOverload : BaseEntity
    {
        public int ResourceId { get; set; }
        public PcResource Resource { get; set; }

        /// <summary>First and last over-capacity day of the run (dates, no time).</summary>
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>The largest daily load of the run and the capacity of that day.</summary>
        public decimal PeakLoad { get; set; }
        public decimal PeakCapacity { get; set; }

        public PcOverloadStatus Status { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }

        public int? AcceptedByUserId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string AcceptReason { get; set; }

        /// <summary>The agent looked for a levelling proposal and found no activity it may move; it looks again when the row changes or after a day.</summary>
        public DateTimeOffset? NoProposalAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// A proposal to move one activity so that an over-allocation goes away. Made by the planning agent (never applied by it): the planner applies or rejects it (Applied changes the dates through the schedule service) and a second
    /// person (PMO, not the applier) confirms the change; until then it is "waiting for the second person", and when that person refuses, the change is taken back through the same service.
    /// </summary>
    public class PcLevelingProposal : BaseEntity
    {
        public int OverloadId { get; set; }
        public PcResourceOverload Overload { get; set; }
        public int ContractId { get; set; }
        public int ActivityId { get; set; }
        public PcActivity Activity { get; set; }
        public PcLevelingKind Kind { get; set; }

        public DateTime CurrentStart { get; set; }
        public DateTime CurrentFinish { get; set; }
        public DateTime ProposedStart { get; set; }
        public DateTime ProposedFinish { get; set; }

        /// <summary>Working days (of the calendar of the project) the start moves.</summary>
        public int ShiftDays { get; set; }

        /// <summary>Working days the finish of the project moves later (0 when the move stays within the float).</summary>
        public int CriticalImpactDays { get; set; }

        /// <summary>The contract has a current baseline: the schedule changes only through a change request, so the proposal cannot be applied directly.</summary>
        public bool NeedsChangeRequest { get; set; }

        public string Note { get; set; }

        public PcLevelingStatus Status { get; set; }
        public bool PreparedByAgent { get; set; }
        public int MakerUserId { get; set; }

        /// <summary>The constraint of the activity before the proposal was applied (to take the change back).</summary>
        public PcConstraint PriorConstraintType { get; set; }
        public DateTime? PriorConstraintDate { get; set; }
        public DateTime? AppliedConstraintDate { get; set; }

        public int? ApplierUserId { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        /// <summary>The second person refused: the change was taken back (the proposal is then Rejected).</summary>
        public bool Reverted { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
