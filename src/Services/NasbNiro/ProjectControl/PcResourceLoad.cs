using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Agents.Steps;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>The rules of the resource load (priority 4, phase 4c) that need no database: spreading a quantity over working days, the capacity of a day, the runs of over-capacity days. Tested apart.</summary>
    internal static class PcLoadRules
    {
        /// <summary>The longest period one histogram query covers.</summary>
        public const int MaxSpanDays = 370;

        /// <summary>The period the over-allocation check looks at, from today.</summary>
        public const int HorizonDays = 180;

        /// <summary>The unit of the stored quantity (four decimals).</summary>
        private const decimal Unit = 0.0001m;

        /// <summary>
        /// The share of a quantity for each of n working days: Uniform equal, FrontLoaded linearly decreasing (n, n-1, ..., 1), BackLoaded linearly increasing (1, 2, ..., n). Each share but the last is rounded
        /// down to four decimals and the last day takes the rest, so the sum is the quantity exactly.
        /// </summary>
        public static decimal[] Spread(decimal quantity, PcResourceDistribution distribution, int days)
        {
            if (days <= 0) return Array.Empty<decimal>();
            var shares = new decimal[days];
            if (days == 1) { shares[0] = quantity; return shares; }
            decimal sum = distribution == PcResourceDistribution.Uniform ? days : days * (days + 1) / 2m;
            decimal used = 0m;
            for (int i = 0; i < days - 1; i++)
            {
                decimal weight = distribution == PcResourceDistribution.Uniform ? 1m : distribution == PcResourceDistribution.FrontLoaded ? days - i : i + 1;
                shares[i] = Math.Floor(quantity * weight / sum / Unit) * Unit;
                used += shares[i];
            }
            shares[days - 1] = quantity - used;
            return shares;
        }

        /// <summary>The working days of a calendar from the start to the finish, both included.</summary>
        public static List<DateTime> WorkingDays(PcWorkCalendar calendar, DateTime start, DateTime finish)
        {
            var days = new List<DateTime>();
            for (DateTime d = start.Date; d <= finish.Date; d = d.AddDays(1)) if (calendar.IsWorking(d)) days.Add(d);
            return days;
        }

        /// <summary>The capacity of a resource on a day: its capacity times the hours of a working day for a resource counted in hours, its capacity for the others; zero on a day the calendar of the resource does not work.</summary>
        public static decimal DayCapacity(decimal capacity, PcResourceUnit unit, bool working, decimal hoursPerDay) =>
            !working ? 0m : unit == PcResourceUnit.Hour ? capacity * hoursPerDay : capacity;

        public sealed class OverRun
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public decimal PeakLoad { get; set; }
            public decimal PeakCapacity { get; set; }
        }

        /// <summary>
        /// The maximal runs of consecutive over-capacity days between the two dates. A day with neither load nor capacity (a day off of the resource) does not break a run; a day within the capacity does.
        /// The peak is the largest daily load of the run (the capacity of that day goes with it).
        /// </summary>
        public static List<OverRun> Runs(IReadOnlyDictionary<DateTime, decimal> load, Func<DateTime, decimal> capacity, DateTime from, DateTime to)
        {
            var runs = new List<OverRun>();
            OverRun current = null;
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                decimal l = load.TryGetValue(d, out decimal v) ? v : 0m;
                decimal c = capacity(d);
                if (l > c)
                {
                    if (current == null) { current = new OverRun { From = d, To = d, PeakLoad = l, PeakCapacity = c }; runs.Add(current); }
                    else
                    {
                        current.To = d;
                        if (l > current.PeakLoad) { current.PeakLoad = l; current.PeakCapacity = c; }
                    }
                }
                else if (l == 0m && c == 0m) continue;
                else current = null;
            }
            return runs;
        }

        /// <summary>The first day of the week (Saturday) that holds the date.</summary>
        public static DateTime WeekStart(DateTime date)
        {
            int back = ((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
            return date.Date.AddDays(-back);
        }
    }

    internal sealed class LoadActivity
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
        public int? CalendarId { get; set; }
        public int? TotalFloat { get; set; }
        public bool IsCritical { get; set; }
        public bool Started { get; set; }
        public PcConstraint ConstraintType { get; set; }
        public DateTime? ConstraintDate { get; set; }
    }

    internal sealed class LoadRow
    {
        public int PlanId { get; set; }
        public int ActivityId { get; set; }
        public int ResourceId { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution Distribution { get; set; }
        public bool IsDraft { get; set; }
    }

    internal sealed class LoadResource
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceKind Kind { get; set; }
        public PcResourceUnit Unit { get; set; }
        public decimal Capacity { get; set; }
        public int? CalendarId { get; set; }

        /// <summary>Priority 4d: the work centre a machine resource is linked to; its capacity, calendar and day length are then read from it (<see cref="PcWorkCentreLink"/>).</summary>
        public int? WorkCenterId { get; set; }

        /// <summary>The hours of a working day of the work centre (shifts x hours per shift) for a linked resource; null for any other.</summary>
        public decimal? FixedHours { get; set; }

        /// <summary>Material and subcontract capacity is not a rate of use: they never raise an over-allocation.</summary>
        public bool Detectable => Kind != PcResourceKind.Material && Kind != PcResourceKind.Subcontract;
    }

    internal sealed class LoadPlan
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int MakerUserId { get; set; }
        public string EditorUserIds { get; set; }
        public bool IsDraft { get; set; }
    }

    /// <summary>One activity's share of one resource on one day.</summary>
    internal struct LoadCell
    {
        public int ResourceId;
        public DateTime Date;
        public int ActivityId;
        public decimal Value;
        public bool IsDraft;

        /// <summary>Priority 4d: the cell is the load of the work orders (production) on the work centre of a linked machine resource, not of a project activity (its ActivityId is 0).</summary>
        public bool IsProduction;
    }

    /// <summary>
    /// The rows that the load of the resources is computed from, read once (a fixed number of queries whatever the number of assignments) and computed in memory: the assignments of the latest approved plan of every live contract
    /// (optionally the open plan of a contract in place of its approved one), the dates of their activities, the resources and the calendars.
    /// </summary>
    internal sealed class LoadModel
    {
        public Dictionary<int, LoadActivity> Activities { get; } = new Dictionary<int, LoadActivity>();
        public List<LoadRow> Rows { get; } = new List<LoadRow>();
        public Dictionary<int, LoadResource> Resources { get; } = new Dictionary<int, LoadResource>();
        public Dictionary<int, LoadPlan> Plans { get; } = new Dictionary<int, LoadPlan>();
        public Dictionary<int, int?> ContractCalendar { get; } = new Dictionary<int, int?>();
        public Dictionary<int, PcWorkCalendar> Calendars { get; } = new Dictionary<int, PcWorkCalendar>();
        public Dictionary<int, decimal> CalendarHours { get; } = new Dictionary<int, decimal>();
        public PcWorkCalendar Company { get; set; }
        public List<LoadCell> Cells { get; } = new List<LoadCell>();

        public PcWorkCalendar CalendarOf(int? calendarId) => calendarId.HasValue && Calendars.TryGetValue(calendarId.Value, out PcWorkCalendar c) ? c : Company;

        /// <summary>The calendar an activity works on: its own, else the one of its contract, else the company calendar.</summary>
        public PcWorkCalendar CalendarOfActivity(LoadActivity a) =>
            a.CalendarId.HasValue && Calendars.ContainsKey(a.CalendarId.Value) ? Calendars[a.CalendarId.Value] : CalendarOf(ContractCalendar.TryGetValue(a.ContractId, out int? c) ? c : null);

        public PcWorkCalendar ContractCalendarOf(int contractId) => CalendarOf(ContractCalendar.TryGetValue(contractId, out int? c) ? c : null);

        /// <summary>
        /// The hours of a working day of a resource: the day length of its own calendar; without one, the longest day length of the activities that load it (the activity calendar, else the calendar of the project, else 7.5 hours),
        /// which is the rule that draws the quantity of a draft plan, so a full-time assignment is never an over-allocation by construction.
        /// </summary>
        public decimal HoursOf(LoadResource r)
        {
            if (r.FixedHours.HasValue) return r.FixedHours.Value;
            if (r.CalendarId.HasValue && CalendarHours.TryGetValue(r.CalendarId.Value, out decimal own) && own > 0m) return own;
            return ResourceHours.TryGetValue(r.Id, out decimal h) ? h : PcResourceRules.DefaultDayHours;
        }

        public Dictionary<int, decimal> ResourceHours { get; } = new Dictionary<int, decimal>();

        private decimal HoursOfActivity(LoadActivity a)
        {
            int? id = a.CalendarId.HasValue && CalendarHours.ContainsKey(a.CalendarId.Value) ? a.CalendarId : ContractCalendar.TryGetValue(a.ContractId, out int? c) ? c : null;
            return id.HasValue && CalendarHours.TryGetValue(id.Value, out decimal h) && h > 0m ? h : PcResourceRules.DefaultDayHours;
        }

        /// <summary>The capacity of a resource on a day (the calendar of the resource, else the company calendar; the day length by <see cref="HoursOf"/>).</summary>
        public decimal Capacity(LoadResource r, DateTime day) => PcLoadRules.DayCapacity(r.Capacity, r.Unit, CalendarOf(r.CalendarId).IsWorking(day), HoursOf(r));

        /// <summary>The working days an activity loads between two dates; an activity whose span has no working day puts its whole quantity on the next working day (a quantity is never lost).</summary>
        public List<DateTime> DaysOf(LoadActivity a, DateTime start, DateTime finish)
        {
            PcWorkCalendar cal = CalendarOfActivity(a);
            List<DateTime> days = PcLoadRules.WorkingDays(cal, start, finish);
            if (days.Count == 0) days.Add(cal.NextWorkingOnOrAfter(start.Date));
            return days;
        }

        public Dictionary<DateTime, decimal> LoadOf(int resourceId, Func<LoadCell, bool> filter = null)
        {
            var load = new Dictionary<DateTime, decimal>();
            foreach (LoadCell c in Cells)
            {
                if (c.ResourceId != resourceId || (filter != null && !filter(c))) continue;
                load[c.Date] = (load.TryGetValue(c.Date, out decimal v) ? v : 0m) + c.Value;
            }
            return load;
        }

        /// <summary>Spreads every row over the working days of its activity and keeps the cells that fall in the period.</summary>
        public void Spread(DateTime from, DateTime to)
        {
            Cells.Clear();
            foreach (LoadRow row in Rows)
            {
                if (!Activities.TryGetValue(row.ActivityId, out LoadActivity a) || !Resources.ContainsKey(row.ResourceId)) continue;
                if (a.Finish.Date < from.Date || a.Start.Date > to.Date) continue;
                List<DateTime> days = DaysOf(a, a.Start, a.Finish);
                decimal[] shares = PcLoadRules.Spread(row.Quantity, row.Distribution, days.Count);
                for (int i = 0; i < days.Count; i++)
                {
                    if (days[i] < from.Date || days[i] > to.Date || shares[i] == 0m) continue;
                    Cells.Add(new LoadCell { ResourceId = row.ResourceId, Date = days[i], ActivityId = row.ActivityId, Value = shares[i], IsDraft = row.IsDraft });
                }
            }
        }

        /// <summary>The over-capacity runs of one resource between the two dates (from the cells that the filter keeps).</summary>
        public List<PcLoadRules.OverRun> RunsOf(LoadResource r, DateTime from, DateTime to, Func<LoadCell, bool> filter = null) =>
            PcLoadRules.Runs(LoadOf(r.Id, filter), d => Capacity(r, d), from, to);

        // ------------------------------------------------------------------------------------------------ the queries

        /// <summary>
        /// The plans of the live contracts that the load comes from: the newest approved plan of each, or, with <paramref name="includeOpen"/>, the open plan of a contract in place of its approved one.
        /// </summary>
        public static IQueryable<PcResourcePlan> PlanQuery(IUnitOfWork uow, int[] contractIds, bool includeOpen) =>
            uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && contractIds.Contains(p.ContractId)
                && (p.Status == PcResourcePlanStatus.Approved || (includeOpen && (p.Status == PcResourcePlanStatus.Draft || p.Status == PcResourcePlanStatus.Submitted || p.Status == PcResourcePlanStatus.Returned))));

        public static IQueryable<LoadRow> RowQuery(IUnitOfWork uow, int[] planIds, int? resourceId, int[] onlyResources = null) =>
            uow.Set<PcActivityResource>().AsNoTracking().Where(x => planIds.Contains(x.PlanId) && !x.Activity.Deleted && !x.Resource.Deleted && (resourceId == null || x.ResourceId == resourceId) && (onlyResources == null || onlyResources.Contains(x.ResourceId)))
                .OrderBy(x => x.PlanId).ThenBy(x => x.ActivityId).ThenBy(x => x.ResourceId)
                .Select(x => new LoadRow { PlanId = x.PlanId, ActivityId = x.ActivityId, ResourceId = x.ResourceId, Quantity = x.Quantity, Distribution = x.Distribution });

        public static IQueryable<LoadActivity> ActivityQuery(IUnitOfWork uow, int[] activityIds) =>
            uow.Set<PcActivity>().AsNoTracking().Where(a => activityIds.Contains(a.Id) && !a.Deleted && a.EarlyStart != null && a.EarlyFinish != null)
                .Select(a => new LoadActivity
                {
                    Id = a.Id, ContractId = a.ContractId, Code = a.Code, Title = a.Title, Start = a.EarlyStart.Value, Finish = a.EarlyFinish.Value, CalendarId = a.CalendarId, TotalFloat = a.TotalFloat, IsCritical = a.IsCritical,
                    Started = a.ActualStart != null || a.Status == PcActivityStatus.InProgress || a.Status == PcActivityStatus.Done, ConstraintType = a.ConstraintType, ConstraintDate = a.ConstraintDate,
                });

        /// <summary>The contracts whose plans load the resources: every contract that is running (not deleted, status Current), whatever its date (the 400 day window of the agents' initiation steps does not apply).</summary>
        public static IQueryable<Entities.Model.NasbNiro.Contracts.ContractEntity> LiveContracts(IUnitOfWork uow) =>
            uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => !c.Deleted && c.Status == Entities.Model.NasbNiro.Contracts.ContractStatus.Current);

        /// <param name="includeProduction">Priority 4d: add the load of the work orders on the work centre of every linked machine resource of the model as cells of their own (ActivityId 0, IsProduction); nothing is added when no resource is linked.</param>
        /// <param name="onlyResources">Only the assignments of these resources are read (the project layer of the production view asks for the linked machines only).</param>
        /// <param name="alsoResources">Resources that are loaded into the model even when they have no assignment (the histogram shows a linked machine that only carries production load).</param>
        public static async Task<LoadModel> LoadAsync(IUnitOfWork uow, DateTime today, int? resourceId, bool includeOpen, DateTime from, DateTime to, CancellationToken ct,
            bool includeProduction = false, int[] onlyResources = null, int[] alsoResources = null)
        {
            var model = new LoadModel();
            var live = await LiveContracts(uow).OrderBy(c => c.Id).Select(c => new { c.Id, c.Code }).ToListAsync(ct);
            int[] contractIds = live.Select(c => c.Id).ToArray();
            Dictionary<int, string> codes = live.ToDictionary(c => c.Id, c => c.Code);

            // the plan of each contract: the open one in place of the approved one when asked, else the newest approved
            var planRows = contractIds.Length == 0 ? new List<PcResourcePlan>() : await PlanQuery(uow, contractIds, includeOpen).ToListAsync(ct);
            foreach (IGrouping<int, PcResourcePlan> g in planRows.GroupBy(p => p.ContractId))
            {
                PcResourcePlan open = includeOpen ? g.Where(p => p.Status != PcResourcePlanStatus.Approved).OrderByDescending(p => p.Version).FirstOrDefault() : null;
                PcResourcePlan chosen = open ?? g.Where(p => p.Status == PcResourcePlanStatus.Approved).OrderByDescending(p => p.Version).FirstOrDefault();
                if (chosen != null) model.Plans[chosen.Id] = new LoadPlan { Id = chosen.Id, ContractId = chosen.ContractId, MakerUserId = chosen.MakerUserId, EditorUserIds = chosen.EditorUserIds, IsDraft = chosen.Status != PcResourcePlanStatus.Approved };
            }
            int[] planIds = model.Plans.Keys.OrderBy(i => i).ToArray();
            if (planIds.Length > 0) model.Rows.AddRange(await RowQuery(uow, planIds, resourceId, onlyResources).ToListAsync(ct));
            foreach (LoadRow r in model.Rows) r.IsDraft = model.Plans[r.PlanId].IsDraft;

            int[] activityIds = model.Rows.Select(r => r.ActivityId).Distinct().OrderBy(i => i).ToArray();
            if (activityIds.Length > 0)
                foreach (LoadActivity a in await ActivityQuery(uow, activityIds).ToListAsync(ct)) { a.ContractCode = codes.TryGetValue(a.ContractId, out string code) ? code : null; model.Activities[a.Id] = a; }

            // the resources: the ones the rows name, and the one that was asked for even when it has no row
            int[] resourceIds = model.Rows.Select(r => r.ResourceId).Concat(resourceId.HasValue ? new[] { resourceId.Value } : Array.Empty<int>()).Concat(alsoResources ?? Array.Empty<int>()).Distinct().OrderBy(i => i).ToArray();
            if (resourceIds.Length > 0)
                foreach (LoadResource r in await uow.Set<PcResource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id) && !r.Deleted)
                    .Select(r => new LoadResource { Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, Unit = r.Unit, Capacity = r.Capacity, CalendarId = r.CalendarId, WorkCenterId = r.Kind == PcResourceKind.Machine && r.Unit == PcResourceUnit.Hour ? r.WorkCenterId : null }).ToListAsync(ct))
                    model.Resources[r.Id] = r;
            // one source of truth with production: a machine resource linked to a work centre takes capacity, calendar and day length from it
            await PcWorkCentreLink.ApplyAsync(uow, model.Resources.Values, ct);

            int[] termContracts = model.Activities.Values.Select(a => a.ContractId).Distinct().ToArray();
            if (termContracts.Length > 0)
                foreach (var t in await uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && termContracts.Contains(t.ContractId)).OrderBy(t => t.Id).Select(t => new { t.ContractId, t.CalendarId }).ToListAsync(ct))
                    if (!model.ContractCalendar.ContainsKey(t.ContractId)) model.ContractCalendar[t.ContractId] = t.CalendarId;

            await LoadCalendarsAsync(uow, model, ct);
            foreach (IGrouping<int, LoadRow> g in model.Rows.GroupBy(r => r.ResourceId))
                model.ResourceHours[g.Key] = g.Where(r => model.Activities.ContainsKey(r.ActivityId)).Select(r => model.HoursOfActivity(model.Activities[r.ActivityId])).DefaultIfEmpty(PcResourceRules.DefaultDayHours).Max();
            model.Spread(from, to);
            if (includeProduction) await PcWorkCentreLink.AddProductionCellsAsync(uow, model, today, from, to, ct);
            return model;
        }

        /// <summary>The company calendar and the calendars that the activities, the resources and the contracts name: three queries whatever their number (the same rules as <c>PcContext.CalendarAsync</c>).</summary>
        private static async Task LoadCalendarsAsync(IUnitOfWork uow, LoadModel model, CancellationToken ct)
        {
            List<DateTime> holidays = (await uow.Set<HolidaysByDate>().AsNoTracking().Select(h => h.Date).ToListAsync(ct)).Select(d => d.Date).ToList();
            List<DayOfWeek> weekly = await uow.Set<HolidaysDayOfWeek>().AsNoTracking().Select(h => h.DayOfWeek).Distinct().ToListAsync(ct);
            if (weekly.Count == 0) weekly.Add(DayOfWeek.Friday);
            int mask = 0;
            for (int d = 0; d < 7; d++) if (!weekly.Contains((DayOfWeek)d)) mask |= 1 << d;
            model.Company = new PcWorkCalendar(mask, holidays, null);

            int[] ids = model.Activities.Values.Where(a => a.CalendarId.HasValue).Select(a => a.CalendarId.Value)
                .Concat(model.Resources.Values.Where(r => r.CalendarId.HasValue).Select(r => r.CalendarId.Value))
                .Concat(model.ContractCalendar.Values.Where(c => c.HasValue).Select(c => c.Value)).Distinct().OrderBy(i => i).ToArray();
            if (ids.Length == 0) return;
            List<PcCalendar> calendars = await uow.Set<PcCalendar>().AsNoTracking().Include(c => c.Exceptions).Where(c => ids.Contains(c.Id) && !c.Deleted).OrderBy(c => c.Id).ToListAsync(ct);
            foreach (PcCalendar c in calendars)
            {
                var off = new List<DateTime>(c.Exceptions.Where(e => !e.IsWorking).Select(e => e.Date));
                if (c.UseCompanyHolidays) off.AddRange(holidays);
                model.Calendars[c.Id] = new PcWorkCalendar(c.WorkingWeekdays, off, c.Exceptions.Where(e => e.IsWorking).Select(e => e.Date));
                model.CalendarHours[c.Id] = c.HoursPerDay;
            }
        }
    }
}
