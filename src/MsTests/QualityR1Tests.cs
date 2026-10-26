using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;

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
            // the back-date window may be zero (no late entry at all) but not negative or a year and a day
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 0));
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 365));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", 366));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.MaxBackdateDays, "x", -1));
            // the CAPA effectiveness wait may be zero (the check at once) but the NCR due days must be at least one
            Assert.IsNull(QualitySettingRules.Check(QualitySettingKeys.EffectivenessWaitDays, "x", 0));
            Assert.IsNotNull(QualitySettingRules.Check(QualitySettingKeys.NcrMinorDays, "x", 0));
            Assert.AreEqual("تنظیم نامعتبر است.", QualitySettingRules.Check("NotASetting", "x", 1));
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
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, false, true), "a delivery of a work order's product");
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, false, false), "a delivery that names neither: the gate asks for the work order");
            Assert.IsFalse(ReleaseCoverage.GateApplies(true, false, false), "spare parts against a sales order: the fulfilment check of the sales order applies");
            Assert.IsFalse(ReleaseCoverage.GateApplies(false, true, false), "spare parts against a service order with no work order");
            Assert.IsTrue(ReleaseCoverage.GateApplies(false, true, true), "a service order that names a work order cannot be used to skip the release note");
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

        // ------------------------------------------------------------------------------------------ D13, D20, D21 CAPA timing

        [TestMethod]
        public void Capa_Effectiveness_Cannot_Be_Judged_Before_The_Wait_Has_Passed()
        {
            Assert.AreEqual(new DateTime(2026, 10, 31), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1, 10, 30, 0), 30));
            Assert.AreEqual(new DateTime(2026, 10, 1), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1, 23, 59, 0), 0), "a wait of zero allows the check the same day");
            Assert.AreEqual(new DateTime(2027, 10, 1), CapaRules.EffectivenessEarliest(new DateTime(2026, 10, 1), 365));
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

        [TestMethod]
        public void NCR_QC_Signature_Is_Given_By_QC_Or_QA_And_Cancel_Is_For_QC_Or_QA()
        {
            CollectionAssert.AreEquivalent(new[] { "QC", "QA" }, InventoryRoles.RoleQualitySigner.Split(','));
            CollectionAssert.AreEquivalent(new[] { "PMO", "QC", "QA" }, InventoryRoles.QcOrQa.Split(','));
            Assert.IsTrue(InventoryRoles.NcrApprovers.Split(',').Contains("QA"), "QA can sign when the QC people drafted or edited the report");
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

        // ------------------------------------------------------------------------------------------ D35 opening import

        [TestMethod]
        public void Only_The_Finance_Manager_May_Let_An_Opening_Balance_Come_In_Without_A_Price()
        {
            Assert.AreEqual("FINANCEMANAGER", OpeningStockService.OpeningZeroPriceRole);
        }
    }
}
