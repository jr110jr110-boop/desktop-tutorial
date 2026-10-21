using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.ProjectControl;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// Working-day calendar. Day offsets count working days from the first working day on or after the project start:
    /// offset 0 is that day, offset 1 the next working day, and so on. Pure and deterministic (no database).
    /// </summary>
    public class PcWorkCalendar
    {
        private readonly int _weekdayMask;
        private readonly HashSet<DateTime> _nonWorking;
        private readonly HashSet<DateTime> _extraWorking;
        private readonly List<DateTime> _days = new List<DateTime>();
        private DateTime _cursor;
        private bool _started;
        private DateTime _origin;

        private const int MaxDays = 30000;

        public PcWorkCalendar(int weekdayMask, IEnumerable<DateTime> nonWorkingDates, IEnumerable<DateTime> extraWorkingDates)
        {
            _weekdayMask = weekdayMask == 0 ? 0x7F : weekdayMask;
            _nonWorking = new HashSet<DateTime>((nonWorkingDates ?? Enumerable.Empty<DateTime>()).Select(d => d.Date));
            _extraWorking = new HashSet<DateTime>((extraWorkingDates ?? Enumerable.Empty<DateTime>()).Select(d => d.Date));
        }

        /// <summary>Monday to Friday, no holidays: the calendar of the unit tests.</summary>
        public static PcWorkCalendar MondayToFriday() => new PcWorkCalendar(MaskOf(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday), null, null);

        public static int MaskOf(params DayOfWeek[] days)
        {
            int mask = 0;
            foreach (DayOfWeek d in days) mask |= 1 << (int)d;
            return mask;
        }

        public bool IsWorking(DateTime date)
        {
            date = date.Date;
            if (_extraWorking.Contains(date)) return true;
            if (_nonWorking.Contains(date)) return false;
            return (_weekdayMask & (1 << (int)date.DayOfWeek)) != 0;
        }

        public DateTime NextWorkingOnOrAfter(DateTime date)
        {
            date = date.Date;
            for (int i = 0; i < 4000; i++)
            {
                if (IsWorking(date)) return date;
                date = date.AddDays(1);
            }
            throw new InvalidOperationException("The calendar has no working day.");
        }

        public DateTime PreviousWorkingOnOrBefore(DateTime date)
        {
            date = date.Date;
            for (int i = 0; i < 4000; i++)
            {
                if (IsWorking(date)) return date;
                date = date.AddDays(-1);
            }
            throw new InvalidOperationException("The calendar has no working day.");
        }

        /// <summary>The date n working days after the given date (the date itself is moved to a working day first); n may be zero.</summary>
        public DateTime AddWorkingDays(DateTime date, int n)
        {
            DateTime d = NextWorkingOnOrAfter(date);
            for (int i = 0; i < n; i++) d = NextWorkingOnOrAfter(d.AddDays(1));
            return d;
        }

        /// <summary>Number of working days in [from, to) (negative when to is before from).</summary>
        public int WorkingDaysBetween(DateTime from, DateTime to)
        {
            from = from.Date; to = to.Date;
            if (to < from) return -WorkingDaysBetween(to, from);
            int count = 0;
            for (DateTime d = from; d < to; d = d.AddDays(1)) if (IsWorking(d)) count++;
            return count;
        }

        // ---------------------------------------------------------------- offsets

        public void SetOrigin(DateTime projectStart)
        {
            _origin = NextWorkingOnOrAfter(projectStart);
            _days.Clear();
            _cursor = _origin;
            _started = true;
        }

        public DateTime Origin
        {
            get
            {
                if (!_started) throw new InvalidOperationException("Origin not set.");
                return _origin;
            }
        }

        private void Fill(int k)
        {
            if (!_started) throw new InvalidOperationException("Origin not set.");
            while (_days.Count <= k)
            {
                if (_days.Count > MaxDays) throw new InvalidOperationException("The schedule is longer than the calendar limit.");
                DateTime d = NextWorkingOnOrAfter(_cursor);
                _days.Add(d);
                _cursor = d.AddDays(1);
            }
        }

        /// <summary>The k-th working day from the origin (k below zero is clamped to the origin).</summary>
        public DateTime DayOf(int k)
        {
            if (k < 0) k = 0;
            Fill(k);
            return _days[k];
        }

        /// <summary>Offset of a date: working days from the origin to that date; a non-working date maps to the next working day.</summary>
        public int OffsetOf(DateTime date)
        {
            date = NextWorkingOnOrAfter(date);
            if (date <= Origin) return 0;
            int k = 0;
            while (true)
            {
                Fill(k);
                if (_days[k] >= date) return k;
                k++;
            }
        }
    }

    public class CpmNode
    {
        public int Id { get; set; }

        /// <summary>Whole working days; zero is a milestone.</summary>
        public int Duration { get; set; }
        public PcConstraint Constraint { get; set; }
        public DateTime? ConstraintDate { get; set; }

        // forecast inputs (used only when actuals are switched on)
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }

        /// <summary>0..100, to compute the remaining duration of an activity that has started.</summary>
        public decimal Percent { get; set; }

        /// <summary>The finish cannot be before this date (a purchase order delivery date).</summary>
        public DateTime? FinishNotBefore { get; set; }
    }

    public class CpmLink
    {
        public int PredecessorId { get; set; }
        public int SuccessorId { get; set; }
        public PcDependencyType Type { get; set; }
        public int Lag { get; set; }
    }

    public class CpmItem
    {
        public int Id { get; set; }
        public int EarlyStartOffset { get; set; }
        public int EarlyFinishOffset { get; set; }
        public int LateStartOffset { get; set; }
        public int LateFinishOffset { get; set; }
        public DateTime EarlyStart { get; set; }
        public DateTime EarlyFinish { get; set; }
        public DateTime LateStart { get; set; }
        public DateTime LateFinish { get; set; }
        public int TotalFloat { get; set; }
        public int FreeFloat { get; set; }
        public bool Critical { get; set; }
    }

    public class CpmResult
    {
        public bool HasCycle { get; set; }
        public List<int> CycleNodes { get; set; } = new List<int>();
        public Dictionary<int, CpmItem> Items { get; set; } = new Dictionary<int, CpmItem>();

        /// <summary>Offset after the last finish; the project lasts this many working days.</summary>
        public int ProjectFinishOffset { get; set; }
        public DateTime ProjectStart { get; set; }
        public DateTime ProjectFinish { get; set; }

        /// <summary>Critical activities in schedule order.</summary>
        public List<int> CriticalPath { get; set; } = new List<int>();
    }

    /// <summary>
    /// Critical path method with FS/SS/FF/SF links and lags, constraints and a forecast mode.
    /// An activity of d working days starting at offset s occupies [s, s+d). FS: s(succ) >= f(pred) + lag; SS: s(succ) >= s(pred) + lag;
    /// FF: f(succ) >= f(pred) + lag; SF: f(succ) >= s(pred) + lag. A milestone (d = 0) is shown on the working day it completes.
    /// </summary>
    public static class PcCpm
    {
        /// <summary>One cycle (list of node ids) or an empty list.</summary>
        public static List<int> FindCycle(IEnumerable<int> nodeIds, IEnumerable<CpmLink> links)
        {
            var next = new Dictionary<int, List<int>>();
            foreach (int id in nodeIds) next[id] = new List<int>();
            foreach (CpmLink l in links)
            {
                if (!next.ContainsKey(l.PredecessorId)) next[l.PredecessorId] = new List<int>();
                if (!next.ContainsKey(l.SuccessorId)) next[l.SuccessorId] = new List<int>();
                next[l.PredecessorId].Add(l.SuccessorId);
            }
            var color = new Dictionary<int, int>();
            foreach (int start in next.Keys.OrderBy(k => k))
            {
                if (color.TryGetValue(start, out int c0) && c0 != 0) continue;
                var stack = new Stack<(int Node, int Index)>();
                var path = new List<int>();
                stack.Push((start, 0));
                color[start] = 1;
                path.Add(start);
                while (stack.Count > 0)
                {
                    (int node, int idx) = stack.Pop();
                    List<int> outs = next[node];
                    if (idx < outs.Count)
                    {
                        stack.Push((node, idx + 1));
                        int to = outs[idx];
                        color.TryGetValue(to, out int c);
                        if (c == 1)
                        {
                            int at = path.IndexOf(to);
                            return path.Skip(at).ToList();
                        }
                        if (c == 0)
                        {
                            color[to] = 1;
                            path.Add(to);
                            stack.Push((to, 0));
                        }
                    }
                    else
                    {
                        color[node] = 2;
                        path.RemoveAt(path.Count - 1);
                    }
                }
            }
            return new List<int>();
        }

        /// <param name="useActuals">Forecast mode: actual dates, remaining durations and data date are applied.</param>
        /// <param name="dataDate">Work up to and including this date is in the past (forecast mode).</param>
        public static CpmResult Run(IList<CpmNode> nodes, IList<CpmLink> links, PcWorkCalendar calendar, DateTime projectStart, bool useActuals, DateTime? dataDate, int criticalThreshold = 0)
        {
            var result = new CpmResult();
            calendar.SetOrigin(projectStart);
            result.ProjectStart = calendar.Origin;
            if (nodes.Count == 0) { result.ProjectFinish = calendar.Origin; return result; }

            Dictionary<int, CpmNode> byId = nodes.ToDictionary(n => n.Id);
            var incoming = nodes.ToDictionary(n => n.Id, n => new List<CpmLink>());
            var outgoing = nodes.ToDictionary(n => n.Id, n => new List<CpmLink>());
            foreach (CpmLink l in links)
            {
                if (!byId.ContainsKey(l.PredecessorId) || !byId.ContainsKey(l.SuccessorId)) continue;
                incoming[l.SuccessorId].Add(l);
                outgoing[l.PredecessorId].Add(l);
            }

            // topological order (Kahn); ready nodes are taken by ascending id so the result never depends on input order
            var indegree = nodes.ToDictionary(n => n.Id, n => incoming[n.Id].Count);
            var ready = new SortedSet<int>(nodes.Where(n => indegree[n.Id] == 0).Select(n => n.Id));
            var order = new List<int>();
            while (ready.Count > 0)
            {
                int id = ready.Min;
                ready.Remove(id);
                order.Add(id);
                foreach (CpmLink l in outgoing[id])
                {
                    indegree[l.SuccessorId]--;
                    if (indegree[l.SuccessorId] == 0) ready.Add(l.SuccessorId);
                }
            }
            if (order.Count != nodes.Count)
            {
                result.HasCycle = true;
                result.CycleNodes = FindCycle(nodes.Select(n => n.Id), links);
                return result;
            }

            int dataOffset = 0;
            if (useActuals && dataDate.HasValue) dataOffset = calendar.OffsetOf(dataDate.Value) + 1;

            var s = new Dictionary<int, int>();
            var f = new Dictionary<int, int>();
            foreach (int id in order)
            {
                CpmNode n = byId[id];
                int d = Math.Max(0, n.Duration);
                int start = 0;
                foreach (CpmLink l in incoming[id])
                {
                    int need;
                    switch (l.Type)
                    {
                        case PcDependencyType.SS: need = s[l.PredecessorId] + l.Lag; break;
                        case PcDependencyType.FF: need = f[l.PredecessorId] + l.Lag - d; break;
                        case PcDependencyType.SF: need = s[l.PredecessorId] + l.Lag - d; break;
                        default: need = f[l.PredecessorId] + l.Lag; break;
                    }
                    if (need > start) start = need;
                }
                switch (n.Constraint)
                {
                    case PcConstraint.StartNoEarlierThan:
                    case PcConstraint.MustStartOn:
                        if (n.ConstraintDate.HasValue) start = Math.Max(start, d == 0 ? calendar.OffsetOf(n.ConstraintDate.Value) + 1 : calendar.OffsetOf(n.ConstraintDate.Value));
                        break;
                    case PcConstraint.FinishNoEarlierThan:
                    case PcConstraint.MustFinishOn:
                        if (n.ConstraintDate.HasValue) start = Math.Max(start, calendar.OffsetOf(n.ConstraintDate.Value) + 1 - d);
                        break;
                }
                if (start < 0) start = 0;

                int finish;
                if (useActuals && n.ActualFinish.HasValue)
                {
                    finish = calendar.OffsetOf(n.ActualFinish.Value) + 1;
                    start = n.ActualStart.HasValue ? calendar.OffsetOf(n.ActualStart.Value) : Math.Max(0, finish - Math.Max(d, 1));
                    if (d == 0) start = finish;
                }
                else if (useActuals && n.ActualStart.HasValue)
                {
                    start = calendar.OffsetOf(n.ActualStart.Value);
                    int remaining = (int)Math.Ceiling(d * (100m - Math.Min(100m, Math.Max(0m, n.Percent))) / 100m);
                    if (remaining == 0 && n.Percent < 100m && d > 0) remaining = 1;
                    finish = Math.Max(dataOffset, start) + remaining;
                    if (d == 0) finish = start;
                }
                else
                {
                    if (useActuals && dataOffset > start && d > 0) start = dataOffset;
                    finish = start + d;
                    if (useActuals && n.FinishNotBefore.HasValue)
                    {
                        int minFinish = calendar.OffsetOf(n.FinishNotBefore.Value) + 1;
                        if (finish < minFinish) { finish = minFinish; start = Math.Max(start, finish - d); }
                    }
                }
                s[id] = start;
                f[id] = finish;
            }

            int projectFinish = f.Values.Max();
            result.ProjectFinishOffset = projectFinish;

            // backward pass
            var lf = new Dictionary<int, int>();
            var ls = new Dictionary<int, int>();
            foreach (int id in Enumerable.Reverse(order))
            {
                CpmNode n = byId[id];
                int d = Math.Max(0, n.Duration);
                int dur = f[id] - s[id];
                if (!useActuals) dur = d;
                int late = projectFinish;
                foreach (CpmLink l in outgoing[id])
                {
                    int bound;
                    switch (l.Type)
                    {
                        case PcDependencyType.SS: bound = ls[l.SuccessorId] - l.Lag + dur; break;
                        case PcDependencyType.FF: bound = lf[l.SuccessorId] - l.Lag; break;
                        case PcDependencyType.SF: bound = lf[l.SuccessorId] - l.Lag + dur; break;
                        default: bound = ls[l.SuccessorId] - l.Lag; break;
                    }
                    if (bound < late) late = bound;
                }
                switch (n.Constraint)
                {
                    case PcConstraint.StartNoLaterThan:
                    case PcConstraint.MustStartOn:
                        if (n.ConstraintDate.HasValue) late = Math.Min(late, calendar.OffsetOf(n.ConstraintDate.Value) + dur);
                        break;
                    case PcConstraint.FinishNoLaterThan:
                    case PcConstraint.MustFinishOn:
                        if (n.ConstraintDate.HasValue) late = Math.Min(late, calendar.OffsetOf(n.ConstraintDate.Value) + 1);
                        break;
                }
                lf[id] = late;
                ls[id] = late - dur;
            }

            foreach (int id in order)
            {
                CpmNode n = byId[id];
                int d = Math.Max(0, n.Duration);
                int dur = f[id] - s[id];
                int free = projectFinish - f[id];
                foreach (CpmLink l in outgoing[id])
                {
                    int slack;
                    switch (l.Type)
                    {
                        case PcDependencyType.SS: slack = s[l.SuccessorId] - (s[id] + l.Lag); break;
                        case PcDependencyType.FF: slack = f[l.SuccessorId] - (f[id] + l.Lag); break;
                        case PcDependencyType.SF: slack = f[l.SuccessorId] - (s[id] + l.Lag); break;
                        default: slack = s[l.SuccessorId] - (f[id] + l.Lag); break;
                    }
                    if (slack < free) free = slack;
                }
                if (free < 0) free = 0;
                int total = ls[id] - s[id];
                var item = new CpmItem
                {
                    Id = id,
                    EarlyStartOffset = s[id], EarlyFinishOffset = f[id], LateStartOffset = ls[id], LateFinishOffset = lf[id],
                    EarlyStart = StartDate(calendar, s[id], f[id], dur), EarlyFinish = FinishDate(calendar, f[id], dur),
                    LateStart = StartDate(calendar, ls[id], lf[id], dur), LateFinish = FinishDate(calendar, lf[id], dur),
                    TotalFloat = total, FreeFloat = free, Critical = total <= criticalThreshold,
                };
                result.Items[id] = item;
            }
            result.ProjectFinish = calendar.DayOf(Math.Max(projectFinish - 1, 0));
            result.CriticalPath = result.Items.Values.Where(i => i.Critical).OrderBy(i => i.EarlyStartOffset).ThenBy(i => i.EarlyFinishOffset).ThenBy(i => i.Id).Select(i => i.Id).ToList();
            return result;
        }

        private static DateTime StartDate(PcWorkCalendar cal, int start, int finish, int duration) =>
            duration > 0 ? cal.DayOf(start) : cal.DayOf(Math.Max(finish - 1, 0));

        private static DateTime FinishDate(PcWorkCalendar cal, int finish, int duration) => cal.DayOf(Math.Max(finish - 1, 0));
    }
}
