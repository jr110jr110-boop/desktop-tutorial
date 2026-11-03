using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfDashboardService
    {
        Task<ResponseDto<ProductionDashboardDto>> GetAsync(DashboardQuery query, CancellationToken ct);
    }

    /// <summary>The production manager's dashboard. Everything is computed from the records; nothing is typed in.</summary>
    public class MfDashboardService : IMfDashboardService
    {
        public const string Roles = "PRODUCTIONMANAGER,SHOPSUPERVISOR,CEO,PMO,PLANNING,MAINTENANCE,QC,QA,FINANCEMANAGER,AUDITOR";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public MfDashboardService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static readonly MfOrderStatus[] OpenStatuses = { MfOrderStatus.Released, MfOrderStatus.InProgress };

        /// <summary>First day of the Persian month of a date.</summary>
        internal static DateTime PersianMonthStart(DateTime day)
        {
            var calendar = new PersianCalendar();
            return calendar.ToDateTime(calendar.GetYear(day), calendar.GetMonth(day), 1, 0, 0, 0, 0).Date;
        }

        public async Task<ResponseDto<ProductionDashboardDto>> GetAsync(DashboardQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(Roles)) return MfText.Bad<ProductionDashboardDto>(MfText.NotAllowed);
            DateTime today = query?.AsOf?.Date ?? DateTime.Today;
            var d = new ProductionDashboardDto { AsOf = today, AsOfText = MfText.Date(today) };

            // late orders and kit shortages (the result of the last scheduler run)
            var orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && OpenStatuses.Contains(o.Status)).ToListAsync(ct);
            List<int> itemIds = orders.Where(o => o.ItemId.HasValue).Select(o => o.ItemId.Value).Distinct().ToList();
            Dictionary<int, string> itemCodes = itemIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
            DashboardOrderDto Row(MfProductionOrder o) => new DashboardOrderDto
            {
                OrderId = o.Id, Number = o.Number, ItemCode = o.ItemId.HasValue ? itemCodes.GetValueOrDefault(o.ItemId.Value) : null, DaysLate = o.DaysLate, NeedDateText = MfText.Date(o.NeedDate),
                PlannedFinishText = MfText.Date(o.PlannedFinish),
            };
            List<MfProductionOrder> late = orders.Where(o => o.IsLate).OrderByDescending(o => o.DaysLate).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
            d.LateOrders = late.Count;
            d.LateOrderList = late.Take(10).Select(Row).ToList();
            List<MfProductionOrder> shortages = orders.Where(o => o.KitShortage).OrderBy(o => o.Number, StringComparer.Ordinal).ToList();
            d.KitShortages = shortages.Count;
            d.KitShortageList = shortages.Take(10).Select(Row).ToList();
            d.LastScheduleRun = await _uow.Set<MfScheduleRun>().AsNoTracking().OrderByDescending(r => r.Id).Select(r => (DateTimeOffset?)r.RunAt).FirstOrDefaultAsync(ct) is DateTimeOffset at ? at.LocalDateTime : (DateTime?)null;
            d.LastScheduleRunText = MfText.Stamp(d.LastScheduleRun);

            // inspection requests waiting for more than two days
            DateTimeOffset limit = new DateTimeOffset(today.AddDays(-2));
            var holds = await (from o in _uow.Set<MfOperation>().AsNoTracking()
                               join p in _uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals p.Id
                               where !o.Deleted && o.Status == MfOperationStatus.QcHold && !o.QcAccepted && o.QcRequestedAt != null && o.QcRequestedAt < limit
                               orderby o.QcRequestedAt
                               select new { o.Id, p.Number, o.Sequence, o.Title, o.QcRequestedAt }).ToListAsync(ct);
            d.QcHoldOverTwoDays = holds.Count;
            d.QcHoldList = holds.Take(10).Select(h => new DashboardOperationDto
            {
                OperationId = h.Id, JobCard = MfNames.Operation(h.Number, h.Sequence), Title = h.Title, DaysOnHold = (int)(today - h.QcRequestedAt.Value.LocalDateTime.Date).TotalDays, RequestedAtText = MfText.Stamp(h.QcRequestedAt),
            }).ToList();

            // load: the next 12 weeks against the capacity
            LoadReportDto load = await MfScheduleService.LoadAsync(_uow, today, 12, 100, null, ct);
            d.OverloadWeeks = load.Overloads.Count;
            d.OverloadList = load.Overloads.Take(10).ToList();

            // machines and maintenance
            var downs = await (from i in _uow.Set<MfMachineInfo>().AsNoTracking()
                               join m in _uow.Set<Machine>().AsNoTracking() on i.MachineId equals m.Id
                               join c in _uow.Set<MfWorkCenter>().AsNoTracking() on i.WorkCenterId equals c.Id into cg
                               from c in cg.DefaultIfEmpty()
                               where !i.Deleted && i.Status == MfMachineStatus.Down
                               orderby m.Title
                               select new DashboardMachineDto { MachineId = m.Id, Title = m.Title, WorkCenterCode = c == null ? null : c.Code, Status = "Down" }).ToListAsync(ct);
            d.MachinesDown = downs.Count;
            d.MachinesDownList = downs.Take(10).ToList();
            d.MaintenanceOverdue = await _uow.Set<MfMaintenanceOrder>().CountAsync(o => !o.Deleted && (o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress)
                && o.DueDate != null && o.DueDate < today, ct);

            // the month so far: pieces, scrap and OEE
            DateTime monthStart = PersianMonthStart(today);
            DateTime end = today.AddDays(1);
            var qty = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.StartAt >= monthStart && b.StartAt < end)
                .GroupBy(b => 1).Select(g => new { Good = g.Sum(b => b.GoodQty), Scrap = g.Sum(b => b.ScrapQty) }).FirstOrDefaultAsync(ct);
            d.MonthGoodQty = qty?.Good ?? 0;
            d.MonthScrapQty = qty?.Scrap ?? 0;
            d.ScrapRatePercent = d.MonthGoodQty + d.MonthScrapQty > 0 ? MfKpi.Pct(d.MonthScrapQty, d.MonthGoodQty + d.MonthScrapQty) : (decimal?)null;
            d.Oee = await MfKpi.OeeAsync(_uow, monthStart, today, null, ct);
            d.OeePercent = MfKpi.Total(d.Oee).OeePercent;
            return ResponseDtoHelper.Ok(d);
        }

        /// <summary>The production figures of the CEO dashboard: late orders (last scheduler run), machines down, work-centre weeks above 110 % in the next four weeks, overdue maintenance.</summary>
        internal static async Task<ProductionCeoCounts> CeoCountsAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var counts = new ProductionCeoCounts();
            counts.LateOrders = await uow.Set<MfProductionOrder>().CountAsync(o => !o.Deleted && (o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress) && o.IsLate, ct);
            counts.MachinesDown = await uow.Set<MfMachineInfo>().CountAsync(i => !i.Deleted && i.Status == MfMachineStatus.Down, ct);
            counts.MaintenanceOverdue = await uow.Set<MfMaintenanceOrder>().CountAsync(o => !o.Deleted && (o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress)
                && o.DueDate != null && o.DueDate < today, ct);
            LoadReportDto load = await MfScheduleService.LoadAsync(uow, today, 4, 110, null, ct);
            counts.OverloadWeeks = load.Overloads.Count;
            counts.Load = load;
            return counts;
        }
    }
}
