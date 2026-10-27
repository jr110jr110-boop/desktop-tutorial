using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Ceo.Drills;
using ViewModels.NasbNiro.CeoDtos;

namespace MsTests
{
    /// <summary>
    /// The drills of the CEO dashboard that read the database themselves, run on the real SQL Server provider with every command answered as an empty database does (no server is needed):
    /// a query that EF Core cannot translate to SQL Server fails here, where the in-memory provider would accept it. The queries that were moved into the database in review item 8 of part A2
    /// (sums and groups, date filters, the last movement of an item) are checked to be in the SQL text, and what a drill puts in its rows and totals is checked against the privacy filter of the AI tools.
    /// </summary>
    [TestClass]
    public class CeoDrillSqlTests
    {
        private static readonly string[] None = new string[0];

        /// <summary>The drills whose Run needs the database only (no module service), with the parameters that reach the other branches of their queries.</summary>
        private static readonly (string Key, string[] Parameters)[] DatabaseDrills =
        {
            ("fin.payables_by_supplier", None),
            ("fin.revenue_by_month", None),
            ("fin.cost_by_month", None),
            ("fin.vouchers_waiting", None),
            ("sl.customers_top", None),
            ("pc.milestones_next", None),
            ("pc.milestones_next", new[] { "days", "365" }),
            ("inv.items_below_minimum", None),
            ("inv.stock_value_by_warehouse", None),
            ("inv.slow_moving", None),
            ("inv.slow_moving", new[] { "days", "3650" }),
            ("inv.top_movements", None),
            ("inv.top_movements", new[] { "kind", "receipt" }),
            ("inv.top_movements", new[] { "kind", "issue" }),
            ("inv.top_movements", new[] { "from", "2025-10-25", "to", "2026-10-25", "kind", "all" }),
            ("pr.spend_by_supplier", None),
            ("sv.warranty_cost_by_month", None),
            ("sv.survey_by_month", None),
            ("of.attendance_month", None),
            ("of.attendance_month", new[] { "by", "day" }),
            ("of.attendance_month", new[] { "by", "unit" }),
        };

        private static async Task<ResponseDto<DrillTableDto>> Run(ApplicationDbContext context, string role, string key, params string[] pairs)
        {
            var parameters = new Dictionary<string, string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) parameters[pairs[i]] = pairs[i + 1];
            var service = new CeoDrillService(new CeoOneService(typeof(IUnitOfWork), context), new CeoFakeUser(role));
            return await service.DrillAsync(key, null, null, parameters, CancellationToken.None);
        }

        /// <summary>Runs one drill and returns what it sent to the database, in order.</summary>
        private static async Task<List<string>> Sql(string key, params string[] pairs)
        {
            ApplicationDbContext context = CeoOverviewServiceTests.SqlServerContext(out CeoOverviewServiceTests.EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                ResponseDto<DrillTableDto> answer = await Run(context, "CEO", key, pairs);
                Assert.IsTrue(answer.Success, key + ": " + answer.Message);
                return empty.Commands.ToList();
            }
        }

        private static string Label((string Key, string[] Parameters) c) => c.Key + (c.Parameters.Length == 0 ? string.Empty : " " + string.Join("=", c.Parameters));

        private static void Matches(string sql, string pattern, string what) =>
            Assert.IsTrue(Regex.IsMatch(sql, pattern, RegexOptions.CultureInvariant), what + " is not in the query: " + sql);

        /// <summary>Every query that reads the table is an aggregate (a group or a count): none of them brings the rows of the table into memory.</summary>
        private static void AssertAggregates(IEnumerable<string> commands, string table, string what)
        {
            List<string> reads = commands.Where(c => c.Contains("FROM [" + table + "]")).ToList();
            Assert.IsTrue(reads.Count > 0, what + ": nothing read " + table);
            foreach (string sql in reads)
                Assert.IsTrue(sql.Contains("GROUP BY") || Regex.IsMatch(sql, @"^SELECT COUNT\("), what + ": the rows of " + table + " are read one by one: " + sql);
        }

        // ------------------------------------------------------------------------------------------ every database drill translates; nothing private in a row or in the totals

        [TestMethod]
        public async Task The_Database_Drills_Translate_To_SqlServer_And_Their_Rows_And_Totals_Survive_The_Privacy_Filter_Of_The_Tools()
        {
            foreach (string role in new[] { "CEO", "PMO" })
                foreach (var drill in DatabaseDrills)
                {
                    ApplicationDbContext context = CeoOverviewServiceTests.SqlServerContext(out CeoOverviewServiceTests.EmptyDatabase empty, out IServiceScope scope);
                    using (scope)
                    {
                        string name = role + " " + Label(drill);
                        ResponseDto<DrillTableDto> answer = await Run(context, role, drill.Key, drill.Parameters);
                        Assert.IsTrue(answer.Success, name + ": " + answer.Message);
                        DrillTableDto table = answer.Data;
                        Assert.AreEqual(drill.Key, table.Key);
                        Assert.IsTrue(empty.Commands.Count > 0, name + " read nothing from the database");

                        // every key of a row is a column of the drill (or the id and the link of the record), and nothing the privacy filter of the AI tools removes is in a row or in the totals
                        DrillDefinition definition = CeoDrillCatalog.Find(drill.Key);
                        var allowed = definition.Columns.Select(c => c.Key).Concat(new[] { "id", "link" }).ToList();
                        foreach (Dictionary<string, object> row in table.Rows)
                            foreach (string key in row.Keys)
                            {
                                CollectionAssert.Contains(allowed, key, name + ": a row holds a key that is neither a column nor the id or the link");
                                Assert.IsTrue(((JsonObject)AiToolShaper.Clean(new JsonObject { [key] = 1 })).ContainsKey(key), name + ": the key «" + key + "» of a row would be removed from the result for the model");
                            }
                        if (table.Totals != null)
                            foreach (string key in table.Totals.Keys)
                                Assert.IsTrue(((JsonObject)AiToolShaper.Clean(new JsonObject { [key] = 1 })).ContainsKey(key), name + ": the total «" + key + "» would be removed from the result for the model");
                    }
                }
        }

        // ------------------------------------------------------------------------------------------ the sums, the groups and the date filters are in the query (review item 8)

        [TestMethod]
        public async Task The_Supplier_Drills_Sum_And_Group_In_The_Query_With_The_Date_Filter_In_The_Query()
        {
            List<string> payables = await Sql("fin.payables_by_supplier");
            Assert.AreEqual(2, payables.Count, "one query for the approved invoices and one for the payments against an invoice");
            AssertAggregates(payables, "ProcSupplierInvoices", "payables");
            AssertAggregates(payables, "ProcPaymentRequests", "payables");
            foreach (string sql in payables) Matches(sql, @"SUM\(\[\w+\]\.\[(TotalAmount|Amount)\]\)", "the sum of the amounts");
            Matches(payables.Single(s => s.Contains("[ProcPaymentRequests]")), @"\[InvoiceId\] IS NOT NULL", "only the payments against an invoice");

            List<string> spend = await Sql("pr.spend_by_supplier");
            Assert.AreEqual(3, spend.Count, "the ordered value, the approved invoices and the payments, one query each, and nothing else");
            AssertAggregates(spend, "ProcPurchaseOrders", "spend");
            AssertAggregates(spend, "ProcSupplierInvoices", "spend");
            AssertAggregates(spend, "ProcPaymentRequests", "spend");
            Matches(spend.Single(s => s.Contains("[ProcPurchaseOrders]")), @"\[OrderDate\] >= @\w+ AND \[\w+\]\.\[OrderDate\] < @\w+", "the range of the order dates");
            Matches(spend.Single(s => s.Contains("[ProcSupplierInvoices]")), @"\[InvoiceDate\] >= @\w+ AND \[\w+\]\.\[InvoiceDate\] < @\w+", "the range of the invoice dates");
            Matches(spend.Single(s => s.Contains("[ProcPaymentRequests]")), @"\[PaidAt\] >= @\w+ AND \[\w+\]\.\[PaidAt\] < @\w+", "the range of the payment dates");
            foreach (string sql in spend) Matches(sql, @"SUM\(", "the sum of the amounts");
        }

        [TestMethod]
        public async Task The_Slow_Moving_Items_Start_From_The_Stock_And_Find_The_Last_Movement_In_The_Query()
        {
            List<string> slow = await Sql("inv.slow_moving");
            Assert.AreEqual(1, slow.Count, "one query: the items in stock with the date of their last movement");
            Matches(slow[0], @"FROM \[FinItemCosts\]", "the items in stock are the start of the query");
            Matches(slow[0], @"\[Quantity\] > 0\.0", "only items with stock");
            Matches(slow[0], @"SELECT MAX\(\[\w+\]\.\[TransactionDate\]\) FROM \[InvStockTransactions\]", "the date of the last movement is computed by the database");
            Matches(slow[0], @"\[IsReversal\] = CAST\(0 AS bit\)", "a reversal is no movement");
            Assert.IsFalse(Regex.IsMatch(slow[0], @"^SELECT [^()]* FROM \[InvStockTransactions\]"), "the movements are not read one by one");
        }

        [TestMethod]
        public async Task The_Busiest_Items_Are_Grouped_In_The_Query_And_The_Range_Is_At_Most_366_Days()
        {
            foreach (string kind in new[] { "receipt", "issue", "all" })
            {
                List<string> moves = await Sql("inv.top_movements", "kind", kind);
                AssertAggregates(moves, "InvStockTransactions", "top movements " + kind);
                string sql = moves.First(s => s.Contains("FROM [InvStockTransactions]"));
                Matches(sql, @"\[TransactionDate\] >= @\w+ AND \[\w+\]\.\[TransactionDate\] < @\w+", "the range of the dates");
                Matches(sql, @"GROUP BY \[\w+\]\.\[ItemId\], \[\w+\]\.\[DocumentId\]", "the group of the item and the document");
                Matches(sql, @"\[IsReversal\] = CAST\(0 AS bit\)", "a reversal is no movement");
                Matches(sql, @"SUM\(CASE WHEN", "the quantities and the values are summed by the database");
                if (kind != "all") Matches(sql, @"\[Type\] IN \(\d+, \d+\)", "the two document types of the kind");
                else Assert.IsFalse(Regex.IsMatch(sql, @"\[Type\] IN \("), "all kinds: no filter by the two types");
            }

            // the longest range is 366 days (a leap year, 1 January to 31 December): a longer one is refused with a Persian message before anything is read
            ApplicationDbContext context = CeoOverviewServiceTests.SqlServerContext(out CeoOverviewServiceTests.EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                ResponseDto<DrillTableDto> allowed = await Run(context, "CEO", "inv.top_movements", "from", "2024-01-01", "to", "2024-12-31");
                Assert.IsTrue(allowed.Success, allowed.Message);
                int before = empty.Commands.Count;
                ResponseDto<DrillTableDto> refused = await Run(context, "CEO", "inv.top_movements", "from", "2024-01-01", "to", "2025-01-01");
                Assert.IsFalse(refused.Success);
                Assert.AreEqual("بازه‌ی گزارش نباید بیش از 366 روز باشد؛ بازه‌ی کوتاه‌تری بدهید.", refused.Message);
                Assert.AreEqual(before, empty.Commands.Count, "nothing was read for a range that is refused");
            }
        }

        [TestMethod]
        public async Task The_Milestones_And_The_Surveys_Are_Filtered_By_State_And_Date_In_The_Query()
        {
            List<string> milestones = await Sql("pc.milestones_next");
            Assert.AreEqual(1, milestones.Count);
            Matches(milestones[0], @"FROM \[PcPaymentMilestones\]", "the milestones");
            Matches(milestones[0], @"\[Status\] IN \(\d+, \d+\)", "only the planned and the due milestones");
            Matches(milestones[0], @"\[PlannedDate\] < @\w+", "the planned date against the last day of the window");

            List<string> surveys = await Sql("sv.survey_by_month");
            string rows = surveys.Single(s => s.Contains("[AnsweredAt]") && s.Contains("[AverageScore] AS [Score]"));
            Matches(rows, @"\[AnsweredAt\] >= @\w+ AND \[\w+\]\.\[AnsweredAt\] < @\w+", "the range of the answer dates");
            // every other query on the surveys is a count or a sum of the database; the rows themselves are read only for the range
            foreach (string sql in surveys.Where(s => s.Contains("FROM [SlSurveys]")))
                Assert.IsTrue(sql.StartsWith("SELECT COUNT(") || sql.StartsWith("SELECT COALESCE(SUM(") || Regex.IsMatch(sql, @"\[AnsweredAt\] >= @\w+"), "the surveys are read one by one outside the range: " + sql);
        }

        [TestMethod]
        public async Task The_Attendance_Of_A_Day_Or_A_Unit_Is_Counted_In_The_Query_Not_Read_Row_By_Row()
        {
            foreach (string by in new[] { "day", "unit" })
            {
                List<string> attendance = await Sql("of.attendance_month", "by", by);
                AssertAggregates(attendance, "OfAttendanceDays", "attendance by " + by);
                Matches(attendance[0], @"GROUP BY \[\w+\]\.\[Date\]", "the group of the day");
                Matches(attendance[0], @"COUNT\(DISTINCT \(\[\w+\]\.\[EmployeeId\]\)\)", "the number of different people of a day");
                Matches(attendance[0], @"COUNT\(CASE WHEN \[\w+\]\.\[Status\] = \d THEN 1 END\)", "the counts of the states");
                Matches(attendance[0], @"\[Date\] >= @\w+ AND \[\w+\]\.\[Date\] <= @\w+", "the range of the dates");
            }
            List<string> perUnit = await Sql("of.attendance_month", "by", "unit");
            Assert.IsTrue(perUnit.Any(s => Regex.IsMatch(s, @"GROUP BY \[\w+\]\.\[EmployeeId\]")), "a unit is the sum of the counts of its people, which the database groups by person");
        }

        [TestMethod]
        public async Task The_Items_Below_The_Minimum_Leave_Out_The_Scrap_Warehouses_In_The_Query()
        {
            List<string> below = await Sql("inv.items_below_minimum");
            Assert.AreEqual(1, below.Count);
            Matches(below[0], @"\[Kind\] <> " + (int)WarehouseKind.Scrap + @"\b", "the scrap warehouses are left out of the stock on hand, as in the figure of the dashboard");
            Matches(below[0], @"SUM\(\[\w+\]\.\[Quantity\]\)", "the stock on hand is summed by the database");
            Matches(below[0], @"\[MinStock\] > 0\.0", "only items with a minimum");
        }

        // ------------------------------------------------------------------------------------------ the people of the attendance (a role that cannot read people)

        [TestMethod]
        public async Task The_PMO_Gets_The_Attendance_By_Day_And_Unit_But_Not_By_Person()
        {
            ApplicationDbContext context = CeoOverviewServiceTests.SqlServerContext(out CeoOverviewServiceTests.EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                foreach (string by in new[] { "day", "unit" })
                {
                    ResponseDto<DrillTableDto> allowed = await Run(context, "PMO", "of.attendance_month", "by", by);
                    Assert.IsTrue(allowed.Success, by + ": " + allowed.Message);
                }
                int before = empty.Commands.Count;
                ResponseDto<DrillTableDto> person = await Run(context, "PMO", "of.attendance_month", "by", "person");
                Assert.IsFalse(person.Success);
                Assert.AreEqual("نمایش حضور و غیاب به تفکیک پرسنل فقط برای منابع انسانی و مدیرعامل مجاز است؛ به تفکیک روز یا واحد بخواهید.", person.Message);
                Assert.AreEqual(before, empty.Commands.Count, "nothing was read for a request that is refused");
            }
        }
    }
}
