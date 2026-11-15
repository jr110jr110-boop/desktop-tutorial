using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Integrity;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>
    /// Agent layer foundation: the reconciler (tasks follow the documents), the executor (an agent runs only its own steps, refusals are counted, waiting is not), the gate inbox, the return, the master board, the
    /// step switches and the safety rules. The steps here are fakes registered only in this test host; the real steps (procurement) have their own tests.
    /// </summary>
    [TestClass]
    public class AgentFrameworkTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(31, "ceo", "CEO");
        private static readonly ClaimsPrincipal Auditor = AppHost.Principal(32, "auditor", "AUDITOR");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(51, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Planner2 = AppHost.Principal(52, "planner.two", "PLANNING");
        private static readonly ClaimsPrincipal Warehouse = AppHost.Principal(61, "storekeeper", "WAREHOUSE");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(26, "buyer", "COMMERCIAL");
        // a seat with a human gate role: the inbox is not for seats
        private static readonly ClaimsPrincipal SeatWithGateRole = AppHost.Principal(77, "seat.ag-plan", "PLANNING", "PMO");

        // ------------------------------------------------------------------------------------------------ the fake steps

        /// <summary>What the fake documents are, and what the fake agent step does when it runs.</summary>
        private sealed class World
        {
            public readonly ConcurrentDictionary<int, string> AgentDocs = new ConcurrentDictionary<int, string>();
            public readonly ConcurrentDictionary<int, string> GateDocs = new ConcurrentDictionary<int, string>();
            public readonly ConcurrentDictionary<int, string> ReturnableDocs = new ConcurrentDictionary<int, string>();
            public Func<int, AgentStepResult> Behave = id => AgentStepResult.Done("FakeOutput", id * 10, "OUT-" + id, "پیش‌نویس ساخته شد");
            public readonly List<int> Runs = new List<int>();
            public string PrincipalSeen;
            public string StepSeen;
            public readonly List<string> Returned = new List<string>();
        }

        private const string AgentStepCode = "TEST.AGENT.MAKE";
        private const string GateStepCode = "TEST.GATE.APPROVE";
        private const string ReturnableStepCode = "TEST.GATE.RETURNABLE";

        private sealed class FakeAgentStep : IAgentStep
        {
            private readonly World _w;
            public FakeAgentStep(World w) { _w = w; }
            public string Code => AgentStepCode;
            public int Station => 6;
            public string AgentCode => AgentCodes.Proc;
            public string Title => "ساخت سند آزمایشی";
            public AgentStepKind Kind => AgentStepKind.Agent;
            public string FallbackRoles => "COMMERCIAL";

            public Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
                Task.FromResult(_w.AgentDocs.OrderBy(d => d.Key).Select(d => new AgentWorkItem { TargetType = "FakeDoc", TargetId = d.Key, TargetNumber = d.Value, ContractId = 7, Summary = "موجودی کم است" }).ToList());

            public Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
            {
                lock (_w.Runs) _w.Runs.Add(item.TargetId);
                _w.PrincipalSeen = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User?.Identity?.Name;
                _w.StepSeen = AgentStepContext.Current;
                return Task.FromResult(_w.Behave(item.TargetId));
            }

            public AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/fake/" + item.TargetId, Title = "ساخت سند آزمایشی " + item.TargetNumber };
        }

        private class FakeGateStep : IAgentStep
        {
            protected readonly World W;
            public FakeGateStep(World w) { W = w; }
            public virtual string Code => GateStepCode;
            public int Station => 6;
            public string AgentCode => AgentCodes.Proc;
            public string Title => "تایید سند آزمایشی";
            public AgentStepKind Kind => AgentStepKind.HumanGate;
            public string FallbackRoles => null;
            protected virtual ConcurrentDictionary<int, string> Docs => W.GateDocs;

            public Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
                Task.FromResult(Docs.OrderBy(d => d.Key).Select(d => new AgentWorkItem { TargetType = "FakeDoc", TargetId = d.Key, TargetNumber = d.Value, ContractId = 7, Summary = "منتظر تایید" }).ToList());

            public Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) => throw new InvalidOperationException("a gate is not run by an agent");

            public AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "planning, pmo", Link = "/fake/" + item.TargetId, Title = "تایید سند " + item.TargetNumber, HasOfficeInboxItem = false };
        }

        private sealed class FakeReturnableGateStep : FakeGateStep, IAgentReturnable
        {
            public FakeReturnableGateStep(World w) : base(w) { }
            public override string Code => ReturnableStepCode;
            protected override ConcurrentDictionary<int, string> Docs => W.ReturnableDocs;

            public Task<ResponseDto<bool>> ReturnAsync(AgentWorkItem item, string reason, IServiceProvider services, CancellationToken ct)
            {
                // the existing return action of the document: the document leaves the gate
                W.Returned.Add(item.TargetId + ":" + reason + ":" + services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User?.Identity?.Name);
                W.ReturnableDocs.TryRemove(item.TargetId, out _);
                return Task.FromResult(ResponseDtoHelper.Ok(true));
            }
        }

        private sealed class Env
        {
            public AppHost Host;
            public World World;
        }

        private static Env NewEnv(string name, IDictionary<string, string> settings = null)
        {
            var world = new World();
            AppHost host = AppHost.WithServices("Agents_" + name + "_" + Guid.NewGuid().ToString("N"), s =>
            {
                s.AddSingleton(world);
                s.AddScoped<IAgentStep>(sp => new FakeAgentStep(world));
                s.AddScoped<IAgentStep>(sp => new FakeGateStep(world));
                s.AddScoped<IAgentStep>(sp => new FakeReturnableGateStep(world));
            }, settings);
            return new Env { Host = host, World = world };
        }

        private static AiActingUser AgentUser(string code)
        {
            AiSeatDefinition def = AiSeatCatalog.Find(code);
            return new AiActingUser { UserId = 100 + AiSeatCatalog.All.ToList().IndexOf(def), UserName = def.UserName, DisplayName = def.Title, Roles = def.Roles };
        }

        /// <summary>Runs as the service user of a seat (the way the seat runner does) with the scope of that run.</summary>
        private static Task<T> AsAgent<T>(Env e, string code, Func<IServiceProvider, Task<T>> body) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), AgentUser(code), code, body);

        private static Task<AiSeatJobResult> RunJob(Env e, string code) =>
            AsAgent(e, code, sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static Task<AgentReconcileResult> Reconcile(Env e, string agent = null) =>
            e.Host.As(Pmo, p => p.GetRequiredService<IAgentReconciler>().ReconcileAsync(agent, Ct));

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static AgentTask TaskOf(Env e, string step, int target) => Tasks(e).Single(t => t.StepCode == step && t.TargetId == target && t.State < AgentTaskState.Done);
        private static List<AgentTaskEvent> Events(Env e, int taskId) => e.Host.Read(u => u.Set<AgentTaskEvent>().AsNoTracking().Where(x => x.TaskId == taskId).OrderBy(x => x.Id).ToList());
        private static int AgentAudits(Env e) => e.Host.Read(u => u.Set<AiAuditEvent>().Count(a => a.Kind == AiAuditKinds.Agent));

        private static void Mutate(Env e, int taskId, Action<AgentTask> change) =>
            e.Host.Write(u => { change(u.Set<AgentTask>().Single(t => t.Id == taskId)); u.SaveChanges(); }, Pmo);

        private static void SeatOff(Env e, string code, bool enabled) =>
            e.Host.Write(u =>
            {
                AiSeat row = u.Set<AiSeat>().FirstOrDefault(s => s.Code == code);
                if (row == null) { AiSeatDefinition d = AiSeatCatalog.Find(code); row = AppHost.Add(u, new AiSeat { Code = code, Title = d.Title, ServiceUserName = d.UserName, Roles = d.RolesText, Schedule = d.Schedule, OutputKind = d.OutputKind }); }
                row.Enabled = enabled;
                u.SaveChanges();
            }, Pmo);

        // ------------------------------------------------------------------------------------------------ the catalogue, the seats, the names

        [TestMethod]
        public void The_Thirteen_Agents_Are_Seats_With_Persian_Titles_That_Say_Agent_And_The_Master_Holds_No_Role()
        {
            string[] codes = { "AG-MASTER", "AG-SALES", "AG-CONTRACT", "AG-INIT", "AG-PLAN", "AG-ENG", "AG-PROC", "AG-WH", "AG-MF", "AG-QC", "AG-SHIP", "AG-SERVICE", "AG-FIN" };
            CollectionAssert.AreEqual(codes, AiSeatCatalog.All.Where(s => s.Code.StartsWith("AG-")).Select(s => s.Code).ToArray());
            Assert.AreEqual(12, AgentCodes.Process.Count);
            CollectionAssert.AreEqual(codes.Skip(1).ToArray(), AgentCodes.Process.ToArray());
            foreach (string code in codes)
            {
                AiSeatDefinition d = AiSeatCatalog.Find(code);
                Assert.AreEqual("poll 2", d.Schedule, code);
                Assert.IsFalse(d.UsesModel, code);
                StringAssert.Contains(d.Title, "ایجنت", code);
                Assert.IsFalse(d.Title.Contains("عامل"), code);
                Assert.IsTrue(AgentCodes.IsAgent(code));
            }
            CollectionAssert.AreEqual(new string[0], AiSeatCatalog.Find("AG-MASTER").Roles);
            CollectionAssert.AreEqual(new[] { "COMMERCIAL" }, AiSeatCatalog.Find("AG-PROC").Roles);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, AiSeatCatalog.Find("AG-WH").Roles);
            CollectionAssert.AreEqual(new[] { "PLANNING" }, AiSeatCatalog.Find("AG-PLAN").Roles);
            CollectionAssert.AreEqual(new[] { "SERVICE" }, AiSeatCatalog.Find("AG-SERVICE").Roles);
            CollectionAssert.AreEqual(new[] { "FINANCIAL", "COMMERCIAL" }, AiSeatCatalog.Find("AG-FIN").Roles);
            CollectionAssert.AreEqual(new[] { "ENGINEER" }, AiSeatCatalog.Find("AG-ENG").Roles);
            CollectionAssert.AreEqual(new[] { "SALES" }, AiSeatCatalog.Find("AG-SALES").Roles);   // the maker lines of the sales services (SalesRoles.Writers); the agent decides and approves nothing   // enough for the instantiation (PbRoles.Requesters); the agent signs and releases nothing
            // no agent holds a role that approves what its own station makes
            foreach (AiSeatDefinition d in AiSeatCatalog.All.Where(s => s.Code.StartsWith("AG-")))
                foreach (string approving in new[] { "CEO", "PMO", "FINANCEMANAGER", "PROJECTMANAGER", "QA" }) CollectionAssert.DoesNotContain(d.Roles, approving, d.Code);
            // one PMO or CEO user may switch an agent off at once; the coordinator stays two-person
            foreach (string code in AgentCodes.Process) Assert.IsTrue(SafetyProfile.IsDraftingSeat(code), code);
            Assert.IsFalse(SafetyProfile.IsDraftingSeat("AG-MASTER"));
            Assert.AreEqual(13, AgentStations.Titles.Count);
            Assert.IsTrue(AgentStations.Titles.All(t => !string.IsNullOrWhiteSpace(t)));
        }

        [TestMethod]
        public void The_Catalogue_Refuses_A_Duplicate_Code_An_Unknown_Agent_And_A_Wrong_Station()
        {
            var w = new World();
            Assert.ThrowsException<InvalidOperationException>(() => new AgentStepCatalog(new IAgentStep[] { new FakeAgentStep(w), new FakeAgentStep(w) }));
            Assert.ThrowsException<InvalidOperationException>(() => new AgentStepCatalog(new IAgentStep[] { new BadStep("A.B", 6, "AG-NOBODY") }));
            Assert.ThrowsException<InvalidOperationException>(() => new AgentStepCatalog(new IAgentStep[] { new BadStep("A.B", 14, AgentCodes.Proc) }));
            Assert.ThrowsException<InvalidOperationException>(() => new AgentStepCatalog(new IAgentStep[] { new BadStep("a.b", 6, AgentCodes.Proc) }));
            var ok = new AgentStepCatalog(new IAgentStep[] { new FakeGateStep(w), new FakeAgentStep(w) });
            Assert.AreEqual(2, ok.All.Count);
            Assert.AreEqual(AgentStepCode, ok.Find("test.agent.make").Code);
            Assert.AreEqual(2, ok.ForAgent("ag-proc").Count);
        }

        private sealed class BadStep : IAgentStep
        {
            public BadStep(string code, int station, string agent) { Code = code; Station = station; AgentCode = agent; }
            public string Code { get; }
            public int Station { get; }
            public string AgentCode { get; }
            public string Title => "x";
            public AgentStepKind Kind => AgentStepKind.Agent;
            public string FallbackRoles => null;
            public Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) => Task.FromResult(new List<AgentWorkItem>());
            public Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) => Task.FromResult(AgentStepResult.Done(null, null, null, null));
            public AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo();
        }

        // ------------------------------------------------------------------------------------------------ the reconciler

        [TestMethod]
        public async Task Reconcile_Is_Idempotent_Two_Runs_Give_One_Open_Task()
        {
            Env e = NewEnv("idem");
            e.World.AgentDocs[1] = "D-1";
            AgentReconcileResult first = await Reconcile(e);
            AgentReconcileResult second = await Reconcile(e);
            Assert.AreEqual(1, first.Created);
            Assert.AreEqual(0, second.Created);
            Assert.AreEqual(0, second.Errors.Count, string.Join("|", second.Errors));
            AgentTask t = Tasks(e).Single();
            Assert.AreEqual(AgentTaskState.Queued, t.State);
            Assert.AreEqual(AgentStepKind.Agent, t.Kind);
            Assert.AreEqual(6, t.Station);
            Assert.AreEqual("AG-PROC", t.AgentCode);
            Assert.AreEqual("D-1", t.TargetNumber);
            Assert.AreEqual(7, t.ContractId);
            Assert.AreEqual("/fake/1", t.Link);
            Assert.AreEqual(1, Events(e, t.Id).Count, "one event for the birth");
            // the three settings rows were made on the first read, on by default
            List<AgentStepSetting> settings = e.Host.Read(u => u.Set<AgentStepSetting>().AsNoTracking().OrderBy(s => s.StepCode).ToList());
            settings = settings.Where(s => s.StepCode.StartsWith("TEST.")).ToList();   // the real steps of the application have their rows too
            Assert.AreEqual(3, settings.Count);
            Assert.IsTrue(settings.All(s => s.Enabled && s.MaxAttempts == 3 && s.GateDueHours == 48));
        }

        [TestMethod]
        public async Task A_Document_That_Leaves_The_Step_Closes_Its_Task_As_Done()
        {
            Env e = NewEnv("leaves");
            e.World.AgentDocs[1] = "D-1";
            e.World.GateDocs[1] = "D-1";
            await Reconcile(e);
            Assert.AreEqual(2, Tasks(e).Count(t => t.State < AgentTaskState.Done));
            AgentTask gate = TaskOf(e, GateStepCode, 1);
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual("PLANNING,PMO", gate.GateRoles, "the roles are normalised");
            Assert.IsTrue(gate.DueAt.HasValue && gate.DueAt.Value > DateTimeOffset.Now.AddHours(47) && gate.DueAt.Value < DateTimeOffset.Now.AddHours(49));
            // the gate has no office-inbox item of its own: the reconciler mirrors it (one item for each role)
            List<OfInboxItem> mirrored = e.Host.Read(u => u.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "agt:" + gate.Id).ToList());
            CollectionAssert.AreEquivalent(new[] { "PLANNING", "PMO" }, mirrored.Select(i => i.RecipientRole).ToArray());
            Assert.IsTrue(mirrored.All(i => i.Status == OfInboxStatus.Unread && i.Kind == OfInboxKind.Approval && i.Link == "/fake/1"));

            // the human used the existing approve action: the document is not at the gate any more
            e.World.GateDocs.TryRemove(1, out _);
            AgentReconcileResult r = await Reconcile(e);
            Assert.AreEqual(1, r.Closed);
            AgentTask closed = Tasks(e).Single(t => t.Id == gate.Id);
            Assert.AreEqual(AgentTaskState.Done, closed.State);
            Assert.AreEqual(AgentText.DocumentChanged, closed.ClosedReason);
            Assert.IsNotNull(closed.ClosedAt);
            Assert.AreEqual(AgentTaskState.Queued, TaskOf(e, AgentStepCode, 1).State, "the other step is untouched");
            Assert.IsTrue(e.Host.Read(u => u.Set<OfInboxItem>().Where(i => i.SourceKey == "agt:" + gate.Id).All(i => i.Status == OfInboxStatus.Done)), "the mirrored items are closed with the task");
            // and a document that comes back to the gate (a revision) gets its task at once: the repeat guard is only for the steps of an agent
            e.World.GateDocs[1] = "D-1";
            AgentReconcileResult again = await Reconcile(e);
            Assert.AreEqual(1, again.Created, "a gate task is made again when the document is at the gate again");
            Assert.AreEqual(0, again.Suppressed);
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, TaskOf(e, GateStepCode, 1).State);
        }

        [TestMethod]
        public async Task A_Step_That_Is_Switched_Off_Or_Whose_Seat_Is_Off_Becomes_A_Fallback_Task_For_People_And_Comes_Back_When_Switched_On()
        {
            Env e = NewEnv("fallback");
            e.World.AgentDocs[1] = "D-1";
            await Reconcile(e);
            Assert.AreEqual(AgentTaskState.Queued, TaskOf(e, AgentStepCode, 1).State);

            // the PMO switches the step off
            Assert.IsTrue((await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(new AgentStepEnableDto { Code = AgentStepCode, Enabled = false }, Ct))).Success);
            e.World.AgentDocs[2] = "D-2";
            AgentReconcileResult r = await Reconcile(e);
            Assert.AreEqual(1, r.FallbackSet, "the queued task is handed to people");
            Assert.AreEqual(1, r.Created, "a new document gets a fallback task at once");
            foreach (int id in new[] { 1, 2 })
            {
                AgentTask t = TaskOf(e, AgentStepCode, id);
                Assert.AreEqual(AgentTaskState.WaitingInput, t.State, "doc " + id);
                Assert.IsTrue(t.IsFallback);
                Assert.AreEqual("COMMERCIAL", t.GateRoles, "the FallbackRoles of the step");
                Assert.IsNotNull(t.DueAt);
            }
            // the buyer sees them in the inbox (a person with another role does not)
            var buyerInbox = await e.Host.As(Buyer, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct));
            Assert.AreEqual(2, buyerInbox.Data.Count);
            Assert.IsTrue(buyerInbox.Data.All(d => d.IsFallback && d.State == AgentTaskState.WaitingInput));
            Assert.AreEqual(0, (await e.Host.As(Warehouse, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct))).Data.Count);
            // the agent does not run a task that was handed to people
            Assert.AreEqual(AiSeatRunStatus.Succeeded, (await RunJob(e, "AG-PROC")).Status);
            Assert.AreEqual(0, e.World.Runs.Count);

            // the PMO switches it on: the tasks go back to the agent
            Assert.IsTrue((await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(new AgentStepEnableDto { Code = AgentStepCode, Enabled = true }, Ct))).Success);
            AgentReconcileResult back = await Reconcile(e);
            Assert.AreEqual(2, back.Restored);
            foreach (int id in new[] { 1, 2 })
            {
                AgentTask t = TaskOf(e, AgentStepCode, id);
                Assert.AreEqual(AgentTaskState.Queued, t.State);
                Assert.IsFalse(t.IsFallback);
                Assert.IsNull(t.GateRoles);
            }

            // the seat of the agent is switched off: the same
            SeatOff(e, "AG-PROC", false);
            AgentReconcileResult seatOff = await Reconcile(e);
            Assert.AreEqual(2, seatOff.FallbackSet);
            Assert.IsTrue(Tasks(e).Where(t => t.State < AgentTaskState.Done).All(t => t.IsFallback && t.State == AgentTaskState.WaitingInput));
            SeatOff(e, "AG-PROC", true);
            Assert.AreEqual(2, (await Reconcile(e)).Restored);
        }

        [TestMethod]
        public async Task A_Gate_Step_That_Is_Switched_Off_Is_Not_Tracked_And_Its_Tasks_Are_Withdrawn()
        {
            Env e = NewEnv("gateoff");
            e.World.GateDocs[3] = "D-3";
            await Reconcile(e);
            Assert.AreEqual(1, Tasks(e).Count);
            await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(new AgentStepEnableDto { Code = GateStepCode, Enabled = false }, Ct));
            AgentReconcileResult r = await Reconcile(e);
            Assert.AreEqual(1, r.Cancelled);
            Assert.AreEqual(AgentTaskState.Cancelled, Tasks(e).Single().State);
            e.World.GateDocs[4] = "D-4";
            Assert.AreEqual(0, (await Reconcile(e)).Created);
        }

        [TestMethod]
        public async Task A_Late_Gate_Is_Told_To_The_Pmo_Once()
        {
            Env e = NewEnv("late");
            e.World.GateDocs[1] = "D-1";
            await Reconcile(e);
            AgentTask gate = Tasks(e).Single();
            Assert.AreEqual(0, (await Reconcile(e)).Escalated, "not late yet");
            Mutate(e, gate.Id, t => t.DueAt = DateTimeOffset.Now.AddHours(-1));
            Assert.AreEqual(1, (await Reconcile(e)).Escalated);
            Assert.AreEqual(0, (await Reconcile(e)).Escalated, "once");
            AgentTask after = Tasks(e).Single();
            Assert.IsNotNull(after.EscalatedAt);
            List<OfInboxItem> alert = e.Host.Read(u => u.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "agt-alert:" + gate.Id).ToList());
            Assert.AreEqual(1, alert.Count);
            Assert.AreEqual("PMO", alert[0].RecipientRole);
            Assert.AreEqual(OfInboxKind.Alert, alert[0].Kind);
            // the board lists it as stuck
            AgentBoardDto board = (await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(null, Ct))).Data;
            Assert.AreEqual(AgentStuckReason.GateOverdue, board.Stuck.Single().Reason);
        }

        // ------------------------------------------------------------------------------------------------ the executor

        [TestMethod]
        public async Task The_Agent_Job_Runs_The_Step_As_The_Seat_And_Closes_The_Task_With_Its_Output()
        {
            Env e = NewEnv("run");
            e.World.AgentDocs[1] = "D-1";
            AiSeatJobResult result = await RunJob(e, "AG-PROC");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, result.Status, result.Error);
            StringAssert.Contains(result.Summary, "done=1");
            CollectionAssert.AreEqual(new[] { 1 }, e.World.Runs);
            Assert.AreEqual("seat.ag-proc", e.World.PrincipalSeen, "the step runs as the service user of the agent");
            Assert.AreEqual(AgentStepCode, e.World.StepSeen, "AgentMaker.Allows sees the step");
            Assert.IsNull(AgentStepContext.Current, "the step context does not leak out of the run");
            AgentTask t = Tasks(e).Single();
            Assert.AreEqual(AgentTaskState.Done, t.State);
            Assert.AreEqual(AgentText.DoneByAgent, t.ClosedReason);
            Assert.AreEqual("FakeOutput", t.OutputType);
            Assert.AreEqual(10, t.OutputId);
            Assert.AreEqual("OUT-1", t.OutputNumber);
            Assert.AreEqual("پیش‌نویس ساخته شد", t.Summary);
            Assert.AreEqual(0, t.AttemptCount);
        }

        [TestMethod]
        public async Task Each_Execution_Writes_One_Audit_Row_Of_Kind_Agent_And_One_Event_Of_The_Task()
        {
            Env e = NewEnv("audit");
            e.World.AgentDocs[1] = "D-1";
            await Reconcile(e);
            AgentTask t = Tasks(e).Single();
            int audits = AgentAudits(e);
            int events = Events(e, t.Id).Count;
            AgentRunOutcome outcome = await AsAgent(e, "AG-PROC", sp => sp.GetRequiredService<IAgentExecutor>().ExecuteTaskAsync("AG-PROC", t.Id, Ct));
            Assert.AreEqual(AgentRunOutcome.Done, outcome);
            Assert.AreEqual(audits + 1, AgentAudits(e));
            List<AgentTaskEvent> after = Events(e, t.Id);
            Assert.AreEqual(events + 1, after.Count);
            AgentTaskEvent last = after.Last();
            Assert.AreEqual(AgentTaskState.Queued, last.FromState);
            Assert.AreEqual(AgentTaskState.Done, last.ToState);
            Assert.AreEqual("AG-PROC", last.SeatCode);
            Assert.AreEqual(AgentUser("AG-PROC").UserId, last.UserId);
            AiAuditEvent row = e.Host.Read(u => u.Set<AiAuditEvent>().AsNoTracking().OrderByDescending(a => a.Id).First(a => a.Kind == "agent"));
            StringAssert.Contains(row.Detail, AgentStepCode);
            StringAssert.Contains(row.Detail, "task " + t.Id);
            StringAssert.Contains(row.Detail, "done");
            Assert.AreEqual("AG-PROC", row.SeatCode);
        }

        [TestMethod]
        public async Task A_Chain_Draft_Then_Gate_Is_Done_In_One_Run_Because_The_Run_Reconciles_Between_Its_Rounds()
        {
            Env e = NewEnv("chain");
            e.World.AgentDocs[1] = "D-1";
            // the fake step moves the document from its own step to the gate, as a service call would
            e.World.Behave = id =>
            {
                e.World.AgentDocs.TryRemove(id, out _);
                e.World.GateDocs[id] = "D-" + id;
                return AgentStepResult.Done("FakeDoc", id, "D-" + id, "ساخته و ارسال شد");
            };
            AiSeatJobResult r = await RunJob(e, "AG-PROC");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            Assert.AreEqual(AgentTaskState.Done, Tasks(e).Single(t => t.StepCode == AgentStepCode).State);
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, Tasks(e).Single(t => t.StepCode == GateStepCode).State, "the next round found the gate");
        }

        [TestMethod]
        public async Task A_Refusal_Is_Counted_With_A_Backoff_And_The_Third_One_Fails_The_Task_Tells_The_Pmo_And_Keeps_The_Persian_Message()
        {
            Env e = NewEnv("refuse");
            e.World.AgentDocs[1] = "D-1";
            e.World.Behave = id => AgentStepResult.Refused("قیمت برآوردی ردیف‌ها کامل نیست");
            await Reconcile(e);
            int id1 = Tasks(e).Single().Id;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Mutate(e, id1, t => t.NextAttemptAt = null);
                AgentRunOutcome outcome = await AsAgent(e, "AG-PROC", sp => sp.GetRequiredService<IAgentExecutor>().ExecuteTaskAsync("AG-PROC", id1, Ct));
                AgentTask t = Tasks(e).Single();
                Assert.AreEqual(attempt, t.AttemptCount);
                Assert.AreEqual("قیمت برآوردی ردیف‌ها کامل نیست", t.LastError);
                if (attempt < 3)
                {
                    Assert.AreEqual(AgentRunOutcome.Refused, outcome);
                    Assert.AreEqual(AgentTaskState.Queued, t.State);
                    TimeSpan wait = t.NextAttemptAt.Value - DateTimeOffset.Now;
                    TimeSpan expected = AgentExecutor.Backoff[attempt - 1];
                    Assert.IsTrue(wait > expected - TimeSpan.FromSeconds(30) && wait <= expected, "backoff " + wait);
                }
                else
                {
                    Assert.AreEqual(AgentRunOutcome.Failed, outcome);
                    Assert.AreEqual(AgentTaskState.Failed, t.State);
                    Assert.IsNull(t.NextAttemptAt);
                }
            }
            // the PMO is told, and the failed task is not run again by the job
            List<OfInboxItem> alert = e.Host.Read(u => u.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "agt-alert:" + id1).ToList());
            Assert.AreEqual(1, alert.Count);
            Assert.AreEqual("PMO", alert[0].RecipientRole);
            StringAssert.Contains(alert[0].Title, "قیمت برآوردی ردیف‌ها کامل نیست");
            int runs = e.World.Runs.Count;
            await RunJob(e, "AG-PROC");
            Assert.AreEqual(runs, e.World.Runs.Count);
            // and a refusal that came as an exception is a refusal too (the message of the exception is kept)
            AgentTask failed = Tasks(e).Single();
            Assert.AreEqual(3, failed.AttemptCount);

            // the PMO retries it: attempts reset, queued, the alert is closed; a person who is not PMO cannot
            Assert.IsFalse((await e.Host.As(Planner, p => p.GetRequiredService<IAgentBoardService>().RetryAsync(new AgentRetryDto { Id = id1 }, Ct))).Success);
            Assert.IsFalse((await e.Host.As(SeatWithGateRole, p => p.GetRequiredService<IAgentBoardService>().RetryAsync(new AgentRetryDto { Id = id1 }, Ct))).Success);
            e.World.Behave = id => AgentStepResult.Done("FakeOutput", id, "OUT", "ok");
            var retry = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().RetryAsync(new AgentRetryDto { Id = id1 }, Ct));
            Assert.IsTrue(retry.Success, retry.Message);
            Assert.AreEqual(AgentTaskState.Queued, retry.Data.State);
            Assert.AreEqual(0, retry.Data.AttemptCount);
            Assert.IsTrue(e.Host.Read(u => u.Set<OfInboxItem>().Where(i => i.SourceKey == "agt-alert:" + id1).All(i => i.Status == OfInboxStatus.Done)));
            Assert.IsFalse((await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().RetryAsync(new AgentRetryDto { Id = id1 }, Ct))).Success, "only a failed task is retried");
            Assert.AreEqual(AgentRunOutcome.Done, await AsAgent(e, "AG-PROC", sp => sp.GetRequiredService<IAgentExecutor>().ExecuteTaskAsync("AG-PROC", id1, Ct)));
        }

        [TestMethod]
        public async Task An_Exception_In_The_Step_Is_A_Refusal_With_Its_Message()
        {
            Env e = NewEnv("throws");
            e.World.AgentDocs[1] = "D-1";
            e.World.Behave = id => throw new InvalidOperationException("خطای آزمایشی پایگاه داده");
            await RunJob(e, "AG-PROC");
            AgentTask t = Tasks(e).Single();
            Assert.AreEqual(AgentTaskState.Queued, t.State);
            Assert.AreEqual(1, t.AttemptCount);
            Assert.AreEqual("خطای آزمایشی پایگاه داده", t.LastError);
            Assert.AreEqual(1, e.World.Runs.Count, "the next round does not retry before the backoff has passed");
        }

        [TestMethod]
        public async Task Waiting_Does_Not_Count_An_Attempt_And_Is_Tried_Again_Later()
        {
            Env e = NewEnv("waiting");
            e.World.AgentDocs[1] = "D-1";
            e.World.Behave = id => AgentStepResult.Waiting("قرارداد هنوز بازنگری نشده است");
            await RunJob(e, "AG-PROC");
            await RunJob(e, "AG-PROC");
            AgentTask t = Tasks(e).Single();
            Assert.AreEqual(AgentTaskState.Queued, t.State);
            Assert.AreEqual(0, t.AttemptCount);
            Assert.AreEqual("قرارداد هنوز بازنگری نشده است", t.LastError);
            TimeSpan wait = t.NextAttemptAt.Value - DateTimeOffset.Now;
            Assert.IsTrue(wait > TimeSpan.FromMinutes(9) && wait <= AgentExecutor.WaitingDelay, "wait " + wait);
            Assert.AreEqual(1, e.World.Runs.Count, "not run again before its time");
            for (int i = 0; i < 5; i++)
            {
                Mutate(e, t.Id, x => x.NextAttemptAt = null);
                await RunJob(e, "AG-PROC");
            }
            Assert.AreEqual(0, Tasks(e).Single().AttemptCount, "waiting never fails a task");
            Assert.AreNotEqual(AgentTaskState.Failed, Tasks(e).Single().State);
        }

        [TestMethod]
        public async Task The_Master_Puts_A_Stale_Running_Task_Back_In_The_Queue_And_Reconciles_Every_Agent_As_The_Master_Seat()
        {
            Env e = NewEnv("stale");
            e.World.AgentDocs[1] = "D-1";
            e.World.AgentDocs[2] = "D-2";
            await Reconcile(e);
            int stale = TaskOf(e, AgentStepCode, 1).Id, fresh = TaskOf(e, AgentStepCode, 2).Id;
            Mutate(e, stale, t => { t.State = AgentTaskState.Running; t.StartedAt = DateTimeOffset.Now.AddMinutes(-31); });
            Mutate(e, fresh, t => { t.State = AgentTaskState.Running; t.StartedAt = DateTimeOffset.Now.AddMinutes(-5); });
            e.World.GateDocs[9] = "D-9";

            // AG-MASTER runs under the guard that limits it to the tables of the agent layer, the AI module and the inbox: the whole reconcile must fit in them
            AiSeatJobResult r = await RunJob(e, "AG-MASTER");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            StringAssert.Contains(r.Summary, "requeued=1");
            Assert.AreEqual(AgentTaskState.Queued, Tasks(e).Single(t => t.Id == stale).State);
            Assert.AreEqual(AgentText.StaleRunning, Tasks(e).Single(t => t.Id == stale).LastError);
            Assert.AreEqual(AgentTaskState.Running, Tasks(e).Single(t => t.Id == fresh).State, "a run of five minutes is alive");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, TaskOf(e, GateStepCode, 9).State, "the master made the task of another agent's gate");
            Assert.IsTrue(Events(e, stale).Any(x => x.FromState == AgentTaskState.Running && x.ToState == AgentTaskState.Queued));
            Assert.IsTrue(Events(e, TaskOf(e, GateStepCode, 9).Id).All(x => x.SeatCode == "AG-MASTER"));
        }

        [TestMethod]
        public async Task An_Agent_Cannot_Run_The_Step_Of_Another_Agent()
        {
            Env e = NewEnv("allow");
            e.World.AgentDocs[1] = "D-1";
            await Reconcile(e);
            int id = Tasks(e).Single().Id;
            int audits = AgentAudits(e);

            // AG-WH asks for the task of AG-PROC
            AgentRunOutcome outcome = await AsAgent(e, "AG-WH", sp => sp.GetRequiredService<IAgentExecutor>().ExecuteTaskAsync("AG-WH", id, Ct));
            Assert.AreEqual(AgentRunOutcome.NotAllowed, outcome);
            Assert.AreEqual(0, e.World.Runs.Count);
            AgentTask t = Tasks(e).Single();
            Assert.AreEqual(AgentTaskState.Queued, t.State, "nothing changed");
            Assert.AreEqual(0, t.AttemptCount);
            Assert.AreEqual(audits + 1, AgentAudits(e), "the refusal is audited");
            // a gate (not an agent step) is not run by anyone, not even by the agent of its chain
            e.World.GateDocs[2] = "D-2";
            await Reconcile(e);
            int gate = TaskOf(e, GateStepCode, 2).Id;
            Assert.AreEqual(AgentRunOutcome.NotAllowed, await AsAgent(e, "AG-PROC", sp => sp.GetRequiredService<IAgentExecutor>().ExecuteTaskAsync("AG-PROC", gate, Ct)));
            // the job of AG-WH finds nothing of AG-PROC to do, the job of AG-PROC does its own
            await RunJob(e, "AG-WH");
            Assert.AreEqual(0, e.World.Runs.Count);
            await RunJob(e, "AG-PROC");
            CollectionAssert.AreEqual(new[] { 1 }, e.World.Runs);
        }

        // ------------------------------------------------------------------------------------------------ the gate inbox

        [TestMethod]
        public async Task The_Inbox_Shows_A_Task_Only_To_The_Holders_Of_Its_Roles_And_Never_To_A_Seat()
        {
            Env e = NewEnv("inbox");
            e.World.GateDocs[1] = "D-1";
            e.World.GateDocs[2] = "D-2";
            await Reconcile(e);
            Func<ClaimsPrincipal, Task<ResponseDto<PagedList<AgentInboxItemDto>>>> inbox = who => e.Host.As(who, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct));

            var planner = await inbox(Planner);
            Assert.IsTrue(planner.Success);
            Assert.AreEqual(2, planner.Data.Count);
            AgentInboxItemDto first = planner.Data[0];
            Assert.AreEqual(GateStepCode, first.StepCode);
            Assert.AreEqual("/fake/" + first.TargetId, first.Link);
            Assert.AreEqual("منتظر تایید", first.Summary);
            Assert.AreEqual("تایید و ارسال: ".Length > 0 ? "تدارکات و خرید" : null, first.StationTitle);
            Assert.AreEqual("PLANNING,PMO", first.GateRoles);
            Assert.IsFalse(first.Overdue);
            Assert.AreEqual(2, (await inbox(Pmo)).Data.Count);
            Assert.AreEqual(0, (await inbox(Warehouse)).Data.Count, "another role sees nothing");
            Assert.AreEqual(0, (await inbox(Ceo)).Data.Count, "the CEO is not in the roles of this gate");

            var seat = await inbox(SeatWithGateRole);
            Assert.IsFalse(seat.Success, "a seat is not shown the inbox, whatever roles it holds");
            Assert.AreEqual(AgentText.SeatsHaveNoInbox, seat.Message);
            // also when the seat runs through the acting flow
            var acting = await AsAgent(e, "AG-PLAN", sp => sp.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct));
            Assert.IsFalse(acting.Success);

            // a task for one user, not for a role
            e.Host.Write(u => { u.Set<AgentTask>().First().GateUserId = 61; u.Set<AgentTask>().First().GateRoles = null; u.SaveChanges(); }, Pmo);
            Assert.AreEqual(1, (await inbox(Warehouse)).Data.Count, "the user of the task sees it");

            var counts = await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().GetCountsAsync(Ct));
            Assert.IsTrue(counts.Success);
            Assert.AreEqual(1, counts.Data.HumanGate);
            Assert.AreEqual(0, counts.Data.Physical);
            Assert.AreEqual(1, counts.Data.Total);
            Assert.IsFalse((await e.Host.As(SeatWithGateRole, p => p.GetRequiredService<IAgentInboxService>().GetCountsAsync(Ct))).Success);

            // the tab and the station filter
            Assert.AreEqual(1, (await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery { Kind = AgentStepKind.HumanGate }, Ct))).Data.Count);
            Assert.AreEqual(0, (await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery { Kind = AgentStepKind.Physical }, Ct))).Data.Count);
            Assert.AreEqual(0, (await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery { Station = 5 }, Ct))).Data.Count);
        }

        [TestMethod]
        public async Task Return_Needs_A_Reason_And_A_Gate_Role_And_Calls_The_Existing_Return_Action_As_The_Person()
        {
            Env e = NewEnv("return");
            e.World.ReturnableDocs[1] = "D-1";
            await Reconcile(e);
            int id = TaskOf(e, ReturnableStepCode, 1).Id;
            Func<ClaimsPrincipal, string, Task<ResponseDto<AgentInboxItemDto>>> back = (who, reason) => e.Host.As(who, p => p.GetRequiredService<IAgentInboxService>().ReturnAsync(new AgentReturnDto { Id = id, Reason = reason }, Ct));

            Assert.IsFalse((await back(Planner, null)).Success);
            Assert.AreEqual(AgentText.ReasonRequired, (await back(Planner, "   ")).Message);
            Assert.AreEqual(AgentText.NotYourGate, (await back(Warehouse, "قیمت‌ها را اصلاح کنید")).Message, "a person without a gate role cannot return");
            Assert.IsFalse((await back(SeatWithGateRole, "قیمت‌ها را اصلاح کنید")).Success, "a seat cannot return");
            Assert.IsFalse((await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().ReturnAsync(new AgentReturnDto { Id = 0, Reason = "x" }, Ct))).Success);
            Assert.AreEqual(0, e.World.Returned.Count);

            var done = await back(Planner, "قیمت‌ها را اصلاح کنید");
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual("1:قیمت‌ها را اصلاح کنید:planner", e.World.Returned.Single(), "the return action ran as the person");
            AgentTask t = Tasks(e).Single(x => x.Id == id);
            Assert.AreEqual("قیمت‌ها را اصلاح کنید", t.ReturnReason);
            Assert.AreEqual(51, t.ReturnedByUserId);
            Assert.IsNotNull(t.ReturnedAt);
            // the document left the gate: the next reconcile closes the task
            await Reconcile(e);
            Assert.AreEqual(AgentTaskState.Done, Tasks(e).Single(x => x.Id == id).State);
            Assert.IsTrue(e.Host.Read(u => u.Set<AiAuditEvent>().Any(a => a.Kind == "agent" && a.Detail.Contains("returned to the agent"))));
        }

        [TestMethod]
        public async Task A_Gate_Without_A_Return_Action_Sends_The_Document_Back_To_The_Agent_That_Made_It()
        {
            Env e = NewEnv("returnagent");
            e.World.AgentDocs[1] = "D-1";
            await RunJob(e, "AG-PROC");   // the agent makes the document: its task is Done
            e.World.AgentDocs.TryRemove(1, out _);
            e.World.GateDocs[1] = "D-1";
            await Reconcile(e);
            int gate = TaskOf(e, GateStepCode, 1).Id;

            var back = await e.Host.As(Planner, p => p.GetRequiredService<IAgentInboxService>().ReturnAsync(new AgentReturnDto { Id = gate, Reason = "مقدار را اصلاح کنید" }, Ct));
            Assert.IsTrue(back.Success, back.Message);
            AgentTask producer = Tasks(e).Single(x => x.StepCode == AgentStepCode);
            Assert.AreEqual(AgentTaskState.Returned, producer.State, "the producing agent task is open again");
            Assert.AreEqual("مقدار را اصلاح کنید", producer.ReturnReason);
            Assert.AreEqual(51, producer.ReturnedByUserId);
            Assert.IsNull(producer.ClosedAt);
            // the agent picks up a Returned task at its next run
            e.World.AgentDocs[1] = "D-1";
            e.World.GateDocs.TryRemove(1, out _);
            int runs = e.World.Runs.Count;
            await RunJob(e, "AG-PROC");
            Assert.AreEqual(runs + 1, e.World.Runs.Count);
            Assert.AreEqual(AgentTaskState.Done, Tasks(e).Single(x => x.StepCode == AgentStepCode).State);
        }

        // ------------------------------------------------------------------------------------------------ the board, the tasks, the steps

        [TestMethod]
        public async Task The_Board_Has_A_Row_Per_Contract_And_Thirteen_Cells_With_The_Worst_Status_And_Who_It_Waits_For()
        {
            Env e = NewEnv("board");
            e.Host.Write(u => { AppHost.Add(u, new ContractEntity { Code = "C-7", Title = "قرارداد آزمایشی", InternalContractNo = "INT-7" }); u.SaveChanges(); }, Pmo);
            int contractId = e.Host.Read(u => u.Set<ContractEntity>().Single().Id);
            e.World.AgentDocs[1] = "D-1";
            e.World.GateDocs[1] = "D-1";
            await Reconcile(e);
            // the fake steps say contract 7: move the tasks to the real contract and age the gate
            e.Host.Write(u => { foreach (AgentTask t in u.Set<AgentTask>()) t.ContractId = contractId; u.SaveChanges(); }, Pmo);
            int gate = Tasks(e).Single(t => t.StepCode == GateStepCode).Id;
            Mutate(e, gate, t => t.CreateDate = DateTimeOffset.Now.AddHours(-30));
            // a closed task of another station of the same contract and one without a contract
            e.Host.Write(u =>
            {
                AppHost.Add(u, new AgentTask { StepCode = "OLD.STEP", Station = 3, AgentCode = "AG-INIT", Kind = AgentStepKind.Agent, State = AgentTaskState.Done, TargetType = "T", TargetId = 1, Title = "قدیمی", ContractId = contractId });
                AppHost.Add(u, new AgentTask { StepCode = "OLD.STEP", Station = 7, AgentCode = "AG-WH", Kind = AgentStepKind.Agent, State = AgentTaskState.Queued, TargetType = "T", TargetId = 2, Title = "کار شرکت" });
                u.SaveChanges();
            }, Pmo);

            var board = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(null, Ct));
            Assert.IsTrue(board.Success, board.Message);
            Assert.AreEqual(13, board.Data.Stations.Count);
            Assert.AreEqual(2, board.Data.Rows.Count);
            AgentBoardRowDto company = board.Data.Rows.Single(r => r.ContractId == null);
            Assert.AreEqual(AgentBoardService.CompanyLabel, company.Label);
            Assert.AreEqual(AgentCellStatus.Agent, company.Cells[6].Status);
            AgentBoardRowDto row = board.Data.Rows.Single(r => r.ContractId == contractId);
            StringAssert.Contains(row.Label, "INT-7");
            Assert.AreEqual(13, row.Cells.Count);
            AgentBoardCellDto six = row.Cells[5];
            Assert.AreEqual(6, six.Station);
            Assert.AreEqual(AgentCellStatus.WaitingGate, six.Status, "the gate is worse than the agent's own queued task");
            Assert.AreEqual("PLANNING,PMO", six.WaitingOn);
            Assert.AreEqual(2, six.OpenCount);
            Assert.IsTrue(six.OldestHours >= 29 && six.OldestHours <= 31, "hours " + six.OldestHours);
            Assert.AreEqual(AgentCellStatus.Idle, row.Cells[2].Status, "station 3 has closed tasks only");
            Assert.AreEqual(AgentCellStatus.NotStarted, row.Cells[0].Status);

            // the CEO and the auditor read it; others do not
            Assert.IsTrue((await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(contractId, Ct))).Success);
            Assert.IsTrue((await e.Host.As(Auditor, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(null, Ct))).Success);
            Assert.IsFalse((await e.Host.As(Planner, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(null, Ct))).Success);
            var one = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(contractId, Ct));
            Assert.AreEqual(1, one.Data.Rows.Count);
            // the task list and the detail with events
            var list = await e.Host.As(Auditor, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { Station = 6, PageSize = 10 }, Ct));
            Assert.AreEqual(2, list.Data.Count);
            var detail = await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetTaskAsync(gate, Ct));
            Assert.IsTrue(detail.Success);
            Assert.AreEqual(1, detail.Data.Events.Count);
            Assert.IsFalse((await e.Host.As(Warehouse, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery(), Ct))).Success);
        }

        [TestMethod]
        public async Task Only_The_Pmo_Switches_A_Step_And_The_Switch_Is_Audited()
        {
            Env e = NewEnv("switch");
            var steps = await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetStepsAsync(Ct));
            Assert.IsTrue(steps.Success);
            Assert.AreEqual(3, steps.Data.Count(s => s.Code.StartsWith("TEST.")));
            Assert.IsTrue(steps.Data.All(s => s.Enabled), "on by default");
            Assert.IsFalse((await e.Host.As(Auditor, p => p.GetRequiredService<IAgentBoardService>().GetStepsAsync(Ct))).Success, "PMO and CEO read the catalogue");
            var off = new AgentStepEnableDto { Code = AgentStepCode, Enabled = false };
            Assert.IsFalse((await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(off, Ct))).Success);
            Assert.IsFalse((await e.Host.As(Planner, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(off, Ct))).Success);
            Assert.IsFalse((await e.Host.As(SeatWithGateRole, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(off, Ct))).Success, "not a seat, whatever roles it holds");
            Assert.IsFalse((await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(new AgentStepEnableDto { Code = "NO.SUCH" }, Ct))).Success);
            Assert.IsTrue(e.Host.Read(u => u.Set<AgentStepSetting>().All(s => s.Enabled)));

            var done = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(off, Ct));
            Assert.IsTrue(done.Success, done.Message);
            Assert.IsFalse(done.Data.Enabled);
            Assert.IsFalse(e.Host.Read(u => u.Set<AgentStepSetting>().Single(s => s.StepCode == AgentStepCode).Enabled));
            Assert.IsTrue(e.Host.Read(u => u.Set<AiAuditEvent>().Any(a => a.Kind == "agent" && a.Detail.Contains(AgentStepCode) && a.Detail.Contains("disabled") && a.UserId == 11)), "the audit row of the AI module");
            Assert.IsTrue(e.Host.Read(u => u.Set<FnAuditLog>().Any(a => a.EntityType == nameof(AgentStepSetting) && a.Action == "Modified" && a.UserId == 11)), "the tamper-evident chain");
        }

        // ------------------------------------------------------------------------------------------------ the scheduler switch

        [TestMethod]
        public async Task The_Scheduler_Does_Not_Start_An_Agent_When_Agents_Jobs_Is_False_But_A_Run_By_Hand_Works()
        {
            Env off = NewEnv("jobsoff", new Dictionary<string, string> { ["Agents:Jobs"] = "false" });
            await off.Host.As(Pmo, p => p.GetRequiredService<IAiSeatAdminService>().GetSeatsAsync(Ct));   // provisions the rows of the seats
            // only the agents are enabled, so that the older seats are not in the way
            off.Host.Write(u => { foreach (AiSeat s in u.Set<AiSeat>().Where(x => !x.Code.StartsWith("AG-"))) s.Enabled = false; u.SaveChanges(); }, Pmo);
            int started = await off.Host.As(Pmo, p => p.GetRequiredService<IAiSeatRunner>().RunDueAsync(DateTime.Now, Ct));
            Assert.AreEqual(0, started);
            Assert.AreEqual(0, off.Host.Read(u => u.Set<AiSeatRun>().Count()));
            // the run by hand through the admin endpoint still starts the agent
            var hand = await off.Host.As(Pmo, p => p.GetRequiredService<IAiSeatAdminService>().RunAsync(new ViewModels.NasbNiro.AiDtos.AiRunSeatDto { Code = "AG-PROC" }, Ct));
            Assert.IsTrue(hand.Success, hand.Message);
            Assert.AreEqual(AiSeatRunStatus.Running, hand.Data.Status);

            Env on = NewEnv("jobson");
            await on.Host.As(Pmo, p => p.GetRequiredService<IAiSeatAdminService>().GetSeatsAsync(Ct));
            on.Host.Write(u => { foreach (AiSeat s in u.Set<AiSeat>().Where(x => !x.Code.StartsWith("AG-"))) s.Enabled = false; u.SaveChanges(); }, Pmo);
            int running = await on.Host.As(Pmo, p => p.GetRequiredService<IAiSeatRunner>().RunDueAsync(DateTime.Now, Ct));
            Assert.AreEqual(13, running, "absent means on: all thirteen agents ran");
            Assert.AreEqual(13, on.Host.Read(u => u.Set<AiSeatRun>().Count(r => r.Status == AiSeatRunStatus.Succeeded)));
        }

        [TestMethod]
        public async Task The_Agents_Are_Provisioned_With_Locked_Users_And_Only_Their_Own_Roles()
        {
            Env e = NewEnv("provision");
            var seats = await e.Host.As(Pmo, p => p.GetRequiredService<IAiSeatAdminService>().GetSeatsAsync(Ct));
            Assert.IsTrue(seats.Success, seats.Message);
            Assert.AreEqual(13, seats.Data.Count(s => s.Code.StartsWith("AG-")));
            Assert.IsTrue(seats.Data.Where(s => s.Code.StartsWith("AG-")).All(s => s.Enabled && s.Schedule == "poll 2"));
            var users = await e.Host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct));
            Assert.AreEqual(0, users["AG-MASTER"].Roles.Length);
            CollectionAssert.AreEqual(new[] { "COMMERCIAL" }, users["AG-PROC"].Roles);
            Assert.AreEqual("seat.ag-master", users["AG-MASTER"].UserName);
        }

        [TestMethod]
        public async Task The_Scheduler_Starts_A_Process_Agent_Only_When_It_Has_Work_Or_Its_Last_Run_Is_Older_Than_Thirty_Minutes_And_The_Master_Always()
        {
            Env e = NewEnv("economy");
            await e.Host.As(Pmo, p => p.GetRequiredService<IAiSeatAdminService>().GetSeatsAsync(Ct));
            e.Host.Write(u => { foreach (AiSeat s in u.Set<AiSeat>().Where(x => !x.Code.StartsWith("AG-"))) s.Enabled = false; u.SaveChanges(); }, Pmo);
            Func<int, Task<int>> tick = minutes => e.Host.As(Pmo, p => p.GetRequiredService<IAiSeatRunner>().RunDueAsync(DateTime.Now.AddMinutes(minutes), Ct));
            Func<string, int> runsOf = code => e.Host.Read(u => u.Set<AiSeatRun>().Count(r => r.Seat.Code == code));
            Func<int> masterAudits = () => e.Host.Read(u => u.Set<AiAuditEvent>().Count(a => a.SeatCode == "AG-MASTER" && a.Kind == AiAuditKinds.Seat));

            // the first tick: no agent has ever run, so all thirteen run once; the master found nothing to change
            Assert.AreEqual(13, await tick(0));
            Assert.AreEqual(0, masterAudits(), "a tick of the master that changed nothing writes no audit event");
            Assert.AreEqual(1, runsOf("AG-MASTER"));

            // three minutes later: the master is due again and runs; the twelve agents have no task and ran a moment ago
            Assert.AreEqual(1, await tick(3));
            Assert.AreEqual(2, runsOf("AG-MASTER"));
            Assert.AreEqual(1, runsOf("AG-PROC"));
            Assert.AreEqual(1, runsOf("AG-WH"));
            Assert.AreEqual(0, masterAudits(), "still nothing changed");

            // a new document: the master of this tick makes its task, and only the agent that owns the task is started after it
            e.World.AgentDocs[1] = "D-1";
            Assert.AreEqual(2, await tick(6));
            Assert.AreEqual(3, runsOf("AG-MASTER"));
            Assert.AreEqual(2, runsOf("AG-PROC"), "AG-PROC has a queued task");
            Assert.AreEqual(1, runsOf("AG-WH"), "AG-WH has none");
            Assert.AreEqual(1, e.World.Runs.Count);
            Assert.AreEqual(1, masterAudits(), "the tick that made a task is audited once, at its end");

            // a task whose next attempt lies in the future is not work yet
            e.World.AgentDocs[2] = "D-2";
            e.World.Behave = id => AgentStepResult.Refused("not yet");
            Assert.AreEqual(2, await tick(9));
            Assert.AreEqual(3, runsOf("AG-PROC"));
            Assert.AreEqual(1, await tick(4), "only the master ticks while the backoff of the refused task lasts");
            Assert.AreEqual(3, runsOf("AG-PROC"));

            // the safety net: thirty minutes after its last run an idle agent runs once more
            Assert.AreEqual(13, await tick(60));
            Assert.AreEqual(2, runsOf("AG-WH"));
        }

        [TestMethod]
        public async Task The_Task_Rows_Can_Be_Filtered_To_The_Company_Row_And_The_Inbox_Names_The_Agent_And_Counts_Only_Waiting_Tasks_As_Overdue_And_Events_Name_Their_People()
        {
            Env e = NewEnv("api");
            e.World.AgentDocs[1] = "D-1";
            e.World.GateDocs[1] = "D-1";
            e.World.GateDocs[2] = "D-2";
            await Reconcile(e);
            // the fake steps say contract 7: one task of a gate belongs to no contract
            int withoutContract = Tasks(e).Single(t => t.StepCode == GateStepCode && t.TargetId == 2).Id;
            Mutate(e, withoutContract, t => t.ContractId = null);
            int overdueGate = Tasks(e).Single(t => t.StepCode == GateStepCode && t.TargetId == 1).Id;
            Mutate(e, overdueGate, t => t.DueAt = DateTimeOffset.Now.AddHours(-2));

            var all = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { PageSize = 20 }, Ct));
            Assert.AreEqual(3, all.Data.Count);
            var none = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { NoContract = true, PageSize = 20 }, Ct));
            Assert.AreEqual(1, none.Data.Count);
            Assert.AreEqual(withoutContract, none.Data[0].Id);
            Assert.IsNull(none.Data[0].ContractId);
            var with = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { NoContract = false, ContractId = 7, PageSize = 20 }, Ct));
            Assert.AreEqual(2, with.Data.Count);
            // NoContract wins over a contract id
            Assert.AreEqual(1, (await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { NoContract = true, ContractId = 7, PageSize = 20 }, Ct))).Data.Count);

            // the inbox: the agent's Persian title, and the overdue count of a waiting task only
            var inbox = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct));
            Assert.AreEqual(2, inbox.Data.Count);
            Assert.IsTrue(inbox.Data.All(d => d.AgentTitle == AiSeatCatalog.Find("AG-PROC").Title));
            Assert.AreEqual(1, inbox.Data.Count(d => d.Overdue));
            Assert.AreEqual(AiSeatCatalog.Find("AG-PROC").Title, all.Data[0].AgentTitle);
            // a task that is not waiting for a person (the agent's own queued task) is never overdue, whatever its due date says
            int queued = Tasks(e).Single(t => t.StepCode == AgentStepCode).Id;
            Mutate(e, queued, t => t.DueAt = DateTimeOffset.Now.AddHours(-5));
            var counts = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentInboxService>().GetCountsAsync(Ct));
            Assert.AreEqual(1, counts.Data.Overdue);

            // the events name the person, or the title of the seat
            e.Host.Write(u =>
            {
                AppHost.Add(u, new AgentTaskEvent { TaskId = overdueGate, At = DateTimeOffset.Now, UserId = 11, ToState = AgentTaskState.WaitingHumanGate, Note = "human" });
                AppHost.Add(u, new AgentTaskEvent { TaskId = overdueGate, At = DateTimeOffset.Now, SeatCode = "AG-PLAN", ToState = AgentTaskState.WaitingHumanGate, Note = "seat" });
                u.SaveChanges();
            }, Pmo);
            var detail = await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetTaskAsync(overdueGate, Ct));
            Assert.IsTrue(detail.Success, detail.Message);
            Assert.AreEqual(AiSeatCatalog.Find("AG-PLAN").Title, detail.Data.Events.Single(x => x.Note == "seat").UserName);
            Assert.AreNotEqual(AiSeatCatalog.Find("AG-PLAN").Title, detail.Data.Events.Single(x => x.Note == "human").UserName, "a person is not shown with the title of a seat");
        }

        // ------------------------------------------------------------------------------------------------ the architecture rules

        [TestMethod]
        public void The_Step_Classes_Never_Name_An_Approval_Or_Write_Through_The_Unit_Of_Work()
        {
            string dir = null;
            for (string d = AppContext.BaseDirectory; d != null && dir == null; d = Directory.GetParent(d)?.FullName)
            {
                string candidate = Path.Combine(d, "Services", "NasbNiro", "Agents", "Steps");
                if (Directory.Exists(candidate)) dir = candidate;
            }
            Assert.IsNotNull(dir, "the folder of the step classes was not found next to the tests");
            string[] forbidden = { "\"approve", "SignAsync", "ApproveAsync", "DecideAsync", "SaveChanges", ".Remove(", ".RemoveRange(" };
            var found = new List<string>();
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string f in forbidden) if (text.Contains(f, StringComparison.Ordinal)) found.Add(Path.GetFileName(file) + " contains " + f);
            }
            Assert.AreEqual(0, found.Count, string.Join("; ", found));
        }

        [TestMethod]
        public void No_Persian_Text_Of_The_Layer_Says_Amel_Where_It_Means_Agent()
        {
            string dir = null;
            for (string d = AppContext.BaseDirectory; d != null && dir == null; d = Directory.GetParent(d)?.FullName)
            {
                string candidate = Path.Combine(d, "Services", "NasbNiro", "Agents");
                if (Directory.Exists(candidate)) dir = candidate;
            }
            Assert.IsNotNull(dir);
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                Assert.IsFalse(File.ReadAllText(file).Replace("مدیرعامل", string.Empty).Contains("عامل"), Path.GetFileName(file) + " says «عامل»; the word of the layer is «ایجنت»");
        }
    }
}
