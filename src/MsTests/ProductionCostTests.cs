using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Production;

namespace MsTests
{
    /// <summary>The pure rules of the production cost posting: the payroll hourly rate, the privacy mask of the labour amount, and the payroll/production labour hand-over.</summary>
    [TestClass]
    public class ProductionCostTests
    {
        [TestMethod]
        public void Paid_Hours_Are_Paid_Days_Times_Daily_Hours_Plus_Overtime_And_Holiday_Hours()
        {
            Assert.AreEqual(208m, MfCostEngine.PaidHours(26, 8, 0, 0));
            Assert.AreEqual(224m, MfCostEngine.PaidHours(26, 8, 10, 6));
            Assert.AreEqual(8m, MfCostEngine.PaidHours(0, 8, 8, 0));
        }

        [TestMethod]
        public void Hourly_Rate_Is_The_Employer_Cost_Divided_By_The_Paid_Hours()
        {
            // employer cost of the month (earnings + employer insurance + provisions) 44,800,000 / (26 x 8 + 10 + 6 = 224 h) = 200,000 a hour
            Assert.AreEqual(200000m, MfCostEngine.HourlyRate(44800000m, MfCostEngine.PaidHours(26, 8, 10, 6)));
            // without the overtime and holiday hours the same cost would give a higher (wrong) rate: 44,800,000 / 208
            Assert.AreNotEqual(200000m, MfCostEngine.HourlyRate(44800000m, MfCostEngine.PaidHours(26, 8, 0, 0)));
            Assert.AreEqual(215384.62m, System.Math.Round(MfCostEngine.HourlyRate(44800000m, 208m), 2));
        }

        [TestMethod]
        public void A_Row_With_Fewer_Than_Three_Payroll_Employees_Is_Protected()
        {
            Assert.IsTrue(MfCostMask.IsProtected(1, 1));
            Assert.IsTrue(MfCostMask.IsProtected(2, 2));
            Assert.IsFalse(MfCostMask.IsProtected(3, 3));
            Assert.IsFalse(MfCostMask.IsProtected(7, 3));
            Assert.IsTrue(MfCostMask.IsProtected(6, 1)); // one payroll employee among five on the work-centre rate: the known part can be subtracted
            Assert.IsFalse(MfCostMask.IsProtected(1, 0)); // the work-centre standard rate is not a person's pay
            Assert.IsFalse(MfCostMask.IsProtected(0, 0));
        }

        [TestMethod]
        public void The_Labour_Amount_Of_A_Protected_Row_Is_Shown_Only_To_Payroll_Roles()
        {
            Assert.IsNull(MfCostMask.Labour(2400m, true, false));
            Assert.AreEqual(2400m, MfCostMask.Labour(2400m, true, true));
            Assert.AreEqual(2400m, MfCostMask.Labour(2400m, false, false));
            Assert.AreEqual(2400m, MfCostMask.Labour(2400m, false, true));
        }

        [TestMethod]
        public void Payroll_Skips_The_Labour_Entry_Of_A_Work_Order_That_Production_Already_Absorbed()
        {
            var labour = new Dictionary<int, decimal> { { 1, 1000m }, { 2, 0m }, { 3, 500m }, { 4, 250m } };
            var posted = PayrollRunService.LabourEntriesToPost(labour, new HashSet<int> { 3 });
            CollectionAssert.AreEqual(new[] { 1, 4 }, posted.ConvertAll(x => x.Key));
            CollectionAssert.AreEqual(new[] { 1000m, 250m }, posted.ConvertAll(x => x.Value));
            Assert.AreEqual(3, PayrollRunService.LabourEntriesToPost(labour, new HashSet<int>()).Count); // nothing absorbed: all three non-zero entries, as before
        }
    }
}
