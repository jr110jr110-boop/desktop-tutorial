using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Ceo;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Ceo;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;
using ViewModels.NasbNiro.SalesDtos;

namespace MsTests
{
    /// <summary>
    /// The CEO overview and its snapshot service run against real providers without a database server.
    /// 1. The SQL Server provider with every command answered as an empty database answers it: a query that EF Core cannot translate to SQL Server fails here
    ///    (the in-memory provider would accept it), and the shape of the overview on an empty company is checked.
    /// 2. The in-memory provider with a few records: the definitions the overview makes itself (overdue payables, sales backlog, stock value by warehouse) and the snapshot rows.
    /// The tests run one after the other (the SQL Server fixture is shared), which is the default of this assembly.
    /// </summary>
    [TestClass]
    public class CeoOverviewServiceTests
    {
        // ==================================================================================== fakes

        /// <summary>Answers each method of an interface with a prepared delegate named like the method; any other method is not part of the test.</summary>
        public class Stub : DispatchProxy
        {
            public Dictionary<string, Delegate> Handlers { get; set; } = new Dictionary<string, Delegate>();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (!Handlers.TryGetValue(targetMethod.Name, out Delegate handler)) throw new NotSupportedException(targetMethod.DeclaringType?.Name + "." + targetMethod.Name + " is not part of this test.");
                try { return handler.DynamicInvoke(args); }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
            }
        }

        private static T Proxy<T>(params (string Name, Delegate Handler)[] handlers) where T : class
        {
            T proxy = DispatchProxy.Create<T, Stub>();
            foreach ((string name, Delegate handler) in handlers) ((Stub)(object)proxy).Handlers[name] = handler;
            return proxy;
        }

        /// <summary>The signed-in user with the given roles (comma separated).</summary>
        private static IInventoryUserContext User(string roles) => Proxy<IInventoryUserContext>(
            ("get_UserId", (Func<int>)(() => 7)),
            ("IsInRole", (Func<string, bool>)(wanted => (wanted ?? string.Empty).Split(',').Select(r => r.Trim()).Any(r => roles.Split(',').Contains(r)))));

        private static ICeoDashboardService DashboardService(CeoDashboardDto dashboard) => Proxy<ICeoDashboardService>(
            ("GetAsync", (Func<CancellationToken, DateTime?, Task<ResponseDto<CeoDashboardDto>>>)((ct, asOf) => Task.FromResult(ResponseDtoHelper.Ok(dashboard)))));

        private class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Problems { get; } = new List<string>();

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (logLevel >= LogLevel.Warning) Problems.Add(logLevel + ": " + formatter(state, exception) + (exception == null ? string.Empty : "  =>  " + exception.GetType().Name + ": " + exception.Message));
            }
        }

        /// <summary>A reader with no row, with one row holding a scalar, or with one row of sample values (1, "x", a date) whatever the column type is.</summary>
        private sealed class FakeReader : DbDataReader
        {
            public enum Shape { NoRows, Scalar, Sample }

            private static readonly DateTime SampleDay = new DateTime(2026, 1, 1);
            private readonly Shape _shape;
            private readonly object _scalar;
            private bool _read;
            private bool _closed;

            public FakeReader(Shape shape, object scalar = null)
            {
                _shape = shape;
                _scalar = scalar;
            }

            private object Raw(Type wanted)
            {
                if (_shape == Shape.Scalar) return _scalar;
                Type type = Nullable.GetUnderlyingType(wanted) ?? wanted;
                if (type == typeof(string)) return "x";
                if (type == typeof(bool)) return false;
                if (type == typeof(DateTime)) return SampleDay;
                if (type == typeof(DateTimeOffset)) return new DateTimeOffset(SampleDay);
                if (type == typeof(Guid)) return Guid.Empty;
                if (type == typeof(byte[])) return new byte[8];
                return 1;
            }

            private T As<T>(Type wanted)
            {
                object raw = Raw(wanted);
                if (raw == null || raw is DBNull) throw new InvalidCastException("The fake row has no value for " + wanted.Name + ".");
                Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                if (raw is T direct) return direct;
                if (type.IsEnum) return (T)Enum.ToObject(type, raw);
                return (T)Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture);
            }

            public override int Depth => 0;
            public override int FieldCount => 1;
            public override bool HasRows => _shape != Shape.NoRows;
            public override bool IsClosed => _closed;
            public override int RecordsAffected => -1;
            public override object this[int ordinal] => GetValue(ordinal);
            public override object this[string name] => throw new NotSupportedException();

            public override bool Read()
            {
                if (_shape == Shape.NoRows || _read) return false;
                _read = true;
                return true;
            }

            public override bool NextResult() => false;
            public override void Close() => _closed = true;
            public override object GetValue(int ordinal) => Raw(typeof(object));
            public override int GetValues(object[] values) { values[0] = GetValue(0); return 1; }
            public override bool IsDBNull(int ordinal) => _shape == Shape.Scalar && (_scalar == null || _scalar is DBNull);
            public override bool GetBoolean(int ordinal) => As<bool>(typeof(bool));
            public override byte GetByte(int ordinal) => As<byte>(typeof(byte));
            public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override char GetChar(int ordinal) => 'x';
            public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override string GetDataTypeName(int ordinal) => "fake";
            public override DateTime GetDateTime(int ordinal) => As<DateTime>(typeof(DateTime));
            public override decimal GetDecimal(int ordinal) => As<decimal>(typeof(decimal));
            public override double GetDouble(int ordinal) => As<double>(typeof(double));
            public override IEnumerator GetEnumerator() => throw new NotSupportedException();
            public override Type GetFieldType(int ordinal) => typeof(object);
            public override float GetFloat(int ordinal) => As<float>(typeof(float));
            public override Guid GetGuid(int ordinal) => As<Guid>(typeof(Guid));
            public override short GetInt16(int ordinal) => As<short>(typeof(short));
            public override int GetInt32(int ordinal) => As<int>(typeof(int));
            public override long GetInt64(int ordinal) => As<long>(typeof(long));
            public override string GetName(int ordinal) => "c" + ordinal;
            public override int GetOrdinal(string name) => 0;
            public override string GetString(int ordinal) => As<string>(typeof(string));
            public override T GetFieldValue<T>(int ordinal) => As<T>(typeof(T));
        }

        /// <summary>Does not open the connection: there is no server.</summary>
        private sealed class NoServer : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
                => new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
        }

        /// <summary>
        /// Answers every query as an empty database does: no rows, and the one row of a scalar query (a count is 0, an existence test is false, a sum or a maximum is null).
        /// The queries of the tables in <see cref="SampleTables"/> get one row of sample values, so that the follow-up queries that depend on a first result run too.
        /// </summary>
        private sealed class EmptyDatabase : DbCommandInterceptor
        {
            public List<string> Commands { get; } = new List<string>();

            public List<string> SampleTables { get; } = new List<string>();

            private DbDataReader Answer(string commandText)
            {
                string sql = Regex.Replace(commandText ?? string.Empty, @"\s+", " ").Trim();
                Commands.Add(sql);
                if (Regex.IsMatch(sql, @"^SELECT COUNT(_BIG)?\(\*\)")) return new FakeReader(FakeReader.Shape.Scalar, 0);
                if (sql.StartsWith("SELECT CASE WHEN EXISTS", StringComparison.Ordinal)) return new FakeReader(FakeReader.Shape.Scalar, false);
                if (sql.StartsWith("SELECT COALESCE(SUM(", StringComparison.Ordinal)) return new FakeReader(FakeReader.Shape.Scalar, 0m);
                if (Regex.IsMatch(sql, @"^SELECT (SUM|AVG|MIN|MAX)\(")) return new FakeReader(FakeReader.Shape.Scalar, DBNull.Value);
                if (SampleTables.Any(t => sql.Contains("FROM [" + t + "]"))) return new FakeReader(FakeReader.Shape.Sample);
                return new FakeReader(FakeReader.Shape.NoRows);
            }

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
                => InterceptionResult<DbDataReader>.SuppressWithResult(Answer(command.CommandText));

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => new ValueTask<InterceptionResult<DbDataReader>>(InterceptionResult<DbDataReader>.SuppressWithResult(Answer(command.CommandText)));
        }

        private static ServiceCollection BaseServices()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.Configure<ViewModels.Identity.Settings.SiteSettings>(o => o.CookieOptions = new ViewModels.Identity.Settings.CookieOptions
            {
                DistributedSqlServerCacheOptions = new ViewModels.Identity.Settings.DistributedSqlServerCacheOptions { TableName = "AppSqlCache", SchemaName = "dbo" },
            });
            services.AddHttpContextAccessor();
            return services;
        }

        // the SQL Server provider is built once for all the tests (building the model of the whole application takes seconds)
        private static readonly object FixtureLock = new object();
        private static ServiceProvider _sqlServer;
        private static EmptyDatabase _empty;

        private static ApplicationDbContext SqlServerContext(out EmptyDatabase empty, out IServiceScope scope)
        {
            lock (FixtureLock)
            {
                if (_sqlServer == null)
                {
                    _empty = new EmptyDatabase();
                    ServiceCollection services = BaseServices();
                    services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer("Server=localhost;Database=none;User Id=x;Password=x;TrustServerCertificate=true").AddInterceptors(new NoServer(), _empty));
                    _sqlServer = services.BuildServiceProvider();
                }
            }
            _empty.Commands.Clear();
            _empty.SampleTables.Clear();
            empty = _empty;
            scope = _sqlServer.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        private static string TableOf(ApplicationDbContext context, Type entity) => context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(entity).GetTableName();

        // ==================================================================================== what the collaborators answer

        private static CeoDashboardDto Dashboard(bool warrantyHidden = false)
        {
            var dashboard = new CeoDashboardDto
            {
                Finance = new CeoFinanceDto
                {
                    CashAndBank = 48_200_000_000m, ReceivablesOpen = 30_000_000_000m, ReceivablesOverdue = 12_000_000_000m, IssuedChequesDue7Amount = 5_000_000_000m, IssuedChequesDue7Count = 3, NetProfitYtd = 2_000_000_000m,
                },
                StockValue = 120_000_000_000m, ItemsBelowMinimum = 11, QuarantineLines = 4, OpenMaterialRequests = 6,
                OpenPurchaseOrders = 5, OpenPurchaseOrdersAmount = 33_000_000_000m, LateOrderLines = 2, BlockedSuppliers = 1,
                OpenNcrs = 4, OverdueCapas = 1, CalibrationOverdue = 0, PendingHPoints = 2,
                PcActiveProjects = 9, PcProjectsRed = 1, PcProjectsAmber = 2, PcLateActivities = 3,
                ProductionLateOrders = 7, ProductionMachinesDown = 1, ProductionMaintenanceOverdue = 2, ProductionOverloadWeeks = 1,
                SalesOpenInquiries = 8, SalesPipelineValue = 75_000_000_000m, SalesHitRatePercent = 35m, SalesProposalsDueSoon = 1, SalesProposalsWaitingApproval = 2, SalesOrdersWaitingCredit = 0,
                ServiceOpenRequests = 12, ServiceSlaBreaches = 2, ServiceUnitsInWarranty = 30, ServiceGoodwillWaiting = 1, ServiceSurveyAverageScore = 4.25m,
                ServiceWarrantyCostYtd = warrantyHidden ? (decimal?)null : 250_000_000m, ServiceWarrantyCostHidden = warrantyHidden,
                OverdueDecisions = 3,
            };
            dashboard.Alerts.Add(new DashboardAlertDto { Level = "info", Text = "info alert", Link = "/a" });
            dashboard.Alerts.Add(new DashboardAlertDto { Level = "danger", Text = "danger alert", Link = "/b" });
            dashboard.Alerts.Add(new DashboardAlertDto { Level = "warning", Text = "warning alert", Link = "/c" });
            return dashboard;
        }

        private static CeoOverviewService Overview(IUnitOfWork uow, string roles, CeoDashboardDto dashboard, IMemoryCache cache, ILogger<CeoOverviewService> logger, bool qualityFails = false)
        {
            var receivables = Proxy<IReceivablesService>(
                ("GetAgeingAsync", (Func<DateTime?, CancellationToken, Task<ResponseDto<AgeingDto>>>)((asOf, ct) =>
                    Task.FromResult(ResponseDtoHelper.Ok(new AgeingDto { Current = 10, Days31To60 = 20, Days61To90 = 30, Over90 = 40, Total = 100 })))));
            var sales = Proxy<ISlDashboardService>(
                ("GetAsync", (Func<DateTime?, CancellationToken, Task<ResponseDto<SlDashboardDto>>>)((asOf, ct) =>
                {
                    var dto = new SlDashboardDto { Won = 6, Lost = 4 };
                    dto.PipelineByStage.Add(new SlStageValueDto { Name = "بررسی", Count = 2, Value = 100 });
                    dto.PipelineByStage.Add(new SlStageValueDto { Name = "پیشنهاد", Count = 3, Value = 200 });
                    return Task.FromResult(ResponseDtoHelper.Ok(dto));
                })));
            var projects = Proxy<IPcReportService>(
                ("GetPortfolioAsync", (Func<ViewModels.NasbNiro.Pagination.PaginationParams, CancellationToken, Task<ResponseDto<PagedList<PcPortfolioRowDto>>>>)((query, ct) =>
                {
                    var rows = new List<PcPortfolioRowDto>
                    {
                        new PcPortfolioRowDto { ContractId = 1, ContractTitle = "green", ProjectCode = "P1", SpiStatus = "green", CpiStatus = "green", Spi = 1.0m, PlannedPercent = 50, PhysicalPercent = 50 },
                        new PcPortfolioRowDto { ContractId = 2, ContractTitle = "amber", ProjectCode = "P2", SpiStatus = "amber", CpiStatus = "green", Spi = 0.9m, PlannedPercent = 60, PhysicalPercent = 54 },
                        new PcPortfolioRowDto { ContractId = 3, ContractTitle = "red", ProjectCode = null, SpiStatus = "green", CpiStatus = "red", Spi = 0.7m, PlannedPercent = 80, PhysicalPercent = 56 },
                    };
                    return Task.FromResult(ResponseDtoHelper.Ok(PagedList<PcPortfolioRowDto>.CreateAsync(rows, 1, 0, ct).GetAwaiter().GetResult()));
                })));
            var quality = Proxy<IQualityDashboardService>(
                ("GetDashboardAsync", (Func<CancellationToken, Task<ResponseDto<QualityDashboardDto>>>)(ct =>
                {
                    if (qualityFails) throw new InvalidOperationException("the quality module is down");
                    var dto = new QualityDashboardDto { OverdueNcrs = 2, CapaOpen = 5, ComplaintsOpen = 3, ComplaintsOverdue = 1, Ncrs = new NcrAgeDto { Major = 1, NotClassified = 2, Age0To7 = 3, Age8To30 = 2, Age31Plus = 1 } };
                    return Task.FromResult(ResponseDtoHelper.Ok(dto));
                })));
            return new CeoOverviewService(uow, User(roles), DashboardService(dashboard), receivables, sales, projects, quality, cache, logger);
        }

        private static CeoKpiDto Kpi(CeoOverviewDto overview, string key) => overview.Sections.SelectMany(s => s.Kpis).First(k => k.Key == key);

        // ==================================================================================== the overview on the SQL Server provider

        [TestMethod]
        public async Task Overview_Queries_Translate_To_SqlServer_And_Every_Section_Is_Built_On_An_Empty_Company()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewService service = Overview(context, "CEO", Dashboard(), cache, log);

                ResponseDto<CeoOverviewDto> answer = await service.GetOverviewAsync(true, CancellationToken.None);

                Assert.IsTrue(answer.Success, answer.Message);
                Assert.AreEqual(0, log.Problems.Count, "a query or a section failed:" + Environment.NewLine + string.Join(Environment.NewLine, log.Problems));
                CeoOverviewDto overview = answer.Data;
                Assert.IsFalse(overview.Cached);
                Assert.AreEqual(DateTime.Today, overview.AsOf);
                Assert.AreEqual(CeoText.Date(DateTime.Today), overview.AsOfText);
                CollectionAssert.AreEqual(CeoKeys.Sections, overview.Sections.Select(s => s.Key).ToArray());
                CollectionAssert.AreEqual(CeoKeys.Headline, overview.Headline.Select(k => k.Key).ToArray());
                CollectionAssert.AreEqual(new[] { 6, 6, 6, 5, 6, 4, 6, 6, 3 }, overview.Sections.Select(s => s.Kpis.Count).ToArray(), "KPIs per section");
                CollectionAssert.AreEqual(new[] { 3, 3, 2, 3, 2, 2, 2, 3, 1 }, overview.Sections.Select(s => s.Charts.Count).ToArray(), "charts per section");
                foreach (CeoSectionDto section in overview.Sections)
                {
                    Assert.IsTrue(string.IsNullOrEmpty(section.Note), section.Key + " carries a note");
                    Assert.IsTrue(section.AnalysisPrompt.Contains("تحلیل"), section.Key + " has no analysis prompt");
                    foreach (CeoKpiDto kpi in section.Kpis)
                    {
                        Assert.IsFalse(string.IsNullOrWhiteSpace(kpi.DrillKey), kpi.Key + " has no drill key");
                        Assert.IsTrue(kpi.Trend == null || kpi.Trend.Count <= 12, kpi.Key + " trend is longer than 12 points");
                    }
                    foreach (CeoChartDto chart in section.Charts)
                    {
                        Assert.IsFalse(string.IsNullOrWhiteSpace(chart.DrillKey), chart.Key + " has no drill key");
                        Assert.IsFalse(chart.Hidden, chart.Key + " is hidden for the CEO");
                        foreach (CeoChartSeriesDto series in chart.Series) Assert.AreEqual(chart.Labels.Count, series.Values.Count, section.Key + "/" + chart.Key + "/" + series.Name + " length");
                    }
                }
                Assert.AreEqual(12, overview.Sections[0].Charts[0].Labels.Count, "revenue chart covers twelve months");
                Assert.IsTrue(empty.Commands.Count > 20, "the sections ran their queries: " + empty.Commands.Count);
            }
        }

        [TestMethod]
        public async Task Headline_Figures_Are_The_Dashboard_Figures_With_Their_Status_Rules()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log).GetOverviewAsync(true, CancellationToken.None)).Data;

                CeoKpiDto cash = overview.Headline[0];
                Assert.AreEqual(48_200_000_000m, cash.Value);
                Assert.AreEqual("rial", cash.Unit);
                Assert.AreEqual(CeoStatus.Ok, cash.Status);
                Assert.AreEqual("۴۸٫۲ میلیارد ریال", cash.ShortText);
                Assert.AreEqual("fin.cash_accounts", cash.DrillKey);
                Assert.AreEqual(CeoStatus.Danger, overview.Headline[1].Status, "40 percent of the receivables are overdue");
                Assert.AreEqual(0m, overview.Headline[2].Value, "no overdue payables in an empty company");
                Assert.AreEqual(0m, overview.Headline[3].Value, "no backlog in an empty company");
                Assert.AreEqual(9m, overview.Headline[4].Value);
                Assert.AreEqual(7m, overview.Headline[5].Value);
                Assert.AreEqual(CeoStatus.Danger, overview.Headline[5].Status, "7 late orders is danger from 5");
                Assert.AreEqual(4m, overview.Headline[6].Value);
                Assert.AreEqual(CeoStatus.Ok, overview.Headline[6].Status, "4 open NCRs is below the warning level of 5");
                Assert.AreEqual(2m, overview.Headline[7].Value);
                Assert.AreEqual(CeoStatus.Danger, overview.Headline[7].Status);
                Assert.IsNull(overview.Headline[4].ShortText, "a count has no short text");

                CollectionAssert.AreEqual(new[] { "danger alert", "warning alert", "info alert" }, overview.Alerts.Select(a => a.Text).ToArray(), "worst first");
                CollectionAssert.AreEqual(new[] { "danger", "warning", "info" }, overview.Alerts.Select(a => a.Severity).ToArray());
                CollectionAssert.AreEqual(CeoKeys.Sections, overview.Pulse.Select(p => p.Key).ToArray());
                Assert.AreEqual(CeoStatus.Danger, overview.Pulse.First(p => p.Key == "finance").Status);
                Assert.AreEqual(CeoStatus.Danger, overview.Pulse.First(p => p.Key == "production").Status);
                Assert.IsTrue(overview.Pulse.First(p => p.Key == "production").DangerCount >= 2, "late orders and the machine down");
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public async Task A_Reader_Who_Does_Not_See_Pay_Gets_The_Warranty_Cost_Masked_And_The_Cache_Never_Mixes_The_Two_Readers()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewService ceo = Overview(context, "CEO", Dashboard(false), cache, log);
                CeoOverviewService pmo = Overview(context, "PMO", Dashboard(true), cache, log);

                CeoOverviewDto first = (await ceo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsFalse(first.Cached);
                CeoKpiDto shown = Kpi(first, "serviceWarrantyCostYtd");
                Assert.IsFalse(shown.Hidden);
                Assert.AreEqual(250_000_000m, shown.Value);

                CeoOverviewDto masked = (await pmo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsFalse(masked.Cached, "the PMO must not be served the copy that was made for the CEO");
                CeoKpiDto hidden = Kpi(masked, "serviceWarrantyCostYtd");
                Assert.IsTrue(hidden.Hidden);
                Assert.IsNull(hidden.Value);
                Assert.AreEqual(CeoText.HiddenPayroll, hidden.ValueText);
                Assert.AreEqual(CeoStatus.Neutral, hidden.Status);

                CeoOverviewDto again = (await pmo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsTrue(again.Cached, "the second answer comes from the cache");
                Assert.IsTrue(Kpi(again, "serviceWarrantyCostYtd").Hidden, "... and it is still the masked copy");
                CeoOverviewDto ceoAgain = (await ceo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsTrue(ceoAgain.Cached);
                Assert.IsFalse(Kpi(ceoAgain, "serviceWarrantyCostYtd").Hidden);
                CeoOverviewDto refreshed = (await ceo.GetOverviewAsync(true, CancellationToken.None)).Data;
                Assert.IsFalse(refreshed.Cached, "refresh computes again");
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public async Task A_Section_That_Fails_Is_Reported_Empty_And_The_Others_Are_Unchanged()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log, qualityFails: true).GetOverviewAsync(true, CancellationToken.None)).Data;

                CeoSectionDto quality = overview.Sections.First(s => s.Key == "quality");
                Assert.AreEqual(0, quality.Kpis.Count);
                Assert.AreEqual(0, quality.Charts.Count);
                Assert.IsFalse(string.IsNullOrWhiteSpace(quality.Note));
                Assert.AreEqual("کیفیت", quality.Title);
                Assert.AreEqual(8, overview.Sections.Count(s => s.Kpis.Count > 0), "the other eight sections are built");
                CeoKpiDto ncrs = overview.Headline.First(k => k.Key == "openNcrs");
                Assert.IsNull(ncrs.Value, "the headline figure of a failed section has no value");
                Assert.AreEqual(CeoText.NoValue, ncrs.ValueText);
                Assert.AreEqual(CeoStatus.Neutral, ncrs.Status);
                Assert.AreEqual(8, overview.Headline.Count, "the headline keeps its eight places");
                Assert.AreEqual(7, overview.Headline.Count(k => k.Value.HasValue), "... and seven of them have a value");
                Assert.AreEqual(1, log.Problems.Count(p => p.StartsWith("Error", StringComparison.Ordinal)), string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public async Task A_Section_Is_Read_By_Key_And_Only_By_The_Ceo_Or_The_Pmo()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewService ceo = Overview(context, "CEO", Dashboard(), cache, log);

                ResponseDto<CeoSectionDto> finance = await ceo.GetSectionAsync(" Finance ", CancellationToken.None);
                Assert.IsTrue(finance.Success, finance.Message);
                Assert.AreEqual("finance", finance.Data.Key);
                Assert.AreEqual("مالی", finance.Data.Title);
                Assert.AreEqual(6, finance.Data.Kpis.Count);
                Assert.AreEqual(3, finance.Data.Charts.Count);
                Assert.IsFalse(cache.TryGetValue(CeoCache.Key(true), out object _), "a section is never cached");

                ResponseDto<CeoSectionDto> unknown = await ceo.GetSectionAsync("nothing", CancellationToken.None);
                Assert.IsFalse(unknown.Success);
                Assert.AreEqual(CeoText.UnknownSection, unknown.Message);

                CeoOverviewService store = Overview(context, "WAREHOUSE", Dashboard(), cache, log);
                ResponseDto<CeoOverviewDto> noOverview = await store.GetOverviewAsync(false, CancellationToken.None);
                Assert.IsFalse(noOverview.Success);
                Assert.AreEqual(CeoText.NotAllowed, noOverview.Message);
                ResponseDto<CeoSectionDto> noSection = await store.GetSectionAsync("finance", CancellationToken.None);
                Assert.IsFalse(noSection.Success);
                Assert.AreEqual(CeoText.NotAllowed, noSection.Message);
                ResponseDto<CeoOverviewDto> financeManager = await Overview(context, "FINANCEMANAGER", Dashboard(), cache, log).GetOverviewAsync(false, CancellationToken.None);
                Assert.IsFalse(financeManager.Success, "the finance manager is not a reader of the CEO overview");

                ResponseDto<CeoSectionDto> pmo = await Overview(context, "PMO", Dashboard(true), cache, log).GetSectionAsync("service", CancellationToken.None);
                Assert.IsTrue(pmo.Success, pmo.Message);
                Assert.IsTrue(pmo.Data.Kpis.First(k => k.Key == "serviceWarrantyCostYtd").Hidden);
            }
        }

        [TestMethod]
        public async Task The_Figures_The_Overview_Defines_Run_All_Their_Queries_On_SqlServer()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                // one sample row in the first query of each figure makes the follow-up queries run too
                empty.SampleTables.Add(TableOf(context, typeof(SupplierInvoice)));
                empty.SampleTables.Add(TableOf(context, typeof(PcContractTerms)));
                empty.SampleTables.Add(TableOf(context, typeof(StockBalance)));
                empty.SampleTables.Add(TableOf(context, typeof(Warehouse)));

                List<CeoOverduePayable> payables = await CeoFigures.OverduePayablesAsync(context, new DateTime(2026, 10, 25), CancellationToken.None);
                CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(context, CancellationToken.None);
                List<CeoWarehouseValue> warehouses = await CeoFigures.StockValueByWarehouseAsync(context, CancellationToken.None);

                Assert.AreEqual(1, payables.Count, "the sample invoice has no payment");
                Assert.IsTrue(backlog.Contracts > 0m, "the sample contract has a value and no invoice");
                Assert.AreEqual(1, warehouses.Count, "the sample balance has a value");
                string[] followUps = { TableOf(context, typeof(PaymentRequest)), TableOf(context, typeof(FnSalesInvoice)), TableOf(context, typeof(Warehouse)) };
                foreach (string table in followUps) Assert.IsTrue(empty.Commands.Any(c => c.Contains("[" + table + "]")), "the follow-up query on " + table + " did not run");
            }
        }

        [TestMethod]
        public void The_Snapshot_Table_Has_The_Agreed_Model()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                var type = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(CeoKpiSnapshot));
                Assert.IsNotNull(type, "CeoKpiSnapshot is part of the model");
                Assert.AreEqual("CeoKpiSnapshots", type.GetTableName());
                Assert.AreEqual("date", type.FindProperty(nameof(CeoKpiSnapshot.Date)).GetColumnType());
                Assert.AreEqual(60, type.FindProperty(nameof(CeoKpiSnapshot.Key)).GetMaxLength());
                Assert.IsFalse(type.FindProperty(nameof(CeoKpiSnapshot.Key)).IsNullable);
                Assert.AreEqual("decimal(18,2)", type.FindProperty(nameof(CeoKpiSnapshot.Value)).GetColumnType());
                var unique = type.GetIndexes().Single(i => i.IsUnique);
                CollectionAssert.AreEqual(new[] { nameof(CeoKpiSnapshot.Date), nameof(CeoKpiSnapshot.Key) }, unique.Properties.Select(p => p.Name).ToArray(), "one row per day and key");
            }
        }

        // ==================================================================================== the figures with a few records (in-memory provider)

        private static ServiceProvider InMemory()
        {
            ServiceCollection services = BaseServices();
            services.AddMemoryCache();
            string name = "ceo-" + Guid.NewGuid();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());
            return services.BuildServiceProvider();
        }

        /// <summary>Records for the three figures; every date is relative to <paramref name="today"/>.</summary>
        private static async Task SeedFiguresAsync(ServiceProvider provider, DateTime today)
        {
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // overdue payables: the term is 30 days from the invoice date
            SupplierInvoice Invoice(int id, SupplierInvoiceStatus status, int ageDays, decimal total, bool deleted = false)
            {
                var invoice = new SupplierInvoice { Id = id, Number = "SI-" + id, Status = status, ProviderId = 1, PurchaseOrderId = 1, InvoiceNumber = "INV-" + id, InvoiceDate = today.AddDays(-ageDays), NetAmount = total, TotalAmount = total };
                if (deleted) invoice.Delete();
                return invoice;
            }
            PaymentRequest Payment(int id, int invoiceId, PaymentRequestStatus status, decimal amount, bool deleted = false)
            {
                var payment = new PaymentRequest { Id = id, Number = "PR-" + id, Status = status, ProviderId = 1, InvoiceId = invoiceId, Amount = amount, RequestDate = today };
                if (deleted) payment.Delete();
                return payment;
            }
            db.Set<SupplierInvoice>().AddRange(
                Invoice(1, SupplierInvoiceStatus.Approved, 85, 1000),    // overdue, 300 paid -> 700
                Invoice(2, SupplierInvoiceStatus.Approved, 15, 800),     // inside the term
                Invoice(3, SupplierInvoiceStatus.Approved, 116, 500),    // fully paid
                Invoice(4, SupplierInvoiceStatus.Draft, 116, 700),       // not approved
                Invoice(5, SupplierInvoiceStatus.Approved, 116, 900, deleted: true),
                Invoice(6, SupplierInvoiceStatus.Approved, 30, 600),     // exactly at the end of the term: not overdue yet
                Invoice(7, SupplierInvoiceStatus.Approved, 31, 400));    // one day past the term -> 400
            db.Set<PaymentRequest>().AddRange(
                Payment(1, 1, PaymentRequestStatus.Paid, 300),
                Payment(2, 1, PaymentRequestStatus.Approved, 200),       // approved is not paid
                Payment(3, 1, PaymentRequestStatus.Paid, 100, deleted: true),
                Payment(4, 3, PaymentRequestStatus.Paid, 500));

            // sales backlog: orders
            SlSalesOrder Order(int id, SlOrderStatus status, bool deleted, params (decimal Amount, decimal Quantity, decimal Invoiced)[] lines)
            {
                var order = new SlSalesOrder { Id = id, Number = "SO-" + id, OrderDate = today, CustomerId = 1, Status = status, Lines = new List<SlSalesOrderLine>() };
                int row = 1;
                foreach (var line in lines) order.Lines.Add(new SlSalesOrderLine { OrderId = id, RowNumber = row++, ItemId = 1, Description = "x", Amount = line.Amount, Quantity = line.Quantity, InvoicedQuantity = line.Invoiced });
                if (deleted) order.Delete();
                return order;
            }
            db.Set<SlSalesOrder>().AddRange(
                Order(1, SlOrderStatus.Confirmed, false, (1000m, 10m, 4m), (500m, 5m, 5m)),   // 1000 x 6/10 = 600, the second line is fully invoiced
                Order(2, SlOrderStatus.PartlyDelivered, false, (300m, 3m, 0m)),                // 300
                Order(3, SlOrderStatus.Draft, false, (9999m, 1m, 0m)),
                Order(4, SlOrderStatus.Confirmed, true, (9999m, 1m, 0m)),
                Order(5, SlOrderStatus.Cancelled, false, (9999m, 1m, 0m)),
                Order(6, SlOrderStatus.Invoiced, false, (9999m, 1m, 1m)));
            // ... and contracts in progress with a value in their project-control terms
            ContractEntity Contract(int id, ContractStatus status) => new ContractEntity { Id = id, Code = "C" + id, Title = "contract " + id, Status = status };
            db.Set<ContractEntity>().AddRange(Contract(1, ContractStatus.Current), Contract(2, ContractStatus.Current), Contract(3, ContractStatus.Done), Contract(4, ContractStatus.Current),
                Contract(5, ContractStatus.Current), Contract(6, ContractStatus.Current), Contract(7, ContractStatus.Current));
            PcContractTerms Terms(int id, int contractId, decimal? value, bool deleted = false)
            {
                var terms = new PcContractTerms { Id = id, ContractId = contractId, ValueExVat = value };
                if (deleted) terms.Delete();
                return terms;
            }
            db.Set<PcContractTerms>().AddRange(Terms(1, 1, 5000), Terms(2, 2, 100), Terms(3, 3, 9999), Terms(4, 4, 8888, deleted: true), Terms(5, 5, null), Terms(6, 6, 0), Terms(7, 7, 2000));
            FnSalesInvoice Sale(int id, int contractId, FnInvoiceType type, FnDocStatus status, decimal net) =>
                new FnSalesInvoice { Id = id, Number = "S-" + id, Date = today, CustomerId = 1, ContractId = contractId, Type = type, Status = status, NetAmount = net };
            db.Set<FnSalesInvoice>().AddRange(
                Sale(1, 1, FnInvoiceType.Progress, FnDocStatus.Posted, 1000),
                Sale(2, 1, FnInvoiceType.Final, FnDocStatus.Posted, 500),
                Sale(3, 1, FnInvoiceType.CreditNote, FnDocStatus.Posted, 200),          // deducted
                Sale(4, 1, FnInvoiceType.Advance, FnDocStatus.Posted, 700),             // an advance is not progress
                Sale(5, 1, FnInvoiceType.RetentionRelease, FnDocStatus.Posted, 300),
                Sale(6, 1, FnInvoiceType.Progress, FnDocStatus.Draft, 400),             // a draft is not invoiced
                Sale(7, 1, FnInvoiceType.Progress, FnDocStatus.Cancelled, 250),
                Sale(8, 2, FnInvoiceType.Progress, FnDocStatus.Posted, 150));           // over-invoiced: the backlog never goes below zero

            // stock value by warehouse
            db.Set<Warehouse>().AddRange(
                new Warehouse { Id = 1, Code = "A", Title = "انبار اصلی" }, new Warehouse { Id = 2, Code = "B", Title = "انبار فرعی" },
                new Warehouse { Id = 3, Code = "C", Title = "انبار خالی" }, new Warehouse { Id = 4, Code = "D", Title = string.Empty });
            db.Set<ItemCost>().AddRange(new ItemCost { Id = 1, ItemId = 1, AverageCost = 10m }, new ItemCost { Id = 2, ItemId = 2, AverageCost = 100m });
            db.Set<InventoryLot>().Add(new InventoryLot { Id = 1, ItemId = 2, LotNumber = "L1", IsCustomerProperty = true });
            db.Set<StockBalance>().AddRange(
                new StockBalance { Id = 1, ItemId = 1, WarehouseId = 1, Quantity = 5 },                  // 50
                new StockBalance { Id = 2, ItemId = 2, WarehouseId = 1, Quantity = 2 },                  // 200
                new StockBalance { Id = 3, ItemId = 1, WarehouseId = 2, Quantity = 100 },                // 1000
                new StockBalance { Id = 4, ItemId = 2, WarehouseId = 2, Quantity = 1, LotId = 1 },       // customer property: not company stock
                new StockBalance { Id = 5, ItemId = 2, WarehouseId = 3, Quantity = 0 },
                new StockBalance { Id = 6, ItemId = 1, WarehouseId = 3, Quantity = -4 },
                new StockBalance { Id = 7, ItemId = 3, WarehouseId = 1, Quantity = 10 },                // an item without a cost row has no value
                new StockBalance { Id = 8, ItemId = 1, WarehouseId = 4, Quantity = 1 });                 // 10, the warehouse has no title: its code is the name
            await db.SaveChangesAsync();
        }

        [TestMethod]
        public async Task Overdue_Payables_Are_The_Unpaid_Balance_Of_Approved_Invoices_Past_The_Thirty_Day_Term()
        {
            using ServiceProvider provider = InMemory();
            DateTime today = new DateTime(2026, 10, 25);
            await SeedFiguresAsync(provider, today);
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            List<CeoOverduePayable> rows = await CeoFigures.OverduePayablesAsync(uow, today, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 1, 7 }, rows.Select(r => r.InvoiceId).ToArray(), "oldest first; the invoice at exactly 30 days is not overdue");
            Assert.AreEqual(700m, rows[0].Balance, "1000 less the 300 paid (an approved request is not a payment, a deleted one does not count)");
            Assert.AreEqual(300m, rows[0].Paid);
            Assert.AreEqual(55, rows[0].DaysOverdue);
            Assert.AreEqual(400m, rows[1].Balance);
            Assert.AreEqual(1, rows[1].DaysOverdue);
            Assert.AreEqual(1100m, await CeoFigures.PayablesOverdueAsync(uow, today, CancellationToken.None));
            Assert.AreEqual(700m, await CeoFigures.PayablesOverdueAsync(uow, today.AddDays(-50), CancellationToken.None), "judged on an earlier day only the invoice that was already past its term counts");
            Assert.AreEqual(0m, await CeoFigures.PayablesOverdueAsync(uow, today.AddDays(-200), CancellationToken.None));
        }

        [TestMethod]
        public async Task Sales_Backlog_Is_The_Uninvoiced_Part_Of_Confirmed_Orders_Plus_The_Uninvoiced_Value_Of_Contracts_In_Progress()
        {
            using ServiceProvider provider = InMemory();
            await SeedFiguresAsync(provider, new DateTime(2026, 10, 25));
            using IServiceScope scope = provider.CreateScope();

            CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);

            Assert.AreEqual(900m, backlog.Orders, "600 of the first order (6 of 10 pieces not invoiced) and 300 of the partly delivered one");
            Assert.AreEqual(5700m, backlog.Contracts, "5000 less 1300 net invoiced (1000 + 500 - 200) and 2000 untouched; the over-invoiced contract adds nothing");
            Assert.AreEqual(6600m, backlog.Total);
        }

        [TestMethod]
        public async Task Stock_Value_By_Warehouse_Is_Quantity_Times_Average_Cost_Of_Company_Stock_Biggest_First()
        {
            using ServiceProvider provider = InMemory();
            await SeedFiguresAsync(provider, new DateTime(2026, 10, 25));
            using IServiceScope scope = provider.CreateScope();

            List<CeoWarehouseValue> rows = await CeoFigures.StockValueByWarehouseAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 2, 1, 4 }, rows.Select(r => r.WarehouseId).ToArray(), "the empty warehouse is left out, the biggest comes first");
            CollectionAssert.AreEqual(new[] { 1000m, 250m, 10m }, rows.Select(r => r.Value).ToArray());
            Assert.AreEqual("انبار فرعی", rows[0].Title);
            Assert.AreEqual("D", rows[2].Title, "a warehouse without a title is named by its code");
        }

        [TestMethod]
        public void Snapshot_Values_Follow_The_Key_Order_And_Are_Rounded_To_Two_Decimals()
        {
            CeoDashboardDto dashboard = Dashboard();
            dashboard.Finance.CashAndBank = 10.005m;
            List<KeyValuePair<string, decimal>> values = CeoFigures.SnapshotValues(dashboard, 5m, 6m);

            CollectionAssert.AreEqual(CeoKeys.Snapshot, values.Select(v => v.Key).ToArray());
            Assert.AreEqual(12, values.Count);
            Assert.AreEqual(10.01m, values[0].Value, "away from zero");
            Assert.AreEqual(30_000_000_000m, values[1].Value);
            Assert.AreEqual(5m, values[2].Value);
            Assert.AreEqual(6m, values[3].Value);
            Assert.AreEqual(9m, values[4].Value);
            Assert.AreEqual(7m, values[5].Value);
            Assert.AreEqual(4m, values[6].Value);
            Assert.AreEqual(2m, values[7].Value);
            Assert.AreEqual(120_000_000_000m, values[8].Value);
            Assert.AreEqual(11m, values[9].Value);
            Assert.AreEqual(33_000_000_000m, values[10].Value);
            Assert.AreEqual(75_000_000_000m, values[11].Value);
        }

        // ==================================================================================== the service charts (in-memory provider)

        /// <summary>
        /// After-sales records for the three service charts: requests, closed warranty orders and answered surveys, today and 40 days ago (always in an earlier Persian month, inside the window).
        /// An order whose labour is the pay of one or two payroll employees is protected (payroll privacy); <paramref name="withProtectedOrder"/> adds one.
        /// </summary>
        private static async Task SeedServiceAsync(ServiceProvider provider, bool withProtectedOrder)
        {
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DateTimeOffset now = DateTimeOffset.Now;
            DateTime today = DateTime.Today;
            SvServiceOrder Order(int id, SvOrderStatus status, SvWarrantyDecision decision, DateTimeOffset closed, decimal labour, decimal parts, int employees, int payroll) => new SvServiceOrder
            {
                Id = id, Number = "SO-" + id, Title = "order " + id, UnitId = 1, Status = status, Decision = decision, ClosedAt = closed,
                LabourAmount = labour, PartsAmount = parts, LabourEmployeeCount = employees, LabourPayrollEmployeeCount = payroll,
            };
            if (withProtectedOrder) db.Set<SvServiceOrder>().Add(Order(1, SvOrderStatus.Closed, SvWarrantyDecision.Warranty, now, 500, 300, 2, 2));
            db.Set<SvServiceOrder>().AddRange(
                Order(2, SvOrderStatus.Closed, SvWarrantyDecision.Warranty, now, 900, 100, 3, 3),
                Order(3, SvOrderStatus.Closed, SvWarrantyDecision.Warranty, now.AddDays(-40), 0, 50, 0, 0),
                Order(4, SvOrderStatus.Closed, SvWarrantyDecision.Billable, now, 7000, 7000, 3, 3),          // the customer pays: not a warranty cost
                Order(5, SvOrderStatus.InProgress, SvWarrantyDecision.Warranty, now, 8000, 8000, 3, 3));   // not closed yet
            SvServiceRequest Request(int id, DateTime received, DateTime? resolved) => new SvServiceRequest { Id = id, Number = "SR-" + id, CustomerId = 1, ReceivedAt = received, ResolvedAt = resolved, Description = "x", Type = SvRequestType.Breakdown };
            db.Set<SvServiceRequest>().AddRange(
                Request(1, today.AddHours(8), today.AddHours(9)),
                Request(2, today.AddDays(-40).AddHours(8), today.AddDays(-38).AddHours(8)),
                Request(3, today.AddHours(10), null));
            SlSurvey Survey(int id, SlSurveyStatus status, DateTimeOffset? answered, decimal? score) => new SlSurvey { Id = id, Number = "SU-" + id, CustomerId = 1, DraftedOn = today, Status = status, AnsweredAt = answered, AverageScore = score };
            db.Set<SlSurvey>().AddRange(
                Survey(1, SlSurveyStatus.Answered, now, 4.00m), Survey(2, SlSurveyStatus.Answered, now, 3.00m), Survey(3, SlSurveyStatus.Answered, now.AddDays(-40), 5.00m), Survey(4, SlSurveyStatus.Drafted, null, null));
            await db.SaveChangesAsync();
        }

        private static CeoChartDto ChartOf(CeoOverviewDto overview, string section, string chart) => overview.Sections.First(s => s.Key == section).Charts.First(c => c.Key == chart);

        [TestMethod]
        public async Task The_Service_Charts_Put_Each_Record_In_The_Persian_Month_Of_Its_Own_Day()
        {
            using ServiceProvider provider = InMemory();
            await SeedServiceAsync(provider, withProtectedOrder: true);
            using IServiceScope scope = provider.CreateScope();
            var log = new RecordingLogger<CeoOverviewService>();
            CeoOverviewDto overview = (await Overview(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "CEO", Dashboard(), provider.GetRequiredService<IMemoryCache>(), log).GetOverviewAsync(true, CancellationToken.None)).Data;

            CeoChartDto requests = ChartOf(overview, "service", "requestsByMonth");
            Assert.AreEqual(12, requests.Labels.Count);
            CeoChartSeriesDto received = requests.Series.First(s => s.Name == "دریافت‌شده"), resolved = requests.Series.First(s => s.Name == "حل‌شده");
            Assert.AreEqual(2m, received.Values.Last(), "two requests came in today; the last point is the Persian month of today");
            Assert.AreEqual(3m, received.Values.Sum());
            Assert.AreEqual(1m, resolved.Values.Last());
            Assert.AreEqual(2m, resolved.Values.Sum());

            CeoChartDto cost = ChartOf(overview, "service", "warrantyCostByMonth");
            Assert.IsFalse(cost.Hidden);
            CeoChartSeriesDto parts = cost.Series.First(s => s.Name == "قطعات"), labour = cost.Series.First(s => s.Name == "نیروی کار");
            Assert.AreEqual(400m, parts.Values.Last(), "300 + 100 of the two warranty orders closed today (a billable and an open order are left out)");
            Assert.AreEqual(450m, parts.Values.Sum(), "... and 50 of the order closed 40 days ago");
            Assert.AreEqual(1400m, labour.Values.Last());
            Assert.AreEqual(1400m, labour.Values.Sum());

            CeoChartDto survey = ChartOf(overview, "service", "surveyScoreByMonth");
            Assert.AreEqual(2, survey.Labels.Count, "only the months with an answered survey");
            CollectionAssert.AreEqual(new[] { 5.00m, 3.50m }, survey.Series[0].Values.ToArray(), "the average of the month, oldest month first");
            Assert.AreEqual(CeoText.MonthLabel(DateTime.Today), survey.Labels.Last());
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task The_Warranty_Cost_Chart_Is_Hidden_From_A_Reader_Without_Pay_Only_When_A_Protected_Order_Is_In_The_Window()
        {
            foreach (bool protectedOrder in new[] { true, false })
            {
                using ServiceProvider provider = InMemory();
                await SeedServiceAsync(provider, protectedOrder);
                using IServiceScope scope = provider.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var log = new RecordingLogger<CeoOverviewService>();

                CeoChartDto pmo = ChartOf((await Overview(uow, "PMO", Dashboard(protectedOrder), new MemoryCache(new MemoryCacheOptions()), log).GetOverviewAsync(true, CancellationToken.None)).Data, "service", "warrantyCostByMonth");
                CeoChartDto ceo = ChartOf((await Overview(uow, "CEO", Dashboard(false), new MemoryCache(new MemoryCacheOptions()), log).GetOverviewAsync(true, CancellationToken.None)).Data, "service", "warrantyCostByMonth");

                Assert.IsFalse(ceo.Hidden, "the CEO always sees the chart");
                Assert.AreEqual(2, ceo.Series.Count);
                Assert.AreEqual(protectedOrder, pmo.Hidden, "protected order in the window: " + protectedOrder);
                if (protectedOrder)
                {
                    Assert.AreEqual(0, pmo.Labels.Count, "a hidden chart has no labels");
                    Assert.AreEqual(0, pmo.Series.Count, "... and no values");
                    Assert.AreEqual("warrantyCostByMonth", pmo.Key, "... but keeps its key and title");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(pmo.Title));
                }
                else
                {
                    Assert.AreEqual(100m, pmo.Series.First(s => s.Name == "قطعات").Values.Last(), "without a protected order the PMO sees the figures");
                }
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        // ==================================================================================== the snapshot service (in-memory provider)

        private static async Task<List<CeoKpiSnapshot>> StoredAsync(ServiceProvider provider)
        {
            using IServiceScope scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Set<CeoKpiSnapshot>().AsNoTracking().OrderBy(s => s.Date).ThenBy(s => s.Key).ToListAsync();
        }

        [TestMethod]
        public async Task A_Snapshot_Stores_One_Row_Per_Day_And_Key_A_Second_Run_Replaces_The_Values_And_The_Cache_Is_Cleared()
        {
            using ServiceProvider provider = InMemory();
            DateTime today = DateTime.Today;
            await SeedFiguresAsync(provider, today);
            CeoDashboardDto dashboard = Dashboard();
            var cache = provider.GetRequiredService<IMemoryCache>();
            cache.Set(CeoCache.Key(true), new CeoOverviewDto(), TimeSpan.FromMinutes(5));
            cache.Set(CeoCache.Key(false), new CeoOverviewDto(), TimeSpan.FromMinutes(5));
            using IServiceScope scope = provider.CreateScope();
            var service = new CeoSnapshotService(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), User("PMO"), DashboardService(dashboard), provider.GetRequiredService<IServiceScopeFactory>(), cache, null);

            ResponseDto<CeoSnapshotResultDto> first = await service.TakeNowAsync(CancellationToken.None);

            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(12, first.Data.Keys);
            Assert.AreEqual(CeoText.Date(today), first.Data.DateText);
            Assert.IsFalse(cache.TryGetValue(CeoCache.Key(true), out object _), "a new snapshot changes the trends: the cached overviews are dropped");
            Assert.IsFalse(cache.TryGetValue(CeoCache.Key(false), out object _));
            List<CeoKpiSnapshot> rows = await StoredAsync(provider);
            Assert.AreEqual(12, rows.Count);
            CollectionAssert.AreEquivalent(CeoKeys.Snapshot, rows.Select(r => r.Key).ToArray());
            Assert.IsTrue(rows.All(r => r.Date == today), "the date is the day without a time");
            Assert.AreEqual(48_200_000_000m, rows.First(r => r.Key == "cash").Value);
            Assert.AreEqual(1100m, rows.First(r => r.Key == "payablesOverdue").Value, "the overdue payables are the figure of the overview");
            Assert.AreEqual(6600m, rows.First(r => r.Key == "salesBacklog").Value, "the backlog is the figure of the overview");
            Assert.AreEqual(7m, rows.First(r => r.Key == "lateProductionOrders").Value);

            // the same day again: the values are replaced, no row is added
            dashboard.Finance.CashAndBank = 1_000m;
            dashboard.OpenNcrs = 40;
            ResponseDto<CeoSnapshotResultDto> second = await service.TakeNowAsync(CancellationToken.None);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(12, second.Data.Keys);
            rows = await StoredAsync(provider);
            Assert.AreEqual(12, rows.Count, "one row per day and key");
            Assert.AreEqual(1_000m, rows.First(r => r.Key == "cash").Value);
            Assert.AreEqual(40m, rows.First(r => r.Key == "openNcrs").Value);

            // another day: twelve more rows
            Assert.AreEqual(12, await service.RunAsync(today.AddDays(1).AddHours(5), CancellationToken.None), "the time of the day is ignored");
            rows = await StoredAsync(provider);
            Assert.AreEqual(24, rows.Count);
            Assert.AreEqual(2, rows.Select(r => r.Date).Distinct().Count());
        }

        [TestMethod]
        public async Task Only_The_Ceo_And_The_Pmo_Take_A_Snapshot_By_Hand()
        {
            using ServiceProvider provider = InMemory();
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var cache = provider.GetRequiredService<IMemoryCache>();
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();

            ResponseDto<CeoSnapshotResultDto> refused = await new CeoSnapshotService(uow, User("WAREHOUSE"), DashboardService(Dashboard()), scopes, cache, null).TakeNowAsync(CancellationToken.None);
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(CeoText.NotAllowed, refused.Message);
            Assert.AreEqual(0, (await StoredAsync(provider)).Count, "nothing was stored");

            ResponseDto<CeoSnapshotResultDto> ceo = await new CeoSnapshotService(uow, User("CEO"), DashboardService(Dashboard()), scopes, cache, null).TakeNowAsync(CancellationToken.None);
            Assert.IsTrue(ceo.Success, ceo.Message);
            Assert.AreEqual(12, ceo.Data.Keys);
        }

        // ==================================================================================== the spoken summaries on the real overview (in-memory provider)

        [TestMethod]
        public async Task The_Spoken_Headline_Says_The_Cash_Of_The_Overview_And_Its_Change_Since_The_Previous_Persian_Month()
        {
            using ServiceProvider provider = InMemory();
            DateTime today = DateTime.Today;
            await SeedFiguresAsync(provider, today);
            CeoDashboardDto dashboard = Dashboard();
            var cache = new MemoryCache(new MemoryCacheOptions());
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // the snapshot of a day of the previous Persian month: the cash was 2.9 billion rial lower than it is now
            DateTime lastMonthDay = CeoTrends.AddMonths(CeoTrends.MonthStart(today), -1).AddDays(10);
            dashboard.Finance.CashAndBank = 45_300_000_000m;
            var snapshots = new CeoSnapshotService(uow, User("PMO"), DashboardService(dashboard), provider.GetRequiredService<IServiceScopeFactory>(), cache, null);
            Assert.AreEqual(12, await snapshots.RunAsync(lastMonthDay, CancellationToken.None));
            dashboard.Finance.CashAndBank = 48_200_000_000m;

            var log = new RecordingLogger<CeoOverviewService>();
            CeoOverviewService overview = Overview(uow, "CEO", dashboard, cache, log);
            var asked = new List<string>();
            ITextToSpeech tts = Proxy<ITextToSpeech>(("SynthesizeAsync", (Func<string, CancellationToken, Task<TtsResult>>)((text, ct) =>
            {
                asked.Add(text);
                return Task.FromResult(new TtsResult { Ok = true, Audio = new byte[3244], Model = "mock" });
            })));
            var speech = new CeoSpeechService(User("CEO"), overview, new PersianNumberWords(), tts, null, null);

            ResponseDto<CeoSpeechDto> headline = await speech.GetAsync("headline", null, CancellationToken.None);

            Assert.IsTrue(headline.Success, headline.Message);
            Assert.IsTrue(headline.Data.Text.Contains("نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال، دو میلیارد و نهصد میلیون بیشتر از ماه پیش"), headline.Data.Text);
            Assert.IsTrue(headline.Data.Text.StartsWith("خلاصه‌ی وضعیت شرکت در روز "), headline.Data.Text);
            Assert.IsFalse(headline.Data.Degraded);
            Assert.AreEqual(4328, headline.Data.AudioBase64.Length);
            Assert.AreEqual(headline.Data.Text, asked.Single(), "the audio is made of the text that is returned");
            CeoKpiDto cash = Kpi((await overview.GetOverviewAsync(false, CancellationToken.None)).Data, "cash");
            Assert.AreEqual(48_200_000_000m, cash.Value);
            Assert.AreEqual(2_900_000_000m, cash.Delta, "the change in the sentence is the change of the KPI");
            Assert.IsTrue((await overview.GetOverviewAsync(false, CancellationToken.None)).Data.Cached, "the headline is spoken from the same cached copy the screen shows");

            ResponseDto<CeoSpeechDto> finance = await speech.GetAsync("section", "finance", CancellationToken.None);
            Assert.IsTrue(finance.Success, finance.Message);
            Assert.IsTrue(finance.Data.Text.StartsWith("بخش مالی. "), finance.Data.Text);
            Assert.IsTrue(finance.Data.Text.Contains("نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال، دو میلیارد و نهصد میلیون بیشتر از ماه پیش"));
            CeoSectionDto financeSection = (await overview.GetSectionAsync("finance", CancellationToken.None)).Data;
            Assert.AreEqual(6, financeSection.Kpis.Count);
            int last = -1;
            foreach (CeoKpiDto kpi in financeSection.Kpis)
            {
                int at = finance.Data.Text.IndexOf(kpi.Title, StringComparison.Ordinal);
                Assert.IsTrue(at > last, "the figure '" + kpi.Title + "' is read, after the one before it");
                last = at;
            }

            ResponseDto<CeoSpeechDto> alerts = await speech.GetAsync("alerts", null, CancellationToken.None);
            Assert.AreEqual("سه هشدار برای امروز ثبت شده است؛ یک مورد خطر، یک مورد هشدار و یک مورد اطلاع. خطر، danger alert. هشدار، warning alert. اطلاع، info alert.", alerts.Data.Text,
                "the alerts of the CEO dashboard, worst first");
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        // ==================================================================================== the nightly job

        [TestMethod]
        public void The_Job_Runs_At_Half_Past_Midnight_And_Can_Be_Switched_Off()
        {
            Assert.AreEqual(new DateTime(2026, 10, 25, 0, 30, 0), CeoHostedService.NextRun(new DateTime(2026, 10, 25, 0, 10, 0)), "before the hour: today");
            Assert.AreEqual(new DateTime(2026, 10, 26, 0, 30, 0), CeoHostedService.NextRun(new DateTime(2026, 10, 25, 0, 30, 0)), "at the hour: tomorrow, the run of tonight is the one just started");
            Assert.AreEqual(new DateTime(2026, 10, 26, 0, 30, 0), CeoHostedService.NextRun(new DateTime(2026, 10, 25, 23, 59, 0)));
            Assert.AreEqual(new DateTime(2027, 1, 1, 0, 30, 0), CeoHostedService.NextRun(new DateTime(2026, 12, 31, 12, 0, 0)));

            IConfiguration Config(string value) => new ConfigurationBuilder().AddInMemoryCollection(value == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Ceo:Jobs"] = value }).Build();
            Assert.IsTrue(CeoHostedService.Enabled(Config(null)), "on by default");
            Assert.IsTrue(CeoHostedService.Enabled(Config("true")));
            Assert.IsFalse(CeoHostedService.Enabled(Config("false")));
            Assert.IsFalse(CeoHostedService.Enabled(Config("False")));
            Assert.IsTrue(CeoHostedService.Enabled(null));
        }
    }
}
