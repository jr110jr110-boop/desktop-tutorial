using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Office
{
    /// <summary>The company calendar: weekly off days (Friday when nothing is configured) and the holiday dates.</summary>
    internal sealed class OfficeCalendar
    {
        private readonly HashSet<DayOfWeek> _weekly;
        private readonly HashSet<DateTime> _holidays;

        private OfficeCalendar(HashSet<DayOfWeek> weekly, HashSet<DateTime> holidays)
        {
            _weekly = weekly;
            _holidays = holidays;
        }

        public static async Task<OfficeCalendar> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<DateTimeOffset> dates = await uow.Set<HolidaysByDate>().AsNoTracking().Select(h => h.Date).ToListAsync(ct);
            List<DayOfWeek> weekly = await uow.Set<HolidaysDayOfWeek>().AsNoTracking().Select(h => h.DayOfWeek).Distinct().ToListAsync(ct);
            if (weekly.Count == 0) weekly.Add(DayOfWeek.Friday);
            return new OfficeCalendar(weekly.ToHashSet(), dates.Select(d => d.Date).ToHashSet());
        }

        public bool IsWeeklyOff(DateTime date) => _weekly.Contains(date.DayOfWeek);

        public bool IsHoliday(DateTime date) => _holidays.Contains(date.Date);

        /// <summary>A company holiday or a weekly off day.</summary>
        public bool IsOff(DateTime date) => IsWeeklyOff(date) || IsHoliday(date);

        /// <summary>The day is a working day of the shift (or of the company calendar when there is no shift).</summary>
        public bool IsWorkingDay(DateTime date, OfShift shift)
        {
            if (IsOff(date)) return false;
            if (shift != null && (shift.WorkDaysMask & (1 << (int)date.DayOfWeek)) == 0) return false;
            return true;
        }
    }

    internal sealed class DayInput
    {
        public DateTime Date { get; set; }
        public bool HasShift { get; set; }
        public int StartMinute { get; set; }
        public int EndMinute { get; set; }
        public int BreakMinutes { get; set; }
        public int NightStartMinute { get; set; } = 22 * 60;
        public int NightEndMinute { get; set; } = 6 * 60;

        /// <summary>A holiday, a weekly off day, or a day the shift does not work.</summary>
        public bool NonWorking { get; set; }

        /// <summary>Clock events of the day (minute resolution, sorted, no duplicates).</summary>
        public List<DateTime> Punches { get; set; } = new List<DateTime>();

        public int GraceMinutes { get; set; }
        public int? MaxOvertimeMinutes { get; set; }
        public bool OvertimeNeedsPreApproval { get; set; } = true;
        public int RoundingMinutes { get; set; }
        public int ApprovedOvertimeMinutes { get; set; }

        public bool DailyPaidLeave { get; set; }

        /// <summary>Approved sick leave: a paid day, but counted apart from the annual leave.</summary>
        public bool DailySickLeave { get; set; }
        public bool DailyUnpaidLeave { get; set; }
        public bool DailyMission { get; set; }

        /// <summary>Hourly leave and hourly mission windows, minutes from midnight (end greater than start).</summary>
        public List<(int From, int To)> HourlyLeave { get; set; } = new List<(int, int)>();
        public List<(int From, int To)> HourlyMission { get; set; } = new List<(int, int)>();
    }

    internal sealed class DayResult
    {
        public OfDayStatus? Status { get; set; }
        public bool NonWorkingDay { get; set; }
        public DateTime? FirstIn { get; set; }
        public DateTime? LastOut { get; set; }
        public int WorkedMinutes { get; set; }
        public int DelayMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int NightMinutes { get; set; }
        public int HolidayMinutes { get; set; }
        public int AbsenceDays { get; set; }
        public int UnpaidLeaveDays { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal SickLeaveDays { get; set; }
        public int MissionDays { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// The daily attendance computation, free of any database access so the numbers can be checked by hand.
    /// Rules: the first and the last clock event of the day are the presence interval; delay and early leave beyond the grace are counted in full;
    /// overtime is the time after the shift end, limited to the approved hours (or the policy maximum when no pre-approval is needed) and rounded down;
    /// night minutes are the part of the presence interval inside the night window; on a non-working day all worked minutes are holiday work.
    /// </summary>
    internal static class AttendanceMath
    {
        public static int Overlap(DateTime aFrom, DateTime aTo, DateTime bFrom, DateTime bTo)
        {
            DateTime from = aFrom > bFrom ? aFrom : bFrom;
            DateTime to = aTo < bTo ? aTo : bTo;
            return to > from ? (int)Math.Floor((to - from).TotalMinutes) : 0;
        }

        /// <summary>
        /// Where the day of <paramref name="day"/> ends and the next one begins for the clock events: halfway between the end of this day's shift and the start of the next day's shift.
        /// A shift that ends after midnight keeps its late events; a day without a shift ends at midnight. Consecutive days share the boundary, so no event falls between two days.
        /// </summary>
        public static DateTime Boundary(DateTime day, OfShift today, OfShift next)
        {
            DateTime d = day.Date;
            DateTime midnight = d.AddDays(1);
            if (today == null) return midnight;
            DateTime end = d.AddMinutes(today.EndMinute <= today.StartMinute ? today.EndMinute + 1440 : today.EndMinute);
            if (next == null) return end.AddHours(3) > midnight ? end.AddHours(3) : midnight;
            DateTime nextStart = midnight.AddMinutes(next.StartMinute);
            if (nextStart < end) return end;
            DateTime mid = end.AddTicks((nextStart - end).Ticks / 2);
            return new DateTime(mid.Year, mid.Month, mid.Day, mid.Hour, mid.Minute, 0);
        }

        private static (DateTime From, DateTime To) Absolute(DayInput d, (int From, int To) w, int start, int end)
        {
            int a = w.From, b = w.To;
            if (b <= a) b += 1440;
            if (end > 1440 && a < start) { a += 1440; b += 1440; }
            return (d.Date.AddMinutes(a), d.Date.AddMinutes(b));
        }

        public static int NightOverlap(DateTime date, DateTime from, DateTime to, int nightStart, int nightEnd)
        {
            int total = 0;
            for (int k = -1; k <= 1; k++)
            {
                DateTime ws = date.Date.AddDays(k).AddMinutes(nightStart);
                DateTime we = date.Date.AddDays(k).AddMinutes(nightEnd <= nightStart ? nightEnd + 1440 : nightEnd);
                total += Overlap(from, to, ws, we);
            }
            return total;
        }

        public static DayResult Compute(DayInput d)
        {
            var r = new DayResult { NonWorkingDay = d.NonWorking };
            List<DateTime> p = d.Punches ?? new List<DateTime>();
            bool complete = p.Count >= 2;
            if (p.Count >= 1) r.FirstIn = p[0];
            if (complete) r.LastOut = p[p.Count - 1];

            int start = d.StartMinute;
            int end = d.EndMinute <= d.StartMinute ? d.EndMinute + 1440 : d.EndMinute;
            DateTime shiftStart = d.Date.AddMinutes(start);
            DateTime shiftEnd = d.Date.AddMinutes(end);

            if (complete)
            {
                int span = (int)Math.Floor((r.LastOut.Value - r.FirstIn.Value).TotalMinutes);
                r.WorkedMinutes = Math.Max(0, span - (d.HasShift ? d.BreakMinutes : 0));
                r.NightMinutes = NightOverlap(d.Date, r.FirstIn.Value, r.LastOut.Value, d.NightStartMinute, d.NightEndMinute);
                r.Status = OfDayStatus.Present;
                if (d.NonWorking)
                {
                    r.HolidayMinutes = r.WorkedMinutes;
                    return r;
                }
                if (!d.HasShift) return r;

                var leaveWindows = d.HourlyLeave.Concat(d.HourlyMission).Select(w => Absolute(d, w, start, end)).ToList();
                int delay = Math.Max(0, (int)Math.Floor((r.FirstIn.Value - shiftStart).TotalMinutes));
                if (delay > 0) delay = Math.Max(0, delay - leaveWindows.Sum(w => Overlap(w.From, w.To, shiftStart, r.FirstIn.Value)));
                int early = Math.Max(0, (int)Math.Floor((shiftEnd - r.LastOut.Value).TotalMinutes));
                if (early > 0) early = Math.Max(0, early - leaveWindows.Sum(w => Overlap(w.From, w.To, r.LastOut.Value, shiftEnd)));
                r.DelayMinutes = delay <= d.GraceMinutes ? 0 : delay;
                r.EarlyLeaveMinutes = early <= d.GraceMinutes ? 0 : early;

                int raw = Math.Max(0, (int)Math.Floor((r.LastOut.Value - shiftEnd).TotalMinutes));
                // with pre-approval the approved request is the limit; without it every minute counts up to the daily maximum (no maximum configured: no limit)
                int cap = d.OvertimeNeedsPreApproval ? d.ApprovedOvertimeMinutes : int.MaxValue;
                if (d.MaxOvertimeMinutes.HasValue) cap = Math.Min(cap, d.MaxOvertimeMinutes.Value);
                int overtime = Math.Min(raw, Math.Max(0, cap));
                if (d.RoundingMinutes > 1) overtime = overtime / d.RoundingMinutes * d.RoundingMinutes;
                r.OvertimeMinutes = overtime;

                // hourly leave is a part of a paid day; hourly mission only excuses the delay and the early leave
                int dayMinutes = Math.Max(1, end - start);
                int leaveMinutes = d.HourlyLeave.Select(w => Absolute(d, w, start, end)).Sum(w => Overlap(w.From, w.To, shiftStart, shiftEnd));
                if (leaveMinutes > 0) r.PaidLeaveDays = Math.Round((decimal)leaveMinutes / dayMinutes, 4);
                return r;
            }

            if (d.NonWorking)
            {
                r.Status = OfDayStatus.Rest;
                if (p.Count == 1) r.Note = "فقط یک تردد در روز تعطیل ثبت شده است.";
                return r;
            }
            if (d.DailyMission) { r.Status = OfDayStatus.Mission; r.MissionDays = 1; return r; }
            if (d.DailyPaidLeave) { r.Status = OfDayStatus.Leave; r.PaidLeaveDays = 1; return r; }
            if (d.DailySickLeave) { r.Status = OfDayStatus.Leave; r.SickLeaveDays = 1; return r; }
            if (d.DailyUnpaidLeave) { r.Status = OfDayStatus.Leave; r.UnpaidLeaveDays = 1; return r; }
            if (p.Count == 1)
            {
                r.Status = OfDayStatus.Incomplete;
                r.Note = "فقط یک تردد ثبت شده است؛ ورود یا خروج فراموش شده است.";
                return r;
            }
            r.Status = OfDayStatus.Absent;
            r.AbsenceDays = 1;
            return r;
        }
    }
}
