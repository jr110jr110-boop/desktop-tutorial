using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Office;

namespace Entities.Model.NasbNiro.Tasks
{
    // Task management (TMS, package P-B7a): staff, project, workshop, personal and meeting-action tasks in one structure, linked to the WBS in both directions.
    // Tables start with "Tm". Numbers are stored; never renumber.

    public enum TmTaskKind { Staff = 1, Project = 2, Workshop = 3, Personal = 4, MeetingAction = 5 }

    /// <summary>
    /// The status dictionary of the requirement (Open, In-Progress, Pending, Assigned, Closed, Rejected) plus Suspended.
    /// Open: registered, nobody works on it yet. Assigned: referred to its assignees, not started. InProgress: somebody started it.
    /// Pending: the work is reported done and waits for the approval of somebody other than the person who reported it. Closed: approved. Rejected: refused by an assignee or the approver. Suspended: put on hold.
    /// </summary>
    public enum TmTaskStatus { Open = 0, InProgress = 1, Pending = 2, Assigned = 3, Closed = 4, Rejected = 5, Suspended = 6 }

    public enum TmPriority { Low = 1, Normal = 2, High = 3, Critical = 4 }

    public enum TmImportance { Low = 1, Medium = 2, High = 3 }

    public enum TmAssigneeRole { Responsible = 1, Participant = 2, Observer = 3 }

    /// <summary>Active: holds the task. Done: reported his part done. Declined: refused the task. Transferred: the task left his cartable (check-out with a reason). Withdrawn: the referral was taken back.</summary>
    public enum TmAssigneeState { Active = 0, Done = 1, Declined = 2, Transferred = 3, Withdrawn = 4 }

    public enum TmRecurrence { None = 0, Daily = 1, Weekly = 2, Monthly = 3, Yearly = 4 }

    public enum TmHistoryKind { Created = 1, Field = 2, Status = 3, Referral = 4, Transfer = 5, Comment = 6, Checklist = 7, Attachment = 8, Link = 9, Progress = 10, Reminder = 11, Watcher = 12 }

    public class TmTask : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public TmTaskKind Kind { get; set; }

        /// <summary>Free dictionary text (for example «پیگیری»، «خرید»، «اداری»).</summary>
        public string Category { get; set; }
        public TmPriority Priority { get; set; } = TmPriority.Normal;
        public TmImportance Importance { get; set; } = TmImportance.Medium;

        /// <summary>Normal, Confidential or Secret (the classification of the letters module): a confidential task is seen only by its creator, assignees, watchers and the CEO.</summary>
        public OfClassification Confidentiality { get; set; }

        // the managerial subject (requirement 2.1)
        public string RequestType { get; set; }
        public string Requester { get; set; }
        public string ReferringOrganisation { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? DueDate { get; set; }

        public decimal EstimatedHours { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal ActualHours { get; set; }
        public decimal ActualCost { get; set; }

        /// <summary>0..100. A closed task counts as 100 in the roll-up to the WBS activity.</summary>
        public decimal PercentComplete { get; set; }

        public TmTaskStatus Status { get; set; }

        /// <summary>The status before the task was suspended (the status it returns to).</summary>
        public TmTaskStatus? StatusBeforeSuspend { get; set; }

        /// <summary>The reason given with the last status change (a rejection, a return, a suspension).</summary>
        public string StatusReason { get; set; }

        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>The person who reported the task done; the approver is never this person.</summary>
        public int? CompletedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        // recurrence: when a recurring task is closed, the next occurrence is created
        public TmRecurrence Recurrence { get; set; }
        public int RecurrenceInterval { get; set; } = 1;
        public DateTime? RecurrenceEnd { get; set; }

        /// <summary>The first task of a recurring series (null for the first one).</summary>
        public int? RecurrenceSourceId { get; set; }

        /// <summary>Days before the due date at which the assignees are reminded, comma separated («7,3,1,0»). Empty: the company default.</summary>
        public string ReminderDays { get; set; }

        public int? ParentTaskId { get; set; }

        // links (both ways with the WBS: the activity lists its tasks)
        public int? ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? DecisionId { get; set; }
        public int? LetterId { get; set; }
        public int? RequestId { get; set; }

        /// <summary>A risk response action or an issue action is a task (the registers of the project site).</summary>
        public int? RiskId { get; set; }
        public int? IssueId { get; set; }

        public ICollection<TmTaskAssignee> Assignees { get; set; }
        public ICollection<TmTaskChecklistItem> Checklist { get; set; }
        public ICollection<TmTaskComment> Comments { get; set; }
        public ICollection<TmTaskWatcher> Watchers { get; set; }
        public ICollection<TmTaskAttachment> Attachments { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// One referral of a task to a person or a position (the cartable). Rows are never deleted and their who, why and when never change: a transfer ends the old row (state, end time, reason)
    /// and adds a new one that points at it, so the chain of referrals is a tree that can always be read back.
    /// </summary>
    public class TmTaskAssignee
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public int? UserId { get; set; }
        public int? PositionId { get; set; }
        public TmAssigneeRole Role { get; set; }
        public TmAssigneeState State { get; set; }

        /// <summary>The reason of the referral («جهت اقدام»، «جهت اطلاع» ...).</summary>
        public string Reason { get; set; }
        public int AssignedByUserId { get; set; }
        public DateTimeOffset AssignedAt { get; set; }

        /// <summary>The referral this one came from (the chained view); null for the first level.</summary>
        public int? ParentId { get; set; }
        public DateTimeOffset? ReadAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? EndedByUserId { get; set; }
        public string EndReason { get; set; }
    }

    public class TmTaskChecklistItem
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public int Seq { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public int? DoneByUserId { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
    }

    /// <summary>Threaded comments. A comment is never edited or deleted.</summary>
    public class TmTaskComment
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public int? ParentId { get; set; }
        public int UserId { get; set; }
        public string Text { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public class TmTaskWatcher
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
    }

    /// <summary>
    /// The files of a task: a link between the task and an uploaded document (Documents upload). Minimal on purpose, so it folds into a generic record attachment later.
    /// A file is voided with a reason (and may be superseded by a newer one); it is never removed.
    /// </summary>
    public class TmTaskAttachment
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public Guid DocumentId { get; set; }
        public string FileName { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public int? SupersedesId { get; set; }
        public bool IsVoid { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }
    }

    /// <summary>The full history of a task (append only): status, referral, comment, field changes, links.</summary>
    public class TmTaskHistory
    {
        public long Id { get; set; }
        public int TaskId { get; set; }
        public TmTask Task { get; set; }
        public DateTimeOffset At { get; set; }
        public int UserId { get; set; }
        public TmHistoryKind Kind { get; set; }
        public string FromValue { get; set; }
        public string ToValue { get; set; }
        public string Text { get; set; }
    }
}
