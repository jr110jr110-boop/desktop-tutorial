using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.InventoryDtos;

namespace MsTests
{
    /// <summary>
    /// The pure rules of the quality remediation R1q (settings bounds, back-date window, release coverage, expiry, CAPA timing, item quality gates, witness notice and evidence,
    /// qualification checks with the welder continuity chain, calibration history, document revision timing, the nightly job schedule). Dates are fixed so every figure is exact.
    /// </summary>
    [TestClass]
    public class QualityR1Tests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 26);

        // ------------------------------------------------------------------------------------------ D30 setting bounds

        [TestMethod]
        public void Every_Default_Setting_Has_Bounds_And_Lies_Inside_Them()
        {
            foreach (QualitySetting setting in QualitySettingKeys.Defaults())
            {
                Assert.IsTrue(QualitySettingRules.TryGetBounds(setting.Key, out int min, out int max), "no bounds for " + setting.Key);
                Assert.IsTrue(setting.Value >= min && setting.Value <= max, $"{setting.Key} default {setting.Value} is outside {min}..{max}");
                Assert.IsNull(QualitySettingRules.Check(setting.Key, setting.Title, setting.Value), "the default of " + setting.Key + " is refused");
            }
        }

        [TestMethod]
        public void Settings_Refuse_A_Typo_That_Would_Turn_A_Control_Off()
        {
            // welder continuity is six months in ASME IX QW-322: 3650 months (a typo for 365 days) would switch the control off
            string refused = QualitySettingRules.Check(QualitySettingKeys.WeldContinuityMonths, "حداکثر وقفه جوشکاری", 3650);
            Assert.IsNotNull(refused);
            StringAssert.Contains(refused, "1");
            StringAssert.Contains(refused, "12");
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.WeldContinuityMonths, "x", 12));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.WeldContinuityMonths, "x", 0));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.WeldContinuityMonths, "x", 13));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.WeldContinuityMonths, "x", 6.5m), "a fraction of a month is not accepted");
            // a technical committee is more than one person and not a crowd
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MajorNcrCommitteeMin, "x", 1));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MajorNcrCommitteeMin, "x", 2));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MajorNcrCommitteeMin, "x", 10));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MajorNcrCommitteeMin, "x", 11));
            // the back-date window may be zero (no late entry at all) but a month at most: a year of back-dating is a typo that would switch the control off (review M14)
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 0));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 30));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 31));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 365));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", -1));
            // the CAPA effectiveness wait is at least a week (a check at once proves nothing) and a year at most; the NCR due days must be at least one
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 0));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 6));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 7));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 365));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 366));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.NcrMinorDays, "x", 0));
            // a witness notice period of zero days would waive the client's witness point at once (review H6)
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MinNoticeDays, "x", 0));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MinNoticeDays, "x", 1));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MinNoticeDays, "x", 30));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MinNoticeDays, "x", 31));
            Assert.AreEqual("تنظیم نامعتبر است.", QualitySettingRules.Check("NotASetting", "x", 1));
        }

        [TestMethod]
        public void A_Stored_Setting_Outside_Its_Range_Is_Read_As_The_Nearest_Bound()
        {
            // rows written before the ranges existed (or by hand in the database) must not switch a control off (review M14)
            Assert.AreEqual(12m, QualitySettingRules.Clamp(QualitySettingKeys.WeldContinuityMonths, 3650m), "a typo for 365 days");
            Assert.AreEqual(1m, QualitySettingRules.Clamp(QualitySettingKeys.WeldContinuityMonths, 0m));
            Assert.AreEqual(1m, QualitySettingRules.Clamp(QualitySettingKeys.WeldContinuityMonths, -4m));
            Assert.AreEqual(6m, QualitySettingRules.Clamp(QualitySettingKeys.WeldContinuityMonths, 6m), "a valid value is untouched");
            Assert.AreEqual(6m, QualitySettingRules.Clamp(QualitySettingKeys.WeldContinuityMonths, 6.9m), "a fraction is cut, not rounded up");
            Assert.AreEqual(30m, QualitySettingRules.Clamp(QualitySettingKeys.MaxBackdateDays, 365m));
            Assert.AreEqual(0m, QualitySettingRules.Clamp(QualitySettingKeys.MaxBackdateDays, 0m), "zero is a valid back-date window");
            Assert.AreEqual(7m, QualitySettingRules.Clamp(QualitySettingKeys.EffectivenessWaitDays, 0m), "an effectiveness wait of zero was read as the check at once");
            Assert.AreEqual(2m, QualitySettingRules.Clamp(QualitySettingKeys.MajorNcrCommitteeMin, 0m), "a committee of nobody");
            Assert.AreEqual(1m, QualitySettingRules.Clamp(QualitySettingKeys.MinNoticeDays, 0m));
            Assert.AreEqual(42.5m, QualitySettingRules.Clamp("NotASetting", 42.5m), "a key without a range is returned unchanged");
            Assert.AreEqual(42.5m, QualitySettingRules.Clamp(null, 42.5m));
            foreach (QualitySetting setting in QualitySettingKeys.Defaults())
                Assert.AreEqual(setting.Value, QualitySettingRules.Clamp(setting.Key, setting.Value), "the default of " + setting.Key + " must be a fixed point of the clamp");
        }

        [TestMethod]
        public void The_Star_Defaults_Of_The_Settings_Are_Those_Of_The_Brief()
        {
            Dictionary<string, decimal> defaults = QualitySettingKeys.Defaults().ToDictionary(s => s.Key, s => s.Value);
            Assert.AreEqual(7m, defaults[QualitySettingKeys.MaxBackdateDays]);
            Assert.AreEqual(30m, defaults[QualitySettingKeys.EffectivenessWaitDays]);
            Assert.AreEqual(1m, defaults[QualitySettingKeys.MinNoticeDays]);
            Assert.AreEqual(6m, defaults[QualitySettingKeys.WeldContinuityMonths], "ASME IX QW-322");
            Assert.AreEqual(3m, defaults[QualitySettingKeys.MajorNcrCommitteeMin], "QC, engineering and manufacturing");
            // the bootstrap marker of the CAPA escalation is a stored row, not a setting a user can edit: it has no range and is not a default
            Assert.IsFalse(QualitySettingRules.TryGetBounds(QualitySettingKeys.CapaEscalationBootstrapped, out _, out _));
            Assert.IsFalse(defaults.ContainsKey(QualitySettingKeys.CapaEscalationBootstrapped));
        }

        // ------------------------------------------------------------------------------------------ D15 back-date window

        [TestMethod]
        public void Record_Date_Window_Is_Exact()
        {
            Assert.IsNull(RecordDateRules.Check(Today, Today, 7, "بازرسی"), "today");
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(-7), Today, 7, "بازرسی"), "exactly seven days back");
            string late = RecordDateRules.Check(Today.AddDays(-8), Today, 7, "بازرسی");
            Assert.IsNotNull(late, "eight days back");
            StringAssert.Contains(late, "8 روز");
            StringAssert.Contains(late, "7 روز");
            StringAssert.Contains(late, "بازرسی");
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(1), Today, 7, "بازرسی"), "tomorrow is tolerated (time zone of the browser)");
            string future = RecordDateRules.Check(Today.AddDays(2), Today, 7, "گزارش آزمون");
            Assert.IsNotNull(future);
            StringAssert.Contains(future, "در آینده");
            StringAssert.Contains(future, "گزارش آزمون");
            // the time of day never matters
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(-7).AddHours(23), Today.AddHours(1), 7, "x"));
            Assert.IsNotNull(RecordDateRules.Check(Today.AddDays(-1), Today, 0, "x"), "no late entry at all");
            Assert.IsNull(RecordDateRules.Check(Today, Today, 0, "x"));
        }

        [TestMethod]
        public void A_Weld_Log_Gets_No_Tomorrow_Leeway()
        {
            // a weld entry restarts the six-month continuity of a welder: at most today (review M15)
            Assert.IsNull(RecordDateRules.Check(Today, Today, 7, "گزارش جوشکاری", allowTomorrow: false), "today");
            string tomorrow = RecordDateRules.Check(Today.AddDays(1), Today, 7, "گزارش جوشکاری", allowTomorrow: false);
            Assert.IsNotNull(tomorrow);
            StringAssert.Contains(tomorrow, "در آینده");
            StringAssert.Contains(tomorrow, "گزارش جوشکاری");
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(-7), Today, 7, "گزارش جوشکاری", allowTomorrow: false), "the back-date window is the same");
            Assert.IsNotNull(RecordDateRules.Check(Today.AddDays(-8), Today, 7, "گزارش جوشکاری", allowTomorrow: false));
            // the other records keep the one day of leeway for the time zone of the browser
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(1), Today, 7, "بازرسی"));
            Assert.IsNull(RecordDateRules.Check(Today.AddDays(1), Today, 7, "بازرسی", allowTomorrow: true));
        }

        // ------------------------------------------------------------------------------------------ D10 release coverage of a delivery

        private static CoverageLine Qty(int item, string code, decimal quantity) => new CoverageLine { ItemId = item, ItemCode = code, Quantity = quantity };

        private static CoverageLine Serial(int item, string code, params string[] serials) =>
            new CoverageLine { ItemId = item, ItemCode = code, IsSerial = true, Quantity = serials.Length, Serials = serials.ToList() };

        [TestMethod]
        public void Delivery_May_Carry_Only_What_The_Release_Note_Lists()
        {
            var released = new[] { Qty(1, "PUMP", 10m) };
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 4m) }, released, new Dictionary<int, decimal>()).Count, "inside the released quantity");
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 4m) }, released, new Dictionary<int, decimal> { { 1, 6m } }).Count, "4 + 6 already delivered = exactly 10");
            List<string> over = ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 5m) }, released, new Dictionary<int, decimal> { { 1, 6m } });
            Assert.AreEqual(1, over.Count, "5 + 6 > 10");
            StringAssert.Contains(over[0], "PUMP");
            StringAssert.Contains(over[0], "11");
            StringAssert.Contains(over[0], "10");
            List<string> unlisted = ReleaseCoverage.Uncovered(new[] { Qty(2, "BOLT", 1m) }, released, null);
            Assert.AreEqual(1, unlisted.Count, "an item the note does not list");
            StringAssert.Contains(unlisted[0], "BOLT");
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 3m), Qty(1, "PUMP", 3m) }, released, null).Count, "two lines of one item are added up (6 of 10)");
            Assert.AreEqual(1, ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 6m), Qty(1, "PUMP", 6m) }, released, null).Count, "two lines of one item are added up (12 of 10)");
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Qty(1, "PUMP", 12m) }, new[] { Qty(1, "PUMP", 5m), Qty(1, "PUMP", 7m) }, null).Count, "two release notes add up (5 + 7)");
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(null, released, null).Count, "nothing delivered, nothing to cover");
        }

        [TestMethod]
        public void Serial_Items_Are_Covered_By_Serial_Number_Not_By_Count()
        {
            var released = new[] { Serial(7, "PMP", "S-1", "S-2") };
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Serial(7, "PMP", "S-2") }, released, null).Count);
            Assert.AreEqual(0, ReleaseCoverage.Uncovered(new[] { Serial(7, "PMP", " s-1 ") }, released, null).Count, "case and spaces do not matter");
            List<string> wrong = ReleaseCoverage.Uncovered(new[] { Serial(7, "PMP", "S-1", "S-3") }, released, new Dictionary<int, decimal> { { 7, 99m } });
            Assert.AreEqual(1, wrong.Count, "S-3 was never released; the quantity already delivered is irrelevant for serial items");
            StringAssert.Contains(wrong[0], "S-3");
        }

        [TestMethod]
        public void The_Release_Note_Gate_Applies_To_Every_Delivery_Except_Spare_Parts_Of_A_Sales_Order_Or_A_Service_Order_Without_A_Work_Order()
        {
            // GateApplies(hasSalesOrder, hasServiceOrder, hasWorkOrder, anyUnitHasWorkOrder)
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, false, true, false), "a delivery of a work order's product");
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, false, false, false), "a delivery that names no order of any kind: the gate asks for the work order");
            Assert.IsFalse(ReleaseCoverage.GateApplies(true, false, false, false), "spare parts against a sales order: the fulfilment check of the sales order applies");
            Assert.IsFalse(ReleaseCoverage.GateApplies(false, true, false, false), "spare parts against a service order with no work order");
            // review H4: naming a sales or a service order must not be a way round the release note of a manufactured product
            Assert.IsTrue(ReleaseCoverage.GateApplies(true, false, true, false), "a sales order that also names the work order");
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, true, true, false), "a service order that names a work order cannot be used to skip the release note");
            Assert.IsTrue(ReleaseCoverage.GateApplies(true, false, false, true), "a sales order with a serial unit that a work order made");
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, true, false, true), "a service order with a serial unit that a work order made");
            Assert.IsTrue(ReleaseCoverage.GateApplies(true, true, true, true));
            Assert.IsFalse(ReleaseCoverage.GateApplies(true, true, false, false), "both orders, no work order anywhere: spare parts");
        }

        // ------------------------------------------------------------------------------------------ D32 expired lots

        [TestMethod]
        public void Lot_Expiry_Is_Judged_By_The_Day_And_Only_Consuming_Documents_Are_Blocked()
        {
            Assert.IsFalse(LotExpiryRules.IsExpired(null, Today), "no expiry date");
            Assert.IsFalse(LotExpiryRules.IsExpired(Today, Today), "the last day is still usable");
            Assert.IsFalse(LotExpiryRules.IsExpired(Today.AddHours(15), Today), "time of day ignored");
            Assert.IsTrue(LotExpiryRules.IsExpired(Today.AddDays(-1), Today));
            foreach (InventoryDocumentType type in new[] { InventoryDocumentType.Issue, InventoryDocumentType.CustomerDelivery, InventoryDocumentType.SubcontractIssue, InventoryDocumentType.StageConversion })
                Assert.IsTrue(LotExpiryRules.Consumes(type), type + " uses the material up");
            // the ways an expired lot leaves the stock properly: to quarantine or scrap (transfer), back to the supplier, sold as scrap, written off
            foreach (InventoryDocumentType type in new[] { InventoryDocumentType.Transfer, InventoryDocumentType.SupplierReturn, InventoryDocumentType.ScrapSale, InventoryDocumentType.AdjustmentDecrease, InventoryDocumentType.PurchaseReceipt })
                Assert.IsFalse(LotExpiryRules.Consumes(type), type + " must stay possible for an expired lot");
        }

        [TestMethod]
        public void An_Expired_Lot_Moves_Only_Into_Quarantine_Or_Scrap()
        {
            // review M13: a transfer (including the release from quarantine and the return from a subcontractor) must not carry expired goods into stock that production draws on
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.Issue, null));
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.CustomerDelivery, null));
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.RawMaterial), "into usable stock");
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.General));
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.Transfer, null), "no destination known");
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.QcRelease, WarehouseKind.Parts), "the release from quarantine into usable stock");
            Assert.IsTrue(LotExpiryRules.Refuses(InventoryDocumentType.SubcontractReturn, WarehouseKind.WorkInProgress), "the return from a subcontractor into usable stock");
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.Quarantine), "to quarantine");
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.Scrap), "to scrap");
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.SupplierReturn, null), "back to the supplier");
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.ScrapSale, null));
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.AdjustmentDecrease, null), "written off");
            Assert.IsFalse(LotExpiryRules.Refuses(InventoryDocumentType.PurchaseReceipt, WarehouseKind.RawMaterial), "receiving is not using");
        }

        [TestMethod]
        public void A_Concession_Covers_An_Expired_Lot_Only_When_It_Was_Given_After_The_Expiry()
        {
            DateTime expiry = Today.AddDays(-10);
            Assert.IsTrue(LotExpiryRules.ConcessionCovers(expiry, new DateTimeOffset(expiry.AddDays(1).AddHours(9))), "approved the day after the lot spoiled");
            Assert.IsTrue(LotExpiryRules.ConcessionCovers(expiry, new DateTimeOffset(expiry.AddHours(8))), "approved on the expiry day itself");
            Assert.IsFalse(LotExpiryRules.ConcessionCovers(expiry, new DateTimeOffset(expiry.AddDays(-1).AddHours(23))), "approved while the lot was still good says nothing about the spoiled lot");
            Assert.IsFalse(LotExpiryRules.ConcessionCovers(expiry, new DateTimeOffset(expiry.AddDays(-30))));
        }

        // ------------------------------------------------------------------------------------------ concession permit, nonconforming stock hold (review H4, M18)

        private static NonConformance Ncr(NcrStatus status, NcrDisposition disposition, bool signedByAll, DateTime? permit = null, DateTime? qualitySigned = null)
        {
            DateTimeOffset signed = new DateTimeOffset((qualitySigned ?? Today.AddDays(-3)).AddHours(9));
            var n = new NonConformance { Status = status, Disposition = disposition, SerialNumbers = "S-1\r\n S-2 \n\nS-3" };
            if (signedByAll)
            {
                n.QualityApprovedAt = signed;
                n.EngineeringApprovedAt = signed.AddHours(1);
                n.ManufacturingApprovedAt = signed.AddHours(2);
            }
            if (permit.HasValue) n.ConcessionApprovedAt = new DateTimeOffset(permit.Value.AddHours(10));
            return n;
        }

        [TestMethod]
        public void A_Concession_Permit_Counts_Only_On_A_Fully_Signed_Concession_With_The_Permit_After_The_Quality_Signature()
        {
            Assert.IsTrue(ConcessionRules.IsValid(Ncr(NcrStatus.Approved, NcrDisposition.Concession, true, Today.AddDays(-2)), false), "the full chain");
            Assert.IsTrue(ConcessionRules.IsValid(Ncr(NcrStatus.Executed, NcrDisposition.Concession, true, Today.AddDays(-2)), false));
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Closed, NcrDisposition.Concession, true, Today.AddDays(-2)), false), "a closed report is history unless asked for");
            Assert.IsTrue(ConcessionRules.IsValid(Ncr(NcrStatus.Closed, NcrDisposition.Concession, true, Today.AddDays(-2)), true));
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Open, NcrDisposition.Concession, true, Today.AddDays(-2)), true), "an open report is not a decision yet");
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Cancelled, NcrDisposition.Concession, true, Today.AddDays(-2)), true));
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Approved, NcrDisposition.Scrap, true, Today.AddDays(-2)), false), "another disposition is not a concession");
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Approved, NcrDisposition.Concession, false, Today.AddDays(-2)), false), "unsigned");
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Approved, NcrDisposition.Concession, true), false), "no permit");
            Assert.IsFalse(ConcessionRules.IsValid(Ncr(NcrStatus.Approved, NcrDisposition.Concession, true, Today.AddDays(-5), Today.AddDays(-3)), false), "the permit given before the quality signature");
            Assert.IsFalse(ConcessionRules.IsValid(null, false));
            NonConformance partial = Ncr(NcrStatus.Approved, NcrDisposition.Concession, true, Today.AddDays(-2));
            partial.ManufacturingApprovedAt = null;
            Assert.IsFalse(ConcessionRules.IsValid(partial, false), "one of the three signatures is missing");
        }

        [TestMethod]
        public void Goods_Of_An_Open_Or_Approved_Ncr_Are_Held_Until_A_Valid_Concession_Or_Execution()
        {
            Assert.IsTrue(NonconformingHoldRules.Holds(Ncr(NcrStatus.Open, NcrDisposition.None, false)));
            Assert.IsTrue(NonconformingHoldRules.Holds(Ncr(NcrStatus.Approved, NcrDisposition.Scrap, true)), "approved but not yet executed");
            Assert.IsFalse(NonconformingHoldRules.Holds(Ncr(NcrStatus.Executed, NcrDisposition.Scrap, true)), "executed: the stock document moved the goods");
            Assert.IsFalse(NonconformingHoldRules.Holds(Ncr(NcrStatus.Closed, NcrDisposition.Scrap, true)));
            Assert.IsFalse(NonconformingHoldRules.Holds(Ncr(NcrStatus.Cancelled, NcrDisposition.None, false)));
            Assert.IsFalse(NonconformingHoldRules.Holds(Ncr(NcrStatus.Approved, NcrDisposition.Concession, true, Today.AddDays(-2))), "a valid concession is the permission to use the goods");
            Assert.IsTrue(NonconformingHoldRules.Holds(Ncr(NcrStatus.Approved, NcrDisposition.Concession, true)), "a concession without its permit still holds");
            NonConformance deleted = Ncr(NcrStatus.Open, NcrDisposition.None, false);
            deleted.Delete();
            Assert.IsFalse(NonconformingHoldRules.Holds(deleted));
            Assert.IsFalse(NonconformingHoldRules.Holds(null));
        }

        [TestMethod]
        public void Held_Goods_Cannot_Be_Used_Up_Produced_Back_Or_Moved_Into_Usable_Stock()
        {
            foreach (InventoryDocumentType type in new[] { InventoryDocumentType.Issue, InventoryDocumentType.CustomerDelivery, InventoryDocumentType.SubcontractIssue, InventoryDocumentType.StageConversion,
                                                           InventoryDocumentType.ProductionReceipt, InventoryDocumentType.ProductionReturn })
                Assert.IsTrue(NonconformingHoldRules.Refuses(type, null), type + " must refuse held goods");
            Assert.IsTrue(NonconformingHoldRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.FinishedGoods), "into stock a customer draws on");
            Assert.IsTrue(NonconformingHoldRules.Refuses(InventoryDocumentType.QcRelease, WarehouseKind.Parts));
            // the way out of the hold: the report's own disposition document (rework into quarantine, scrap, return to the supplier) and the write-offs
            Assert.IsFalse(NonconformingHoldRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.Quarantine), "rework or repair goes to quarantine");
            Assert.IsFalse(NonconformingHoldRules.Refuses(InventoryDocumentType.Transfer, WarehouseKind.Scrap), "scrap");
            Assert.IsFalse(NonconformingHoldRules.Refuses(InventoryDocumentType.SupplierReturn, null), "return to the supplier");
            Assert.IsFalse(NonconformingHoldRules.Refuses(InventoryDocumentType.AdjustmentDecrease, null));
            Assert.IsFalse(NonconformingHoldRules.Refuses(InventoryDocumentType.PurchaseReceipt, WarehouseKind.RawMaterial));
        }

        [TestMethod]
        public void A_Hold_Blocks_The_Held_Quantity_Not_The_Whole_Lot()
        {
            // 10 on hand, 4 held by an open report: 6 may go, 7 may not
            Assert.IsFalse(NonconformingHoldRules.BlocksQuantity(10m, 6m, 4m), "exactly the free quantity");
            Assert.IsTrue(NonconformingHoldRules.BlocksQuantity(10m, 7m, 4m));
            Assert.IsFalse(NonconformingHoldRules.BlocksQuantity(10m, 10m, 0m), "nothing is held in this warehouse");
            Assert.IsTrue(NonconformingHoldRules.BlocksQuantity(4m, 4m, 4m), "all of it is held");
            Assert.IsFalse(NonconformingHoldRules.BlocksQuantity(4m, 0m, 4m), "a document that takes nothing out of the lot");
            Assert.IsTrue(NonconformingHoldRules.BlocksQuantity(3m, 0m, 4m), "less on hand than the reports hold: the shortfall is still held back");
        }

        [TestMethod]
        public void The_Serials_Of_A_Report_Are_Read_One_Per_Line_And_Trimmed()
        {
            CollectionAssert.AreEqual(new[] { "S-1", "S-2", "S-3" }, NonconformingHoldRules.SerialsOf(Ncr(NcrStatus.Open, NcrDisposition.None, false)));
            Assert.AreEqual(0, NonconformingHoldRules.SerialsOf(new NonConformance()).Count);
            Assert.AreEqual(0, NonconformingHoldRules.SerialsOf(null).Count);
        }

        // ------------------------------------------------------------------------------------------ D13, D20, D21 CAPA timing

        [TestMethod]
        public void Capa_Effectiveness_Cannot_Be_Judged_Before_The_Wait_Has_Passed()
        {
            Assert.AreEqual(new DateTime(2026, 10, 31), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1, 10, 30, 0), 30));
            Assert.AreEqual(new DateTime(2026, 10, 8), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1, 23, 59, 0), 7), "the shortest waiting period the settings allow is seven days");
            Assert.AreEqual(new DateTime(2027, 10, 1), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1), 365));
        }

        [TestMethod]
        public void Capa_Is_Implemented_On_The_Day_Of_Its_Last_Action()
        {
            Assert.AreEqual(new DateTime(2026, 10, 24), CapaRules.ImplementedOn(new DateTime?[] { new DateTime(2026, 10, 20), new DateTime(2026, 10, 24, 15, 0, 0), null }, Today), "the last day an action was done");
            Assert.AreEqual(Today.Date, CapaRules.ImplementedOn(new DateTime?[] { null }, Today), "no day known: today");
            Assert.AreEqual(Today.Date, CapaRules.ImplementedOn(null, Today));
            // an action entered seven days late (the default back-date window) lets the shortest waiting period run out today; an action done today does not
            Assert.AreEqual(Today.Date, CapaRules.EffectivenessEarliest(CapaRules.ImplementedOn(new DateTime?[] { Today.AddDays(-7) }, Today), 7));
            Assert.AreEqual(Today.Date.AddDays(7), CapaRules.EffectivenessEarliest(CapaRules.ImplementedOn(new DateTime?[] { Today }, Today), 7));
        }

        [TestMethod]
        public void Late_Capa_Goes_To_QA_At_Once_And_To_The_CEO_After_The_Set_Days()
        {
            Assert.AreEqual(0, CapaRules.EscalationLevel(null, Today, 14), "no due date");
            Assert.AreEqual(0, CapaRules.EscalationLevel(Today, Today, 14), "due today is not late");
            Assert.AreEqual(0, CapaRules.EscalationLevel(Today.AddDays(5), Today, 14));
            Assert.AreEqual(1, CapaRules.EscalationLevel(Today.AddDays(-1), Today, 14), "one day late: QA");
            Assert.AreEqual(1, CapaRules.EscalationLevel(Today.AddDays(-13), Today, 14), "13 days late: still QA only");
            Assert.AreEqual(2, CapaRules.EscalationLevel(Today.AddDays(-14), Today, 14), "14 days late: the CEO as well");
            Assert.AreEqual(2, CapaRules.EscalationLevel(Today.AddDays(-400), Today, 14));
            Assert.AreEqual(2, CapaRules.EscalationLevel(Today.AddDays(-3), Today, 3), "the CEO setting is respected");
        }

        [TestMethod]
        public void Only_Qa_Cancels_A_Capa_That_A_Rule_Or_The_System_Required()
        {
            Assert.IsTrue(CapaRules.IsMandatory(true, CapaSource.Other), "drafted by the system");
            Assert.IsTrue(CapaRules.IsMandatory(false, CapaSource.AuditFinding));
            Assert.IsTrue(CapaRules.IsMandatory(false, CapaSource.Ncr));
            Assert.IsFalse(CapaRules.IsMandatory(false, CapaSource.Other));
            Assert.IsFalse(CapaRules.IsMandatory(false, CapaSource.CustomerComplaint));
            Assert.IsFalse(CapaRules.IsMandatory(false, CapaSource.Risk));
        }

        [TestMethod]
        public void A_New_Capa_Starts_In_Round_One()
        {
            // the column defaults to 1 in the database as well, so existing rows are round one
            Assert.AreEqual(1, new Capa().Iteration);
            Assert.AreEqual(1, new CapaAction().Iteration);
            Assert.AreEqual(0, new Capa().EscalationLevel);
        }

        // ------------------------------------------------------------------------------------------ D11 item quality gates

        [TestMethod]
        public void Weakening_An_Item_Quality_Gate_Is_Recognised()
        {
            Assert.IsFalse(ItemQualityRules.IsWeakening(true, CertificateType.T31, 365, true, CertificateType.T31, 365), "nothing changed");
            Assert.IsTrue(ItemQualityRules.IsWeakening(true, null, null, false, null, null), "inspection on receipt switched off");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, null, null, true, null, null), "inspection switched on");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, CertificateType.T32, null, false, CertificateType.T31, null), "3.2 lowered to 3.1");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, CertificateType.T31, null, false, CertificateType.T22, null), "3.1 lowered to 2.2");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, CertificateType.T22, null, false, null, null), "certificate requirement removed");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, CertificateType.T22, null, false, CertificateType.T31, null), "2.2 raised to 3.1");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, null, null, false, CertificateType.T22, null), "a requirement added");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, null, 365, false, null, null), "shelf life removed");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, null, 365, false, null, 730), "shelf life lengthened");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, null, 365, false, null, 180), "shelf life shortened");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, null, null, false, null, 365), "a shelf life added");
            Assert.IsTrue(ItemQualityRules.IsWeakening(false, null, 365, false, null, 0), "zero means none");
            Assert.IsFalse(ItemQualityRules.IsWeakening(false, null, 0, false, null, null), "zero and none are the same");
            Assert.IsNull(ItemQualityRules.NormalizeShelfLife(0));
            Assert.IsNull(ItemQualityRules.NormalizeShelfLife(-5));
            Assert.AreEqual(30, ItemQualityRules.NormalizeShelfLife(30));
        }

        [TestMethod]
        public void A_New_Item_Starts_With_The_Inspection_Gate_Of_Its_Group()
        {
            Assert.IsFalse(ItemQualityRules.DefaultInspection("05"), "consumables and office supplies are received without inspection");
            foreach (string group in new[] { "01", "02", "03", "04", "06", "07", "08", "12", "50", null })
                Assert.IsTrue(ItemQualityRules.DefaultInspection(group), "group " + (group ?? "(none)") + " starts with the incoming inspection");
        }

        [TestMethod]
        public void Item_Quality_Roles_Exclude_The_Warehouse_And_Planning()
        {
            string[] allowed = InventoryRoles.ItemQuality.Split(',');
            CollectionAssert.AreEquivalent(new[] { "PMO", "QC", "QA" }, allowed);
            foreach (string storekeeper in new[] { "WAREHOUSE", "WHSUPERVISOR", "PLANNING", "ENGINEERING" })
                Assert.IsFalse(allowed.Contains(storekeeper), storekeeper + " must not change a quality gate of an item");
        }

        // ------------------------------------------------------------------------------------------ D9, D18 witness notice, not applicable, evidence

        [TestMethod]
        public void Witness_Point_Is_Waived_Only_After_The_Whole_Notice_Period()
        {
            Assert.IsNull(ItpNoticeRules.Deadline(null, 10));
            Assert.AreEqual(new DateTime(2026, 10, 11), ItpNoticeRules.Deadline(new DateTime(2026, 10, 1, 14, 0, 0), 10));
            Assert.IsFalse(ItpNoticeRules.WaivedByNotice(null, 10, Today), "no notice, no waiver");
            Assert.IsFalse(ItpNoticeRules.WaivedByNotice(Today, 10, Today), "notice today, period just started");
            Assert.IsFalse(ItpNoticeRules.WaivedByNotice(Today.AddDays(-9), 10, Today), "day 9 of 10");
            Assert.IsTrue(ItpNoticeRules.WaivedByNotice(Today.AddDays(-10), 10, Today), "the deadline day itself");
            Assert.IsTrue(ItpNoticeRules.WaivedByNotice(Today.AddDays(-12), 10, Today));
            Assert.IsFalse(ItpNoticeRules.WaivedByNotice(Today.AddDays(-12), 60, Today), "abroad: 60 days");
            Assert.IsTrue(ItpNoticeRules.WaivedByNotice(Today.AddDays(-60), 60, Today));
        }

        [TestMethod]
        public void A_Typed_Notice_Date_Other_Than_Today_Is_Refused()
        {
            Assert.IsNull(ItpNoticeRules.CheckTypedDate(null, Today));
            Assert.IsNull(ItpNoticeRules.CheckTypedDate(Today.AddHours(11), Today), "today, whatever the time");
            string back = ItpNoticeRules.CheckTypedDate(Today.AddDays(-12), Today);
            Assert.IsNotNull(back, "a back-dated notice would waive the witness at once");
            StringAssert.Contains(back, "عقب‌تر");
            Assert.IsNotNull(ItpNoticeRules.CheckTypedDate(Today.AddDays(1), Today), "a future date is not a notice either");
        }

        [TestMethod]
        public void Only_The_Client_Letter_May_Drop_A_Client_Or_Third_Party_Hold_Or_Witness()
        {
            Assert.IsTrue(ItpNoticeRules.ThirdPartyControlled(ItpPoint.H, ItpPoint.R), "TPI hold");
            Assert.IsTrue(ItpNoticeRules.ThirdPartyControlled(ItpPoint.W, ItpPoint.None), "TPI witness");
            Assert.IsTrue(ItpNoticeRules.ThirdPartyControlled(ItpPoint.R, ItpPoint.H), "client hold");
            Assert.IsTrue(ItpNoticeRules.ThirdPartyControlled(ItpPoint.None, ItpPoint.W), "client witness");
            Assert.IsFalse(ItpNoticeRules.ThirdPartyControlled(ItpPoint.R, ItpPoint.R), "review points only");
            Assert.IsFalse(ItpNoticeRules.ThirdPartyControlled(ItpPoint.RA, ItpPoint.A), "random audit and attendance are not holds or witnesses");
            Assert.IsFalse(ItpNoticeRules.ThirdPartyControlled(ItpPoint.None, ItpPoint.None));
        }

        [TestMethod]
        public void An_Activity_Without_A_Required_Report_Needs_Proof_To_Be_Done()
        {
            Assert.IsFalse(ItpNoticeRules.HasEvidence(false, null));
            Assert.IsTrue(ItpNoticeRules.HasEvidence(true, null), "an accepted inspection record on the activity");
            Assert.IsTrue(ItpNoticeRules.HasEvidence(false, Guid.NewGuid()), "a document");
            Assert.IsTrue(ItpNoticeRules.HasEvidence(true, Guid.NewGuid()));
        }

        [TestMethod]
        public void The_Notice_Period_Is_Never_Shorter_Than_The_Company_Minimum()
        {
            // plans saved before the minimum existed may hold 0 days; a witness point would be waived the day it was announced (review H6)
            Assert.AreEqual(1, ItpNoticeRules.EffectiveDays(0, 1));
            Assert.AreEqual(10, ItpNoticeRules.EffectiveDays(10, 1));
            Assert.AreEqual(5, ItpNoticeRules.EffectiveDays(2, 5), "the company raised its minimum above the stored period");
            Assert.AreEqual(0, ItpNoticeRules.EffectiveDays(0, 0));
            Assert.AreEqual(3, ItpNoticeRules.EffectiveDays(3, -2), "a negative minimum is read as none");
            // with the effective period a legacy plan with 0 days is no longer waived on the notice day itself
            Assert.IsFalse(ItpNoticeRules.WaivedByNotice(Today, ItpNoticeRules.EffectiveDays(0, 1), Today));
            Assert.IsTrue(ItpNoticeRules.WaivedByNotice(Today.AddDays(-1), ItpNoticeRules.EffectiveDays(0, 1), Today));
            Assert.IsTrue(ItpNoticeRules.WaivedByNotice(Today, 0, Today), "what the legacy value would have done");

            Assert.IsNotNull(ItpNoticeRules.CheckNoticeDays(0, 1));
            Assert.IsNull(ItpNoticeRules.CheckNoticeDays(1, 1));
            Assert.IsNull(ItpNoticeRules.CheckNoticeDays(365, 1));
            Assert.IsNotNull(ItpNoticeRules.CheckNoticeDays(366, 1));
            Assert.IsNotNull(ItpNoticeRules.CheckNoticeDays(-3, 1));
            string refused = ItpNoticeRules.CheckNoticeDays(2, 3);
            Assert.IsNotNull(refused);
            StringAssert.Contains(refused, "3");
            Assert.AreEqual(300, ItpNoticeRules.MaxReferenceLength, "the column length of the reference of a not-applicable decision");
        }

        [TestMethod]
        public void A_Witness_Letter_Counts_From_The_Day_It_Was_Sent_Inside_The_Window_And_After_The_Plan_Approval()
        {
            DateTime approved = Today.AddDays(-4);
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(Today, Today, approved, 7), "sent today");
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(Today.AddDays(-2), Today, approved, 7), "sent two days ago, after the approval");
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(approved, Today, approved, 7), "sent on the approval day itself");
            string beforeApproval = ItpNoticeRules.CheckLetterDate(approved.AddDays(-1), Today, approved, 30);
            Assert.IsNotNull(beforeApproval, "a letter older than the approval of the plan proves nothing about this plan");
            StringAssert.Contains(beforeApproval, "تایید برنامه");
            string future = ItpNoticeRules.CheckLetterDate(Today.AddDays(1), Today, approved, 7);
            Assert.IsNotNull(future);
            StringAssert.Contains(future, "آینده");
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(Today.AddDays(-7), Today, null, 7), "no approval date known: only the window applies; seven days back is inside");
            string old = ItpNoticeRules.CheckLetterDate(Today.AddDays(-8), Today, null, 7);
            Assert.IsNotNull(old, "eight days back is outside a window of seven");
            StringAssert.Contains(old, "7 روز");
            Assert.IsNotNull(ItpNoticeRules.CheckLetterDate(Today.AddDays(-1), Today, null, 0), "a window of zero: only today");
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(Today, Today, null, 0));
            Assert.IsNull(ItpNoticeRules.CheckLetterDate(Today.AddHours(22), Today.AddHours(1), approved, 7), "time of day never matters");
        }

        [TestMethod]
        public void The_Client_Letter_That_Drops_A_Control_Point_Must_Be_Newer_Than_The_Last_Plan_Approval()
        {
            DateTime approved = Today.AddDays(-20);
            Assert.IsNull(ItpNoticeRules.CheckClientLetterDate(Today, Today, approved));
            Assert.IsNull(ItpNoticeRules.CheckClientLetterDate(approved, Today, approved), "written on the approval day");
            Assert.IsNull(ItpNoticeRules.CheckClientLetterDate(Today.AddDays(-5), Today, null), "the plan was never approved: any past letter");
            string older = ItpNoticeRules.CheckClientLetterDate(approved.AddDays(-1), Today, approved);
            Assert.IsNotNull(older, "a letter older than the approval cannot drop a point the client approved");
            StringAssert.Contains(older, "پیش از آخرین تایید");
            Assert.IsNotNull(ItpNoticeRules.CheckClientLetterDate(Today.AddDays(1), Today, approved), "a letter from the future");
        }

        [TestMethod]
        public void The_Client_Approval_Date_Is_Today_Or_Inside_The_Back_Date_Window()
        {
            Assert.IsNull(ItpNoticeRules.CheckApprovalDate(Today, Today, 7));
            Assert.IsNull(ItpNoticeRules.CheckApprovalDate(Today.AddDays(-7), Today, 7), "exactly seven days back");
            string old = ItpNoticeRules.CheckApprovalDate(Today.AddDays(-8), Today, 7);
            Assert.IsNotNull(old);
            StringAssert.Contains(old, "7 روز");
            Assert.IsNotNull(ItpNoticeRules.CheckApprovalDate(Today.AddDays(1), Today, 7), "an approval from the future would let a letter 'after the approval' be a future letter");
            Assert.IsNull(ItpNoticeRules.CheckApprovalDate(Today, Today, 0));
            Assert.IsNotNull(ItpNoticeRules.CheckApprovalDate(Today.AddDays(-1), Today, 0));
        }

        [TestMethod]
        public void Only_A_Customer_Of_The_Contract_Stands_For_Its_Client()
        {
            // IsClient(supplier, customer, customerId, linkedCustomerIds)
            Assert.IsFalse(ClientPartyRules.IsClient(true, false, null, new int[0]), "a supplier is never the client");
            Assert.IsFalse(ClientPartyRules.IsClient(true, true, 4, new[] { 4 }), "a party that is a supplier too is still not the client");
            Assert.IsTrue(ClientPartyRules.IsClient(false, true, 4, new[] { 4, 9 }), "a customer Sales linked to the contract");
            Assert.IsFalse(ClientPartyRules.IsClient(false, true, 5, new[] { 4, 9 }), "a customer of another contract");
            Assert.IsFalse(ClientPartyRules.IsClient(false, false, null, new[] { 4 }), "an organisation (a ministry, an agency) when the contract's customer is known");
            Assert.IsFalse(ClientPartyRules.IsClient(false, true, null, new[] { 4 }), "a customer party not tied to a customer record");
            Assert.IsTrue(ClientPartyRules.IsClient(false, true, 5, new int[0]), "no customer recorded on the contract: any non-supplier party");
            Assert.IsTrue(ClientPartyRules.IsClient(false, false, null, null), "no link information at all");
        }

        [TestMethod]
        public void An_Evidence_File_Must_Be_Recent_From_A_Quality_Uploader_And_Used_Once()
        {
            DateTimeOffset At(int daysBack) => new DateTimeOffset(Today.AddDays(-daysBack).AddHours(11));
            Assert.IsNull(EvidenceDocumentRules.Check(At(0), true, false, Today, 7), "uploaded today by QC");
            Assert.IsNull(EvidenceDocumentRules.Check(At(7), true, false, Today, 7), "seven days back is inside the window");
            string old = EvidenceDocumentRules.Check(At(8), true, false, Today, 7);
            Assert.IsNotNull(old);
            StringAssert.Contains(old, "7 روز");
            Assert.IsNotNull(EvidenceDocumentRules.Check(At(-3), true, false, Today, 7), "an upload stamped in the future");
            string legacy = EvidenceDocumentRules.Check(default(DateTimeOffset), true, false, Today, 7);
            Assert.IsNotNull(legacy, "a file loaded before uploads were stamped has no date");
            StringAssert.Contains(legacy, "قدیمی");
            Assert.IsNotNull(EvidenceDocumentRules.Check(new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero), true, false, Today, 7), "before 2000 is the same as never");
            string wrongUploader = EvidenceDocumentRules.Check(At(0), false, false, Today, 7);
            Assert.IsNotNull(wrongUploader, "uploaded by somebody who is not in quality or engineering");
            StringAssert.Contains(wrongUploader, "QC");
            string reused = EvidenceDocumentRules.Check(At(0), true, true, Today, 7);
            Assert.IsNotNull(reused, "one file proves one activity");
            StringAssert.Contains(reused, "فعالیت دیگری");
            Assert.IsNull(EvidenceDocumentRules.Check(At(0), true, false, Today, 0), "window zero, uploaded today: acceptable");
            Assert.IsNotNull(EvidenceDocumentRules.Check(At(1), true, false, Today, 0), "window zero: yesterday's upload is refused");
        }

        [TestMethod]
        public void The_Quality_Roles_That_May_Upload_Evidence_Exclude_Production_And_The_Warehouse()
        {
            string[] uploaders = QualityRoles.EvidenceUploaders.Split(',');
            CollectionAssert.AreEquivalent(new[] { "QC", "QA", "ENGINEERING" }, uploaders);
            foreach (string role in new[] { "PRODUCTIONMANAGER", "CONSTRUCTION", "WAREHOUSE", "WHSUPERVISOR", "PLANNING", "COMMERCIAL" })
                Assert.IsFalse(uploaders.Contains(role), role + " is not a quality uploader");
        }

        [TestMethod]
        public void The_Maker_Of_A_Record_Is_Everyone_Who_Created_Or_Edited_It()
        {
            // the one rule behind the maker-checker of the NCR, the ITP, the CAPA and the review (reviews H3, H7, M11)
            string ids = Services.NasbNiro.Finance.LedgerMath.WithEditor(null, 5);
            Assert.AreEqual(",5,", ids);
            ids = Services.NasbNiro.Finance.LedgerMath.WithEditor(ids, 7);
            Assert.AreEqual(",5,7,", ids);
            Assert.AreEqual(",5,7,", Services.NasbNiro.Finance.LedgerMath.WithEditor(ids, 5), "an editor is recorded once");
            Assert.IsTrue(Services.NasbNiro.Finance.LedgerMath.Touched(0, ids, 5));
            Assert.IsTrue(Services.NasbNiro.Finance.LedgerMath.Touched(0, ids, 7));
            Assert.IsFalse(Services.NasbNiro.Finance.LedgerMath.Touched(0, ids, 57), "user 57 is not user 5 or 7");
            Assert.IsFalse(Services.NasbNiro.Finance.LedgerMath.Touched(0, ids, 6));
            Assert.IsTrue(Services.NasbNiro.Finance.LedgerMath.Touched(9, null, 9), "the creator");
            Assert.IsFalse(Services.NasbNiro.Finance.LedgerMath.Touched(9, null, 8));
            Assert.IsFalse(Services.NasbNiro.Finance.LedgerMath.Touched(0, null, 0), "a record the system drafted (creator 0) has no maker among the users");
            Assert.IsFalse(Services.NasbNiro.Finance.LedgerMath.Touched(0, ",", 3));
        }

        // ------------------------------------------------------------------------------------------ D41 document revisions

        [TestMethod]
        public void A_Revision_With_A_Future_Date_Waits_For_Its_Day()
        {
            Assert.IsTrue(RevisionRules.TakesEffectNow(Today, Today), "today");
            Assert.IsTrue(RevisionRules.TakesEffectNow(Today.AddDays(-30), Today), "a past date (the revision was approved late)");
            Assert.IsFalse(RevisionRules.TakesEffectNow(Today.AddDays(1), Today), "tomorrow: the current revision stays in force today");
            Assert.IsTrue(RevisionRules.TakesEffectNow(Today.AddHours(20), Today.AddHours(1)), "time of day ignored");
            Assert.AreEqual(5, (int)ControlledDocumentStatus.Scheduled, "stored value of the new status");
        }

        // ------------------------------------------------------------------------------------------ D14, D15 calibration history

        private static CalibrationEvent Calibration(DateTime date, DateTime? nextDue, bool asLeftPass = true, bool deleted = false)
        {
            var e = new CalibrationEvent { EventDate = date, NextDue = nextDue, AsLeftPass = asLeftPass };
            if (deleted) e.Delete();
            return e;
        }

        [TestMethod]
        public void A_Back_Dated_Record_Is_Judged_By_The_Calibration_That_Covered_That_Day()
        {
            var events = new List<CalibrationEvent>
            {
                Calibration(Today.AddDays(-200), Today.AddDays(165)),
                Calibration(Today, Today.AddDays(365)),
            };
            Assert.IsTrue(CalibrationHistory.WasValidOn(events, Today.AddDays(-100)), "inside the first interval");
            Assert.IsTrue(CalibrationHistory.WasValidOn(events, Today.AddDays(-200)), "on the calibration day");
            Assert.IsTrue(CalibrationHistory.WasValidOn(events, Today.AddDays(165)), "on the due day");
            Assert.IsFalse(CalibrationHistory.WasValidOn(events, Today.AddDays(-201)), "the day before the first calibration");
            Assert.IsTrue(CalibrationHistory.WasValidOn(events, Today.AddDays(200)), "inside the second interval");
            Assert.IsFalse(CalibrationHistory.WasValidOn(events, Today.AddDays(366)), "after the last due day");
            // an instrument calibrated only today was not calibrated three days ago
            Assert.IsFalse(CalibrationHistory.WasValidOn(new[] { Calibration(Today, Today.AddDays(365)) }, Today.AddDays(-3)));
            Assert.IsTrue(CalibrationHistory.WasValidOn(new[] { Calibration(Today, Today.AddDays(365)) }, Today));
        }

        [TestMethod]
        public void Calibration_History_Ignores_Failed_Deleted_And_Open_Ended_Events()
        {
            Assert.IsFalse(CalibrationHistory.WasValidOn(new[] { Calibration(Today.AddDays(-10), Today.AddDays(300), asLeftPass: false) }, Today), "failed as-left");
            Assert.IsFalse(CalibrationHistory.WasValidOn(new[] { Calibration(Today.AddDays(-10), Today.AddDays(300), deleted: true) }, Today), "deleted");
            Assert.IsFalse(CalibrationHistory.WasValidOn(new[] { Calibration(Today.AddDays(-10), null) }, Today), "no next due date");
            Assert.IsFalse(CalibrationHistory.WasValidOn(null, Today));
            Assert.IsFalse(CalibrationHistory.WasValidOn(new List<CalibrationEvent>(), Today));
        }

        [TestMethod]
        public void Calibration_Is_Computed_From_The_Dates_For_Waived_And_Out_Of_Service_Instruments()
        {
            var overdue = new MeasuringEquipment { Code = "INN-IST-9", RequiresCalibration = true, NextDue = Today.AddDays(-1), Status = EquipmentStatus.Calibrated };
            Assert.AreEqual(EquipmentStatus.Overdue, CalibrationRules.Effective(overdue, Today, 30), "the stored status is never trusted");
            StringAssert.Contains(CalibrationRules.CheckUsable(overdue, Today, 30), "INN-IST-9");
            Assert.IsNull(CalibrationRules.CheckUsable(overdue, Today.AddDays(-1), 30), "usable on the due day itself");
            var waived = new MeasuringEquipment { Code = "INN-IST-10", RequiresCalibration = false };
            Assert.AreEqual(EquipmentStatus.NotRequired, CalibrationRules.Effective(waived, Today, 30));
            Assert.IsNull(CalibrationRules.CheckUsable(waived, Today, 30));
            var out_ = new MeasuringEquipment { Code = "INN-IST-11", RequiresCalibration = false, Status = EquipmentStatus.OutOfService, OutOfServiceReason = "dropped" };
            Assert.AreEqual(EquipmentStatus.OutOfService, CalibrationRules.Effective(out_, Today, 30), "out of service wins even without a calibration duty");
            StringAssert.Contains(CalibrationRules.CheckUsable(out_, Today, 30), "dropped");
        }

        // ------------------------------------------------------------------------------------------ D17 inspector qualification, D15 welder continuity

        private static Qualification Cert(QualificationKind kind, DateTime issued, DateTime? expiry = null, bool revoked = false, bool superseded = false) =>
            new Qualification { Kind = kind, IssuedDate = issued, ExpiryDate = expiry, Revoked = revoked, Superseded = superseded, Method = "GTAW" };

        [TestMethod]
        public void Inspector_Competence_Needs_At_Least_One_Valid_Certificate()
        {
            string none = QualificationRules.CheckHeld(new Qualification[0], Today, 6, "بازرس");
            Assert.IsNotNull(none);
            StringAssert.Contains(none, "بازرس");
            Assert.IsNull(QualificationRules.CheckHeld(new[] { Cert(QualificationKind.Inspector, Today.AddDays(-30), Today.AddDays(300)) }, Today, 6, "بازرس"));
            string expired = QualificationRules.CheckHeld(new[] { Cert(QualificationKind.Inspector, Today.AddDays(-800), Today.AddDays(-10)) }, Today, 6, "بازرس");
            Assert.IsNotNull(expired);
            StringAssert.Contains(expired, "منقضی");
            Assert.IsNull(QualificationRules.CheckHeld(new[]
            {
                Cert(QualificationKind.Inspector, Today.AddDays(-800), Today.AddDays(-10)),
                Cert(QualificationKind.Inspector, Today.AddDays(-30), Today.AddDays(300)),
            }, Today, 6, "بازرس"), "an expired old certificate does not matter when a valid one exists");
            string revoked = QualificationRules.CheckHeld(new[] { Cert(QualificationKind.Inspector, Today.AddDays(-30), Today.AddDays(300), revoked: true) }, Today, 6, "بازرس");
            Assert.IsNotNull(revoked);
            StringAssert.Contains(revoked, "ابطال");
            Assert.IsNotNull(QualificationRules.CheckHeld(new[] { Cert(QualificationKind.Inspector, Today.AddDays(-30), Today.AddDays(300), superseded: true) }, Today, 6, "بازرس"), "replaced by a renewal");
            Assert.IsNotNull(QualificationRules.CheckHeld(new[] { Cert(QualificationKind.Inspector, Today.AddDays(5)) }, Today, 6, "بازرس"), "issued in the future");
        }

        [TestMethod]
        public void Weld_Continuity_Chain_Follows_The_Last_Weld_Not_The_Issue_Date()
        {
            // a welder certificate issued 300 days ago; six months of continuity (ASME IX QW-322); every figure is a fixed day
            Qualification q = Cert(QualificationKind.Welder, Today.AddDays(-300));
            DateTime weldA = Today.AddDays(-250), weldB = Today.AddDays(-150);

            // weld A: the first weld, six months after the issue date is day -120, so day -250 is inside
            Assert.AreEqual(QualificationState.Valid, QualificationRules.State(q, weldA, null, 6, 0), "weld 250 days ago, no earlier weld");
            // today, the last weld being A (250 days ago): six months after A is about day -67, so the welder has lapsed
            Assert.AreEqual(QualificationState.Lapsed, QualificationRules.State(q, Today, weldA, 6, 0), "weld today, the last weld 250 days ago");
            // weld B 150 days ago is inside six months of A (about 100 days after it)
            Assert.AreEqual(QualificationState.Valid, QualificationRules.State(q, weldB, weldA, 6, 0), "weld 150 days ago, last weld 250 days ago");
            // today after B: six months after B ends about 33 days from now
            Assert.AreEqual(QualificationState.Valid, QualificationRules.State(q, Today, weldB, 6, 0), "weld today, the last weld 150 days ago");
            // with no weld at all the certificate lapsed six months after it was issued
            Assert.AreEqual(QualificationState.Lapsed, QualificationRules.State(q, Today, null, 6, 0), "never welded since the issue 300 days ago");
            // the same chain for a stricter company setting (3 months): B is already too late after A
            Assert.AreEqual(QualificationState.Lapsed, QualificationRules.State(q, weldB, weldA, 3, 0), "three months of continuity");
            // an NDT certificate has no continuity rule
            Assert.AreEqual(QualificationState.Valid, QualificationRules.State(Cert(QualificationKind.Inspector, Today.AddDays(-300)), Today, null, 6, 0));
        }

        [TestMethod]
        public void A_Weld_Entry_Is_Judged_On_The_Weld_Day_And_On_The_Entry_Day()
        {
            // the service checks both days (D15): valid on the back-dated weld day does not make it valid today
            // issued six months and two days ago: the six months ended on 2026-10-24, so the welder was valid on 2026-10-21 and has lapsed on 2026-10-26
            Qualification q = Cert(QualificationKind.Welder, Today.AddMonths(-6).AddDays(-2));
            DateTime weldDay = Today.AddDays(-5);
            Assert.AreEqual(new DateTime(2026, 10, 24), q.IssuedDate.AddMonths(6), "the continuity period ends on 2026-10-24");
            Assert.AreEqual(QualificationState.Valid, QualificationRules.State(q, weldDay, null, 6, 0), "valid on the weld day, three days before the six months end");
            Assert.AreEqual(QualificationState.Lapsed, QualificationRules.State(q, Today, null, 6, 0), "lapsed today: no weld in six months since the issue");
            // the weld itself, entered after the fact, restarts the clock only when it is accepted; the entry-day check refuses it
        }

        // ------------------------------------------------------------------------------------------ NCR helpers

        [TestMethod]
        public void Ncr_Reinspection_Kinds_And_Concession_Dispositions()
        {
            Assert.IsTrue(NcrReinspection.IsKnown(NcrReinspection.Inspection));
            Assert.IsTrue(NcrReinspection.IsKnown(NcrReinspection.Record));
            Assert.IsTrue(NcrReinspection.IsKnown(NcrReinspection.Report));
            Assert.IsFalse(NcrReinspection.IsKnown("Other"));
            Assert.IsFalse(NcrReinspection.IsKnown(null));
            Assert.AreEqual("QcInspection", NcrReinspection.Inspection, "stored in the database");
            Assert.AreEqual("QcRecord", NcrReinspection.Record);
            Assert.AreEqual("TestReport", NcrReinspection.Report);
        }

        private static string[] RolesOf(Type controller, string action)
        {
            MethodInfo method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, controller.Name + "." + action + " does not exist");
            string roles = method.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Roles).FirstOrDefault(r => !string.IsNullOrEmpty(r));
            Assert.IsNotNull(roles, controller.Name + "." + action + " has no role list");
            return roles.Split(',').Select(r => r.Trim()).ToArray();
        }

        [TestMethod]
        public void The_Role_Lists_Of_The_Quality_Decisions_On_The_Routes_Exclude_The_People_Who_Must_Not_Make_Them()
        {
            Type ncr = typeof(WebApi.Controllers.InventoryQualityController);
            Type items = typeof(WebApi.Controllers.InventoryItemsController);

            // the QC signature of an NCR comes from a QC user or the QA manager (who steps in when the QC people drafted or edited the report); the other signatures stay with their departments
            string[] approvers = RolesOf(ncr, "ApproveNcrAsync");
            CollectionAssert.IsSubsetOf(new[] { "QC", "QA", "ENGINEERING" }, approvers);
            // closing or cancelling an NCR is QC and QA only: PMO reads the reports but does not close them (review L24); who may do which is decided inside with the maker rule
            CollectionAssert.AreEquivalent(new[] { "QC", "QA" }, RolesOf(ncr, "CloseNcrAsync"));
            CollectionAssert.AreEquivalent(new[] { "QC", "QA" }, InventoryRoles.RoleQualitySigner.Split(','));

            // the quality gates of an item, their pending changes, and the second approval: the storekeepers and planners who keep the master data are outside
            string[] gate = { "PMO", "QC", "QA" };
            CollectionAssert.AreEquivalent(gate, RolesOf(items, "SaveQualityAsync"));
            CollectionAssert.AreEquivalent(gate, RolesOf(items, "GetQualityChangesAsync"));
            CollectionAssert.AreEquivalent(new[] { "QA" }, RolesOf(items, "DecideQualityChangeAsync"));
            foreach (string storekeeper in new[] { "WAREHOUSE", "WHSUPERVISOR", "PLANNING", "ENGINEERING" })
            {
                Assert.IsFalse(RolesOf(items, "SaveQualityAsync").Contains(storekeeper), storekeeper + " must not change a quality gate of an item");
                Assert.IsFalse(RolesOf(items, "GetQualityChangesAsync").Contains(storekeeper), storekeeper + " must not read the pending gate changes");
            }
        }

        // ------------------------------------------------------------------------------------------ D27 readers

        [TestMethod]
        public void Quality_Records_Are_Readable_By_The_Roles_That_Work_With_Them()
        {
            string[] readers = QualityRoles.Readers.Split(',');
            foreach (string role in new[] { "AUDITOR", "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "MAINTENANCE", "SERVICE", "PROJECTMANAGER", "DCC", "QA", "QC", "PMO", "CEO" })
                CollectionAssert.Contains(readers, role);
            // writing stays with quality: the new readers did not get any write role
            foreach (string role in new[] { "AUDITOR", "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "MAINTENANCE", "SERVICE", "PROJECTMANAGER", "DCC" })
            {
                Assert.IsFalse(QualityRoles.QualityManagers.Split(',').Contains(role));
                Assert.IsFalse(QualityRoles.CapaClosers.Split(',').Contains(role));
                Assert.IsFalse(QualityRoles.Inspectors.Split(',').Contains(role));
            }
        }

        // ------------------------------------------------------------------------------------------ the nightly job

        [TestMethod]
        public void The_Quality_Job_Runs_At_Four_And_Can_Be_Switched_Off()
        {
            Assert.AreEqual(new DateTime(2026, 10, 26, 4, 0, 0), QualityHostedService.NextRun(new DateTime(2026, 10, 26, 3, 59, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 27, 4, 0, 0), QualityHostedService.NextRun(new DateTime(2026, 10, 26, 4, 0, 0)), "at 04:00 sharp the next run is tomorrow");
            Assert.AreEqual(new DateTime(2026, 10, 27, 4, 0, 0), QualityHostedService.NextRun(new DateTime(2026, 10, 26, 23, 59, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 26, 4, 0, 0), QualityHostedService.NextRun(new DateTime(2026, 10, 26, 0, 0, 0)));

            Assert.IsTrue(QualityHostedService.Enabled(null));
            Assert.IsTrue(QualityHostedService.Enabled(Config(null)));
            Assert.IsTrue(QualityHostedService.Enabled(Config("true")));
            Assert.IsFalse(QualityHostedService.Enabled(Config("false")));
            Assert.IsFalse(QualityHostedService.Enabled(Config("FALSE")));
        }

        private static IConfiguration Config(string value) =>
            new ConfigurationBuilder().AddInMemoryCollection(value == null ? new Dictionary<string, string>() : new Dictionary<string, string> { { "Quality:Jobs", value } }).Build();

        // ------------------------------------------------------------------------------------------ D35 opening import, M16 upload stamp

        /// <summary>A stand-in that fails the moment anything touches it: proves that a service refused a request before it read or wrote any data.</summary>
        public class TouchedProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args) => throw new InvalidOperationException("touched: " + targetMethod.Name);
        }

        private sealed class RoleUser : IInventoryUserContext
        {
            private readonly string[] _roles;
            public RoleUser(params string[] roles) { _roles = roles; }
            public int UserId => 7;
            public bool IsInRole(string role) => role.Split(',').Any(r => _roles.Contains(r.Trim(), StringComparer.OrdinalIgnoreCase));
        }

        [TestMethod]
        public async Task An_Opening_Balance_Without_A_Price_Is_Refused_Before_Any_Data_Is_Read_Unless_The_Finance_Manager_Allows_It()
        {
            IUnitOfWork uow = DispatchProxy.Create<IUnitOfWork, TouchedProxy>();
            IInventoryDocumentService documents = DispatchProxy.Create<IInventoryDocumentService, TouchedProxy>();
            var dto = new OpeningImportDto
            {
                AllowZeroPrice = true,
                Rows = new List<OpeningImportRowDto> { new OpeningImportRowDto { ItemCode = "X-1", WarehouseCode = "W1", Quantity = 5m } },
            };

            // stock without a price is valued at zero in the books: warehouse, planning, QA and PMO cannot accept that on the finance manager's behalf
            foreach (string role in new[] { "WAREHOUSE", "WHSUPERVISOR", "PLANNING", "PMO", "QA", "FINANCIAL" })
            {
                var service = new OpeningStockService(uow, new RoleUser(role), documents);
                var refused = await service.ImportAsync(dto, CancellationToken.None);
                Assert.IsFalse(refused.Success, role + " must not let stock in without a price");
                StringAssert.Contains(refused.Message, "مدیر مالی");
            }

            // the finance manager passes the gate: the service goes on to read the items, which the stand-in does not allow (so the call ends in the stand-in's exception)
            var finance = new OpeningStockService(uow, new RoleUser("FINANCEMANAGER"), documents);
            bool wentOn = false;
            try { await finance.ImportAsync(dto, CancellationToken.None); }
            catch (InvalidOperationException touched) { wentOn = touched.Message.StartsWith("touched", StringComparison.Ordinal); }
            Assert.IsTrue(wentOn, "the finance manager's request must reach the data");

            // without the knowing consent the role does not matter for the gate: the rows are read and each unpriced row is refused later
            dto.AllowZeroPrice = false;
            bool read = false;
            try { await new OpeningStockService(uow, new RoleUser("WAREHOUSE"), documents).ImportAsync(dto, CancellationToken.None); }
            catch (InvalidOperationException touched) { read = touched.Message.StartsWith("touched", StringComparison.Ordinal); }
            Assert.IsTrue(read, "an import that does not ask for zero prices is not stopped by the gate");

            // an empty file is refused first
            var empty = await new OpeningStockService(uow, new RoleUser("FINANCEMANAGER"), documents).ImportAsync(new OpeningImportDto { AllowZeroPrice = true }, CancellationToken.None);
            Assert.IsFalse(empty.Success);
        }

        [TestMethod]
        public void An_Uploaded_File_Carries_The_Upload_Moment_And_The_Uploader()
        {
            DateTimeOffset before = DateTimeOffset.Now;
            var file = new Entities.Model.Document("evidence/a.pdf", "application/pdf");
            DateTimeOffset after = DateTimeOffset.Now;
            Assert.IsTrue(file.RegisterDate >= before && file.RegisterDate <= after, "the upload moment is stamped when the file is created");
            Assert.IsNull(file.UploadedByUserId, "not known until the uploader is set (files uploaded before the stamp keep null)");
            Assert.AreEqual(12, file.SetUploader(12).UploadedByUserId);
            Assert.IsNull(file.SetUploader(null).UploadedByUserId);
            Assert.IsNull(file.SetUploader(0).UploadedByUserId, "an anonymous or unknown user id is not an uploader");
        }
    }
}
