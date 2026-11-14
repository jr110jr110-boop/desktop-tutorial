using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>One planned activity of a baseline, as the planned-value curve needs it.</summary>
    internal sealed class PcCurveItem
    {
        public int ActivityId;
        public DateTime Start;
        public DateTime Finish;
        public decimal Budget;
        public int Duration;

        /// <summary>0 = the calendar of the contract; else the id of the calendar of the activity (R1c, M2).</summary>
        public int CalendarKey;
    }

    /// <summary>
    /// R1c (M4, ISO 21500 / PMBOK 7 cost baseline): the planned-value curve of a baseline. It is computed once, from the approved activity budgets and the calendars of the
    /// day of the approval, and then stored (<see cref="PcBaselineCostPeriod"/>) and read back from the rows; a holiday added to a calendar afterwards does not move the plan.
    /// One value per calendar day of the project range; the working days of each calendar are counted per day so that the planned share of one activity
    /// on a given day can be read later without any calendar. Pure (no database).
    /// </summary>
    internal sealed class PcCurve
    {
        public const int ContractCalendar = 0;

        private DateTime _start;
        private int _days;
        private PcAccrualMode _mode;
        private readonly Dictionary<int, int[]> _cum = new Dictionary<int, int[]>();
        private decimal[] _project = Array.Empty<decimal>();
        private readonly List<int> _contractWorkingDays = new List<int>();

        public bool IsEmpty => _days == 0;
        public DateTime Start => _start;
        public DateTime End => _start.AddDays(Math.Max(0, _days - 1));
        public int Days => _days;

        /// <summary>The number of working days of the contract calendar inside the range.</summary>
        public int ContractWorkingDays => _contractWorkingDays.Count;

        /// <summary>The planned value of the whole plan (the budget at completion of the rows).</summary>
        public decimal Total => _days == 0 ? 0m : _project[_days - 1];

        /// <summary>Computes the curve of the items. <paramref name="calendarOf"/> gives the calendar of a key (0 = contract).</summary>
        public static PcCurve Build(IList<PcCurveItem> items, Func<int, PcWorkCalendar> calendarOf, PcAccrualMode mode)
        {
            var curve = new PcCurve { _mode = mode };
            if (items == null || items.Count == 0) return curve;
            curve._start = items.Min(i => i.Start).Date;
            DateTime end = items.Max(i => i.Finish).Date;
            curve._days = Math.Max(1, (int)(end - curve._start).TotalDays + 1);
            foreach (int key in items.Select(i => i.CalendarKey).Concat(new[] { ContractCalendar }).Distinct())
            {
                PcWorkCalendar cal = calendarOf(key);
                var cum = new int[curve._days];
                int run = 0;
                for (int d = 0; d < curve._days; d++)
                {
                    if (cal.IsWorking(curve._start.AddDays(d))) run++;
                    cum[d] = run;
                }
                curve._cum[key] = cum;
            }
            curve.IndexContractDays();
            curve._project = new decimal[curve._days];
            for (int d = 0; d < curve._days; d++)
            {
                DateTime day = curve._start.AddDays(d);
                decimal sum = 0m;
                foreach (PcCurveItem it in items) sum += it.Budget * curve.Fraction(it, day);
                curve._project[d] = Math.Round(sum, 4, MidpointRounding.AwayFromZero);
            }
            return curve;
        }

        private void IndexContractDays()
        {
            _contractWorkingDays.Clear();
            int[] cum = _cum[ContractCalendar];
            for (int d = 0; d < _days; d++)
                if (cum[d] > (d == 0 ? 0 : cum[d - 1])) _contractWorkingDays.Add(d);
        }

        /// <summary>Reads the curve back from the stored rows of a baseline (the project rows and the calendar rows).</summary>
        public static PcCurve FromRows(IEnumerable<PcBaselineCostPeriod> rows, PcAccrualMode mode)
        {
            var curve = new PcCurve { _mode = mode };
            List<PcBaselineCostPeriod> project = rows.Where(r => r.CalendarId == null).OrderBy(r => r.PeriodStart).ToList();
            if (project.Count == 0) return curve;
            curve._start = project[0].PeriodStart.Date;
            curve._days = project.Count;
            curve._project = project.Select(r => r.PlannedCumulative).ToArray();
            curve._cum[ContractCalendar] = project.Select(r => r.WorkingOffset).ToArray();
            foreach (var group in rows.Where(r => r.CalendarId != null).GroupBy(r => r.CalendarId.Value))
            {
                var cum = new int[curve._days];
                foreach (PcBaselineCostPeriod r in group)
                {
                    int index = (int)(r.PeriodStart.Date - curve._start).TotalDays;
                    if (index >= 0 && index < curve._days) cum[index] = r.WorkingOffset;
                }
                curve._cum[group.Key] = cum;
            }
            curve.IndexContractDays();
            return curve;
        }

        /// <summary>The rows to store for a baseline: one project row per day (with the working days of the contract calendar) and one row per day for every other calendar.</summary>
        public List<PcBaselineCostPeriod> ToRows(int baselineId)
        {
            var rows = new List<PcBaselineCostPeriod>(_days * Math.Max(1, _cum.Count));
            int[] contract = _cum.Count == 0 ? new int[0] : _cum[ContractCalendar];
            for (int d = 0; d < _days; d++)
                rows.Add(new PcBaselineCostPeriod { BaselineId = baselineId, PeriodStart = _start.AddDays(d), PlannedCumulative = _project[d], CalendarId = null, WorkingOffset = contract[d] });
            foreach (var pair in _cum.Where(p => p.Key != ContractCalendar).OrderBy(p => p.Key))
                for (int d = 0; d < _days; d++)
                    rows.Add(new PcBaselineCostPeriod { BaselineId = baselineId, PeriodStart = _start.AddDays(d), PlannedCumulative = 0m, CalendarId = pair.Key, WorkingOffset = pair.Value[d] });
            return rows;
        }

        private int[] CumOf(int key) => _cum.TryGetValue(key, out int[] cum) ? cum : _cum[ContractCalendar];

        /// <summary>Working days of the calendar from the first day of the range up to and including the day (zero before the range, the total after it).</summary>
        public int WorkingThrough(int key, DateTime day)
        {
            if (_days == 0) return 0;
            int index = (int)(day.Date - _start).TotalDays;
            if (index < 0) return 0;
            int[] cum = CumOf(key);
            return cum[Math.Min(index, _days - 1)];
        }

        /// <summary>Working days of the calendar in [from, to] (both inclusive).</summary>
        public int WorkingBetween(int key, DateTime from, DateTime to) => Math.Max(0, WorkingThrough(key, to) - WorkingThrough(key, from.Date.AddDays(-1)));

        /// <summary>The planned share (0-1) of an activity on a day: nothing before its start, all of it from its finish, and in between the elapsed share of its working days (or nothing until the finish when the accrual is at the end).</summary>
        public decimal Fraction(PcCurveItem it, DateTime day)
        {
            day = day.Date;
            if (day < it.Start.Date) return 0m;
            if (day >= it.Finish.Date) return 1m;
            if (_mode == PcAccrualMode.AtEnd || it.Duration == 0) return 0m;
            int total = WorkingBetween(it.CalendarKey, it.Start, it.Finish);
            if (total <= 0) return 0m;
            int done = WorkingBetween(it.CalendarKey, it.Start, day);
            return Math.Min(1m, Math.Max(0m, (decimal)done / total));
        }

        /// <summary>The planned value of the whole plan at the end of the day (zero before the range, the budget at completion after it).</summary>
        public decimal Pv(DateTime day)
        {
            if (_days == 0) return 0m;
            int index = (int)(day.Date - _start).TotalDays;
            if (index < 0) return 0m;
            return _project[Math.Min(index, _days - 1)];
        }

        /// <summary>The planned value at the end of the k-th working day of the contract (k from 0), for the earned schedule.</summary>
        public decimal PvAtWorkingDay(int k)
        {
            if (_days == 0 || _contractWorkingDays.Count == 0) return 0m;
            if (k < 0) return 0m;
            return _project[_contractWorkingDays[Math.Min(k, _contractWorkingDays.Count - 1)]];
        }

        /// <summary>
        /// The working days of the contract that have passed from the start of the range to the day (the actual time of the earned schedule). After the end of the range
        /// the days are counted on the live calendar given, because the frozen rows stop at the planned finish.
        /// </summary>
        public int ActualDays(DateTime day, PcWorkCalendar liveCalendar)
        {
            if (_days == 0) return 0;
            day = day.Date;
            int inside = WorkingThrough(ContractCalendar, day);
            if (day <= End || liveCalendar == null) return inside;
            return inside + Math.Max(0, liveCalendar.WorkingDaysBetween(End.AddDays(1), day.AddDays(1)));
        }
    }

    /// <summary>Loads and stores the frozen curve of a baseline (see <see cref="PcCurve"/>).</summary>
    internal static class PcFrozenPlan
    {
        /// <summary>The curve items of the rows of a baseline.</summary>
        public static List<PcCurveItem> ItemsOf(IEnumerable<PcBaselineActivity> rows) =>
            rows.Select(r => new PcCurveItem { ActivityId = r.ActivityId, Start = r.Start.Date, Finish = r.Finish.Date, Budget = r.Budget, Duration = r.DurationDays, CalendarKey = r.CalendarId ?? PcCurve.ContractCalendar }).ToList();

        /// <summary>The working calendars the items need, loaded once: key 0 is the calendar of the contract.</summary>
        public static async Task<Func<int, PcWorkCalendar>> CalendarsAsync(IUnitOfWork uow, PcContractTerms terms, IEnumerable<int> keys, CancellationToken cancellationToken)
        {
            var map = new Dictionary<int, PcWorkCalendar> { [PcCurve.ContractCalendar] = await PcContext.CalendarForContractAsync(uow, terms, cancellationToken) };
            foreach (int key in keys.Where(k => k != PcCurve.ContractCalendar).Distinct())
                map[key] = await PcContext.CalendarAsync(uow, key, cancellationToken);
            return key => map.TryGetValue(key, out PcWorkCalendar cal) ? cal : map[PcCurve.ContractCalendar];
        }

        /// <summary>Computes the curve of the rows with the calendars of today and returns the rows to store (not added to the context).</summary>
        public static async Task<List<PcBaselineCostPeriod>> PeriodsAsync(IUnitOfWork uow, int baselineId, PcContractTerms terms, IList<PcBaselineActivity> rows, CancellationToken cancellationToken)
        {
            if (rows.Count == 0) return new List<PcBaselineCostPeriod>();
            List<PcCurveItem> items = ItemsOf(rows);
            Func<int, PcWorkCalendar> calendars = await CalendarsAsync(uow, terms, items.Select(i => i.CalendarKey), cancellationToken);
            PcCurve curve = PcCurve.Build(items, calendars, terms?.AccrualMode ?? PcAccrualMode.Linear);
            return curve.ToRows(baselineId);
        }

        /// <summary>Adds the frozen rows of a baseline that has none (a baseline of before R1c, or one whose rows were not written). Returns true when rows were added; the caller saves.</summary>
        public static async Task<bool> EnsureAsync(IUnitOfWork uow, PcBaseline baseline, CancellationToken cancellationToken)
        {
            if (await uow.Set<PcBaselineCostPeriod>().AnyAsync(p => p.BaselineId == baseline.Id, cancellationToken)) return false;
            List<PcBaselineActivity> rows = await uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == baseline.Id).OrderBy(r => r.ActivityId).ToListAsync(cancellationToken);
            PcContractTerms terms = await PcContext.TermsAsync(uow, baseline.ContractId, false, cancellationToken);
            List<PcBaselineCostPeriod> periods = await PeriodsAsync(uow, baseline.Id, terms, rows, cancellationToken);
            if (periods.Count == 0) return false;
            uow.Set<PcBaselineCostPeriod>().AddRange(periods);
            return true;
        }
    }

    /// <summary>R1c (H1, M4): the rows of a baseline and what has to be true before they are written or approved.</summary>
    internal static class PcBaselineRows
    {
        /// <summary>The row of an activity (dates are the dates of the plan run).</summary>
        public static PcBaselineActivity RowOf(PcActivity a, decimal? effectiveWeight) => new PcBaselineActivity
        {
            ActivityId = a.Id, Code = a.Code, Title = a.Title, WbsNodeId = a.WbsNodeId, Phase = a.Phase, Start = a.EarlyStart.Value.Date, Finish = a.EarlyFinish.Value.Date, DurationDays = a.DurationDays,
            Weight = a.Weight, Budget = a.BudgetAtCompletion, ProgressMethod = a.ProgressMethod, CalendarId = a.CalendarId, ResponsibleUnit = a.ResponsibleUnit, EffectiveWeight = effectiveWeight,
        };

        /// <summary>
        /// Null when a baseline may be built from these activities (ISO 21500 / PMBOK 7: the cost baseline is the approved budget): the budget at completion is above zero and every
        /// activity carries a budget or a weight. Otherwise the Persian error.
        /// </summary>
        public static string CreationProblem(IList<PcActivity> activities)
        {
            if (activities.Count == 0) return "فعالیتی برای ثبت خط مبنا وجود ندارد.";
            if (activities.Sum(a => a.BudgetAtCompletion) <= 0m) return "مجموع بودجه فعالیت‌ها باید بیشتر از صفر باشد؛ بودجه فعالیت‌ها را وارد کنید.";
            List<string> bare = activities.Where(a => a.BudgetAtCompletion <= 0m && a.Weight <= 0m).Select(a => a.Code).ToList();
            if (bare.Count > 0)
                return "هر فعالیت باید بودجه یا وزن داشته باشد؛ برای این فعالیت‌ها هیچ‌کدام ثبت نشده است: " + string.Join("، ", bare.Take(10)) + (bare.Count > 10 ? " و " + (bare.Count - 10).ToString(CultureInfo.InvariantCulture) + " مورد دیگر" : string.Empty) + ".";
            return null;
        }

        /// <summary>The same check on the rows of a draft baseline (approval): the budget at completion is above zero and every row carries a budget or a weight.</summary>
        public static string RowsProblem(IList<PcBaselineActivity> rows)
        {
            if (rows.Count == 0) return "فعالیتی برای ثبت خط مبنا وجود ندارد.";
            if (rows.Sum(r => r.Budget) <= 0m) return "مجموع بودجه فعالیت‌ها باید بیشتر از صفر باشد؛ بودجه فعالیت‌ها را وارد کنید و خط مبنای تازه بسازید.";
            List<string> bare = rows.Where(r => r.Budget <= 0m && r.Weight <= 0m).Select(r => r.Code).ToList();
            if (bare.Count > 0) return "هر فعالیت باید بودجه یا وزن داشته باشد؛ برای این فعالیت‌ها هیچ‌کدام ثبت نشده است: " + string.Join("، ", bare.Take(10)) + (bare.Count > 10 ? " و " + (bare.Count - 10).ToString(CultureInfo.InvariantCulture) + " مورد دیگر" : string.Empty) + ".";
            return null;
        }

        /// <summary>The responsible unit as the signature slots see it (trimmed, upper case, empty is none).</summary>
        private static string UnitKey(string unit) => string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim().ToUpperInvariant();

        /// <summary>The codes of the activities whose plan differs between the rows of a draft and the live plan (also the activities added or removed since). Empty when the draft is still the plan.</summary>
        public static List<string> Differences(IList<PcBaselineActivity> rows, IList<PcActivity> live, IDictionary<int, decimal> effectiveWeights)
        {
            var result = new List<string>();
            Dictionary<int, PcActivity> byId = live.ToDictionary(a => a.Id);
            foreach (PcBaselineActivity r in rows)
            {
                if (!byId.TryGetValue(r.ActivityId, out PcActivity a)) { result.Add(r.Code); continue; }
                decimal? weight = effectiveWeights != null && effectiveWeights.TryGetValue(a.Id, out decimal w) ? w : (decimal?)null;
                bool weightDiffers = r.EffectiveWeight.HasValue && weight.HasValue && Math.Abs(r.EffectiveWeight.Value - weight.Value) > 0.000001m;
                if (a.EarlyStart?.Date != r.Start.Date || a.EarlyFinish?.Date != r.Finish.Date || a.DurationDays != r.DurationDays || a.Weight != r.Weight || a.BudgetAtCompletion != r.Budget
                    || a.WbsNodeId != r.WbsNodeId || a.Phase != r.Phase || a.CalendarId != r.CalendarId || weightDiffers
                    || UnitKey(a.ResponsibleUnit) != UnitKey(r.ResponsibleUnit))
                    result.Add(r.Code);
            }
            var inRows = new HashSet<int>(rows.Select(r => r.ActivityId));
            result.AddRange(live.Where(a => !inRows.Contains(a.Id)).Select(a => a.Code));
            return result;
        }
    }
}
