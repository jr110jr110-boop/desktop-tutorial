using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.GoLive;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>Package G with real seeded data: the legacy modes before the cut-over against the strict gates after it, and both adoption workflows.</summary>
    [TestClass]
    public class PackageGFlowTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Dcc = AppHost.Principal(31, "dcc.one", "DCC");
        private static readonly ClaimsPrincipal Reviewer = AppHost.Principal(32, "engineer.one", "ENGINEER");
        private static readonly ClaimsPrincipal Approver = AppHost.Principal(33, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(34, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(41, "eng.maker", "ENGINEER");
        private static readonly ClaimsPrincipal Checker = AppHost.Principal(42, "qa.checker", "QA");
        private static readonly ClaimsPrincipal Inspector = AppHost.Principal(43, "qc.inspector", "QC");
        private static readonly ClaimsPrincipal MakerAndQa = AppHost.Principal(44, "eng.and.qa", "ENGINEER", "QA");
        private static readonly ClaimsPrincipal Prep = AppHost.Principal(21, "eng.prep", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(22, "eng.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(35, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal EngAndPmo = AppHost.Principal(45, "eng.and.pmo", "ENGINEERING", "PMO");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(46, "buyer.one", "COMMERCIAL");

        internal static AppHost NewHost(string name, int cutOverDays)
        {
            var host = new AppHost("PKG_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 21, 22, 31, 32, 33, 34, 35, 41, 42, 43, 44, 45, 46 }) AppHost.Add(u, new User { Id = id, UserName = "pkg.user" + id, Email = "pkg.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            host.WithoutDesignControl();
            // the cut-over instant: in the future = everything that exists now is legacy; in the past = everything created now is new work
            host.Write(u => { AppHost.Add(u, new GlActivation { StartAt = DateTimeOffset.Now.AddDays(cutOverDays) }); u.SaveChanges(); }, Seeder);
            return host;
        }

        private static int NewContract(AppHost host)
        {
            int id = 0;
            host.Write(u => { var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current }); u.SaveChanges(); id = c.Id; }, Seeder);
            return id;
        }

        private static int NewItem(AppHost host, string part)
        {
            int id = 0;
            host.Write(u => { var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "part", PartNumber = part }); u.SaveChanges(); id = i.Id; }, Seeder);
            return id;
        }

        private static async Task<(int Deliverable, int Revision)> DraftDrawing(AppHost host, int contract, string part)
        {
            int doc = 0;
            host.Write(u => { var d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "drawing", Discipline = "Mechanical", DocumentType = "Drawing" }); u.SaveChanges(); doc = d.Id; }, Seeder);
            var link = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = part }, default));
            Assert.IsTrue(link.Success, link.Message);
            var rev = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "first issue" }, default));
            Assert.IsTrue(rev.Success, rev.Message);
            Guid fid = Guid.Empty;
            host.Write(u => { var d = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf"); u.Set<Entities.Model.Document>().Add(d); u.SaveChanges(); fid = d.Id; }, Seeder);
            var f = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().AddFileAsync(new PcRevisionFileAddDto { RevisionId = rev.Data.Id, DocumentId = fid, FileName = "a.pdf", Role = PcRevisionFileRole.Pdf }, default));
            Assert.IsTrue(f.Success, f.Message);
            return (doc, rev.Data.Id);
        }

        /// <summary>A drawing revision that is in force from before the programme had characteristic lists: released directly, with a release date in the past and no list.</summary>
        private static async Task<(int Deliverable, int Revision)> LegacyDrawing(AppHost host, int contract, string part)
        {
            var d = await DraftDrawing(host, contract, part);
            host.Write(u => { var r = u.Set<PcDocumentRevision>().Single(x => x.Id == d.Revision); r.Status = PcDocStatus.Released; r.ReleasedAt = DateTimeOffset.Now.AddDays(-30); u.SaveChanges(); }, Seeder);
            return d;
        }

        private static async Task ReleaseRevision(AppHost host, int revisionId)
        {
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "review"), (Approver, "approve"), (Pmo, "release") })
            {
                var r = await host.As(step.Item1, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = revisionId, Action = step.Item2 }, default));
                Assert.IsTrue(r.Success, step.Item2 + ": " + r.Message);
            }
        }

        private static Task<ResponseDto<T>> Chars<T>(AppHost host, ClaimsPrincipal who, Func<ICharacteristicService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<ICharacteristicService>()));

        private static List<SaveCharLineDto> TwoLines() => new List<SaveCharLineDto>
        {
            new SaveCharLineDto { No = "1", Name = "Diameter", Nominal = 50m, TolPlus = 0.1m, TolMinus = 0.1m, Unit = "mm", Class = CharClass.Critical },
            new SaveCharLineDto { No = "2", Name = "Length", Nominal = 100m, TolPlus = 0.5m, TolMinus = 0.5m, Unit = "mm", Class = CharClass.Major },
        };

        private static Task<ResponseDto<T>> Svc<T>(AppHost host, ClaimsPrincipal who, Func<IServiceProvider, Task<ResponseDto<T>>> call) => host.As(who, call);

        // ------------------------------------------------------------------------------------------------ PCA: legacy before the cut-over, strict after

        private static int ProjectWorkOrder(AppHost host, int contract)
        {
            int wo = 0;
            host.Write(u =>
            {
                var w = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "wo", ContractId = contract, CreateDate = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new PcWorkOrderInfo { WorkOrderId = w.Id, ContractId = contract, EquipmentProjectCode = "EP-1" });
                u.SaveChanges();
                wo = w.Id;
            }, Seeder);
            return wo;
        }

        [TestMethod]
        public async Task A_Legacy_Project_Work_Order_Shows_The_Legacy_Note_And_Is_Not_Blocked_And_A_New_One_Is_Blocked()
        {
            AppHost legacy = NewHost("pca_old", 1);
            int woOld = ProjectWorkOrder(legacy, NewContract(legacy));
            var old = await legacy.As(Pmo, p => p.GetRequiredService<IReleaseService>().GetChecksAsync(woOld, default));
            Assert.IsTrue(old.Success, old.Message);
            ReleaseCheckDto pcaOld = old.Data.Checks.Single(c => c.Code == "PCA_MATCH");
            Assert.IsTrue(pcaOld.Passed, "a legacy work order is not blocked by the configuration audit");
            Assert.IsTrue(pcaOld.Detail.Contains(GlText.PcaLegacy), "the note is visible: " + pcaOld.Detail);

            PcaResult direct = await legacy.As(Pmo, p => PhysicalConfigurationAudit.RunAsync(p.GetRequiredService<IUnitOfWork>(), woOld, Ct));
            Assert.IsTrue(direct.Legacy);
            Assert.IsFalse(direct.Applicable);

            // the use is written to the register by the release; the register keeps one row per release note and the AUDIT rule AUD-CO-001 reads it
            legacy.Write(u => { GlLegacyRegister.NoteAsync(u, GlLegacyKind.PcaNotApplicable, "ReleaseNote:RN-1", woOld, null, null, "RN-1", pcaOld.Detail, 34, Ct).GetAwaiter().GetResult(); u.SaveChanges(); }, Pmo);
            Assert.AreEqual(1, legacy.Read(u => u.Set<GlLegacyUse>().Count(x => x.Kind == GlLegacyKind.PcaNotApplicable && x.WorkOrderId == woOld)));

            AppHost strict = NewHost("pca_new", -1);
            int woNew = ProjectWorkOrder(strict, NewContract(strict));
            var fresh = await strict.As(Pmo, p => p.GetRequiredService<IReleaseService>().GetChecksAsync(woNew, default));
            ReleaseCheckDto pcaNew = fresh.Data.Checks.Single(c => c.Code == "PCA_MATCH");
            Assert.IsFalse(pcaNew.Passed, "after the cut-over a project work order without a baseline blocks");
            Assert.IsFalse(pcaNew.Detail.Contains(GlText.PcaLegacy));
        }

        // ------------------------------------------------------------------------------------------------ QC accept

        private static Task<ResponseDto<QcRecordDto>> Accept(AppHost host, int item, string note) =>
            host.As(Inspector, p => p.GetRequiredService<IInspectionService>().SaveRecordAsync(new SaveQcRecordDto { Type = QcRecordType.InProcess, ItemId = item, Quantity = 1, Result = QcResult.Accept, RecordDate = DateTime.Today, Note = note }, default));

        [TestMethod]
        public async Task A_Legacy_Qc_Accept_Needs_Evidence_Carries_The_Note_And_Is_Registered_While_A_New_Drawing_Needs_The_List()
        {
            AppHost legacy = NewHost("qc_old", 1);
            int item = NewItem(legacy, "PN-QO");
            await LegacyDrawing(legacy, NewContract(legacy), "PN-QO");
            var noEvidence = await Accept(legacy, item, "ok");
            Assert.IsFalse(noEvidence.Success, "the legacy accept needs the evidence of the decision");
            var good = await Accept(legacy, item, "بازرسی چشمی و اندازه‌گیری با کولیس طبق برگه بازرسی شماره ۱۲");
            Assert.IsTrue(good.Success, good.Message);
            Assert.IsFalse(string.IsNullOrWhiteSpace(good.Data.LegacyNote), "the note is on the record");
            Assert.AreEqual(1, legacy.Read(u => u.Set<GlLegacyUse>().Count(x => x.Kind == GlLegacyKind.QcAcceptWithoutList && x.ItemId == item)));
            Assert.AreEqual(0, legacy.Read(u => u.Set<QcMeasurement>().Count()), "no typed nominal or tolerance");

            AppHost strict = NewHost("qc_new", -1);
            int item2 = NewItem(strict, "PN-QN");
            var d = await DraftDrawing(strict, NewContract(strict), "PN-QN");
            strict.Write(u => { var r = u.Set<PcDocumentRevision>().Single(x => x.Id == d.Revision); r.Status = PcDocStatus.Released; r.ReleasedAt = DateTimeOffset.Now; u.SaveChanges(); }, Seeder);
            var refused = await Accept(strict, item2, "بازرسی چشمی و اندازه‌گیری با کولیس طبق برگه بازرسی شماره ۱۲");
            Assert.IsFalse(refused.Success, "after the cut-over a drawing without a list cannot be accepted");
            Assert.AreEqual(0, strict.Read(u => u.Set<GlLegacyUse>().Count()));
        }

        // ------------------------------------------------------------------------------------------------ FAI

        private static (int Order, int Item) ProductionOrderFor(AppHost host, int contract, string part)
        {
            int order = 0, item = 0;
            host.Write(u =>
            {
                var w = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "wo", ContractId = contract, CreateDate = DateTimeOffset.Now });
                var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "part", PartNumber = part });
                u.SaveChanges();
                var o = AppHost.Add(u, new MfProductionOrder { Number = "MO-" + Guid.NewGuid().ToString("N").Substring(0, 5), WorkOrderId = w.Id, ContractId = contract, ItemId = i.Id, Quantity = 1, Unit = "EA" });
                u.SaveChanges();
                order = o.Id; item = i.Id;
            }, Seeder);
            return (order, item);
        }

        [TestMethod]
        public async Task The_First_Article_Is_Not_Opened_For_A_Legacy_Part_And_Is_Opened_For_A_New_Revision()
        {
            AppHost legacy = NewHost("fai_old", 1);
            var lo = ProductionOrderFor(legacy, NewContract(legacy), "PN-FO");
            await LegacyDrawing(legacy, NewContract(legacy), "PN-FO");
            string refusal = await legacy.As(Pmo, async p =>
            {
                var uow = p.GetRequiredService<IUnitOfWork>();
                MfProductionOrder o = await uow.Set<MfProductionOrder>().FirstAsync(x => x.Id == lo.Order, Ct);
                string r = await FirstArticleGate.EnsureAtReleaseAsync(uow, o, new List<MfOperation>(), 34, DateTimeOffset.Now, Ct);
                await uow.SaveChangesAsync(Ct);
                return r;
            });
            Assert.IsNull(refusal, "a legacy part is not stopped");
            Assert.AreEqual(0, legacy.Read(u => u.Set<QcFirstArticle>().Count()), "no first article was opened");
            Assert.AreEqual(1, legacy.Read(u => u.Set<GlLegacyUse>().Count(x => x.Kind == GlLegacyKind.FaiNotOpened)), "the use is registered");

            AppHost strict = NewHost("fai_new", -1);
            var so = ProductionOrderFor(strict, NewContract(strict), "PN-FN");
            var d = await DraftDrawing(strict, NewContract(strict), "PN-FN");
            var saved = await Chars(strict, Maker, s => s.SaveAsync(new SaveCharListDto { RevisionId = d.Revision, Items = TwoLines() }, default));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Chars(strict, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsTrue((await Chars(strict, Checker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default))).Success);
            await ReleaseRevision(strict, d.Revision);
            FaiRequirement req = await strict.As(Pmo, async p =>
            {
                var uow = p.GetRequiredService<IUnitOfWork>();
                MfProductionOrder o = await uow.Set<MfProductionOrder>().FirstAsync(x => x.Id == so.Order, Ct);
                return await FirstArticleGate.EvaluateAsync(uow, o, new List<MfOperation>(), Ct);
            });
            Assert.IsFalse(req.Legacy);
            Assert.IsTrue(req.Required, "a revision released after the cut-over opens a first article");
        }

        // ------------------------------------------------------------------------------------------------ adoption of the characteristic list

        [TestMethod]
        public async Task The_Characteristic_Adoption_Is_Made_By_Engineering_Approved_By_Another_Person_Of_Qa_And_Then_The_Normal_Pick_Works()
        {
            AppHost host = NewHost("char", 1);
            int item = NewItem(host, "PN-CA");
            var d = await LegacyDrawing(host, NewContract(host), "PN-CA");
            Task<ResponseDto<T>> Do<T>(ClaimsPrincipal who, Func<IGlCharAdoptionService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IGlCharAdoptionService>()));
            var req = new GlSaveCharAdoptionDto { RevisionId = d.Revision, Reason = "نقشه پیش از آغاز کنترل منتشر شده است", EvidenceRef = "برگه بازرسی ۱۲", Items = TwoLines() };

            Assert.IsFalse((await Do(Checker, s => s.RequestAsync(req, Ct))).Success, "QA is the checker, not the maker");
            Assert.IsFalse((await Do(Maker, s => s.RequestAsync(new GlSaveCharAdoptionDto { RevisionId = d.Revision, Reason = "short", EvidenceRef = "برگه بازرسی ۱۲", Items = TwoLines() }, Ct))).Success, "the reason has a minimum length");
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 90, UserName = "seat.eng", Roles = new[] { "ENGINEERING", "ENGINEER" } }, "ENG", sp => sp.GetRequiredService<IGlCharAdoptionService>().RequestAsync(req, Ct));
            Assert.IsFalse(ai.Success, "an AI seat never requests");

            var made = await Do(MakerAndQa, s => s.RequestAsync(req, Ct));
            Assert.IsTrue(made.Success, made.Message);
            Assert.AreEqual(1, made.Data.Status == 0 ? 1 : 0);
            Assert.IsFalse((await Do(Maker, s => s.RequestAsync(req, Ct))).Success, "one live adoption per revision");
            var self = await Do(MakerAndQa, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "accept" }, Ct));
            Assert.IsFalse(self.Success, "the creator never accepts his own adoption even with the QA role");
            var aiAccept = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 91, UserName = "seat.qa", Roles = new[] { "QA", "PMO" } }, "QA", sp => sp.GetRequiredService<IGlCharAdoptionService>().ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "accept" }, Ct));
            Assert.IsFalse(aiAccept.Success, "an AI seat never accepts");
            Assert.IsFalse((await Do(Maker, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "accept" }, Ct))).Success, "an engineer does not accept");

            var ok = await Do(Checker, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "accept" }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(2, ok.Data.Status);
            QcCharacteristicList list = legacyList(host, d.Revision);
            Assert.AreEqual(CharListStatus.Released, list.Status, "the revision is in force, so the approved list is released at once (no ECO for the first list)");

            // the normal pick now works with the typed nominal and tolerance taken from the list
            int dia = host.Read(u => u.Set<QcCharacteristic>().Single(c => c.ListId == list.Id && c.No == "1").Id);
            int len = host.Read(u => u.Set<QcCharacteristic>().Single(c => c.ListId == list.Id && c.No == "2").Id);
            int eq = 0;
            host.Write(u => { var e = AppHost.Add(u, new MeasuringEquipment { Code = "EQ-" + Guid.NewGuid().ToString("N").Substring(0, 5), Name = "gauge", Type = EquipmentType.Other }); u.SaveChanges(); eq = e.Id; }, Seeder);
            PickOutcome pick = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(p.GetRequiredService<IUnitOfWork>(), item, QcRecordType.Dimensional, null, 1m, QcResult.Accept,
                new List<PickInput> { new PickInput { CharacteristicId = dia, Measured = 50.05m, EquipmentId = eq }, new PickInput { CharacteristicId = len, Measured = 100m, EquipmentId = eq } }, default));
            Assert.IsNull(pick.Error, pick.Error);
            Assert.IsTrue(pick.Governed);
            Assert.IsFalse(pick.Legacy);

            var again = await Do(Maker, s => s.RequestAsync(req, Ct));
            Assert.IsFalse(again.Success, "an accepted adoption is final for the revision");
        }

        private static QcCharacteristicList legacyList(AppHost host, int revisionId) => host.Read(u => u.Set<QcCharacteristicList>().Single(x => x.RevisionId == revisionId && x.Status != CharListStatus.Void));

        [TestMethod]
        public async Task A_Characteristic_Adoption_Can_Be_Voided_With_A_Reason_And_Is_Refused_For_A_Revision_Released_After_The_Cut_Over()
        {
            AppHost host = NewHost("char_void", 1);
            var d = await LegacyDrawing(host, NewContract(host), "PN-CV");
            Task<ResponseDto<T>> Do<T>(ClaimsPrincipal who, Func<IGlCharAdoptionService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IGlCharAdoptionService>()));
            var made = await Do(Maker, s => s.RequestAsync(new GlSaveCharAdoptionDto { RevisionId = d.Revision, Reason = "نقشه پیش از آغاز کنترل منتشر شده است", EvidenceRef = "برگه بازرسی ۱۲", Items = TwoLines() }, Ct));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsFalse((await Do(Maker, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "void", Note = "x" }, Ct))).Success, "a void needs a reason");
            var voided = await Do(Maker, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "void", Note = "مشخصه‌ها اشتباه وارد شد و دوباره تهیه می‌شود" }, Ct));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.AreEqual(3, voided.Data.Status);
            Assert.AreEqual(1, host.Read(u => u.Set<GlCharAdoption>().Count()), "voided, never deleted");

            AppHost strict = NewHost("char_new", -1);
            var d2 = await DraftDrawing(strict, NewContract(strict), "PN-CN");
            strict.Write(u => { var r = u.Set<PcDocumentRevision>().Single(x => x.Id == d2.Revision); r.Status = PcDocStatus.Released; r.ReleasedAt = DateTimeOffset.Now; u.SaveChanges(); }, Seeder);
            var refused = await strict.As(Maker, p => p.GetRequiredService<IGlCharAdoptionService>().RequestAsync(new GlSaveCharAdoptionDto { RevisionId = d2.Revision, Reason = "نقشه پیش از آغاز کنترل منتشر شده است", EvidenceRef = "برگه بازرسی ۱۲", Items = TwoLines() }, Ct));
            Assert.IsFalse(refused.Success, "a revision released after the cut-over goes through the ordinary route");
        }

        // ------------------------------------------------------------------------------------------------ adoption of the project configuration

        private static (int WorkOrder, int Product, int Bom) LegacyProject(AppHost host)
        {
            int wo = 0, product = 0, bom = 0;
            int contract = NewContract(host);
            host.Write(u =>
            {
                var w = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "legacy wo", ContractId = contract, CreateDate = DateTimeOffset.Now });
                var pr = AppHost.Add(u, new PbProduct { Code = "77.10.00.900.1", Title = "product", IsActive = true });
                u.SaveChanges();
                AppHost.Add(u, new PcWorkOrderInfo { WorkOrderId = w.Id, ContractId = contract, EquipmentProjectCode = "EP-9" });
                var h = AppHost.Add(u, new PbBomHeader { ProductId = pr.Id, Code = "BOM-900", Title = "bom", Revision = 1, Status = PbBomStatus.Released, AssemblyPartNumber = "77.10.00.900.1", ReleasedAt = DateTimeOffset.Now.AddDays(-20) });
                u.SaveChanges();
                AppHost.Add(u, new PbBomLine { BomHeaderId = h.Id, SortOrder = 1, ItemNumber = 1, PartNumber = "77.10.01.900.1", PartName = "Plate", Qty = 2, Unit = "عدد" });
                u.SaveChanges();
                wo = w.Id; product = pr.Id; bom = h.Id;
            }, Seeder);
            return (wo, product, bom);
        }

        [TestMethod]
        public async Task The_Configuration_Adoption_Needs_Three_Different_People_And_Creates_The_Baseline_Which_Makes_The_Audit_Strict()
        {
            AppHost host = NewHost("cfg", 1);
            var p = LegacyProject(host);
            Task<ResponseDto<T>> Do<T>(ClaimsPrincipal who, Func<IGlConfigAdoptionService, Task<ResponseDto<T>>> call) => host.As(who, s => call(s.GetRequiredService<IGlConfigAdoptionService>()));
            var dto = new GlSaveConfigAdoptionDto { WorkOrderId = p.WorkOrder, ProductId = p.Product, BomHeaderId = p.Bom, Reason = "پروژه پیش از آغاز کنترل پیکربندی شروع شده است", EvidenceRef = "فهرست BOM قدیمی شماره ۷", ComparisonNote = "ردیف‌های BOM قدیمی با بانک محصول تطبیق داده شد" };

            Assert.IsFalse((await Do(Pmo, s => s.RequestAsync(dto, Ct))).Success, "the PMO does not prepare");
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 90, UserName = "seat.eng", Roles = new[] { "ENGINEERING" } }, "ENG", sp => sp.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(dto, Ct));
            Assert.IsFalse(ai.Success, "an AI seat never prepares");
            Assert.IsFalse((await Do(Prep, s => s.RequestAsync(new GlSaveConfigAdoptionDto { WorkOrderId = p.WorkOrder, ProductId = p.Product, BomHeaderId = p.Bom, Reason = dto.Reason, EvidenceRef = dto.EvidenceRef, ComparisonNote = "x" }, Ct))).Success, "the comparison is required");

            var made = await Do(Prep, s => s.RequestAsync(dto, Ct));
            Assert.IsTrue(made.Success, made.Message);
            Assert.AreEqual(1, made.Data.BankLineCount);
            Assert.IsFalse((await Do(Eng2, s => s.RequestAsync(dto, Ct))).Success, "one live adoption per work order");

            Assert.IsFalse((await Do(Prep, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "eng-review" }, Ct))).Success, "the preparer cannot review");
            Assert.IsFalse((await Do(Pmo, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "eng-review" }, Ct))).Success, "the engineering review is made by engineering");
            Assert.IsFalse((await Do(Eng2, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "pmo-review" }, Ct))).Success, "the PMO review is made by the PMO");
            var aiReview = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 91, UserName = "seat.pmo", Roles = new[] { "PMO", "ENGINEERING" } }, "PMO", sp => sp.GetRequiredService<IGlConfigAdoptionService>().ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "pmo-review" }, Ct));
            Assert.IsFalse(aiReview.Success, "an AI seat never reviews");

            var first = await Do(EngAndPmo, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "eng-review", Note = "بازبینی مهندسی" }, Ct));
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(1, first.Data.Status, "partly reviewed");
            var twice = await Do(EngAndPmo, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "pmo-review" }, Ct));
            Assert.IsFalse(twice.Success, "the same person cannot give both reviews");
            Assert.AreEqual(0, host.Read(u => u.Set<PbProjectBaseline>().Count()), "no baseline yet");

            var last = await Do(Pmo, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "pmo-review", Note = "بازبینی PMO" }, Ct));
            Assert.IsTrue(last.Success, last.Message);
            Assert.AreEqual(2, last.Data.Status);
            Assert.IsNotNull(last.Data.BaselineId);
            Assert.AreEqual(1, host.Read(u => u.Set<PbProjectBaseline>().Count(b => b.WorkOrderId == p.WorkOrder && b.IsCurrent)));
            Assert.AreEqual(1, host.Read(u => u.Set<PbProjectBomLine>().Count(l => l.BaselineId == last.Data.BaselineId)));

            // the audit is strict from now on: the legacy note is gone and the missing genealogy of the plate blocks
            PcaResult pca = await host.As(Pmo, s => PhysicalConfigurationAudit.RunAsync(s.GetRequiredService<IUnitOfWork>(), p.WorkOrder, Ct));
            Assert.IsFalse(pca.Legacy);
            Assert.IsTrue(pca.Applicable);
            Assert.IsFalse(pca.Passed);
            Assert.AreEqual(1, pca.Problems.Count);

            Assert.IsFalse((await Do(Prep, s => s.ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "void", Note = "دلیل ابطال کافی است" }, Ct))).Success, "an accepted adoption is not voided");
        }

        [TestMethod]
        public async Task A_Configuration_Adoption_Is_Refused_For_A_New_Work_Order_And_Can_Be_Voided_While_Open()
        {
            AppHost strict = NewHost("cfg_new", -1);
            var n = LegacyProject(strict);
            var dto = new GlSaveConfigAdoptionDto { WorkOrderId = n.WorkOrder, ProductId = n.Product, BomHeaderId = n.Bom, Reason = "پروژه پیش از آغاز کنترل پیکربندی شروع شده است", EvidenceRef = "فهرست BOM قدیمی شماره ۷", ComparisonNote = "ردیف‌های BOM قدیمی با بانک محصول تطبیق داده شد" };
            Assert.IsFalse((await strict.As(Prep, s => s.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(dto, Ct))).Success, "a work order created after the cut-over needs the ordinary baseline");

            AppHost host = NewHost("cfg_void", 1);
            var p = LegacyProject(host);
            dto.WorkOrderId = p.WorkOrder; dto.ProductId = p.Product; dto.BomHeaderId = p.Bom;
            var made = await host.As(Prep, s => s.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(dto, Ct));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsFalse((await host.As(Prep, s => s.GetRequiredService<IGlConfigAdoptionService>().ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "void", Note = "x" }, Ct))).Success);
            var v = await host.As(Prep, s => s.GetRequiredService<IGlConfigAdoptionService>().ActionAsync(new GlAdoptionActionDto { Id = made.Data.Id, Action = "void", Note = "BOM اشتباه انتخاب شده بود" }, Ct));
            Assert.IsTrue(v.Success, v.Message);
            var again = await host.As(Prep, s => s.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(dto, Ct));
            Assert.IsTrue(again.Success, "after a void a new request is possible: " + again.Message);
            Assert.AreEqual(2, host.Read(u => u.Set<GlConfigAdoption>().Count()), "the voided row stays");
        }
    }
}
