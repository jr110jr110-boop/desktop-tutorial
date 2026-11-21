using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1f fix round: no dead end after the PMO accepts an incomplete package, debts never born overdue, alarms and gates follow the new order, the instantiate chokepoint waits for the release, coverage and leftovers.</summary>
    [TestClass]
    public class EngineeringE1fFixTests
    {
        private const string Reason = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده";
        private const string OtherReason = "دلیل تازه: تامین‌کننده تاریخ جدیدی برای تحویل نقشه‌ها اعلام کرده و مدارک هنوز نرسیده است";

        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static Task<EnHandoverDto> GetAs(AppHost host, ClaimsPrincipal who, int id) =>
            host.As(who, async p => Ok(await p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct)));


        private static async Task<string> AskPermit(AppHost host, int handover, string reason = Reason, int hours = 24)
        {
            var r = await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = handover, Reason = reason, DebtHours = hours }, Ct));
            return r.Success ? null : r.Message;
        }

        private static Task<EnWaiverDto> Waiver(AppHost host, ClaimsPrincipal who, int id) =>
            host.As(who, async p => Ok(await p.GetRequiredService<IEnHandoverService>().GetWaiverAsync(id, Ct)));

        private static async Task<List<AgentWorkItem>> Discover(AppHost host, string code)
        {
            ClaimsPrincipal seat = await SeatAsync(host);
            return await host.As(seat, async p => await p.GetServices<IAgentStep>().Single(x => x.Code == code).DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), DateTime.Today, Ct));
        }

        /// <summary>A new-design case whose draft is submitted by the manager and accepted by the PMO while both required documents are missing.</summary>
        private static async Task<(int Contract, int CaseId, int Handover)> Accepted(AppHost host)
        {
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            return (contract, caseId, h.Id);
        }

        // ---------------------------------------------------------------- group 1

        [TestMethod]
        public async Task G1_Refresh_In_Accepted_Fills_Only_Missing_Lines_Sends_The_Package_Back_To_The_Pmo_And_Voids_The_Requested_Permit()
        {
            AppHost host = Started("e1ffix_g1refresh");
            (int contract, int caseId, int h) = await Accepted(host);
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h));
            Assert.IsNull(await AskPermit(host, h));
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);

            // the flag: only the manager; engineers and the PMO cannot
            Assert.IsTrue((await GetAs(host, Mgr1, h)).CanRefresh);
            Assert.IsFalse((await GetAs(host, Eng1, h)).CanRefresh);
            Assert.IsFalse((await GetAs(host, Pmo2, h)).CanRefresh);
            Refused(await Act(host, Eng1, "refresh", d => d.Id = h), "مجوز");

            // nothing arrived: nothing changes and the message says so
            Refused(await Act(host, Mgr1, "refresh", d => d.Id = h), "هیچ مدرک تازه");
            Assert.AreEqual(EnHandoverStatus.Accepted, host.Read(u => u.Set<EnHandover>().Single().Status));
            Assert.AreEqual(EnWaiverStatus.Requested, host.Read(u => u.Set<EnWaiver>().Single().Status));

            // one document arrived: that line is filled, the package goes back to Submitted, the permit is voided by the system, the CEO decided nothing
            Deliverable(host, contract, "DWG");
            EnHandoverDto r = Ok(await Act(host, Mgr1, "refresh", d => d.Id = h));
            Assert.AreEqual((int)EnHandoverStatus.Submitted, r.Status);
            Assert.AreEqual(1, r.MissingRequiredCount);
            Assert.IsFalse(r.Complete);
            Assert.AreEqual((int)EnLineState.Available, r.Lines.Single(l => l.DocumentType == "DWG").State);
            Assert.AreEqual((int)EnLineState.Missing, r.Lines.Single(l => l.DocumentType == "DATASHEET").State);
            Assert.AreEqual((int?)2101, host.Read(u => u.Set<EnHandover>().Single().SubmitConfirmedByUserId));
            Assert.AreEqual(EnCaseStatus.HandedOver, host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).Status));
            EnWaiver voided = host.Read(u => u.Set<EnWaiver>().Single());
            Assert.AreEqual(EnWaiverStatus.Void, voided.Status);
            Assert.AreEqual("بسته از حالت پذیرفته‌شده خارج شد", voided.DecisionNote);
            Assert.IsNull(voided.DecidedByUserId, "not a decision of the CEO");
            Assert.AreEqual(0, host.Read(u => u.Set<EnDistribution>().Count(d => !d.Deleted)), "the draft notices are drafted again after the new acceptance");
            EnWaiverDto dto = await Waiver(host, Ceo, permit);
            Assert.IsFalse(dto.CanDecide);
            Assert.AreEqual(0, (await Discover(host, "ENG.WAIVER.DECIDE")).Count);

            // the rounds go on without the CEO: accept again, the manager may now ask a fresh permit
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h));
            Assert.IsTrue((await GetAs(host, Mgr1, h)).CanRequestWaiver);
            Assert.IsNull(await AskPermit(host, h));
            Assert.AreEqual(2, host.Read(u => u.Set<EnWaiver>().Count()));
        }

        [TestMethod]
        public async Task G1_Refresh_That_Completes_The_Package_Needs_No_Permit_And_Never_Swaps_A_Line_That_Had_A_Document()
        {
            AppHost host = Started("e1ffix_g1complete");
            (int contract, int caseId) = await NewDesign(host);
            int dwg = Deliverable(host, contract, "DWG");
            EnHandoverDto draft = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Guid? firstFile = draft.Lines.Single(l => l.DocumentType == "DWG").DocumentId;
            Ok(await Act(host, Mgr1, "submit", d => d.Id = draft.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = draft.Id));
            // a newer file for the already supplied line must NOT replace the one the PMO accepted; only the missing line is filled
            Deliverable(host, contract, "DATASHEET");
            host.Write(u => { u.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().First(x => x.Id == dwg).DocumentId = File(host, 2111); u.SaveChanges(); }, Seeder);
            Assert.IsNull(await AskPermit(host, draft.Id));
            EnHandoverDto r = Ok(await Act(host, Mgr1, "refresh", d => d.Id = draft.Id));
            Assert.IsTrue(r.Complete);
            Assert.AreEqual(0, r.MissingRequiredCount);
            Assert.AreEqual(firstFile, r.Lines.Single(l => l.DocumentType == "DWG").DocumentId, "a line that had a document is never swapped");
            Assert.AreEqual("بسته کامل شد", host.Read(u => u.Set<EnWaiver>().Single().DecisionNote));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = draft.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = draft.Id));
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = draft.Id));
            Assert.AreEqual((int)EnHandoverStatus.Released, rel.Status);
            Assert.AreEqual(0, host.Read(u => u.Set<EnDocumentDebt>().Count()));
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count(w => w.Status == EnWaiverStatus.Approved)), "the CEO approved nothing");
        }

        [TestMethod]
        public async Task G1_Refresh_Is_Refused_For_A_Complete_Or_Released_Package_Or_After_An_Old_Order_Permit_Was_Used()
        {
            AppHost host = Started("e1ffix_g1refused");
            (int contract, int caseId, int h) = await Accepted(host);
            host.Write(u => { u.Set<EnHandover>().Single().WaiverId = 7777; u.SaveChanges(); }, Seeder);
            EnHandoverDto a = await GetAs(host, Mgr1, h);
            Assert.IsFalse(a.CanRefresh);
            StringAssert.Contains(a.RefreshBlockedReason, "قبلاً به کار رفته");
            Refused(await Act(host, Mgr1, "refresh", d => d.Id = h), "قبلاً به کار رفته");
            host.Write(u => { u.Set<EnHandover>().Single().Status = EnHandoverStatus.Released; u.SaveChanges(); }, Seeder);
            EnHandoverDto rel = await GetAs(host, Mgr1, h);
            Assert.IsFalse(rel.CanRefresh);
            Refused(await Act(host, Mgr1, "refresh", d => d.Id = h), "فقط پیش‌نویس");
        }

        [TestMethod]
        public async Task G1_The_Ceo_Decides_Only_While_Accepted_And_Old_Requests_Are_Replaced_By_The_Request_After_The_Acceptance()
        {
            AppHost host = Started("e1ffix_g1old");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            // data of the old order: a permit Requested in the draft
            host.Write(u =>
            {
                var lines = u.Set<EnHandoverLine>().Where(l => l.HandoverId == h.Id).ToList();
                string snapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { Lines = EnHandoverRules.MissingKeys(lines), DossierUnconfirmed = false });
                AppHost.Add(u, new EnWaiver { Number = "EWV-OLD", HandoverId = h.Id, CaseId = caseId, Reason = Reason, DebtHours = 24, MissingSnapshot = snapshot, Status = EnWaiverStatus.Requested, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddHours(-3) });
                u.SaveChanges();
            }, Seeder);
            int old = host.Read(u => u.Set<EnWaiver>().Single().Id);
            EnWaiverDto dto = await Waiver(host, Ceo, old);
            Assert.IsFalse(dto.CanDecide);
            Assert.AreEqual(EnHandoverService.DecideOnlyAfterAccept, dto.DecideBlockedReason);
            Assert.AreEqual(EnHandoverService.DecideOnlyAfterAccept, Refused(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = old, Decision = "approve" }, Ct))));
            Assert.AreEqual(0, (await Discover(host, "ENG.WAIVER.DECIDE")).Count, "the CEO's gate lists only decidable permits");
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual(EnHandoverService.DecideOnlyAfterAccept, Refused(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = old, Decision = "approve" }, Ct))), "not during the rounds");
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            // accepted: the stale request is not a live permit; the manager asks once more and the stale one is voided at that moment
            Assert.IsTrue((await GetAs(host, Mgr1, h.Id)).CanRequestWaiver);
            Assert.IsTrue((await Waiver(host, Ceo, old)).CanDecide, "after the acceptance the old request is decidable too, until the manager asks again and replaces it");
            Assert.IsNull(await AskPermit(host, h.Id));
            List<EnWaiver> all = host.Read(u => u.Set<EnWaiver>().OrderBy(x => x.Id).ToList());
            Assert.AreEqual(EnWaiverStatus.Void, all[0].Status);
            Assert.AreEqual("درخواست پیش از پذیرش PMO بود و با درخواست پس از پذیرش جایگزین شد", all[0].DecisionNote);
            Assert.AreEqual(EnWaiverStatus.Requested, all[1].Status);
            Assert.IsTrue((await Waiver(host, Ceo, all[1].Id)).CanDecide);
            Assert.AreEqual(1, (await Discover(host, "ENG.WAIVER.DECIDE")).Count);
        }

        [TestMethod]
        public async Task G1_A_Request_Left_Behind_Is_Voided_When_The_Package_Is_Complete_And_The_Ceo_Cannot_Decide_It()
        {
            AppHost host = Started("e1ffix_g1leftover");
            (int contract, int caseId) = await NewDesign(host);
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            // a Requested permit exists on a complete accepted package (data left by a dossier confirmed after the request)
            host.Write(u => { AppHost.Add(u, new EnWaiver { Number = "EWV-L1", HandoverId = h.Id, CaseId = caseId, Reason = Reason, DebtHours = 24, MissingSnapshot = "{}", Status = EnWaiverStatus.Requested, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddHours(1) }); u.SaveChanges(); }, Seeder);
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);
            EnWaiverDto dto = await Waiver(host, Ceo, permit);
            Assert.IsFalse(dto.CanDecide);
            StringAssert.Contains(dto.DecideBlockedReason, "لازم نیست");
            Assert.AreEqual(0, (await Discover(host, "ENG.WAIVER.DECIDE")).Count);
            Refused(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "approve" }, Ct)), "لازم نیست");
            EnWaiver after = host.Read(u => u.Set<EnWaiver>().Single());
            Assert.AreEqual(EnWaiverStatus.Void, after.Status, "the system voided it, the CEO decided nothing");
            Assert.AreEqual("بسته کامل شد", after.DecisionNote);
            Assert.IsNull(after.DecidedByUserId);

            // the same at the release: a Requested permit left on a package that is released complete is voided there
            host.Write(u => { AppHost.Add(u, new EnWaiver { Number = "EWV-L2", HandoverId = h.Id, CaseId = caseId, Reason = Reason, DebtHours = 24, MissingSnapshot = "{}", Status = EnWaiverStatus.Requested, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddHours(1) }); u.SaveChanges(); }, Seeder);
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.IsTrue(host.Read(u => u.Set<EnWaiver>().All(w => w.Status == EnWaiverStatus.Void)));
            Assert.AreEqual("بسته کامل شد", host.Read(u => u.Set<EnWaiver>().Single(w => w.Number == "EWV-L2").DecisionNote));
        }

        [TestMethod]
        public async Task G1_An_Old_Order_Permit_That_Was_Already_Used_Allows_No_Second_Request()
        {
            AppHost host = Started("e1ffix_g1second");
            (int contract, int caseId, int h) = await Accepted(host);
            host.Write(u => { u.Set<EnHandover>().Single().WaiverId = 4242; u.SaveChanges(); }, Seeder);
            EnHandoverDto dto = await GetAs(host, Mgr1, h);
            Assert.IsFalse(dto.CanRequestWaiver);
            StringAssert.Contains(dto.RequestWaiverBlockedReason, "قبلاً به کار رفته");
            Assert.AreEqual(dto.RequestWaiverBlockedReason, await AskPermit(host, h), "the flag and the action say the same");
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()));
        }

        [TestMethod]
        public async Task G1_After_A_Rejection_A_New_Request_Needs_A_New_Reason()
        {
            AppHost host = Started("e1ffix_g1reject");
            (int contract, int caseId, int h) = await Accepted(host);
            Assert.IsNull(await AskPermit(host, h));
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "reject", Note = "مدارک باید پیش از ابلاغ کامل شود" }, Ct)));
            Assert.IsTrue((await GetAs(host, Mgr1, h)).CanRequestWaiver);
            StringAssert.Contains(await AskPermit(host, h), "دلیل تازه");
            Assert.IsNull(await AskPermit(host, h, OtherReason));
            Assert.AreEqual(2, host.Read(u => u.Set<EnWaiver>().Count()));
            // the CEO cannot decide while the package is not accepted any more (refresh sent it back) and the request is void
            Deliverable(host, host.Read(u => u.Set<EnHandover>().Single().ContractId), "DWG");
            Ok(await Act(host, Mgr1, "refresh", d => d.Id = h));
            Assert.AreEqual(1, host.Read(u => u.Set<EnWaiver>().Count(w => w.Status == EnWaiverStatus.Void)));
        }

        // ---------------------------------------------------------------- group 2

        private static async Task ApprovePermit(AppHost host, int handover, int hours = 24)
        {
            Assert.IsNull(await AskPermit(host, handover, Reason, hours));
            int permit = host.Read(u => u.Set<EnWaiver>().OrderByDescending(x => x.Id).First().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "approve" }, Ct)));
        }

        [TestMethod]
        public async Task G2_The_Debt_Deadline_Counts_From_The_Release_Not_From_The_Approval_So_A_Late_Release_Never_Makes_A_Debt_Overdue_At_Birth()
        {
            AppHost host = Started("e1ffix_g2late");
            (int contract, int caseId, int h) = await Accepted(host);
            await ApprovePermit(host, h, 24);
            EnWaiverDto approved = await Waiver(host, Ceo, host.Read(u => u.Set<EnWaiver>().Single().Id));
            Assert.AreEqual(24, approved.DebtHours, "the approved office hours are kept");
            Assert.IsNull(approved.DebtDueAt, "the approval no longer starts the clock");
            // the approval is long ago when the PMO releases
            host.Write(u => { EnWaiver w = u.Set<EnWaiver>().Single(); w.RequestedAt = DateTimeOffset.Now.AddDays(-12); w.DecidedAt = DateTimeOffset.Now.AddDays(-10); u.SaveChanges(); }, Seeder);
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h));
            DateTimeOffset before = DateTimeOffset.Now.AddMinutes(-2);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h));
            DateTimeOffset after = DateTimeOffset.Now.AddMinutes(2);
            Assert.AreEqual(2, rel.Debts.Count);
            Assert.IsTrue(rel.Debts.All(d => d.DueAt > DateTimeOffset.Now), "no debt is born overdue");
            Assert.IsTrue(rel.Debts.All(d => !d.Overdue));
            Assert.IsTrue(rel.Debts.All(d => d.DueAt >= Services.NasbNiro.Office.OfficeClock.Add(before, 24) && d.DueAt <= Services.NasbNiro.Office.OfficeClock.Add(after, 24)), "24 office hours from the release");
            Assert.AreEqual(rel.Debts[0].DueAt, rel.Waiver.DebtDueAt, "the waiver shows the deadline after the release");
            // the unit notice shows the deadline that is still ahead
            string notice = rel.Distribution.Select(x => x.ScopeNote).First(t => t.Contains("هنوز نرسیده، مهلت تا"));
            StringAssert.Contains(notice, EnText.Stamp(rel.Debts[0].DueAt));
            Assert.AreEqual(0, (await Discover(host, "ENG.DEBT.SUPPLY")).Count(i => i.Summary.Contains("از سررسید گذشته")));
            Assert.AreEqual(0, (await host.As(Ceo, p => Services.NasbNiro.Engineering.EnRisk.OverdueDebtsAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct))).Count);
        }

        [TestMethod]
        public async Task G2_An_Old_Order_Permit_With_A_Fixed_Past_Instant_Is_Recomputed_From_The_Release()
        {
            AppHost host = Started("e1ffix_g2old");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            host.Write(u =>
            {
                var lines = u.Set<EnHandoverLine>().Where(l => l.HandoverId == h.Id).ToList();
                string snapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { Lines = EnHandoverRules.MissingKeys(lines), DossierUnconfirmed = false });
                AppHost.Add(u, new EnWaiver { Number = "EWV-OLD2", HandoverId = h.Id, CaseId = caseId, Reason = Reason, DebtHours = 12, DebtDueAt = DateTimeOffset.Now.AddDays(-5), MissingSnapshot = snapshot, Status = EnWaiverStatus.Approved, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddDays(-6), DecidedByUserId = 2121, DecidedAt = DateTimeOffset.Now.AddDays(-5) });
                u.SaveChanges();
            }, Seeder);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            DateTimeOffset before = DateTimeOffset.Now.AddMinutes(-2);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual(2, rel.Debts.Count);
            Assert.IsTrue(rel.Debts.All(d => d.DueAt > DateTimeOffset.Now && d.DueAt >= Services.NasbNiro.Office.OfficeClock.Add(before, 12)), "12 office hours from the release, not the old fixed instant");
            Assert.IsTrue(rel.Waiver.DebtDueAt > DateTimeOffset.Now);
        }

        [TestMethod]
        public async Task G2_A_Debt_Whose_Line_Became_Not_Applicable_Is_Voided_By_The_Settle_Code_With_A_Note()
        {
            AppHost host = Started("e1ffix_g2na");
            await ApprovedList(host, "VDDR-P", ("DWG", true, false));
            int product = Product(host);
            Ok(await Dossier(host, Eng1, product, "assess", code: "VDDR-P"));   // the drawing line stays missing
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { ProductId = product, Code = "BOM-N", AssemblyPartNumber = "SYS", Title = "BOM", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbOpcRouting { ProductId = product, PartNumber = "SYS", Title = "OPC", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Approved, PreparedByUserId = 1 });
                u.SaveChanges();
            }, Seeder);
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", x => x.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", x => { x.Classification = 1; x.ProductId = product; x.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-P"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", x => x.CaseId = c.Id));
            Assert.AreEqual(1, h.MissingRequiredCount);
            Ok(await Act(host, Mgr1, "submit", x => x.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", x => x.Id = h.Id));
            await ApprovePermit(host, h.Id, 24);
            Ok(await Act(host, Pmo1, "draft-distribution", x => x.Id = h.Id));
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", x => x.Id = h.Id));
            Assert.AreEqual(2, rel.Debts.Count, "the drawing line and the dossier confirmation");
            Assert.AreEqual(2, (await Discover(host, "ENG.DEBT.SUPPLY")).Count);
            // an approved deviation makes the drawing line not applicable (the deviation code sets the state of the dossier line)
            host.Write(u => { var d = u.Set<EnPlatformDocument>().Single(x => x.DocumentType == "DWG"); d.State = EnLineState.NotApplicable; d.NotApplicableReason = "بند با انحراف تاییدشده غیرقابل‌اجرا شد"; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(1, (await Discover(host, "ENG.DEBT.SETTLE")).Count, "the agent finds the debt that can close");
            Assert.AreEqual(1, (await Discover(host, "ENG.DEBT.SUPPLY")).Count, "it is no longer an open demand on the engineers");
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))));
            List<EnDocumentDebt> debts = host.Read(u => u.Set<EnDocumentDebt>().OrderBy(x => x.Id).ToList());
            EnDocumentDebt dwg = debts.Single(x => x.DocumentType == "DWG");
            Assert.AreEqual(EnDebtStatus.Void, dwg.Status);
            Assert.AreEqual(EnHandoverService.DebtVoidNotApplicable, dwg.VoidReason);
            Assert.AreEqual(EnDebtStatus.Open, debts.Single(x => x.DocumentType == EnDebtTypes.DossierConfirm).Status, "the dossier confirmation is still owed");
            Assert.AreEqual(0, (await Discover(host, "ENG.DEBT.SETTLE")).Count);
        }
    }
}
