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
        public void Cost_Math_And_Privacy()
        {
            Assert.AreEqual(1500000m, SvCostMath.Labour(7.5m, 200000m));
            Assert.AreEqual(333m, SvCostMath.Labour(1m, 333.4m));
            Assert.AreEqual(9m, SvCostMath.BillableHours(3m, 6m));
            // one or two people behind a payroll-based amount would show a person's pay; three do not; a standard-rate amount (no payroll) is never protected
            Assert.IsTrue(SvPrivacy.LabourProtected(1, 1));
            Assert.IsTrue(SvPrivacy.LabourProtected(2, 2));
            Assert.IsFalse(SvPrivacy.LabourProtected(3, 3));
            Assert.IsFalse(SvPrivacy.LabourProtected(1, 0));
            Assert.IsTrue(SvPrivacy.LabourProtected(5, 2), "only two of the five came from payroll");
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
        public void Protected_Order_Posts_Only_Its_Parts_At_The_Close_And_Nothing_Without_Parts()
        {
            // the labour of a protected order is deferred (zero here): the event carries the parts and no labour amount at all
            Assert.IsTrue(SvLabourMonth.Deferred(480000m, true));
            Assert.IsFalse(SvLabourMonth.Deferred(480000m, false), "a labour of three or more people is posted with the order");
            Assert.IsFalse(SvLabourMonth.Deferred(0m, true), "nothing to defer");
            var lines = SvPosting.WarrantyLines(true, 0m, 500000m, 80000m, "SVO-4");
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(580000m, lines.Sum(l => l.Debit));
            Assert.IsFalse(lines.Any(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed));
            Assert.IsFalse(lines.Any(l => l.Debit == 480000m || l.Credit == 480000m));
            // no parts: the lines carry no amount (the service posts no event then)
            Assert.AreEqual(0m, SvPosting.WarrantyLines(true, 0m, 0m, 0m, "SVO-4").Sum(l => l.Debit));
        }

        private static SvPendingLabour Pend(int id, SvWarrantyDecision decision, int? contract, decimal labour, int[] employees, int[] payroll, string center = "AFTERSALES") =>
            new SvPendingLabour { OrderId = id, Decision = decision, ContractId = contract, CostCenter = center, Labour = labour, Employees = employees.ToHashSet(), PayrollEmployees = payroll.ToHashSet() };

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
        public void Monthly_Event_Has_One_Debit_One_Credit_And_No_Order_Number()
        {
            // O4 of the smoke: one payroll employee, warranty, 480,000
            var lines = SvLabourMonth.EventLines(new[] { Pend(4, SvWarrantyDecision.Warranty, 9, 480000m, new[] { 1 }, new[] { 1 }) }, 140508);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(Services.NasbNiro.Finance.AccountRoles.WarrantyProvision, lines[0].Role);
            Assert.AreEqual(480000m, lines[0].Debit);
            Assert.AreEqual(Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed, lines[1].Role);
            Assert.AreEqual(480000m, lines[1].Credit);
            Assert.IsFalse(lines.Any(l => l.Description.Contains("SVO")), "no order number in any line");
            // goodwill alone goes to the expense account
            var goodwill = SvLabourMonth.EventLines(new[] { Pend(5, SvWarrantyDecision.Goodwill, null, 100m, new[] { 1 }, new[] { 1 }) }, 140508);
            Assert.AreEqual(Services.NasbNiro.Finance.AccountRoles.WarrantyExpense, goodwill[0].Role);
            // billable work and the visits of an agreement post no event
            Assert.AreEqual(0, SvLabourMonth.EventLines(new[] { Pend(6, SvWarrantyDecision.Billable, 9, 700m, new[] { 1 }, new[] { 1 }) }, 140508).Count);
            Assert.AreEqual(0, SvLabourMonth.EventLines(new[] { Pend(7, SvWarrantyDecision.None, 9, 700m, new[] { 1 }, new[] { 1 }) }, 140508).Count);
        }

        [TestMethod]
        public void Monthly_Event_Splits_Warranty_And_Goodwill_Only_When_Each_Has_Three_People()
        {
            var big = new[] { 1, 2, 3 };
            var split = SvLabourMonth.EventLines(new[]
            {
                Pend(1, SvWarrantyDecision.Warranty, null, 600m, big, big),
                Pend(2, SvWarrantyDecision.Goodwill, null, 300m, new[] { 4, 5, 6 }, new[] { 4, 5, 6 }),
            }, 140508);
            Assert.AreEqual(3, split.Count);
            Assert.AreEqual(600m, split.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.WarrantyProvision).Debit);
            Assert.AreEqual(300m, split.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.WarrantyExpense).Debit);
            Assert.AreEqual(900m, split.Single(l => l.Role == Services.NasbNiro.Finance.AccountRoles.ServiceLabourAbsorbed).Credit);

            // the goodwill share is one person: showing it alone would show his pay, so one debit line of the larger kind carries the month
            var merged = SvLabourMonth.EventLines(new[]
            {
                Pend(1, SvWarrantyDecision.Warranty, null, 600m, big, big),
                Pend(2, SvWarrantyDecision.Goodwill, null, 300m, new[] { 4 }, new[] { 4 }),
            }, 140508);
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(Services.NasbNiro.Finance.AccountRoles.WarrantyProvision, merged[0].Role);
            Assert.AreEqual(900m, merged[0].Debit);
            Assert.AreEqual(900m, merged[1].Credit);
            Assert.AreEqual(merged.Sum(l => l.Debit), merged.Sum(l => l.Credit));
        }

        [TestMethod]
        public void Labour_Cost_Entries_Are_One_Per_Contract_And_A_Small_Contract_Joins_The_Cost_Centre_Entry()
        {
            var three = new[] { 1, 2, 3 };
            var groups = SvLabourMonth.CostGroups(new[]
            {
                Pend(1, SvWarrantyDecision.Warranty, 10, 100m, three, three),
                Pend(2, SvWarrantyDecision.Billable, 10, 50m, three, three),
                Pend(3, SvWarrantyDecision.Warranty, 20, 70m, new[] { 9 }, new[] { 9 }),
                Pend(4, SvWarrantyDecision.Goodwill, null, 30m, new[] { 8 }, new[] { 8 }),
                Pend(5, SvWarrantyDecision.None, 30, 0m, new[] { 7 }, new[] { 7 }),
            });
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual((10, "AFTERSALES", 150m), (groups[0].ContractId.Value, groups[0].CostCenter, groups[0].Amount), "contract 10: three people, its own entry");
            Assert.IsNull(groups[1].ContractId, "contract 20 was one person: joined with the entry without contract");
            Assert.AreEqual(100m, groups[1].Amount);
            Assert.AreEqual(250m, groups.Sum(g => g.Amount), "the month total is kept");
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
