using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 8 (production): the production agent drafts the orders, takes their components and runs the release check (the reasons go to the task while it fails); supervisors release; the shop floor
    /// has one physical task per released order; the QC inspector decides on each operation in the QC hold. No decision column ever holds an agent seat.
    /// </summary>
    [TestClass]
    public class AgentProductionTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Supervisor = AppHost.Principal(1501, "shop.supervisor", "SHOPSUPERVISOR");
        private static readonly ClaimsPrincipal Manager = AppHost.Principal(1502, "production.manager", "PRODUCTIONMANAGER");
        private static readonly ClaimsPrincipal Inspector = AppHost.Principal(1503, "qc.inspector", "QC");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentMf_" + name + "_" + Guid.NewGuid().ToString("N"));
            var e = new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                foreach ((int id, string n) in new[] { (1501, "shop.supervisor"), (1502, "production.manager"), (1503, "qc.inspector") })
                    AppHost.Add(u, new User { Id = id, UserName = n, Email = n + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return e;
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-MF"); }
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

        private static void AssertRoles(string expected, AgentTask task) => CollectionAssert.AreEquivalent(expected.Split(','), task.GateRoles.Split(','), task.StepCode + " roles " + task.GateRoles);

        private static MfProductionOrder Order(Env e, int id) => e.Host.Read(u => u.Set<MfProductionOrder>().AsNoTracking().Include(o => o.Operations).Include(o => o.Components).Single(o => o.Id == id));

        private static int SeedContract(Env e, out int workOrder)
        {
            int id = 0, wo = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-MF", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30) });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-MF", Title = "پمپ", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                u.SaveChanges();
                id = c.Id; wo = w.Id;
            }, Seeder);
            workOrder = wo;
            return id;
        }

        // ------------------------------------------------------------------------------------------------ drafting, components and the failing check

        [TestMethod]
        public async Task M9_The_Engineering_Documents_Check_Of_A_Controlled_Contract_Blocks_The_Release_And_Cannot_Be_Overridden()
        {
            Env e = await NewEnv("engdocs");
            int contract = SeedContract(e, out int wo);
            int order = SeedReadyOrder(e, contract, wo, "PO-ENG", out _);
            ResponseDto<ReleaseCheckResultDto> before = await e.Host.As(Supervisor, s => s.GetRequiredService<IMfProductionService>().ReleaseCheckAsync(order, Ct));
            Assert.IsFalse(before.Data.Checks.Any(c => c.Code == "engdocs"), "a legacy contract adds no item");
            MsTests.EnKit.StartControl(e.Host);   // the control started yesterday: the contract (created now) is controlled and has no released engineering handover
            e.Host.Write(u => { u.Set<ContractEntity>().First(c => c.Id == contract).CreateDate = DateTimeOffset.Now; u.SaveChanges(); }, Seeder);
            ResponseDto<ReleaseCheckResultDto> after = await e.Host.As(Manager, s => s.GetRequiredService<IMfProductionService>().ReleaseCheckAsync(order, Ct));
            ReleaseCheckDto check = after.Data.Checks.Single(c => c.Code == "engdocs");
            Assert.IsFalse(check.Passed);
            Assert.IsFalse(check.Overridable, "no override reason releases an order without the PMO-released engineering documents");
            Assert.IsFalse(after.Data.CanRelease);
            ResponseDto<OrderDto> refused = await e.Host.As(Manager, s => s.GetRequiredService<IMfProductionService>().ReleaseAsync(new ReleaseOrderDto { Id = order, OverrideReason = "دلیل کافی برای عبور از این بررسی نوشته شده است" }, Ct));
            Assert.IsFalse(refused.Success);
            Assert.AreNotEqual(MfOrderStatus.Released, Order(e, order).Status);
        }

        [TestMethod]
        public async Task The_Agent_Drafts_The_Orders_Takes_The_Components_And_Reports_The_Reasons_Of_A_Failing_Release_Check_On_The_Task()
        {
            Env e = await NewEnv("draft");
            int contract = SeedContract(e, out int wo);
            int mfSeat = e.Seats["AG-MF"].UserId;
            int item = 0;
            e.Host.Write(u =>
            {
                // a make process whose output code is an item of the warehouse: the legacy way to name what the work order makes
                InventoryItem part = AppHost.Add(u, new InventoryItem { Code = "EXIT-1", Title = "شافت", Unit = "عدد" });
                AssemblyEntity asm = AppHost.Add(u, new AssemblyEntity { Title = "مونتاژ", Code = "A-1", WorkOrderId = wo });
                u.SaveChanges();
                Bom bom = AppHost.Add(u, new Bom { AssemblyId = asm.Id, Title = "شافت", Count = 2, Unit = "عدد" });
                u.SaveChanges();
                AppHost.Add(u, new ProcessEntity { BomId = bom.Id, Title = "ساخت شافت", Type = ProcessType.Assembly, ExitCommodityCode = "EXIT-1" });
                u.SaveChanges();
                item = part.Id;
            }, Seeder);

            await Settle(e);
            AgentTask draft = Closed(e, "MF.ORDERS.DRAFT");
            Assert.AreEqual(8, draft.Station);
            Assert.AreEqual("AG-MF", draft.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, draft.Kind);
            Assert.AreEqual(contract, draft.ContractId);
            MfProductionOrder order = e.Host.Read(u => u.Set<MfProductionOrder>().AsNoTracking().Single(o => o.WorkOrderId == wo));
            Assert.AreEqual(MfOrderStatus.Draft, order.Status);
            Assert.IsTrue(order.AutoDrafted);
            Assert.AreEqual(mfSeat, order.CreatorUserId);
            Assert.AreEqual(2m, order.Quantity);
            Assert.AreEqual(item, order.ItemId);
            Assert.AreEqual(0, Open(e, "MF.ORDERS.DRAFT").Count, "one order is enough for the work order");

            // the components are taken again from the master by the agent (the master gives none here), once
            Closed(e, "MF.COMPONENTS");
            Assert.AreEqual(mfSeat, Order(e, order.Id).EditedByUserId);
            Assert.AreEqual(0, Open(e, "MF.COMPONENTS").Count);

            // the release check fails (no routing, no drawing, no ITP): the reasons are on the task, which waits and is tried again later; no gate yet
            AgentTask check = OpenOne(e, "MF.RELEASE.CHECK");
            Assert.AreEqual(AgentTaskState.Queued, check.State);
            Assert.AreEqual(order.Id, check.TargetId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(check.LastError), "the reasons are the last error of the task");
            StringAssert.Contains(check.LastError, "مسیر ساخت");
            Assert.AreEqual(0, check.AttemptCount, "a reason that is not met yet is not an attempt");
            Assert.AreEqual(0, Open(e, "MF.RELEASE").Count);
            Assert.AreEqual(0, Open(e, "MF.SHOPFLOOR").Count);

            Assert.AreEqual(MfOrderStatus.Draft, Order(e, order.Id).Status, "the agent never releases");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 8 && t.Kind == AgentStepKind.Input), "no Input task: the check names what is missing and its owners fix it where it lives");
        }

        // ------------------------------------------------------------------------------------------------ a partly covered work order

        [TestMethod]
        public async Task A_Work_Order_Whose_Baseline_Has_A_Make_Part_Without_An_Order_Gets_Only_The_Missing_Order()
        {
            Env e = await NewEnv("partial");
            int contract = SeedContract(e, out int wo);
            int top = 0, partA = 0, partB = 0;
            e.Host.Write(u =>
            {
                InventoryItem t = AppHost.Add(u, new InventoryItem { Code = "TOP", Title = "پمپ", Unit = "عدد", PartNumber = "TOP" });
                InventoryItem a = AppHost.Add(u, new InventoryItem { Code = "P-A", Title = "بدنه", Unit = "عدد", PartNumber = "P-A" });
                InventoryItem b = AppHost.Add(u, new InventoryItem { Code = "P-B", Title = "شافت", Unit = "عدد", PartNumber = "P-B" });
                PbBomHeader h = AppHost.Add(u, new PbBomHeader { AssemblyPartNumber = "TOP", Title = "پمپ", Code = "BOM-TOP", Revision = 1 });
                u.SaveChanges();
                PbProjectBaseline bl = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = wo, ContractId = contract, ProductId = 1, BomHeaderId = h.Id, IsCurrent = true, CopiedAt = DateTimeOffset.Now, Count = 1 });
                u.SaveChanges();
                int sort = 0;
                foreach (InventoryItem part in new[] { a, b })
                    AppHost.Add(u, new PbProjectBomLine { BaselineId = bl.Id, SortOrder = ++sort, PartNumber = part.PartNumber, PartName = part.Title, ItemId = part.Id, Qty = 1, TotalQty = 1, Unit = "عدد", MakeOrBuy = PbMakeOrBuy.Make });
                u.SaveChanges();
                // the top assembly and part A already have an order (a person made them, nobody touches them again); part B has none
                foreach (InventoryItem covered in new[] { t, a })
                    AppHost.Add(u, new MfProductionOrder { Number = "PO-" + covered.Code, WorkOrderId = wo, ContractId = contract, ItemId = covered.Id, Quantity = 1, Unit = "عدد", Status = MfOrderStatus.Draft, EditedByUserId = 1001 });
                u.SaveChanges();
                top = t.Id; partA = a.Id; partB = b.Id;
            }, Seeder);

            await Settle(e);
            AgentTask draft = Closed(e, "MF.ORDERS.DRAFT");
            Assert.AreEqual(wo, draft.TargetId);
            List<MfProductionOrder> orders = e.Host.Read(u => u.Set<MfProductionOrder>().AsNoTracking().Where(o => o.WorkOrderId == wo).ToList());
            Assert.AreEqual(3, orders.Count, "exactly the missing order was added, nothing was duplicated");
            Assert.AreEqual(1, orders.Count(o => o.ItemId == top));
            Assert.AreEqual(1, orders.Count(o => o.ItemId == partA));
            MfProductionOrder made = orders.Single(o => o.ItemId == partB);
            Assert.AreEqual(e.Seats["AG-MF"].UserId, made.CreatorUserId);
            Assert.IsTrue(made.AutoDrafted);
            Assert.AreEqual(MfOrderStatus.Draft, made.Status);
            Assert.AreEqual(0, Open(e, "MF.ORDERS.DRAFT").Count, "complete now: no task is made again");
        }

        // ------------------------------------------------------------------------------------------------ release, shop floor, QC decision

        /// <summary>A routing of one step with a work centre, approved for exactly its content, a "no drawing" flag, an approved ITP and a draft order with one operation (and a component when asked).</summary>
        private static int SeedReadyOrder(Env e, int contract, int wo, string number, out int operation, decimal componentQuantity = 0m, bool first = true)
        {
            int orderId = 0, opId = 0;
            e.Host.Write(u =>
            {
                InventoryItem part;
                if (first)
                {
                    part = AppHost.Add(u, new InventoryItem { Code = "MF-PART", Title = "بدنه پمپ", Unit = "عدد" });
                    u.SaveChanges();
                    MfWorkCenter wc = AppHost.Add(u, new MfWorkCenter { Code = "WC-1", Title = "تراش", IsActive = true });
                    u.SaveChanges();
                    OperationTemplate template = AppHost.Add(u, new OperationTemplate { ItemId = part.Id, Sequence = 10, Title = "تراشکاری", RequiresQc = true, IsActive = true });
                    u.SaveChanges();
                    AppHost.Add(u, new MfOperationStandard { OperationTemplateId = template.Id, WorkCenterId = wc.Id, InspectionRequirement = MfInspectionRequirement.Qc });
                    u.SaveChanges();
                    MfResolvedRouting routing = MfRouting.KeyForItemAsync(u, part, Ct).GetAwaiter().GetResult();
                    var standards = MfRouting.StandardsAsync(u, routing.Steps.Select(s => s.Id), Ct).GetAwaiter().GetResult();
                    string hash = MfRouting.HashAsync(u, routing.Steps, standards, Ct).GetAwaiter().GetResult();
                    AppHost.Add(u, new MfRoutingRevision { ItemId = part.Id, Revision = 1, Status = MfRoutingStatus.Approved, ContentHash = hash, ApprovedAt = DateTimeOffset.Now, ProductionApprovedByUserId = 1001, EngineeringApprovedByUserId = 1002 });
                    AppHost.Add(u, new NoDrawingFlag { ItemId = part.Id, PartNumber = "MF-PART", Reason = "قطعه ساده", Status = NoDrawingStatus.Active });
                    AppHost.Add(u, new Itp { Number = "ITP-MF", WorkOrderId = wo, ContractId = contract, Revision = 1, Status = ItpStatus.ClientApproved });
                    u.SaveChanges();
                }
                part = u.Set<InventoryItem>().Single(i => i.Code == "MF-PART");
                MfRoutingRevision rev = u.Set<MfRoutingRevision>().Single(r => r.ItemId == part.Id);
                OperationTemplate t = u.Set<OperationTemplate>().Single(x => x.ItemId == part.Id);
                MfWorkCenter center = u.Set<MfWorkCenter>().Single(c => c.Code == "WC-1");
                var order = AppHost.Add(u, new MfProductionOrder
                {
                    Number = number, WorkOrderId = wo, ContractId = contract, ItemId = part.Id, Quantity = 1, Unit = "عدد", Status = MfOrderStatus.Draft, AutoDrafted = true, RoutingRevisionId = rev.Id,
                    Operations = new List<MfOperation>(), Components = new List<MfOrderComponent>(), EditedByUserId = 1001,
                });
                order.Operations.Add(new MfOperation { Sequence = 10, OperationTemplateId = t.Id, Title = "تراشکاری", WorkCenterId = center.Id, Status = MfOperationStatus.Waiting, InspectionRequirement = MfInspectionRequirement.Qc });
                if (componentQuantity > 0)
                {
                    InventoryItem raw = AppHost.Add(u, new InventoryItem { Code = "RM-" + number, Title = "شمش", Unit = "کیلوگرم" });
                    u.SaveChanges();
                    order.Components.Add(new MfOrderComponent { ItemId = raw.Id, Quantity = componentQuantity, Unit = "کیلوگرم" });
                }
                u.SaveChanges();
                orderId = order.Id; opId = order.Operations.Single().Id;
            }, Seeder);
            operation = opId;
            return orderId;
        }

        [TestMethod]
        public async Task A_Passing_Check_Shows_The_Release_Gate_And_A_Shortage_Only_The_Production_Manager_Then_One_Shop_Floor_Task_And_One_QC_Gate_Per_Operation_In_The_Hold()
        {
            Env e = await NewEnv("release");
            int contract = SeedContract(e, out int wo);
            int ok = SeedReadyOrder(e, contract, wo, "PO-OK", out int okOp);
            int shortOrder = SeedReadyOrder(e, contract, wo, "PO-SHORT", out _, 5m, false);
            int ready = ok;

            ResponseDto<ReleaseCheckResultDto> direct = await e.Host.As(Supervisor, s => s.GetRequiredService<IMfProductionService>().ReleaseCheckAsync(ok, Ct));
            Assert.IsTrue(direct.Success, direct.Message);
            Assert.IsTrue(direct.Data.CanRelease, "the seed passes the service's own release check: " + string.Join(" | ", direct.Data.Checks.Where(c => !c.Passed).Select(c => c.Code + ":" + c.Message)));

            await Settle(e);
            // the agent runs nothing for an order that passes: no check task, the gate is there
            Assert.AreEqual(0, Open(e, "MF.RELEASE.CHECK").Count, Dump(e));
            List<AgentTask> gates = Open(e, "MF.RELEASE");
            Assert.AreEqual(2, gates.Count, Dump(e));
            AgentTask gate = gates.Single(g => g.TargetId == ok);
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual(8, gate.Station);
            AssertRoles("SHOPSUPERVISOR,PRODUCTIONMANAGER", gate);
            // the order with a shortage of stock is released by the production manager only (with a reason)
            AgentTask shortGate = gates.Single(g => g.TargetId == shortOrder);
            AssertRoles("PRODUCTIONMANAGER", shortGate);

            ResponseDto<OrderDto> refused = await e.Host.As(Supervisor, s => s.GetRequiredService<IMfProductionService>().ReleaseAsync(new ReleaseOrderDto { Id = shortOrder, OverrideReason = "دلیل" }, Ct));
            Assert.IsFalse(refused.Success, "the service agrees: a shortage is for the production manager");
            ResponseDto<OrderDto> released = await e.Host.As(Supervisor, s => s.GetRequiredService<IMfProductionService>().ReleaseAsync(new ReleaseOrderDto { Id = ok }, Ct));
            Assert.IsTrue(released.Success, released.Message);
            await Settle(e);
            Closed(e, "MF.RELEASE");
            MfProductionOrder rel = Order(e, ok);
            Assert.AreEqual(MfOrderStatus.Released, rel.Status);
            Assert.AreEqual(1501, rel.ReleasedByUserId);
            Assert.IsFalse(e.Seats.Values.Any(x => x.UserId == rel.ReleasedByUserId), "no seat released the order");

            // one physical task for the released order, however many operations it has
            AgentTask floor = OpenOne(e, "MF.SHOPFLOOR");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, floor.State);
            Assert.AreEqual(AgentStepKind.Physical, floor.Kind);
            Assert.AreEqual(ok, floor.TargetId);
            AssertRoles("OPERATOR,SHOPSUPERVISOR,PRODUCTIONMANAGER,ASSEMBLER,CONSTRUCTION", floor);
            Assert.AreEqual(1, Open(e, "MF.RELEASE").Count, "the order with the shortage still waits for the manager");

            // the operation reaches the QC hold: one gate for the inspector (the operator's terminal does this; the state is the fact)
            e.Host.Write(u => { MfOperation op = u.Set<MfOperation>().Single(x => x.Id == okOp); op.Status = MfOperationStatus.QcHold; op.QcRequestedAt = DateTimeOffset.Now; u.SaveChanges(); }, Seeder);
            await Settle(e);
            AgentTask qc = OpenOne(e, "MF.QC.DECISION");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, qc.State);
            Assert.AreEqual("MfOperation", qc.TargetType);
            Assert.AreEqual(okOp, qc.TargetId);
            Assert.AreEqual("QC", qc.GateRoles);
            Assert.AreEqual(1, Open(e, "MF.SHOPFLOOR").Count, "still one task for the order");

            ResponseDto<OperationDto> decided = await e.Host.As(Inspector, s => s.GetRequiredService<IMfProductionService>().QcDecisionAsync(new QcDecisionDto { OperationId = okOp, Result = QcResult.Accept }, Ct));
            Assert.IsTrue(decided.Success, decided.Message);
            await Settle(e);
            Closed(e, "MF.QC.DECISION");

            // the order is finished: the shop floor task closes by itself
            e.Host.Write(u =>
            {
                MfProductionOrder o = u.Set<MfProductionOrder>().Single(x => x.Id == ok);
                o.Status = MfOrderStatus.Completed;
                u.Set<MfProductionOrder>().Single(x => x.Id == shortOrder).Status = MfOrderStatus.Cancelled;
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            Closed(e, "MF.SHOPFLOOR");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 8 && t.State < AgentTaskState.Done), Dump(e));

            // the probe: every order of the contract is finished
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 8).Status);
            Assert.AreEqual(ready, ok);
        }
    }
}
