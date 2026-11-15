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
    }
}
