using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tasks;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Tasks;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcProgressApprovalService
    {
        Task<ResponseDto<PcChainDto>> GetChainAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcChainDto>> SaveChainAsync(PcChainSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcSubmissionListDto>>> GetSubmissionsAsync(PcSubmissionQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcSubmissionDto>> GetSubmissionAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcSubmissionDto>> SaveSubmissionAsync(PcSubmissionSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcSubmissionDto>> DecideAsync(PcSubmissionDecisionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcNotUpdatedDto>>> GetNotUpdatedAsync(PcNotUpdatedQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcOverdueUpdateDto>>> GetOverdueUpdatesAsync(int? contractId, CancellationToken cancellationToken);

        /// <summary>The daily job: tells the approver of a level that waited longer than the limit (one item per report and level).</summary>
        Task<int> RemindWaitingAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// The progress-approval cartable (appendix 1). A resource reports progress for an assigned activity or task; the report is locked for him from the moment it is sent; the chain of the project (project manager, senior manager, PMO ...)
    /// approves level by level, each approver is a person other than the submitter and may correct the values; a rejection returns the report with the reason; the approved value goes through the same dated-event path as the manual proposal.
    /// </summary>
    public class PcProgressApprovalService : IPcProgressApprovalService
    {
        private const string Managers = "PMO,CEO,PROJECTMANAGER,PLANNING";
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;
        private readonly ITaskService _tasks;
        private readonly IWorkflowNotifier _notify;

        public PcProgressApprovalService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress, ITaskService tasks, IWorkflowNotifier notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);
        private const string NotFoundText = "گزارش پیشرفت پیدا نشد.";
        private List<int> _positions;

        private async Task<List<int>> PositionsAsync(CancellationToken ct) => _positions ??= (await OfficeActors.ActingAsync(_uow, Me, ct)).Select(a => a.PositionId).ToList();

        private async Task<bool> MatchesAsync(string role, int? positionId, CancellationToken ct) =>
            role != null ? _user.IsInRole(role) : positionId.HasValue && (await PositionsAsync(ct)).Contains(positionId.Value);

        // ============================================================================ chain

        private async Task<List<PcProgressChainLevel>> ChainRowsAsync(int contractId, CancellationToken ct) =>
            await _uow.Set<PcProgressChainLevel>().AsNoTracking().Where(l => l.ContractId == contractId).OrderBy(l => l.Level).ToListAsync(ct);

        private async Task<List<PcProgressChainLevel>> EffectiveChainAsync(int contractId, CancellationToken ct)
        {
            List<PcProgressChainLevel> rows = await ChainRowsAsync(contractId, ct);
            if (rows.Count > 0) return rows;
            int n = 0;
            return PcProgressChainRules.DefaultChain().Select(d => new PcProgressChainLevel { ContractId = contractId, Level = ++n, Title = d.Title, Role = d.Role }).ToList();
        }

        public async Task<ResponseDto<PcChainDto>> GetChainAsync(int contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(ProjectControlRoles.Readers)) return Bad<PcChainDto>(OfficeText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, ct)) return OfficeText.NotFound<PcChainDto>(PcText.ContractNotFound);
            return ResponseDtoHelper.Ok(await ChainDtoAsync(contractId, ct));
        }

        private async Task<PcChainDto> ChainDtoAsync(int contractId, CancellationToken ct)
        {
            List<PcProgressChainLevel> own = await ChainRowsAsync(contractId, ct);
            List<PcProgressChainLevel> rows = own.Count > 0 ? own : await EffectiveChainAsync(contractId, ct);
            Dictionary<int, string> titles = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.PositionId), ct);
            int inApproval = await _uow.Set<PcProgressSubmission>().CountAsync(s => !s.Deleted && s.ContractId == contractId && s.State == PcSubmissionState.InApproval, ct);
            return new PcChainDto
            {
                ContractId = contractId, IsDefault = own.Count == 0, Locked = inApproval > 0, InApprovalCount = inApproval,
                Levels = rows.Select(r => new PcChainLevelDto { Level = r.Level, Title = r.Title, Role = r.Role, PositionId = r.PositionId, PositionTitle = titles.Title(r.PositionId) }).ToList(),
            };
        }

        public async Task<ResponseDto<PcChainDto>> SaveChainAsync(PcChainSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcChainDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO,PROJECTMANAGER,PLANNING")) return Bad<PcChainDto>(OfficeText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, ct)) return OfficeText.NotFound<PcChainDto>(PcText.ContractNotFound);
            var levels = (dto.Levels ?? new List<PcChainLevelDto>()).Select(l => (Role: Clean(l.Role)?.ToUpperInvariant(), l.PositionId)).ToList();
            string problem = PcProgressChainRules.Problem(levels);
            if (problem != null) return Bad<PcChainDto>(problem);
            List<int> positionIds = dto.Levels.Where(l => l.PositionId.HasValue).Select(l => l.PositionId.Value).Distinct().ToList();
            if (positionIds.Count > 0 && await _uow.Set<OfPosition>().CountAsync(p => positionIds.Contains(p.Id) && !p.Deleted && p.IsActive, ct) != positionIds.Count) return Bad<PcChainDto>("سمت انتخاب‌شده پیدا نشد یا غیرفعال است.");
            // a report in approval keeps the levels it was sent with, but the chain is not changed under the people who are deciding: wait until the queue is empty
            if (await _uow.Set<PcProgressSubmission>().AnyAsync(s => !s.Deleted && s.ContractId == dto.ContractId && s.State == PcSubmissionState.InApproval, ct))
                return Bad<PcChainDto>("تا زمانی که گزارشی در جریان تایید است زنجیره تغییر نمی‌کند؛ ابتدا آن‌ها را تعیین تکلیف کنید.");
            List<PcProgressChainLevel> old = await _uow.Set<PcProgressChainLevel>().Where(l => l.ContractId == dto.ContractId).ToListAsync(ct);
            _uow.Set<PcProgressChainLevel>().RemoveRange(old);
            int n = 0;
            foreach (PcChainLevelDto l in dto.Levels)
            {
                string role = Clean(l.Role)?.ToUpperInvariant();
                _uow.Set<PcProgressChainLevel>().Add(new PcProgressChainLevel
                {
                    ContractId = dto.ContractId, Level = ++n, Role = role, PositionId = role == null ? l.PositionId : null,
                    Title = OfficeText.Cap(Clean(l.Title) ?? (role ?? "سمت " + l.PositionId), 100),
                });
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ChainDtoAsync(dto.ContractId, ct));
        }

        // ============================================================================ submissions

        private async Task<string> LoadTargetAsync(PcSubmissionSaveDto dto, Action<decimal, string> found, CancellationToken ct)
        {
            if (!Enum.IsDefined(typeof(PcSubmissionTarget), dto.Target)) return OfficeText.InvalidData;
            if (dto.Target == PcSubmissionTarget.Activity)
            {
                if (!dto.ActivityId.HasValue) return "فعالیت را مشخص کنید.";
                PcActivity a = await _uow.Set<PcActivity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ActivityId.Value && !x.Deleted, ct);
                if (a == null || a.ContractId != dto.ContractId) return "فعالیت پیدا نشد یا متعلق به این پروژه نیست.";
                if (a.ProgressMethod != PcProgressMethod.Manual && a.ProgressMethod != PcProgressMethod.Contractor && a.ProgressMethod != PcProgressMethod.Quantity)
                    return "پیشرفت این فعالیت از رویدادهای سیستم یا از وظایف محاسبه می‌شود؛ پیشرفت وظایف آن را گزارش کنید.";
                bool mayReport = _user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")
                    || await _uow.Set<TmTaskAssignee>().AnyAsync(t => t.UserId == Me && t.State == TmAssigneeState.Active && t.Task.ActivityId == a.Id && !t.Task.Deleted, ct);
                if (!mayReport) return OfficeText.NotAllowed + " (گزارش پیشرفت فعالیت با مسئول آن است)";
                found(a.PercentCache, a.Code + " " + a.Title);
                return null;
            }
            if (!dto.TaskId.HasValue) return "وظیفه را مشخص کنید.";
            TmTask t = await _tasks.FindVisibleAsync(dto.TaskId.Value, false, ct);
            if (t == null || t.Deleted) return "وظیفه پیدا نشد.";
            if (t.ContractId != dto.ContractId) return "وظیفه به این پروژه وصل نیست.";
            if (t.Status != TmTaskStatus.Open && t.Status != TmTaskStatus.Assigned && t.Status != TmTaskStatus.InProgress) return "در وضعیت «" + TaskRules.StatusText(t.Status) + "» پیشرفت وظیفه گزارش نمی‌شود.";
            List<int> pos = await PositionsAsync(ct);
            bool mine = await _uow.Set<TmTaskAssignee>().AnyAsync(x => x.TaskId == t.Id && x.State == TmAssigneeState.Active && x.Role != TmAssigneeRole.Observer && (x.UserId == Me || (x.PositionId != null && pos.Contains(x.PositionId.Value))), ct);
            if (!mine) return OfficeText.NotAllowed + " (گزارش پیشرفت وظیفه با گیرنده آن است)";
            found(t.PercentComplete, t.Number + " " + t.Title);
            return null;
        }

        public async Task<ResponseDto<PcSubmissionDto>> SaveSubmissionAsync(PcSubmissionSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcSubmissionDto>(OfficeText.InvalidData);
            if (dto.Percent < 0m || dto.Percent > 100m) return Bad<PcSubmissionDto>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
            if (dto.ActualCost < 0m) return Bad<PcSubmissionDto>("هزینه منفی نیست.");
            if (dto.ActualCost > 0m)
            {
                if (dto.Target != PcSubmissionTarget.Activity) return Bad<PcSubmissionDto>("هزینه به فعالیت گزارش می‌شود.");
                if (!Enum.IsDefined(typeof(CostKind), dto.CostKind)) return Bad<PcSubmissionDto>("نوع هزینه را مشخص کنید.");
                if (Clean(dto.CostNote) == null) return Bad<PcSubmissionDto>("برای هزینه، توضیح تعهدی (بابت چه کاری) را بنویسید.");
            }
            if (dto.ActualStart.HasValue && dto.ActualStart.Value.Date > DateTime.Today) return Bad<PcSubmissionDto>("تاریخ شروع واقعی در آینده است.");
            if (dto.ActualFinish.HasValue && (dto.ActualFinish.Value.Date > DateTime.Today || (dto.ActualStart.HasValue && dto.ActualFinish.Value.Date < dto.ActualStart.Value.Date))) return Bad<PcSubmissionDto>("تاریخ پایان واقعی نامعتبر است.");
            if (dto.Percent >= 100m && !dto.ActualFinish.HasValue) dto.ActualFinish = DateTime.Today;
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, ct)) return OfficeText.NotFound<PcSubmissionDto>(PcText.ContractNotFound);
            decimal previous = 0m;
            string problem = await LoadTargetAsync(dto, (p, _) => previous = p, ct);
            if (problem != null) return Bad<PcSubmissionDto>(problem);
            List<Guid> files = (dto.AttachmentDocumentIds ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
            if (files.Count > 20) return Bad<PcSubmissionDto>("حداکثر ۲۰ فایل پیوست می‌شود.");
            if (files.Count > 0)
            {
                // only files the submitter uploaded himself
                int own = await _uow.Set<Document>().CountAsync(d => files.Contains(d.Id) && d.UploadedByUserId == Me, ct);
                if (own != files.Count) return Bad<PcSubmissionDto>("فقط فایل‌هایی که خودتان بارگذاری کرده‌اید پیوست می‌شود.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            PcProgressSubmission s;
            if (dto.Id.HasValue)
            {
                s = await _uow.Set<PcProgressSubmission>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (s == null) return OfficeText.NotFound<PcSubmissionDto>(NotFoundText);
                if (s.CreatorUserId != Me) return Bad<PcSubmissionDto>(OfficeText.NotAllowed + " (گزارش را فقط ارسال‌کننده ویرایش می‌کند)");
                if (!PcSubmissionRules.CanEdit(s.State)) return Bad<PcSubmissionDto>(s.State == PcSubmissionState.InApproval ? "گزارش در جریان تایید قفل است؛ تا تصمیم تاییدکننده تغییر نمی‌کند." : "گزارش تاییدشده تغییر نمی‌کند.");
                if (s.Target != dto.Target || s.ActivityId != dto.ActivityId || s.TaskId != dto.TaskId || s.ContractId != dto.ContractId) return Bad<PcSubmissionDto>("هدف گزارش بعد از ثبت تغییر نمی‌کند.");
            }
            else
            {
                bool open = dto.Target == PcSubmissionTarget.Activity
                    ? await _uow.Set<PcProgressSubmission>().AnyAsync(x => !x.Deleted && x.ActivityId == dto.ActivityId && (x.State == PcSubmissionState.Draft || x.State == PcSubmissionState.InApproval || x.State == PcSubmissionState.Returned), ct)
                    : await _uow.Set<PcProgressSubmission>().AnyAsync(x => !x.Deleted && x.TaskId == dto.TaskId && (x.State == PcSubmissionState.Draft || x.State == PcSubmissionState.InApproval || x.State == PcSubmissionState.Returned), ct);
                if (open) return Bad<PcSubmissionDto>("برای این هدف گزارش بازی هست (پیش‌نویس، در جریان تایید یا برگشتی)؛ همان را ادامه دهید.");
                s = new PcProgressSubmission { CreateDate = now, CreatorUserId = Me, ContractId = dto.ContractId, Target = dto.Target, ActivityId = dto.ActivityId, TaskId = dto.TaskId, State = PcSubmissionState.Draft, Steps = new List<PcProgressApproval>() };
                s.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.ProgressSubmission, "PG", DateTime.Today, ct);
                _uow.Set<PcProgressSubmission>().Add(s);
            }
            s.Percent = PcText.Round2(dto.Percent); s.PreviousPercent = previous; s.ActualStart = dto.ActualStart?.Date; s.ActualFinish = dto.ActualFinish?.Date;
            s.ActualCost = PcText.Round2(dto.ActualCost); s.CostKind = dto.ActualCost > 0m ? dto.CostKind : 0; s.CostNote = OfficeText.Cap(Clean(dto.CostNote), 500); s.Comment = OfficeText.Cap(Clean(dto.Comment), 2000);
            s.AttachmentDocumentIds = files.Count == 0 ? null : string.Join(",", files);
            s.ModifyDate = now; s.ModifierUserId = Me;
            List<PcProgressChainLevel> chain = null;
            if (dto.Submit)
            {
                chain = await EffectiveChainAsync(s.ContractId, ct);
                s.Round++;
                s.State = PcSubmissionState.InApproval; s.CurrentLevel = chain[0].Level; s.SubmittedAt = now; s.SubmittedByUserId = Me; s.ResultNote = null;
                s.Steps ??= new List<PcProgressApproval>();
                foreach (PcProgressChainLevel l in chain) s.Steps.Add(new PcProgressApproval { Round = s.Round, Level = l.Level, Title = l.Title, Role = l.Role, PositionId = l.PositionId, State = PcApprovalStepState.Pending });
            }
            AddHistory(s, dto.Submit ? "submitted" : "draft-saved", 0, null, null);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcSubmissionDto>(OfficeText.Concurrency); }
            if (dto.Submit) await NotifyLevelAsync(s, chain[0].Role, chain[0].PositionId, ct);
            return await ReloadAsync(s.Id, ct);
        }

        private void AddHistory(PcProgressSubmission s, string action, int level, string note, string changesJson)
        {
            _uow.Set<PcProgressSubmissionHistory>().Add(new PcProgressSubmissionHistory { Submission = s, At = DateTimeOffset.Now, UserId = Me, Action = action, Round = s.Round, Level = level, Note = OfficeText.Cap(note, 1000), ChangesJson = changesJson });
        }

        private async Task NotifyLevelAsync(PcProgressSubmission s, string role, int? positionId, CancellationToken ct)
        {
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "pgs:" + s.Id.ToString(CultureInfo.InvariantCulture), Module = "progress", SourceType = "progress-submission", SourceId = s.Id, Title = "گزارش پیشرفت منتظر تایید شما: " + s.Number,
                Link = "/project-control/progress-approval?open=" + s.Id.ToString(CultureInfo.InvariantCulture), Kind = OfInboxKind.Approval, Priority = OfUrgency.Normal,
                Roles = role, PositionIds = positionId.HasValue ? new List<int> { positionId.Value } : new List<int>(), ReplaceRecipients = true,
            }, ct);
        }

        // ============================================================================ decide

        public async Task<ResponseDto<PcSubmissionDto>> DecideAsync(PcSubmissionDecisionDto dto, CancellationToken ct)
        {
            try { return await DecideCoreAsync(dto, ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcSubmissionDto>(OfficeText.Concurrency); }
        }

        private async Task<ResponseDto<PcSubmissionDto>> DecideCoreAsync(PcSubmissionDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcSubmissionDto>(OfficeText.InvalidData);
            PcProgressSubmission s = await _uow.Set<PcProgressSubmission>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (s == null) return OfficeText.NotFound<PcSubmissionDto>(NotFoundText);
            if (s.State != PcSubmissionState.InApproval) return Bad<PcSubmissionDto>("این گزارش در جریان تایید نیست.");
            PcProgressApproval step = s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel && x.State == PcApprovalStepState.Pending);
            if (step == null) return Bad<PcSubmissionDto>("سطح تایید جاری پیدا نشد.");
            if (s.SubmittedByUserId == Me || s.CreatorUserId == Me) return Bad<PcSubmissionDto>(PcText.SameUser + " (ارسال‌کننده گزارش را تایید نمی‌کند)");
            if (!await MatchesAsync(step.Role, step.PositionId, ct)) return Bad<PcSubmissionDto>(OfficeText.NotAllowed + " (نوبت تایید سطح «" + step.Title + "» است)");
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcSubmissionDto>("دلیل برگشت را بنویسید.");
            string note = OfficeText.Cap(Clean(dto.Note), 1000);
            DateTimeOffset now = DateTimeOffset.Now;

            if (!dto.Approve)
            {
                step.State = PcApprovalStepState.Returned; step.DecidedByUserId = Me; step.DecidedAt = now; step.Note = note;
                s.State = PcSubmissionState.Returned; s.ResultNote = note; s.CurrentLevel = 0;
                AddHistory(s, "returned", step.Level, note, null);
                await SaveAsync(ct);
                await _notify.CloseAsync("pgs:" + s.Id.ToString(CultureInfo.InvariantCulture), "برگشت داده شد", ct);
                await NotifyUserAsync(s, "گزارش پیشرفت " + s.Number + " برگشت داده شد: " + note, ct);
                return await ReloadAsync(s.Id, ct);
            }

            // the approver may correct the values before approving; every change is recorded
            var changes = new List<object>();
            if (dto.Percent.HasValue && dto.Percent.Value != s.Percent)
            {
                if (dto.Percent.Value < 0m || dto.Percent.Value > 100m) return Bad<PcSubmissionDto>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
                changes.Add(new { field = "percent", from = s.Percent, to = PcText.Round2(dto.Percent.Value) }); s.Percent = PcText.Round2(dto.Percent.Value);
            }
            if (dto.ActualStart.HasValue && dto.ActualStart.Value.Date != s.ActualStart)
            {
                if (dto.ActualStart.Value.Date > DateTime.Today) return Bad<PcSubmissionDto>("تاریخ شروع واقعی در آینده است.");
                changes.Add(new { field = "actualStart", from = s.ActualStart, to = dto.ActualStart.Value.Date }); s.ActualStart = dto.ActualStart.Value.Date;
            }
            if (dto.ActualFinish.HasValue && dto.ActualFinish.Value.Date != s.ActualFinish)
            {
                if (dto.ActualFinish.Value.Date > DateTime.Today || (s.ActualStart.HasValue && dto.ActualFinish.Value.Date < s.ActualStart.Value)) return Bad<PcSubmissionDto>("تاریخ پایان واقعی نامعتبر است.");
                changes.Add(new { field = "actualFinish", from = s.ActualFinish, to = dto.ActualFinish.Value.Date }); s.ActualFinish = dto.ActualFinish.Value.Date;
            }
            if (dto.ActualCost.HasValue && dto.ActualCost.Value != s.ActualCost)
            {
                if (dto.ActualCost.Value < 0m || (dto.ActualCost.Value > 0m && s.Target != PcSubmissionTarget.Activity)) return Bad<PcSubmissionDto>("هزینه نامعتبر است.");
                changes.Add(new { field = "actualCost", from = s.ActualCost, to = PcText.Round2(dto.ActualCost.Value) }); s.ActualCost = PcText.Round2(dto.ActualCost.Value);
                if (s.ActualCost > 0m && s.CostKind == 0) return Bad<PcSubmissionDto>("نوع هزینه در گزارش مشخص نیست؛ گزارش را برای اصلاح برگردانید.");
            }
            if (Clean(dto.CostNote) != null && Clean(dto.CostNote) != s.CostNote) { changes.Add(new { field = "costNote", from = s.CostNote, to = Clean(dto.CostNote) }); s.CostNote = OfficeText.Cap(Clean(dto.CostNote), 500); }
            if (s.ActualCost > 0m && Clean(s.CostNote) == null) return Bad<PcSubmissionDto>("برای هزینه، توضیح تعهدی لازم است.");
            if (s.Percent >= 100m && !s.ActualFinish.HasValue) s.ActualFinish = DateTime.Today;
            string changesJson = changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
            if (changes.Count > 0) AddHistory(s, "edited", step.Level, "اصلاح مقادیر توسط تاییدکننده", changesJson);

            step.State = PcApprovalStepState.Approved; step.DecidedByUserId = Me; step.DecidedAt = now; step.Note = note;
            AddHistory(s, "approved", step.Level, note, null);
            PcProgressApproval next = s.Steps.Where(x => x.Round == s.Round && x.Level > step.Level && x.State == PcApprovalStepState.Pending).OrderBy(x => x.Level).FirstOrDefault();
            if (next != null)
            {
                s.CurrentLevel = next.Level; s.ModifyDate = now; s.ModifierUserId = Me;
                await SaveAsync(ct);
                await NotifyLevelAsync(s, next.Role, next.PositionId, ct);
                return await ReloadAsync(s.Id, ct);
            }

            // the last level: the value is written to the plan. Only the PMO or planning writes (the same people who approve a manual proposal), so the chain is not a way around them.
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcSubmissionDto>(OfficeText.NotAllowed + " (مقدار نهایی را PMO یا برنامه‌ریزی در برنامه می‌نویسد)");
            if (s.Target == PcSubmissionTarget.Activity)
            {
                ResponseDto<PcAppliedProgress> applied = await _progress.ApplyApprovedProgressAsync(new PcApprovedProgress
                {
                    ActivityId = s.ActivityId.Value, Percent = s.Percent, ActualStart = s.ActualStart, ActualFinish = s.ActualFinish, Comment = s.Comment, SubmittedByUserId = s.SubmittedByUserId ?? s.CreatorUserId,
                    ApprovedByUserId = Me, Cost = s.ActualCost, CostKind = s.CostKind, CostNote = s.CostNote,
                }, ct);
                if (!applied.Success) return Bad<PcSubmissionDto>(applied.Message ?? OfficeText.InvalidData);
                s.State = PcSubmissionState.Approved; s.CurrentLevel = 0; s.AppliedAt = now; s.ResultNote = note;
                AddHistory(s, "applied", step.Level, "مقدار در برنامه نوشته شد", null);
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return Bad<PcSubmissionDto>(OfficeText.Concurrency); }
                s.ManualProgressId = applied.Data.Manual.Id; s.CostEntryId = applied.Data.Cost?.Id;
                await _progress.ApplyAsync(s.ContractId, ct);
                await _uow.SaveChangesAsync(ct);
            }
            else
            {
                ResponseDto<bool> applied = await _tasks.ApplyApprovedProgressAsync(s.TaskId.Value, s.Percent, s.ActualStart, s.ActualFinish, s.Comment, Me, ct);
                if (!applied.Success) return Bad<PcSubmissionDto>(applied.Message ?? OfficeText.InvalidData);
                s.State = PcSubmissionState.Approved; s.CurrentLevel = 0; s.AppliedAt = now; s.ResultNote = note;
                AddHistory(s, "applied", step.Level, "مقدار در وظیفه نوشته شد", null);
                await SaveAsync(ct);
            }
            await _notify.CloseAsync("pgs:" + s.Id.ToString(CultureInfo.InvariantCulture), "تایید نهایی شد", ct);
            await NotifyUserAsync(s, "گزارش پیشرفت " + s.Number + " تایید و در برنامه ثبت شد.", ct);
            return await ReloadAsync(s.Id, ct);
        }

        private Task SaveAsync(CancellationToken ct) => _uow.SaveChangesAsync(ct);

        private Task NotifyUserAsync(PcProgressSubmission s, string title, CancellationToken ct) => _notify.NotifyAsync(new NotifyRequest
        {
            SourceKey = "pgs-result:" + s.Id.ToString(CultureInfo.InvariantCulture) + ":" + s.Round.ToString(CultureInfo.InvariantCulture), Module = "progress", SourceType = "progress-submission", SourceId = s.Id, Title = title,
            Link = "/project-control/progress-approval?open=" + s.Id.ToString(CultureInfo.InvariantCulture), Kind = OfInboxKind.Alert, Priority = OfUrgency.Normal, UserIds = new List<int> { s.SubmittedByUserId ?? s.CreatorUserId },
        }, ct);

        // ============================================================================ read

        private async Task<ResponseDto<PcSubmissionDto>> ReloadAsync(int id, CancellationToken ct)
        {
            PcProgressSubmission s = await _uow.Set<PcProgressSubmission>().AsNoTracking().Include(x => x.Steps).FirstAsync(x => x.Id == id, ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(s, ct));
        }

        private async Task<bool> CanSeeAsync(PcProgressSubmission s, CancellationToken ct)
        {
            if (s.CreatorUserId == Me || s.SubmittedByUserId == Me || _user.IsInRole(Managers)) return true;
            foreach (PcProgressApproval step in s.Steps ?? new List<PcProgressApproval>())
                if (await MatchesAsync(step.Role, step.PositionId, ct)) return true;
            return false;
        }

        public async Task<ResponseDto<PcSubmissionDto>> GetSubmissionAsync(int id, CancellationToken ct)
        {
            PcProgressSubmission s = await _uow.Set<PcProgressSubmission>().AsNoTracking().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (s == null || !await CanSeeAsync(s, ct)) return OfficeText.NotFound<PcSubmissionDto>(NotFoundText);
            return ResponseDtoHelper.Ok(await ToDtoAsync(s, ct));
        }

        private async Task<Dictionary<int, string>> TargetTitlesAsync(IEnumerable<PcProgressSubmission> rows, CancellationToken ct)
        {
            List<PcProgressSubmission> list = rows.ToList();
            var result = new Dictionary<int, string>();
            List<int> actIds = list.Where(r => r.ActivityId.HasValue).Select(r => r.ActivityId.Value).Distinct().ToList();
            List<int> taskIds = list.Where(r => r.TaskId.HasValue).Select(r => r.TaskId.Value).Distinct().ToList();
            var acts = actIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code + " " + a.Title, ct);
            var tasks = taskIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<TmTask>().AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Confidentiality == OfClassification.Normal ? t.Number + " " + t.Title : t.Number, ct);
            foreach (PcProgressSubmission r in list)
                result[r.Id] = r.ActivityId.HasValue && acts.TryGetValue(r.ActivityId.Value, out string a) ? a : r.TaskId.HasValue && tasks.TryGetValue(r.TaskId.Value, out string t) ? t : null;
            return result;
        }

        private async Task<PcSubmissionDto> ToDtoAsync(PcProgressSubmission s, CancellationToken ct)
        {
            List<PcProgressSubmissionHistory> history = await _uow.Set<PcProgressSubmissionHistory>().AsNoTracking().Where(h => h.SubmissionId == s.Id).OrderBy(h => h.At).ThenBy(h => h.Id).ToListAsync(ct);
            var wanted = new List<int?> { s.SubmittedByUserId, s.CreatorUserId };
            wanted.AddRange(s.Steps.Select(x => x.DecidedByUserId));
            wanted.AddRange(history.Select(h => (int?)h.UserId));
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, wanted, ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, s.Steps.Select(x => x.PositionId), ct);
            string target = (await TargetTitlesAsync(new[] { s }, ct)).GetValueOrDefault(s.Id);
            string contract = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => c.Id == s.ContractId).Select(c => c.Title).FirstOrDefaultAsync(ct);
            PcProgressApproval current = s.State == PcSubmissionState.InApproval ? s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel) : null;
            bool canDecide = current != null && current.State == PcApprovalStepState.Pending && s.SubmittedByUserId != Me && s.CreatorUserId != Me && await MatchesAsync(current.Role, current.PositionId, ct);
            string activityCode = null, taskNumber = null;
            if (s.ActivityId.HasValue) activityCode = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.Id == s.ActivityId.Value).Select(a => a.Code).FirstOrDefaultAsync(ct);
            if (s.TaskId.HasValue) taskNumber = await _uow.Set<TmTask>().AsNoTracking().Where(t => t.Id == s.TaskId.Value).Select(t => t.Number).FirstOrDefaultAsync(ct);
            return new PcSubmissionDto
            {
                Id = s.Id, Number = s.Number, ContractId = s.ContractId, ContractTitle = contract, Target = s.Target, ActivityId = s.ActivityId, ActivityCode = activityCode, TaskId = s.TaskId, TaskNumber = taskNumber, TargetTitle = target,
                Percent = s.Percent, PreviousPercent = s.PreviousPercent, ActualStart = s.ActualStart, ActualStartText = OfficeText.Date(s.ActualStart), ActualFinish = s.ActualFinish, ActualFinishText = OfficeText.Date(s.ActualFinish),
                ActualCost = s.ActualCost, CostKind = s.CostKind, CostNote = s.CostNote, Comment = s.Comment, AttachmentDocumentIds = ParseGuids(s.AttachmentDocumentIds), State = s.State, Round = s.Round, CurrentLevel = s.CurrentLevel,
                CurrentLevelTitle = current?.Title, SubmittedByName = names.Name(s.SubmittedByUserId ?? s.CreatorUserId), SubmittedAtText = OfficeText.Stamp(s.SubmittedAt), ResultNote = s.ResultNote, ManualProgressId = s.ManualProgressId, CostEntryId = s.CostEntryId,
                Locked = !PcSubmissionRules.CanEdit(s.State), CanEdit = PcSubmissionRules.CanEdit(s.State) && s.CreatorUserId == Me, CanDecide = canDecide,
                Steps = s.Steps.OrderBy(x => x.Round).ThenBy(x => x.Level).Select(x => new PcApprovalStepDto
                {
                    Round = x.Round, Level = x.Level, Title = x.Title, Role = x.Role, PositionTitle = positions.Title(x.PositionId), State = x.State, DecidedByName = names.Name(x.DecidedByUserId), DecidedAtText = OfficeText.Stamp(x.DecidedAt), Note = x.Note,
                }).ToList(),
                History = history.Select(h => new PcSubmissionHistoryDto { Id = h.Id, Action = h.Action, ActionText = PcSubmissionRules.ActionText(h.Action), Round = h.Round, Level = h.Level, UserName = names.Name(h.UserId), AtText = OfficeText.Stamp(h.At), Note = h.Note, ChangesJson = h.ChangesJson }).ToList(),
            };
        }

        private static List<Guid> ParseGuids(string csv) =>
            string.IsNullOrWhiteSpace(csv) ? new List<Guid>() : csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Guid.TryParse(x, out Guid g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();

        public async Task<ResponseDto<PagedList<PcSubmissionListDto>>> GetSubmissionsAsync(PcSubmissionQuery query, CancellationToken ct)
        {
            query ??= new PcSubmissionQuery();
            string cartable = (query.Cartable ?? "mine").Trim().ToLowerInvariant();
            IQueryable<PcProgressSubmission> q = _uow.Set<PcProgressSubmission>().AsNoTracking().Include(s => s.Steps).Where(s => !s.Deleted);
            if (query.ContractId.HasValue) q = q.Where(s => s.ContractId == query.ContractId.Value);
            if (query.State.HasValue) q = q.Where(s => s.State == query.State.Value);
            int me = Me;
            List<PcProgressSubmission> rows;
            if (cartable == "all")
            {
                if (!_user.IsInRole(Managers)) return Bad<PagedList<PcSubmissionListDto>>(OfficeText.NotAllowed);
                rows = null;
            }
            else if (cartable == "waiting")
            {
                // the reports whose current level is mine (a role I hold or a position I hold or act for), and not my own
                List<PcProgressSubmission> pending = await q.Where(s => s.State == PcSubmissionState.InApproval && s.CreatorUserId != me && s.SubmittedByUserId != me).ToListAsync(ct);
                rows = new List<PcProgressSubmission>();
                foreach (PcProgressSubmission s in pending)
                {
                    PcProgressApproval cur = s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel && x.State == PcApprovalStepState.Pending);
                    if (cur != null && await MatchesAsync(cur.Role, cur.PositionId, ct)) rows.Add(s);
                }
            }
            else
            {
                q = q.Where(s => s.CreatorUserId == me);
                rows = null;
            }
            int total, pageNumber = Math.Max(1, query.PageNumber), size = query.PageSize <= 0 ? 25 : query.PageSize;
            List<PcProgressSubmission> page;
            if (rows == null)
            {
                total = await q.CountAsync(ct);
                page = await q.OrderByDescending(s => s.Id).Skip((pageNumber - 1) * size).Take(size).ToListAsync(ct);
            }
            else
            {
                total = rows.Count;
                page = rows.OrderByDescending(s => s.Id).Skip((pageNumber - 1) * size).Take(size).ToList();
            }
            Dictionary<int, string> titles = await TargetTitlesAsync(page, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, page.Select(s => s.SubmittedByUserId ?? s.CreatorUserId).Select(i => (int?)i), ct);
            List<int> contractIds = page.Select(s => s.ContractId).Distinct().ToList();
            Dictionary<int, string> contracts = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
            DateTime today = DateTime.Today;
            var list = new List<PcSubmissionListDto>();
            foreach (PcProgressSubmission s in page)
            {
                PcProgressApproval cur = s.State == PcSubmissionState.InApproval ? s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel) : null;
                DateTimeOffset since = s.Steps.Where(x => x.Round == s.Round && x.DecidedAt.HasValue).Select(x => x.DecidedAt.Value).DefaultIfEmpty(s.SubmittedAt ?? s.CreateDate).Max();
                list.Add(new PcSubmissionListDto
                {
                    Id = s.Id, Number = s.Number, ContractId = s.ContractId, ContractTitle = contracts.GetValueOrDefault(s.ContractId), Target = s.Target, TargetTitle = titles.GetValueOrDefault(s.Id), Percent = s.Percent, PreviousPercent = s.PreviousPercent,
                    ActualCost = s.ActualCost, State = s.State, Round = s.Round, CurrentLevel = s.CurrentLevel, CurrentLevelTitle = cur?.Title, SubmittedByName = names.Name(s.SubmittedByUserId ?? s.CreatorUserId), SubmittedAtText = OfficeText.Stamp(s.SubmittedAt),
                    WaitingDays = s.State == PcSubmissionState.InApproval ? Math.Max(0, (int)(today - since.LocalDateTime.Date).TotalDays) : 0, CanDecide = cartable == "waiting",
                });
            }
            return ResponseDtoHelper.Ok((PagedList<PcSubmissionListDto>)new PcPage<PcSubmissionListDto>(list, total, pageNumber, size));
        }

        // ============================================================================ reports

        public async Task<ResponseDto<List<PcNotUpdatedDto>>> GetNotUpdatedAsync(PcNotUpdatedQuery query, CancellationToken ct)
        {
            query ??= new PcNotUpdatedQuery();
            if (!_user.IsInRole(Managers)) return Bad<List<PcNotUpdatedDto>>(OfficeText.NotAllowed);
            int days = query.Days ?? (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.ProgressStaleDays];
            DateTime today = DateTime.Today;
            var result = new List<PcNotUpdatedDto>();
            // tasks of a project that are open and assigned: the last progress entry in their history (or the day they were assigned)
            IQueryable<TmTask> tq = _uow.Set<TmTask>().AsNoTracking().Where(t => !t.Deleted && t.ContractId != null && t.Kind != TmTaskKind.Personal && t.Confidentiality == OfClassification.Normal
                && (t.Status == TmTaskStatus.Assigned || t.Status == TmTaskStatus.InProgress) && t.Assignees.Any(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer));
            if (query.ContractId.HasValue) tq = tq.Where(t => t.ContractId == query.ContractId.Value);
            List<TmTask> tasks = await tq.Include(t => t.Assignees).OrderBy(t => t.Id).Take(2000).ToListAsync(ct);
            List<int> taskIds = tasks.Select(t => t.Id).ToList();
            var lastProgress = await _uow.Set<TmTaskHistory>().AsNoTracking().Where(h => taskIds.Contains(h.TaskId) && h.Kind == TmHistoryKind.Progress).GroupBy(h => h.TaskId).Select(g => new { TaskId = g.Key, At = g.Max(h => h.At) }).ToListAsync(ct);
            Dictionary<int, string> userNames = await InventoryUsers.GetNamesAsync(_uow, tasks.SelectMany(t => t.Assignees).Select(a => a.UserId), ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, tasks.SelectMany(t => t.Assignees).Select(a => a.PositionId), ct);
            Dictionary<int, string> contracts = await _uow.Set<ContractEntity>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Title, ct);
            foreach (TmTask t in tasks)
            {
                DateTime last = (lastProgress.FirstOrDefault(x => x.TaskId == t.Id)?.At ?? t.Assignees.Where(a => a.Role != TmAssigneeRole.Observer).Select(a => a.AssignedAt).DefaultIfEmpty(t.CreateDate).Min()).LocalDateTime.Date;
                int since = (int)(today - last).TotalDays;
                if (since < days) continue;
                result.Add(new PcNotUpdatedDto
                {
                    Target = PcSubmissionTarget.Task, ContractId = t.ContractId.Value, ContractTitle = contracts.GetValueOrDefault(t.ContractId.Value), TargetId = t.Id, Code = t.Number, Title = t.Title,
                    AssigneeNames = string.Join("، ", t.Assignees.Where(a => a.State == TmAssigneeState.Active && a.Role != TmAssigneeRole.Observer).Select(a => a.UserId.HasValue ? userNames.Name(a.UserId) : positions.Title(a.PositionId)).Where(n => n != null).Distinct()),
                    Percent = t.PercentComplete, LastUpdateText = OfficeText.Date(last), DaysSince = since,
                });
            }
            // activities reported by a person that should be running by the plan
            IQueryable<PcActivity> aq = _uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.Status != PcActivityStatus.Done && a.Status != PcActivityStatus.OnHold && a.DurationDays > 0
                && (a.ProgressMethod == PcProgressMethod.Manual || a.ProgressMethod == PcProgressMethod.Contractor || a.ProgressMethod == PcProgressMethod.Quantity) && (a.ForecastStart ?? a.EarlyStart) != null && (a.ForecastStart ?? a.EarlyStart) <= today);
            if (query.ContractId.HasValue) aq = aq.Where(a => a.ContractId == query.ContractId.Value);
            List<PcActivity> acts = await aq.OrderBy(a => a.Id).Take(2000).ToListAsync(ct);
            List<int> actIds = acts.Select(a => a.Id).ToList();
            var lastManual = (await _uow.Set<PcManualProgress>().AsNoTracking().Where(m => actIds.Contains(m.ActivityId) && m.State == 1).Select(m => new { m.ActivityId, m.DecidedAt }).ToListAsync(ct)).GroupBy(m => m.ActivityId).ToDictionary(g => g.Key, g => g.Max(m => m.DecidedAt));
            foreach (PcActivity a in acts)
            {
                DateTime planned = (a.ForecastStart ?? a.EarlyStart).Value.Date;
                DateTime last = lastManual.TryGetValue(a.Id, out DateTimeOffset? at) && at.HasValue ? at.Value.LocalDateTime.Date : planned;
                int since = (int)(today - last).TotalDays;
                if (since < days) continue;
                result.Add(new PcNotUpdatedDto
                {
                    Target = PcSubmissionTarget.Activity, ContractId = a.ContractId, ContractTitle = contracts.GetValueOrDefault(a.ContractId), TargetId = a.Id, Code = a.Code, Title = a.Title, AssigneeNames = a.ResponsibleUnit,
                    Percent = a.PercentCache, LastUpdateText = lastManual.ContainsKey(a.Id) ? OfficeText.Date(last) : "هنوز گزارشی ثبت نشده", DaysSince = since,
                });
            }
            return ResponseDtoHelper.Ok(result.OrderByDescending(r => r.DaysSince).ToList());
        }

        public async Task<ResponseDto<List<PcOverdueUpdateDto>>> GetOverdueUpdatesAsync(int? contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(Managers)) return Bad<List<PcOverdueUpdateDto>>(OfficeText.NotAllowed);
            int sla = (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.ProgressApprovalSlaDays];
            IQueryable<PcProgressSubmission> q = _uow.Set<PcProgressSubmission>().AsNoTracking().Include(s => s.Steps).Where(s => !s.Deleted && s.State == PcSubmissionState.InApproval);
            if (contractId.HasValue) q = q.Where(s => s.ContractId == contractId.Value);
            List<PcProgressSubmission> rows = await q.ToListAsync(ct);
            Dictionary<int, string> titles = await TargetTitlesAsync(rows, ct);
            Dictionary<int, string> contracts = await _uow.Set<ContractEntity>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Title, ct);
            DateTime today = DateTime.Today;
            var result = new List<PcOverdueUpdateDto>();
            foreach (PcProgressSubmission s in rows)
            {
                DateTimeOffset since = s.Steps.Where(x => x.Round == s.Round && x.DecidedAt.HasValue).Select(x => x.DecidedAt.Value).DefaultIfEmpty(s.SubmittedAt ?? s.CreateDate).Max();
                int waiting = (int)(today - since.LocalDateTime.Date).TotalDays;
                if (waiting <= sla) continue;
                PcProgressApproval cur = s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel);
                result.Add(new PcOverdueUpdateDto { SubmissionId = s.Id, Number = s.Number, ContractTitle = contracts.GetValueOrDefault(s.ContractId), TargetTitle = titles.GetValueOrDefault(s.Id), WaitingAt = OfficeText.Date(since), Level = s.CurrentLevel, LevelTitle = cur?.Title, WaitingDays = waiting });
            }
            return ResponseDtoHelper.Ok(result.OrderByDescending(r => r.WaitingDays).ToList());
        }

        public async Task<int> RemindWaitingAsync(CancellationToken ct)
        {
            int sla = (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.ProgressApprovalSlaDays];
            List<PcProgressSubmission> rows = await _uow.Set<PcProgressSubmission>().AsNoTracking().Include(s => s.Steps).Where(s => !s.Deleted && s.State == PcSubmissionState.InApproval).ToListAsync(ct);
            DateTime today = DateTime.Today;
            int sent = 0;
            foreach (PcProgressSubmission s in rows)
            {
                DateTimeOffset since = s.Steps.Where(x => x.Round == s.Round && x.DecidedAt.HasValue).Select(x => x.DecidedAt.Value).DefaultIfEmpty(s.SubmittedAt ?? s.CreateDate).Max();
                if ((today - since.LocalDateTime.Date).TotalDays <= sla) continue;
                PcProgressApproval cur = s.Steps.FirstOrDefault(x => x.Round == s.Round && x.Level == s.CurrentLevel);
                if (cur == null) continue;
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "pgs-late:" + s.Id.ToString(CultureInfo.InvariantCulture) + ":" + s.Round.ToString(CultureInfo.InvariantCulture) + ":" + cur.Level.ToString(CultureInfo.InvariantCulture), Module = "progress", SourceType = "progress-submission", SourceId = s.Id,
                    Title = "گزارش پیشرفت " + s.Number + " بیش از حد مجاز منتظر تصمیم شماست", Link = "/project-control/progress-approval?open=" + s.Id.ToString(CultureInfo.InvariantCulture), Kind = OfInboxKind.Approval, Priority = OfUrgency.Urgent,
                    Roles = cur.Role, PositionIds = cur.PositionId.HasValue ? new List<int> { cur.PositionId.Value } : new List<int>(),
                }, ct);
                sent++;
            }
            return sent;
        }
    }

    public static class PcSubmissionRules
    {
        /// <summary>A report is editable by its submitter while it is a draft or after it was returned; it is locked in approval and after approval.</summary>
        public static bool CanEdit(PcSubmissionState state) => state == PcSubmissionState.Draft || state == PcSubmissionState.Returned;

        public static string ActionText(string action) => action switch
        {
            "draft-saved" => "ذخیره پیش‌نویس", "submitted" => "ارسال برای تایید", "edited" => "اصلاح توسط تاییدکننده", "approved" => "تایید سطح", "returned" => "برگشت با دلیل", "applied" => "ثبت در برنامه", _ => action,
        };
    }
}
