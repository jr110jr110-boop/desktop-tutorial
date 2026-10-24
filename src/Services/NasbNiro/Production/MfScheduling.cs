using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Production
{
    /// <summary>A work centre as the scheduler sees it: its working calendar, hours per day and the maintenance downtime per day.</summary>
    internal sealed class MfCenter
    {
        public MfWorkCenter Wc;
        public PcWorkCalendar Calendar;

        /// <summary>Shifts x hours per shift of ONE resource.</summary>
        public decimal ShiftHours;

        /// <summary>Shift hours x parallel resources.</summary>
        public decimal Gross;

        /// <summary>Efficiency x utilisation (both as fractions).</summary>
        public decimal Factor;

        /// <summary>Hours of downtime per day (maintenance, breakdowns, machines that are down), summed over the machines of the work centre.</summary>
        public Dictionary<DateTime, decimal> Down = new Dictionary<DateTime, decimal>();

        /// <summary>The first shift of a day starts at this hour (★ assumption: the work centre has no shift start time); the shifts follow each other without a gap.</summary>
        public const decimal FirstShiftStartHour = 8m;

        /// <summary>Hours of a day the work centre could produce when nothing is down: shifts x hours x parallel resources x efficiency x utilisation, on working days only.</summary>
        public decimal FullCapacity(DateTime day) => Calendar.IsWorking(day.Date) ? Gross * Factor : 0;

        /// <summary>
        /// Available hours of a day, as the brief states it: shifts x hours x parallel resources x efficiency x utilisation - downtime hours (the downtime as clock hours inside the
        /// shifts), on working days only; never below zero.
        /// </summary>
        public decimal Capacity(DateTime day)
        {
            day = day.Date;
            if (!Calendar.IsWorking(day)) return 0;
            Down.TryGetValue(day, out decimal down);
            return Math.Max(0, Gross * Factor - down);
        }

        /// <summary>The time of a calendar day the shifts of one resource cover (one block from the first shift start; moved earlier when the shifts would run past midnight).</summary>
        public (DateTime From, DateTime To) ShiftWindow(DateTime day)
        {
            decimal total = Math.Min(Math.Max(ShiftHours, 0), 24m);
            decimal start = Math.Min(FirstShiftStartHour, 24m - total);
            return (day.Date.AddHours((double)start), day.Date.AddHours((double)(start + total)));
        }

        /// <summary>Hours of the interval [from, to) that lie inside the shift window of a day.</summary>
        public decimal ShiftOverlapHours(DateTime day, DateTime from, DateTime to)
        {
            var (a, b) = ShiftWindow(day);
            DateTime x = from > a ? from : a, y = to < b ? to : b;
            return y > x ? (decimal)(y - x).TotalHours : 0;
        }
    }

    internal sealed class MfOpPlan
    {
        public MfOperation Op;

        /// <summary>The hours still to be placed (planned less booked for a started operation).</summary>
        public decimal Hours;

        /// <summary>Done, skipped or rejected: not scheduled again.</summary>
        public bool Completed;

        public DateTime? Start;
        public DateTime? Finish;
        public string Note;
        public decimal MoveDays;
    }

    internal sealed class MfOrderPlan
    {
        public MfProductionOrder Order;
        public int Priority;
        public DateTime? Need;
        public List<MfOpPlan> Ops = new List<MfOpPlan>();
        public DateTime? Start;
        public DateTime? Finish;
        public bool IsLate;
        public int DaysLate;
        public DateTime? KitReady;
        public bool KitShortage;
        public string Note;
        public PcActivity Activity;
    }

    internal sealed class MfScheduleData
    {
        public DateTime AsOf;
        public List<MfOrderPlan> Orders = new List<MfOrderPlan>();
        public Dictionary<int, MfCenter> Centers = new Dictionary<int, MfCenter>();

        /// <summary>Hours the finite plan put on a work centre and day.</summary>
        public Dictionary<(int, DateTime), decimal> Scheduled = new Dictionary<(int, DateTime), decimal>();

        /// <summary>Hours the open orders need on a work centre and day when nothing waits for capacity.</summary>
        public Dictionary<(int, DateTime), decimal> Demand = new Dictionary<(int, DateTime), decimal>();
    }

    /// <summary>
    /// The finite-capacity scheduler (deterministic: the same records and the same "today" give the same dates). Operations of released orders are placed forward, order by order in
    /// priority (CPM total float ascending, then the need date), each from the latest of today, the kit date and the end of the previous operation plus its move days, onto the
    /// working days of its work centre. A second pass without the capacity limit gives the demand of the load report.
    /// </summary>
    internal static class MfScheduler
    {
        /// <summary>The scheduler looks this many days ahead of today; an operation that does not fit is reported as not scheduled.</summary>
        public const int HorizonDays = 400;

        private const decimal MinFree = 0.0001m;

        /// <param name="allCenters">Also load every active work centre (the load report shows centres without orders).</param>
        public static async Task<MfScheduleData> ComputeAsync(IUnitOfWork uow, DateTime asOf, CancellationToken ct, bool allCenters = false)
        {
            var data = new MfScheduleData { AsOf = asOf.Date };
            List<MfProductionOrder> orders = await uow.Set<MfProductionOrder>().Where(o => !o.Deleted && (o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress)).ToListAsync(ct);
            if (orders.Count == 0 && !allCenters) return data;
            List<int> orderIds = orders.Select(o => o.Id).ToList();
            List<MfOperation> ops = await uow.Set<MfOperation>().Where(o => !o.Deleted && orderIds.Contains(o.OrderId)).ToListAsync(ct);
            Dictionary<int, MfFlow.Urgency> urgency = await MfFlow.UrgencyAsync(uow, ct);

            List<int> activityIds = orders.Where(o => o.ActivityId.HasValue).Select(o => o.ActivityId.Value).Distinct().ToList();
            Dictionary<int, PcActivity> activities = activityIds.Count == 0 ? new Dictionary<int, PcActivity>()
                : await uow.Set<PcActivity>().Where(a => activityIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

            // work centres, their calendars and downtime
            List<int> centerIds = ops.Where(o => o.WorkCenterId.HasValue && !o.IsOutside).Select(o => o.WorkCenterId.Value).Distinct().ToList();
            List<MfWorkCenter> centers = await uow.Set<MfWorkCenter>().AsNoTracking().Where(c => !c.Deleted && (centerIds.Contains(c.Id) || (allCenters && c.IsActive))).ToListAsync(ct);
            data.Centers = await BuildCentersAsync(uow, centers, ct);
            await BuildDowntimeAsync(uow, data, ct);

            List<int> templateIds = ops.Where(o => o.OperationTemplateId.HasValue).Select(o => o.OperationTemplateId.Value).Distinct().ToList();
            Dictionary<int, decimal> moves = templateIds.Count == 0 ? new Dictionary<int, decimal>()
                : await uow.Set<MfOperationStandard>().AsNoTracking().Where(s => !s.Deleted && templateIds.Contains(s.OperationTemplateId)).ToDictionaryAsync(s => s.OperationTemplateId, s => s.MoveDays, ct);
            List<int> opIds = ops.Select(o => o.Id).ToList();
            Dictionary<int, decimal> booked = await uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && opIds.Contains(b.OperationId))
                .GroupBy(b => b.OperationId).Select(g => new { Id = g.Key, Hours = g.Sum(b => b.Hours) }).ToDictionaryAsync(x => x.Id, x => x.Hours, ct);
            Dictionary<int, (DateTime? Ready, bool Short)> kits = await KitAsync(uow, orders, data.AsOf, ct);

            foreach (MfProductionOrder order in orders)
            {
                urgency.TryGetValue(order.Id, out MfFlow.Urgency u);
                var plan = new MfOrderPlan { Order = order, Priority = u != null && u.Priority > 0 ? u.Priority : 0, Need = u?.NeedDate?.Date };
                if (order.ActivityId.HasValue && activities.TryGetValue(order.ActivityId.Value, out PcActivity act)) plan.Activity = act;
                if (kits.TryGetValue(order.Id, out var kit)) { plan.KitReady = kit.Ready; plan.KitShortage = kit.Short; }
                foreach (MfOperation op in MfFlow.Chain(ops.Where(o => o.OrderId == order.Id)))
                {
                    var p = new MfOpPlan { Op = op };
                    p.Completed = op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped || op.Status == MfOperationStatus.Rejected;
                    decimal planned = op.SetupHoursPlanned + op.RunHoursPlanned;
                    booked.TryGetValue(op.Id, out decimal done);
                    switch (op.Status)
                    {
                        case MfOperationStatus.InProgress:
                        case MfOperationStatus.Paused:
                            p.Hours = Math.Max(0, planned - done);
                            break;
                        case MfOperationStatus.QcHold:
                            p.Hours = 0;
                            break;
                        default:
                            p.Hours = planned;
                            break;
                    }
                    if (op.OperationTemplateId.HasValue && moves.TryGetValue(op.OperationTemplateId.Value, out decimal mv)) p.MoveDays = mv;
                    plan.Ops.Add(p);
                }
                data.Orders.Add(plan);
            }
            data.Orders = data.Orders.OrderBy(p => p.Priority > 0 ? 0 : 1).ThenBy(p => p.Priority).ThenBy(p => p.Order.Number, StringComparer.Ordinal).ToList();

            // pass 1: finite capacity (written to the operations); pass 2: no capacity limit (the demand of the load report)
            foreach (MfOrderPlan plan in data.Orders) PlanOrder(data, plan, true, data.Scheduled);
            foreach (MfOrderPlan plan in data.Orders) PlanOrder(data, plan, false, data.Demand);

            foreach (MfOrderPlan plan in data.Orders)
            {
                List<MfOpPlan> placed = plan.Ops.Where(o => !o.Completed && o.Start.HasValue).ToList();
                bool missing = plan.Ops.Any(o => !o.Completed && !o.Finish.HasValue);
                if (placed.Count > 0) plan.Start = placed.Min(o => o.Start);
                if (!missing && placed.Count > 0) plan.Finish = placed.Max(o => o.Finish);
                if (missing) plan.Note = string.Join("؛ ", plan.Ops.Where(o => !o.Completed && !string.IsNullOrWhiteSpace(o.Note)).Select(o => MfNames.Operation(plan.Order.Number, o.Op.Sequence) + ": " + o.Note).Distinct());
                if (plan.KitShortage && string.IsNullOrWhiteSpace(plan.Note)) plan.Note = plan.KitReady.HasValue ? "کیت مواد کامل نیست؛ تاریخ پیش‌بینی تحویل مواد: " + MfText.Date(plan.KitReady) : "کیت مواد کامل نیست و تحویل مواد (سفارش خرید باز) تعیین نشده است.";
                if (missing) { plan.IsLate = true; plan.DaysLate = 0; }
                else if (plan.Finish.HasValue && plan.Need.HasValue)
                {
                    int days = (plan.Finish.Value.Date - plan.Need.Value.Date).Days;
                    plan.IsLate = days > 0;
                    plan.DaysLate = Math.Max(0, days);
                }
            }
            return data;
        }

        /// <summary>The working calendar and the daily hours of work centres (no downtime).</summary>
        internal static async Task<Dictionary<int, MfCenter>> BuildCentersAsync(IUnitOfWork uow, IEnumerable<MfWorkCenter> centers, CancellationToken ct)
        {
            var result = new Dictionary<int, MfCenter>();
            var calendars = new Dictionary<int, PcWorkCalendar>();
            PcWorkCalendar company = null;
            foreach (MfWorkCenter wc in centers)
            {
                PcWorkCalendar cal;
                if (wc.CalendarId.HasValue)
                {
                    if (!calendars.TryGetValue(wc.CalendarId.Value, out cal)) { cal = await PcContext.CalendarAsync(uow, wc.CalendarId, ct); calendars[wc.CalendarId.Value] = cal; }
                }
                else cal = company ??= await PcContext.CalendarAsync(uow, null, ct);
                decimal shift = wc.ShiftsPerDay * wc.HoursPerShift;
                result[wc.Id] = new MfCenter { Wc = wc, Calendar = cal, ShiftHours = shift, Gross = shift * Math.Max(wc.ParallelResources, 1), Factor = wc.EfficiencyPercent / 100m * (wc.UtilisationPercent / 100m) };
            }
            return result;
        }

        /// <summary>One pass over one order. Finite: the hours already given to a day are not available again. Infinite: only the demand is recorded.</summary>
        private static void PlanOrder(MfScheduleData data, MfOrderPlan plan, bool finite, Dictionary<(int, DateTime), decimal> used)
        {
            DateTime? ready = null;
            bool placedAny = false;
            bool broken = false;
            var finishes = new Dictionary<MfOpPlan, DateTime?>();
            for (int i = 0; i < plan.Ops.Count; i++)
            {
                MfOpPlan p = plan.Ops[i];
                if (p.Completed)
                {
                    DateTime f = p.Op.FinishedAt?.LocalDateTime.Date ?? p.Op.PlannedFinish?.Date ?? data.AsOf;
                    ready = ready.HasValue && ready.Value > f ? ready : f;
                    continue;
                }
                if (broken)
                {
                    if (finite) { p.Start = null; p.Finish = null; p.Note = "عملیات قبلی برنامه‌ریزی نشد."; }
                    continue;
                }
                DateTime earliest = data.AsOf;
                if (ready.HasValue && ready.Value > earliest) earliest = ready.Value;
                if (i == 0 && plan.KitReady.HasValue && plan.KitReady.Value > earliest) earliest = plan.KitReady.Value;
                DateTime? start = null, finish = null;
                string note = null;
                if (p.Op.IsOutside)
                {
                    start = earliest;
                    finish = earliest.AddDays(Math.Max(p.Op.OutsideLeadDays, 0));
                }
                else if (!p.Op.WorkCenterId.HasValue || !data.Centers.TryGetValue(p.Op.WorkCenterId.Value, out MfCenter center))
                {
                    note = "مرکز کار عملیات تعیین نشده است.";
                }
                else if (Place(center, earliest, p.Hours, used, finite, out DateTime s, out DateTime e)) { start = s; finish = e; }
                else note = $"ظرفیت مرکز کار {center.Wc.Code} در {HorizonDays} روز آینده برای این عملیات کافی نیست (ماشین خراب یا تقویم بدون روز کاری).";

                if (finite) { p.Start = start; p.Finish = finish; p.Note = note; }
                if (!finish.HasValue) { broken = true; continue; }
                placedAny = true;
                ready = finish.Value.AddDays((double)Math.Ceiling(Math.Max(p.MoveDays, 0)));
                finishes[p] = finish;
            }
            _ = placedAny;
        }

        /// <summary>Gives <paramref name="hours"/> to the working days from <paramref name="earliest"/> on. Zero hours take no capacity and end on the day they can start.</summary>
        internal static bool Place(MfCenter c, DateTime earliest, decimal hours, Dictionary<(int, DateTime), decimal> used, bool finite, out DateTime start, out DateTime finish)
        {
            start = finish = earliest.Date;
            if (hours <= 0) return true;
            DateTime d = earliest.Date;
            decimal remaining = hours;
            bool started = false;
            for (int i = 0; i < HorizonDays; i++, d = d.AddDays(1))
            {
                // finite: what is left of the day after downtime and the hours already given; infinite (the demand): the full capacity, a machine that is down still has its load
                decimal free = finite ? c.Capacity(d) : c.FullCapacity(d);
                used.TryGetValue((c.Wc.Id, d), out decimal taken);
                if (finite) free -= taken;
                if (free <= MinFree) continue;
                decimal take = Math.Min(free, remaining);
                if (!started) { start = d; started = true; }
                used[(c.Wc.Id, d)] = taken + take;
                remaining -= take;
                if (remaining <= 0) { finish = d; return true; }
            }
            return false;
        }

        // ================================================================================== downtime

        private static async Task BuildDowntimeAsync(IUnitOfWork uow, MfScheduleData data, CancellationToken ct)
        {
            if (data.Centers.Count == 0) return;
            List<int> centerIds = data.Centers.Keys.ToList();
            List<MfMachineInfo> infos = await uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.WorkCenterId != null && centerIds.Contains(i.WorkCenterId.Value)).ToListAsync(ct);
            if (infos.Count == 0) return;
            List<int> machineIds = infos.Select(i => i.MachineId).ToList();
            List<MfMaintenanceOrder> mos = await uow.Set<MfMaintenanceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && machineIds.Contains(o.MachineId) && o.Status != MfMaintenanceOrderStatus.Cancelled).ToListAsync(ct);
            DateTime from = data.AsOf, to = data.AsOf.AddDays(HorizonDays);
            foreach (MfMachineInfo info in infos)
            {
                MfCenter c = data.Centers[info.WorkCenterId.Value];
                decimal share = MachineShare(c.Wc.ParallelResources, infos.Count(i => i.WorkCenterId == info.WorkCenterId));
                var perDay = new Dictionary<DateTime, decimal>();
                void Add(DateTime day, decimal hours) => perDay[day] = Math.Min(c.ShiftHours, (perDay.TryGetValue(day, out decimal h) ? h : 0) + hours);
                // a stop from start to end (an open end runs to the end of the horizon): per day the overlap with the shift window of that day, at most one shift of the machine
                void Stop(DateTime start, DateTime? end)
                {
                    DateTime stop = end ?? to.AddDays(1);
                    for (DateTime d = start.Date < from ? from : start.Date; d <= to && d <= stop.Date; d = d.AddDays(1))
                    {
                        decimal hours = c.ShiftOverlapHours(d, start, stop);
                        if (hours > 0) Add(d, hours);
                    }
                }
                void Full(DateTime s, DateTime e) { for (DateTime d = s.Date < from ? from : s.Date; d <= e.Date && d <= to; d = d.AddDays(1)) Add(d, c.ShiftHours); }
                bool covered = false;
                foreach (MfMaintenanceOrder o in mos.Where(x => x.MachineId == info.MachineId))
                {
                    bool open = o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress;
                    if (open) covered = true;
                    if (o.Kind == MfMaintenanceKind.Breakdown)
                    {
                        DateTime s = o.DowntimeStart ?? o.CreateDate.LocalDateTime;
                        Stop(s, o.DowntimeEnd ?? (open ? (DateTime?)null : (o.DoneAt?.LocalDateTime ?? s)));
                    }
                    else if (open)
                    {
                        DateTime? start = o.DowntimeStart?.Date ?? o.DueDate?.Date;
                        if (!start.HasValue || o.EstimatedHours <= 0) continue;
                        decimal remaining = o.EstimatedHours;
                        for (DateTime d = start.Value < from ? from : start.Value; remaining > 0 && d <= to; d = d.AddDays(1))
                        {
                            if (!c.Calendar.IsWorking(d)) continue;
                            decimal take = Math.Min(remaining, c.ShiftHours);
                            Add(d, take);
                            remaining -= take;
                        }
                    }
                }
                // a machine that is down, in repair or retired with no order explaining it is out of production from today
                if (info.Status == MfMachineStatus.Retired || (!covered && (info.Status == MfMachineStatus.Down || info.Status == MfMachineStatus.UnderMaintenance))) Full(from, to);
                foreach (var kv in perDay) c.Down[kv.Key] = (c.Down.TryGetValue(kv.Key, out decimal h) ? h : 0) + kv.Value * share;
            }
        }

        /// <summary>
        /// The part of a work centre's capacity one of its machines stands for: the parallel resources are shared by the machines (when there are more machines than resources),
        /// otherwise each machine is one resource.
        /// </summary>
        internal static decimal MachineShare(int parallelResources, int machines)
        {
            int p = Math.Max(parallelResources, 1);
            return machines <= 0 || machines <= p ? 1m : p / (decimal)machines;
        }

        // ================================================================================== kit

        private static readonly WarehouseKind[] CompanyKinds = { WarehouseKind.General, WarehouseKind.RawMaterial, WarehouseKind.Parts, WarehouseKind.WorkInProgress, WarehouseKind.FinishedGoods };

        /// <summary>
        /// For the orders whose kit is not issued yet: is the stock enough for what is left to issue, and from which date the open purchase orders cover the rest.
        /// Every order is looked at on its own (what other orders still have to take is not reserved).
        /// </summary>
        internal static async Task<Dictionary<int, (DateTime? Ready, bool Short)>> KitAsync(IUnitOfWork uow, List<MfProductionOrder> orders, DateTime asOf, CancellationToken ct)
        {
            var result = new Dictionary<int, (DateTime?, bool)>();
            List<MfProductionOrder> kitOrders = orders.Where(o => o.KitRequestId.HasValue && string.IsNullOrWhiteSpace(o.KitOverrideReason)).ToList();
            if (kitOrders.Count == 0) return result;
            List<int> requestIds = kitOrders.Select(o => o.KitRequestId.Value).Distinct().ToList();
            List<int> usable = await uow.Set<MaterialRequest>().AsNoTracking().Where(r => requestIds.Contains(r.Id) && !r.Deleted && r.Status != MaterialRequestStatus.Rejected).Select(r => r.Id).ToListAsync(ct);
            var lines = await uow.Set<MaterialRequestLine>().AsNoTracking().Where(l => usable.Contains(l.MaterialRequestId)).Select(l => new { l.MaterialRequestId, l.ItemId, l.Quantity, l.IssuedQuantity }).ToListAsync(ct);
            List<int> itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            if (itemIds.Count == 0) return result;
            var onHand = await (from b in uow.Set<StockBalance>().AsNoTracking()
                                join w in uow.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                                where itemIds.Contains(b.ItemId) && !w.Deleted && w.IsActive && CompanyKinds.Contains(w.Kind)
                                group b by b.ItemId into g
                                select new { Id = g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            PurchaseOrderStatus[] open = { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };
            var incoming = (await (from l in uow.Set<PurchaseOrderLine>().AsNoTracking()
                                   join p in uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                                   where l.ItemId != null && itemIds.Contains(l.ItemId.Value) && !p.Deleted && open.Contains(p.Status)
                                   select new { ItemId = l.ItemId.Value, l.Quantity, l.ReceivedQuantity, Delivery = l.DeliveryDate ?? p.DeliveryDate }).ToListAsync(ct))
                .Where(x => x.Delivery.HasValue && x.Quantity > x.ReceivedQuantity).OrderBy(x => x.Delivery).ToList();
            foreach (MfProductionOrder o in kitOrders)
            {
                bool anyShort = false, unresolved = false;
                DateTime? ready = null;
                foreach (var g in lines.Where(l => l.MaterialRequestId == o.KitRequestId.Value && l.Quantity > l.IssuedQuantity).GroupBy(l => l.ItemId))
                {
                    decimal need = g.Sum(l => l.Quantity - l.IssuedQuantity);
                    decimal stock = onHand.FirstOrDefault(x => x.Id == g.Key)?.Qty ?? 0;
                    if (stock >= need) continue;
                    anyShort = true;
                    decimal missing = need - stock, cumulative = 0;
                    DateTime? cover = null;
                    foreach (var line in incoming.Where(x => x.ItemId == g.Key))
                    {
                        cumulative += line.Quantity - line.ReceivedQuantity;
                        if (cumulative >= missing) { cover = line.Delivery.Value.Date; break; }
                    }
                    if (cover.HasValue) ready = ready.HasValue && ready.Value > cover.Value ? ready : cover;
                    else unresolved = true;
                }
                if (anyShort) result[o.Id] = (unresolved ? (DateTime?)null : ready, true);
            }
            return result;
        }

        // ================================================================================== weeks

        /// <summary>Persian weeks start on Saturday.</summary>
        public static DateTime WeekStart(DateTime day)
        {
            DateTime d = day.Date;
            return d.AddDays(-(((int)d.DayOfWeek + 1) % 7));
        }
    }
}
