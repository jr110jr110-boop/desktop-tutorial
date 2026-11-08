using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// P-B7b: the document lifecycle (revision codes, immutability, supersede, maker-checker, reject reason, AI seats), check-out locks, links, the shop-floor view of the latest released revision,
    /// transmittals that cite a revision, documents received from the client, the Excel template round trip, the tender register and the distribution notices.
    /// </summary>
    [TestClass]
    public class EdmsTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Dcc = AppHost.Principal(31, "dcc.one", "DCC");
        private static readonly ClaimsPrincipal Reviewer = AppHost.Principal(32, "engineer.one", "ENGINEER");
        private static readonly ClaimsPrincipal Approver = AppHost.Principal(33, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(34, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Operator = AppHost.Principal(35, "operator.one", "OPERATOR");
        private static readonly ClaimsPrincipal Dcc2 = AppHost.Principal(36, "dcc.two", "DCC");
        private static readonly ClaimsPrincipal ListMaker = AppHost.Principal(38, "list.maker", "ENGINEER");
        private static readonly ClaimsPrincipal ListChecker = AppHost.Principal(39, "list.checker", "QA");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(37, "commercial.one", "COMMERCIAL");

        private static AppHost NewHost(string name)
        {
            var host = new AppHost("P7b_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 31, 32, 33, 34, 35, 36, 37, 38, 39 }) AppHost.Add(u, new User { Id = id, UserName = "p7b.user" + id, Email = "p7b.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return host.WithoutDesignControl(); // Package A1: these tests are about the EDMS gates; the design verification gate has its own tests
        }

        private static int NewContract(AppHost host)
        {
            int id = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static int NewDeliverable(AppHost host, int contractId, bool client = false, string number = null, int? workOrderId = null)
        {
            int id = 0;
            host.Write(u =>
            {
                var d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contractId, WorkOrderId = workOrderId, DocumentNumber = number ?? "DOC-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "drawing", ClientSubmittal = client, Discipline = "Mechanical", DocumentType = "Drawing" });
                u.SaveChanges();
                id = d.Id;
            }, Seeder);
            return id;
        }

        private static Guid NewFile(AppHost host)
        {
            Guid id = Guid.Empty;
            host.Write(u =>
            {
                var d = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf");
                u.Set<Entities.Model.Document>().Add(d);
                u.SaveChanges();
                id = d.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<T>> Edms<T>(AppHost host, ClaimsPrincipal who, Func<IEdmsService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IEdmsService>()));

        private static async Task<PcRevisionDto> Draft(AppHost host, int deliverableId, bool withFile = true)
        {
            var r = await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = deliverableId, Reason = "first issue" }, default));
            Assert.IsTrue(r.Success, r.Message);
            if (withFile)
            {
                Guid fid = NewFile(host);
                var f = await Edms(host, Dcc, s => s.AddFileAsync(new PcRevisionFileAddDto { RevisionId = r.Data.Id, DocumentId = fid, FileName = "a.pdf", Role = PcRevisionFileRole.Pdf }, default));
                Assert.IsTrue(f.Success, f.Message);
            }
            return r.Data;
        }

        /// <summary>Package B (H2): a drawing linked to a part is released only with an approved characteristic list; these tests are about documents, so the list is the empty one «بدون مشخصه قابل اندازه‌گیری» (reason, maker, checker).</summary>
        private static async Task EnsureEmptyList(AppHost host, int revisionId)
        {
            bool linked = host.Read(u => u.Set<PcDocumentRevision>().Where(r => r.Id == revisionId).Join(u.Set<PcDocumentLink>().Where(l => !l.Deleted && l.UnlinkReason == null),
                r => r.DeliverableId, l => l.DeliverableId, (r, l) => l.Kind).Any(k => k == PcDocLinkKind.Item || k == PcDocLinkKind.PartNumber || k == PcDocLinkKind.BomLine));
            if (!linked) return;
            var saved = await host.As(ListMaker, p => p.GetRequiredService<Services.NasbNiro.Quality.ICharacteristicService>().SaveAsync(new ViewModels.NasbNiro.QualityDtos.SaveCharListDto { RevisionId = revisionId, Reason = "این نقشه مشخصه قابل اندازه‌گیری ندارد (آزمون)" }, default));
            Assert.IsTrue(saved.Success, saved.Message);
            foreach (var step in new[] { (ListMaker, "submit"), (ListChecker, "approve") })
            {
                var r = await host.As(step.Item1, p => p.GetRequiredService<Services.NasbNiro.Quality.ICharacteristicService>().ActionAsync(new ViewModels.NasbNiro.QualityDtos.CharListActionDto { Id = saved.Data.Id, Action = step.Item2 }, default));
                Assert.IsTrue(r.Success, step.Item2 + ": " + r.Message);
            }
        }

        private static async Task Release(AppHost host, int revisionId)
        {
            await EnsureEmptyList(host, revisionId);
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "review"), (Approver, "approve"), (Pmo, "release") })
            {
                var r = await Edms(host, step.Item1, s => s.ActionAsync(new PcRevisionActionDto { Id = revisionId, Action = step.Item2 }, default));
                Assert.IsTrue(r.Success, step.Item2 + ": " + r.Message);
            }
        }

        private static async Task<PcRevisionDto> ReleasedRevision(AppHost host, int deliverableId)
        {
            PcRevisionDto r = await Draft(host, deliverableId);
            await Release(host, r.Id);
            return r;
        }

        // ------------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void RevisionCodesFollowTheScheme()
        {
            Assert.AreEqual("00", EdmsRules.NextCode(1, 1));
            Assert.AreEqual("09", EdmsRules.NextCode(1, 10));
            Assert.AreEqual("A", EdmsRules.NextCode(2, 1));
            Assert.AreEqual("Z", EdmsRules.NextCode(2, 26));
            Assert.AreEqual("AA", EdmsRules.NextCode(2, 27));
        }

        [TestMethod]
        public void OnlyReleasedRevisionsAreSentUnlessForReview()
        {
            Assert.IsTrue(EdmsRules.CanSend(PcDocStatus.Released, PcTransmittalPurpose.ForInformation));
            Assert.IsFalse(EdmsRules.CanSend(PcDocStatus.Draft, PcTransmittalPurpose.ForApproval));
            Assert.IsFalse(EdmsRules.CanSend(PcDocStatus.Superseded, PcTransmittalPurpose.ForApproval));
            Assert.IsTrue(EdmsRules.CanSend(PcDocStatus.UnderReview, PcTransmittalPurpose.ForApproval));
            Assert.IsFalse(EdmsRules.CanSend(PcDocStatus.UnderReview, PcTransmittalPurpose.ForInformation));
        }

        // ------------------------------------------------------------------------------------------------ lifecycle

        [TestMethod]
        public async Task ReleaseSupersedesThePreviousRevisionAndReleasedRevisionsAreImmutable()
        {
            AppHost host = NewHost("life");
            int contract = NewContract(host), doc = NewDeliverable(host, contract);
            PcRevisionDto first = await ReleasedRevision(host, doc);
            Assert.AreEqual("00", first.Code);

            // a released revision takes no more files
            Guid lateFile = NewFile(host);
            var late = await Edms(host, Dcc, s => s.AddFileAsync(new PcRevisionFileAddDto { RevisionId = first.Id, DocumentId = lateFile, FileName = "late.pdf" }, default));
            Assert.IsFalse(late.Success);

            PcRevisionDto second = await Draft(host, doc);
            Assert.AreEqual("01", second.Code);
            await Release(host, second.Id);

            var history = await Edms(host, Pmo, s => s.GetHistoryAsync(doc, default));
            Assert.AreEqual(PcDocStatus.Superseded, history.Data.Revisions.First(r => r.Id == first.Id).Status);
            Assert.AreEqual(PcDocStatus.Released, history.Data.Revisions.First(r => r.Id == second.Id).Status);
            string revision = host.Read(u => u.Set<PcEngineeringDeliverable>().Where(d => d.Id == doc).Select(d => d.Revision).First());
            Assert.AreEqual("01", revision);
            // the register line follows the lifecycle
            Assert.AreEqual(PcIssueState.Approved, host.Read(u => u.Set<PcEngineeringDeliverable>().Where(d => d.Id == doc).Select(d => d.IssueState).First()));
        }

        [TestMethod]
        public async Task OnlyOneOpenRevisionAtATime()
        {
            AppHost host = NewHost("open");
            int doc = NewDeliverable(host, NewContract(host));
            await Draft(host, doc);
            var again = await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "again" }, default));
            Assert.IsFalse(again.Success);
        }

        [TestMethod]
        public async Task MakerCheckerSeparatesPreparerReviewerAndApprover()
        {
            AppHost host = NewHost("mc");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto r = await Draft(host, doc);
            Assert.IsTrue((await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "submit" }, default))).Success);
            // the preparer never reviews or approves his own revision
            Assert.IsFalse((await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "review" }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
            // approval needs a review first
            Assert.IsFalse((await Edms(host, Approver, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
            Assert.IsTrue((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "review" }, default))).Success);
            // the reviewer is not the approver
            Assert.IsFalse((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
            Assert.IsTrue((await Edms(host, Approver, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
        }

        [TestMethod]
        public async Task RejectNeedsAReasonAndReturnsTheRevisionToDraft()
        {
            AppHost host = NewHost("reject");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto r = await Draft(host, doc);
            await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "submit" }, default));
            Assert.IsFalse((await Edms(host, Reviewer, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "reject" }, default))).Success);
            var ok = await Edms(host, Reviewer, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "reject", Note = "dimensions are wrong" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(PcDocStatus.Draft, ok.Data.Status);
            Assert.AreEqual("dimensions are wrong", ok.Data.RejectReason);
        }

        [TestMethod]
        public async Task AnAiSeatNeverReviewsApprovesOrReleases()
        {
            AppHost host = NewHost("ai");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto r = await Draft(host, doc);
            await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "submit" }, default));
            var seat = new AiActingUser { UserId = 90, UserName = "seat.docs", Roles = new[] { "ENGINEERING", "PMO", "DCC" } };
            var result = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "DOCS", sp => sp.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "review" }, default));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(PcDocStatus.UnderReview, host.Read(u => u.Set<PcDocumentRevision>().Where(x => x.Id == r.Id).Select(x => x.Status).First()));
        }

        [TestMethod]
        public async Task ChangeImpactRevisionNeedsAnApprovedChangeRequestBeforeRelease()
        {
            AppHost host = NewHost("change");
            int doc = NewDeliverable(host, NewContract(host));
            var r = await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "changes the cost", ChangeImpact = true }, default));
            Guid chFile = NewFile(host);
            await Edms(host, Dcc, s => s.AddFileAsync(new PcRevisionFileAddDto { RevisionId = r.Data.Id, DocumentId = chFile, FileName = "a.pdf" }, default));
            var submit = await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Data.Id, Action = "submit" }, default));
            Assert.IsFalse(submit.Success, "no change request linked");
        }

        // ------------------------------------------------------------------------------------------------ check-out

        [TestMethod]
        public async Task CheckOutLocksTheDocumentForOthersAndOnlyAManagerCancelsItWithAReason()
        {
            AppHost host = NewHost("lock");
            int doc = NewDeliverable(host, NewContract(host));
            Assert.IsTrue((await Edms(host, Dcc, s => s.CheckOutAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc2, s => s.CheckOutAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc2, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "x" }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc2, s => s.CheckInAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc2, s => s.CancelCheckOutAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success, "a reason is required");
            Assert.IsFalse((await Edms(host, Reviewer, s => s.CancelCheckOutAsync(new PcCheckOutDto { DeliverableId = doc, Reason = "gone" }, default))).Success, "not a manager");
            Assert.IsTrue((await Edms(host, Dcc2, s => s.CancelCheckOutAsync(new PcCheckOutDto { DeliverableId = doc, Reason = "colleague is away" }, default))).Success);
            Assert.IsNull(host.Read(u => u.Set<PcEngineeringDeliverable>().Where(d => d.Id == doc).Select(d => d.CheckedOutByUserId).First()));
            Assert.IsTrue(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.DeliverableId == doc && e.Kind == PcDocEventKind.CheckoutCancelled)));
        }

        // ------------------------------------------------------------------------------------------------ links and the shop floor

        private static (int ItemId, int OrderId, int WorkOrderId) NewOrder(AppHost host, int contractId, string part)
        {
            int item = 0, order = 0, wo = 0;
            host.Write(u =>
            {
                var w = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "wo", ContractId = contractId });
                var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "part", PartNumber = part });
                u.SaveChanges();
                var o = AppHost.Add(u, new MfProductionOrder { Number = "MO-" + Guid.NewGuid().ToString("N").Substring(0, 5), WorkOrderId = w.Id, ContractId = contractId, ItemId = i.Id, Quantity = 1, Unit = "EA" });
                u.SaveChanges();
                item = i.Id; order = o.Id; wo = w.Id;
            }, Seeder);
            return (item, order, wo);
        }

        /// <summary>Package B: with no released drawing a make part passes the drawings check only with an active «بدون نقشه» flag.</summary>
        private static void ActiveNoDrawing(AppHost host, int itemId)
        {
            host.Write(u =>
            {
                u.Set<Entities.Model.NasbNiro.Quality.NoDrawingFlag>().Add(new Entities.Model.NasbNiro.Quality.NoDrawingFlag
                {
                    ItemId = itemId, PartNumber = "PN", Reason = "no drawing for this test part", Status = Entities.Model.NasbNiro.Quality.NoDrawingStatus.Active,
                    CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now, CreatorUserId = 1, ModifierUserId = 2, DecidedByUserId = 2, DecidedAt = DateTimeOffset.Now,
                });
                u.SaveChanges();
            }, Seeder);
        }

        [TestMethod]
        public async Task TheShopFloorSeesOnlyTheLatestReleasedRevisionOfTheLinkedDocuments()
        {
            AppHost host = NewHost("shop");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-100");
            int doc = NewDeliverable(host, contract), other = NewDeliverable(host, contract), draftOnly = NewDeliverable(host, contract);
            foreach (int d in new[] { doc, draftOnly })
                Assert.IsTrue((await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = d, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-100" }, default))).Success);
            // a link to another part never shows
            Assert.IsTrue((await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = other, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-OTHER" }, default))).Success);
            await ReleasedRevision(host, other);
            PcRevisionDto r0 = await ReleasedRevision(host, doc);
            await Draft(host, draftOnly);
            // revision 01 is a draft: the floor still shows 00
            PcRevisionDto r1 = await Draft(host, doc);
            var docs = await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default));
            Assert.IsTrue(docs.Success, docs.Message);
            Assert.AreEqual(1, docs.Data.Count);
            Assert.AreEqual(r0.Id, docs.Data[0].RevisionId);
            Assert.AreEqual("00", docs.Data[0].Code);
            // once 01 is released only 01 shows (00 is superseded)
            await Release(host, r1.Id);
            docs = await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default));
            Assert.AreEqual(1, docs.Data.Count);
            Assert.AreEqual(r1.Id, docs.Data[0].RevisionId);
            Assert.AreEqual("01", docs.Data[0].Code);
        }

        [TestMethod]
        public async Task ALinkBoundToAnOperationShowsOnlyForThatOperationAndTheControlledCopyIsStamped()
        {
            AppHost host = NewHost("op");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-200");
            int template = 0, op1 = 0, op2 = 0;
            host.Write(u =>
            {
                var t1 = AppHost.Add(u, new OperationTemplate { Sequence = 1, Title = "turning" });
                var t2 = AppHost.Add(u, new OperationTemplate { Sequence = 2, Title = "milling" });
                u.SaveChanges();
                template = t1.Id;
                op1 = AppHost.Add(u, new MfOperation { OrderId = o.OrderId, Sequence = 1, OperationTemplateId = t1.Id, Title = "turning" }).Id;
                op2 = AppHost.Add(u, new MfOperation { OrderId = o.OrderId, Sequence = 2, OperationTemplateId = t2.Id, Title = "milling" }).Id;
                u.SaveChanges();
            }, Seeder);
            int doc = NewDeliverable(host, contract);
            Assert.IsTrue((await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-200", OperationTemplateId = template }, default))).Success);
            PcRevisionDto rev = await ReleasedRevision(host, doc);
            Assert.AreEqual(1, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, op1, default))).Data.Count);
            Assert.AreEqual(0, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, op2, default))).Data.Count);

            var stamp = await Edms(host, Operator, s => s.ControlledCopyAsync("revision", rev.Id, null, true, default));
            Assert.IsTrue(stamp.Success, stamp.Message);
            Assert.AreEqual("00", stamp.Data.Code);
            StringAssert.Contains(stamp.Data.StampText, "00");
            StringAssert.Contains(stamp.Data.StampText, "p7b.user35");
            Assert.IsTrue(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.RevisionId == rev.Id && e.Kind == PcDocEventKind.Printed)));
        }

        [TestMethod]
        public async Task ADraftRevisionHasNoControlledCopy()
        {
            AppHost host = NewHost("copy");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto r = await Draft(host, doc);
            Assert.IsFalse((await Edms(host, Operator, s => s.ControlledCopyAsync("revision", r.Id, null, true, default))).Success);
        }

        [TestMethod]
        public async Task PartDocumentsListsEveryLinkedDocumentWithTheReleasedRevision()
        {
            AppHost host = NewHost("part");
            int contract = NewContract(host);
            int doc = NewDeliverable(host, contract), draftOnly = NewDeliverable(host, contract);
            await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-300" }, default));
            await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = draftOnly, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-300" }, default));
            // a duplicate link is refused, a bad part is refused
            Assert.IsFalse((await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-300" }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber }, default))).Success);
            await ReleasedRevision(host, doc);
            var list = await Edms(host, Pmo, s => s.GetPartDocumentsAsync(new PcDocLinkQuery { PartNumber = "PN-300" }, default));
            Assert.AreEqual(2, list.Data.Count);
            Assert.AreEqual("00", list.Data.Single(x => x.DeliverableId == doc).ReleasedCode);
            Assert.IsNull(list.Data.Single(x => x.DeliverableId == draftOnly).ReleasedCode);
        }

        [TestMethod]
        public async Task AnUnlinkNeedsAReasonAndHidesTheLinkInsteadOfDeletingIt()
        {
            AppHost host = NewHost("unlink");
            int doc = NewDeliverable(host, NewContract(host));
            var link = await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-400" }, default));
            Assert.IsFalse((await Edms(host, Dcc, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id, Reason = "the part number was wrong here" }, default))).Success, "A1: the link of a design drawing is removed by ENGINEERING or QA, not the document controller");
            Assert.IsFalse((await Edms(host, Approver, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id, Reason = "wrong part" }, default))).Success, "A1: the reason has the minimum length");
            Assert.IsTrue((await Edms(host, Approver, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id, Reason = "the part number was wrong here" }, default))).Success);
            Assert.AreEqual(0, (await Edms(host, Pmo, s => s.GetLinksAsync(new PcDocLinkQuery { DeliverableId = doc }, default))).Data.Count);
            Assert.AreEqual(1, host.Read(u => u.Set<PcDocumentLink>().IgnoreQueryFilters().Count(l => l.Id == link.Data.Id && l.Deleted)));
        }

        [TestMethod]
        public async Task TagsAreUniquePerProject()
        {
            AppHost host = NewHost("tag");
            int contract = NewContract(host);
            Assert.IsTrue((await Edms(host, Dcc, s => s.SaveTagAsync(new PcTagSaveDto { ContractId = contract, Code = "pv-101", Title = "valve", IsActive = true }, default))).Success);
            Assert.IsFalse((await Edms(host, Dcc, s => s.SaveTagAsync(new PcTagSaveDto { ContractId = contract, Code = "PV-101", IsActive = true }, default))).Success);
            Assert.AreEqual("PV-101", (await Edms(host, Pmo, s => s.GetTagsAsync(contract, default))).Data.Single().Code);
        }

        // ------------------------------------------------------------------------------------------------ transmittals and incoming documents

        private static PcTransmittalDto Outgoing(int contract, int doc, int? revisionId, string text = null, PcTransmittalPurpose purpose = PcTransmittalPurpose.ForApproval) =>
            new PcTransmittalDto { ContractId = contract, Direction = PcTransmittalDirection.Outgoing, TransmittalDate = DateTime.Today, Lines = new List<PcTransmittalLineDto> { new PcTransmittalLineDto { DeliverableId = doc, RevisionId = revisionId, Revision = text, Purpose = purpose } } };

        [TestMethod]
        public async Task ATransmittalCitesARevisionNotFreeText()
        {
            AppHost host = NewHost("trn");
            int contract = NewContract(host), doc = NewDeliverable(host, contract, client: true);
            // the default (0) lets a document without revisions go with a revision text; with the setting on it is refused
            host.Write(u =>
            {
                var row = u.Set<PcSetting>().FirstOrDefault(x => x.Key == PcSettingKeys.EdmsRequireRevision);
                if (row == null) AppHost.Add(u, new PcSetting { Key = PcSettingKeys.EdmsRequireRevision, Value = 1m, Title = "t" }); else row.Value = 1m;
                u.SaveChanges();
            }, Seeder);
            Assert.IsFalse((await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, doc, null, "A", PcTransmittalPurpose.ForApproval), default))).Success);
            PcRevisionDto draft = await Draft(host, doc);
            // a draft is not sent
            Assert.IsFalse((await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, doc, draft.Id), default))).Success);
            await Release(host, draft.Id);
            var ok = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, doc, draft.Id), default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual("00", ok.Data.Lines.Single().Revision);
            Assert.AreEqual(draft.Id, ok.Data.Lines.Single().RevisionId);
            // the revision of another document is refused
            int doc2 = NewDeliverable(host, contract, client: true);
            Assert.IsFalse((await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, doc2, draft.Id), default))).Success);
        }

        [TestMethod]
        public async Task TheTransitionDefaultSendsALegacyDocumentWithTextButAlwaysByRevisionOnceItHasOne()
        {
            AppHost host = NewHost("trn0");
            int contract = NewContract(host), legacy = NewDeliverable(host, contract, client: true), withRevisions = NewDeliverable(host, contract, client: true);
            var ok = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, legacy, null, "A", PcTransmittalPurpose.ForApproval), default));
            Assert.IsTrue(ok.Success, ok.Message);
            await Draft(host, withRevisions);
            Assert.IsFalse((await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(Outgoing(contract, withRevisions, null, "A", PcTransmittalPurpose.ForApproval), default))).Success);
        }

        [TestMethod]
        public async Task ADocumentReceivedFromTheClientCreatesItsRegisterLineAndRevision()
        {
            AppHost host = NewHost("recv");
            int contract = NewContract(host);
            Guid recvFile = NewFile(host);
            var dto = new PcTransmittalDto
            {
                ContractId = contract, Direction = PcTransmittalDirection.Incoming, TransmittalDate = DateTime.Today,
                Lines = new List<PcTransmittalLineDto> { new PcTransmittalLineDto { DeliverableId = 0, Purpose = PcTransmittalPurpose.ForInformation, NewDocument = new PcReceivedDocumentDto { DocumentNumber = "CL-001", Title = "client spec", Revision = "R2", DocumentId = recvFile, FileName = "spec.pdf" } } },
            };
            var t = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(dto, default));
            Assert.IsTrue(t.Success, t.Message);
            Assert.AreEqual("R2", t.Data.Lines.Single().Revision);
            var d = host.Read(u => u.Set<PcEngineeringDeliverable>().Single(x => x.DocumentNumber == "CL-001"));
            Assert.IsTrue(d.ReceivedFromClient);
            Assert.IsTrue(d.ClientSubmittal);
            var revs = host.Read(u => u.Set<PcDocumentRevision>().Where(r => r.DeliverableId == d.Id).ToList());
            Assert.AreEqual(1, revs.Count);
            Assert.AreEqual(PcDocStatus.UnderReview, revs[0].Status, "a received copy waits for a second person; it is not released by the receiver");

            // the second receipt of the same number brings a new revision; the first copy nobody accepted is replaced
            dto.Lines[0].NewDocument.Revision = "R3";
            var t2 = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(dto, default));
            Assert.IsTrue(t2.Success, t2.Message);
            revs = host.Read(u => u.Set<PcDocumentRevision>().Where(r => r.DeliverableId == d.Id).OrderBy(r => r.Sequence).ToList());
            Assert.AreEqual(2, revs.Count);
            Assert.AreEqual(PcDocStatus.Superseded, revs[0].Status);
            Assert.AreEqual(PcDocStatus.UnderReview, revs[1].Status);
            // a received document gets no internal revision
            Assert.IsFalse((await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = d.Id, Reason = "x" }, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ Excel

        [TestMethod]
        public async Task TheMdrTemplateImportsAndTheExportReadsBack()
        {
            AppHost host = NewHost("xlsx");
            int contract = NewContract(host);
            var template = await Edms(host, Dcc, s => s.ExportMdrAsync(contract, false, null, true, default));
            Assert.IsTrue(template.Success);
            var imported = await Edms(host, Dcc, s => s.ImportMdrAsync(contract, template.Data, default));
            Assert.IsTrue(imported.Success, imported.Message);
            Assert.AreEqual(1, imported.Data.Created);
            // the same file again updates the line
            var again = await Edms(host, Dcc, s => s.ImportMdrAsync(contract, template.Data, default));
            Assert.AreEqual(0, again.Data.Created);
            Assert.AreEqual(1, again.Data.Updated);
            var export = await Edms(host, Pmo, s => s.ExportMdrAsync(contract, false, null, false, default));
            Assert.IsTrue(export.Data.Length > 100);
            Assert.IsFalse((await Edms(host, Dcc, s => s.ImportMdrAsync(contract, new byte[] { 1, 2, 3 }, default))).Success);
            Assert.IsFalse((await Edms(host, Operator, s => s.ImportMdrAsync(contract, template.Data, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ tender register

        [TestMethod]
        public async Task TenderDocumentsHaveTheirOwnSeriesAndThePackageLinksToTheContractOnlyWhenWon()
        {
            AppHost host = NewHost("tender");
            int contract = NewContract(host);
            var pkg = await Edms(host, Commercial, s => s.SaveTenderPackageAsync(new PcTenderPackageSaveDto { Title = "technical proposal" }, default));
            Assert.IsTrue(pkg.Success, pkg.Message);
            StringAssert.StartsWith(pkg.Data.Code, "TP");
            var withDoc = await Edms(host, Commercial, s => s.SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = pkg.Data.Id, Title = "general arrangement", Discipline = "Mechanical" }, default));
            Assert.IsTrue(withDoc.Success, withDoc.Message);
            var number = withDoc.Data.Documents.Single().Number;
            StringAssert.StartsWith(number, "TD");
            int docId = withDoc.Data.Documents.Single().Id;
            Assert.IsFalse((await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "link-contract", ContractId = contract }, default))).Success, "not won yet");
            Assert.IsTrue((await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "issue", DocumentId = docId }, default))).Success);
            // an issued document is not edited, only voided with a reason
            Assert.IsFalse((await Edms(host, Commercial, s => s.SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { Id = docId, PackageId = pkg.Data.Id, Title = "changed" }, default))).Success);
            Assert.IsFalse((await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "void", DocumentId = docId }, default))).Success);
            Assert.IsTrue((await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "void", DocumentId = docId, Reason = "superseded by new package" }, default))).Success);
            Assert.IsTrue((await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "win" }, default))).Success);
            var linked = await Edms(host, Commercial, s => s.TenderActionAsync(new PcTenderActionDto { Id = pkg.Data.Id, Action = "link-contract", ContractId = contract }, default));
            Assert.IsTrue(linked.Success, linked.Message);
            Assert.AreEqual(contract, linked.Data.ContractId);
            Assert.AreEqual(1, (await Edms(host, Pmo, s => s.TendersOfContractAsync(contract, default))).Data.Count);
            Assert.AreEqual(1, host.Read(u => u.Set<PcTenderDocument>().IgnoreQueryFilters().Count(d => d.Deleted)), "the voided document is kept");
            Assert.IsFalse((await Edms(host, Operator, s => s.SaveTenderPackageAsync(new PcTenderPackageSaveDto { Title = "x" }, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ distribution, notices and the approver matrix

        [TestMethod]
        public async Task ReleaseNotifiesTheDistributionListAndTheReceiverAcknowledges()
        {
            AppHost host = NewHost("notice");
            int doc = NewDeliverable(host, NewContract(host));
            Assert.IsTrue((await Edms(host, Pmo, s => s.SaveDistributionAsync(new PcDistributionSaveDto { Discipline = "Mechanical", UserId = 35, IsActive = true }, default))).Success);
            Assert.IsFalse((await Edms(host, Operator, s => s.SaveDistributionAsync(new PcDistributionSaveDto { Discipline = "Mechanical", UserId = 35, IsActive = true }, default))).Success);
            await ReleasedRevision(host, doc);
            var mine = await Edms(host, Operator, s => s.GetNoticesAsync(new PcDocNoticeQuery { MineOnly = true, OpenOnly = true }, default));
            Assert.AreEqual(1, mine.Data.Count);
            int id = mine.Data.First().Id;
            // somebody else cannot acknowledge it
            Assert.IsFalse((await Edms(host, Dcc, s => s.AcknowledgeAsync(id, default))).Success);
            Assert.IsTrue((await Edms(host, Operator, s => s.AcknowledgeAsync(id, default))).Success);
            Assert.IsFalse((await Edms(host, Operator, s => s.AcknowledgeAsync(id, default))).Success);
            Assert.AreEqual(0, (await Edms(host, Operator, s => s.GetNoticesAsync(new PcDocNoticeQuery { MineOnly = true, OpenOnly = true }, default))).Data.Count);
        }

        [TestMethod]
        public async Task TheApproverMatrixOfADisciplineDecidesWhoReviewsAndApproves()
        {
            AppHost host = NewHost("matrix");
            int doc = NewDeliverable(host, NewContract(host));
            // only the PMO edits the matrix
            Assert.IsFalse((await Edms(host, Approver, s => s.SaveApproverAsync(new PcApproverSaveDto { Discipline = "Mechanical", UserId = 33, CanReview = true, CanApprove = true, IsActive = true }, default))).Success);
            Assert.IsTrue((await Edms(host, Pmo, s => s.SaveApproverAsync(new PcApproverSaveDto { Discipline = "Mechanical", UserId = 32, CanReview = true, CanApprove = false, IsActive = true }, default))).Success);
            Assert.IsTrue((await Edms(host, Pmo, s => s.SaveApproverAsync(new PcApproverSaveDto { Discipline = "Mechanical", UserId = 33, CanReview = false, CanApprove = true, IsActive = true }, default))).Success);
            PcRevisionDto r = await Draft(host, doc);
            await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "submit" }, default));
            // the matrix names user 32 as reviewer and user 33 as approver: a DCC person (the fallback reviewer) is no longer a reviewer of this discipline
            Assert.IsFalse((await Edms(host, Dcc2, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "review" }, default))).Success);
            Assert.IsTrue((await Edms(host, Reviewer, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "review" }, default))).Success);
            Assert.IsFalse((await Edms(host, Reviewer, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
            Assert.IsTrue((await Edms(host, Approver, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Id, Action = "approve" }, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ compare and obsolete

        [TestMethod]
        public async Task CompareShowsTheMetadataDifferencesAndObsoleteNeedsAReason()
        {
            AppHost host = NewHost("compare");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto a = await ReleasedRevision(host, doc);
            var b = await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "wall thickness changed" }, default));
            var cmp = await Edms(host, Pmo, s => s.CompareAsync(a.Id, b.Data.Id, default));
            Assert.IsTrue(cmp.Success, cmp.Message);
            Assert.IsTrue(cmp.Data.Differences.Any(x => x.Field.Length > 0));
            Assert.IsFalse((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = a.Id, Action = "obsolete" }, default))).Success);
            var ok = await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = a.Id, Action = "obsolete", Note = "design withdrawn" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(PcDocStatus.Obsolete, ok.Data.Status);
        }

        [TestMethod]
        public async Task ADocumentWithoutAReadRoleIsRefused()
        {
            AppHost host = NewHost("roles");
            int doc = NewDeliverable(host, NewContract(host));
            var guest = AppHost.Principal(99, "guest", "SALES");
            Assert.IsFalse((await Edms(host, guest, s => s.GetHistoryAsync(doc, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ fix round (independent review)

        private static readonly ClaimsPrincipal Auditor = AppHost.Principal(38, "auditor.one", "AUDITOR");
        private static readonly ClaimsPrincipal Production = AppHost.Principal(39, "production.one", "PRODUCTIONMANAGER");

        private static async Task<PcReleaseGate> Gate(AppHost host, int orderId)
        {
            var r = await host.As(Pmo, p => p.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().ReleaseCheckAsync(orderId, default));
            Assert.IsTrue(r.Success, r.Message);
            var c = r.Data.Checks.Single(x => x.Code == "drawings");
            return new PcReleaseGate { Passed = c.Passed, Message = c.Message };
        }

        private sealed class PcReleaseGate { public bool Passed; public string Message; }

        private static Task Link(AppHost host, int deliverable, string part) =>
            Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = deliverable, Kind = PcDocLinkKind.PartNumber, PartNumber = part }, default));

        private static async Task<int> Receive(AppHost host, int contract, string number, string revision)
        {
            Guid f = NewFile(host);
            var dto = new PcTransmittalDto
            {
                ContractId = contract, Direction = PcTransmittalDirection.Incoming, TransmittalDate = DateTime.Today,
                Lines = new List<PcTransmittalLineDto> { new PcTransmittalLineDto { DeliverableId = 0, Purpose = PcTransmittalPurpose.ForInformation, NewDocument = new PcReceivedDocumentDto { DocumentNumber = number, Title = "client doc", Revision = revision, DocumentId = f, FileName = "c.pdf" } } },
            };
            var t = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(dto, default));
            Assert.IsTrue(t.Success, t.Message);
            return host.Read(u => u.Set<PcEngineeringDeliverable>().Where(x => x.DocumentNumber == number).Select(x => x.Id).Single());
        }

        [TestMethod]
        public async Task AReceivedClientDocumentWaitsForASecondHumanAndStaysOutOfTheShopFloorAndTheGate()
        {
            AppHost host = NewHost("fx_recv");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-RCV");
            int doc = await Receive(host, contract, "CL-777", "R1");
            await Link(host, doc, "PN-RCV");
            PcDocumentRevision rev = host.Read(u => u.Set<PcDocumentRevision>().Single(r => r.DeliverableId == doc));
            Assert.AreEqual(PcDocStatus.UnderReview, rev.Status);
            Assert.IsNull(rev.ReleasedAt);
            // not in force: nothing on the floor, and the linked document blocks the gate
            Assert.AreEqual(0, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default))).Data.Count);
            Assert.IsFalse((await Gate(host, o.OrderId)).Passed);
            // the receiver cannot accept it, an AI seat cannot, a second human can
            Assert.IsFalse((await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = rev.Id, Action = "release" }, default))).Success);
            var seat = new AiActingUser { UserId = 91, UserName = "seat.accept", Roles = new[] { "PMO", "DCC" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "DOCS", sp => sp.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = rev.Id, Action = "release" }, default));
            Assert.IsFalse(ai.Success);
            Assert.IsTrue((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = rev.Id, Action = "release" }, default))).Success);
            Assert.AreEqual(1, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default))).Data.Count);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed);
        }

        [TestMethod]
        public async Task ASecondReceiptReplacesTheCopyNobodyAcceptedAndPointsToIt()
        {
            AppHost host = NewHost("fx_recv2");
            int contract = NewContract(host);
            int doc = await Receive(host, contract, "CL-888", "R1");
            await Receive(host, contract, "CL-888", "R2");
            var revs = host.Read(u => u.Set<PcDocumentRevision>().Where(r => r.DeliverableId == doc).OrderBy(r => r.Sequence).ToList());
            Assert.AreEqual(PcDocStatus.Superseded, revs[0].Status);
            Assert.AreEqual(revs[1].Id, revs[0].SupersededByRevisionId);
            Assert.AreEqual(PcDocStatus.UnderReview, revs[1].Status);
            Assert.IsTrue(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.RevisionId == revs[0].Id && e.Kind == PcDocEventKind.Superseded)));
        }

        [TestMethod]
        public async Task WhoAddedTheFileOrSubmittedNeverReviewsOrApprovesIt()
        {
            AppHost host = NewHost("fx_maker");
            int doc = NewDeliverable(host, NewContract(host));
            var r = await Edms(host, Dcc, s => s.CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "x" }, default));
            Guid f = NewFile(host);
            Assert.IsTrue((await Edms(host, Dcc2, s => s.AddFileAsync(new PcRevisionFileAddDto { RevisionId = r.Data.Id, DocumentId = f, FileName = "a.pdf" }, default))).Success);
            Assert.IsTrue((await Edms(host, Dcc, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Data.Id, Action = "submit" }, default))).Success);
            // Dcc2 uploaded the content: he may not review it, although his role could
            Assert.IsFalse((await Edms(host, Dcc2, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Data.Id, Action = "review" }, default))).Success);
            Assert.IsTrue((await Edms(host, Reviewer, s => s.ActionAsync(new PcRevisionActionDto { Id = r.Data.Id, Action = "review" }, default))).Success);
        }

        [TestMethod]
        public async Task TheFloorRolesDoNotReadTheHistoryOfADocumentOnlyTheReleasedRevision()
        {
            AppHost host = NewHost("fx_read");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto r = await Draft(host, doc);
            foreach (ClaimsPrincipal floor in new[] { Operator, Production })
            {
                Assert.IsFalse((await Edms(host, floor, s => s.GetHistoryAsync(doc, default))).Success);
                Assert.IsFalse((await Edms(host, floor, s => s.GetRevisionAsync(r.Id, default))).Success);
                Assert.IsFalse((await Edms(host, floor, s => s.ExportMdrAsync(1, false, null, true, default))).Success);
            }
            Assert.IsTrue((await Edms(host, Auditor, s => s.GetHistoryAsync(doc, default))).Success);
        }

        [TestMethod]
        public async Task AfterTheFirstReleaseTheIdentityOfTheDocumentIsFrozen()
        {
            AppHost host = NewHost("fx_freeze");
            int contract = NewContract(host), doc = NewDeliverable(host, contract, number: "FRZ-1");
            await ReleasedRevision(host, doc);
            var mdr = new Func<string, string, Task<ResponseDto<PcDeliverableDto>>>((title, discipline) => host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().SaveDeliverableAsync(
                new PcDeliverableDto { Id = doc, ContractId = contract, DocumentNumber = "FRZ-1", Title = title, Discipline = discipline, DocumentType = null, ClientSubmittal = false, Required = true, ProgressWeight = 1m }, default)));
            var d0 = host.Read(u => u.Set<PcEngineeringDeliverable>().Single(x => x.Id == doc));
            Assert.IsFalse((await mdr("another title", d0.Discipline)).Success);
            Assert.IsFalse((await mdr(d0.Title, "Electrical")).Success);
            // the legacy issue actions are closed once revisions exist
            var first = await host.As(Dcc, p => p.GetRequiredService<IPcMdrService>().DeliverableActionAsync(new PcDeliverableActionDto { Id = doc, Action = "first-issue", Revision = "Z" }, default));
            Assert.IsFalse(first.Success);
        }

        [TestMethod]
        public async Task AnAsBuiltRevisionIsStillInForceAndIsSupersededByTheNextRelease()
        {
            AppHost host = NewHost("fx_asbuilt");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-AB");
            int doc = NewDeliverable(host, contract);
            await Link(host, doc, "PN-AB");
            PcRevisionDto r0 = await ReleasedRevision(host, doc);
            Assert.IsTrue((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = r0.Id, Action = "as-built" }, default))).Success);
            var docs = await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default));
            Assert.AreEqual(1, docs.Data.Count);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed);
            PcRevisionDto r1 = await Draft(host, doc);
            await Release(host, r1.Id);
            var revs = host.Read(u => u.Set<PcDocumentRevision>().Where(r => r.DeliverableId == doc).OrderBy(r => r.Sequence).ToList());
            Assert.AreEqual(PcDocStatus.Superseded, revs[0].Status);
            Assert.AreEqual(revs[1].Id, revs[0].SupersededByRevisionId);
            Assert.AreEqual(PcDocStatus.Released, revs[1].Status);
        }

        [TestMethod]
        public async Task AnObsoleteDocumentLosesItsLinksAndNoLongerBlocksTheGate()
        {
            AppHost host = NewHost("fx_obs");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-OB");
            int doc = NewDeliverable(host, contract);
            await Link(host, doc, "PN-OB");
            PcRevisionDto r0 = await ReleasedRevision(host, doc);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed);
            Assert.IsTrue((await Edms(host, Pmo, s => s.ActionAsync(new PcRevisionActionDto { Id = r0.Id, Action = "obsolete", Note = "replaced by the client" }, default))).Success);
            ActiveNoDrawing(host, o.ItemId);
            Assert.AreEqual(0, host.Read(u => u.Set<PcDocumentLink>().Count(l => l.DeliverableId == doc && !l.Deleted)));
            Assert.AreEqual(0, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default))).Data.Count);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed, "a retired document does not hold the order");
        }

        [TestMethod]
        public async Task TheReleaseGateWantsAReleasedRevisionOfTheLinkedDocument()
        {
            AppHost host = NewHost("fx_gate");
            int contract = NewContract(host);
            var o = NewOrder(host, contract, "PN-GT");
            int doc = NewDeliverable(host, contract);
            await Link(host, doc, "PN-GT");
            await Draft(host, doc);
            var blocked = await Gate(host, o.OrderId);
            Assert.IsFalse(blocked.Passed);
            StringAssert.Contains(blocked.Message, "نسخه‌ی منتشرشده");
            PcDocumentRevision rev = host.Read(u => u.Set<PcDocumentRevision>().Single(r => r.DeliverableId == doc));
            await Release(host, rev.Id);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed);
            // a legacy line without any revision counts when it was approved
            int legacy = NewDeliverable(host, contract);
            await Link(host, legacy, "PN-GT");
            Assert.IsFalse((await Gate(host, o.OrderId)).Passed);
            host.Write(u => { u.Set<PcEngineeringDeliverable>().Single(d => d.Id == legacy).IssueState = PcIssueState.Approved; u.SaveChanges(); }, Seeder);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed);
        }

        [TestMethod]
        public async Task ADraftOfAnotherProjectDoesNotBlockButAReleasedPartDrawingOfAnotherProjectCounts()
        {
            AppHost host = NewHost("fx_scope");
            int mine = NewContract(host), other = NewContract(host);
            var o = NewOrder(host, mine, "PN-SC");
            int foreignDraft = NewDeliverable(host, other), foreignReleased = NewDeliverable(host, other);
            await Link(host, foreignDraft, "PN-SC");
            await Link(host, foreignReleased, "PN-SC");
            await Draft(host, foreignDraft);
            ActiveNoDrawing(host, o.ItemId);
            Assert.IsTrue((await Gate(host, o.OrderId)).Passed, "a draft of another project is not this order's concern");
            await ReleasedRevision(host, foreignReleased);
            Assert.AreEqual(1, (await Edms(host, Operator, s => s.GetShopDocumentsAsync(o.OrderId, null, default))).Data.Count, "a released part drawing is company-wide");
        }

        [TestMethod]
        public async Task ALinkCannotBeVoidedWhileSomebodyElseHoldsTheCheckOutAndAFileRemovalHasItsOwnEvent()
        {
            AppHost host = NewHost("fx_lock");
            int doc = NewDeliverable(host, NewContract(host));
            var link = await Edms(host, Dcc, s => s.SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-LK" }, default));
            Assert.IsTrue((await Edms(host, Dcc, s => s.CheckOutAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success);
            Assert.IsFalse((await Edms(host, Approver, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id, Reason = "no longer needed for the part" }, default))).Success, "somebody else holds the check-out");
            Assert.IsTrue((await Edms(host, Dcc, s => s.CheckInAsync(new PcCheckOutDto { DeliverableId = doc }, default))).Success);
            Assert.IsTrue((await Edms(host, Approver, s => s.VoidLinkAsync(new PcDocLinkVoidDto { Id = link.Data.Id, Reason = "no longer needed for the part" }, default))).Success);
            PcRevisionDto r = await Draft(host, doc);
            int fileId = r.Files.Count > 0 ? r.Files[0].Id : (await Edms(host, Dcc, s => s.GetRevisionAsync(r.Id, default))).Data.Files[0].Id;
            Assert.IsTrue((await Edms(host, Dcc, s => s.RemoveFileAsync(new PcRevisionFileRemoveDto { FileId = fileId, Reason = "wrong file" }, default))).Success);
            Assert.IsTrue(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.RevisionId == r.Id && e.Kind == PcDocEventKind.FileRemoved)));
        }

        [TestMethod]
        public async Task TheControlledCopyStampCarriesAShamsiDate()
        {
            AppHost host = NewHost("fx_stamp");
            int doc = NewDeliverable(host, NewContract(host));
            PcRevisionDto rev = await ReleasedRevision(host, doc);
            var peek = await Edms(host, Operator, s => s.ControlledCopyAsync("revision", rev.Id, null, false, default));
            StringAssert.Contains(peek.Data.StampText, "/");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(peek.Data.StampText, @"\b1[34]\d\d/\d\d/\d\d\b"), peek.Data.StampText);
            Assert.IsFalse(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.RevisionId == rev.Id && e.Kind == PcDocEventKind.Printed)), "reading the stamp writes nothing");
            await Edms(host, Operator, s => s.ControlledCopyAsync("revision", rev.Id, null, true, default));
            Assert.IsTrue(host.Read(u => u.Set<PcDocumentEvent>().Any(e => e.RevisionId == rev.Id && e.Kind == PcDocEventKind.Printed)));
        }
    }
}
