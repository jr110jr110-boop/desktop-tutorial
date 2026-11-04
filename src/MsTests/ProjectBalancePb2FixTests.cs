using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectBalance;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace MsTests
{
    /// <summary>PB2 fix round: one free pool per item, the BOM tree, the order statuses, the supply scope, the hard-reservation guard, release and re-enable, the draft requests that people edit, and the links of the documents.</summary>
    public partial class ProjectBalancePb2Tests
    {
        private const string SubAssembly = "99.11.02.000.2";

        private static async Task DeviateAsync(AppHost host, Project pr, PbChangeKind kind, string targetPart, decimal? newQty = null, string newPart = null, PbMakeOrBuy? makeOrBuy = null, bool child = false)
        {
            int baselineId = host.Read(u => u.Set<PbProjectBaseline>().Single(b => b.WorkOrderId == pr.WorkOrderId && b.IsCurrent).Id);
            int lineId = host.Read(u => u.Set<PbProjectBomLine>().Where(l => l.BaselineId == baselineId && l.PartNumber == targetPart && !l.IsRemoved && (child ? l.ParentLineId != null : true)).OrderBy(l => l.Id).First().Id);
            var saved = await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SaveChangeAsync(new SavePbChangeDto { BaselineId = baselineId, Kind = kind, TargetLineId = lineId, NewQty = newQty, PartNumber = newPart, PartName = newPart, MakeOrBuy = makeOrBuy, Reason = "project need" }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            int id = saved.Data.Id;
            Assert.IsTrue((await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SubmitChangeAsync(new PbChangeActionDto { Id = id }, Ct))).Success);
            Assert.IsTrue((await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = id }, Ct))).Success);
            Assert.IsTrue((await Run(host, Plan2, p => p.GetRequiredService<IPbProductService>().ApproveChangePlanningAsync(new PbChangeActionDto { Id = id }, Ct))).Success);
            var applied = await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id }, Ct));
            Assert.IsTrue(applied.Success, applied.Message);
        }

        private static void AcceptReview(AppHost host, Project pr) =>
            host.Write(u => { AppHost.Add(u, new PcContractReview { ContractId = pr.ContractId, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept }); u.SaveChanges(); }, Pmo);

        // ------------------------------------------------------------------------------------------ H2: the tree

        [TestMethod]
        public void Tree_QuantityIsTheProductOfTheAncestors_AssemblyScrapIsCarried_AndARemovedAssemblyCutsItsParts()
        {
            var lines = new List<PbProjectBomLine>
            {
                new PbProjectBomLine { Id = 1, Qty = 2m },                        // assembly A, 2 per product
                new PbProjectBomLine { Id = 2, Qty = 4m, ParentLineId = 1 },      // part of A, 4 per A
                new PbProjectBomLine { Id = 3, Qty = 3m, ParentLineId = 2 },      // part of that part, 3 per part
                new PbProjectBomLine { Id = 4, Qty = 5m },                        // a root part
            };
            var t = PbBomTree.Totals(lines, 3, null);
            Assert.AreEqual(6m, t[1]);
            Assert.AreEqual(24m, t[2]);
            Assert.AreEqual(72m, t[3]);
            Assert.AreEqual(15m, t[4]);
            // the assembly carries 10 percent scrap: its parts are multiplied by 1.1, the assembly itself is not
            var scrapped = PbBomTree.Totals(lines, 3, l => l.Id == 1 ? 0.1m : 0m);
            Assert.AreEqual(6m, scrapped[1]);
            Assert.AreEqual(26.4m, scrapped[2]);
            Assert.AreEqual(79.2m, scrapped[3]);
            // removed assembly: it and everything below count zero
            lines[0].IsRemoved = true;
            var cut = PbBomTree.Totals(lines, 3, null);
            Assert.AreEqual(0m, cut[1]);
            Assert.AreEqual(0m, cut[2]);
            Assert.AreEqual(0m, cut[3]);
            Assert.AreEqual(15m, cut[4]);
        }

        [TestMethod]
        public async Task Deviation_RemovedAssembly_DropsAllItsParts_FromTheBalance()
        {
            AppHost host = NewHost("treeremove");
            Project pr = await SetUpAsync(host);
            await DeviateAsync(host, pr, PbChangeKind.Remove, SubAssembly);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.AreEqual(6m, Sum(run, pr.ScrewItem, l => l.GrossQty), "only the 2 direct screws x 3 are left");
            Assert.AreEqual(0, run.Lines.Count(l => l.ItemId == pr.BoltItem), "the bolt belonged to the removed assembly");
        }

        [TestMethod]
        public async Task Deviation_QuantityOfAnAssembly_ScalesItsParts_AndAChangeOfAPartKeepsTheParentMultiplier()
        {
            AppHost host = NewHost("treeqty");
            Project pr = await SetUpAsync(host);
            await DeviateAsync(host, pr, PbChangeKind.Quantity, SubAssembly, 4m);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.AreEqual(6m + 4m * 4m * 3m, Sum(run, pr.ScrewItem, l => l.GrossQty), "4 screws x 4 assemblies x 3 units + 6 direct");
            Assert.AreEqual(12m, Sum(run, pr.BoltItem, l => l.GrossQty), "1 bolt x 4 assemblies x 3 units");
            await DeviateAsync(host, pr, PbChangeKind.Quantity, Screw, 5m, child: true);
            BalanceRunDto run2 = await BalanceAsync(host, pr);
            Assert.AreEqual(6m + 5m * 4m * 3m, Sum(run2, pr.ScrewItem, l => l.GrossQty), "the part is now 5 per assembly and the assembly multiplier (4 x 3) stays");
        }

        [TestMethod]
        public async Task Deviation_ReplacedAssembly_DropsItsParts_AndTheNewPartTakesTheAssemblyQuantity()
        {
            AppHost host = NewHost("treereplace");
            Project pr = await SetUpAsync(host);
            var spare = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = Spare, PartTitle = "Spare screw", Material = "ST37" }, Ct));
            Assert.IsTrue(spare.Success, spare.Message);
            await DeviateAsync(host, pr, PbChangeKind.Replace, SubAssembly, null, Spare, PbMakeOrBuy.Purchase);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.AreEqual(6m, Sum(run, pr.ScrewItem, l => l.GrossQty), "the screws below the replaced assembly are gone");
            Assert.AreEqual(0, run.Lines.Count(l => l.ItemId == pr.BoltItem));
            Assert.AreEqual(6m, Sum(run, spare.Data.Id, l => l.GrossQty), "2 x 3 units of the replacement");
        }

        // ------------------------------------------------------------------------------------------ H1: one free pool

        [TestMethod]
        public async Task Balance_RunThreeTimes_GivesTheSameFigures_NoDoubleUse_AndNoRequestIsDraftedAgain()
        {
            AppHost host = NewHost("stable");
            Project pr = await SetUpAsync(host);
            AcceptReview(host, pr);
            var spare = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = Spare, PartTitle = "Spare screw", Material = "ST37" }, Ct));
            Stock(host, pr.General, pr.ScrewItem, 10m);
            Stock(host, pr.General, spare.Data.Id, 100m);
            var proposed = await Run(host, Eng1, p => p.GetRequiredService<IItemSubstituteService>().SaveAsync(new SaveSubstituteDto { ItemId = pr.ScrewItem, SubstituteItemId = spare.Data.Id, Ratio = 2m, Scope = SubstituteScope.Global, Reason = "equivalent" }, Ct));
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IItemSubstituteService>().DecideAsync(new SubstituteDecisionDto { Id = proposed.Data.Id, Action = "approve" }, Ct))).Success);

            var runs = new List<BalanceRunDto>();
            for (int i = 0; i < 3; i++)
            {
                BalanceRunDto run = await BalanceAsync(host, pr);
                runs.Add(run);
                var d = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct));
                Assert.IsTrue(d.Success, d.Message);
            }
            foreach (BalanceRunDto run in runs)
            {
                // 31 screws needed: 10 from stock, the other 21 from the substitute at ratio 2 = 42 units; nothing is short
                Assert.AreEqual(10m, Sum(run, pr.ScrewItem, l => l.ReservedQty), "run " + run.RunNumber);
                Assert.AreEqual(42m, Sum(run, pr.ScrewItem, l => l.SubstituteQty), "run " + run.RunNumber);
                Assert.AreEqual(0m, Sum(run, pr.ScrewItem, l => l.ShortQty), "run " + run.RunNumber);
                Assert.AreEqual(8m, Sum(run, pr.BoltItem, l => l.ShortQty), "run " + run.RunNumber);
            }
            // the reservations did not grow with the runs
            Assert.AreEqual(10m, host.Read(u => u.Set<ProjectReservation>().Where(r => r.WorkOrderId == pr.WorkOrderId && r.ItemId == pr.ScrewItem && r.Status == ReservationStatus.Active).Sum(r => r.Quantity)));
            Assert.AreEqual(42m, host.Read(u => u.Set<ProjectReservation>().Where(r => r.WorkOrderId == pr.WorkOrderId && r.ItemId == spare.Data.Id && r.Status == ReservationStatus.Active).Sum(r => r.Quantity)));
            // one draft request (for the bolts), the same one every time
            var requests = host.Read(u => u.Set<PurchaseRequest>().Where(r => r.ProjectBaselineId != null && !r.Deleted).ToList());
            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual(1, host.Read(u => u.Set<PurchaseRequestLine>().Count(l => l.PurchaseRequest.ProjectBaselineId != null)));
        }

        // ------------------------------------------------------------------------------------------ H3, M3, M4: what counts as supply

        private static (int RequestLineId, int RequestId) AddRequest(AppHost host, Project pr, int item, decimal qty, int? workOrderId, int? contractId, PurchaseRequestStatus status = PurchaseRequestStatus.Submitted)
        {
            int line = 0, request = 0;
            host.Write(u =>
            {
                var req = AppHost.Add(u, new PurchaseRequest { Number = "PR-T" + Guid.NewGuid().ToString("N").Substring(0, 6), Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = status, RequestDate = DateTime.Today, WorkOrderId = workOrderId, ContractId = contractId });
                u.SaveChanges();
                var l = new PurchaseRequestLine { PurchaseRequestId = req.Id, RowNumber = 1, ItemId = item, Quantity = qty, Unit = "عدد", Description = "line" };
                u.Set<PurchaseRequestLine>().Add(l);
                u.SaveChanges();
                line = l.Id; request = req.Id;
            }, Pmo);
            return (line, request);
        }

        private static void AddOrder(AppHost host, int item, decimal qty, decimal received, PurchaseOrderStatus status, int? requestLineId, int? workOrderId, int? contractId)
        {
            host.Write(u =>
            {
                var po = AppHost.Add(u, new PurchaseOrder { Number = "PO-T" + Guid.NewGuid().ToString("N").Substring(0, 6), Status = status, Kind = PurchaseKind.Goods, OrderDate = DateTime.Today, ProviderId = 1, WorkOrderId = workOrderId, ContractId = contractId });
                u.SaveChanges();
                u.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, ItemId = item, Quantity = qty, ReceivedQuantity = received, RequestLineId = requestLineId, WorkOrderId = workOrderId, ContractId = contractId, Unit = "عدد", DeliveryDate = DateTime.Today.AddDays(10) });
                u.SaveChanges();
            }, Pmo);
        }

        [TestMethod]
        public async Task Balance_OneSetOfOrderStatuses_ForTheRequestAndForTheOrderPool()
        {
            AppHost host = NewHost("postatus");
            Project pr = await SetUpAsync(host);
            var (lineId, _) = AddRequest(host, pr, pr.ScrewItem, 20m, pr.WorkOrderId, pr.ContractId);
            AddOrder(host, pr.ScrewItem, 12m, 0m, PurchaseOrderStatus.Draft, lineId, pr.WorkOrderId, pr.ContractId);       // a draft order already holds 12 of the 20
            AddOrder(host, pr.ScrewItem, 5m, 0m, PurchaseOrderStatus.Cancelled, lineId, pr.WorkOrderId, pr.ContractId);     // cancelled: nowhere
            AddOrder(host, pr.ScrewItem, 6m, 2m, PurchaseOrderStatus.Closed, lineId, pr.WorkOrderId, pr.ContractId);        // closed short: only the 2 received count against the request, and nothing arrives
            BalanceRunDto run = await BalanceAsync(host, pr);
            // request 20 - 12 (draft order) - 2 (received on the closed order) = 6 still open; 12 arrive on the draft order; 31 - 6 - 12 = 13 short
            Assert.AreEqual(6m, Sum(run, pr.ScrewItem, l => l.OnOrderPrQty));
            Assert.AreEqual(12m, Sum(run, pr.ScrewItem, l => l.OnOrderPoQty));
            Assert.AreEqual(13m, Sum(run, pr.ScrewItem, l => l.ShortQty));
        }

        [TestMethod]
        public async Task Balance_GeneralSupplyIsNotCounted_AndContractSupplyIsGivenToOneWorkOrderOnly()
        {
            AppHost host = NewHost("scope");
            Project pr = await SetUpAsync(host);
            // a second work order of the same contract, with its own copy of the product
            int wo2 = 0;
            host.Write(u => { var w = AppHost.Add(u, new Entities.Model.NasbNiro.Contracts.WorkOrder { Code = "W-A2", Title = "unit A2", Count = 1, ContractId = pr.ContractId, EquipmentId = 1 }); u.SaveChanges(); wo2 = w.Id; }, Pmo);
            var inst = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo2, ProductId = pr.ProductId }, Ct));
            Assert.IsTrue(inst.Success, inst.Message);
            AddRequest(host, pr, pr.ScrewItem, 7m, null, null);                         // no project: general supply
            AddOrder(host, pr.ScrewItem, 9m, 0m, PurchaseOrderStatus.Approved, null, null, null);
            AddRequest(host, pr, pr.ScrewItem, 5m, null, pr.ContractId);                // the contract, no work order
            var first = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = pr.WorkOrderId }, Ct));
            var second = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = wo2 }, Ct));
            Assert.AreEqual(5m, Sum(first.Data.Runs.Single(), pr.ScrewItem, l => l.OnOrderPrQty), "the first work order of the contract gets the contract supply");
            Assert.AreEqual(0m, Sum(first.Data.Runs.Single(), pr.ScrewItem, l => l.OnOrderPoQty), "the order with no project is nobody's");
            Assert.AreEqual(0m, Sum(second.Data.Runs.Single(), pr.ScrewItem, l => l.OnOrderPrQty), "the same supply is not counted twice");
        }

        [TestMethod]
        public async Task Balance_AKitAndTheReservationItIsDraftedFromAreOneClaim_NotTwo()
        {
            AppHost host = NewHost("claim");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 15m);
            host.Write(u =>
            {
                u.Set<ProjectReservation>().Add(new ProjectReservation { ContractId = pr.OtherContractId, WorkOrderId = pr.OtherWorkOrderId, BaselineId = 9999, ItemId = pr.ScrewItem, Quantity = 10m, Status = ReservationStatus.Active, IsHard = true, Reason = "other" });
                var mr = AppHost.Add(u, new MaterialRequest { Number = "MR-T1", Status = MaterialRequestStatus.Approved, RequestDate = DateTime.Today, WorkOrderId = pr.OtherWorkOrderId });
                u.SaveChanges();
                u.Set<MaterialRequestLine>().Add(new MaterialRequestLine { MaterialRequestId = mr.Id, RowNumber = 1, ItemId = pr.ScrewItem, Quantity = 10m });
                u.SaveChanges();
            }, Pmo);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.AreEqual(5m, Sum(run, pr.ScrewItem, l => l.ReservedQty), "15 on hand - 10 held by the other order (reservation and kit counted once)");
        }

        // ------------------------------------------------------------------------------------------ M5, M6: reservations

        private static InventoryDocument Doc(InventoryDocumentType type, int? workOrderId, int warehouse, int item, decimal quantity, int? lot = null, int? destination = null) => new InventoryDocument
        {
            Type = type, WorkOrderId = workOrderId, WarehouseId = warehouse, DestinationWarehouseId = destination,
            Lines = new List<InventoryDocumentLine> { new InventoryDocumentLine { ItemId = item, Quantity = quantity, LotId = lot, Direction = DocumentLineDirection.Out } },
        };

        private static Task<string> Check(AppHost host, InventoryDocument d) =>
            host.As(Plan1, async p => await ProjectReservationRules.CheckIssueAsync(p.GetRequiredService<IUnitOfWork>(), d, Ct));

        [TestMethod]
        public async Task HardReservation_GuardsEveryDocumentThatTakesStockOut_ByLotAndSerial()
        {
            AppHost host = NewHost("guard");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            await BalanceAsync(host, pr);   // 10 reserved for the project
            int other = pr.OtherWorkOrderId;
            foreach (var type in new[] { InventoryDocumentType.AdjustmentDecrease, InventoryDocumentType.CustomerDelivery, InventoryDocumentType.ScrapSale, InventoryDocumentType.StageConversion, InventoryDocumentType.SubcontractIssue, InventoryDocumentType.Issue })
                Assert.IsNotNull(await Check(host, Doc(type, type == InventoryDocumentType.Issue || type == InventoryDocumentType.SubcontractIssue ? other : (int?)null, pr.General, pr.ScrewItem, 3m, null, type == InventoryDocumentType.SubcontractIssue ? pr.Quarantine : (int?)null)), type + " eats into the reservation");
            // a transfer between two counted warehouses does not change what is available; a transfer out of the counted ones does
            host.Write(u => { var w = AppHost.Add(u, new Warehouse { Code = "WH3", Title = "parts", Kind = WarehouseKind.Parts, IsActive = true }); u.SaveChanges(); }, Pmo);
            int parts = host.Read(u => u.Set<Warehouse>().Single(w => w.Code == "WH3").Id);
            Assert.IsNull(await Check(host, Doc(InventoryDocumentType.Transfer, null, pr.General, pr.ScrewItem, 3m, null, parts)), "counted to counted");
            Assert.IsNotNull(await Check(host, Doc(InventoryDocumentType.Transfer, null, pr.General, pr.ScrewItem, 3m, null, pr.Quarantine)), "counted to quarantine");
            Assert.IsNull(await Check(host, Doc(InventoryDocumentType.AdjustmentDecrease, pr.WorkOrderId, pr.General, pr.ScrewItem, 3m)), "the owner may");
        }

        [TestMethod]
        public async Task HardReservation_OfOneLot_IsHeldForThatLotOnly()
        {
            AppHost host = NewHost("lot");
            Project pr = await SetUpAsync(host);
            host.Write(u =>
            {
                u.Set<StockBalance>().Add(new StockBalance { ItemId = pr.ScrewItem, WarehouseId = pr.General, LotId = 101, Quantity = 10m });
                u.Set<StockBalance>().Add(new StockBalance { ItemId = pr.ScrewItem, WarehouseId = pr.General, LotId = 102, Quantity = 10m });
                u.Set<ProjectReservation>().Add(new ProjectReservation { ContractId = pr.ContractId, WorkOrderId = pr.WorkOrderId, BaselineId = 1, ItemId = pr.ScrewItem, LotId = 101, Quantity = 10m, Status = ReservationStatus.Active, IsHard = true, Reason = "lot" });
                u.SaveChanges();
            }, Pmo);
            Assert.IsNull(await Check(host, Doc(InventoryDocumentType.Issue, pr.OtherWorkOrderId, pr.General, pr.ScrewItem, 8m, 102)), "the other lot is free");
            Assert.IsNotNull(await Check(host, Doc(InventoryDocumentType.Issue, pr.OtherWorkOrderId, pr.General, pr.ScrewItem, 3m, 101)), "the reserved lot is held");
        }

        [TestMethod]
        public async Task Reservation_IsUsedBySubcontractIssue_AndGivenBackWhenTheIssueIsCancelled()
        {
            AppHost host = NewHost("undo");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            await BalanceAsync(host, pr);
            await host.As(Plan1, async p =>
            {
                var uow = p.GetRequiredService<IUnitOfWork>();
                InventoryDocument d = Doc(InventoryDocumentType.SubcontractIssue, pr.WorkOrderId, pr.General, pr.ScrewItem, 4m, null, pr.Quarantine);
                await ProjectReservationRules.ApplyIssueAsync(uow, d, 15, Ct);
                await uow.SaveChangesAsync(Ct);
                return true;
            });
            Assert.AreEqual(4m, host.Read(u => u.Set<ProjectReservation>().Single(r => r.WorkOrderId == pr.WorkOrderId && r.ItemId == pr.ScrewItem).IssuedQuantity), "the send to the contractor used 4");
            // the same document is cancelled: its lines are on file, so the reservation gets the 4 back
            int docId = 0;
            host.Write(u => { var d = Doc(InventoryDocumentType.SubcontractIssue, pr.WorkOrderId, pr.General, pr.ScrewItem, 4m, null, pr.Quarantine); d.Number = "SI-T1"; d.Status = InventoryDocumentStatus.Confirmed; u.Set<InventoryDocument>().Add(d); u.SaveChanges(); docId = d.Id; }, Pmo);
            await host.As(Plan1, async p =>
            {
                var uow = p.GetRequiredService<IUnitOfWork>();
                InventoryDocument d = uow.Set<InventoryDocument>().Single(x => x.Id == docId);
                await ProjectReservationRules.UndoIssueAsync(uow, d, 15, Ct);
                await uow.SaveChangesAsync(Ct);
                return true;
            });
            var r = host.Read(u => u.Set<ProjectReservation>().Single(x => x.WorkOrderId == pr.WorkOrderId && x.ItemId == pr.ScrewItem));
            Assert.AreEqual(0m, r.IssuedQuantity);
            Assert.AreEqual(ReservationStatus.Active, r.Status);
        }

        [TestMethod]
        public async Task ReleasedStock_IsNotReservedAgainByTheBalance_UntilAPlannerEnablesIt()
        {
            AppHost host = NewHost("reenable");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            await BalanceAsync(host, pr);
            ReservationDto r = await FirstReservationAsync(host, pr);
            Assert.IsTrue((await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().RequestReleaseAsync(new ReservationActionDto { Id = r.Id, Reason = "not needed" }, Ct))).Success);
            var released = await Run(host, Plan2, p => p.GetRequiredService<IProjectReservationService>().DecideReleaseAsync(new ReservationActionDto { Id = r.Id, Approve = true }, Ct));
            Assert.IsTrue(released.Success, released.Message);
            Assert.IsTrue(released.Data.AutoReserveBlocked);

            await BalanceAsync(host, pr);
            Assert.AreEqual(0, host.Read(u => u.Set<ProjectReservation>().Count(x => x.WorkOrderId == pr.WorkOrderId && x.ItemId == pr.ScrewItem && x.Status == ReservationStatus.Active)), "the balance did not take the released stock again");

            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().ReEnableAsync(new ReservationActionDto { Id = r.Id }, Ct))).Success, "a reason is required");
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IProjectReservationService>().ReEnableAsync(new ReservationActionDto { Id = r.Id, Reason = "x" }, Ct))).Success, "planning only");
            var on = await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().ReEnableAsync(new ReservationActionDto { Id = r.Id, Reason = "needed again" }, Ct));
            Assert.IsTrue(on.Success, on.Message);
            await BalanceAsync(host, pr);
            Assert.AreEqual(10m, host.Read(u => u.Set<ProjectReservation>().Where(x => x.WorkOrderId == pr.WorkOrderId && x.ItemId == pr.ScrewItem && x.Status == ReservationStatus.Active).Sum(x => x.Quantity)));
        }

        // ------------------------------------------------------------------------------------------ M1, M7, M8: drafts

        [TestMethod]
        public async Task Drafts_AreNotMadeFromAnEmptyRun()
        {
            AppHost host = NewHost("emptyrun");
            Project pr = await SetUpAsync(host);
            AcceptReview(host, pr);
            BalanceRunDto run = await BalanceAsync(host, pr);
            // a header whose lines were never saved (what a half-failed run used to leave)
            host.Write(u => { var r = u.Set<ProjectMaterialBalance>().Single(x => x.Id == run.Id); r.LineCount += 5; u.SaveChanges(); }, Pmo);
            var d = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct));
            Assert.IsFalse(d.Success);
            Assert.AreEqual(BalanceMessages.RunEmpty, d.Message);
        }

        [TestMethod]
        public void Conflict_IsAn409_WithAMessage()
        {
            var r = Common.Results.ResponseDtoHelper.Conflict<int>(BalanceMessages.RunBusy);
            Assert.IsFalse(r.Success);
            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, r.HttpStatusCode);
        }

        [TestMethod]
        public async Task Drafts_KeepTheMaker_TheHumanLines_AndTheLinesAPersonEdited()
        {
            AppHost host = NewHost("human");
            Project pr = await SetUpAsync(host);
            AcceptReview(host, pr);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.IsTrue((await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct))).Success);
            int requestId = host.Read(u => u.Set<PurchaseRequest>().Single(r => r.ProjectBaselineId != null).Id);
            // a person adds a line of their own and edits the quantity of the bolt line
            host.Write(u =>
            {
                u.Set<PurchaseRequestLine>().Add(new PurchaseRequestLine { PurchaseRequestId = requestId, RowNumber = 9, ItemId = pr.General, Quantity = 3m, Unit = "عدد", Description = "added by hand" });
                var bolt = u.Set<PurchaseRequestLine>().Single(l => l.PurchaseRequestId == requestId && l.ItemId == pr.BoltItem);
                bolt.Quantity = 20m; bolt.ManuallyEdited = true;
                u.SaveChanges();
            }, Plan2);
            Stock(host, pr.General, pr.BoltItem, 3m);
            BalanceRunDto run2 = await BalanceAsync(host, pr);
            var again = await Run(host, Plan2, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run2.Id, Ct));
            Assert.IsTrue(again.Success, again.Message);
            Assert.AreEqual(15, host.Read(u => u.Set<PurchaseRequest>().Single(r => r.Id == requestId).CreatorUserId), "the maker of the draft is the first planner, not the one who re-ran");
            Assert.AreEqual(20m, host.Read(u => u.Set<PurchaseRequestLine>().Single(l => l.PurchaseRequestId == requestId && l.ItemId == pr.BoltItem).Quantity), "an edited quantity is not reset");
            Assert.IsTrue(host.Read(u => u.Set<PurchaseRequestLine>().Any(l => l.PurchaseRequestId == requestId && l.Description == "added by hand")), "the line a person added stays");
        }

        [TestMethod]
        public async Task LinkedDrawing_WhenTheSourceIsVoided_IsObsoleteOnTheSubmittedRequest_AndTheLinkIsAFlag()
        {
            AppHost host = NewHost("obsolete");
            Project pr = await SetUpAsync(host);
            AcceptReview(host, pr);
            Guid drawing = Guid.NewGuid();
            int sourceId = 0;
            host.Write(u =>
            {
                var a = AppHost.Add(u, new RecordAttachment { OwnerType = AttachmentOwnerType.InventoryItem, OwnerId = pr.ScrewItem, DocumentId = drawing, Kind = AttachmentKind.Drawing, Title = "[پیوند] a title that only looks like a link", FileName = "screw.pdf", UploadedAt = DateTimeOffset.Now, UploadedByUserId = 11 });
                u.SaveChanges();
                sourceId = a.Id;
            }, Pmo);
            Stock(host, pr.General, pr.ScrewItem, 1m);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.IsTrue((await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct))).Success);
            var link = host.Read(u => u.Set<RecordAttachment>().Single(a => a.LinkedFromAttachmentId == sourceId && a.OwnerType == AttachmentOwnerType.PurchaseRequestLine));
            Assert.IsFalse(link.LinkObsolete);
            // the request is submitted; then the drawing is voided at its source
            host.Write(u => { foreach (var r in u.Set<PurchaseRequest>().Where(r => r.ProjectBaselineId != null).ToList()) r.Status = PurchaseRequestStatus.Submitted; u.SaveChanges(); }, Plan1);
            var voided = await Run(host, Pmo, p => p.GetRequiredService<IAttachmentService>().VoidAsync(new ViewModels.NasbNiro.AttachmentDtos.VoidAttachmentDto { Id = sourceId, Reason = "wrong revision" }, Ct));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.IsTrue(host.Read(u => u.Set<RecordAttachment>().Single(a => a.Id == link.Id).LinkObsolete), "the link on the submitted request is obsolete");
        }

        // ------------------------------------------------------------------------------------------ Low items

        [TestMethod]
        public void Units_PiecesAreWholeNumbersRoundedUp_OtherUnitsKeepFourDecimals_AndDifferentUnitsAreFlagged()
        {
            Assert.AreEqual(8m, BalanceRules.RoundUnit(7.5m, "عدد"));
            Assert.AreEqual(8m, BalanceRules.RoundUnit(7.0001m, "pcs"));
            Assert.AreEqual(7.5m, BalanceRules.RoundUnit(7.5m, "kg"));
            Assert.AreEqual(7.5556m, BalanceRules.RoundUnit(7.55555m, "m"));
            Assert.IsFalse(BalanceRules.UnitsDiffer("kg", "کیلوگرم"));
            Assert.IsFalse(BalanceRules.UnitsDiffer("عدد", "pcs"));
            Assert.IsTrue(BalanceRules.UnitsDiffer("m", "kg"));
            Assert.IsFalse(BalanceRules.UnitsDiffer(null, "kg"));
        }

        [TestMethod]
        public void Allocate_SubstituteStockReservedForTheProject_CoversTheDemandBeforeFreeStock()
        {
            var rows = new List<DemandRow> { new DemandRow { Required = 10m } };
            var a = BalanceRules.Allocate(rows, 0m, 0m, 4m, 3m, true, 0m, null);
            Assert.AreEqual(4m, a[0].Credit);
            Assert.AreEqual(3m, a[0].FromFree);
            Assert.AreEqual(3m, a[0].ReservedNew);
            Assert.AreEqual(3m, a[0].Short);
        }

        [TestMethod]
        public async Task Balance_DropsNothingForAPartsOfAnotherUnit_ButFlagsTheLine()
        {
            AppHost host = NewHost("unit");
            Project pr = await SetUpAsync(host);
            host.Write(u => { foreach (var l in u.Set<PbProjectBomLine>().Where(l => l.PartNumber == Screw).ToList()) l.Unit = "m"; u.SaveChanges(); }, Pmo);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.IsTrue(run.Lines.Where(l => l.ItemId == pr.ScrewItem).All(l => l.UnitMismatch), "no conversion is defined: the line is flagged");
        }
    }
}
