using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ceo;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

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
        public void Issued_Cheques_Are_Ok_When_Nothing_Falls_Due_Whatever_The_Cash_Is()
        {
            // review item 10: a negative cash with no cheque due is judged by the cash figure, not by the cheques figure
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(0m, 0m, -5m), "nothing is due in 7 or 30 days");
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(0m, 0m, 0m));
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(0m, 0m, 1_000m));
            Assert.AreEqual(CeoStatus.Warn, CeoRules.IssuedCheques(0m, 10m, -5m), "something is due within thirty days and there is no cash");
            Assert.AreEqual(CeoStatus.Danger, CeoRules.IssuedCheques(10m, 10m, -5m), "something is due within seven days and there is no cash");
            Assert.AreEqual(CeoStatus.Ok, CeoRules.IssuedCheques(500m, 500m, 500m), "exactly the cash is covered");
            Assert.AreEqual(CeoStatus.Danger, CeoRules.Cash(-5m), "the negative cash itself is still a danger on its own figure");
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
        public void Apply_Ends_The_Trend_With_The_Live_Value_And_Sets_Delta_Against_The_Previous_Persian_Month_Only()
        {
            // today is in Aban 1405; Mehr 1405 starts on 2026-09-23
            var points = new List<CeoTrendPoint>
            {
                new CeoTrendPoint { Month = new DateTime(2026, 8, 23), Date = new DateTime(2026, 9, 22), Value = 100m },
                new CeoTrendPoint { Month = new DateTime(2026, 9, 23), Date = new DateTime(2026, 10, 22), Value = 120m },
            };
            var kpi = new CeoKpiDto { Key = "openNcrs", Unit = CeoUnit.Count, Value = 150m };
            CeoTrends.Apply(kpi, points, Today, higherIsBetter: false);
            CollectionAssert.AreEqual(new decimal[] { 100m, 120m, 150m }, kpi.Trend, "the live value is the point of the current month");
            CollectionAssert.AreEqual(new[] { "شهریور ۱۴۰۵", "مهر ۱۴۰۵", "آبان ۱۴۰۵" }, kpi.TrendLabels);
            Assert.AreEqual(30m, kpi.Delta);
            Assert.AreEqual("۳۰ مورد بیشتر از ماه قبل", kpi.DeltaText);
            Assert.AreEqual(false, kpi.DeltaGood, "more open NCRs is not good");

            // the previous month has no point: no delta, the points still show (the older month and the live value)
            var older = new List<CeoTrendPoint> { points[0] };
            var kpi2 = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 5m };
            CeoTrends.Apply(kpi2, older, Today, higherIsBetter: true);
            Assert.IsNull(kpi2.Delta);
            Assert.IsNull(kpi2.DeltaText);
            Assert.IsNull(kpi2.DeltaGood);
            CollectionAssert.AreEqual(new decimal[] { 100m, 5m }, kpi2.Trend);

            // no snapshot at all (go-live): the sparkline still shows one point, the live value; there is nothing to compare with
            var kpi3 = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 5m };
            CeoTrends.Apply(kpi3, new List<CeoTrendPoint>(), Today, true);
            CollectionAssert.AreEqual(new decimal[] { 5m }, kpi3.Trend);
            CollectionAssert.AreEqual(new[] { "آبان ۱۴۰۵" }, kpi3.TrendLabels);
            Assert.IsNull(kpi3.Delta);
            Assert.IsNull(kpi3.DeltaText);
            Assert.IsNull(kpi3.DeltaGood);

            // a figure that has no value now (nothing to show) and no history has no trend either
            var kpi4 = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = null };
            CeoTrends.Apply(kpi4, new List<CeoTrendPoint>(), Today, true);
            Assert.IsNull(kpi4.Trend);
            Assert.IsNull(kpi4.TrendLabels);
            Assert.IsNull(kpi4.Delta);
        }

        [TestMethod]
        public void A_Snapshot_Of_The_Current_Month_Never_Shows_Twice_The_Live_Value_Replaces_It()
        {
            var points = new List<CeoTrendPoint>
            {
                new CeoTrendPoint { Month = new DateTime(2026, 9, 23), Date = new DateTime(2026, 10, 22), Value = 120m },
                new CeoTrendPoint { Month = new DateTime(2026, 10, 23), Date = new DateTime(2026, 10, 24), Value = 999m },   // "snapshot now" of this month
            };
            var kpi = new CeoKpiDto { Key = "openNcrs", Unit = CeoUnit.Count, Value = 150m };

            CeoTrends.Apply(kpi, points, Today, higherIsBetter: false);

            CollectionAssert.AreEqual(new decimal[] { 120m, 150m }, kpi.Trend);
            Assert.AreEqual(30m, kpi.Delta, "the change is against the end of last month, not against the snapshot of this one");
        }

        [TestMethod]
        public void The_Change_Is_Rounded_To_The_Display_Precision_Of_The_Unit_Before_It_Is_Judged()
        {
            var previous = new List<CeoTrendPoint> { new CeoTrendPoint { Month = new DateTime(2026, 9, 23), Date = new DateTime(2026, 10, 22), Value = 1000m } };

            // rials are whole rials: 0.4 of a rial is no change
            var rial = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 1000.4m };
            CeoTrends.Apply(rial, previous, Today, higherIsBetter: true);
            Assert.AreEqual(0m, rial.Delta);
            Assert.AreEqual("بدون تغییر نسبت به ماه قبل", rial.DeltaText);
            Assert.IsNull(rial.DeltaGood, "a change that rounds to nothing has no opinion");

            var rialMore = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 1000.5m };
            CeoTrends.Apply(rialMore, previous, Today, higherIsBetter: true);
            Assert.AreEqual(1m, rialMore.Delta, "half away from zero: one rial");
            Assert.AreEqual(true, rialMore.DeltaGood);

            var rialLess = new CeoKpiDto { Key = "cash", Unit = CeoUnit.Rial, Value = 999.5m };
            CeoTrends.Apply(rialLess, previous, Today, higherIsBetter: true);
            Assert.AreEqual(-1m, rialLess.Delta);
            Assert.AreEqual(false, rialLess.DeltaGood);

            // percentages are tenths
            var percentSame = new CeoKpiDto { Key = "oee", Unit = CeoUnit.Percent, Value = 1000.04m };
            CeoTrends.Apply(percentSame, previous, Today, higherIsBetter: true);
            Assert.AreEqual(0m, percentSame.Delta);
            Assert.AreEqual("بدون تغییر نسبت به ماه قبل", percentSame.DeltaText);
            Assert.IsNull(percentSame.DeltaGood);

            var percentMore = new CeoKpiDto { Key = "oee", Unit = CeoUnit.Percent, Value = 1000.06m };
            CeoTrends.Apply(percentMore, previous, Today, higherIsBetter: true);
            Assert.AreEqual(0.1m, percentMore.Delta);
            Assert.AreEqual("۰٫۱٪ بیشتر از ماه قبل", percentMore.DeltaText);

            // days are whole, counts keep two decimals
            var days = new CeoKpiDto { Key = "lateDays", Unit = CeoUnit.Days, Value = 1000.4m };
            CeoTrends.Apply(days, previous, Today, higherIsBetter: false);
            Assert.AreEqual(0m, days.Delta);
            Assert.IsNull(days.DeltaGood);

            var count = new CeoKpiDto { Key = "openNcrs", Unit = CeoUnit.Count, Value = 1000.004m };
            CeoTrends.Apply(count, previous, Today, higherIsBetter: false);
            Assert.AreEqual(0m, count.Delta);
            Assert.IsNull(count.DeltaGood);

            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 2 }, new[] { CeoTrends.Precision(CeoUnit.Rial), CeoTrends.Precision(CeoUnit.Days), CeoTrends.Precision(CeoUnit.Percent), CeoTrends.Precision(CeoUnit.Hours), CeoTrends.Precision(CeoUnit.Count) });
        }

        // ------------------------------------------------------------------ red and amber projects: one definition

        private static PcPortfolioRowDto Project(string spi, string cpi) => new PcPortfolioRowDto { SpiStatus = spi, CpiStatus = cpi };

        [TestMethod]
        public void A_Project_Is_Red_When_Any_Index_Is_Red_Amber_When_None_Is_Red_And_Any_Is_Amber_And_Never_Both()
        {
            PcPortfolioRowDto[] rows =
            {
                Project("red", "red"), Project("red", "amber"), Project("amber", "red"), Project("red", "green"), Project("green", "red"), Project(null, "red"),
                Project("amber", "amber"), Project("amber", "green"), Project("green", "amber"), Project("amber", null),
                Project("green", "green"), Project(null, null),
            };

            Assert.AreEqual(6, rows.Count(PcReportService.IsRed));
            Assert.AreEqual(4, rows.Count(PcReportService.IsAmber), "a red project that also has an amber index is not counted twice");
            Assert.AreEqual(0, rows.Count(r => PcReportService.IsRed(r) && PcReportService.IsAmber(r)), "no project has two colours");
            Assert.AreEqual(2, rows.Length - rows.Count(PcReportService.IsRed) - rows.Count(PcReportService.IsAmber), "the rest are green");
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
