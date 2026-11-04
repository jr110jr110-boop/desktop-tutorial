using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tasks;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // Package P-B7a: progress-approval cartable, project registers, extended daily report. Enums are numbers; "...Text" fields are Persian with Shamsi dates.

    // ------------------------------------------------------------------------------------------ progress approval

    public class PcChainLevelDto
    {
        public int Level { get; set; }
        public string Title { get; set; }

        /// <summary>Role name (for example PROJECTMANAGER, PLANNING, PMO) or null when the level is a position.</summary>
        public string Role { get; set; }
        public int? PositionId { get; set; }
        public string PositionTitle { get; set; }
    }

    public class PcChainDto
    {
        public int ContractId { get; set; }

        /// <summary>True when the project has no chain of its own and the default one (project manager, then PMO) applies.</summary>
        public bool IsDefault { get; set; }

        /// <summary>The chain cannot be changed while a report is in approval.</summary>
        public bool Locked { get; set; }
        public int InApprovalCount { get; set; }
        public List<PcChainLevelDto> Levels { get; set; } = new List<PcChainLevelDto>();
    }

    public class PcChainSaveDto
    {
        public int ContractId { get; set; }
        public List<PcChainLevelDto> Levels { get; set; } = new List<PcChainLevelDto>();
    }

    public class PcSubmissionQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        /// <summary>mine (my reports), waiting (waiting for my decision), all (PMO and project managers).</summary>
        public string Cartable { get; set; }
        public int? ContractId { get; set; }
        public PcSubmissionState? State { get; set; }
    }

    public class PcSubmissionSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public PcSubmissionTarget Target { get; set; }
        public int? ActivityId { get; set; }
        public int? TaskId { get; set; }
        public decimal Percent { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }
        public decimal ActualCost { get; set; }
        public int CostKind { get; set; }
        public string CostNote { get; set; }
        public string Comment { get; set; }

        /// <summary>True sends the report into the chain (it is locked for the submitter from then on).</summary>
        public bool Submit { get; set; }
    }

    public class PcSubmissionDecisionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }

        /// <summary>The approver may correct the values before approving; every changed value is written into the history.</summary>
        public decimal? Percent { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }
        public decimal? ActualCost { get; set; }
        public string CostNote { get; set; }
    }

    public class PcApprovalStepDto
    {
        public int Round { get; set; }
        public int Level { get; set; }
        public string Title { get; set; }
        public string Role { get; set; }
        public string PositionTitle { get; set; }
        public PcApprovalStepState State { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string Note { get; set; }
    }

    public class PcSubmissionHistoryDto
    {
        public long Id { get; set; }
        public string Action { get; set; }
        public string ActionText { get; set; }
        public int Round { get; set; }
        public int Level { get; set; }
        public string UserName { get; set; }
        public string AtText { get; set; }
        public string Note { get; set; }
        public string ChangesJson { get; set; }
    }

    public class PcSubmissionDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public string ContractTitle { get; set; }
        public PcSubmissionTarget Target { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public int? TaskId { get; set; }
        public string TaskNumber { get; set; }
        public string TargetTitle { get; set; }
        public decimal Percent { get; set; }
        public decimal PreviousPercent { get; set; }
        public DateTime? ActualStart { get; set; }
        public string ActualStartText { get; set; }
        public DateTime? ActualFinish { get; set; }
        public string ActualFinishText { get; set; }
        public decimal ActualCost { get; set; }
        public int CostKind { get; set; }
        public string CostNote { get; set; }
        public string Comment { get; set; }
        public int AttachmentCount { get; set; }
        public PcSubmissionState State { get; set; }
        public int Round { get; set; }
        public int CurrentLevel { get; set; }
        public string CurrentLevelTitle { get; set; }
        public string SubmittedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string ResultNote { get; set; }
        public int? ManualProgressId { get; set; }
        public int? CostEntryId { get; set; }

        /// <summary>The report is locked for the submitter (in approval or approved).</summary>
        public bool Locked { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDecide { get; set; }
        public List<PcApprovalStepDto> Steps { get; set; } = new List<PcApprovalStepDto>();
        public List<PcSubmissionHistoryDto> History { get; set; } = new List<PcSubmissionHistoryDto>();
    }

    public class PcSubmissionListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public string ContractTitle { get; set; }
        public PcSubmissionTarget Target { get; set; }
        public string TargetTitle { get; set; }
        public decimal Percent { get; set; }
        public decimal PreviousPercent { get; set; }
        public decimal ActualCost { get; set; }
        public PcSubmissionState State { get; set; }
        public int Round { get; set; }
        public int CurrentLevel { get; set; }
        public string CurrentLevelTitle { get; set; }
        public string SubmittedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public int WaitingDays { get; set; }
        public bool CanDecide { get; set; }
    }

    public class PcNotUpdatedQuery
    {
        public int? ContractId { get; set; }
        public int? Days { get; set; }
    }

    public class PcNotUpdatedDto
    {
        public PcSubmissionTarget Target { get; set; }
        public int ContractId { get; set; }
        public string ContractTitle { get; set; }
        public int TargetId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string AssigneeNames { get; set; }
        public decimal Percent { get; set; }
        public string LastUpdateText { get; set; }
        public int DaysSince { get; set; }
    }

    public class PcOverdueUpdateDto
    {
        public int SubmissionId { get; set; }
        public string Number { get; set; }
        public string ContractTitle { get; set; }
        public string TargetTitle { get; set; }
        public string WaitingAt { get; set; }
        public int Level { get; set; }
        public string LevelTitle { get; set; }
        public int WaitingDays { get; set; }
    }

    // ------------------------------------------------------------------------------------------ registers

    public class PcRegisterQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int ContractId { get; set; }
        public string Contains { get; set; }
        public int? Status { get; set; }
        public string Category { get; set; }
    }

    public class PcRiskSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public string Title { get; set; }
        public string Cause { get; set; }
        public string Event { get; set; }
        public string Effect { get; set; }
        public string Category { get; set; }
        public bool IsOpportunity { get; set; }
        public int Probability { get; set; }
        public int Impact { get; set; }
        public int? OwnerPositionId { get; set; }
        public PcRiskResponse? Response { get; set; }
        public string ResponsePlan { get; set; }
        public DateTime? ReviewDate { get; set; }
        public int? ResidualProbability { get; set; }
        public int? ResidualImpact { get; set; }
    }

    public class PcRiskDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Cause { get; set; }
        public string Event { get; set; }
        public string Effect { get; set; }
        public string Category { get; set; }
        public bool IsOpportunity { get; set; }
        public int Probability { get; set; }
        public int Impact { get; set; }
        public int Score { get; set; }

        /// <summary>low, medium, high, critical (limits from the settings).</summary>
        public string Level { get; set; }
        public int? OwnerPositionId { get; set; }
        public string OwnerPositionTitle { get; set; }
        public PcRiskResponse? Response { get; set; }
        public string ResponsePlan { get; set; }
        public PcRiskStatus Status { get; set; }
        public DateTime? ReviewDate { get; set; }
        public string ReviewDateText { get; set; }
        public bool ReviewOverdue { get; set; }
        public int? ResidualProbability { get; set; }
        public int? ResidualImpact { get; set; }
        public int? ResidualScore { get; set; }
        public string ClosureNote { get; set; }
        public int ActionCount { get; set; }
        public int OpenActionCount { get; set; }
    }

    public class PcRiskStatusDto
    {
        public int Id { get; set; }
        public PcRiskStatus Status { get; set; }
        public string Note { get; set; }
    }

    public class PcHeatCellDto
    {
        public int Probability { get; set; }
        public int Impact { get; set; }
        public int Count { get; set; }
        public List<int> RiskIds { get; set; } = new List<int>();
    }

    public class PcRiskHeatMapDto
    {
        public int ContractId { get; set; }
        public int HighScore { get; set; }
        public int CriticalScore { get; set; }
        public List<PcHeatCellDto> Cells { get; set; } = new List<PcHeatCellDto>();
        public int OpenThreats { get; set; }
        public int OpenOpportunities { get; set; }
    }

    public class PcIssueSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public TmPriority Priority { get; set; } = TmPriority.Normal;
        public int? OwnerPositionId { get; set; }
        public DateTime? RaisedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int? ActivityId { get; set; }
        public string DelayReasonCode { get; set; }
    }

    public class PcIssueDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public TmPriority Priority { get; set; }
        public int? OwnerPositionId { get; set; }
        public string OwnerPositionTitle { get; set; }
        public DateTime RaisedDate { get; set; }
        public string RaisedDateText { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public PcIssueStatus Status { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string DelayReasonCode { get; set; }
        public string Resolution { get; set; }
        public string ResolvedByName { get; set; }
        public string ResolvedAtText { get; set; }
        public string ClosedByName { get; set; }
        public string ClosedAtText { get; set; }
        public int ActionCount { get; set; }
    }

    /// <summary>resolve (needs the resolution text), close (a person other than the resolver), reopen.</summary>
    public class PcIssueActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class PcStakeholderSaveDto
    {
        public int? Id { get; set; }
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
    }

    public class PcStakeholderDto : PcStakeholderSaveDto
    {
        /// <summary>manage-closely, keep-satisfied, keep-informed, monitor (the power / interest grid).</summary>
        public string Quadrant { get; set; }
        public string QuadrantText { get; set; }

        /// <summary>Desired minus current engagement: above zero means work is needed.</summary>
        public int EngagementGap { get; set; }
        public string OwnerPositionTitle { get; set; }
    }

    public class PcStakeholderMatrixDto
    {
        public int ContractId { get; set; }
        public List<PcStakeholderDto> ManageClosely { get; set; } = new List<PcStakeholderDto>();
        public List<PcStakeholderDto> KeepSatisfied { get; set; } = new List<PcStakeholderDto>();
        public List<PcStakeholderDto> KeepInformed { get; set; } = new List<PcStakeholderDto>();
        public List<PcStakeholderDto> Monitor { get; set; } = new List<PcStakeholderDto>();
    }

    public class PcLessonSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public PcPhase? Phase { get; set; }
        public string Category { get; set; }
        public bool IsPositive { get; set; }
        public string WhatHappened { get; set; }
        public string Cause { get; set; }
        public string Recommendation { get; set; }
        public bool Submit { get; set; }
    }

    public class PcLessonDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Number { get; set; }
        public PcPhase? Phase { get; set; }
        public string Category { get; set; }
        public bool IsPositive { get; set; }
        public string WhatHappened { get; set; }
        public string Cause { get; set; }
        public string Recommendation { get; set; }
        public PcApprovalState State { get; set; }
        public string CreatorName { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
    }

    /// <summary>Generic decision of a register row: Approve true accepts, false rejects (a reason is then required).</summary>
    public class PcRegisterDecisionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class PcProjDeliverableSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? MdrDeliverableId { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class PcProjDeliverableDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsNodeCode { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public int? MdrDeliverableId { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public PcDeliverableStatus Status { get; set; }
        public string DeliveredByName { get; set; }
        public string DeliveredAtText { get; set; }
        public string AcceptedByName { get; set; }
        public string AcceptedAtText { get; set; }
        public string AcceptanceNote { get; set; }
    }

    /// <summary>deliver, accept, reject (a reason), reset.</summary>
    public class PcProjDeliverableActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class PcPhotoSaveDto
    {
        public int ContractId { get; set; }
        public string Caption { get; set; }
        public DateTime? TakenDate { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
    }

    public class PcPhotoDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }

        /// <summary>The picture: the newest valid attachment of the photo (null until it is uploaded through the attachment panel).</summary>
        public Guid? DocumentId { get; set; }
        public int AttachmentCount { get; set; }
        public string Caption { get; set; }
        public DateTime TakenDate { get; set; }
        public string TakenDateText { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public bool IsVoid { get; set; }
        public string VoidReason { get; set; }
        public string UploadedByName { get; set; }
    }

    public class PcPhotoVoidDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
    }

    public class PcEvaluationScoreDto
    {
        public int Seq { get; set; }
        public string Criterion { get; set; }
        public decimal Weight { get; set; }

        /// <summary>0..5.</summary>
        public decimal Score { get; set; }
        public string Comment { get; set; }
    }

    public class PcEvaluationSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public PcEvaluationParty PartyKind { get; set; }
        public int? ProviderId { get; set; }
        public string PartyName { get; set; }
        public DateTime? EvaluationDate { get; set; }
        public string Comment { get; set; }
        public List<PcEvaluationScoreDto> Scores { get; set; } = new List<PcEvaluationScoreDto>();
        public bool Submit { get; set; }
    }

    public class PcEvaluationDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Number { get; set; }
        public PcEvaluationParty PartyKind { get; set; }
        public int? ProviderId { get; set; }
        public string PartyName { get; set; }
        public DateTime EvaluationDate { get; set; }
        public string EvaluationDateText { get; set; }
        public PcApprovalState State { get; set; }
        public decimal TotalScore { get; set; }
        public string Grade { get; set; }
        public string Comment { get; set; }
        public string EvaluatorName { get; set; }
        public List<PcEvaluationScoreDto> Scores { get; set; } = new List<PcEvaluationScoreDto>();
    }

    public class PcEvaluationTemplateDto
    {
        public PcEvaluationParty PartyKind { get; set; }
        public List<PcEvaluationScoreDto> Criteria { get; set; } = new List<PcEvaluationScoreDto>();
    }

    public class PcWeeklyGenerateDto
    {
        public int ContractId { get; set; }

        /// <summary>Any day of the week; the week is Saturday to Friday. Default: the current week.</summary>
        public DateTime? Date { get; set; }
    }

    public class PcWeeklySaveDto
    {
        public int Id { get; set; }
        public string Summary { get; set; }
        public string NextWeekPlan { get; set; }
    }

    public class PcWeeklyAutoDto
    {
        public decimal ProgressPercent { get; set; }
        public decimal? PlannedPercent { get; set; }
        public decimal? Spi { get; set; }
        public decimal? Cpi { get; set; }
        public int ActivitiesStarted { get; set; }
        public int ActivitiesFinished { get; set; }
        public int LateActivities { get; set; }
        public int NewIssues { get; set; }
        public int OpenIssues { get; set; }
        public int ClosedIssues { get; set; }
        public int OpenRisks { get; set; }
        public int HighRisks { get; set; }
        public int TasksClosed { get; set; }
        public int TasksOverdue { get; set; }
        public int DailyReports { get; set; }
        public int HseEvents { get; set; }
        public List<string> Highlights { get; set; } = new List<string>();
    }

    public class PcWeeklyReportDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Number { get; set; }
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public string WeekText { get; set; }
        public PcWeeklyState State { get; set; }
        public PcWeeklyAutoDto Auto { get; set; } = new PcWeeklyAutoDto();
        public string Summary { get; set; }
        public string NextWeekPlan { get; set; }
        public string GeneratedAtText { get; set; }
        public string ReleasedByName { get; set; }
        public string ReleasedAtText { get; set; }
    }

    public class PcPhaseEvaluationSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public PcPhase Phase { get; set; }
        public DateTime? EvaluationDate { get; set; }
        public int ScheduleScore { get; set; }
        public int CostScore { get; set; }
        public int QualityScore { get; set; }
        public int ScopeScore { get; set; }
        public string Achievements { get; set; }
        public string Problems { get; set; }
        public string Recommendation { get; set; }
        public PcPhaseDecision Decision { get; set; } = PcPhaseDecision.Proceed;
        public bool Submit { get; set; }
    }

    public class PcPhaseEvaluationDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public PcPhase Phase { get; set; }
        public DateTime EvaluationDate { get; set; }
        public string EvaluationDateText { get; set; }
        public int ScheduleScore { get; set; }
        public int CostScore { get; set; }
        public int QualityScore { get; set; }
        public int ScopeScore { get; set; }
        public decimal OverallScore { get; set; }
        public string Achievements { get; set; }
        public string Problems { get; set; }
        public string Recommendation { get; set; }
        public PcPhaseDecision Decision { get; set; }
        public PcApprovalState State { get; set; }
        public string CreatorName { get; set; }
        public string DecidedByName { get; set; }
        public string DecisionNote { get; set; }
    }

    // ------------------------------------------------------------------------------------------ one-page dashboard

    public class PcOnePageMilestoneDto
    {
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public DateTime? PlannedDate { get; set; }
        public string PlannedDateText { get; set; }
        public decimal Percent { get; set; }
        public bool Done { get; set; }
        public bool Late { get; set; }
    }

    public class PcSCurvePointDto
    {
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal? ActualPercent { get; set; }
    }

    public class PcOnePageDto
    {
        public int ContractId { get; set; }
        public string ContractTitle { get; set; }
        public string ContractNumber { get; set; }

        // charter summary
        public string CharterTitle { get; set; }
        public string CharterObjective { get; set; }
        public string CharterScope { get; set; }
        public string ProjectManagerName { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public decimal? Budget { get; set; }

        // progress
        public decimal ProgressPercent { get; set; }
        public decimal? PlannedPercent { get; set; }
        public decimal? Spi { get; set; }
        public decimal? Cpi { get; set; }
        public string Health { get; set; }
        public int LateActivities { get; set; }

        public List<PcOnePageMilestoneDto> Milestones { get; set; } = new List<PcOnePageMilestoneDto>();
        public int DeliverablesTotal { get; set; }
        public int DeliverablesAccepted { get; set; }
        public int DeliverablesOverdue { get; set; }
        public int IssuesOpen { get; set; }
        public int IssuesOverdue { get; set; }
        public int IssuesHigh { get; set; }
        public int RisksOpen { get; set; }
        public int RisksHigh { get; set; }
        public int RisksCritical { get; set; }
        public List<PcRiskDto> TopRisks { get; set; } = new List<PcRiskDto>();
        public List<PcIssueDto> TopIssues { get; set; } = new List<PcIssueDto>();
        public int TasksOpen { get; set; }
        public int TasksOverdue { get; set; }
        public int ProgressWaiting { get; set; }
        public List<PcSCurvePointDto> SCurve { get; set; } = new List<PcSCurvePointDto>();
    }

    // ------------------------------------------------------------------------------------------ daily report (appendix 5)

    public class PcDailyManpowerDto
    {
        public PcManpowerKind Kind { get; set; }
        public string Title { get; set; }
        public string ActivityType { get; set; }
        public int Count { get; set; }
        public int Present { get; set; }
        public int Absent { get; set; }
        public decimal PersonDays { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyMaterialDto
    {
        public int? ItemId { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyMachineryDto
    {
        public string Name { get; set; }
        public PcMachineOwnership Ownership { get; set; }
        public int Present { get; set; }
        public int Active { get; set; }
        public decimal Hours { get; set; }
        public string Note { get; set; }
    }

    public class PcDailyObstacleDto
    {
        public string Category { get; set; }
        public string Description { get; set; }
        public int? ActivityId { get; set; }
        public decimal ImpactHours { get; set; }
        public bool Resolved { get; set; }
    }

    public class PcDailyHseDto
    {
        public PcHseKind Kind { get; set; }
        public string Description { get; set; }
        public int PersonsInvolved { get; set; }
        public string ActionTaken { get; set; }
    }

    public class PcDailyListQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int ContractId { get; set; }
        public int? State { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class PcDailyListDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string Unit { get; set; }
        public DateTime ReportDate { get; set; }
        public string ReportDateText { get; set; }
        public int State { get; set; }
        public int LineCount { get; set; }
        public string ConfirmedByName { get; set; }
    }
}
