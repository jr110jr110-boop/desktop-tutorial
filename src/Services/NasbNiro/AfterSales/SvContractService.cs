using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvContractService
    {
        Task<ResponseDto<PagedList<SvContractDto>>> GetContractsAsync(SvContractQuery query, CancellationToken ct);
        Task<ResponseDto<SvContractDto>> GetContractAsync(int id, CancellationToken ct);
        Task<ResponseDto<SvContractDto>> SaveContractAsync(SvSaveContractDto dto, CancellationToken ct);
        Task<ResponseDto<SvContractDto>> ContractActionAsync(SvContractActionDto dto, CancellationToken ct);

        /// <summary>Runs the jobs for a person (the day can be given for tests and what-if runs).</summary>
        Task<ResponseDto<SvJobsResultDto>> RunJobsAsync(SvJobsRunDto dto, CancellationToken ct);

        /// <summary>The nightly run of the hosted service (no user; the day is today).</summary>
        Task<SvJobsResultDto> RunNightlyAsync(CancellationToken ct);
    }

    /// <summary>LTSA contracts and the jobs of the module: periodic visits, delivery surveys, expiry and the SLA-breach alert. Every job is idempotent.</summary>
    public class SvContractService : ISvContractService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IConfiguration _configuration;

        public SvContractService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
            _configuration = configuration;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        // ================================================================================== contracts

        private async Task<List<SvContractDto>> DtosAsync(List<SvContract> rows, CancellationToken ct)
        {
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            List<int> unitIds = rows.SelectMany(r => r.Units.Select(u => u.UnitId)).Distinct().ToList();
            List<int> ids = rows.Select(r => r.Id).ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> tags = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Tag, ct);
            Dictionary<int, int> drafted = (await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.LtsaContractId != null && ids.Contains(o.LtsaContractId.Value)).GroupBy(o => o.LtsaContractId.Value)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
            return rows.Select(r => new SvContractDto
            {
                Id = r.Id, Number = r.Number, Title = r.Title, CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId), Status = r.Status, StatusText = SvText.ContractStatus(r.Status),
                StartDate = r.StartDate, StartDateText = SlText.Date(r.StartDate), EndDate = r.EndDate, EndDateText = SlText.Date(r.EndDate), Scope = r.Scope, VisitIntervalMonths = r.VisitIntervalMonths, PriceRial = r.PriceRial,
                NextVisitDate = r.NextVisitDate, NextVisitDateText = SlText.Date(r.NextVisitDate), UnitIds = r.Units.OrderBy(u => u.Id).Select(u => u.UnitId).ToList(),
                UnitTags = r.Units.OrderBy(u => u.Id).Select(u => tags.GetValueOrDefault(u.UnitId)).ToList(), DraftedVisits = drafted.GetValueOrDefault(r.Id), RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SvContractDto>>> GetContractsAsync(SvContractQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<PagedList<SvContractDto>>(SlText.NotAllowed);
            query ??= new SvContractQuery();
            IQueryable<SvContract> q = _uow.Set<SvContract>().AsNoTracking().Where(c => !c.Deleted);
            if (query.Status.HasValue) q = q.Where(c => c.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(c => c.CustomerId == query.CustomerId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Number.Contains(text) || c.Title.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(c => c.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SvContract> full = await _uow.Set<SvContract>().AsNoTracking().Include(c => c.Units).Where(c => ids.Contains(c.Id)).ToListAsync(ct);
                return await DtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SvContractDto>> GetContractAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SvContractDto>(SlText.NotAllowed);
            SvContract row = await _uow.Set<SvContract>().AsNoTracking().Include(c => c.Units).FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
            if (row == null) return SlText.NotFound<SvContractDto>("قرارداد خدمات پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SvContract> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SvContractDto>> SaveContractAsync(SvSaveContractDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvContractDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SvContractDto>("عنوان قرارداد خدمات را وارد کنید.");
            string tooLong = SvLimits.First((dto.Title, SvLimits.Title, "عنوان قرارداد خدمات"), (dto.Scope, SvLimits.LongText, "دامنه‌ی قرارداد"));
            if (tooLong != null) return Bad<SvContractDto>(tooLong);
            if (dto.EndDate.Date <= dto.StartDate.Date) return Bad<SvContractDto>("پایان قرارداد باید بعد از شروع آن باشد.");
            if (dto.VisitIntervalMonths <= 0) return Bad<SvContractDto>("فاصله‌ی بازدیدها (ماه) باید بیشتر از صفر باشد.");
            if (dto.PriceRial.HasValue && dto.PriceRial.Value < 0) return Bad<SvContractDto>("قیمت نمی‌تواند منفی باشد.");
            List<int> unitIds = (dto.UnitIds ?? new List<int>()).Distinct().ToList();
            if (unitIds.Count == 0) return Bad<SvContractDto>("حداقل یک واحد نصب‌شده انتخاب کنید.");
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SvContractDto>("مشتری پیدا نشد.");
            var units = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id) && !u.Deleted).Select(u => new { u.Id, u.CustomerId, u.Decommissioned }).ToListAsync(ct);
            if (units.Count != unitIds.Count) return Bad<SvContractDto>("یکی از واحدها پیدا نشد.");
            if (units.Any(u => u.CustomerId.HasValue && u.CustomerId.Value != dto.CustomerId)) return Bad<SvContractDto>("یکی از واحدها مال مشتری انتخاب‌شده نیست.");
            if (units.Any(u => u.Decommissioned)) return Bad<SvContractDto>("واحد از رده خارج در قرارداد نمی‌آید.");

            DateTimeOffset now = DateTimeOffset.Now;
            SvContract row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SvContract>().Include(c => c.Units).FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return SlText.NotFound<SvContractDto>("قرارداد خدمات پیدا نشد.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvContractDto>(SlText.InvalidData);
                if (row.Status != SvContractStatus.Draft) return Bad<SvContractDto>("قرارداد فعال‌شده ویرایش نمی‌شود.");
            }
            else
            {
                row = new SvContract { Number = await InventoryNumbers.NextNumberAsync(_uow, SvNumbers.Contract, "LTSA", dto.StartDate.Date, ct), Status = SvContractStatus.Draft, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SvContract>().Add(row);
            }
            row.Title = Clean(dto.Title); row.CustomerId = dto.CustomerId; row.StartDate = dto.StartDate.Date; row.EndDate = dto.EndDate.Date; row.Scope = Clean(dto.Scope);
            row.VisitIntervalMonths = dto.VisitIntervalMonths; row.PriceRial = dto.PriceRial; row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me); row.ModifyDate = now; row.ModifierUserId = Me;
            row.NextVisitDate = SvLtsa.VisitDates(row.StartDate, row.EndDate, row.VisitIntervalMonths).Cast<DateTime?>().FirstOrDefault();
            foreach (SvContractUnit u in row.Units.Where(u => !unitIds.Contains(u.UnitId)).ToList()) { row.Units.Remove(u); _uow.Set<SvContractUnit>().Remove(u); }
            foreach (int id in unitIds.Where(id => row.Units.All(u => u.UnitId != id))) row.Units.Add(new SvContractUnit { UnitId = id });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvContractDto>(SlText.Concurrency); }
            await SvGates.ContractAsync(_notify, row, ct);
            return await GetContractAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SvContractDto>> ContractActionAsync(SvContractActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvContractDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SvContractDto>(SlText.InvalidData);
            SvContract row = await _uow.Set<SvContract>().Include(c => c.Units).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, ct);
            if (row == null) return SlText.NotFound<SvContractDto>("قرارداد خدمات پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvContractDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "activate":
                    if (row.Status != SvContractStatus.Draft) return Bad<SvContractDto>("فقط قرارداد پیش‌نویس فعال می‌شود.");
                    if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SvContractDto>(SlText.SameUser);
                    row.Status = SvContractStatus.Active; row.ActivatedByUserId = Me; row.ActivatedAt = now;
                    break;
                case "cancel":
                    if (row.Status != SvContractStatus.Draft && row.Status != SvContractStatus.Active) return Bad<SvContractDto>("این قرارداد لغو نمی‌شود.");
                    row.Status = SvContractStatus.Cancelled;
                    break;
                default:
                    return Bad<SvContractDto>("عملیات نامعتبر است.");
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvContractDto>(SlText.Concurrency); }
            await SvGates.ContractAsync(_notify, row, ct);
            return await GetContractAsync(row.Id, ct);
        }

        // ================================================================================== jobs

        public async Task<ResponseDto<SvJobsResultDto>> RunJobsAsync(SvJobsRunDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole("SERVICE,CEO")) return Bad<SvJobsResultDto>(SlText.NotAllowed);
            DateTime asOf = (dto?.AsOf ?? DateTime.Today).Date;
            // a day in the future changes real data (visits drafted, agreements expired): only when the switch is on (the smoke test turns it on)
            bool allowFuture = string.Equals(_configuration?[SvJobsGuard.AllowFutureKey], "true", StringComparison.OrdinalIgnoreCase);
            string refusal = SvJobsGuard.Refusal(asOf, DateTime.Today, allowFuture);
            if (refusal != null) return Bad<SvJobsResultDto>(refusal);
            return ResponseDtoHelper.Ok(await RunAsync(asOf, Me, ct));
        }

        public Task<SvJobsResultDto> RunNightlyAsync(CancellationToken ct) => RunAsync(DateTime.Today, 0, ct);

        private async Task<SvJobsResultDto> RunAsync(DateTime asOf, int userId, CancellationToken ct)
        {
            var result = new SvJobsResultDto { AsOf = asOf };
            int leadVisits = await SvSettings.IntAsync(_uow, SvSettingKeys.VisitLeadDays, SvSettingKeys.DefaultVisitLeadDays, ct);
            int leadSurvey = await SvSettings.IntAsync(_uow, SvSettingKeys.SurveyLeadDays, SvSettingKeys.DefaultSurveyLeadDays, ct);
            DateTimeOffset now = DateTimeOffset.Now;

            // 1) agreements that ended
            List<SvContract> ended = await _uow.Set<SvContract>().Where(c => !c.Deleted && c.Status == SvContractStatus.Active && c.EndDate < asOf).ToListAsync(ct);
            foreach (SvContract c in ended) { c.Status = SvContractStatus.Expired; c.NextVisitDate = null; c.ModifyDate = now; c.ModifierUserId = userId; }
            result.ContractsExpired = ended.Count;

            // 2) the periodic visits that are due become draft service orders (one per unit and visit date)
            List<SvContract> active = await _uow.Set<SvContract>().Include(c => c.Units).Where(c => !c.Deleted && c.Status == SvContractStatus.Active && c.EndDate >= asOf).ToListAsync(ct);
            string center = null;
            foreach (SvContract c in active)
            {
                DateTime activated = (c.ActivatedAt ?? now).LocalDateTime.Date;
                List<DateTime> visits = SvLtsa.VisitDates(c.StartDate, c.EndDate, c.VisitIntervalMonths).Where(d => d > activated).ToList();
                List<SvServiceOrder> existing = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.LtsaContractId == c.Id && !o.Deleted && o.Status != SvOrderStatus.Cancelled).ToListAsync(ct);
                foreach (DateTime visit in visits.Where(v => SvLtsa.Due(v, asOf, leadVisits)))
                    foreach (SvContractUnit cu in c.Units.OrderBy(u => u.Id))
                    {
                        if (existing.Any(o => o.UnitId == cu.UnitId && o.PlannedStart == visit)) continue;
                        SvInstalledUnit unit = await _uow.Set<SvInstalledUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == cu.UnitId && !u.Deleted, ct);
                        if (unit == null || unit.Decommissioned) continue;
                        center ??= await SvSettings.EnsureCostCenterAsync(_uow, ct);
                        var order = new SvServiceOrder
                        {
                            Number = await InventoryNumbers.NextNumberAsync(_uow, SvNumbers.Order, "SVO", asOf, ct), UnitId = unit.Id, CustomerId = c.CustomerId, ContractId = unit.ContractId, LtsaContractId = c.Id,
                            Kind = SvOrderKind.Field, Status = SvOrderStatus.Draft, Title = $"بازدید دوره‌ای {c.Number} - {unit.Tag} - {SlText.Date(visit)}", PlannedStart = visit, PlannedEnd = visit, WarrantyTestedOn = visit,
                            CostCenterCode = center, DraftNote = "پیش‌نویس سیستم از بازدید دوره‌ای قرارداد خدمات؛ مهندسان را انتخاب و دستور را تأیید کنید.",
                            CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId,
                        };
                        _uow.Set<SvServiceOrder>().Add(order);
                        existing.Add(order);
                        result.VisitsDrafted++;
                    }
                // the next visit without a full set of orders
                c.NextVisitDate = visits.Cast<DateTime?>().FirstOrDefault(v => c.Units.Any(cu => !existing.Any(o => o.UnitId == cu.UnitId && o.PlannedStart == v.Value)));
            }

            // 3) delivery surveys: the units the system drafted at a handover, once the handover is old enough
            var handovers = await _uow.Set<SvInstalledUnit>().AsNoTracking()
                .Where(u => !u.Deleted && u.ShipmentId != null && u.CustomerId != null && u.HandoverDate != null).Select(u => new { u.ShipmentId, u.CustomerId, u.ContractId, u.HandoverDate }).ToListAsync(ct);
            foreach (var g in handovers.GroupBy(h => h.ShipmentId.Value).OrderBy(g => g.Key))
            {
                var first = g.OrderBy(h => h.HandoverDate).First();
                if (first.HandoverDate.Value.Date.AddDays(leadSurvey) > asOf) continue;
                if (await SvSurveyDraft.ForShipmentAsync(_uow, g.Key, first.CustomerId.Value, first.ContractId, asOf, userId, ct)) result.SurveysDrafted++;
            }

            await _uow.SaveChangesAsync(ct);

            // 4) the SLA-breach alert of the open requests
            List<SvServiceRequest> open = await _uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status != SvRequestStatus.Closed && r.Status != SvRequestStatus.Resolved).ToListAsync(ct);
            foreach (SvServiceRequest r in open)
            {
                bool breached = SvSla.Breached(r.ResponseDue, r.FirstResponseAt, asOf) || SvSla.Breached(r.ResolutionDue, r.ResolvedAt, asOf);
                await SvGates.BreachAsync(_notify, r, breached, ct);
                if (breached) result.BreachAlerts++;
            }

            // 5) the units the system drafted at a handover wait in the inbox of service until a person confirms them (the handover itself runs in the project-control module, which has no inbox)
            List<SvInstalledUnit> waiting = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted && !u.IsConfirmed && !u.Decommissioned).ToListAsync(ct);
            foreach (SvInstalledUnit u in waiting) await SvGates.UnitAsync(_notify, u, ct);

            // 6) the visit orders and the surveys the system drafted wait for a person (confirm the engineers; send or skip), and their gates close when the person acted
            List<SvServiceOrder> visitOrders = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.LtsaContractId != null && o.Status == SvOrderStatus.Draft).ToListAsync(ct);
            foreach (SvServiceOrder o in visitOrders) await SvGates.VisitAsync(_notify, o, ct);
            List<SlSurvey> drafted = await _uow.Set<SlSurvey>().AsNoTracking().Where(x => !x.Deleted && x.Status == SlSurveyStatus.Drafted).ToListAsync(ct);
            foreach (SlSurvey x in drafted) await SvGates.SurveyAsync(_notify, x, ct);
            return result;
        }
    }
}
