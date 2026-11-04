using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Trace;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.TraceDtos;

namespace MsTests
{
    /// <summary>PB4 review fixes: returns subtract, several output lots share the inputs by quantity, the hook never breaks a confirmation, the reverse trace reaches material issued outside an order kit and the installed base.</summary>
    [TestClass]
    public class TraceReviewPb4Tests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static T Seed<T>(AppHost host, T entity) where T : class => TraceGenealogyPb4Tests.Seed(host, entity);

        private static Task<ResponseDto<DocumentDto>> ReturnAsync(AppHost host, TraceGenealogyPb4Tests.Scenario s, int kit, int itemId, decimal qty, int lotId) =>
            host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.ProductionReturn, DocumentDate = DateTime.Today, WarehouseId = s.Wh, WorkOrderId = s.WorkOrder, ContractId = s.Contract, MaterialRequestId = kit,
                    Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = itemId, Quantity = qty, LotId = lotId } },
                }, Ct);
                Assert.IsTrue(saved.Success, "return draft: " + saved.Message);
                return await svc.ConfirmAsync(saved.Data.Id, Ct);
            });

        [TestMethod]
        public async Task A_Return_From_Production_Is_Subtracted_From_The_Consumed_Quantity_And_Does_Not_Count_As_An_Issue()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("ret");
            var s = TraceGenealogyPb4Tests.SeedBase(host);
            var o = TraceGenealogyPb4Tests.SeedOrder(host, s, "PO-4101", s.PartItem, 10, (s.RmItem, 10));
            Assert.IsTrue((await TraceGenealogyPb4Tests.IssueAsync(host, s, o.Kit, (s.RmItem, 10, s.LotA))).Success);
            Assert.IsTrue((await TraceGenealogyPb4Tests.ReceiptAsync(host, s, o.Order, "PO-4101", s.PartItem, 10)).Success);
            Assert.AreEqual(10m, TraceGenealogyPb4Tests.Links(host, o.Order).Single().Quantity);

            var ret = await ReturnAsync(host, s, o.Kit, s.RmItem, 4, s.LotA);
            Assert.IsTrue(ret.Success, ret.Message);
            Assert.AreEqual(6m, TraceGenealogyPb4Tests.Links(host, o.Order).Single().Quantity, "issue 10, return 4: the order consumed 6");
            Assert.AreEqual(10m, host.Read(u => u.Set<MaterialRequestLine>().Single(l => l.MaterialRequestId == o.Kit).IssuedQuantity), "a return is not an issue: the issued quantity of the kit is untouched");

            Assert.IsTrue((await ReturnAsync(host, s, o.Kit, s.RmItem, 6, s.LotA)).Success);
            Assert.AreEqual(0, TraceGenealogyPb4Tests.Links(host, o.Order).Count, "everything came back: the lot was not consumed, so there is no link");

            var cancel = await host.As(Pmo, p => p.GetRequiredService<IInventoryDocumentService>().CancelAsync(ret.Data.Id, new CancelDocumentDto { Reason = "اشتباه در ثبت" }, Ct));
            Assert.IsTrue(cancel.Success, cancel.Message);
            Assert.AreEqual(4m, TraceGenealogyPb4Tests.Links(host, o.Order).Single().Quantity, "the 4 returned by the cancelled document count as consumed again");
        }

        [TestMethod]
        public async Task Several_Output_Lots_Of_One_Receipt_Share_Each_Input_In_Proportion_To_What_They_Received()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("split");
            var s = TraceGenealogyPb4Tests.SeedBase(host);
            var o = TraceGenealogyPb4Tests.SeedOrder(host, s, "PO-4102", s.PartItem, 10, (s.RmItem, 10));
            Assert.IsTrue((await TraceGenealogyPb4Tests.IssueAsync(host, s, o.Kit, (s.RmItem, 10, s.LotA))).Success);
            var receipt = await host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.ProductionReceipt, DocumentDate = DateTime.Today, WarehouseId = s.Wip, WorkOrderId = s.WorkOrder, ContractId = s.Contract, ReferenceNumber = "PO-4102",
                    Lines = new List<SaveDocumentLineDto>
                    {
                        new SaveDocumentLineDto { ItemId = s.PartItem, Quantity = 6, LotNumber = "SPLIT-A" },
                        new SaveDocumentLineDto { ItemId = s.PartItem, Quantity = 4, LotNumber = "SPLIT-B" },
                    },
                }, Ct);
                Assert.IsTrue(saved.Success, saved.Message);
                var uow = p.GetRequiredService<IUnitOfWork>();
                uow.Set<Entities.Model.NasbNiro.Production.MfProductionOrder>().Single(x => x.Id == o.Order).ProductionReceiptId = saved.Data.Id;
                uow.SaveChanges();
                return await svc.ConfirmAsync(saved.Data.Id, Ct);
            });
            Assert.IsTrue(receipt.Success, receipt.Message);
            var links = TraceGenealogyPb4Tests.Links(host, o.Order);
            Assert.AreEqual(2, links.Count);
            Assert.AreEqual(10m, links.Sum(l => l.Quantity), "the 10 kg are not counted twice");
            Assert.IsTrue(links.All(l => l.IsShared));
            var lots = host.Read(u => u.Set<InventoryLot>().Where(l => l.ItemId == s.PartItem).ToDictionary(l => l.LotNumber, l => l.Id));
            Assert.AreEqual(6m, links.Single(l => l.OutputLotId == lots["SPLIT-A"]).Quantity);
            Assert.AreEqual(4m, links.Single(l => l.OutputLotId == lots["SPLIT-B"]).Quantity);
        }

        [TestMethod]
        public async Task A_Failure_In_The_Hook_Never_Reaches_The_Caller_And_Leaves_No_Half_Written_Link()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("hook");
            await host.As(Pmo, async p =>
            {
                var counting = new TraceReversePb4Tests.CountingUow(p.GetRequiredService<IUnitOfWork>()) { ThrowOnSet = true };
                await GenealogyBuilder.AfterDocumentAsync(counting, 1, InventoryDocumentType.ProductionReceipt, 1, null, Ct);
                return 0;
            });
            // a cancelled token is the one thing that is passed on
            var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => host.As(Pmo, async p =>
            {
                await GenealogyBuilder.AfterDocumentAsync(p.GetRequiredService<IUnitOfWork>(), 1, InventoryDocumentType.ProductionReceipt, 1, null, cts.Token);
                return 0;
            }));
        }

        [TestMethod]
        public async Task The_Link_Key_Is_A_Filtered_Unique_Index()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("idx");
            var indexes = await host.As(Pmo, p =>
            {
                var model = p.GetRequiredService<ApplicationDbContext>().Model.FindEntityType(typeof(GenealogyLink));
                return Task.FromResult(model.GetIndexes().Select(i => (i.IsUnique, (string)i.FindAnnotation("Relational:Filter")?.Value, i.Properties.Count)).ToList());
            });
            Assert.AreEqual(1, indexes.Count(i => i.IsUnique && i.Item2 == "[Deleted] = 0" && i.Item3 == 7));
        }

        [TestMethod]
        public async Task The_Reverse_Trace_Reaches_Material_Issued_Outside_An_Order_And_The_Installed_Base()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("outside");
            var c = TraceReversePb4Tests.SeedChain(host);
            int issueDoc = Seed(host, new InventoryDocument { Number = "IS-9001", Type = InventoryDocumentType.Issue, Status = InventoryDocumentStatus.Confirmed, DocumentDate = DateTime.Today, WarehouseId = c.S.Wh, WorkOrderId = c.S.WorkOrder }).Id;
            Seed(host, new StockTransaction { ItemId = c.S.RmItem, WarehouseId = c.S.Wh, LotId = c.S.LotA, Quantity = -5, DocumentId = issueDoc, TransactionDate = DateTime.Today, CreatedAt = DateTimeOffset.Now, CreatedByUserId = 1 });
            Seed(host, new SvInstalledUnit { Tag = "T-01", Title = "واحد نصب‌شده", SerialUnitId = c.Assembly, ContractId = c.S.Contract, WorkOrderId = c.S.WorkOrder, HandoverDate = DateTime.Today });

            var r = await host.As(Pmo, p => p.GetRequiredService<ITraceCenterService>().ReverseAsync(new ReverseTraceQuery { Q = "H-1001" }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(1, r.Data.Consumptions.Count);
            Assert.AreEqual("IS-9001", r.Data.Consumptions[0].DocumentNumber);
            Assert.AreEqual(5m, r.Data.Consumptions[0].Quantity);
            Assert.AreEqual(1, r.Data.Installed.Count);
            Assert.AreEqual("SN-1", r.Data.Installed[0].SerialNumber);
            Assert.AreEqual("کارفرمای نمونه", r.Data.Installed[0].Customer);
            Assert.AreEqual("WO-401", r.Data.Installed[0].WorkOrderCode);
            Assert.AreEqual(3, r.Data.Counts.Nodes, "the chain itself is unchanged");
        }

        [TestMethod]
        public async Task A_Part_Tree_Shows_The_Orders_Of_The_Part_Itself()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("rootbuilt");
            var c = TraceReversePb4Tests.SeedChain(host);
            var r = await host.As(Pmo, p => p.GetRequiredService<ITraceCenterService>().TreeAsync(new TraceQuery { Q = "99.22.01.001.2" }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            var all = new List<TraceNodeDto>();
            void Walk(TraceNodeDto n) { all.Add(n); n.Children.ForEach(Walk); }
            Walk(r.Data.Root);
            Assert.IsTrue(all.Any(n => n.Kind == "order" && n.Title.Contains("PO-4002")), "the as-built orders of the part appear");
        }

        [TestMethod]
        public async Task Security_Assembly_And_Construction_See_Codes_But_Not_Supplier_Or_Customer_Names()
        {
            AppHost host = TraceGenealogyPb4Tests.NewHost("names");
            var c = TraceReversePb4Tests.SeedChain(host);
            foreach (string role in new[] { "SECURITY", "ASSEMBLER", "CONSTRUCTION" })
            {
                var who = AppHost.Principal(7, role.ToLowerInvariant() + ".one", role);
                var r = await host.As(who, p => p.GetRequiredService<ITraceCenterService>().ReverseAsync(new ReverseTraceQuery { Q = "H-1001" }, Ct));
                Assert.IsTrue(r.Success, role + ": " + r.Message);
                Assert.IsTrue(r.Data.NamesMasked);
                Assert.AreEqual("محرمانه", r.Data.Roots[0].ProviderName);
                Assert.AreEqual("محرمانه", r.Data.Projects[0].Customer);
                Assert.AreEqual("محرمانه", r.Data.Deliveries[0].Customer);
                Assert.AreEqual("WO-401", r.Data.Projects[0].WorkOrderCode, "codes stay");
                var bySupplier = await host.As(who, p => p.GetRequiredService<ITraceCenterService>().ReverseAsync(new ReverseTraceQuery { Q = "تامین", Kind = "supplier" }, Ct));
                Assert.IsFalse(bySupplier.Success, "searching by supplier name would give the name away");
                var tree = await host.As(who, p => p.GetRequiredService<ITraceCenterService>().TreeAsync(new TraceQuery { Q = "H-1001" }, Ct));
                Assert.IsTrue(tree.Success, tree.Message);
                var nodes = new List<TraceNodeDto>();
                void Walk(TraceNodeDto n) { nodes.Add(n); n.Children.ForEach(Walk); }
                Walk(tree.Data.Root);
                Assert.IsTrue(tree.Data.NamesMasked);
                Assert.IsFalse(nodes.SelectMany(n => n.Facts).Any(f => (f.Value ?? "").Contains("تامین‌کننده نمونه") || (f.Value ?? "").Contains("کارفرمای نمونه")), role + ": no name in the tree");
            }
            var qc = await host.As(AppHost.Principal(2, "qc.one", "QC"), p => p.GetRequiredService<ITraceCenterService>().ReverseAsync(new ReverseTraceQuery { Q = "H-1001" }, Ct));
            Assert.IsFalse(qc.Data.NamesMasked);
            Assert.AreEqual("تامین‌کننده نمونه الف", qc.Data.Roots[0].ProviderName);
            Assert.AreEqual("کارفرمای نمونه", qc.Data.Projects[0].Customer);
        }
    }
}
