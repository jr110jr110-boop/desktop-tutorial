using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>One point of a monthly trend: the last snapshot of a Persian month.</summary>
    public sealed class CeoTrendPoint
    {
        /// <summary>First day of the Persian month the point stands for.</summary>
        public DateTime Month { get; set; }

        /// <summary>The day the snapshot was taken.</summary>
        public DateTime Date { get; set; }

        public decimal Value { get; set; }
    }

    /// <summary>
    /// Monthly windows and trends. Months are Persian months. A figure with no transactional history (a balance, an open count)
    /// is drawn from the nightly snapshots: the point of a month is the last snapshot taken in it (the job stores the figures of the day that ended, so the last
    /// one of a month is the true month end), and a month without a snapshot has no point unless <see cref="CeoHistory"/> can derive it from the records.
    /// </summary>
    public static class CeoTrends
    {
        /// <summary>A trend has at most this many monthly points.</summary>
        public const int MonthCount = 12;

        private static readonly PersianCalendar Calendar = new PersianCalendar();

        /// <summary>First day of the Persian month that contains the day.</summary>
        public static DateTime MonthStart(DateTime day) =>
            Calendar.ToDateTime(Calendar.GetYear(day), Calendar.GetMonth(day), 1, 0, 0, 0, 0).Date;

        /// <summary>The first day of the month that is <paramref name="months"/> months after (or before, when negative) the month starting on <paramref name="monthStart"/>.</summary>
        public static DateTime AddMonths(DateTime monthStart, int months)
        {
            int index = Calendar.GetYear(monthStart) * 12 + (Calendar.GetMonth(monthStart) - 1) + months;
            int year = index / 12;
            int month = index % 12 + 1;
            return Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0).Date;
        }

        /// <summary>The first days of the last <paramref name="count"/> Persian months, oldest first, ending with the month of <paramref name="today"/>.</summary>
        public static List<DateTime> Window(DateTime today, int count = MonthCount)
        {
            DateTime current = MonthStart(today);
            var months = new List<DateTime>();
            for (int i = count - 1; i >= 0; i--) months.Add(AddMonths(current, -i));
            return months;
        }

        /// <summary>The Persian month labels of a window: "آبان ۱۴۰۴", ..., "مهر ۱۴۰۵".</summary>
        public static List<string> Labels(IEnumerable<DateTime> months) => months.Select(CeoText.MonthLabel).ToList();

        /// <summary>
        /// The month-end points of a snapshot series: for each Persian month of the window, the snapshot of the latest day in that month.
        /// Months without a snapshot give no point; the result is oldest first.
        /// </summary>
        public static List<CeoTrendPoint> MonthEnds(IEnumerable<(DateTime Date, decimal Value)> snapshots, DateTime today, int count = MonthCount)
        {
            HashSet<DateTime> window = Window(today, count).ToHashSet();
            return (snapshots ?? Enumerable.Empty<(DateTime Date, decimal Value)>())
                .Select(s => new CeoTrendPoint { Month = MonthStart(s.Date), Date = s.Date.Date, Value = s.Value })
                .Where(p => window.Contains(p.Month))
                .GroupBy(p => p.Month)
                .Select(g => g.OrderByDescending(p => p.Date).First())
                .OrderBy(p => p.Month)
                .ToList();
        }

        /// <summary>The decimals a figure of a unit is shown with: whole rials and days, tenths of a percent and of an hour, hundredths for any other number.</summary>
        public static int Precision(string unit)
        {
            switch (unit)
            {
                case CeoUnit.Rial:
                case CeoUnit.Days: return 0;
                case CeoUnit.Percent:
                case CeoUnit.Hours: return 1;
                default: return 2;
            }
        }

        /// <summary>
        /// Puts the monthly trend on a KPI. The points before the current Persian month come from <paramref name="points"/> (the month-end values of the snapshots,
        /// plus the values derived from the records for the months before the first snapshot); the last point is always the live value of the KPI, so a trend shows
        /// something from the first day. The change is the live value against the point of the previous Persian month (empty when that month has no point), rounded to the
        /// precision the unit is shown in before it is judged: a change that rounds to nothing is "no change" in the number, the text and the good/bad flag alike.
        /// </summary>
        /// <param name="higherIsBetter">True when a rise is favourable, false when a fall is, null when the figure has no preferred direction.</param>
        public static void Apply(CeoKpiDto kpi, IReadOnlyList<CeoTrendPoint> points, DateTime today, bool? higherIsBetter)
        {
            if (kpi == null) return;
            DateTime thisMonth = MonthStart(today);
            var series = (points ?? Array.Empty<CeoTrendPoint>()).Where(p => p.Month < thisMonth).OrderBy(p => p.Month).Select(p => (p.Month, p.Value)).ToList();
            if (kpi.Value.HasValue) series.Add((thisMonth, kpi.Value.Value));
            if (series.Count == 0)
            {
                kpi.Trend = null;
                kpi.TrendLabels = null;
                return;
            }
            kpi.Trend = series.Select(p => p.Value).ToList();
            kpi.TrendLabels = series.Select(p => CeoText.MonthLabel(p.Month)).ToList();
            if (!kpi.Value.HasValue) return;

            DateTime previousMonth = AddMonths(thisMonth, -1);
            int previous = series.FindIndex(p => p.Month == previousMonth);
            if (previous < 0) return;
            decimal delta = Math.Round(kpi.Value.Value - series[previous].Value, Precision(kpi.Unit), MidpointRounding.AwayFromZero);
            kpi.Delta = delta;
            kpi.DeltaText = CeoText.DeltaText(delta, kpi.Unit);
            kpi.DeltaGood = CeoRules.DeltaGood(delta, higherIsBetter);
        }

        /// <summary>The first and last day (inclusive) of the Persian month starting on <paramref name="monthStart"/>.</summary>
        public static (DateTime From, DateTime To) Span(DateTime monthStart) => (monthStart.Date, AddMonths(monthStart, 1).AddDays(-1));

        /// <summary>The index of the window month a day falls in, or -1 when it is outside the window.</summary>
        public static int IndexOf(IReadOnlyList<DateTime> window, DateTime day)
        {
            DateTime month = MonthStart(day);
            for (int i = 0; i < window.Count; i++) if (window[i] == month) return i;
            return -1;
        }

        /// <summary>Adds amounts to the months of a window: one array of sums per call, a day outside the window is ignored.</summary>
        public static decimal[] SumByMonth(IReadOnlyList<DateTime> window, IEnumerable<(DateTime Day, decimal Amount)> rows)
        {
            var sums = new decimal[window.Count];
            foreach (var row in rows ?? Enumerable.Empty<(DateTime Day, decimal Amount)>())
            {
                int i = IndexOf(window, row.Day);
                if (i >= 0) sums[i] += row.Amount;
            }
            return sums;
        }
    }
}
