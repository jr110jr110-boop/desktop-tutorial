using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Entities.Model.NasbNiro.Inventory;
using Services.NasbNiro.Inventory;

namespace MsTests
{
    /// <summary>PB5: the pure rules of the warehouse completion package (freight spread, work calendar, consignment levels, stagnant buckets, reorder, import checks, title and unit rules).</summary>
    [TestClass]
    public class Pb5Tests
    {
        private static FreightRules.LineInput L(int id, bool metal, decimal? kg, decimal value) => new FreightRules.LineInput { LineId = id, ItemId = id, IsMetal = metal, WeightKg = kg, Value = value };

        [TestMethod]
        public void Freight_metal_is_spread_by_weight_and_adds_up()
        {
            var shares = FreightRules.Allocate(1000m, new[] { L(1, true, 100, 5), L(2, true, 300, 5) }, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(250m, shares.Single(s => s.LineId == 1).Amount);
            Assert.AreEqual(750m, shares.Single(s => s.LineId == 2).Amount);
            Assert.IsTrue(shares.All(s => s.Basis == FreightBasis.Weight));
        }

        [TestMethod]
        public void Freight_non_metal_is_spread_by_value_and_remainder_is_kept()
        {
            var shares = FreightRules.Allocate(100m, new[] { L(1, false, null, 1), L(2, false, null, 1), L(3, false, null, 1) }, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(100m, shares.Sum(s => s.Amount));
            Assert.IsTrue(shares.All(s => s.Basis == FreightBasis.Value));
            Assert.AreEqual(34m, shares.Single(s => s.LineId == 1).Amount);
        }

        [TestMethod]
        public void Freight_mixed_receipt_splits_by_value_first()
        {
            var shares = FreightRules.Allocate(1000m, new[] { L(1, true, 10, 600), L(2, false, null, 400) }, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(600m, shares.Single(s => s.LineId == 1).Amount);
            Assert.AreEqual(400m, shares.Single(s => s.LineId == 2).Amount);
            Assert.AreEqual(1000m, shares.Sum(s => s.Amount));
        }

        [TestMethod]
        public void Freight_refuses_missing_weight_and_zero_amount()
        {
            Assert.IsNull(FreightRules.Allocate(100m, new[] { L(1, true, null, 5) }, out string e1));
            Assert.IsNotNull(e1);
            Assert.IsNull(FreightRules.Allocate(0m, new[] { L(1, false, null, 5) }, out string e2));
            Assert.IsNotNull(e2);
            Assert.IsNull(FreightRules.Allocate(100m, new[] { L(1, false, null, 0) }, out string e3));
            Assert.IsNotNull(e3);
        }

        [TestMethod]
        public void Freight_weight_units()
        {
            Assert.AreEqual(5m, FreightRules.WeightKg("kg", 5m, null));
            Assert.AreEqual(2000m, FreightRules.WeightKg("ton", 2m, null));
            Assert.AreEqual(30m, FreightRules.WeightKg("عدد", 3m, 10m));
            Assert.IsNull(FreightRules.WeightKg("عدد", 3m, null));
            Assert.IsTrue(FreightRules.IsMetal("01"));
            Assert.IsFalse(FreightRules.IsMetal("02"));
        }

        [TestMethod]
        public void Work_calendar_skips_friday_and_holidays()
        {
            var cal = new WorkCalendar(null, new[] { new DateTime(2026, 3, 18) });
            // Thursday 2026-03-12 -> Friday is skipped: Saturday 13th is the first working day
            Assert.AreEqual(1, cal.Between(new DateTime(2026, 3, 12), new DateTime(2026, 3, 14)));
            Assert.IsFalse(cal.IsWorkingDay(new DateTime(2026, 3, 13)));
            Assert.IsFalse(cal.IsWorkingDay(new DateTime(2026, 3, 18)));
            Assert.AreEqual(new DateTime(2026, 3, 16), cal.AddWorkingDays(new DateTime(2026, 3, 12), 3));
            Assert.AreEqual(0, cal.Between(new DateTime(2026, 3, 14), new DateTime(2026, 3, 14)));
        }

        [TestMethod]
        public void Consignment_levels()
        {
            DateTime today = new DateTime(2026, 5, 1);
            Assert.AreEqual(0, ConsignmentRules.Level(today.AddDays(8), today));
            Assert.AreEqual(1, ConsignmentRules.Level(today.AddDays(7), today));
            Assert.AreEqual(1, ConsignmentRules.Level(today.AddDays(4), today));
            Assert.AreEqual(2, ConsignmentRules.Level(today.AddDays(3), today));
            Assert.AreEqual(2, ConsignmentRules.Level(today, today));
            Assert.AreEqual(3, ConsignmentRules.Level(today.AddDays(-1), today));
            Assert.AreEqual(today.AddDays(30), ConsignmentRules.DefaultDue(today));
        }

        [TestMethod]
        public void Stagnant_buckets()
        {
            Assert.AreEqual(0, StagnantRules.Bucket(59));
            Assert.AreEqual(1, StagnantRules.Bucket(60));
            Assert.AreEqual(2, StagnantRules.Bucket(90));
            Assert.AreEqual(3, StagnantRules.Bucket(180));
            Assert.AreEqual(1, StagnantRules.Bucket(30, 30, 60, 120));
        }

        [TestMethod]
        public void Reorder_policy_group_default_and_override()
        {
            var groups = ReorderRules.Effective(new[]
            {
                new ReorderRules.GroupDefaults { Id = 1, Min = 50, LeadDays = 10 },
                new ReorderRules.GroupDefaults { Id = 2, ParentId = 1, Max = 400 },
            });
            Assert.AreEqual(50m, groups[2].Min);
            Assert.AreEqual(10, groups[2].LeadDays);
            var fromGroup = ReorderRules.For(0, null, null, null, groups[2]);
            Assert.IsTrue(fromGroup.FromGroup);
            Assert.AreEqual(50m, fromGroup.Min);
            var own = ReorderRules.For(20, null, null, 5, groups[2]);
            Assert.IsFalse(own.FromGroup);
            Assert.AreEqual(20m, own.Min);
            Assert.AreEqual(5, own.LeadDays);
        }

        [TestMethod]
        public void Reorder_point_and_suggested_quantity()
        {
            Assert.AreEqual(70m, ReorderRules.ReorderPoint(10m, 2m, 30));
            Assert.AreEqual(10m, ReorderRules.ReorderPoint(10m, 0m, 30));
            Assert.AreEqual(0m, ReorderRules.SuggestedQuantity(70m, 200m, null, 60m, 20m));
            Assert.AreEqual(140m, ReorderRules.SuggestedQuantity(70m, 200m, null, 40m, 20m));
            Assert.AreEqual(100m, ReorderRules.SuggestedQuantity(70m, 200m, 100m, 40m, 0m));
            Assert.AreEqual(30m, ReorderRules.SuggestedQuantity(70m, 80m, 5m, 40m, 0m));
        }

        [TestMethod]
        public void Import_parse_checks_each_row()
        {
            var rows = ExcelImportRules.Parse("receipt", new List<string[]>
            {
                new[] { "ab-1", "۱٬۲۵۰٫۵", "۱۰۰۰", "L1", "H1", "A-01", "ok" },
                new[] { "ab-2", "x", "5" },
                new[] { "ab-3", "0", "5" },
                new[] { "ab-1", "3", "1000", "L1", "", "A-01" },
                new[] { "", "" },
                new[] { "", "4" },
            });
            Assert.AreEqual(5, rows.Count);
            Assert.IsNull(rows[0].Error);
            Assert.AreEqual("AB-1", rows[0].ItemCode);
            Assert.AreEqual(1250.5m, rows[0].Quantity);
            Assert.AreEqual(1000m, rows[0].UnitPrice);
            Assert.AreEqual("L1", rows[0].Lot);
            Assert.AreEqual("H1", rows[0].Heat);
            Assert.IsNotNull(rows[1].Error);
            Assert.IsNotNull(rows[2].Error);
            Assert.IsNotNull(rows[3].Error);
            Assert.IsNotNull(rows[4].Error);
            Assert.AreEqual(2, rows[0].Number);
        }

        [TestMethod]
        public void Import_count_allows_zero_and_issue_does_not()
        {
            Assert.IsNull(ExcelImportRules.Parse("count", new List<string[]> { new[] { "x1", "0" } })[0].Error);
            Assert.IsNotNull(ExcelImportRules.Parse("issue", new List<string[]> { new[] { "x1", "0" } })[0].Error);
            foreach (string kind in new[] { "receipt", "issue", "count", "items", "opening" })
                Assert.IsTrue(ExcelImportRules.Headers[kind].Length >= 5, kind);
        }

        [TestMethod]
        public void Title_rules_exact_near_and_different_sizes()
        {
            Assert.AreEqual(ItemTitleRules.Match.Exact, ItemTitleRules.Compare("پیچ M24", "پيچ  m۲۴"));
            Assert.AreEqual(ItemTitleRules.Match.Exact, ItemTitleRules.Compare("شفت‌ روتور", "شفت روتور"));
            Assert.AreEqual(ItemTitleRules.Match.None, ItemTitleRules.Compare("پیچ M8", "پیچ M10"));
            Assert.AreEqual(ItemTitleRules.Match.Near, ItemTitleRules.Compare("روتور کامل توربین", "روتور کامل توربیین"));
            Assert.AreEqual(ItemTitleRules.Match.Near, ItemTitleRules.Compare("شفت روتور", "روتور شفت"));
            Assert.AreEqual(ItemTitleRules.Match.None, ItemTitleRules.Compare("", "x"));
        }

        [TestMethod]
        public void Unit_conversion_direct_inverse_and_item_override()
        {
            var rows = new List<UnitConversionRules.Row>
            {
                new UnitConversionRules.Row { ItemId = null, From = "شاخه", To = "kg", Factor = 10m },
                new UnitConversionRules.Row { ItemId = 7, From = "شاخه", To = "kg", Factor = 12m },
            };
            Assert.AreEqual(12m, UnitConversionRules.Factor(rows, 7, "شاخه", "KG"));
            Assert.AreEqual(10m, UnitConversionRules.Factor(rows, 8, "شاخه", "kg"));
            Assert.AreEqual(0.1m, UnitConversionRules.Factor(rows, 8, "kg", "شاخه"));
            Assert.AreEqual(1m, UnitConversionRules.Factor(rows, 8, "kg", "kg"));
            Assert.IsNull(UnitConversionRules.Factor(rows, 8, "متر", "kg"));
        }
    }
}
