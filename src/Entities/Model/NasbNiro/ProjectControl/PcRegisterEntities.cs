using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Tasks;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Package P-B7a: the progress-approval cartable (appendix 1), the PMBOK registers of the project site (appendix 2) and the extended daily site report (appendix 5).
    // Numbers are stored; never renumber.

    // ------------------------------------------------------------------------------------------ progress-approval cartable

    public enum PcSubmissionTarget { Activity = 1, Task = 2 }

    /// <summary>Draft: the resource may still edit. InApproval: locked for the submitter. Approved: applied to the plan. Returned: back to the submitter with the reason (editable again).</summary>
    public enum PcSubmissionState { Draft = 0, InApproval = 1, Approved = 2, Returned = 3 }

    public enum PcApprovalStepState { Pending = 0, Approved = 1, Returned = 2 }

    /// <summary>One level of the approval chain of a project: a role or a named position. Levels are numbered from 1; the last one is the PMO (or planning) who writes the approved value to the plan.</summary>
    public class PcProgressChainLevel
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int Level { get; set; }
        public string Title { get; set; }

        /// <summary>Upper case role name (for example PROJECTMANAGER, PMO); null when the level is a position.</summary>
        public string Role { get; set; }

        /// <summary>The position of the organisation chart (OfPosition) whose holder or deputy approves; null when the level is a role.</summary>
        public int? PositionId { get; set; }
    }

    /// <summary>The progress a resource reports for one activity or one task, with its approval round.</summary>
    public class PcProgressSubmission : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public PcSubmissionTarget Target { get; set; }
        public int? ActivityId { get; set; }
        public int? TaskId { get; set; }

        public decimal Percent { get; set; }

        /// <summary>The percent of the activity or task when the report was made (what the approver compares with).</summary>
        public decimal PreviousPercent { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }

        /// <summary>The cost incurred in this report (rial). After the chain approves it becomes a manual cost entry that a second finance person checks before it counts.</summary>
        public decimal ActualCost { get; set; }
        public int CostKind { get; set; }

        /// <summary>The accrual note: what the cost is for (required when there is a cost).</summary>
        public string CostNote { get; set; }
        public string Comment { get; set; }

        /// <summary>The person who last chose the percent (the submitter, or the approver who corrected it at an intermediate level). He is the proposer of the plan record.</summary>
        public int? PercentSetByUserId { get; set; }

        /// <summary>The person who last chose the cost amount: the maker of the cost entry. The finance check excludes him and everyone who decided the report.</summary>
        public int? CostSetByUserId { get; set; }

        public PcSubmissionState State { get; set; }

        /// <summary>1 for the first send; a returned report that is sent again starts a new round (the old rounds stay in the history).</summary>
        public int Round { get; set; }

        /// <summary>The level that decides now (0 when the chain is finished or not started).</summary>
        public int CurrentLevel { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? SubmittedByUserId { get; set; }
        public string ResultNote { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int? ManualProgressId { get; set; }
        public int? CostEntryId { get; set; }

        public ICollection<PcProgressApproval> Steps { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A level of the chain as it was when the report was sent (a later change of the chain does not touch a report in flight).</summary>
    public class PcProgressApproval
    {
        public int Id { get; set; }
        public int SubmissionId { get; set; }
        public PcProgressSubmission Submission { get; set; }
        public int Round { get; set; }
        public int Level { get; set; }
        public string Title { get; set; }
        public string Role { get; set; }
        public int? PositionId { get; set; }
        public PcApprovalStepState State { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Append only history of a report: sent, edited by an approver (with the changed values), approved, returned, applied.</summary>
    public class PcProgressSubmissionHistory
    {
        public long Id { get; set; }
        public int SubmissionId { get; set; }
        public PcProgressSubmission Submission { get; set; }
        public DateTimeOffset At { get; set; }
        public int UserId { get; set; }
        public string Action { get; set; }
        public int Round { get; set; }
        public int Level { get; set; }
        public string Note { get; set; }
        public string ChangesJson { get; set; }
    }

    // ------------------------------------------------------------------------------------------ PMBOK registers

    public enum PcApprovalState { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

    public enum PcRiskResponse { Avoid = 1, Mitigate = 2, Transfer = 3, Accept = 4, Escalate = 5, Exploit = 6, Enhance = 7, Share = 8 }

    public enum PcRiskStatus { Identified = 0, Assessed = 1, Responding = 2, Occurred = 3, Closed = 4 }

    /// <summary>Risk register (PMBOK): cause, event and effect; probability x impact on a 1..5 scale gives the score (1..25).</summary>
    public class PcRisk : BaseEntity
    {
        public int ContractId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Cause { get; set; }
        public string Event { get; set; }
        public string Effect { get; set; }
        public string Category { get; set; }

        /// <summary>A positive risk (opportunity).</summary>
        public bool IsOpportunity { get; set; }
        public int Probability { get; set; }
        public int Impact { get; set; }

        /// <summary>Probability x impact, stored so lists can sort and filter on it.</summary>
        public int Score { get; set; }
        public int? OwnerPositionId { get; set; }
        public PcRiskResponse? Response { get; set; }
        public string ResponsePlan { get; set; }
        public PcRiskStatus Status { get; set; }
        public DateTime? ReviewDate { get; set; }
        public int? ResidualProbability { get; set; }
        public int? ResidualImpact { get; set; }
        public int? ResidualScore { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public string ClosureNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum PcIssueStatus { Open = 0, InProgress = 1, Resolved = 2, Closed = 3 }

    /// <summary>Issue log: the person who resolved an issue is not the person who closes it.</summary>
    public class PcIssue : BaseEntity
    {
        public int ContractId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public TmPriority Priority { get; set; } = TmPriority.Normal;
        public int? OwnerPositionId { get; set; }
        public DateTime RaisedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public PcIssueStatus Status { get; set; }
        public int? ActivityId { get; set; }

        /// <summary>The delay reason code of the daily report that this issue explains.</summary>
        public string DelayReasonCode { get; set; }
        public string Resolution { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public int? ResolvedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum PcEngagement { Unaware = 1, Resistant = 2, Neutral = 3, Supportive = 4, Leading = 5 }

    /// <summary>Stakeholder register: roles and organisations, not private contact data.</summary>
    public class PcStakeholder : BaseEntity
    {
        public int ContractId { get; set; }
        public string Name { get; set; }
        public string Organization { get; set; }
        public string RoleTitle { get; set; }
        public string Category { get; set; }
        public int Influence { get; set; }
        public int Interest { get; set; }
        public PcEngagement EngagementCurrent { get; set; } = PcEngagement.Neutral;
        public PcEngagement EngagementDesired { get; set; } = PcEngagement.Supportive;
        public string Expectations { get; set; }
        public string CommunicationNeed { get; set; }
        public string CommunicationFrequency { get; set; }
        public int? OwnerPositionId { get; set; }
        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Lessons learned: approved by the PMO, never by its author.</summary>
    public class PcLesson : BaseEntity
    {
        public int ContractId { get; set; }
        public string Number { get; set; }
        public PcPhase? Phase { get; set; }
        public string Category { get; set; }
        public bool IsPositive { get; set; }
        public string WhatHappened { get; set; }
        public string Cause { get; set; }
        public string Recommendation { get; set; }
        public PcApprovalState State { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum PcDeliverableStatus { Planned = 0, InProgress = 1, Delivered = 2, Accepted = 3, Rejected = 4 }

    /// <summary>Project deliverables list, linked to the WBS, an activity and the engineering document register (MDR). Accepted by somebody other than the one who delivered it.</summary>
    public class PcProjectDeliverable : BaseEntity
    {
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? MdrDeliverableId { get; set; }
        public DateTime? DueDate { get; set; }
        public PcDeliverableStatus Status { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public int? DeliveredByUserId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public int? AcceptedByUserId { get; set; }
        public string AcceptanceNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Project photo library. The image is a record attachment (owner PcProjectPhoto, kind Photo); a photo is voided with a reason, never deleted.</summary>
    public class PcProjectPhoto : BaseEntity
    {
        public int ContractId { get; set; }
        public string Caption { get; set; }
        public DateTime TakenDate { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public bool IsVoid { get; set; }
        public string VoidReason { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
    }

    public enum PcEvaluationParty { Contractor = 1, Consultant = 2 }

    /// <summary>Contractor / consultant evaluation questionnaire at project close. The evaluator is not the buyer who ordered from the party.</summary>
    public class PcPartyEvaluation : BaseEntity
    {
        public int ContractId { get; set; }
        public string Number { get; set; }
        public PcEvaluationParty PartyKind { get; set; }
        public int? ProviderId { get; set; }
        public string PartyName { get; set; }
        public DateTime EvaluationDate { get; set; }
        public PcApprovalState State { get; set; }

        /// <summary>Weighted score 0..100 (computed when the questionnaire is submitted).</summary>
        public decimal TotalScore { get; set; }
        public string Grade { get; set; }
        public string Comment { get; set; }
        public int? EvaluatorUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public ICollection<PcPartyEvaluationScore> Scores { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcPartyEvaluationScore
    {
        public int Id { get; set; }
        public int EvaluationId { get; set; }
        public PcPartyEvaluation Evaluation { get; set; }
        public int Seq { get; set; }
        public string Criterion { get; set; }
        public decimal Weight { get; set; }

        /// <summary>0..5.</summary>
        public decimal Score { get; set; }
        public string Comment { get; set; }
    }

    public enum PcWeeklyState { Draft = 0, Released = 1 }

    /// <summary>Weekly report: generated from progress, issues, risks and tasks; the summary is edited by the project team and the PMO releases it (a released report is read only).</summary>
    public class PcWeeklyReport : BaseEntity
    {
        public int ContractId { get; set; }
        public string Number { get; set; }
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public PcWeeklyState State { get; set; }

        /// <summary>The generated figures (JSON), refreshed on every generation until release.</summary>
        public string AutoJson { get; set; }
        public string Summary { get; set; }
        public string NextWeekPlan { get; set; }
        public DateTimeOffset? GeneratedAt { get; set; }
        public int? LastEditorUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum PcPhaseDecision { Proceed = 1, ProceedWithConditions = 2, Hold = 3 }

    /// <summary>Phase evaluation: a scored review at the end of a project phase, approved by the PMO and not by its author.</summary>
    public class PcPhaseEvaluation : BaseEntity
    {
        public int ContractId { get; set; }
        public PcPhase Phase { get; set; }
        public DateTime EvaluationDate { get; set; }

        /// <summary>Scores 1..5.</summary>
        public int ScheduleScore { get; set; }
        public int CostScore { get; set; }
        public int QualityScore { get; set; }
        public int ScopeScore { get; set; }
        public decimal OverallScore { get; set; }
        public string Achievements { get; set; }
        public string Problems { get; set; }
        public string Recommendation { get; set; }
        public PcPhaseDecision Decision { get; set; } = PcPhaseDecision.Proceed;
        public PcApprovalState State { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ daily site report (appendix 5)

    public enum PcManpowerKind { Internal = 1, Contractor = 2 }
    public enum PcMachineOwnership { Owned = 1, Hired = 2 }
    public enum PcHseKind { NearMiss = 1, FirstAid = 2, LostTime = 3, PropertyDamage = 4, Environmental = 5, Observation = 6 }

    /// <summary>Manpower of the day: own staff by position (present / absent) and contractors by activity type with person-days.</summary>
    public class PcDailyManpower
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public PcManpowerKind Kind { get; set; }

        /// <summary>Internal: the position or trade. Contractor: the company.</summary>
        public string Title { get; set; }

        /// <summary>Contractor only: the type of work.</summary>
        public string ActivityType { get; set; }

        /// <summary>Internal: head count planned for the day.</summary>
        public int Count { get; set; }
        public int Present { get; set; }
        public int Absent { get; set; }

        /// <summary>Contractor: person-days worked.</summary>
        public decimal PersonDays { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyMaterial
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public int? ItemId { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyMachinery
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public string Name { get; set; }
        public PcMachineOwnership Ownership { get; set; }
        public int Present { get; set; }
        public int Active { get; set; }
        public decimal Hours { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyObstacle
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public int? ActivityId { get; set; }
        public decimal ImpactHours { get; set; }
        public bool Resolved { get; set; }
    }

    /// <summary>HSE events of the day. Describes what happened, never who (no personal names).</summary>
    public class PcDailyHse
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public PcHseKind Kind { get; set; }
        public string Description { get; set; }
        public int PersonsInvolved { get; set; }
        public string ActionTaken { get; set; }
    }
}
