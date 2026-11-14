using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, the owner's mandate "the AI never approves": the data layer refuses a seat that puts its own user id into a column that records a decision of a person, whatever role the seat holds and whatever
    /// the service forgot to check. A human with the same roles is not touched, nor a background job without a seat principal. The existing seats keep working (their own test classes are the proof).
    /// </summary>
    [TestClass]
    public class SeatApprovalGuardTests
    {
        public TestContext TestContext { get; set; }

        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Requester = AppHost.Principal(5, "requester", "WAREHOUSE");

        // an agent-like seat with the roles that approve; the guard must refuse it anyway
        private static readonly ClaimsPrincipal SeatPlanner = AppHost.Principal(99, "seat.guard-test", "PLANNING", "PMO", "CEO", "COMMERCIAL", "FINANCIAL");
        private static readonly ClaimsPrincipal HumanPlanner = AppHost.Principal(51, "planner", "PLANNING");
        private static readonly ClaimsPrincipal HumanPmo = AppHost.Principal(12, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Master = AppHost.Principal(98, "seat.ag-master");

        private static AppHost NewHost(string name) => new AppHost("SeatGuard_" + name + "_" + Guid.NewGuid().ToString("N"));

        private sealed class Outcome
        {
            public bool Success;
            public string Message;
            public Exception Error;
            public bool Refused => !Success;
            public bool RefusedByGuard => Error != null && Error.Message.Contains(SeatApprovalGuard.Message);
        }

        /// <summary>A service answers success=false for a rule, and the data layer throws for the guard: both are a refusal; the test tells them apart.</summary>
        private static async Task<Outcome> Try<T>(Func<Task<Common.Results.ResponseDto<T>>> call)
        {
            try
            {
                var r = await call();
                return new Outcome { Success = r.Success, Message = r.Message };
            }
            catch (InvalidOperationException ex)
            {
                return new Outcome { Success = false, Message = ex.Message, Error = ex };
            }
        }

        // ------------------------------------------------------------------------------------------------ the constants and the sweep

        [TestMethod]
        public void The_Prefix_And_The_Master_Name_Equal_The_Names_Of_The_Seats()
        {
            Assert.AreEqual(AiSeatNames.UserPrefix, SeatApprovalGuard.SeatPrefix);
            Assert.AreEqual(AiSeatNames.UserName("AG-MASTER"), SeatApprovalGuard.MasterUserName);
            Assert.AreEqual(AiSeatNames.UserName("AUDIT"), AuditorWriteGuard.SeatUserName);
            Assert.IsTrue(SeatApprovalGuard.IsSeatName("seat.wh-reorder"));
            Assert.IsTrue(SeatApprovalGuard.IsSeatName("  SEAT.Ceo-Brief "));
            Assert.IsFalse(SeatApprovalGuard.IsSeatName("seatbelt"));
            Assert.IsFalse(SeatApprovalGuard.IsSeatName("admin"));
            Assert.IsFalse(SeatApprovalGuard.IsSeatName(null));
            foreach (AiSeatDefinition d in AiSeatCatalog.All) Assert.IsTrue(SeatApprovalGuard.IsSeatName(d.UserName), d.Code);
        }

        [TestMethod]
        public void The_Name_Pattern_Matches_The_Decision_Columns_Only()
        {
            foreach (string name in new[] { "ApprovedByUserId", "PlanningApprovedByUserId", "TechnicalReviewedByUserId", "ClosedByUserId", "RejectedByUserId", "ReleasedByUserId", "PaidByUserId", "FinalizedByUserId", "QaUserId", "PmoUserId", "CeoUserId", "VoidQaUserId" })
                Assert.IsTrue(SeatApprovalGuard.MatchesName(name), name);
            foreach (string name in new[] { "CreatorUserId", "ModifierUserId", "PreparedByUserId", "SelectedByUserId", "OwnerUserId", "UserId", "RequestedByUserId", "ApprovedAt", "ApprovalNote" })
                Assert.IsFalse(SeatApprovalGuard.MatchesName(name), name);
        }

        [TestMethod]
        public void Model_Sweep_Lists_Every_Entity_And_Property_The_Guard_Watches()
        {
            AppHost host = NewHost("sweep");
            List<(string Entity, string Property)> swept = SeatApprovalGuard.Sweep(host.Model);
            TestContext.WriteLine("SeatApprovalGuard watches " + swept.Count + " columns:");
            foreach (var g in swept.GroupBy(x => x.Entity)) TestContext.WriteLine("  " + g.Key + ": " + string.Join(", ", g.Select(x => x.Property)));
            foreach (var x in swept) Console.WriteLine("GUARD " + x.Entity + "." + x.Property);

            Assert.IsTrue(swept.Contains(("PurchaseRequest", "PlanningApprovedByUserId")));
            Assert.IsTrue(swept.Contains(("PurchaseRequest", "ManagementApprovedByUserId")));
            Assert.IsTrue(swept.Contains(("PurchaseOrder", "ApprovedByUserId")));
            Assert.IsTrue(swept.Contains(("PurchaseInquiry", "CommercialApprovedByUserId")));
            Assert.IsTrue(swept.Contains(("SupplierQuote", "TechnicalReviewedByUserId")));
            Assert.IsTrue(swept.Contains(("PaymentRequest", "ApprovedByUserId")));
            Assert.IsTrue(swept.Contains(("PaymentRequest", "PaidByUserId")));
            // only int and int? columns (a decision is a user id); the decision time and note columns are not watched
            Assert.IsFalse(swept.Any(x => x.Property.EndsWith("At", StringComparison.Ordinal) || x.Property.EndsWith("Note", StringComparison.Ordinal)));
            // the columns of the agent layer itself: a person returns a task (never a seat, so the guard watches it)
            Assert.IsTrue(swept.Contains(("AgentTask", "ReturnedByUserId")));
            Assert.AreEqual(swept.Count, swept.Distinct().Count());
            // the one reviewed exception: a document check-out matches the name pattern but is not a decision
            Assert.IsTrue(SeatApprovalGuard.MatchesName("CheckedOutByUserId"));
            Assert.IsFalse(swept.Contains(("PcEngineeringDeliverable", "CheckedOutByUserId")), "check-out is excepted");
            Assert.IsTrue(swept.Contains(("PcEngineeringDeliverable", "ApprovedByUserId")), "the approval of the same entity is still watched");
            
        }

        [TestMethod]
        public void A_Seat_May_Check_Out_A_Deliverable_But_Not_Approve_It()
        {
            AppHost host = NewHost("checkout");
            int id = 0;
            host.Write(u =>
            {
                var d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-1", Title = "x", CheckedOutByUserId = 99 });
                u.SaveChanges();
                id = d.Id;
            }, SeatPlanner);
            Assert.AreEqual(99, host.Read(u => u.Set<PcEngineeringDeliverable>().Single(x => x.Id == id).CheckedOutByUserId));
            var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { u.Set<PcEngineeringDeliverable>().Single(x => x.Id == id).ApprovedByUserId = 99; u.SaveChanges(); }, SeatPlanner));
            StringAssert.Contains(ex.Message, "ApprovedByUserId");
        }

        // ------------------------------------------------------------------------------------------------ a seat cannot decide, a human can

        private static int SeedSubmittedRequest(AppHost host, decimal estimate = 1_000_000m)
        {
            int id = 0;
            host.Write(u =>
            {
                var request = AppHost.Add(u, new PurchaseRequest
                {
                    Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Submitted,
                    RequestDate = DateTime.Today, EstimatedAmount = estimate, SubmittedAt = DateTimeOffset.Now, CreatorUserId = 5,
                });
                u.SaveChanges();
                id = request.Id;
            }, Requester);
            return id;
        }

        [TestMethod]
        public async Task A_Seat_With_The_Planning_Role_Cannot_Approve_A_Request_And_A_Human_Can()
        {
            AppHost host = NewHost("request");
            int id = SeedSubmittedRequest(host);

            Outcome seat = await Try(() => host.As(SeatPlanner, p => p.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = id, Action = "approve-planning" }, CancellationToken.None)));
            Assert.IsTrue(seat.Refused, "the seat approved");
            Assert.IsTrue(seat.RefusedByGuard, "it is the data layer that refused: " + seat.Message);
            PurchaseRequest after = host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id));
            Assert.IsNull(after.PlanningApprovedByUserId);
            Assert.IsNull(after.PlanningApprovedAt);
            Assert.AreEqual(PurchaseRequestStatus.Submitted, after.Status);

            Outcome human = await Try(() => host.As(HumanPlanner, p => p.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = id, Action = "approve-planning" }, CancellationToken.None)));
            Assert.IsTrue(human.Success, human.Message);
            Assert.AreEqual(51, host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).PlanningApprovedByUserId);
        }

        [TestMethod]
        public async Task A_Seat_With_The_Ceo_Role_Cannot_Approve_A_Request_For_Management()
        {
            AppHost host = NewHost("management");
            int id = SeedSubmittedRequest(host);
            Outcome seat = await Try(() => host.As(SeatPlanner, p => p.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = id, Action = "approve-management" }, CancellationToken.None)));
            Assert.IsTrue(seat.RefusedByGuard, seat.Message);
            Assert.IsNull(host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).ManagementApprovedByUserId);
        }

        private static int SeedDraftOrder(AppHost host)
        {
            int id = 0;
            host.Write(u =>
            {
                var provider = AppHost.Add(u, new Provider { Name = "supplier" });
                u.SaveChanges();
                var order = AppHost.Add(u, new PurchaseOrder
                {
                    Number = "PO-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = PurchaseOrderStatus.Draft, Kind = PurchaseKind.Service, OrderDate = DateTime.Today, ProviderId = provider.Id,
                    TotalAmount = 1_000_000m, CreatorUserId = 26, ModifierUserId = 26,
                });
                u.SaveChanges();
                id = order.Id;
            }, Seeder);
            return id;
        }

        [TestMethod]
        public async Task A_Seat_With_The_Pmo_Role_Cannot_Approve_An_Order_And_A_Human_Can()
        {
            AppHost host = NewHost("order");
            int id = SeedDraftOrder(host);
            Outcome seat = await Try(() => host.As(SeatPlanner, p => p.GetRequiredService<IProcurementService>().OrderActionAsync(new ProcurementActionDto { Id = id, Action = "approve" }, CancellationToken.None)));
            Assert.IsTrue(seat.Refused, "the seat approved the order");
            Assert.IsTrue(seat.RefusedByGuard, seat.Message);
            PurchaseOrder after = host.Read(u => u.Set<PurchaseOrder>().Single(x => x.Id == id));
            Assert.IsNull(after.ApprovedByUserId);
            Assert.AreEqual(PurchaseOrderStatus.Draft, after.Status);

            Outcome human = await Try(() => host.As(HumanPmo, p => p.GetRequiredService<IProcurementService>().OrderActionAsync(new ProcurementActionDto { Id = id, Action = "approve" }, CancellationToken.None)));
            Assert.IsTrue(human.Success, human.Message);
            Assert.AreEqual(12, host.Read(u => u.Set<PurchaseOrder>().Single(x => x.Id == id)).ApprovedByUserId);
        }

        [TestMethod]
        public async Task The_Seat_That_Selected_The_Winner_Cannot_Give_The_Commercial_Approval()
        {
            // the service lets the person who selected also give "approve-commercial" (ProcurementService.cs:735); the guard closes that hole for a seat
            AppHost host = NewHost("commercial");
            int id = 0;
            host.Write(u =>
            {
                var provider = AppHost.Add(u, new Provider { Name = "supplier" });
                var request = AppHost.Add(u, new PurchaseRequest { Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = PurchaseRequestStatus.Approved, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today, EstimatedAmount = 1_000_000m });
                u.SaveChanges();
                var inquiry = AppHost.Add(u, new PurchaseInquiry { Number = "IQ-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = InquiryStatus.Selected, InquiryDate = DateTime.Today, PurchaseRequestId = request.Id, SelectedByUserId = 99, SelectedAt = DateTimeOffset.Now });
                u.SaveChanges();
                var quote = AppHost.Add(u, new SupplierQuote { InquiryId = inquiry.Id, ProviderId = provider.Id, QuoteNumber = "Q1", QuoteDate = DateTime.Today, TechnicallyAccepted = true, TotalAmount = 1_000_000m });
                u.SaveChanges();
                inquiry.SelectedQuoteId = quote.Id;
                u.SaveChanges();
                id = inquiry.Id;
            }, Seeder);

            Outcome seat = await Try(() => host.As(SeatPlanner, p => p.GetRequiredService<IProcurementService>().InquiryActionAsync(new ProcurementActionDto { Id = id, Action = "approve-commercial" }, CancellationToken.None)));
            Assert.IsTrue(seat.RefusedByGuard, seat.Message);
            Assert.IsNull(host.Read(u => u.Set<PurchaseInquiry>().Single(x => x.Id == id)).CommercialApprovedByUserId);
        }

        [TestMethod]
        public async Task A_Seat_With_The_Pmo_Role_Cannot_Approve_A_Payment_Request()
        {
            AppHost host = NewHost("payment");
            int id = 0;
            host.Write(u =>
            {
                var provider = AppHost.Add(u, new Provider { Name = "supplier" });
                u.SaveChanges();
                var payment = AppHost.Add(u, new PaymentRequest { Number = "PAY-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = PaymentRequestStatus.Draft, RequestDate = DateTime.Today, ProviderId = provider.Id, Amount = 1_000_000m, CreatorUserId = 28 });
                u.SaveChanges();
                id = payment.Id;
            }, Seeder);

            Outcome seat = await Try(() => host.As(SeatPlanner, p => p.GetRequiredService<IPayablesService>().PaymentActionAsync(new ProcurementActionDto { Id = id, Action = "approve" }, CancellationToken.None)));
            Assert.IsTrue(seat.RefusedByGuard, seat.Message);
            Assert.IsNull(host.Read(u => u.Set<PaymentRequest>().Single(x => x.Id == id)).ApprovedByUserId);

            Outcome human = await Try(() => host.As(HumanPmo, p => p.GetRequiredService<IPayablesService>().PaymentActionAsync(new ProcurementActionDto { Id = id, Action = "approve" }, CancellationToken.None)));
            Assert.IsTrue(human.Success, human.Message);
        }

        // ------------------------------------------------------------------------------------------------ what the guard does not touch

        [TestMethod]
        public void A_Background_Job_Without_A_Principal_And_A_Human_Write_Any_Column()
        {
            AppHost host = NewHost("untouched");
            int id = SeedSubmittedRequest(host);
            // no principal at all (a background job): the id 99 is just a number
            host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == id).PlanningApprovedByUserId = 99; u.SaveChanges(); }, null);
            Assert.AreEqual(99, host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).PlanningApprovedByUserId);
            // a human whose id is the same number
            host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == id).ManagementApprovedByUserId = 77; u.SaveChanges(); }, AppHost.Principal(77, "human.number.seventy-seven", "CEO"));
            Assert.AreEqual(77, host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).ManagementApprovedByUserId);
        }

        [TestMethod]
        public void A_Seat_May_Write_Its_Own_Drafts_And_Save_A_Row_That_Already_Carries_Its_Id()
        {
            AppHost host = NewHost("drafts");
            int id = 0;
            // a request made by the seat (CreatorUserId is the seat's id: not a decision column)
            host.Write(u =>
            {
                var request = AppHost.Add(u, new PurchaseRequest { Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = PurchaseRequestStatus.Draft, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today, CreatorUserId = 99 });
                u.SaveChanges();
                id = request.Id;
            }, SeatPlanner);
            // a row that already carries the seat's id in a decision column (written by someone else earlier) is saved again for another change: the value did not change, so it is allowed
            host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == id).PlanningApprovedByUserId = 99; u.SaveChanges(); }, null);
            host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == id).Description = "edited by the seat"; u.SaveChanges(); }, SeatPlanner);
            Assert.AreEqual("edited by the seat", host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).Description);
            // but setting it again to the seat's id on another row is refused
            int other = 0;
            host.Write(u =>
            {
                var r = AppHost.Add(u, new PurchaseRequest { Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = PurchaseRequestStatus.Draft, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today });
                u.SaveChanges();
                other = r.Id;
            }, Seeder);
            var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == other).PlanningApprovedByUserId = 99; u.SaveChanges(); }, SeatPlanner));
            StringAssert.Contains(ex.Message, SeatApprovalGuard.Message);
            StringAssert.Contains(ex.Message, "PurchaseRequest.PlanningApprovedByUserId");
        }

        [TestMethod]
        public void A_Seat_Can_Put_Another_Persons_Id_Where_It_Records_Who_Decided_Only_As_A_Copy()
        {
            // the guard is about the seat's OWN id: a seat that copies the id of the person who decided (a revision keeps the approver of the old revision) is not deciding
            AppHost host = NewHost("copy");
            int id = SeedSubmittedRequest(host);
            host.Write(u => { u.Set<PurchaseRequest>().Single(x => x.Id == id).PlanningApprovedByUserId = 51; u.SaveChanges(); }, SeatPlanner);
            Assert.AreEqual(51, host.Read(u => u.Set<PurchaseRequest>().Single(x => x.Id == id)).PlanningApprovedByUserId);
        }

        // ------------------------------------------------------------------------------------------------ the coordinator

        [TestMethod]
        public void The_Master_Seat_Writes_Only_The_Tables_Of_The_Agent_Layer_The_Ai_Module_And_The_Inbox()
        {
            AppHost host = NewHost("master");
            host.Write(u =>
            {
                AppHost.Add(u, new AgentTask { StepCode = "X.Y", Station = 6, AgentCode = "AG-PROC", Kind = AgentStepKind.Agent, TargetType = "T", TargetId = 1, Title = "x" });
                u.SaveChanges();
            }, Master);
            Assert.AreEqual(1, host.Read(u => u.Set<AgentTask>().Count()));

            var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u =>
            {
                AppHost.Add(u, new InventoryItem { Code = "I-1", Title = "plate", Unit = "عدد" });
                u.SaveChanges();
            }, Master));
            StringAssert.Contains(ex.Message, SeatApprovalGuard.MasterMessage);
            Assert.AreEqual(0, host.Read(u => u.Set<InventoryItem>().Count()));

            // another seat is not limited to those tables (it is limited by its roles and by the decision columns only)
            host.Write(u => { AppHost.Add(u, new InventoryItem { Code = "I-2", Title = "plate", Unit = "عدد" }); u.SaveChanges(); }, SeatPlanner);
            Assert.AreEqual(1, host.Read(u => u.Set<InventoryItem>().Count()));
        }
    }
}
