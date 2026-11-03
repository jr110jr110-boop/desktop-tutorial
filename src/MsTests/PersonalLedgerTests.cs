using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;

namespace MsTests
{
    /// <summary>
    /// Release R1f: the personal ledger lines (the loan and advance receivable kept per employee). Outside PAYROLL, FINANCEMANAGER and the CEO the lines of an employee detail account are merged into
    /// one anonymous line per voucher, account and side; the totals never change. Pure functions only: no database.
    /// </summary>
    [TestClass]
    public class PersonalLedgerTests
    {
        private sealed class Roles : IInventoryUserContext
        {
            private readonly string[] _roles;
            public Roles(params string[] roles) { _roles = roles; }
            public int UserId => 1;
            public bool IsInRole(string role) => !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _roles.Contains(r.Trim()));
        }

        private static VoucherLineDto Line(int row, int account, string code, decimal debit, decimal credit, string detailCode = null) => new VoucherLineDto
        {
            RowNumber = row, AccountId = account, AccountCode = code, AccountTitle = "t" + code, Debit = debit, Credit = credit,
            Detail1Id = detailCode == null ? (int?)null : 100 + row, Detail1Code = detailCode, Detail1Title = detailCode, Description = "d" + row, SourceRole = "r" + row, WorkOrderId = row == 1 ? 9 : (int?)null,
        };

        [TestMethod]
        public void Only_The_Payroll_Roles_And_The_Ceo_See_Persons()
        {
            foreach (string role in new[] { "PAYROLL", "FINANCEMANAGER", "CEO" }) Assert.IsTrue(PersonalLedger.Sees(new Roles(role)), role);
            foreach (string role in new[] { "FINANCIAL", "TREASURY", "AUDITOR", "PMO", "HR" }) Assert.IsFalse(PersonalLedger.Sees(new Roles(role)), role);
            Assert.IsFalse(PersonalLedger.Sees(new Roles()));
            Assert.IsFalse(PersonalLedger.Sees(null));
        }

        [TestMethod]
        public void Personal_Voucher_Lines_Of_One_Account_And_Side_Become_One_Anonymous_Line()
        {
            // a payroll voucher: the expense, two recoveries of the loan receivable (two persons), one refund on the debit side, the payable
            var lines = new List<VoucherLineDto>
            {
                Line(1, 5, "50101", 5000000m, 0m),
                Line(2, 7, "10401", 0m, 2000000m, "EMP-1"),
                Line(3, 7, "10401", 0m, 1500000m, "EMP-2"),
                Line(4, 7, "10401", 500000m, 0m, "EMP-3"),
                Line(5, 9, "30101", 0m, 2000000m),
            };
            var personal = new List<bool> { false, true, true, true, false };
            List<VoucherLineDto> merged = PersonalLedger.Merge(lines, personal);

            Assert.AreEqual(4, merged.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, merged.Select(l => l.RowNumber).ToArray(), "the rows are numbered again");
            Assert.AreEqual("50101", merged[0].AccountCode);
            Assert.AreEqual(9, merged[0].WorkOrderId, "a line that is not personal is not touched");
            // the credits of the receivable: 2,000,000 + 1,500,000 on one line, at the place of the first
            Assert.AreEqual("10401", merged[1].AccountCode);
            Assert.AreEqual(0m, merged[1].Debit);
            Assert.AreEqual(3500000m, merged[1].Credit);
            // the debit side stays a line of its own
            Assert.AreEqual("10401", merged[2].AccountCode);
            Assert.AreEqual(500000m, merged[2].Debit);
            Assert.AreEqual(0m, merged[2].Credit);
            foreach (VoucherLineDto m in new[] { merged[1], merged[2] })
            {
                Assert.IsNull(m.Detail1Id);
                Assert.IsNull(m.Detail1Code);
                Assert.AreEqual(PersonalLedger.MaskedTitle, m.Detail1Title);
                Assert.IsNull(m.Detail2Id);
                Assert.AreEqual(PersonalLedger.MaskedNote, m.Description);
            }
            Assert.AreEqual("30101", merged[3].AccountCode);
            // the totals do not change
            Assert.AreEqual(lines.Sum(l => l.Debit), merged.Sum(l => l.Debit));
            Assert.AreEqual(lines.Sum(l => l.Credit), merged.Sum(l => l.Credit));
            Assert.AreEqual(5500000m, merged.Sum(l => l.Debit));
            Assert.AreEqual(5500000m, merged.Sum(l => l.Credit));
        }

        [TestMethod]
        public void A_Single_Personal_Line_Loses_Its_Person_But_Keeps_Its_Amount()
        {
            var lines = new List<VoucherLineDto> { Line(1, 5, "50101", 2000000m, 0m), Line(2, 7, "10401", 0m, 2000000m, "EMP-12") };
            List<VoucherLineDto> merged = PersonalLedger.Merge(lines, new List<bool> { false, true });
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(2000000m, merged[1].Credit);
            Assert.IsNull(merged[1].Detail1Code);
            Assert.IsFalse(merged.Any(l => (l.Detail1Code ?? "").StartsWith("EMP-", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void Two_Personal_Accounts_Are_Merged_Separately()
        {
            var lines = new List<VoucherLineDto>
            {
                Line(1, 7, "10401", 0m, 1000000m, "EMP-1"),
                Line(2, 8, "10402", 0m, 400000m, "EMP-1"),
                Line(3, 7, "10401", 0m, 250000m, "EMP-2"),
                Line(4, 5, "50101", 1650000m, 0m),
            };
            List<VoucherLineDto> merged = PersonalLedger.Merge(lines, new List<bool> { true, true, true, false });
            Assert.AreEqual(3, merged.Count);
            Assert.AreEqual(1250000m, merged.Single(l => l.AccountCode == "10401").Credit);
            Assert.AreEqual(400000m, merged.Single(l => l.AccountCode == "10402").Credit);
            Assert.AreEqual(1650000m, merged.Single(l => l.AccountCode == "50101").Debit);
        }

        [TestMethod]
        public void Nothing_Personal_Leaves_The_Lines_As_They_Are()
        {
            var lines = new List<VoucherLineDto> { Line(1, 5, "50101", 10m, 0m, "SUP-1"), Line(2, 9, "30101", 0m, 10m, "SUP-1") };
            List<VoucherLineDto> merged = PersonalLedger.Merge(lines, new List<bool> { false, false });
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("SUP-1", merged[0].Detail1Code);
            Assert.AreEqual("SUP-1", merged[1].Detail1Code);
            Assert.AreEqual(1, merged[0].RowNumber);
            Assert.AreEqual(2, merged[1].RowNumber);
        }

        [TestMethod]
        public void Statement_Rows_Of_One_Voucher_Account_And_Side_Are_Merged_And_The_Total_Stays()
        {
            DateTime day = new DateTime(2026, 10, 1);
            var rows = new List<PersonalLedger.Row>
            {
                new PersonalLedger.Row { Date = day, VoucherId = 10, Number = 5, AccountCode = "10401", DetailTitle = "EMP-1", Description = "x", Credit = 2000000m, Personal = true },
                new PersonalLedger.Row { Date = day, VoucherId = 10, Number = 5, AccountCode = "10401", DetailTitle = "EMP-2", Description = "y", Credit = 1000000m, Personal = true },
                new PersonalLedger.Row { Date = day, VoucherId = 10, Number = 5, AccountCode = "50101", DetailTitle = null, Description = "z", Debit = 3000000m },
                new PersonalLedger.Row { Date = day.AddDays(1), VoucherId = 11, Number = 6, AccountCode = "10401", DetailTitle = "EMP-1", Description = "w", Credit = 500000m, Personal = true },
            };
            List<PersonalLedger.Row> merged = PersonalLedger.MergeRows(rows);
            Assert.AreEqual(3, merged.Count);
            Assert.AreEqual(3000000m, merged[0].Credit);
            Assert.AreEqual(PersonalLedger.MaskedTitle, merged[0].DetailTitle);
            Assert.AreEqual(PersonalLedger.MaskedNote, merged[0].Description);
            Assert.AreEqual(3000000m, merged[1].Debit);
            Assert.IsNull(merged[1].DetailTitle);
            Assert.AreEqual(11, merged[2].VoucherId, "another voucher is never merged into the first");
            Assert.AreEqual(500000m, merged[2].Credit);
            Assert.AreEqual(rows.Sum(r => r.Debit), merged.Sum(r => r.Debit));
            Assert.AreEqual(rows.Sum(r => r.Credit), merged.Sum(r => r.Credit));
        }

        [TestMethod]
        public void The_Audit_Json_Of_A_Voucher_Line_Is_Matched_By_Its_Employee_Detail()
        {
            var employees = new List<int> { 12, 40 };
            Assert.IsTrue(PersonalLedger.MentionsDetail("{\"RowNumber\":1,\"Detail1Id\":12,\"Credit\":2000000.0}", employees));
            Assert.IsTrue(PersonalLedger.MentionsDetail("{\"Detail2Id\":40}", employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail("{\"Detail1Id\":13,\"Detail2Id\":41}", employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail("{\"Detail1Id\":null}", employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail("{\"Detail1Id\":\"12\"}", employees), "only a number is a detail id");
            Assert.IsFalse(PersonalLedger.MentionsDetail("not json", employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail("[12]", employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail(null, employees));
            Assert.IsFalse(PersonalLedger.MentionsDetail("{\"Detail1Id\":12}", new List<int>()));
        }
    }
}
