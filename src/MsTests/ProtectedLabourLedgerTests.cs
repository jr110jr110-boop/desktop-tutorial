using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;

namespace MsTests
{
    /// <summary>A stand-in for the services that the ledger code under test never calls: any call that has no handler fails loudly.</summary>
    public class LedgerStub<T> : DispatchProxy where T : class
    {
        private readonly Dictionary<string, Func<object[], object>> _handlers = new Dictionary<string, Func<object[], object>>();

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (_handlers.TryGetValue(targetMethod.Name, out var handler)) return handler(args);
            throw new NotSupportedException(typeof(T).Name + "." + targetMethod.Name + " is not part of this test");
        }

        public static T Create(params (string Name, Func<object[], object> Handler)[] handlers)
        {
            T proxy = DispatchProxy.Create<T, LedgerStub<T>>();
            foreach (var (name, handler) in handlers) ((LedgerStub<T>)(object)proxy)._handlers[name] = handler;
            return proxy;
        }
    }

    /// <summary>
    /// R1p round 3, C1: the voucher lines flagged as confidential labour (the quarter-end variance line of a pool that is too small to publish) are read by PAYROLL, FINANCEMANAGER and CEO only. A flagged line always comes
    /// with an equal flagged line on the other side, so that the books of a reader who does not see them still balance, and nothing the other readers can open may let them subtract the flagged amount: the budget report,
    /// the consolidation, the comparison with Sepidar, the full Sepidar export, the year-end close, the copy of a voucher and the audit trail are checked here on an in-memory ledger. Every figure is worked out by hand.
    /// </summary>
    [TestClass]
    public class ProtectedLabourLedgerTests
    {
        private sealed class FakeUser : IInventoryUserContext
        {
            private readonly string[] _roles;
            public FakeUser(int id, params string[] roles) { UserId = id; _roles = roles; }
            public int UserId { get; }
            public bool IsInRole(string role) => role.Split(',').Any(r => _roles.Contains(r.Trim(), StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The ledger of Persian year 1404 (2025-03-21 to 2026-03-20) with these final vouchers (the second and third are the labour true-up: a debit of the variance account against a credit of the absorbed-labour account, flagged):
        /// <list type="bullet">
        /// <item>V1 (2025-07-01, Tir) cash 10101 debit 1,000,000 against revenue 60101 credit 1,000,000;</item>
        /// <item>V2 (2025-07-01, Tir) direct labour 70102 debit 300,000 against bank 10102 credit 300,000;</item>
        /// <item>V3 (2025-07-05, Tir, automatic, FLAGGED) variance 70114 debit 70,000 against direct labour 70102 credit 70,000;</item>
        /// <item>V4 (2025-07-05, Tir, automatic, FLAGGED) direct labour 70102 debit 20,000 against bank 10102 credit 20,000 (a pair across a temporary and a permanent account: the year-end rule has to hold for it too);</item>
        /// <item>V5 (2025-08-01, Mordad) cash 10101 debit 5,000 against revenue 60101 credit 5,000.</item>
        /// </list>
        /// </summary>
        private sealed class Ledger : IDisposable
        {
            private readonly ServiceProvider _provider;
            private readonly IServiceScope _scope;
            private int _number;
            public ApplicationDbContext Db { get; }
            public FnFiscalYear Year { get; private set; }
            public Dictionary<string, FnAccount> Accounts { get; private set; }
            public FnPeriod Tir => Year.Periods.Single(p => p.Number == 4);
            public FnPeriod Mordad => Year.Periods.Single(p => p.Number == 5);
            public FnVoucher V1, V2, V3, V4, V5;

            private Ledger()
            {
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddLogging();
                services.Configure<ViewModels.Identity.Settings.SiteSettings>(o => o.CookieOptions = new ViewModels.Identity.Settings.CookieOptions { DistributedSqlServerCacheOptions = new ViewModels.Identity.Settings.DistributedSqlServerCacheOptions { TableName = "AppSqlCache", SchemaName = "dbo" } });
                services.AddHttpContextAccessor();
                string name = "r1p-gl-" + Guid.NewGuid().ToString("N");
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
                _provider = services.BuildServiceProvider();
                _scope = _provider.CreateScope();
                Db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            }

            public void Dispose()
            {
                _scope.Dispose();
                _provider.Dispose();
            }

            public static async Task<Ledger> CreateAsync(bool withVouchers = true)
            {
                var ledger = new Ledger();
                await FinanceSeed.EnsureLegalEntitiesAsync(ledger.Db, default);
                await FinanceSeed.EnsureSettingsAsync(ledger.Db, default);
                await FinanceSeed.EnsureChartAsync(ledger.Db, default);
                await FinanceSeed.EnsureMappingAsync(ledger.Db, default);
                ledger.Year = await FinanceSeed.EnsureFiscalYearAsync(ledger.Db, new DateTime(2025, 6, 1), default);
                ledger.Accounts = await ledger.Db.Set<FnAccount>().ToDictionaryAsync(a => a.Code);
                if (withVouchers)
                {
                    ledger.V1 = await ledger.AddAsync(new DateTime(2025, 7, 1), FnVoucherType.Normal, "فروش نقدی", ("10101", 1000000m, 0m, false), ("60101", 0m, 1000000m, false));
                    ledger.V2 = await ledger.AddAsync(new DateTime(2025, 7, 1), FnVoucherType.Normal, "دستمزد پرداختی", ("70102", 300000m, 0m, false), ("10102", 0m, 300000m, false));
                    ledger.V3 = await ledger.AddAsync(new DateTime(2025, 7, 5), FnVoucherType.Auto, "تعدیل نرخ دستمزد", ("70114", 70000m, 0m, true), ("70102", 0m, 70000m, true));
                    ledger.V4 = await ledger.AddAsync(new DateTime(2025, 7, 5), FnVoucherType.Auto, "تعدیل نرخ دستمزد", ("70102", 20000m, 0m, true), ("10102", 0m, 20000m, true));
                    ledger.V5 = await ledger.AddAsync(new DateTime(2025, 8, 1), FnVoucherType.Normal, "فروش نقدی", ("10101", 5000m, 0m, false), ("60101", 0m, 5000m, false));
                }
                return ledger;
            }

            public async Task<FnVoucher> AddAsync(DateTime date, FnVoucherType type, string description, params (string Code, decimal Debit, decimal Credit, bool Flag)[] lines)
            {
                _number++;
                var voucher = new FnVoucher
                {
                    FiscalYearId = Year.Id, Date = date, Type = type, Status = FnVoucherStatus.Final, Number = _number, TemporaryNumber = _number, Description = description, TotalAmount = lines.Sum(l => l.Debit),
                    CreatedByUserId = 1, CreatedAt = DateTimeOffset.Now, CheckedByUserId = 2, CheckedAt = DateTimeOffset.Now, FinalizedByUserId = 3, FinalizedAt = DateTimeOffset.Now,
                    Lines = lines.Select((l, i) => new FnVoucherLine { RowNumber = i + 1, AccountId = Accounts[l.Code].Id, Debit = l.Debit, Credit = l.Credit, Description = "سطر " + (i + 1), LabourProtected = l.Flag }).ToList(),
                };
                Db.Set<FnVoucher>().Add(voucher);
                await Db.SaveChangesAsync();
                return voucher;
            }

            public async Task CloseAllPeriodsAsync()
            {
                foreach (FnPeriod p in Year.Periods) p.Status = FnPeriodStatus.Closed;
                await Db.SaveChangesAsync();
            }

            public GlService Gl(string role, int userId = 11) => new GlService(Db, new FakeUser(userId, role),
                LedgerStub<IEventPostingService>.Create(),
                LedgerStub<ILedgerReportService>.Create(("ReconcileAsync", a => Task.FromResult(ResponseDtoHelper.Ok(new LedgerReconciliationDto { Clean = true })))),
                LedgerStub<IReceivablesService>.Create(),
                LedgerStub<global::Services.NasbNiro.Office.INotificationService>.Create());

            public SepidarFullExportService Export(string role, int userId = 11) => new SepidarFullExportService(Db, new FakeUser(userId, role));
        }

        // ------------------------------------------------------------------------------------------ year-end close

        private static (decimal Debit, decimal Credit) At(FnVoucher voucher, string code, bool flagged, Dictionary<int, string> codes) =>
            (voucher.Lines.Where(l => codes[l.AccountId] == code && l.LabourProtected == flagged).Sum(l => l.Debit), voucher.Lines.Where(l => codes[l.AccountId] == code && l.LabourProtected == flagged).Sum(l => l.Credit));

        [TestMethod]
        public async Task The_Year_End_Close_Writes_The_Confidential_Part_Of_Each_Account_As_Lines_Of_Its_Own_And_The_Visible_Lines_Carry_Only_The_Visible_Balances()
        {
            using var ledger = await Ledger.CreateAsync();
            await ledger.CloseAllPeriodsAsync();

            var refused = await ledger.Gl("FINANCIAL").CloseYearAsync(ledger.Year.Id, new CloseYearDto(), default);
            Assert.IsFalse(refused.Success, "only the finance manager closes a year");
            var closed = await ledger.Gl("FINANCEMANAGER").CloseYearAsync(ledger.Year.Id, new CloseYearDto(), default);
            Assert.IsTrue(closed.Success, closed.Message);

            Dictionary<int, string> codes = ledger.Accounts.Values.ToDictionary(a => a.Id, a => a.Code);
            List<FnVoucher> created = await ledger.Db.Set<FnVoucher>().Include(v => v.Lines)
                .Where(v => v.Type == FnVoucherType.YearEndTemporaryClose || v.Type == FnVoucherType.Closing || v.Type == FnVoucherType.Opening).OrderBy(v => v.Id).ToListAsync();
            Assert.AreEqual(4, created.Count, "the temporary accounts, the summary account, the closing and the opening voucher");
            FnVoucher temporary = created.Single(v => v.Type == FnVoucherType.YearEndTemporaryClose && v.Description.Contains("بستن حساب‌های موقت"));
            FnVoucher summary = created.Single(v => v.Type == FnVoucherType.YearEndTemporaryClose && v.Description.Contains("انتقال سود"));
            FnVoucher closing = created.Single(v => v.Type == FnVoucherType.Closing);
            FnVoucher opening = created.Single(v => v.Type == FnVoucherType.Opening);

            // every voucher balances twice: its visible lines by themselves and its flagged lines by themselves, so a reader who does not see the flagged lines meets balanced vouchers
            foreach (FnVoucher v in created)
            {
                Assert.AreEqual(v.Lines.Where(l => !l.LabourProtected).Sum(l => l.Debit), v.Lines.Where(l => !l.LabourProtected).Sum(l => l.Credit), "visible lines of " + v.Description);
                Assert.AreEqual(v.Lines.Where(l => l.LabourProtected).Sum(l => l.Debit), v.Lines.Where(l => l.LabourProtected).Sum(l => l.Credit), "flagged lines of " + v.Description);
                Assert.AreEqual(v.Lines.Sum(l => l.Debit), v.TotalAmount);
            }

            // visible balances (debit less credit): cash 10101 +1,005,000; bank 10102 -300,000; revenue 60101 -1,005,000; direct labour 70102 +300,000. Flagged balances: variance 70114 +70,000; direct labour 70102 -70,000 + 20,000 = -50,000; bank 10102 -20,000.
            // (1) the temporary accounts are closed: visible 60101 debit 1,005,000 and 70102 credit 300,000, the difference 705,000 as a credit of the summary account 50901;
            //     flagged 70114 credit 70,000 and 70102 debit 50,000, the difference (debit 50,000 - credit 70,000 = -20,000) as a flagged debit of 50901 of 20,000
            Assert.AreEqual((1005000m, 0m), At(temporary, "60101", false, codes));
            Assert.AreEqual((0m, 300000m), At(temporary, "70102", false, codes));
            Assert.AreEqual((0m, 705000m), At(temporary, "50901", false, codes));
            Assert.AreEqual((0m, 70000m), At(temporary, "70114", true, codes));
            Assert.AreEqual((50000m, 0m), At(temporary, "70102", true, codes));
            Assert.AreEqual((20000m, 0m), At(temporary, "50901", true, codes));
            Assert.AreEqual(6, temporary.Lines.Count);
            // (2) the summary account is moved to the retained earnings: visible debit 705,000 against a credit of 50301; flagged credit 20,000 against a debit of 20,000
            Assert.AreEqual((705000m, 0m), At(summary, "50901", false, codes));
            Assert.AreEqual((0m, 705000m), At(summary, "50301", false, codes));
            Assert.AreEqual((0m, 20000m), At(summary, "50901", true, codes));
            Assert.AreEqual((20000m, 0m), At(summary, "50301", true, codes));
            Assert.AreEqual(4, summary.Lines.Count);
            // (3) the closing voucher of the permanent accounts and (4) the opening voucher of the next year: the unflagged lines carry the visible balances only (bank 10102: 300,000, not 320,000)
            Assert.AreEqual((0m, 1005000m), At(closing, "10101", false, codes));
            Assert.AreEqual((300000m, 0m), At(closing, "10102", false, codes));
            Assert.AreEqual((705000m, 0m), At(closing, "50301", false, codes));
            Assert.AreEqual((20000m, 0m), At(closing, "10102", true, codes));
            Assert.AreEqual((0m, 20000m), At(closing, "50301", true, codes));
            Assert.AreEqual(5, closing.Lines.Count);
            Assert.AreEqual((1005000m, 0m), At(opening, "10101", false, codes));
            Assert.AreEqual((0m, 300000m), At(opening, "10102", false, codes));
            Assert.AreEqual((0m, 705000m), At(opening, "50301", false, codes));
            Assert.AreEqual((0m, 20000m), At(opening, "10102", true, codes));
            Assert.AreEqual((20000m, 0m), At(opening, "50301", true, codes));
            Assert.AreEqual(5, opening.Lines.Count);
            Assert.AreNotEqual(ledger.Year.Id, opening.FiscalYearId, "the opening voucher is in the next year");

            // after the close every account of the year is at zero in both layers: the summary account is the same as it would be without the flagged lines
            var all = await ledger.Db.Set<FnVoucherLine>().Where(l => l.Voucher.FiscalYearId == ledger.Year.Id).ToListAsync();
            foreach (string code in new[] { "10101", "10102", "60101", "70102", "70114", "50901", "50301" })
            {
                int id = ledger.Accounts[code].Id;
                Assert.AreEqual(0m, all.Where(l => l.AccountId == id && !l.LabourProtected).Sum(l => l.Debit - l.Credit), code + " visible");
                Assert.AreEqual(0m, all.Where(l => l.AccountId == id && l.LabourProtected).Sum(l => l.Debit - l.Credit), code + " flagged");
            }

            // a reader who does not see the flagged lines gets vouchers with the total of the visible debits only
            // (the closing voucher: visible debits 300,000 + 705,000 = 1,005,000 against the credit of cash; the flagged debit of bank 20,000 comes on top for the readers of the flagged lines)
            var seen = (await ledger.Gl("FINANCIAL").GetVoucherAsync(closing.Id, default)).Data;
            Assert.AreEqual(1005000m, seen.TotalAmount);
            Assert.AreEqual(3, seen.Lines.Count);
            Assert.IsTrue(seen.LabourHidden);
            var full = (await ledger.Gl("FINANCEMANAGER").GetVoucherAsync(closing.Id, default)).Data;
            Assert.AreEqual(1025000m, full.TotalAmount);
            Assert.AreEqual(5, full.Lines.Count);
        }

        [TestMethod]
        public async Task The_Year_End_Close_Of_A_Ledger_Without_Confidential_Lines_Is_As_It_Was()
        {
            using var ledger = await Ledger.CreateAsync(withVouchers: false);
            await ledger.AddAsync(new DateTime(2025, 7, 1), FnVoucherType.Normal, "فروش", ("10101", 1000m, 0m, false), ("60101", 0m, 1000m, false));
            await ledger.AddAsync(new DateTime(2025, 7, 2), FnVoucherType.Normal, "هزینه", ("70102", 400m, 0m, false), ("10101", 0m, 400m, false));
            await ledger.CloseAllPeriodsAsync();
            var closed = await ledger.Gl("FINANCEMANAGER").CloseYearAsync(ledger.Year.Id, new CloseYearDto(), default);
            Assert.IsTrue(closed.Success, closed.Message);
            var created = await ledger.Db.Set<FnVoucher>().Include(v => v.Lines).Where(v => v.Type == FnVoucherType.YearEndTemporaryClose || v.Type == FnVoucherType.Closing || v.Type == FnVoucherType.Opening).ToListAsync();
            Assert.AreEqual(4, created.Count);
            Assert.IsTrue(created.SelectMany(v => v.Lines).All(l => !l.LabourProtected), "no flagged line when there is nothing to protect");
            // profit 1,000 - 400 = 600 to the retained earnings; the cash that is left (1,000 - 400 = 600) is closed against it
            FnVoucher closing = created.Single(v => v.Type == FnVoucherType.Closing);
            Assert.AreEqual(2, closing.Lines.Count, "cash and retained earnings");
            Assert.AreEqual(600m, closing.TotalAmount, "debit of the retained earnings 600 against the credit of cash 600");
        }

        // ------------------------------------------------------------------------------------------ copy

        [TestMethod]
        public async Task A_Voucher_With_A_Confidential_Line_Is_Copied_Only_By_The_Readers_Of_The_Line()
        {
            using var ledger = await Ledger.CreateAsync();
            DateTime date = new DateTime(2026, 10, 1);   // 1405/07/09, an open year

            var refused = await ledger.Gl("FINANCIAL").CopyVoucherAsync(new VoucherIdDto { Id = ledger.V3.Id, Date = date }, default);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "سطر محرمانه");
            Assert.AreEqual(5, await ledger.Db.Set<FnVoucher>().CountAsync(), "nothing was copied");

            // the voucher without such a line is copied as before
            var plain = await ledger.Gl("FINANCIAL").CopyVoucherAsync(new VoucherIdDto { Id = ledger.V1.Id, Date = date }, default);
            Assert.IsTrue(plain.Success, plain.Message);
            Assert.AreEqual(1000000m, plain.Data.TotalAmount);

            // and the finance manager, who reads the lines, may copy it (the copy keeps the flag)
            var copied = await ledger.Gl("FINANCEMANAGER").CopyVoucherAsync(new VoucherIdDto { Id = ledger.V3.Id, Date = date }, default);
            Assert.IsTrue(copied.Success, copied.Message);
            Assert.AreEqual(70000m, copied.Data.TotalAmount);
            var copy = await ledger.Db.Set<FnVoucher>().Include(v => v.Lines).SingleAsync(v => v.Id == copied.Data.Id);
            Assert.IsTrue(copy.Lines.All(l => l.LabourProtected));
        }

        // ------------------------------------------------------------------------------------------ the full Sepidar export

        [TestMethod]
        public async Task The_Full_Sepidar_Export_Preview_List_And_File_Of_A_Period_With_Confidential_Lines_Are_For_The_Readers_Of_The_Lines_Only()
        {
            using var ledger = await Ledger.CreateAsync();
            await ledger.CloseAllPeriodsAsync();
            var tirExport = new FnVoucherExport
            {
                Number = "EXP-1404-0001", LegalEntityId = 1, PeriodId = ledger.Tir.Id, Status = FnExportStatus.Active, Vouchers = 4, Rows = 8, TotalDebit = 1390000m, TotalCredit = 1390000m, Sha256 = "x1", Content = "a,b\r\n1,2\r\n",
                ExportedByUserId = 1, ExportedAt = DateTimeOffset.Now,
            };
            var mordadExport = new FnVoucherExport
            {
                Number = "EXP-1404-0002", LegalEntityId = 1, PeriodId = ledger.Mordad.Id, Status = FnExportStatus.Active, Vouchers = 1, Rows = 2, TotalDebit = 5000m, TotalCredit = 5000m, Sha256 = "x2", Content = "c,d\r\n3,4\r\n",
                ExportedByUserId = 1, ExportedAt = DateTimeOffset.Now,
            };
            ledger.Db.Set<FnVoucherExport>().AddRange(tirExport, mordadExport);
            await ledger.Db.SaveChangesAsync();

            foreach (string role in new[] { "FINANCIAL", "AUDITOR", "TREASURY", "PMO" })
            {
                var export = ledger.Export(role);
                // Tir holds the flagged lines: no preview, no file, no totals
                var preview = await export.PreviewAsync(ledger.Tir.Id, default);
                Assert.IsFalse(preview.Success, role);
                StringAssert.Contains(preview.Message, ledger.Tir.Title);
                StringAssert.Contains(preview.Message, "محرمانه");
                var file = await export.DownloadAsync(tirExport.Id, default);
                Assert.IsFalse(file.Success, role);
                StringAssert.Contains(file.Message, ledger.Tir.Title);
                var list = (await export.GetExportsAsync(null, default)).Data;
                var hiddenRow = list.Single(e => e.PeriodId == ledger.Tir.Id);
                Assert.AreEqual((true, 0, 0, 0m, 0m), (hiddenRow.LabourHidden, hiddenRow.Vouchers, hiddenRow.Rows, hiddenRow.TotalDebit, hiddenRow.TotalCredit), role);
                // Mordad has none: everything as before
                var plain = await export.PreviewAsync(ledger.Mordad.Id, default);
                Assert.IsTrue(plain.Success, role + " " + plain.Message);
                Assert.AreEqual((1, 2, 5000m, 5000m), (plain.Data.Vouchers, plain.Data.Rows, plain.Data.TotalDebit, plain.Data.TotalCredit), role);
                Assert.IsTrue((await export.DownloadAsync(mordadExport.Id, default)).Success, role);
                var visibleRow = list.Single(e => e.PeriodId == ledger.Mordad.Id);
                Assert.AreEqual((false, 1, 2, 5000m), (visibleRow.LabourHidden, visibleRow.Vouchers, visibleRow.Rows, visibleRow.TotalDebit), role);
            }

            foreach (string role in new[] { "FINANCEMANAGER", "CEO" })
            {
                var export = ledger.Export(role);
                var preview = await export.PreviewAsync(ledger.Tir.Id, default);
                Assert.IsTrue(preview.Success, role + " " + preview.Message);
                // Tir: V1 and V2 (2 lines each), V3 and V4 (2 lines each): 4 vouchers, 8 lines; the debits 1,000,000 + 300,000 + 70,000 + 20,000
                Assert.AreEqual((4, 8, 1390000m, 1390000m), (preview.Data.Vouchers, preview.Data.Rows, preview.Data.TotalDebit, preview.Data.TotalCredit), role);
                var file = await export.DownloadAsync(tirExport.Id, default);
                Assert.IsTrue(file.Success, role + " " + file.Message);
                Assert.AreEqual((byte)0xEF, file.Data[0], "UTF-8 with the byte order mark");
                var row = (await export.GetExportsAsync(null, default)).Data.Single(e => e.PeriodId == ledger.Tir.Id);
                Assert.AreEqual((false, 4, 8, 1390000m), (row.LabourHidden, row.Vouchers, row.Rows, row.TotalDebit), role);
            }
        }

        // ------------------------------------------------------------------------------------------ reports that sum the lines

        [TestMethod]
        public async Task The_Budget_Report_And_The_Consolidation_Sum_The_Confidential_Lines_Only_For_The_Readers_Of_The_Lines()
        {
            using var ledger = await Ledger.CreateAsync();
            var budget = new FnBudget
            {
                Number = "B-1404-1", LegalEntityId = 1, FiscalYearId = ledger.Year.Id, Version = 1, Kind = FnBudgetKind.Annual, Status = FnBudgetStatus.Approved, Title = "بودجه ۱۴۰۴", CreatedByUserId = 1, CreatedAt = DateTimeOffset.Now,
                Lines = new List<FnBudgetLine>
                {
                    new FnBudgetLine { AccountId = ledger.Accounts["70102"].Id, Period = 4, Amount = 400000m },
                    new FnBudgetLine { AccountId = ledger.Accounts["70114"].Id, Period = 4, Amount = 100000m },
                },
            };
            ledger.Db.Set<FnBudget>().Add(budget);
            await ledger.Db.SaveChangesAsync();

            // actual of direct labour 70102: debit 300,000 (V2) and 20,000 (V4, flagged) less credit 70,000 (V3, flagged); of the variance account 70114: debit 70,000 (V3, flagged); revenue 60101 (not budgeted) 1,005,000
            var query = new FnBudgetReportQuery { BudgetId = budget.Id };
            foreach (string role in new[] { "FINANCEMANAGER", "CEO" })
            {
                var report = (await ledger.Gl(role).ReportAsync(query, default)).Data;
                Assert.AreEqual(250000m, report.Rows.Single(r => r.Key == "70102").Actual, role);
                Assert.AreEqual(70000m, report.Rows.Single(r => r.Key == "70114").Actual, role);
                Assert.AreEqual(320000m, report.TotalActual, role);
                Assert.AreEqual(1005000m, report.UnbudgetedActual, role);
            }
            foreach (string role in new[] { "FINANCIAL", "TREASURY", "AUDITOR", "PMO" })
            {
                var report = (await ledger.Gl(role).ReportAsync(query, default)).Data;
                Assert.AreEqual(300000m, report.Rows.Single(r => r.Key == "70102").Actual, role);
                Assert.AreEqual(0m, report.Rows.Single(r => r.Key == "70114").Actual, role);
                Assert.AreEqual(300000m, report.TotalActual, role);
                Assert.AreEqual(1005000m, report.UnbudgetedActual, role);
            }

            // consolidation of the year, one company, one currency: the amounts are debit less credit per account
            var range = new ConsolidationQuery { From = new DateTime(2025, 3, 21), To = new DateTime(2026, 3, 20) };
            var manager = (await ledger.Gl("FINANCEMANAGER").ConsolidationAsync(range, default)).Data;
            Assert.AreEqual(250000m, manager.Rows.Single(r => r.AccountCode == "70102").Total);
            Assert.AreEqual(70000m, manager.Rows.Single(r => r.AccountCode == "70114").Total);
            Assert.AreEqual(-320000m, manager.Rows.Single(r => r.AccountCode == "10102").Total);
            var accountant = (await ledger.Gl("FINANCIAL").ConsolidationAsync(range, default)).Data;
            Assert.AreEqual(300000m, accountant.Rows.Single(r => r.AccountCode == "70102").Total);
            Assert.IsFalse(accountant.Rows.Any(r => r.AccountCode == "70114"), "the account that holds only flagged lines does not appear");
            Assert.AreEqual(-300000m, accountant.Rows.Single(r => r.AccountCode == "10102").Total);
            Assert.AreEqual(0m, accountant.TotalBefore, "the books of the reader balance without the flagged lines");
            Assert.AreEqual(0m, manager.TotalBefore);
        }

        [TestMethod]
        public async Task The_Comparison_With_Sepidar_Is_Made_From_The_Visible_Lines_Whoever_Runs_It_And_Says_That_Confidential_Lines_Exist()
        {
            using var ledger = await Ledger.CreateAsync();
            await ledger.CloseAllPeriodsAsync();
            // the Sepidar side holds the whole ledger of Tir, flagged lines included
            var rows = new List<ParallelRowDto>
            {
                new ParallelRowDto { AccountCode = "10101", Debit = 1000000m }, new ParallelRowDto { AccountCode = "60101", Credit = 1000000m },
                new ParallelRowDto { AccountCode = "70102", Debit = 320000m, Credit = 70000m }, new ParallelRowDto { AccountCode = "70114", Debit = 70000m },
                new ParallelRowDto { AccountCode = "10102", Credit = 320000m },
            };
            foreach (string role in new[] { "FINANCIAL", "FINANCEMANAGER" })
            {
                var run = (await ledger.Gl(role).CompareAsync(new ParallelCompareDto { PeriodId = ledger.Tir.Id, Rows = rows }, default)).Data;
                // cash and revenue match; direct labour and bank differ (the PMIS side is 300,000 debit and 300,000 credit); the variance account exists only at Sepidar
                Assert.AreEqual((5, 2, 2, 0, 1), (run.Rows, run.Matched, run.Differs, run.PmisOnly, run.SepidarOnly), role);
                var labour = run.Differences.Single(d => d.AccountCode == "70102");
                Assert.AreEqual((300000m, 0m, 320000m, 70000m), (labour.PmisDebit, labour.PmisCredit, labour.SepidarDebit, labour.SepidarCredit), role);
                var bank = run.Differences.Single(d => d.AccountCode == "10102");
                Assert.AreEqual((0m, 300000m), (bank.PmisDebit, bank.PmisCredit), role);
                Assert.IsTrue(run.LabourHidden, role);
            }
            // a period without such lines carries no remark
            var plain = (await ledger.Gl("FINANCEMANAGER").CompareAsync(new ParallelCompareDto { PeriodId = ledger.Mordad.Id, Rows = new List<ParallelRowDto> { new ParallelRowDto { AccountCode = "10101", Debit = 5000m }, new ParallelRowDto { AccountCode = "60101", Credit = 5000m } } }, default)).Data;
            Assert.AreEqual((2, 2), (plain.Rows, plain.Matched));
            Assert.IsFalse(plain.LabourHidden);
        }

        // ------------------------------------------------------------------------------------------ the audit trail

        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

        [TestMethod]
        public async Task The_Audit_Trail_Never_Contains_The_Amounts_Of_A_Confidential_Line_Or_The_Total_Of_A_Voucher_That_Holds_One()
        {
            using var ledger = await Ledger.CreateAsync();
            const string hidden = "محرمانه (حقوق)";
            int baseline = await ledger.Db.Set<FnAuditLog>().MaxAsync(l => (int?)l.Id) ?? 0;   // the trail is append-only: the rows the seed wrote are not the subject here and are left out by their number

            // a change of a flagged line (the debit of V3) and of a plain line (the debit of V2)
            FnVoucherLine flagged = await ledger.Db.Set<FnVoucherLine>().SingleAsync(l => l.VoucherId == ledger.V3.Id && l.Debit > 0);
            FnVoucherLine plain = await ledger.Db.Set<FnVoucherLine>().SingleAsync(l => l.VoucherId == ledger.V2.Id && l.Debit > 0);
            flagged.Debit = 71000m;
            flagged.Description = "اصلاح";
            plain.Debit = 310000m;
            await ledger.Db.SaveChangesAsync();
            var logs = await ledger.Db.Set<FnAuditLog>().Where(l => l.Id > baseline && l.EntityType == nameof(FnVoucherLine)).ToListAsync();
            Assert.AreEqual(2, logs.Count);
            var flaggedLog = logs.Single(l => l.EntityId == flagged.Id);
            Assert.AreEqual(hidden, Json(flaggedLog.BeforeJson).GetProperty("Debit").GetString());
            Assert.AreEqual(hidden, Json(flaggedLog.AfterJson).GetProperty("Debit").GetString());
            Assert.AreEqual("اصلاح", Json(flaggedLog.AfterJson).GetProperty("Description").GetString(), "what is not an amount is written as before");
            Assert.IsFalse(flaggedLog.BeforeJson.Contains("70000") || flaggedLog.AfterJson.Contains("71000"), "the amount is nowhere in the row");
            var plainLog = logs.Single(l => l.EntityId == plain.Id);
            Assert.AreEqual(300000m, Json(plainLog.BeforeJson).GetProperty("Debit").GetDecimal(), "a plain line keeps its trail");
            Assert.AreEqual(310000m, Json(plainLog.AfterJson).GetProperty("Debit").GetDecimal());

            // a flagged line that is deleted
            FnVoucherLine gone = await ledger.Db.Set<FnVoucherLine>().SingleAsync(l => l.VoucherId == ledger.V4.Id && l.Credit > 0);
            ledger.Db.Set<FnVoucherLine>().Remove(gone);
            await ledger.Db.SaveChangesAsync();
            var deleted = await ledger.Db.Set<FnAuditLog>().SingleAsync(l => l.Id > baseline && l.EntityType == nameof(FnVoucherLine) && l.Action == "Deleted");
            Assert.AreEqual(hidden, Json(deleted.BeforeJson).GetProperty("Credit").GetString());
            Assert.IsFalse(deleted.BeforeJson.Contains("20000"));

            // a voucher that holds a flagged line (a reversal, here) is added with its total hidden; a voucher without one keeps it
            await ledger.AddAsync(new DateTime(2025, 7, 10), FnVoucherType.Reversal, "ابطال", ("70102", 70000m, 0m, true), ("70114", 0m, 70000m, true));
            await ledger.AddAsync(new DateTime(2025, 7, 10), FnVoucherType.Reversal, "ابطال", ("10101", 9000m, 0m, false), ("60101", 0m, 9000m, false));
            var vouchers = await ledger.Db.Set<FnAuditLog>().Where(l => l.Id > baseline && l.EntityType == nameof(FnVoucher) && l.Action == "Added").OrderBy(l => l.Id).ToListAsync();
            Assert.AreEqual(2, vouchers.Count);
            Assert.AreEqual(hidden, Json(vouchers[0].AfterJson).GetProperty("TotalAmount").GetString());
            Assert.AreEqual(9000m, Json(vouchers[1].AfterJson).GetProperty("TotalAmount").GetDecimal());
        }

        [TestMethod]
        public async Task The_Cost_Report_Keeps_A_Protected_Company_Line_As_A_Row_Of_Its_Own_So_That_The_Published_Variances_Stay_Readable()
        {
            using var ledger = await Ledger.CreateAsync(withVouchers: false);
            DateTimeOffset now = DateTimeOffset.Now;
            CostEntry Entry(DateTime date, CostKind kind, decimal amount, bool flagged, int? workOrderId, string number) =>
                new CostEntry { EntryDate = date, Kind = kind, WorkOrderId = workOrderId, Amount = amount, LabourProtected = flagged, SourceType = "Test", SourceId = 1, SourceNumber = number, Description = "x", CreatedAt = now, CreatedByUserId = 1 };
            ledger.Db.Set<CostEntry>().AddRange(
                // the published variance of one month and the protected quarter-end variance of a later one: both without contract, work order and cost centre
                Entry(new DateTime(2026, 8, 22), CostKind.Labour, 3400000m, false, null, "MLM-140505"),
                Entry(new DateTime(2026, 9, 22), CostKind.Labour, -50000m, true, null, "MLM-140506"),
                // a work order of the earlier release: one protected labour entry among the others hides the labour of the whole row
                Entry(new DateTime(2026, 8, 6), CostKind.Labour, 100000m, true, 5, "MCR-1"),
                Entry(new DateTime(2026, 8, 7), CostKind.Labour, 50000m, false, 5, "MCR-2"),
                Entry(new DateTime(2026, 8, 7), CostKind.Material, 20000m, false, 5, "DOC-1"));
            await ledger.Db.SaveChangesAsync();

            IReadOnlyList<WorkOrderCostDto> RowsOf(string role) =>
                new FinanceLinkService(ledger.Db, LedgerStub<ICostingService>.Create(), new FakeUser(7, role)).GetWorkOrderCostsAsync(null, default).GetAwaiter().GetResult().Data;

            // the accountant: the published variance is a readable row, the protected one a hidden row of its own, the work order one hidden row
            var accountant = RowsOf("FINANCIAL");
            Assert.AreEqual(3, accountant.Count);
            var published = accountant.Single(r => r.WorkOrderId == null && !r.LabourHidden);
            Assert.AreEqual(3400000m, published.Labour);
            Assert.AreEqual(3400000m, published.Total);
            var hiddenCompany = accountant.Single(r => r.WorkOrderId == null && r.LabourHidden);
            Assert.IsNull(hiddenCompany.Labour);
            Assert.IsNull(hiddenCompany.Total);
            var hiddenOrder = accountant.Single(r => r.WorkOrderId == 5);
            Assert.IsTrue(hiddenOrder.LabourHidden);
            Assert.IsNull(hiddenOrder.Labour);
            Assert.IsNull(hiddenOrder.Total);
            Assert.AreEqual(20000m, hiddenOrder.Material, "what is not labour stays");

            // payroll, the finance manager and the CEO read every amount
            foreach (string role in new[] { "PAYROLL", "FINANCEMANAGER", "CEO" })
            {
                var rows = RowsOf(role);
                Assert.AreEqual(3, rows.Count, role);
                Assert.IsFalse(rows.Any(r => r.LabourHidden), role);
                CollectionAssert.AreEquivalent(new[] { 3400000m, -50000m }, rows.Where(r => r.WorkOrderId == null).Select(r => r.Labour.Value).ToArray(), role);
                var order = rows.Single(r => r.WorkOrderId == 5);
                Assert.AreEqual(150000m, order.Labour, role);
                Assert.AreEqual(170000m, order.Total, role);
            }
        }
    }
}
