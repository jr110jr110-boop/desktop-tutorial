using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Extentions;
using DataLayer.Context;
using IocConfig;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ViewModels.Identity.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AuditSeat;
using Services.NasbNiro.Quality;

namespace MsTests
{
    /// <summary>
    /// The in-memory database accepts queries that SQL Server cannot translate; the application runs on SQL Server. This test builds the context with the SQL Server provider and a dummy connection, lets the connection
    /// and the commands be swallowed (every query answers with no rows), and runs every rule: EF translates each query to SQL before it is sent, so a query that SQL Server cannot express fails here with
    /// "could not be translated". The first-level query of each rule is covered (the queries that depend on rows found are covered by the in-memory tests of the rule).
    /// </summary>
    [TestClass]
    public class PackageP9SqlTranslationTests
    {
        private sealed class Swallow : DbCommandInterceptor
        {
            public static int Swallowed;
            private static DbDataReader Empty() { Interlocked.Increment(ref Swallowed); return new DataTable().CreateDataReader(); }

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => InterceptionResult<DbDataReader>.SuppressWithResult(Empty());

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
                new ValueTask<InterceptionResult<DbDataReader>>(InterceptionResult<DbDataReader>.SuppressWithResult(Empty()));

            public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) => InterceptionResult<object>.SuppressWithResult(null);

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
                new ValueTask<InterceptionResult<object>>(InterceptionResult<object>.SuppressWithResult(null));
        }

        private sealed class NoConnect : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
        }

        private static ServiceProvider SqlServerProvider()
        {
            var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", reloadOnChange: false, optional: false).Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<SiteSettings>(options => configuration.Bind(options));
            SiteSettings site = services.GetSiteSettings();
            site.ActiveDatabase = ActiveDatabase.SqlServer;
            site.ConnectionStrings.SqlServer.ApplicationDbContextConnection = "Server=tcp:127.0.0.1,1;Database=p9translation;User Id=x;Password=x;Encrypt=False;Pooling=False;Connect Timeout=1";
            services.AddRequiredEfInternalServices(site);
            services.AddDbContext<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(site);
                optionsBuilder.UseInternalServiceProvider(serviceProvider);
                optionsBuilder.AddInterceptors(new Swallow(), new NoConnect());
            });
            return services.BuildServiceProvider();
        }

        [TestMethod]
        public void Every_Rule_Query_Translates_To_Sql_Server()
        {
            var problems = new List<string>();
            int before = Swallow.Swallowed;
            using (ServiceProvider provider = SqlServerProvider())
            using (IServiceScope scope = provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (AusRuleDef rule in AusCatalog.All)
                {
                    foreach (bool incremental in new[] { false, true })
                    {
                        var ctx = new AusContext
                        {
                            Uow = uow, Services = null, Today = now.LocalDateTime.Date, Now = now, Max = 2000, Ct = CancellationToken.None,
                            Settings = AusSettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value),
                            Since = incremental ? (DateTimeOffset?)now.AddDays(-1) : null,
                        };
                        try { rule.Query(ctx).GetAwaiter().GetResult(); }
                        catch (Exception ex)
                        {
                            string text = ex.ToString();
                            if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of"))
                                problems.Add(rule.Code + (incremental ? " (incremental)" : "") + ": " + ex.Message.Split('\n')[0]);
                        }
                    }
                }
            }
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(Swallow.Swallowed - before >= 100, "queries that reached the command stage (so they were translated): " + (Swallow.Swallowed - before));
        }

        [TestMethod]
        public void The_Read_Paths_Of_The_Audit_Services_Translate_To_Sql_Server()
        {
            var host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            int before = Swallow.Swallowed;
            var problems = new List<string>();
            void Try(string name, Func<IServiceProvider, Task> call)
            {
                try { host.As(PackageP9AuditSeatTests.Qa1, async sp => { await call(sp); return 0; }).GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    string text = ex.ToString();
                    if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of"))
                        problems.Add(name + ": " + ex.Message.Split('\n')[0]);
                }
            }
            var ct = CancellationToken.None;
            Try("status", sp => sp.GetRequiredService<IAusFindingService>().GetStatusAsync(ct));
            Try("findings", sp => sp.GetRequiredService<IAusFindingService>().GetAsync(new ViewModels.NasbNiro.AuditSeatDtos.AusFindingQuery { OpenOnly = true, Overdue = true, Mine = true, Severity = "3", Status = "1", ProcessCode = "QUALITY", RuleCode = "AUD-NC-001", Text = "x", Limit = 50 }, ct));
            Try("finding", sp => sp.GetRequiredService<IAusFindingService>().GetOneAsync(1, ct));
            Try("rules", sp => sp.GetRequiredService<IAusRunService>().GetRulesAsync(ct));
            Try("runs", sp => sp.GetRequiredService<IAusRunService>().GetRunsAsync(20, ct));
            Try("reports", sp => sp.GetRequiredService<IAusReportService>().GetAsync(null, 20, ct));
            Try("report", sp => sp.GetRequiredService<IAusReportService>().GetOneAsync(1, ct));
            Try("report content", sp => ((AusReportService)sp.GetRequiredService<IAusReportService>()).BuildContentAsync(DateTime.Today.AddDays(-7), DateTime.Today, ct));
            Try("programs", sp => sp.GetRequiredService<IAusProgramService>().GetAsync(ct));
            Try("process audits", sp => sp.GetRequiredService<IAusAuditService>().GetAsync(50, ct));
            Try("recheck of a finding", sp => sp.GetRequiredService<IAusRunService>().StillDeviatesAsync(new Entities.Model.NasbNiro.AuditSeat.AusFinding { RuleCode = "AUD-NC-001", ObjectKey = "k" }, ct));
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(Swallow.Swallowed - before >= 10, "queries that reached the command stage: " + (Swallow.Swallowed - before));
        }

        [TestMethod]
        public void The_New_Tables_Map_To_Sql_Server_Without_Model_Errors()
        {
            using (ServiceProvider provider = SqlServerProvider())
            using (IServiceScope scope = provider.CreateScope())
            {
                var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;
                foreach (string table in new[] { "AusPrograms", "AusProgramItems", "AusRuleStates", "AusRuns", "AusFindings", "AusFindingEvents", "AusReports", "AusAudits", "AusAuditChecks" })
                    Assert.IsTrue(model.GetEntityTypes().Any(e => e.GetTableName() == table), table);
            }
        }
    }
}
