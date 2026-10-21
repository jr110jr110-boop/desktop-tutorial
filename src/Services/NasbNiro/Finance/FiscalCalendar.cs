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
        public string PeriodTitle(int year, int number) => Months[number - 1] + " " + year.ToString(CultureInfo.InvariantCulture);
    }

    public static class FiscalCalendars
    {
        /// <summary>The calendar of every legal entity.</summary>
        public static IFiscalCalendar Current { get; } = new PersianFiscalCalendar();
    }
}
