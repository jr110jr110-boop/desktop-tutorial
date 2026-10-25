using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
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
