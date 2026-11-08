using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.OpsData;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.OpsDataDtos;

namespace MsTests
{
    /// <summary>PB8: operational data control. The arithmetic of every rule, the baseline-conformance rules, the engineering readiness checklist, the gate and its exceptions, and the daily seat.</summary>
    [TestClass]
    public class OpsDataTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Supervisor = AppHost.Principal(2, "wh.supervisor", "WHSUPERVISOR");
        private static readonly ClaimsPrincipal Seat = AppHost.Principal(3, "seat.ops-data", "WHSUPERVISOR");

        private static OpsRuleSetting S(string code) => OpsRuleCatalog.SettingOf(null, code);
        private static OpsRuleSetting S(decimal warn, decimal block, int min = 1, decimal floor = 0) => new OpsRuleSetting { Enabled = true, Warn = warn, Block = block, MinHistory = min, Floor = floor };

        // ------------------------------------------------------------------------------------------ arithmetic

        [TestMethod]
        public void Deviation_Fold_And_Classify()
        {
            Assert.AreEqual(-30m, OpsRules.Deviation(105m, 150m));
            Assert.IsNull(OpsRules.Deviation(1m, 0m));
            Assert.AreEqual(900m, OpsRules.Fold(1000m, 100m), "ten times");
            Assert.AreEqual(900m, OpsRules.Fold(10m, 100m), "one tenth is as suspect as ten times");
            Assert.IsNull(OpsRules.Fold(0m, 5m));
            var s = S(10, 50);
            Assert.IsNull(OpsRules.Classify(9.99m, s));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.Classify(10m, s));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.Classify(49.99m, s));
            Assert.AreEqual(OpsSeverity.Block, OpsRules.Classify(50m, s));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.Classify(5000m, S(10, 0)), "a block threshold of 0 never blocks");
            Assert.IsNull(OpsRules.Classify(100m, new OpsRuleSetting { Enabled = false, Warn = 1, Block = 2 }), "a disabled rule says nothing");
        }

        [TestMethod]
        public void UomFactor_Detects_Thousand_And_One_Thousandth()
        {
            Assert.AreEqual(1000m, OpsRules.UomFactor(150000m, 150m, 5m));
            Assert.AreEqual(0.001m, OpsRules.UomFactor(0.15m, 150m, 5m));
            Assert.IsNull(OpsRules.UomFactor(160m, 150m, 5m));
            Assert.AreEqual(1000m, OpsRules.UomFactor(1040m, 1m, 5m), "inside the 5 percent tolerance");
            Assert.IsNull(OpsRules.UomFactor(1100m, 1m, 5m), "outside the tolerance");
            var f = OpsRules.UnitMismatch(S(OpsRuleCodes.Uom), 1, 1, 1, "x", "وزن", 150000m, 150m);
            Assert.AreEqual(OpsSeverity.Block, f.Severity);
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.UnitMismatch(new OpsRuleSetting { Enabled = true, Warn = 5, Block = 0 }, 1, 1, 1, "x", "وزن", 150000m, 150m).Severity);
        }

        [TestMethod]
        public void Receipt_Weight_105_Instead_Of_150_Is_A_Warning_With_The_Numbers()
        {
            var f = OpsRules.RcvWeight(S(OpsRuleCodes.RcvWeight), 1, 1, 7, "میلگرد", 105m, null, 150m);
            Assert.AreEqual(OpsSeverity.Warning, f.Severity);
            Assert.AreEqual(105m, f.Entered); Assert.AreEqual(150m, f.Reference); Assert.AreEqual(30m, f.DeviationPercent);
            StringAssert.Contains(f.Message, "105");
            StringAssert.Contains(f.Message, "150");
            Assert.IsNull(OpsRules.RcvWeight(S(OpsRuleCodes.RcvWeight), 1, 1, 7, "x", 145m, null, 150m), "3.3 percent is inside the warning threshold");
            Assert.AreEqual(OpsSeverity.Block, OpsRules.RcvWeight(S(OpsRuleCodes.RcvWeight), 1, 1, 7, "x", 400m, null, 150m).Severity, "more than 100 percent");
            Assert.IsNull(OpsRules.RcvWeight(S(OpsRuleCodes.RcvWeight), 1, 1, 7, "x", null, null, 150m), "nothing to compare with");
            // the certificate is the reference when it exists
            var cert = OpsRules.RcvWeight(S(OpsRuleCodes.RcvWeight), 1, 1, 7, "x", 100m, 120m, 150m);
            Assert.AreEqual(120m, cert.Reference);
        }

        [TestMethod]
        public void Price_Ten_Times_Is_A_Block_And_Money_Is_Masked_For_Readers_Without_Price_Access()
        {
            var f = OpsRules.Price(OpsRuleCodes.RcvPrice, S(OpsRuleCodes.RcvPrice), 1, 1, 7, "x", 1000m, 100m, "میانگین ۳ رسید آخر");
            Assert.AreEqual(OpsSeverity.Block, f.Severity);
            Assert.AreEqual(900m, f.DeviationPercent);
            Assert.IsTrue(f.IsMoney);
            Assert.IsFalse(f.MessageMasked.Contains("1000"));
            Assert.AreEqual(OpsSeverity.Block, OpsRules.Price(OpsRuleCodes.RcvPrice, S(OpsRuleCodes.RcvPrice), 1, 1, 7, "x", 10m, 100m, "t").Severity, "one tenth");
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.Price(OpsRuleCodes.RcvPrice, S(OpsRuleCodes.RcvPrice), 1, 1, 7, "x", 160m, 100m, "t").Severity);
            Assert.IsNull(OpsRules.Price(OpsRuleCodes.RcvPrice, S(OpsRuleCodes.RcvPrice), 1, 1, 7, "x", 120m, 100m, "t"));
        }

        [TestMethod]
        public void Production_Time_Times_Five_Blocks_And_The_Floor_Protects_Tiny_Standards()
        {
            var s = S(OpsRuleCodes.PrdTime);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 10m, 2m).Severity, "5x = 400 percent");
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 4m, 2m).Severity, "100 percent");
            Assert.IsNull(OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 3m, 2m));
            Assert.IsNull(OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 0.5m, 0m), "a zero standard uses the floor of 30 minutes (0.5 hour): 0.5 is not beyond it");
            Assert.IsNull(OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 1.1m, 0m), "H4: no standard (planned <= 0) means no finding at all");
            Assert.IsNull(OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 5m, -1m));
            Assert.IsNotNull(OpsRules.ProductionTime(s, 1, "تراش", "اجرا", 1.1m, 0.05m), "a tiny standard is protected by the floor, not ignored");
        }

        [TestMethod]
        public void Finding_Keys_Do_Not_Depend_On_The_Line_Id_And_Separate_The_Fields()
        {
            Assert.AreEqual(OpsKey.Of(2, null, 5, "R-X", "price"), OpsKey.Of(2, null, 5, "R-X", "price"));
            Assert.AreNotEqual(OpsKey.Of(2, null, 5, "R-X", "price"), OpsKey.Of(2, null, 5, "R-X", "qty"));
            Assert.AreNotEqual(OpsKey.Of(2, null, 5, "R-X", "price"), OpsKey.Of(3, null, 5, "R-X", "price"));
            CollectionAssert.Contains(OpsSettingKeys.SecurityRelevant.ToList(), OpsRuleCatalog.ExceptionMargin);
        }

        [TestMethod]
        public void Scrap_And_Reject_Need_Their_Minimum_History()
        {
            var s = S(200, 900, 3);
            Assert.IsNull(OpsRules.ProductionScrap(s, 1, "op", 20, 100, 0.01m, 2), "two earlier operations are not enough");
            var f = OpsRules.ProductionScrap(s, 1, "op", 20, 100, 0.01m, 5);
            Assert.AreEqual(OpsSeverity.Block, f.Severity, "20 scrapped, 1 expected: 1900 percent");
            Assert.IsNull(OpsRules.ProductionScrap(s, 1, "op", 1, 100, 0.001m, 5), "one piece is always expected");
            Assert.IsNull(OpsRules.QcRejected(S(OpsRuleCodes.QcReject), 1, 1, "x", 50, 100, 0.05m, 2));
            var q = OpsRules.QcRejected(S(OpsRuleCodes.QcReject), 1, 1, "x", 50, 100, 0.05m, 5);
            Assert.AreEqual(OpsSeverity.Warning, q.Severity, "the QC reject rule never blocks by default");
        }

        [TestMethod]
        public void Count_Variance_Floor_And_Date_Rules()
        {
            var s = S(OpsRuleCodes.CntVar);
            Assert.IsNull(OpsRules.CountVariance(s, 1, 1, 1, "x", "عدد", 100, 99, 1000m), "a tiny value is ignored under the floor");
            Assert.AreEqual(OpsSeverity.Block, OpsRules.CountVariance(s, 1, 1, 1, "x", "عدد", 100, 40, 1_000_000m).Severity, "60 percent and 60 million");
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.CountVariance(s, 1, 1, 1, "x", "عدد", 100, 85, 1_000_000m).Severity);
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.CountVariance(s, 1, 1, 1, "x", "عدد", 100, 40, null).Severity, "uncosted stock is never a block");
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.CountVariance(s, 1, 1, 1, "x", "عدد", 0, 40, 1_000_000m).Severity, "a zero book is never a block");
            DateTime today = new DateTime(2026, 11, 8);
            Assert.IsNull(OpsRules.DateBack(S(OpsRuleCodes.Date), today.AddDays(-29), today));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.DateBack(S(OpsRuleCodes.Date), today.AddDays(-30), today).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.DateBack(S(OpsRuleCodes.Date), today.AddDays(-1095), today).Severity);
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.DateForward(S(OpsRuleCodes.DateFwd), today.AddDays(7), today).Severity);
            Assert.IsNull(OpsRules.DateForward(S(OpsRuleCodes.DateFwd), today.AddDays(6), today));
        }

        [TestMethod]
        public void Issue_Rules_Compare_The_Cumulative_Quantity()
        {
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.IssueAgainstKit(S(OpsRuleCodes.IssQty), 1, 1, 1, "x", "kg", 112m, 100m).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.IssueAgainstKit(S(OpsRuleCodes.IssQty), 1, 1, 1, "x", "kg", 150m, 100m).Severity);
            Assert.IsNull(OpsRules.IssueAgainstKit(S(OpsRuleCodes.IssQty), 1, 1, 1, "x", "kg", 100m, 100m));
            Assert.AreEqual(OpsSeverity.Block, OpsRules.IssueAgainstCeiling(S(OpsRuleCodes.IssCeil), 1, 1, 1, "x", "kg", 130m, 100m).Severity);
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.IssueAgainstCeiling(S(OpsRuleCodes.IssCeil), 1, 1, 1, "x", "kg", 110m, 100m).Severity);
        }

        [TestMethod]
        public void Receipt_Quantity_Over_The_Open_Order_Quantity_Warns()
        {
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.RcvQty(S(OpsRuleCodes.RcvQty), 1, 1, 1, "x", "kg", 108m, 100m).Severity);
            Assert.IsNull(OpsRules.RcvQty(S(OpsRuleCodes.RcvQty), 1, 1, 1, "x", "kg", 104m, 100m));
            Assert.IsNull(OpsRules.RcvQty(S(OpsRuleCodes.RcvQty), 1, 1, 1, "x", "kg", 50m, 100m));
        }

        // ------------------------------------------------------------------------------------------ baseline conformance

        [TestMethod]
        public void Baseline_Rules_Pass_Warn_Or_Block()
        {
            // item not in the BOM
            Assert.IsNull(OpsRules.BomItem(S(OpsRuleCodes.BlBomItem), 1, 1, 1, "x", "WO-1", true));
            Assert.AreEqual(OpsSeverity.Block, OpsRules.BomItem(S(OpsRuleCodes.BlBomItem), 1, 1, 1, "x", "WO-1", false).Severity);
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.BomItem(new OpsRuleSetting { Enabled = true, Block = 0 }, 1, 1, 1, "x", "WO-1", false).Severity, "the setting turns the block into a warning");
            // quantity: inside the tolerance a warning, beyond it a block
            var q = new OpsRuleSetting { Enabled = true, Warn = 0, Block = 5 };
            Assert.IsNull(OpsRules.BomQuantity(q, 1, 1, 1, "x", "kg", 100m, 100m));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.BomQuantity(q, 1, 1, 1, "x", "kg", 104m, 100m).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.BomQuantity(q, 1, 1, 1, "x", "kg", 106m, 100m).Severity);
            // stage
            Assert.IsNull(OpsRules.OpcStage(S(OpsRuleCodes.BlOpcStage), 1, 1, 1, "x", "16.1", "CT", false, false, null, false), "no routing: no evidence");
            Assert.AreEqual(OpsSeverity.Block, OpsRules.OpcStage(S(OpsRuleCodes.BlOpcStage), 1, 1, 1, "x", "16.1", "ZZ", true, false, null, false).Severity, "suffix not in the routing");
            Assert.AreEqual(OpsSeverity.Block, OpsRules.OpcStage(S(OpsRuleCodes.BlOpcStage), 1, 1, 1, "x", "16.1", "CT", true, true, "RL", false).Severity, "the previous stage is not done");
            Assert.IsNull(OpsRules.OpcStage(S(OpsRuleCodes.BlOpcStage), 1, 1, 1, "x", "16.1", "CT", true, true, "RL", true));
            // operation out of sequence / wrong work centre
            Assert.AreEqual(OpsSeverity.Block, OpsRules.OpcOperation(S(OpsRuleCodes.BlOpcOp), 1, "تراش", "PO-1", new[] { "برش" }, false, null, null).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.OpcOperation(S(OpsRuleCodes.BlOpcOp), 1, "تراش", "PO-1", new string[0], true, "A", "B").Severity);
            Assert.IsNull(OpsRules.OpcOperation(S(OpsRuleCodes.BlOpcOp), 1, "تراش", "PO-1", new string[0], false, null, null));
            // ITP
            Assert.AreEqual(OpsSeverity.Block, OpsRules.ItpCoverage(S(OpsRuleCodes.BlItp), 1, "UT", new[] { "ضخامت" }, new string[0]).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.ItpCoverage(S(OpsRuleCodes.BlItp), 1, "UT", new string[0], new[] { "ضخامت" }).Severity);
            Assert.IsNull(OpsRules.ItpCoverage(S(OpsRuleCodes.BlItp), 1, "UT", new string[0], new string[0]));
            // spec and drawing revision
            Assert.AreEqual(OpsSeverity.Block, OpsRules.Spec(S(OpsRuleCodes.BlSpec), 1, 1, 1, "x", "WCB", "A105", false).Severity);
            Assert.IsNull(OpsRules.Spec(S(OpsRuleCodes.BlSpec), 1, 1, 1, "x", "WC-B", "wcb", false), "punctuation and case do not matter");
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.DocumentRevision(S(OpsRuleCodes.BlSpec), 1, "DWG-1", false, true).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.DocumentRevision(S(OpsRuleCodes.BlSpec), 1, "DWG-1", true, false).Severity);
            // WBS
            Assert.AreEqual(OpsSeverity.Block, OpsRules.Wbs(S(OpsRuleCodes.BlWbs), null, "x", false, false).Severity);
            Assert.AreEqual(OpsSeverity.Block, OpsRules.Wbs(S(OpsRuleCodes.BlWbs), null, "x", true, false).Severity);
            Assert.IsNull(OpsRules.Wbs(S(OpsRuleCodes.BlWbs), null, "x", true, true));
        }

        [TestMethod]
        public void Settings_Have_Defaults_Bounds_And_Relations()
        {
            var keys = OpsRuleCatalog.Defaults().Select(d => d.Key).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "no key twice");
            foreach (string k in keys) Assert.IsTrue(OpsRuleCatalog.TryGetBounds(k, out _, out _), k);
            Assert.IsTrue(keys.Contains("OpsData.R-RCV-PRICE.Block"));
            Assert.IsTrue(keys.Contains("OpsReady.RD-OPC.Hard"));
            var bad = new Dictionary<string, decimal> { { "OpsData.R-RCV-WEIGHT.Warn", 50 }, { "OpsData.R-RCV-WEIGHT.Block", 20 } };
            Assert.IsNotNull(OpsRuleCatalog.CheckRelations(bad));
            Assert.AreEqual(OpsSeverity.Warning, OpsRules.Classify(60, OpsRuleCatalog.SettingOf(new Dictionary<string, decimal> { { "OpsData.R-RCV-WEIGHT.Block", 0 } }, OpsRuleCodes.RcvWeight)), "a settings value of 0 turns the block off");
        }

        // ------------------------------------------------------------------------------------------ the engineering readiness checklist

        private static ReadinessPart Make(string pn, PbMakeOrBuy mob = PbMakeOrBuy.Make) => new ReadinessPart
        {
            PartNumber = pn, Name = "n" + pn, MakeOrBuy = mob, HasItem = true, HasUnit = true, HasSpec = true, HasReleasedDrawing = true, IsMetal = false,
        };

        private static ReadinessRoutingFact Routing(string pn) => new ReadinessRoutingFact { PartNumber = pn, Approved = true, OperationSteps = 2, QcPoints = 0 };

        private static ReadinessFacts Complete()
        {
            var f = new ReadinessFacts { ProductId = 1, BomReleased = true, WbsApproved = true };
            f.Parts.Add(Make("A.1")); f.Parts.Add(Make("B.1", PbMakeOrBuy.Purchase));
            f.Routings["A.1"] = Routing("A.1");
            return f;
        }

        private static ReadinessItemDto Item(ReadinessResult r, string code) => r.Items.Single(i => i.Code == code);

        [TestMethod]
        public void Readiness_A_Complete_Product_Has_No_Hard_Gap()
        {
            ReadinessResult r = OpsReadiness.Evaluate(Complete(), null);
            Assert.AreEqual(0, r.HardMissing);
            Assert.IsTrue(r.Items.Where(i => i.Code != ReadinessCodes.Design).All(i => i.Status == OpsReadiness.Ok)); // Package A1: the design item is not applicable while the facts do not ask for it
            Assert.AreEqual(100, r.Percent);
            Assert.IsFalse(r.Items.Any(i => i.Code == ReadinessCodes.Itp), "project-level items need a work order");
        }

        [TestMethod]
        public void Readiness_Each_Hard_Item_Is_Missing_When_Its_Data_Is_Missing()
        {
            var f = Complete(); f.BomReleased = false;
            Assert.AreEqual(OpsReadiness.MissingStatus, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Bom).Status);
            f = Complete(); f.WbsApproved = false;
            Assert.AreEqual(OpsReadiness.MissingStatus, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Wbs).Status);
            // OPC: no routing at all for the only make part = Missing; routing without work centre = Partial/Missing with the step named
            f = Complete(); f.Routings.Clear();
            var opc = Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Opc);
            Assert.AreEqual(OpsReadiness.MissingStatus, opc.Status); Assert.AreEqual(1, opc.MissingCount);
            Assert.AreEqual("ENGINEERING", opc.Owner); Assert.IsTrue(opc.Hard);
            f = Complete(); f.Parts.Add(Make("C.1")); f.Routings["C.1"] = Routing("C.1"); f.Routings["A.1"].StepsWithoutWorkCentre.Add("تراش");
            opc = Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Opc);
            Assert.AreEqual(OpsReadiness.Partial, opc.Status, "A.1 lacks a work centre, C.1 is complete");
            f = Complete(); f.Routings["A.1"].StepsWithoutHours.Add("جوش");
            StringAssert.Contains(Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Opc).Offenders[0].Text, "زمان استاندارد");
            // item coding / unit / conversion
            f = Complete(); f.Parts[1].HasItem = false;
            Assert.AreEqual(1, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Item).MissingCount);
            f = Complete(); f.Parts[1].ConversionMissing = true;
            StringAssert.Contains(Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Item).Offenders[0].Text, "ضریب تبدیل");
            // drawing
            f = Complete(); f.Parts[0].HasReleasedDrawing = false;
            Assert.AreEqual(OpsReadiness.MissingStatus, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Drawing).Status);
            // spec of a purchase part
            f = Complete(); f.Parts[1].HasSpec = false;
            Assert.AreEqual(1, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Spec).MissingCount);
        }

        [TestMethod]
        public void Readiness_Itp_And_Project_Level_Soft_Items_Need_A_Work_Order()
        {
            var f = Complete(); f.WorkOrderId = 5; f.Routings["A.1"].QcPoints = 3;
            var r = OpsReadiness.Evaluate(f, null);
            Assert.AreEqual(OpsReadiness.MissingStatus, Item(r, ReadinessCodes.Itp).Status, "QC points but no ITP");
            f.ItpExists = true; f.ItpActivitiesWithCharacteristics = 2;
            Assert.AreEqual(1, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Itp).MissingCount);
            f.ItpActivitiesWithCharacteristics = 3;
            Assert.AreEqual(OpsReadiness.Ok, Item(OpsReadiness.Evaluate(f, null), ReadinessCodes.Itp).Status);
            r = OpsReadiness.Evaluate(f, null);
            foreach (string soft in new[] { ReadinessCodes.Mdr, ReadinessCodes.ItpClient, ReadinessCodes.Schedule })
            {
                Assert.IsFalse(Item(r, soft).Hard, soft);
                Assert.AreEqual(OpsReadiness.MissingStatus, Item(r, soft).Status, soft);
            }
            Assert.AreEqual(0, r.HardMissing, "soft items never block");
            Assert.IsTrue(r.SoftMissing >= 3);
        }

        [TestMethod]
        public void Readiness_Soft_Items_Weight_LongLead_And_Stage_Codes()
        {
            var f = Complete();
            f.Parts[1].IsMetal = true; f.Parts[1].MetalWithoutWeight = true; f.Parts[1].LeadTimeMissing = true; f.Routings["A.1"].StepsWithoutStage.Add("تراش");
            var r = OpsReadiness.Evaluate(f, null);
            Assert.AreEqual(1, Item(r, ReadinessCodes.Weight).MissingCount);
            Assert.AreEqual(1, Item(r, ReadinessCodes.LongLead).MissingCount);
            Assert.AreEqual(1, Item(r, ReadinessCodes.Stage).MissingCount);
            Assert.AreEqual(0, r.HardMissing);
            Assert.AreEqual(3, r.SoftMissing);
            Assert.IsTrue(r.Percent < 100 && r.Percent >= 60, r.Percent.ToString());
        }

        [TestMethod]
        public void Readiness_Settings_Switch_An_Item_Off_Or_Change_Hard_To_Soft()
        {
            var f = Complete(); f.Routings.Clear();
            var settings = new Dictionary<string, decimal> { { "OpsReady.RD-OPC.Hard", 0 } };
            var r = OpsReadiness.Evaluate(f, settings);
            Assert.IsFalse(Item(r, ReadinessCodes.Opc).Hard);
            Assert.AreEqual(0, r.HardMissing);
            settings = new Dictionary<string, decimal> { { "OpsReady.RD-OPC.Enabled", 0 } };
            Assert.IsFalse(OpsReadiness.Evaluate(f, settings).Items.Any(i => i.Code == ReadinessCodes.Opc));
            settings = new Dictionary<string, decimal> { { "OpsReady.Enabled", 0 } };
            Assert.AreEqual(0, OpsReadiness.Evaluate(f, settings).Items.Count, "the master switch");
        }

        [TestMethod]
        public void Readiness_Message_Lists_The_Items_And_Who_Must_Provide_Them()
        {
            var f = Complete(); f.Routings.Clear(); f.BomReleased = false;
            var r = OpsReadiness.Evaluate(f, null);
            string m = OpsReadiness.Message(r.HardGaps, "x:");
            StringAssert.Contains(m, "RD-OPC"); StringAssert.Contains(m, "RD-BOM"); StringAssert.Contains(m, "مهندسی");
            StringAssert.Contains(m, "A.1");
        }

        // ------------------------------------------------------------------------------------------ the gate (real services, in-memory database)

        private static AppHost NewHost(string name) => new AppHost("Ops_" + name + "_" + Guid.NewGuid().ToString("N"));

        private sealed class Wh { public int Raw, Quarantine, Item, Provider; }

        private static Wh Seed(AppHost host)
        {
            var w = new Wh();
            w.Raw = TraceGenealogyPb4Tests.Seed(host, new Warehouse { Code = "W-RAW", Title = "انبار مواد", Kind = WarehouseKind.RawMaterial }).Id;
            w.Provider = TraceGenealogyPb4Tests.Seed(host, new Entities.Model.NasbNiro.Providers.Provider { Name = "تامین‌کننده نمونه" }).Id;
            w.Item = TraceGenealogyPb4Tests.Seed(host, new InventoryItem { Code = "RM-1", Title = "میلگرد", Unit = "kg", TrackingMode = ItemTrackingMode.None, RequiresIncomingInspection = false }).Id;
            TraceGenealogyPb4Tests.Seed(host, new ItemCost { ItemId = w.Item, AverageCost = 100m });
            return w;
        }

        private static Task<ResponseDto<DocumentDto>> DraftReceipt(AppHost host, Wh w, decimal qty, decimal price, decimal? weighed)
        {
            return host.As(Maker, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                return await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.PurchaseReceipt, DocumentDate = DateTime.Today, WarehouseId = w.Raw, ProviderId = w.Provider, Description = "خرید خرد",
                    Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = w.Item, Quantity = qty, UnitPrice = price, WeighedWeightKg = weighed } },
                }, Ct);
            });
        }

        /// <summary>H1: the price rule needs real history (two confirmed non-PO receipts); there is no average-cost fallback.</summary>
        private static async Task Prime(AppHost host, Wh w)
        {
            for (int i = 0; i < 2; i++)
            {
                var d = await DraftReceipt(host, w, 10m, 100m, null);
                Assert.IsTrue(d.Success, d.Message);
                var c = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(d.Data.Id, Ct));
                Assert.IsTrue(c.Success, c.Message);
            }
        }

        [TestMethod]
        public async Task Gate_Price_Without_History_Is_Not_Judged_And_There_Is_No_Average_Cost_Fallback()
        {
            AppHost host = NewHost("nohist");
            Wh w = Seed(host);
            var saved = await DraftReceipt(host, w, 10m, 1000m, null);
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(0, saved.Data.OpsFindings.Count(f => f.RuleCode == OpsRuleCodes.RcvPrice), "the item has an average cost of 100 but no receipt history");
            Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(saved.Data.Id, Ct))).Success);
        }

        [TestMethod]
        public async Task Gate_Warning_Is_Returned_In_The_Dto_And_Stored_With_The_Document_And_The_Confirmation_Passes()
        {
            AppHost host = NewHost("warn");
            Wh w = Seed(host);
            var saved = await DraftReceipt(host, w, 150m, 100m, 105m);
            Assert.IsTrue(saved.Success, saved.Message);
            var got = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().GetDocumentAsync(saved.Data.Id, Ct));
            var weight = got.Data.OpsFindings.Single(f => f.RuleCode == OpsRuleCodes.RcvWeight);
            Assert.AreEqual(OpsSeverity.Warning, weight.Severity);
            Assert.AreEqual(105m, weight.EnteredValue); Assert.AreEqual(150m, weight.ReferenceValue); Assert.AreEqual(30m, weight.DeviationPercent);
            var confirm = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(saved.Data.Id, Ct));
            Assert.IsTrue(confirm.Success, confirm.Message);
            OpsFinding stored = host.Read(u => u.Set<OpsFinding>().Single(f => f.DocumentId == saved.Data.Id && f.RuleCode == OpsRuleCodes.RcvWeight));
            Assert.AreEqual(OpsSeverity.Warning, stored.Severity);
            Assert.AreEqual(OpsFindingStatus.Open, stored.Status);
            var after = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().GetDocumentAsync(saved.Data.Id, Ct));
            Assert.AreEqual(1, after.Data.OpsFindings.Count(f => f.RuleCode == OpsRuleCodes.RcvWeight), "a confirmed document shows its stored warning");
        }

        [TestMethod]
        public async Task Gate_Price_Ten_Times_Is_Refused_Until_Another_Supervisor_Approves_An_Exception()
        {
            AppHost host = NewHost("block");
            Wh w = Seed(host);
            await Prime(host, w);
            var saved = await DraftReceipt(host, w, 10m, 1000m, null);
            Assert.IsTrue(saved.Success, saved.Message);
            int id = saved.Data.Id;
            var refused = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, OpsRuleCodes.RcvPrice);
            OpsFinding f = host.Read(u => u.Set<OpsFinding>().Single(x => x.DocumentId == id && x.RuleCode == OpsRuleCodes.RcvPrice));
            Assert.AreEqual(OpsSeverity.Block, f.Severity);

            // the maker asks; a justification is required
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = f.Id, Justification = "کوتاه" }, Ct))).Success);
            var req = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = f.Id, Justification = "قیمت جدید طبق پیش‌فاکتور تامین‌کننده است" }, Ct));
            Assert.IsTrue(req.Success, req.Message);
            // still refused while the exception waits
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct))).Success);
            // the creator cannot approve his own exception
            var own = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct));
            Assert.IsFalse(own.Success);
            // an AI seat cannot, even with the supervisor role
            var ai = await host.As(Seat, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct));
            Assert.IsFalse(ai.Success);
            // a person without the supervisor role cannot
            var plain = AppHost.Principal(9, "plain.user", "WAREHOUSE");
            Assert.IsFalse((await host.As(plain, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            // the supervisor approves
            var ok = await host.As(Supervisor, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true, Reason = "پیش‌فاکتور دیده شد" }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(OpsExceptionStatus.Approved, host.Read(u => u.Set<OpsException>().Single(e => e.Id == req.Data.Id).Status));
            var pass = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            Assert.IsTrue(pass.Success, pass.Message);
            Assert.IsNotNull(host.Read(u => u.Set<OpsException>().Single(e => e.Id == req.Data.Id).UsedAt));
        }

        [TestMethod]
        public async Task Gate_An_Approved_Exception_Does_Not_Cover_A_Changed_Value()
        {
            AppHost host = NewHost("changed");
            Wh w = Seed(host);
            await Prime(host, w);
            var saved = await DraftReceipt(host, w, 10m, 1000m, null);
            int id = saved.Data.Id;
            await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            OpsFinding f = host.Read(u => u.Set<OpsFinding>().Single(x => x.DocumentId == id && x.RuleCode == OpsRuleCodes.RcvPrice));
            var req = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = f.Id, Justification = "قیمت جدید طبق پیش‌فاکتور تامین‌کننده است" }, Ct));
            Assert.IsTrue((await host.As(Supervisor, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            // the price is edited after the approval
            var edit = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().SaveDraftAsync(new SaveDocumentDto
            {
                Id = id, Type = InventoryDocumentType.PurchaseReceipt, DocumentDate = DateTime.Today, WarehouseId = w.Raw, ProviderId = w.Provider, Description = "خرید خرد",
                Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = w.Item, Quantity = 10m, UnitPrice = 2000m } },
            }, Ct));
            Assert.IsTrue(edit.Success, edit.Message);
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct))).Success, "the exception was for the earlier price");
        }

        [TestMethod]
        public async Task Gate_Price_Reference_Is_The_Median_So_One_Confirmed_Outlier_Does_Not_Block_Normal_Prices()
        {
            AppHost host = NewHost("median");
            Wh w = Seed(host);
            await Prime(host, w);                                   // 100, 100
            var odd = await DraftReceipt(host, w, 10m, 9000m, null);   // a capital purchase: blocked, then excepted by another supervisor
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(odd.Data.Id, Ct))).Success);
            OpsFinding f = host.Read(u => u.Set<OpsFinding>().Single(x => x.DocumentId == odd.Data.Id && x.RuleCode == OpsRuleCodes.RcvPrice));
            var req = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = f.Id, Justification = "خرید سرمایه‌ای با فاکتور جداگانه" }, Ct));
            Assert.IsTrue((await host.As(Supervisor, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(odd.Data.Id, Ct))).Success);
            // history is now 9000, 100, 100: the average (3066) would block a normal price of 100; the median (100) does not
            var normal = await DraftReceipt(host, w, 10m, 100m, null);
            Assert.AreEqual(0, normal.Data.OpsFindings.Count(x => x.RuleCode == OpsRuleCodes.RcvPrice));
            Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(normal.Data.Id, Ct))).Success);
        }

        [TestMethod]
        public async Task Gate_Real_Ui_Flow_Save_Confirm_Refused_Request_Approve_Save_Again_Confirm_Passes()
        {
            AppHost host = NewHost("uiflow");
            Wh w = Seed(host);
            await Prime(host, w);
            var saved = await DraftReceipt(host, w, 10m, 1000m, null);
            Assert.IsTrue(saved.Success, saved.Message);
            int id = saved.Data.Id;
            Assert.AreEqual(1, saved.Data.OpsFindings.Count(f => f.RuleCode == OpsRuleCodes.RcvPrice), "the draft view already shows the block");
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct))).Success);
            var reloaded = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().GetDocumentAsync(id, Ct));
            var draftFinding = reloaded.Data.OpsFindings.Single(f => f.RuleCode == OpsRuleCodes.RcvPrice);
            Assert.IsTrue(draftFinding.Id.HasValue, "after the refusal the reloaded draft carries the stored finding id (H2)");
            var req = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = draftFinding.Id.Value, Justification = "قیمت جدید طبق پیش‌فاکتور تامین‌کننده است" }, Ct));
            Assert.IsTrue(req.Success, req.Message);
            var dup = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = draftFinding.Id.Value, Justification = "درخواست تکراری برای همان یافته" }, Ct));
            Assert.IsFalse(dup.Success, "a second pending request is refused with a message, not an error");
            Assert.IsFalse(string.IsNullOrWhiteSpace(dup.Message));
            Assert.IsTrue((await host.As(Supervisor, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            // the form saves the same content again: the lines are recreated with new ids
            int oldLine = host.Read(u => u.Set<InventoryDocumentLine>().First(l => l.DocumentId == id).Id);
            var again = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().SaveDraftAsync(new SaveDocumentDto
            {
                Id = id, Type = InventoryDocumentType.PurchaseReceipt, DocumentDate = DateTime.Today, WarehouseId = w.Raw, ProviderId = w.Provider, Description = "خرید خرد",
                Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = w.Item, Quantity = 10m, UnitPrice = 1000m } },
            }, Ct));
            Assert.IsTrue(again.Success, again.Message);
            int newLine = host.Read(u => u.Set<InventoryDocumentLine>().Where(l => l.DocumentId == id).Max(l => l.Id));
            Assert.AreNotEqual(oldLine, newLine, "the save recreated the line");
            var pass = await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            Assert.IsTrue(pass.Success, pass.Message);
            Assert.AreEqual(1, host.Read(u => u.Set<OpsException>().Count(e => e.UsedAt != null)));
        }

        [TestMethod]
        public async Task Gate_The_Approver_Of_An_Exception_Cannot_Also_Confirm_The_Document()
        {
            AppHost host = NewHost("m1");
            Wh w = Seed(host);
            await Prime(host, w);
            var saved = await DraftReceipt(host, w, 10m, 1000m, null);
            int id = saved.Data.Id;
            await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            OpsFinding f = host.Read(u => u.Set<OpsFinding>().Single(x => x.DocumentId == id && x.RuleCode == OpsRuleCodes.RcvPrice));
            var req = await host.As(Maker, p => p.GetRequiredService<IOpsExceptionService>().RequestAsync(new RequestOpsExceptionDto { FindingId = f.Id, Justification = "قیمت جدید طبق پیش‌فاکتور تامین‌کننده است" }, Ct));
            Assert.IsTrue((await host.As(Supervisor, p => p.GetRequiredService<IOpsExceptionService>().DecideAsync(new DecideOpsExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            var byApprover = await host.As(Supervisor, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct));
            Assert.IsFalse(byApprover.Success, "M1: the exception does not cover a confirmation made by its own approver");
            Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(id, Ct))).Success);
        }

        [TestMethod]
        public async Task Data_Quality_Counts_Resolution_And_Verification_Follow_The_Maker_Checker_Rule()
        {
            AppHost host = NewHost("quality");
            Wh w = Seed(host);
            var a = await DraftReceipt(host, w, 150m, 100m, 105m);
            var b = await DraftReceipt(host, w, 150m, 100m, 60m);
            foreach (var d in new[] { a, b }) Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(d.Data.Id, Ct))).Success);
            var list = await host.As(Supervisor, p => p.GetRequiredService<IOpsDataQualityService>().GetFindingsAsync(new OpsFindingQuery { Rule = OpsRuleCodes.RcvWeight, State = "open", PageSize = 0 }, Ct));
            Assert.IsTrue(list.Success, list.Message);
            Assert.AreEqual(2, list.Data.Count);
            var sum = await host.As(Supervisor, p => p.GetRequiredService<IOpsDataQualityService>().SummaryAsync(null, Ct));
            Assert.AreEqual(2, sum.Data.OpenFindings); Assert.AreEqual(2, sum.Data.OpenWarnings); Assert.AreEqual(0, sum.Data.OpenBlocks);
            Assert.AreEqual(2, sum.Data.ByRule.Single(r => r.Key == OpsRuleCodes.RcvWeight).Count);
            int first = list.Data.First().Id.Value;
            // the maker may not verify his own data
            Assert.IsFalse((await host.As(Maker, p => p.GetRequiredService<IOpsDataQualityService>().ResolveAsync(new ResolveOpsFindingDto { Id = first, Resolution = "verified", Reason = "وزن درست است" }, Ct))).Success);
            // the supervisor verifies with a reason (a reason is required)
            Assert.IsFalse((await host.As(Supervisor, p => p.GetRequiredService<IOpsDataQualityService>().ResolveAsync(new ResolveOpsFindingDto { Id = first, Resolution = "verified" }, Ct))).Success);
            var v = await host.As(Supervisor, p => p.GetRequiredService<IOpsDataQualityService>().ResolveAsync(new ResolveOpsFindingDto { Id = first, Resolution = "verified", Reason = "با ترازوی دوم وزن شد" }, Ct));
            Assert.IsTrue(v.Success, v.Message);
            sum = await host.As(Supervisor, p => p.GetRequiredService<IOpsDataQualityService>().SummaryAsync(null, Ct));
            Assert.AreEqual(1, sum.Data.OpenFindings);
        }

        // ------------------------------------------------------------------------------------------ the seat

        [TestMethod]
        public void Seat_Is_Rule_Based_Scheduled_At_0615_And_Has_Read_Only_Warehouse_Roles()
        {
            AiSeatDefinition seat = AiSeatCatalog.Find("OPS-DATA");
            Assert.IsNotNull(seat);
            Assert.IsFalse(seat.UsesModel);
            Assert.AreEqual("daily 06:15", seat.Schedule);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, seat.Roles);
        }

        [TestMethod]
        public async Task Seat_Finds_The_Repeated_Quantity_Once_And_A_Second_Run_Finds_Nothing_New()
        {
            AppHost host = NewHost("seat");
            Wh w = Seed(host);
            for (int i = 0; i < 4; i++)
            {
                var d = await DraftReceipt(host, w, 77m, 100m, null);
                Assert.IsTrue((await host.As(Maker, p => p.GetRequiredService<IInventoryDocumentService>().ConfirmAsync(d.Data.Id, Ct))).Success);
            }
            Func<Task<AiSeatJobResult>> run = () => host.As<AiSeatJobResult>(Maker, p => p.GetServices<IAiSeatJob>().Single(j => j.Code == "OPS-DATA").RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));
            var first = await run();
            Assert.AreEqual(AiSeatRunStatus.Succeeded, first.Status, first.Error);
            Assert.AreEqual(1, host.Read(u => u.Set<OpsFinding>().Count(f => f.RuleCode == OpsRuleCodes.SeatRepeat && f.Source == OpsSource.Seat)));
            Assert.AreEqual(1, host.Read(u => u.Set<AiNote>().Count(n => n.SeatCode == "OPS-DATA" && n.RuleCode == OpsRuleCodes.SeatRepeat)));
            var second = await run();
            StringAssert.Contains(second.Summary, "findings=0");
            Assert.AreEqual(1, host.Read(u => u.Set<AiNote>().Count(n => n.SeatCode == "OPS-DATA" && n.RuleCode == OpsRuleCodes.SeatRepeat)), "one note per document and rule");
        }

        // ------------------------------------------------------------------------------------------ readiness exceptions: two persons, neither the requester

        [TestMethod]
        public async Task Readiness_Exception_Needs_The_Pmo_And_The_Ceo_Both_Different_From_The_Requester_And_Never_An_Ai_Seat()
        {
            AppHost host = NewHost("rdy");
            int product = TraceGenealogyPb4Tests.Seed(host, new PbProduct { Code = "P-1", Title = "محصول", IsActive = true }).Id;
            int contract = TraceGenealogyPb4Tests.Seed(host, new Entities.Model.NasbNiro.Contracts.ContractEntity { Code = "C-1", Title = "قرارداد", Employer = "کارفرما" }).Id;
            int wo = TraceGenealogyPb4Tests.Seed(host, new Entities.Model.NasbNiro.Contracts.WorkOrder { Code = "WO-1", Title = "دستور", ContractId = contract, Count = 1 }).Id;
            var requester = AppHost.Principal(21, "planner.one", "PLANNING");
            var pmo = AppHost.Principal(22, "pmo.main", "PMO");
            var ceo = AppHost.Principal(23, "ceo.main", "CEO");
            var seat = AppHost.Principal(24, "seat.ops-data", "PMO");
            // the gate refuses: no released BOM and no WBS template
            var gate = await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().GateForBaselineAsync(wo, product, Ct));
            Assert.IsNotNull(gate.Refusal);
            StringAssert.Contains(gate.Refusal, "RD-BOM");
            StringAssert.Contains(gate.Refusal, "RD-WBS");
            Assert.IsFalse((await host.As(seat, p => p.GetRequiredService<IOpsReadinessService>().RequestExceptionAsync(new RequestReadinessExceptionDto { WorkOrderId = wo, ProductId = product, Justification = "شروع فوری به دستور مدیرعامل" }, Ct))).Success, "an AI seat cannot ask");
            var req = await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().RequestExceptionAsync(new RequestReadinessExceptionDto { WorkOrderId = wo, ProductId = product, Justification = "شروع فوری به دستور مدیرعامل" }, Ct));
            Assert.IsTrue(req.Success, req.Message);
            Assert.IsFalse((await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().DecideExceptionAsync(new DecideReadinessExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success, "not the requester");
            Assert.IsFalse((await host.As(seat, p => p.GetRequiredService<IOpsReadinessService>().DecideExceptionAsync(new DecideReadinessExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success, "never an AI seat");
            Assert.IsTrue((await host.As(pmo, p => p.GetRequiredService<IOpsReadinessService>().DecideExceptionAsync(new DecideReadinessExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            Assert.IsFalse((await host.As(pmo, p => p.GetRequiredService<IOpsReadinessService>().DecideExceptionAsync(new DecideReadinessExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success, "the same person cannot approve twice");
            Assert.AreEqual(OpsReadinessExceptionStatus.Pending, host.Read(u => u.Set<OpsReadinessException>().Single().Status), "one approval is not enough");
            Assert.IsNotNull((await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().GateForBaselineAsync(wo, product, Ct))).Refusal);
            Assert.IsTrue((await host.As(ceo, p => p.GetRequiredService<IOpsReadinessService>().DecideExceptionAsync(new DecideReadinessExceptionDto { Id = req.Data.Id, Approve = true }, Ct))).Success);
            Assert.AreEqual(OpsReadinessExceptionStatus.Approved, host.Read(u => u.Set<OpsReadinessException>().Single().Status));
            var pass = await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().GateForBaselineAsync(wo, product, Ct));
            Assert.IsNull(pass.Refusal);
            Assert.AreEqual(OpsReadinessExceptionStatus.Approved, host.Read(u => u.Set<OpsReadinessException>().Single().Status), "the gate itself does not spend the exception");
            // another product of the same work order is not covered
            int other = TraceGenealogyPb4Tests.Seed(host, new PbProduct { Code = "P-1B", Title = "محصول دوم", IsActive = true }).Id;
            Assert.IsNotNull((await host.As(requester, p => p.GetRequiredService<IOpsReadinessService>().GateForBaselineAsync(wo, other, Ct))).Refusal);
            await host.As(requester, async p => { await p.GetRequiredService<IOpsReadinessService>().AfterBaselineAsync(wo, product, Ct); return 0; });
            Assert.AreEqual(OpsReadinessExceptionStatus.Used, host.Read(u => u.Set<OpsReadinessException>().Single().Status), "spent only after the baseline was really made");
        }

        [TestMethod]
        public async Task Readiness_Obligations_Are_Opened_With_A_Due_Date_And_Closed_When_The_Data_Arrives()
        {
            AppHost host = NewHost("oblig");
            int product = TraceGenealogyPb4Tests.Seed(host, new PbProduct { Code = "P-2", Title = "محصول", IsActive = true }).Id;
            int contract = TraceGenealogyPb4Tests.Seed(host, new Entities.Model.NasbNiro.Contracts.ContractEntity { Code = "C-2", Title = "قرارداد", Employer = "کارفرما" }).Id;
            int wo = TraceGenealogyPb4Tests.Seed(host, new Entities.Model.NasbNiro.Contracts.WorkOrder { Code = "WO-2", Title = "دستور", ContractId = contract, Count = 1 }).Id;
            var pmo = AppHost.Principal(22, "pmo.main", "PMO");
            await host.As(pmo, async p => { await p.GetRequiredService<IOpsReadinessService>().AfterBaselineAsync(wo, product, Ct); return 0; });
            var open = host.Read(u => u.Set<OpsReadinessObligation>().Where(o => o.Status == OpsObligationStatus.Open).ToList());
            Assert.IsTrue(open.Any(o => o.ItemCode == ReadinessCodes.Bom && o.Hard));
            Assert.IsTrue(open.All(o => o.DueDate == DateTime.Today.AddDays(7)), "the due date is ★7 days");
            // the released BOM and the approved WBS template arrive
            TraceGenealogyPb4Tests.Seed(host, new PbBomHeader { ProductId = product, Code = "X-BOM", AssemblyPartNumber = "X", Status = PbBomStatus.Released });
            TraceGenealogyPb4Tests.Seed(host, new PbWbsTemplate { ProductId = product, Title = "t", Version = 1, IsApproved = true });
            int bomId = host.Read(u => u.Set<PbBomHeader>().Single(h => h.ProductId == product).Id);
            TraceGenealogyPb4Tests.Seed(host, new PbProjectBaseline { WorkOrderId = wo, ProductId = product, BomHeaderId = bomId, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
            string summary = await host.As(pmo, p => p.GetRequiredService<IOpsReadinessService>().DailyAsync(DateTime.Today, Ct));
            Assert.IsNotNull(summary);
            Assert.AreEqual(OpsObligationStatus.Closed, host.Read(u => u.Set<OpsReadinessObligation>().Single(o => o.ItemCode == ReadinessCodes.Bom).Status));
        }
    }
}
