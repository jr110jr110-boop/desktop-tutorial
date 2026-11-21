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

        // ---------------------------------------------------------------- group 3

        private static async Task<List<EnRiskRow>> Risk(AppHost host) =>
            await host.As(Ceo, p => EnRisk.ComputeAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct));

        /// <summary>A platform case (dossier of standard VDDR-P with one required drawing line), its handover drafted; the drawing is supplied or not.</summary>
        private static async Task<(int Product, int CaseId, int Handover)> PlatformDraft(AppHost host, bool supplyDrawing)
        {
            await ApprovedList(host, "VDDR-P", ("DWG", true, false));
            int product = Product(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "VDDR-P"));
            if (supplyDrawing) Ok(await Supply(host, Eng1, d.Lines.Single(l => l.DocumentType == "DWG").Id, File(host, 2111)));
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { ProductId = product, Code = "BOM-G3", AssemblyPartNumber = "SYS", Title = "BOM", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
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
            return (product, c.Id, h.Id);
        }

        [TestMethod]
        public async Task G3_The_Document_Risk_And_The_Early_Warning_Cover_A_Case_Whose_Incomplete_Package_Is_Submitted_Or_Accepted_Until_It_Is_Released()
        {
            AppHost host = Started("e1ffix_g3risk");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.IsTrue((await Risk(host)).Any(r => r.CaseId == caseId), "draft");
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual(EnCaseStatus.HandedOver, host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).Status));
            EnRiskRow sub = (await Risk(host)).Single(r => r.CaseId == caseId);
            Assert.IsTrue(sub.Awarded, "the CEO's permanent item keeps standing");
            Assert.AreEqual(2, sub.Missing);
            StringAssert.Contains(string.Join(" ", sub.Reasons), "نزد PMO");
            Assert.AreEqual(1, (await Discover(host, "ENG.RISK.WATCH")).Count(i => i.TargetId == caseId), "the early warning stays open when the incomplete package is submitted");
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Assert.AreEqual(EnCaseStatus.Accepted, host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).Status));
            Assert.IsTrue((await Risk(host)).Single(r => r.CaseId == caseId).Awarded);
            Assert.AreEqual(1, (await Discover(host, "ENG.RISK.WATCH")).Count(i => i.TargetId == caseId));
            // an approved permit covers the lines: no longer a document risk; released: the case leaves
            await ApprovePermit(host, h.Id, 24);
            Assert.IsFalse((await Risk(host)).Any(r => r.CaseId == caseId), "covered by the approved permit (its debts are tracked instead)");
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.IsFalse((await Risk(host)).Any(r => r.CaseId == caseId));
        }

        [TestMethod]
        public async Task G3_A_Platform_Case_With_An_Unconfirmed_Dossier_Stays_At_Risk_While_Its_Package_Waits_At_The_Pmo()
        {
            AppHost host = Started("e1ffix_g3riskdoss");
            (int product, int caseId, int h) = await PlatformDraft(host, true);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h));
            Assert.AreEqual(EnCaseStatus.HandedOver, host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).Status));
            Assert.IsTrue((await Risk(host)).Any(r => r.CaseId == caseId), "submitted, only the dossier confirmation is missing");
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h));
            Assert.IsTrue((await Risk(host)).Any(r => r.CaseId == caseId));
            Ok(await Dossier(host, Eng1, product, "complete", code: "VDDR-P"));
            Ok(await Dossier(host, Mgr1, product, "confirm", code: "VDDR-P"));
            Assert.IsFalse((await Risk(host)).Any(r => r.CaseId == caseId), "complete: no risk");
        }

        [TestMethod]
        public async Task G3_The_Lock_Message_Comes_From_The_Release_Check_And_Disappears_When_The_Ceo_Approved_The_Permit()
        {
            AppHost host = Started("e1ffix_g3lock");
            (int contract, int caseId, int h) = await Accepted(host);
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h));
            EnHandoverDto before = await GetAs(host, Pmo2, h);
            Assert.IsFalse(before.CanRelease);
            Assert.AreEqual(before.ReleaseBlockedReason, before.LockMessage, "the same check");
            StringAssert.Contains(before.LockMessage, "ابلاغ به اجرا قفل است");
            Assert.IsNull(await AskPermit(host, h));
            Assert.IsNotNull((await GetAs(host, Pmo2, h)).LockMessage, "a request is not an approval");
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "approve" }, Ct)));
            EnHandoverDto after = await GetAs(host, Pmo2, h);
            Assert.IsTrue(after.CanRelease, after.ReleaseBlockedReason);
            Assert.IsNull(after.LockMessage, "after the CEO approved the red box is gone");
            Assert.IsNotNull(after.IncompleteNotice, "the package is still incomplete: the amber notice stays");
        }

        [TestMethod]
        public async Task G3_The_Release_Gate_Opens_Only_When_Release_Is_Possible_And_The_Waiting_Work_Belongs_To_The_Manager_Then_The_Ceo()
        {
            AppHost host = Started("e1ffix_g3gates");
            (int contract, int caseId, int h) = await Accepted(host);
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.RELEASE")).Count, "locked: the PMO cannot act");
            List<AgentWorkItem> complete = await Discover(host, "ENG.HANDOVER.COMPLETE");
            Assert.AreEqual(1, complete.Count);
            Assert.AreEqual(EnRoles.Manager, complete[0].GateRoles, "the manager roles, not every engineering writer");
            StringAssert.Contains(complete[0].Summary, "مجوز اضطرار");
            Assert.IsNull(await AskPermit(host, h));
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.COMPLETE")).Count);
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.RELEASE")).Count, "the CEO's permit is pending: still not the PMO's turn");
            Assert.AreEqual(1, (await Discover(host, "ENG.WAIVER.DECIDE")).Count);
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "approve" }, Ct)));
            List<AgentWorkItem> release = await Discover(host, "ENG.HANDOVER.RELEASE");
            Assert.AreEqual(1, release.Count, "approved: the release is possible now");
            Assert.AreEqual(EnRoles.Pmo, release[0].GateRoles);
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.COMPLETE")).Count);

            // a complete accepted package: the release gate is open at once, no permit gate
            AppHost host2 = Started("e1ffix_g3gates2");
            (int contract2, int caseId2) = await NewDesign(host2);
            Deliverable(host2, contract2, "DWG"); Deliverable(host2, contract2, "DATASHEET");
            EnHandoverDto h2 = Ok(await Act(host2, Eng1, "draft", d => d.CaseId = caseId2));
            Ok(await Act(host2, Mgr1, "submit", d => d.Id = h2.Id));
            Ok(await Act(host2, Pmo1, "accept", d => d.Id = h2.Id));
            Assert.AreEqual(1, (await Discover(host2, "ENG.HANDOVER.RELEASE")).Count);
            Assert.AreEqual(0, (await Discover(host2, "ENG.HANDOVER.COMPLETE")).Count);
        }

        [TestMethod]
        public async Task G3_When_Only_The_Dossier_Confirmation_Is_Missing_The_Gate_Tells_The_Manager_To_Confirm_It_And_No_Permit_Is_Needed()
        {
            AppHost host = Started("e1ffix_g3dossier");
            (int product, int caseId, int h) = await PlatformDraft(host, true);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h));
            List<AgentWorkItem> gate = await Discover(host, "ENG.HANDOVER.COMPLETE");
            Assert.AreEqual(1, gate.Count, "a dossier-only gap also has its gate");
            Assert.AreEqual(EnRoles.Manager, gate[0].GateRoles);
            StringAssert.Contains(gate[0].Summary, "تایید");
            StringAssert.Contains(gate[0].Summary, "مجوز اضطرار لازم نیست");
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.RELEASE")).Count);
            Ok(await Dossier(host, Eng1, product, "complete", code: "VDDR-P"));
            Ok(await Dossier(host, Mgr1, product, "confirm", code: "VDDR-P"));
            Assert.AreEqual(0, (await Discover(host, "ENG.HANDOVER.COMPLETE")).Count);
            Assert.AreEqual(1, (await Discover(host, "ENG.HANDOVER.RELEASE")).Count, "the release is possible without any permit");
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h));
            Assert.IsTrue((await GetAs(host, Pmo2, h)).CanRelease);
            Ok(await Act(host, Pmo2, "release", d => d.Id = h));
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()));
        }

        // ---------------------------------------------------------------- group 4

        private static Task<string> Instantiate(AppHost host, int contract) =>
            host.As(Eng1, p => Services.NasbNiro.Engineering.EnGates.InstantiateRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct));

        [TestMethod]
        public async Task G4_The_Instantiate_Chokepoint_Passes_Only_When_The_Pmo_Released_The_Package_Not_At_The_Acceptance()
        {
            AppHost host = Started("e1ffix_g4");
            (int contract, int caseId) = await NewDesign(host);
            string notOpened = "مدارک مهندسی این قرارداد هنوز از سوی PMO به اجرا ابلاغ نشده است (پرونده مهندسی ";
            StringAssert.StartsWith(await Instantiate(host, contract), notOpened);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            StringAssert.StartsWith(await Instantiate(host, contract), notOpened);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            StringAssert.StartsWith(await Instantiate(host, contract), notOpened);
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            string accepted = await Instantiate(host, contract);
            StringAssert.StartsWith(accepted, notOpened, "accepted is not enough any more");
            StringAssert.EndsWith(accepted, ").");
            // the gate state shown to the work-order screens says the same
            ViewModels.NasbNiro.EngineeringDtos.EnGateStateDto state = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetGateStateAsync(null, contract, Ct)));
            Assert.IsFalse(state.Passed);
            StringAssert.StartsWith(state.BlockedReason, notOpened);
            // an incomplete package with a requested/approved permit is still not released: still refused
            Assert.IsNull(await AskPermit(host, h.Id));
            StringAssert.StartsWith(await Instantiate(host, contract), notOpened);
            int permit = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = permit, Decision = "approve" }, Ct)));
            StringAssert.StartsWith(await Instantiate(host, contract), notOpened, "an approved permit alone does not open it either");
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.IsNull(await Instantiate(host, contract), "released: it passes");
            Assert.IsTrue(Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetGateStateAsync(null, contract, Ct))).Passed);
        }

        // ---------------------------------------------------------------- group 5

        private static void SeedPermit(AppHost host, int handover, int caseId, EnWaiverStatus status, string number, bool dossierUnconfirmed, string[] keys = null)
        {
            host.Write(u =>
            {
                var lines = u.Set<EnHandoverLine>().Where(l => l.HandoverId == handover).ToList();
                string snapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { Lines = (keys ?? EnHandoverRules.MissingKeys(lines).ToArray()).ToList(), DossierUnconfirmed = dossierUnconfirmed });
                AppHost.Add(u, new EnWaiver { Number = number, HandoverId = handover, CaseId = caseId, Reason = Reason, DebtHours = 24, MissingSnapshot = snapshot, Status = status, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddHours(-3), DecidedByUserId = status == EnWaiverStatus.Approved ? 2121 : (int?)null, DecidedAt = status == EnWaiverStatus.Approved ? DateTimeOffset.Now.AddHours(-2) : (DateTimeOffset?)null });
                u.SaveChanges();
            }, Seeder);
        }

        [TestMethod]
        public async Task G5_The_Release_Refuses_A_Permit_That_No_Longer_Covers_The_Missing_Lines_And_The_Flag_Says_The_Same()
        {
            AppHost host = Started("e1ffix_g5cover");
            (int contract, int caseId) = await NewDesign(host);
            int datasheet = Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(1, h.MissingRequiredCount, "only the drawing is missing");
            // old-order data: the permit was approved in the draft for exactly that missing set
            SeedPermit(host, h.Id, caseId, EnWaiverStatus.Approved, "EWV-COV", false);
            // the datasheet is withdrawn after the approval and the draft is read again: a second required line is missing now
            host.Write(u => { u.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().First(x => x.Id == datasheet).IssueState = Entities.Model.NasbNiro.ProjectControl.PcIssueState.NotIssued; u.SaveChanges(); }, Seeder);
            EnHandoverDto r = Ok(await Act(host, Eng1, "refresh", d => d.Id = h.Id));
            Assert.AreEqual(2, r.MissingRequiredCount);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto shown = await GetAs(host, Pmo2, h.Id);
            Assert.IsFalse(shown.CanRelease);
            string refused = Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "پوشش");
            StringAssert.Contains(refused, "مجوز اضطرار این تحویل را پوشش نمی‌دهد: 1 مدرک الزامی تازه کم شده");
            Assert.AreEqual(shown.ReleaseBlockedReason, refused, "the flag and the action give the same text");
            Assert.AreEqual(shown.LockMessage, refused, "and so does the lock message");
            EnWaiverDto w = await Waiver(host, Pmo2, host.Read(u => u.Set<EnWaiver>().Single().Id));
            Assert.IsFalse(w.Covers);
            StringAssert.Contains(w.CoverageNote, "پوشش نمی‌دهد");
            Assert.AreEqual(0, host.Read(u => u.Set<EnDocumentDebt>().Count()), "a refused release creates no debt");
        }

        [TestMethod]
        public async Task G5_A_Permit_Taken_For_A_Confirmed_Dossier_Does_Not_Cover_A_Dossier_That_Lost_Its_Confirmation()
        {
            AppHost host = Started("e1ffix_g5dossier");
            (int product, int caseId, int h) = await PlatformDraft(host, true);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h));
            SeedPermit(host, h, caseId, EnWaiverStatus.Approved, "EWV-DOS", false, new string[0]);   // the snapshot says: the dossier was confirmed when the permit was asked
            string refused = Refused(await Act(host, Pmo2, "release", d => d.Id = h), "پوشش");
            StringAssert.Contains(refused, "تایید پروندهٔ پلتفرم پس از درخواست باطل شده است");
            Assert.AreEqual(refused, (await GetAs(host, Pmo2, h)).ReleaseBlockedReason);
            // a permit that knew about the unconfirmed dossier covers it
            host.Write(u => { u.Set<EnWaiver>().Single().MissingSnapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { DossierUnconfirmed = true }); u.SaveChanges(); }, Seeder);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h));
            Assert.AreEqual(1, rel.Debts.Count, "only the dossier confirmation is owed");
            Assert.AreEqual(EnDebtTypes.DossierConfirm, rel.Debts[0].DocumentType);
        }

        [TestMethod]
        public async Task G5_Leftover_Permits_A_Void_Or_A_Return_Voids_Requested_And_A_Complete_Release_Never_Touches_An_Approved_One()
        {
            // a void of a draft voids the Requested permit left on it
            AppHost host = Started("e1ffix_g5left1");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            SeedPermit(host, h.Id, caseId, EnWaiverStatus.Requested, "EWV-L1", false);
            Ok(await Act(host, Mgr1, "void", d => { d.Id = h.Id; d.Note = "پیش‌نویس باطل می‌شود چون فهرست مدارک عوض شد"; }));
            EnWaiver left = host.Read(u => u.Set<EnWaiver>().Single());
            Assert.AreEqual(EnWaiverStatus.Void, left.Status);
            Assert.AreEqual("بسته از حالت پذیرفته‌شده خارج شد", left.DecisionNote);
            Assert.IsNull(left.DecidedByUserId);

            // a PMO return voids it too
            AppHost host2 = Started("e1ffix_g5left2");
            (int contract2, int caseId2) = await NewDesign(host2);
            EnHandoverDto h2 = Ok(await Act(host2, Eng1, "draft", d => d.CaseId = caseId2));
            SeedPermit(host2, h2.Id, caseId2, EnWaiverStatus.Requested, "EWV-L2", false);
            Ok(await Act(host2, Mgr1, "submit", d => d.Id = h2.Id));
            Ok(await Act(host2, Pmo1, "return", d => { d.Id = h2.Id; d.Note = "نقشه‌های اجرایی هنوز کامل نیست و باید برسد"; }));
            Assert.AreEqual(EnWaiverStatus.Void, host2.Read(u => u.Set<EnWaiver>().Single().Status));

            // a complete package released with an Approved unused permit of the old order: the permit is NOT touched
            AppHost host3 = Started("e1ffix_g5left3");
            (int contract3, int caseId3) = await NewDesign(host3);
            Deliverable(host3, contract3, "DWG"); Deliverable(host3, contract3, "DATASHEET");
            EnHandoverDto h3 = Ok(await Act(host3, Eng1, "draft", d => d.CaseId = caseId3));
            SeedPermit(host3, h3.Id, caseId3, EnWaiverStatus.Approved, "EWV-L3", false, new[] { "DWG|X" });
            Ok(await Act(host3, Mgr1, "submit", d => d.Id = h3.Id));
            Ok(await Act(host3, Pmo1, "accept", d => d.Id = h3.Id));
            Ok(await Act(host3, Pmo1, "draft-distribution", d => d.Id = h3.Id));
            EnHandoverDto rel = Ok(await Act(host3, Pmo2, "release", d => d.Id = h3.Id));
            Assert.AreEqual(0, rel.Debts.Count);
            EnWaiver stays = host3.Read(u => u.Set<EnWaiver>().Single());
            Assert.AreEqual(EnWaiverStatus.Approved, stays.Status, "an approval is never voided by the system");
            Assert.IsNull(stays.UsedAt);
        }

        [TestMethod]
        public async Task G5_The_Open_Handover_Of_A_Case_Is_The_Newest_Round_Not_The_Returned_One()
        {
            AppHost host = Started("e1ffix_g5open");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto r1 = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = r1.Id));
            Ok(await Act(host, Pmo1, "return", d => { d.Id = r1.Id; d.Note = "نقشه‌های اجرایی هنوز کامل نیست و باید برسد"; }));
            Assert.AreEqual(r1.Id, Ok(await GetCase(host, Mgr1, caseId)).OpenHandoverId, "only a returned round exists: it is shown");
            EnHandoverDto r2 = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(r2.Id, Ok(await GetCase(host, Mgr1, caseId)).OpenHandoverId, "round 2 is the open one");
            Ok(await Act(host, Mgr1, "submit", d => d.Id = r2.Id));
            Assert.AreEqual(r2.Id, Ok(await GetCase(host, Mgr1, caseId)).OpenHandoverId);
            // the list read by the smoke: the drafts of the case
            var list = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().GetHandoversAsync(new EnHandoverQuery { CaseId = caseId, Status = (int)EnHandoverStatus.Returned }, Ct)));
            Assert.AreEqual(1, list.TotalCount);
            Assert.AreEqual(r1.Id, list.First().Id);
        }
    }
}
