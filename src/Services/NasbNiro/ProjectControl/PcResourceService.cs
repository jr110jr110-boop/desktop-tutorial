using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcResourceService
    {
        Task<ResponseDto<PagedList<PcResourceDto>>> GetResourcesAsync(PcResourceQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceDto>> GetResourceAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceDto>> SaveResourceAsync(PcResourceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceDto>> SetResourceActiveAsync(int id, bool active, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteResourceAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcResourceRateDto>>> GetRatesAsync(PcResourceRateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceRateDto>> SaveRateAsync(PcResourceRateSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceRateDto>> RateActionAsync(PcResourceRateActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcResourcePlanDto>> GetPlanAsync(int contractId, int? version, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcResourcePlanDto>>> GetPlanVersionsAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> OpenPlanAsync(PcResourcePlanOpenDto dto, CancellationToken cancellationToken);

        /// <summary>The planning agent (step RES.PLAN.DRAFT) only: opens the draft plan of a contract with the assignments the rule gives.</summary>
        Task<ResponseDto<PcResourcePlanDto>> DraftPlanAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> SaveAssignmentsAsync(PcActivityAssignmentsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> DeleteAssignmentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> MarkUnassignedAsync(PcMarkUnassignedDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> PlanActionAsync(PcResourcePlanActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourcePlanDto>> DiscardPlanAsync(PcResourcePlanDiscardDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcResourceBudgetDto>> GetBudgetAsync(int planId, DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcBaselineResourceDto>>> GetBaselineResourcesAsync(int baselineId, CancellationToken cancellationToken);

        /// <summary>Priority 4d: reads an MS Project (MSPDI) file and shows, without saving anything, how its resources and assignments map to the pool and to the activities of the open plan.</summary>
        Task<ResponseDto<PcMspPreviewDto>> PreviewImportAsync(PcMspImportRequestDto dto, CancellationToken cancellationToken);

        /// <summary>Priority 4d: puts the assignments of the reviewed mapping into the open resource plan (Source = Import). Rates and costs of the file are never imported.</summary>
        Task<ResponseDto<PcMspImportResultDto>> ImportAsync(PcMspImportRequestDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resource pool, standard rates, resource plan and assignments of project control (priority 4, phases 4a and 4b). The planning agent drafts the plan; people review, submit and approve it.
    /// Maker and checker differ; an AI seat never submits or approves (the data layer refuses it too). A standard rate is never set on a resource linked to a person, and what could show one person's cost is masked.
    /// </summary>
    public partial class PcResourceService : IPcResourceService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public PcResourceService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);
        private static ResponseDto<T> NotFound<T>(string m) => PcText.NotFound<T>(m);
        private static string Version(byte[] rowVersion) => rowVersion == null ? null : Convert.ToBase64String(rowVersion);

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int?> ids, CancellationToken ct) => await InventoryUsers.GetNamesAsync(_uow, ids, ct);

        private static string NameOf(Dictionary<int, string> names, int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : null;

        // ============================================================================================== resources (4a)

        private async Task<List<PcResourceDto>> ToDtosAsync(List<PcResource> rows, CancellationToken ct)
        {
            int[] ids = rows.Select(r => r.Id).ToArray();
            // priority 4d: a machine resource linked to a work centre reads its capacity and calendar from it (one source of truth with production)
            int[] centreIds = rows.Where(r => PcWorkCentreLink.IsLinked(r)).Select(r => r.WorkCenterId.Value).Distinct().ToArray();
            Dictionary<int, MfWorkCenter> centres = centreIds.Length == 0 ? new Dictionary<int, MfWorkCenter>() : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => centreIds.Contains(w.Id) && !w.Deleted).ToDictionaryAsync(w => w.Id, ct);
            MfWorkCenter CentreOf(PcResource r) => PcWorkCentreLink.IsLinked(r) && centres.TryGetValue(r.WorkCenterId.Value, out MfWorkCenter w) ? w : null;
            int[] calendarIds = rows.Select(r => CentreOf(r)?.CalendarId ?? r.CalendarId).Where(c => c.HasValue).Select(c => c.Value).Distinct().ToArray();
            Dictionary<int, string> calendars = calendarIds.Length == 0 ? new Dictionary<int, string>() : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => calendarIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            int[] roleIds = rows.Where(r => r.RateResourceId.HasValue).Select(r => r.RateResourceId.Value).Distinct().ToArray();
            Dictionary<int, string> roleCodes = roleIds.Length == 0 ? new Dictionary<int, string>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);
            List<int> used = ids.Length == 0 ? new List<int>() : await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => ids.Contains(x.ResourceId)).Select(x => x.ResourceId).Distinct().ToListAsync(ct);
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(_uow, ids, DateTime.Today, ct);
            bool sees = MfPrivacy.SeesPayroll(_user);
            return rows.Select(r =>
            {
                bool hidden = !sees && PcResourceRules.IsSensitive(r);
                MfWorkCenter wc = CentreOf(r);
                int? calendarId = wc != null ? wc.CalendarId : r.CalendarId;
                return new PcResourceDto
                {
                    Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, Unit = r.Unit, Capacity = wc != null ? PcWorkCentreLink.CapacityOf(wc) : r.Capacity, CalendarId = calendarId,
                    CapacityFromWorkCenter = wc != null, WorkCenterCode = wc?.Code, WorkCenterDayHours = wc != null ? PcWorkCentreLink.DayHoursOf(wc) : (decimal?)null,
                    CalendarName = calendarId.HasValue && calendars.TryGetValue(calendarId.Value, out string cn) ? cn : null, ResponsibleUnit = r.ResponsibleUnit,
                    EmployeeId = r.EmployeeId, WorkCenterId = r.WorkCenterId, ProviderId = r.ProviderId, RateResourceId = r.RateResourceId,
                    RateResourceCode = r.RateResourceId.HasValue && roleCodes.TryGetValue(r.RateResourceId.Value, out string rc) ? rc : null,
                    IsActive = r.IsActive, Notes = r.Notes, HasAssignments = used.Contains(r.Id), CanCarryRate = PcResourceRules.CanCarryRate(r),
                    EffectiveRate = hidden ? null : (rates.TryGetValue(r.Id, out decimal? rate) ? rate : null), RateHidden = hidden, RowVersion = Version(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcResourceDto>>> GetResourcesAsync(PcResourceQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PagedList<PcResourceDto>>(PcResourceText.NotAllowed);
            query ??= new PcResourceQuery();
            IQueryable<PcResource> q = _uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted);
            if (query.Kind.HasValue) q = q.Where(r => r.Kind == query.Kind.Value);
            if (query.IsActive.HasValue) q = q.Where(r => r.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Code.Contains(text) || r.Title.Contains(text));
            PagedList<PcResource> page = await PagedList<PcResource>.CreateAsync(q.OrderBy(r => r.Code).ThenBy(r => r.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcResourceDto> dtos = await ToDtosAsync(page.ToList(), cancellationToken);
            return ResponseDtoHelper.Ok((PagedList<PcResourceDto>)new PcPage<PcResourceDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcResourceDto>> GetResourceAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourceDto>(PcResourceText.NotAllowed);
            PcResource r = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return r == null ? NotFound<PcResourceDto>(PcResourceText.ResourceNotFound) : ResponseDtoHelper.Ok((await ToDtosAsync(new List<PcResource> { r }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<PcResourceDto>> SaveResourceAsync(PcResourceDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourceDto>(PcText.InvalidData);
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourceDto>(PcResourceText.NotAllowed);
            string code = Clean(dto.Code), title = Clean(dto.Title);
            // priority 4d: a machine linked to a work centre AND counted in hours takes its capacity and calendar from it; the typed values are not used.
            // A NEW or CHANGED link needs the unit hour (and the work centre must not already be read by another active linked machine); a machine that was already linked with another unit stays editable and keeps its own numbers.
            if (dto.WorkCenterId.HasValue && dto.Kind == PcResourceKind.Machine)
            {
                MfWorkCenter centre = await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, cancellationToken);
                if (centre == null) return Bad<PcResourceDto>("مرکز کار پیدا نشد.");
                PcResource before = dto.Id > 0 ? await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken) : null;
                bool linkIsNew = before == null || before.Kind != PcResourceKind.Machine || before.WorkCenterId != dto.WorkCenterId;
                if (dto.Unit != PcResourceUnit.Hour)
                {
                    if (linkIsNew) return Bad<PcResourceDto>(PcResourceText.LinkedMachineUnit);
                }
                else
                {
                    bool becomesLinked = linkIsNew || before.Unit != PcResourceUnit.Hour;
                    if (becomesLinked && (before == null || before.IsActive))
                    {
                        string other = await _uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.IsActive && r.Kind == PcResourceKind.Machine && r.Unit == PcResourceUnit.Hour && r.WorkCenterId == dto.WorkCenterId && r.Id != dto.Id).OrderBy(r => r.Id).Select(r => r.Code).FirstOrDefaultAsync(cancellationToken);
                        if (other != null) return Bad<PcResourceDto>(PcResourceText.CentreAlreadyLinked + " (" + other + ")");
                    }
                    dto.Capacity = PcWorkCentreLink.StoredCapacityOf(centre); dto.CalendarId = centre.CalendarId;
                }
            }
            string shape = PcResourceRules.ValidateShape(code, title, dto.Kind, dto.Unit, dto.Capacity, dto.EmployeeId, dto.WorkCenterId, dto.ProviderId, dto.RateResourceId);
            if (shape != null) return Bad<PcResourceDto>(shape);
            string unit = PcResourceRules.Upper(dto.ResponsibleUnit);
            if (unit != null && unit.Length > 30) return Bad<PcResourceDto>("واحد مسئول حداکثر ۳۰ نویسه است.");
            string notes = Clean(dto.Notes);
            if (notes != null && notes.Length > 1000) return Bad<PcResourceDto>("یادداشت حداکثر ۱۰۰۰ نویسه است.");
            if (dto.CalendarId.HasValue && !await _uow.Set<PcCalendar>().AnyAsync(c => c.Id == dto.CalendarId.Value && !c.Deleted, cancellationToken)) return Bad<PcResourceDto>("تقویم کاری پیدا نشد.");
            if (dto.EmployeeId.HasValue && !await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.EmployeeId.Value, cancellationToken)) return Bad<PcResourceDto>("کارمند پیدا نشد.");
            if (dto.WorkCenterId.HasValue && !await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, cancellationToken)) return Bad<PcResourceDto>("مرکز کار پیدا نشد.");
            if (dto.ProviderId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value, cancellationToken)) return Bad<PcResourceDto>("تامین‌کننده پیدا نشد.");
            if (dto.RateResourceId.HasValue)
            {
                PcResource role = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RateResourceId.Value && !r.Deleted, cancellationToken);
                if (role == null || role.Kind != PcResourceKind.Labour || role.EmployeeId.HasValue || role.RateResourceId.HasValue || role.Id == dto.Id) return Bad<PcResourceDto>("منبع مبنای نرخ باید یک منبع نقش (نیروی انسانی بدون پیوند به کارمند) باشد.");
            }
            if (await _uow.Set<PcResource>().AnyAsync(r => !r.Deleted && r.Code == code && r.Id != dto.Id, cancellationToken)) return Bad<PcResourceDto>("کد منبع تکراری است.");

            DateTimeOffset now = DateTimeOffset.Now;
            PcResource row;
            if (dto.Id > 0)
            {
                row = await _uow.Set<PcResource>().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
                if (row == null) return NotFound<PcResourceDto>(PcResourceText.ResourceNotFound);
                if (!string.IsNullOrEmpty(dto.RowVersion)) _uow.Entry(row).Property(r => r.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion);
                bool hasAssignments = await _uow.Set<PcActivityResource>().AnyAsync(x => x.ResourceId == row.Id, cancellationToken) || await _uow.Set<PcBaselineResource>().AnyAsync(x => x.ResourceId == row.Id, cancellationToken);
                bool hasRates = await _uow.Set<PcResourceRate>().AnyAsync(x => x.ResourceId == row.Id && !x.Deleted && x.Status != PcRateStatus.Rejected, cancellationToken);
                if ((hasAssignments || hasRates) && (row.Kind != dto.Kind || row.Unit != dto.Unit)) return Bad<PcResourceDto>("نوع و واحد شمارش منبعی که در طرح منابع به کار رفته یا نرخ دارد تغییر نمی‌کند؛ منبع تازه بسازید.");
                if (hasRates && dto.EmployeeId.HasValue) return Bad<PcResourceDto>(PcResourceText.RatePrivacy);
                // the cost of a resource that has a rate or a frozen baseline row is judged by its capacity and its employee link: a change that moves it across the line of one person's cost is for the payroll roles only
                bool baselineRows = await _uow.Set<PcBaselineResource>().AnyAsync(x => x.ResourceId == row.Id, cancellationToken);
                bool crosses = PcResourceRules.IsSensitive(row) != PcResourceRules.IsSensitive(dto.Kind, dto.Capacity, dto.EmployeeId.HasValue || dto.RateResourceId.HasValue) || row.EmployeeId.HasValue != dto.EmployeeId.HasValue;
                if ((hasRates || baselineRows) && crosses && !MfPrivacy.SeesPayroll(_user)) return Bad<PcResourceDto>(PcResourceText.SensitivityChange);
                if (await _uow.Set<PcResource>().AnyAsync(r => !r.Deleted && r.RateResourceId == row.Id, cancellationToken) && dto.EmployeeId.HasValue) return Bad<PcResourceDto>("این منبع مبنای نرخ منبع دیگری است و نمی‌تواند به کارمند وصل شود.");
                row.ModifyDate = now; row.ModifierUserId = Me;
            }
            else
            {
                row = new PcResource { CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
                _uow.Set<PcResource>().Add(row);
            }
            row.Code = code; row.Title = title; row.Kind = dto.Kind; row.Unit = dto.Unit; row.Capacity = dto.Capacity; row.CalendarId = dto.CalendarId; row.ResponsibleUnit = unit;
            row.EmployeeId = dto.EmployeeId; row.WorkCenterId = dto.WorkCenterId; row.ProviderId = dto.ProviderId; row.RateResourceId = dto.RateResourceId;
            if (dto.Id <= 0) row.IsActive = true; // an edit never switches a resource on or off (the activate and deactivate actions do)
            row.Notes = notes;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourceDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourceDto>("کد منبع تکراری است."); }
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<PcResource> { row }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<PcResourceDto>> SetResourceActiveAsync(int id, bool active, CancellationToken cancellationToken)
        {
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourceDto>(PcResourceText.NotAllowed);
            PcResource row = await _uow.Set<PcResource>().FirstOrDefaultAsync(r => r.Id == id && !r.Deleted, cancellationToken);
            if (row == null) return NotFound<PcResourceDto>(PcResourceText.ResourceNotFound);
            if (row.IsActive != active)
            {
                row.IsActive = active; row.ModifyDate = DateTimeOffset.Now; row.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return Bad<PcResourceDto>(PcText.Concurrency); }
            }
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<PcResource> { row }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<bool>> DeleteResourceAsync(int id, CancellationToken cancellationToken)
        {
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<bool>(PcResourceText.NotAllowed);
            PcResource row = await _uow.Set<PcResource>().FirstOrDefaultAsync(r => r.Id == id && !r.Deleted, cancellationToken);
            if (row == null) return NotFound<bool>(PcResourceText.ResourceNotFound);
            if (await _uow.Set<PcActivityResource>().AnyAsync(x => x.ResourceId == id, cancellationToken) || await _uow.Set<PcBaselineResource>().AnyAsync(x => x.ResourceId == id, cancellationToken))
                return Bad<bool>("منبع در طرح منابع یا خط مبنا به کار رفته است و حذف نمی‌شود؛ آن را غیرفعال کنید.");
            if (await _uow.Set<PcResourceRate>().AnyAsync(x => x.ResourceId == id && !x.Deleted, cancellationToken)) return Bad<bool>("منبع نرخ ثبت‌شده دارد و حذف نمی‌شود؛ آن را غیرفعال کنید.");
            if (await _uow.Set<PcResource>().AnyAsync(r => !r.Deleted && r.RateResourceId == id, cancellationToken)) return Bad<bool>("منبع، مبنای نرخ منبع دیگری است و حذف نمی‌شود؛ آن را غیرفعال کنید.");
            row.Delete(); row.ModifyDate = DateTimeOffset.Now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // ============================================================================================== standard rates (4a)

        private async Task<List<PcResourceRateDto>> RateDtosAsync(List<PcResourceRate> rows, CancellationToken ct)
        {
            int[] resourceIds = rows.Select(r => r.ResourceId).Distinct().ToArray();
            Dictionary<int, PcResource> resources = resourceIds.Length == 0 ? new Dictionary<int, PcResource>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            Dictionary<int, string> names = await NamesAsync(rows.SelectMany(r => new int?[] { r.MakerUserId, r.ApprovedByUserId }), ct);
            bool sees = MfPrivacy.SeesPayroll(_user);
            return rows.Select(r =>
            {
                resources.TryGetValue(r.ResourceId, out PcResource res);
                bool hidden = !sees && PcResourceRules.IsSensitive(res);
                return new PcResourceRateDto
                {
                    Id = r.Id, ResourceId = r.ResourceId, ResourceCode = res?.Code, ResourceTitle = res?.Title, Rate = hidden ? null : r.Rate, RateHidden = hidden, EffectiveFrom = r.EffectiveFrom, Status = r.Status,
                    MakerUserId = r.MakerUserId, MakerName = NameOf(names, r.MakerUserId), SubmittedAt = r.SubmittedAt, ApprovedByUserId = r.ApprovedByUserId, ApprovedByName = NameOf(names, r.ApprovedByUserId),
                    ApprovedAt = r.ApprovedAt, RejectedAt = r.RejectedAt, RejectReason = r.RejectReason, RowVersion = Version(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcResourceRateDto>>> GetRatesAsync(PcResourceRateQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PagedList<PcResourceRateDto>>(PcResourceText.NotAllowed);
            query ??= new PcResourceRateQuery();
            IQueryable<PcResourceRate> q = _uow.Set<PcResourceRate>().AsNoTracking().Where(r => !r.Deleted);
            if (query.ResourceId.HasValue) q = q.Where(r => r.ResourceId == query.ResourceId.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            PagedList<PcResourceRate> page = await PagedList<PcResourceRate>.CreateAsync(q.OrderBy(r => r.ResourceId).ThenByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcResourceRateDto> dtos = await RateDtosAsync(page.ToList(), cancellationToken);
            return ResponseDtoHelper.Ok((PagedList<PcResourceRateDto>)new PcPage<PcResourceRateDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcResourceRateDto>> SaveRateAsync(PcResourceRateSaveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourceRateDto>(PcText.InvalidData);
            if (IsAi || !_user.IsInRole(PcResourceRules.RateMakers)) return Bad<PcResourceRateDto>(PcResourceText.NotAllowed + " (نرخ را مدیر مالی پیشنهاد می‌دهد)");
            PcResource resource = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.ResourceId && !r.Deleted, cancellationToken);
            if (resource == null) return NotFound<PcResourceRateDto>(PcResourceText.ResourceNotFound);
            // salary privacy: a person-linked resource never carries a rate (its cost would be one person's pay)
            if (!PcResourceRules.CanCarryRate(resource)) return Bad<PcResourceRateDto>(PcResourceText.RatePrivacy);
            if (dto.Rate < 0m || dto.Rate > PcResourceRules.MaxRate) return Bad<PcResourceRateDto>("نرخ باید صفر یا بیشتر باشد.");
            if (dto.EffectiveFrom.Year < 2000 || dto.EffectiveFrom.Year > 2100) return Bad<PcResourceRateDto>("تاریخ شروع اعتبار نرخ نامعتبر است.");
            DateTime from = dto.EffectiveFrom.Date;
            int ownId = dto.Id ?? 0;
            if (await _uow.Set<PcResourceRate>().AnyAsync(r => !r.Deleted && r.ResourceId == resource.Id && r.EffectiveFrom == from && r.Id != ownId && (r.Status == PcRateStatus.Approved || r.Status == PcRateStatus.Submitted), cancellationToken))
                return Bad<PcResourceRateDto>(PcResourceText.RateDuplicate);
            DateTimeOffset now = DateTimeOffset.Now;
            PcResourceRate row;
            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                row = await _uow.Set<PcResourceRate>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (row == null) return NotFound<PcResourceRateDto>(PcResourceText.RateNotFound);
                if (row.Status != PcRateStatus.Draft) return Bad<PcResourceRateDto>("فقط نرخ پیش‌نویس ویرایش می‌شود.");
                if (row.MakerUserId != Me) return Bad<PcResourceRateDto>("نرخ پیش‌نویس را فقط پیشنهاددهنده آن ویرایش می‌کند.");
                if (row.ResourceId != resource.Id) return Bad<PcResourceRateDto>("منبع نرخ تغییر نمی‌کند؛ نرخ تازه پیشنهاد کنید.");
                if (!string.IsNullOrEmpty(dto.RowVersion)) _uow.Entry(row).Property(r => r.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion);
            }
            else
            {
                row = new PcResourceRate { ResourceId = resource.Id, Status = PcRateStatus.Draft, MakerUserId = Me, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcResourceRate>().Add(row);
            }
            row.Rate = dto.Rate; row.EffectiveFrom = from; row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourceRateDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok((await RateDtosAsync(new List<PcResourceRate> { row }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<PcResourceRateDto>> RateActionAsync(PcResourceRateActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcResourceRateDto>(PcText.InvalidData);
            PcResourceRate row = await _uow.Set<PcResourceRate>().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
            if (row == null) return NotFound<PcResourceRateDto>(PcResourceText.RateNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                {
                    if (IsAi || !_user.IsInRole(PcResourceRules.RateMakers)) return Bad<PcResourceRateDto>(PcResourceText.NotAllowed);
                    if (row.Status != PcRateStatus.Draft) return Bad<PcResourceRateDto>("فقط نرخ پیش‌نویس ارسال می‌شود.");
                    if (row.MakerUserId != Me) return Bad<PcResourceRateDto>("نرخ پیش‌نویس را فقط پیشنهاددهنده آن ارسال می‌کند.");
                    PcResource resource = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == row.ResourceId && !r.Deleted, cancellationToken);
                    if (resource == null || !PcResourceRules.CanCarryRate(resource)) return Bad<PcResourceRateDto>(PcResourceText.RatePrivacy);
                    if (await DuplicateRateAsync(row, cancellationToken)) return Bad<PcResourceRateDto>(PcResourceText.RateDuplicate);
                    row.Status = PcRateStatus.Submitted; row.SubmittedAt = now;
                    break;
                }
                case "approve":
                {
                    if (IsAi) return Bad<PcResourceRateDto>(PcResourceText.AiDecision);
                    if (!_user.IsInRole(PcResourceRules.RateApprovers)) return Bad<PcResourceRateDto>(PcResourceText.NotAllowed + " (تایید نرخ با نفر دوم مالی است)");
                    if (row.Status != PcRateStatus.Submitted) return Bad<PcResourceRateDto>("فقط نرخ ارسال‌شده تایید می‌شود.");
                    if (row.MakerUserId == Me) return Bad<PcResourceRateDto>(PcResourceText.SameUser + " (پیشنهاددهنده نرخ نمی‌تواند آن را تایید کند)");
                    PcResource resource = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == row.ResourceId && !r.Deleted, cancellationToken);
                    if (resource == null || !PcResourceRules.CanCarryRate(resource)) return Bad<PcResourceRateDto>(PcResourceText.RatePrivacy);
                    if (await DuplicateRateAsync(row, cancellationToken)) return Bad<PcResourceRateDto>(PcResourceText.RateDuplicate);
                    row.Status = PcRateStatus.Approved; row.ApprovedAt = now; row.ApprovedByUserId = Me;
                    break;
                }
                case "reject":
                {
                    if (IsAi) return Bad<PcResourceRateDto>(PcResourceText.AiDecision);
                    if (!_user.IsInRole(PcResourceRules.RateApprovers)) return Bad<PcResourceRateDto>(PcResourceText.NotAllowed);
                    if (row.Status != PcRateStatus.Submitted) return Bad<PcResourceRateDto>("فقط نرخ ارسال‌شده رد می‌شود.");
                    if (row.MakerUserId == Me) return Bad<PcResourceRateDto>(PcResourceText.SameUser);
                    string reason = Clean(dto.Note);
                    if (reason == null) return Bad<PcResourceRateDto>("علت رد نرخ را بنویسید.");
                    if (reason.Length > 1000) return Bad<PcResourceRateDto>("علت رد حداکثر ۱۰۰۰ نویسه است.");
                    row.Status = PcRateStatus.Rejected; row.RejectedAt = now; row.RejectedByUserId = Me; row.RejectReason = reason;
                    break;
                }
                default:
                    return Bad<PcResourceRateDto>("عملیات نامعتبر است.");
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourceRateDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourceRateDto>(PcResourceText.RateDuplicate); }
            return ResponseDtoHelper.Ok((await RateDtosAsync(new List<PcResourceRate> { row }, cancellationToken))[0]);
        }

        /// <summary>Another submitted or approved rate of the same resource and date (two live rates for one date would make the effective rate ambiguous).</summary>
        private Task<bool> DuplicateRateAsync(PcResourceRate row, CancellationToken ct) =>
            _uow.Set<PcResourceRate>().AnyAsync(r => !r.Deleted && r.ResourceId == row.ResourceId && r.EffectiveFrom == row.EffectiveFrom && r.Id != row.Id && (r.Status == PcRateStatus.Approved || r.Status == PcRateStatus.Submitted), ct);

        // ============================================================================================== the resource plan (4b)

        private async Task<int?> ProjectManagerAsync(int contractId, CancellationToken ct) =>
            await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId && !t.Deleted).Select(t => t.ProjectManagerUserId).FirstOrDefaultAsync(ct);

        private async Task<PcResourcePlanDto> PlanDtoAsync(PcResourcePlan plan, bool withAssignments, CancellationToken ct) => (await PlanDtosAsync(new List<PcResourcePlan> { plan }, withAssignments, ct))[0];

        /// <summary>The plans as DTOs with a fixed number of queries whatever the number of plans.</summary>
        private async Task<List<PcResourcePlanDto>> PlanDtosAsync(List<PcResourcePlan> plans, bool withAssignments, CancellationToken ct)
        {
            var result = new List<PcResourcePlanDto>();
            if (plans.Count == 0) return result;
            int[] planIds = plans.Select(p => p.Id).ToArray();
            int[] contractIds = plans.Select(p => p.ContractId).Distinct().ToArray();
            Dictionary<int, string> codes = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var termRows = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.ProjectManagerUserId != null && contractIds.Contains(t.ContractId)).OrderBy(t => t.Id).Select(t => new { t.ContractId, Manager = t.ProjectManagerUserId.Value }).ToListAsync(ct);
            Dictionary<int, int> managers = termRows.GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First().Manager);
            Dictionary<int, string> names = await NamesAsync(plans.SelectMany(p => new int?[] { p.MakerUserId, p.ApprovedByUserId }).Concat(managers.Values.Select(m => (int?)m)), ct);
            var needing = await _uow.Set<PcActivity>().AsNoTracking().Where(a => contractIds.Contains(a.ContractId) && !a.Deleted && a.DurationDays > 0).OrderBy(a => a.Id).Select(a => new { a.Id, a.ContractId }).ToListAsync(ct);
            var skippedRows = await _uow.Set<PcResourcePlanSkip>().AsNoTracking().Where(s => planIds.Contains(s.PlanId)).OrderBy(s => s.ActivityId).Select(s => new { s.PlanId, s.ActivityId }).ToListAsync(ct);
            var rows = new List<(int PlanId, PcActivityResourceDto Dto)>();
            if (withAssignments)
            {
                var full = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => planIds.Contains(x.PlanId) && !x.Activity.Deleted).OrderBy(x => x.PlanId).ThenBy(x => x.Activity.Code).ThenBy(x => x.ActivityId).ThenBy(x => x.Resource.Code).ThenBy(x => x.ResourceId)
                    .Select(x => new PcActivityResourceDto
                    {
                        Id = x.Id, PlanId = x.PlanId, ActivityId = x.ActivityId, ActivityCode = x.Activity.Code, ActivityTitle = x.Activity.Title, ResourceId = x.ResourceId, ResourceCode = x.Resource.Code, ResourceTitle = x.Resource.Title,
                        ResourceKind = x.Resource.Kind, ResourceUnit = x.Resource.Unit, Quantity = x.Quantity, Distribution = x.Distribution, ResponsibleRole = x.ResponsibleRole, Source = x.Source,
                    }).ToListAsync(ct);
                rows.AddRange(full.Select(d => (d.PlanId, d)));
            }
            else
            {
                var light = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => planIds.Contains(x.PlanId) && !x.Activity.Deleted).Select(x => new { x.PlanId, x.ActivityId }).Distinct().ToListAsync(ct);
                rows.AddRange(light.Select(x => (x.PlanId, new PcActivityResourceDto { PlanId = x.PlanId, ActivityId = x.ActivityId })));
            }
            foreach (PcResourcePlan plan in plans)
            {
                List<int> need = needing.Where(a => a.ContractId == plan.ContractId).Select(a => a.Id).ToList();
                List<int> skipped = skippedRows.Where(x => x.PlanId == plan.Id).Select(x => x.ActivityId).ToList();
                List<PcActivityResourceDto> mine = rows.Where(r => r.PlanId == plan.Id).Select(r => r.Dto).ToList();
                HashSet<int> handled = new HashSet<int>(mine.Select(r => r.ActivityId)); handled.UnionWith(skipped);
                int? manager = managers.TryGetValue(plan.ContractId, out int m) ? m : (int?)null;
                // who really decides: the project manager, or the PMO when there is none or the manager made or edited the plan
                bool barred = manager.HasValue && (plan.MakerUserId == manager.Value || PcResourceRules.IsEditor(plan.EditorUserIds, manager.Value));
                int? decider = manager.HasValue && !barred ? manager : null;
                result.Add(new PcResourcePlanDto
                {
                    Id = plan.Id, ContractId = plan.ContractId, ContractCode = codes.TryGetValue(plan.ContractId, out string code) ? code : null, Version = plan.Version, Status = plan.Status, MakerUserId = plan.MakerUserId, MakerName = NameOf(names, plan.MakerUserId),
                    PreparedByAgent = plan.PreparedByAgent, ChangeRequestId = plan.ChangeRequestId, SubmittedAt = plan.SubmittedAt, ApprovedAt = plan.ApprovedAt, ApprovedByUserId = plan.ApprovedByUserId,
                    ApprovedByName = NameOf(names, plan.ApprovedByUserId), ReturnedAt = plan.ReturnedAt, ReturnReason = plan.ReturnReason, ApproverUserId = manager,
                    DeciderUserId = decider, DeciderName = decider.HasValue ? NameOf(names, decider) : "PMO",
                    Editable = plan.Status == PcResourcePlanStatus.Draft || plan.Status == PcResourcePlanStatus.Returned, ActivitiesNeedingResource = need.Count,
                    UnassignedCount = need.Count(id => !handled.Contains(id)), MarkedUnassignedActivityIds = skipped, Assignments = withAssignments ? mine : new List<PcActivityResourceDto>(), RowVersion = Version(plan.RowVersion),
                });
            }
            return result;
        }

        public async Task<ResponseDto<PcResourcePlanDto>> GetPlanAsync(int contractId, int? version, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourcePlanDto>(PcResourceText.NotAllowed);
            IQueryable<PcResourcePlan> q = _uow.Set<PcResourcePlan>().AsNoTracking().Where(p => p.ContractId == contractId && !p.Deleted);
            PcResourcePlan plan;
            if (version.HasValue) plan = await q.FirstOrDefaultAsync(p => p.Version == version.Value, cancellationToken);
            else
            {
                // the open plan when there is one, else the newest approved one
                plan = await q.Where(p => p.Status != PcResourcePlanStatus.Approved).OrderByDescending(p => p.Version).FirstOrDefaultAsync(cancellationToken)
                    ?? await q.OrderByDescending(p => p.Version).FirstOrDefaultAsync(cancellationToken);
            }
            return plan == null ? NotFound<PcResourcePlanDto>(PcResourceText.NoPlan) : ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        public async Task<ResponseDto<List<PcResourcePlanDto>>> GetPlanVersionsAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<List<PcResourcePlanDto>>(PcResourceText.NotAllowed);
            List<PcResourcePlan> plans = await _uow.Set<PcResourcePlan>().AsNoTracking().Where(p => p.ContractId == contractId && !p.Deleted).OrderBy(p => p.Version).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await PlanDtosAsync(plans, false, cancellationToken));
        }

        /// <summary>
        /// Whether a new plan of the contract may be opened now: no open plan; before the baseline always; after it only through an approved change request that no plan has used (the same path that changes the activities).
        /// Returns the Persian refusal (or null) and the change request that the new version carries.
        /// </summary>
        private async Task<(string Error, int? ChangeRequestId)> OpenRuleAsync(int contractId, int? requestedChange, CancellationToken ct)
        {
            if (await PcResourceData.HasOpenPlanAsync(_uow, contractId, ct)) return (PcResourceText.OpenPlanExists, null);
            if (!await PcPlanLock.IsLockedAsync(_uow, contractId, ct)) return (null, null);
            int? cr = await PcResourceData.OpenableChangeRequestAsync(_uow, contractId, requestedChange, ct);
            return cr.HasValue ? ((string)null, cr) : (PcResourceText.Locked, (int?)null);
        }

        private async Task<PcResourcePlan> NewPlanAsync(int contractId, int? changeRequestId, bool byAgent, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            int max = await _uow.Set<PcResourcePlan>().Where(p => p.ContractId == contractId).Select(p => (int?)p.Version).MaxAsync(ct) ?? 0;
            var plan = new PcResourcePlan
            {
                ContractId = contractId, Version = max + 1, Status = PcResourcePlanStatus.Draft, MakerUserId = Me, PreparedByAgent = byAgent, ChangeRequestId = changeRequestId,
                EditorUserIds = PcResourceRules.AddEditor(null, Me), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me, Assignments = new List<PcActivityResource>(), Skips = new List<PcResourcePlanSkip>(),
            };
            // a version that follows an approved one starts as a copy of it
            PcResourcePlan previous = await PcResourceData.LatestApprovedAsync(_uow, contractId, false, ct);
            if (previous != null)
            {
                List<PcActivityResource> rows = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == previous.Id && !x.Activity.Deleted).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(ct);
                foreach (PcActivityResource r in rows)
                    plan.Assignments.Add(new PcActivityResource { ActivityId = r.ActivityId, ResourceId = r.ResourceId, Quantity = r.Quantity, Distribution = r.Distribution, ResponsibleRole = r.ResponsibleRole, Source = r.Source, EditedByUserId = Me, EditedAt = now });
                List<int> skipped = await _uow.Set<PcResourcePlanSkip>().AsNoTracking().Where(s => s.PlanId == previous.Id && !s.Activity.Deleted).Select(s => s.ActivityId).ToListAsync(ct);
                foreach (int a in skipped) plan.Skips.Add(new PcResourcePlanSkip { ActivityId = a, MarkedByUserId = Me, MarkedAt = now });
            }
            _uow.Set<PcResourcePlan>().Add(plan);
            return plan;
        }

        public async Task<ResponseDto<PcResourcePlanDto>> OpenPlanAsync(PcResourcePlanOpenDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourcePlanDto>(PcText.InvalidData);
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourcePlanDto>(PcResourceText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return NotFound<PcResourcePlanDto>(PcText.ContractNotFound);
            var rule = await OpenRuleAsync(dto.ContractId, dto.ChangeRequestId, cancellationToken);
            if (rule.Error != null) return Bad<PcResourcePlanDto>(rule.Error);
            PcResourcePlan plan = await NewPlanAsync(dto.ContractId, rule.ChangeRequestId, false, cancellationToken);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcResourceText.OpenPlanExists); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        public async Task<ResponseDto<PcResourcePlanDto>> DraftPlanAsync(int contractId, CancellationToken cancellationToken)
        {
            // the one place where the AI makes a plan: the planning agent in its own step (the draft is the maker side; submitting and approving stay with people)
            if (!AgentMaker.Allows("RES.PLAN.DRAFT") || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourcePlanDto>(PcResourceText.NotAllowed);
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && !c.Deleted, cancellationToken);
            if (contract == null) return NotFound<PcResourcePlanDto>(PcText.ContractNotFound);
            var rule = await OpenRuleAsync(contractId, null, cancellationToken);
            if (rule.Error != null) return Bad<PcResourcePlanDto>(rule.Error);
            PcResourcePlan plan = await NewPlanAsync(contractId, rule.ChangeRequestId, true, cancellationToken);

            // the rows the rule gives for the activities that the copy of the approved plan does not cover
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            decimal? projectHours = terms?.CalendarId == null ? null : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => c.Id == terms.CalendarId.Value).Select(c => (decimal?)c.HoursPerDay).FirstOrDefaultAsync(cancellationToken);
            var acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted && a.DurationDays > 0).OrderBy(a => a.Id).Select(a => new { a.Id, a.DurationDays, a.ResponsibleUnit, a.CalendarId }).ToListAsync(cancellationToken);
            int[] calendarIds = acts.Where(a => a.CalendarId.HasValue).Select(a => a.CalendarId.Value).Distinct().ToArray();
            Dictionary<int, decimal> hours = calendarIds.Length == 0 ? new Dictionary<int, decimal>() : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => calendarIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.HoursPerDay, cancellationToken);
            int[] actIds = acts.Select(a => a.Id).ToArray();
            var lines = await _uow.Set<PcCostBaselineLine>().AsNoTracking().Where(l => l.ContractId == contractId && l.ActivityId != null && l.ResourceClass != null && actIds.Contains(l.ActivityId.Value))
                .Select(l => new { ActivityId = l.ActivityId.Value, Class = l.ResourceClass.Value }).Distinct().ToListAsync(cancellationToken);
            HashSet<int> covered = new HashSet<int>(plan.Assignments.Select(x => x.ActivityId)); covered.UnionWith(plan.Skips.Select(s => s.ActivityId));
            var draftActivities = acts.Where(a => !covered.Contains(a.Id)).Select(a => new PcResourceRules.DraftActivity
            {
                Id = a.Id, DurationDays = a.DurationDays, ResponsibleUnit = a.ResponsibleUnit,
                DayHours = PcResourceRules.DayHours(a.CalendarId.HasValue && hours.TryGetValue(a.CalendarId.Value, out decimal h) ? h : (decimal?)null, projectHours),
                Classes = new HashSet<PcResourceClass>(lines.Where(l => l.ActivityId == a.Id).Select(l => l.Class)),
            }).ToList();
            var pool = await _uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.IsActive && r.EmployeeId == null && r.ResponsibleUnit != null).OrderBy(r => r.Code).ThenBy(r => r.Id)
                .Select(r => new PcResourceRules.DraftResource { Id = r.Id, Code = r.Code, Kind = r.Kind, Unit = r.Unit, ResponsibleUnit = r.ResponsibleUnit }).ToListAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcResourceRules.DraftRow row in PcResourceRules.Draft(draftActivities, pool))
                plan.Assignments.Add(new PcActivityResource { ActivityId = row.ActivityId, ResourceId = row.ResourceId, Quantity = row.Quantity, Distribution = PcResourceDistribution.Uniform, ResponsibleRole = row.ResponsibleRole, Source = PcAssignmentSource.Agent, EditedByUserId = Me, EditedAt = now });
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcResourceText.OpenPlanExists); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        /// <summary>The plan for an edit: tracked, editable (draft or returned) and, after the baseline, only a version that an approved change request opened.</summary>
        private async Task<(PcResourcePlan Plan, string Error)> EditablePlanAsync(int planId, CancellationToken ct)
        {
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return (null, PcResourceText.NotAllowed);
            PcResourcePlan plan = await _uow.Set<PcResourcePlan>().FirstOrDefaultAsync(p => p.Id == planId && !p.Deleted, ct);
            if (plan == null) return (null, PcResourceText.PlanNotFound);
            if (plan.Status != PcResourcePlanStatus.Draft && plan.Status != PcResourcePlanStatus.Returned) return (null, PcResourceText.NotEditable);
            if (plan.ChangeRequestId == null) { if (await PcPlanLock.IsLockedAsync(_uow, plan.ContractId, ct)) return (null, PcResourceText.Locked); }
            else
            {
                // a version opened by a change request is worked on only while that request is approved and being carried out
                string change = await PcResourceChange.RequestProblemAsync(_uow, plan.ChangeRequestId.Value, ct);
                if (change != null) return (null, change);
            }
            return (plan, null);
        }

        /// <summary>A person who edits the plan becomes its maker (the plan stays "prepared by the agent" for the history).</summary>
        private void Touch(PcResourcePlan plan, DateTimeOffset now)
        {
            plan.MakerUserId = Me; plan.EditorUserIds = PcResourceRules.AddEditor(plan.EditorUserIds, Me); plan.ModifyDate = now; plan.ModifierUserId = Me;
        }

        public async Task<ResponseDto<PcResourcePlanDto>> SaveAssignmentsAsync(PcActivityAssignmentsDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourcePlanDto>(PcText.InvalidData);
            var editable = await EditablePlanAsync(dto.PlanId, cancellationToken);
            if (editable.Error != null) return editable.Error == PcResourceText.PlanNotFound ? NotFound<PcResourcePlanDto>(editable.Error) : Bad<PcResourcePlanDto>(editable.Error);
            PcResourcePlan plan = editable.Plan;
            if (!await _uow.Set<PcActivity>().AnyAsync(a => a.Id == dto.ActivityId && a.ContractId == plan.ContractId && !a.Deleted, cancellationToken)) return Bad<PcResourcePlanDto>(PcResourceText.ActivityNotFound);
            List<PcAssignmentLineDto> lines = dto.Lines ?? new List<PcAssignmentLineDto>();
            if (lines.GroupBy(l => l.ResourceId).Any(g => g.Count() > 1)) return Bad<PcResourcePlanDto>("یک منبع بیش از یک بار در فهرست آمده است.");
            int[] resourceIds = lines.Select(l => l.ResourceId).ToArray();
            Dictionary<int, PcResource> resources = await _uow.Set<PcResource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id) && !r.Deleted).ToDictionaryAsync(r => r.Id, cancellationToken);
            List<PcActivityResource> existing = await _uow.Set<PcActivityResource>().Where(x => x.PlanId == plan.Id && x.ActivityId == dto.ActivityId).ToListAsync(cancellationToken);
            foreach (PcAssignmentLineDto l in lines)
            {
                if (!resources.TryGetValue(l.ResourceId, out PcResource res)) return Bad<PcResourcePlanDto>(PcResourceText.ResourceNotFound);
                bool already = existing.Any(x => x.ResourceId == l.ResourceId);
                if (!res.IsActive && !already) return Bad<PcResourcePlanDto>("منبع «" + res.Code + "» غیرفعال است.");
                if (l.Quantity <= 0m || l.Quantity > PcResourceRules.MaxQuantity) return Bad<PcResourcePlanDto>("مقدار منبع «" + res.Code + "» باید بیشتر از صفر باشد.");
                if (l.Distribution.HasValue && !Enum.IsDefined(typeof(PcResourceDistribution), l.Distribution.Value)) return Bad<PcResourcePlanDto>("نحوه توزیع نامعتبر است.");
                string role = Clean(l.ResponsibleRole);
                if (role != null && role.Length > 100) return Bad<PcResourcePlanDto>("نقش مسئول حداکثر ۱۰۰ نویسه است.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcAssignmentLineDto l in lines)
            {
                PcActivityResource row = existing.FirstOrDefault(x => x.ResourceId == l.ResourceId);
                if (row == null)
                {
                    row = new PcActivityResource { PlanId = plan.Id, ActivityId = dto.ActivityId, ResourceId = l.ResourceId };
                    _uow.Set<PcActivityResource>().Add(row);
                }
                row.Quantity = PcResourceRules.Round4(l.Quantity); row.Distribution = l.Distribution ?? (row.Distribution == 0 ? PcResourceDistribution.Uniform : row.Distribution);
                row.ResponsibleRole = Clean(l.ResponsibleRole); row.Source = PcAssignmentSource.Human; row.EditedByUserId = Me; row.EditedAt = now;
            }
            if (dto.RemoveOthers) _uow.RemoveRange(existing.Where(x => !resourceIds.Contains(x.ResourceId)).ToList());
            if (lines.Count > 0)
            {
                // an activity that gets a resource is no longer «بدون منبع»
                List<PcResourcePlanSkip> marks = await _uow.Set<PcResourcePlanSkip>().Where(s => s.PlanId == plan.Id && s.ActivityId == dto.ActivityId).ToListAsync(cancellationToken);
                _uow.RemoveRange(marks);
            }
            Touch(plan, now);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        public async Task<ResponseDto<PcResourcePlanDto>> DeleteAssignmentAsync(int id, CancellationToken cancellationToken)
        {
            PcActivityResource row = await _uow.Set<PcActivityResource>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row == null) return NotFound<PcResourcePlanDto>("منبع فعالیت پیدا نشد.");
            var editable = await EditablePlanAsync(row.PlanId, cancellationToken);
            if (editable.Error != null) return editable.Error == PcResourceText.PlanNotFound ? NotFound<PcResourcePlanDto>(editable.Error) : Bad<PcResourcePlanDto>(editable.Error);
            _uow.Set<PcActivityResource>().Remove(row);
            Touch(editable.Plan, DateTimeOffset.Now);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(editable.Plan, true, cancellationToken));
        }

        public async Task<ResponseDto<PcResourcePlanDto>> MarkUnassignedAsync(PcMarkUnassignedDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourcePlanDto>(PcText.InvalidData);
            var editable = await EditablePlanAsync(dto.PlanId, cancellationToken);
            if (editable.Error != null) return editable.Error == PcResourceText.PlanNotFound ? NotFound<PcResourcePlanDto>(editable.Error) : Bad<PcResourcePlanDto>(editable.Error);
            PcResourcePlan plan = editable.Plan;
            DateTimeOffset now = DateTimeOffset.Now;
            List<PcResourcePlanSkip> marks = await _uow.Set<PcResourcePlanSkip>().Where(s => s.PlanId == plan.Id).ToListAsync(cancellationToken);
            List<int> needing = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == plan.ContractId && !a.Deleted && a.DurationDays > 0).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync(cancellationToken);
            HashSet<int> assigned = new HashSet<int>(await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id).Select(x => x.ActivityId).Distinct().ToListAsync(cancellationToken));
            if (dto.Unmark)
            {
                HashSet<int> ids = new HashSet<int>(dto.ActivityIds ?? new List<int>());
                _uow.RemoveRange(marks.Where(m => dto.AllRemaining || ids.Contains(m.ActivityId)).ToList());
            }
            else
            {
                List<int> targets = dto.AllRemaining ? needing.Where(id => !assigned.Contains(id)).ToList() : (dto.ActivityIds ?? new List<int>()).Distinct().ToList();
                if (targets.Count == 0) return Bad<PcResourcePlanDto>("فعالیتی برای علامت‌گذاری «بدون منبع» نیست.");
                int[] targetIds = targets.ToArray();
                HashSet<int> valid = new HashSet<int>(await _uow.Set<PcActivity>().AsNoTracking().Where(a => targetIds.Contains(a.Id) && a.ContractId == plan.ContractId && !a.Deleted).Select(a => a.Id).ToListAsync(cancellationToken));
                foreach (int id in targets)
                {
                    if (!valid.Contains(id)) return Bad<PcResourcePlanDto>(PcResourceText.ActivityNotFound);
                    if (assigned.Contains(id)) return Bad<PcResourcePlanDto>("فعالیتی که منبع دارد «بدون منبع» نمی‌شود؛ ابتدا منبع آن را بردارید.");
                    if (marks.Any(m => m.ActivityId == id)) continue;
                    _uow.Set<PcResourcePlanSkip>().Add(new PcResourcePlanSkip { PlanId = plan.Id, ActivityId = id, MarkedByUserId = Me, MarkedAt = now });
                }
            }
            Touch(plan, now);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        public async Task<ResponseDto<PcResourcePlanDto>> DiscardPlanAsync(PcResourcePlanDiscardDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourcePlanDto>(PcText.InvalidData);
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourcePlanDto>(PcResourceText.NotAllowed);
            PcResourcePlan plan = await _uow.Set<PcResourcePlan>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, cancellationToken);
            if (plan == null) return NotFound<PcResourcePlanDto>(PcResourceText.PlanNotFound);
            if (plan.Status == PcResourcePlanStatus.Approved) return Bad<PcResourcePlanDto>("طرح تاییدشده دور انداخته نمی‌شود؛ فقط طرح باز (پیش‌نویس، ارسال‌شده یا برگشت‌خورده) دور انداخته می‌شود.");
            if (plan.ChangeRequestId.HasValue) return Bad<PcResourcePlanDto>("نسخه‌ای که با درخواست تغییر باز شده دور انداخته نمی‌شود.");
            if (await PcPlanLock.IsLockedAsync(_uow, plan.ContractId, cancellationToken)) return Bad<PcResourcePlanDto>(PcResourceText.Locked);
            string reason = Clean(dto.Reason);
            if (reason != null && reason.Length > 900) return Bad<PcResourcePlanDto>("علت حداکثر ۹۰۰ نویسه است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcResourcePlanDto before = await PlanDtoAsync(plan, true, cancellationToken);
            plan.ReturnReason = reason == null ? "دور انداخته شد." : "دور انداخته شد: " + reason;
            plan.Delete(); plan.ModifyDate = now; plan.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            before.Editable = false;
            return ResponseDtoHelper.Ok(before);
        }

        /// <summary>The project manager of the contract decides on the plan; a contract without one is decided by the PMO (the rule of the payment authorisation of a progress statement).</summary>
        private async Task<string> DeciderProblemAsync(PcResourcePlan plan, CancellationToken ct)
        {
            if (IsAi) return PcResourceText.AiDecision;
            int? manager = await ProjectManagerAsync(plan.ContractId, ct);
            // a project manager who made or edited the plan cannot decide it; then (like a contract without a manager) the PMO decides
            bool managerBarred = manager.HasValue && (plan.MakerUserId == manager.Value || PcResourceRules.IsEditor(plan.EditorUserIds, manager.Value));
            bool allowed = manager.HasValue && !managerBarred ? manager.Value == Me : _user.IsInRole(ProjectControlRoles.Pmo);
            if (!allowed) return PcResourceText.NotAllowed + " (مدیر پروژه همین قرارداد)";
            if (plan.MakerUserId == Me || PcResourceRules.IsEditor(plan.EditorUserIds, Me)) return PcResourceText.SameUser + " (سازنده یا ویرایش‌کننده طرح نمی‌تواند آن را تایید یا برگشت دهد)";
            return null;
        }

        public async Task<ResponseDto<PcResourcePlanDto>> PlanActionAsync(PcResourcePlanActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcResourcePlanDto>(PcText.InvalidData);
            PcResourcePlan plan = await _uow.Set<PcResourcePlan>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, cancellationToken);
            if (plan == null) return NotFound<PcResourcePlanDto>(PcResourceText.PlanNotFound);
            if (!string.IsNullOrEmpty(dto.RowVersion)) _uow.Entry(plan).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                {
                    if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcResourcePlanDto>(PcResourceText.NotAllowed + " (ارسال طرح با برنامه‌ریز انسانی است)");
                    if (plan.Status != PcResourcePlanStatus.Draft && plan.Status != PcResourcePlanStatus.Returned) return Bad<PcResourcePlanDto>("فقط طرح پیش‌نویس یا برگشت‌خورده ارسال می‌شود.");
                    if (plan.ChangeRequestId == null) { if (await PcPlanLock.IsLockedAsync(_uow, plan.ContractId, cancellationToken)) return Bad<PcResourcePlanDto>(PcResourceText.Locked); }
                    else
                    {
                        string change = await PcResourceChange.RequestProblemAsync(_uow, plan.ChangeRequestId.Value, cancellationToken);
                        if (change != null) return Bad<PcResourcePlanDto>(change);
                    }
                    PcResourcePlanDto state = await PlanDtoAsync(plan, false, cancellationToken);
                    if (state.UnassignedCount > 0) return Bad<PcResourcePlanDto>($"برای {state.UnassignedCount} فعالیت هنوز منبعی ثبت نشده است؛ برای هر فعالیت منبع بگذارید یا آن را «بدون منبع» علامت بزنید.");
                    plan.Status = PcResourcePlanStatus.Submitted; plan.SubmittedAt = now; Touch(plan, now);
                    plan.ReturnReason = null;
                    break;
                }
                case "approve":
                {
                    string problem = await DeciderProblemAsync(plan, cancellationToken);
                    if (problem != null) return Bad<PcResourcePlanDto>(problem);
                    if (plan.Status != PcResourcePlanStatus.Submitted) return Bad<PcResourcePlanDto>("فقط طرح ارسال‌شده تایید می‌شود.");
                    if (plan.RowVersion != null && string.IsNullOrEmpty(dto.RowVersion)) return Bad<PcResourcePlanDto>(PcResourceText.RowVersionMissing);
                    PcBaseline changeBaseline = null;
                    if (plan.ChangeRequestId.HasValue)
                    {
                        string change = await PcResourceChange.RequestProblemAsync(_uow, plan.ChangeRequestId.Value, cancellationToken);
                        if (change != null) return Bad<PcResourcePlanDto>(change);
                        var built = await PcResourceChange.BaselineAsync(_uow, plan.ContractId, plan.ChangeRequestId.Value, cancellationToken);
                        if (built.Error != null) return Bad<PcResourcePlanDto>(built.Error);
                        changeBaseline = built.Baseline;
                    }
                    plan.Status = PcResourcePlanStatus.Approved; plan.ApprovedAt = now; plan.ApprovedByUserId = Me; plan.ModifyDate = now; plan.ModifierUserId = Me;
                    if (changeBaseline != null) await ReplaceBaselineRowsAsync(plan, changeBaseline, now, cancellationToken);
                    break;
                }
                case "return":
                {
                    string problem = await DeciderProblemAsync(plan, cancellationToken);
                    if (problem != null) return Bad<PcResourcePlanDto>(problem);
                    if (plan.Status != PcResourcePlanStatus.Submitted) return Bad<PcResourcePlanDto>("فقط طرح ارسال‌شده برگشت می‌خورد.");
                    if (plan.RowVersion != null && string.IsNullOrEmpty(dto.RowVersion)) return Bad<PcResourcePlanDto>(PcResourceText.RowVersionMissing);
                    string reason = Clean(dto.Note);
                    if (reason == null) return Bad<PcResourcePlanDto>("علت برگشت طرح منابع را بنویسید.");
                    if (reason.Length > 1000) return Bad<PcResourcePlanDto>("علت برگشت حداکثر ۱۰۰۰ نویسه است.");
                    plan.Status = PcResourcePlanStatus.Returned; plan.ReturnedAt = now; plan.ReturnedByUserId = Me; plan.ReturnReason = reason; plan.ModifyDate = now; plan.ModifierUserId = Me;
                    break;
                }
                default:
                    return Bad<PcResourcePlanDto>("عملیات نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcResourcePlanDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await PlanDtoAsync(plan, true, cancellationToken));
        }

        /// <summary>A version that an approved change request opened replaces the resource rows frozen with the baseline that the request built (and with no other baseline).</summary>
        private async Task ReplaceBaselineRowsAsync(PcResourcePlan plan, PcBaseline baseline, DateTimeOffset now, CancellationToken ct)
        {
            _uow.RemoveRange(await _uow.Set<PcBaselineResource>().Where(x => x.BaselineId == baseline.Id).ToListAsync(ct));
            HashSet<int> inPlan = new HashSet<int>(await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(a => a.BaselineId == baseline.Id).Select(a => a.ActivityId).ToListAsync(ct));
            await SnapshotOfPlanAsync(plan, baseline, inPlan, now.Date, ct);
        }

        private async Task SnapshotOfPlanAsync(PcResourcePlan plan, PcBaseline baseline, ISet<int> activityIds, DateTime asOf, CancellationToken ct)
        {
            List<PcActivityResource> rows = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id && !x.Activity.Deleted).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(ct);
            rows = rows.Where(r => activityIds.Contains(r.ActivityId)).ToList();
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(_uow, rows.Select(r => r.ResourceId), asOf, ct);
            foreach (PcActivityResource r in rows)
                _uow.Set<PcBaselineResource>().Add(new PcBaselineResource { BaselineId = baseline.Id, ActivityId = r.ActivityId, ResourceId = r.ResourceId, Quantity = r.Quantity, Distribution = r.Distribution, RateAtBaseline = rates.TryGetValue(r.ResourceId, out decimal? rate) ? rate : null });
        }

        // ============================================================================================== budget view and baseline rows

        public async Task<ResponseDto<PcResourceBudgetDto>> GetBudgetAsync(int planId, DateTime? asOf, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourceBudgetDto>(PcResourceText.NotAllowed);
            PcResourcePlan plan = await _uow.Set<PcResourcePlan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && !p.Deleted, cancellationToken);
            if (plan == null) return NotFound<PcResourceBudgetDto>(PcResourceText.PlanNotFound);
            DateTime day = (asOf ?? DateTime.Today).Date;
            var rows = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id && !x.Activity.Deleted).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId)
                .Select(x => new { x.Id, x.ActivityId, ActivityCode = x.Activity.Code, x.ResourceId, x.Quantity }).ToListAsync(cancellationToken);
            int[] ids = rows.Select(r => r.ResourceId).Distinct().ToArray();
            Dictionary<int, PcResource> resources = ids.Length == 0 ? new Dictionary<int, PcResource>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(_uow, ids, day, cancellationToken);
            bool sees = MfPrivacy.SeesPayroll(_user);
            var dto = new PcResourceBudgetDto { PlanId = plan.Id, ContractId = plan.ContractId, AsOf = day };
            var visible = new List<(PcResource Resource, decimal Quantity, decimal Amount)>();
            foreach (var r in rows)
            {
                PcResource res = resources[r.ResourceId];
                decimal? rate = rates.TryGetValue(r.ResourceId, out decimal? v) ? v : null;
                bool hidden = !sees && PcResourceRules.IsSensitive(res);
                var line = new PcResourceBudgetLineDto
                {
                    AssignmentId = r.Id, ActivityId = r.ActivityId, ActivityCode = r.ActivityCode, ResourceId = res.Id, ResourceCode = res.Code, ResourceTitle = res.Title,
                    ResourceClass = PcResourceRules.ClassOf(res.Kind), Quantity = r.Quantity, NoApprovedRate = !rate.HasValue, Note = rate.HasValue ? null : "بدون نرخ مصوب", RateHidden = hidden,
                };
                if (!rate.HasValue) dto.NoRateCount++;
                else if (hidden) dto.HiddenLineCount++;
                else
                {
                    line.Rate = rate; line.Amount = PcResourceRules.Round2(r.Quantity * rate.Value);
                    visible.Add((res, r.Quantity, line.Amount.Value));
                }
                dto.Lines.Add(line);
            }
            dto.RateHidden = dto.HiddenLineCount > 0;
            dto.ByResource = visible.GroupBy(x => x.Resource.Id).OrderBy(g => g.First().Resource.Code, StringComparer.Ordinal).Select(g => new PcResourceBudgetGroupDto
            {
                ResourceId = g.Key, ResourceCode = g.First().Resource.Code, ResourceTitle = g.First().Resource.Title, ResourceClass = PcResourceRules.ClassOf(g.First().Resource.Kind),
                Quantity = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount), LineCount = g.Count(),
            }).ToList();
            dto.ByClass = visible.GroupBy(x => PcResourceRules.ClassOf(x.Resource.Kind)).OrderBy(g => (int)g.Key).Select(g => new PcResourceBudgetGroupDto
            {
                ResourceClass = g.Key, Quantity = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount), LineCount = g.Count(),
            }).ToList();
            dto.Total = visible.Sum(x => x.Amount);
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<List<PcBaselineResourceDto>>> GetBaselineResourcesAsync(int baselineId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<List<PcBaselineResourceDto>>(PcResourceText.NotAllowed);
            if (!await _uow.Set<PcBaseline>().AsNoTracking().AnyAsync(b => b.Id == baselineId && !b.Deleted, cancellationToken)) return NotFound<List<PcBaselineResourceDto>>("خط مبنا پیدا نشد.");
            var rows = await _uow.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == baselineId).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(cancellationToken);
            int[] activityIds = rows.Select(r => r.ActivityId).Distinct().ToArray();
            int[] resourceIds = rows.Select(r => r.ResourceId).Distinct().ToArray();
            Dictionary<int, string> codes = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(a => a.BaselineId == baselineId && activityIds.Contains(a.ActivityId)).ToDictionaryAsync(a => a.ActivityId, a => a.Code, cancellationToken);
            Dictionary<int, PcResource> resources = resourceIds.Length == 0 ? new Dictionary<int, PcResource>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            bool sees = MfPrivacy.SeesPayroll(_user);
            return ResponseDtoHelper.Ok(rows.Select(r =>
            {
                resources.TryGetValue(r.ResourceId, out PcResource res);
                bool hidden = !sees && PcResourceRules.IsSensitive(res);
                return new PcBaselineResourceDto
                {
                    BaselineId = r.BaselineId, ActivityId = r.ActivityId, ActivityCode = codes.TryGetValue(r.ActivityId, out string c) ? c : null, ResourceId = r.ResourceId, ResourceCode = res?.Code,
                    Quantity = r.Quantity, Distribution = r.Distribution, RateAtBaseline = hidden ? null : r.RateAtBaseline, RateHidden = hidden,
                };
            }).ToList());
        }
    }
}
