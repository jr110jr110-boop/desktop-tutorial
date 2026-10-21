using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcBaselineService
    {
        Task<ResponseDto<PagedList<PcBaselineDto>>> GetBaselinesAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcBaselineDto>> GetBaselineAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcBaselineDto>> CreateBaselineAsync(PcBaselineCreateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcBaselineDto>> BaselineActionAsync(PcBaselineActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcCostLineDto>>> GetCostLinesAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcCostLineDto>> SaveCostLineAsync(PcCostLineDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteCostLineAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcChangeDto>>> GetChangesAsync(PcChangeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcChangeDto>> GetChangeAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcChangeDto>> SaveChangeAsync(PcChangeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcChangeDto>> ChangeActionAsync(PcChangeActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcAuthorityDto>>> GetAuthoritiesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<PcAuthorityDto>>> SaveAuthorityAsync(PcAuthorityDto dto, CancellationToken cancellationToken);
        Task<List<int>> OverdueChangeContractIdsAsync(CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcClaimDto>>> GetClaimsAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcClaimDto>> SaveClaimAsync(PcClaimDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcClaimDto>> ClaimActionAsync(PcClaimActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Baselines (WI-PL-009 17), integrated change control (WI-PL-013) and claims.</summary>
    public class PcBaselineService : IPcBaselineService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcScheduleService _schedule;
        private readonly IPcProjectService _project;

        public PcBaselineService(IUnitOfWork uow, IInventoryUserContext user, IPcScheduleService schedule, IPcProjectService project)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _project = project ?? throw new ArgumentNullException(nameof(project));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        // ============================================================================ baselines

        private async Task<PcBaselineDto> ToDtoAsync(PcBaseline b, CancellationToken cancellationToken)
        {
            List<PcBaselineSignature> sigs = b.Signatures != null ? b.Signatures.ToList() : await _uow.Set<PcBaselineSignature>().AsNoTracking().Where(s => s.BaselineId == b.Id).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, sigs.Select(s => s.UserId), cancellationToken);
            int count = await _uow.Set<PcBaselineActivity>().AsNoTracking().CountAsync(a => a.BaselineId == b.Id, cancellationToken);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, b.ContractId, false, cancellationToken);
            return new PcBaselineDto
            {
                Id = b.Id, ContractId = b.ContractId, Number = b.Number, Kind = b.Kind, Status = b.Status, Reason = b.Reason, ChangeRequestId = b.ChangeRequestId, ProjectStart = b.ProjectStart, ProjectFinish = b.ProjectFinish,
                Bac = b.Bac, ManagementReserve = b.ManagementReserve, ActivityCount = count, ApprovedAt = b.ApprovedAt?.Year > 1 ? b.ApprovedAt : null, IsCurrent = terms != null && terms.CurrentBaselineId == b.Id,
                Signatures = sigs.OrderBy(s => s.UnitRole).Select(s => new PcBaselineSignatureDto
                    { UnitRole = s.UnitRole, UserId = s.UserId, UserName = s.UserId.HasValue && names.TryGetValue(s.UserId.Value, out string nm) ? nm : null, SignedAt = s.SignedAt }).ToList(),
                RowVersion = b.RowVersion == null ? null : Convert.ToBase64String(b.RowVersion),
            };
        }

        public async Task<ResponseDto<PagedList<PcBaselineDto>>> GetBaselinesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcBaseline> q = _uow.Set<PcBaseline>().AsNoTracking().Include(b => b.Signatures).Where(b => !b.Deleted);
            if (query.ContractId.HasValue) q = q.Where(b => b.ContractId == query.ContractId.Value);
            PagedList<PcBaseline> page = await PagedList<PcBaseline>.CreateAsync(q.OrderBy(b => b.ContractId).ThenBy(b => b.Number).AsSplitQuery(), query.PageNumber, query.PageSize, cancellationToken);
            var dtos = new List<PcBaselineDto>();
            foreach (PcBaseline b in page) dtos.Add(await ToDtoAsync(b, cancellationToken));
            return ResponseDtoHelper.Ok((PagedList<PcBaselineDto>)new PcPage<PcBaselineDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcBaselineDto>> GetBaselineAsync(int id, CancellationToken cancellationToken)
        {
            PcBaseline b = await _uow.Set<PcBaseline>().AsNoTracking().Include(x => x.Signatures).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return b == null ? PcText.NotFound<PcBaselineDto>("خط مبنا پیدا نشد.") : ResponseDtoHelper.Ok(await ToDtoAsync(b, cancellationToken));
        }

        /// <summary>Builds a baseline from the current plan run (tracked, nothing saved). Activities need planned dates.</summary>
        private async Task<(PcBaseline Baseline, string Error)> BuildBaselineAsync(int contractId, int number, string reason, CancellationToken cancellationToken)
        {
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
            if (acts.Count == 0) return (null, "فعالیتی برای ثبت خط مبنا وجود ندارد.");
            if (acts.Any(a => !a.EarlyStart.HasValue || !a.EarlyFinish.HasValue)) return (null, "ابتدا محاسبه مسیر بحرانی را اجرا کنید؛ همه فعالیت‌ها باید تاریخ برنامه داشته باشند.");
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var b = new PcBaseline
            {
                ContractId = contractId, Number = number, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Draft, Reason = Clean(reason),
                ProjectStart = acts.Min(a => a.EarlyStart.Value).Date, ProjectFinish = acts.Max(a => a.EarlyFinish.Value).Date,
                Bac = acts.Sum(a => a.BudgetAtCompletion), ManagementReserve = terms?.ManagementReserve ?? 0m,
                CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me, Signatures = new List<PcBaselineSignature>(), Activities = new List<PcBaselineActivity>(),
            };
            foreach (PcActivity a in acts)
                b.Activities.Add(new PcBaselineActivity
                {
                    ActivityId = a.Id, Code = a.Code, Title = a.Title, WbsNodeId = a.WbsNodeId, Phase = a.Phase, Start = a.EarlyStart.Value.Date, Finish = a.EarlyFinish.Value.Date, DurationDays = a.DurationDays,
                    Weight = a.Weight, Budget = a.BudgetAtCompletion, ProgressMethod = a.ProgressMethod, CalendarId = a.CalendarId, ResponsibleUnit = a.ResponsibleUnit,
                });
            foreach (string unit in acts.Select(a => a.ResponsibleUnit).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim().ToUpperInvariant()).Distinct().OrderBy(u => u))
                b.Signatures.Add(new PcBaselineSignature { UnitRole = unit });
            return (b, null);
        }

        public async Task<ResponseDto<PcBaselineDto>> CreateBaselineAsync(PcBaselineCreateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcBaselineDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcBaselineDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcBaselineDto>(PcText.ContractNotFound);
            if (await _uow.Set<PcBaseline>().AnyAsync(b => b.ContractId == dto.ContractId && !b.Deleted && (b.Status == PcBaselineStatus.Draft || b.Status == PcBaselineStatus.ManagersSigned), cancellationToken))
                return Bad<PcBaselineDto>("یک خط مبنای در انتظار امضا یا تایید وجود دارد.");
            int max = await _uow.Set<PcBaseline>().Where(b => b.ContractId == dto.ContractId && !b.Deleted).Select(b => (int?)b.Number).MaxAsync(cancellationToken) ?? -1;
            var built = await BuildBaselineAsync(dto.ContractId, max + 1, dto.Reason ?? (max < 0 ? "خط مبنای اولیه" : "بازنگری"), cancellationToken);
            if (built.Error != null) return Bad<PcBaselineDto>(built.Error);
            _uow.Set<PcBaseline>().Add(built.Baseline);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(built.Baseline, cancellationToken));
        }

        public async Task<ResponseDto<PcBaselineDto>> BaselineActionAsync(PcBaselineActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcBaselineDto>(PcText.InvalidData);
            PcBaseline b = await _uow.Set<PcBaseline>().Include(x => x.Signatures).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (b == null) return PcText.NotFound<PcBaselineDto>("خط مبنا پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "sign":
                {
                    if (b.Status != PcBaselineStatus.Draft) return Bad<PcBaselineDto>("امضا فقط برای خط مبنای پیش‌نویس ممکن است.");
                    List<PcBaselineSignature> mine = b.Signatures.Where(s => s.SignedAt == null && _user.IsInRole(s.UnitRole)).ToList();
                    if (mine.Count == 0) return Bad<PcBaselineDto>("امضای باقی‌مانده‌ای برای نقش شما وجود ندارد.");
                    foreach (PcBaselineSignature s in mine) { s.UserId = Me; s.SignedAt = now; }
                    if (b.Signatures.All(s => s.SignedAt != null)) b.Status = PcBaselineStatus.ManagersSigned;
                    break;
                }
                case "approve":
                {
                    if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcBaselineDto>(PcText.NotAllowed + " (تایید با معاون PMO است)");
                    if (b.Status != PcBaselineStatus.ManagersSigned) return Bad<PcBaselineDto>("ابتدا همه مدیران واحدها باید برنامه را امضا کنند.");
                    PcContractTerms terms = await PcContext.TermsAsync(_uow, b.ContractId, true, cancellationToken);
                    if (terms == null) return Bad<PcBaselineDto>("شرایط قرارداد ثبت نشده است.");
                    foreach (PcBaseline old in await _uow.Set<PcBaseline>().Where(x => x.ContractId == b.ContractId && x.Status == PcBaselineStatus.Approved && x.Id != b.Id).ToListAsync(cancellationToken)) old.Status = PcBaselineStatus.Superseded;
                    b.Status = PcBaselineStatus.Approved; b.ApprovedAt = now; b.ApprovedByUserId = Me;
                    terms.CurrentBaselineId = b.Id;
                    await _project.MarkStepAsync(b.ContractId, PcInitiationStep.BaselineApproved, Me, false, Clean(dto.Note), cancellationToken);
                    break;
                }
                default:
                    return Bad<PcBaselineDto>("عملیات نامعتبر است.");
            }
            b.ModifyDate = now; b.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcBaselineDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(b, cancellationToken));
        }

        // ----------------------------------------------------------------------------- cost baseline lines (CBS)

        public async Task<ResponseDto<PagedList<PcCostLineDto>>> GetCostLinesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcCostBaselineLine> q = _uow.Set<PcCostBaselineLine>().AsNoTracking();
            if (query.ContractId.HasValue) q = q.Where(l => l.ContractId == query.ContractId.Value);
            IQueryable<PcCostLineDto> proj = q.OrderBy(l => l.Id).Select(l => new PcCostLineDto
                { Id = l.Id, ContractId = l.ContractId, WbsNodeId = l.WbsNodeId, ActivityId = l.ActivityId, CostClass = l.CostClass, ResourceClass = l.ResourceClass, Amount = l.Amount, Description = l.Description });
            return ResponseDtoHelper.Ok(await PagedList<PcCostLineDto>.CreateAsync(proj, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcCostLineDto>> SaveCostLineAsync(PcCostLineDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(PcCostClass), dto.CostClass)) return Bad<PcCostLineDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcCostLineDto>(PcText.NotAllowed);
            if (dto.Amount < 0) return Bad<PcCostLineDto>("مبلغ نمی‌تواند منفی باشد.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcCostLineDto>(PcText.ContractNotFound);
            PcCostBaselineLine l;
            if (dto.Id.HasValue)
            {
                l = await _uow.Set<PcCostBaselineLine>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, cancellationToken);
                if (l == null) return PcText.NotFound<PcCostLineDto>("ردیف پیدا نشد.");
            }
            else { l = new PcCostBaselineLine { ContractId = dto.ContractId }; _uow.Set<PcCostBaselineLine>().Add(l); }
            l.WbsNodeId = dto.WbsNodeId; l.ActivityId = dto.ActivityId; l.CostClass = dto.CostClass; l.ResourceClass = dto.ResourceClass; l.Amount = dto.Amount; l.Description = Clean(dto.Description);
            await _uow.SaveChangesAsync(cancellationToken);
            dto.Id = l.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<bool>> DeleteCostLineAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<bool>(PcText.NotAllowed);
            PcCostBaselineLine l = await _uow.Set<PcCostBaselineLine>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (l == null) return PcText.NotFound<bool>("ردیف پیدا نشد.");
            _uow.Set<PcCostBaselineLine>().Remove(l);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // ============================================================================ change control

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private async Task EnsureAuthoritiesAsync(CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcApprovalAuthority>().AnyAsync(cancellationToken)) return;
            _uow.Set<PcApprovalAuthority>().AddRange(new[]
            {
                new PcApprovalAuthority { Role = "PROJECTMANAGER", CanDecide = false, Note = "★ may only recommend" },
                new PcApprovalAuthority { Role = "PMO", CanDecide = true, MaxAmount = 5000000000m, MaxDelayDays = 30, Note = "★ PMO deputy: up to 5,000,000,000 rial and 30 days" },
                new PcApprovalAuthority { Role = "CEO", CanDecide = true, Note = "★ above the PMO limits" },
            });
            await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<List<PcAuthorityDto>>> GetAuthoritiesAsync(CancellationToken cancellationToken)
        {
            await EnsureAuthoritiesAsync(cancellationToken);
            List<PcAuthorityDto> list = await _uow.Set<PcApprovalAuthority>().AsNoTracking().OrderBy(a => a.Id).Select(a => new PcAuthorityDto
                { Id = a.Id, Role = a.Role, CanDecide = a.CanDecide, MaxAmount = a.MaxAmount, MaxDelayDays = a.MaxDelayDays, Note = a.Note }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<List<PcAuthorityDto>>> SaveAuthorityAsync(PcAuthorityDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Role) == null) return Bad<List<PcAuthorityDto>>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<List<PcAuthorityDto>>(PcText.NotAllowed);
            await EnsureAuthoritiesAsync(cancellationToken);
            PcApprovalAuthority a = dto.Id.HasValue ? await _uow.Set<PcApprovalAuthority>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, cancellationToken) : null;
            if (dto.Id.HasValue && a == null) return PcText.NotFound<List<PcAuthorityDto>>("ردیف پیدا نشد.");
            if (a == null) { a = new PcApprovalAuthority(); _uow.Set<PcApprovalAuthority>().Add(a); }
            a.Role = PcText.Upper(dto.Role); a.CanDecide = dto.CanDecide; a.MaxAmount = dto.MaxAmount; a.MaxDelayDays = dto.MaxDelayDays; a.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetAuthoritiesAsync(cancellationToken);
        }

        private async Task<PcChangeDto> ToDtoAsync(PcChangeRequest c, CancellationToken cancellationToken)
        {
            List<PcChangeBoardMember> board = c.Board != null ? c.Board.ToList() : await _uow.Set<PcChangeBoardMember>().AsNoTracking().Where(m => m.ChangeRequestId == c.Id).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, board.Select(m => m.UserId), cancellationToken);
            var dto = new PcChangeDto
            {
                Id = c.Id, Number = c.Number, ContractId = c.ContractId, Origin = c.Origin, Type = c.Type, RequesterUserId = c.RequesterUserId, ManagerAgreement = c.ManagerAgreement, Description = c.Description, Reason = c.Reason,
                Status = c.Status, RegisteredAt = c.RegisteredAt, DueDate = c.DueDate, Escalated = c.Escalated, RequestedPriceDelta = c.RequestedPriceDelta, RequestedDaysDelta = c.RequestedDaysDelta,
                ImpactDeltaDays = c.ImpactDeltaDays, ImpactDeltaCost = c.ImpactDeltaCost, ImpactOnCriticalPath = c.ImpactOnCriticalPath, AffectedPoLines = c.AffectedPoLines, AffectedItpPoints = c.AffectedItpPoints,
                AnalysedAt = c.AnalysedAt, ImpactScope = c.ImpactScope, ImpactResources = c.ImpactResources, ImpactQuality = c.ImpactQuality, ImpactRisk = c.ImpactRisk, ImpactProcurement = c.ImpactProcurement,
                ImpactClientContract = c.ImpactClientContract, Recommendation = c.Recommendation, Decision = c.Decision, DecisionNote = c.DecisionNote, DecidedByRole = c.DecidedByRole, DecidedAt = c.DecidedAt,
                BaselineId = c.BaselineId, RowVersion = c.RowVersion == null ? null : Convert.ToBase64String(c.RowVersion),
                Board = board.OrderBy(m => m.Id).Select(m => new PcChangeBoardDto
                    { Role = m.Role, UserId = m.UserId, UserName = m.UserId.HasValue && names.TryGetValue(m.UserId.Value, out string nm) ? nm : null, Vote = m.Vote, VotedAt = m.VotedAt, Comment = m.Comment }).ToList(),
            };
            try { if (!string.IsNullOrEmpty(c.ScenarioJson)) dto.Scenario = JsonSerializer.Deserialize<List<PcChangeScenarioItemDto>>(c.ScenarioJson, Json) ?? new List<PcChangeScenarioItemDto>(); } catch (JsonException) { }
            try { if (!string.IsNullOrEmpty(c.ImpactJson)) dto.ImpactActivities = JsonSerializer.Deserialize<List<PcImpactActivityDto>>(c.ImpactJson, Json) ?? new List<PcImpactActivityDto>(); } catch (JsonException) { }
            return dto;
        }

        public async Task<ResponseDto<PagedList<PcChangeDto>>> GetChangesAsync(PcChangeQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcChangeQuery();
            IQueryable<PcChangeRequest> q = _uow.Set<PcChangeRequest>().AsNoTracking().Include(c => c.Board).Where(c => !c.Deleted);
            if (query.ContractId.HasValue) q = q.Where(c => c.ContractId == query.ContractId.Value);
            if (query.Status.HasValue) q = q.Where(c => c.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Number.Contains(text) || c.Description.Contains(text));
            PagedList<PcChangeRequest> page = await PagedList<PcChangeRequest>.CreateAsync(q.OrderByDescending(c => c.Id).AsSplitQuery(), query.PageNumber, query.PageSize, cancellationToken);
            var dtos = new List<PcChangeDto>();
            foreach (PcChangeRequest c in page) dtos.Add(await ToDtoAsync(c, cancellationToken));
            return ResponseDtoHelper.Ok((PagedList<PcChangeDto>)new PcPage<PcChangeDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcChangeDto>> GetChangeAsync(int id, CancellationToken cancellationToken)
        {
            PcChangeRequest c = await _uow.Set<PcChangeRequest>().AsNoTracking().Include(x => x.Board).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return c == null ? PcText.NotFound<PcChangeDto>("درخواست تغییر پیدا نشد.") : ResponseDtoHelper.Ok(await ToDtoAsync(c, cancellationToken));
        }

        public async Task<ResponseDto<PcChangeDto>> SaveChangeAsync(PcChangeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Description) == null || Clean(dto.Reason) == null) return Bad<PcChangeDto>("شرح و دلیل تغییر را وارد کنید.");
            if (!_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcChangeDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcChangeOrigin), dto.Origin) || !Enum.IsDefined(typeof(PcChangeType), dto.Type)) return Bad<PcChangeDto>("منشأ و نوع تغییر را مشخص کنید.");
            if (dto.Origin == PcChangeOrigin.InternalRequest && Clean(dto.ManagerAgreement) == null) return Bad<PcChangeDto>("موافقت مدیر درخواست‌کننده لازم است.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcChangeDto>(PcText.ContractNotFound);
            List<PcChangeScenarioItemDto> scenario = dto.Scenario ?? new List<PcChangeScenarioItemDto>();
            List<int> ids = scenario.Select(s => s.ActivityId).ToList();
            if (ids.Distinct().Count() != ids.Count) return Bad<PcChangeDto>("فعالیت تکراری در سناریو است.");
            if (ids.Count > 0 && await _uow.Set<PcActivity>().CountAsync(a => ids.Contains(a.Id) && a.ContractId == dto.ContractId && !a.Deleted, cancellationToken) != ids.Count) return Bad<PcChangeDto>("فعالیتی متعلق به این قرارداد نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcChangeRequest c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<PcChangeRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (c == null) return PcText.NotFound<PcChangeDto>("درخواست تغییر پیدا نشد.");
                if (c.Status != PcChangeStatus.Draft) return Bad<PcChangeDto>("فقط پیش‌نویس قابل ویرایش است.");
            }
            else
            {
                c = new PcChangeRequest { ContractId = dto.ContractId, RequesterUserId = Me, Status = PcChangeStatus.Draft, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcChangeRequest>().Add(c);
            }
            c.Origin = dto.Origin; c.Type = dto.Type; c.ManagerAgreement = Clean(dto.ManagerAgreement); c.Description = Clean(dto.Description); c.Reason = Clean(dto.Reason);
            c.ScenarioJson = JsonSerializer.Serialize(scenario); c.RequestedPriceDelta = dto.RequestedPriceDelta; c.RequestedDaysDelta = dto.RequestedDaysDelta;
            c.ModifyDate = now; c.ModifierUserId = Me;
            if (c.Number == null) c.Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.ChangeRequest, "CR", DateTime.Today, cancellationToken);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcChangeDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(c, cancellationToken));
        }

        private static string RoleOfUnit(string unit) => string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().ToUpperInvariant();

        public async Task<ResponseDto<PcChangeDto>> ChangeActionAsync(PcChangeActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcChangeDto>(PcText.InvalidData);
            PcChangeRequest c = await _uow.Set<PcChangeRequest>().Include(x => x.Board).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcChangeDto>("درخواست تغییر پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            List<PcChangeScenarioItemDto> scenario = new List<PcChangeScenarioItemDto>();
            try { if (!string.IsNullOrEmpty(c.ScenarioJson)) scenario = JsonSerializer.Deserialize<List<PcChangeScenarioItemDto>>(c.ScenarioJson, Json) ?? scenario; } catch (JsonException) { }

            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "register":
                {
                    if (c.Status != PcChangeStatus.Draft) return Bad<PcChangeDto>("درخواست قبلاً ثبت شده است.");
                    if (c.RequesterUserId != Me && !_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcChangeDto>(PcText.NotAllowed);
                    Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
                    PcContractTerms terms = await PcContext.TermsAsync(_uow, c.ContractId, false, cancellationToken);
                    PcWorkCalendar cal = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
                    cal.SetOrigin(DateTime.Today);
                    c.DueDate = cal.AddWorkingDays(DateTime.Today, (int)settings[PcSettingKeys.ChangeAnalysisDays]);
                    c.RegisteredAt = now; c.Status = PcChangeStatus.Registered;
                    // default change board: PM, planning manager, the managers of the affected units, QC when quality is touched, commercial when the contract is touched
                    var roles = new List<string> { "PROJECTMANAGER", "PLANNING" };
                    List<int> actIds = scenario.Select(s => s.ActivityId).ToList();
                    var affected = await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).Select(a => new { a.ResponsibleUnit, a.ItpActivityId, a.SourceType }).ToListAsync(cancellationToken);
                    roles.AddRange(affected.Select(a => RoleOfUnit(a.ResponsibleUnit)).Where(r => r != null));
                    if (affected.Any(a => a.ItpActivityId.HasValue || a.SourceType == PcSourceType.ItpActivity)) roles.Add("QC");
                    if (c.Origin == PcChangeOrigin.ClientChangeOrder || c.RequestedPriceDelta != 0m || c.RequestedDaysDelta != 0) roles.Add("COMMERCIAL");
                    foreach (string r in roles.Distinct()) c.Board.Add(new PcChangeBoardMember { Role = r, Vote = PcVote.None });
                    break;
                }
                case "analyze":
                {
                    if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcChangeDto>(PcText.NotAllowed);
                    if (c.Status != PcChangeStatus.Registered && c.Status != PcChangeStatus.InAnalysis) return Bad<PcChangeDto>("تحلیل اثر فقط برای درخواست ثبت‌شده ممکن است.");
                    string err = await AnalyseAsync(c, scenario, cancellationToken);
                    if (err != null) return Bad<PcChangeDto>(err);
                    c.ImpactScope = Clean(dto.ImpactScope) ?? c.ImpactScope; c.ImpactResources = Clean(dto.ImpactResources) ?? c.ImpactResources; c.ImpactQuality = Clean(dto.ImpactQuality) ?? c.ImpactQuality;
                    c.ImpactRisk = Clean(dto.ImpactRisk) ?? c.ImpactRisk; c.ImpactProcurement = Clean(dto.ImpactProcurement) ?? c.ImpactProcurement; c.ImpactClientContract = Clean(dto.ImpactClientContract) ?? c.ImpactClientContract;
                    c.AnalysedAt = now; c.Status = PcChangeStatus.InAnalysis;
                    break;
                }
                case "recommend":
                {
                    if (!_user.IsInRole(ProjectControlRoles.ProjectManagers)) return Bad<PcChangeDto>(PcText.NotAllowed);
                    if (c.Status != PcChangeStatus.InAnalysis) return Bad<PcChangeDto>("ابتدا تحلیل اثر انجام شود.");
                    if (Clean(dto.Note) == null) return Bad<PcChangeDto>("متن پیشنهاد را بنویسید.");
                    c.Recommendation = Clean(dto.Note); c.RecommendedByUserId = Me;
                    break;
                }
                case "vote":
                {
                    if (c.Status != PcChangeStatus.InAnalysis) return Bad<PcChangeDto>("رای‌گیری پس از تحلیل اثر انجام می‌شود.");
                    if (dto.Vote != PcVote.Approve && dto.Vote != PcVote.Reject) return Bad<PcChangeDto>("رای باید موافق یا مخالف باشد.");
                    string wanted = PcText.Upper(dto.BoardRole);
                    PcChangeBoardMember m = c.Board.FirstOrDefault(x => x.Vote == PcVote.None && (wanted == null || x.Role == wanted) && _user.IsInRole(x.Role));
                    if (m == null) return Bad<PcChangeDto>("رای باقی‌مانده‌ای برای نقش شما در هیئت وجود ندارد.");
                    m.UserId = Me; m.Vote = dto.Vote; m.VotedAt = now; m.Comment = Clean(dto.Note);
                    break;
                }
                case "decide":
                {
                    string err = await DecideAsync(c, dto, scenario, now, cancellationToken);
                    if (err != null) return Bad<PcChangeDto>(err);
                    break;
                }
                case "close":
                {
                    if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcChangeDto>(PcText.NotAllowed);
                    if (c.Status != PcChangeStatus.Implementing) return Bad<PcChangeDto>("فقط تغییر در حال اجرا بسته می‌شود.");
                    c.Status = PcChangeStatus.Closed;
                    break;
                }
                default:
                    return Bad<PcChangeDto>("عملیات نامعتبر است.");
            }
            c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcChangeDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(c, cancellationToken));
        }

        /// <summary>Impact analysis: runs the pure CPM twice (current plan and plan with the scenario durations) and compares.</summary>
        private async Task<string> AnalyseAsync(PcChangeRequest c, List<PcChangeScenarioItemDto> scenario, CancellationToken cancellationToken)
        {
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == c.ContractId && !a.Deleted).ToListAsync(cancellationToken);
            if (acts.Count == 0) return "فعالیتی برای تحلیل وجود ندارد.";
            Dictionary<int, PcActivity> byId = acts.ToDictionary(a => a.Id);
            if (scenario.Any(s => !byId.ContainsKey(s.ActivityId))) return "فعالیت سناریو پیدا نشد.";
            CpmResult before = await _schedule.PreviewPlanAsync(c.ContractId, null, cancellationToken);
            if (before == null) return "محاسبه برنامه ممکن نیست (حلقه یا نبود تاریخ شروع).";
            var overrides = scenario.ToDictionary(s => s.ActivityId, s => Math.Max(0, byId[s.ActivityId].DurationDays + s.DurationDelta));
            CpmResult after = await _schedule.PreviewPlanAsync(c.ContractId, overrides, cancellationToken);
            if (after == null) return "محاسبه برنامه ممکن نیست.";
            PcContractTerms terms = await PcContext.TermsAsync(_uow, c.ContractId, false, cancellationToken);
            PcWorkCalendar cal = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
            cal.SetOrigin(before.ProjectStart);
            c.ImpactDeltaDays = after.ProjectFinishOffset - before.ProjectFinishOffset;
            c.ImpactDeltaCost = scenario.Sum(s => s.BudgetDelta);
            c.ImpactOnCriticalPath = scenario.Any(s => before.Items.TryGetValue(s.ActivityId, out CpmItem it) && it.Critical);
            c.AffectedPoLines = scenario.Count(s => byId[s.ActivityId].PurchaseOrderLineId.HasValue || byId[s.ActivityId].PurchaseRequestLineId.HasValue);
            c.AffectedItpPoints = scenario.Count(s => byId[s.ActivityId].ItpActivityId.HasValue || byId[s.ActivityId].SourceType == PcSourceType.ItpActivity);
            var impact = new List<PcImpactActivityDto>();
            foreach (PcActivity a in acts.OrderBy(a => a.Id))
            {
                if (!before.Items.TryGetValue(a.Id, out CpmItem b) || !after.Items.TryGetValue(a.Id, out CpmItem n)) continue;
                if (b.EarlyFinish != n.EarlyFinish) impact.Add(new PcImpactActivityDto { ActivityId = a.Id, Code = a.Code, OldFinish = b.EarlyFinish, NewFinish = n.EarlyFinish, Critical = n.Critical });
            }
            c.ImpactJson = JsonSerializer.Serialize(impact);
            return null;
        }

        private async Task<string> DecideAsync(PcChangeRequest c, PcChangeActionDto dto, List<PcChangeScenarioItemDto> scenario, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (c.Status != PcChangeStatus.InAnalysis) return "تصمیم‌گیری پس از تحلیل اثر انجام می‌شود.";
            if (dto.Decision == PcChangeDecision.None || !Enum.IsDefined(typeof(PcChangeDecision), dto.Decision)) return "تصمیم را مشخص کنید.";
            if (c.Recommendation == null) return "ابتدا مدیر پروژه پیشنهاد خود را ثبت کند.";
            await EnsureAuthoritiesAsync(cancellationToken);
            decimal amount = Math.Abs(c.ImpactDeltaCost ?? 0m);
            int delay = Math.Abs(c.ImpactDeltaDays ?? 0);
            List<PcApprovalAuthority> authorities = await _uow.Set<PcApprovalAuthority>().AsNoTracking().Where(a => a.CanDecide).ToListAsync(cancellationToken);
            PcApprovalAuthority mine = authorities.FirstOrDefault(a => _user.IsInRole(a.Role) && (a.MaxAmount == null || amount <= a.MaxAmount) && (a.MaxDelayDays == null || delay <= a.MaxDelayDays));
            if (mine == null)
            {
                c.Escalated = true;
                return authorities.Any(a => _user.IsInRole(a.Role)) ? "مبلغ یا تاخیر از حد اختیار شما بیشتر است؛ تصمیم با مدیرعامل است." : PcText.NotAllowed;
            }
            if (Clean(dto.Note) == null) return "دلیل تصمیم را بنویسید.";
            if (dto.Decision == PcChangeDecision.Approve)
            {
                if (c.Board.Any(m => m.Vote == PcVote.None)) return "همه اعضای هیئت باید رای دهند.";
                if (c.Board.Any(m => m.Vote == PcVote.Reject) && !_user.IsInRole(ProjectControlRoles.Ceo)) return "عضو هیئت مخالفت کرده است؛ تایید فقط با مدیرعامل ممکن است.";
            }
            c.Decision = dto.Decision; c.DecisionNote = Clean(dto.Note); c.DecidedByUserId = Me; c.DecidedByRole = mine.Role; c.DecidedAt = now;
            switch (dto.Decision)
            {
                case PcChangeDecision.Reject: c.Status = PcChangeStatus.Rejected; return null;
                case PcChangeDecision.Defer: case PcChangeDecision.MoreInfo: c.Status = PcChangeStatus.Registered; c.Decision = dto.Decision; return null;
            }

            // approved: apply the scenario to the activities, recompute the plan and issue the next baseline (approved at once, the board is the approval)
            List<PcActivity> acts = await _uow.Set<PcActivity>().Where(a => a.ContractId == c.ContractId && !a.Deleted).ToListAsync(cancellationToken);
            Dictionary<int, PcActivity> byId = acts.ToDictionary(a => a.Id);
            foreach (PcChangeScenarioItemDto s in scenario)
            {
                if (!byId.TryGetValue(s.ActivityId, out PcActivity a)) return "فعالیت سناریو پیدا نشد.";
                a.DurationDays = Math.Max(0, a.DurationDays + s.DurationDelta); a.BudgetAtCompletion += s.BudgetDelta; a.ModifyDate = now; a.ModifierUserId = Me;
            }
            CpmResult result = await _schedule.ApplyPlanNoSaveAsync(c.ContractId, cancellationToken);
            if (result == null) return "محاسبه برنامه جدید ممکن نیست.";
            int max = await _uow.Set<PcBaseline>().Where(b => b.ContractId == c.ContractId && !b.Deleted).Select(b => (int?)b.Number).MaxAsync(cancellationToken) ?? -1;
            var built = await BuildBaselineAsync(c.ContractId, max + 1, "تغییر " + c.Number, cancellationToken);
            if (built.Error != null) return built.Error;
            PcBaseline nb = built.Baseline;
            // the tracked activities hold the new dates, the builder read the old ones from the database: rebuild the rows from the tracked state
            nb.Activities.Clear();
            foreach (PcActivity a in acts)
                nb.Activities.Add(new PcBaselineActivity
                {
                    ActivityId = a.Id, Code = a.Code, Title = a.Title, WbsNodeId = a.WbsNodeId, Phase = a.Phase, Start = a.EarlyStart.Value.Date, Finish = a.EarlyFinish.Value.Date, DurationDays = a.DurationDays,
                    Weight = a.Weight, Budget = a.BudgetAtCompletion, ProgressMethod = a.ProgressMethod, CalendarId = a.CalendarId, ResponsibleUnit = a.ResponsibleUnit,
                });
            nb.ProjectStart = acts.Min(a => a.EarlyStart.Value).Date; nb.ProjectFinish = acts.Max(a => a.EarlyFinish.Value).Date; nb.Bac = acts.Sum(a => a.BudgetAtCompletion);
            nb.Signatures.Clear();
            nb.Status = PcBaselineStatus.Approved; nb.ApprovedAt = now; nb.ApprovedByUserId = Me; nb.ChangeRequestId = c.Id;
            foreach (PcBaseline old in await _uow.Set<PcBaseline>().Where(x => x.ContractId == c.ContractId && x.Status == PcBaselineStatus.Approved).ToListAsync(cancellationToken)) old.Status = PcBaselineStatus.Superseded;
            _uow.Set<PcBaseline>().Add(nb);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, c.ContractId, true, cancellationToken);
            c.Status = PcChangeStatus.Implementing;
            c.Baseline = nb;
            if (terms != null) terms.CurrentBaseline = nb;
            return null;
        }

        public async Task<List<int>> OverdueChangeContractIdsAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            return await _uow.Set<PcChangeRequest>().AsNoTracking().Where(c => !c.Deleted && c.DueDate != null && c.DueDate < today && (c.Status == PcChangeStatus.Registered || c.Status == PcChangeStatus.InAnalysis)).Select(c => c.ContractId).Distinct().ToListAsync(cancellationToken);
        }

        // ============================================================================ claims

        private static PcClaimDto ToDto(PcClaim c) => new PcClaimDto
        {
            Id = c.Id, Number = c.Number, ContractId = c.ContractId, Direction = c.Direction, Issue = c.Issue, Basis = c.Basis, DaysQuantified = c.DaysQuantified, AmountQuantified = c.AmountQuantified, Status = c.Status,
            IsCrossClaim = c.IsCrossClaim, PmoApproved = c.PmoApproved, DesignManufacturingApproved = c.DesignManufacturingApproved, ReviewNote = c.ReviewNote, RowVersion = c.RowVersion == null ? null : Convert.ToBase64String(c.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcClaimDto>>> GetClaimsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcClaim> q = _uow.Set<PcClaim>().AsNoTracking().Where(c => !c.Deleted);
            if (query.ContractId.HasValue) q = q.Where(c => c.ContractId == query.ContractId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Number.Contains(text) || c.Issue.Contains(text));
            PagedList<PcClaim> page = await PagedList<PcClaim>.CreateAsync(q.OrderByDescending(c => c.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcClaimDto> dtos = page.Select(ToDto).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcClaimDto>)new PcPage<PcClaimDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcClaimDto>> SaveClaimAsync(PcClaimDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Issue) == null) return Bad<PcClaimDto>("موضوع ادعا را وارد کنید.");
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",COMMERCIAL")) return Bad<PcClaimDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcClaimDirection), dto.Direction)) return Bad<PcClaimDto>("جهت ادعا را مشخص کنید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcClaimDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcClaim c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<PcClaim>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (c == null) return PcText.NotFound<PcClaimDto>("ادعا پیدا نشد.");
                if (c.Status == PcClaimStatus.Settled || c.Status == PcClaimStatus.Withdrawn) return Bad<PcClaimDto>("ادعای بسته‌شده ویرایش نمی‌شود.");
            }
            else
            {
                c = new PcClaim { ContractId = dto.ContractId, Status = PcClaimStatus.Issue, CreateDate = now, CreatorUserId = Me, Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Claim, "CL", DateTime.Today, cancellationToken) };
                _uow.Set<PcClaim>().Add(c);
            }
            c.Direction = dto.Direction; c.Issue = Clean(dto.Issue); c.Basis = Clean(dto.Basis); c.IsCrossClaim = dto.IsCrossClaim; c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcClaimDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(c));
        }

        public async Task<ResponseDto<PcClaimDto>> ClaimActionAsync(PcClaimActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcClaimDto>(PcText.InvalidData);
            PcClaim c = await _uow.Set<PcClaim>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcClaimDto>("ادعا پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "quantify":
                    if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",COMMERCIAL")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status != PcClaimStatus.Issue && c.Status != PcClaimStatus.Quantified) return Bad<PcClaimDto>("برآورد فقط برای ادعای تازه ممکن است.");
                    if ((dto.Days ?? 0) <= 0 && (dto.Amount ?? 0m) <= 0m) return Bad<PcClaimDto>("زمان یا مبلغ برآورد را وارد کنید.");
                    c.DaysQuantified = dto.Days; c.AmountQuantified = dto.Amount; c.Status = PcClaimStatus.Quantified;
                    break;
                case "approve-pmo":
                    if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcClaimDto>(PcText.NotAllowed);
                    c.PmoApproved = true; c.ReviewedByUserId = Me; c.ReviewedAt = now; c.ReviewNote = Clean(dto.Note) ?? c.ReviewNote;
                    break;
                case "approve-design":
                    if (!_user.IsInRole("ENGINEERING,CONSTRUCTION,PMO")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    c.DesignManufacturingApproved = true; c.ReviewedByUserId = Me; c.ReviewedAt = now; c.ReviewNote = Clean(dto.Note) ?? c.ReviewNote;
                    break;
                case "notify":
                    if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",COMMERCIAL")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status != PcClaimStatus.Quantified) return Bad<PcClaimDto>("ابتدا ادعا برآورد شود.");
                    if (!c.PmoApproved || !c.DesignManufacturingApproved) return Bad<PcClaimDto>("ابلاغ ادعا نیازمند تایید PMO و واحد طراحی/ساخت است.");
                    c.Status = PcClaimStatus.Notified;
                    break;
                case "negotiate":
                    if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",COMMERCIAL")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status != PcClaimStatus.Notified) return Bad<PcClaimDto>("ادعا هنوز ابلاغ نشده است.");
                    c.Status = PcClaimStatus.Negotiating;
                    break;
                case "settle":
                    if (!_user.IsInRole(ProjectControlRoles.Commercial + ",CEO")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status != PcClaimStatus.Negotiating && c.Status != PcClaimStatus.Notified) return Bad<PcClaimDto>("ادعا در مرحله تسویه نیست.");
                    c.Status = PcClaimStatus.Settled; c.ReviewNote = Clean(dto.Note) ?? c.ReviewNote;
                    break;
                case "withdraw":
                    if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",COMMERCIAL,CEO")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status == PcClaimStatus.Settled) return Bad<PcClaimDto>("ادعای تسویه‌شده پس گرفته نمی‌شود.");
                    c.Status = PcClaimStatus.Withdrawn;
                    break;
                case "litigate":
                    if (!_user.IsInRole("CEO")) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (c.Status != PcClaimStatus.Negotiating) return Bad<PcClaimDto>("ارجاع به مراجع قضایی پس از مذاکره ممکن است.");
                    c.Status = PcClaimStatus.Litigation;
                    break;
                default:
                    return Bad<PcClaimDto>("عملیات نامعتبر است.");
            }
            c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcClaimDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(c));
        }
    }
}
