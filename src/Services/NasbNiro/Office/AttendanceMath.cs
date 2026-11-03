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
    /// <summary>
    /// The LAN addresses a time-clock device may push from (release R1f): a comma separated list of IPv4 addresses or IPv4/CIDR ranges ("192.168.1.20,10.0.5.0/24"). An empty list means any address.
    /// </summary>
    internal static class DeviceAddress
    {
        private const int MaxEntries = 10;

        private static bool TryParseEntry(string text, out uint network, out uint mask, out string normalized)
        {
            network = 0; mask = 0; normalized = null;
            string t = OfficeText.Digits(text ?? "").Trim();
            if (t.Length == 0) return false;
            int slash = t.IndexOf('/');
            string ipText = slash < 0 ? t : t.Substring(0, slash);
            int prefix = 32;
            if (slash >= 0 && (!int.TryParse(t.Substring(slash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix < 0 || prefix > 32)) return false;
            // IPAddress.TryParse accepts short forms such as "10.1" and numbers such as "167772161"; only the dotted four-part form is a device address
            string[] parts = ipText.Split('.');
            if (parts.Length != 4 || !parts.All(x => x.Length > 0 && x.Length <= 3 && x.All(char.IsAsciiDigit))) return false;
            if (!System.Net.IPAddress.TryParse(ipText, out System.Net.IPAddress ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            uint value = ToUInt(ip);
            mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            network = value & mask;
            normalized = prefix == 32 ? ip.ToString() : new System.Net.IPAddress(ToBytes(network)).ToString() + "/" + prefix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static uint ToUInt(System.Net.IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static byte[] ToBytes(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        /// <summary>Validates and normalises the list (null for an empty list). The error is a Persian sentence.</summary>
        public static bool TryNormalize(string text, out string normalized, out string error)
        {
            normalized = null; error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            string[] items = text.Split(new[] { ',', '،', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            foreach (string item in items)
            {
                if (!TryParseEntry(item, out _, out _, out string one))
                {
                    error = $"نشانی «{item}» نامعتبر است؛ فهرست نشانی‌های مجاز دستگاه را به صورت IPv4 یا بازه CIDR و با ویرگول جدا بنویسید (مثلاً 192.168.1.20 یا 10.0.5.0/24).";
                    return false;
                }
                if (!list.Contains(one)) list.Add(one);
            }
            if (list.Count > MaxEntries) { error = $"حداکثر {MaxEntries} نشانی یا بازه برای هر دستگاه مجاز است."; return false; }
            string joined = string.Join(",", list);
            if (joined.Length > 200) { error = "فهرست نشانی‌های مجاز از ۲۰۰ نویسه بیشتر است."; return false; }
            normalized = joined;
            return true;
        }

        /// <summary>The address the request came from as an IPv4 number: an IPv4-mapped IPv6 address is unwrapped and the IPv6 loopback counts as 127.0.0.1; any other IPv6 address has no IPv4 form.</summary>
        private static bool TryParseRemote(string remote, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(remote) || !System.Net.IPAddress.TryParse(remote.Trim(), out System.Net.IPAddress ip)) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (System.Net.IPAddress.IsLoopback(ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) ip = System.Net.IPAddress.Loopback;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            value = ToUInt(ip);
            return true;
        }

        /// <summary>True when the list is empty (any address) or the remote address is inside one of its entries.</summary>
        public static bool Allows(string allowed, string remote)
        {
            if (string.IsNullOrWhiteSpace(allowed)) return true;
            if (!TryParseRemote(remote, out uint value)) return false;
            foreach (string item in allowed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryParseEntry(item, out uint network, out uint mask, out _) && (value & mask) == network) return true;
            return false;
        }
    }

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

        /// <summary>The night window (minutes from midnight), read from the shift or, for a day without a shift, from the rate-table keys NightWindowStartMinute / NightWindowEndMinute. No literal default: equal values mean no night window.</summary>
        public int NightStartMinute { get; set; }
        public int NightEndMinute { get; set; }

        /// <summary>A holiday, a weekly off day, or a day the shift does not work.</summary>
        public bool NonWorking { get; set; }

        /// <summary>Clock events of the day (minute resolution, sorted, no duplicates).</summary>
        public List<DateTime> Punches { get; set; } = new List<DateTime>();

        public int GraceMinutes { get; set; }
        public int? MaxOvertimeMinutes { get; set; }

        /// <summary>Overtime and holiday work count only up to the approved overtime of the day (policy switch OvertimeRequiresApproval, starred default on); the rest is reported as unapproved.</summary>
        public bool OvertimeRequiresApproval { get; set; } = true;
        public int RoundingMinutes { get; set; }

        /// <summary>Deduction coefficient for a delay or an early leave beyond the grace (company: 3).</summary>
        public decimal DelayPenaltyFactor { get; set; } = 3m;

        /// <summary>Early leave beyond the grace is deducted like lateness (policy switch EarlyLeaveDeduction, starred default on).</summary>
        public bool EarlyLeaveDeductionEnabled { get; set; } = true;
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

        /// <summary>Deducted time for the delay: DelayMinutes x the coefficient (the whole delay, not only the part above the grace).</summary>
        public int DelayDeductionMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }

        /// <summary>Deducted time for the early leave: EarlyLeaveMinutes x the same coefficient as the delay (0 inside the grace or when the switch is off).</summary>
        public int EarlyLeaveDeductionMinutes { get; set; }
        public int OvertimeMinutes { get; set; }

        /// <summary>Overtime or holiday work no approved request covers: reported, never paid (counted only when the policy requires approval).</summary>
        public int UnapprovedOvertimeMinutes { get; set; }
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
    /// a delay beyond the grace is deducted as delay x coefficient (company: grace 15, coefficient 3: 14 minutes late = 0, 16 minutes late = 48 minutes deducted; no leave is used for lateness);
    /// early leave beyond the grace is deducted the same way (same coefficient) unless the company switch is off;
    /// overtime is the time after the shift end, limited to the approved hours of the day when the policy requires approval (the rest is reported as unapproved), to the policy maximum and rounded down;
    /// night minutes are the part of the presence interval inside the night window; on a non-working day all worked minutes are holiday work, counted in the overtime bucket (same approval rule, maximum and rounding, and the monthly cap at the close).
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
            if (nightStart == nightEnd) return 0; // no night window configured
            int total = 0;
            for (int k = -1; k <= 1; k++)
            {
                DateTime ws = date.Date.AddDays(k).AddMinutes(nightStart);
                DateTime we = date.Date.AddDays(k).AddMinutes(nightEnd <= nightStart ? nightEnd + 1440 : nightEnd);
                total += Overlap(from, to, ws, we);
            }
            return total;
        }

        /// <summary>Deducted minutes for a delay that is already beyond the grace: the whole delay times the coefficient, rounded to a minute. A negative coefficient counts as 0.</summary>
        public static int DelayDeduction(int delayMinutes, decimal factor) =>
            delayMinutes <= 0 || factor <= 0m ? 0 : (int)Math.Round(delayMinutes * factor, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The overtime and the holiday work of a month that count: holiday work sits in the overtime bucket, so ONE monthly cap covers both. The days are filled in date order until the cap is used up;
        /// what comes after is not counted (a cap of 0 or less counts nothing).
        /// </summary>
        public static (int OvertimeMinutes, int HolidayMinutes) MonthlyExtraWork(IEnumerable<(DateTime Date, int OvertimeMinutes, int HolidayMinutes)> days, int capMinutes)
        {
            int left = Math.Max(0, capMinutes), overtime = 0, holiday = 0;
            foreach (var day in days.OrderBy(x => x.Date))
            {
                int takeOvertime = Math.Min(left, Math.Max(0, day.OvertimeMinutes));
                overtime += takeOvertime; left -= takeOvertime;
                int takeHoliday = Math.Min(left, Math.Max(0, day.HolidayMinutes));
                holiday += takeHoliday; left -= takeHoliday;
            }
            return (overtime, holiday);
        }

        /// <summary>
        /// Extra work of one day (overtime after the shift, or the minutes worked on a day off) as it counts: with the approval rule on only up to the approved overtime of the day, never above the daily maximum, rounded down.
        /// <paramref name="unapproved"/> is the part no approved request covers (0 when the rule is off): reported, never paid.
        /// </summary>
        public static int CountedExtraWork(DayInput d, int rawMinutes, out int unapproved)
        {
            int raw = Math.Max(0, rawMinutes);
            int covered = d.OvertimeRequiresApproval ? Math.Min(raw, Math.Max(0, d.ApprovedOvertimeMinutes)) : raw;
            unapproved = d.OvertimeRequiresApproval ? raw - covered : 0;
            int counted = covered;
            if (d.MaxOvertimeMinutes.HasValue) counted = Math.Min(counted, Math.Max(0, d.MaxOvertimeMinutes.Value));
            if (d.RoundingMinutes > 1) counted = counted / d.RoundingMinutes * d.RoundingMinutes;
            return counted;
        }

        /// <summary>
        /// The deducted minutes of a day as a part of a paid day (deducted minutes / scheduled minutes of the shift), at most one day. This is how the lateness deduction reaches payroll:
        /// as fractions of an absence day, so it lowers the paid days exactly once.
        /// </summary>
        public static decimal DeductionDays(int deductionMinutes, int scheduledMinutes) =>
            deductionMinutes <= 0 || scheduledMinutes <= 0 ? 0m : Math.Min(1m, (decimal)deductionMinutes / scheduledMinutes);

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
                    // holiday work is overtime-bucket work: the approval rule, the daily maximum and the rounding apply; the monthly cap is applied at the close
                    r.HolidayMinutes = CountedExtraWork(d, r.WorkedMinutes, out int unapprovedHoliday);
                    r.UnapprovedOvertimeMinutes = unapprovedHoliday;
                    return r;
                }
                if (!d.HasShift) return r;

                var leaveWindows = d.HourlyLeave.Concat(d.HourlyMission).Select(w => Absolute(d, w, start, end)).ToList();
                int delay = Math.Max(0, (int)Math.Floor((r.FirstIn.Value - shiftStart).TotalMinutes));
                if (delay > 0) delay = Math.Max(0, delay - leaveWindows.Sum(w => Overlap(w.From, w.To, shiftStart, r.FirstIn.Value)));
                int early = Math.Max(0, (int)Math.Floor((shiftEnd - r.LastOut.Value).TotalMinutes));
                if (early > 0) early = Math.Max(0, early - leaveWindows.Sum(w => Overlap(w.From, w.To, r.LastOut.Value, shiftEnd)));
                r.DelayMinutes = delay <= d.GraceMinutes ? 0 : delay;
                r.DelayDeductionMinutes = DelayDeduction(r.DelayMinutes, d.DelayPenaltyFactor);
                r.EarlyLeaveMinutes = early <= d.GraceMinutes ? 0 : early;
                // early leave beyond the grace is deducted like lateness: the whole minutes x the same coefficient (company switch EarlyLeaveDeduction, starred default on)
                r.EarlyLeaveDeductionMinutes = d.EarlyLeaveDeductionEnabled ? DelayDeduction(r.EarlyLeaveMinutes, d.DelayPenaltyFactor) : 0;

                int raw = Math.Max(0, (int)Math.Floor((r.LastOut.Value - shiftEnd).TotalMinutes));
                // with the approval rule the approved request of the day is the limit and the rest is reported as unapproved; without it every minute counts. The daily maximum and the rounding apply to both.
                r.OvertimeMinutes = CountedExtraWork(d, raw, out int unapprovedOvertime);
                r.UnapprovedOvertimeMinutes = unapprovedOvertime;

                // hourly leave is a part of a paid day; hourly mission only excuses the delay and the early leave
                // the day is the scheduled working time (end − start − break), the same basis as the lateness deduction and the month close
                int dayMinutes = Math.Max(1, end - start - (d.HasShift ? d.BreakMinutes : 0));
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
