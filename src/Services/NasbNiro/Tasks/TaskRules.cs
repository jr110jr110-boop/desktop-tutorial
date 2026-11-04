using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.Tasks;

namespace Services.NasbNiro.Tasks
{
    /// <summary>Role names (upper case, as in the JWT) of the task module. Every signed-in user may create and work on tasks; the rules of who sees and who approves are in <see cref="TaskService"/>.</summary>
    public static class TaskRoles
    {
        /// <summary>See every non-personal, non-confidential task, and reopen a closed one.</summary>
        public const string Managers = "PMO,CEO";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (the task module and the progress approval use 601..610).</summary>
    internal static class TaskNumbers
    {
        public const int Task = 601;
        public const int ProgressSubmission = 602;
        public const int Risk = 603;
        public const int Issue = 604;
        public const int Lesson = 605;
        public const int Evaluation = 606;
        public const int WeeklyReport = 607;
    }

    /// <summary>Pure rules of the task module (no database): the status machine, reminder offsets, recurrence and the roll-up of tasks into the percent of a WBS activity.</summary>
    public static class TaskRules
    {
        public static readonly string[] Actions = { "start", "complete", "approve", "return", "reject", "suspend", "resume", "reopen" };

        /// <summary>
        /// The status machine. start: Open or Assigned to InProgress. complete: Open, Assigned or InProgress to Pending (a personal task goes straight to Closed). approve: Pending to Closed.
        /// return: Pending back to InProgress. reject: any open status to Rejected. suspend: Open, Assigned or InProgress to Suspended. resume: Suspended back to where it was. reopen: Closed or Rejected to Open.
        /// </summary>
        public static bool TryNext(TmTaskStatus from, string action, TmTaskStatus? before, out TmTaskStatus to)
        {
            to = from;
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "start":
                    if (from != TmTaskStatus.Open && from != TmTaskStatus.Assigned) return false;
                    to = TmTaskStatus.InProgress; return true;
                case "complete":
                    if (from != TmTaskStatus.Open && from != TmTaskStatus.Assigned && from != TmTaskStatus.InProgress) return false;
                    to = TmTaskStatus.Pending; return true;
                case "approve":
                    if (from != TmTaskStatus.Pending) return false;
                    to = TmTaskStatus.Closed; return true;
                case "return":
                    if (from != TmTaskStatus.Pending) return false;
                    to = TmTaskStatus.InProgress; return true;
                case "reject":
                    if (from != TmTaskStatus.Open && from != TmTaskStatus.Assigned && from != TmTaskStatus.InProgress && from != TmTaskStatus.Pending) return false;
                    to = TmTaskStatus.Rejected; return true;
                case "suspend":
                    if (from != TmTaskStatus.Open && from != TmTaskStatus.Assigned && from != TmTaskStatus.InProgress) return false;
                    to = TmTaskStatus.Suspended; return true;
                case "resume":
                    if (from != TmTaskStatus.Suspended) return false;
                    to = before.HasValue && before.Value != TmTaskStatus.Suspended ? before.Value : TmTaskStatus.Open; return true;
                case "reopen":
                    if (from != TmTaskStatus.Closed && from != TmTaskStatus.Rejected) return false;
                    to = TmTaskStatus.Open; return true;
                default:
                    return false;
            }
        }

        public static bool IsOpen(TmTaskStatus s) => s != TmTaskStatus.Closed && s != TmTaskStatus.Rejected;

        public static string StatusText(TmTaskStatus s)
        {
            switch (s)
            {
                case TmTaskStatus.Open: return "باز";
                case TmTaskStatus.InProgress: return "در دست اقدام";
                case TmTaskStatus.Pending: return "در انتظار تایید";
                case TmTaskStatus.Assigned: return "ارجاع‌شده";
                case TmTaskStatus.Closed: return "بسته‌شده";
                case TmTaskStatus.Rejected: return "ردشده";
                case TmTaskStatus.Suspended: return "معلق";
                default: return s.ToString();
            }
        }

        /// <summary>"7, 3 ،1" to {7, 3, 1}: whole numbers 0..365, distinct, largest first. Anything else is dropped.</summary>
        public static List<int> ParseOffsets(string csv)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(csv)) return result;
            foreach (string part in csv.Split(new[] { ',', '،', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(Digits(part.Trim()), NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v >= 0 && v <= 365 && !result.Contains(v)) result.Add(v);
            return result.OrderByDescending(v => v).ToList();
        }

        /// <summary>True when every part of the text is a valid offset (used to refuse a typo instead of dropping it silently).</summary>
        public static bool OffsetsValid(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return true;
            foreach (string part in csv.Split(new[] { ',', '،', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!int.TryParse(Digits(part.Trim()), NumberStyles.None, CultureInfo.InvariantCulture, out int v) || v < 0 || v > 365) return false;
            return true;
        }

        private static string Digits(string s)
        {
            var chars = s.Select(c => c >= '۰' && c <= '۹' ? (char)('0' + (c - '۰')) : c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray();
            return new string(chars);
        }

        /// <summary>The next due date of a recurring task; null when the rule ends before it.</summary>
        public static DateTime? NextOccurrence(DateTime due, TmRecurrence rule, int interval, DateTime? end)
        {
            if (rule == TmRecurrence.None) return null;
            int n = Math.Max(1, interval);
            DateTime next;
            switch (rule)
            {
                case TmRecurrence.Daily: next = due.AddDays(n); break;
                case TmRecurrence.Weekly: next = due.AddDays(7 * n); break;
                case TmRecurrence.Monthly:
                    {
                        var pc = new PersianCalendar();
                        next = pc.AddMonths(due, n);
                        break;
                    }
                case TmRecurrence.Yearly: next = new PersianCalendar().AddYears(due, n); break;
                default: return null;
            }
            if (end.HasValue && next.Date > end.Value.Date) return null;
            return next.Date;
        }

        /// <summary>
        /// The percent of a WBS activity from its tasks: the tasks weighted by their estimated hours (a task without an estimate counts as one hour); the percent is the one the approval cartable approved (closing a task never counts as 100 by itself), a rejected one is left out.
        /// Null when no task counts.
        /// </summary>
        public static decimal? RollUpPercent(IEnumerable<(decimal EstimatedHours, decimal Percent, TmTaskStatus Status)> tasks)
        {
            decimal weights = 0m, points = 0m;
            foreach (var t in tasks)
            {
                if (t.Status == TmTaskStatus.Rejected) continue;
                decimal w = t.EstimatedHours > 0m ? t.EstimatedHours : 1m;
                decimal p = Math.Min(100m, Math.Max(0m, t.Percent));
                weights += w; points += w * p;
            }
            return weights <= 0m ? (decimal?)null : Math.Round(points / weights, 4, MidpointRounding.AwayFromZero);
        }

        /// <summary>The start (Saturday) and the end (Friday) of the Iranian week that contains the day.</summary>
        public static (DateTime Start, DateTime End) WeekOf(DateTime day)
        {
            int back = ((int)day.DayOfWeek + 1) % 7; // Saturday = 0 ... Friday = 6
            DateTime start = day.Date.AddDays(-back);
            return (start, start.AddDays(6));
        }

        public static (DateTime Start, DateTime End) MonthOf(DateTime day)
        {
            var pc = new PersianCalendar();
            int y = pc.GetYear(day), m = pc.GetMonth(day);
            DateTime start = pc.ToDateTime(y, m, 1, 0, 0, 0, 0);
            int days = pc.GetDaysInMonth(y, m);
            return (start, start.AddDays(days - 1));
        }
    }
}
