using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Sales;

namespace MsTests
{
    /// <summary>The pure rules of sales: estimate arithmetic, credit check, approval route, win rate, name matching and document line amounts.</summary>
    [TestClass]
    public class SalesMathTests
    {
        private static SlEstimateInputs Full() => new SlEstimateInputs
        {
            Material = 1000000m, Labour = 500000m, Engineering = 200000m, Subcontract = 0m, PackingTransport = 300000m,
            OverheadPercent = 10m, ContingencyPercent = 5m, TargetMarginPercent = 20m,
            GuaranteeAmount = 10000000m, GuaranteeMonths = 6m, GuaranteeRatePercentPerMonth = 0.5m,
            RiskExpectedCosts = new List<decimal> { 50000m, 25000.50m },
        };

        [TestMethod]
        public void Estimate_Totals_Are_Exact()
        {
            var t = SlEstimateMath.Compute(Full());
            Assert.AreEqual(2000000m, t.DirectCost);
            Assert.AreEqual(300000m, t.GuaranteeCost);
            Assert.AreEqual(0m, t.FinancingCost);
            Assert.AreEqual(75000.50m, t.RiskCost);
            Assert.AreEqual(100000m, t.ContingencyCost);
            Assert.AreEqual(2475000.50m, t.CostBeforeOverhead);
            Assert.AreEqual(247500.05m, t.OverheadCost);
            Assert.AreEqual(2722500.55m, t.TotalCost);
            Assert.AreEqual(3403125.69m, t.Price);
            Assert.AreEqual(0, t.Missing.Count);
        }

        [TestMethod]
        public void Estimate_Without_Inputs_Has_No_Price_And_Names_What_Is_Missing()
        {
            var i = Full();
            i.OverheadPercent = null;
            i.TargetMarginPercent = null;
            i.MissingCostLines = 2;
            var t = SlEstimateMath.Compute(i);
            Assert.IsNull(t.Price);
            Assert.AreEqual(3, t.Missing.Count);
        }

        [TestMethod]
        public void Guarantee_Without_Rate_Is_Reported_Not_Assumed()
        {
            var i = Full();
            i.GuaranteeRatePercentPerMonth = null;
            var t = SlEstimateMath.Compute(i);
            Assert.AreEqual(0m, t.GuaranteeCost);
            Assert.IsNull(t.Price);
            Assert.AreEqual(1, t.Missing.Count);
        }

        [TestMethod]
        public void Credit_Within_Limit_Needs_No_Approval()
        {
            var r = SlCredit.Evaluate(1000m, 600m, 300m);
            Assert.IsFalse(r.NeedsApproval);
        }

        [TestMethod]
        public void Credit_Over_Limit_Reports_The_Exact_Excess()
        {
            var r = SlCredit.Evaluate(1000m, 600m, 500m);
            Assert.IsTrue(r.NeedsApproval);
            Assert.AreEqual(100m, r.Excess);
        }

        [TestMethod]
        public void Credit_Without_A_Limit_Always_Needs_Approval()
        {
            var r = SlCredit.Evaluate(null, 0m, 1m);
            Assert.IsTrue(r.NeedsApproval);
            Assert.IsNull(r.Excess);
        }

        [TestMethod]
        public void Proposal_Goes_To_The_Ceo_When_Below_Cost_Above_Threshold_Or_No_Threshold()
        {
            Assert.IsFalse(SlApproval.NeedsCeo(100m, false, 200m));
            Assert.IsFalse(SlApproval.NeedsCeo(200m, false, 200m));
            Assert.IsTrue(SlApproval.NeedsCeo(300m, false, 200m));
            Assert.IsTrue(SlApproval.NeedsCeo(100m, true, 200m));
            Assert.IsTrue(SlApproval.NeedsCeo(100m, false, null));
        }

        [TestMethod]
        public void Hit_Rate_Is_Won_Over_Decided_With_Two_Decimals()
        {
            Assert.AreEqual(75m, SlProposalService.Percent(3, 1));
            Assert.AreEqual(33.33m, SlProposalService.Percent(1, 2));
            Assert.AreEqual(66.67m, SlProposalService.Percent(2, 1));
            Assert.IsNull(SlProposalService.Percent(0, 0));
        }

        [TestMethod]
        public void Names_Match_Across_Arabic_Persian_Letters_Digits_And_Spaces()
        {
            Assert.AreEqual(SlText.Normalize("شرکت  كيان‌صنعت ۱۲"), SlText.Normalize("شرکت کیانصنعت 12"));
            Assert.AreNotEqual(SlText.Normalize("شرکت الف"), SlText.Normalize("شرکت ب"));
            Assert.IsNull(SlText.Normalize("   "));
            Assert.IsNull(SlText.Normalize(null));
        }

        [TestMethod]
        public void Line_Amounts_Are_Whole_Rials_Like_The_Finance_Invoice()
        {
            var a = SlText.LineAmount(3m, 1250m, 10m);
            Assert.AreEqual(3750m, a.Gross);
            Assert.AreEqual(375m, a.Discount);
            Assert.AreEqual(3375m, a.Amount);
            var b = SlText.LineAmount(2.5m, 333m, 0m);
            Assert.AreEqual(833m, b.Gross);
            Assert.AreEqual(833m, b.Amount);
            var c = SlText.LineAmount(1m, 999m, 15m);
            Assert.AreEqual(150m, c.Discount);
            Assert.AreEqual(849m, c.Amount);
        }
    }
}
