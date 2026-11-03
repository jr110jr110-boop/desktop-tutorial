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
        Task<ResponseDto<List<PcBaselineActivityDto>>> GetBaselineActivitiesAsync(int baselineId, CancellationToken cancellationToken);
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

        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public PcBaselineService(IUnitOfWork uow, IInventoryUserContext user, IPcScheduleService schedule, IPcProjectService project, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _notify = notify;
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

        /// <summary>R1c (M3): the frozen rows of a baseline (the dates and values of the approved plan), for the grey bar of the Gantt chart.</summary>
        public async Task<ResponseDto<List<PcBaselineActivityDto>>> GetBaselineActivitiesAsync(int baselineId, CancellationToken cancellationToken)
        {
            if (!await _uow.Set<PcBaseline>().AsNoTracking().AnyAsync(b => b.Id == baselineId && !b.Deleted, cancellationToken)) return PcText.NotFound<List<PcBaselineActivityDto>>("خط مبنا پیدا نشد.");
            List<PcBaselineActivityDto> rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == baselineId).OrderBy(r => r.Start).ThenBy(r => r.Code)
                .Select(r => new PcBaselineActivityDto
                {
                    ActivityId = r.ActivityId, Code = r.Code, Title = r.Title, WbsNodeId = r.WbsNodeId, Phase = r.Phase, Start = r.Start, Finish = r.Finish, DurationDays = r.DurationDays,
                    Weight = r.Weight, EffectiveWeight = r.EffectiveWeight, Budget = r.Budget, ProgressMethod = r.ProgressMethod, CalendarId = r.CalendarId, ResponsibleUnit = r.ResponsibleUnit,
                }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(rows);
        }

        /// <summary>The activities of a contract (not tracked) and the share of each in the whole project, as the tree and the weights give it today.</summary>
        private async Task<(List<PcActivity> Activities, Dictionary<int, decimal> Weights)> LiveAsync(int contractId, CancellationToken cancellationToken)
        {
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
            List<PcWbsNode> nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == contractId && !n.Deleted).ToListAsync(cancellationToken);
            return (acts, PcProgressService.EffectiveWeights(nodes, acts));
        }

        /// <summary>
        /// Null when the dates saved on the activities are the result of the plan run of today (no activity, dependency, duration or calendar was changed after the last critical path run);
        /// a baseline freezes dates, so they must be the dates the network gives.
        /// </summary>
        private async Task<string> PlanRunProblemAsync(int contractId, IList<PcActivity> acts, CancellationToken cancellationToken)
        {
            CpmResult run = await _schedule.PreviewPlanAsync(contractId, null, cancellationToken);
            if (run == null) return "محاسبه برنامه ممکن نیست (حلقه در شبکه یا نبود تاریخ شروع).";
            List<string> old = acts.Where(a => !run.Items.TryGetValue(a.Id, out CpmItem item) || a.EarlyStart?.Date != item.EarlyStart.Date || a.EarlyFinish?.Date != item.EarlyFinish.Date).Select(a => a.Code).ToList();
            return old.Count == 0 ? null : "تاریخ‌های برنامه با شبکه فعلی فعالیت‌ها هم‌خوان نیست؛ محاسبه مسیر بحرانی را دوباره اجرا کنید (" + string.Join("، ", old.Take(10)) + (old.Count > 10 ? " و ..." : string.Empty) + ").";
        }

        /// <summary>Builds a baseline object (nothing saved) from the activities with their plan dates. Signature slots: one per responsible unit.</summary>
        private PcBaseline NewBaseline(int contractId, int number, string reason, IList<PcActivity> acts, IDictionary<int, decimal> weights, PcContractTerms terms, DateTimeOffset now)
        {
            var b = new PcBaseline
            {
                ContractId = contractId, Number = number, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Draft, Reason = Clean(reason),
                ProjectStart = acts.Min(a => a.EarlyStart.Value).Date, ProjectFinish = acts.Max(a => a.EarlyFinish.Value).Date,
                Bac = acts.Sum(a => a.BudgetAtCompletion), ManagementReserve = terms?.ManagementReserve ?? 0m,
                CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me, Signatures = new List<PcBaselineSignature>(), Activities = new List<PcBaselineActivity>(),
            };
            foreach (PcActivity a in acts.OrderBy(a => a.Id))
                b.Activities.Add(PcBaselineRows.RowOf(a, weights != null && weights.TryGetValue(a.Id, out decimal w) ? w : (decimal?)null));
            foreach (string unit in acts.Select(a => a.ResponsibleUnit).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim().ToUpperInvariant()).Distinct().OrderBy(u => u))
                b.Signatures.Add(new PcBaselineSignature { UnitRole = unit });
            return b;
        }

        /// <summary>Builds a baseline from the plan of today (tracked, nothing saved). The plan must be calculated, every activity needs a budget or a weight and the total budget must be above zero.</summary>
        private async Task<(PcBaseline Baseline, string Error)> BuildBaselineAsync(int contractId, int number, string reason, CancellationToken cancellationToken)
        {
            var live = await LiveAsync(contractId, cancellationToken);
            if (live.Activities.Count == 0) return (null, "فعالیتی برای ثبت خط مبنا وجود ندارد.");
            if (live.Activities.Any(a => !a.EarlyStart.HasValue || !a.EarlyFinish.HasValue)) return (null, "ابتدا محاسبه مسیر بحرانی را اجرا کنید؛ همه فعالیت‌ها باید تاریخ برنامه داشته باشند.");
            string problem = PcBaselineRows.CreationProblem(live.Activities) ?? await PlanRunProblemAsync(contractId, live.Activities, cancellationToken);
            if (problem != null) return (null, problem);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            return (NewBaseline(contractId, number, reason, live.Activities, live.Weights, terms, DateTimeOffset.Now), null);
        }

        /// <summary>The codes of the activities in which a draft baseline differs from the plan of today (including activities added or removed since); empty when the draft is still the plan.</summary>
        private async Task<List<string>> DraftDifferencesAsync(PcBaseline b, CancellationToken cancellationToken)
        {
            var live = await LiveAsync(b.ContractId, cancellationToken);
            List<PcBaselineActivity> rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == b.Id).ToListAsync(cancellationToken);
            return PcBaselineRows.Differences(rows, live.Activities, live.Weights);
        }

        private async Task<string> StaleDraftProblemAsync(PcBaseline b, CancellationToken cancellationToken)
        {
            List<string> diff = await DraftDifferencesAsync(b, cancellationToken);
            return diff.Count == 0 ? null : "برنامه پس از ساخت این خط مبنا تغییر کرده است (" + string.Join("، ", diff.Distinct().Take(10)) + ")؛ خط مبنای تازه بسازید.";
        }

        public async Task<ResponseDto<PcBaselineDto>> CreateBaselineAsync(PcBaselineCreateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcBaselineDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcBaselineDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcBaselineDto>(PcText.ContractNotFound);
            List<PcBaseline> existing = await _uow.Set<PcBaseline>().Where(b => b.ContractId == dto.ContractId && !b.Deleted).ToListAsync(cancellationToken);
            // R1c (H1): this is the way to the original baseline only; every later baseline is the result of an approved change request (PMBOK 7 integrated change control)
            if (existing.Any(b => b.Status == PcBaselineStatus.Approved || b.Status == PcBaselineStatus.Superseded))
                return Bad<PcBaselineDto>("خط مبنای اولیه این قرارداد ساخته شده است؛ خط مبنای بعدی فقط با تایید «درخواست تغییر» ساخته می‌شود.");
            // R1c (H8): no baseline before the contract review is accepted (and not while a re-review after an amendment or variation order is waiting)
            string gate = await PcGates.RequireBaselineReviewAsync(_uow, dto.ContractId, cancellationToken);
            if (gate != null) return Bad<PcBaselineDto>(gate);
            PcBaseline pending = existing.FirstOrDefault(b => b.Status == PcBaselineStatus.Draft || b.Status == PcBaselineStatus.ManagersSigned);
            if (pending != null && (await DraftDifferencesAsync(pending, cancellationToken)).Count == 0)
                return Bad<PcBaselineDto>("یک خط مبنای در انتظار امضا یا تایید وجود دارد.");
            var built = await BuildBaselineAsync(dto.ContractId, 0, dto.Reason ?? "خط مبنای اولیه", cancellationToken);
            if (built.Error != null) return Bad<PcBaselineDto>(built.Error);
            if (pending != null)
            {
                // the plan changed after that draft was made: the draft is withdrawn (its signatures would stand under a plan nobody sees) and the new one takes its number
                pending.Delete(); pending.ModifyDate = DateTimeOffset.Now; pending.ModifierUserId = Me;
                await _uow.SaveChangesAsync(cancellationToken);
            }
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
                    string stale = await StaleDraftProblemAsync(b, cancellationToken);
                    if (stale != null) return Bad<PcBaselineDto>(stale);
                    foreach (PcBaselineSignature s in mine) { s.UserId = Me; s.SignedAt = now; }
                    if (b.Signatures.All(s => s.SignedAt != null)) b.Status = PcBaselineStatus.ManagersSigned;
                    break;
                }
                case "approve":
                {
                    if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcBaselineDto>(PcText.NotAllowed + " (تایید با معاون PMO است)");
                    if (b.Status != PcBaselineStatus.ManagersSigned) return Bad<PcBaselineDto>("ابتدا همه مدیران واحدها باید برنامه را امضا کنند.");
                    // R1c (M1): the approver is neither the person who built the baseline nor one of the managers who signed it
                    if (b.CreatorUserId == Me) return Bad<PcBaselineDto>(PcText.SameUser + " (سازنده خط مبنا نمی‌تواند آن را تایید کند)");
                    if (b.Signatures.Any(s => s.UserId == Me)) return Bad<PcBaselineDto>(PcText.SameUser + " (امضاکننده برنامه نمی‌تواند تاییدکننده خط مبنا باشد)");
                    PcContractTerms terms = await PcContext.TermsAsync(_uow, b.ContractId, true, cancellationToken);
                    if (terms == null) return Bad<PcBaselineDto>("شرایط قرارداد ثبت نشده است.");
                    // R1c (H1): a baseline after the original one exists only as the result of an approved change request
                    if (b.ChangeRequestId == null && (b.Number > 0 || (terms.CurrentBaselineId != null && terms.CurrentBaselineId != b.Id)))
                        return Bad<PcBaselineDto>("خط مبنای جاری وجود دارد؛ خط مبنای بعدی فقط با تایید «درخواست تغییر» ساخته می‌شود.");
                    string gate = await PcGates.RequireBaselineReviewAsync(_uow, b.ContractId, cancellationToken);
                    if (gate != null) return Bad<PcBaselineDto>(gate);
                    string stale = await StaleDraftProblemAsync(b, cancellationToken);
                    if (stale != null) return Bad<PcBaselineDto>(stale);
                    var live = await LiveAsync(b.ContractId, cancellationToken);
                    string planProblem = await PlanRunProblemAsync(b.ContractId, live.Activities, cancellationToken);
                    if (planProblem != null) return Bad<PcBaselineDto>(planProblem);
                    List<PcBaselineActivity> rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == b.Id).ToListAsync(cancellationToken);
                    string rowsProblem = PcBaselineRows.RowsProblem(rows);
                    if (rowsProblem != null) return Bad<PcBaselineDto>(rowsProblem);
                    // R1c (M4): the planned-value curve is frozen with the baseline (with the calendars of today); later holiday changes do not move it
                    if (!await _uow.Set<PcBaselineCostPeriod>().AnyAsync(p => p.BaselineId == b.Id, cancellationToken))
                        _uow.Set<PcBaselineCostPeriod>().AddRange(await PcFrozenPlan.PeriodsAsync(_uow, b.Id, terms, rows, cancellationToken));
                    foreach (PcBaseline old in await _uow.Set<PcBaseline>().Where(x => x.ContractId == b.ContractId && x.Status == PcBaselineStatus.Approved && x.Id != b.Id).ToListAsync(cancellationToken)) old.Status = PcBaselineStatus.Superseded;
                    // the cost lines (CBS) entered so far are part of the approved cost baseline
                    foreach (PcCostBaselineLine line in await _uow.Set<PcCostBaselineLine>().Where(l => l.ContractId == b.ContractId && l.BaselineId == null).ToListAsync(cancellationToken)) line.BaselineId = b.Id;
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
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "baseline:" + b.Id, b.Status == PcBaselineStatus.ManagersSigned, "project-control", "baseline", b.Id,
                "تایید خط مبنا (شماره " + b.Number + ")", "/project-control/baselines?id=" + b.Id, ProjectControlRoles.Pmo, cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(b, cancellationToken));
        }

        // ----------------------------------------------------------------------------- cost baseline lines (CBS)

        public async Task<ResponseDto<PagedList<PcCostLineDto>>> GetCostLinesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcCostBaselineLine> q = _uow.Set<PcCostBaselineLine>().AsNoTracking();
            if (query.ContractId.HasValue) q = q.Where(l => l.ContractId == query.ContractId.Value);
            IQueryable<PcCostLineDto> proj = q.OrderBy(l => l.Id).Select(l => new PcCostLineDto
                { Id = l.Id, ContractId = l.ContractId, WbsNodeId = l.WbsNodeId, ActivityId = l.ActivityId, CostClass = l.CostClass, ResourceClass = l.ResourceClass, Amount = l.Amount, Description = l.Description, BaselineId = l.BaselineId });
            return ResponseDtoHelper.Ok(await PagedList<PcCostLineDto>.CreateAsync(proj, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcCostLineDto>> SaveCostLineAsync(PcCostLineDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(PcCostClass), dto.CostClass)) return Bad<PcCostLineDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcCostLineDto>(PcText.NotAllowed);
            if (dto.Amount < 0) return Bad<PcCostLineDto>("مبلغ نمی‌تواند منفی باشد.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcCostLineDto>(PcText.ContractNotFound);
            // R1c (H1, M4): the cost lines are the breakdown of the cost baseline; once the baseline is approved they change only through a change request
            if (await PcPlanLock.IsLockedAsync(_uow, dto.ContractId, cancellationToken)) return Bad<PcCostLineDto>(PcPlanLock.Refusal);
            PcCostBaselineLine l;
            if (dto.Id.HasValue)
            {
                l = await _uow.Set<PcCostBaselineLine>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, cancellationToken);
                if (l == null) return PcText.NotFound<PcCostLineDto>("ردیف پیدا نشد.");
                if (l.ContractId != dto.ContractId) return Bad<PcCostLineDto>(PcText.InvalidData);
            }
            else { l = new PcCostBaselineLine { ContractId = dto.ContractId }; _uow.Set<PcCostBaselineLine>().Add(l); }
            l.WbsNodeId = dto.WbsNodeId; l.ActivityId = dto.ActivityId; l.CostClass = dto.CostClass; l.ResourceClass = dto.ResourceClass; l.Amount = dto.Amount; l.Description = Clean(dto.Description);
            await _uow.SaveChangesAsync(cancellationToken);
            dto.Id = l.Id; dto.BaselineId = l.BaselineId;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<bool>> DeleteCostLineAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<bool>(PcText.NotAllowed);
            PcCostBaselineLine l = await _uow.Set<PcCostBaselineLine>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (l == null) return PcText.NotFound<bool>("ردیف پیدا نشد.");
            if (await PcPlanLock.IsLockedAsync(_uow, l.ContractId, cancellationToken)) return Bad<bool>(PcPlanLock.Refusal);
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
            foreach (PcChangeScenarioItemDto item in scenario)
            {
                item.Code = Clean(item.Code); item.Title = Clean(item.Title); item.ResponsibleUnit = PcText.Upper(item.ResponsibleUnit);
                foreach (PcScenarioLinkDto link in item.Links ?? new List<PcScenarioLinkDto>()) link.OtherCode = Clean(link.OtherCode);
            }
            if (scenario.Count > 0)
            {
                // R1c (H1): the scenario is the only way to change the approved plan; before the original baseline is approved the plan is edited directly
                if (!await PcPlanLock.IsLockedAsync(_uow, dto.ContractId, cancellationToken))
                    return Bad<PcChangeDto>("سناریوی تغییر برای اصلاح خط مبنای تاییدشده است؛ تا پیش از تایید خط مبنای اولیه برنامه را مستقیم ویرایش کنید.");
                string scenarioProblem = await _schedule.ValidateScenarioAsync(dto.ContractId, scenario, cancellationToken);
                if (scenarioProblem != null) return Bad<PcChangeDto>(scenarioProblem);
            }
            DateTimeOffset now = DateTimeOffset.Now;
            PcChangeRequest c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<PcChangeRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (c == null) return PcText.NotFound<PcChangeDto>("درخواست تغییر پیدا نشد.");
                if (c.Status != PcChangeStatus.Draft) return Bad<PcChangeDto>("فقط پیش‌نویس قابل ویرایش است.");
                if (c.ContractId != dto.ContractId) return Bad<PcChangeDto>(PcText.InvalidData);
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
                    string scenarioProblem = await _schedule.ValidateScenarioAsync(c.ContractId, scenario, cancellationToken);
                    if (scenarioProblem != null) return Bad<PcChangeDto>(scenarioProblem);
                    Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
                    PcContractTerms terms = await PcContext.TermsAsync(_uow, c.ContractId, false, cancellationToken);
                    PcWorkCalendar cal = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
                    cal.SetOrigin(DateTime.Today);
                    c.DueDate = cal.AddWorkingDays(DateTime.Today, (int)settings[PcSettingKeys.ChangeAnalysisDays]);
                    c.RegisteredAt = now; c.Status = PcChangeStatus.Registered;
                    // default change board: PM, planning manager, the managers of the affected units, QC when quality is touched, commercial when the contract is touched
                    var roles = new List<string> { "PROJECTMANAGER", "PLANNING" };
                    Dictionary<int, PcActivity> byId = (await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == c.ContractId && !a.Deleted).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);
                    roles.AddRange(PcScenario.UnitsOf(scenario, byId));
                    if (scenario.Any(s => s.Operation != PcScenarioOperation.Add && byId.TryGetValue(s.ActivityId, out PcActivity touched) && (touched.ItpActivityId.HasValue || touched.SourceType == PcSourceType.ItpActivity))) roles.Add("QC");
                    if (c.Origin == PcChangeOrigin.ClientChangeOrder || c.RequestedPriceDelta != 0m || c.RequestedDaysDelta != 0 || PcScenario.CostDelta(scenario, byId) != 0m) roles.Add("COMMERCIAL");
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
            {
                // office inbox: the request waits for the analysis, then for the votes of the board, then for the decision
                bool open = c.Status == PcChangeStatus.Registered || c.Status == PcChangeStatus.InAnalysis;
                string waitingRoles = c.Status == PcChangeStatus.Registered ? ProjectControlRoles.PlanningOrPm
                    : c.Board.Any(m => m.Vote == PcVote.None) ? string.Join(",", c.Board.Where(m => m.Vote == PcVote.None).Select(m => m.Role).Distinct()) : ProjectControlRoles.Pmo + ",CEO";
                await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "cr:" + c.Id, open, "project-control", "change-request", c.Id, "درخواست تغییر " + c.Number,
                    "/project-control/changes?id=" + c.Id, waitingRoles, cancellationToken, c.DueDate);
            }
            return ResponseDtoHelper.Ok(await ToDtoAsync(c, cancellationToken));
        }

        /// <summary>Impact analysis: runs the pure CPM twice (the plan today and the plan with the scenario: duration changes, activities added and removed) and compares.</summary>
        private async Task<string> AnalyseAsync(PcChangeRequest c, List<PcChangeScenarioItemDto> scenario, CancellationToken cancellationToken)
        {
            PcScenarioPreview preview = await _schedule.PreviewScenarioAsync(c.ContractId, scenario, cancellationToken);
            if (preview.Error != null) return preview.Error;
            CpmResult before = preview.Before, after = preview.After;
            Dictionary<int, PcActivity> byId = preview.Activities;
            var existing = scenario.Where(s => s.Operation != PcScenarioOperation.Add).ToList();
            c.ImpactDeltaDays = after.ProjectFinishOffset - before.ProjectFinishOffset;
            c.ImpactDeltaCost = PcScenario.CostDelta(scenario, byId);
            c.ImpactOnCriticalPath = existing.Any(s => before.Items.TryGetValue(s.ActivityId, out CpmItem it) && it.Critical) || after.Items.Any(kv => kv.Key < 0 && kv.Value.Critical);
            c.AffectedPoLines = existing.Count(s => byId[s.ActivityId].PurchaseOrderLineId.HasValue || byId[s.ActivityId].PurchaseRequestLineId.HasValue);
            c.AffectedItpPoints = existing.Count(s => byId[s.ActivityId].ItpActivityId.HasValue || byId[s.ActivityId].SourceType == PcSourceType.ItpActivity);
            var impact = new List<PcImpactActivityDto>();
            foreach (PcActivity a in byId.Values.OrderBy(a => a.Id))
            {
                bool had = before.Items.TryGetValue(a.Id, out CpmItem b), has = after.Items.TryGetValue(a.Id, out CpmItem n);
                if (had && has && b.EarlyFinish != n.EarlyFinish) impact.Add(new PcImpactActivityDto { ActivityId = a.Id, Code = a.Code, OldFinish = b.EarlyFinish, NewFinish = n.EarlyFinish, Critical = n.Critical });
                else if (had && !has) impact.Add(new PcImpactActivityDto { ActivityId = a.Id, Code = a.Code, OldFinish = b.EarlyFinish, NewFinish = null, Critical = b.Critical });
            }
            foreach (KeyValuePair<int, CpmItem> kv in after.Items.Where(kv => kv.Key < 0).OrderByDescending(kv => kv.Key))
                impact.Add(new PcImpactActivityDto { ActivityId = 0, Code = preview.Codes.TryGetValue(kv.Key, out string code) ? code : null, OldFinish = null, NewFinish = kv.Value.EarlyFinish, Critical = kv.Value.Critical });
            c.ImpactJson = JsonSerializer.Serialize(impact);
            return null;
        }

        private async Task<string> DecideAsync(PcChangeRequest c, PcChangeActionDto dto, List<PcChangeScenarioItemDto> scenario, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (c.Status != PcChangeStatus.InAnalysis) return "تصمیم‌گیری پس از تحلیل اثر انجام می‌شود.";
            if (dto.Decision == PcChangeDecision.None || !Enum.IsDefined(typeof(PcChangeDecision), dto.Decision)) return "تصمیم را مشخص کنید.";
            if (c.Recommendation == null) return "ابتدا مدیر پروژه پیشنهاد خود را ثبت کند.";
            // R1c (M1): whoever asked for the change or recommended it does not decide on it
            if (c.RequesterUserId == Me) return PcText.SameUser + " (درخواست‌کننده تغییر نمی‌تواند درباره آن تصمیم بگیرد)";
            if (c.RecommendedByUserId == Me) return PcText.SameUser + " (پیشنهاددهنده تغییر نمی‌تواند درباره آن تصمیم بگیرد)";
            await EnsureAuthoritiesAsync(cancellationToken);
            // authority is judged on the larger of the computed impact and what the client/requester asks for
            decimal amount = Math.Max(Math.Abs(c.ImpactDeltaCost ?? 0m), Math.Abs(c.RequestedPriceDelta));
            int delay = Math.Max(Math.Abs(c.ImpactDeltaDays ?? 0), Math.Abs(c.RequestedDaysDelta));
            List<PcApprovalAuthority> authorities = await _uow.Set<PcApprovalAuthority>().AsNoTracking().Where(a => a.CanDecide).ToListAsync(cancellationToken);
            PcApprovalAuthority mine = authorities.FirstOrDefault(a => _user.IsInRole(a.Role) && (a.MaxAmount == null || amount <= a.MaxAmount) && (a.MaxDelayDays == null || delay <= a.MaxDelayDays));
            if (mine == null)
            {
                if (!c.Escalated)
                {
                    c.Escalated = true; // kept even though the decision is refused: the CEO sees it on the dashboard
                    await _uow.SaveChangesAsync(cancellationToken);
                }
                return authorities.Any(a => _user.IsInRole(a.Role)) ? "مبلغ یا تاخیر از حد اختیار شما بیشتر است؛ تصمیم با مدیرعامل است." : PcText.NotAllowed;
            }
            if (Clean(dto.Note) == null) return "دلیل تصمیم را بنویسید.";
            if (dto.Decision == PcChangeDecision.Approve)
            {
                if (c.Board.Any(m => m.Vote == PcVote.None)) return "همه اعضای هیئت باید رای دهند.";
                if (c.Board.Any(m => m.Vote == PcVote.Reject) && !_user.IsInRole(ProjectControlRoles.Ceo)) return "عضو هیئت مخالفت کرده است؛ تایید فقط با مدیرعامل ممکن است.";
            }
            if (dto.Decision != PcChangeDecision.Approve)
            {
                RecordDecision(c, dto, mine.Role, now);
                c.Status = dto.Decision == PcChangeDecision.Reject ? PcChangeStatus.Rejected : PcChangeStatus.Registered;
                return null;
            }

            // approved: the scenario is carried out on the approved plan and the next baseline is issued from the result (approved at once, the board and this decision are the approval).
            // Nothing of the decision is recorded before the plan was changed, so a refusal leaves the request in analysis.
            // R1c (H2): the request is claimed first (decision and status saved, checked against the row version), so two parallel decisions cannot both apply the scenario;
            // when the plan change or the baseline fails, the claim is given back and a retry applies the scenario again from the current baseline rows.
            if (scenario.Count > 0)
            {
                RecordDecision(c, dto, mine.Role, now);
                c.Status = PcChangeStatus.Implementing; c.ModifyDate = now; c.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return PcText.Concurrency; }
                string applyError;
                try { applyError = await ApplyApprovedScenarioAsync(c, scenario, now, cancellationToken); }
                catch (Exception)
                {
                    await ReleaseClaimAsync(c, cancellationToken);
                    throw;
                }
                if (applyError != null) { await ReleaseClaimAsync(c, cancellationToken); return applyError; }
                return null;
            }
            RecordDecision(c, dto, mine.Role, now);
            c.Status = PcChangeStatus.Implementing;
            return null;
        }

        /// <summary>Gives the claim of a decision back (the request is in analysis again, nothing of the decision stays).</summary>
        private async Task ReleaseClaimAsync(PcChangeRequest c, CancellationToken cancellationToken)
        {
            try
            {
                // a half-built baseline of this attempt is dropped from the context before the claim is saved back
                foreach (PcBaselineCostPeriod p in _uow.Set<PcBaselineCostPeriod>().Local.Where(x => x.Id == 0).ToList()) _uow.Entry(p).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                foreach (PcBaseline b in _uow.Set<PcBaseline>().Local.Where(x => x.Id == 0).ToList())
                {
                    foreach (PcBaselineActivity r in b.Activities.ToList()) _uow.Entry(r).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    _uow.Entry(b).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }
                c.Decision = PcChangeDecision.None; c.DecisionNote = null; c.DecidedByUserId = null; c.DecidedByRole = null; c.DecidedAt = null;
                c.Status = PcChangeStatus.InAnalysis; c.Baseline = null;
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (Exception) { /* the request stays claimed only if the database is gone; the next decision is refused as already decided */ }
        }

        private void RecordDecision(PcChangeRequest c, PcChangeActionDto dto, string role, DateTimeOffset now)
        {
            c.Decision = dto.Decision; c.DecisionNote = Clean(dto.Note); c.DecidedByUserId = Me; c.DecidedByRole = role; c.DecidedAt = now;
        }

        /// <summary>
        /// R1c (H1): applies the scenario of an approved change request to the plan and builds the new baseline from the approved items over the current baseline, never from the live values.
        /// A request without scenario items (a price or time change that does not touch the plan) builds no baseline. Returns the Persian error, or null.
        /// </summary>
        private async Task<string> ApplyApprovedScenarioAsync(PcChangeRequest c, List<PcChangeScenarioItemDto> scenario, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (scenario.Count == 0) return null;
            string gate = await PcGates.RequireBaselineReviewAsync(_uow, c.ContractId, cancellationToken);
            if (gate != null) return gate;
            PcContractTerms terms = await PcContext.TermsAsync(_uow, c.ContractId, true, cancellationToken);
            if (terms?.CurrentBaselineId == null) return "خط مبنای جاری وجود ندارد؛ سناریوی تغییر فقط پس از تایید خط مبنای اولیه اعمال می‌شود.";
            List<PcBaselineActivity> currentRows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == terms.CurrentBaselineId.Value).ToListAsync(cancellationToken);
            PcScenarioOutcome outcome = await _schedule.ApplyScenarioAsync(c.ContractId, currentRows, scenario, cancellationToken);
            if (outcome.Error != null) return outcome.Error;
            string problem = PcBaselineRows.CreationProblem(outcome.Activities);
            if (problem != null) return problem;
            int max = await _uow.Set<PcBaseline>().Where(b => b.ContractId == c.ContractId && !b.Deleted).Select(b => (int?)b.Number).MaxAsync(cancellationToken) ?? -1;
            PcBaseline nb = NewBaseline(c.ContractId, max + 1, "تغییر " + c.Number, outcome.Activities, outcome.EffectiveWeights, terms, now);
            nb.Signatures.Clear();
            nb.Status = PcBaselineStatus.Approved; nb.ApprovedAt = now; nb.ApprovedByUserId = Me; nb.ChangeRequestId = c.Id;
            // the planned-value curve of the new baseline is frozen with it
            foreach (PcBaselineCostPeriod period in await PcFrozenPlan.PeriodsAsync(_uow, 0, terms, nb.Activities.ToList(), cancellationToken))
            {
                period.Baseline = nb;
                _uow.Set<PcBaselineCostPeriod>().Add(period);
            }
            foreach (PcBaseline old in await _uow.Set<PcBaseline>().Where(x => x.ContractId == c.ContractId && x.Status == PcBaselineStatus.Approved).ToListAsync(cancellationToken)) old.Status = PcBaselineStatus.Superseded;
            _uow.Set<PcBaseline>().Add(nb);
            c.Baseline = nb;
            terms.CurrentBaseline = nb;
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
            QuantifiedByUserId = c.QuantifiedByUserId, LastEditedByUserId = c.LastEditedByUserId, PmoApprovedByUserId = c.PmoApprovedByUserId, PmoApprovedAt = c.PmoApprovedAt, DesignApprovedByUserId = c.DesignApprovedByUserId, DesignApprovedAt = c.DesignApprovedAt,
        };

        /// <summary>
        /// R1a2 (H4): both approvals and the reviewer go back to "not given". A new quantification or an edit of the text means the people approved something else than what the claim now says,
        /// so the approvals are given again (the audit trail keeps the earlier ones).
        /// </summary>
        private static void ResetClaimApprovals(PcClaim c)
        {
            c.PmoApproved = false; c.PmoApprovedByUserId = null; c.PmoApprovedAt = null;
            c.DesignManufacturingApproved = false; c.DesignApprovedByUserId = null; c.DesignApprovedAt = null;
            c.ReviewedByUserId = null; c.ReviewedAt = null;
        }

        /// <summary>
        /// R1a2 (H4, L-7): the problem with an approval that does not depend on which side approves, or null. A claim is approved only on the quantified amount (status Quantified),
        /// and not by the person who registered it, the person who wrote its text last or the person who quantified it (an approval of one's own work is no approval).
        /// </summary>
        private string ClaimApprovalProblem(PcClaim c)
        {
            if (c.Status != PcClaimStatus.Quantified) return "تایید فقط برای ادعای برآوردشده ممکن است؛ ابتدا ادعا برآورد شود.";
            if (c.CreatorUserId == Me || c.QuantifiedByUserId == Me || c.LastEditedByUserId == Me)
                return PcText.SameUser + " (تایید‌کننده نمی‌تواند ثبت‌کننده، آخرین ویرایش‌کننده‌ی متن یا برآوردکننده‌ی همین ادعا باشد)";
            return null;
        }

        /// <summary>The person who gave the PMO approval; a claim approved before R1a2 has no own column, its single approval is in ReviewedByUserId.</summary>
        private static int? PmoApproverOf(PcClaim c) => c.PmoApprovedByUserId ?? (c.PmoApproved && !c.DesignManufacturingApproved ? c.ReviewedByUserId : null);

        private static int? DesignApproverOf(PcClaim c) => c.DesignApprovedByUserId ?? (c.DesignManufacturingApproved && !c.PmoApproved ? c.ReviewedByUserId : null);

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
                // R1a2 (H4): what the client was told does not change afterwards; a claim that was notified is withdrawn and registered again
                if (c.Status == PcClaimStatus.Notified || c.Status == PcClaimStatus.Negotiating || c.Status == PcClaimStatus.Litigation)
                    return Bad<PcClaimDto>("ادعای ابلاغ‌شده ویرایش نمی‌شود؛ در صورت نیاز آن را پس بگیرید و ادعای تازه ثبت کنید.");
                bool edited = c.Direction != dto.Direction || c.Issue != Clean(dto.Issue) || c.Basis != Clean(dto.Basis) || c.IsCrossClaim != dto.IsCrossClaim;
                if (edited && (c.PmoApproved || c.DesignManufacturingApproved)) ResetClaimApprovals(c);
                // R1a2 (L-7): the person whose save changed the text is remembered; he is barred from both approvals like the creator and the quantifier
                if (edited) c.LastEditedByUserId = Me;
            }
            else
            {
                c = new PcClaim { ContractId = dto.ContractId, Status = PcClaimStatus.Issue, CreateDate = now, CreatorUserId = Me, LastEditedByUserId = Me, Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Claim, "CL", DateTime.Today, cancellationToken) };
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
                    // R1a2 (H4): a new quantification starts the approvals again, and the quantifier is remembered (he or she cannot approve)
                    ResetClaimApprovals(c);
                    c.DaysQuantified = dto.Days; c.AmountQuantified = dto.Amount; c.Status = PcClaimStatus.Quantified; c.QuantifiedByUserId = Me;
                    break;
                // R1a (finding H9): a claim is notified only after two different people approved it, the PMO deputy and the design and manufacturing side.
                // The entity keeps one reviewer id: while one approval exists it is that approver's id, so the second approver is compared with it; a repeated
                // approval is refused so the id is never overwritten before the second approval (the audit trail keeps both approvals).
                case "approve-pmo":
                    if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcClaimDto>(PcText.NotAllowed);
                    if (ClaimApprovalProblem(c) is string pmoProblem) return Bad<PcClaimDto>(pmoProblem);
                    if (c.PmoApproved) return Bad<PcClaimDto>("تایید PMO برای این ادعا قبلاً ثبت شده است.");
                    if (c.DesignManufacturingApproved && DesignApproverOf(c) == Me) return Bad<PcClaimDto>(PcText.SameUser + " (تایید PMO و تایید طراحی/ساخت باید از دو نفر مختلف باشد)");
                    c.PmoApproved = true; c.PmoApprovedByUserId = Me; c.PmoApprovedAt = now;
                    c.ReviewedByUserId = Me; c.ReviewedAt = now; c.ReviewNote = Clean(dto.Note) ?? c.ReviewNote;
                    break;
                case "approve-design":
                    // the PMO deputy does not approve for the design side: the design and manufacturing deputy is engineering (or the project manager) ★ to be confirmed by the company
                    if (!_user.IsInRole("ENGINEERING,PROJECTMANAGER")) return Bad<PcClaimDto>(PcText.NotAllowed + " (تایید طراحی/ساخت با مدیر مهندسی یا مدیر پروژه است)");
                    if (ClaimApprovalProblem(c) is string designProblem) return Bad<PcClaimDto>(designProblem);
                    if (c.DesignManufacturingApproved) return Bad<PcClaimDto>("تایید طراحی/ساخت برای این ادعا قبلاً ثبت شده است.");
                    if (c.PmoApproved && PmoApproverOf(c) == Me) return Bad<PcClaimDto>(PcText.SameUser + " (تایید PMO و تایید طراحی/ساخت باید از دو نفر مختلف باشد)");
                    c.DesignManufacturingApproved = true; c.DesignApprovedByUserId = Me; c.DesignApprovedAt = now;
                    c.ReviewedByUserId = Me; c.ReviewedAt = now; c.ReviewNote = Clean(dto.Note) ?? c.ReviewNote;
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
