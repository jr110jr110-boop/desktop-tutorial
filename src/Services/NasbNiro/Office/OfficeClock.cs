using System;

namespace Services.NasbNiro.Office
{
    /// <summary>
    /// Employer rule 14: office time for the work of humans. Office hours are 08:00 to 19:00 (11 hours) in Iran time,
    /// Saturday to Thursday, no lunch break. Only Friday is outside; official holidays count as working days.
    /// Pure static math: no database, no settings.
    /// </summary>
    public static class OfficeClock
    {
        /// <summary>A stopped human task is escalated to the CEO after this many office hours (6 working days and 6 hours).</summary>
        public const int StopLimitHours = 72;

        public const int OpenHour = 8;
        public const int CloseHour = 19;
        public const int HoursPerDay = CloseHour - OpenHour;
        public const int HoursPerWeek = HoursPerDay * 6;   // Saturday to Thursday
        public const int MaxHours = 100000;

        /// <summary>Iran time is a fixed UTC+03:30 (daylight saving was abolished); a constant on purpose, so the math never depends on the server time zone.</summary>
        public static readonly TimeSpan IranOffset = new TimeSpan(3, 30, 0);

        private static bool IsWorkingDay(DateTime d) => d.DayOfWeek != DayOfWeek.Friday;

        /// <summary>First office instant (Iran local time) at or after the given local time.</summary>
        private static DateTime Normalize(DateTime t)
        {
            DateTime day = t.Date;
            if (IsWorkingDay(day))
            {
                if (t < day.AddHours(OpenHour)) return day.AddHours(OpenHour);
                if (t < day.AddHours(CloseHour)) return t;
            }
            day = day.AddDays(1);
            while (!IsWorkingDay(day)) day = day.AddDays(1);
            return day.AddHours(OpenHour);
        }

        private static DateTime ToLocal(DateTimeOffset t) => t.ToUniversalTime().DateTime + IranOffset;

        /// <summary>The instant at which <paramref name="officeHours"/> office hours have passed after <paramref name="start"/>. The result has the offset of <paramref name="start"/>.</summary>
        public static DateTimeOffset Add(DateTimeOffset start, int officeHours)
        {
            if (officeHours <= 0) return start;
            if (officeHours > MaxHours) throw new ArgumentOutOfRangeException(nameof(officeHours), "Office hours above " + MaxHours + " are refused.");
            DateTime t = Normalize(ToLocal(start));
            // a whole week from an office instant holds exactly 66 office hours: jump whole weeks first (keep at least 1 hour for the day loop, so a result exactly at closing time stays on that day)
            int weeks = (officeHours - 1) / HoursPerWeek;
            t = t.AddDays(7 * weeks);
            double left = officeHours - weeks * HoursPerWeek;
            while (true)
            {
                DateTime close = t.Date.AddHours(CloseHour);
                double avail = (close - t).TotalHours;
                if (left <= avail) { t = t.AddHours(left); break; }
                left -= avail;
                t = Normalize(close);
            }
            DateTime utc = t - IranOffset;
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(start.Offset);
        }

        /// <summary>Office hours between two instants (0 when <paramref name="to"/> is not after <paramref name="from"/>).</summary>
        public static double Between(DateTimeOffset from, DateTimeOffset to)
        {
            if (to <= from) return 0;
            DateTime a = ToLocal(from), b = ToLocal(to);
            double total = 0;
            DateTime t = Normalize(a);
            while (t < b)
            {
                DateTime close = t.Date.AddHours(CloseHour);
                DateTime end = b < close ? b : close;
                total += (end - t).TotalHours;
                t = Normalize(close);
            }
            return total;
        }
    }
}
