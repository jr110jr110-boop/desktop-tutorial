using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AfterSales;

namespace MsTests
{
    /// <summary>The pure rules of after-sales service: SLA due times and breaches, the warranty period and decision, the periodic visit dates, KPI arithmetic, survey averages and cost privacy.</summary>
    [TestClass]
    public class ServiceMathTests
    {
        [TestMethod]
        public void Sla_Due_Is_Received_Plus_Hours_And_Empty_Without_A_Rule()
        {
            var received = new DateTime(2026, 10, 25, 9, 30, 0);
            Assert.AreEqual(new DateTime(2026, 10, 25, 13, 30, 0), SvSla.Due(received, 4m));
            Assert.AreEqual(new DateTime(2026, 10, 26, 10, 0, 0), SvSla.Due(received, 24.5m));
            Assert.IsNull(SvSla.Due(received, null));
        }

        [TestMethod]
        public void Sla_Breach_Compares_The_Response_Or_Now_With_The_Due_Time()
        {
            var due = new DateTime(2026, 10, 25, 13, 30, 0);
            Assert.IsFalse(SvSla.Breached(due, new DateTime(2026, 10, 25, 13, 30, 0), new DateTime(2026, 11, 1)), "answered exactly on time");
            Assert.IsTrue(SvSla.Breached(due, new DateTime(2026, 10, 25, 13, 31, 0), new DateTime(2026, 10, 25, 14, 0, 0)), "answered a minute late");
            Assert.IsTrue(SvSla.Breached(due, null, new DateTime(2026, 10, 25, 13, 31, 0)), "still open after the due time");
            Assert.IsFalse(SvSla.Breached(due, null, new DateTime(2026, 10, 25, 13, 30, 0)), "open at the due time");
            Assert.IsFalse(SvSla.Breached(null, null, new DateTime(2030, 1, 1)), "no SLA, no breach");
        }

        [TestMethod]
        public void Warranty_Period_And_Coverage()
        {
            DateTime end = SvWarranty.End(new DateTime(2026, 1, 31, 10, 0, 0), 12);
            Assert.AreEqual(new DateTime(2027, 1, 31), end);
            Assert.AreEqual(new DateTime(2026, 2, 28), SvWarranty.End(new DateTime(2026, 1, 31), 1));
            var start = new DateTime(2026, 1, 31);
            Assert.IsTrue(SvWarranty.Covers(start, end, start));
            Assert.IsTrue(SvWarranty.Covers(start, end, end.AddHours(15)), "the whole end day is covered");
            Assert.IsFalse(SvWarranty.Covers(start, end, end.AddDays(1)));
            Assert.IsFalse(SvWarranty.Covers(start, end, start.AddDays(-1)));
            Assert.IsFalse(SvWarranty.Covers(null, null, start));
        }

        [TestMethod]
        public void Unit_Status_Order_Of_Precedence()
        {
            Assert.AreEqual(SvUnitStatus.Decommissioned, SvWarranty.Status(true, true, true));
            Assert.AreEqual(SvUnitStatus.InWarranty, SvWarranty.Status(false, true, true));
            Assert.AreEqual(SvUnitStatus.UnderLtsa, SvWarranty.Status(false, true, false));
            Assert.AreEqual(SvUnitStatus.OutOfWarranty, SvWarranty.Status(false, false, false));
        }

        [TestMethod]
        public void Warranty_Decision_Rules()
        {
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, true, false, false));
            Assert.IsNotNull(SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, false, false, false), "out of warranty");
            Assert.IsNotNull(SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, true, true, false), "misuse");
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Billable, false, true, false), "billable is always allowed");
            Assert.IsNotNull(SvWarranty.CheckDecision(SvWarrantyDecision.Goodwill, false, false, false), "goodwill needs a request");
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Goodwill, false, false, true));
            Assert.IsNotNull(SvWarranty.CheckDecision(SvWarrantyDecision.None, true, false, true));
        }

        [TestMethod]
        public void Ltsa_Visit_Dates_And_Lead_Window()
        {
            List<DateTime> dates = SvLtsa.VisitDates(new DateTime(2026, 1, 15), new DateTime(2027, 1, 15), 4);
            CollectionAssert.AreEqual(new[] { new DateTime(2026, 5, 15), new DateTime(2026, 9, 15), new DateTime(2027, 1, 15) }, dates);
            Assert.AreEqual(0, SvLtsa.VisitDates(new DateTime(2026, 1, 15), new DateTime(2027, 1, 15), 0).Count);
            Assert.AreEqual(0, SvLtsa.VisitDates(new DateTime(2026, 1, 15), new DateTime(2026, 3, 1), 2).Count, "the second month is past the end");
            var visit = new DateTime(2026, 5, 15);
            Assert.IsTrue(SvLtsa.Due(visit, new DateTime(2026, 4, 15), 30), "exactly the lead days before");
            Assert.IsFalse(SvLtsa.Due(visit, new DateTime(2026, 4, 14), 30));
            Assert.IsTrue(SvLtsa.Due(visit, new DateTime(2026, 6, 1), 30), "after the date it is still due");
        }

        [TestMethod]
        public void Kpi_Averages_Mtbf_And_Repeats()
        {
            var t0 = new DateTime(2026, 10, 1, 8, 0, 0);
            decimal? avg = SvKpiMath.AverageHours(new[] { (t0, t0.AddHours(2)), (t0, t0.AddHours(5)), (t0, t0.AddHours(5.5)) });
            Assert.AreEqual(4.17m, avg, "(2 + 5 + 5.5) / 3 = 4.1666...");
            Assert.IsNull(SvKpiMath.AverageHours(new List<(DateTime, DateTime)>()));
            Assert.AreEqual(1250m, SvKpiMath.Mtbf(5000m, 4));
            Assert.AreEqual(1666.67m, SvKpiMath.Mtbf(5000m, 3));
            Assert.IsNull(SvKpiMath.Mtbf(5000m, 0));
            Assert.AreEqual(0, SvKpiMath.Repeats(0));
            Assert.AreEqual(0, SvKpiMath.Repeats(1));
            Assert.AreEqual(2, SvKpiMath.Repeats(3));
        }

        [TestMethod]
        public void Survey_Scores_And_Average()
        {
            Assert.IsTrue(SvSurveyMath.ValidScore(1));
            Assert.IsTrue(SvSurveyMath.ValidScore(5));
            Assert.IsFalse(SvSurveyMath.ValidScore(0));
            Assert.IsFalse(SvSurveyMath.ValidScore(6));
            Assert.AreEqual(4.25m, SvSurveyMath.Average(new[] { 5, 4, 4, 4 }));
            Assert.AreEqual(3.33m, SvSurveyMath.Average(new[] { 3, 3, 4 }));
            Assert.IsNull(SvSurveyMath.Average(new int[0]));
        }

        [TestMethod]
        public void Only_Verified_Answers_Make_The_Satisfaction_Average_And_The_Rest_Is_Counted_Apart()
        {
            // R1p review 10: the people the scores are about typed them in, so an average counts only the surveys QA verified with the customer; the others are counted next to it
            var deleted = new SlSurvey { Id = 6, Status = SlSurveyStatus.Answered, Verified = true, AverageScore = 5m };
            deleted.Delete();
            IQueryable<SlSurvey> surveys = new List<SlSurvey>
            {
                new SlSurvey { Id = 1, Status = SlSurveyStatus.Answered, Verified = true, AverageScore = 4m },
                new SlSurvey { Id = 2, Status = SlSurveyStatus.Answered, Verified = true, AverageScore = 3.5m },
                new SlSurvey { Id = 3, Status = SlSurveyStatus.Answered, Verified = false, AverageScore = 5m },
                new SlSurvey { Id = 4, Status = SlSurveyStatus.Sent, Verified = false, AverageScore = null },
                new SlSurvey { Id = 5, Status = SlSurveyStatus.Skipped, Verified = true, AverageScore = null },
                deleted,
                new SlSurvey { Id = 7, Status = SlSurveyStatus.Answered, Verified = true, AverageScore = null },
            }.AsQueryable();
            CollectionAssert.AreEqual(new[] { 1, 2 }, SvSurveyMath.VerifiedAnswers(surveys).Select(s => s.Id).ToArray(), "answered, verified, with a score, not deleted");
            CollectionAssert.AreEqual(new[] { 3 }, SvSurveyMath.UnverifiedAnswers(surveys).Select(s => s.Id).ToArray(), "answered but not verified");
            Assert.AreEqual(3.75m, SvSurveyMath.VerifiedAnswers(surveys).Average(s => s.AverageScore.Value), "(4 + 3.5) / 2 - the unverified 5 is not in it");
        }

        [TestMethod]
        public void Cost_Math_Rounds_The_Labour_To_The_Whole_Rial_And_The_Engineers_Are_Named_Only_To_Service_And_Payroll()
        {
            Assert.AreEqual(1500000m, SvCostMath.Labour(7.5m, 200000m));
            Assert.AreEqual(333m, SvCostMath.Labour(1m, 333.4m));
            Assert.AreEqual(9m, SvCostMath.BillableHours(3m, 6m));
            // R1p D2 (review items 1 and 2): the engineers named on an order and on its time entries are visible to SERVICE and the payroll readers (and the engineer himself) only;
            // PMO, SALES and the quality roles read the hours without a name
            CollectionAssert.AreEquivalent(new[] { "SERVICE", "PAYROLL", "FINANCEMANAGER", "CEO" }, SvRoles.NameSeers.Split(','));
            CollectionAssert.AreEquivalent(new[] { "PAYROLL", "FINANCEMANAGER", "CEO" }, SvRoles.PayrollSeers.Split(','));
            foreach (string role in new[] { "PMO", "SALES", "QA", "QC", "AUDITOR", "WAREHOUSE", "FINANCIAL" }) Assert.IsFalse(SvRoles.NameSeers.Split(',').Contains(role), role);
            // the month-end run is for the finance manager and the CEO; finance and payroll read the closed months
            CollectionAssert.AreEquivalent(new[] { "FINANCEMANAGER", "CEO" }, SvLabourService.Runners.Split(','));
            CollectionAssert.IsSubsetOf(new[] { "FINANCEMANAGER", "CEO", "FINANCIAL", "PAYROLL" }, SvLabourService.Readers.Split(','));
        }

        [TestMethod]
        public void Persian_Year_Starts_On_The_First_Of_Farvardin()
        {
            Assert.AreEqual(new DateTime(2026, 3, 21), SvPersianYear.Start(new DateTime(2026, 10, 25)));
            Assert.AreEqual(new DateTime(2026, 3, 21), SvPersianYear.Start(new DateTime(2026, 3, 21)));
            Assert.AreEqual(new DateTime(2025, 3, 21), SvPersianYear.Start(new DateTime(2026, 3, 20)));
        }

        [TestMethod]
        public void Limits_Refuse_A_Text_Longer_Than_Its_Column_And_Cut_A_System_Text()
        {
            Assert.IsNull(SvLimits.TooLong(new string('x', 4000), SvLimits.LongText, "شرح"));
            string msg = SvLimits.TooLong(new string('x', 4001), SvLimits.LongText, "شرح");
            Assert.IsNotNull(msg);
            StringAssert.Contains(msg, "حداکثر ۴۰۰۰ نویسه");
            Assert.IsNull(SvLimits.TooLong(null, 10, "x"));
            StringAssert.Contains(SvLimits.First(("ok", 10, "الف"), (new string('y', 301), SvLimits.Title, "عنوان")), "حداکثر ۳۰۰ نویسه");
            Assert.IsNull(SvLimits.First(("ok", 10, "الف")));
            Assert.AreEqual(4000, SvLimits.Cut(new string('z', 5000), 4000).Length);
            Assert.AreEqual("abc", SvLimits.Cut("abc", 4000));
        }

        [TestMethod]
        public void Jobs_Guard_Refuses_A_Future_Day_Unless_The_Test_Setting_Is_On()
        {
            var today = new DateTime(2026, 10, 25, 14, 0, 0);
            Assert.IsNull(SvJobsGuard.Refusal(new DateTime(2026, 10, 25), today, false));
            Assert.IsNull(SvJobsGuard.Refusal(new DateTime(2026, 10, 1), today, false));
            Assert.IsNotNull(SvJobsGuard.Refusal(new DateTime(2026, 10, 26), today, false));
            Assert.IsNull(SvJobsGuard.Refusal(new DateTime(2027, 1, 1), today, true));
            Assert.AreEqual("Service:AllowFutureAsOf", SvJobsGuard.AllowFutureKey);
        }

        [TestMethod]
        public void Parts_Documents_Follow_The_State_Of_Their_Order()
        {
            Assert.IsNull(SvDocumentRules.CheckConfirm(SvOrderStatus.Planned, "SVO-1"));
            Assert.IsNull(SvDocumentRules.CheckConfirm(SvOrderStatus.InProgress, "SVO-1"));
            Assert.IsNotNull(SvDocumentRules.CheckConfirm(SvOrderStatus.Draft, "SVO-1"));
            Assert.IsNotNull(SvDocumentRules.CheckConfirm(SvOrderStatus.Closed, "SVO-1"));
            Assert.IsNotNull(SvDocumentRules.CheckConfirm(SvOrderStatus.Cancelled, "SVO-1"));
            Assert.IsNotNull(SvDocumentRules.CheckCancel(SvOrderStatus.Closed, "SVO-1"));
            Assert.IsNull(SvDocumentRules.CheckCancel(SvOrderStatus.InProgress, "SVO-1"));
            Assert.IsNull(SvDocumentRules.CheckCancel(SvOrderStatus.Planned, "SVO-1"));
        }

        [TestMethod]
        public void Warranty_Event_Splits_The_Parts_Credit_And_Uses_The_Service_Labour_Account()
        {
            // O1 of the smoke: 12 h x 400,000 = 4,800,000 labour, 500,000 consumed, 80,000 left on site
            var lines = SvPosting.WarrantyLines(true, 4800000m, 500000m, 80000m, "SVO-1");
            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual(5380000m, lines.Where(l => l.Role == Services.NasbNiro.Finance.AccountRoles.WarrantyProvision).Sum(l => l.Debit));
            Assert.AreEqual(4800000m, lines.Where(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed).Sum(l => l.Credit));
            Assert.AreEqual(500000m, lines.Where(l => l.Role == Services.NasbNiro.Finance.AccountRoles.MaterialExpense).Sum(l => l.Credit));
            Assert.AreEqual(80000m, lines.Where(l => l.Role == Services.NasbNiro.Finance.AccountRoles.Cogs).Sum(l => l.Credit));
            Assert.AreEqual(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
            Assert.IsFalse(lines.Any(l => l.Role == Services.NasbNiro.Finance.AccountRoles.LabourAbsorbed));

            // goodwill with labour only: the expense account against the service labour account, nothing else
            var goodwill = SvPosting.WarrantyLines(false, 1200000m, 0m, 0m, "SVO-3");
            Assert.AreEqual(2, goodwill.Count);
            Assert.AreEqual(1200000m, goodwill.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.WarrantyExpense).Debit);
            Assert.AreEqual(1200000m, goodwill.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed).Credit);
        }

        [TestMethod]
        public void The_Labour_Of_An_Order_Is_Posted_At_The_Close_At_The_Standard_Rate_Whoever_The_Engineer_Is()
        {
            // R1p round 3: the labour of an order is hours x the standard rate that finance set for the after-sales work centre, never the engineer's own pay and never an average of a pool, so one engineer on one order
            // no longer defers it and the amount tells nothing about anybody's pay (the month-end variance is a separate company-level line).
            // 12 h at the standard rate 90,000 = 1,080,000
            decimal labour = global::Services.NasbNiro.Production.MfLabourRates.Absorbed(12m, 90000m);
            Assert.AreEqual(1080000m, labour);
            var lines = SvPosting.WarrantyLines(true, labour, 500000m, 80000m, "SVO-4");
            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual(1660000m, lines.Sum(l => l.Debit), "1,080,000 labour + 500,000 consumed + 80,000 left on site");
            Assert.AreEqual(1080000m, lines.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed).Credit);
            Assert.AreEqual(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
            // 7.5 h at 101,111.1111: 758,333.33 -> 758,333 (whole rial, half away from zero)
            Assert.AreEqual(758333m, global::Services.NasbNiro.Production.MfLabourRates.Absorbed(7.5m, 101111.1111m));
            // 7.5 h at the standard 90,000 = 675,000
            Assert.AreEqual(675000m, global::Services.NasbNiro.Production.MfLabourRates.Absorbed(7.5m, 90000m));
            // parts only: the lines carry no labour amount
            var partsOnly = SvPosting.WarrantyLines(true, 0m, 500000m, 80000m, "SVO-4");
            Assert.AreEqual(3, partsOnly.Count);
            Assert.AreEqual(580000m, partsOnly.Sum(l => l.Debit));
            Assert.IsFalse(partsOnly.Any(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed));
            // nothing at all: the lines carry no amount (the service posts no event then)
            Assert.AreEqual(0m, SvPosting.WarrantyLines(true, 0m, 0m, 0m, "SVO-4").Sum(l => l.Debit));
        }

        [TestMethod]
        public void An_Order_Of_The_Earlier_Release_With_A_Small_Payroll_Group_Is_Recognised_By_Its_Markers_And_An_Order_Of_This_Release_Never_Is()
        {
            // the earlier release stored no pooled amount and held back the labour of an order that fewer than three payroll-rated engineers worked on
            Assert.IsTrue(SvLegacyLabour.IsProtected(null, 1, 1));
            Assert.IsTrue(SvLegacyLabour.IsProtected(0m, 2, 2));
            Assert.IsTrue(SvLegacyLabour.IsProtected(null, 5, 2), "five engineers, two of them with a payslip");
            Assert.IsTrue(SvLegacyLabour.IsProtected(-400000m, 2, 2), "the data step keeps the standard-rate part of a converted row as a pooled amount of zero or less: the row stays masked");
            Assert.IsFalse(SvLegacyLabour.IsProtected(null, 3, 3));
            Assert.IsFalse(SvLegacyLabour.IsProtected(null, 4, 0), "nobody with a payslip: there is no pay to show");
            Assert.IsFalse(SvLegacyLabour.IsProtected(null, 0, 0));
            // an order of this release always holds the positive amount its payroll-rated hours absorbed at the standard rate
            Assert.IsFalse(SvLegacyLabour.IsProtected(900000m, 1, 1));
            Assert.IsFalse(SvLegacyLabour.IsProtected(1m, 2, 2));

            var earlier = new SvServiceOrder { Number = "SVO-OLD", LabourEmployeeCount = 1, LabourPayrollEmployeeCount = 1, LabourAmount = 400000m };
            var current = new SvServiceOrder { Number = "SVO-NEW", LabourEmployeeCount = 1, LabourPayrollEmployeeCount = 1, LabourAmount = 400000m, LabourPooledAmount = 360000m };
            var big = new SvServiceOrder { Number = "SVO-BIG", LabourEmployeeCount = 4, LabourPayrollEmployeeCount = 4, LabourAmount = 900000m };
            Assert.IsTrue(SvLegacyLabour.IsProtected(earlier));
            Assert.IsFalse(SvLegacyLabour.IsProtected(current));
            Assert.IsFalse(SvLegacyLabour.IsProtected(big));
            Assert.IsFalse(SvLegacyLabour.IsProtected((SvServiceOrder)null));
            // the query condition gives the same answers as the method
            var protectedByFilter = new[] { earlier, current, big }.AsQueryable().Where(SvLegacyLabour.Filter).Select(o => o.Number).ToList();
            CollectionAssert.AreEqual(new[] { "SVO-OLD" }, protectedByFilter);
            // what a reader gets: nothing of a protected order unless he reads payroll
            Assert.AreEqual(0m, SvLegacyLabour.Visible(400000m, true, false));
            Assert.AreEqual(400000m, SvLegacyLabour.Visible(400000m, true, true));
            Assert.AreEqual(400000m, SvLegacyLabour.Visible(400000m, false, false));
        }

        [TestMethod]
        public void Labour_Months_Are_Persian_Months()
        {
            Assert.AreEqual(140508, SvLabourMonth.Of(new DateTime(2026, 10, 25)));
            Assert.AreEqual(140507, SvLabourMonth.Of(new DateTime(2026, 10, 22)));
            Assert.AreEqual(140509, SvLabourMonth.Next(140508));
            Assert.AreEqual(140601, SvLabourMonth.Next(140512));
            Assert.AreEqual("1405/08", SvLabourMonth.Text(140508));
            Assert.AreEqual(new DateTime(2026, 11, 21), SvLabourMonth.End(140508), "Aban has 30 days: the last day is 30 Aban");
            // a month is due only after it has ended
            Assert.IsFalse(SvLabourMonth.Due(140508, new DateTime(2026, 11, 21)));
            Assert.IsTrue(SvLabourMonth.Due(140508, new DateTime(2026, 11, 22)));
        }

        [TestMethod]
        public void A_Posted_Month_Is_Never_Reopened_A_Later_Close_Goes_To_The_Next_Month()
        {
            var day = new DateTime(2026, 10, 25); // 1405/08
            Assert.AreEqual(140508, SvLabourMonth.Assign(day, new int[0]));
            Assert.AreEqual(140508, SvLabourMonth.Assign(day, new[] { 140507 }), "an earlier month posted does not matter");
            Assert.AreEqual(140509, SvLabourMonth.Assign(day, new[] { 140508 }));
            Assert.AreEqual(140510, SvLabourMonth.Assign(day, new[] { 140508, 140509 }));
        }

        [TestMethod]
        public void The_Service_Month_Closes_Only_After_It_Has_Ended_And_A_Carried_Month_Waits_In_The_Next_Open_One()
        {
            Assert.AreEqual("SVL-140508", SvLabourMonth.Number(140508));
            Assert.IsFalse(SvLabourMonth.Due(140508, new DateTime(2026, 11, 20)), "the month is still running");
            Assert.IsTrue(SvLabourMonth.Due(140508, new DateTime(2026, 11, 22)), "1405/09/01");
            Assert.AreEqual(140509, SvLabourMonth.NextOpen(140508, new int[0]));
            Assert.AreEqual(140510, SvLabourMonth.NextOpen(140508, new[] { 140509 }), "a closed month is never reopened");
            Assert.AreEqual(140601, SvLabourMonth.NextOpen(140512, new[] { 140510 }));
            Assert.AreEqual("ServiceLabourMonth", SvLabourMonth.SourceType);
        }

        [TestMethod]
        public void Spare_Part_Price_Comes_From_The_Newest_List_And_The_Highest_Quantity_Tier()
        {
            var older = new SlPriceList { Id = 1, ValidFrom = new DateTime(2026, 1, 1), Lines = new List<SlPriceListLine> { new SlPriceListLine { ItemId = 7, UnitPrice = 100m, MinQuantity = 0m } } };
            var newer = new SlPriceList { Id = 2, ValidFrom = new DateTime(2026, 6, 1), Lines = new List<SlPriceListLine> { new SlPriceListLine { ItemId = 7, UnitPrice = 300m, MinQuantity = 0m }, new SlPriceListLine { ItemId = 7, UnitPrice = 250m, MinQuantity = 10m } } };
            var lists = new[] { older, newer };
            Assert.AreEqual(300m, SvPricing.PriceOf(lists, 7, 1m));
            Assert.AreEqual(250m, SvPricing.PriceOf(lists, 7, 10m));
            Assert.AreEqual(250m, SvPricing.PriceOf(lists, 7, 25m));
            Assert.IsNull(SvPricing.PriceOf(lists, 8, 1m), "an item without a price is never priced by guess");
            Assert.IsNull(SvPricing.PriceOf(new SlPriceList[0], 7, 1m));
        }

        [TestMethod]
        public void Stamp_Carries_The_Persian_Date_And_The_Clock_Time()
        {
            string stamp = SvText.Stamp(new DateTime(2026, 10, 25, 16, 5, 0));
            Assert.IsTrue(stamp.EndsWith(" 16:05"), stamp);
            Assert.IsNull(SvText.Stamp(null));
        }

        [TestMethod]
        public void Entity_Model_With_The_Service_Tables_Builds()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.Configure<ViewModels.Identity.Settings.SiteSettings>(o => o.CookieOptions = new ViewModels.Identity.Settings.CookieOptions { DistributedSqlServerCacheOptions = new ViewModels.Identity.Settings.DistributedSqlServerCacheOptions { TableName = "AppSqlCache", SchemaName = "dbo" } });
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer("Server=localhost;Database=none;User Id=x;Password=x;TrustServerCertificate=true"));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
            foreach (Type type in new[] { typeof(SvInstalledUnit), typeof(SvServiceRequest), typeof(SvServiceOrder), typeof(SvContract), typeof(SlSurvey), typeof(SvTimeEntry), typeof(SvSlaRule) })
                Assert.IsNotNull(model.FindEntityType(type), type.Name);
            Assert.IsNotNull(model.FindEntityType(typeof(InventoryDocument)).GetForeignKeys().FirstOrDefault(f => f.Properties.Any(p => p.Name == "ServiceOrderId")));
        }
    }
}
