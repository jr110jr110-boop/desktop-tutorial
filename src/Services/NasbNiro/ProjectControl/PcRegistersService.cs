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
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
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
    public interface IPcRegistersService
    {
        Task<ResponseDto<PagedList<PcRiskDto>>> GetRisksAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcRiskDto>> SaveRiskAsync(PcRiskSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRiskDto>> SetRiskStatusAsync(PcRiskStatusDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRiskHeatMapDto>> GetRiskHeatMapAsync(int contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcIssueDto>>> GetIssuesAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcIssueDto>> SaveIssueAsync(PcIssueSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcIssueDto>> IssueActionAsync(PcIssueActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<PcStakeholderDto>>> GetStakeholdersAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcStakeholderDto>> SaveStakeholderAsync(PcStakeholderSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcStakeholderMatrixDto>> GetStakeholderMatrixAsync(int contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcLessonDto>>> GetLessonsAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcLessonDto>> SaveLessonAsync(PcLessonSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcLessonDto>> DecideLessonAsync(PcRegisterDecisionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcProjDeliverableDto>>> GetDeliverablesAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcProjDeliverableDto>> SaveDeliverableAsync(PcProjDeliverableSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcProjDeliverableDto>> DeliverableActionAsync(PcProjDeliverableActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcPhotoDto>>> GetPhotosAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcPhotoDto>> AddPhotoAsync(PcPhotoSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcPhotoDto>> VoidPhotoAsync(PcPhotoVoidDto dto, CancellationToken cancellationToken);

        ResponseDto<PcEvaluationTemplateDto> GetEvaluationTemplate(PcEvaluationParty kind);
        Task<ResponseDto<PagedList<PcEvaluationDto>>> GetEvaluationsAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcEvaluationDto>> SaveEvaluationAsync(PcEvaluationSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcEvaluationDto>> DecideEvaluationAsync(PcRegisterDecisionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcWeeklyReportDto>>> GetWeeklyReportsAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcWeeklyReportDto>> GenerateWeeklyAsync(PcWeeklyGenerateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcWeeklyReportDto>> SaveWeeklyAsync(PcWeeklySaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcWeeklyReportDto>> ReleaseWeeklyAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcPhaseEvaluationDto>>> GetPhaseEvaluationsAsync(PcRegisterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcPhaseEvaluationDto>> SavePhaseEvaluationAsync(PcPhaseEvaluationSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcPhaseEvaluationDto>> DecidePhaseEvaluationAsync(PcRegisterDecisionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcOnePageDto>> GetOnePageAsync(int contractId, CancellationToken cancellationToken);
    }

    /// <summary>The PMBOK registers of the project site (appendix 2) and the one-page project dashboard.</summary>
    public class PcRegistersService : IPcRegistersService
    {
        private const string Writers = ProjectControlRoles.UnitManagers + ",PMO";
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;

        public PcRegistersService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static string Cap(string s, int max) => OfficeText.Cap(Clean(s), max);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);
        private static ResponseDto<T> NotFound<T>(string m) => OfficeText.NotFound<T>(m);
        private bool CanRead => _user.IsInRole(ProjectControlRoles.Readers);
        private bool CanWrite => _user.IsInRole(Writers);
        private const string Denied = OfficeText.NotAllowed;

        private async Task<string> ContractProblemAsync(int contractId, CancellationToken ct) =>
            await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, ct) ? null : PcText.ContractNotFound;

        private async Task<(int Page, int Size)> PageOf(PcRegisterQuery q) => await Task.FromResult((Math.Max(1, q.PageNumber), q.PageSize <= 0 ? 25 : Math.Min(q.PageSize, 200)));

        private static ResponseDto<PagedList<T>> Page<T>(List<T> rows, int total, int page, int size) => ResponseDtoHelper.Ok((PagedList<T>)new PcPage<T>(rows, total, page, size));

        private async Task<int> ActivityOkAsync(int? activityId, int contractId, CancellationToken ct) =>
            !activityId.HasValue || await _uow.Set<PcActivity>().AnyAsync(a => a.Id == activityId.Value && a.ContractId == contractId && !a.Deleted, ct) ? 0 : 1;

        private async Task<bool> PositionOkAsync(int? positionId, CancellationToken ct) =>
            !positionId.HasValue || await _uow.Set<OfPosition>().AnyAsync(p => p.Id == positionId.Value && !p.Deleted && p.IsActive, ct);

        // ============================================================================ risks

        private async Task<(int High, int Critical, int ReviewDays)> RiskLimitsAsync(CancellationToken ct)
        {
            Dictionary<string, decimal> s = await PcSettingKeys.LoadAsync(_uow, ct);
            return ((int)s[PcSettingKeys.RiskHighScore], (int)s[PcSettingKeys.RiskCriticalScore], (int)s[PcSettingKeys.RiskReviewDays]);
        }

        private async Task<List<PcRiskDto>> RiskDtosAsync(List<PcRisk> rows, CancellationToken ct)
        {
            var (high, critical, _) = await RiskLimitsAsync(ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            var tasks = await _uow.Set<TmTask>().AsNoTracking().Where(t => !t.Deleted && t.RiskId != null && ids.Contains(t.RiskId.Value)).Select(t => new { RiskId = t.RiskId.Value, t.Status }).ToListAsync(ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.OwnerPositionId), ct);
            DateTime today = DateTime.Today;
            return rows.Select(r => new PcRiskDto
            {
                Id = r.Id, ContractId = r.ContractId, Number = r.Number, Title = r.Title, Cause = r.Cause, Event = r.Event, Effect = r.Effect, Category = r.Category, IsOpportunity = r.IsOpportunity, Probability = r.Probability,
                Impact = r.Impact, Score = r.Score, Level = PcRiskRules.Level(r.Score, high, critical), OwnerPositionId = r.OwnerPositionId, OwnerPositionTitle = positions.Title(r.OwnerPositionId), Response = r.Response,
                ResponsePlan = r.ResponsePlan, Status = r.Status, ReviewDate = r.ReviewDate, ReviewDateText = OfficeText.Date(r.ReviewDate), ReviewOverdue = r.ReviewDate.HasValue && r.ReviewDate.Value.Date < today && r.Status != PcRiskStatus.Closed,
                ResidualProbability = r.ResidualProbability, ResidualImpact = r.ResidualImpact, ResidualScore = r.ResidualScore, ClosureNote = r.ClosureNote,
                ActionCount = tasks.Count(t => t.RiskId == r.Id), OpenActionCount = tasks.Count(t => t.RiskId == r.Id && TaskRules.IsOpen(t.Status)),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcRiskDto>>> GetRisksAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcRiskDto>>(Denied);
            IQueryable<PcRisk> q = _uow.Set<PcRisk>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.Status.HasValue) q = q.Where(r => (int)r.Status == query.Status.Value);
            string cat = Clean(query.Category); if (cat != null) q = q.Where(r => r.Category == cat);
            string text = Clean(query.Contains); if (text != null) q = q.Where(r => r.Title.Contains(text) || r.Number.Contains(text));
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcRisk> rows = await q.OrderByDescending(r => r.Score).ThenBy(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await RiskDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcRiskDto>> SaveRiskAsync(PcRiskSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcRiskDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcRiskDto>(Denied);
            string title = Clean(dto.Title);
            if (title == null) return Bad<PcRiskDto>("عنوان ریسک را بنویسید.");
            if (!PcRiskRules.ValidScale(dto.Probability) || !PcRiskRules.ValidScale(dto.Impact)) return Bad<PcRiskDto>("احتمال و اثر ریسک عددی بین ۱ و ۵ است.");
            if ((dto.ResidualProbability.HasValue != dto.ResidualImpact.HasValue) || (dto.ResidualProbability.HasValue && (!PcRiskRules.ValidScale(dto.ResidualProbability.Value) || !PcRiskRules.ValidScale(dto.ResidualImpact.Value))))
                return Bad<PcRiskDto>("احتمال و اثر باقی‌مانده را با هم و بین ۱ و ۵ وارد کنید.");
            if (dto.Response.HasValue && !Enum.IsDefined(typeof(PcRiskResponse), dto.Response.Value)) return Bad<PcRiskDto>(OfficeText.InvalidData);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcRiskDto>(problem);
            if (!await PositionOkAsync(dto.OwnerPositionId, ct)) return Bad<PcRiskDto>("سمت مسئول پیدا نشد.");
            var (_, _, reviewDays) = await RiskLimitsAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            PcRisk r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcRisk>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null) return NotFound<PcRiskDto>("ریسک پیدا نشد.");
                if (r.Status == PcRiskStatus.Closed) return Bad<PcRiskDto>("ریسک بسته‌شده تغییر نمی‌کند.");
                if (r.ContractId != dto.ContractId) return Bad<PcRiskDto>("پروژه ریسک تغییر نمی‌کند.");
            }
            else
            {
                r = new PcRisk { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me, Status = PcRiskStatus.Identified };
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Risk, "RK", DateTime.Today, ct);
                _uow.Set<PcRisk>().Add(r);
            }
            r.Title = Cap(title, 300); r.Cause = Cap(dto.Cause, 1000); r.Event = Cap(dto.Event, 1000); r.Effect = Cap(dto.Effect, 1000); r.Category = Cap(dto.Category, 60); r.IsOpportunity = dto.IsOpportunity;
            r.Probability = dto.Probability; r.Impact = dto.Impact; r.Score = PcRiskRules.Score(dto.Probability, dto.Impact); r.OwnerPositionId = dto.OwnerPositionId;
            r.Response = dto.Response; r.ResponsePlan = Cap(dto.ResponsePlan, 2000);
            r.ReviewDate = (dto.ReviewDate ?? DateTime.Today.AddDays(Math.Max(1, reviewDays))).Date;
            r.ResidualProbability = dto.ResidualProbability; r.ResidualImpact = dto.ResidualImpact;
            r.ResidualScore = dto.ResidualProbability.HasValue ? PcRiskRules.Score(dto.ResidualProbability.Value, dto.ResidualImpact.Value) : (int?)null;
            if (r.Status == PcRiskStatus.Identified) r.Status = PcRiskStatus.Assessed;
            if (r.Response.HasValue && r.Status == PcRiskStatus.Assessed) r.Status = PcRiskStatus.Responding;
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcRiskDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await RiskDtosAsync(new List<PcRisk> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcRiskDto>> SetRiskStatusAsync(PcRiskStatusDto dto, CancellationToken ct)
        {
            if (dto == null || !Enum.IsDefined(typeof(PcRiskStatus), dto.Status)) return Bad<PcRiskDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcRiskDto>(Denied);
            PcRisk r = await _uow.Set<PcRisk>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcRiskDto>("ریسک پیدا نشد.");
            if (r.Status == PcRiskStatus.Closed) return Bad<PcRiskDto>("ریسک بسته‌شده تغییر نمی‌کند.");
            if (dto.Status == PcRiskStatus.Identified) return Bad<PcRiskDto>("به وضعیت «شناسایی‌شده» برنمی‌گردد.");
            if (Clean(dto.Note) == null && (dto.Status == PcRiskStatus.Closed || dto.Status == PcRiskStatus.Occurred)) return Bad<PcRiskDto>("توضیح (دلیل بستن یا وقوع) را بنویسید.");
            if (dto.Status == PcRiskStatus.Closed)
            {
                if (await _uow.Set<TmTask>().AnyAsync(t => !t.Deleted && t.RiskId == r.Id && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected, ct)) return Bad<PcRiskDto>("اقدام‌های (وظایف) باز این ریسک را ببندید یا رد کنید.");
                r.ClosedAt = DateTimeOffset.Now; r.ClosedByUserId = Me; r.ClosureNote = Cap(dto.Note, 1000);
            }
            r.Status = dto.Status; r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            if (dto.Status == PcRiskStatus.Occurred) r.ClosureNote = Cap(dto.Note, 1000);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await RiskDtosAsync(new List<PcRisk> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcRiskHeatMapDto>> GetRiskHeatMapAsync(int contractId, CancellationToken ct)
        {
            if (!CanRead) return Bad<PcRiskHeatMapDto>(Denied);
            var (high, critical, _) = await RiskLimitsAsync(ct);
            var open = await _uow.Set<PcRisk>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == contractId && r.Status != PcRiskStatus.Closed).Select(r => new { r.Id, r.Probability, r.Impact, r.IsOpportunity }).ToListAsync(ct);
            var map = new PcRiskHeatMapDto { ContractId = contractId, HighScore = high, CriticalScore = critical, OpenThreats = open.Count(r => !r.IsOpportunity), OpenOpportunities = open.Count(r => r.IsOpportunity) };
            for (int p = 1; p <= 5; p++)
                for (int i = 1; i <= 5; i++)
                {
                    List<int> ids = open.Where(r => !r.IsOpportunity && r.Probability == p && r.Impact == i).Select(r => r.Id).ToList();
                    map.Cells.Add(new PcHeatCellDto { Probability = p, Impact = i, Count = ids.Count, RiskIds = ids });
                }
            return ResponseDtoHelper.Ok(map);
        }

        // ============================================================================ issues

        private async Task<List<PcIssueDto>> IssueDtosAsync(List<PcIssue> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.Id).ToList();
            var actions = await _uow.Set<TmTask>().AsNoTracking().Where(t => !t.Deleted && t.IssueId != null && ids.Contains(t.IssueId.Value)).GroupBy(t => t.IssueId.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new[] { r.ResolvedByUserId, r.ClosedByUserId }), ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.OwnerPositionId), ct);
            List<int> actIds = rows.Where(r => r.ActivityId.HasValue).Select(r => r.ActivityId.Value).Distinct().ToList();
            Dictionary<int, string> codes = actIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            DateTime today = DateTime.Today;
            return rows.Select(r => new PcIssueDto
            {
                Id = r.Id, ContractId = r.ContractId, Number = r.Number, Title = r.Title, Description = r.Description, Category = r.Category, Priority = r.Priority, OwnerPositionId = r.OwnerPositionId, OwnerPositionTitle = positions.Title(r.OwnerPositionId),
                RaisedDate = r.RaisedDate, RaisedDateText = OfficeText.Date(r.RaisedDate), DueDate = r.DueDate, DueDateText = OfficeText.Date(r.DueDate), Overdue = r.DueDate.HasValue && r.DueDate.Value.Date < today && r.Status != PcIssueStatus.Closed && r.Status != PcIssueStatus.Resolved,
                Status = r.Status, ActivityId = r.ActivityId, ActivityCode = r.ActivityId.HasValue && codes.TryGetValue(r.ActivityId.Value, out string c) ? c : null, DelayReasonCode = r.DelayReasonCode, Resolution = r.Resolution,
                ResolvedByName = names.Name(r.ResolvedByUserId), ResolvedAtText = OfficeText.Stamp(r.ResolvedAt), ClosedByName = names.Name(r.ClosedByUserId), ClosedAtText = OfficeText.Stamp(r.ClosedAt),
                ActionCount = actions.FirstOrDefault(a => a.Id == r.Id)?.N ?? 0,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcIssueDto>>> GetIssuesAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcIssueDto>>(Denied);
            IQueryable<PcIssue> q = _uow.Set<PcIssue>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.Status.HasValue) q = q.Where(r => (int)r.Status == query.Status.Value);
            string cat = Clean(query.Category); if (cat != null) q = q.Where(r => r.Category == cat);
            string text = Clean(query.Contains); if (text != null) q = q.Where(r => r.Title.Contains(text) || r.Number.Contains(text));
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcIssue> rows = await q.OrderBy(r => r.Status == PcIssueStatus.Closed).ThenByDescending(r => r.Priority).ThenBy(r => r.DueDate).ThenBy(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await IssueDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcIssueDto>> SaveIssueAsync(PcIssueSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcIssueDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcIssueDto>(Denied);
            string title = Clean(dto.Title);
            if (title == null) return Bad<PcIssueDto>("عنوان مشکل را بنویسید.");
            if (!Enum.IsDefined(typeof(TmPriority), dto.Priority)) return Bad<PcIssueDto>(OfficeText.InvalidData);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcIssueDto>(problem);
            if (!await PositionOkAsync(dto.OwnerPositionId, ct)) return Bad<PcIssueDto>("سمت مسئول پیدا نشد.");
            if (await ActivityOkAsync(dto.ActivityId, dto.ContractId, ct) != 0) return Bad<PcIssueDto>("فعالیت متعلق به این پروژه نیست.");
            DateTime raised = (dto.RaisedDate ?? DateTime.Today).Date;
            if (raised > DateTime.Today) return Bad<PcIssueDto>("تاریخ ثبت مشکل در آینده است.");
            if (dto.DueDate.HasValue && dto.DueDate.Value.Date < raised) return Bad<PcIssueDto>("مهلت پیش از تاریخ ثبت است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcIssue r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcIssue>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null) return NotFound<PcIssueDto>("مشکل پیدا نشد.");
                if (r.Status == PcIssueStatus.Closed) return Bad<PcIssueDto>("مشکل بسته‌شده تغییر نمی‌کند.");
                if (r.ContractId != dto.ContractId) return Bad<PcIssueDto>("پروژه مشکل تغییر نمی‌کند.");
            }
            else
            {
                r = new PcIssue { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me, Status = PcIssueStatus.Open };
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Issue, "IS", DateTime.Today, ct);
                _uow.Set<PcIssue>().Add(r);
            }
            r.Title = Cap(title, 300); r.Description = Cap(dto.Description, 2000); r.Category = Cap(dto.Category, 60); r.Priority = dto.Priority; r.OwnerPositionId = dto.OwnerPositionId;
            r.RaisedDate = raised; r.DueDate = dto.DueDate?.Date; r.ActivityId = dto.ActivityId; r.DelayReasonCode = Cap(dto.DelayReasonCode, 40);
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcIssueDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await IssueDtosAsync(new List<PcIssue> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcIssueDto>> IssueActionAsync(PcIssueActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<PcIssueDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcIssueDto>(Denied);
            PcIssue r = await _uow.Set<PcIssue>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcIssueDto>("مشکل پیدا نشد.");
            string note = Cap(dto.Note, 2000);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "start":
                    if (r.Status != PcIssueStatus.Open) return Bad<PcIssueDto>("فقط مشکل باز شروع می‌شود.");
                    r.Status = PcIssueStatus.InProgress;
                    break;
                case "resolve":
                    if (r.Status != PcIssueStatus.Open && r.Status != PcIssueStatus.InProgress) return Bad<PcIssueDto>("این مشکل قابل رفع‌شدن نیست.");
                    if (note == null) return Bad<PcIssueDto>("شرح راه‌حل را بنویسید.");
                    r.Status = PcIssueStatus.Resolved; r.Resolution = note; r.ResolvedAt = now; r.ResolvedByUserId = Me;
                    break;
                case "close":
                    if (r.Status != PcIssueStatus.Resolved) return Bad<PcIssueDto>("فقط مشکل رفع‌شده بسته می‌شود.");
                    if (r.ResolvedByUserId == Me) return Bad<PcIssueDto>(PcText.SameUser + " (بستن مشکل با کسی غیر از رفع‌کننده است)");
                    r.Status = PcIssueStatus.Closed; r.ClosedAt = now; r.ClosedByUserId = Me;
                    break;
                case "reopen":
                    if (r.Status != PcIssueStatus.Resolved && r.Status != PcIssueStatus.Closed) return Bad<PcIssueDto>("این مشکل باز است.");
                    if (note == null) return Bad<PcIssueDto>("دلیل بازگشایی را بنویسید.");
                    if (r.Status == PcIssueStatus.Closed && !_user.IsInRole("PMO,PROJECTMANAGER")) return Bad<PcIssueDto>(Denied);
                    r.Status = PcIssueStatus.InProgress; r.ClosedAt = null; r.ClosedByUserId = null; r.ResolvedAt = null; r.ResolvedByUserId = null;
                    break;
                default:
                    return Bad<PcIssueDto>("عمل نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcIssueDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await IssueDtosAsync(new List<PcIssue> { r }, ct))[0]);
        }

        // ============================================================================ stakeholders

        private async Task<List<PcStakeholderDto>> StakeholderDtosAsync(List<PcStakeholder> rows, CancellationToken ct)
        {
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.OwnerPositionId), ct);
            return rows.Select(r =>
            {
                string quadrant = PcStakeholderRules.Quadrant(r.Influence, r.Interest);
                return new PcStakeholderDto
                {
                    Id = r.Id, ContractId = r.ContractId, Name = r.Name, Organization = r.Organization, RoleTitle = r.RoleTitle, Category = r.Category, Influence = r.Influence, Interest = r.Interest, EngagementCurrent = r.EngagementCurrent,
                    EngagementDesired = r.EngagementDesired, Expectations = r.Expectations, CommunicationNeed = r.CommunicationNeed, CommunicationFrequency = r.CommunicationFrequency, OwnerPositionId = r.OwnerPositionId, IsActive = r.IsActive,
                    Quadrant = quadrant, QuadrantText = PcStakeholderRules.QuadrantText(quadrant), EngagementGap = (int)r.EngagementDesired - (int)r.EngagementCurrent, OwnerPositionTitle = positions.Title(r.OwnerPositionId),
                };
            }).ToList();
        }

        public async Task<ResponseDto<List<PcStakeholderDto>>> GetStakeholdersAsync(int contractId, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<PcStakeholderDto>>(Denied);
            List<PcStakeholder> rows = await _uow.Set<PcStakeholder>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == contractId).OrderByDescending(r => r.Influence * r.Interest).ThenBy(r => r.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await StakeholderDtosAsync(rows, ct));
        }

        public async Task<ResponseDto<PcStakeholderDto>> SaveStakeholderAsync(PcStakeholderSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcStakeholderDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcStakeholderDto>(Denied);
            string name = Clean(dto.Name);
            if (name == null) return Bad<PcStakeholderDto>("نام ذی‌نفع (شخص یا سازمان) را بنویسید.");
            if (dto.Influence < 1 || dto.Influence > 5 || dto.Interest < 1 || dto.Interest > 5) return Bad<PcStakeholderDto>("میزان نفوذ و علاقه عددی بین ۱ و ۵ است.");
            if (!Enum.IsDefined(typeof(PcEngagement), dto.EngagementCurrent) || !Enum.IsDefined(typeof(PcEngagement), dto.EngagementDesired)) return Bad<PcStakeholderDto>(OfficeText.InvalidData);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcStakeholderDto>(problem);
            if (!await PositionOkAsync(dto.OwnerPositionId, ct)) return Bad<PcStakeholderDto>("سمت مسئول ارتباط پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcStakeholder r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcStakeholder>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null || r.ContractId != dto.ContractId) return NotFound<PcStakeholderDto>("ذی‌نفع پیدا نشد.");
            }
            else
            {
                r = new PcStakeholder { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcStakeholder>().Add(r);
            }
            // the register keeps roles and organisations; there is no field for a private phone number or address
            r.Name = Cap(name, 200); r.Organization = Cap(dto.Organization, 200); r.RoleTitle = Cap(dto.RoleTitle, 200); r.Category = Cap(dto.Category, 60); r.Influence = dto.Influence; r.Interest = dto.Interest;
            r.EngagementCurrent = dto.EngagementCurrent; r.EngagementDesired = dto.EngagementDesired; r.Expectations = Cap(dto.Expectations, 1000); r.CommunicationNeed = Cap(dto.CommunicationNeed, 1000);
            r.CommunicationFrequency = Cap(dto.CommunicationFrequency, 100); r.OwnerPositionId = dto.OwnerPositionId; r.IsActive = dto.IsActive;
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcStakeholderDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await StakeholderDtosAsync(new List<PcStakeholder> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcStakeholderMatrixDto>> GetStakeholderMatrixAsync(int contractId, CancellationToken ct)
        {
            ResponseDto<List<PcStakeholderDto>> all = await GetStakeholdersAsync(contractId, ct);
            if (!all.Success) return Bad<PcStakeholderMatrixDto>(all.Message);
            var m = new PcStakeholderMatrixDto { ContractId = contractId };
            foreach (PcStakeholderDto s in all.Data.Where(s => s.IsActive))
                (s.Quadrant == "manage-closely" ? m.ManageClosely : s.Quadrant == "keep-satisfied" ? m.KeepSatisfied : s.Quadrant == "keep-informed" ? m.KeepInformed : m.Monitor).Add(s);
            return ResponseDtoHelper.Ok(m);
        }

        // ============================================================================ lessons learned

        private async Task<List<PcLessonDto>> LessonDtosAsync(List<PcLesson> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatorUserId, r.DecidedByUserId }), ct);
            return rows.Select(r => new PcLessonDto
            {
                Id = r.Id, ContractId = r.ContractId, Number = r.Number, Phase = r.Phase, Category = r.Category, IsPositive = r.IsPositive, WhatHappened = r.WhatHappened, Cause = r.Cause, Recommendation = r.Recommendation, State = r.State,
                CreatorName = names.Name(r.CreatorUserId), DecidedByName = names.Name(r.DecidedByUserId), DecidedAtText = OfficeText.Stamp(r.DecidedAt), DecisionNote = r.DecisionNote,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcLessonDto>>> GetLessonsAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcLessonDto>>(Denied);
            IQueryable<PcLesson> q = _uow.Set<PcLesson>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.Status.HasValue) q = q.Where(r => (int)r.State == query.Status.Value);
            string text = Clean(query.Contains); if (text != null) q = q.Where(r => r.WhatHappened.Contains(text) || r.Recommendation.Contains(text) || r.Number.Contains(text));
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcLesson> rows = await q.OrderByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await LessonDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcLessonDto>> SaveLessonAsync(PcLessonSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcLessonDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcLessonDto>(Denied);
            if (Clean(dto.WhatHappened) == null || Clean(dto.Recommendation) == null) return Bad<PcLessonDto>("شرح اتفاق و توصیه را بنویسید.");
            if (dto.Phase.HasValue && !Enum.IsDefined(typeof(PcPhase), dto.Phase.Value)) return Bad<PcLessonDto>(OfficeText.InvalidData);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcLessonDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            PcLesson r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcLesson>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null || r.ContractId != dto.ContractId) return NotFound<PcLessonDto>("درس آموخته پیدا نشد.");
                if (r.CreatorUserId != Me) return Bad<PcLessonDto>(Denied + " (فقط ثبت‌کننده ویرایش می‌کند)");
                if (r.State == PcApprovalState.Submitted || r.State == PcApprovalState.Approved) return Bad<PcLessonDto>("درس آموخته ارسال‌شده یا تاییدشده تغییر نمی‌کند.");
            }
            else
            {
                r = new PcLesson { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Lesson, "LL", DateTime.Today, ct);
                _uow.Set<PcLesson>().Add(r);
            }
            r.Phase = dto.Phase; r.Category = Cap(dto.Category, 60); r.IsPositive = dto.IsPositive; r.WhatHappened = Cap(dto.WhatHappened, 2000); r.Cause = Cap(dto.Cause, 2000); r.Recommendation = Cap(dto.Recommendation, 2000);
            r.State = dto.Submit ? PcApprovalState.Submitted : PcApprovalState.Draft;
            if (dto.Submit) r.SubmittedAt = now;
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcLessonDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await LessonDtosAsync(new List<PcLesson> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcLessonDto>> DecideLessonAsync(PcRegisterDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcLessonDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO")) return Bad<PcLessonDto>(Denied + " (تایید درس آموخته با PMO است)");
            PcLesson r = await _uow.Set<PcLesson>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcLessonDto>("درس آموخته پیدا نشد.");
            if (r.State != PcApprovalState.Submitted) return Bad<PcLessonDto>("این مورد در انتظار تصمیم نیست.");
            if (r.CreatorUserId == Me) return Bad<PcLessonDto>(PcText.SameUser + " (نویسنده درس آموخته آن را تایید نمی‌کند)");
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcLessonDto>("دلیل رد را بنویسید.");
            r.State = dto.Approve ? PcApprovalState.Approved : PcApprovalState.Rejected; r.DecidedByUserId = Me; r.DecidedAt = DateTimeOffset.Now; r.DecisionNote = Cap(dto.Note, 1000);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcLessonDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await LessonDtosAsync(new List<PcLesson> { r }, ct))[0]);
        }

        // ============================================================================ deliverables

        private async Task<List<PcProjDeliverableDto>> DeliverableDtosAsync(List<PcProjectDeliverable> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.DeliveredByUserId, r.AcceptedByUserId }), ct);
            List<int> actIds = rows.Where(r => r.ActivityId.HasValue).Select(r => r.ActivityId.Value).Distinct().ToList();
            List<int> wbsIds = rows.Where(r => r.WbsNodeId.HasValue).Select(r => r.WbsNodeId.Value).Distinct().ToList();
            Dictionary<int, string> acts = actIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            Dictionary<int, string> wbs = wbsIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => wbsIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Code, ct);
            DateTime today = DateTime.Today;
            return rows.Select(r => new PcProjDeliverableDto
            {
                Id = r.Id, ContractId = r.ContractId, Code = r.Code, Title = r.Title, Description = r.Description, WorkOrderId = r.WorkOrderId, WbsNodeId = r.WbsNodeId, WbsNodeCode = r.WbsNodeId.HasValue && wbs.TryGetValue(r.WbsNodeId.Value, out string w) ? w : null,
                ActivityId = r.ActivityId, ActivityCode = r.ActivityId.HasValue && acts.TryGetValue(r.ActivityId.Value, out string a) ? a : null, MdrDeliverableId = r.MdrDeliverableId, DueDate = r.DueDate, DueDateText = OfficeText.Date(r.DueDate),
                Overdue = r.DueDate.HasValue && r.DueDate.Value.Date < today && r.Status != PcDeliverableStatus.Accepted, Status = r.Status, DeliveredByName = names.Name(r.DeliveredByUserId), DeliveredAtText = OfficeText.Stamp(r.DeliveredAt),
                AcceptedByName = names.Name(r.AcceptedByUserId), AcceptedAtText = OfficeText.Stamp(r.AcceptedAt), AcceptanceNote = r.AcceptanceNote,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcProjDeliverableDto>>> GetDeliverablesAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcProjDeliverableDto>>(Denied);
            IQueryable<PcProjectDeliverable> q = _uow.Set<PcProjectDeliverable>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.Status.HasValue) q = q.Where(r => (int)r.Status == query.Status.Value);
            string text = Clean(query.Contains); if (text != null) q = q.Where(r => r.Title.Contains(text) || r.Code.Contains(text));
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcProjectDeliverable> rows = await q.OrderBy(r => r.Code).ThenBy(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await DeliverableDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcProjDeliverableDto>> SaveDeliverableAsync(PcProjDeliverableSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcProjDeliverableDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcProjDeliverableDto>(Denied);
            string code = PcText.Upper(dto.Code), title = Clean(dto.Title);
            if (code == null || title == null) return Bad<PcProjDeliverableDto>("کد و عنوان تحویلی را بنویسید.");
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcProjDeliverableDto>(problem);
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, ct)) return Bad<PcProjDeliverableDto>("دستور کار متعلق به این پروژه نیست.");
            if (dto.WbsNodeId.HasValue && !await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == dto.WbsNodeId.Value && n.ContractId == dto.ContractId && !n.Deleted, ct)) return Bad<PcProjDeliverableDto>("گره WBS متعلق به این پروژه نیست.");
            if (await ActivityOkAsync(dto.ActivityId, dto.ContractId, ct) != 0) return Bad<PcProjDeliverableDto>("فعالیت متعلق به این پروژه نیست.");
            if (dto.MdrDeliverableId.HasValue && !await _uow.Set<PcEngineeringDeliverable>().AnyAsync(d => d.Id == dto.MdrDeliverableId.Value && !d.Deleted, ct)) return Bad<PcProjDeliverableDto>("ردیف MDR پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcProjectDeliverable r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcProjectDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null || r.ContractId != dto.ContractId) return NotFound<PcProjDeliverableDto>("تحویلی پیدا نشد.");
                if (r.Status == PcDeliverableStatus.Accepted) return Bad<PcProjDeliverableDto>("تحویلی پذیرفته‌شده تغییر نمی‌کند.");
            }
            else
            {
                r = new PcProjectDeliverable { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcProjectDeliverable>().Add(r);
            }
            if (await _uow.Set<PcProjectDeliverable>().AnyAsync(x => x.ContractId == dto.ContractId && x.Code == code && !x.Deleted && x.Id != r.Id, ct)) return Bad<PcProjDeliverableDto>("کد تحویلی در این پروژه تکراری است.");
            r.Code = Cap(code, 50); r.Title = Cap(title, 300); r.Description = Cap(dto.Description, 1000); r.WorkOrderId = dto.WorkOrderId; r.WbsNodeId = dto.WbsNodeId; r.ActivityId = dto.ActivityId; r.MdrDeliverableId = dto.MdrDeliverableId; r.DueDate = dto.DueDate?.Date;
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcProjDeliverableDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await DeliverableDtosAsync(new List<PcProjectDeliverable> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcProjDeliverableDto>> DeliverableActionAsync(PcProjDeliverableActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<PcProjDeliverableDto>(OfficeText.InvalidData);
            PcProjectDeliverable r = await _uow.Set<PcProjectDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcProjDeliverableDto>("تحویلی پیدا نشد.");
            string note = Cap(dto.Note, 1000);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "start":
                    if (!CanWrite) return Bad<PcProjDeliverableDto>(Denied);
                    if (r.Status != PcDeliverableStatus.Planned) return Bad<PcProjDeliverableDto>("فقط تحویلی برنامه‌ریزی‌شده شروع می‌شود.");
                    r.Status = PcDeliverableStatus.InProgress;
                    break;
                case "deliver":
                    if (!CanWrite) return Bad<PcProjDeliverableDto>(Denied);
                    if (r.Status != PcDeliverableStatus.Planned && r.Status != PcDeliverableStatus.InProgress && r.Status != PcDeliverableStatus.Rejected) return Bad<PcProjDeliverableDto>("این تحویلی قابل تحویل‌دادن نیست.");
                    r.Status = PcDeliverableStatus.Delivered; r.DeliveredAt = now; r.DeliveredByUserId = Me;
                    break;
                case "accept":
                case "reject":
                    {
                        bool accept = dto.Action.Trim().ToLowerInvariant() == "accept";
                        if (!_user.IsInRole("PMO,PROJECTMANAGER")) return Bad<PcProjDeliverableDto>(Denied + " (پذیرش تحویلی با مدیر پروژه یا PMO است)");
                        if (r.Status != PcDeliverableStatus.Delivered) return Bad<PcProjDeliverableDto>("فقط تحویلی تحویل‌شده پذیرفته یا رد می‌شود.");
                        if (r.DeliveredByUserId == Me) return Bad<PcProjDeliverableDto>(PcText.SameUser + " (تحویل‌دهنده آن را نمی‌پذیرد)");
                        if (!accept && note == null) return Bad<PcProjDeliverableDto>("دلیل رد را بنویسید.");
                        r.Status = accept ? PcDeliverableStatus.Accepted : PcDeliverableStatus.Rejected; r.AcceptedAt = now; r.AcceptedByUserId = Me; r.AcceptanceNote = note;
                        break;
                    }
                default:
                    return Bad<PcProjDeliverableDto>("عمل نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcProjDeliverableDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await DeliverableDtosAsync(new List<PcProjectDeliverable> { r }, ct))[0]);
        }

        // ============================================================================ photos

        private async Task<List<PcPhotoDto>> PhotoDtosAsync(List<PcProjectPhoto> rows, CancellationToken ct)
        {
            List<int> actIds = rows.Where(r => r.ActivityId.HasValue).Select(r => r.ActivityId.Value).Distinct().ToList();
            Dictionary<int, string> acts = actIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.CreatorUserId), ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            var files = await _uow.Set<RecordAttachment>().AsNoTracking().Where(a => a.OwnerType == AttachmentOwnerType.PcProjectPhoto && ids.Contains(a.OwnerId) && !a.Deleted && !a.Voided && a.RemovedAt == null && a.SupersededById == null)
                .Select(a => new { a.OwnerId, a.DocumentId, a.Id }).ToListAsync(ct);
            return rows.Select(r => new PcPhotoDto
            {
                Id = r.Id, ContractId = r.ContractId, DocumentId = files.Where(f => f.OwnerId == r.Id).OrderByDescending(f => f.Id).Select(f => (Guid?)f.DocumentId).FirstOrDefault(), AttachmentCount = files.Count(f => f.OwnerId == r.Id), Caption = r.Caption, TakenDate = r.TakenDate, TakenDateText = OfficeText.Date(r.TakenDate), WbsNodeId = r.WbsNodeId, ActivityId = r.ActivityId,
                ActivityCode = r.ActivityId.HasValue && acts.TryGetValue(r.ActivityId.Value, out string c) ? c : null, IsVoid = r.IsVoid, VoidReason = r.VoidReason, UploadedByName = names.Name(r.CreatorUserId),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcPhotoDto>>> GetPhotosAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcPhotoDto>>(Denied);
            IQueryable<PcProjectPhoto> q = _uow.Set<PcProjectPhoto>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId && !r.IsVoid);
            string text = Clean(query.Contains); if (text != null) q = q.Where(r => r.Caption.Contains(text));
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcProjectPhoto> rows = await q.OrderByDescending(r => r.TakenDate).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await PhotoDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcPhotoDto>> AddPhotoAsync(PcPhotoSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcPhotoDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcPhotoDto>(Denied);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcPhotoDto>(problem);
            DateTime taken = (dto.TakenDate ?? DateTime.Today).Date;
            if (taken > DateTime.Today) return Bad<PcPhotoDto>("تاریخ عکس در آینده است.");
            if (dto.WbsNodeId.HasValue && !await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == dto.WbsNodeId.Value && n.ContractId == dto.ContractId && !n.Deleted, ct)) return Bad<PcPhotoDto>("گره WBS متعلق به این پروژه نیست.");
            if (await ActivityOkAsync(dto.ActivityId, dto.ContractId, ct) != 0) return Bad<PcPhotoDto>("فعالیت متعلق به این پروژه نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            var r = new PcProjectPhoto { ContractId = dto.ContractId, Caption = Cap(dto.Caption, 500), TakenDate = taken, WbsNodeId = dto.WbsNodeId, ActivityId = dto.ActivityId, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
            _uow.Set<PcProjectPhoto>().Add(r);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await PhotoDtosAsync(new List<PcProjectPhoto> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcPhotoDto>> VoidPhotoAsync(PcPhotoVoidDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcPhotoDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcPhotoDto>(Denied);
            if (Clean(dto.Reason) == null) return Bad<PcPhotoDto>("دلیل ابطال تصویر را بنویسید.");
            PcProjectPhoto r = await _uow.Set<PcProjectPhoto>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcPhotoDto>("تصویر پیدا نشد.");
            if (r.IsVoid) return Bad<PcPhotoDto>("این تصویر قبلاً باطل شده است.");
            if (r.CreatorUserId != Me && !_user.IsInRole("PMO,PROJECTMANAGER")) return Bad<PcPhotoDto>(Denied);
            r.IsVoid = true; r.VoidReason = Cap(dto.Reason, 500); r.VoidedByUserId = Me; r.VoidedAt = DateTimeOffset.Now; r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await PhotoDtosAsync(new List<PcProjectPhoto> { r }, ct))[0]);
        }

        // ============================================================================ contractor / consultant evaluation

        public ResponseDto<PcEvaluationTemplateDto> GetEvaluationTemplate(PcEvaluationParty kind)
        {
            if (!CanRead) return Bad<PcEvaluationTemplateDto>(Denied);
            var rows = kind == PcEvaluationParty.Consultant ? PcEvaluationRules.ConsultantCriteria : PcEvaluationRules.ContractorCriteria;
            int n = 0;
            return ResponseDtoHelper.Ok(new PcEvaluationTemplateDto { PartyKind = kind, Criteria = rows.Select(c => new PcEvaluationScoreDto { Seq = ++n, Criterion = c.Criterion, Weight = c.Weight }).ToList() });
        }

        private async Task<List<PcEvaluationDto>> EvaluationDtosAsync(List<PcPartyEvaluation> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.EvaluatorUserId), ct);
            return rows.Select(r => new PcEvaluationDto
            {
                Id = r.Id, ContractId = r.ContractId, Number = r.Number, PartyKind = r.PartyKind, ProviderId = r.ProviderId, PartyName = r.PartyName, EvaluationDate = r.EvaluationDate, EvaluationDateText = OfficeText.Date(r.EvaluationDate),
                State = r.State, TotalScore = r.TotalScore, Grade = r.Grade, Comment = r.Comment, EvaluatorName = names.Name(r.EvaluatorUserId),
                Scores = (r.Scores ?? new List<PcPartyEvaluationScore>()).OrderBy(s => s.Seq).Select(s => new PcEvaluationScoreDto { Seq = s.Seq, Criterion = s.Criterion, Weight = s.Weight, Score = s.Score, Comment = s.Comment }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcEvaluationDto>>> GetEvaluationsAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcEvaluationDto>>(Denied);
            IQueryable<PcPartyEvaluation> q = _uow.Set<PcPartyEvaluation>().AsNoTracking().Include(e => e.Scores).Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.Status.HasValue) q = q.Where(r => (int)r.State == query.Status.Value);
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcPartyEvaluation> rows = await q.OrderByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await EvaluationDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcEvaluationDto>> SaveEvaluationAsync(PcEvaluationSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcEvaluationDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO,PROJECTMANAGER,COMMERCIAL")) return Bad<PcEvaluationDto>(Denied);
            if (!Enum.IsDefined(typeof(PcEvaluationParty), dto.PartyKind)) return Bad<PcEvaluationDto>(OfficeText.InvalidData);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcEvaluationDto>(problem);
            string party = Clean(dto.PartyName);
            if (dto.ProviderId.HasValue)
            {
                party ??= await _uow.Set<Provider>().AsNoTracking().Where(p => p.Id == dto.ProviderId.Value).Select(p => p.Name).FirstOrDefaultAsync(ct);
                if (party == null) return Bad<PcEvaluationDto>("تامین‌کننده پیدا نشد.");
            }
            if (party == null) return Bad<PcEvaluationDto>("نام پیمانکار یا مشاور را مشخص کنید.");
            List<PcEvaluationScoreDto> scores = dto.Scores ?? new List<PcEvaluationScoreDto>();
            if (scores.Count == 0) return Bad<PcEvaluationDto>("معیارهای ارزیابی را وارد کنید.");
            if (scores.Any(s => Clean(s.Criterion) == null || s.Weight <= 0m || s.Weight > 100m || s.Score < 0m || s.Score > 5m)) return Bad<PcEvaluationDto>("وزن هر معیار بیشتر از صفر و امتیاز بین ۰ و ۵ است.");
            if (dto.Submit)
            {
                if (!dto.ProviderId.HasValue) return Bad<PcEvaluationDto>("برای ارسال ارزیابی، پیمانکار یا مشاور را از فهرست تامین‌کنندگان انتخاب کنید.");
                // the buyer who ordered from the party cannot judge it
                if (dto.ProviderId.HasValue && await _uow.Set<PurchaseOrder>().AnyAsync(o => !o.Deleted && o.ProviderId == dto.ProviderId.Value && o.ContractId == dto.ContractId && o.CreatorUserId == Me, ct))
                    return Bad<PcEvaluationDto>(PcText.SameUser + " (خریداری که از این طرف خرید کرده، آن را ارزیابی نمی‌کند)");
            }
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            PcPartyEvaluation r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcPartyEvaluation>().Include(x => x.Scores).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null || r.ContractId != dto.ContractId) return NotFound<PcEvaluationDto>("ارزیابی پیدا نشد.");
                if (r.EvaluatorUserId != Me) return Bad<PcEvaluationDto>(Denied + " (فقط ارزیاب ویرایش می‌کند)");
                if (r.State == PcApprovalState.Submitted || r.State == PcApprovalState.Approved) return Bad<PcEvaluationDto>("ارزیابی ارسال‌شده یا تاییدشده تغییر نمی‌کند.");
                _uow.Set<PcPartyEvaluationScore>().RemoveRange(r.Scores);
                r.Scores = new List<PcPartyEvaluationScore>();
            }
            else
            {
                r = new PcPartyEvaluation { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me, EvaluatorUserId = Me, Scores = new List<PcPartyEvaluationScore>() };
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.Evaluation, "EV", DateTime.Today, ct);
                _uow.Set<PcPartyEvaluation>().Add(r);
            }
            int seq = 0;
            foreach (PcEvaluationScoreDto s in scores) r.Scores.Add(new PcPartyEvaluationScore { Seq = ++seq, Criterion = Cap(s.Criterion, 200), Weight = s.Weight, Score = s.Score, Comment = Cap(s.Comment, 500) });
            r.PartyKind = dto.PartyKind; r.ProviderId = dto.ProviderId; r.PartyName = Cap(party, 200); r.EvaluationDate = (dto.EvaluationDate ?? DateTime.Today).Date; r.Comment = Cap(dto.Comment, 2000);
            r.TotalScore = PcEvaluationRules.Total(scores.Select(s => (s.Weight, s.Score)));
            r.Grade = PcEvaluationRules.Grade(r.TotalScore, settings[PcSettingKeys.GradeA], settings[PcSettingKeys.GradeB], settings[PcSettingKeys.GradeC]);
            r.State = dto.Submit ? PcApprovalState.Submitted : PcApprovalState.Draft;
            if (dto.Submit) r.SubmittedAt = now;
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcEvaluationDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await EvaluationDtosAsync(new List<PcPartyEvaluation> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcEvaluationDto>> DecideEvaluationAsync(PcRegisterDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcEvaluationDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO")) return Bad<PcEvaluationDto>(Denied + " (تایید ارزیابی با PMO است)");
            PcPartyEvaluation r = await _uow.Set<PcPartyEvaluation>().Include(x => x.Scores).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcEvaluationDto>("ارزیابی پیدا نشد.");
            if (r.State != PcApprovalState.Submitted) return Bad<PcEvaluationDto>("این ارزیابی در انتظار تصمیم نیست.");
            if (r.EvaluatorUserId == Me) return Bad<PcEvaluationDto>(PcText.SameUser);
            // the PMO who bought from the party does not decide on its evaluation either
            if (r.ProviderId.HasValue && await _uow.Set<PurchaseOrder>().AnyAsync(o => !o.Deleted && o.ProviderId == r.ProviderId.Value && o.ContractId == r.ContractId && o.CreatorUserId == Me, ct))
                return Bad<PcEvaluationDto>(PcText.SameUser + " (خریداری که از این طرف خرید کرده، درباره ارزیابی آن تصمیم نمی‌گیرد)");
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcEvaluationDto>("دلیل رد را بنویسید.");
            r.State = dto.Approve ? PcApprovalState.Approved : PcApprovalState.Rejected; r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            if (!dto.Approve) r.Comment = Cap((r.Comment == null ? "" : r.Comment + "\n") + "دلیل رد: " + dto.Note, 2000);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await EvaluationDtosAsync(new List<PcPartyEvaluation> { r }, ct))[0]);
        }

        // ============================================================================ weekly report

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private async Task<PcWeeklyReportDto> WeeklyDtoAsync(PcWeeklyReport r, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ReleasedByUserId }, ct);
            PcWeeklyAutoDto auto = new PcWeeklyAutoDto();
            try { if (!string.IsNullOrWhiteSpace(r.AutoJson)) auto = JsonSerializer.Deserialize<PcWeeklyAutoDto>(r.AutoJson, Json) ?? auto; } catch (JsonException) { }
            return new PcWeeklyReportDto
            {
                Id = r.Id, ContractId = r.ContractId, Number = r.Number, WeekStart = r.WeekStart, WeekEnd = r.WeekEnd, WeekText = OfficeText.Date(r.WeekStart) + " تا " + OfficeText.Date(r.WeekEnd), State = r.State, Auto = auto,
                Summary = r.Summary, NextWeekPlan = r.NextWeekPlan, GeneratedAtText = OfficeText.Stamp(r.GeneratedAt), ReleasedByName = names.Name(r.ReleasedByUserId), ReleasedAtText = OfficeText.Stamp(r.ReleasedAt),
            };
        }

        public async Task<ResponseDto<PagedList<PcWeeklyReportDto>>> GetWeeklyReportsAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcWeeklyReportDto>>(Denied);
            IQueryable<PcWeeklyReport> q = _uow.Set<PcWeeklyReport>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcWeeklyReport> rows = await q.OrderByDescending(r => r.WeekStart).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            var list = new List<PcWeeklyReportDto>();
            foreach (PcWeeklyReport r in rows) list.Add(await WeeklyDtoAsync(r, ct));
            return Page(list, total, page, size);
        }

        private async Task<PcWeeklyAutoDto> BuildAutoAsync(int contractId, DateTime start, DateTime end, CancellationToken ct)
        {
            var auto = new PcWeeklyAutoDto();
            ResponseDto<PcProgressDto> prog = await _progress.GetProgressAsync(contractId, ct);
            if (prog.Success) { auto.ProgressPercent = prog.Data.PhysicalPercent; auto.PlannedPercent = prog.Data.PlannedPercent; auto.LateActivities = prog.Data.LateActivities; }
            ResponseDto<PcEvmDto> evm = await _progress.GetEvmAsync(contractId, null, ct);
            if (evm.Success) { auto.Spi = evm.Data.Spi; auto.Cpi = evm.Data.Cpi; }
            DateTime endDay = end.Date.AddDays(1);
            auto.ActivitiesStarted = await _uow.Set<PcActivity>().CountAsync(a => !a.Deleted && a.ContractId == contractId && a.ActualStart >= start && a.ActualStart < endDay, ct);
            auto.ActivitiesFinished = await _uow.Set<PcActivity>().CountAsync(a => !a.Deleted && a.ContractId == contractId && a.ActualFinish >= start && a.ActualFinish < endDay, ct);
            auto.NewIssues = await _uow.Set<PcIssue>().CountAsync(i => !i.Deleted && i.ContractId == contractId && i.RaisedDate >= start && i.RaisedDate < endDay, ct);
            auto.OpenIssues = await _uow.Set<PcIssue>().CountAsync(i => !i.Deleted && i.ContractId == contractId && i.Status != PcIssueStatus.Closed && i.Status != PcIssueStatus.Resolved, ct);
            auto.ClosedIssues = await _uow.Set<PcIssue>().CountAsync(i => !i.Deleted && i.ContractId == contractId && i.ClosedAt != null && i.ClosedAt >= start && i.ClosedAt < endDay, ct);
            var (high, _, _) = await RiskLimitsAsync(ct);
            auto.OpenRisks = await _uow.Set<PcRisk>().CountAsync(r => !r.Deleted && r.ContractId == contractId && r.Status != PcRiskStatus.Closed, ct);
            auto.HighRisks = await _uow.Set<PcRisk>().CountAsync(r => !r.Deleted && r.ContractId == contractId && r.Status != PcRiskStatus.Closed && !r.IsOpportunity && r.Score >= high, ct);
            DateTime today = DateTime.Today;
            auto.TasksClosed = await _uow.Set<TmTask>().CountAsync(t => !t.Deleted && t.ContractId == contractId && t.ClosedAt != null && t.ClosedAt >= start && t.ClosedAt < endDay, ct);
            auto.TasksOverdue = await _uow.Set<TmTask>().CountAsync(t => !t.Deleted && t.ContractId == contractId && t.DueDate != null && t.DueDate < today && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected, ct);
            auto.DailyReports = await _uow.Set<PcDailyReport>().CountAsync(d => !d.Deleted && d.ContractId == contractId && d.State == 1 && d.ReportDate >= start && d.ReportDate <= end.Date, ct);
            auto.HseEvents = await _uow.Set<PcDailyHse>().CountAsync(h => !h.Report.Deleted && h.Report.ContractId == contractId && h.Report.ReportDate >= start && h.Report.ReportDate <= end.Date, ct);
            if (auto.LateActivities > 0) auto.Highlights.Add(auto.LateActivities.ToString(CultureInfo.InvariantCulture) + " فعالیت دچار تاخیر است.");
            if (auto.HighRisks > 0) auto.Highlights.Add(auto.HighRisks.ToString(CultureInfo.InvariantCulture) + " ریسک با امتیاز بالا باز است.");
            if (auto.TasksOverdue > 0) auto.Highlights.Add(auto.TasksOverdue.ToString(CultureInfo.InvariantCulture) + " وظیفه معوق است.");
            if (auto.HseEvents > 0) auto.Highlights.Add(auto.HseEvents.ToString(CultureInfo.InvariantCulture) + " رویداد HSE در گزارش‌های روزانه ثبت شده است.");
            return auto;
        }

        public async Task<ResponseDto<PcWeeklyReportDto>> GenerateWeeklyAsync(PcWeeklyGenerateDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcWeeklyReportDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcWeeklyReportDto>(Denied);
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcWeeklyReportDto>(problem);
            (DateTime start, DateTime end) = TaskRules.WeekOf((dto.Date ?? DateTime.Today).Date);
            if (start > DateTime.Today) return Bad<PcWeeklyReportDto>("گزارش هفتگی برای هفته آینده ساخته نمی‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcWeeklyReport r = await _uow.Set<PcWeeklyReport>().FirstOrDefaultAsync(x => !x.Deleted && x.ContractId == dto.ContractId && x.WeekStart == start, ct);
            if (r != null && r.State == PcWeeklyState.Released) return Bad<PcWeeklyReportDto>("گزارش این هفته منتشر شده و تغییر نمی‌کند.");
            if (r == null)
            {
                r = new PcWeeklyReport { ContractId = dto.ContractId, WeekStart = start, WeekEnd = end, State = PcWeeklyState.Draft, CreateDate = now, CreatorUserId = Me };
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, TaskNumbers.WeeklyReport, "WR", DateTime.Today, ct);
                _uow.Set<PcWeeklyReport>().Add(r);
            }
            r.AutoJson = JsonSerializer.Serialize(await BuildAutoAsync(dto.ContractId, start, end, ct), Json);
            r.GeneratedAt = now; r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<PcWeeklyReportDto>("گزارش این هفته هم‌زمان ساخته شد؛ دوباره باز کنید."); }
            return ResponseDtoHelper.Ok(await WeeklyDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PcWeeklyReportDto>> SaveWeeklyAsync(PcWeeklySaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcWeeklyReportDto>(OfficeText.InvalidData);
            if (!CanWrite) return Bad<PcWeeklyReportDto>(Denied);
            PcWeeklyReport r = await _uow.Set<PcWeeklyReport>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcWeeklyReportDto>("گزارش هفتگی پیدا نشد.");
            if (r.State == PcWeeklyState.Released) return Bad<PcWeeklyReportDto>("گزارش منتشرشده فقط خواندنی است.");
            r.Summary = Cap(dto.Summary, 4000); r.NextWeekPlan = Cap(dto.NextWeekPlan, 4000); r.LastEditorUserId = Me; r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcWeeklyReportDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok(await WeeklyDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PcWeeklyReportDto>> ReleaseWeeklyAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole("PMO")) return Bad<PcWeeklyReportDto>(Denied + " (انتشار گزارش هفتگی با PMO است)");
            PcWeeklyReport r = await _uow.Set<PcWeeklyReport>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return NotFound<PcWeeklyReportDto>("گزارش هفتگی پیدا نشد.");
            if (r.State == PcWeeklyState.Released) return Bad<PcWeeklyReportDto>("این گزارش قبلاً منتشر شده است.");
            if (Clean(r.Summary) == null) return Bad<PcWeeklyReportDto>("پیش از انتشار خلاصه گزارش را بنویسید.");
            if (r.LastEditorUserId == Me) return Bad<PcWeeklyReportDto>(PcText.SameUser + " (ویرایشگر خلاصه، گزارش را منتشر نمی‌کند)");
            r.State = PcWeeklyState.Released; r.ReleasedAt = DateTimeOffset.Now; r.ReleasedByUserId = Me; r.ModifyDate = r.ReleasedAt.Value; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcWeeklyReportDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok(await WeeklyDtoAsync(r, ct));
        }

        // ============================================================================ phase evaluation

        private async Task<List<PcPhaseEvaluationDto>> PhaseDtosAsync(List<PcPhaseEvaluation> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatorUserId, r.DecidedByUserId }), ct);
            return rows.Select(r => new PcPhaseEvaluationDto
            {
                Id = r.Id, ContractId = r.ContractId, Phase = r.Phase, EvaluationDate = r.EvaluationDate, EvaluationDateText = OfficeText.Date(r.EvaluationDate), ScheduleScore = r.ScheduleScore, CostScore = r.CostScore, QualityScore = r.QualityScore,
                ScopeScore = r.ScopeScore, OverallScore = r.OverallScore, Achievements = r.Achievements, Problems = r.Problems, Recommendation = r.Recommendation, Decision = r.Decision, State = r.State,
                CreatorName = names.Name(r.CreatorUserId), DecidedByName = names.Name(r.DecidedByUserId), DecisionNote = r.DecisionNote,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcPhaseEvaluationDto>>> GetPhaseEvaluationsAsync(PcRegisterQuery query, CancellationToken ct)
        {
            query ??= new PcRegisterQuery();
            if (!CanRead) return Bad<PagedList<PcPhaseEvaluationDto>>(Denied);
            IQueryable<PcPhaseEvaluation> q = _uow.Set<PcPhaseEvaluation>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            var (page, size) = await PageOf(query);
            int total = await q.CountAsync(ct);
            List<PcPhaseEvaluation> rows = await q.OrderByDescending(r => r.EvaluationDate).ThenByDescending(r => r.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return Page(await PhaseDtosAsync(rows, ct), total, page, size);
        }

        public async Task<ResponseDto<PcPhaseEvaluationDto>> SavePhaseEvaluationAsync(PcPhaseEvaluationSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcPhaseEvaluationDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO,PROJECTMANAGER,PLANNING")) return Bad<PcPhaseEvaluationDto>(Denied);
            if (!Enum.IsDefined(typeof(PcPhase), dto.Phase) || !Enum.IsDefined(typeof(PcPhaseDecision), dto.Decision)) return Bad<PcPhaseEvaluationDto>(OfficeText.InvalidData);
            if (!PcPhaseRules.ValidScore(dto.ScheduleScore) || !PcPhaseRules.ValidScore(dto.CostScore) || !PcPhaseRules.ValidScore(dto.QualityScore) || !PcPhaseRules.ValidScore(dto.ScopeScore)) return Bad<PcPhaseEvaluationDto>("امتیاز هر محور عددی بین ۱ و ۵ است.");
            string problem = await ContractProblemAsync(dto.ContractId, ct);
            if (problem != null) return NotFound<PcPhaseEvaluationDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            PcPhaseEvaluation r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcPhaseEvaluation>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null || r.ContractId != dto.ContractId) return NotFound<PcPhaseEvaluationDto>("ارزیابی فاز پیدا نشد.");
                if (r.CreatorUserId != Me) return Bad<PcPhaseEvaluationDto>(Denied + " (فقط ثبت‌کننده ویرایش می‌کند)");
                if (r.State == PcApprovalState.Submitted || r.State == PcApprovalState.Approved) return Bad<PcPhaseEvaluationDto>("ارزیابی ارسال‌شده یا تاییدشده تغییر نمی‌کند.");
            }
            else
            {
                r = new PcPhaseEvaluation { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcPhaseEvaluation>().Add(r);
            }
            r.Phase = dto.Phase; r.EvaluationDate = (dto.EvaluationDate ?? DateTime.Today).Date; r.ScheduleScore = dto.ScheduleScore; r.CostScore = dto.CostScore; r.QualityScore = dto.QualityScore; r.ScopeScore = dto.ScopeScore;
            r.OverallScore = PcPhaseRules.Overall(dto.ScheduleScore, dto.CostScore, dto.QualityScore, dto.ScopeScore); r.Achievements = Cap(dto.Achievements, 2000); r.Problems = Cap(dto.Problems, 2000); r.Recommendation = Cap(dto.Recommendation, 2000);
            r.Decision = dto.Decision; r.State = dto.Submit ? PcApprovalState.Submitted : PcApprovalState.Draft; r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcPhaseEvaluationDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await PhaseDtosAsync(new List<PcPhaseEvaluation> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PcPhaseEvaluationDto>> DecidePhaseEvaluationAsync(PcRegisterDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcPhaseEvaluationDto>(OfficeText.InvalidData);
            if (!_user.IsInRole("PMO")) return Bad<PcPhaseEvaluationDto>(Denied + " (تایید ارزیابی فاز با PMO است)");
            PcPhaseEvaluation r = await _uow.Set<PcPhaseEvaluation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return NotFound<PcPhaseEvaluationDto>("ارزیابی فاز پیدا نشد.");
            if (r.State != PcApprovalState.Submitted) return Bad<PcPhaseEvaluationDto>("این ارزیابی در انتظار تصمیم نیست.");
            if (r.CreatorUserId == Me) return Bad<PcPhaseEvaluationDto>(PcText.SameUser + " (نویسنده ارزیابی آن را تایید نمی‌کند)");
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcPhaseEvaluationDto>("دلیل رد را بنویسید.");
            r.State = dto.Approve ? PcApprovalState.Approved : PcApprovalState.Rejected; r.DecidedByUserId = Me; r.DecidedAt = DateTimeOffset.Now; r.DecisionNote = Cap(dto.Note, 1000);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcPhaseEvaluationDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok((await PhaseDtosAsync(new List<PcPhaseEvaluation> { r }, ct))[0]);
        }

        // ============================================================================ one-page dashboard

        public async Task<ResponseDto<PcOnePageDto>> GetOnePageAsync(int contractId, CancellationToken ct)
        {
            if (!CanRead) return Bad<PcOnePageDto>(Denied);
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && !c.Deleted, ct);
            if (contract == null) return NotFound<PcOnePageDto>(PcText.ContractNotFound);
            DateTime today = DateTime.Today;
            var dto = new PcOnePageDto { ContractId = contractId, ContractTitle = contract.Title, ContractNumber = contract.Code, Budget = contract.Price };

            // charter summary
            PcProjectCharter charter = await _uow.Set<PcProjectCharter>().AsNoTracking().FirstOrDefaultAsync(c => c.ContractId == contractId && !c.Deleted, ct);
            if (charter != null) { dto.CharterTitle = contract.Title; dto.CharterObjective = Short(charter.Overview); dto.CharterScope = Short(charter.ScopeDescription); }
            int? pmId = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId && !t.Deleted).Select(t => t.ProjectManagerUserId).FirstOrDefaultAsync(ct);
            if (pmId.HasValue) dto.ProjectManagerName = (await InventoryUsers.GetNamesAsync(_uow, new int?[] { pmId }, ct)).Name(pmId);

            // progress and S-curve
            ResponseDto<PcProgressDto> prog = await _progress.GetProgressAsync(contractId, ct);
            if (prog.Success) { dto.ProgressPercent = prog.Data.PhysicalPercent; dto.PlannedPercent = prog.Data.PlannedPercent; dto.LateActivities = prog.Data.LateActivities; }
            ResponseDto<PcEvmDto> evm = await _progress.GetEvmAsync(contractId, null, ct);
            if (evm.Success)
            {
                dto.Spi = evm.Data.Spi; dto.Cpi = evm.Data.Cpi; dto.Health = evm.Data.SpiStatus == "red" || evm.Data.CpiStatus == "red" ? "red" : evm.Data.SpiStatus == "amber" || evm.Data.CpiStatus == "amber" ? "amber" : evm.Data.SpiStatus == "none" && evm.Data.CpiStatus == "none" ? "none" : "green";
            }
            List<DateTime?> bounds = await _uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.ContractId == contractId).Select(a => (DateTime?)(a.ForecastStart ?? a.EarlyStart)).ToListAsync(ct);
            dto.PlannedStart = bounds.Where(b => b.HasValue).Select(b => b.Value).DefaultIfEmpty().Min() is DateTime ps && ps != default ? ps : (DateTime?)null;
            dto.PlannedFinish = evm.Success ? evm.Data.BaselineFinish ?? evm.Data.ForecastFinish : null;
            dto.PlannedStartText = OfficeText.Date(dto.PlannedStart); dto.PlannedFinishText = OfficeText.Date(dto.PlannedFinish);
            ResponseDto<List<PcCashFlowPointDto>> flow = await _progress.GetCashFlowAsync(contractId, ct);
            if (flow.Success && flow.Data.Count > 0)
            {
                decimal max = flow.Data.Max(p => p.PlannedCumulative);
                if (max > 0m)
                    dto.SCurve = flow.Data.Select(p => new PcSCurvePointDto
                    {
                        Date = p.Date, DateText = OfficeText.Date(p.Date), PlannedPercent = Math.Round(p.PlannedCumulative / max * 100m, 2),
                        ActualPercent = p.Date.Date <= today && p.EarnedValue.HasValue ? Math.Round(p.EarnedValue.Value / max * 100m, 2) : (decimal?)null,
                    }).ToList();
            }

            // milestones: activities of the milestone method or with no duration
            var ms = await _uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.ContractId == contractId && (a.ProgressMethod == PcProgressMethod.Milestone || a.DurationDays == 0))
                .OrderBy(a => a.ForecastFinish ?? a.EarlyFinish).ThenBy(a => a.Sort).Take(12).ToListAsync(ct);
            dto.Milestones = ms.Select(a => new PcOnePageMilestoneDto
            {
                ActivityId = a.Id, Code = a.Code, Title = a.Title, PlannedDate = a.ForecastFinish ?? a.EarlyFinish, PlannedDateText = OfficeText.Date(a.ForecastFinish ?? a.EarlyFinish), Percent = a.PercentCache,
                Done = a.Status == PcActivityStatus.Done, Late = a.Status != PcActivityStatus.Done && (a.ForecastFinish ?? a.EarlyFinish).HasValue && (a.ForecastFinish ?? a.EarlyFinish).Value.Date < today,
            }).ToList();

            // deliverables, issues, risks, tasks, waiting reports
            var del = await _uow.Set<PcProjectDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.ContractId == contractId).Select(d => new { d.Status, d.DueDate }).ToListAsync(ct);
            dto.DeliverablesTotal = del.Count; dto.DeliverablesAccepted = del.Count(d => d.Status == PcDeliverableStatus.Accepted);
            dto.DeliverablesOverdue = del.Count(d => d.Status != PcDeliverableStatus.Accepted && d.DueDate.HasValue && d.DueDate.Value.Date < today);
            List<PcIssue> issues = await _uow.Set<PcIssue>().AsNoTracking().Where(i => !i.Deleted && i.ContractId == contractId && i.Status != PcIssueStatus.Closed).ToListAsync(ct);
            dto.IssuesOpen = issues.Count; dto.IssuesOverdue = issues.Count(i => i.DueDate.HasValue && i.DueDate.Value.Date < today && i.Status != PcIssueStatus.Resolved); dto.IssuesHigh = issues.Count(i => i.Priority >= TmPriority.High);
            dto.TopIssues = (await IssueDtosAsync(issues.OrderByDescending(i => i.Priority).ThenBy(i => i.DueDate).Take(5).ToList(), ct));
            var (high, critical, _) = await RiskLimitsAsync(ct);
            List<PcRisk> risks = await _uow.Set<PcRisk>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == contractId && r.Status != PcRiskStatus.Closed).ToListAsync(ct);
            dto.RisksOpen = risks.Count; dto.RisksHigh = risks.Count(r => !r.IsOpportunity && r.Score >= high); dto.RisksCritical = risks.Count(r => !r.IsOpportunity && r.Score >= critical);
            dto.TopRisks = await RiskDtosAsync(risks.OrderByDescending(r => r.Score).ThenBy(r => r.Id).Take(5).ToList(), ct);
            dto.TasksOpen = await _uow.Set<TmTask>().CountAsync(t => !t.Deleted && t.ContractId == contractId && t.Kind != TmTaskKind.Personal && t.Confidentiality == OfClassification.Normal && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected, ct);
            dto.TasksOverdue = await _uow.Set<TmTask>().CountAsync(t => !t.Deleted && t.ContractId == contractId && t.Kind != TmTaskKind.Personal && t.Confidentiality == OfClassification.Normal && t.DueDate != null && t.DueDate < today && t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected, ct);
            dto.ProgressWaiting = await _uow.Set<PcProgressSubmission>().CountAsync(s => !s.Deleted && s.ContractId == contractId && s.State == PcSubmissionState.InApproval, ct);
            return ResponseDtoHelper.Ok(dto);
        }

        private static string Short(string s) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > 400 ? s.Trim().Substring(0, 400) + "..." : s.Trim());
    }
}
