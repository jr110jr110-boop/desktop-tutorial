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
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectBalance;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.InventoryDtos;

namespace MsTests
{
    /// <summary>Agent layer, station 7 (warehouse) and the feeders of planning: the kit chain from the balance to the approved material request, the physical tasks, the relaxed kit service, and the "complete" probes of the board.</summary>
    [TestClass]
    public class AgentWarehouseTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal ProductionManager = AppHost.Principal(1071, "production.manager", "PRODUCTIONMANAGER");
        private static readonly ClaimsPrincipal Storekeeper = AppHost.Principal(1061, "storekeeper", "WAREHOUSE");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentWh_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Master(Env e) { AiSeatJobResult r = await RunSeat(e, "AG-MASTER"); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }
        private static async Task Agent(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static AgentTask OpenOne(Env e, string step) => Open(e, step).Single();
        private static AgentTask Closed(Env e, string step) => Tasks(e).Single(t => t.StepCode == step && t.State == AgentTaskState.Done);
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        private sealed class Project
        {
            public int Contract, WorkOrder, Baseline, Run, Item;
        }

        /// <summary>A contract with a current baseline, a balance run, and one activity that starts in 3 days with 5 units of the item reserved.</summary>
        private static Project SeedProject(Env e, bool withRun = true)
        {
            var p = new Project();
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-WH", Title = "قرارداد آزمایشی", InternalContractNo = "INT-WH" });
                u.SaveChanges();
                WorkOrder wo = AppHost.Add(u, new WorkOrder { Code = "WO-1", Title = "wo", ContractId = c.Id, Count = 1 });
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "I-700", Title = "bearing", Unit = "عدد" });
                u.SaveChanges();
                PbProjectBaseline baseline = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = wo.Id, ContractId = c.Id, ProductId = 1, BomHeaderId = 1, IsCurrent = true, CopiedAt = DateTimeOffset.Now.AddDays(-1), Count = 1 });
                u.SaveChanges();
                p.Contract = c.Id; p.WorkOrder = wo.Id; p.Baseline = baseline.Id; p.Item = item.Id;
                if (!withRun) return;
                ProjectMaterialBalance run = AppHost.Add(u, new ProjectMaterialBalance { ContractId = c.Id, WorkOrderId = wo.Id, BaselineId = baseline.Id, BomHeaderId = 1, RunNumber = 1, RunAt = DateTimeOffset.Now, RunByUserId = 1001, Trigger = "test" });
                u.SaveChanges();
                AppHost.Add(u, new ProjectMaterialBalanceLine { BalanceId = run.Id, ItemId = item.Id, ActivityId = 77, UseByDate = DateTime.Today.AddDays(3), ReservedQty = 5m, GrossQty = 5m, PartName = "bearing", Unit = "عدد" });
                u.SaveChanges();
                p.Run = run.Id;
            }, Seeder);
            return p;
        }

        private static Task<ResponseDto<MaterialRequestDto>> MrAction(Env e, ClaimsPrincipal who, int id, string action, string reason = null) =>
            e.Host.As(who, s => s.GetRequiredService<IInventoryWorkflowService>().RequestActionAsync(new MaterialRequestActionDto { Id = id, Action = action, Reason = reason }, Ct));

        [TestMethod]
        public async Task The_Kit_Chain_Drafts_Submits_And_Waits_For_Planning_And_The_Production_Manager_And_Then_For_The_Storekeeper()
        {
            Env e = await NewEnv("kit");
            Project p = SeedProject(e);
            int planId = e.Seats["AG-PLAN"].UserId;

            await Master(e);
            AgentTask kit = OpenOne(e, "WH.KIT.DRAFT");
            Assert.AreEqual("AG-PLAN", kit.AgentCode);
            Assert.AreEqual(7, kit.Station);
            Assert.AreEqual(AgentStepKind.Agent, kit.Kind);
            Assert.AreEqual(AgentTaskState.Queued, kit.State);
            Assert.AreEqual(p.Contract, kit.ContractId);
            Assert.AreEqual(0, Open(e, "PLAN.BALANCE.RUN").Count, "the baseline has a balance run already");

            // the planning agent drafts the kit as itself (the kit service is relaxed for this step only) and submits it in the same run
            await Agent(e, "AG-PLAN");
            MaterialRequest mr = e.Host.Read(u => u.Set<MaterialRequest>().AsNoTracking().Include(m => m.Lines).Single());
            Assert.AreEqual(MaterialRequestStatus.Submitted, mr.Status, Dump(e));
            Assert.AreEqual(planId, mr.CreatorUserId);
            Assert.AreEqual(77, mr.ActivityId);
            Assert.AreEqual(p.WorkOrder, mr.WorkOrderId);
            Assert.AreEqual(1, mr.Lines.Count);
            Assert.AreEqual(5m, mr.Lines.Single().Quantity);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "WH.KIT.DRAFT").State);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "WH.MR.SUBMIT").State);
            await Agent(e, "AG-PLAN");
            Assert.AreEqual(1, e.Host.Read(u => u.Set<MaterialRequest>().Count()), "the kit is made once per activity");

            // the two approvals of the material request: planning, then the production manager
            await Master(e);
            AgentTask gate = OpenOne(e, "WH.MR.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual("PLANNING,PMO,CONSTRUCTION,PRODUCTIONMANAGER", gate.GateRoles);
            Assert.AreEqual("/inventory/material-requests/" + mr.Id, gate.Link);
            Assert.AreEqual(p.Contract, gate.ContractId);
            Assert.IsFalse((await MrAction(e, Storekeeper, mr.Id, "approve-planning")).Success, "the storekeeper does not approve");
            Assert.IsTrue((await MrAction(e, Planner, mr.Id, "approve-planning")).Success);
            await Master(e);
            Assert.AreEqual("CONSTRUCTION,PRODUCTIONMANAGER", OpenOne(e, "WH.MR.APPROVE").GateRoles, "planning is done: only the production manager is asked");
            Assert.IsFalse((await MrAction(e, Planner, mr.Id, "approve-manufacturing")).Success);
            Assert.IsTrue((await MrAction(e, ProductionManager, mr.Id, "approve-manufacturing")).Success);
            Assert.AreEqual(MaterialRequestStatus.Approved, e.Host.Read(u => u.Set<MaterialRequest>().Single().Status));

            // the issue is physical work
            await Master(e);
            Assert.AreEqual(0, Open(e, "WH.MR.APPROVE").Count);
            AgentTask issue = OpenOne(e, "WH.MR.ISSUE");
            Assert.AreEqual(AgentStepKind.Physical, issue.Kind);
            Assert.AreEqual(AgentTaskState.WaitingPhysical, issue.State);
            Assert.AreEqual("WAREHOUSE,WHSUPERVISOR", issue.GateRoles);

            var board = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(p.Contract, Ct));
            Assert.AreEqual(AgentCellStatus.WaitingPhysical, board.Data.Rows.Single().Cells[6].Status);

            // the warehouse closes the request after the issue: the task closes, and the station is complete (the probe)
            Assert.IsTrue((await MrAction(e, Storekeeper, mr.Id, "close")).Success);
            await Master(e);
            Assert.AreEqual(0, Open(e, "WH.MR.ISSUE").Count);
            var done = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(p.Contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, done.Data.Rows.Single().Cells[6].Status, "station 7 is complete for the contract");
            Assert.AreEqual(AgentCellStatus.Done, done.Data.Rows.Single().Cells[6].Status);
            Assert.AreEqual(AgentCellStatus.NotStarted, done.Data.Rows.Single().Cells[0].Status);

            // the agent never signed anything
            MaterialRequest last = e.Host.Read(u => u.Set<MaterialRequest>().AsNoTracking().Single());
            Assert.AreEqual(1051, last.PlanningApprovedByUserId);
            Assert.AreEqual(1071, last.ManufacturingApprovedByUserId);
            Assert.IsFalse(e.Seats.Values.Any(s => s.UserId == last.PlanningApprovedByUserId || s.UserId == last.ManufacturingApprovedByUserId || s.UserId == last.RejectedByUserId || s.UserId == last.ClosedByUserId));
        }

        [TestMethod]
        public async Task The_Kit_Service_Is_Relaxed_For_The_Kit_Step_Only_And_A_Baseline_Without_A_Balance_Gets_One_From_The_Planning_Agent()
        {
            Env e = await NewEnv("relax");
            SeedProject(e);
            Func<string, Task<ResponseDto<ViewModels.NasbNiro.ProjectBalanceDtos.KitRunResultDto>>> asPlan = code =>
                AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-PLAN"], "AG-PLAN", sp =>
                {
                    using (AgentStepContext.Enter(code)) return sp.GetRequiredService<IKitService>().DraftKitsAsync(false, Ct);
                });
            var outside = await asPlan(null);
            Assert.IsFalse(outside.Success, "an agent outside the kit step is still refused");
            var other = await asPlan("PLAN.BALANCE.RUN");
            Assert.IsFalse(other.Success, "and inside another step");
            var inside = await asPlan("WH.KIT.DRAFT");
            Assert.IsTrue(inside.Success, inside.Message);
            Assert.AreEqual(1, inside.Data.Drafted);
            // a human with the planning role is unchanged
            var human = await e.Host.As(Planner, s => s.GetRequiredService<IKitService>().DraftKitsAsync(false, Ct));
            Assert.IsTrue(human.Success);
            Assert.AreEqual(0, human.Data.Drafted, "the activity has its kit");

            // a baseline with no run: the planning agent's balance task
            Env f = await NewEnv("balance");
            SeedProject(f, withRun: false);
            await Master(f);
            AgentTask balance = OpenOne(f, "PLAN.BALANCE.RUN");
            Assert.AreEqual("AG-PLAN", balance.AgentCode);
            Assert.AreEqual(4, balance.Station);
            Assert.AreEqual(AgentStepKind.Agent, balance.Kind);
            Assert.AreEqual("WO-1", balance.TargetNumber);
        }

        [TestMethod]
        public async Task The_Receipt_Confirmation_And_The_Incoming_Inspection_Are_Physical_Tasks_For_The_Storekeeper_And_Quality()
        {
            Env e = await NewEnv("physical");
            Project p = SeedProject(e, withRun: false);
            e.Host.Write(u =>
            {
                Warehouse w = AppHost.Add(u, new Warehouse { Code = "W1", Title = "main" });
                u.SaveChanges();
                InventoryDocument receipt = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.PurchaseReceipt, Number = "RC-1", DocumentDate = DateTime.Today, Status = InventoryDocumentStatus.Draft, WarehouseId = w.Id, PurchaseOrderId = 5 });
                u.SaveChanges();
                AppHost.Add(u, new InventoryDocumentLine { DocumentId = receipt.Id, RowNumber = 1, ItemId = p.Item, Quantity = 4m });
                AppHost.Add(u, new QcInspection { Number = "QI-1", ReceiptDocumentId = receipt.Id, ReceiptLineId = 1, ItemId = p.Item, WarehouseId = w.Id, Quantity = 4m, Status = InspectionStatus.Pending });
                u.SaveChanges();
            }, Seeder);
            await Master(e);
            AgentTask confirm = OpenOne(e, "WH.DOC.CONFIRM");
            Assert.AreEqual(AgentStepKind.Physical, confirm.Kind);
            Assert.AreEqual("WAREHOUSE,WHSUPERVISOR", confirm.GateRoles);
            Assert.AreEqual("RC-1", confirm.TargetNumber);
            StringAssert.Contains(confirm.Summary, "رسید خرید");
            AgentTask inspect = OpenOne(e, "WH.INSPECT");
            Assert.AreEqual(AgentStepKind.Physical, inspect.Kind);
            Assert.AreEqual("QC", inspect.GateRoles);
            Assert.AreEqual("/inventory/inspections", inspect.Link);
            Assert.AreEqual("QI-1", inspect.TargetNumber);
            // both appear in the inbox of the right roles only
            var storekeeper = await e.Host.As(Storekeeper, s => s.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery { Kind = AgentStepKind.Physical }, Ct));
            Assert.AreEqual(1, storekeeper.Data.Count);
            Assert.AreEqual("WH.DOC.CONFIRM", storekeeper.Data[0].StepCode);
            var quality = await e.Host.As(AppHost.Principal(1081, "qc", "QC"), s => s.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery { Kind = AgentStepKind.Physical }, Ct));
            Assert.AreEqual(1, quality.Data.Count);
            Assert.AreEqual("WH.INSPECT", quality.Data[0].StepCode);
        }

        [TestMethod]
        public async Task The_Procurement_Station_Is_Complete_For_A_Contract_Whose_Requests_And_Orders_Are_All_Finished()
        {
            Env e = await NewEnv("probe6");
            Project done = SeedProject(e, withRun: false);
            int open = 0;
            e.Host.Write(u =>
            {
                ContractEntity c2 = AppHost.Add(u, new ContractEntity { Code = "C-2", Title = "second", InternalContractNo = "INT-2" });
                u.SaveChanges();
                open = c2.Id;
                AppHost.Add(u, new PurchaseRequest { Number = "PR-A", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Closed, RequestDate = DateTime.Today, ContractId = done.Contract });
                AppHost.Add(u, new PurchaseRequest { Number = "PR-B", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Ordered, RequestDate = DateTime.Today, ContractId = done.Contract });
                AppHost.Add(u, new PurchaseOrder { Number = "PO-A", Status = PurchaseOrderStatus.Received, Kind = PurchaseKind.Goods, ProviderId = 1, OrderDate = DateTime.Today, ContractId = done.Contract });
                AppHost.Add(u, new PurchaseRequest { Number = "PR-C", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Approved, RequestDate = DateTime.Today, ContractId = open });
                u.SaveChanges();
            }, Seeder);
            var board = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(done.Contract, Ct));
            AgentBoardRowDto finished = board.Data.Rows.Single();
            Assert.AreEqual(AgentCellStatus.Done, finished.Cells[5].Status);
            AgentBoardRowDto running = (await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(open, Ct))).Data.Rows.Single();
            Assert.AreNotEqual(AgentCellStatus.Done, running.Cells[5].Status, "an approved request without its order is not finished");
            // an order still in the shop (released) keeps the station open
            e.Host.Write(u => { AppHost.Add(u, new PurchaseOrder { Number = "PO-B", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Goods, ProviderId = 1, OrderDate = DateTime.Today, ContractId = done.Contract }); u.SaveChanges(); }, Seeder);
            var again = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(done.Contract, Ct));
            Assert.AreNotEqual(AgentCellStatus.Done, again.Data.Rows.Single().Cells[5].Status);
        }
    }
}
