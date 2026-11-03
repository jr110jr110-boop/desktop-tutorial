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
using Microsoft.Extensions.Primitives;
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
using ViewModels.NasbNiro.ProductionDtos;
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

        internal static T Proxy<T>(params (string Name, Delegate Handler)[] handlers) where T : class
        {
            T proxy = DispatchProxy.Create<T, Stub>();
            foreach ((string name, Delegate handler) in handlers) ((Stub)(object)proxy).Handlers[name] = handler;
            return proxy;
        }

        /// <summary>The signed-in user with the given roles (comma separated).</summary>
        internal static IInventoryUserContext User(string roles) => Proxy<IInventoryUserContext>(
            ("get_UserId", (Func<int>)(() => 7)),
            ("IsInRole", (Func<string, bool>)(wanted => (wanted ?? string.Empty).Split(',').Select(r => r.Trim()).Any(r => roles.Split(',').Contains(r)))));

        /// <summary>
        /// The stubbed modules behind the overview: how each one behaves (works, fails) and how often it was asked, so that a test can say "the CEO dashboard was computed once"
        /// or "the receivables module was not asked at all".
        /// </summary>
        internal sealed class Modules
        {
            /// <summary>The CEO dashboard cannot be computed (the quality module is down): no section can be built.</summary>
            public bool DashboardFails { get; set; }

            /// <summary>The receivables module answers with a failure: the finance section cannot be built.</summary>
            public bool ReceivablesFail { get; set; }

            /// <summary>The project portfolio throws: the projects section cannot be built.</summary>
            public bool PortfolioFails { get; set; }

            /// <summary>The load report the CEO dashboard hands over (null: the production section loads it itself).</summary>
            public LoadReportDto ProductionLoad { get; set; }

            public AgeingDto Ageing { get; set; } = new AgeingDto { Current = 10, Days31To60 = 20, Days61To90 = 30, Over90 = 40, Total = 100 };

            public List<PcPortfolioRowDto> Portfolio { get; set; } = new List<PcPortfolioRowDto>
            {
                new PcPortfolioRowDto { ContractId = 1, ContractTitle = "green", ProjectCode = "P1", SpiStatus = "green", CpiStatus = "green", Spi = 1.0m, PlannedPercent = 50, PhysicalPercent = 50 },
                new PcPortfolioRowDto { ContractId = 2, ContractTitle = "amber", ProjectCode = "P2", SpiStatus = "amber", CpiStatus = "green", Spi = 0.9m, PlannedPercent = 60, PhysicalPercent = 54 },
                new PcPortfolioRowDto { ContractId = 3, ContractTitle = "red", ProjectCode = null, SpiStatus = "green", CpiStatus = "red", Spi = 0.7m, PlannedPercent = 80, PhysicalPercent = 56 },
            };

            /// <summary>What the payables ageing of the ledger answers (per supplier, in buckets).</summary>
            public List<FnNoteAgeingDto> Payables { get; } = new List<FnNoteAgeingDto>();

            public int PartsCalls { get; set; }
            public int DashboardCalls { get; set; }
            public int AgeingCalls { get; set; }
            public int PortfolioCalls { get; set; }
            public int PayableCalls { get; set; }
            public List<DateTime> AgeingDays { get; } = new List<DateTime>();
            public List<DateTime> PayableDays { get; } = new List<DateTime>();
        }

        /// <summary>The module results the CEO dashboard hands over with itself (quality, sales, the load of production).</summary>
        private static CeoDashboardParts Parts(CeoDashboardDto dashboard, Modules modules = null) => new CeoDashboardParts
        {
            Dashboard = dashboard,
            Quality = new QualityDashboardDto { OverdueNcrs = 2, CapaOpen = 5, ComplaintsOpen = 3, ComplaintsOverdue = 1, Ncrs = new NcrAgeDto { Major = 1, NotClassified = 2, Age0To7 = 3, Age8To30 = 2, Age31Plus = 1 } },
            Sales = new SlCeoCountsDto
            {
                Won = 6, Lost = 4,
                PipelineByStage = new List<SlStageValueDto> { new SlStageValueDto { Name = "بررسی", Count = 2, Value = 100 }, new SlStageValueDto { Name = "پیشنهاد", Count = 3, Value = 200 } },
            },
            ProductionLoad = modules?.ProductionLoad,
        };

        private static ICeoDashboardService DashboardService(CeoDashboardDto dashboard, Modules modules = null) => Proxy<ICeoDashboardService>(
            ("GetAsync", (Func<CancellationToken, DateTime?, Task<ResponseDto<CeoDashboardDto>>>)((ct, asOf) =>
            {
                ct.ThrowIfCancellationRequested();
                if (modules != null) modules.DashboardCalls++;
                if (modules != null && modules.DashboardFails) throw new InvalidOperationException("the quality module is down");
                return Task.FromResult(ResponseDtoHelper.Ok(dashboard));
            })),
            ("GetPartsAsync", (Func<CancellationToken, Task<CeoDashboardParts>>)(ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (modules != null) modules.PartsCalls++;
                if (modules != null && modules.DashboardFails) throw new InvalidOperationException("the quality module is down");
                return Task.FromResult(Parts(dashboard, modules));
            })));

        internal static IPayableAgeingService PayableAgeing(Modules modules) => Proxy<IPayableAgeingService>(
            ("PayableAgeingAsync", (Func<DateTime, CancellationToken, Task<List<FnNoteAgeingDto>>>)((day, ct) =>
            {
                modules.PayableCalls++;
                modules.PayableDays.Add(day);
                return Task.FromResult(modules.Payables.ToList());
            })));

        private static IReceivablesService Receivables(Modules modules) => Proxy<IReceivablesService>(
            ("GetAgeingAsync", (Func<DateTime?, CancellationToken, Task<ResponseDto<AgeingDto>>>)((asOf, ct) =>
            {
                modules.AgeingCalls++;
                modules.AgeingDays.Add(asOf ?? DateTime.MinValue);
                return Task.FromResult(modules.ReceivablesFail ? ResponseDtoHelper.BadRequest<AgeingDto>("the receivables module is down") : ResponseDtoHelper.Ok(modules.Ageing));
            })));

        private static IPcReportService Portfolio(Modules modules) => Proxy<IPcReportService>(
            ("GetPortfolioAsync", (Func<PaginationParams, CancellationToken, Task<ResponseDto<PagedList<PcPortfolioRowDto>>>>)((query, ct) =>
            {
                modules.PortfolioCalls++;
                if (modules.PortfolioFails) throw new InvalidOperationException("the project control module is down");
                return Task.FromResult(ResponseDtoHelper.Ok(PagedList<PcPortfolioRowDto>.CreateAsync(modules.Portfolio, 1, 0, ct).GetAwaiter().GetResult()));
            })));

        internal class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Problems { get; } = new List<string>();

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (logLevel >= LogLevel.Warning) Problems.Add(logLevel + ": " + formatter(state, exception) + (exception == null ? string.Empty : "  =>  " + exception.GetType().Name + ": " + exception.Message));
            }
        }

        /// <summary>A memory cache that remembers for how long each entry was stored, so that a test can tell the 120 seconds of a complete overview from the 10 seconds of one with a failed section.</summary>
        private sealed class RecordingCache : IMemoryCache
        {
            private readonly MemoryCache _inner = new MemoryCache(new MemoryCacheOptions());

            public Dictionary<string, TimeSpan?> Lifetimes { get; } = new Dictionary<string, TimeSpan?>();

            public ICacheEntry CreateEntry(object key) => new Entry(this, key, _inner.CreateEntry(key));

            public bool TryGetValue(object key, out object value) => _inner.TryGetValue(key, out value);

            public void Remove(object key) => _inner.Remove(key);

            public void Dispose() => _inner.Dispose();

            private sealed class Entry : ICacheEntry
            {
                private readonly RecordingCache _owner;
                private readonly ICacheEntry _entry;

                public Entry(RecordingCache owner, object key, ICacheEntry entry)
                {
                    _owner = owner;
                    Key = key;
                    _entry = entry;
                }

                public object Key { get; }
                public object Value { get => _entry.Value; set => _entry.Value = value; }
                public DateTimeOffset? AbsoluteExpiration { get => _entry.AbsoluteExpiration; set => _entry.AbsoluteExpiration = value; }
                public TimeSpan? AbsoluteExpirationRelativeToNow { get => _entry.AbsoluteExpirationRelativeToNow; set => _entry.AbsoluteExpirationRelativeToNow = value; }
                public TimeSpan? SlidingExpiration { get => _entry.SlidingExpiration; set => _entry.SlidingExpiration = value; }
                public IList<IChangeToken> ExpirationTokens => _entry.ExpirationTokens;
                public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _entry.PostEvictionCallbacks;
                public CacheItemPriority Priority { get => _entry.Priority; set => _entry.Priority = value; }
                public long? Size { get => _entry.Size; set => _entry.Size = value; }

                public void Dispose()
                {
                    _owner.Lifetimes[Convert.ToString(Key)] = _entry.AbsoluteExpirationRelativeToNow;
                    _entry.Dispose();
                }
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
        internal sealed class EmptyDatabase : DbCommandInterceptor
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
                // the earliest record of a sample table is a day in the past, so that the queries that wait for it (the history of a key) run too
                if (sql.StartsWith("SELECT MIN(", StringComparison.Ordinal) && SampleTables.Any(t => sql.Contains("FROM [" + t + "]"))) return new FakeReader(FakeReader.Shape.Scalar, new DateTime(2025, 1, 1));
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

        internal static ApplicationDbContext SqlServerContext(out EmptyDatabase empty, out IServiceScope scope)
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

        internal static CeoDashboardDto Dashboard(bool warrantyHidden = false)
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

        internal static CeoOverviewService Overview(IUnitOfWork uow, string roles, CeoDashboardDto dashboard, IMemoryCache cache, ILogger<CeoOverviewService> logger, Modules modules = null)
        {
            modules ??= new Modules();
            return new CeoOverviewService(uow, User(roles), DashboardService(dashboard, modules), Receivables(modules), PayableAgeing(modules), Portfolio(modules), cache, logger);
        }

        internal static CeoKpiDto Kpi(CeoOverviewDto overview, string key) => overview.Sections.SelectMany(s => s.Kpis).First(k => k.Key == key);

        // ==================================================================================== the overview on the SQL Server provider

        [TestMethod]
        public async Task Overview_Queries_Translate_To_SqlServer_And_Every_Section_Is_Built_On_An_Empty_Company()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new RecordingCache())
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules();
                CeoOverviewService service = Overview(context, "CEO", Dashboard(), cache, log, modules);

                ResponseDto<CeoOverviewDto> answer = await service.GetOverviewAsync(true, CancellationToken.None);

                Assert.IsTrue(answer.Success, answer.Message);
                Assert.AreEqual(0, log.Problems.Count, "a query or a section failed:" + Environment.NewLine + string.Join(Environment.NewLine, log.Problems));
                Assert.AreEqual(CeoCache.Lifetime, cache.Lifetimes[CeoCache.Key(true)], "a complete overview is kept for the full lifetime");

                // review item 5: one computation asks each module once, however many sections draw on it
                Assert.AreEqual(1, modules.PartsCalls, "the CEO dashboard with its parts (quality, sales, production load) is computed once for the nine sections");
                Assert.AreEqual(0, modules.DashboardCalls, "... and the sections do not ask the plain dashboard again");
                Assert.AreEqual(1, modules.PortfolioCalls, "the project portfolio is read once");
                Assert.AreEqual(1, modules.PayableCalls, "the payables ageing is read once");
                Assert.AreEqual(1, modules.AgeingCalls, "the receivables ageing is read once (an empty company has no history to derive)");
                Assert.AreEqual(DateTime.Today, modules.AgeingDays.Single());
                Assert.AreEqual(DateTime.Today, modules.PayableDays.Single(), "the payables are aged on today");
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

                // review item 17: from the first day the trend of a snapshot key shows one point, the live value of the current Persian month
                List<CeoKpiDto> trended = overview.Sections.SelectMany(s => s.Kpis).Where(k => k.Trend != null).ToList();
                CollectionAssert.AreEquivalent(CeoKeys.Snapshot, trended.Select(k => k.Key).ToArray(), "the twelve snapshot keys carry a trend, no other key does");
                foreach (CeoKpiDto kpi in trended)
                {
                    CollectionAssert.AreEqual(new[] { kpi.Value.Value }, kpi.Trend, kpi.Key + ": an empty company has no history, only the live point");
                    CollectionAssert.AreEqual(new[] { CeoText.MonthLabel(DateTime.Today) }, kpi.TrendLabels, kpi.Key);
                    Assert.IsNull(kpi.Delta, kpi.Key + ": nothing to compare the live value with");
                    Assert.IsNull(kpi.DeltaGood, kpi.Key);
                }
                foreach (CeoKpiDto headline in overview.Headline) Assert.IsNotNull(headline.Trend, headline.Key + ": the headline figures all have a sparkline");
            }
        }

        [TestMethod]
        public async Task Every_Section_Builds_Exactly_The_KPIs_Of_The_Spec_Table_In_Its_Order()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log).GetOverviewAsync(true, CancellationToken.None)).Data;

                foreach (CeoSectionDto section in overview.Sections)
                {
                    List<CeoKpiSpec> specs = CeoKpiSpecs.Of(section.Key).ToList();
                    CollectionAssert.AreEqual(specs.Select(s => s.Key).ToArray(), section.Kpis.Select(k => k.Key).ToArray(), section.Key + ": the KPIs are the specs, in order");
                    foreach (CeoKpiDto kpi in section.Kpis)
                    {
                        CeoKpiSpec spec = specs.Single(s => s.Key == kpi.Key);
                        Assert.AreEqual(spec.Title, kpi.Title, kpi.Key);
                        Assert.AreEqual(spec.Unit, kpi.Unit, kpi.Key);
                        Assert.AreEqual(spec.Link, kpi.Link, kpi.Key);
                        Assert.AreEqual(spec.DrillKey, kpi.DrillKey, kpi.Key);
                        Assert.AreEqual(spec.DrillParams == null, kpi.DrillParams == null, kpi.Key + ": drill parameters come from the spec");
                        if (spec.DrillParams != null) CollectionAssert.AreEqual(spec.DrillParams(DateTime.Today).OrderBy(p => p.Key).ToList(), kpi.DrillParams.OrderBy(p => p.Key).ToList(), kpi.Key);
                    }
                }
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public void The_Spec_Table_Is_Complete_Unique_And_Points_At_Real_Drills_And_Front_Routes()
        {
            Assert.AreEqual(48, CeoKpiSpecs.All.Count);
            Assert.AreEqual(48, CeoKpiSpecs.All.Select(s => s.Key).Distinct().Count(), "every key once");
            CollectionAssert.AreEqual(new[] { 6, 6, 6, 5, 6, 4, 6, 6, 3 }, CeoKeys.Sections.Select(s => CeoKpiSpecs.Of(s).Count()).ToArray());
            CollectionAssert.AreEqual(CeoKeys.Sections, CeoKpiSpecs.All.Select(s => s.Section).Distinct().ToArray(), "the specs are grouped in the order of the sections");
            foreach (string key in CeoKeys.Headline) Assert.IsNotNull(CeoKpiSpecs.Get(key), key);
            foreach (string key in CeoKeys.Snapshot) Assert.IsNotNull(CeoKpiSpecs.Get(key), key);

            HashSet<string> drills = typeof(CeoDrillRef).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToHashSet();
            foreach (CeoKpiSpec spec in CeoKpiSpecs.All)
            {
                Assert.IsTrue(drills.Contains(spec.DrillKey), spec.Key + " drill key " + spec.DrillKey + " is not in the drill catalogue");
                Assert.IsTrue(spec.Link != null && spec.Link.StartsWith("/"), spec.Key + " link " + spec.Link);
                Assert.IsFalse(spec.Link.StartsWith("/finance/"), spec.Key + " uses a route of the finance module the front does not have: " + spec.Link);
                Assert.IsTrue(new[] { CeoUnit.Rial, CeoUnit.Count, CeoUnit.Percent, CeoUnit.Days, CeoUnit.Hours }.Contains(spec.Unit), spec.Key + " unit " + spec.Unit);
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.Title), spec.Key);
            }
            Assert.AreEqual("درخواست‌های خارج از مهلت (SLA)", CeoKpiSpecs.Get("serviceSlaBreaches").Title, "the bracket of the SLA title is dropped by the speech");
        }

        [TestMethod]
        public void Placeholders_Keep_The_Spec_Of_Their_KPI_Without_A_Value()
        {
            DateTime today = new DateTime(2026, 10, 25);
            foreach (CeoKpiSpec spec in CeoKpiSpecs.All)
            {
                CeoKpiDto placeholder = CeoKpiSpecs.Placeholder(spec, today);

                Assert.AreEqual(spec.Key, placeholder.Key);
                Assert.AreEqual(spec.Title, placeholder.Title);
                Assert.AreEqual(spec.DrillKey, placeholder.DrillKey);
                Assert.IsNull(placeholder.Value, spec.Key);
                Assert.AreEqual(CeoStatus.Neutral, placeholder.Status, spec.Key);
                Assert.AreEqual(CeoText.NoValue, placeholder.ValueText, spec.Key);
                Assert.AreEqual(spec.DrillParams != null, placeholder.DrillParams != null, spec.Key);
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
        public async Task A_Section_That_Cannot_Be_Computed_Keeps_The_Shape_Of_The_Contract_With_Placeholders_A_Note_And_A_Neutral_Pulse()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new RecordingCache())
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules { ReceivablesFail = true };   // the finance section needs the ageing of the receivables module
                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

                // review item 12: the failed section is not an empty box. Its KPIs are placeholders with their keys, titles, units and links, no value, status neutral; no charts; a note says why
                CeoSectionDto finance = overview.Sections.First(s => s.Key == "finance");
                CollectionAssert.AreEqual(CeoKpiSpecs.Of("finance").Select(s => s.Key).ToArray(), finance.Kpis.Select(k => k.Key).ToArray(), "the six KPIs keep their keys and their order");
                foreach (CeoKpiDto placeholder in finance.Kpis)
                {
                    CeoKpiSpec spec = CeoKpiSpecs.Get(placeholder.Key);
                    Assert.AreEqual(spec.Title, placeholder.Title, placeholder.Key);
                    Assert.AreEqual(spec.Unit, placeholder.Unit, placeholder.Key);
                    Assert.AreEqual(spec.Link, placeholder.Link, placeholder.Key);
                    Assert.AreEqual(spec.DrillKey, placeholder.DrillKey, placeholder.Key);
                    Assert.IsNull(placeholder.Value, placeholder.Key);
                    Assert.AreEqual(CeoText.NoValue, placeholder.ValueText, placeholder.Key);
                    Assert.AreEqual(CeoStatus.Neutral, placeholder.Status, placeholder.Key);
                    Assert.IsNull(placeholder.Trend, placeholder.Key);
                    Assert.IsNull(placeholder.Delta, placeholder.Key);
                    Assert.IsFalse(placeholder.Hidden, placeholder.Key);
                }
                Assert.AreEqual(0, finance.Charts.Count, "no charts for a section that could not be computed");
                Assert.AreEqual(CeoText.SectionUnavailable, finance.Note);
                Assert.AreEqual("مالی", finance.Title);
                Assert.AreEqual(CeoLinks.FinanceDashboard, finance.Link);
                Assert.IsTrue(finance.AnalysisPrompt.Contains("تحلیل"));
                Assert.AreEqual(0, finance.AlertCount);

                CeoPulseDto pulse = overview.Pulse.First(p => p.Key == "finance");
                Assert.AreEqual(CeoStatus.Neutral, pulse.Status, "a section without figures is neutral, not green");
                Assert.AreEqual(0, pulse.DangerCount);
                Assert.AreEqual(0, pulse.WarnCount);

                // the other eight sections are built and untouched
                Assert.AreEqual(8, overview.Sections.Count(s => string.IsNullOrEmpty(s.Note)));
                CollectionAssert.AreEqual(new[] { 6, 6, 6, 5, 6, 4, 6, 6, 3 }, overview.Sections.Select(s => s.Kpis.Count).ToArray(), "every section keeps its number of KPIs");
                Assert.IsTrue(overview.Sections.Where(s => s.Key != "finance").All(s => s.Charts.Count > 0));
                Assert.AreEqual(CeoStatus.Danger, overview.Pulse.First(p => p.Key == "production").Status);

                // the headline keeps its eight places: the three finance figures are placeholders, the other five have a value
                Assert.AreEqual(8, overview.Headline.Count);
                CollectionAssert.AreEqual(CeoKeys.Headline, overview.Headline.Select(k => k.Key).ToArray());
                CollectionAssert.AreEqual(new[] { "cash", "receivablesOpen", "payablesOverdue" }, overview.Headline.Where(k => !k.Value.HasValue).Select(k => k.Key).ToArray());
                Assert.IsTrue(overview.Headline.Where(k => !k.Value.HasValue).All(k => k.Status == CeoStatus.Neutral && k.ValueText == CeoText.NoValue));
                Assert.AreEqual(1, log.Problems.Count(p => p.StartsWith("Error", StringComparison.Ordinal)), string.Join(Environment.NewLine, log.Problems));

                // review item 4: an overview with a failed section is kept for ten seconds only, so a module that comes back shows up quickly
                Assert.AreEqual(CeoCache.FailedLifetime, cache.Lifetimes[CeoCache.Key(true)]);
                Assert.IsTrue(CeoCache.FailedLifetime <= TimeSpan.FromSeconds(10));
            }
        }

        [TestMethod]
        public async Task When_The_Ceo_Dashboard_Cannot_Be_Computed_Every_Section_Is_Reported_At_Once_Without_Trying_Nine_Times()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new RecordingCache())
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules { DashboardFails = true };
                CeoOverviewService service = Overview(context, "CEO", Dashboard(), cache, log, modules);

                CeoOverviewDto overview = (await service.GetOverviewAsync(true, CancellationToken.None)).Data;

                // review item 4: the failure is remembered for the whole computation, the dashboard (and the modules behind it) is asked once, not once per section
                Assert.AreEqual(1, modules.PartsCalls, "the CEO dashboard was tried once");
                Assert.AreEqual(0, modules.AgeingCalls + modules.PortfolioCalls + modules.PayableCalls, "no section went on to ask a module");
                Assert.AreEqual(0, empty.Commands.Count, "... or the database");
                Assert.AreEqual(1, log.Problems.Count(p => p.StartsWith("Error", StringComparison.Ordinal)), "one error in the log, not nine: " + string.Join(Environment.NewLine, log.Problems));

                CollectionAssert.AreEqual(CeoKeys.Sections, overview.Sections.Select(s => s.Key).ToArray());
                CollectionAssert.AreEqual(new[] { 6, 6, 6, 5, 6, 4, 6, 6, 3 }, overview.Sections.Select(s => s.Kpis.Count).ToArray(), "the shape of the contract is kept");
                foreach (CeoSectionDto section in overview.Sections)
                {
                    Assert.AreEqual(CeoText.DashboardUnavailable, section.Note, section.Key);
                    Assert.AreEqual(0, section.Charts.Count, section.Key);
                    Assert.IsTrue(section.Kpis.All(k => !k.Value.HasValue && k.Status == CeoStatus.Neutral && k.Trend == null), section.Key);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(section.Title), section.Key);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(section.Link), section.Key);
                }
                Assert.IsTrue(overview.Pulse.All(p => p.Status == CeoStatus.Neutral && p.DangerCount == 0 && p.WarnCount == 0), "every light is neutral");
                Assert.AreEqual(8, overview.Headline.Count);
                Assert.IsTrue(overview.Headline.All(k => !k.Value.HasValue && k.Status == CeoStatus.Neutral));
                Assert.AreEqual(0, overview.Alerts.Count, "there is no dashboard to take the alerts from");
                Assert.AreEqual(CeoCache.FailedLifetime, cache.Lifetimes[CeoCache.Key(true)], "an overview that failed is kept for ten seconds at most");

                // the dashboard comes back: a refresh computes everything again and the complete overview is kept for the full 120 seconds
                modules.DashboardFails = false;
                CeoOverviewDto stillFailed = (await service.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsTrue(stillFailed.Cached, "within the ten seconds the failed copy is served");
                CeoOverviewDto recovered = (await service.GetOverviewAsync(true, CancellationToken.None)).Data;
                Assert.IsFalse(recovered.Cached);
                Assert.IsTrue(recovered.Sections.All(s => string.IsNullOrEmpty(s.Note)), "all nine sections are built again");
                Assert.AreEqual(CeoCache.Lifetime, cache.Lifetimes[CeoCache.Key(true)]);
                Assert.AreEqual(TimeSpan.FromSeconds(120), CeoCache.Lifetime);
            }
        }

        [TestMethod]
        public async Task A_Section_Asked_For_On_Its_Own_Reports_A_Dashboard_Failure_The_Same_Way()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules { DashboardFails = true };

                ResponseDto<CeoSectionDto> quality = await Overview(context, "CEO", Dashboard(), cache, log, modules).GetSectionAsync("quality", CancellationToken.None);

                Assert.IsTrue(quality.Success, quality.Message);
                Assert.AreEqual(CeoText.DashboardUnavailable, quality.Data.Note);
                Assert.AreEqual(6, quality.Data.Kpis.Count);
                Assert.AreEqual(0, quality.Data.Charts.Count);
                Assert.AreEqual(1, modules.PartsCalls);
                Assert.AreEqual(1, log.Problems.Count(p => p.StartsWith("Error", StringComparison.Ordinal)));
                Assert.IsFalse(cache.TryGetValue(CeoCache.Key(true), out object _), "a section computed on its own is not stored");
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
                Assert.IsFalse(cache.TryGetValue(CeoCache.Key(true), out object _), "a section computed on its own is not stored in the cache");

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
        public async Task A_Section_Is_Served_From_The_Cached_Overview_And_Computed_Only_On_Refresh_Or_When_There_Is_No_Cache()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules();
                CeoDashboardDto dashboard = Dashboard();
                CeoOverviewService ceo = Overview(context, "CEO", dashboard, cache, log, modules);

                // no overview in the cache yet: the section is computed (and, being on its own, not stored)
                CeoSectionDto first = (await ceo.GetSectionAsync("finance", CancellationToken.None)).Data;
                Assert.AreEqual(1, modules.PartsCalls);
                Assert.AreEqual(48_200_000_000m, first.Kpis.First(k => k.Key == "cash").Value);
                Assert.IsFalse(cache.TryGetValue(CeoCache.Key(true), out object _));

                // the screen loads the overview; from then on the section endpoint (and the voice) say what the screen shows
                await ceo.GetOverviewAsync(false, CancellationToken.None);
                Assert.AreEqual(2, modules.PartsCalls);
                dashboard.Finance.CashAndBank = 1_000m;
                CeoSectionDto cached = (await ceo.GetSectionAsync(" Finance ", CancellationToken.None)).Data;
                Assert.AreEqual(2, modules.PartsCalls, "served from the cached overview: no module was asked");
                Assert.AreEqual(48_200_000_000m, cached.Kpis.First(k => k.Key == "cash").Value, "... so it is the figure of the screen");

                // refresh=true computes the section now
                CeoSectionDto fresh = (await ceo.GetSectionAsync("finance", true, CancellationToken.None)).Data;
                Assert.AreEqual(3, modules.PartsCalls);
                Assert.AreEqual(1_000m, fresh.Kpis.First(k => k.Key == "cash").Value);
                CeoSectionDto stillCached = (await ceo.GetSectionAsync("finance", CancellationToken.None)).Data;
                Assert.AreEqual(48_200_000_000m, stillCached.Kpis.First(k => k.Key == "cash").Value, "a section refreshed on its own does not replace the cached overview");

                // the cached overview of the CEO is not served to a reader who sees other figures: the PMO computes its own
                var pmoModules = new Modules();
                CeoSectionDto pmo = (await Overview(context, "PMO", Dashboard(true), cache, log, pmoModules).GetSectionAsync("service", CancellationToken.None)).Data;
                Assert.AreEqual(1, pmoModules.PartsCalls, "the masked variant has its own cache entry");
                Assert.IsTrue(pmo.Kpis.First(k => k.Key == "serviceWarrantyCostYtd").Hidden);
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public async Task The_Cache_Keeps_Its_Own_Copy_So_A_Caller_Who_Changes_What_It_Got_Changes_Nothing_For_The_Next_Reader()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                CeoOverviewService ceo = Overview(context, "CEO", Dashboard(), cache, log);

                // review item 18: what the first (computed) answer holds is a copy of what is cached, and so is every cached answer
                CeoOverviewDto computed = (await ceo.GetOverviewAsync(true, CancellationToken.None)).Data;
                decimal cash = computed.Sections[0].Kpis[0].Value.Value;
                int cashTrend = computed.Sections[0].Kpis[0].Trend.Count;
                int headlineTrend = computed.Headline[0].Trend.Count;
                int alerts = computed.Alerts.Count;
                Scribble(computed);

                CeoOverviewDto cached = (await ceo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.IsTrue(cached.Cached);
                Assert.AreEqual(cash, cached.Sections[0].Kpis[0].Value.Value, "the value of the cached copy is the computed one");
                Assert.AreEqual(cashTrend, cached.Sections[0].Kpis[0].Trend.Count);
                Assert.AreEqual(headlineTrend, cached.Headline[0].Trend.Count);
                Assert.AreEqual(alerts, cached.Alerts.Count);
                Assert.AreEqual("مالی", cached.Sections[0].Title);
                Assert.IsNull(cached.Sections[0].Note);
                Assert.IsFalse(cached.Sections[0].Charts[0].Series[0].Values.Contains(-99m));
                Assert.IsTrue(cached.Sections.SelectMany(s => s.Kpis).Where(k => k.DrillParams != null).All(k => !k.DrillParams.ContainsKey("scribble")));
                Assert.IsTrue(cached.Sections.SelectMany(s => s.Charts).Where(c => c.DrillParams != null).All(c => !c.DrillParams.ContainsKey("scribble")));

                Scribble(cached);
                CeoOverviewDto again = (await ceo.GetOverviewAsync(false, CancellationToken.None)).Data;
                Assert.AreEqual(cash, again.Sections[0].Kpis[0].Value.Value, "a second reader gets an untouched copy too");
                Assert.AreEqual(alerts, again.Alerts.Count);
                Assert.IsFalse(object.ReferenceEquals(cached, again));
                Assert.IsFalse(object.ReferenceEquals(cached.Sections[0], again.Sections[0]));

                // and the same for a section read from the cache
                CeoSectionDto section = (await ceo.GetSectionAsync("finance", CancellationToken.None)).Data;
                section.Kpis.Clear();
                section.Charts.Clear();
                section.Note = "changed";
                CeoSectionDto sectionAgain = (await ceo.GetSectionAsync("finance", CancellationToken.None)).Data;
                Assert.AreEqual(6, sectionAgain.Kpis.Count);
                Assert.AreEqual(3, sectionAgain.Charts.Count);
                Assert.IsNull(sectionAgain.Note);
            }
        }

        /// <summary>What a careless caller might do with an overview it was given: edit figures, trends, charts, alerts and drill parameters in place.</summary>
        private static void Scribble(CeoOverviewDto overview)
        {
            foreach (CeoKpiDto kpi in overview.Headline.Concat(overview.Sections.SelectMany(s => s.Kpis)))
            {
                kpi.Value = -99m;
                kpi.Status = CeoStatus.Danger;
                kpi.Trend?.Add(-99m);
                kpi.TrendLabels?.Add("x");
                if (kpi.DrillParams != null) kpi.DrillParams["scribble"] = "1";
            }
            foreach (CeoChartDto chart in overview.Sections.SelectMany(s => s.Charts))
            {
                foreach (CeoChartSeriesDto series in chart.Series) for (int i = 0; i < series.Values.Count; i++) series.Values[i] = -99m;
                chart.Labels.Add("x");
                if (chart.DrillParams != null) chart.DrillParams["scribble"] = "1";
            }
            overview.Alerts.Clear();
            overview.Sections[0].Title = "changed";
            overview.Sections[0].Note = "changed";
        }

        [TestMethod]
        public async Task The_Figures_The_Overview_Defines_Run_All_Their_Queries_On_SqlServer()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                // one sample row in the first query of each figure makes the follow-up queries run too
                empty.SampleTables.Add(TableOf(context, typeof(PcContractTerms)));
                empty.SampleTables.Add(TableOf(context, typeof(StockBalance)));
                empty.SampleTables.Add(TableOf(context, typeof(Warehouse)));

                CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(context, CancellationToken.None);
                List<CeoWarehouseValue> warehouses = await CeoFigures.StockValueByWarehouseAsync(context, CancellationToken.None);

                Assert.IsTrue(backlog.Contracts > 0m, "the sample contract has a value and no invoice");
                Assert.AreEqual(1, warehouses.Count, "the sample balance has a value");
                string[] followUps = { TableOf(context, typeof(FnSalesInvoice)), TableOf(context, typeof(Warehouse)) };
                foreach (string table in followUps) Assert.IsTrue(empty.Commands.Any(c => c.Contains("[" + table + "]")), "the follow-up query on " + table + " did not run");
            }
        }

        [TestMethod]
        public async Task The_History_Queries_Of_The_Four_Backfilled_Keys_Translate_To_SqlServer()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            {
                // a sample row in the first query of each key makes the queries that follow it run
                empty.SampleTables.Add(TableOf(context, typeof(FnSalesInvoice)));
                empty.SampleTables.Add(TableOf(context, typeof(NonConformance)));
                empty.SampleTables.Add(TableOf(context, typeof(SvServiceRequest)));
                empty.SampleTables.Add(TableOf(context, typeof(AccountingEventLine)));
                var log = new RecordingLogger<CeoOverviewService>();
                List<DateTime> months = CeoHistory.PastMonths(DateTime.Today);
                Func<DateTime, CancellationToken, Task<decimal?>> receivables = (day, ct) => Task.FromResult<decimal?>(1m);

                foreach (string key in CeoHistory.Keys)
                {
                    List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(context, key, receivables, months, log, CancellationToken.None);
                    Assert.IsNotNull(points, key + ": the derivation failed:" + Environment.NewLine + string.Join(Environment.NewLine, log.Problems));
                }
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
                Assert.IsTrue(empty.Commands.Count >= 4, "each key ran its queries: " + empty.Commands.Count);
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

        internal static ServiceProvider InMemory(Action<IServiceCollection> configure = null)
        {
            ServiceCollection services = BaseServices();
            services.AddMemoryCache();
            string name = "ceo-" + Guid.NewGuid();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());
            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }

        /// <summary>Records for the two figures read from the records (the sales backlog and the stock value by warehouse); every date is relative to <paramref name="today"/>.</summary>
        private static async Task SeedFiguresAsync(ServiceProvider provider, DateTime today)
        {
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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

        private static FnNoteAgeingDto AgeingRow(string name, decimal current, decimal d31, decimal d61, decimal over90) =>
            new FnNoteAgeingDto { Name = name, Current = current, Days31To60 = d31, Days61To90 = d61, Over90 = over90, Total = current + d31 + d61 + over90 };

        [TestMethod]
        public async Task Overdue_Payables_Are_The_Buckets_Older_Than_Thirty_Days_Of_The_Payables_Ageing_Of_The_Ledger()
        {
            // review item 3: the figure is the finance module's own payables ageing (the notes of the financial statements), not an approximation from procurement invoices
            var modules = new Modules();
            modules.Payables.Add(AgeingRow("تأمین‌کننده الف", 100, 300, 200, 0));        // 500 overdue
            modules.Payables.Add(AgeingRow("تأمین‌کننده ب", 500, 0, 0, 0));              // all current: left out
            modules.Payables.Add(AgeingRow("تأمین‌کننده ج", 0, 50, 0, 900));             // 950 overdue, the biggest
            modules.Payables.Add(AgeingRow("تأمین‌کننده د", 10, 700, 0, 0));             // 700 overdue
            modules.Payables.Add(AgeingRow("تأمین‌کننده ه", 0, 0, 0, 0));                // owes nothing
            IPayableAgeingService ageing = PayableAgeing(modules);
            DateTime today = new DateTime(2026, 10, 25);

            List<CeoOverduePayable> rows = await CeoFigures.OverduePayablesAsync(ageing, today.AddHours(13), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "تأمین‌کننده ج", "تأمین‌کننده د", "تأمین‌کننده الف" }, rows.Select(r => r.Supplier).ToArray(), "suppliers with something overdue, the biggest overdue amount first");
            CollectionAssert.AreEqual(new[] { 950m, 700m, 500m }, rows.Select(r => r.Overdue).ToArray(), "overdue = 31-60 + 61-90 + over 90 days");
            Assert.AreEqual(10m, rows[1].Current, "the current bucket is carried but is not overdue");
            Assert.AreEqual(710m, rows[1].Total, "the total is everything owed to the supplier");
            Assert.AreEqual(50m, rows[0].Days31To60);
            Assert.AreEqual(0m, rows[0].Days61To90);
            Assert.AreEqual(900m, rows[0].Over90);
            Assert.AreEqual(today, modules.PayableDays.Single(), "the ageing is asked for the day without the time");

            Assert.AreEqual(2150m, await CeoFigures.PayablesOverdueAsync(ageing, today, CancellationToken.None), "the KPI is the sum of the overdue amounts");
            Assert.AreEqual(2, modules.PayableCalls, "one call of the ageing per figure");
        }

        [TestMethod]
        public async Task Overdue_Payables_Of_Equal_Amounts_Are_Ordered_By_Supplier_Name_And_No_Ageing_Is_Zero()
        {
            var modules = new Modules();
            modules.Payables.Add(AgeingRow("ب", 0, 100, 0, 0));
            modules.Payables.Add(AgeingRow("الف", 0, 0, 100, 0));
            IPayableAgeingService ageing = PayableAgeing(modules);

            List<CeoOverduePayable> rows = await CeoFigures.OverduePayablesAsync(ageing, new DateTime(2026, 10, 25), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "الف", "ب" }, rows.Select(r => r.Supplier).ToArray(), "a tie is broken by the name, so the list never changes order between two reads");

            IPayableAgeingService nothing = Proxy<IPayableAgeingService>(("PayableAgeingAsync", (Func<DateTime, CancellationToken, Task<List<FnNoteAgeingDto>>>)((day, ct) => Task.FromResult<List<FnNoteAgeingDto>>(null))));
            Assert.AreEqual(0, (await CeoFigures.OverduePayablesAsync(nothing, new DateTime(2026, 10, 25), CancellationToken.None)).Count, "no payable account or no fiscal year: nothing is owed");
            Assert.AreEqual(0m, await CeoFigures.PayablesOverdueAsync(nothing, new DateTime(2026, 10, 25), CancellationToken.None));
        }

        [TestMethod]
        public async Task The_Payables_KPI_Of_The_Overview_Is_The_Ageing_Figure_And_Its_Note_Says_What_Is_Not_In_It()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules();
                modules.Payables.Add(AgeingRow("تأمین‌کننده الف", 100, 300, 200, 0));
                modules.Payables.Add(AgeingRow("تأمین‌کننده ج", 0, 50, 0, 900));
                CeoDashboardDto dashboard = Dashboard();
                dashboard.Finance.CashAndBank = 1_000m;

                CeoOverviewDto overview = (await Overview(context, "CEO", dashboard, cache, log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

                CeoKpiDto payables = overview.Headline.First(k => k.Key == "payablesOverdue");
                Assert.AreEqual(1450m, payables.Value);
                Assert.AreEqual(CeoStatus.Danger, payables.Status, "1450 overdue is more than the 1000 in the bank");
                Assert.AreEqual(CeoDrillRef.FinPayablesOverdue, payables.DrillKey);
                Assert.IsTrue(payables.Note.Contains("۲ تأمین‌کننده"), payables.Note);
                Assert.IsTrue(payables.Note.Contains("پیش‌پرداخت") && payables.Note.Contains("علی‌الحساب"), "advances and progress payments are mentioned: " + payables.Note);
                Assert.AreEqual(CeoLinks.TreasuryPayments, payables.Link);
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
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
            // R1p D25: the chart averages the VERIFIED answers only; the fifth survey is answered but not verified, so it is counted apart and never averaged
            SlSurvey Survey(int id, SlSurveyStatus status, DateTimeOffset? answered, decimal? score, bool verified = true) => new SlSurvey { Id = id, Number = "SU-" + id, CustomerId = 1, DraftedOn = today, Status = status, AnsweredAt = answered, AverageScore = score, Verified = verified && status == SlSurveyStatus.Answered };
            db.Set<SlSurvey>().AddRange(
                Survey(1, SlSurveyStatus.Answered, now, 4.00m), Survey(2, SlSurveyStatus.Answered, now, 3.00m), Survey(3, SlSurveyStatus.Answered, now.AddDays(-40), 5.00m), Survey(4, SlSurveyStatus.Drafted, null, null),
                Survey(5, SlSurveyStatus.Answered, now, 1.00m, verified: false));
            await db.SaveChangesAsync();
        }

        internal static CeoChartDto ChartOf(CeoOverviewDto overview, string section, string chart) => overview.Sections.First(s => s.Key == section).Charts.First(c => c.Key == chart);

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
            Assert.AreEqual(2, survey.Labels.Count, "only the months with a verified answered survey");
            CollectionAssert.AreEqual(new[] { 5.00m, 3.50m }, survey.Series[0].Values.ToArray(), "the average of the verified answers of the month, oldest month first (the unverified score of 1 is left out)");
            StringAssert.Contains(survey.Note, "۱ پاسخ هنوز تأیید نشده", "the chart says how many answers wait for the verification");
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

        /// <summary>Stubbed modules whose payables ageing shows <paramref name="overdue"/> rial past thirty days (and one supplier that is current).</summary>
        private static Modules ModulesWithOverduePayables(decimal overdue)
        {
            var modules = new Modules();
            modules.Payables.Add(AgeingRow("تأمین‌کننده الف", 0, overdue - 400m, 0, 0));
            modules.Payables.Add(AgeingRow("تأمین‌کننده ب", 0, 0, 0, 400m));
            modules.Payables.Add(AgeingRow("تأمین‌کننده ج", 250m, 0, 0, 0));
            return modules;
        }

        private static CeoSnapshotService Snapshots(IUnitOfWork uow, string roles, CeoDashboardDto dashboard, Modules modules, ServiceProvider provider, IMemoryCache cache)
            => new CeoSnapshotService(uow, User(roles), DashboardService(dashboard, modules), PayableAgeing(modules), provider.GetRequiredService<IServiceScopeFactory>(), cache, null);

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
            Modules modules = ModulesWithOverduePayables(1100m);
            var service = Snapshots(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", dashboard, modules, provider, cache);

            // "snapshot now" stays on today (the night job stores the day that ended, see the hosted service tests)
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
            Assert.AreEqual(1100m, rows.First(r => r.Key == "payablesOverdue").Value, "the overdue payables are the figure of the overview: what the ledger ageing shows past thirty days");
            Assert.AreEqual(today, modules.PayableDays.Last(), "... aged on the day that is stored");
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

            ResponseDto<CeoSnapshotResultDto> refused = await Snapshots(uow, "WAREHOUSE", Dashboard(), new Modules(), provider, cache).TakeNowAsync(CancellationToken.None);
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(CeoText.NotAllowed, refused.Message);
            Assert.AreEqual(0, (await StoredAsync(provider)).Count, "nothing was stored");

            ResponseDto<CeoSnapshotResultDto> ceo = await Snapshots(uow, "CEO", Dashboard(), new Modules(), provider, cache).TakeNowAsync(CancellationToken.None);
            Assert.IsTrue(ceo.Success, ceo.Message);
            Assert.AreEqual(12, ceo.Data.Keys);
        }

        [TestMethod]
        public async Task The_Night_Job_Stores_The_Day_That_Ended_So_The_Last_Snapshot_Of_A_Persian_Month_Is_Its_True_Month_End()
        {
            // review item 6: the job runs at 00:30 of the next day; the figures are those of the end of the day that has just ended and are stored under that day
            Assert.AreEqual(new DateTime(2026, 10, 25), CeoHostedService.SnapshotDay(new DateTime(2026, 10, 26, 0, 30, 0)));
            Assert.AreEqual(new DateTime(2026, 12, 31), CeoHostedService.SnapshotDay(new DateTime(2027, 1, 1, 0, 30, 0)), "across the Gregorian year");
            Assert.AreEqual(new DateTime(2026, 11, 21), CeoHostedService.SnapshotDay(new DateTime(2026, 11, 22, 0, 30, 0)), "00:30 of 1405/09/01 stores 1405/08/30, the last day of Aban");

            using ServiceProvider provider = InMemory(services =>
            {
                services.AddScoped(_ => User("CEO"));
                services.AddScoped(_ => DashboardService(Dashboard(), new Modules()));
                services.AddScoped(_ => PayableAgeing(new Modules()));
                services.AddScoped<ICeoSnapshotService, CeoSnapshotService>();
            });
            var log = new RecordingLogger<CeoHostedService>();
            var job = new CeoHostedService(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), log);

            // Aban 1405 runs from 2026-10-23 to 2026-11-21; Azar starts on 2026-11-22. The job of the first night of Azar stores the last day of Aban.
            Assert.AreEqual(12, await job.StoreAsync(new DateTime(2026, 11, 22, 0, 30, 0), catchUp: false, CancellationToken.None));
            Assert.AreEqual(12, await job.StoreAsync(new DateTime(2026, 10, 24, 0, 30, 0), catchUp: false, CancellationToken.None));

            List<CeoKpiSnapshot> rows = await StoredAsync(provider);
            CollectionAssert.AreEqual(new[] { new DateTime(2026, 10, 23), new DateTime(2026, 11, 21) }, rows.Select(r => r.Date).Distinct().ToArray(), "the days that ended, not the days the job ran");
            List<CeoTrendPoint> points = CeoTrends.MonthEnds(rows.Where(r => r.Key == "cash").Select(r => (r.Date, r.Value)), new DateTime(2026, 11, 22));
            Assert.AreEqual(1, points.Count, "both days are in Aban");
            Assert.AreEqual(new DateTime(2026, 11, 21), points[0].Date, "the point of Aban is its last day");
            Assert.AreEqual("آبان ۱۴۰۵", CeoText.MonthLabel(points[0].Month));
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task At_Start_Up_A_Missing_Row_For_Yesterday_Is_Caught_Up_Once_And_A_Present_One_Is_Left_Alone()
        {
            // review item 19: when the server was down at 00:30, the first start stores yesterday once (with the figures of that moment)
            CeoDashboardDto dashboard = Dashboard();
            using ServiceProvider provider = InMemory(services =>
            {
                services.AddScoped(_ => User("CEO"));
                services.AddScoped(_ => DashboardService(dashboard, new Modules()));
                services.AddScoped(_ => PayableAgeing(new Modules()));
                services.AddScoped<ICeoSnapshotService, CeoSnapshotService>();
            });
            var log = new RecordingLogger<CeoHostedService>();
            var job = new CeoHostedService(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), log);
            DateTime now = new DateTime(2026, 10, 26, 8, 0, 0);

            Assert.AreEqual(12, await job.StoreAsync(now, catchUp: true, CancellationToken.None), "yesterday had no row: it is stored");
            List<CeoKpiSnapshot> rows = await StoredAsync(provider);
            Assert.IsTrue(rows.All(r => r.Date == new DateTime(2026, 10, 25)), "under yesterday, not today");
            Assert.AreEqual(48_200_000_000m, rows.First(r => r.Key == "cash").Value);

            dashboard.Finance.CashAndBank = 1_000m;
            Assert.AreEqual(0, await job.StoreAsync(now.AddHours(1), catchUp: true, CancellationToken.None), "yesterday has rows now: nothing to do");
            rows = await StoredAsync(provider);
            Assert.AreEqual(12, rows.Count);
            Assert.AreEqual(48_200_000_000m, rows.First(r => r.Key == "cash").Value, "the catch-up never overwrites a row the night job wrote");

            // the night job itself replaces the day's values (it is the better figure: the end of the day)
            Assert.AreEqual(12, await job.StoreAsync(new DateTime(2026, 10, 26, 0, 30, 0), catchUp: false, CancellationToken.None));
            rows = await StoredAsync(provider);
            Assert.AreEqual(1_000m, rows.First(r => r.Key == "cash").Value);
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task A_Failed_Night_Job_Is_Logged_As_An_Error_With_The_Persian_Date_And_Stops_Nothing()
        {
            var modules = new Modules { DashboardFails = true };
            using ServiceProvider provider = InMemory(services =>
            {
                services.AddScoped(_ => User("CEO"));
                services.AddScoped(_ => DashboardService(Dashboard(), modules));
                services.AddScoped(_ => PayableAgeing(modules));
                services.AddScoped<ICeoSnapshotService, CeoSnapshotService>();
            });
            var log = new RecordingLogger<CeoHostedService>();
            var job = new CeoHostedService(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), log);
            DateTime now = new DateTime(2026, 10, 26, 0, 30, 0);

            Assert.AreEqual(0, await job.StoreAsync(now, catchUp: false, CancellationToken.None), "a failure is reported as nothing stored, not thrown");
            Assert.AreEqual(0, await job.StoreAsync(now, catchUp: true, CancellationToken.None));

            Assert.AreEqual(2, log.Problems.Count(p => p.StartsWith("Error", StringComparison.Ordinal)), string.Join(Environment.NewLine, log.Problems));
            Assert.IsTrue(log.Problems.All(p => p.Contains(CeoText.Date(new DateTime(2026, 10, 25)))), "the log line names the day in Shamsi: " + string.Join(Environment.NewLine, log.Problems));
            Assert.AreEqual(0, (await StoredAsync(provider)).Count);

            // cancellation is not a failure: it propagates so that the host can stop
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { await job.StoreAsync(now, catchUp: false, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert.IsTrue(cancelled);
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
            var snapshots = Snapshots(uow, "PMO", dashboard, new Modules(), provider, cache);
            Assert.AreEqual(12, await snapshots.RunAsync(lastMonthDay, CancellationToken.None));
            dashboard.Finance.CashAndBank = 48_200_000_000m;

            var log = new RecordingLogger<CeoOverviewService>();
            var modules = new Modules();
            CeoOverviewService overview = Overview(uow, "CEO", dashboard, cache, log, modules);
            var asked = new List<string>();
            ITextToSpeech tts = Proxy<ITextToSpeech>(("SynthesizeAsync", (Func<string, CancellationToken, Task<TtsResult>>)((text, ct) =>
            {
                asked.Add(text);
                return Task.FromResult(new TtsResult { Ok = true, Audio = new byte[3244], Model = "mock" });
            })));
            IAiNoteService notes = Proxy<IAiNoteService>();   // the headline, a section and the alerts do not read a note
            var speech = new CeoSpeechService(User("CEO"), overview, notes, new PersianNumberWords(), tts, null, null);

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
            Assert.AreEqual(1, modules.PartsCalls, "the headline computed the overview, the section was spoken from that cached overview: no figure was computed again");
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

            Assert.AreEqual("Ceo:Jobs", CeoConfigKeys.Jobs);
            IConfiguration Config(string value) => new ConfigurationBuilder().AddInMemoryCollection(value == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Ceo:Jobs"] = value }).Build();
            Assert.IsTrue(CeoHostedService.Enabled(Config(null)), "on by default");
            Assert.IsTrue(CeoHostedService.Enabled(Config("true")));
            Assert.IsFalse(CeoHostedService.Enabled(Config("false")));
            Assert.IsFalse(CeoHostedService.Enabled(Config("False")));
            Assert.IsTrue(CeoHostedService.Enabled(null));
            Assert.AreEqual(TimeSpan.FromSeconds(30), CeoHostedService.CatchUpDelay, "the start-up catch-up waits for the application to settle");
        }

        // ==================================================================================== drill parameters, the colours of the projects and the notes of the charts

        private static Dictionary<string, string> Params(params string[] pairs)
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < pairs.Length; i += 2) result[pairs[i]] = pairs[i + 1];
            return result;
        }

        private static void AssertParams(Dictionary<string, string> expected, Dictionary<string, string> actual, string what)
        {
            if (expected == null)
            {
                Assert.IsNull(actual, what + ": the drill has no filter for this figure, so it carries no parameters");
                return;
            }
            Assert.IsNotNull(actual, what);
            CollectionAssert.AreEqual(expected.OrderBy(p => p.Key).ToList(), actual.OrderBy(p => p.Key).ToList(), what);
        }

        [TestMethod]
        public async Task Drill_Parameters_Open_The_Drill_On_The_Figure_On_The_Screen_And_Only_Where_The_Drill_Can_Filter_By_It()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                DateTime today = DateTime.Today;
                string day = CeoText.Iso(today), monthStart = CeoText.Iso(CeoTrends.MonthStart(today)), yearStart = CeoText.Iso(FinanceSeed.YearStart(FinanceSeed.PersianYearOf(today)));

                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log).GetOverviewAsync(true, CancellationToken.None)).Data;

                // the figures whose drill has to be filtered to list the records the figure counts (review item 6 of part A2): the red and the amber projects, the two figures of the year so far
                // and the five counts that are a part of what their drill lists by default (the purchase requests waiting for approval and not the approved ones too, the letters in approval,
                // the goodwill orders, the overdue NCRs, the instruments whose calibration is overdue and not only due soon); every other figure opens its drill unfiltered
                // (the backlog opens sl.orders_open without parameters: that drill lists the open orders by itself, and the total of its remainingAmount is the sales-order part of the figure)
                var kpis = new Dictionary<string, Dictionary<string, string>>
                {
                    ["projectsRed"] = Params("status", "danger"),
                    ["projectsAmber"] = Params("status", "warn"),
                    ["netProfitYtd"] = Params("from", yearStart, "to", day),
                    ["serviceWarrantyCostYtd"] = Params("from", yearStart, "to", day),
                    ["purchaseRequestsWaiting"] = Params("status", "submitted"),
                    ["lettersWaiting"] = Params("stage", "approval"),
                    ["serviceGoodwillWaiting"] = Params("goodwillOnly", "true"),
                    ["overdueNcrs"] = Params("onlyOverdue", "true"),
                    ["calibrationOverdue"] = Params("days", "0"),
                };
                foreach (CeoKpiDto kpi in overview.Sections.SelectMany(s => s.Kpis)) AssertParams(kpis.TryGetValue(kpi.Key, out Dictionary<string, string> expected) ? expected : null, kpi.DrillParams, "KPI " + kpi.Key);
                AssertParams(null, overview.Headline.First(k => k.Key == "salesBacklog").DrillParams, "the headline copy of salesBacklog");
                Assert.AreEqual(CeoDrillRef.PcPortfolio, Kpi(overview, "projectsRed").DrillKey);
                Assert.AreEqual(CeoDrillRef.PcPortfolio, Kpi(overview, "projectsAmber").DrillKey);
                Assert.AreEqual(CeoDrillRef.SlOrdersOpen, Kpi(overview, "salesBacklog").DrillKey);

                // the charts by month open the drill on the latest month so far (the first day of the Persian month to today) when the drill has a period; the five whose drills have none keep the open drill.
                // The stock flow chart draws the receipts and the issues of the month, and its drill lists the receipts only when it is told so (kind=receipt): the chart opens the list of what it shows first
                string[] withPeriod = { "revenueCost", "proposalResults", "oeeByWorkCenter", "stockFlowByMonth", "warrantyCostByMonth", "surveyScoreByMonth", "attendanceMonth" };
                string[] withoutPeriod = { "ordersByMonth", "ncrByMonth", "ordersAndPayments", "requestsByMonth", "cashFlow" };
                List<CeoChartDto> charts = overview.Sections.SelectMany(s => s.Charts).ToList();
                Assert.AreEqual(21, charts.Count);
                foreach (CeoChartDto chart in charts)
                    AssertParams(chart.Key == "stockFlowByMonth" ? Params("from", monthStart, "to", day, "kind", "receipt") : withPeriod.Contains(chart.Key) ? Params("from", monthStart, "to", day) : null, chart.DrillParams, "chart " + chart.Key);
                CollectionAssert.AreEquivalent(withPeriod, charts.Where(c => c.DrillParams != null).Select(c => c.Key).ToArray());
                foreach (string key in withoutPeriod) Assert.IsNotNull(charts.FirstOrDefault(c => c.Key == key), key + " is a chart of the overview");
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        private static PcPortfolioRowDto ProjectRow(int id, string spi, string cpi, decimal? index = 1m) => new PcPortfolioRowDto
        {
            ContractId = id, ContractTitle = "project " + id, ProjectCode = "P" + id, SpiStatus = spi, CpiStatus = cpi, Spi = index, PlannedPercent = 50, PhysicalPercent = 40,
        };

        [TestMethod]
        public async Task The_Project_Donut_Gives_Every_Project_One_Colour_Red_Before_Amber_And_Its_Slices_Add_Up_To_The_Projects()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                var modules = new Modules();
                modules.Portfolio = new List<PcPortfolioRowDto>
                {
                    ProjectRow(1, "red", "green"),        // red
                    ProjectRow(2, "amber", "red"),        // red: one red index decides, it is not counted as amber too
                    ProjectRow(3, "green", "amber"),      // amber
                    ProjectRow(4, "amber", "amber"),      // amber
                    ProjectRow(5, "green", "green"),      // green
                    ProjectRow(6, "none", "none"),        // no index yet: green
                };

                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

                CeoChartDto donut = ChartOf(overview, "projects", "projectStatus");
                CollectionAssert.AreEqual(new[] { "سبز", "زرد", "قرمز" }, donut.Labels.ToArray());
                CollectionAssert.AreEqual(new[] { 2m, 2m, 2m }, donut.Series[0].Values.ToArray(), "two green, two amber, two red");
                Assert.AreEqual((decimal)modules.Portfolio.Count, donut.Series[0].Values.Sum(), "every project once: the slices add up to the number of projects");
                Assert.AreEqual(modules.Portfolio.Count(PcReportService.IsRed), (int)donut.Series[0].Values[2], "the red slice is the red projects of the project control module");
                Assert.AreEqual(modules.Portfolio.Count(PcReportService.IsAmber), (int)donut.Series[0].Values[1]);
                Assert.IsTrue(donut.Note.Contains("هر پروژه یک بار") && donut.Note.Contains("قرمز نباشد"), donut.Note);
                Assert.IsTrue(Kpi(overview, "projectsAmber").Note.Contains("هیچ‌کدام قرمز نیست"), "the KPI says the same: " + Kpi(overview, "projectsAmber").Note);

                // the weakest projects by schedule index come first in the progress chart
                CeoChartDto progress = ChartOf(overview, "projects", "progressByProject");
                Assert.AreEqual(6, progress.Labels.Count);
                Assert.AreEqual(1, modules.PortfolioCalls);
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
            }
        }

        [TestMethod]
        public async Task The_Note_Of_The_Load_Chart_Counts_The_Weeks_Without_Capacity_That_The_Chart_Can_Only_Show_As_Zero()
        {
            ApplicationDbContext context = SqlServerContext(out EmptyDatabase empty, out IServiceScope scope);
            using (scope)
            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            {
                var log = new RecordingLogger<CeoOverviewService>();
                LoadCellDto Cell(decimal? percent, bool overloaded) => new LoadCellDto { LoadPercent = percent, Overloaded = overloaded };
                var modules = new Modules
                {
                    ProductionLoad = new LoadReportDto
                    {
                        Weeks = 4, Threshold = 110,
                        Rows =
                        {
                            new LoadRowDto { WorkCenterId = 1, Code = "CNC", Cells = { Cell(120m, true), Cell(null, true), Cell(null, true), Cell(40m, false) } },    // two weeks with load and no capacity
                            new LoadRowDto { WorkCenterId = 2, Code = "WELD", Cells = { Cell(null, false), Cell(30m, false) } },                                      // no capacity and no load: nothing to say
                            new LoadRowDto { WorkCenterId = 3, Code = "PAINT" },
                        },
                    },
                };

                CeoOverviewDto overview = (await Overview(context, "CEO", Dashboard(), cache, log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

                CeoChartDto load = ChartOf(overview, "production", "workCenterLoad");
                CollectionAssert.AreEqual(new[] { "CNC", "WELD", "PAINT" }, load.Labels.ToArray());
                CollectionAssert.AreEqual(new[] { 120m, 30m, 0m }, load.Series[0].Values.ToArray(), "the highest percentage of each work centre; a week without capacity has none and counts as zero");
                Assert.IsTrue(load.Note.Contains(CeoText.Number(2) + " هفته‌ی مرکز کار ظرفیت ندارد ولی بار دارد"), load.Note);
                Assert.IsTrue(load.Note.Contains("در شمارش هفته‌های اضافه‌بار آمده است"), "the KPI counts them: " + load.Note);
                Assert.IsTrue(Kpi(overview, "overloadWeeks").Note.Contains("هفته‌ی بدون ظرفیت"), Kpi(overview, "overloadWeeks").Note);
                Assert.AreEqual(1, modules.PartsCalls);
                Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));

                // no such week: the note is the plain one
                modules.ProductionLoad = new LoadReportDto { Rows = { new LoadRowDto { WorkCenterId = 1, Code = "CNC", Cells = { Cell(120m, true), Cell(40m, false) } } } };
                CeoChartDto plain = ChartOf((await Overview(context, "CEO", Dashboard(), cache, log, modules).GetOverviewAsync(true, CancellationToken.None)).Data, "production", "workCenterLoad");
                Assert.IsFalse(plain.Note.Contains("ظرفیت ندارد"), plain.Note);
            }
        }

        [TestMethod]
        public async Task The_Notes_Of_The_Ageing_And_The_Warehouse_Charts_Name_The_Difference_From_The_Figure_Beside_Them_Only_When_There_Is_One()
        {
            using ServiceProvider provider = InMemory();
            await SeedFiguresAsync(provider, DateTime.Today);
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var log = new RecordingLogger<CeoOverviewService>();
            var modules = new Modules();           // the ageing totals 100
            CeoDashboardDto dashboard = Dashboard();
            dashboard.Finance.ReceivablesOpen = 100m;      // the same as the ageing
            dashboard.StockValue = 1260m;                  // the same as the warehouses: 1000 + 250 + 10

            CeoOverviewDto same = (await Overview(uow, "CEO", dashboard, new MemoryCache(new MemoryCacheOptions()), log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

            CeoChartDto ageingChart = ChartOf(same, "finance", "receivablesAgeing");
            CollectionAssert.AreEqual(new[] { 10m, 20m, 30m, 40m }, ageingChart.Series[0].Values.ToArray());
            Assert.IsFalse(ageingChart.Note.Contains("اختلاف"), "the ageing and the open receivables agree: " + ageingChart.Note);
            CeoChartDto warehouseChart = ChartOf(same, "inventory", "stockValueByWarehouse");
            CollectionAssert.AreEqual(new[] { 1000m, 250m, 10m }, warehouseChart.Series[0].Values.ToArray());
            Assert.IsFalse(warehouseChart.Note.Contains("اختلاف"), "the warehouses add up to the stock value: " + warehouseChart.Note);

            // an invoice or a receipt dated after today is not in the ageing of today and is in the open receivables; a negative balance or an item without a cost is not in the warehouses
            dashboard.Finance.ReceivablesOpen = 175m;
            dashboard.StockValue = 1500m;
            CeoOverviewDto different = (await Overview(uow, "CEO", dashboard, new MemoryCache(new MemoryCacheOptions()), log, modules).GetOverviewAsync(true, CancellationToken.None)).Data;

            ageingChart = ChartOf(different, "finance", "receivablesAgeing");
            Assert.IsTrue(ageingChart.Note.Contains("اختلاف دارد") && ageingChart.Note.Contains(CeoText.ShortRial(75m)) && ageingChart.Note.Contains("بعد از امروز"), ageingChart.Note);
            warehouseChart = ChartOf(different, "inventory", "stockValueByWarehouse");
            Assert.IsTrue(warehouseChart.Note.Contains("اختلاف دارد") && warehouseChart.Note.Contains(CeoText.ShortRial(240m)) && warehouseChart.Note.Contains("موجودی منفی"), warehouseChart.Note);
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }
    }
}
