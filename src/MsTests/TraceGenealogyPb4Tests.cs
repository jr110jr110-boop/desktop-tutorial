using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Trace;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.TraceDtos;

namespace MsTests
{
    /// <summary>
    /// PB4 manufacturing genealogy: the links between the lots a production order consumed (kit issues) and what its production receipt made, written by the real warehouse document service when the documents are
    /// confirmed, rebuilt idempotently, and the report of what the backfill cannot link.
    /// </summary>
    [TestClass]
    public class TraceGenealogyPb4Tests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly CancellationToken Ct = CancellationToken.None;

        internal sealed class Scenario
        {
            public int Wh, Wip, Provider, Contract, WorkOrder;
            public int RmItem, PartItem, StageItem, AsmItem, ScrewItem;
            public int LotA, LotB;
        }

        internal static AppHost NewHost(string name) => new AppHost("Pb4_" + name + "_" + Guid.NewGuid().ToString("N"));

        internal static T Seed<T>(AppHost host, T entity) where T : class
        {
            host.Write(uow => { AppHost.Add(uow, entity); uow.SaveChanges(); });
            return entity;
        }

        /// <summary>Warehouses, items (raw material by lot, a part by lot, its stage item, an assembly by serial, an untracked screw), two raw-material lots with their heat numbers and stock, and one work order.</summary>
        internal static Scenario SeedBase(AppHost host)
        {
            var s = new Scenario();
            s.Wh = Seed(host, new Warehouse { Code = "W-RAW", Title = "انبار مواد", Kind = WarehouseKind.RawMaterial }).Id;
            s.Wip = Seed(host, new Warehouse { Code = "W-WIP", Title = "کار در جریان", Kind = WarehouseKind.WorkInProgress }).Id;
            s.Provider = Seed(host, new Entities.Model.NasbNiro.Providers.Provider { Name = "تامین‌کننده نمونه الف" }).Id;
            s.Contract = Seed(host, new ContractEntity { Code = "C-401", Title = "قرارداد نمونه", Employer = "کارفرمای نمونه" }).Id;
            s.WorkOrder = Seed(host, new WorkOrder { Code = "WO-401", Title = "دستور ساخت نمونه", ContractId = s.Contract, Count = 2 }).Id;
            s.RmItem = Seed(host, new InventoryItem { Code = "RM-STEEL", Title = "میلگرد فولادی", Unit = "kg", TrackingMode = ItemTrackingMode.Lot }).Id;
            s.PartItem = Seed(host, new InventoryItem { Code = "99.22.01.001.2", Title = "شفت نمونه", Unit = "عدد", TrackingMode = ItemTrackingMode.Lot, PartNumber = "99.22.01.001.2" }).Id;
            s.StageItem = Seed(host, new InventoryItem { Code = "99.22.01.001.2-RL", Title = "شفت نمونه - تراش خشن", Unit = "عدد", TrackingMode = ItemTrackingMode.Lot, PartNumber = "99.22.01.001.2", StageCode2 = "RL", PartItemId = s.PartItem }).Id;
            s.AsmItem = Seed(host, new InventoryItem { Code = "99.22.00.000.2", Title = "مجموعه نمونه", Unit = "عدد", TrackingMode = ItemTrackingMode.Serial, PartNumber = "99.22.00.000.2" }).Id;
            s.ScrewItem = Seed(host, new InventoryItem { Code = "SCREW-M8", Title = "پیچ", Unit = "عدد", TrackingMode = ItemTrackingMode.None }).Id;
            s.LotA = Seed(host, new InventoryLot { ItemId = s.RmItem, LotNumber = "LOT-A", HeatNumber = "H-1001", CertificateNumber = "CERT-1001", ProviderId = s.Provider }).Id;
            s.LotB = Seed(host, new InventoryLot { ItemId = s.RmItem, LotNumber = "LOT-B", HeatNumber = "H-2002", CertificateNumber = "CERT-2002", ProviderId = s.Provider }).Id;
            Seed(host, new StockBalance { ItemId = s.RmItem, WarehouseId = s.Wh, LotId = s.LotA, Quantity = 100 });
            Seed(host, new StockBalance { ItemId = s.RmItem, WarehouseId = s.Wh, LotId = s.LotB, Quantity = 100 });
            Seed(host, new StockBalance { ItemId = s.ScrewItem, WarehouseId = s.Wh, Quantity = 100 });
            return s;
        }

        /// <summary>A released production order with an approved kit (material request) for the given components and two operations.</summary>
        internal static (int Order, int Kit, int Op1, int Op2) SeedOrder(AppHost host, Scenario s, string number, int itemId, decimal quantity, params (int ItemId, decimal Qty)[] components)
        {
            var kit = new MaterialRequest { Number = "MR-" + number, Status = MaterialRequestStatus.Approved, RequestDate = DateTime.Today, WorkOrderId = s.WorkOrder, Lines = new List<MaterialRequestLine>() };
            int row = 1;
            foreach (var c in components) kit.Lines.Add(new MaterialRequestLine { RowNumber = row++, ItemId = c.ItemId, Quantity = c.Qty });
            Seed(host, kit);
            var order = Seed(host, new MfProductionOrder { Number = number, WorkOrderId = s.WorkOrder, ContractId = s.Contract, ItemId = itemId, Quantity = quantity, Unit = "عدد", Status = MfOrderStatus.Completed, KitRequestId = kit.Id });
            var op1 = Seed(host, new MfOperation { OrderId = order.Id, Sequence = 20, Title = "تراش", Status = MfOperationStatus.Done, GoodQty = quantity });
            var op0 = Seed(host, new MfOperation { OrderId = order.Id, Sequence = 10, Title = "برش", Status = MfOperationStatus.Done, GoodQty = quantity });
            return (order.Id, kit.Id, op0.Id, op1.Id);
        }

        internal static Task<ResponseDto<DocumentDto>> IssueAsync(AppHost host, Scenario s, int kit, params (int ItemId, decimal Qty, int? LotId)[] lines) =>
            host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.Issue, DocumentDate = DateTime.Today, WarehouseId = s.Wh, WorkOrderId = s.WorkOrder, ContractId = s.Contract, MaterialRequestId = kit,
                    Lines = lines.Select(l => new SaveDocumentLineDto { ItemId = l.ItemId, Quantity = l.Qty, LotId = l.LotId }).ToList(),
                }, Ct);
                Assert.IsTrue(saved.Success, "issue draft: " + saved.Message);
                return await svc.ConfirmAsync(saved.Data.Id, Ct);
            });

        /// <summary>Draft and confirm the production receipt of an order; the order points at it (the production flow does the same).</summary>
        internal static Task<ResponseDto<DocumentDto>> ReceiptAsync(AppHost host, Scenario s, int orderId, string orderNumber, int itemId, decimal qty, List<string> serials = null) =>
            host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var line = new SaveDocumentLineDto { ItemId = itemId, Quantity = qty };
                if (serials == null) line.LotNumber = orderNumber; else line.SerialNumbers = serials;
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.ProductionReceipt, DocumentDate = DateTime.Today, WarehouseId = s.Wip, WorkOrderId = s.WorkOrder, ContractId = s.Contract, ReferenceNumber = orderNumber,
                    Lines = new List<SaveDocumentLineDto> { line },
                }, Ct);
                Assert.IsTrue(saved.Success, "receipt draft: " + saved.Message);
                var uow = p.GetRequiredService<IUnitOfWork>();
                var order = uow.Set<MfProductionOrder>().Single(o => o.Id == orderId);
                order.ProductionReceiptId = saved.Data.Id;
                uow.SaveChanges();
                return await svc.ConfirmAsync(saved.Data.Id, Ct);
            });

        internal static List<GenealogyLink> Links(AppHost host, int orderId, bool includeDeleted = false) =>
            host.Read(u => u.Set<GenealogyLink>().Where(l => l.ProductionOrderId == orderId && (includeDeleted || !l.Deleted)).OrderBy(l => l.Id).ToList());

        // ------------------------------------------------------------------------------------------ links from issue + receipt

        [TestMethod]
        public async Task Confirming_The_Receipt_Links_Every_Issued_Lot_To_The_Output_Lot_With_Quantity_Order_And_First_Operation()
        {
            AppHost host = NewHost("links");
            Scenario s = SeedBase(host);
            var o = SeedOrder(host, s, "PO-4001", s.PartItem, 10, (s.RmItem, 10));
            var issue = await IssueAsync(host, s, o.Kit, (s.RmItem, 6, s.LotA), (s.RmItem, 4, s.LotB));
            Assert.IsTrue(issue.Success, issue.Message);
            Assert.AreEqual(0, Links(host, o.Order).Count, "nothing is linked before the receipt: the output does not exist yet");

            var receipt = await ReceiptAsync(host, s, o.Order, "PO-4001", s.PartItem, 10);
            Assert.IsTrue(receipt.Success, receipt.Message);

            List<GenealogyLink> links = Links(host, o.Order);
            Assert.AreEqual(2, links.Count, "one link per input lot");
            int outLot = host.Read(u => u.Set<InventoryLot>().Single(l => l.ItemId == s.PartItem && l.LotNumber == "PO-4001").Id);
            Assert.IsTrue(links.All(l => l.OutputLotId == outLot && l.OutputItemId == s.PartItem && l.OutputSerialUnitId == null), "the output is the lot of the production receipt (named after the order)");
            Assert.AreEqual(6m, links.Single(l => l.InputLotId == s.LotA).Quantity);
            Assert.AreEqual(4m, links.Single(l => l.InputLotId == s.LotB).Quantity);
            Assert.IsTrue(links.All(l => l.OperationId == o.Op1), "the kit feeds the first operation of the order (sequence 10)");
            Assert.IsTrue(links.All(l => l.IssueDocumentId == issue.Data.Id && l.ReceiptDocumentId == receipt.Data.Id && !l.IsShared && l.Source == GenealogySource.Live));
            Assert.IsTrue(links.All(l => l.InputItemId == s.RmItem));
        }

        [TestMethod]
        public async Task Rebuild_Changes_Nothing_When_The_Documents_Did_Not_Change_And_A_Cancelled_Receipt_Removes_The_Links()
        {
            AppHost host = NewHost("rebuild");
            Scenario s = SeedBase(host);
            var o = SeedOrder(host, s, "PO-4002", s.PartItem, 5, (s.RmItem, 5));
            Assert.IsTrue((await IssueAsync(host, s, o.Kit, (s.RmItem, 5, s.LotA))).Success);
            var receipt = await ReceiptAsync(host, s, o.Order, "PO-4002", s.PartItem, 5);
            Assert.AreEqual(1, Links(host, o.Order).Count);

            var again = await host.As(Pmo, p => p.GetRequiredService<IGenealogyService>().RebuildOrderAsync(o.Order, Ct));
            Assert.IsTrue(again.Success, again.Message);
            Assert.AreEqual(0, again.Data.LinksCreated + again.Data.LinksUpdated + again.Data.LinksRemoved, "idempotent: running it again changes nothing");
            Assert.AreEqual(1, Links(host, o.Order).Count);

            var cancel = await host.As(Pmo, p => p.GetRequiredService<IInventoryDocumentService>().CancelAsync(receipt.Data.Id, new CancelDocumentDto { Reason = "اشتباه در ثبت" }, Ct));
            Assert.IsTrue(cancel.Success, cancel.Message);
            Assert.AreEqual(0, Links(host, o.Order).Count, "a cancelled receipt leaves no genealogy");
            Assert.AreEqual(1, Links(host, o.Order, true).Count(l => l.Deleted), "the row is marked deleted, not erased");
        }

        [TestMethod]
        public async Task An_Issue_Confirmed_After_The_Receipt_Is_Added_To_The_Links()
        {
            AppHost host = NewHost("late");
            Scenario s = SeedBase(host);
            var o = SeedOrder(host, s, "PO-4003", s.PartItem, 8, (s.RmItem, 8));
            Assert.IsTrue((await IssueAsync(host, s, o.Kit, (s.RmItem, 5, s.LotA))).Success);
            Assert.IsTrue((await ReceiptAsync(host, s, o.Order, "PO-4003", s.PartItem, 8)).Success);
            Assert.AreEqual(1, Links(host, o.Order).Count);

            Assert.IsTrue((await IssueAsync(host, s, o.Kit, (s.RmItem, 3, s.LotB))).Success, "the rest of the kit is issued late");
            List<GenealogyLink> links = Links(host, o.Order);
            Assert.AreEqual(2, links.Count);
            Assert.AreEqual(3m, links.Single(l => l.InputLotId == s.LotB).Quantity);
        }

        [TestMethod]
        public async Task Serial_Outputs_Share_Each_Input_Equally_And_Are_Marked_Shared()
        {
            AppHost host = NewHost("serial");
            Scenario s = SeedBase(host);
            var o = SeedOrder(host, s, "PO-4004", s.AsmItem, 2, (s.RmItem, 10));
            Assert.IsTrue((await IssueAsync(host, s, o.Kit, (s.RmItem, 10, s.LotA))).Success);
            var receipt = await ReceiptAsync(host, s, o.Order, "PO-4004", s.AsmItem, 2, new List<string> { "SN-4004-1", "SN-4004-2" });
            Assert.IsTrue(receipt.Success, receipt.Message);

            List<GenealogyLink> links = Links(host, o.Order);
            Assert.AreEqual(2, links.Count, "one input lot x two output serial pieces");
            Assert.IsTrue(links.All(l => l.OutputSerialUnitId.HasValue && l.OutputLotId == null && l.IsShared && l.Quantity == 5m));
            Assert.AreEqual(2, links.Select(l => l.OutputSerialUnitId).Distinct().Count());
        }

        [TestMethod]
        public async Task Backfill_Links_Finished_Orders_And_Reports_What_It_Could_Not_Link()
        {
            AppHost host = NewHost("backfill");
            Scenario s = SeedBase(host);

            // order 1: linked normally, then its links are erased as if the order had been finished before the genealogy existed
            var good = SeedOrder(host, s, "PO-4010", s.PartItem, 4, (s.RmItem, 4));
            Assert.IsTrue((await IssueAsync(host, s, good.Kit, (s.RmItem, 4, s.LotA))).Success);
            Assert.IsTrue((await ReceiptAsync(host, s, good.Order, "PO-4010", s.PartItem, 4)).Success);
            host.Write(u => { foreach (var l in u.Set<GenealogyLink>().ToList()) u.Set<GenealogyLink>().Remove(l); u.SaveChanges(); });
            Assert.AreEqual(0, Links(host, good.Order, true).Count);

            // order 2: a receipt but no kit at all (the issue was made without a reference to the order)
            var noKit = SeedOrder(host, s, "PO-4011", s.PartItem, 2, (s.RmItem, 2));
            var noKitReceipt = await ReceiptAsync(host, s, noKit.Order, "PO-4011", s.PartItem, 2);
            Assert.IsTrue(noKitReceipt.Success, noKitReceipt.Message);
            host.Write(u => { u.Set<MfProductionOrder>().Single(x => x.Id == noKit.Order).KitRequestId = null; u.SaveChanges(); });

            // order 3: the receipt is still a draft
            var draft = SeedOrder(host, s, "PO-4012", s.PartItem, 2, (s.RmItem, 2));
            host.Write(u =>
            {
                var d = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.ProductionReceipt, Status = InventoryDocumentStatus.Draft, WarehouseId = s.Wip, DocumentDate = DateTime.Today });
                u.SaveChanges();
                u.Set<MfProductionOrder>().Single(x => x.Id == draft.Order).ProductionReceiptId = d.Id;
                u.SaveChanges();
            });

            // order 4: the kit's material is an untracked screw (no lot, no serial)
            var screw = SeedOrder(host, s, "PO-4013", s.PartItem, 3, (s.ScrewItem, 6));
            Assert.IsTrue((await IssueAsync(host, s, screw.Kit, (s.ScrewItem, 6, null))).Success);
            Assert.IsTrue((await ReceiptAsync(host, s, screw.Order, "PO-4013", s.PartItem, 3)).Success);
            host.Write(u => { foreach (var l in u.Set<GenealogyLink>().Where(l => l.ProductionOrderId == screw.Order).ToList()) u.Set<GenealogyLink>().Remove(l); u.SaveChanges(); });

            var report = await host.As(Pmo, p => p.GetRequiredService<IGenealogyService>().BackfillAsync(Ct));
            Assert.IsTrue(report.Success, report.Message);
            GenealogySyncDto r = report.Data;
            Assert.AreEqual(4, r.OrdersSeen, "the four orders that have a production receipt");
            Assert.AreEqual(2, r.OrdersLinked, "the normal order and the screw order");
            Assert.AreEqual(2, r.LinksCreated);
            Assert.AreEqual(1, r.OrdersWithoutKit);
            Assert.AreEqual(1, r.OrdersReceiptNotConfirmed);
            Assert.AreEqual(1, r.InputsWithoutTracking);
            Assert.IsTrue(r.Notes.Count >= 4, "every reason is explained");
            Assert.AreEqual(GenealogySource.Backfill, Links(host, good.Order).Single().Source);
            var screwLink = Links(host, screw.Order).Single();
            Assert.IsNull(screwLink.InputLotId);
            Assert.AreEqual(s.ScrewItem, screwLink.InputItemId, "an untracked input is linked at item level only");

            var second = await host.As(Pmo, p => p.GetRequiredService<IGenealogyService>().BackfillAsync(Ct));
            Assert.AreEqual(0, second.Data.LinksCreated + second.Data.LinksUpdated + second.Data.LinksRemoved, "the backfill can be run twice");
        }

        [TestMethod]
        public async Task Only_The_Admin_Roles_Rebuild_And_An_Ai_Seat_Never_Does()
        {
            AppHost host = NewHost("roles");
            Scenario s = SeedBase(host);
            var o = SeedOrder(host, s, "PO-4020", s.PartItem, 1, (s.RmItem, 1));
            var attrs = typeof(WebApi.Controllers.TraceCenterController).GetMethod("RebuildAsync").GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
            Assert.AreEqual(TraceRoles.Admin, attrs.Roles);
            Assert.IsFalse(TraceRoles.Admin.Split(',').Contains("WAREHOUSE"), "the storekeeper who confirmed the documents does not rebuild the links");

            var ai = new Services.NasbNiro.Ai.AiActingUser { UserId = 99, UserName = "ai.planner", DisplayName = "ai", Roles = new[] { "PLANNING" } };
            var refused = await host.As(AppHost.Principal(99, "ai.planner", "PLANNING"), p =>
                Services.NasbNiro.Ai.AiActing.RunAsync(p.GetRequiredService<IServiceScopeFactory>(), ai, "TEST", async sp => await sp.GetRequiredService<IGenealogyService>().RebuildOrderAsync(o.Order, Ct)));
            Assert.IsFalse(refused.Success, "an AI seat does not rebuild the genealogy");
            var backfill = await host.As(AppHost.Principal(99, "ai.planner", "PLANNING"), p =>
                Services.NasbNiro.Ai.AiActing.RunAsync(p.GetRequiredService<IServiceScopeFactory>(), ai, "TEST", async sp => await sp.GetRequiredService<IGenealogyService>().BackfillAsync(Ct)));
            Assert.IsFalse(backfill.Success);
        }
    }
}
