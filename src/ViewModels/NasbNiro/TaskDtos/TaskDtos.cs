using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Tasks;

namespace ViewModels.NasbNiro.TaskDtos
{
    // Task management (api/v1/tasks). Enums are numbers; the "...Text" fields are Persian (Shamsi dates). Texts the user types are plain text.

    public class TmTaskQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string Contains { get; set; }

        /// <summary>mine, assigned-by-me, assigned-to-me, overdue, waiting (waiting for my approval), done, unit (the cartables of my positions), watching, all. Default: mine.</summary>
        public string Cartable { get; set; }
        public TmTaskStatus? Status { get; set; }
        public TmTaskKind? Kind { get; set; }
        public TmPriority? Priority { get; set; }
        public int? ContractId { get; set; }
        public int? ActivityId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ParentTaskId { get; set; }
        public int? DecisionId { get; set; }
        public int? RiskId { get; set; }
        public int? IssueId { get; set; }

        /// <summary>One position's cartable (the caller must hold or act for it).</summary>
        public int? PositionId { get; set; }
        public DateTime? DueFrom { get; set; }
        public DateTime? DueTo { get; set; }
        public string Category { get; set; }
    }

    public class TmTaskListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public TmTaskKind Kind { get; set; }
        public string Category { get; set; }
        public TmPriority Priority { get; set; }
        public TmImportance Importance { get; set; }
        public OfClassification Confidentiality { get; set; }
        public TmTaskStatus Status { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public decimal PercentComplete { get; set; }
        public decimal EstimatedHours { get; set; }
        public int? ContractId { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public int? ParentTaskId { get; set; }
        public string CreatorName { get; set; }
        public List<string> AssigneeNames { get; set; } = new List<string>();
        public int ChecklistTotal { get; set; }
        public int ChecklistDone { get; set; }
        public int CommentCount { get; set; }
        public int AttachmentCount { get; set; }
    }

    public class TmAssigneeDto
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public int? PositionId { get; set; }
        public string PositionTitle { get; set; }
        public TmAssigneeRole Role { get; set; }
        public TmAssigneeState State { get; set; }
        public string Reason { get; set; }
        public int AssignedByUserId { get; set; }
        public string AssignedByName { get; set; }
        public DateTimeOffset AssignedAt { get; set; }
        public string AssignedAtText { get; set; }
        public int? ParentId { get; set; }
        public DateTimeOffset? ReadAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public string EndedAtText { get; set; }
        public string EndedByName { get; set; }
        public string EndReason { get; set; }

        /// <summary>The caller holds this referral (as the user, or as the holder or deputy of the position).</summary>
        public bool Mine { get; set; }
    }

    public class TmChecklistItemDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public string DoneByName { get; set; }
        public string DoneAtText { get; set; }
    }

    public class TmCommentDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Text { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
    }


    public class TmHistoryDto
    {
        public long Id { get; set; }
        public TmHistoryKind Kind { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
        public string FromValue { get; set; }
        public string ToValue { get; set; }
        public string Text { get; set; }
    }

    public class TmTaskDto : TmTaskListDto
    {
        public string Description { get; set; }
        public string RequestType { get; set; }
        public string Requester { get; set; }
        public string ReferringOrganisation { get; set; }
        public DateTime? StartDate { get; set; }
        public string StartDateText { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal ActualHours { get; set; }
        public decimal ActualCost { get; set; }
        public string StatusReason { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string CompletedByName { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedByName { get; set; }
        public TmRecurrence Recurrence { get; set; }
        public int RecurrenceInterval { get; set; }
        public DateTime? RecurrenceEnd { get; set; }
        public int? RecurrenceSourceId { get; set; }
        public string ReminderDays { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsNodeCode { get; set; }
        public string ActivityTitle { get; set; }
        public string ContractTitle { get; set; }
        public int? DecisionId { get; set; }
        public int? LetterId { get; set; }
        public int? RequestId { get; set; }
        public int? RiskId { get; set; }
        public int? IssueId { get; set; }
        public string ParentTaskTitle { get; set; }
        public int CreatorUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public List<TmAssigneeDto> Assignees { get; set; } = new List<TmAssigneeDto>();
        public List<TmChecklistItemDto> Checklist { get; set; } = new List<TmChecklistItemDto>();
        public List<TmCommentDto> Comments { get; set; } = new List<TmCommentDto>();
        public List<TmHistoryDto> History { get; set; } = new List<TmHistoryDto>();
        public List<int> WatcherUserIds { get; set; } = new List<int>();
        public List<TmTaskListDto> SubTasks { get; set; } = new List<TmTaskListDto>();
        public bool Watching { get; set; }

        /// <summary>The actions the caller may take now (start, complete, approve, return, reject, suspend, resume, reopen, edit, assign, transfer, progress, comment).</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();
    }

    public class TmTaskSaveDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public TmTaskKind Kind { get; set; } = TmTaskKind.Staff;
        public string Category { get; set; }
        public TmPriority Priority { get; set; } = TmPriority.Normal;
        public TmImportance Importance { get; set; } = TmImportance.Medium;
        public OfClassification Confidentiality { get; set; }
        public string RequestType { get; set; }
        public string Requester { get; set; }
        public string ReferringOrganisation { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal EstimatedHours { get; set; }
        public decimal EstimatedCost { get; set; }
        public TmRecurrence Recurrence { get; set; }
        public int RecurrenceInterval { get; set; } = 1;
        public DateTime? RecurrenceEnd { get; set; }
        public string ReminderDays { get; set; }
        public int? ParentTaskId { get; set; }
        public int? ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? DecisionId { get; set; }
        public int? LetterId { get; set; }
        public int? RequestId { get; set; }
        public int? RiskId { get; set; }
        public int? IssueId { get; set; }

        /// <summary>A new task only: the first referral (can also be done later with assign).</summary>
        public List<TmAssignTargetDto> Assignees { get; set; } = new List<TmAssignTargetDto>();
        public List<string> Checklist { get; set; } = new List<string>();
    }

    public class TmAssignTargetDto
    {
        public int? UserId { get; set; }
        public int? PositionId { get; set; }
        public TmAssigneeRole Role { get; set; } = TmAssigneeRole.Responsible;
        public string Reason { get; set; }
    }

    public class TmTaskAssignDto
    {
        public int TaskId { get; set; }
        public List<TmAssignTargetDto> Targets { get; set; } = new List<TmAssignTargetDto>();

        /// <summary>The referral this one continues (the chained view); null when the creator refers the task. An assignee may only continue his own referral.</summary>
        public int? ParentAssigneeId { get; set; }
    }

    public class TmTaskTransferDto
    {
        public int TaskId { get; set; }

        /// <summary>The referral that leaves its cartable.</summary>
        public int AssigneeId { get; set; }
        public int? ToUserId { get; set; }
        public int? ToPositionId { get; set; }

        /// <summary>The reason of the check-out (required).</summary>
        public string Reason { get; set; }
    }

    public class TmTaskWithdrawDto
    {
        public int TaskId { get; set; }
        public int AssigneeId { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>action: start, complete, approve, return, reject, suspend, resume, reopen. Reject / return / suspend / reopen need a note.</summary>
    public class TmTaskActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class TmTaskProgressDto
    {
        public int TaskId { get; set; }
        public decimal? Percent { get; set; }
        public decimal? ActualHours { get; set; }
        public decimal? ActualCost { get; set; }
        public string Note { get; set; }
    }

    /// <summary>action: add, toggle, remove (only an item that is not done).</summary>
    public class TmTaskChecklistDto
    {
        public int TaskId { get; set; }
        public string Action { get; set; }
        public int? ItemId { get; set; }
        public string Text { get; set; }
    }

    public class TmTaskCommentDto
    {
        public int TaskId { get; set; }
        public int? ParentId { get; set; }
        public string Text { get; set; }
    }



    public class TmReferralNodeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Who { get; set; }
        public string ByWhom { get; set; }
        public TmAssigneeRole Role { get; set; }
        public TmAssigneeState State { get; set; }
        public string Reason { get; set; }
        public string AssignedAtText { get; set; }
        public string EndedAtText { get; set; }
        public string EndReason { get; set; }
        public List<TmReferralNodeDto> Children { get; set; } = new List<TmReferralNodeDto>();
    }

    public class TmBoardColumnDto
    {
        public TmTaskStatus Status { get; set; }
        public int Count { get; set; }
        public List<TmTaskListDto> Items { get; set; } = new List<TmTaskListDto>();
    }

    public class TmAgendaQuery
    {
        /// <summary>day, week, month.</summary>
        public string Mode { get; set; } = "week";
        public DateTime? Date { get; set; }

        /// <summary>False: every task the caller may see; default true: his own cartable.</summary>
        public bool? MineOnly { get; set; }
    }

    public class TmAgendaItemDto
    {
        public int TaskId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public TmTaskStatus Status { get; set; }
        public TmPriority Priority { get; set; }

        /// <summary>due, start or reminder.</summary>
        public string Kind { get; set; }
        public bool Overdue { get; set; }
    }

    public class TmAgendaDayDto
    {
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public int WeekDay { get; set; }
        public List<TmAgendaItemDto> Items { get; set; } = new List<TmAgendaItemDto>();
    }

    public class TmSummaryDto
    {
        public int Total { get; set; }
        public int Open { get; set; }
        public int Closed { get; set; }
        public int Overdue { get; set; }
        public int Pending { get; set; }
        public decimal AveragePercent { get; set; }
        public Dictionary<string, int> ByStatus { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ByKind { get; set; } = new Dictionary<string, int>();
    }

    public class TmCountsDto
    {
        public int AssignedToMe { get; set; }
        public int AssignedByMe { get; set; }
        public int Overdue { get; set; }
        public int WaitingForMe { get; set; }
        public int Unit { get; set; }
    }

    public class TmReminderRunDto
    {
        public int Reminders { get; set; }
        public int Overdue { get; set; }
        public int Escalations { get; set; }
    }
}
