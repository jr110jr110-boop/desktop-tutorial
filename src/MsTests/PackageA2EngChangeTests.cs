using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Entities.Identity;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.EngChange;
using Services.NasbNiro.Production;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.EngChangeDtos;
using ViewModels.NasbNiro.ProductBankDtos;

namespace MsTests
{
    /// <summary>
    /// Package A2: engineering change request and order (computed impact, dispositions, two distinct approvers ENGINEERING + QA, notifications), the gates on BOM release / obsolete, effectivity,
    /// the frozen legacy BOM and the components that come from the project baseline.
    /// </summary>
    [TestClass]
    public class PackageA2EngChangeTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Raiser = AppHost.Principal(61, "a2.raiser", "ENGINEER");
        private static readonly ClaimsPrincipal EngMgr = AppHost.Principal(62, "a2.engmgr", "ENGINEERING");
        private static readonly ClaimsPrincipal EngMgr2 = AppHost.Principal(63, "a2.engmgr2", "ENGINEERING");
        private static readonly ClaimsPrincipal Qa = AppHost.Principal(64, "a2.qa", "QA");
        private static readonly ClaimsPrincipal Qa2 = AppHost.Principal(65, "a2.qa2", "QA");
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(66, "a2.maker", "ENGINEER");
        private static readonly ClaimsPrincipal Prep = AppHost.Principal(71, "a2.prep", "ENGINEERING");
        private static readonly ClaimsPrincipal Chk = AppHost.Principal(72, "a2.chk", "ENGINEERING");
        private static readonly ClaimsPrincipal QcUser = AppHost.Principal(73, "a2.qc", "QC");
        private static readonly ClaimsPrincipal Mfg = AppHost.Principal(74, "a2.mfg", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Appr = AppHost.Principal(1, "a2.pmo1", "PMO");
        private static readonly ClaimsPrincipal Rel = AppHost.Principal(2, "a2.pmo2", "PMO");

        private const string PartA = "99.11.00.000.2";
        private const string PartB = "99.12.00.000.2";
        private const string Comp1 = "99.11.01.001.2";
        private const string Comp2 = "99.11.01.002.2";

        private static AppHost NewHost(string name)
        {
            var host = new AppHost("A2_" + name + "_" + Guid.NewGuid().ToString("N")).WithoutReadinessGate();
            host.Write(u =>
            {
                foreach (int id in new[] { 61, 62, 63, 64, 65, 66, 71, 72, 73, 74 }) AppHost.Add(u, new User { Id = id, UserName = "a2.user" + id, Email = "a2.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            });
            return host;
        }

        private static Task<T> Run<T>(AppHost host, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => host.As(who, call);

        private static SavePbBomLineDto Line(int no, string pn, decimal qty) =>
            new SavePbBomLineDto { ItemNumber = no, PartNumber = pn, PartName = "part " + pn, Qty = qty, Unit = "عدد", Material = "WCB", MakeOrBuy = PbMakeOrBuy.Make, NetWeight = 10m, GrossWeight = 12m };

        private static async Task<PbBomDto> SignAndReleaseAsync(AppHost host, int id, bool expectRelease = true)
        {
            Assert.IsTrue((await Run(host, Prep, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = id }, Ct))).Success);
            foreach (var (who, step) in new[] { (Chk, PbBomSignature.Checked), (QcUser, PbBomSignature.QcChecked), (Mfg, PbBomSignature.ManufacturingChecked), (Appr, PbBomSignature.Approved) })
            {
                var s = await Run(host, who, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = step }, Ct));
                Assert.IsTrue(s.Success, step + ": " + s.Message);
            }
            var rel = await Run(host, Rel, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = id }, Ct));
            if (expectRelease) Assert.IsTrue(rel.Success, rel.Message);
            return rel.Data;
        }

        private static async Task<PbBomDto> ReleaseFirstAsync(AppHost host, string assembly, params SavePbBomLineDto[] lines)
        {
            var saved = await Run(host, Prep, p => p.GetRequiredService<IPbBomService>().SaveAsync(new SavePbBomDto { AssemblyPartNumber = assembly, Title = "BOM " + assembly, Lines = lines.ToList() }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            return await SignAndReleaseAsync(host, saved.Data.Id);
        }

        /// <summary>A new draft revision of a released BOM, ready to be signed.</summary>
        private static async Task<PbBomDto> DraftRevisionAsync(AppHost host, int fromId)
        {
            var rev = await Run(host, Prep, p => p.GetRequiredService<IPbBomService>().NewRevisionAsync(new PbBomActionDto { Id = fromId, Note = "تغییر آزمون" }, Ct));
            Assert.IsTrue(rev.Success, rev.Message);
            return rev.Data;
        }

        /// <summary>The whole route to an approved ECO that names the BOM revision: request, order, change, impact, dispositions, effectivity, two approvals.</summary>
        private static async Task<EcOrderDto> ApprovedEcoAsync(AppHost host, int bomId, EcChangeAction action = EcChangeAction.NewRevision, string part = PartA, bool approve = true)
        {
            var ecr = await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().SaveRequestAsync(new SaveEcRequestDto
            {
                Title = "تغییر آزمون", Reason = "دلیل تغییر برای آزمون خودکار", Classification = EcClass.ClassI, Urgency = EcUrgency.Normal,
                Targets = new List<EcTargetDto> { new EcTargetDto { Kind = EcTargetKind.Part, PartNumber = part } },
            }, Ct));
            Assert.IsTrue(ecr.Success, ecr.Message);
            Assert.IsTrue((await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "submit" }, Ct))).Success);
            var ap = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "approve" }, Ct));
            Assert.IsTrue(ap.Success, ap.Message);
            var eco = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveOrderAsync(new SaveEcOrderDto { RequestId = ecr.Data.Id, EffectiveFromDate = DateTime.Today, EffectiveFromSerial = "SN-0100" }, Ct));
            Assert.IsTrue(eco.Success, eco.Message);
            int id = eco.Data.Id;
            var ch = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().AddChangeAsync(new EcOrderChangeSaveDto { OrderId = id, Kind = EcChangeKind.Bom, TargetId = bomId, Action = action }, Ct));
            Assert.IsTrue(ch.Success, ch.Message);
            var imp = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            Assert.IsTrue(imp.Success, imp.Message);
            foreach (EcDispositionDto d in imp.Data.Dispositions.Where(d => !d.Stale))
            {
                var s = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveDispositionAsync(new EcDispositionSaveDto
                {
                    OrderId = id, ImpactKey = d.ImpactKey, Disposition = EcDisposition.UseAsIs, Note = "بدون اثر بر کارکرد، تایید شده", OwnerUserId = 66, DueDate = DateTime.Today.AddDays(5),
                }, Ct));
                Assert.IsTrue(s.Success, s.Message);
            }
            var sub = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "submit" }, Ct));
            Assert.IsTrue(sub.Success, sub.Message);
            if (!approve) return sub.Data;
            Assert.IsTrue((await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success);
            var fin = await Run(host, Qa, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-qa" }, Ct));
            Assert.IsTrue(fin.Success, fin.Message);
            Assert.AreEqual(EcoStatus.Approved, fin.Data.Status);
            return fin.Data;
        }

        // ------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void Effectivity_BySerialOrderAndDate_EarliestPointWins_AndStraddleIsRefused()
        {
            var bySerial = new EcOrder { EffectiveFromSerial = "SN-0100" };
            Assert.AreEqual(EcPosition.After, EcRules.Position(bySerial, "PO-1", new[] { "SN-0100", "SN-0101" }, DateTime.Today));
            Assert.AreEqual(EcPosition.Before, EcRules.Position(bySerial, "PO-1", new[] { "SN-0050", "SN-0099" }, DateTime.Today));
            Assert.AreEqual(EcPosition.Straddle, EcRules.Position(bySerial, "PO-1", new[] { "SN-0099", "SN-0100" }, DateTime.Today));
            Assert.AreEqual(EcPosition.Before, EcRules.SerialPosition(bySerial, "SN-9", DateTime.Today), "natural comparison: 9 is before 100");
            var byOrder = new EcOrder { EffectiveFromOrderNumber = "PO-MF-000120" };
            Assert.AreEqual(EcPosition.After, EcRules.Position(byOrder, "PO-MF-000121", null, DateTime.Today));
            Assert.AreEqual(EcPosition.Before, EcRules.Position(byOrder, "PO-MF-000119", null, DateTime.Today));
            var byDate = new EcOrder { EffectiveFromDate = new DateTime(2030, 1, 1) };
            Assert.AreEqual(EcPosition.Before, EcRules.Position(byDate, "PO-1", null, new DateTime(2029, 12, 31)));
            Assert.AreEqual(EcPosition.After, EcRules.Position(byDate, "PO-1", null, new DateTime(2030, 1, 1)));
            var both = new EcOrder { EffectiveFromOrderNumber = "PO-MF-000120", EffectiveFromDate = new DateTime(2020, 1, 1) };
            Assert.AreEqual(EcPosition.After, EcRules.Position(both, "PO-MF-000001", null, DateTime.Today), "the date already passed: the earliest stated point applies");
            Assert.AreEqual(EcPosition.NotStated, EcRules.Position(new EcOrder(), "PO-1", null, DateTime.Today));
        }

        [TestMethod]
        public void ApproverRules_NotCreatorNotEditorNotTheOtherApprover()
        {
            Assert.IsNotNull(EcRules.ApproverProblem(5, 5, ",5,", null), "the creator");
            Assert.IsNotNull(EcRules.ApproverProblem(6, 5, ",5,6,", null), "an editor");
            Assert.IsNotNull(EcRules.ApproverProblem(7, 5, ",5,", 7), "the same person twice");
            Assert.IsNull(EcRules.ApproverProblem(8, 5, ",5,", 7));
            Assert.IsFalse(EcRules.IsAllowed(EcImpactCategory.Delivered, EcDisposition.Scrap));
            Assert.IsTrue(EcRules.IsAllowed(EcImpactCategory.Delivered, EcDisposition.RetrofitDeliveredUnit));
        }

        // ------------------------------------------------------------------------------------------ gates

        [TestMethod]
        public async Task SecondBomRevision_NeedsAnApprovedEco_FirstRevisionDoesNot()
        {
            AppHost host = NewHost("gate");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1), Line(2, Comp2, 2));   // a first revision needs no order
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            var refused = await SignAndReleaseAsync(host, r1.Id, false);
            Assert.IsNull(refused, "the release of a second revision is refused without an engineering change order");
            Assert.AreEqual(PbBomStatus.Approved, host.Read(u => u.Set<PbBomHeader>().Single(x => x.Id == r1.Id).Status));

            var gate = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().CheckAsync(EcChangeKind.Bom, r1.Id, EcChangeAction.NewRevision, Ct));
            Assert.IsTrue(gate.Data.Required && !gate.Data.Ok);

            EcOrderDto eco = await ApprovedEcoAsync(host, r1.Id);
            var rel = await Run(host, Rel, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = r1.Id }, Ct));
            Assert.IsTrue(rel.Success, rel.Message);
            Assert.AreEqual(PbBomStatus.Released, rel.Data.Status);
            gate = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().CheckAsync(EcChangeKind.Bom, r1.Id, EcChangeAction.NewRevision, Ct));
            Assert.IsTrue(gate.Data.Ok);
            Assert.AreEqual(eco.Number, gate.Data.OrderNumber);
        }

        [TestMethod]
        public async Task SubmittedOrderIsNotEnough_OnlyAnApprovedOrderOpensTheGate()
        {
            AppHost host = NewHost("gate2");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            await ApprovedEcoAsync(host, r1.Id, approve: false);   // submitted, not approved
            Assert.IsNull(await SignAndReleaseAsync(host, r1.Id, false), "an order that waits for its approvals does not open the gate");
        }

        [TestMethod]
        public async Task Obsolete_NeedsAnEcoAndAnInUseCheck()
        {
            AppHost host = NewHost("obsolete");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            var none = await Run(host, Rel, p => p.GetRequiredService<IPbBomService>().ObsoleteAsync(new PbBomActionDto { Id = r0.Id, Note = "کنار گذاشتن" }, Ct));
            Assert.IsFalse(none.Success, "no order");
            await ApprovedEcoAsync(host, r0.Id, EcChangeAction.Obsolete);
            // after the approval a project took this BOM: the in-use check refuses until the order has a disposition for it
            string bomCode = host.Read(u => u.Set<PbBomHeader>().Single(x => x.Id == r0.Id).Code);
            host.Write(u =>
            {
                AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 9001, ProductId = 1, BomHeaderId = r0.Id, SourceBomCode = bomCode, SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
            });
            var inUse = await Run(host, Rel, p => p.GetRequiredService<IPbBomService>().ObsoleteAsync(new PbBomActionDto { Id = r0.Id, Note = "کنار گذاشتن" }, Ct));
            Assert.IsFalse(inUse.Success);
            StringAssert.Contains(inUse.Message, "در حال استفاده");
        }

        // ------------------------------------------------------------------------------------------ workflow

        [TestMethod]
        public async Task Impact_IsComputedByTheServer_WhereUsedAtAllLevels_AndEveryItemNeedsADisposition()
        {
            AppHost host = NewHost("impact");
            PbBomDto a0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            await ReleaseFirstAsync(host, PartB, Line(1, PartA, 2));
            PbBomDto a1 = await DraftRevisionAsync(host, a0.Id);
            var ecr = await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().SaveRequestAsync(new SaveEcRequestDto
            {
                Title = "t", Reason = "دلیل تغییر برای آزمون خودکار", Targets = new List<EcTargetDto> { new EcTargetDto { Kind = EcTargetKind.Part, PartNumber = PartA } },
            }, Ct));
            await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "submit" }, Ct));
            Assert.IsFalse((await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "approve" }, Ct))).Success, "the originator does not approve the request");
            Assert.IsTrue((await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "approve" }, Ct))).Success);
            var eco = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveOrderAsync(new SaveEcOrderDto { RequestId = ecr.Data.Id, EffectiveFromDate = DateTime.Today }, Ct));
            int id = eco.Data.Id;
            await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().AddChangeAsync(new EcOrderChangeSaveDto { OrderId = id, Kind = EcChangeKind.Bom, TargetId = a1.Id }, Ct));
            var early = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "submit" }, Ct));
            Assert.IsFalse(early.Success, "no impact analysis yet");
            var imp = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            Assert.IsTrue(imp.Success, imp.Message);
            Assert.IsTrue(imp.Data.Impact.HashOk);
            Assert.AreEqual(64, imp.Data.Impact.Sha256.Length);
            Assert.IsTrue(imp.Data.Impact.Items.Any(i => i.Category == EcImpactCategory.WhereUsed && i.PartNumber == PartB), "the BOM of PartB uses PartA");
            Assert.AreEqual(8, imp.Data.Impact.Counts.Count, "all eight lists are shown");
            var blocked = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "submit" }, Ct));
            Assert.IsFalse(blocked.Success, "every impacted item needs a disposition");
            StringAssert.Contains(blocked.Message, "تکلیف");
            // a disposition outside the list is refused, a stable hash comes back when the data did not change
            var key = imp.Data.Dispositions.First(d => d.Category == EcImpactCategory.WhereUsed).ImpactKey;
            var wrong = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveDispositionAsync(new EcDispositionSaveDto { OrderId = id, ImpactKey = key, Disposition = EcDisposition.ReturnToSupplier }, Ct));
            Assert.IsFalse(wrong.Success);
            var again = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            Assert.AreEqual(imp.Data.Impact.Items.Count, again.Data.Impact.Items.Count);
        }

        [TestMethod]
        public async Task Approval_RefusesTheCreatorTheRequesterTheSamePersonTwiceAndAnAiSeat()
        {
            AppHost host = NewHost("approve2");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            EcOrderDto sub = await ApprovedEcoAsync(host, r1.Id, approve: false);
            int id = sub.Id;
            var creatorAsEngineering = AppHost.Principal(66, "a2.maker", "ENGINEERING");
            Assert.IsFalse((await Run(host, creatorAsEngineering, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success, "the creator");
            var requesterAsEngineering = AppHost.Principal(61, "a2.raiser", "ENGINEERING");
            Assert.IsFalse((await Run(host, requesterAsEngineering, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success, "the originator of the request");
            var ai = new Services.NasbNiro.Ai.AiActingUser { UserId = 63, UserName = "ai.eng", DisplayName = "ai", Roles = new[] { "ENGINEERING" } };
            var viaAi = await host.As(EngMgr2, p => Services.NasbNiro.Ai.AiActing.RunAsync(p.GetRequiredService<IServiceScopeFactory>(), ai, "TEST", async sp =>
                await sp.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct)));
            Assert.IsFalse(viaAi.Success, "never an AI seat");
            Assert.IsFalse((await Run(host, Qa, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success, "QA does not give the engineering approval");
            Assert.IsTrue((await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success);
            Assert.IsFalse((await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-qa" }, Ct))).Success, "one person cannot give both");
            Assert.IsFalse((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-qa" }, Ct))).Success, "wrong role");
            var fin = await Run(host, Qa2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-qa" }, Ct));
            Assert.IsTrue(fin.Success, fin.Message);
            Assert.AreEqual(EcoStatus.Approved, fin.Data.Status);
            Assert.AreNotEqual(fin.Data.EngineeringApprovedByName, fin.Data.QaApprovedByName);
        }

        [TestMethod]
        public async Task FinalApproval_NotifiesPmoAndCeo_AndMakesTasksThatCloseOnlyAfterQaVerification()
        {
            AppHost host = NewHost("notify");
            PbBomDto a0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            await ReleaseFirstAsync(host, PartB, Line(1, PartA, 2));
            PbBomDto a1 = await DraftRevisionAsync(host, a0.Id);
            // the ECO with a real task: the where-used BOM of PartB changes to the new revision
            var ecr = await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().SaveRequestAsync(new SaveEcRequestDto
            {
                Title = "t", Reason = "دلیل تغییر برای آزمون خودکار", Targets = new List<EcTargetDto> { new EcTargetDto { Kind = EcTargetKind.Part, PartNumber = PartA } },
            }, Ct));
            await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "submit" }, Ct));
            await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "approve" }, Ct));
            var eco = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveOrderAsync(new SaveEcOrderDto { RequestId = ecr.Data.Id, EffectiveFromOrderNumber = "PO-MF-000500" }, Ct));
            int id = eco.Data.Id;
            await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().AddChangeAsync(new EcOrderChangeSaveDto { OrderId = id, Kind = EcChangeKind.Bom, TargetId = a1.Id }, Ct));
            var imp = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            foreach (var d in imp.Data.Dispositions)
                Assert.IsTrue((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveDispositionAsync(new EcDispositionSaveDto
                { OrderId = id, ImpactKey = d.ImpactKey, Disposition = EcDisposition.ChangeToNewRevision, OwnerUserId = 66, DueDate = DateTime.Today.AddDays(3) }, Ct))).Success);
            Assert.IsTrue((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "submit" }, Ct))).Success);
            Assert.IsTrue(host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == $"ec:eco:{id}" && i.RecipientRole == "QA")), "the missing step is in the inbox of its role");
            Assert.IsTrue((await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-engineering" }, Ct))).Success);
            var fin = await Run(host, Qa, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "approve-qa" }, Ct));
            Assert.IsTrue(fin.Success, fin.Message);
            Assert.IsTrue(host.Read(u => u.Set<OfInboxItem>().Count(i => i.SourceKey == $"ec:eco:info:{id}" && (i.RecipientRole == "PMO" || i.RecipientRole == "CEO"))) == 2, "PMO and CEO are told, they do not approve");
            Assert.IsTrue(fin.Data.Tasks.Count >= 1 && fin.Data.Tasks.All(t => t.OwnerUserId == 66));

            var close = await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "close" }, Ct));
            Assert.IsFalse(close.Success, "open tasks");
            foreach (var t in fin.Data.Tasks)
            {
                Assert.IsTrue((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().TaskActionAsync(new EcTaskActionDto { Id = t.Id, Action = "done", Note = "انجام شد" }, Ct))).Success);
                Assert.IsFalse((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().TaskActionAsync(new EcTaskActionDto { Id = t.Id, Action = "verify" }, Ct))).Success, "only QA verifies");
                Assert.IsTrue((await Run(host, Qa, p => p.GetRequiredService<IEngChangeService>().TaskActionAsync(new EcTaskActionDto { Id = t.Id, Action = "verify" }, Ct))).Success);
            }
            var stillOpen = await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "close" }, Ct));
            Assert.IsFalse(stillOpen.Success, "the named revision is not released yet");
            // release the named revision (the order is approved) and close
            await SignAndReleaseAsync(host, a1.Id);
            var closed = await Run(host, EngMgr2, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "close" }, Ct));
            Assert.IsTrue(closed.Success, closed.Message);
            Assert.AreEqual(EcoStatus.Closed, closed.Data.Status);
        }

        // ------------------------------------------------------------------------------------------ effectivity of an order, legacy freeze, baseline components

        [TestMethod]
        public async Task ProductionOrder_BeforeThePoint_KeepsTheOldRevision_AtOrAfterItNeedsTheNewOne_UnlessCompleteOnOld()
        {
            AppHost host = NewHost("verdict");
            InventoryItem item = null;
            host.Write(u =>
            {
                item = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 777, ProductId = 1, BomHeaderId = 1, SourceBomCode = "BOM-A", SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
            });
            int baselineId = host.Read(u => u.Set<PbProjectBaseline>().Single(b => b.WorkOrderId == 777).Id);
            host.Write(u =>
            {
                var eco = AppHost.Add(u, new EcOrder { Number = "EC-ECO-T1", RequestId = 1, Status = EcoStatus.Approved, AffectedParts = "," + PartA + ",", EffectiveFromSerial = "SN-0100", Title = "t" });
                u.SaveChanges();
                AppHost.Add(u, new EcOrderChange { OrderId = eco.Id, Kind = EcChangeKind.Bom, Action = EcChangeAction.NewRevision, TargetId = 5, Code = "BOM-A", NewRevision = 1 });
                u.SaveChanges();
            });
            int ecoId = host.Read(u => u.Set<EcOrder>().Single(o => o.Number == "EC-ECO-T1").Id);
            var before = new MfProductionOrder { Id = 40, Number = "PO-MF-1", WorkOrderId = 777, SerialsPlanned = "SN-0010\nSN-0011" };
            var after = new MfProductionOrder { Id = 41, Number = "PO-MF-2", WorkOrderId = 777, SerialsPlanned = "SN-0100\nSN-0101" };
            var straddle = new MfProductionOrder { Id = 42, Number = "PO-MF-3", WorkOrderId = 777, SerialsPlanned = "SN-0099\nSN-0100" };
            var v1 = host.Read(u => EcGate.OrderVerdictAsync(u, before, item, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNull(v1.Problem); Assert.IsTrue(v1.StaleRoutingAllowed, "before the point: the old revision stays");
            var v2 = host.Read(u => EcGate.OrderVerdictAsync(u, after, item, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(v2.Problem, "at/after the point on the old revision: refused");
            StringAssert.Contains(v2.Problem, "EC-ECO-T1");
            var v3 = host.Read(u => EcGate.OrderVerdictAsync(u, straddle, item, DateTime.Today, Ct).GetAwaiter().GetResult());
            StringAssert.Contains(v3.Problem, "تقسیم");
            host.Write(u =>
            {
                AppHost.Add(u, new EcDispositionItem { OrderId = ecoId, Category = EcImpactCategory.Order, ImpactKey = "ORDER:41", Text = "x", Disposition = EcDisposition.CompleteOnOldRevision, Note = "تکمیل با نسخه قدیم" });
                u.SaveChanges();
            });
            var v4 = host.Read(u => EcGate.OrderVerdictAsync(u, after, item, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNull(v4.Problem, "the disposition «complete on old revision» allows it");
            Assert.IsTrue(baselineId > 0);
        }

        [TestMethod]
        public async Task LegacyBom_IsReadOnly_UntilTheSafetySwitchIsOff()
        {
            AppHost host = NewHost("freeze");
            var frozen = await Run(host, Appr, p => p.GetRequiredService<Services.NasbNiro.Contracts.IBomService>().AddBomAsync(new ViewModels.NasbNiro.BomDtos.AddBomDto { AssemblyId = 1 }, Ct).AsTask());
            Assert.IsFalse(frozen.Success);
            StringAssert.Contains(frozen.Message, "بانک محصول");
            SafetyTestSupport.SetProductBank(host, PbSettingKeys.LegacyBomFrozen, "false");
            var open = await Run(host, Appr, p => p.GetRequiredService<Services.NasbNiro.Contracts.IBomService>().AddBomAsync(new ViewModels.NasbNiro.BomDtos.AddBomDto { AssemblyId = 1 }, Ct).AsTask());
            Assert.IsFalse(open.Message?.Contains("بانک محصول") == true, "no longer the freeze message");
            Assert.IsTrue(Services.NasbNiro.Integrity.SafetyProfile.Refusal(Entities.Model.NasbNiro.Integrity.SafetyStore.ProductBank, PbSettingKeys.LegacyBomFrozen) != null, "a safety-profile setting: two-person change");
            Assert.IsTrue(Services.NasbNiro.Integrity.SafetyProfile.Refusal(Entities.Model.NasbNiro.Integrity.SafetyStore.ProductBank, PbSettingKeys.DistinctReviewers) != null, "the four BOM signatures setting stays registered");
        }

        [TestMethod]
        public async Task Components_ComeFromTheBaseline_LegacyIsTheFlaggedFallback_AndAChangeNeedsDeviationOrEco()
        {
            AppHost host = NewHost("baseline");
            InventoryItem output = null, comp = null, other = null;
            host.Write(u =>
            {
                output = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                comp = AppHost.Add(u, new InventoryItem { Code = Comp1, Title = "C1", Unit = "عدد", PartNumber = Comp1, IsActive = true });
                other = AppHost.Add(u, new InventoryItem { Code = "OTHER-1", Title = "O", Unit = "عدد", IsActive = true });
                u.SaveChanges();
                var b = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 800, ProductId = 1, BomHeaderId = 1, SourceBomCode = "BOM-A", SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, AssemblyPartNumber = PartA, PartNumber = Comp1, ItemId = comp.Id, PartName = "c", Qty = 3, TotalQty = 3, Unit = "عدد" });
                u.SaveChanges();
            });
            var fromBaseline = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 800, null, output, 2, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(MfBaselineDrafting.SourceBaseline, fromBaseline.Source);
            Assert.AreEqual(1, fromBaseline.Lines.Count);
            Assert.AreEqual(comp.Id, fromBaseline.Lines[0].ItemId);
            Assert.AreEqual(6m, fromBaseline.Lines[0].Quantity, "3 per unit x 2 units");
            var legacy = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 801, null, output, 2, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(MfBaselineDrafting.SourceLegacy, legacy.Source);
            StringAssert.Contains(legacy.Note, "BOM قدیمی");

            var order = new MfProductionOrder { Id = 90, Number = "PO-MF-9", WorkOrderId = 800, ProcessId = 5, AutoDrafted = true, Quantity = 2, ComponentSource = MfBaselineDrafting.SourceBaseline };
            var same = host.Read(u => MfBaselineDrafting.ChangeProblemAsync(u, order, output, new List<(int, decimal)> { (comp.Id, 6m) }, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNull(same, "the list the master gives is accepted");
            var changed = host.Read(u => MfBaselineDrafting.ChangeProblemAsync(u, order, output, new List<(int, decimal)> { (other.Id, 1m) }, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(changed, "a planner cannot replace the components");
            StringAssert.Contains(changed, "انحراف");
        }
    }
}
