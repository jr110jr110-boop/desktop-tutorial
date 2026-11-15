using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Agents.Steps;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace MsTests
{
    /// <summary>
    /// The in-memory database accepts queries that SQL Server cannot translate, and the coordinator of the agents reconciles every step on a real SQL Server (CI fails when it reports an error). This test builds the
    /// context with the SQL Server provider and a dummy connection (the connection and the commands are swallowed: every query answers with no rows) and runs the discovery of every step of the catalogue:
    /// EF translates each query to SQL before it is sent, so a query SQL Server cannot express fails here with "could not be translated". The first-level queries are covered (the ones that depend on rows found
    /// are covered by the in-memory scenario tests).
    /// </summary>
    [TestClass]
    public class AgentSqlTranslationTests
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

        [TestMethod]
        public void The_Discovery_Of_Every_Step_Translates_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            int before = Swallow.Swallowed;
            var problems = new List<string>();
            int steps = 0;
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                foreach (IAgentStep step in scope.ServiceProvider.GetServices<IAgentStep>().OrderBy(s => s.Code, StringComparer.Ordinal))
                {
                    steps++;
                    try { step.DiscoverAsync(uow, DateTime.Today, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception ex)
                    {
                        string text = ex.ToString();
                        if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of"))
                            problems.Add(step.Code + ": " + ex.Message.Split('\n')[0]);
                    }
                }
            }
            Assert.IsTrue(steps >= 100, "steps found: " + steps);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(Swallow.Swallowed - before >= 100, "queries that reached the command stage (so they were translated): " + (Swallow.Swallowed - before));
        }

        /// <summary>
        /// Priority 4: the discovery of RES.PLAN.DRAFT and the hold on PLAN.BASELINE.CREATE run their big queries only when the pool has a resource, which the empty answers above never give;
        /// here the same queries are built and translated without being run.
        /// </summary>
        [TestMethod]
        public void The_Queries_Behind_The_Resource_Steps_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                DateTime today = DateTime.Today;
                IQueryable<int> live = ConFacts.Live(uow, today).Select(c => c.Id);
                IQueryable<int> before = ResFacts.DraftBeforeBaseline(uow, live);
                IQueryable<int> durations = ResFacts.WithDurations(uow), openPlans = ResFacts.WithOpenPlan(uow);
                IQueryable<int> afterChange = ResFacts.DraftAfterChange(uow).Where(id => durations.Contains(id) && !openPlans.Contains(id));
                string draft = ConFacts.Live(uow, today).Where(c => before.Contains(c.Id) || afterChange.Contains(c.Id)).OrderBy(c => c.Id).Take(200).ToQueryString();
                string baseline = ConFacts.Live(uow, today).Where(c => !openPlans.Contains(c.Id) && !before.Contains(c.Id)).OrderBy(c => c.Id).Take(30).ToQueryString();
                Assert.IsTrue(draft.Contains("PcResourcePlans") && draft.Contains("PcChangeRequests"), draft);
                Assert.IsTrue(draft.Contains("PcBaselines"), "the change draft needs the baseline of the request: " + draft);
                Assert.IsTrue(baseline.Contains("PcResourcePlans"), baseline);
                string match = ResFacts.WithMatch(uow).ToQueryString();
                Assert.IsTrue(match.Contains("PcResources") && match.Contains("PcActivities"), match);

                // the queries of the three plan steps (Draft, Submitted and Returned) are built by the same functions that GapsAsync and the approve step run, and translated for every status;
                // the later ones (activities, assigned, skipped, manager) are fed sample ids because an empty reader never gets that far in this host
                foreach (PcResourcePlanStatus status in new[] { PcResourcePlanStatus.Draft, PcResourcePlanStatus.Submitted, PcResourcePlanStatus.Returned })
                {
                    string plans = ResFacts.PlanRows(uow, status, today).ToQueryString();
                    Assert.IsTrue(plans.Contains("PcResourcePlans") && plans.Contains("TOP("), status + ": " + plans);
                }
                int[] ids = { 1, 2, 3 };
                Assert.IsTrue(ResFacts.ActivityRows(uow, ids).ToQueryString().Contains("PcActivities"));
                Assert.IsTrue(ResFacts.AssignedRows(uow, ids).ToQueryString().Contains("PcActivityResources"));
                Assert.IsTrue(ResFacts.SkippedRows(uow, ids).ToQueryString().Contains("PcResourcePlanSkips"));
                Assert.IsTrue(ResFacts.ManagerRows(uow, ids).ToQueryString().Contains("PcContractTerms"));
                Assert.IsTrue(ConFacts.Live(uow, today).Where(c => ids.Contains(c.Id)).ToQueryString().Length > 0);
            }
        }
    }
}
