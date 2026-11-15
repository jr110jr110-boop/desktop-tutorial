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
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, stations 10 and 11 (packing, shipping, delivery): the shipping agent drafts the shipment with its lines and packing list from the released release note; people pack, check, give the shipping
    /// permit, confirm the warehouse document, ship, deliver, hand over and commission. No decision column of the documents ever holds an agent seat.
    /// </summary>
    [TestClass]
    public class AgentShippingTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Packer = AppHost.Principal(1301, "packer", "WAREHOUSE");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(1302, "qc.checker", "QC");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1303, "planner.one", "PLANNING");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(1304, "commercial.one", "COMMERCIAL");
        private static readonly ClaimsPrincipal Service = AppHost.Principal(1305, "service.one", "SERVICE");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentShip_" + name + "_" + Guid.NewGuid().ToString("N"));
            var e = new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                foreach ((int id, string name2) in new[] { (1301, "packer"), (1302, "qc.checker"), (1303, "planner.one"), (1304, "commercial.one"), (1305, "service.one") })
                    AppHost.Add(u, new User { Id = id, UserName = name2, Email = name2 + "@example.com", IsActive = true });
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
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-SHIP"); }
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

        private static PcShipment Shipment(Env e, int wo) => e.Host.Read(u => u.Set<PcShipment>().AsNoTracking().Include(s => s.Lines).Single(s => s.WorkOrderId == wo));

        private static Task<ResponseDto<PcShipmentDto>> Act(Env e, ClaimsPrincipal who, PcShipmentActionDto dto) =>
            e.Host.As(who, s => s.GetRequiredService<IPcReportService>().ShipmentActionAsync(dto, Ct));

        /// <summary>A running contract, a work order of one unit with one serial piece and a released release note that lists the piece.</summary>
        private static int SeedReleased(Env e, out int contract, out int serialUnit, int count = 1)
        {
            int cid = 0, wo = 0, su = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-SH", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30) });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-SH", Title = "پمپ", ContractId = c.Id, Count = count, EquipmentId = 1 });
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "P-SH", Title = "پمپ سانتریفیوژ", Unit = "دستگاه", TrackingMode = ItemTrackingMode.Serial });
                u.SaveChanges();
                SerialUnit unit = AppHost.Add(u, new SerialUnit { ItemId = item.Id, SerialNumber = "SN-0001", Status = SerialUnitStatus.InStock, WorkOrderId = w.Id });
                u.SaveChanges();
                var note = AppHost.Add(u, new ReleaseNote { Number = "RN-SH", WorkOrderId = w.Id, ContractId = c.Id, Status = ReleaseNoteStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-1), ReleasedByUserId = 1001, Lines = new List<ReleaseNoteLine>() });
                note.Lines.Add(new ReleaseNoteLine { RowNumber = 1, ItemId = item.Id, Quantity = 1, SerialNumbers = "SN-0001" });
                u.SaveChanges();
                cid = c.Id; wo = w.Id; su = unit.Id;
            }, Seeder);
            contract = cid; serialUnit = su;
            return wo;
        }

        [TestMethod]
        public async Task The_Shipment_Is_Drafted_By_The_Agent_And_Then_Packed_Checked_Released_Shipped_Delivered_Handed_Over_And_Commissioned_By_People()
        {
            Env e = await NewEnv("flow");
            int wo = SeedReleased(e, out int contract, out int serialUnit);
            int shipSeat = e.Seats["AG-SHIP"].UserId;
            Func<int, bool> isSeat = id => e.Seats.Values.Any(s => s.UserId == id);

            // 1. the agent drafts the shipment with the serial piece of the release note and a packing list written from the lines
            await Settle(e);
            AgentTask draft = Closed(e, "SHIP.DRAFT");
            Assert.AreEqual(10, draft.Station);
            Assert.AreEqual("AG-SHIP", draft.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, draft.Kind);
            PcShipment s = Shipment(e, wo);
            Assert.AreEqual(PcShipmentStatus.Planned, s.Status);
            Assert.AreEqual(shipSeat, s.CreatorUserId);
            Assert.AreEqual(contract, s.ContractId);
            Assert.AreEqual(1, s.Lines.Count);
            Assert.AreEqual(serialUnit, s.Lines.Single().SerialUnitId);
            Assert.AreEqual(1m, s.Lines.Single().Quantity);
            StringAssert.Contains(s.PackingList, "SN-0001");
            StringAssert.Contains(s.PackingList, "RN-SH");
            AgentTask pack = OpenOne(e, "SHIP.PACK");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, pack.State);
            Assert.AreEqual(AgentStepKind.Physical, pack.Kind);
            Assert.AreEqual(10, pack.Station);
            AssertRoles("PLANNING,PMO,WAREHOUSE,WHSUPERVISOR", pack);
            Assert.AreEqual(0, Open(e, "SHIP.CHECK").Count + Open(e, "SHIP.RELEASE").Count + Open(e, "SHIP.DELIVERYDOC").Count + Open(e, "SHIP.SHIP").Count);

            // 2. the storekeeper packs and records the verification
            Assert.IsTrue((await Act(e, Packer, new PcShipmentActionDto { Id = s.Id, Action = "pack", PackingVerification = "تعداد و سلامت بسته‌ها شمارش شد" })).Success);
            await Settle(e);
            Closed(e, "SHIP.PACK");
            AgentTask check = OpenOne(e, "SHIP.CHECK");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, check.State);
            AssertRoles("QC,QA", check);
            Assert.AreEqual(0, Open(e, "SHIP.RELEASE").Count, "the permit waits for the check");

            // 3. QC checks the packing (the service refuses the packer)
            ResponseDto<PcShipmentDto> samePerson = await Act(e, AppHost.Principal(1301, "packer", "WAREHOUSE,QC"), new PcShipmentActionDto { Id = s.Id, Action = "check-packing" });
            Assert.IsFalse(samePerson.Success, "the packer does not check his own packing");
            Assert.IsTrue((await Act(e, Qc, new PcShipmentActionDto { Id = s.Id, Action = "check-packing" })).Success);
            await Settle(e);
            Closed(e, "SHIP.CHECK");
            AgentTask release = OpenOne(e, "SHIP.RELEASE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, release.State);
            AssertRoles("PLANNING,PMO", release);

            // 4. planning gives the shipping permit
            Assert.IsTrue((await Act(e, Planner, new PcShipmentActionDto { Id = s.Id, Action = "release" })).Success);
            await Settle(e);
            Closed(e, "SHIP.RELEASE");
            AgentTask docTask = OpenOne(e, "SHIP.DELIVERYDOC");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, docTask.State);
            AssertRoles("WAREHOUSE,WHSUPERVISOR", docTask);
            Assert.AreEqual(0, Open(e, "SHIP.SHIP").Count, "the shipment cannot leave before the warehouse document is confirmed");

            // 5. the warehouse makes and confirms the customer-delivery document (physical: the goods are counted and loaded)
            int document = 0;
            e.Host.Write(u =>
            {
                InventoryDocument d = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.CustomerDelivery, Number = "CD-1", Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today, ContractId = contract, WorkOrderId = wo });
                u.SaveChanges();
                document = d.Id;
            }, Seeder);
            await Settle(e);
            Closed(e, "SHIP.DELIVERYDOC");
            AgentTask ship = OpenOne(e, "SHIP.SHIP");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, ship.State);
            AssertRoles("PLANNING,PMO,WAREHOUSE,WHSUPERVISOR", ship);

            // 6. the shipment leaves: the waybill exists only on the carrier's paper (typed by the person who ships)
            Assert.IsTrue((await Act(e, Packer, new PcShipmentActionDto { Id = s.Id, Action = "ship", Waybill = "WB-77", ShippingDocuments = "بارنامه و صورت‌وضعیت حمل", InventoryDocumentId = document })).Success);
            await Settle(e);
            Closed(e, "SHIP.SHIP");
            AgentTask deliver = OpenOne(e, "SHIP.DELIVER");
            Assert.AreEqual(11, deliver.Station);
            Assert.AreEqual(AgentStepKind.Physical, deliver.Kind);
            AssertRoles("PLANNING,PMO,COMMERCIAL,WAREHOUSE,WHSUPERVISOR", deliver);

            // 7. delivered with the signed receipt (a paper outside the system)
            Assert.IsTrue((await Act(e, Commercial, new PcShipmentActionDto { Id = s.Id, Action = "deliver", DeliveryReceiptNumber = "RC-9", SignedReceiptDocumentId = Guid.NewGuid() })).Success);
            await Settle(e);
            Closed(e, "SHIP.DELIVER");
            AgentTask handover = OpenOne(e, "SHIP.HANDOVER");
            Assert.AreEqual(AgentStepKind.Physical, handover.Kind);
            AssertRoles("PLANNING,PMO,COMMERCIAL", handover);
            ResponseDto<AgentBoardDto> mid = await e.Host.As(Seeder, sp => sp.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, mid.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 10).Status, "station 10 is finished once the shipment left");
            Assert.AreNotEqual(AgentCellStatus.Done, mid.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 11).Status);

            // 8. the handover minutes, then the commissioning date
            Assert.IsTrue((await Act(e, Commercial, new PcShipmentActionDto { Id = s.Id, Action = "handover", HandoverMinutesNumber = "HM-5" })).Success);
            await Settle(e);
            Closed(e, "SHIP.HANDOVER");
            AgentTask commission = OpenOne(e, "SHIP.COMMISSION");
            Assert.AreEqual(AgentStepKind.Physical, commission.Kind);
            AssertRoles("PLANNING,PMO,COMMERCIAL,SERVICE", commission);
            Assert.IsTrue((await Act(e, Service, new PcShipmentActionDto { Id = s.Id, Action = "commission" })).Success);
            await Settle(e);
            Closed(e, "SHIP.COMMISSION");
            Assert.AreEqual(0, Tasks(e).Count(t => t.State < AgentTaskState.Done && t.StepCode.StartsWith("SHIP.")), Dump(e));

            // the probes: both stations are complete
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, sp => sp.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            var cells = board.Data.Rows.Single(r => r.ContractId == contract).Cells;
            Assert.AreEqual(AgentCellStatus.Done, cells.Single(c => c.Station == 10).Status);
            Assert.AreEqual(AgentCellStatus.Done, cells.Single(c => c.Station == 11).Status);

            // no decision column holds a seat
            PcShipment last = Shipment(e, wo);
            Assert.AreEqual(PcShipmentStatus.HandoverReceived, last.Status);
            Assert.IsFalse(isSeat(last.PackedByUserId ?? 0) || isSeat(last.PackingCheckedByUserId ?? 0), "packed by and checked by are people");
            Assert.AreEqual(1301, last.PackedByUserId);
            Assert.AreEqual(1302, last.PackingCheckedByUserId);
            Assert.AreEqual(shipSeat, last.CreatorUserId, "the agent made the draft only");

            // the only human tasks are gates and physical work: every Input would need a comment here, and there is none
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station >= 10 && t.Station <= 11 && t.Kind == AgentStepKind.Input), Dump(e));
        }

        [TestMethod]
        public async Task A_Work_Order_Without_A_Released_Release_Note_Gets_No_Shipment_And_An_Excess_Of_Pieces_Becomes_One_Descriptive_Line()
        {
            Env e = await NewEnv("rules");
            // a draft release note does not start a shipment
            int woDraft = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-D", Title = "ساخت", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-5) });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-D", Title = "x", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "P-D", Title = "x", Unit = "عدد" });
                u.SaveChanges();
                var note = AppHost.Add(u, new ReleaseNote { Number = "RN-D", WorkOrderId = w.Id, ContractId = c.Id, Status = ReleaseNoteStatus.Draft, Lines = new List<ReleaseNoteLine>() });
                note.Lines.Add(new ReleaseNoteLine { RowNumber = 1, ItemId = item.Id, Quantity = 1 });
                u.SaveChanges();
                woDraft = w.Id;
            }, Seeder);
            await Settle(e);
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "SHIP.DRAFT"), Dump(e));
            Assert.AreEqual(0, e.Host.Read(u => u.Set<PcShipment>().Count(s => s.WorkOrderId == woDraft)));

            // a released note that lists two kinds of items for a work order of one unit: one descriptive line of the ordered quantity (the service never ships more than ordered)
            int woMany = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-M", Title = "ساخت", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-5) });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-M", Title = "x", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                InventoryItem a = AppHost.Add(u, new InventoryItem { Code = "P-A", Title = "a", Unit = "عدد" });
                InventoryItem b = AppHost.Add(u, new InventoryItem { Code = "P-B", Title = "b", Unit = "عدد" });
                u.SaveChanges();
                var note = AppHost.Add(u, new ReleaseNote { Number = "RN-M", WorkOrderId = w.Id, ContractId = c.Id, Status = ReleaseNoteStatus.Released, ReleasedAt = DateTimeOffset.Now, ReleasedByUserId = 1001, Lines = new List<ReleaseNoteLine>() });
                note.Lines.Add(new ReleaseNoteLine { RowNumber = 1, ItemId = a.Id, Quantity = 1 });
                note.Lines.Add(new ReleaseNoteLine { RowNumber = 2, ItemId = b.Id, Quantity = 1 });
                u.SaveChanges();
                woMany = w.Id;
            }, Seeder);
            await Settle(e);
            PcShipment s = Shipment(e, woMany);
            Assert.AreEqual(1, s.Lines.Count);
            Assert.IsNull(s.Lines.Single().ItemId);
            Assert.AreEqual(1m, s.Lines.Single().Quantity);
            StringAssert.Contains(s.Lines.Single().Description, "RN-M");
            StringAssert.Contains(s.PackingList, "RN-M");
            Assert.AreEqual(1, Open(e, "SHIP.PACK").Count(t => t.TargetId == s.Id));
        }

        [TestMethod]
        public async Task The_Gate_Roles_Of_The_Shipping_Steps_Are_The_Role_Checks_Of_The_Shipment_Actions()
        {
            Env e = await NewEnv("roles");
            int wo = SeedReleased(e, out int contract, out _);
            await Settle(e);
            PcShipment s = Shipment(e, wo);
            // each action answers "not allowed" to a user outside the roles of its step and does not answer it to every role of the step
            Func<string, string, Task<bool>> allowed = async (role, action) =>
            {
                ResponseDto<PcShipmentDto> r = await Act(e, AppHost.Principal(1399, "role.probe", role), new PcShipmentActionDto { Id = s.Id, Action = action });
                return r.Message == null || !r.Message.Contains("مجوز");
            };
            foreach (string role in new[] { "PLANNING", "PMO", "WAREHOUSE", "WHSUPERVISOR" }) Assert.IsTrue(await allowed(role, "pack"), "pack " + role);
            Assert.IsFalse(await allowed("QC", "pack"));
            foreach (string role in new[] { "QC", "QA" }) Assert.IsTrue(await allowed(role, "check-packing"), "check " + role);
            Assert.IsFalse(await allowed("WAREHOUSE", "check-packing"));
            foreach (string role in new[] { "PLANNING", "PMO" }) Assert.IsTrue(await allowed(role, "release"), "release " + role);
            Assert.IsFalse(await allowed("QC", "release"));
            foreach (string role in new[] { "PLANNING", "PMO", "COMMERCIAL", "WAREHOUSE", "WHSUPERVISOR" }) Assert.IsTrue(await allowed(role, "deliver"), "deliver " + role);
            Assert.IsFalse(await allowed("QC", "deliver"));
            foreach (string role in new[] { "PLANNING", "PMO", "COMMERCIAL" }) Assert.IsTrue(await allowed(role, "handover"), "handover " + role);
            Assert.IsFalse(await allowed("WAREHOUSE", "handover"));
            foreach (string role in new[] { "PLANNING", "PMO", "COMMERCIAL", "SERVICE" }) Assert.IsTrue(await allowed(role, "commission"), "commission " + role);
            Assert.IsFalse(await allowed("WAREHOUSE", "commission"));
            Assert.AreEqual(contract, s.ContractId);
        }
    }
}
