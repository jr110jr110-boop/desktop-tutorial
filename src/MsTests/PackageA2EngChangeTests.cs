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
            Assert.AreEqual(imp.Data.Impact.Sha256, again.Data.Impact.Sha256, "the fingerprint depends on the data only (not on the time of the computation)");
            var third = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            Assert.AreEqual(imp.Data.Impact.Sha256, third.Data.Impact.Sha256);
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
            // a project whose baseline stands on the old revision: its project manager (project control terms) is told as well
            host.Write(u =>
            {
                var wo = AppHost.Add(u, new Entities.Model.NasbNiro.Contracts.WorkOrder { Code = "W-A2-NOTIFY", Title = "wo", ContractId = 5 });
                u.SaveChanges();
                AppHost.Add(u, new PbProjectBaseline { WorkOrderId = wo.Id, ProductId = 1, BomHeaderId = a0.Id, SourceBomCode = a0.Code, SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                AppHost.Add(u, new Entities.Model.NasbNiro.ProjectControl.PcContractTerms { ContractId = 5, ProjectManagerUserId = 74 });
                u.SaveChanges();
            });
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
            Assert.IsTrue(host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == $"ec:eco:info:{id}" && i.RecipientUserId == 74)), "the project manager of the affected project is told");
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
            // the delete paths are frozen too (before the switch is turned off)
            SafetyTestSupport.SetProductBank(host, PbSettingKeys.LegacyBomFrozen, "true");
            var del = await Run(host, Appr, p => p.GetRequiredService<Services.NasbNiro.Contracts.IBomService>().DeleteBomAsync(1, Ct).AsTask());
            Assert.IsFalse(del.Success); StringAssert.Contains(del.Message, "بانک محصول");
            var delMany = await Run(host, Appr, p => p.GetRequiredService<Services.NasbNiro.Contracts.IBomService>().DeleteBomCollectionAsync(new List<int> { 1 }, Ct).AsTask());
            Assert.IsFalse(delMany.Success); StringAssert.Contains(delMany.Message, "بانک محصول");
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

        // ------------------------------------------------------------------------------------------ fix round 1

        [TestMethod]
        public async Task Guard_IsOpenedOnlyForTheListedOrder_AndOnlyToTheExactComponentsOfTheNamedRevision()
        {
            AppHost host = NewHost("guardx");
            InventoryItem output = null, c1 = null, c2 = null, c3 = null;
            host.Write(u =>
            {
                output = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                c1 = AppHost.Add(u, new InventoryItem { Code = Comp1, Title = "C1", Unit = "عدد", PartNumber = Comp1, IsActive = true });
                c2 = AppHost.Add(u, new InventoryItem { Code = Comp2, Title = "C2", Unit = "عدد", PartNumber = Comp2, IsActive = true });
                c3 = AppHost.Add(u, new InventoryItem { Code = "99.11.01.003.2", Title = "C3", Unit = "عدد", PartNumber = "99.11.01.003.2", IsActive = true });
                u.SaveChanges();
            });
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            var edit = await Run(host, Prep, p => p.GetRequiredService<IPbBomService>().SaveAsync(new SavePbBomDto { Id = r1.Id, AssemblyPartNumber = PartA, ChangeReason = "تغییر آزمون", Title = "BOM", Lines = new List<SavePbBomLineDto> { Line(1, Comp2, 4) } }, Ct));
            Assert.IsTrue(edit.Success, edit.Message);
            await SignAndReleaseAsync(host, r1.Id, false);
            EcOrderDto eco = await ApprovedEcoAsync(host, r1.Id);
            host.Write(u =>
            {
                var b = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 810, ProductId = 1, BomHeaderId = r0.Id, SourceBomCode = "BOM-A", SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, AssemblyPartNumber = PartA, PartNumber = Comp1, ItemId = c1.Id, PartName = "c", Qty = 1, TotalQty = 1, Unit = "عدد" });
                u.SaveChanges();
            });
            var order = new MfProductionOrder { Id = 91, Number = "PO-MF-91", WorkOrderId = 810, ProcessId = 5, AutoDrafted = true, Quantity = 2, ComponentSource = MfBaselineDrafting.SourceBaseline };
            string Problem(MfProductionOrder o, params (int, decimal)[] want) =>
                host.Read(u => MfBaselineDrafting.ChangeProblemAsync(u, o, output, want.ToList(), DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(Problem(order, (c2.Id, 8m)), "an approved order exists, but it does not list this production order: still refused");
            host.Write(u =>
            {
                AppHost.Add(u, new EcDispositionItem { OrderId = eco.Id, Category = EcImpactCategory.Order, ImpactKey = EcRules.Key("ORDER", 91), Text = "x", Disposition = EcDisposition.ChangeToNewRevision, Note = "به بازنگری جدید" });
                u.SaveChanges();
            });
            Assert.IsNull(Problem(order, (c2.Id, 8m)), "listed with «change to the new revision»: exactly the components of the named revision (4 x 2 units)");
            Assert.IsNotNull(Problem(order, (c3.Id, 8m)), "another item is refused");
            Assert.IsNotNull(Problem(order, (c2.Id, 7m)), "another quantity is refused");
            Assert.IsNotNull(Problem(order, (c2.Id, 8m), (c3.Id, 1m)), "an extra item is refused");
            Assert.IsNotNull(Problem(new MfProductionOrder { Id = 92, Number = "PO-MF-92", WorkOrderId = 810, ProcessId = 5, AutoDrafted = true, Quantity = 2, ComponentSource = MfBaselineDrafting.SourceBaseline }, (c2.Id, 8m)), "another order is not unlocked by this one");
        }

        [TestMethod]
        public async Task AnEcoWithoutAnEffectivityPoint_CannotBeSent()
        {
            AppHost host = NewHost("noeff");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            var ecr = await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().SaveRequestAsync(new SaveEcRequestDto
            {
                Title = "t", Reason = "دلیل تغییر برای آزمون خودکار", Targets = new List<EcTargetDto> { new EcTargetDto { Kind = EcTargetKind.Part, PartNumber = PartA } },
            }, Ct));
            await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "submit" }, Ct));
            Assert.IsTrue((await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().RequestActionAsync(new EcRequestActionDto { Id = ecr.Data.Id, Action = "approve" }, Ct))).Success);
            var eco = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveOrderAsync(new SaveEcOrderDto { RequestId = ecr.Data.Id }, Ct));
            int id = eco.Data.Id;
            Assert.IsTrue((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().AddChangeAsync(new EcOrderChangeSaveDto { OrderId = id, Kind = EcChangeKind.Bom, TargetId = r1.Id }, Ct))).Success);
            var imp = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "refresh-impact" }, Ct));
            foreach (EcDispositionDto d in imp.Data.Dispositions.Where(d => !d.Stale))
                Assert.IsTrue((await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().SaveDispositionAsync(new EcDispositionSaveDto { OrderId = id, ImpactKey = d.ImpactKey, Disposition = EcDisposition.UseAsIs, Note = "بدون اثر بر کارکرد", OwnerUserId = 66, DueDate = DateTime.Today.AddDays(3) }, Ct))).Success);
            var sub = await Run(host, Maker, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = id, Action = "submit" }, Ct));
            Assert.IsFalse(sub.Success);
            StringAssert.Contains(sub.Message, "نقطه اثر");
        }

        [TestMethod]
        public async Task ApprovedEco_CanBeVoided_AndThenNoLongerOpensTheGate()
        {
            AppHost host = NewHost("void");
            PbBomDto r0 = await ReleaseFirstAsync(host, PartA, Line(1, Comp1, 1));
            PbBomDto r1 = await DraftRevisionAsync(host, r0.Id);
            await SignAndReleaseAsync(host, r1.Id, false);
            EcOrderDto eco = await ApprovedEcoAsync(host, r1.Id);
            var byRaiser = await Run(host, Raiser, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = eco.Id, Action = "void", Note = "x" }, Ct));
            Assert.IsFalse(byRaiser.Success, "only ENGINEERING or QA");
            var noReason = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = eco.Id, Action = "void" }, Ct));
            Assert.IsFalse(noReason.Success, "a reason is needed");
            var voided = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().OrderActionAsync(new EcOrderActionDto { Id = eco.Id, Action = "void", Note = "تایید اشتباه بود" }, Ct));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.AreEqual(EcoStatus.Cancelled, voided.Data.Status);
            var gate = await Run(host, EngMgr, p => p.GetRequiredService<IEngChangeService>().CheckAsync(EcChangeKind.Bom, r1.Id, EcChangeAction.NewRevision, Ct));
            Assert.IsFalse(gate.Data.Ok, "a voided order opens nothing");
        }

        [TestMethod]
        public async Task Deviation_LinesWithoutAnAssemblyPartNumber_StillReachTheDraftedOrders()
        {
            AppHost host = NewHost("deviation");
            PbBomDto top = await ReleaseFirstAsync(host, PartA, Line(1, PartB, 1));
            InventoryItem outA = null, outB = null, c1 = null, c2 = null;
            host.Write(u =>
            {
                outA = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                outB = AppHost.Add(u, new InventoryItem { Code = PartB, Title = "B", Unit = "عدد", PartNumber = PartB, IsActive = true });
                c1 = AppHost.Add(u, new InventoryItem { Code = Comp1, Title = "C1", Unit = "عدد", PartNumber = Comp1, IsActive = true });
                c2 = AppHost.Add(u, new InventoryItem { Code = Comp2, Title = "C2", Unit = "عدد", PartNumber = Comp2, IsActive = true });
                u.SaveChanges();
                var b = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 820, ProductId = 1, BomHeaderId = top.Id, SourceBomCode = "BOM-A", SourceBomRevision = 0, IsCurrent = true, Count = 1, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
                var sub = AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, AssemblyPartNumber = PartA, PartNumber = PartB, ItemId = outB.Id, PartName = "b", Qty = 1, TotalQty = 1, Unit = "عدد" });
                u.SaveChanges();
                // lines a deviation added: no AssemblyPartNumber (older rows), one under the sub-assembly occurrence, one directly under the top
                AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, ParentLineId = sub.Id, AssemblyPartNumber = null, PartNumber = Comp2, ItemId = c2.Id, PartName = "c2", Qty = 2, TotalQty = 2, Unit = "عدد" });
                AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, AssemblyPartNumber = null, PartNumber = Comp1, ItemId = c1.Id, PartName = "c1", Qty = 3, TotalQty = 3, Unit = "عدد" });
                u.SaveChanges();
            });
            var forB = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 820, null, outB, 5, Ct).GetAwaiter().GetResult());
            Assert.IsTrue(forB.Lines.Any(l => l.ItemId == c2.Id && l.Quantity == 10m), "the line added under the sub-assembly belongs to it (2 x 5)");
            Assert.IsFalse(forB.Lines.Any(l => l.ItemId == c1.Id), "the top-level line does not leak into the sub-assembly");
            var forA = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 820, null, outA, 2, Ct).GetAwaiter().GetResult());
            Assert.IsTrue(forA.Lines.Any(l => l.ItemId == c1.Id && l.Quantity == 6m), "the line added under the top assembly belongs to it (3 x 2)");
            Assert.IsFalse(forA.Lines.Any(l => l.ItemId == c2.Id), "the sub-assembly's line is not consumed twice by the top assembly");
        }

        [TestMethod]
        public void OpcAndRoutingRevisions_AreRefusedWithoutAnApprovedEco_FirstOnesAreNot_AndANoteIsNotAnExemption()
        {
            AppHost host = NewHost("opcgate");
            InventoryItem item = null;
            host.Write(u =>
            {
                item = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                AppHost.Add(u, new PbOpcRouting { PartNumber = PartA, Title = "OPC", Revision = 0, Status = PbRoutingStatus.Approved, PreparedByUserId = 1 });
                AppHost.Add(u, new PbOpcRouting { PartNumber = PartA, Title = "OPC", Revision = 1, Status = PbRoutingStatus.Draft, PreparedByUserId = 1 });
                AppHost.Add(u, new MfRoutingRevision { ItemId = item.Id, Revision = 1, Status = MfRoutingStatus.Approved });
                // the note a user can type does not exempt a revision any more
                AppHost.Add(u, new MfRoutingRevision { ItemId = item.Id, Revision = 2, Status = MfRoutingStatus.Draft, Note = "از چارت OPC 16.13.02.000.2" });
                u.SaveChanges();
            });
            var opcs = host.Read(u => u.Set<PbOpcRouting>().OrderBy(x => x.Revision).ToList());
            string first = host.Read(u => EcGate.OpcApproveProblemAsync(u, opcs[0], Ct).GetAwaiter().GetResult());
            Assert.IsNull(first, "the first approved chart needs no order");
            string second = host.Read(u => EcGate.OpcApproveProblemAsync(u, opcs[1], Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(second, "the second revision of an approved chart needs an order");
            StringAssert.Contains(second, "دستور تغییر مهندسی");
            var routings = host.Read(u => u.Set<MfRoutingRevision>().OrderBy(x => x.Revision).ToList());
            Assert.IsNull(host.Read(u => EcGate.RoutingRevisionProblemAsync(u, routings[0], Ct).GetAwaiter().GetResult()), "the first approved routing of the item is exempt by fact");
            string r2 = host.Read(u => EcGate.RoutingRevisionProblemAsync(u, routings[1], Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(r2, "revision 2 with a typed note is still refused");
            // an approved order that names the revision opens the gate
            host.Write(u =>
            {
                var req = AppHost.Add(u, new EcRequest { Number = "ECR-T-OPC", Title = "t", Reason = "test change", Status = EcrStatus.Approved, RequestedByUserId = 1 });
                u.SaveChanges();
                var eco = AppHost.Add(u, new EcOrder { Number = "ECO-T-OPC", RequestId = req.Id, Title = "t", Status = EcoStatus.Approved, EffectiveFromDate = DateTime.Today });
                u.SaveChanges();
                u.Set<EcOrderChange>().Add(new EcOrderChange { OrderId = eco.Id, Kind = EcChangeKind.Opc, Action = EcChangeAction.NewRevision, TargetId = opcs[1].Id });
                u.Set<EcOrderChange>().Add(new EcOrderChange { OrderId = eco.Id, Kind = EcChangeKind.Routing, Action = EcChangeAction.NewRevision, TargetId = routings[1].Id });
                u.SaveChanges();
            });
            Assert.IsNull(host.Read(u => EcGate.OpcApproveProblemAsync(u, opcs[1], Ct).GetAwaiter().GetResult()));
            Assert.IsNull(host.Read(u => EcGate.RoutingRevisionProblemAsync(u, routings[1], Ct).GetAwaiter().GetResult()));
        }

        [TestMethod]
        public void PlannedSerials_CannotBeRewrittenAcrossTheEffectivityPoint()
        {
            AppHost host = NewHost("serials");
            InventoryItem item = null;
            host.Write(u =>
            {
                item = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                var eco = AppHost.Add(u, new EcOrder { Number = "EC-ECO-S1", RequestId = 1, Status = EcoStatus.Approved, AffectedParts = "," + PartA + ",", EffectiveFromSerial = "SN-0100", Title = "t" });
                u.SaveChanges();
                AppHost.Add(u, new EcOrderChange { OrderId = eco.Id, Kind = EcChangeKind.Bom, Action = EcChangeAction.NewRevision, TargetId = 5, Code = "BOM-A", NewRevision = 1 });
                u.SaveChanges();
            });
            var order = new MfProductionOrder { Id = 50, Number = "PO-MF-50", WorkOrderId = 777, SerialsPlanned = "SN-0010\nSN-0011" };
            string P(string planned) => host.Read(u => EcGate.SerialsChangeProblemAsync(u, order, item, planned, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.IsNull(P("SN-0011\nSN-0010"), "the same serials");
            Assert.IsNull(P("SN-0010\nSN-0012"), "still before the point");
            Assert.IsNotNull(P("SN-0100\nSN-0101"), "moved across the point without the order");
            Assert.IsNotNull(P("SN-0099\nSN-0100"), "straddles the point");
        }

        [TestMethod]
        public async Task Drafting_PlansFromTheBaseline_StageItemsDoNotConsumeTwice_AndTheUnitIsConverted()
        {
            AppHost host = NewHost("plan");
            PbBomDto top = await ReleaseFirstAsync(host, PartA, Line(1, PartB, 1));
            InventoryItem outA = null, plainB = null, stageB = null, c1 = null;
            host.Write(u =>
            {
                outA = AppHost.Add(u, new InventoryItem { Code = PartA, Title = "A", Unit = "عدد", PartNumber = PartA, IsActive = true });
                plainB = AppHost.Add(u, new InventoryItem { Code = PartB, Title = "B", Unit = "عدد", PartNumber = PartB, IsActive = true });
                stageB = AppHost.Add(u, new InventoryItem { Code = PartB + "-S1", Title = "B stage", Unit = "عدد", PartNumber = PartB, StageCode2 = "S1", IsActive = true });
                c1 = AppHost.Add(u, new InventoryItem { Code = Comp1, Title = "C1", Unit = "g", PartNumber = Comp1, IsActive = true });
                u.SaveChanges();
                AppHost.Add(u, new ItemUnitConversion { ItemId = null, FromUnit = "kg", ToUnit = "g", Factor = 1000m, IsActive = true });
                var b = AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 830, ProductId = 1, BomHeaderId = top.Id, SourceBomCode = top.Code, SourceBomRevision = 0, IsCurrent = true, Count = 3, CopiedAt = DateTimeOffset.Now });
                u.SaveChanges();
                var sub = AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, AssemblyPartNumber = PartA, PartNumber = PartB, ItemId = plainB.Id, PartName = "b", Qty = 1, TotalQty = 3, Unit = "عدد", MakeOrBuy = PbMakeOrBuy.Make });
                u.SaveChanges();
                AppHost.Add(u, new PbProjectBomLine { BaselineId = b.Id, ParentLineId = sub.Id, AssemblyPartNumber = PartB, PartNumber = Comp1, ItemId = c1.Id, PartName = "c", Qty = 2, TotalQty = 6, Unit = "kg", MakeOrBuy = PbMakeOrBuy.Purchase });
                u.SaveChanges();
            });
            var (targets, notes) = host.Read(u => MfBaselineDrafting.PlanAsync(u, 830, Ct).GetAwaiter().GetResult());
            Assert.IsNotNull(targets, "a baseline exists: drafting is from the baseline (no legacy Process or Bom is needed)");
            Assert.AreEqual(2, targets.Count, string.Join("|", notes));
            Assert.AreEqual(3m, targets.Single(t => t.PartNumber == PartA).Quantity, "the top assembly: the number of products");
            Assert.AreEqual(3m, targets.Single(t => t.PartNumber == PartB).Quantity, "the make part: total of the work order");
            Assert.IsNull(host.Read(u => MfBaselineDrafting.PlanAsync(u, 831, Ct).GetAwaiter().GetResult()).Targets, "no baseline: the flagged legacy path decides");

            var forPlain = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 830, null, plainB, 3, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(MfBaselineDrafting.SourceBaseline, forPlain.Source);
            Assert.AreEqual(6000m, forPlain.Lines.Single(l => l.ItemId == c1.Id).Quantity, "6 kg become 6000 g of the item");
            var forStage = host.Read(u => MfBaselineDrafting.ExpectedAsync(u, 830, null, stageB, 3, Ct).GetAwaiter().GetResult());
            Assert.IsFalse(forStage.Lines.Any(l => l.ItemId == c1.Id), "the stage item of the same part does not consume the components again");
        }
    }
}
