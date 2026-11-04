using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tasks;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Files;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.TaskDtos;

namespace Services.NasbNiro.Tasks
{
    public interface ITaskService
    {
        Task<ResponseDto<PagedList<TmTaskListDto>>> GetTasksAsync(TmTaskQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> GetTaskAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> SaveTaskAsync(TmTaskSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> ActionAsync(TmTaskActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> AssignAsync(TmTaskAssignDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> TransferAsync(TmTaskTransferDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> WithdrawAsync(TmTaskWithdrawDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<TmReferralNodeDto>>> GetReferralTreeAsync(int taskId, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> UpdateProgressAsync(TmTaskProgressDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> ChecklistAsync(TmTaskChecklistDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> CommentAsync(TmTaskCommentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> WatchAsync(int taskId, bool watch, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> AttachAsync(TmTaskAttachDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> VoidAttachmentAsync(TmTaskVoidDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<TmBoardColumnDto>>> GetBoardAsync(TmTaskQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<TmAgendaDayDto>>> GetAgendaAsync(TmAgendaQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<TmTaskListDto>>> GetByActivityAsync(int activityId, CancellationToken cancellationToken);
        Task<ResponseDto<TmSummaryDto>> GetSummaryAsync(int? contractId, CancellationToken cancellationToken);
        Task<ResponseDto<TmCountsDto>> GetCountsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<TmTaskDto>> FromDecisionAsync(int decisionId, CancellationToken cancellationToken);

        /// <summary>The daily job: due reminders, overdue alerts and the escalation to the creator. Idempotent (one inbox item per task, offset and recipient).</summary>
        Task<ResponseDto<TmReminderRunDto>> RunRemindersAsync(CancellationToken cancellationToken);

        /// <summary>True when the caller may see the task (the rule that also decides who reads the files of a task).</summary>
        Task<bool> CanReadTaskAsync(int taskId, CancellationToken cancellationToken);

        /// <summary>The task as a plain row for the other modules (progress approval): null when it does not exist or the caller may not see it.</summary>
        Task<TmTask> FindVisibleAsync(int taskId, bool track, CancellationToken cancellationToken);

        /// <summary>
        /// Used by the progress-approval cartable after the whole chain approved: sets the percent, hours and dates of the task, keeps the status machine honest (0 to more than 0 starts it), writes the history
        /// and rolls the percent up into the WBS activity. The caller saves nothing else for the task.
        /// </summary>
        Task<ResponseDto<bool>> ApplyApprovedProgressAsync(int taskId, decimal percent, DateTime? actualStart, DateTime? actualFinish, string note, int approvedByUserId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Task management (TMS): staff, project, workshop, personal and meeting-action tasks in one structure; many assignees with a role and a reason; cartables of persons and positions; check-out with a reason;
    /// the chained referral tree; checklist, comments, watchers, attachments and a full append-only history; roll-up of the task percent into the WBS activity and back.
    /// Rules: whoever reports a task done cannot approve it; a personal task needs no approval; a referral row is never changed or deleted (a transfer ends it and adds a new one that points at it).
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IWorkflowNotifier _notify;
        private readonly IPcProgressService _progress;

        public TaskService(IUnitOfWork uow, IInventoryUserContext user, IWorkflowNotifier notify, IPcProgressService progress)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);
        private const string NotAllowed = OfficeText.NotAllowed;
        private const string NotFoundText = "وظیفه پیدا نشد.";

        // ============================================================================ who is the caller

        private sealed class Ctx
        {
            public int Me;
            public List<int> Positions = new List<int>();
            public bool Pmo, Ceo, Reader;
        }

        private Ctx _ctx;

        private async Task<Ctx> CtxAsync(CancellationToken ct)
        {
            if (_ctx != null) return _ctx;
            var c = new Ctx { Me = Me, Pmo = _user.IsInRole("PMO"), Ceo = _user.IsInRole("CEO"), Reader = _user.IsInRole(ProjectControlRoles.Readers) };
            c.Positions = (await OfficeActors.ActingAsync(_uow, c.Me, ct)).Select(a => a.PositionId).ToList();
            return _ctx = c;
        }

        /// <summary>The tasks the caller may see: his own (creator, any referral, watcher), the normal non-personal ones for the PMO and the CEO and (with a project) for the project readers, the confidential ones for the CEO.</summary>
        private IQueryable<TmTask> Visible(IQueryable<TmTask> q, Ctx c)
        {
            int me = c.Me; List<int> pos = c.Positions; bool pmo = c.Pmo, ceo = c.Ceo, reader = c.Reader;
            return q.Where(t => !t.Deleted && (t.CreatorUserId == me
                || t.Assignees.Any(a => a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value)))
                || t.Watchers.Any(w => w.UserId == me)
                || (t.Kind != TmTaskKind.Personal && t.Confidentiality == OfClassification.Normal && (pmo || ceo || (reader && t.ContractId != null)))
                || (t.Kind != TmTaskKind.Personal && t.Confidentiality == OfClassification.Confidential && ceo)));
        }

        public async Task<bool> CanReadTaskAsync(int taskId, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            return await Visible(_uow.Set<TmTask>().AsNoTracking().Where(t => t.Id == taskId), c).AnyAsync(ct);
        }

        public async Task<TmTask> FindVisibleAsync(int taskId, bool track, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            IQueryable<TmTask> q = _uow.Set<TmTask>().Where(t => t.Id == taskId);
            if (!track) q = q.AsNoTracking();
            return await Visible(q, c).FirstOrDefaultAsync(ct);
        }

        private sealed class Rights
        {
            public bool IsCreator, IsAssignee, IsResponsible, IsObserver, IsAssigner, Pmo, Ceo;

            /// <summary>Creator, a person who referred it, the PMO: they change the task itself and refer it.</summary>
            public bool Editor => IsCreator || IsAssigner || Pmo;
        }

        private Rights RightsOf(TmTask t, Ctx c)
        {
            List<TmTaskAssignee> rows = t.Assignees?.ToList() ?? new List<TmTaskAssignee>();
            bool Mine(TmTaskAssignee a) => a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value));
            return new Rights
            {
                IsCreator = t.CreatorUserId == c.Me,
                IsAssignee = rows.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer && Mine(a)),
                IsResponsible = rows.Any(a => a.State == TmAssigneeState.Active && a.Role == TmAssigneeRole.Responsible && Mine(a)),
                IsObserver = rows.Any(a => a.State == TmAssigneeState.Active && a.Role == TmAssigneeRole.Observer && Mine(a)),
                IsAssigner = rows.Any(a => a.AssignedByUserId == c.Me && (a.UserId != c.Me)),
                Pmo = c.Pmo, Ceo = c.Ceo,
            };
        }

        /// <summary>Who may approve a pending task: the creator, a person who referred it or the PMO; never the person who reported it done.</summary>
        private static bool CanApprove(TmTask t, Rights r, Ctx c) => t.Status == TmTaskStatus.Pending && t.CompletedByUserId != c.Me && (r.IsCreator || r.IsAssigner || r.Pmo);

        private List<string> Allowed(TmTask t, Rights r, Ctx c)
        {
            var list = new List<string>();
            bool open = TaskRules.IsOpen(t.Status);
            bool personal = t.Kind == TmTaskKind.Personal;
            bool noAssignee = !(t.Assignees ?? new List<TmTaskAssignee>()).Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer);
            bool worker = r.IsAssignee || (r.IsCreator && (personal || noAssignee));
            if (open && t.Status != TmTaskStatus.Suspended && t.Status != TmTaskStatus.Pending) list.Add("comment-open");
            list.Add("comment");
            if (t.Status != TmTaskStatus.Closed && r.Editor) { list.Add("edit"); }
            if (t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected && t.Status != TmTaskStatus.Pending && t.Status != TmTaskStatus.Suspended && (r.Editor || r.IsAssignee)) list.Add("assign");
            if (t.Status == TmTaskStatus.Rejected && r.Editor) list.Add("assign");
            if (open && t.Status != TmTaskStatus.Pending && t.Status != TmTaskStatus.Suspended && (r.IsAssignee || r.Editor)) list.Add("transfer");
            if (worker && (t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Assigned)) list.Add("start");
            if (worker && (t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Assigned || t.Status == TmTaskStatus.InProgress)) { list.Add("complete"); list.Add("progress"); }
            if (CanApprove(t, r, c)) { list.Add("approve"); list.Add("return"); }
            if (open && (r.IsAssignee || r.Editor) && t.Status != TmTaskStatus.Suspended) list.Add("reject");
            if ((t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Assigned || t.Status == TmTaskStatus.InProgress) && (r.Editor || r.IsResponsible)) list.Add("suspend");
            if (t.Status == TmTaskStatus.Suspended && (r.Editor || r.IsAssignee)) list.Add("resume");
            if ((t.Status == TmTaskStatus.Closed && r.Pmo) || (t.Status == TmTaskStatus.Rejected && r.Editor)) list.Add("reopen");
            return list.Distinct().ToList();
        }

        // ============================================================================ history, notifications, roll-up

        private void Hist(TmTask t, TmHistoryKind kind, string from, string to, string text)
        {
            _uow.Set<TmTaskHistory>().Add(new TmTaskHistory { Task = t, At = DateTimeOffset.Now, UserId = Me, Kind = kind, FromValue = OfficeText.Cap(from, 500), ToValue = OfficeText.Cap(to, 500), Text = OfficeText.Cap(text, 2000) });
        }

        private static string Shown(TmTask t) => t.Confidentiality == OfClassification.Normal ? t.Title : "وظیفه محرمانه شماره " + t.Number;
        private static string Link(TmTask t) => "/tasks?open=" + t.Id.ToString(CultureInfo.InvariantCulture);
        private static OfUrgency Urgency(TmTask t) => t.Priority == TmPriority.Critical ? OfUrgency.Immediate : t.Priority == TmPriority.High ? OfUrgency.Urgent : OfUrgency.Normal;

        private async Task NotifyAssigneesAsync(TmTask t, IEnumerable<TmTaskAssignee> rows, string prefix, CancellationToken ct)
        {
            foreach (var group in rows.GroupBy(r => r.Role == TmAssigneeRole.Observer))
            {
                var users = group.Where(r => r.UserId.HasValue).Select(r => r.UserId.Value).Distinct().Where(u => u != Me).ToList();
                var positions = group.Where(r => r.PositionId.HasValue).Select(r => r.PositionId.Value).Distinct().ToList();
                if (users.Count + positions.Count == 0) continue;
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "task:" + t.Id.ToString(CultureInfo.InvariantCulture), Module = "tasks", SourceType = "task", SourceId = t.Id, Title = prefix + Shown(t), Link = Link(t),
                    Kind = group.Key ? OfInboxKind.Alert : OfInboxKind.Task, Priority = Urgency(t), DueDate = t.DueDate, UserIds = users, PositionIds = positions,
                }, ct);
            }
        }

        private Task CloseNoticesAsync(TmTask t, string reason, CancellationToken ct) =>
            Task.WhenAll(_notify.CloseAsync("task:" + t.Id.ToString(CultureInfo.InvariantCulture), reason, ct));

        private async Task NotifyApproversAsync(TmTask t, CancellationToken ct)
        {
            var users = new HashSet<int> { t.CreatorUserId };
            foreach (int u in t.Assignees.Where(a => a.AssignedByUserId > 0).Select(a => a.AssignedByUserId)) users.Add(u);
            users.Remove(t.CompletedByUserId ?? 0);
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "task-approve:" + t.Id.ToString(CultureInfo.InvariantCulture), Module = "tasks", SourceType = "task", SourceId = t.Id, Title = "تایید انجام وظیفه: " + Shown(t), Link = Link(t),
                Kind = OfInboxKind.Approval, Priority = Urgency(t), DueDate = t.DueDate, UserIds = users.ToList(), Roles = "PMO", ReplaceRecipients = true,
            }, ct);
        }

        /// <summary>The percent of every WBS activity that is measured from tasks is recomputed by the progress engine (real-time reflection of the task progress in the WBS).</summary>
        private async Task RollUpAsync(IEnumerable<int?> activityIds, CancellationToken ct)
        {
            List<int> ids = activityIds.Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
            if (ids.Count == 0) return;
            List<int> contracts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => ids.Contains(a.Id) && !a.Deleted && a.ProgressMethod == PcProgressMethod.Tasks).Select(a => a.ContractId).Distinct().ToListAsync(ct);
            foreach (int contractId in contracts)
            {
                await _progress.ApplyAsync(contractId, ct);
                await _uow.SaveChangesAsync(ct);
            }
        }

        // ============================================================================ mapping

        private async Task<TmTaskDto> ToDtoAsync(TmTask t, Ctx c, CancellationToken ct)
        {
            TmTask full = await _uow.Set<TmTask>().AsNoTracking().AsSplitQuery().Include(x => x.Assignees).Include(x => x.Checklist).Include(x => x.Comments).Include(x => x.Watchers).Include(x => x.Attachments)
                .FirstAsync(x => x.Id == t.Id, ct);
            List<TmTaskHistory> history = await _uow.Set<TmTaskHistory>().AsNoTracking().Where(h => h.TaskId == t.Id).OrderBy(h => h.At).ThenBy(h => h.Id).ToListAsync(ct);
            List<TmTask> subs = await Visible(_uow.Set<TmTask>().AsNoTracking().Where(s => s.ParentTaskId == t.Id), c).OrderBy(s => s.Id).ToListAsync(ct);
            var userIds = new List<int?> { full.CreatorUserId, full.CompletedByUserId, full.ClosedByUserId };
            userIds.AddRange(full.Assignees.Select(a => (int?)a.UserId));
            userIds.AddRange(full.Assignees.Select(a => (int?)a.AssignedByUserId));
            userIds.AddRange(full.Assignees.Select(a => a.EndedByUserId));
            userIds.AddRange(full.Checklist.Select(i => i.DoneByUserId));
            userIds.AddRange(full.Comments.Select(m => (int?)m.UserId));
            userIds.AddRange(full.Attachments.Select(a => (int?)a.UploadedByUserId));
            userIds.AddRange(full.Attachments.Select(a => a.VoidedByUserId));
            userIds.AddRange(history.Select(h => (int?)h.UserId));
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, userIds, ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, full.Assignees.Select(a => a.PositionId), ct);
            TmTaskDto dto = await ListDtoAsync<TmTaskDto>(new List<TmTask> { full }, names, ct, c).ContinueWith(x => x.Result.First(), ct);
            dto.Description = full.Description; dto.RequestType = full.RequestType; dto.Requester = full.Requester; dto.ReferringOrganisation = full.ReferringOrganisation;
            dto.StartDate = full.StartDate; dto.StartDateText = OfficeText.Date(full.StartDate); dto.EstimatedCost = full.EstimatedCost; dto.ActualHours = full.ActualHours; dto.ActualCost = full.ActualCost;
            dto.StatusReason = full.StatusReason; dto.StartedAt = full.StartedAt; dto.CompletedAt = full.CompletedAt; dto.CompletedByName = names.Name(full.CompletedByUserId);
            dto.ClosedAt = full.ClosedAt; dto.ClosedByName = names.Name(full.ClosedByUserId); dto.Recurrence = full.Recurrence; dto.RecurrenceInterval = full.RecurrenceInterval;
            dto.RecurrenceEnd = full.RecurrenceEnd; dto.RecurrenceSourceId = full.RecurrenceSourceId; dto.ReminderDays = full.ReminderDays; dto.WbsNodeId = full.WbsNodeId;
            dto.DecisionId = full.DecisionId; dto.LetterId = full.LetterId; dto.RequestId = full.RequestId; dto.RiskId = full.RiskId; dto.IssueId = full.IssueId;
            dto.CreatorUserId = full.CreatorUserId; dto.CreatedAt = full.CreateDate; dto.CreatedAtText = OfficeText.Stamp(full.CreateDate);
            if (full.WbsNodeId.HasValue) dto.WbsNodeCode = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.Id == full.WbsNodeId.Value).Select(n => n.Code).FirstOrDefaultAsync(ct);
            if (full.ActivityId.HasValue) dto.ActivityTitle = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.Id == full.ActivityId.Value).Select(a => a.Title).FirstOrDefaultAsync(ct);
            if (full.ContractId.HasValue) dto.ContractTitle = await _uow.Set<ContractEntity>().AsNoTracking().Where(k => k.Id == full.ContractId.Value).Select(k => k.Title).FirstOrDefaultAsync(ct);
            if (full.ParentTaskId.HasValue) dto.ParentTaskTitle = await _uow.Set<TmTask>().AsNoTracking().Where(p => p.Id == full.ParentTaskId.Value).Select(p => p.Title).FirstOrDefaultAsync(ct);
            dto.Assignees = full.Assignees.OrderBy(a => a.Id).Select(a => new TmAssigneeDto
            {
                Id = a.Id, UserId = a.UserId, UserName = names.Name(a.UserId), PositionId = a.PositionId, PositionTitle = positions.Title(a.PositionId), Role = a.Role, State = a.State, Reason = a.Reason,
                AssignedByUserId = a.AssignedByUserId, AssignedByName = names.Name(a.AssignedByUserId), AssignedAt = a.AssignedAt, AssignedAtText = OfficeText.Stamp(a.AssignedAt), ParentId = a.ParentId,
                ReadAt = a.ReadAt, EndedAt = a.EndedAt, EndedAtText = OfficeText.Stamp(a.EndedAt), EndedByName = names.Name(a.EndedByUserId), EndReason = a.EndReason,
                Mine = a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value)),
            }).ToList();
            dto.Checklist = full.Checklist.OrderBy(i => i.Seq).ThenBy(i => i.Id).Select(i => new TmChecklistItemDto { Id = i.Id, Seq = i.Seq, Text = i.Text, Done = i.Done, DoneByName = names.Name(i.DoneByUserId), DoneAtText = OfficeText.Stamp(i.DoneAt) }).ToList();
            dto.Comments = full.Comments.OrderBy(m => m.At).ThenBy(m => m.Id).Select(m => new TmCommentDto { Id = m.Id, ParentId = m.ParentId, UserId = m.UserId, UserName = names.Name(m.UserId), Text = m.Text, At = m.At, AtText = OfficeText.Stamp(m.At) }).ToList();
            dto.Attachments = full.Attachments.OrderBy(a => a.Id).Select(a => new TmAttachmentDto
            {
                Id = a.Id, DocumentId = a.DocumentId, FileName = a.FileName, UploadedByName = names.Name(a.UploadedByUserId), UploadedAtText = OfficeText.Stamp(a.UploadedAt), SupersedesId = a.SupersedesId, IsVoid = a.IsVoid,
                VoidReason = a.VoidReason, VoidedByName = names.Name(a.VoidedByUserId),
            }).ToList();
            dto.AttachmentCount = dto.Attachments.Count(a => !a.IsVoid);
            dto.History = history.Select(h => new TmHistoryDto { Id = h.Id, Kind = h.Kind, UserName = names.Name(h.UserId), At = h.At, AtText = OfficeText.Stamp(h.At), FromValue = h.FromValue, ToValue = h.ToValue, Text = h.Text }).ToList();
            dto.WatcherUserIds = full.Watchers.Select(w => w.UserId).ToList();
            dto.Watching = full.Watchers.Any(w => w.UserId == c.Me);
            dto.SubTasks = (await ListDtoAsync<TmTaskListDto>(subs, names, ct, c));
            dto.AllowedActions = Allowed(full, RightsOf(full, c), c);
            return dto;
        }

        /// <summary>The list rows of the given tasks (assignee names, checklist and comment counts, activity code).</summary>
        private async Task<List<T>> ListDtoAsync<T>(List<TmTask> tasks, Dictionary<int, string> knownNames, CancellationToken ct, Ctx c) where T : TmTaskListDto, new()
        {
            List<int> ids = tasks.Select(t => t.Id).ToList();
            var assignees = await _uow.Set<TmTaskAssignee>().AsNoTracking().Where(a => ids.Contains(a.TaskId) && (a.State == TmAssigneeState.Active || a.State == TmAssigneeState.Done)).Select(a => new { a.TaskId, a.UserId, a.PositionId, a.Role }).ToListAsync(ct);
            var check = await _uow.Set<TmTaskChecklistItem>().AsNoTracking().Where(i => ids.Contains(i.TaskId)).GroupBy(i => i.TaskId).Select(g => new { TaskId = g.Key, Total = g.Count(), Done = g.Count(i => i.Done) }).ToListAsync(ct);
            var comments = await _uow.Set<TmTaskComment>().AsNoTracking().Where(m => ids.Contains(m.TaskId)).GroupBy(m => m.TaskId).Select(g => new { TaskId = g.Key, N = g.Count() }).ToListAsync(ct);
            var files = await _uow.Set<TmTaskAttachment>().AsNoTracking().Where(a => ids.Contains(a.TaskId) && !a.IsVoid).GroupBy(a => a.TaskId).Select(g => new { TaskId = g.Key, N = g.Count() }).ToListAsync(ct);
            List<int> actIds = tasks.Where(t => t.ActivityId.HasValue).Select(t => t.ActivityId.Value).Distinct().ToList();
            Dictionary<int, string> actCodes = actIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            var wanted = new List<int?>(tasks.Select(t => (int?)t.CreatorUserId));
            wanted.AddRange(assignees.Select(a => a.UserId));
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, wanted, ct);
            foreach (var kv in knownNames) names[kv.Key] = kv.Value;
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, assignees.Select(a => a.PositionId), ct);
            DateTime today = DateTime.Today;
            var result = new List<T>();
            foreach (TmTask t in tasks)
            {
                var d = new T
                {
                    Id = t.Id, Number = t.Number, Title = t.Confidentiality == OfClassification.Normal || IsParty(t, c) ? t.Title : "وظیفه محرمانه", Kind = t.Kind, Category = t.Category, Priority = t.Priority,
                    Importance = t.Importance, Confidentiality = t.Confidentiality, Status = t.Status, DueDate = t.DueDate, DueDateText = OfficeText.Date(t.DueDate),
                    Overdue = t.DueDate.HasValue && t.DueDate.Value.Date < today && TaskRules.IsOpen(t.Status), PercentComplete = t.Status == TmTaskStatus.Closed ? 100m : t.PercentComplete,
                    EstimatedHours = t.EstimatedHours, ContractId = t.ContractId, ActivityId = t.ActivityId, ActivityCode = t.ActivityId.HasValue && actCodes.TryGetValue(t.ActivityId.Value, out string code) ? code : null,
                    ParentTaskId = t.ParentTaskId, CreatorName = names.Name(t.CreatorUserId),
                    AssigneeNames = assignees.Where(a => a.TaskId == t.Id && a.Role != TmAssigneeRole.Observer).Select(a => a.UserId.HasValue ? names.Name(a.UserId) : positions.Title(a.PositionId)).Where(n => n != null).Distinct().ToList(),
                    ChecklistTotal = check.FirstOrDefault(x => x.TaskId == t.Id)?.Total ?? 0, ChecklistDone = check.FirstOrDefault(x => x.TaskId == t.Id)?.Done ?? 0,
                    CommentCount = comments.FirstOrDefault(x => x.TaskId == t.Id)?.N ?? 0, AttachmentCount = files.FirstOrDefault(x => x.TaskId == t.Id)?.N ?? 0,
                };
                result.Add(d);
            }
            return result;
        }

        private static bool IsParty(TmTask t, Ctx c) => t.CreatorUserId == c.Me || (t.Assignees != null && t.Assignees.Any(a => a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value))));

        private async Task<ResponseDto<TmTaskDto>> ReloadAsync(int id, Ctx c, CancellationToken ct)
        {
            TmTask t = await _uow.Set<TmTask>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(t, c, ct));
        }

        // ============================================================================ lists

        private IQueryable<TmTask> Filter(IQueryable<TmTask> q, TmTaskQuery query, Ctx c, bool cartableApplies)
        {
            int me = c.Me; List<int> pos = c.Positions; DateTime today = DateTime.Today; bool pmo = c.Pmo;
            if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
            if (query.Kind.HasValue) q = q.Where(t => t.Kind == query.Kind.Value);
            if (query.Priority.HasValue) q = q.Where(t => t.Priority == query.Priority.Value);
            if (query.ContractId.HasValue) q = q.Where(t => t.ContractId == query.ContractId.Value);
            if (query.ActivityId.HasValue) q = q.Where(t => t.ActivityId == query.ActivityId.Value);
            if (query.WbsNodeId.HasValue) q = q.Where(t => t.WbsNodeId == query.WbsNodeId.Value);
            if (query.ParentTaskId.HasValue) q = q.Where(t => t.ParentTaskId == query.ParentTaskId.Value);
            if (query.DecisionId.HasValue) q = q.Where(t => t.DecisionId == query.DecisionId.Value);
            if (query.RiskId.HasValue) q = q.Where(t => t.RiskId == query.RiskId.Value);
            if (query.IssueId.HasValue) q = q.Where(t => t.IssueId == query.IssueId.Value);
            if (query.DueFrom.HasValue) q = q.Where(t => t.DueDate >= query.DueFrom.Value.Date);
            if (query.DueTo.HasValue) q = q.Where(t => t.DueDate <= query.DueTo.Value.Date);
            string category = Clean(query.Category);
            if (category != null) q = q.Where(t => t.Category == category);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(t => (t.Confidentiality == OfClassification.Normal && (t.Title.Contains(text) || t.Description.Contains(text))) || t.Number.Contains(text));
            if (query.PositionId.HasValue)
            {
                int p = query.PositionId.Value;
                q = q.Where(t => t.Assignees.Any(a => a.PositionId == p && a.State == TmAssigneeState.Active));
            }
            if (!cartableApplies) return q;
            switch ((query.Cartable ?? "mine").Trim().ToLowerInvariant())
            {
                case "all":
                    return q;
                case "assigned-to-me":
                    return q.Where(t => t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected
                        && t.Assignees.Any(a => (a.State == TmAssigneeState.Active || a.State == TmAssigneeState.Done) && a.Role != TmAssigneeRole.Observer && (a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value)))));
                case "assigned-by-me":
                    return q.Where(t => t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected && t.Assignees.Any(a => a.AssignedByUserId == me && a.UserId != me));
                case "overdue":
                    return q.Where(t => t.DueDate != null && t.DueDate < today && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected
                        && (t.CreatorUserId == me || t.Assignees.Any(a => a.State == TmAssigneeState.Active && (a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value))))));
                case "waiting":
                    return q.Where(t => t.Status == TmTaskStatus.Pending && t.CompletedByUserId != me && (pmo || t.CreatorUserId == me || t.Assignees.Any(a => a.AssignedByUserId == me && a.UserId != me)));
                case "done":
                    return q.Where(t => t.Status == TmTaskStatus.Closed && (t.CreatorUserId == me || t.Assignees.Any(a => a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value)))));
                case "unit":
                    return q.Where(t => t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected && t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.PositionId != null && pos.Contains(a.PositionId.Value)));
                case "watching":
                    return q.Where(t => t.Watchers.Any(w => w.UserId == me));
                default:
                    return q.Where(t => t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected
                        && (t.Assignees.Any(a => (a.State == TmAssigneeState.Active || a.State == TmAssigneeState.Done) && a.Role != TmAssigneeRole.Observer && (a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value))))
                            || (t.CreatorUserId == me && (t.Kind == TmTaskKind.Personal || !t.Assignees.Any(a => a.State == TmAssigneeState.Active)))));
            }
        }

        public async Task<ResponseDto<PagedList<TmTaskListDto>>> GetTasksAsync(TmTaskQuery query, CancellationToken ct)
        {
            query ??= new TmTaskQuery();
            Ctx c = await CtxAsync(ct);
            if (query.PositionId.HasValue && !c.Positions.Contains(query.PositionId.Value) && !c.Pmo && !c.Ceo) return Bad<PagedList<TmTaskListDto>>(NotAllowed + " (کارتابل سمت دیگر)");
            IQueryable<TmTask> q = Filter(Visible(_uow.Set<TmTask>().AsNoTracking(), c), query, c, true);
            q = q.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.Id);
            PagedList<TmTask> page = await PagedList<TmTask>.CreateAsync(q, query.PageNumber, query.PageSize, ct);
            List<TmTaskListDto> rows = await ListDtoAsync<TmTaskListDto>(page.ToList(), new Dictionary<int, string>(), ct, c);
            return ResponseDtoHelper.Ok((PagedList<TmTaskListDto>)new PcPageOf<TmTaskListDto>(rows, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<TmTaskDto>> GetTaskAsync(int id, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            TmTask t = await Visible(_uow.Set<TmTask>().AsNoTracking().Where(x => x.Id == id), c).FirstOrDefaultAsync(ct);
            if (t == null) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            // the first time a holder opens his referral it is marked read
            return ResponseDtoHelper.Ok(await ToDtoAsync(t, c, ct));
        }

        public async Task<ResponseDto<List<TmBoardColumnDto>>> GetBoardAsync(TmTaskQuery query, CancellationToken ct)
        {
            query ??= new TmTaskQuery();
            Ctx c = await CtxAsync(ct);
            IQueryable<TmTask> q = Filter(Visible(_uow.Set<TmTask>().AsNoTracking(), c), query, c, !string.IsNullOrWhiteSpace(query.Cartable));
            int take = query.PageSize <= 0 || query.PageSize > 100 ? 40 : query.PageSize;
            var columns = new List<TmBoardColumnDto>();
            foreach (TmTaskStatus status in new[] { TmTaskStatus.Open, TmTaskStatus.Assigned, TmTaskStatus.InProgress, TmTaskStatus.Pending, TmTaskStatus.Suspended, TmTaskStatus.Closed, TmTaskStatus.Rejected })
            {
                IQueryable<TmTask> col = q.Where(t => t.Status == status);
                List<TmTask> items = await col.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.Id).Take(take).ToListAsync(ct);
                columns.Add(new TmBoardColumnDto { Status = status, Count = await col.CountAsync(ct), Items = await ListDtoAsync<TmTaskListDto>(items, new Dictionary<int, string>(), ct, c) });
            }
            return ResponseDtoHelper.Ok(columns);
        }

        public async Task<ResponseDto<List<TmTaskListDto>>> GetByActivityAsync(int activityId, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            if (!c.Reader && !c.Pmo && !c.Ceo) { /* only the tasks the caller may see are returned below */ }
            List<TmTask> tasks = await Visible(_uow.Set<TmTask>().AsNoTracking().Where(t => t.ActivityId == activityId), c).OrderBy(t => t.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await ListDtoAsync<TmTaskListDto>(tasks, new Dictionary<int, string>(), ct, c));
        }

        public async Task<ResponseDto<TmSummaryDto>> GetSummaryAsync(int? contractId, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            IQueryable<TmTask> q = Visible(_uow.Set<TmTask>().AsNoTracking(), c);
            if (contractId.HasValue) q = q.Where(t => t.ContractId == contractId.Value);
            var rows = await q.Select(t => new { t.Status, t.Kind, t.DueDate, t.PercentComplete }).ToListAsync(ct);
            DateTime today = DateTime.Today;
            var dto = new TmSummaryDto
            {
                Total = rows.Count, Open = rows.Count(r => TaskRules.IsOpen(r.Status)), Closed = rows.Count(r => r.Status == TmTaskStatus.Closed), Pending = rows.Count(r => r.Status == TmTaskStatus.Pending),
                Overdue = rows.Count(r => r.DueDate.HasValue && r.DueDate.Value.Date < today && TaskRules.IsOpen(r.Status)),
                AveragePercent = rows.Count == 0 ? 0m : Math.Round(rows.Average(r => r.Status == TmTaskStatus.Closed ? 100m : r.PercentComplete), 2),
            };
            foreach (var g in rows.GroupBy(r => r.Status)) dto.ByStatus[g.Key.ToString()] = g.Count();
            foreach (var g in rows.GroupBy(r => r.Kind)) dto.ByKind[g.Key.ToString()] = g.Count();
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<TmCountsDto>> GetCountsAsync(CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            IQueryable<TmTask> v = Visible(_uow.Set<TmTask>().AsNoTracking(), c);
            var counts = new TmCountsDto();
            counts.AssignedToMe = await Filter(v, new TmTaskQuery { Cartable = "assigned-to-me" }, c, true).CountAsync(ct);
            counts.AssignedByMe = await Filter(v, new TmTaskQuery { Cartable = "assigned-by-me" }, c, true).CountAsync(ct);
            counts.Overdue = await Filter(v, new TmTaskQuery { Cartable = "overdue" }, c, true).CountAsync(ct);
            counts.WaitingForMe = await Filter(v, new TmTaskQuery { Cartable = "waiting" }, c, true).CountAsync(ct);
            counts.Unit = await Filter(v, new TmTaskQuery { Cartable = "unit" }, c, true).CountAsync(ct);
            return ResponseDtoHelper.Ok(counts);
        }

        public async Task<ResponseDto<List<TmAgendaDayDto>>> GetAgendaAsync(TmAgendaQuery query, CancellationToken ct)
        {
            query ??= new TmAgendaQuery();
            Ctx c = await CtxAsync(ct);
            DateTime day = (query.Date ?? DateTime.Today).Date;
            string mode = (query.Mode ?? "week").Trim().ToLowerInvariant();
            (DateTime start, DateTime end) = mode == "day" ? (day, day) : mode == "month" ? TaskRules.MonthOf(day) : TaskRules.WeekOf(day);
            IQueryable<TmTask> q = Visible(_uow.Set<TmTask>().AsNoTracking(), c).Where(t => t.Status != TmTaskStatus.Rejected);
            if (query.MineOnly != false) q = Filter(q, new TmTaskQuery { Cartable = "mine" }, c, true);
            List<TmTask> tasks = await q.Where(t => (t.DueDate != null && t.DueDate >= start.AddDays(-365) && t.DueDate <= end.AddDays(365)) || (t.StartDate != null && t.StartDate >= start && t.StartDate <= end)).ToListAsync(ct);
            int defaultOffset = (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.TaskReminderDays];
            DateTime today = DateTime.Today;
            var days = new Dictionary<DateTime, TmAgendaDayDto>();
            for (DateTime d = start; d <= end; d = d.AddDays(1))
                days[d] = new TmAgendaDayDto { Date = d, DateText = OfficeText.Date(d), WeekDay = (int)d.DayOfWeek };
            void Add(DateTime d, TmTask t, string kind)
            {
                if (days.TryGetValue(d.Date, out TmAgendaDayDto row))
                    row.Items.Add(new TmAgendaItemDto { TaskId = t.Id, Number = t.Number, Title = t.Confidentiality == OfClassification.Normal || IsParty(t, c) ? t.Title : "وظیفه محرمانه", Status = t.Status, Priority = t.Priority, Kind = kind, Overdue = t.DueDate.HasValue && t.DueDate.Value.Date < today && TaskRules.IsOpen(t.Status) });
            }
            foreach (TmTask t in tasks)
            {
                if (t.DueDate.HasValue) Add(t.DueDate.Value, t, "due");
                if (t.StartDate.HasValue) Add(t.StartDate.Value, t, "start");
                if (t.DueDate.HasValue && TaskRules.IsOpen(t.Status))
                {
                    List<int> offsets = TaskRules.ParseOffsets(t.ReminderDays);
                    if (offsets.Count == 0) offsets.Add(defaultOffset);
                    foreach (int o in offsets.Where(o => o > 0)) Add(t.DueDate.Value.AddDays(-o), t, "reminder");
                }
            }
            return ResponseDtoHelper.Ok(days.Values.OrderBy(d => d.Date).ToList());
        }

        public async Task<ResponseDto<List<TmReferralNodeDto>>> GetReferralTreeAsync(int taskId, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            if (!await Visible(_uow.Set<TmTask>().AsNoTracking().Where(t => t.Id == taskId), c).AnyAsync(ct)) return OfficeText.NotFound<List<TmReferralNodeDto>>(NotFoundText);
            List<TmTaskAssignee> rows = await _uow.Set<TmTaskAssignee>().AsNoTracking().Where(a => a.TaskId == taskId).OrderBy(a => a.Id).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new[] { r.UserId, (int?)r.AssignedByUserId }), ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.PositionId), ct);
            var nodes = rows.ToDictionary(r => r.Id, r => new TmReferralNodeDto
            {
                Id = r.Id, ParentId = r.ParentId, Who = r.UserId.HasValue ? names.Name(r.UserId) : positions.Title(r.PositionId), ByWhom = names.Name(r.AssignedByUserId), Role = r.Role, State = r.State, Reason = r.Reason,
                AssignedAtText = OfficeText.Stamp(r.AssignedAt), EndedAtText = OfficeText.Stamp(r.EndedAt), EndReason = r.EndReason,
            });
            var roots = new List<TmReferralNodeDto>();
            foreach (TmReferralNodeDto n in nodes.Values.OrderBy(n => n.Id))
            {
                if (n.ParentId.HasValue && nodes.TryGetValue(n.ParentId.Value, out TmReferralNodeDto parent)) parent.Children.Add(n);
                else roots.Add(n);
            }
            return ResponseDtoHelper.Ok(roots);
        }

        // ============================================================================ create and edit

        private async Task<string> ValidateLinksAsync(TmTaskSaveDto dto, TmTask existing, CancellationToken ct)
        {
            int? contractId = dto.ContractId;
            if (dto.ActivityId.HasValue)
            {
                var a = await _uow.Set<PcActivity>().AsNoTracking().Where(x => x.Id == dto.ActivityId.Value && !x.Deleted).Select(x => new { x.ContractId, x.WbsNodeId }).FirstOrDefaultAsync(ct);
                if (a == null) return "فعالیت پیدا نشد.";
                if (contractId.HasValue && contractId.Value != a.ContractId) return "فعالیت متعلق به این پروژه نیست.";
                dto.ContractId = a.ContractId;
                if (!dto.WbsNodeId.HasValue) dto.WbsNodeId = a.WbsNodeId;
            }
            contractId = dto.ContractId;
            if (contractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(k => k.Id == contractId.Value && !k.Deleted, ct)) return PcText.ContractNotFound;
            if (dto.WbsNodeId.HasValue)
            {
                if (!contractId.HasValue) return "برای اتصال به گره WBS پروژه را مشخص کنید.";
                if (!await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == dto.WbsNodeId.Value && n.ContractId == contractId.Value && !n.Deleted, ct)) return "گره WBS متعلق به این پروژه نیست.";
            }
            if (dto.DecisionId.HasValue && !await _uow.Set<OfDecision>().AnyAsync(d => d.Id == dto.DecisionId.Value, ct)) return "مصوبه پیدا نشد.";
            if (dto.LetterId.HasValue && !await _uow.Set<OfLetter>().AnyAsync(l => l.Id == dto.LetterId.Value && !l.Deleted, ct)) return "نامه پیدا نشد.";
            if (dto.RequestId.HasValue && !await _uow.Set<OfRequest>().AnyAsync(r => r.Id == dto.RequestId.Value && !r.Deleted, ct)) return "درخواست پیدا نشد.";
            if (dto.RiskId.HasValue)
            {
                int? riskContract = await _uow.Set<PcRisk>().AsNoTracking().Where(r => r.Id == dto.RiskId.Value && !r.Deleted).Select(r => (int?)r.ContractId).FirstOrDefaultAsync(ct);
                if (!riskContract.HasValue) return "ریسک پیدا نشد.";
                if (contractId.HasValue && contractId.Value != riskContract.Value) return "ریسک متعلق به این پروژه نیست.";
                dto.ContractId = riskContract.Value;
            }
            if (dto.IssueId.HasValue)
            {
                int? issueContract = await _uow.Set<PcIssue>().AsNoTracking().Where(r => r.Id == dto.IssueId.Value && !r.Deleted).Select(r => (int?)r.ContractId).FirstOrDefaultAsync(ct);
                if (!issueContract.HasValue) return "مشکل پیدا نشد.";
                if (dto.ContractId.HasValue && dto.ContractId.Value != issueContract.Value) return "مشکل متعلق به این پروژه نیست.";
                dto.ContractId = issueContract.Value;
            }
            if (dto.ParentTaskId.HasValue)
            {
                if (existing != null && dto.ParentTaskId.Value == existing.Id) return "وظیفه نمی‌تواند زیرمجموعه خودش باشد.";
                Ctx c = await CtxAsync(ct);
                if (!await Visible(_uow.Set<TmTask>().AsNoTracking().Where(t => t.Id == dto.ParentTaskId.Value), c).AnyAsync(ct)) return "وظیفه مادر پیدا نشد.";
                int? walk = dto.ParentTaskId; int guard = 0;
                while (walk.HasValue && guard++ < 30)
                {
                    if (existing != null && walk.Value == existing.Id) return "وظیفه مادر، زیرمجموعه همین وظیفه است (حلقه).";
                    walk = await _uow.Set<TmTask>().AsNoTracking().Where(t => t.Id == walk.Value).Select(t => t.ParentTaskId).FirstOrDefaultAsync(ct);
                }
            }
            return null;
        }

        public async Task<ResponseDto<TmTaskDto>> SaveTaskAsync(TmTaskSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string title = Clean(dto.Title);
            if (title == null) return Bad<TmTaskDto>("عنوان وظیفه را بنویسید.");
            if (title.Length > 300) return Bad<TmTaskDto>("عنوان وظیفه حداکثر ۳۰۰ نویسه است.");
            if (!Enum.IsDefined(typeof(TmTaskKind), dto.Kind) || !Enum.IsDefined(typeof(TmPriority), dto.Priority) || !Enum.IsDefined(typeof(TmImportance), dto.Importance) || !Enum.IsDefined(typeof(OfClassification), dto.Confidentiality))
                return Bad<TmTaskDto>(OfficeText.InvalidData);
            if (!Enum.IsDefined(typeof(TmRecurrence), dto.Recurrence)) return Bad<TmTaskDto>(OfficeText.InvalidData);
            if (dto.EstimatedHours < 0m || dto.EstimatedCost < 0m || dto.EstimatedHours > 1000000m) return Bad<TmTaskDto>("زمان و هزینه برآوردی نمی‌تواند منفی باشد.");
            if (dto.StartDate.HasValue && dto.DueDate.HasValue && dto.DueDate.Value.Date < dto.StartDate.Value.Date) return Bad<TmTaskDto>("مهلت پیش از تاریخ شروع است.");
            if (!TaskRules.OffsetsValid(dto.ReminderDays)) return Bad<TmTaskDto>("روزهای یادآوری باید عددهای ۰ تا ۳۶۵ جداشده با ویرگول باشد.");
            if (dto.Recurrence != TmRecurrence.None && (!dto.DueDate.HasValue || dto.RecurrenceInterval < 1 || dto.RecurrenceInterval > 365)) return Bad<TmTaskDto>("برای وظیفه تکرارشونده مهلت و فاصله تکرار (۱ تا ۳۶۵) را وارد کنید.");
            if ((dto.Kind == TmTaskKind.Project || dto.Kind == TmTaskKind.Workshop) && !dto.ContractId.HasValue && !dto.ActivityId.HasValue && !dto.RiskId.HasValue && !dto.IssueId.HasValue) return Bad<TmTaskDto>("وظیفه پروژه‌ای یا کارگاهی را به یک پروژه وصل کنید.");
            if (dto.Kind == TmTaskKind.Personal && (dto.Assignees?.Count ?? 0) > 0) return Bad<TmTaskDto>("وظیفه شخصی به دیگری ارجاع نمی‌شود.");
            Ctx c = await CtxAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;

            TmTask t;
            bool isNew = !dto.Id.HasValue;
            var oldActivity = (int?)null;
            if (isNew)
            {
                string problem = await ValidateLinksAsync(dto, null, ct);
                if (problem != null) return Bad<TmTaskDto>(problem);
                t = new TmTask { CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me, Status = TmTaskStatus.Open, Assignees = new List<TmTaskAssignee>(), Checklist = new List<TmTaskChecklistItem>(), Watchers = new List<TmTaskWatcher>(), Comments = new List<TmTaskComment>(), Attachments = new List<TmTaskAttachment>() };
                t.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Task, "TK", DateTime.Today, ct);
                _uow.Set<TmTask>().Add(t);
            }
            else
            {
                t = await _uow.Set<TmTask>().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
                Rights r = RightsOf(t, c);
                if (!r.Editor) return Bad<TmTaskDto>(NotAllowed + " (فقط ثبت‌کننده، ارجاع‌دهنده یا PMO وظیفه را ویرایش می‌کند)");
                if (t.Status == TmTaskStatus.Closed) return Bad<TmTaskDto>("وظیفه بسته‌شده تغییر نمی‌کند؛ برای تغییر آن را دوباره باز کنید.");
                if (t.Kind != dto.Kind && (t.Kind == TmTaskKind.Personal || dto.Kind == TmTaskKind.Personal) && t.Assignees.Any()) return Bad<TmTaskDto>("نوع وظیفه شخصی با وظیفه ارجاع‌شده عوض نمی‌شود.");
                string problem = await ValidateLinksAsync(dto, t, ct);
                if (problem != null) return Bad<TmTaskDto>(problem);
                oldActivity = t.ActivityId;
            }

            // field changes into the history
            void Change(string label, string from, string to) { if (!isNew && from != to) Hist(t, TmHistoryKind.Field, from, to, label); }
            Change("عنوان", t.Title, title);
            Change("مهلت", OfficeText.Date(t.DueDate), OfficeText.Date(dto.DueDate));
            Change("شروع", OfficeText.Date(t.StartDate), OfficeText.Date(dto.StartDate));
            Change("اولویت", t.Priority.ToString(), dto.Priority.ToString());
            Change("اهمیت", t.Importance.ToString(), dto.Importance.ToString());
            Change("حساسیت", t.Confidentiality.ToString(), dto.Confidentiality.ToString());
            Change("نوع", t.Kind.ToString(), dto.Kind.ToString());
            Change("دسته", t.Category, Clean(dto.Category));
            Change("زمان برآوردی", t.EstimatedHours.ToString("0.##", CultureInfo.InvariantCulture), dto.EstimatedHours.ToString("0.##", CultureInfo.InvariantCulture));
            Change("هزینه برآوردی", t.EstimatedCost.ToString("0.##", CultureInfo.InvariantCulture), dto.EstimatedCost.ToString("0.##", CultureInfo.InvariantCulture));
            Change("پروژه", t.ContractId?.ToString(CultureInfo.InvariantCulture), dto.ContractId?.ToString(CultureInfo.InvariantCulture));
            Change("فعالیت WBS", t.ActivityId?.ToString(CultureInfo.InvariantCulture), dto.ActivityId?.ToString(CultureInfo.InvariantCulture));
            Change("گره WBS", t.WbsNodeId?.ToString(CultureInfo.InvariantCulture), dto.WbsNodeId?.ToString(CultureInfo.InvariantCulture));
            Change("وظیفه مادر", t.ParentTaskId?.ToString(CultureInfo.InvariantCulture), dto.ParentTaskId?.ToString(CultureInfo.InvariantCulture));
            Change("تکرار", t.Recurrence.ToString() + "/" + t.RecurrenceInterval, dto.Recurrence + "/" + dto.RecurrenceInterval);
            if (!isNew && Clean(t.Description) != Clean(dto.Description)) Hist(t, TmHistoryKind.Field, null, null, "شرح تغییر کرد");

            t.Title = title; t.Description = OfficeText.Cap(Clean(dto.Description), 4000); t.Kind = dto.Kind; t.Category = OfficeText.Cap(Clean(dto.Category), 60);
            t.Priority = dto.Priority; t.Importance = dto.Importance; t.Confidentiality = dto.Confidentiality;
            t.RequestType = OfficeText.Cap(Clean(dto.RequestType), 100); t.Requester = OfficeText.Cap(Clean(dto.Requester), 200); t.ReferringOrganisation = OfficeText.Cap(Clean(dto.ReferringOrganisation), 200);
            t.StartDate = dto.StartDate?.Date; t.DueDate = dto.DueDate?.Date; t.EstimatedHours = dto.EstimatedHours; t.EstimatedCost = dto.EstimatedCost;
            t.Recurrence = dto.Recurrence; t.RecurrenceInterval = Math.Max(1, dto.RecurrenceInterval); t.RecurrenceEnd = dto.RecurrenceEnd?.Date;
            t.ReminderDays = Clean(dto.ReminderDays) == null ? null : string.Join(",", TaskRules.ParseOffsets(dto.ReminderDays));
            t.ParentTaskId = dto.ParentTaskId; t.ContractId = dto.ContractId; t.WbsNodeId = dto.WbsNodeId; t.ActivityId = dto.ActivityId; t.DecisionId = dto.DecisionId;
            t.LetterId = dto.LetterId; t.RequestId = dto.RequestId; t.RiskId = dto.RiskId; t.IssueId = dto.IssueId;
            t.ModifyDate = now; t.ModifierUserId = Me;

            var newRows = new List<TmTaskAssignee>();
            if (isNew)
            {
                Hist(t, TmHistoryKind.Created, null, TaskRules.StatusText(TmTaskStatus.Open), "وظیفه ثبت شد");
                int seq = 0;
                foreach (string item in (dto.Checklist ?? new List<string>()).Select(Clean).Where(x => x != null))
                    t.Checklist.Add(new TmTaskChecklistItem { Seq = ++seq, Text = OfficeText.Cap(item, 500) });
                if (dto.Assignees != null && dto.Assignees.Count > 0)
                {
                    string problem = await BuildAssigneesAsync(t, dto.Assignees, null, newRows, now, ct);
                    if (problem != null) { _uow.Entry(t).State = EntityState.Detached; return Bad<TmTaskDto>(problem); }
                    if (newRows.Any(r => r.Role != TmAssigneeRole.Observer)) { t.Status = TmTaskStatus.Assigned; Hist(t, TmHistoryKind.Status, TaskRules.StatusText(TmTaskStatus.Open), TaskRules.StatusText(TmTaskStatus.Assigned), null); }
                }
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            if (newRows.Count > 0) await NotifyAssigneesAsync(t, newRows, "وظیفه جدید به شما ارجاع شد: ", ct);
            await RollUpAsync(new int?[] { oldActivity, t.ActivityId }, ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        // ============================================================================ referral

        /// <summary>Checks the targets and adds one referral row for each (on the task, not saved). Returns the Persian problem or null.</summary>
        private async Task<string> BuildAssigneesAsync(TmTask t, List<TmAssignTargetDto> targets, int? parentId, List<TmTaskAssignee> added, DateTimeOffset now, CancellationToken ct)
        {
            if (targets == null || targets.Count == 0) return "گیرنده ارجاع را مشخص کنید.";
            foreach (TmAssignTargetDto target in targets)
            {
                if (target.UserId.HasValue == target.PositionId.HasValue) return "برای هر گیرنده فقط یکی از «کاربر» یا «سمت» را مشخص کنید.";
                if (!Enum.IsDefined(typeof(TmAssigneeRole), target.Role)) return OfficeText.InvalidData;
                if (target.UserId.HasValue && !await _uow.Set<User>().AnyAsync(u => u.Id == target.UserId.Value && u.IsActive && !u.IsDeleted, ct)) return "کاربر پیدا نشد یا غیرفعال است.";
                if (target.PositionId.HasValue && !await _uow.Set<OfPosition>().AnyAsync(p => p.Id == target.PositionId.Value && !p.Deleted && p.IsActive, ct)) return "سمت پیدا نشد یا غیرفعال است.";
                var all = (t.Assignees ?? new List<TmTaskAssignee>()).Concat(added).ToList();
                if (all.Any(a => a.State == TmAssigneeState.Active && a.UserId == target.UserId && a.PositionId == target.PositionId && a.Role == target.Role))
                    return "این گیرنده همین نقش را همین حالا در وظیفه دارد.";
                var row = new TmTaskAssignee
                {
                    Task = t, UserId = target.UserId, PositionId = target.PositionId, Role = target.Role, State = TmAssigneeState.Active,
                    Reason = OfficeText.Cap(Clean(target.Reason) ?? (target.Role == TmAssigneeRole.Observer ? "جهت اطلاع" : "جهت اقدام"), 1000), AssignedByUserId = Me, AssignedAt = now, ParentId = parentId,
                };
                added.Add(row);
                if (t.Assignees == null) t.Assignees = new List<TmTaskAssignee>();
                t.Assignees.Add(row);
                string who = target.UserId.HasValue ? "کاربر " + target.UserId.Value.ToString(CultureInfo.InvariantCulture) : "سمت " + target.PositionId.Value.ToString(CultureInfo.InvariantCulture);
                Hist(t, TmHistoryKind.Referral, null, who + " (" + target.Role + ")", row.Reason);
            }
            return null;
        }

        public async Task<ResponseDto<TmTaskDto>> AssignAsync(TmTaskAssignDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            Ctx c = await CtxAsync(ct);
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == dto.TaskId && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            if (t.Kind == TmTaskKind.Personal) return Bad<TmTaskDto>("وظیفه شخصی به دیگری ارجاع نمی‌شود.");
            if (t.Status == TmTaskStatus.Closed || t.Status == TmTaskStatus.Pending || t.Status == TmTaskStatus.Suspended) return Bad<TmTaskDto>("در وضعیت «" + TaskRules.StatusText(t.Status) + "» ارجاع جدید ممکن نیست.");
            Rights r = RightsOf(t, c);
            int? parent = dto.ParentAssigneeId;
            if (parent.HasValue)
            {
                TmTaskAssignee p = t.Assignees.FirstOrDefault(a => a.Id == parent.Value);
                if (p == null) return Bad<TmTaskDto>("ارجاع مبدأ در این وظیفه نیست.");
                bool mine = p.UserId == c.Me || (p.PositionId.HasValue && c.Positions.Contains(p.PositionId.Value));
                if (!mine && !r.Editor) return Bad<TmTaskDto>(NotAllowed + " (ارجاع زنجیره‌ای فقط از ارجاع خودتان)");
            }
            else if (r.Editor) { /* the creator refers at the first level */ }
            else if (r.IsAssignee)
            {
                // an assignee refers on from his own (latest active) referral
                parent = t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer && (a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value)))).OrderBy(a => a.Id).Select(a => (int?)a.Id).FirstOrDefault();
            }
            else return Bad<TmTaskDto>(NotAllowed);
            var added = new List<TmTaskAssignee>();
            string problem = await BuildAssigneesAsync(t, dto.Targets, parent, added, DateTimeOffset.Now, ct);
            if (problem != null) { foreach (TmTaskAssignee a in added) _uow.Entry(a).State = EntityState.Detached; return Bad<TmTaskDto>(problem); }
            if (added.Any(a => a.Role != TmAssigneeRole.Observer) && (t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Rejected))
            {
                Hist(t, TmHistoryKind.Status, TaskRules.StatusText(t.Status), TaskRules.StatusText(TmTaskStatus.Assigned), null);
                t.Status = TmTaskStatus.Assigned; t.StatusReason = null;
            }
            t.ModifyDate = DateTimeOffset.Now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            await NotifyAssigneesAsync(t, added, "وظیفه جدید به شما ارجاع شد: ", ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> TransferAsync(TmTaskTransferDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<TmTaskDto>("دلیل انتقال بین کارتابل‌ها را بنویسید.");
            if (dto.ToUserId.HasValue == dto.ToPositionId.HasValue) return Bad<TmTaskDto>("کارتابل مقصد را (کاربر یا سمت) مشخص کنید.");
            Ctx c = await CtxAsync(ct);
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == dto.TaskId && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            if (t.Status == TmTaskStatus.Closed || t.Status == TmTaskStatus.Rejected || t.Status == TmTaskStatus.Pending || t.Status == TmTaskStatus.Suspended) return Bad<TmTaskDto>("در وضعیت «" + TaskRules.StatusText(t.Status) + "» انتقال ممکن نیست.");
            TmTaskAssignee old = t.Assignees.FirstOrDefault(a => a.Id == dto.AssigneeId);
            if (old == null) return Bad<TmTaskDto>("ارجاع پیدا نشد.");
            if (old.State != TmAssigneeState.Active) return Bad<TmTaskDto>("این ارجاع دیگر در کارتابل فعال نیست و منتقل نمی‌شود.");
            Rights r = RightsOf(t, c);
            bool holder = old.UserId == c.Me || (old.PositionId.HasValue && c.Positions.Contains(old.PositionId.Value));
            if (!holder && !r.Editor) return Bad<TmTaskDto>(NotAllowed + " (انتقال فقط توسط دارنده کارتابل، ثبت‌کننده یا PMO)");
            if (old.UserId == dto.ToUserId && old.PositionId == dto.ToPositionId) return Bad<TmTaskDto>("کارتابل مقصد همان کارتابل فعلی است.");
            var added = new List<TmTaskAssignee>();
            DateTimeOffset now = DateTimeOffset.Now;
            string problem = await BuildAssigneesAsync(t, new List<TmAssignTargetDto> { new TmAssignTargetDto { UserId = dto.ToUserId, PositionId = dto.ToPositionId, Role = old.Role, Reason = reason } }, old.Id, added, now, ct);
            if (problem != null) { foreach (TmTaskAssignee a in added) _uow.Entry(a).State = EntityState.Detached; return Bad<TmTaskDto>(problem); }
            // the old referral ends; who, why and when of its creation stay as they were
            old.State = TmAssigneeState.Transferred; old.EndedAt = now; old.EndedByUserId = Me; old.EndReason = OfficeText.Cap(reason, 1000);
            Hist(t, TmHistoryKind.Transfer, null, null, reason);
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            await NotifyAssigneesAsync(t, added, "وظیفه به کارتابل شما منتقل شد: ", ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> WithdrawAsync(TmTaskWithdrawDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<TmTaskDto>("دلیل پس‌گرفتن ارجاع را بنویسید.");
            Ctx c = await CtxAsync(ct);
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == dto.TaskId && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            if (!RightsOf(t, c).Editor) return Bad<TmTaskDto>(NotAllowed);
            if (t.Status == TmTaskStatus.Closed || t.Status == TmTaskStatus.Pending) return Bad<TmTaskDto>("در وضعیت «" + TaskRules.StatusText(t.Status) + "» ارجاع پس گرفته نمی‌شود.");
            TmTaskAssignee row = t.Assignees.FirstOrDefault(a => a.Id == dto.AssigneeId);
            if (row == null || row.State != TmAssigneeState.Active) return Bad<TmTaskDto>("ارجاع فعال پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            row.State = TmAssigneeState.Withdrawn; row.EndedAt = now; row.EndedByUserId = Me; row.EndReason = OfficeText.Cap(reason, 1000);
            Hist(t, TmHistoryKind.Referral, "ارجاع " + row.Id.ToString(CultureInfo.InvariantCulture), "پس گرفته شد", reason);
            if (t.Status == TmTaskStatus.Assigned && !t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer))
            {
                Hist(t, TmHistoryKind.Status, TaskRules.StatusText(TmTaskStatus.Assigned), TaskRules.StatusText(TmTaskStatus.Open), null);
                t.Status = TmTaskStatus.Open;
            }
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            return await ReloadAsync(t.Id, c, ct);
        }

        // ============================================================================ status machine

        public async Task<ResponseDto<TmTaskDto>> ActionAsync(TmTaskActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string action = dto.Action.Trim().ToLowerInvariant();
            if (!TaskRules.Actions.Contains(action)) return Bad<TmTaskDto>("عمل نامعتبر است.");
            Ctx c = await CtxAsync(ct);
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).Include(x => x.Checklist).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            Rights r = RightsOf(t, c);
            string note = Clean(dto.Note);
            TmTaskStatus from = t.Status;
            if (!TaskRules.TryNext(from, action, t.StatusBeforeSuspend, out TmTaskStatus to)) return Bad<TmTaskDto>("از وضعیت «" + TaskRules.StatusText(from) + "» این عمل ممکن نیست.");
            bool personal = t.Kind == TmTaskKind.Personal;
            bool noAssignee = !t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer);
            bool worker = r.IsAssignee || (r.IsCreator && (personal || noAssignee));
            DateTimeOffset now = DateTimeOffset.Now;
            var spawn = false;
            switch (action)
            {
                case "start":
                    if (!worker) return Bad<TmTaskDto>(NotAllowed + " (فقط گیرنده وظیفه آن را شروع می‌کند)");
                    t.StartedAt ??= now;
                    break;
                case "complete":
                    if (!worker) return Bad<TmTaskDto>(NotAllowed + " (فقط گیرنده وظیفه انجام آن را گزارش می‌کند)");
                    if (t.Checklist.Any(i => !i.Done)) return Bad<TmTaskDto>("چک‌لیست وظیفه کامل نیست؛ موارد باقی‌مانده را انجام دهید.");
                    t.StartedAt ??= now; t.CompletedAt = now; t.CompletedByUserId = Me;
                    foreach (TmTaskAssignee a in t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer && (a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value))))) a.State = TmAssigneeState.Done;
                    if (personal) { to = TmTaskStatus.Closed; spawn = true; }
                    break;
                case "approve":
                    if (!CanApprove(t, r, c)) return Bad<TmTaskDto>(t.CompletedByUserId == Me ? PcText.SameUser + " (گزارش‌دهنده انجام وظیفه آن را تایید نمی‌کند)" : NotAllowed);
                    spawn = true;
                    break;
                case "return":
                    if (!CanApprove(t, r, c)) return Bad<TmTaskDto>(t.CompletedByUserId == Me ? PcText.SameUser : NotAllowed);
                    if (note == null) return Bad<TmTaskDto>("دلیل برگشت را بنویسید.");
                    foreach (TmTaskAssignee a in t.Assignees.Where(a => a.State == TmAssigneeState.Done)) a.State = TmAssigneeState.Active;
                    t.CompletedAt = null; t.CompletedByUserId = null;
                    break;
                case "reject":
                    {
                        if (note == null) return Bad<TmTaskDto>("دلیل رد را بنویسید.");
                        if (!r.IsAssignee && !r.Editor) return Bad<TmTaskDto>(NotAllowed);
                        if (!r.Editor)
                        {
                            // an assignee declines his own referral; the task is rejected only when nobody else holds it
                            foreach (TmTaskAssignee a in t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer && (a.UserId == c.Me || (a.PositionId.HasValue && c.Positions.Contains(a.PositionId.Value)))))
                            { a.State = TmAssigneeState.Declined; a.EndedAt = now; a.EndedByUserId = Me; a.EndReason = OfficeText.Cap(note, 1000); }
                            if (t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer))
                            {
                                Hist(t, TmHistoryKind.Referral, null, "ارجاع را نپذیرفت", note);
                                t.ModifyDate = now; t.ModifierUserId = Me;
                                try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
                                return await ReloadAsync(t.Id, c, ct);
                            }
                        }
                        break;
                    }
                case "suspend":
                    if (!r.Editor && !r.IsResponsible) return Bad<TmTaskDto>(NotAllowed);
                    if (note == null) return Bad<TmTaskDto>("دلیل تعلیق را بنویسید.");
                    t.StatusBeforeSuspend = from;
                    break;
                case "resume":
                    if (!r.Editor && !r.IsAssignee) return Bad<TmTaskDto>(NotAllowed);
                    t.StatusBeforeSuspend = null;
                    break;
                case "reopen":
                    if (from == TmTaskStatus.Closed ? !r.Pmo : !r.Editor) return Bad<TmTaskDto>(NotAllowed + (from == TmTaskStatus.Closed ? " (بازگشایی وظیفه بسته‌شده با PMO است)" : ""));
                    if (note == null) return Bad<TmTaskDto>("دلیل بازگشایی را بنویسید.");
                    t.ClosedAt = null; t.ClosedByUserId = null; t.CompletedAt = null; t.CompletedByUserId = null;
                    if (t.PercentComplete >= 100m) t.PercentComplete = 99m;
                    foreach (TmTaskAssignee a in t.Assignees.Where(a => a.State == TmAssigneeState.Done)) a.State = TmAssigneeState.Active;
                    to = t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer) ? TmTaskStatus.Assigned : TmTaskStatus.Open;
                    break;
            }
            if (to == TmTaskStatus.Closed)
            {
                t.ClosedAt = now; t.ClosedByUserId = Me; t.PercentComplete = 100m;
                if (personal && t.CompletedByUserId == null) t.CompletedByUserId = Me;
            }
            t.Status = to;
            t.StatusReason = OfficeText.Cap(note, 1000);
            Hist(t, TmHistoryKind.Status, TaskRules.StatusText(from), TaskRules.StatusText(to), note);
            t.ModifyDate = now; t.ModifierUserId = Me;

            TmTask next = spawn && to == TmTaskStatus.Closed ? await BuildNextOccurrenceAsync(t, now, ct) : null;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }

            if (to == TmTaskStatus.Pending) await NotifyApproversAsync(t, ct);
            if (from == TmTaskStatus.Pending) await _notify.CloseAsync("task-approve:" + t.Id.ToString(CultureInfo.InvariantCulture), "تصمیم گرفته شد", ct);
            if (to == TmTaskStatus.Closed || to == TmTaskStatus.Rejected) await CloseNoticesAsync(t, "وظیفه " + TaskRules.StatusText(to) + " شد", ct);
            if (action == "return") await NotifyAssigneesAsync(t, t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer), "وظیفه برای اصلاح برگشت: ", ct);
            if (to == TmTaskStatus.Rejected && !r.IsAssignee) await NotifyAssigneesAsync(t, t.Assignees.Where(a => a.State == TmAssigneeState.Active), "وظیفه رد شد: ", ct);
            if (next != null) await NotifyAssigneesAsync(next, next.Assignees, "وظیفه تکراری تازه: ", ct);
            await RollUpAsync(new int?[] { t.ActivityId }, ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        /// <summary>When a recurring task closes, the next occurrence is created (same assignees, a fresh checklist, the next due date). Added to the context, not saved.</summary>
        private async Task<TmTask> BuildNextOccurrenceAsync(TmTask t, DateTimeOffset now, CancellationToken ct)
        {
            if (t.Recurrence == TmRecurrence.None || !t.DueDate.HasValue) return null;
            DateTime? due = TaskRules.NextOccurrence(t.DueDate.Value, t.Recurrence, t.RecurrenceInterval, t.RecurrenceEnd);
            if (!due.HasValue) return null;
            int sourceId = t.RecurrenceSourceId ?? t.Id;
            var next = new TmTask
            {
                CreateDate = now, ModifyDate = now, CreatorUserId = t.CreatorUserId, ModifierUserId = Me, Title = t.Title, Description = t.Description, Kind = t.Kind, Category = t.Category, Priority = t.Priority, Importance = t.Importance,
                Confidentiality = t.Confidentiality, RequestType = t.RequestType, Requester = t.Requester, ReferringOrganisation = t.ReferringOrganisation,
                StartDate = t.StartDate.HasValue && t.DueDate.HasValue ? due.Value.AddDays(-(t.DueDate.Value - t.StartDate.Value).TotalDays) : (DateTime?)null, DueDate = due, EstimatedHours = t.EstimatedHours, EstimatedCost = t.EstimatedCost,
                Recurrence = t.Recurrence, RecurrenceInterval = t.RecurrenceInterval, RecurrenceEnd = t.RecurrenceEnd, RecurrenceSourceId = sourceId, ReminderDays = t.ReminderDays, ParentTaskId = t.ParentTaskId,
                ContractId = t.ContractId, WbsNodeId = t.WbsNodeId, ActivityId = t.ActivityId, Status = TmTaskStatus.Open,
                Assignees = new List<TmTaskAssignee>(), Checklist = new List<TmTaskChecklistItem>(), Watchers = new List<TmTaskWatcher>(), Comments = new List<TmTaskComment>(), Attachments = new List<TmTaskAttachment>(),
            };
            next.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Task, "TK", DateTime.Today, ct);
            foreach (TmTaskChecklistItem item in t.Checklist ?? new List<TmTaskChecklistItem>()) next.Checklist.Add(new TmTaskChecklistItem { Seq = item.Seq, Text = item.Text });
            foreach (TmTaskAssignee a in t.Assignees.Where(a => a.ParentId == null && (a.State == TmAssigneeState.Active || a.State == TmAssigneeState.Done)).ToList())
                next.Assignees.Add(new TmTaskAssignee { UserId = a.UserId, PositionId = a.PositionId, Role = a.Role, State = TmAssigneeState.Active, Reason = "تکرار دوره‌ای وظیفه " + t.Number, AssignedByUserId = Me, AssignedAt = now });
            // transferred referrals: the current holders
            foreach (TmTaskAssignee a in t.Assignees.Where(a => a.ParentId != null && (a.State == TmAssigneeState.Active || a.State == TmAssigneeState.Done)))
                if (!next.Assignees.Any(x => x.UserId == a.UserId && x.PositionId == a.PositionId && x.Role == a.Role))
                    next.Assignees.Add(new TmTaskAssignee { UserId = a.UserId, PositionId = a.PositionId, Role = a.Role, State = TmAssigneeState.Active, Reason = "تکرار دوره‌ای وظیفه " + t.Number, AssignedByUserId = Me, AssignedAt = now });
            if (next.Assignees.Any(a => a.Role != TmAssigneeRole.Observer)) next.Status = TmTaskStatus.Assigned;
            _uow.Set<TmTask>().Add(next);
            _uow.Set<TmTaskHistory>().Add(new TmTaskHistory { Task = next, At = now, UserId = Me, Kind = TmHistoryKind.Created, ToValue = TaskRules.StatusText(next.Status), Text = "تکرار دوره‌ای وظیفه " + t.Number });
            Hist(t, TmHistoryKind.Link, null, "تکرار بعدی ساخته شد", OfficeText.Date(due));
            return next;
        }

        // ============================================================================ progress, checklist, comments, watchers, files

        public async Task<ResponseDto<TmTaskDto>> UpdateProgressAsync(TmTaskProgressDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            Ctx c = await CtxAsync(ct);
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == dto.TaskId && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return OfficeText.NotFound<TmTaskDto>(NotFoundText);
            Rights r = RightsOf(t, c);
            bool noAssignee = !t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer);
            if (!(r.IsAssignee || (r.IsCreator && (t.Kind == TmTaskKind.Personal || noAssignee)))) return Bad<TmTaskDto>(NotAllowed + " (پیشرفت را گیرنده وظیفه ثبت می‌کند)");
            if (t.Status != TmTaskStatus.Open && t.Status != TmTaskStatus.Assigned && t.Status != TmTaskStatus.InProgress) return Bad<TmTaskDto>("در وضعیت «" + TaskRules.StatusText(t.Status) + "» پیشرفت ثبت نمی‌شود.");
            if (dto.Percent.HasValue && (dto.Percent.Value < 0m || dto.Percent.Value > 100m)) return Bad<TmTaskDto>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
            if ((dto.ActualHours.HasValue && dto.ActualHours.Value < 0m) || (dto.ActualCost.HasValue && dto.ActualCost.Value < 0m)) return Bad<TmTaskDto>("زمان و هزینه واقعی منفی نیست.");
            bool linked = t.ContractId.HasValue && t.ActivityId.HasValue && t.Kind != TmTaskKind.Personal;
            if (dto.Percent.HasValue && linked && dto.Percent.Value != t.PercentComplete)
                return Bad<TmTaskDto>("درصد پیشرفت وظیفه‌ای که به فعالیت WBS وصل است از «کارتابل تایید پیشرفت» و با تایید زنجیره ثبت می‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Percent.HasValue && dto.Percent.Value != t.PercentComplete)
            {
                Hist(t, TmHistoryKind.Progress, t.PercentComplete.ToString("0.##", CultureInfo.InvariantCulture), dto.Percent.Value.ToString("0.##", CultureInfo.InvariantCulture), Clean(dto.Note));
                t.PercentComplete = dto.Percent.Value;
                if (dto.Percent.Value > 0m && (t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Assigned))
                {
                    Hist(t, TmHistoryKind.Status, TaskRules.StatusText(t.Status), TaskRules.StatusText(TmTaskStatus.InProgress), null);
                    t.Status = TmTaskStatus.InProgress; t.StartedAt ??= now;
                }
            }
            if (dto.ActualHours.HasValue && dto.ActualHours.Value != t.ActualHours) { Hist(t, TmHistoryKind.Progress, "ساعت " + t.ActualHours.ToString("0.##", CultureInfo.InvariantCulture), "ساعت " + dto.ActualHours.Value.ToString("0.##", CultureInfo.InvariantCulture), null); t.ActualHours = dto.ActualHours.Value; }
            if (dto.ActualCost.HasValue && dto.ActualCost.Value != t.ActualCost) { Hist(t, TmHistoryKind.Progress, "هزینه " + t.ActualCost.ToString("0.##", CultureInfo.InvariantCulture), "هزینه " + dto.ActualCost.Value.ToString("0.##", CultureInfo.InvariantCulture), "هزینه واقعی وظیفه اطلاعاتی است و در حسابداری پروژه نمی‌نشیند"); t.ActualCost = dto.ActualCost.Value; }
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            await RollUpAsync(new int?[] { t.ActivityId }, ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<bool>> ApplyApprovedProgressAsync(int taskId, decimal percent, DateTime? actualStart, DateTime? actualFinish, string note, int approvedByUserId, CancellationToken ct)
        {
            TmTask t = await _uow.Set<TmTask>().FirstOrDefaultAsync(x => x.Id == taskId && !x.Deleted, ct);
            if (t == null) return OfficeText.NotFound<bool>(NotFoundText);
            if (t.Status == TmTaskStatus.Closed || t.Status == TmTaskStatus.Rejected || t.Status == TmTaskStatus.Suspended) return Bad<bool>("در وضعیت «" + TaskRules.StatusText(t.Status) + "» پیشرفت وظیفه ثبت نمی‌شود.");
            if (percent < 0m || percent > 100m) return Bad<bool>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (percent != t.PercentComplete) Hist(t, TmHistoryKind.Progress, t.PercentComplete.ToString("0.##", CultureInfo.InvariantCulture), percent.ToString("0.##", CultureInfo.InvariantCulture), "با تایید کارتابل پیشرفت" + (Clean(note) == null ? "" : ": " + Clean(note)));
            t.PercentComplete = percent;
            if (percent > 0m && (t.Status == TmTaskStatus.Open || t.Status == TmTaskStatus.Assigned))
            {
                Hist(t, TmHistoryKind.Status, TaskRules.StatusText(t.Status), TaskRules.StatusText(TmTaskStatus.InProgress), null);
                t.Status = TmTaskStatus.InProgress;
            }
            if (percent > 0m) t.StartedAt ??= actualStart.HasValue ? new DateTimeOffset(actualStart.Value.Date) : now;
            if (actualStart.HasValue && !t.StartDate.HasValue) t.StartDate = actualStart.Value.Date;
            t.ModifyDate = now; t.ModifierUserId = approvedByUserId;
            await _uow.SaveChangesAsync(ct);
            await RollUpAsync(new int?[] { t.ActivityId }, ct);
            return ResponseDtoHelper.Ok(true);
        }

        private async Task<(TmTask Task, Rights Rights, string Problem)> LoadForEditAsync(int taskId, bool allowClosed, Ctx c, CancellationToken ct)
        {
            TmTask t = await _uow.Set<TmTask>().Include(x => x.Assignees).Include(x => x.Checklist).Include(x => x.Watchers).Include(x => x.Attachments).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == taskId && !x.Deleted, ct);
            if (t == null || !await CanReadTaskAsync(t.Id, ct)) return (null, null, NotFoundText);
            if (!allowClosed && (t.Status == TmTaskStatus.Closed || t.Status == TmTaskStatus.Rejected)) return (t, null, "وظیفه " + TaskRules.StatusText(t.Status) + " تغییر نمی‌کند.");
            return (t, RightsOf(t, c), null);
        }

        public async Task<ResponseDto<TmTaskDto>> ChecklistAsync(TmTaskChecklistDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            Ctx c = await CtxAsync(ct);
            var (t, r, problem) = await LoadForEditAsync(dto.TaskId, false, c, ct);
            if (problem != null) return t == null ? OfficeText.NotFound<TmTaskDto>(problem) : Bad<TmTaskDto>(problem);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            bool noAssignee = !t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer);
            bool worker = r.IsAssignee || r.Editor || (r.IsCreator && noAssignee);
            if (!worker) return Bad<TmTaskDto>(NotAllowed);
            switch (action)
            {
                case "add":
                    {
                        string text = Clean(dto.Text);
                        if (text == null) return Bad<TmTaskDto>("متن مورد چک‌لیست را بنویسید.");
                        t.Checklist.Add(new TmTaskChecklistItem { Seq = (t.Checklist.Count == 0 ? 0 : t.Checklist.Max(i => i.Seq)) + 1, Text = OfficeText.Cap(text, 500) });
                        Hist(t, TmHistoryKind.Checklist, null, "افزوده شد", text);
                        break;
                    }
                case "toggle":
                    {
                        TmTaskChecklistItem item = t.Checklist.FirstOrDefault(i => i.Id == dto.ItemId);
                        if (item == null) return Bad<TmTaskDto>("مورد چک‌لیست پیدا نشد.");
                        item.Done = !item.Done; item.DoneByUserId = item.Done ? Me : (int?)null; item.DoneAt = item.Done ? now : (DateTimeOffset?)null;
                        Hist(t, TmHistoryKind.Checklist, null, item.Done ? "انجام شد" : "از حالت انجام برگشت", item.Text);
                        break;
                    }
                case "remove":
                    {
                        TmTaskChecklistItem item = t.Checklist.FirstOrDefault(i => i.Id == dto.ItemId);
                        if (item == null) return Bad<TmTaskDto>("مورد چک‌لیست پیدا نشد.");
                        if (item.Done) return Bad<TmTaskDto>("مورد انجام‌شده حذف نمی‌شود.");
                        if (!r.Editor) return Bad<TmTaskDto>(NotAllowed);
                        _uow.Set<TmTaskChecklistItem>().Remove(item);
                        Hist(t, TmHistoryKind.Checklist, null, "حذف شد", item.Text);
                        break;
                    }
                default:
                    return Bad<TmTaskDto>("عمل چک‌لیست نامعتبر است.");
            }
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<TmTaskDto>(OfficeText.Concurrency); }
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> CommentAsync(TmTaskCommentDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string text = Clean(dto.Text);
            if (text == null) return Bad<TmTaskDto>("متن توضیح را بنویسید.");
            Ctx c = await CtxAsync(ct);
            var (t, _, problem) = await LoadForEditAsync(dto.TaskId, true, c, ct);
            if (problem != null) return OfficeText.NotFound<TmTaskDto>(problem);
            if (dto.ParentId.HasValue && !await _uow.Set<TmTaskComment>().AnyAsync(m => m.Id == dto.ParentId.Value && m.TaskId == t.Id, ct)) return Bad<TmTaskDto>("توضیح مبدأ در این وظیفه نیست.");
            _uow.Set<TmTaskComment>().Add(new TmTaskComment { TaskId = t.Id, ParentId = dto.ParentId, UserId = Me, Text = OfficeText.Cap(text, 4000), At = DateTimeOffset.Now });
            Hist(t, TmHistoryKind.Comment, null, null, OfficeText.Cap(text, 200));
            await _uow.SaveChangesAsync(ct);
            // the watchers and the other parties see the comment in their inbox
            var users = new HashSet<int>(t.Watchers.Select(w => w.UserId)) { t.CreatorUserId };
            foreach (int u in t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.UserId.HasValue).Select(a => a.UserId.Value)) users.Add(u);
            users.Remove(Me);
            if (users.Count > 0 && TaskRules.IsOpen(t.Status))
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "task-comment:" + t.Id.ToString(CultureInfo.InvariantCulture), Module = "tasks", SourceType = "task", SourceId = t.Id, Title = "توضیح تازه در وظیفه: " + Shown(t), Link = Link(t), Kind = OfInboxKind.Alert,
                    Priority = OfUrgency.Normal, UserIds = users.ToList(),
                }, ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> WatchAsync(int taskId, bool watch, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            var (t, _, problem) = await LoadForEditAsync(taskId, true, c, ct);
            if (problem != null) return OfficeText.NotFound<TmTaskDto>(problem);
            TmTaskWatcher existing = t.Watchers.FirstOrDefault(w => w.UserId == Me);
            if (watch && existing == null) { t.Watchers.Add(new TmTaskWatcher { UserId = Me, At = DateTimeOffset.Now }); Hist(t, TmHistoryKind.Watcher, null, "پیگیری شروع شد", null); }
            else if (!watch && existing != null) { _uow.Set<TmTaskWatcher>().Remove(existing); Hist(t, TmHistoryKind.Watcher, null, "پیگیری پایان یافت", null); }
            await _uow.SaveChangesAsync(ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> AttachAsync(TmTaskAttachDto dto, CancellationToken ct)
        {
            if (dto == null || dto.DocumentId == Guid.Empty) return Bad<TmTaskDto>(OfficeText.InvalidData);
            Ctx c = await CtxAsync(ct);
            var (t, r, problem) = await LoadForEditAsync(dto.TaskId, false, c, ct);
            if (problem != null) return t == null ? OfficeText.NotFound<TmTaskDto>(problem) : Bad<TmTaskDto>(problem);
            if (!(r.Editor || r.IsAssignee || r.IsCreator)) return Bad<TmTaskDto>(NotAllowed);
            // the file must be the caller's own upload (a file of another record is never opened to the viewers of a task through this door)
            var doc = await _uow.Set<Document>().AsNoTracking().Where(d => d.Id == dto.DocumentId).Select(d => new { d.Id, d.Name, d.UploadedByUserId }).FirstOrDefaultAsync(ct);
            if (doc == null) return OfficeText.NotFound<TmTaskDto>("فایل پیدا نشد.");
            if (doc.UploadedByUserId != Me) return Bad<TmTaskDto>("فقط فایلی که خودتان بارگذاری کرده‌اید پیوست می‌شود.");
            if (await _uow.Set<TmTaskAttachment>().AnyAsync(a => a.TaskId == t.Id && a.DocumentId == dto.DocumentId && !a.IsVoid, ct)) return Bad<TmTaskDto>("این فایل قبلاً پیوست شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            TmTaskAttachment older = null;
            if (dto.SupersedesId.HasValue)
            {
                older = t.Attachments.FirstOrDefault(a => a.Id == dto.SupersedesId.Value && !a.IsVoid);
                if (older == null) return Bad<TmTaskDto>("پیوست قدیمی برای جایگزینی پیدا نشد.");
                older.IsVoid = true; older.VoidedByUserId = Me; older.VoidedAt = now; older.VoidReason = "جایگزین شد با فایل تازه";
            }
            t.Attachments.Add(new TmTaskAttachment { DocumentId = dto.DocumentId, FileName = OfficeText.Cap(Clean(dto.FileName) ?? doc.Name, 300), UploadedByUserId = Me, UploadedAt = now, SupersedesId = older?.Id });
            Hist(t, TmHistoryKind.Attachment, null, "پیوست شد", Clean(dto.FileName) ?? doc.Name);
            await _uow.SaveChangesAsync(ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        public async Task<ResponseDto<TmTaskDto>> VoidAttachmentAsync(TmTaskVoidDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<TmTaskDto>(OfficeText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<TmTaskDto>("دلیل ابطال پیوست را بنویسید.");
            Ctx c = await CtxAsync(ct);
            var (t, r, problem) = await LoadForEditAsync(dto.TaskId, false, c, ct);
            if (problem != null) return t == null ? OfficeText.NotFound<TmTaskDto>(problem) : Bad<TmTaskDto>(problem);
            TmTaskAttachment a = t.Attachments.FirstOrDefault(x => x.Id == dto.AttachmentId);
            if (a == null) return Bad<TmTaskDto>("پیوست پیدا نشد.");
            if (a.IsVoid) return Bad<TmTaskDto>("این پیوست قبلاً باطل شده است.");
            if (a.UploadedByUserId != Me && !r.Editor) return Bad<TmTaskDto>(NotAllowed + " (ابطال پیوست با بارگذارنده یا ثبت‌کننده وظیفه است)");
            a.IsVoid = true; a.VoidedByUserId = Me; a.VoidedAt = DateTimeOffset.Now; a.VoidReason = OfficeText.Cap(reason, 1000);
            Hist(t, TmHistoryKind.Attachment, a.FileName, "باطل شد", reason);
            await _uow.SaveChangesAsync(ct);
            return await ReloadAsync(t.Id, c, ct);
        }

        // ============================================================================ meeting decisions, reminders

        public async Task<ResponseDto<TmTaskDto>> FromDecisionAsync(int decisionId, CancellationToken ct)
        {
            Ctx c = await CtxAsync(ct);
            OfDecision d = await _uow.Set<OfDecision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == decisionId, ct);
            if (d == null) return OfficeText.NotFound<TmTaskDto>("مصوبه پیدا نشد.");
            OfMeeting m = await _uow.Set<OfMeeting>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.MeetingId && !x.Deleted, ct);
            if (m == null || m.Status != OfMeetingStatus.MinutesApproved) return Bad<TmTaskDto>("فقط مصوبه صورتجلسه تاییدشده به وظیفه تبدیل می‌شود.");
            if (d.Status != OfDecisionStatus.Open) return Bad<TmTaskDto>("مصوبه باز نیست.");
            bool may = _user.IsInRole(OfficeRoles.MeetingCreators) || c.Positions.Contains(d.OwnerPositionId) || (m.ChairPositionId.HasValue && c.Positions.Contains(m.ChairPositionId.Value));
            if (!may) return Bad<TmTaskDto>(NotAllowed);
            TmTask existing = await _uow.Set<TmTask>().AsNoTracking().FirstOrDefaultAsync(t => t.DecisionId == decisionId && !t.Deleted && t.Status != TmTaskStatus.Rejected, ct);
            if (existing != null) return Bad<TmTaskDto>("برای این مصوبه قبلاً وظیفه " + existing.Number + " ساخته شده است.");
            var dto = new TmTaskSaveDto
            {
                Title = OfficeText.Cap(Clean(d.Text) ?? "مصوبه جلسه", 300), Description = d.Text, Kind = TmTaskKind.MeetingAction, Category = "مصوبه جلسه", Priority = TmPriority.Normal, DueDate = d.DueDate, DecisionId = d.Id,
                Requester = "جلسه " + m.Number, RequestType = "مصوبه صورتجلسه",
                Assignees = new List<TmAssignTargetDto> { new TmAssignTargetDto { PositionId = d.OwnerPositionId, Role = TmAssigneeRole.Responsible, Reason = "مصوبه جلسه " + m.Number } },
            };
            return await SaveTaskAsync(dto, ct);
        }

        public async Task<ResponseDto<TmReminderRunDto>> RunRemindersAsync(CancellationToken ct)
        {
            DateTime today = DateTime.Today;
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, ct);
            int defaultOffset = (int)settings[PcSettingKeys.TaskReminderDays];
            int escalate = (int)settings[PcSettingKeys.TaskOverdueEscalateDays];
            List<TmTask> tasks = await _uow.Set<TmTask>().AsNoTracking().Include(t => t.Assignees).Where(t => !t.Deleted && t.DueDate != null && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected && t.Status != TmTaskStatus.Suspended).ToListAsync(ct);
            var result = new TmReminderRunDto();
            foreach (TmTask t in tasks)
            {
                var holders = t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer).ToList();
                var users = holders.Where(a => a.UserId.HasValue).Select(a => a.UserId.Value).Distinct().ToList();
                var positions = holders.Where(a => a.PositionId.HasValue).Select(a => a.PositionId.Value).Distinct().ToList();
                if (users.Count + positions.Count == 0) { users.Add(t.CreatorUserId); }
                int daysLeft = (int)(t.DueDate.Value.Date - today).TotalDays;
                string key = t.Id.ToString(CultureInfo.InvariantCulture);
                if (daysLeft >= 0)
                {
                    List<int> offsets = TaskRules.ParseOffsets(t.ReminderDays);
                    if (offsets.Count == 0) offsets.Add(defaultOffset);
                    if (offsets.Contains(daysLeft))
                    {
                        await _notify.NotifyAsync(new NotifyRequest
                        {
                            SourceKey = "task-due:" + key + ":" + daysLeft.ToString(CultureInfo.InvariantCulture), Module = "tasks", SourceType = "task", SourceId = t.Id,
                            Title = (daysLeft == 0 ? "مهلت وظیفه امروز است: " : "مهلت وظیفه نزدیک است (" + daysLeft.ToString(CultureInfo.InvariantCulture) + " روز): ") + Shown(t), Link = Link(t), Kind = OfInboxKind.Alert,
                            Priority = daysLeft == 0 ? OfUrgency.Urgent : OfUrgency.Normal, DueDate = t.DueDate, UserIds = users, PositionIds = positions,
                        }, ct);
                        result.Reminders++;
                    }
                }
                else
                {
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = "task-overdue:" + key, Module = "tasks", SourceType = "task", SourceId = t.Id, Title = "وظیفه معوق: " + Shown(t), Link = Link(t), Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent,
                        DueDate = t.DueDate, UserIds = users, PositionIds = positions,
                    }, ct);
                    result.Overdue++;
                    if (-daysLeft >= escalate && !users.Contains(t.CreatorUserId))
                    {
                        await _notify.NotifyAsync(new NotifyRequest
                        {
                            SourceKey = "task-overdue-escalation:" + key, Module = "tasks", SourceType = "task", SourceId = t.Id, Title = "وظیفه ارجاع‌شده شما معوق است: " + Shown(t), Link = Link(t), Kind = OfInboxKind.Alert,
                            Priority = OfUrgency.Urgent, DueDate = t.DueDate, UserIds = new List<int> { t.CreatorUserId },
                        }, ct);
                        result.Escalations++;
                    }
                }
            }
            return ResponseDtoHelper.Ok(result);
        }
    }

    /// <summary>The page type that carries the converted rows (the services keep one per module).</summary>
    internal sealed class PcPageOf<T> : PagedList<T>
    {
        public PcPageOf(IEnumerable<T> items, int count, int pageNumber, int pageSize) : base(items, count, pageNumber, pageSize) { }
    }

    public sealed class FileTaskRules : IFileTaskRules
    {
        private readonly ITaskService _tasks;
        public FileTaskRules(ITaskService tasks) { _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks)); }
        public Task<bool> CanReadTaskAsync(int taskId, CancellationToken cancellationToken) => _tasks.CanReadTaskAsync(taskId, cancellationToken);
    }
}
