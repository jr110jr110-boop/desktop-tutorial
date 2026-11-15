using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Agent layer, station 4 (planning): the WBS, the weights, the dictionary, the critical path and the first baseline by the planning agent; people approve the nodes, sign and approve the baseline.</summary>
    [TestClass]
    public class AgentPlanningTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(1081, "project.manager", "PROJECTMANAGER");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentPlan_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-PLAN"); }
            await Run(e, "AG-MASTER");
        }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        private static AgentTask OpenOne(Env e, string step)
        {
            List<AgentTask> list = Open(e, step);
            Assert.AreEqual(1, list.Count, step + " open count: " + Dump(e));
            return list[0];
        }

        private static AgentTask Closed(Env e, string step)
        {
            AgentTask t = Tasks(e).LastOrDefault(x => x.StepCode == step && x.State == AgentTaskState.Done);
            Assert.IsNotNull(t, step + " not done: " + Dump(e));
            return t;
        }

        private static T Svc<T, S>(Env e, ClaimsPrincipal who, Func<S, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<S>())).GetAwaiter().GetResult();

        private static int SeedContract(Env e)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-400", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30), StartDate = DateTimeOffset.Now.Date, Price = 1_000_000_000m });
                u.SaveChanges();
                AppHost.Add(u, new WorkOrder { Code = "WO-1", Title = "پمپ شماره یک", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                AppHost.Add(u, new PcContractReview { ContractId = c.Id, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, Kind = PcReviewKind.StandardProduct });
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000_000m, ScheduleStartDate = DateTime.Today, ContractDeliveryDate = DateTime.Today.AddDays(200), ProjectCode = "026" + "01" + "MA" + "01" });
                PcProjectInitiation init = AppHost.Add(u, new PcProjectInitiation { ContractId = c.Id, CurrentStep = PcInitiationStep.WbsDrafted, Steps = new List<PcInitiationStepRecord>() });
                foreach (PcInitiationStep s in Enum.GetValues(typeof(PcInitiationStep)))
                    init.Steps.Add(new PcInitiationStepRecord { Step = s, DoneAt = s <= PcInitiationStep.BomRequested ? DateTimeOffset.Now : (DateTimeOffset?)null });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static List<PcWbsNode> Nodes(Env e, int contractId) => e.Host.Read(u => u.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == contractId && !n.Deleted).OrderBy(n => n.Id).ToList());
        private static List<PcActivity> Activities(Env e, int contractId) => e.Host.Read(u => u.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToList());

        [TestMethod]
        public async Task The_Planning_Chain_From_The_Work_Order_To_The_Approved_Baseline_Is_Made_By_The_Agent_And_Decided_By_People()
        {
            Env e = await NewEnv("chain");
            int contract = SeedContract(e);
            int planUser = e.Seats["AG-PLAN"].UserId;

            await Settle(e);
            AgentTask generate = Closed(e, "PLAN.WBS.GENERATE");
            Assert.AreEqual(4, generate.Station);
            Assert.AreEqual("AG-PLAN", generate.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, generate.Kind);
            List<PcWbsNode> nodes = Nodes(e, contract);
            Assert.IsTrue(nodes.Any(n => n.NodeType == PcWbsNodeType.Product && n.Title == "پمپ شماره یک"), "the product node of the work order");
            Assert.IsTrue(nodes.All(n => n.CreatorUserId == planUser), "the agent made the structure");
            List<PcActivity> acts = Activities(e, contract);
            Assert.AreEqual(4, acts.Count, "release note, packing, shipment, delivery");
            Assert.IsTrue(e.Host.Read(u => u.Set<PcInitiationStepRecord>().Single(s => s.Initiation.ContractId == contract && s.Step == PcInitiationStep.WbsDrafted).DoneAt != null), "the initiation step of the WBS");
            Assert.AreEqual(0, Open(e, "PLAN.WEIGHTS").Count, "the generation weighted the structure");

            // the dictionary of the nodes is written by the agent, so that the reviewer has something to approve
            Closed(e, "PLAN.WBS.DICTIONARY");
            Assert.IsTrue(Nodes(e, contract).All(n => !string.IsNullOrWhiteSpace(n.Dictionary)));
            AgentTask approve = OpenOne(e, "PLAN.WBS.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, approve.State);
            Assert.AreEqual(ProcStepCommonRoles("PROJECTMANAGER,PMO"), approve.GateRoles);

            // the critical path run by the agent: every activity has dates
            Closed(e, "PLAN.CPM");
            Assert.IsTrue(Activities(e, contract).All(a => a.EarlyStart.HasValue && a.EarlyFinish.HasValue));

            // no budget source in the system: the one Input of the station
            AgentTask budget = OpenOne(e, "PLAN.BUDGET.INPUT");
            Assert.AreEqual(AgentTaskState.WaitingInput, budget.State);
            Assert.AreEqual(ProcStepCommonRoles("PLANNING,PMO"), budget.GateRoles);
            Assert.AreEqual(0, Open(e, "PLAN.BASELINE.CREATE").Count);

            // the planner enters the cost budget of the activities
            List<PcActivityDto> dtos = Svc<ResponseDto<PagedList<PcActivityDto>>, IPcScheduleService>(e, Planner, s => s.GetActivitiesAsync(new PcActivityQuery { ContractId = contract, PageSize = 100 }, Ct)).Data.ToList();
            Assert.AreEqual(4, dtos.Count);
            foreach (PcActivityDto d in dtos) { d.BudgetAtCompletion = 250_000_000m; Assert.IsTrue(Svc<ResponseDto<PcActivityDto>, IPcScheduleService>(e, Planner, s => s.SaveActivityAsync(d, Ct)).Success); }
            await Settle(e);
            Assert.AreEqual(0, Open(e, "PLAN.BUDGET.INPUT").Count);

            // the agent makes the draft baseline; every responsible unit signs, the PMO deputy approves
            AgentTask created = Closed(e, "PLAN.BASELINE.CREATE");
            Assert.AreEqual("PcBaseline", created.OutputType);
            PcBaseline draft = e.Host.Read(u => u.Set<PcBaseline>().Include(b => b.Signatures).AsNoTracking().Single(b => b.ContractId == contract));
            Assert.AreEqual(planUser, draft.CreatorUserId);
            Assert.AreEqual(PcBaselineStatus.Draft, draft.Status);
            string[] units = draft.Signatures.Select(s => s.UnitRole).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "COMMERCIAL", "QC", "WAREHOUSE" }, units);
            AgentTask sign = OpenOne(e, "PLAN.BASELINE.SIGN");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, sign.State);
            Assert.AreEqual(ProcStepCommonRoles("COMMERCIAL,QC,WAREHOUSE"), sign.GateRoles);
            Assert.AreEqual(0, Open(e, "PLAN.BASELINE.APPROVE").Count);
            int n = 1100;
            foreach (string unit in units)
                Assert.IsTrue(Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, AppHost.Principal(n++, "manager." + unit, unit), s => s.BaselineActionAsync(new PcBaselineActionDto { Id = draft.Id, Action = "sign" }, Ct)).Success, unit);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "PLAN.BASELINE.SIGN").Count);
            AgentTask baselineApprove = OpenOne(e, "PLAN.BASELINE.APPROVE");
            Assert.AreEqual("PMO", baselineApprove.GateRoles);
            Assert.IsTrue(Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, Pmo, s => s.BaselineActionAsync(new PcBaselineActionDto { Id = draft.Id, Action = "approve" }, Ct)).Success);

            // the project manager approves the nodes of the WBS (not the agent that edited them)
            foreach (PcWbsNode node in Nodes(e, contract))
                Assert.IsTrue(Svc<ResponseDto<PcWbsNodeDto>, IPcScheduleService>(e, ProjectManager, s => s.ApproveWbsNodeAsync(node.Id, Ct)).Success, node.Code);
            await Settle(e);

            Assert.AreEqual(draft.Id, e.Host.Read(u => u.Set<PcContractTerms>().Single(t => t.ContractId == contract).CurrentBaselineId));
            ResponseDto<AgentBoardDto> board = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 4).Status, Dump(e));

            // no seat id in any decision column
            int[] seatIds = e.Seats.Values.Select(x => x.UserId).ToArray();
            e.Host.Read(u =>
            {
                foreach (PcBaseline b in u.Set<PcBaseline>().Include(x => x.Signatures).AsNoTracking())
                {
                    Assert.IsFalse(b.ApprovedByUserId.HasValue && seatIds.Contains(b.ApprovedByUserId.Value));
                    Assert.IsFalse(b.Signatures.Any(s => s.UserId.HasValue && seatIds.Contains(s.UserId.Value)));
                }
                foreach (PcWbsNode node in u.Set<PcWbsNode>().AsNoTracking()) Assert.IsFalse(node.ApprovedByUserId.HasValue && seatIds.Contains(node.ApprovedByUserId.Value));
                return 0;
            });

            // the only human Input of the station: the cost budget (no source of it in the system); the other stations (the engineering BOM input) are not this test's
            string[] inputs = Tasks(e).Where(t => t.Kind == AgentStepKind.Input && t.StepCode.StartsWith("PLAN.", StringComparison.Ordinal)).Select(t => t.StepCode).Distinct().ToArray();
            CollectionAssert.AreEqual(new[] { "PLAN.BUDGET.INPUT" }, inputs, Dump(e));
        }

        [TestMethod]
        public async Task An_Inspection_Plan_That_Comes_After_The_Wbs_Adds_Activities_And_The_Plan_Is_Calculated_Again()
        {
            Env e = await NewEnv("itp");
            int contract = SeedContract(e);
            await Settle(e);
            Closed(e, "PLAN.CPM");
            int before = Activities(e, contract).Count;
            int wo = e.Host.Read(u => u.Set<WorkOrder>().Single(w => w.ContractId == contract).Id);
            e.Host.Write(u =>
            {
                Itp itp = AppHost.Add(u, new Itp { Number = "ITP-1", WorkOrderId = wo, ContractId = contract, Status = ItpStatus.InUse });
                u.SaveChanges();
                AppHost.Add(u, new ItpActivity { ItpId = itp.Id, Seq = 1, Stage = "ساخت", Activity = "بازرسی ابعادی", GatesRelease = true, Status = ItpActivityStatus.Planned });
                AppHost.Add(u, new ItpActivity { ItpId = itp.Id, Seq = 2, Stage = "آزمون", Activity = "آزمون هیدرواستاتیک", GatesRelease = true, Status = ItpActivityStatus.Planned });
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            AgentTask itpTask = Closed(e, "PLAN.ITP.ACTIVITIES");
            Assert.AreEqual("WorkOrder", itpTask.TargetType);
            Assert.AreEqual(wo, itpTask.TargetId);
            Assert.AreEqual(before + 2, Activities(e, contract).Count);
            // the repeat guard (30 minutes) holds back the second plan run for the same contract: the dates of the new activities wait for it
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "PLAN.CPM" && t.State == AgentTaskState.Done), Dump(e));
            e.Host.Write(u => { foreach (AgentTask t in u.Set<AgentTask>().Where(x => x.StepCode == "PLAN.CPM")) t.ClosedAt = DateTimeOffset.Now.AddHours(-1); u.SaveChanges(); }, Seeder);
            await Settle(e);
            Assert.AreEqual(2, Activities(e, contract).Count(a => a.SourceType == PcSourceType.ItpActivity));
            Assert.IsTrue(Activities(e, contract).All(a => a.EarlyStart.HasValue), "the plan was calculated again after the new activities");
            Assert.AreEqual(2, Tasks(e).Count(t => t.StepCode == "PLAN.CPM" && t.State == AgentTaskState.Done), Dump(e));
            await Run(e, "AG-PLAN");
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "PLAN.ITP.ACTIVITIES"), "no second task for the same ITP");
        }

        private static string ProcStepCommonRoles(string roles) => string.Join(",", roles.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct());
    }
}
