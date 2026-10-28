using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvDashboardService
    {
        /// <param name="asOf">The moment SLA breaches and the warranty are judged at; now when empty (tests and what-if views).</param>
        Task<ResponseDto<SvDashboardDto>> GetAsync(DateTime? asOf, CancellationToken ct);

        Task<ResponseDto<SvKpiDto>> GetKpisAsync(SvKpiQuery query, CancellationToken ct);
    }

    /// <summary>The service dashboard, the KPIs, and the service numbers of the CEO dashboard.</summary>
    public class SvDashboardService : ISvDashboardService
    {
        private static readonly SvRequestStatus[] OpenStatuses = { SvRequestStatus.Open, SvRequestStatus.Assigned, SvRequestStatus.InProgress, SvRequestStatus.WaitingCustomer };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SvDashboardService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        /// <summary>
        /// The figures of the service dashboard. The warranty cost is a plain figure for every reader: its labour is absorbed at the pooled average rate (R1p D2), never one engineer's pay.
        /// The satisfaction average counts verified answers only (R1p D25: an answer typed in by the people it is about is not a figure until QA has checked it with the customer); the answered but unverified surveys are counted next to it.
        /// </summary>
        internal static async Task<SvDashboardDto> CoreAsync(IUnitOfWork uow, DateTime asOf, CancellationToken ct)
        {
            var dto = new SvDashboardDto { AsOf = asOf, AsOfText = SlText.Date(asOf) };
            List<SvServiceRequest> open = await uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && OpenStatuses.Contains(r.Status)).ToListAsync(ct);
            dto.OpenRequests = open.Count;
            dto.OpenByPriority = Enum.GetValues(typeof(SvPriority)).Cast<SvPriority>().OrderBy(p => p)
                .Select(p => new SvPriorityCountDto { Priority = p, PriorityText = SvText.Priority(p), Count = open.Count(r => r.Priority == p) }).ToList();
            dto.SlaBreaches = open.Count(r => SvSla.Breached(r.ResponseDue, r.FirstResponseAt, asOf) || SvSla.Breached(r.ResolutionDue, r.ResolvedAt, asOf));

            List<SvInstalledUnit> units = await uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted && !u.Decommissioned).ToListAsync(ct);
            HashSet<int> ltsa = await SvUnitService.UnderLtsaAsync(uow, asOf, ct);
            dto.UnitsInWarranty = units.Count(u => SvUnitService.StatusOf(u, ltsa, asOf) == SvUnitStatus.InWarranty);
            dto.UnitsUnderLtsa = units.Count(u => SvUnitService.StatusOf(u, ltsa, asOf) == SvUnitStatus.UnderLtsa);
            dto.UnitsOutOfWarranty = units.Count(u => SvUnitService.StatusOf(u, ltsa, asOf) == SvUnitStatus.OutOfWarranty);
            dto.UnitsToConfirm = units.Count(u => !u.IsConfirmed);

            DateTime yearStart = SvPersianYear.Start(asOf);
            DateTimeOffset from = new DateTimeOffset(yearStart, TimeZoneInfo.Local.GetUtcOffset(yearStart));
            var warranty = await uow.Set<SvServiceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.Decision == SvWarrantyDecision.Warranty && o.ClosedAt >= from)
                .Select(o => new { o.LabourAmount, o.PartsAmount }).ToListAsync(ct);
            dto.WarrantyOrdersYtd = warranty.Count;
            dto.WarrantyCostYtd = warranty.Sum(o => o.LabourAmount + o.PartsAmount);

            dto.OpenOrders = await uow.Set<SvServiceOrder>().AsNoTracking().CountAsync(o => !o.Deleted && (o.Status == SvOrderStatus.Draft || o.Status == SvOrderStatus.Planned || o.Status == SvOrderStatus.InProgress), ct);
            dto.OrdersWaitingClose = await uow.Set<SvServiceOrder>().AsNoTracking().CountAsync(o => !o.Deleted && o.Status == SvOrderStatus.InProgress, ct);
            dto.GoodwillWaiting = await uow.Set<SvServiceOrder>().AsNoTracking().CountAsync(o => !o.Deleted && o.GoodwillRequestedAt != null && o.Decision != SvWarrantyDecision.Goodwill && (o.Status == SvOrderStatus.Draft || o.Status == SvOrderStatus.Planned || o.Status == SvOrderStatus.InProgress), ct); // the same orders as the goodwill gate (SvGates.GoodwillAsync): every order that is not closed or cancelled
            dto.SurveysDrafted = await uow.Set<SlSurvey>().AsNoTracking().CountAsync(s => !s.Deleted && s.Status == SlSurveyStatus.Drafted, ct);
            // R1p D25: verified answers only; the unverified ones are counted, not averaged
            List<decimal> averages = await SvSurveyMath.VerifiedAnswers(uow.Set<SlSurvey>().AsNoTracking()).Select(s => s.AverageScore.Value).ToListAsync(ct);
            dto.SurveyAverageScore = averages.Count == 0 ? (decimal?)null : SlText.R2(averages.Average());
            dto.SurveysVerified = averages.Count;
            dto.SurveysUnverified = await SvSurveyMath.UnverifiedAnswers(uow.Set<SlSurvey>().AsNoTracking()).CountAsync(ct);
            return dto;
        }

        public async Task<ResponseDto<SvDashboardDto>> GetAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return SlText.Bad<SvDashboardDto>(SlText.NotAllowed);
            return ResponseDtoHelper.Ok(await CoreAsync(_uow, asOf ?? DateTime.Now, ct));
        }

        public async Task<ResponseDto<SvKpiDto>> GetKpisAsync(SvKpiQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return SlText.Bad<SvKpiDto>(SlText.NotAllowed);
            query ??= new SvKpiQuery();
            IQueryable<SvServiceRequest> rq = _uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (query.From.HasValue) rq = rq.Where(r => r.ReceivedAt >= query.From.Value);
            if (query.To.HasValue) rq = rq.Where(r => r.ReceivedAt < query.To.Value.Date.AddDays(1));
            List<SvServiceRequest> requests = await rq.ToListAsync(ct);
            var dto = new SvKpiDto { AsOf = (query.AsOf ?? DateTime.Now), RequestsCounted = requests.Count };
            dto.FirstResponseHours = SvKpiMath.AverageHours(requests.Where(r => r.FirstResponseAt.HasValue).Select(r => (r.ReceivedAt, r.FirstResponseAt.Value)));
            dto.ResolutionHours = SvKpiMath.AverageHours(requests.Where(r => r.ResolvedAt.HasValue).Select(r => (r.ReceivedAt, r.ResolvedAt.Value)));

            List<SvInstalledUnit> units = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted).ToListAsync(ct);
            Dictionary<int, SvInstalledUnit> byId = units.ToDictionary(u => u.Id);
            var failures = requests.Where(r => r.Type == SvRequestType.Breakdown && r.UnitId.HasValue && byId.ContainsKey(r.UnitId.Value)).GroupBy(r => r.UnitId.Value).ToList();
            dto.RepeatFailures = failures.Where(g => g.Count() > 1).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => new SvRepeatFailureDto { UnitId = g.Key, UnitTag = byId[g.Key].Tag, Failures = g.Count(), Repeats = SvKpiMath.Repeats(g.Count()) }).ToList();
            dto.TotalFailures = failures.Sum(g => g.Count());

            // R1p D36: MTBF = the hours the ACTIVE units ran inside the period / the failures of the same units in the same period. The hours come from the meter readings of the closed orders;
            // a unit whose period cannot be measured (no reading before the period, or none inside it) is left out of the hours and of the failures, and counted in MtbfUnitsExcluded.
            var readingRows = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.RunningHoursReading != null && o.ClosedAt != null)
                .Select(o => new { o.UnitId, o.ClosedAt, Reading = o.RunningHoursReading.Value }).ToListAsync(ct);
            var readingsByUnit = readingRows.GroupBy(r => r.UnitId).ToDictionary(g => g.Key, g => g.Select(r => (r.ClosedAt.Value.LocalDateTime.Date, r.Reading)).ToList());
            var periodHours = new Dictionary<int, decimal>();
            foreach (SvInstalledUnit u in units.Where(u => !u.Decommissioned))
            {
                decimal? h = SvKpiMath.PeriodHours(readingsByUnit.GetValueOrDefault(u.Id) ?? new List<(DateTime, decimal)>(), query.From, query.To, u.CommissioningDate ?? u.HandoverDate);
                if (h.HasValue) periodHours[u.Id] = h.Value;
            }
            dto.MtbfUnits = periodHours.Count;
            dto.MtbfUnitsExcluded = units.Count(u => !u.Decommissioned) - periodHours.Count;
            dto.TotalRunningHours = periodHours.Values.Sum();
            dto.MtbfFailures = failures.Where(g => periodHours.ContainsKey(g.Key)).Sum(g => g.Count());
            dto.MtbfHours = SvKpiMath.Mtbf(dto.TotalRunningHours, dto.MtbfFailures);

            var orders = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.Decision == SvWarrantyDecision.Warranty)
                .Select(o => new { o.UnitId, o.LabourAmount, o.PartsAmount }).ToListAsync(ct);
            foreach (var g in units.GroupBy(u => u.ProductType).OrderBy(g => g.Key))
            {
                var typeOrders = orders.Where(o => byId.ContainsKey(o.UnitId) && byId[o.UnitId].ProductType == g.Key).ToList();
                int count = failures.Where(f => byId[f.Key].ProductType == g.Key && periodHours.ContainsKey(f.Key)).Sum(f => f.Count());
                decimal hours = g.Where(u => periodHours.ContainsKey(u.Id)).Sum(u => periodHours[u.Id]);
                dto.ByProduct.Add(new SvProductKpiDto
                {
                    ProductType = g.Key, ProductTypeText = SlText.Product(g.Key), Units = g.Count(), RunningHours = hours, Failures = count, MtbfHours = SvKpiMath.Mtbf(hours, count),
                    WarrantyOrders = typeOrders.Count, WarrantyCost = typeOrders.Sum(o => o.LabourAmount + o.PartsAmount),
                });
            }
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
