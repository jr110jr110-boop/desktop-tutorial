using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
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
    /// <summary>
    /// PB2: the automatic material balance. The explosion of a multi-level BOM with scrap, the exact netting arithmetic (stock of the counted warehouses, the reservations of other projects, open requests),
    /// approved substitutes, the idempotent draft purchase requests with their technical documents, and the reservation rules (reason, two people, the issue guard).
    /// All names are generic.
    /// </summary>
    [TestClass]
    public class ProjectBalancePb2Tests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Eng1 = AppHost.Principal(11, "engineer.one", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(12, "engineer.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(13, "qc.one", "QC");
        private static readonly ClaimsPrincipal Shop = AppHost.Principal(14, "shop.one", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Plan1 = AppHost.Principal(15, "planning.one", "PLANNING");
        private static readonly ClaimsPrincipal Plan2 = AppHost.Principal(16, "planning.two", "PLANNING");
        private static readonly ClaimsPrincipal Sales = AppHost.Principal(17, "sales.one", "COMMERCIAL");
        private static readonly CancellationToken Ct = CancellationToken.None;

        private const string Screw = "70.04.06.052.1";
        private const string Bolt = "70.04.06.053.1";
        private const string Spare = "70.04.06.099.1";

        private static AppHost NewHost(string name) => new AppHost("Pb2_" + name + "_" + Guid.NewGuid().ToString("N"));
        private static Task<T> Run<T>(AppHost host, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => host.As(who, call);

        // ------------------------------------------------------------------------------------------ the rules without a database

        [TestMethod]
        public void ScrapFactor_IsTheEnteredFactor_ElseFromTheWeights_ElseZero()
        {
            Assert.AreEqual(0.1m, BalanceRules.ScrapFactor(0.1m, 10m, 12m));
            Assert.AreEqual(0.2m, BalanceRules.ScrapFactor(null, 10m, 12m), "gross / net - 1");
            Assert.AreEqual(0.2m, BalanceRules.ScrapFactor(0m, 10m, 12m));
            Assert.AreEqual(0m, BalanceRules.ScrapFactor(null, null, 12m));
            var (gross, scrap) = BalanceRules.Requirement(24m, 0.1m);
            Assert.AreEqual(24m, gross);
            Assert.AreEqual(2.4m, scrap);
        }

        [TestMethod]
        public void Allocate_CoversInOrder_IssuedReservedFreeRequestsOrders_AndLeavesTheShortage()
        {
            var rows = new List<DemandRow> { new DemandRow { Required = 10m, UseBy = new DateTime(2030, 1, 10) }, new DemandRow { Required = 20m, UseBy = new DateTime(2030, 2, 10) } };
            var pos = new List<PoArrival> { new PoArrival { Date = new DateTime(2030, 1, 20), Quantity = 4m }, new PoArrival { Date = new DateTime(2030, 3, 1), Quantity = 100m } };
            var a = BalanceRules.Allocate(rows, 2m, 3m, 6m, true, 5m, pos);
            // row 1: 2 issued, 3 reserved, 5 free; nothing else is needed
            Assert.AreEqual(2m, a[0].Issued);
            Assert.AreEqual(3m, a[0].ReservedExisting);
            Assert.AreEqual(5m, a[0].FromFree);
            Assert.AreEqual(0m, a[0].Short);
            // row 2: 1 free left, 5 on requests, 4 from the order that arrives in time (the second order is too late), the rest is short
            Assert.AreEqual(1m, a[1].FromFree);
            Assert.AreEqual(5m, a[1].Pr);
            Assert.AreEqual(4m, a[1].Po);
            Assert.AreEqual(10m, a[1].Short);
        }

        [TestMethod]
        public void Substitute_UsesTheRatio_AndOnlyTheFreeStock()
        {
            var (covered, units) = BalanceRules.Substitute(19m, 2m, 100m);
            Assert.AreEqual(19m, covered);
            Assert.AreEqual(38m, units);
            var (covered2, units2) = BalanceRules.Substitute(19m, 2m, 10m);
            Assert.AreEqual(5m, covered2, "10 units of the substitute replace 5 of the item");
            Assert.AreEqual(10m, units2);
            Assert.AreEqual(0m, BalanceRules.Substitute(19m, 2m, 0m).Covered);
        }

        // ------------------------------------------------------------------------------------------ the project

        private sealed class Project
        {
            public int ContractId, WorkOrderId, OtherContractId, OtherWorkOrderId, ScrewItem, BoltItem, SubBomId, TopBomId, General, Quarantine;
        }

        private static SavePbBomLineDto Line(int no, string pn, string name, decimal qty, PbMakeOrBuy mob, decimal? scrap = null, int? child = null) =>
            new SavePbBomLineDto { ItemNumber = no, PartNumber = pn, PartName = name, Qty = qty, Unit = "عدد", Material = "ST37", MakeOrBuy = mob, ScrapFactor = scrap, ChildBomId = child };

        private static async Task<PbBomDto> ReleaseAsync(AppHost host, string assembly, int? productId, params SavePbBomLineDto[] lines)
        {
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(new SavePbBomDto { ProductId = productId, AssemblyPartNumber = assembly, Title = "BOM " + assembly, Lines = lines.ToList() }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct))).Success);
            foreach (var (who, step) in new[] { (Eng2, PbBomSignature.Checked), (Qc, PbBomSignature.QcChecked), (Shop, PbBomSignature.ManufacturingChecked), (Pmo, PbBomSignature.Approved) })
            {
                var s = await Run(host, who, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = saved.Data.Id, Step = step }, Ct));
                Assert.IsTrue(s.Success, step + ": " + s.Message);
            }
            var rel = await Run(host, AppHost.Principal(2, "pmo.two", "PMO"), p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(rel.Success, rel.Message);
            return rel.Data;
        }

        private static (int ContractId, int WorkOrderId) NewWorkOrder(AppHost host, string code, int count)
        {
            int contract = 0, wo = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + code, Title = "contract " + code, Status = ContractStatus.Current });
                u.SaveChanges();
                var w = AppHost.Add(u, new WorkOrder { Code = "W-" + code, Title = "unit " + code, Count = count, ContractId = c.Id, EquipmentId = 1 });
                u.SaveChanges();
                contract = c.Id; wo = w.Id;
            }, Pmo);
            return (contract, wo);
        }

        /// <summary>
        /// A top assembly: 2 screws, and 2 sub-assemblies of (4 screws and 1 bolt with 25 percent scrap). The work order is for 3 units, so
        /// screws: 2 x 3 + 4 x 2 x 3 = 30 (scrap factor of the screw line is 0.1 on the direct line only: 6 x 0.1 = 0.6), bolts: 1 x 2 x 3 = 6, scrap 1.5.
        /// </summary>
        private static async Task<Project> SetUpAsync(AppHost host)
        {
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            Assert.IsTrue(product.Success, product.Message);
            var sub = await ReleaseAsync(host, "99.11.02.000.2", null, Line(1, Screw, "Hex screw", 4, PbMakeOrBuy.Purchase), Line(2, Bolt, "Anchor bolt", 1, PbMakeOrBuy.Purchase, 0.25m));
            var top = await ReleaseAsync(host, "99.11.00.000.2", product.Data.Id, Line(1, Screw, "Hex screw", 2, PbMakeOrBuy.Purchase, 0.1m), Line(2, "99.11.02.000.2", "Sub assembly", 2, PbMakeOrBuy.Make, null, sub.Id));
            var (contract, wo) = NewWorkOrder(host, "A", 3);
            var (otherContract, otherWo) = NewWorkOrder(host, "B", 1);
            var res = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = product.Data.Id }, Ct));
            Assert.IsTrue(res.Success, res.Message);
            var pr = new Project { ContractId = contract, WorkOrderId = wo, OtherContractId = otherContract, OtherWorkOrderId = otherWo, SubBomId = sub.Id, TopBomId = top.Id };
            host.Write(u =>
            {
                pr.ScrewItem = u.Set<InventoryItem>().Single(i => i.Code == Screw).Id;
                pr.BoltItem = u.Set<InventoryItem>().Single(i => i.Code == Bolt).Id;
                var general = AppHost.Add(u, new Warehouse { Code = "WH1", Title = "general", Kind = WarehouseKind.General, IsActive = true });
                var quarantine = AppHost.Add(u, new Warehouse { Code = "WH2", Title = "quarantine", Kind = WarehouseKind.Quarantine, IsActive = true });
                u.SaveChanges();
                pr.General = general.Id; pr.Quarantine = quarantine.Id;
            }, Pmo);
            return pr;
        }

        private static void Stock(AppHost host, int warehouse, int item, decimal quantity) =>
            host.Write(u => { u.Set<StockBalance>().Add(new StockBalance { ItemId = item, WarehouseId = warehouse, Quantity = quantity }); u.SaveChanges(); }, Pmo);

        private static async Task<BalanceRunDto> BalanceAsync(AppHost host, Project pr, bool drafts = false)
        {
            var res = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = pr.WorkOrderId, CreateDrafts = drafts }, Ct));
            Assert.IsTrue(res.Success, res.Message);
            return res.Data.Runs.Single();
        }

        private static decimal Sum(BalanceRunDto run, int item, Func<BalanceLineDto, decimal> pick) => run.Lines.Where(l => l.ItemId == item).Sum(pick);

        // ------------------------------------------------------------------------------------------ explosion and netting

        [TestMethod]
        public async Task Balance_ExplodesAllLevels_WithScrap_AndNetsTheStockExactly()
        {
            AppHost host = NewHost("net");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            Stock(host, pr.Quarantine, pr.ScrewItem, 100m);   // quarantine is not counted
            host.Write(u =>
            {
                u.Set<ProjectReservation>().Add(new ProjectReservation { ContractId = pr.OtherContractId, WorkOrderId = pr.OtherWorkOrderId, BaselineId = 9999, ItemId = pr.ScrewItem, Quantity = 4m, Status = ReservationStatus.Active, IsHard = true, Reason = "other project" });
                var req = AppHost.Add(u, new PurchaseRequest { Number = "PR-X1", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Submitted, RequestDate = DateTime.Today });
                u.SaveChanges();
                u.Set<PurchaseRequestLine>().Add(new PurchaseRequestLine { PurchaseRequestId = req.Id, RowNumber = 1, ItemId = pr.ScrewItem, Quantity = 5m, Unit = "عدد", Description = "screws" });
                u.SaveChanges();
            }, Pmo);

            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.AreEqual(2, run.RunNumber, "run 1 was made by taking the product from the bank");
            // screws: direct 2 x 3 = 6 (scrap 0.6) plus 4 x 2 x 3 = 24 through the sub assemblies
            Assert.AreEqual(30m, Sum(run, pr.ScrewItem, l => l.GrossQty));
            Assert.AreEqual(0.6m, Sum(run, pr.ScrewItem, l => l.ScrapQty));
            Assert.AreEqual(30.6m, Sum(run, pr.ScrewItem, l => l.RequiredQty));
            // 10 on hand (quarantine left out) - 4 held by another project = 6 free; 5 on an open request; the rest is short: 30.6 - 6 - 5 = 19.6
            Assert.AreEqual(6m, Sum(run, pr.ScrewItem, l => l.ReservedQty));
            Assert.AreEqual(5m, Sum(run, pr.ScrewItem, l => l.OnOrderPrQty));
            Assert.AreEqual(19.6m, Sum(run, pr.ScrewItem, l => l.ShortQty));
            // bolts: 1 x 2 x 3 = 6 with 25 percent scrap = 1.5, no stock at all
            Assert.AreEqual(6m, Sum(run, pr.BoltItem, l => l.GrossQty));
            Assert.AreEqual(1.5m, Sum(run, pr.BoltItem, l => l.ScrapQty));
            Assert.AreEqual(7.5m, Sum(run, pr.BoltItem, l => l.ShortQty));
            // the run keeps its header: project, BOM revision and the settings it used
            Assert.AreEqual(pr.WorkOrderId, run.WorkOrderId);
            Assert.AreEqual(0, run.BomRevision);
            StringAssert.Contains(run.SettingsJson, "warehouseKinds");
            // the stock that was free is now reserved for the project
            Assert.AreEqual(6m, host.Read(u => u.Set<ProjectReservation>().Where(r => r.WorkOrderId == pr.WorkOrderId && r.ItemId == pr.ScrewItem).Sum(r => r.Quantity)));
        }

        [TestMethod]
        public async Task Balance_RunsAgain_AndTheDiffShowsWhatChanged()
        {
            AppHost host = NewHost("diff");
            Project pr = await SetUpAsync(host);
            BalanceRunDto first = await BalanceAsync(host, pr);
            Assert.AreEqual(2, first.RunNumber);
            Stock(host, pr.General, pr.BoltItem, 3m);
            BalanceRunDto second = await BalanceAsync(host, pr);
            Assert.AreEqual(3, second.RunNumber);
            Assert.AreEqual(first.Id, second.PreviousRunId);
            Assert.AreEqual(7.5m, Sum(first, pr.BoltItem, l => l.ShortQty));
            Assert.AreEqual(4.5m, Sum(second, pr.BoltItem, l => l.ShortQty));
            var diff = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().GetDiffAsync(second.Id, null, Ct));
            Assert.IsTrue(diff.Success, diff.Message);
            Assert.AreEqual(1, diff.Data.Changed);
            Assert.AreEqual(0, diff.Data.Added);
            Assert.AreEqual(0, diff.Data.Removed);
            var row = diff.Data.Rows.Single();
            Assert.AreEqual(7.5m, row.OldShort);
            Assert.AreEqual(4.5m, row.NewShort);
        }

        [TestMethod]
        public async Task Balance_NeedsPlanning()
        {
            AppHost host = NewHost("role");
            Project pr = await SetUpAsync(host);
            var denied = await Run(host, Sales, p => p.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = pr.WorkOrderId }, Ct));
            Assert.IsFalse(denied.Success);
        }

        // ------------------------------------------------------------------------------------------ substitutes

        [TestMethod]
        public async Task Substitute_IsUsedOnlyWhenApproved_ByAnotherEngineer_AndTheBalanceShowsWhich()
        {
            AppHost host = NewHost("sub");
            Project pr = await SetUpAsync(host);
            var spare = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = Spare, PartTitle = "Spare screw", Material = "ST37" }, Ct));
            Assert.IsTrue(spare.Success, spare.Message);
            Stock(host, pr.General, spare.Data.Id, 100m);

            var save = new SaveSubstituteDto { ItemId = pr.ScrewItem, SubstituteItemId = spare.Data.Id, Ratio = 2m, Scope = SubstituteScope.Global, Reason = "equivalent grade" };
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IItemSubstituteService>().SaveAsync(save, Ct))).Success, "role");
            var proposed = await Run(host, Eng1, p => p.GetRequiredService<IItemSubstituteService>().SaveAsync(save, Ct));
            Assert.IsTrue(proposed.Success, proposed.Message);
            Assert.AreEqual(SubstituteStatus.Proposed, proposed.Data.Status);

            BalanceRunDto before = await BalanceAsync(host, pr);
            Assert.AreEqual(30.6m, Sum(before, pr.ScrewItem, l => l.ShortQty), "a proposal is not used");

            var self = await Run(host, Eng1, p => p.GetRequiredService<IItemSubstituteService>().DecideAsync(new SubstituteDecisionDto { Id = proposed.Data.Id, Action = "approve" }, Ct));
            Assert.IsFalse(self.Success, "the proposer never approves");
            var planning = await Run(host, Plan1, p => p.GetRequiredService<IItemSubstituteService>().DecideAsync(new SubstituteDecisionDto { Id = proposed.Data.Id, Action = "approve" }, Ct));
            Assert.IsFalse(planning.Success, "approval is engineering");
            var approved = await Run(host, Eng2, p => p.GetRequiredService<IItemSubstituteService>().DecideAsync(new SubstituteDecisionDto { Id = proposed.Data.Id, Action = "approve" }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);

            BalanceRunDto after = await BalanceAsync(host, pr);
            // 30.6 short is covered by 61.2 units of the substitute (ratio 2): the item is not short any more and the line says which substitute
            Assert.AreEqual(0m, Sum(after, pr.ScrewItem, l => l.ShortQty));
            Assert.AreEqual(61.2m, Sum(after, pr.ScrewItem, l => l.SubstituteQty));
            Assert.IsTrue(after.Lines.Where(l => l.ItemId == pr.ScrewItem).Any(l => l.SubstituteItemId == spare.Data.Id));
        }

        // ------------------------------------------------------------------------------------------ the draft purchase requests

        [TestMethod]
        public async Task Drafts_AreMadeOnce_UpdatedOnRerun_CarryTheDocuments_AndAreNeverSubmittedByTheSystem()
        {
            AppHost host = NewHost("draft");
            Project pr = await SetUpAsync(host);
            Guid bomDoc = Guid.NewGuid(), drawing = Guid.NewGuid();
            host.Write(u =>
            {
                AppHost.Add(u, new RecordAttachment { OwnerType = AttachmentOwnerType.PbBomHeader, OwnerId = pr.TopBomId, DocumentId = bomDoc, Kind = AttachmentKind.Bom, Title = "BOM", FileName = "bom.pdf", UploadedAt = DateTimeOffset.Now, UploadedByUserId = 11 });
                AppHost.Add(u, new RecordAttachment { OwnerType = AttachmentOwnerType.InventoryItem, OwnerId = pr.ScrewItem, DocumentId = drawing, Kind = AttachmentKind.Drawing, Title = "Screw drawing", FileName = "screw.pdf", UploadedAt = DateTimeOffset.Now, UploadedByUserId = 11 });
                u.SaveChanges();
            }, Pmo);

            host.Write(u => { AppHost.Add(u, new PcContractReview { ContractId = pr.ContractId, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept }); u.SaveChanges(); }, Pmo);
            BalanceRunDto run = await BalanceAsync(host, pr);
            var first = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct));
            Assert.IsTrue(first.Success, first.Message);
            Assert.IsTrue(first.Data.Created >= 1);
            var requests = host.Read(u => u.Set<PurchaseRequest>().Where(r => r.ProjectBaselineId != null && !r.Deleted).ToList());
            Assert.AreEqual(first.Data.Created, requests.Count);
            Assert.IsTrue(requests.All(r => r.Status == PurchaseRequestStatus.Draft && r.Source == PurchaseRequestSource.Bom && r.ContractId == pr.ContractId), "drafts only, for the project");
            int lines = host.Read(u => u.Set<PurchaseRequestLine>().Count(l => l.PurchaseRequest.ProjectBaselineId != null));
            Assert.AreEqual(2, lines, "one line per short item");
            Assert.AreEqual(30.6m, host.Read(u => u.Set<PurchaseRequestLine>().Single(l => l.ItemId == pr.ScrewItem).Quantity));
            Assert.AreEqual(7.5m, host.Read(u => u.Set<PurchaseRequestLine>().Single(l => l.ItemId == pr.BoltItem).Quantity));
            Assert.IsTrue(host.Read(u => u.Set<RecordAttachment>().Any(a => a.DocumentId == bomDoc && a.OwnerType == AttachmentOwnerType.PurchaseRequest && a.RemovedAt == null)), "the BOM document is linked to the request");
            Assert.IsTrue(host.Read(u => u.Set<RecordAttachment>().Any(a => a.DocumentId == drawing && a.OwnerType == AttachmentOwnerType.PurchaseRequestLine && a.RemovedAt == null)), "the drawing is linked to the line");

            // what the planner sees on the request: the BOM document by link (the owner's own panel lists links as items)
            int prId = requests.Single().Id;
            var panel = await Run(host, Plan1, p => p.GetRequiredService<IAttachmentService>().GetPanelAsync(AttachmentOwnerType.PurchaseRequest, prId, Ct));
            Assert.IsTrue(panel.Success, panel.Message);
            Assert.AreEqual(1, panel.Data.Items.Count(i => !i.Voided), "panel items: " + string.Join(",", panel.Data.Items.Select(i => i.Title)) + " propagated " + panel.Data.Propagated.Count);

            // the same run again changes nothing: one request per group, no new rows
            var again = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct));
            Assert.IsTrue(again.Success, again.Message);
            Assert.AreEqual(0, again.Data.Created);
            Assert.AreEqual(requests.Count, host.Read(u => u.Set<PurchaseRequest>().Count(r => r.ProjectBaselineId != null && !r.Deleted)));
            Assert.AreEqual(2, host.Read(u => u.Set<PurchaseRequestLine>().Count(l => l.PurchaseRequest.ProjectBaselineId != null)));
            Assert.AreEqual(1, host.Read(u => u.Set<RecordAttachment>().Count(a => a.DocumentId == bomDoc && a.OwnerType == AttachmentOwnerType.PurchaseRequest && a.RemovedAt == null)), "no duplicate link");

            // stock arrives: a new run and the draft is updated, not duplicated
            Stock(host, pr.General, pr.BoltItem, 3m);
            BalanceRunDto run2 = await BalanceAsync(host, pr);
            var updated = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run2.Id, Ct));
            Assert.IsTrue(updated.Success, updated.Message);
            Assert.AreEqual(0, updated.Data.Created);
            Assert.AreEqual(4.5m, host.Read(u => u.Set<PurchaseRequestLine>().Single(l => l.ItemId == pr.BoltItem).Quantity));

            // once planning sends a request it is left alone; the next run counts it as on order and drafts nothing new for it
            host.Write(u => { foreach (var r in u.Set<PurchaseRequest>().Where(r => r.ProjectBaselineId != null).ToList()) r.Status = PurchaseRequestStatus.Submitted; u.SaveChanges(); }, Plan1);
            BalanceRunDto run3 = await BalanceAsync(host, pr);
            Assert.AreEqual(0m, Sum(run3, pr.ScrewItem, l => l.ShortQty));
            var third = await Run(host, Plan1, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run3.Id, Ct));
            Assert.IsTrue(third.Success, third.Message);
            Assert.AreEqual(0, third.Data.Created);
            Assert.AreEqual(requests.Count, host.Read(u => u.Set<PurchaseRequest>().Count(r => r.ProjectBaselineId != null && !r.Deleted)));
            Assert.IsTrue(host.Read(u => u.Set<PurchaseRequest>().Where(r => r.ProjectBaselineId != null).All(r => r.Status == PurchaseRequestStatus.Submitted)));
        }

        [TestMethod]
        public async Task Drafts_NeedPlanning()
        {
            AppHost host = NewHost("draftrole");
            Project pr = await SetUpAsync(host);
            BalanceRunDto run = await BalanceAsync(host, pr);
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IProjectBalanceService>().CreateDraftsAsync(run.Id, Ct))).Success);
        }

        // ------------------------------------------------------------------------------------------ reservations

        private static async Task<ReservationDto> FirstReservationAsync(AppHost host, Project pr)
        {
            var list = await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().GetAsync(new ReservationQuery { WorkOrderId = pr.WorkOrderId, ItemId = pr.ScrewItem }, Ct));
            Assert.IsTrue(list.Success, list.Message);
            return list.Data.First();
        }

        [TestMethod]
        public async Task Reservation_EditNeedsAReason_AndReleaseNeedsAnotherPerson()
        {
            AppHost host = NewHost("resv");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            await BalanceAsync(host, pr);
            ReservationDto r = await FirstReservationAsync(host, pr);
            Assert.IsTrue(r.IsHard, "hard by default");
            Assert.AreEqual(10m, r.Quantity);

            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().UpdateAsync(new UpdateReservationDto { Id = r.Id, Quantity = 8m }, Ct))).Success, "a reason is required");
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IProjectReservationService>().UpdateAsync(new UpdateReservationDto { Id = r.Id, Quantity = 8m, Reason = "x" }, Ct))).Success, "planning only");
            var edited = await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().UpdateAsync(new UpdateReservationDto { Id = r.Id, Quantity = 8m, IsHard = false, Reason = "less is needed now" }, Ct));
            Assert.IsTrue(edited.Success, edited.Message);
            Assert.AreEqual(8m, edited.Data.Quantity);
            Assert.IsFalse(edited.Data.IsHard);
            Assert.AreEqual("less is needed now", edited.Data.Reason);

            var ask = await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().RequestReleaseAsync(new ReservationActionDto { Id = r.Id, Reason = "project changed" }, Ct));
            Assert.IsTrue(ask.Success, ask.Message);
            var self = await Run(host, Plan1, p => p.GetRequiredService<IProjectReservationService>().DecideReleaseAsync(new ReservationActionDto { Id = r.Id, Approve = true }, Ct));
            Assert.IsFalse(self.Success, "the person who asked does not approve");
            var other = await Run(host, Plan2, p => p.GetRequiredService<IProjectReservationService>().DecideReleaseAsync(new ReservationActionDto { Id = r.Id, Approve = true }, Ct));
            Assert.IsTrue(other.Success, other.Message);
            Assert.AreEqual(ReservationStatus.Released, other.Data.Status);
            Assert.AreEqual(8m, other.Data.ReleasedQuantity);
        }

        [TestMethod]
        public async Task Issue_ToAnotherProject_IsRefusedWhileTheStockIsReserved_AndTheOwnerMayIssue()
        {
            AppHost host = NewHost("issue");
            Project pr = await SetUpAsync(host);
            Stock(host, pr.General, pr.ScrewItem, 10m);
            await BalanceAsync(host, pr);   // all 10 are reserved for the project

            InventoryDocument Doc(int workOrderId, decimal quantity) => new InventoryDocument
            {
                Type = InventoryDocumentType.Issue, WorkOrderId = workOrderId,
                Lines = new List<InventoryDocumentLine> { new InventoryDocumentLine { ItemId = pr.ScrewItem, Quantity = quantity } },
            };
            string refusal = await host.As(Plan1, async p => await ProjectReservationRules.CheckIssueAsync(p.GetRequiredService<IUnitOfWork>(), Doc(pr.OtherWorkOrderId, 3m), Ct));
            Assert.IsNotNull(refusal, "the other project may not take reserved stock");
            StringAssert.Contains(refusal, "رزرو");
            string own = await host.As(Plan1, async p => await ProjectReservationRules.CheckIssueAsync(p.GetRequiredService<IUnitOfWork>(), Doc(pr.WorkOrderId, 3m), Ct));
            Assert.IsNull(own, "the project that holds the reservation may issue");
        }
    }
}
