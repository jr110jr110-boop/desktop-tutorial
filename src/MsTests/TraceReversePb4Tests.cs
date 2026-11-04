using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Trace;
using ViewModels.NasbNiro.TraceDtos;

namespace MsTests
{
    /// <summary>PB4 reverse trace (where used across levels), the product tree of a lot with its purchase chain, price masking and the query count that must not grow with the data.</summary>
    [TestClass]
    public class TraceReversePb4Tests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(2, "qc.one", "QC");
        private static readonly ClaimsPrincipal Fin = AppHost.Principal(3, "fin.one", "FINANCIAL");
        private static readonly CancellationToken Ct = CancellationToken.None;

        private static T Seed<T>(AppHost host, T entity) where T : class
        {
            host.Write(uow => { AppHost.Add(uow, entity); uow.SaveChanges(); });
            return entity;
        }

        private sealed class Chain
        {
            public TraceGenealogyPb4Tests.Scenario S;
            public int OrderId, PartLot, StageLot, Assembly, DeliveryDoc, Pr, Po, ReceiptDoc;
        }

        /// <summary>
        /// Heat H-1001 (lot A) was bought on PR-1 / PO-1 / receipt R-1, consumed by order PO-4001 (part lot), whose stage lot fed order PO-4002 (a second part lot),
        /// which was installed in the assembly serial SN-1, delivered to the customer. Lot B (heat H-2002) went into a separate part lot and nowhere else.
        /// </summary>
        private static Chain SeedChain(AppHost host)
        {
            var c = new Chain { S = TraceGenealogyPb4Tests.SeedBase(host) };
            var s = c.S;
            var o1 = TraceGenealogyPb4Tests.SeedOrder(host, s, "PO-4001", s.StageItem, 10, (s.RmItem, 10));
            var o2 = TraceGenealogyPb4Tests.SeedOrder(host, s, "PO-4002", s.PartItem, 10, (s.StageItem, 10));
            var o3 = TraceGenealogyPb4Tests.SeedOrder(host, s, "PO-4003", s.PartItem, 5, (s.RmItem, 5));
            c.OrderId = o1.Order;
            int stageLot = Seed(host, new InventoryLot { ItemId = s.StageItem, LotNumber = "PO-4001" }).Id;
            int partLot = Seed(host, new InventoryLot { ItemId = s.PartItem, LotNumber = "PO-4002" }).Id;
            int otherLot = Seed(host, new InventoryLot { ItemId = s.PartItem, LotNumber = "PO-4003" }).Id;
            c.StageLot = stageLot; c.PartLot = partLot;
            int receipt = Seed(host, new InventoryDocument { Number = "R-0001", Type = InventoryDocumentType.ProductionReceipt, Status = InventoryDocumentStatus.Confirmed, DocumentDate = DateTime.Today, WarehouseId = s.Wip }).Id;
            void Link(int order, int outItem, int outLot, int inItem, int inLot, decimal q) =>
                Seed(host, new GenealogyLink { ProductionOrderId = order, OutputItemId = outItem, OutputLotId = outLot, InputItemId = inItem, InputLotId = inLot, Quantity = q, ReceiptDocumentId = receipt, LinkDate = DateTime.Today, Source = GenealogySource.Live });
            Link(o1.Order, s.StageItem, stageLot, s.RmItem, s.LotA, 10);
            Link(o2.Order, s.PartItem, partLot, s.StageItem, stageLot, 10);
            Link(o3.Order, s.PartItem, otherLot, s.RmItem, s.LotB, 5);
            int sn = Seed(host, new SerialUnit { ItemId = s.AsmItem, SerialNumber = "SN-1", Status = SerialUnitStatus.Delivered, WorkOrderId = s.WorkOrder }).Id;
            c.Assembly = sn;
            Seed(host, new AssemblyLink { ParentSerialUnitId = sn, ChildLotId = partLot, Quantity = 1, InstalledAt = DateTimeOffset.Now, InstalledByUserId = 1 });
            int delivery = Seed(host, new InventoryDocument { Number = "D-0001", Type = InventoryDocumentType.CustomerDelivery, Status = InventoryDocumentStatus.Confirmed, DocumentDate = DateTime.Today, WarehouseId = s.Wh, WorkOrderId = s.WorkOrder, ContractId = s.Contract }).Id;
            c.DeliveryDoc = delivery;
            Seed(host, new StockTransaction { ItemId = s.AsmItem, WarehouseId = s.Wh, SerialUnitId = sn, Quantity = -1, DocumentId = delivery, TransactionDate = DateTime.Today, CreatedAt = DateTimeOffset.Now, CreatedByUserId = 1 });

            // the purchase chain of lot A: request -> order -> receipt (with a price) ; the request carries a drawing and the order an invoice
            var pr = Seed(host, new PurchaseRequest { Number = "PR-0001", Status = PurchaseRequestStatus.Approved, RequestDate = DateTime.Today, Lines = new List<PurchaseRequestLine>() });
            var prLine = Seed(host, new PurchaseRequestLine { PurchaseRequestId = pr.Id, RowNumber = 1, ItemId = s.RmItem, Quantity = 100, Unit = "kg" });
            var po = Seed(host, new PurchaseOrder { Number = "PUR-0001", Status = PurchaseOrderStatus.Approved, OrderDate = DateTime.Today, ProviderId = s.Provider });
            var poLine = Seed(host, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, RequestLineId = prLine.Id, ItemId = s.RmItem, Quantity = 100, Unit = "kg", UnitPrice = 777000 });
            int pdoc = Seed(host, new InventoryDocument { Number = "RC-0001", Type = InventoryDocumentType.PurchaseReceipt, Status = InventoryDocumentStatus.Confirmed, DocumentDate = DateTime.Today, WarehouseId = s.Wh, ProviderId = s.Provider, PurchaseOrderId = po.Id }).Id;
            Seed(host, new InventoryDocumentLine { DocumentId = pdoc, ItemId = s.RmItem, Quantity = 100, LotId = s.LotA, UnitPrice = 777000, PurchaseOrderLineId = poLine.Id });
            c.Pr = pr.Id; c.Po = po.Id; c.ReceiptDoc = pdoc;
            AttachTo(host, AttachmentOwnerType.PurchaseRequest, pr.Id, AttachmentKind.Drawing, "نقشه نمونه");
            AttachTo(host, AttachmentOwnerType.PurchaseOrder, po.Id, AttachmentKind.Invoice, "فاکتور نمونه");
            return c;
        }

        private static void AttachTo(AppHost host, AttachmentOwnerType type, int ownerId, AttachmentKind kind, string title)
        {
            host.Write(uow =>
            {
                var d = new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + ".pdf", ".pdf").SetUploader(1).SetName(title).SetCode("T").SetDescription("d");
                AppHost.Add(uow, d);
                uow.SaveChanges();
                AppHost.Add(uow, new RecordAttachment { OwnerType = type, OwnerId = ownerId, DocumentId = d.Id, Kind = kind, Title = title, FileName = title + ".pdf", UploadedByUserId = 1, UploadedAt = DateTimeOffset.Now });
                uow.SaveChanges();
            });
        }

        private static Task<ResponseDto<ReverseTraceDto>> Reverse(AppHost host, ClaimsPrincipal who, ReverseTraceQuery q) =>
            host.As(who, p => p.GetRequiredService<ITraceCenterService>().ReverseAsync(q, Ct));

        private static Task<ResponseDto<TraceTreeDto>> Tree(AppHost host, ClaimsPrincipal who, TraceQuery q) =>
            host.As(who, p => p.GetRequiredService<ITraceCenterService>().TreeAsync(q, Ct));

        private static IEnumerable<TraceNodeDto> Flatten(TraceNodeDto n)
        {
            yield return n;
            foreach (var c in n.Children) foreach (var x in Flatten(c)) yield return x;
        }

        // ------------------------------------------------------------------------------------------ reverse trace

        [TestMethod]
        public async Task Reverse_From_A_Heat_Number_Reaches_Every_Level_To_The_Serial_The_Project_And_The_Delivery()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("rev");
            Chain c = SeedChain(host);
            var r = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "H-1001" });
            Assert.IsTrue(r.Success, r.Message);
            ReverseTraceDto d = r.Data;
            Assert.AreEqual(1, d.Roots.Count);
            Assert.AreEqual(3, d.Nodes.Count, "stage lot, part lot, assembly serial; lot B's part lot is not reached");
            Assert.IsTrue(d.Nodes.Any(n => n.LotNumber == "PO-4001"));
            Assert.IsTrue(d.Nodes.Any(n => n.LotNumber == "PO-4002"));
            Assert.IsFalse(d.Nodes.Any(n => n.LotNumber == "PO-4003"));
            Assert.AreEqual(3, d.Nodes.Single(n => n.SerialNumber == "SN-1").Level, "three levels away from the raw material");
            Assert.AreEqual(1, d.Terminals.Count);
            Assert.AreEqual("SN-1", d.Terminals[0].SerialNumber);
            Assert.AreEqual(3, d.Items.Count, "stage item, part item, assembly item");
            Assert.AreEqual(1, d.Projects.Count);
            Assert.AreEqual("WO-401", d.Projects[0].WorkOrderCode);
            Assert.AreEqual("کارفرمای نمونه", d.Projects[0].Customer);
            Assert.AreEqual(1, d.Deliveries.Count);
            Assert.AreEqual("D-0001", d.Deliveries[0].DocumentNumber);
            Assert.AreEqual(3, d.Counts.Nodes);
            Assert.AreEqual(1, d.Counts.Deliveries);
            Assert.IsFalse(d.Truncated);
        }

        [TestMethod]
        public async Task Reverse_From_The_Supplier_The_Purchase_Order_And_The_Purchase_Request_Gives_The_Same_Set()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("rev2");
            Chain c = SeedChain(host);
            // lot B has no purchase receipt in this data, so only lot A's chain is reachable from the order and the request
            var byPo = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "PUR-0001", Kind = "po" });
            var byPr = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "PR-0001", Kind = "pr" });
            Assert.IsTrue(byPo.Success, byPo.Message);
            Assert.IsTrue(byPr.Success, byPr.Message);
            Assert.AreEqual(3, byPo.Data.Nodes.Count);
            Assert.AreEqual(3, byPr.Data.Nodes.Count);
            Assert.AreEqual(1, byPo.Data.Counts.Deliveries);
            Assert.AreEqual(1, byPr.Data.Counts.Deliveries);
            var bySerial = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "SN-1", Kind = "serial" });
            Assert.IsTrue(bySerial.Success, bySerial.Message);
            Assert.AreEqual(0, bySerial.Data.Nodes.Count, "a finished product is not used in anything else");
            Assert.AreEqual(1, bySerial.Data.Deliveries.Count, "but its delivery is shown");
        }

        [TestMethod]
        public async Task Reverse_Depth_Limit_Stops_The_Walk_And_Says_So()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("rev3");
            SeedChain(host);
            var r = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "H-1001", Depth = 1 });
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(1, r.Data.Nodes.Count, "only the first level");
            Assert.IsTrue(r.Data.Truncated);
        }

        [TestMethod]
        public async Task Qc_And_The_Warehouse_Read_The_Trace_But_Are_Not_Price_Readers()
        {
            Assert.IsFalse(TraceRoles.Readers.Split(',').Contains("NOBODY"));
            Assert.IsTrue(TraceRoles.Readers.Split(',').Contains("QC"));
            Assert.IsFalse(TraceRoles.Prices.Split(',').Contains("WAREHOUSE"));
            Assert.IsFalse(TraceRoles.Prices.Split(',').Contains("QC"));
            await Task.CompletedTask;
        }

        // ------------------------------------------------------------------------------------------ product tree, masking, files

        [TestMethod]
        public async Task The_Tree_Of_A_Lot_Shows_Heat_And_Certificate_Through_The_Purchase_Chain_With_The_Request_Drawing_Propagated()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("tree");
            Chain c = SeedChain(host);
            var r = await Tree(host, Pmo, new TraceQuery { Q = "H-1001" });
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.Data.Root);
            List<TraceNodeDto> all = Flatten(r.Data.Root).ToList();
            Assert.IsTrue(all.Any(n => n.Kind == "receipt" && n.Code == "RC-0001"), "the receipt that brought the lot in");
            Assert.IsTrue(all.Any(n => n.Facts.Any(f => (f.Value ?? "").Contains("H-1001"))), "the heat number is shown");
            Assert.IsTrue(all.Any(n => n.Facts.Any(f => (f.Value ?? "").Contains("CERT-1001"))), "the certificate number is shown");
            Assert.IsTrue(all.SelectMany(n => n.Files).Any(f => f.Title == "نقشه نمونه" && f.Propagated), "the request drawing reaches the chain, flagged as propagated");
            Assert.IsFalse(r.Data.PricesMasked);
            Assert.IsTrue(all.SelectMany(n => n.Facts).Any(f => (f.Value ?? "").Contains("777,000")), "finance sees the unit price");
        }

        [TestMethod]
        public async Task Qc_Sees_No_Price_And_No_Invoice_But_Still_Sees_The_Chain_And_The_Drawing()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("mask");
            SeedChain(host);
            var r = await Tree(host, Qc, new TraceQuery { Q = "H-1001" });
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsTrue(r.Data.PricesMasked);
            List<TraceNodeDto> all = Flatten(r.Data.Root).ToList();
            Assert.IsTrue(all.Any(n => n.Kind == "receipt"), "the chain is visible");
            Assert.IsFalse(all.SelectMany(n => n.Facts).Any(f => (f.Label ?? "").Contains("قیمت") || (f.Value ?? "").Contains("777")), "no price in any fact");
            Assert.IsFalse(all.SelectMany(n => n.Files).Any(f => f.Title == "فاکتور نمونه"), "the invoice is not shown to QC");
            Assert.IsTrue(all.SelectMany(n => n.Files).Any(f => f.Title == "نقشه نمونه"), "the technical drawing is");

            var fin = await Tree(host, Fin, new TraceQuery { Q = "H-1001" });
            Assert.IsTrue(fin.Success, fin.Message);
            Assert.IsTrue(Flatten(fin.Data.Root).SelectMany(n => n.Files).Any(f => f.Title == "فاکتور نمونه"), "finance sees the invoice");
        }

        // ------------------------------------------------------------------------------------------ no N+1

        /// <summary>Counts how many times the service asks the context for a table: one call is one query, so the count must not depend on the number of lots.</summary>
        private sealed class CountingUow : IUnitOfWork
        {
            private readonly IUnitOfWork _inner;
            public int Calls;
            public CountingUow(IUnitOfWork inner) { _inner = inner; }
            public DbSet<TEntity> Set<TEntity>() where TEntity : class { Calls++; return _inner.Set<TEntity>(); }
            public void AddRange<TEntity>(IEnumerable<TEntity> entities) where TEntity : class => _inner.AddRange(entities);
            public void RemoveRange<TEntity>(IEnumerable<TEntity> entities) where TEntity : class => _inner.RemoveRange(entities);
            public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => _inner.Entry(entity);
            public void MarkAsChanged<TEntity>(TEntity entity) where TEntity : class => _inner.MarkAsChanged(entity);
            public T GetShadowPropertyValue<T>(object entity, string propertyName) where T : IConvertible => _inner.GetShadowPropertyValue<T>(entity, propertyName);
            public object GetShadowPropertyValue(object entity, string propertyName) => _inner.GetShadowPropertyValue(entity, propertyName);
            public void ExecuteSqlCommand(string query) => _inner.ExecuteSqlCommand(query);
            public void ExecuteSqlCommand(string query, params object[] parameters) => _inner.ExecuteSqlCommand(query, parameters);
            public int SaveChanges(bool acceptAllChangesOnSuccess) => _inner.SaveChanges(acceptAllChangesOnSuccess);
            public int SaveChanges() => _inner.SaveChanges();
            public Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => _inner.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _inner.SaveChangesAsync(cancellationToken);
            public void Dispose() { }
        }

        private static async Task<int> CountedReverse(AppHost host, string q)
        {
            int calls = 0;
            await host.As(Pmo, async p =>
            {
                var counting = new CountingUow(p.GetRequiredService<IUnitOfWork>());
                var svc = new TraceCenterService(counting, p.GetRequiredService<Services.NasbNiro.Inventory.IInventoryUserContext>(), p.GetRequiredService<Services.NasbNiro.Attachments.IAttachmentService>());
                var res = await svc.ReverseAsync(new ReverseTraceQuery { Q = q }, Ct);
                Assert.IsTrue(res.Success, res.Message);
                calls = counting.Calls;
                return 0;
            });
            return calls;
        }

        [TestMethod]
        public async Task The_Reverse_Query_Count_Does_Not_Grow_With_The_Number_Of_Consuming_Lots()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("n1");
            Chain c = SeedChain(host);
            int few = await CountedReverse(host, "H-1001");
            // 25 more part lots, each fed from lot A by its own order
            var s = c.S;
            for (int i = 0; i < 25; i++)
            {
                var o = TraceGenealogyPb4Tests.SeedOrder(host, s, "PX-" + i, s.PartItem, 1, (s.RmItem, 1));
                int lot = Seed(host, new InventoryLot { ItemId = s.PartItem, LotNumber = "PX-" + i }).Id;
                Seed(host, new GenealogyLink { ProductionOrderId = o.Order, OutputItemId = s.PartItem, OutputLotId = lot, InputItemId = s.RmItem, InputLotId = s.LotA, Quantity = 1, ReceiptDocumentId = c.ReceiptDoc, LinkDate = DateTime.Today, Source = GenealogySource.Live });
            }
            int many = await CountedReverse(host, "H-1001");
            Assert.AreEqual(few, many, "same number of table reads for 3 and 28 nodes");
            var r = await Reverse(host, Pmo, new ReverseTraceQuery { Q = "H-1001" });
            Assert.AreEqual(28, r.Data.Nodes.Count);
        }
    }
}
