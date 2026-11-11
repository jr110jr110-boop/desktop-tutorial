using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Services.NasbNiro.ProductBank;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.WorkOrders;
using ViewModels.NasbNiro.EquipmentAssemblyDtos;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.EquipmentProcessDtos;
using ViewModels.NasbNiro.GeneratedAssembliesDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.PredecessorCheckDtos;
using ViewModels.NasbNiro.WorkOrderDtos;

namespace MsTests
{
    /// <summary>
    /// FX1: the product sub-assemblies page (one query, tree in memory), the work order from a product (topological order, Persian refusal, never a throw
    /// and never an endless loop), the BOM add (validated first, saved in one go), and the read-only predecessor check of the product bank.
    /// </summary>
    [TestClass]
    public class Fx1PredecessorTests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(7, "pmo.user", "PMO");
        private static readonly Lazy<AppHost> SharedHost = new Lazy<AppHost>(() => new AppHost("Fx1_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => SharedHost.Value;

        // ---------------------------------------------------------------------------------------------- helpers

        private static int NewProduct()
        {
            int id = 0;
            H.Write(u => { var e = AppHost.Add(u, new Equipment { Code = "E" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "product" }); u.SaveChanges(); id = e.Id; }, Pmo);
            return id;
        }

        private static int NewAssembly(int equipmentId, string code, int? predecessorId = null, bool deleted = false)
        {
            int id = 0;
            H.Write(u =>
            {
                var a = AppHost.Add(u, new EquipmentAssembly { Code = code, Title = "مونتاژ " + code, EquipmentId = equipmentId });
                u.SaveChanges();
                id = a.Id;
                if (predecessorId.HasValue) SetAssemblyPredecessor(u, a, predecessorId.Value);
                if (deleted) a.Delete();
                u.SaveChanges();
            }, Pmo);
            return id;
        }

        /// <summary>The setter is protected and an in-memory database checks no foreign key: the shadow of the property is written directly (also for ids that do not exist).</summary>
        private static void SetAssemblyPredecessor(IUnitOfWork u, EquipmentAssembly a, int predecessorId) =>
            u.Entry(a).Property(nameof(EquipmentAssembly.PredecessorId)).CurrentValue = predecessorId;

        private static void SetAssemblyPredecessorLater(int assemblyId, int predecessorId) =>
            H.Write(u => { SetAssemblyPredecessor(u, u.Set<EquipmentAssembly>().Single(x => x.Id == assemblyId), predecessorId); u.SaveChanges(); }, Pmo);

        private static int NewBom(int assemblyId, string title, int? predecessorId = null, bool deleted = false)
        {
            int id = 0;
            H.Write(u =>
            {
                var b = AppHost.Add(u, new EquipmentBom { Title = title, EquipmentAssemblyId = assemblyId, PredecessorId = predecessorId, Count = 1 });
                if (deleted) b.Delete();
                u.SaveChanges();
                id = b.Id;
            }, Pmo);
            return id;
        }

        private static void NewProcess(int bomId) =>
            H.Write(u => { AppHost.Add(u, new EquipmentProcess { EquipmentBomId = bomId, Title = "برش", EnterCommodityCode = "C1" }); u.SaveChanges(); }, Pmo);

        private static Task<T> AsPmo<T>(Func<IServiceProvider, Task<T>> call) => H.As(Pmo, call);

        private static void Unfreeze() => SafetyTestSupport.SetProductBank(H, PbSettingKeys.LegacyBomFrozen, "false");

        private static Task<ResponseGeneratedAssembliesDto> Generate(int equipmentId) =>
            AsPmo(p => p.GetRequiredService<IEquipmentService>().GenerateAssembliesFromEquipmentAsync(equipmentId, CancellationToken.None).AsTask());

        private static async Task<List<ResponseEquipmentAssemblyDto>> Tree(int equipmentId)
        {
            ResponseDto<PagedList<ResponseEquipmentAssemblyDto>> r = await AsPmo(p => p.GetRequiredService<IEquipmentAssemblyService>()
                .GetAllResponseEquipmentAssemblyDtoAsync(new EquipmentAssemblyPaginationParams { EquipmentId = equipmentId, IsDeleted = false, PageNumber = 1, PageSize = 50 }, CancellationToken.None).AsTask());
            Assert.IsTrue(r.Success, r.Message);
            return r.Data.ToList();
        }

        private static string Codes(IEnumerable<ResponseEquipmentAssemblyDto> nodes) => string.Join(",", (nodes ?? Enumerable.Empty<ResponseEquipmentAssemblyDto>()).Select(x => x.Code));

        // ---------------------------------------------------------------------------------------------- the tree read

        [TestMethod, Timeout(60000)]
        public async Task ThreeSiblingAssembliesAndOneSubAssembly_GiveTheExactTree()
        {
            int product = NewProduct();
            int a1 = NewAssembly(product, "A1");
            int a2 = NewAssembly(product, "A2");
            int a3 = NewAssembly(product, "A3");
            int b1 = NewAssembly(product, "B1", a2);

            List<ResponseEquipmentAssemblyDto> roots = await Tree(product);

            Assert.AreEqual("A1,A2,A3", Codes(roots));
            Assert.AreEqual("", Codes(roots[0].Children));
            Assert.AreEqual("B1", Codes(roots[1].Children));
            Assert.AreEqual("", Codes(roots[2].Children));
            Assert.AreEqual(a2, roots[1].Children.Single().ParentId);
            Assert.AreEqual("", Codes(roots[1].Children.Single().Children));
            Assert.IsNotNull(roots[0].Children, "an assembly without children has an empty list, not null");
        }

        [TestMethod, Timeout(60000)]
        public async Task SiblingsOnTheSecondLevelAndADeletedChild_AreReadWithoutAParallelQuery()
        {
            int product = NewProduct();
            int root = NewAssembly(product, "R");
            int c1 = NewAssembly(product, "C1", root);
            NewAssembly(product, "C2", root);
            NewAssembly(product, "C3", root);
            NewAssembly(product, "D1", c1);
            NewAssembly(product, "D2", c1);
            NewAssembly(product, "GONE", root, deleted: true);
            int otherProduct = NewProduct();
            NewAssembly(otherProduct, "OTHER");

            List<ResponseEquipmentAssemblyDto> roots = await Tree(product);

            Assert.AreEqual("R", Codes(roots));
            Assert.AreEqual("C1,C2,C3", Codes(roots[0].Children));
            Assert.AreEqual("D1,D2", Codes(roots[0].Children.First().Children));
        }

        [TestMethod, Timeout(60000)]
        public async Task AStoredLoopIsNotExpandedAgain_AndTheReadTerminates()
        {
            int product = NewProduct();
            int root = NewAssembly(product, "ROOT");
            int x = NewAssembly(product, "X", root);
            int y = NewAssembly(product, "Y", x);
            SetAssemblyPredecessorLater(x, y);   // X <-> Y is a loop under ROOT? X now hangs below Y, and Y below X: both leave ROOT

            List<ResponseEquipmentAssemblyDto> roots = await Tree(product);

            Assert.AreEqual("ROOT", Codes(roots));
            Assert.AreEqual("", Codes(roots[0].Children), "X and Y only point at each other now, so nothing hangs below ROOT");

            // a loop reachable from a root: P(root of the query) <- Q <- P is cut at the node already on the path
            ResponseDto<ResponseEquipmentAssemblyDto> one = await AsPmo(p => p.GetRequiredService<IEquipmentAssemblyService>()
                .GetResponseEquipmentAssemblyDtoAsync(new GetEquipmentAssemblyDto { Id = x, EquipmentId = product }, CancellationToken.None).AsTask());
            Assert.IsTrue(one.Success, one.Message);
            Assert.AreEqual("Y", Codes(one.Data.Children));
            Assert.AreEqual("", Codes(one.Data.Children.Single().Children), "X is already on the path, so it is not expanded again below Y");
        }

        // ---------------------------------------------------------------------------------------------- work order from a product

        [TestMethod, Timeout(60000)]
        public async Task APredecessorLaterInTheQueue_IsPlacedFirst()
        {
            int product = NewProduct();
            int first = NewAssembly(product, "LATE");          // lowest id, but it follows the one created next
            int second = NewAssembly(product, "EARLY");
            SetAssemblyPredecessorLater(first, second);
            int bomB = NewBom(first, "bom-b");
            int bomA = NewBom(first, "bom-a");
            H.Write(u => { u.Set<EquipmentBom>().Single(b => b.Id == bomB).PredecessorId = bomA; u.SaveChanges(); }, Pmo);

            ResponseGeneratedAssembliesDto result = await Generate(product);

            Assert.IsNull(result.Problem, result.Problem);
            List<Entities.Model.NasbNiro.Contracts.AssemblyEntity> list = result.Assemblies.ToList();
            Assert.AreEqual("EARLY,LATE", string.Join(",", list.Select(a => a.Code)));
            Assert.AreSame(list[0], list[1].Predecessor);
            Assert.AreEqual("bom-a,bom-b", string.Join(",", list[1].Boms.OrderBy(b => b.Title).Select(b => b.Title)));
            Assert.AreEqual("bom-a", list[1].Boms.Single(b => b.Title == "bom-b").Predecessor.Title);
        }

        [TestMethod, Timeout(60000)]
        public async Task ADeletedPredecessor_IsAPersianRefusalNamingTheItem_NoThrow()
        {
            int product = NewProduct();
            int gone = NewAssembly(product, "GONE", deleted: true);
            NewAssembly(product, "WELD", gone);

            ResponseGeneratedAssembliesDto result = await Generate(product);

            Assert.IsTrue(result.EquipmentExists);
            Assert.IsNull(result.Assemblies);
            StringAssert.Contains(result.Problem, "مونتاژ «مونتاژ WELD»");
            StringAssert.Contains(result.Problem, "«مونتاژ GONE»");
            StringAssert.Contains(result.Problem, "حذف شده است");
        }

        [TestMethod, Timeout(60000)]
        public async Task APredecessorOfAnotherProduct_AndAMissingOne_AreRefusedAndTheLoopEnds()
        {
            int product = NewProduct();
            int other = NewProduct();
            int foreign = NewAssembly(other, "FOREIGN");
            NewAssembly(product, "S1", foreign);
            NewAssembly(product, "S2", 987654);   // does not exist at all

            ResponseGeneratedAssembliesDto result = await Generate(product);

            Assert.IsNull(result.Assemblies);
            StringAssert.Contains(result.Problem, "متعلق به محصول دیگری است");
            StringAssert.Contains(result.Problem, "وجود ندارد");
            StringAssert.Contains(result.Problem, "S1");
            StringAssert.Contains(result.Problem, "S2");
        }

        [TestMethod, Timeout(60000)]
        public async Task ABomWithADeletedPredecessor_IsRefusedInPersian()
        {
            int product = NewProduct();
            int assembly = NewAssembly(product, "ASM");
            int gone = NewBom(assembly, "قطعه حذف", deleted: true);
            NewBom(assembly, "قطعه زنده", gone);

            ResponseGeneratedAssembliesDto result = await Generate(product);

            Assert.IsNull(result.Assemblies);
            StringAssert.Contains(result.Problem, "BOM «قطعه زنده»");
            StringAssert.Contains(result.Problem, "«قطعه حذف»");
            StringAssert.Contains(result.Problem, "حذف شده است");
        }

        [TestMethod, Timeout(60000)]
        public async Task ALoopOfPredecessors_IsRefused_AndOnlyTheLoopIsNamed()
        {
            int product = NewProduct();
            int ok = NewAssembly(product, "OK");
            int p = NewAssembly(product, "P");
            int q = NewAssembly(product, "Q", p);
            int below = NewAssembly(product, "BELOW", q);
            SetAssemblyPredecessorLater(p, q);

            ResponseGeneratedAssembliesDto result = await Generate(product);

            Assert.IsNull(result.Assemblies);
            StringAssert.Contains(result.Problem, "حلقه");
            StringAssert.Contains(result.Problem, "«مونتاژ P»");
            StringAssert.Contains(result.Problem, "«مونتاژ Q»");
            Assert.IsFalse(result.Problem.Contains("«مونتاژ OK»"), result.Problem);
            Assert.IsFalse(result.Problem.Contains("«مونتاژ BELOW»"), result.Problem);
        }

        [TestMethod, Timeout(60000)]
        public async Task AddingAWorkOrderFromAProductWithABadPredecessor_AnswersBadRequestWithTheMessage()
        {
            int product = NewProduct();
            int gone = NewAssembly(product, "GONE", deleted: true);
            NewAssembly(product, "WELD", gone);
            int contract = 0;
            H.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract" });
                u.SaveChanges(); contract = c.Id;
                AppHost.Add(u, new ContractEquipment { ContractId = c.Id, EquipmentId = product, Count = 5 });
                u.SaveChanges();
            }, Pmo);

            ResponseDto<ResponseWorkOrderDto> r = await AsPmo(p => p.GetRequiredService<IWorkOrderService>()
                .AddWorkOrderAsync(new AddWorkOrderDto { ContractId = contract, EquipmentId = product, Code = "W1", Title = "دستور", Description = "d", Count = 1 }, CancellationToken.None).AsTask());

            Assert.IsFalse(r.Success);
            StringAssert.Contains(r.Message, "ساخت دستور کار از محصول ممکن نیست");
            StringAssert.Contains(r.Message, "«مونتاژ GONE»");
            Assert.AreEqual(0, H.Read(u => u.Set<WorkOrder>().Count(w => w.EquipmentId == product)), "nothing is saved");
        }

        // ---------------------------------------------------------------------------------------------- BOM add

        private static AddEquipmentBomWithEqueipmentProccessDto BomDto(int assemblyId, int? predecessor = null, List<ResponseEquipmentProcessDto> processes = null) =>
            new AddEquipmentBomWithEqueipmentProccessDto { EquipmentAssemblyId = assemblyId, Title = "ورق", Count = 2, PositionNumber = 1, PredecessorId = predecessor, equipmentProcesses = processes };

        private static Task<ResponseDto<ResponseEquipmentBomDto>> AddBom(AddEquipmentBomWithEqueipmentProccessDto dto) =>
            AsPmo(p => p.GetRequiredService<IEquipmentBomService>().AddEquipmentBomAsync(dto, CancellationToken.None).AsTask());

        private static int BomCount(int assemblyId) => H.Read(u => u.Set<EquipmentBom>().Count(b => b.EquipmentAssemblyId == assemblyId));

        [TestMethod, Timeout(60000)]
        public async Task ABomWithNullProcesses_IsSavedWithoutProcesses_AndNothingThrows()
        {
            Unfreeze();
            int product = NewProduct();
            int assembly = NewAssembly(product, "ASM");

            ResponseDto<ResponseEquipmentBomDto> r = await AddBom(BomDto(assembly, processes: null));

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(1, BomCount(assembly));
            Assert.AreEqual(0, H.Read(u => u.Set<EquipmentProcess>().Count(p => p.EquipmentBomId == r.Data.Id)));
        }

        [TestMethod, Timeout(60000)]
        public async Task ABomWithProcesses_IsSavedTogetherWithThem()
        {
            Unfreeze();
            int product = NewProduct();
            int assembly = NewAssembly(product, "ASM");
            var processes = new List<ResponseEquipmentProcessDto>
            {
                new ResponseEquipmentProcessDto { Title = "برش", EnterCommodityCode = "C1", ExitCommodityCode = "C2", WeightPercent = 40 },
                new ResponseEquipmentProcessDto { Title = "جوش", EnterCommodityCode = "C2", ExitCommodityCode = "C3", WeightPercent = 60 },
            };

            ResponseDto<ResponseEquipmentBomDto> r = await AddBom(BomDto(assembly, processes: processes));

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual("برش,جوش", string.Join(",", H.Read(u => u.Set<EquipmentProcess>().Where(p => p.EquipmentBomId == r.Data.Id).OrderBy(p => p.Id).Select(p => p.Title).ToList())));
        }

        [TestMethod, Timeout(60000)]
        public async Task ABomWithABadPredecessor_IsRefusedAndNothingIsSaved()
        {
            Unfreeze();
            int product = NewProduct();
            int assembly = NewAssembly(product, "ASM");
            int goneBom = NewBom(assembly, "قطعه حذف", deleted: true);
            int otherProduct = NewProduct();
            int otherAssembly = NewAssembly(otherProduct, "OTHER");
            int foreignBom = NewBom(otherAssembly, "قطعه بیگانه");
            int before = BomCount(assembly);
            var processes = new List<ResponseEquipmentProcessDto> { new ResponseEquipmentProcessDto { Title = "برش", EnterCommodityCode = "C1" } };
            int processesBefore = H.Read(u => u.Set<EquipmentProcess>().Count());

            ResponseDto<ResponseEquipmentBomDto> deleted = await AddBom(BomDto(assembly, goneBom, processes));
            ResponseDto<ResponseEquipmentBomDto> foreign = await AddBom(BomDto(assembly, foreignBom, processes));
            ResponseDto<ResponseEquipmentBomDto> missing = await AddBom(BomDto(assembly, 987654, processes));

            Assert.IsFalse(deleted.Success); StringAssert.Contains(deleted.Message, "حذف شده است");
            Assert.IsFalse(foreign.Success); StringAssert.Contains(foreign.Message, "متعلق به محصول دیگری است");
            Assert.IsFalse(missing.Success); StringAssert.Contains(missing.Message, "وجود ندارد");
            Assert.AreEqual(before, BomCount(assembly));
            Assert.AreEqual(processesBefore, H.Read(u => u.Set<EquipmentProcess>().Count()), "no process of a refused BOM is saved");
        }

        [TestMethod, Timeout(60000)]
        public async Task ABomWithAGoodPredecessor_IsSaved_AndAnEditThatClosesALoopIsRefused()
        {
            Unfreeze();
            int product = NewProduct();
            int assembly = NewAssembly(product, "ASM");
            int first = NewBom(assembly, "اول");

            ResponseDto<ResponseEquipmentBomDto> second = await AddBom(BomDto(assembly, first));
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual(first, second.Data.PredecessorId);

            // the edit path: the first line may not get the second as predecessor (it would close a loop)
            ResponseDto<ResponseEquipmentBomDto> loop = await AsPmo(p => p.GetRequiredService<IEquipmentBomService>().UpdateEquipmentBomAsync(
                new UpdateEquipmentBomDto { Id = first, EquipmentAssemblyId = assembly, Title = "اول", Count = 1, PredecessorId = second.Data.Id }, CancellationToken.None).AsTask());
            Assert.IsFalse(loop.Success);
            StringAssert.Contains(loop.Message, "حلقه");
            Assert.IsNull(H.Read(u => u.Set<EquipmentBom>().Single(b => b.Id == first).PredecessorId), "the refused edit changed nothing");
        }

        [TestMethod, Timeout(60000)]
        public async Task DeletingAnAssembly_ClearsThePredecessorOfBomLinesOfOtherAssemblies_SoTheWorkOrderStillWorks()
        {
            int product = NewProduct();
            int asm1 = NewAssembly(product, "ONE");
            int asm2 = NewAssembly(product, "TWO");
            int before = NewBom(asm2, "قطعه دوم");
            int follower = NewBom(asm1, "قطعه اول", before);

            ResponseDto<ResponseEquipmentAssemblyDto> deleted = await AsPmo(p => p.GetRequiredService<IEquipmentAssemblyService>().DeleteEquipmentAssemblyAsync(asm2, CancellationToken.None).AsTask());
            Assert.IsTrue(deleted.Success, deleted.Message);

            Assert.IsNull(H.Read(u => u.Set<EquipmentBom>().Single(b => b.Id == follower).PredecessorId), "the line that followed a deleted line has no predecessor any more");
            ResponseGeneratedAssembliesDto result = await Generate(product);
            Assert.IsNull(result.Problem, result.Problem);
            Assert.AreEqual("ONE", string.Join(",", result.Assemblies.Select(a => a.Code)));
        }

        // ---------------------------------------------------------------------------------------------- the check tool

        [TestMethod, Timeout(60000)]
        public async Task TheCheckListsInvalidPredecessorsAndBomsWithoutProcesses_AndChangesNothing()
        {
            int product = NewProduct();
            int gone = NewAssembly(product, "GONE", deleted: true);
            NewAssembly(product, "WELD", gone);
            int p = NewAssembly(product, "LP");
            int q = NewAssembly(product, "LQ", p);
            SetAssemblyPredecessorLater(p, q);
            int bom = NewBom(p, "بدون فرایند");
            int withProcess = NewBom(q, "با فرایند");
            NewProcess(withProcess);

            int clean = NewProduct();
            int cleanAssembly = NewAssembly(clean, "CLEAN");
            int cleanBom = NewBom(cleanAssembly, "سالم");
            NewProcess(cleanBom);

            int assembliesBefore = H.Read(u => u.Set<EquipmentAssembly>().Count());
            ResponseDto<PredecessorCheckReportDto> r = await AsPmo(s => s.GetRequiredService<IEquipmentPredecessorCheckService>().CheckAsync(CancellationToken.None));

            Assert.IsTrue(r.Success, r.Message);
            List<PredecessorCheckRowDto> mine = r.Data.Rows.Where(x => x.EquipmentId == product).ToList();
            Assert.AreEqual("Cycle,Cycle,Deleted,NoProcesses", string.Join(",", mine.Select(x => x.Problem).OrderBy(x => x, StringComparer.Ordinal)));
            Assert.AreEqual(0, r.Data.Rows.Count(x => x.EquipmentId == clean), "a clean product has no finding");
            Assert.IsTrue(mine.All(x => !string.IsNullOrWhiteSpace(x.Reason) && x.ProposedFix.Contains("تأیید نفر دوم")));
            Assert.AreEqual(r.Data.Rows.Count, r.Data.FindingCount);
            Assert.AreEqual(assembliesBefore, H.Read(u => u.Set<EquipmentAssembly>().Count()));
        }

        // ---------------------------------------------------------------------------------------------- pure helpers

        [TestMethod]
        public void TheOrdering_PlacesPredecessorsFirst_FindsMissingAndOnlyTheNodesOfALoop()
        {
            var items = new[] { (Id: 1, Pred: (int?)3), (Id: 2, Pred: (int?)null), (Id: 3, Pred: (int?)2), (Id: 4, Pred: (int?)99), (Id: 5, Pred: (int?)6), (Id: 6, Pred: (int?)5), (Id: 7, Pred: (int?)5) };

            var result = PredecessorOrdering.Sort(items, x => x.Id, x => x.Pred);

            Assert.AreEqual("2,3,1", string.Join(",", result.Ordered.Select(x => x.Id)));
            Assert.AreEqual("4", string.Join(",", result.MissingPredecessor.Select(x => x.Id)));
            Assert.AreEqual("5,6", string.Join(",", result.OnCycle.Select(x => x.Id)));
            Assert.IsFalse(result.IsClean);
            Assert.IsTrue(PredecessorOrdering.Sort(new[] { (Id: 1, Pred: (int?)null) }, x => x.Id, x => x.Pred).IsClean);
        }

        [TestMethod]
        public void TheTreeBuilder_CutsALoopAtTheNodeOnThePath()
        {
            var nodes = new[] { new Node(1, null), new Node(2, 1), new Node(3, 2), new Node(4, 3) };
            nodes[0].ParentId = 4;   // 1 <- 4 <- 3 <- 2 <- 1 is a loop
            var roots = new[] { new Node(2, 1) };

            List<int> cycles = AssemblyTreeBuilder.AttachChildren(roots, nodes, n => n.Id, n => n.ParentId, (n, c) => n.Children = c.ToList());

            Assert.AreEqual("3", string.Join(",", roots[0].Children.Select(c => c.Id)));
            Assert.AreEqual("4", string.Join(",", roots[0].Children[0].Children.Select(c => c.Id)));
            Assert.AreEqual("1", string.Join(",", roots[0].Children[0].Children[0].Children.Select(c => c.Id)));
            Assert.IsTrue(cycles.Contains(2), "node 2 is already on the path when 1 would be expanded below 4");
        }

        private sealed class Node
        {
            public Node(int id, int? parent) { Id = id; ParentId = parent; }
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public List<Node> Children { get; set; } = new List<Node>();
        }

        [TestMethod]
        public void PagingAddsAnOrderByIdOnlyWhenTheQueryHasNoOrdering()
        {
            IQueryable<Equipment> plain = new[] { new Equipment { Title = "b" }, new Equipment { Title = "a" } }.AsQueryable();
            Assert.IsTrue(PagedList<Equipment>.EnsureOrdered(plain).Expression.ToString().Contains("OrderBy"));

            IQueryable<Equipment> ordered = plain.OrderByDescending(x => x.Title);
            string text = PagedList<Equipment>.EnsureOrdered(ordered).Expression.ToString();
            Assert.IsFalse(text.Contains(".OrderBy(") && !text.Contains("OrderByDescending"), "an ordered query is left as it is: " + text);
            Assert.AreSame(ordered, PagedList<Equipment>.EnsureOrdered(ordered));
        }

        [TestMethod]
        public async Task TheGlobalFilter_AnswersClientAbortWith499_AndARealTimeoutStaysAnError()
        {
            var logger = new CapturingLogger<WebApi.HttpGlobalExceptionFilter>();
            var env = new TestWebHostEnvironment();
            var filter = new WebApi.HttpGlobalExceptionFilter(env, logger);

            var aborted = new System.Threading.CancellationTokenSource();
            var http1 = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = aborted.Token };
            aborted.Cancel();
            var ctx1 = Context(http1, new TaskCanceledException("client closed"));
            filter.OnException(ctx1);
            Assert.AreEqual(499, http1.Response.StatusCode);
            Assert.IsTrue(ctx1.ExceptionHandled);
            Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Information, logger.Levels.Single());

            logger.Levels.Clear();
            var http2 = new Microsoft.AspNetCore.Http.DefaultHttpContext();   // the client is still there: a real timeout
            var ctx2 = Context(http2, new OperationCanceledException("timeout"));
            filter.OnException(ctx2);
            Assert.AreEqual(500, http2.Response.StatusCode);
            Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Error, logger.Levels.Single());
            await Task.CompletedTask;
        }

        private static Microsoft.AspNetCore.Mvc.Filters.ExceptionContext Context(Microsoft.AspNetCore.Http.HttpContext http, Exception ex) =>
            new Microsoft.AspNetCore.Mvc.Filters.ExceptionContext(
                new Microsoft.AspNetCore.Mvc.ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
                new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>()) { Exception = ex };

        private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
        {
            public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new List<Microsoft.Extensions.Logging.LogLevel>();
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Levels.Add(logLevel);
        }
    }
}
