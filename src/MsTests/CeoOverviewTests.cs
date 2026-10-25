using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ceo;
using ViewModels.NasbNiro.CeoDtos;

namespace MsTests
{
    /// <summary>The pure rules of the CEO overview: status thresholds, change against the previous month, Persian month labels, short rial text and the month-end choice of snapshots.</summary>
    [TestClass]
    public class CeoOverviewTests
    {
        // 1405/08/03 (Aban 1405)
        private static readonly DateTime Today = new DateTime(2026, 10, 25);

        // ------------------------------------------------------------------ status thresholds

        [TestMethod]
        public void Count_Status_Is_Ok_Below_Warn_Warn_From_Warn_And_Danger_From_Danger()
        {
            Assert.AreEqual(CeoStatus.Ok, CeoRules.ByCount(0, 1, 5));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.ByCount(1, 1, 5));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.ByCount(4, 1, 5));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.ByCount(5, 1, 5));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.ByCount(50, 1, 5));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.ByAny(0, 3));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.ByAny(2, 3));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.ByAny(3, 3));
        }

        [TestMethod]
        public void Cash_Is_Danger_Only_When_Negative()
        {
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Cash(-1m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Cash(0m), "an empty till is not a debt");
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Cash(48_200_000_000m));
        }

        [TestMethod]
        public void Profit_Oee_HitRate_And_Satisfaction_Have_Their_Thresholds()
        {
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Profit(-1m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Profit(0m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Oee(39.9m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Oee(40m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Oee(59.9m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Oee(60m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.HitRate(19.9m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.HitRate(20m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Satisfaction(2.9m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Satisfaction(3m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Satisfaction(3.9m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Satisfaction(4m));
        }

        [TestMethod]
        public void Overdue_Receivables_Are_Danger_From_Thirty_Percent_Of_The_Open_Amount()
        {
            Assert.AreEqual(CeoStatus.Ok, CeoRules.ReceivablesOverdue(0m, 1000m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.ReceivablesOverdue(299m, 1000m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.ReceivablesOverdue(300m, 1000m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.ReceivablesOverdue(5m, 0m), "nothing open to compare with: still a warning");
        }

        [TestMethod]
        public void Overdue_Payables_Are_Danger_Only_When_They_Exceed_The_Cash()
        {
            Assert.AreEqual(CeoStatus.Ok, CeoRules.PayablesOverdue(0m, 0m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.PayablesOverdue(500m, 500m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.PayablesOverdue(501m, 500m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.PayablesOverdue(1m, -10m));
        }

        [TestMethod]
        public void Issued_Cheques_Are_Danger_When_Seven_Days_Exceed_The_Cash_And_Warn_When_Thirty_Days_Do()
        {
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(0m, 0m, 0m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.IssuedCheques(100m, 900m, 500m));
            Assert.AreEqual(CeoStatus.Danger, CeoRules.IssuedCheques(600m, 900m, 500m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(100m, 400m, 500m));
        }

        [TestMethod]
        public void Projects_Light_Follows_The_Worst_Project()
        {
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Projects(1, 3));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Projects(0, 1));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Projects(0, 0));
        }

        [TestMethod]
        public void Pulse_Is_Danger_With_Any_Danger_Warn_With_Any_Warn_Ok_With_A_Rule_And_Neutral_Without()
        {
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Pulse(new[] { CeoStatus.Ok, CeoStatus.Warn, CeoStatus.Danger }));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.Pulse(new[] { CeoStatus.Ok, CeoStatus.Neutral, CeoStatus.Warn }));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.Pulse(new[] { CeoStatus.Ok, CeoStatus.Neutral }));
            Assert.AreEqual(CeoStatus.Neutral, CeoRules.Pulse(new[] { CeoStatus.Neutral }));
            Assert.AreEqual(CeoStatus.Neutral, CeoRules.Pulse(new string[0]));
        }

        // ------------------------------------------------------------------ change against the previous month

        [TestMethod]
        public void Delta_Good_Follows_The_Preferred_Direction()
        {
            Assert.AreEqual(true, CeoRules.DeltaGood(10m, true), "cash rose");
            Assert.AreEqual(false, CeoRules.DeltaGood(-10m, true), "cash fell");
            Assert.AreEqual(true, CeoRules.DeltaGood(-2m, false), "open NCRs fell");
            Assert.AreEqual(false, CeoRules.DeltaGood(2m, false), "open NCRs rose");
            Assert.IsNull(CeoRules.DeltaGood(0m, true), "no change: no opinion");
            Assert.IsNull(CeoRules.DeltaGood(5m, null), "active projects have no preferred direction");
        }

        [TestMethod]
        public void Delta_Text_Has_No_Sign_And_Says_More_Or_Less_Than_The_Previous_Month()
        {
            Assert.AreEqual("۲٫۱ میلیارد ریال بیشتر از ماه قبل", CeoText.DeltaText(2_100_000_000m, CeoUnit.Rial));
            Assert.AreEqual("۳۵۰ میلیون ریال کمتر از ماه قبل", CeoText.DeltaText(-350_000_000m, CeoUnit.Rial));
            Assert.AreEqual("۳ مورد بیشتر از ماه قبل", CeoText.DeltaText(3m, CeoUnit.Count));
            Assert.AreEqual("۲٫۵٪ کمتر از ماه قبل", CeoText.DeltaText(-2.5m, CeoUnit.Percent));
            Assert.AreEqual("بدون تغییر نسبت به ماه قبل", CeoText.DeltaText(0m, CeoUnit.Count));
        }

        [TestMethod]
        public void Apply_Sets_Delta_Against_The_Last_Snapshot_Of_The_Previous_Persian_Month_Only()
        {
            // today is in Aban 1405; Mehr 1405 starts on 2026-09-23
            var points = new List<CeoTrendPoint>
            {
                new CeoTrendPoint { Month = new DateTime(2026, 8, 23), Date = new DateTime(2026, 9, 22), Value = 100m },
                new CeoTrendPoint { Month = new DateTime(2026, 9, 23), Date = new DateTime(2026, 10, 22), Value = 120m },
            };
            var kpi = new CeoKpiDto { Key = "openNcrs", Unit = CeoUnit.Count, Value = 150m };
            CeoTrends.Apply(kpi, points, Today, higherIsBetter: false);
            CollectionAssert.AreEqual(new decimal[] { 100m, 120m }, kpi.Trend);
            CollectionAssert.AreEqual(new[] { "شهریور ۱۴۰۵", "مهر ۱۴۰۵" }, kpi.TrendLabels);
            Assert.AreEqual(30m, kpi.Delta);
            Assert.AreEqual("۳۰ مورد بیشتر از ماه قبل", kpi.DeltaText);
            Assert.AreEqual(false, kpi.DeltaGood, "more open NCRs is not good");

            // the previous month has no snapshot: no delta, the points still show
            var older = new List<CeoTrendPoint> { points[0] };
            var kpi2 = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 5m };
            CeoTrends.Apply(kpi2, older, Today, higherIsBetter: true);
            Assert.IsNull(kpi2.Delta);
            Assert.IsNull(kpi2.DeltaText);
            Assert.IsNull(kpi2.DeltaGood);
            Assert.AreEqual(1, kpi2.Trend.Count);

            // no snapshot at all: no trend
            var kpi3 = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 5m };
            CeoTrends.Apply(kpi3, new List<CeoTrendPoint>(), Today, true);
            Assert.IsNull(kpi3.Trend);
            Assert.IsNull(kpi3.TrendLabels);
            Assert.IsNull(kpi3.Delta);
        }

        // ------------------------------------------------------------------ Persian months

        [TestMethod]
        public void Month_Window_Crosses_The_Year_Boundary_With_The_Right_Persian_Labels()
        {
            // 1405/08/03: the twelve months are Azar 1404 ... Aban 1405 (Esfand 1404 is followed by Farvardin 1405)
            List<DateTime> window = CeoTrends.Window(Today);
            Assert.AreEqual(12, window.Count);
            CollectionAssert.AreEqual(new[]
            {
                "آذر ۱۴۰۴", "دی ۱۴۰۴", "بهمن ۱۴۰۴", "اسفند ۱۴۰۴", "فروردین ۱۴۰۵", "اردیبهشت ۱۴۰۵",
                "خرداد ۱۴۰۵", "تیر ۱۴۰۵", "مرداد ۱۴۰۵", "شهریور ۱۴۰۵", "مهر ۱۴۰۵", "آبان ۱۴۰۵",
            }, CeoTrends.Labels(window));
            Assert.AreEqual(new DateTime(2026, 10, 23), window[11], "Aban 1405 starts on 2026-10-23");
            Assert.AreEqual(new DateTime(2026, 3, 21), window[4], "Farvardin 1405 starts on 2026-03-21");
        }

        [TestMethod]
        public void Month_Arithmetic_Handles_Year_Boundaries_Both_Ways()
        {
            DateTime farvardin = CeoTrends.MonthStart(new DateTime(2026, 3, 25));
            Assert.AreEqual(new DateTime(2026, 3, 21), farvardin);
            Assert.AreEqual("اسفند ۱۴۰۴", CeoText.MonthLabel(CeoTrends.AddMonths(farvardin, -1)));
            Assert.AreEqual("فروردین ۱۴۰۶", CeoText.MonthLabel(CeoTrends.AddMonths(farvardin, 12)));
            Assert.AreEqual("بهمن ۱۴۰۴", CeoText.MonthLabel(CeoTrends.AddMonths(farvardin, -2)));
            Assert.AreEqual("اسفند ۱۴۰۵", CeoText.MonthLabel(CeoTrends.AddMonths(farvardin, 11)));
            (DateTime from, DateTime to) = CeoTrends.Span(CeoTrends.MonthStart(Today));
            Assert.AreEqual(new DateTime(2026, 10, 23), from);
            Assert.AreEqual(new DateTime(2026, 11, 21), to, "Aban has 30 days");
        }

        [TestMethod]
        public void Sums_Are_Added_To_The_Months_Of_The_Window_And_Days_Outside_Are_Ignored()
        {
            List<DateTime> window = CeoTrends.Window(Today, 3); // Shahrivar, Mehr, Aban 1405
            decimal[] sums = CeoTrends.SumByMonth(window, new[]
            {
                (new DateTime(2026, 10, 24), 10m),   // Aban
                (new DateTime(2026, 10, 1), 5m),     // Mehr
                (new DateTime(2026, 10, 22), 2m),    // Mehr
                (new DateTime(2026, 6, 1), 99m),     // Khordad: outside
            });
            CollectionAssert.AreEqual(new[] { 0m, 7m, 10m }, sums);
        }

        // ------------------------------------------------------------------ text

        [TestMethod]
        public void Short_Rial_Text_Uses_Thousand_Million_And_Billion_With_One_Decimal()
        {
            Assert.AreEqual("۴۸٫۲ میلیارد ریال", CeoText.ShortRial(48_200_000_000m));
            Assert.AreEqual("۳۵۰ میلیون ریال", CeoText.ShortRial(350_000_000m));
            Assert.AreEqual("۹۰۰ هزار ریال", CeoText.ShortRial(900_000m));
            Assert.AreEqual("۹۵۰ ریال", CeoText.ShortRial(950m));
            Assert.AreEqual("۰ ریال", CeoText.ShortRial(0m));
            Assert.AreEqual("۱٫۳ میلیون ریال", CeoText.ShortRial(1_250_000m), "one decimal, half away from zero");
            Assert.AreEqual("۱٬۲۳۴٫۵ میلیارد ریال", CeoText.ShortRial(1_234_500_000_000m), "thousands of billions keep the separator");
            Assert.AreEqual("-۴۸٫۲ میلیارد ریال", CeoText.ShortRial(-48_200_000_000m));
            Assert.AreEqual("۱ میلیون ریال", CeoText.ShortRial(999_950m), "999.95 thousand rounds up to one million");
            Assert.AreEqual("۱ هزار ریال", CeoText.ShortRial(1_000m));
        }

        [TestMethod]
        public void Value_Text_Uses_Persian_Digits_Separators_And_The_Unit()
        {
            Assert.AreEqual("۱٬۲۳۴٬۵۶۷ ریال", CeoText.ValueText(1234567m, CeoUnit.Rial));
            Assert.AreEqual("۱۲٫۵٪", CeoText.ValueText(12.5m, CeoUnit.Percent));
            Assert.AreEqual("۷", CeoText.ValueText(7m, CeoUnit.Count));
            Assert.AreEqual("۳ روز", CeoText.ValueText(3m, CeoUnit.Days));
            Assert.AreEqual("۱۲٫۵ ساعت", CeoText.ValueText(12.5m, CeoUnit.Hours));
            Assert.AreEqual("—", CeoText.ValueText(null, CeoUnit.Rial));
            Assert.IsNull(CeoText.ShortText(5m, CeoUnit.Count), "only rials have a short text");
            Assert.AreEqual("۵۰۰ هزار ریال", CeoText.ShortText(500_000m, CeoUnit.Rial));
        }

        [TestMethod]
        public void Shamsi_Date_Text_Matches_The_Other_Modules()
        {
            Assert.AreEqual("1405/08/03", CeoText.Date(Today));
            Assert.AreEqual("آبان ۱۴۰۵", CeoText.MonthLabel(Today));
        }

        // ------------------------------------------------------------------ snapshots: the month-end choice

        [TestMethod]
        public void Month_End_Choice_Takes_The_Latest_Snapshot_Of_Each_Persian_Month()
        {
            var rows = new List<(DateTime Date, decimal Value)>
            {
                (new DateTime(2026, 9, 25), 1m),   // Mehr (Mehr: 09-23 .. 10-22)
                (new DateTime(2026, 10, 20), 2m),  // Mehr, later
                (new DateTime(2026, 10, 22), 3m),  // Mehr, the last day of the month
                (new DateTime(2026, 10, 23), 4m),  // Aban, first day
                (new DateTime(2026, 10, 25), 5m),  // Aban, later: this is the Aban point
                (new DateTime(2026, 3, 20), 6m),   // Esfand 1404
                (new DateTime(2025, 1, 1), 7m),    // outside the window
            };
            List<CeoTrendPoint> points = CeoTrends.MonthEnds(rows, Today);
            CollectionAssert.AreEqual(new[] { 6m, 3m, 5m }, points.Select(p => p.Value).ToArray());
            CollectionAssert.AreEqual(new[] { "اسفند ۱۴۰۴", "مهر ۱۴۰۵", "آبان ۱۴۰۵" }, points.Select(p => CeoText.MonthLabel(p.Month)).ToArray());
            Assert.AreEqual(new DateTime(2026, 10, 22), points[1].Date);
        }

        [TestMethod]
        public void Two_Snapshots_On_The_Same_Day_Give_One_Point_And_No_Rows_Give_None()
        {
            // the store keeps one row per (day, key); even if two rows of one day were read, the month still has one point
            var rows = new List<(DateTime Date, decimal Value)> { (Today, 8m), (Today, 8m) };
            Assert.AreEqual(1, CeoTrends.MonthEnds(rows, Today).Count);
            Assert.AreEqual(0, CeoTrends.MonthEnds(new List<(DateTime Date, decimal Value)>(), Today).Count);
            Assert.AreEqual(0, CeoTrends.MonthEnds(null, Today).Count);
        }

        [TestMethod]
        public void The_Headline_And_Snapshot_Key_Lists_Are_Fixed()
        {
            CollectionAssert.AreEqual(new[] { "cash", "receivablesOpen", "payablesOverdue", "salesBacklog", "activeProjects", "lateProductionOrders", "openNcrs", "serviceSlaBreaches" }, CeoKeys.Headline);
            CollectionAssert.AreEqual(new[] { "finance", "sales", "projects", "production", "quality", "inventory", "procurement", "service", "office" }, CeoKeys.Sections);
            Assert.AreEqual(12, CeoKeys.Snapshot.Length);
            Assert.AreEqual(CeoKeys.Snapshot.Length, CeoKeys.Snapshot.Distinct().Count(), "snapshot keys are unique");
            Assert.IsTrue(CeoKeys.Snapshot.All(k => k.Length <= 60), "the key column holds 60 characters");
            foreach (string key in CeoKeys.Headline) CollectionAssert.Contains(CeoKeys.Snapshot, key);
        }
    }
}
