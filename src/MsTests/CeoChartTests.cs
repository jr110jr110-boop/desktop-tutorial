using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ceo;
using ViewModels.NasbNiro.CeoDtos;

namespace MsTests
{
    /// <summary>
    /// Review item 20: the definitions of the monthly charts, checked against records placed in the last Persian month (and today). Each chart says in its note where it differs from the figure beside it,
    /// or is aligned with it: the cash flow leaves out the transfers between the cash boxes and bank accounts, the stock flow nets the returns to the supplier, the NCR chart leaves out the cancelled reports.
    /// </summary>
    [TestClass]
    public class CeoChartTests
    {
        /// <summary>The position of last month in a chart by month (the window has twelve months and ends with the current one).</summary>
        private const int LastMonth = CeoTrends.MonthCount - 2;

        private const int ThisMonth = CeoTrends.MonthCount - 1;

        private static async Task<CeoOverviewDto> OverviewAsync(ServiceProvider provider, CeoOverviewServiceTests.RecordingLogger<CeoOverviewService> log)
        {
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            return (await CeoOverviewServiceTests.Overview(uow, "CEO", CeoOverviewServiceTests.Dashboard(), new MemoryCache(new MemoryCacheOptions()), log).GetOverviewAsync(true, CancellationToken.None)).Data;
        }

        [TestMethod]
        public async Task The_Cash_Flow_Chart_Counts_Money_Entering_And_Leaving_The_Company_Not_Money_Moving_Between_Its_Cash_Boxes_And_Bank_Accounts()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                DateTime last = CeoHistoryTests.Month(1);
                db.Set<AccountingEvent>().AddRange(
                    CeoHistoryTests.Event(1, last.AddDays(3), AccountingEventStatus.Posted, CeoHistoryTests.Line("BankAccount", debit: 1000), CeoHistoryTests.Line("Revenue", credit: 1000)),     // in 1000
                    CeoHistoryTests.Event(2, last.AddDays(5), AccountingEventStatus.Posted, CeoHistoryTests.Line("CashBox", credit: 400), CeoHistoryTests.Line("Expense", debit: 400)),         // out 400
                    CeoHistoryTests.Event(3, last.AddDays(6), AccountingEventStatus.Posted, CeoHistoryTests.Line("BankAccount", credit: 300), CeoHistoryTests.Line("CashBox", debit: 300)),     // a transfer: neither in nor out
                    CeoHistoryTests.Event(4, last.AddDays(7), AccountingEventStatus.Posted,                                                                                                    // 500 into the bank, 200 of it from a cash box: 300 came from outside
                        CeoHistoryTests.Line("BankAccount", debit: 500), CeoHistoryTests.Line("CashBox", credit: 200), CeoHistoryTests.Line("Customer", credit: 300)),
                    CeoHistoryTests.Event(5, last.AddDays(8), AccountingEventStatus.NotRequired, CeoHistoryTests.Line("BankAccount", debit: 9999)),                                           // not an event of the books
                    CeoHistoryTests.Event(6, DateTime.Today, AccountingEventStatus.Pending, CeoHistoryTests.Line("BankAccount", debit: 50), CeoHistoryTests.Line("Revenue", credit: 50)));      // in 50, today
                await db.SaveChangesAsync();
            }
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();

            CeoOverviewDto overview = await OverviewAsync(provider, log);

            CeoChartDto flow = CeoOverviewServiceTests.ChartOf(overview, "finance", "cashFlow");
            Assert.AreEqual(CeoTrends.MonthCount, flow.Labels.Count);
            CeoChartSeriesDto income = flow.Series.First(s => s.Name == "ورودی"), outgo = flow.Series.First(s => s.Name == "خروجی");
            Assert.AreEqual(1300m, income.Values[LastMonth], "1000 and the 300 that came from outside; the transfer and the 200 that only changed place are not income");
            Assert.AreEqual(400m, outgo.Values[LastMonth], "the 400 paid; the transfer is not an expense");
            Assert.AreEqual(50m, income.Values[ThisMonth], "a pending event counts, as it does in the cash balance");
            Assert.AreEqual(0m, outgo.Values[ThisMonth]);
            Assert.AreEqual(1350m, income.Values.Sum(), "the not-required event is left out");
            Assert.AreEqual(400m, outgo.Values.Sum());
            Assert.IsTrue(flow.Note.Contains("انتقال میان صندوق‌ها و حساب‌های بانکی"), "the note names the rule: " + flow.Note);
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        private static InventoryDocument Document(int id, InventoryDocumentType type, DateTime day) => new InventoryDocument { Id = id, Type = type, Number = "D-" + id, DocumentDate = day, Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1 };

        private static StockTransaction Move(int id, int documentId, decimal amount, DateTime day) => new StockTransaction { Id = id, ItemId = 1, WarehouseId = 1, Quantity = Math.Sign(amount), CostAmount = amount, TransactionDate = day, DocumentId = documentId };

        [TestMethod]
        public async Task The_Stock_Flow_Chart_Nets_Supplier_Returns_From_The_Receipts_And_Production_Returns_From_The_Consumption_And_Ignores_Other_Movements()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                DateTime day = CeoHistoryTests.Month(1).AddDays(2);
                db.Set<InventoryDocument>().AddRange(
                    Document(1, InventoryDocumentType.PurchaseReceipt, day), Document(2, InventoryDocumentType.SupplierReturn, day), Document(3, InventoryDocumentType.Issue, day),
                    Document(4, InventoryDocumentType.ProductionReturn, day), Document(5, InventoryDocumentType.Transfer, day));
                db.Set<StockTransaction>().AddRange(
                    Move(1, 1, 1000m, day),               // a receipt is stored positive
                    Move(2, 2, -200m, day),               // a return to the supplier is stored negative: the receipts of the month are 1000 - 200
                    Move(3, 3, -300m, day),               // an issue to production is stored negative
                    Move(4, 4, 50m, day),                 // a return from production is stored positive: the consumption is 300 - 50
                    Move(5, 5, 500m, day), Move(6, 5, -500m, day),          // a transfer between warehouses is neither
                    Move(7, 1, -100m, DateTime.Today));   // the cancellation of a receipt (a reversal row on the receipt's document) reduces the receipts of the month it is made in
                await db.SaveChangesAsync();
            }
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();

            CeoOverviewDto overview = await OverviewAsync(provider, log);

            CeoChartDto flow = CeoOverviewServiceTests.ChartOf(overview, "inventory", "stockFlowByMonth");
            CeoChartSeriesDto received = flow.Series.First(s => s.Name == "رسید خرید"), consumed = flow.Series.First(s => s.Name == "مصرف تولید");
            Assert.AreEqual(800m, received.Values[LastMonth], "receipts net of the returns to the supplier");
            Assert.AreEqual(250m, consumed.Values[LastMonth], "consumption net of the returns from production");
            Assert.AreEqual(-100m, received.Values[ThisMonth], "a cancellation is deducted in the month it is made");
            Assert.AreEqual(0m, consumed.Values[ThisMonth]);
            Assert.AreEqual(700m, received.Values.Sum());
            Assert.AreEqual(250m, consumed.Values.Sum(), "the transfer is in neither series");
            Assert.IsTrue(flow.Note.Contains("خالص از برگشت به تأمین‌کننده") && flow.Note.Contains("خالص از برگشت از تولید"), flow.Note);
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task The_Ncr_Chart_Counts_Reports_Opened_And_Closed_By_Month_Without_The_Cancelled_And_Deleted_Ones()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                DateTime last = CeoHistoryTests.Month(1);
                db.Set<NonConformance>().AddRange(
                    CeoHistoryTests.Ncr(1, last.AddDays(2), NcrStatus.Open),
                    CeoHistoryTests.Ncr(2, last.AddDays(4), NcrStatus.Closed, new DateTimeOffset(DateTime.Today)),     // reported last month, closed this month
                    CeoHistoryTests.Ncr(3, last.AddDays(5), NcrStatus.Cancelled),                                      // a cancelled report is not counted: the KPI, the donut and the quality page leave it out too
                    CeoHistoryTests.Ncr(4, last.AddDays(6), NcrStatus.Open, deleted: true));
                await db.SaveChangesAsync();
            }
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();

            CeoOverviewDto overview = await OverviewAsync(provider, log);

            CeoChartDto chart = CeoOverviewServiceTests.ChartOf(overview, "quality", "ncrByMonth");
            CeoChartSeriesDto opened = chart.Series.First(s => s.Name == "ثبت‌شده"), closed = chart.Series.First(s => s.Name == "بسته‌شده");
            Assert.AreEqual(2m, opened.Values[LastMonth], "the open one and the closed one; not the cancelled or the deleted one");
            Assert.AreEqual(2m, opened.Values.Sum());
            Assert.AreEqual(1m, closed.Values[ThisMonth], "closed by its closing time");
            Assert.AreEqual(1m, closed.Values.Sum());
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }
    }
}
