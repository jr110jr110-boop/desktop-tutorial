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
using Services.NasbNiro.ProjectControl;
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
            Services.NasbNiro.Engineering.EnControl.Probe = true;   // the engineering steps reach their main queries (M8): the empty probe database has no start day and no approved list
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
            Services.NasbNiro.Engineering.EnControl.Probe = false;
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

        /// <summary>Runs a query against the swallowing connection: only a failure to TRANSLATE counts (an empty reader cannot materialise rows).</summary>
        private static void Probe(string what, Action run)
        {
            try { run(); }
            catch (Exception ex)
            {
                string text = ex.ToString();
                if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of"))
                    Assert.Fail(what + ": " + ex.Message.Split('\n')[0]);
            }
        }

        /// <summary>The Sepidar transfer: the queries behind prepare (a change check per kind), send, the fix gate and the approval gate, and the dashboard alert, translated for every kind without being run.</summary>
        [TestMethod]
        public void The_Queries_Behind_The_Sepidar_Transfer_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                foreach (Services.NasbNiro.Finance.Sp.SpSource source in Services.NasbNiro.Finance.Sp.SpCatalog.All)
                {
                    string need = Services.NasbNiro.Finance.Sp.SpFacts.NeedingWork(uow, source, DateTime.Today).OrderBy(r => r.Id).Select(r => r.Id).Take(Services.NasbNiro.Finance.Sp.SpFacts.Cap).ToQueryString();
                    Assert.IsTrue(need.Contains("SpOutboxItems") && need.Contains("TOP("), source.Code + ": " + need);
                    Assert.IsTrue(need.Contains("RowVersion"), source.Code + " compares the row version in SQL: " + need);
                    string gone = Services.NasbNiro.Finance.Sp.SpFacts.NoLongerFinal(uow, source).OrderBy(i => i.Id).Take(Services.NasbNiro.Finance.Sp.SpFacts.Cap).ToQueryString();
                    Assert.IsTrue(gone.Contains("SpOutboxItems") && gone.Contains("NOT EXISTS"), source.Code + " finds the items whose source is no longer final in SQL: " + gone);
                }
                string sendable = Services.NasbNiro.Finance.Sp.SpFacts.Sendable(uow, DateTimeOffset.Now).OrderBy(i => i.Id).Take(200).Select(i => new { i.Id, i.SourceNumber, i.AttemptCount, i.Title }).ToQueryString();
                Assert.IsTrue(sendable.Contains("SpTransferTypes") && sendable.Contains("SpOutboxItems"), sendable);
                Assert.IsTrue(Services.NasbNiro.Finance.Sp.SpFacts.NeedingFix(uow).OrderBy(i => i.Id).Take(200).ToQueryString().Contains("SpOutboxItems"));
                Assert.IsTrue(Services.NasbNiro.Finance.Sp.SpFacts.OfEnabled(uow, Entities.Model.NasbNiro.Finance.SpOutboxStatus.AwaitingApproval).OrderBy(i => i.Id).Take(200).ToQueryString().Contains("SpTransferTypes"));
                // the awaited ones run against the swallowing connection (the first-level queries of the steps)
                foreach (IAgentStep step in scope.ServiceProvider.GetServices<IAgentStep>().Where(x => x.Code.StartsWith("SP.OUTBOX.", StringComparison.Ordinal)))
                    Probe(step.Code, () => step.DiscoverAsync(uow, DateTime.Today, CancellationToken.None).GetAwaiter().GetResult());
                string stale = Services.NasbNiro.Finance.Sp.SpFacts.StaleSendingToRecover(uow, DateTimeOffset.Now, false).OrderBy(i => i.Id).Take(50).ToQueryString();
                Assert.IsTrue(stale.Contains("SpOutboxItems") && stale.Contains("LastAttemptAt"), stale);
                string staleOff = Services.NasbNiro.Finance.Sp.SpFacts.StaleSendingToRecover(uow, DateTimeOffset.Now, true).OrderBy(i => i.Id).Take(50).ToQueryString();
                Assert.IsTrue(staleOff.Contains("SpTransferTypes") && staleOff.Contains("SpOutboxItems"), staleOff);
                Probe("sendable count", () => Services.NasbNiro.Finance.Sp.SpFacts.Sendable(uow, DateTimeOffset.Now).CountAsync().GetAwaiter().GetResult());
                Probe("failed count", () => uow.Set<Entities.Model.NasbNiro.Finance.SpOutboxItem>().AsNoTracking().CountAsync(i => i.Status == Entities.Model.NasbNiro.Finance.SpOutboxStatus.Failed && (true || !i.PayrollRestricted)).GetAwaiter().GetResult());
            }
        }

        /// <summary>The three gap steps (the returned inquiry, the service request and the issue draft): their discovery queries translate, including the ones that an empty answer never reaches.</summary>
        [TestMethod]
        public void The_Queries_Behind_The_Gap_Steps_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                string[] codes = { "PROC.INQUIRY.FIX", "SV.REQUEST.ASSIGN", "SV.REQUEST.RESOLVE", "SV.REQUEST.CLOSE", "WH.ISSUE.DRAFT" };
                foreach (string code in codes)
                {
                    IAgentStep step = scope.ServiceProvider.GetServices<IAgentStep>().Single(x => x.Code == code);
                    Probe(code, () => step.DiscoverAsync(uow, DateTime.Today, CancellationToken.None).GetAwaiter().GetResult());
                }
                // the stock plan of the issue draft runs only for a request that was found: translate it with sample ids
                int[] ids = { 1, 2 };
                string balances = uow.Set<Entities.Model.NasbNiro.Inventory.StockBalance>().AsNoTracking().Where(b => ids.Contains(b.ItemId) && b.LotId == null && b.Quantity > 0)
                    .OrderBy(b => b.WarehouseId).ThenBy(b => b.Id).Select(b => new { b.ItemId, b.WarehouseId, b.LocationId, b.Quantity }).ToQueryString();
                Assert.IsTrue(balances.Contains("StockBalances"), balances);
            }
        }

        /// <summary>Phase 4c: the queries behind the load check, the levelling steps and the load model translate to SQL Server without being run.</summary>
        [TestMethod]
        public void The_Queries_Behind_The_Load_And_Levelling_Steps_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                DateTime today = DateTime.Today;
                string open = LvlFacts.OpenOverloads(uow).ToQueryString();
                Assert.IsTrue(open.Contains("PcResourceOverloads") && open.Contains("TOP("), open);
                string need = LvlFacts.NeedingProposal(uow, DateTimeOffset.Now, DateTimeOffset.Now).ToQueryString();
                Assert.IsTrue(need.Contains("PcResourceOverloads") && need.Contains("PcLevelingProposals"), need);
                foreach (PcLevelingStatus status in new[] { PcLevelingStatus.Proposed, PcLevelingStatus.Applied })
                {
                    string proposals = LvlFacts.Proposals(uow, status).ToQueryString();
                    Assert.IsTrue(proposals.Contains("PcLevelingProposals") && proposals.Contains("TOP("), status + ": " + proposals);
                }
                Assert.IsTrue(LvlFacts.ApprovedPlanIds(uow).ToQueryString().Contains("PcResourcePlans"));
                // the cheap questions of the check step and the change stamp: awaited against the swallowing connection, so a query the server could not translate throws here
                var problems = new List<string>();
                foreach (Func<Task> ask in new Func<Task>[] { () => LvlFacts.HasLoadAsync(uow, CancellationToken.None), () => LvlFacts.AnyOpenAsync(uow, CancellationToken.None), () => LvlFacts.StampAsync(uow, CancellationToken.None), () => LvlFacts.LatestChangeAsync(uow, CancellationToken.None) })
                    try { ask().GetAwaiter().GetResult(); }
                    catch (Exception ex) { string text = ex.ToString(); if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of")) problems.Add(ex.Message.Split('\n')[0]); }
                Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
                int[] ids = { 1, 2, 3 };
                string plans = LoadModel.PlanQuery(uow, ids, true).ToQueryString();
                Assert.IsTrue(plans.Contains("PcResourcePlans"), plans);
                Assert.IsTrue(LoadModel.RowQuery(uow, ids, 5).ToQueryString().Contains("PcActivityResources"));
                Assert.IsTrue(LoadModel.RowQuery(uow, ids, null).ToQueryString().Contains("PcActivityResources"));
                Assert.IsTrue(LoadModel.ActivityQuery(uow, ids).ToQueryString().Contains("PcActivities"));
            }
        }

        [TestMethod]
        public void The_Queries_Behind_The_Resource_Cost_And_Production_Link_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                DateTime today = DateTime.Today;
                string need = ResCostFacts.NeedingProposal(uow).ToQueryString();
                Assert.IsTrue(need.Contains("PcResourcePlans") && need.Contains("PcResourceCostProposals"), need);
                Assert.IsTrue(ResCostFacts.WithOpenProposals(uow).ToQueryString().Contains("PcResourceCostProposals"));
                int[] ids = { 1, 2, 3 };
                string counts = PcResourceReportSection.OverloadCounts(uow, ids).ToQueryString();
                Assert.IsTrue(counts.Contains("PcResourceOverloads") && counts.Contains("GROUP BY"), counts);
                Assert.IsTrue(LoadModel.RowQuery(uow, ids, null, new[] { 4, 5 }).ToQueryString().Contains("PcActivityResources"));
                // the awaited queries run against the swallowing connection, so one that the server could not translate throws here
                var problems = new List<string>();
                var model = new LoadModel();
                foreach (Func<Task> ask in new Func<Task>[]
                {
                    () => PcWorkCentreLink.LinksAsync(uow, CancellationToken.None),
                    () => PcWorkCentreLink.ProjectLayerAsync(uow, today, today, today.AddDays(30), new List<PcWorkCentreLink.Link> { new PcWorkCentreLink.Link { ResourceId = 1, WorkCenterId = 1, Unit = PcResourceUnit.Hour } }, CancellationToken.None),
                    () => PcWorkCentreLink.AddProductionCellsAsync(uow, model, today, today, today.AddDays(30), CancellationToken.None),
                    () => PcResourceActuals.LoadAsync(uow, 1, today, CancellationToken.None),
                    // the queries that an empty database would skip behind its own empty-list checks
                    () => PcResourceData.EffectiveRatesAsync(uow, new[] { 1, 2, 3 }, today, CancellationToken.None),
                    () => Services.NasbNiro.Production.MfScheduler.ComputeAsync(uow, today, CancellationToken.None, true),
                    () => LvlFacts.StampAsync(uow, CancellationToken.None),
                    () => PcResourceReportSection.BuildAsync(uow, 1, today, CancellationToken.None),
                    () => LvlFacts.LatestChangeAsync(uow, CancellationToken.None),
                })
                    try { ask().GetAwaiter().GetResult(); }
                    catch (Exception ex) { string text = ex.ToString(); if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression") || text.Contains("Translation of")) problems.Add(ex.Message.Split('\n')[0]); }
                Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            }
        }
        /// <summary>ENG-E1 part 3: the discoveries of the steps added in S5, S6, S9 and S10 (and the changed ones) translate to SQL Server; the connection swallows every command, so a query the server could not express throws here.</summary>
        [TestMethod]
        public void The_Discoveries_Of_The_Engineering_E1_Part3_Steps_Translate_To_Sql_Server()
        {
            AppHost host = AppHost.SqlTranslationOnly(new Swallow(), new NoConnect());
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var problems = new List<string>();
                Services.NasbNiro.Engineering.EnControl.Probe = true;
                foreach (string code in new[] { "ENG.OPINION.INPUT", "ENG.CASE.OPEN", "ENG.DIST.DRAFT", "ENG.DIST.NOTIFY", "ENG.DIST.ACK", "ENG.INFO.ANSWER", "ENG.PLATFORM.REGISTER", "ENG.PLATFORM.STANDARD", "ENG.REF.DRAFT", "ENG.PLATFORM.ASSESS", "ENG.PLATFORM.COMPLETE", "ENG.PLATFORM.CONFIRM" })
                {
                    IAgentStep step = scope.ServiceProvider.GetServices<IAgentStep>().Single(x => x.Code == code);
                    try { step.DiscoverAsync(uow, DateTime.Today, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception ex) { string text = ex.ToString(); if (text.Contains("could not be translated") || text.Contains("cannot be translated") || text.Contains("LINQ expression")) problems.Add(code + ": " + ex.Message); }   // the swallowing reader has no columns: only a translation failure counts
                }
                Services.NasbNiro.Engineering.EnControl.Probe = false;
                Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            }
        }
    }
}
