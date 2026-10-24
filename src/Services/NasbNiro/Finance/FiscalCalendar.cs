using System;
using System.Globalization;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// The single place where fiscal years and periods get their dates. Only the Persian (Shamsi) calendar exists for now;
    /// see docs/calendar-seam.md for where another calendar would be added.
    /// </summary>
    public interface IFiscalCalendar
    {
        int YearOf(DateTime date);
        DateTime YearStart(int year);
        DateTime YearEnd(int year);
        DateTime PeriodStart(int year, int number);
        DateTime PeriodEnd(int year, int number);
        string PeriodTitle(int year, int number);
        /// <summary>The date as the user reads it: yyyy/MM/dd in this calendar.</summary>
        string Text(DateTime date);
        /// <summary>Date and time: yyyy/MM/dd HH:mm.</summary>
        string TextTime(DateTime date);
        /// <summary>Digits only (yyyyMMdd) for file names.</summary>
        string Stamp(DateTime date);
        /// <summary>Converts a Gregorian ISO date/time string (as JSON writes it) to this calendar's text; any other string is returned as is.</summary>
        string IsoToText(string value);
    }

    public sealed class PersianFiscalCalendar : IFiscalCalendar
    {
        private static readonly PersianCalendar Cal = new PersianCalendar();
        private static readonly string[] Months =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
        };

        public int YearOf(DateTime date) => Cal.GetYear(date);
        public DateTime YearStart(int year) => Cal.ToDateTime(year, 1, 1, 0, 0, 0, 0);
        public DateTime YearEnd(int year) => Cal.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0).AddDays(-1);
        public DateTime PeriodStart(int year, int number) => Cal.ToDateTime(year, number, 1, 0, 0, 0, 0);
        public DateTime PeriodEnd(int year, int number) => number >= 12 ? YearEnd(year) : Cal.ToDateTime(year, number + 1, 1, 0, 0, 0, 0).AddDays(-1);
        public string Text(DateTime date) => date.Year < 1000 || date.Year > 9000 ? "" : string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", Cal.GetYear(date), Cal.GetMonth(date), Cal.GetDayOfMonth(date));
        public string TextTime(DateTime date) => Text(date) + " " + date.ToString("HH:mm", CultureInfo.InvariantCulture);
        public string Stamp(DateTime date) => Text(date).Replace("/", "");
        private static readonly System.Text.RegularExpressions.Regex Iso = new System.Text.RegularExpressions.Regex(@"^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        public string IsoToText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 10 || value.Length > 40) return value;
            var m = Iso.Match(value);
            if (!m.Success) return value;
            int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (y < 1700 || y > 9000) return value;
            try
            {
                var d = new DateTime(y, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                if (m.Groups[4].Success && (m.Groups[4].Value != "00" || m.Groups[5].Value != "00")) return Text(d) + " " + m.Groups[4].Value + ":" + m.Groups[5].Value;
                return Text(d);
            }
            catch (ArgumentOutOfRangeException) { return value; }
        }
        public string PeriodTitle(int year, int number) => Months[number - 1] + " " + year.ToString(CultureInfo.InvariantCulture);
    }

    public static class FiscalCalendars
    {
        /// <summary>The calendar of every legal entity.</summary>
        public static IFiscalCalendar Current { get; } = new PersianFiscalCalendar();

        /// <summary>Shortcut for what every user-visible date goes through.</summary>
        public static string Text(DateTime date) => Current.Text(date);
    }
}
