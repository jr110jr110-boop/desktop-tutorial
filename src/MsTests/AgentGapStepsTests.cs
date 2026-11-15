using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Procurement;

namespace MsTests
{
    /// <summary>
    /// The three gaps of the agent catalogue: a returned inquiry goes back to its buyer (PROC.INQUIRY.FIX), the states of a service request that no step moved (SV.REQUEST.ASSIGN, RESOLVE, CLOSE), and the draft issue of an
    /// approved material request that has plain stock (WH.ISSUE.DRAFT, which stops at the draft: the storekeeper confirms).
    /// </summary>
    [TestClass]
    public class AgentGapStepsTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentGap_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e, params string[] agents)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); foreach (string a in agents) await Run(e, a); }
            await Run(e, "AG-MASTER");
        }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        // ------------------------------------------------------------------------------------------------ B1: the returned inquiry

        [TestMethod]
        public async Task A_Returned_Inquiry_Waits_For_Its_Buyer_Until_A_New_Revision_Or_A_Cancel()
        {
            Env e = await NewEnv("inquiry");
            int a = 0, b = 0, c = 0, live = 0;
            e.Host.Write(u =>
            {
                var request = AppHost.Add(u, new PurchaseRequest { Number = "PR-RET", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Approved, RequestDate = DateTime.Today });
                u.SaveChanges();
                PurchaseInquiry Make(string number, InquiryStatus status, string reason = null) =>
                    AppHost.Add(u, new PurchaseInquiry { Number = number, PurchaseRequestId = request.Id, Status = status, InquiryDate = DateTime.Today, ReturnReason = reason, RequiredQuotes = 1 });
                var ret = Make("INQ-A", InquiryStatus.Returned, "برآورد با بودجه نمی‌خواند");
                var returnedAndRevised = Make("INQ-B", InquiryStatus.Returned, "ناقص");
                var returnedAndCancelled = Make("INQ-C", InquiryStatus.Returned, "ناقص");
                var open = Make("INQ-D", InquiryStatus.Open);
                u.SaveChanges();
                returnedAndRevised.SupersededByInquiryId = open.Id;     // the buyer made revision 2: this one is finished
                u.SaveChanges();
                a = ret.Id; b = returnedAndRevised.Id; c = returnedAndCancelled.Id; live = open.Id;
            }, Seeder);

            await Settle(e, "AG-PROC");
            List<AgentTask> fix = Open(e, "PROC.INQUIRY.FIX");
            CollectionAssert.AreEquivalent(new[] { a, c }, fix.Select(t => t.TargetId).ToArray(), "the two returned inquiries that nobody revised: " + Dump(e));
            AgentTask task = fix.Single(t => t.TargetId == a);
            Assert.AreEqual(a, task.TargetId);
            Assert.AreEqual(AgentStepKind.Input, task.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, task.State);
            Assert.AreEqual(6, task.Station);
            Assert.AreEqual("AG-PROC", task.AgentCode);
            Assert.AreEqual(ProcurementRoles.Commercial, task.GateRoles, "the maker role of the inquiry");
            Assert.AreEqual("/procurement/inquiries/" + a, task.Link);
            StringAssert.Contains(task.Summary, "برآورد با بودجه نمی‌خواند");

            // a returned inquiry that was already revised, an open one: no task
            Assert.AreEqual(0, fix.Count(t => t.TargetId == b || t.TargetId == live));

            // the buyer cancels one and revises the other: both tasks close
            e.Host.Write(u => { u.Set<PurchaseInquiry>().Single(i => i.Id == c).Status = InquiryStatus.Cancelled; u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-PROC");
            CollectionAssert.AreEqual(new[] { a }, Open(e, "PROC.INQUIRY.FIX").Select(t => t.TargetId).ToArray(), "the cancelled one closed: " + Dump(e));
            e.Host.Write(u => { u.Set<PurchaseInquiry>().Single(i => i.Id == a).SupersededByInquiryId = live; u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-PROC");
            Assert.AreEqual(0, Open(e, "PROC.INQUIRY.FIX").Count, Dump(e));
            Assert.AreEqual(2, Tasks(e).Count(t => t.StepCode == "PROC.INQUIRY.FIX" && t.State == AgentTaskState.Done), "the tasks closed (not failed): " + Dump(e));
        }

        // ------------------------------------------------------------------------------------------------ B3: the service request

        [TestMethod]
        public async Task A_Service_Request_Is_Assigned_Resolved_And_Closed_By_People_The_Steps_Only_Put_It_In_Front_Of_Them()
        {
            Env e = await NewEnv("service");
            int fresh = 0, assigned = 0, work = 0, resolved = 0, openOrder = 0;
            e.Host.Write(u =>
            {
                SvServiceRequest Req(string n, SvRequestStatus status, int? user = null) =>
                    AppHost.Add(u, new SvServiceRequest { Number = n, CustomerId = 1, ReceivedAt = DateTime.Today, Description = "x", Type = SvRequestType.Breakdown, Priority = SvPriority.Normal, Status = status, AssignedToUserId = user, CreateDate = DateTimeOffset.Now });
                SvServiceRequest r1 = Req("SVR-1", SvRequestStatus.Open);
                SvServiceRequest r2 = Req("SVR-2", SvRequestStatus.Assigned, 1901);
                SvServiceRequest r3 = Req("SVR-3", SvRequestStatus.InProgress, 1901);
                SvServiceRequest r4 = Req("SVR-4", SvRequestStatus.Resolved, 1901);
                SvServiceRequest r5 = Req("SVR-5", SvRequestStatus.InProgress, 1901);
                u.SaveChanges();
                SvServiceOrder Order(string n, SvRequestStatus _, int requestId, SvOrderStatus status) =>
                    AppHost.Add(u, new SvServiceOrder { Number = n, RequestId = requestId, UnitId = 1, CustomerId = 1, Title = "t", Status = status, CreateDate = DateTimeOffset.Now });
                Order("SVO-1", r3.Status, r3.Id, SvOrderStatus.Closed);
                Order("SVO-2", r3.Status, r3.Id, SvOrderStatus.Cancelled);
                Order("SVO-3", r5.Status, r5.Id, SvOrderStatus.Closed);
                Order("SVO-4", r5.Status, r5.Id, SvOrderStatus.InProgress);   // still open: the request cannot be resolved yet
                u.SaveChanges();
                fresh = r1.Id; assigned = r2.Id; work = r3.Id; resolved = r4.Id; openOrder = r5.Id;
            }, Seeder);

            await Settle(e, "AG-SERVICE");
            List<AgentTask> assign = Open(e, "SV.REQUEST.ASSIGN"), resolve = Open(e, "SV.REQUEST.RESOLVE"), close = Open(e, "SV.REQUEST.CLOSE");
            CollectionAssert.AreEqual(new[] { fresh }, assign.Select(t => t.TargetId).ToArray(), Dump(e));
            CollectionAssert.AreEqual(new[] { work }, resolve.Select(t => t.TargetId).ToArray(), "closed or cancelled orders only, with at least one closed: " + Dump(e));
            CollectionAssert.AreEqual(new[] { resolved }, close.Select(t => t.TargetId).ToArray(), Dump(e));
            Assert.AreEqual(AgentStepKind.Input, assign[0].Kind);
            Assert.AreEqual(AgentStepKind.Input, resolve[0].Kind);
            Assert.AreEqual(AgentStepKind.HumanGate, close[0].Kind);
            Assert.IsTrue(new[] { assign[0], resolve[0], close[0] }.All(t => t.Station == 12 && t.AgentCode == "AG-SERVICE"));
            Assert.AreEqual("SERVICE", assign[0].GateRoles, "nobody owns an unassigned request: the roles");
            Assert.IsTrue(new[] { resolve[0], close[0] }.All(t => t.GateUserId == 1901 && t.GateRoles == null), "an assigned request goes to its assignee");
            Assert.AreEqual("/service/requests/" + work, resolve[0].Link);
            CollectionAssert.DoesNotContain(resolve.Select(t => t.TargetId).ToList(), openOrder);
            CollectionAssert.DoesNotContain(assign.Select(t => t.TargetId).ToList(), assigned);

            // the people act (the request rules are unchanged): the tasks close
            e.Host.Write(u =>
            {
                u.Set<SvServiceRequest>().Single(r => r.Id == fresh).Status = SvRequestStatus.Assigned;
                u.Set<SvServiceRequest>().Single(r => r.Id == fresh).AssignedToUserId = 1901;
                u.Set<SvServiceRequest>().Single(r => r.Id == work).Status = SvRequestStatus.Resolved;
                u.Set<SvServiceRequest>().Single(r => r.Id == resolved).Status = SvRequestStatus.Closed;
                u.SaveChanges();
            }, Seeder);
            await Settle(e, "AG-SERVICE");
            Assert.AreEqual(0, Open(e, "SV.REQUEST.ASSIGN").Count, Dump(e));
            Assert.AreEqual(0, Open(e, "SV.REQUEST.RESOLVE").Count, Dump(e));
            CollectionAssert.AreEqual(new[] { work }, Open(e, "SV.REQUEST.CLOSE").Select(t => t.TargetId).ToArray(), "the request that was just resolved now waits to be closed");
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));
        }

        // ------------------------------------------------------------------------------------------------ B2: the issue draft

        private sealed class Stock { public int Request, Warehouse, Item; }

        private static Stock SeedIssue(Env e, decimal onHand, MaterialRequestStatus status = MaterialRequestStatus.Approved, ItemTrackingMode tracking = ItemTrackingMode.None, decimal requested = 5m)
        {
            var s = new Stock();
            e.Host.Write(u =>
            {
                Warehouse w = AppHost.Add(u, new Warehouse { Code = "W-" + Guid.NewGuid().ToString("N").Substring(0, 4), Title = "main" });
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "I-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "bearing", Unit = "عدد", TrackingMode = tracking });
                u.SaveChanges();
                if (onHand > 0) { u.Set<StockBalance>().Add(new StockBalance { ItemId = item.Id, WarehouseId = w.Id, Quantity = onHand }); u.SaveChanges(); }
                MaterialRequest mr = AppHost.Add(u, new MaterialRequest { Number = "MR-" + Guid.NewGuid().ToString("N").Substring(0, 5), RequestDate = DateTime.Today, Status = status });
                u.SaveChanges();
                u.Set<MaterialRequestLine>().Add(new MaterialRequestLine { MaterialRequestId = mr.Id, RowNumber = 1, ItemId = item.Id, Quantity = requested });
                u.SaveChanges();
                s.Request = mr.Id; s.Warehouse = w.Id; s.Item = item.Id;
            }, Seeder);
            return s;
        }

        private static List<InventoryDocument> Documents(Env e) => e.Host.Read(u => u.Set<InventoryDocument>().AsNoTracking().Include(d => d.Lines).OrderBy(d => d.Id).ToList());

        [TestMethod]
        public async Task The_Warehouse_Agent_Drafts_The_Issue_Of_An_Approved_Request_That_Has_Plain_Stock_And_Leaves_The_Confirmation_To_The_Storekeeper()
        {
            Env e = await NewEnv("issue");
            Stock s = SeedIssue(e, onHand: 8m);
            await Settle(e, "AG-WH");

            List<InventoryDocument> docs = Documents(e);
            Assert.AreEqual(1, docs.Count, Dump(e));
            InventoryDocument d = docs[0];
            Assert.AreEqual(InventoryDocumentType.Issue, d.Type);
            Assert.AreEqual(InventoryDocumentStatus.Draft, d.Status, "the agent never confirms");
            Assert.AreEqual(s.Request, d.MaterialRequestId);
            Assert.AreEqual(s.Warehouse, d.WarehouseId);
            Assert.AreEqual(1, d.Lines.Count);
            Assert.AreEqual(5m, d.Lines.Single().Quantity);
            Assert.AreEqual(e.Seats["AG-WH"].UserId, d.CreatorUserId);
            Assert.AreEqual(8m, e.Host.Read(u => u.Set<StockBalance>().Single().Quantity), "a draft moves no stock");
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "WH.ISSUE.DRAFT" && t.State == AgentTaskState.Done), Dump(e));

            // the physical step now belongs to the draft: the storekeeper counts and confirms it; the request is no longer waiting for a draft
            AgentTask confirm = Open(e, "WH.DOC.CONFIRM").Single();
            Assert.AreEqual(d.Id, confirm.TargetId);
            Assert.AreEqual(AgentStepKind.Physical, confirm.Kind);
            Assert.AreEqual(0, Open(e, "WH.MR.ISSUE").Count, "an approved request with a draft is not offered for a new issue");
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));

            // idempotent: more runs make no second draft
            await Settle(e, "AG-WH");
            Assert.AreEqual(1, Documents(e).Count);
        }

        [TestMethod]
        public async Task The_Issue_Draft_Is_Not_Made_Without_Enough_Plain_Stock_Or_For_A_Tracked_Item_Or_An_Unapproved_Request()
        {
            Env e = await NewEnv("issue-no");
            SeedIssue(e, onHand: 3m);                                                       // not enough (5 requested)
            SeedIssue(e, onHand: 9m, tracking: ItemTrackingMode.Lot);                       // a lot must be chosen by the storekeeper
            SeedIssue(e, onHand: 9m, status: MaterialRequestStatus.Submitted);              // not approved
            SeedIssue(e, onHand: 0m);                                                       // no stock row at all
            await Settle(e, "AG-WH");
            Assert.AreEqual(0, Documents(e).Count, Dump(e));
            Assert.AreEqual(0, Open(e, "WH.ISSUE.DRAFT").Count);
            Assert.AreEqual(3, Open(e, "WH.MR.ISSUE").Count, "the approved ones that the agent cannot draft stay with the storekeeper: " + Dump(e));
        }

        // ------------------------------------------------------------------------------------------------ review: the issue draft never ends in a refusal

        private static int AddWarehouse(Env e, WarehouseKind kind, int itemId, decimal onHand, int? workOrderId = null)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                Warehouse w = AppHost.Add(u, new Warehouse { Code = "W-" + Guid.NewGuid().ToString("N").Substring(0, 4), Title = kind.ToString(), Kind = kind, WorkOrderId = workOrderId });
                u.SaveChanges();
                u.Set<StockBalance>().Add(new StockBalance { ItemId = itemId, WarehouseId = w.Id, Quantity = onHand });
                u.SaveChanges();
                id = w.Id;
            }, Seeder);
            return id;
        }

        [TestMethod]
        public async Task The_Issue_Draft_Uses_Only_Usable_Warehouse_Kinds_And_Never_Ends_In_A_Failed_Task()
        {
            Env e = await NewEnv("issue-kinds");
            Stock s = SeedIssue(e, onHand: 0m);
            // every unusable kind holds the stock first (lower ids); the general warehouse is last
            foreach (WarehouseKind kind in new[] { WarehouseKind.Quarantine, WarehouseKind.Scrap, WarehouseKind.WorkInProgress, WarehouseKind.Subcontractor, WarehouseKind.CustomerProperty })
                AddWarehouse(e, kind, s.Item, 50m);
            await Settle(e, "AG-WH");
            Assert.AreEqual(0, Documents(e).Count, "no usable warehouse: no draft: " + Dump(e));
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed || !string.IsNullOrEmpty(t.LastError) && t.StepCode == "WH.ISSUE.DRAFT"), Dump(e));
            Assert.AreEqual(1, Open(e, "WH.MR.ISSUE").Count, "it stays with the storekeeper");

            int general = AddWarehouse(e, WarehouseKind.General, s.Item, 50m);
            await Settle(e, "AG-WH");
            Assert.AreEqual(general, Documents(e).Single().WarehouseId, "the first usable warehouse");
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));
        }

        [TestMethod]
        public async Task The_Issue_Draft_Uses_The_Warehouse_Named_On_The_Request_And_Skips_One_That_Is_Not_Usable_Or_Belongs_To_Another_Project()
        {
            Env e = await NewEnv("issue-named");
            Stock s = SeedIssue(e, onHand: 20m);                      // the first warehouse
            int second = AddWarehouse(e, WarehouseKind.Parts, s.Item, 20m);
            e.Host.Write(u => { u.Set<MaterialRequest>().Single(m => m.Id == s.Request).WarehouseId = second; u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-WH");
            Assert.AreEqual(second, Documents(e).Single().WarehouseId, "the warehouse of the request, not the lowest id");

            // a request that names a quarantine warehouse is not drafted (and is not a failure)
            Stock q = SeedIssue(e, onHand: 20m);
            int quarantine = AddWarehouse(e, WarehouseKind.Quarantine, q.Item, 20m);
            e.Host.Write(u => { u.Set<MaterialRequest>().Single(m => m.Id == q.Request).WarehouseId = quarantine; u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-WH");
            Assert.AreEqual(1, Documents(e).Count, "still one: " + Dump(e));
            Assert.IsTrue(Open(e, "WH.MR.ISSUE").Any(t => t.TargetId == q.Request));

            // a project warehouse of another work order is skipped (the project rule of the draft)
            Stock p = SeedIssue(e, onHand: 0m);
            int order = 0;
            e.Host.Write(u => { var w = AppHost.Add(u, new WorkOrder { Title = "wo" }); u.SaveChanges(); order = w.Id; u.Set<MaterialRequest>().Single(m => m.Id == p.Request).WorkOrderId = order; u.SaveChanges(); }, Seeder);
            AddWarehouse(e, WarehouseKind.General, p.Item, 20m, workOrderId: order + 1000);
            await Settle(e, "AG-WH");
            Assert.AreEqual(1, Documents(e).Count, "the request of another project is not drafted from it: " + Dump(e));
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));
        }

        [TestMethod]
        public async Task A_Deleted_Agent_Draft_Is_Not_Made_Again_And_The_Request_Goes_To_The_Storekeeper()
        {
            Env e = await NewEnv("issue-deleted");
            Stock s = SeedIssue(e, onHand: 8m);
            await Settle(e, "AG-WH");
            InventoryDocument d = Documents(e).Single();
            e.Host.Write(u => { u.Entry(u.Set<InventoryDocument>().Single(x => x.Id == d.Id)).Property(nameof(InventoryDocument.Deleted)).CurrentValue = true; u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-WH");
            Assert.AreEqual(1, Documents(e).Count, "no second draft after the first was deleted: " + Dump(e));
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "WH.ISSUE.DRAFT" && t.State == AgentTaskState.Done), Dump(e));
            Assert.IsTrue(Open(e, "WH.MR.ISSUE").Any(t => t.TargetId == s.Request), "the storekeeper decides now: " + Dump(e));
        }

        [TestMethod]
        public async Task An_Open_Nonconformity_Report_Or_Another_Open_Draft_Keeps_The_Agent_From_Drafting_And_A_Request_Has_Only_One_Task()
        {
            Env e = await NewEnv("issue-hold");
            Stock held = SeedIssue(e, onHand: 20m);
            e.Host.Write(u => { u.Set<NonConformance>().Add(new NonConformance { Number = "NCR-1", ItemId = held.Item, Status = NcrStatus.Open, ReportDate = DateTime.Today, Quantity = 1m }); u.SaveChanges(); }, Seeder);
            await Settle(e, "AG-WH");
            Assert.AreEqual(0, Documents(e).Count, "the item is held: " + Dump(e));
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));

            // two requests for the same stock: the second sees what the first draft takes
            Env f = await NewEnv("issue-two");
            Stock one = SeedIssue(f, onHand: 8m);
            int itemId = one.Item;
            int second = 0;
            f.Host.Write(u =>
            {
                MaterialRequest mr = AppHost.Add(u, new MaterialRequest { Number = "MR-SECOND", RequestDate = DateTime.Today, Status = MaterialRequestStatus.Approved });
                u.SaveChanges();
                u.Set<MaterialRequestLine>().Add(new MaterialRequestLine { MaterialRequestId = mr.Id, RowNumber = 1, ItemId = itemId, Quantity = 5m });
                u.SaveChanges();
                second = mr.Id;
            }, Seeder);
            // after one reconcile pass: one task for the draft of each request that has free stock, and no storekeeper task for those
            await Run(f, "AG-MASTER");
            Assert.AreEqual(1, Open(f, "WH.ISSUE.DRAFT").Count, "only one of the two can be drafted from 8 pieces: " + Dump(f));
            Assert.IsFalse(Open(f, "WH.MR.ISSUE").Any(t => t.TargetId == Open(f, "WH.ISSUE.DRAFT").Single().TargetId), "no duplicate task for the request that the agent drafts: " + Dump(f));
            await Settle(f, "AG-WH");
            Assert.AreEqual(1, Documents(f).Count, Dump(f));
            Assert.IsTrue(Open(f, "WH.MR.ISSUE").Any(t => t.TargetId == (Documents(f).Single().MaterialRequestId == one.Request ? second : one.Request)), "the other request waits for the storekeeper: " + Dump(f));
            AgentTask done = Tasks(f).Single(t => t.StepCode == "WH.ISSUE.DRAFT" && t.State == AgentTaskState.Done);
            Assert.IsFalse((done.Summary ?? string.Empty).Contains("null"), done.Summary);
            Assert.IsFalse((done.Summary ?? string.Empty).Contains("()"), done.Summary);
        }

        // ------------------------------------------------------------------------------------------------ review: S8

        [TestMethod]
        public async Task A_Returned_Inquiry_Of_A_Closed_Purchase_Request_Has_No_Task_And_A_Service_Task_Goes_To_The_Assignee_Spare_Parts_Included()
        {
            Env e = await NewEnv("s8");
            int open = 0, closed = 0;
            e.Host.Write(u =>
            {
                var r1 = AppHost.Add(u, new PurchaseRequest { Number = "PR-OPEN", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Approved, RequestDate = DateTime.Today });
                var r2 = AppHost.Add(u, new PurchaseRequest { Number = "PR-CLOSED", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Closed, RequestDate = DateTime.Today });
                u.SaveChanges();
                var i1 = AppHost.Add(u, new PurchaseInquiry { Number = "INQ-1", PurchaseRequestId = r1.Id, Status = InquiryStatus.Returned, InquiryDate = DateTime.Today, RequiredQuotes = 1 });
                var i2 = AppHost.Add(u, new PurchaseInquiry { Number = "INQ-2", PurchaseRequestId = r2.Id, Status = InquiryStatus.Returned, InquiryDate = DateTime.Today, RequiredQuotes = 1 });
                u.SaveChanges();
                open = i1.Id; closed = i2.Id;
            }, Seeder);
            await Settle(e, "AG-PROC");
            CollectionAssert.AreEqual(new[] { open }, Open(e, "PROC.INQUIRY.FIX").Select(t => t.TargetId).ToArray(), Dump(e));

            int spare = 0, resolved = 0;
            e.Host.Write(u =>
            {
                SvServiceRequest Req(string n, SvRequestType type, SvRequestStatus status) =>
                    AppHost.Add(u, new SvServiceRequest { Number = n, CustomerId = 1, ReceivedAt = DateTime.Today, Description = "x", Type = type, Priority = SvPriority.Normal, Status = status, AssignedToUserId = 1901, CreateDate = DateTimeOffset.Now });
                SvServiceRequest a = Req("SVR-SP", SvRequestType.SpareParts, SvRequestStatus.InProgress);
                SvServiceRequest b = Req("SVR-RS", SvRequestType.Breakdown, SvRequestStatus.Resolved);
                u.SaveChanges();
                spare = a.Id; resolved = b.Id;
            }, Seeder);
            await Settle(e, "AG-SERVICE");
            AgentTask resolve = Open(e, "SV.REQUEST.RESOLVE").Single(t => t.TargetId == spare);
            Assert.AreEqual(1901, resolve.GateUserId, "the assignee");
            Assert.IsNull(resolve.GateRoles);
            AgentTask close = Open(e, "SV.REQUEST.CLOSE").Single(t => t.TargetId == resolved);
            Assert.AreEqual(1901, close.GateUserId);
        }
    }
}
