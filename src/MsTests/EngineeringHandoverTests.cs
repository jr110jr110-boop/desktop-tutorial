using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1: the handover lock at point B, the CEO emergency waiver (single use, snapshot coverage), the document debts, the round trips with the PMO, the release, the distribution and the two chokepoints.</summary>
    [TestClass]
    public class EngineeringHandoverTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        internal static Task<ResponseDto<EnHandoverDto>> Act(AppHost host, ClaimsPrincipal who, string action, Action<EnHandoverActionDto> set = null)
        {
            EnHandoverActionDto dto = new EnHandoverActionDto { Action = action };
            set?.Invoke(dto);
            return host.As(who, p => p.GetRequiredService<IEnHandoverService>().HandoverActionAsync(dto, Ct));
        }

        private static Task<ResponseDto<EnWaiverDto>> Request(AppHost host, ClaimsPrincipal who, int handover, string reason, DateTimeOffset due) =>
            host.As(who, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = handover, Reason = reason, DebtHours = (int)Math.Round((due - DateTimeOffset.Now).TotalHours) }, Ct));   // the deadline is in hours and counts from the CEO's approval

        private static Task<ResponseDto<EnWaiverDto>> Decide(AppHost host, ClaimsPrincipal who, int waiver, string decision, string note = null) =>
            host.As(who, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = decision, Note = note }, Ct));

        private static Task<ResponseDto<EnHandoverDto>> Get(AppHost host, ClaimsPrincipal who, int id) => host.As(who, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct));

        internal static int Deliverable(AppHost host, int contract, string type, PcIssueState state = PcIssueState.Approved, bool file = true)
        {
            Guid? doc = file ? File(host, 2111) : (Guid?)null;
            int id = 0;
            host.Write(u =>
            {
                PcEngineeringDeliverable d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract, Title = "مدرک " + type, DocumentType = type, Discipline = "مکانیک", IssueState = state, DocumentId = doc, Revision = "A", CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); id = d.Id;
            }, Seeder);
            return id;
        }

        private static void SetState(AppHost host, int deliverable, PcIssueState state) =>
            host.Write(u => { u.Set<PcEngineeringDeliverable>().First(x => x.Id == deliverable).IssueState = state; u.SaveChanges(); }, Seeder);

        /// <summary>A new-design contract case approved by the CEO (so the contract is won), with a reference list of two required documents.</summary>
        internal static async Task<(int Contract, int CaseId)> NewDesign(AppHost host)
        {
            await ApprovedList(host, "VDDR-H", ("DWG", true, false), ("DATASHEET", true, false));
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "هیچ محصولی مطابق نیست"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-H"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            return (contract, c.Id);
        }

        [TestMethod]
        public async Task The_Handover_Of_A_Case_With_A_Product_Carries_The_System_Proven_Bom_And_Opc_Lines_With_A_Link_And_No_File()
        {
            AppHost host = Started("hosys");
            (int contract, int caseId) = await NewDesign(host);
            int product = Product(host);
            host.Write(u => { u.Set<EnCase>().First(x => x.Id == caseId).ProductId = product; u.SaveChanges(); }, Seeder);

            // the list has only two document types of its own; the system adds the BOM and the OPC of the product as required lines, both missing while the bank has neither
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(4, h.Lines.Count);
            Assert.AreEqual(4, h.MissingRequiredCount);
            EnHandoverLineDto bom = h.Lines.Single(l => l.DocumentType == EnDocTypes.Bom);
            EnHandoverLineDto opc = h.Lines.Single(l => l.DocumentType == EnDocTypes.Opc);
            Assert.IsTrue(bom.Required && opc.Required);
            Assert.AreEqual((int)EnLineState.Missing, bom.State);
            Assert.AreEqual((int)EnLineState.Missing, opc.State);
            Assert.AreEqual("/product-bank/products/" + product + "/bom", bom.LinkPath);
            Assert.AreEqual("/product-bank/products/" + product + "/routing", opc.LinkPath);
            Assert.IsNull(bom.DocumentId); Assert.IsNull(opc.DocumentId);

            // a released BOM and an approved route make both lines available on the refresh; the revision of the BOM is shown, no file is attached
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { ProductId = product, Code = "BOM-SYS", AssemblyPartNumber = "SYS", Title = "BOM", Revision = 3, Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbOpcRouting { ProductId = product, PartNumber = "SYS", Title = "OPC", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Approved, PreparedByUserId = 1 });
                u.SaveChanges();
            }, Seeder);
            h = Ok(await Get(host, Eng1, h.Id));
            h = Ok(await Act(host, Eng1, "refresh", d => { d.Id = h.Id; d.RowVersion = h.RowVersion; }));
            bom = h.Lines.Single(l => l.DocumentType == EnDocTypes.Bom);
            opc = h.Lines.Single(l => l.DocumentType == EnDocTypes.Opc);
            Assert.AreEqual((int)EnLineState.Available, bom.State);
            Assert.AreEqual("3", bom.Revision);
            Assert.AreEqual((int)EnLineState.Available, opc.State);
            Assert.IsNull(bom.DocumentId); Assert.IsNull(opc.DocumentId);
            Assert.AreEqual(2, h.MissingRequiredCount);   // only the two document types of the list are still missing
        }

        [TestMethod]
        public async Task The_Handover_Is_Locked_Until_Complete_And_A_Ceo_Waiver_Opens_It_Once_For_The_Missing_Set_And_The_Debts_Follow()
        {
            AppHost host = Started("ho1");
            (int contract, int caseId) = await NewDesign(host);
            // before any handover the two chokepoints hold the contract
            Assert.IsNotNull(await host.As(Eng1, async p => await EnGates.InstantiateRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)));
            (bool controlled, bool passed, string message) = await host.As(Eng1, async p => await EnGates.ProductionReleaseAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct));
            Assert.IsTrue(controlled); Assert.IsFalse(passed); Assert.IsNotNull(message);

            Refused(await Act(host, Nobody, "draft", d => d.CaseId = caseId));
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            StringAssert.StartsWith(h.Number, "EHO-");
            Assert.AreEqual(1, h.Round);
            Assert.AreEqual(2, h.MissingRequiredCount);
            Assert.IsNotNull(h.LockMessage);
            StringAssert.StartsWith(h.IncompleteNotice, "این بسته ناقص است");
            StringAssert.Contains(h.IncompleteNotice, "رفت‌وبرگشت با PMO ادامه دارد");
            Refused(await Act(host, Eng1, "draft", d => d.CaseId = caseId), "تحویل باز");
            // ENG-E1f: an INCOMPLETE package may go to the PMO (no lock on the submit); the CEO is not asked during the rounds
            const string reasonText = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده";
            DateTimeOffset due = DateTimeOffset.Now.AddHours(72);   // exactly the ceiling is accepted
            h = Ok(await Get(host, Mgr1, h.Id));
            Assert.IsTrue(h.CanSubmit, h.SubmitBlockedReason);
            Assert.IsFalse(h.CanRequestWaiver);
            StringAssert.Contains(h.RequestWaiverBlockedReason, "پس از پایان رفت‌وبرگشت");
            Refused(await Act(host, Eng1, "submit", d => d.Id = h.Id));
            Refused(await Request(host, Mgr1, h.Id, reasonText, due), "پس از پایان رفت‌وبرگشت");   // no permit in the draft
            EnHandoverDto s = Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual((int)EnHandoverStatus.Submitted, s.Status);
            Assert.IsFalse(s.Complete);
            Assert.AreEqual(2, s.MissingRequiredCount);
            Assert.AreEqual(0, s.Debts.Count, "no debt before the release");
            Assert.IsNull(s.WaiverId);
            Assert.IsNotNull(s.IncompleteNotice);
            Assert.AreEqual((int)EnCaseStatus.HandedOver, Ok(await GetCase(host, Mgr1, caseId)).Status);
            Refused(await Request(host, Mgr1, h.Id, reasonText, due), "پس از پایان رفت‌وبرگشت");   // no permit while the PMO still reviews
            Assert.IsFalse(Ok(await Get(host, Mgr1, h.Id)).CanRequestWaiver);
            Assert.IsTrue(Ok(await Get(host, Pmo1, h.Id)).CanAccept, "the PMO may accept an incomplete package");

            // the PMO side: not the commercial, not the submitter; the return needs a reason
            Refused(await Act(host, Comm1, "accept", d => d.Id = h.Id));
            Refused(await Act(host, Mgr1, "accept", d => d.Id = h.Id));
            Refused(await Act(host, Pmo1, "return", d => { d.Id = h.Id; d.Note = "کوتاه"; }), "15");
            EnHandoverDto r = Ok(await Act(host, Pmo1, "return", d => { d.Id = h.Id; d.Note = "نقشه‌های اجرایی هنوز کامل نیست"; }));
            Assert.AreEqual((int)EnHandoverStatus.Returned, r.Status);
            Refused(await Request(host, Mgr1, h.Id, reasonText, due), "پس از پایان رفت‌وبرگشت");
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()), "a PMO return and the next round never involve the CEO");
            EnHandoverDto r2 = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(2, r2.Round);
            // ENG-E1e: the new round carries the PMO's return of the previous round (number, who, when, reason); the first round carries none
            Assert.IsNull(r.PreviousReturnNumber);
            Assert.AreEqual(h.Number, r2.PreviousReturnNumber);
            Assert.AreEqual("نقشه‌های اجرایی هنوز کامل نیست", r2.PreviousReturnReason);
            Assert.AreEqual(r.ReturnedByName, r2.PreviousReturnByName);   // the same name the returned round shows
            Assert.IsFalse(string.IsNullOrWhiteSpace(r2.PreviousReturnAtText));
            Assert.AreEqual(h.Number, Ok(await Get(host, Mgr1, r2.Id)).PreviousReturnNumber);

            // round two: the drawing now exists (approved with a file), the datasheet does not; the package still goes to the PMO and is accepted incomplete
            Deliverable(host, contract, "DWG");
            Ok(await Act(host, Eng1, "refresh", d => d.Id = r2.Id));
            r2 = Ok(await Get(host, Mgr1, r2.Id));
            Assert.AreEqual(1, r2.MissingRequiredCount);
            Assert.AreEqual(0, Ok(await Act(host, Mgr1, "submit", d => d.Id = r2.Id)).Debts.Count);
            Ok(await Act(host, Pmo1, "accept", d => d.Id = r2.Id));
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()), "still no CEO request");

            // the permit is asked ONCE, now: the manager only, a real reason, a future date within the maximum
            Assert.IsTrue(Ok(await Get(host, Mgr1, r2.Id)).CanRequestWaiver);
            Refused(await Request(host, Eng1, r2.Id, reasonText, due));
            Refused(await Request(host, Mgr1, r2.Id, "کوتاه", due), "50");
            Refused(await Request(host, Mgr1, r2.Id, reasonText, DateTimeOffset.Now.AddHours(-1)), "ساعت");
            Refused(await Request(host, Mgr1, r2.Id, new string((char)0x645, 49), due), "50");   // 49 characters
            Refused(await Request(host, Mgr1, r2.Id, reasonText, DateTimeOffset.Now.AddHours(73)), "72");
            EnWaiverDto w2 = Ok(await Request(host, Mgr1, r2.Id, reasonText, due));
            StringAssert.StartsWith(w2.Number, "EWV-");
            Assert.AreEqual(1, w2.MissingLines.Count);
            Refused(await Request(host, Mgr1, r2.Id, reasonText, due), "مجوز اضطرار باز");
            // the release stays locked until the CEO approves; only the CEO decides
            Refused(await Act(host, Pmo2, "release", d => d.Id = r2.Id), "ابلاغ به اجرا قفل است");
            EnHandoverDto locked = Ok(await Get(host, Pmo2, r2.Id));
            Assert.IsFalse(locked.CanRelease);
            StringAssert.Contains(locked.ReleaseBlockedReason, "ابلاغ به اجرا قفل است");
            Refused(await Decide(host, Mgr2, w2.Id, "approve"), "مدیرعامل");
            Refused(await Decide(host, Ceo, w2.Id, "reject", "کوتاه"), "15");
            Refused(await Decide(host, Ceo, w2.Id, "maybe"), "نامعتبر");
            Refused(await Act(host, Pmo2, "release", d => d.Id = r2.Id), "ابلاغ به اجرا قفل است");
            w2 = Ok(await Decide(host, Ceo, w2.Id, "approve"));
            Assert.AreEqual((int)EnWaiverStatus.Approved, w2.Status);
            Refused(await Decide(host, Ceo, w2.Id, "approve"), "در انتظار");

            // accept and release: the releaser did not submit or prepare; only the PMO releases
            Refused(await Act(host, Pmo2, "release", d => d.Id = r2.Id), "ابلاغ ممکن نیست");   // the lock is open now, but the distribution rows are not built yet
            Refused(await Act(host, Eng1, "draft-distribution", d => d.Id = r2.Id), "PMO");   // the notices to the units never go from engineering
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = r2.Id));
            Refused(await Act(host, Eng1, "release", d => d.Id = r2.Id));
            Refused(await Act(host, Mgr1, "release", d => d.Id = r2.Id));
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = r2.Id));
            Assert.AreEqual((int)EnHandoverStatus.Released, rel.Status);
            Assert.AreEqual(1, rel.Debts.Count, "the permit is spent at the release: the missing document becomes a debt");
            Assert.IsTrue(rel.Debts.All(d => d.Status == (int)EnDebtStatus.Open && d.DueAt >= Services.NasbNiro.Office.OfficeClock.Add(DateTimeOffset.Now.AddMinutes(-10), 72) && d.DueAt <= Services.NasbNiro.Office.OfficeClock.Add(DateTimeOffset.Now, 72)));   // 72 OFFICE hours counted from the approval
            Assert.IsTrue(rel.Waiver.Used);
            Assert.IsTrue(rel.Distribution.Any(d => d.ScopeNote.Contains("هنوز نرسیده، مهلت تا")), "the notice shows the deadline of the debt created in the same release: " + string.Join(" || ", rel.Distribution.Select(d => d.ScopeNote)));
            Assert.AreEqual((int)EnCaseStatus.Released, Ok(await GetCase(host, Mgr1, caseId)).Status);
            Assert.IsNull(await host.As(Eng1, async p => await EnGates.InstantiateRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)));
            (controlled, passed, _) = await host.As(Eng1, async p => await EnGates.ProductionReleaseAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct));
            Assert.IsTrue(controlled); Assert.IsTrue(passed);

            // the debt is settled only from the document: nothing without it, then the engineer's agent settles it
            Assert.AreEqual(0, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))));
            Deliverable(host, contract, "DATASHEET");
            Refused(await host.As(Nobody, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct)));
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))));
            Assert.IsTrue(Ok(await Get(host, Mgr1, r2.Id)).Debts.All(d => d.Status == (int)EnDebtStatus.Settled && d.SettledDocumentId.HasValue));

            // the distribution: six units, each confirms with its own roles, once
            Refused(await Act(host, Nobody, "distribute", d => d.Id = r2.Id));
            Refused(await Act(host, Eng1, "distribute", d => d.Id = r2.Id), "PMO");
            EnHandoverDto dist = Ok(await Act(host, Pmo1, "distribute", d => d.Id = r2.Id));
            Assert.AreEqual(6, dist.Distribution.Count);
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = r2.Id), "قبلاً");
            int qa = dist.Distribution.Single(x => x.Unit == (int)EnUnit.Qa).Id, production = dist.Distribution.Single(x => x.Unit == (int)EnUnit.Production).Id;
            Refused(await Act(host, Qa1, "acknowledge", d => d.DistributionId = production));
            Ok(await Act(host, Qa1, "acknowledge", d => d.DistributionId = qa));
            Refused(await Act(host, Qa1, "acknowledge", d => d.DistributionId = qa), "قبلاً");
            Ok(await Act(host, Prod1, "acknowledge", d => d.DistributionId = production));
        }

        [TestMethod]
        public async Task A_Waiver_Request_Never_Expires_And_Stays_An_Alarm_Until_The_Ceo_Decides()
        {
            AppHost host = Started("ho9");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));   // ENG-E1f: the permit is asked only after the PMO accepted the (incomplete) package
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Request(host, Mgr1, h.Id, "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(24)));
            host.Write(u => { foreach (EnWaiver w in u.Set<EnWaiver>().ToList()) { w.RequestedAt = DateTimeOffset.Now.AddDays(-30); } u.SaveChanges(); }, Seeder);
            ClaimsPrincipal seat = await SeatAsync(host);
            List<Services.NasbNiro.Agents.AgentWorkItem> gate = await host.As(seat, async p => await p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == "ENG.WAIVER.DECIDE").DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), DateTime.Today, Ct));
            Assert.AreEqual(1, gate.Count, "thirty days later the gate is still open");
            EnOverviewDto o = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.WaiversAwaitingCeo);
            DateTimeOffset nowHo9 = DateTimeOffset.Now;
            Assert.AreEqual(Math.Floor(Services.NasbNiro.Office.OfficeClock.Between(nowHo9.AddDays(-30), nowHo9)), o.OldestWaiverWaitingHours, 1, "office hours, not calendar hours");
            Assert.IsTrue(o.OldestWaiverWaitingHours < 719);
            EnWaiverDto w1 = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().GetWaiversAsync(new EnWaiverQuery(), Ct))).Single();
            Assert.AreEqual(0, w1.Status, "still requested, never expired");
            Ok(await Decide(host, Ceo, w1.Id, "approve", null));
            Assert.AreEqual(0, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).WaiversAwaitingCeo);
        }

        [TestMethod]
        public async Task A_Seat_Never_Submits_Accepts_Releases_Or_Acknowledges_And_Prepares_The_Draft()
        {
            AppHost host = Started("ho2");
            (int contract, int caseId) = await NewDesign(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            EnHandoverDto h = Ok(await Act(host, seat, "draft", d => d.CaseId = caseId));
            Assert.IsTrue(h.PreparedByAgent);
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            Ok(await Act(host, seat, "refresh", d => d.Id = h.Id));
            h = Ok(await Get(host, Mgr1, h.Id));
            Assert.IsTrue(h.CanSubmit, h.SubmitBlockedReason);
            Assert.IsTrue(h.Complete);
            ClaimsPrincipal seatManager = await SeatAsync(host, "AG-ENG", "ENGINEERING", "ENGINEER");
            Refused(await Act(host, seatManager, "submit", d => d.Id = h.Id));
            // complete: the manager submits without a waiver and the handover is complete, so no debt exists
            EnHandoverDto s = Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.IsTrue(s.Complete);
            Assert.AreEqual(0, s.Debts.Count);
            Assert.IsNull(s.WaiverId);
            ClaimsPrincipal seatPmo = await SeatAsync(host, "AG-ENG", "PMO"), seatInit = await SeatAsync(host, "AG-INIT", "PLANNING");
            Refused(await Act(host, seatPmo, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, seatInit, "draft-distribution", d => d.Id = h.Id));
            Refused(await Act(host, seatPmo, "release", d => d.Id = h.Id));
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            EnHandoverDto dist = Ok(await Act(host, seatInit, "distribute", d => d.Id = h.Id));
            ClaimsPrincipal seatQa = await SeatAsync(host, "AG-ENG", "QA");
            Refused(await Act(host, seatQa, "acknowledge", d => d.DistributionId = dist.Distribution.First().Id));
        }

        [TestMethod]
        public async Task A_Platform_Handover_Is_Locked_While_The_Dossier_Is_Unconfirmed_And_Legacy_Contracts_Have_No_Case()
        {
            AppHost host = Started("ho3");
            await ApprovedList(host, "VDDR-P", ("DWG", true, false));
            int product = Product(host);
            Ok(await Dossier(host, Eng1, product, "assess", code: "VDDR-P"));   // a product is a platform only with a dossier
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-P"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = c.Id));
            h = Ok(await Get(host, Mgr1, h.Id));
            Assert.IsTrue(h.CanSubmit, "ENG-E1f: an unconfirmed dossier no longer blocks the sending to the PMO");
            StringAssert.Contains(h.LockMessage, "قفل");
            StringAssert.Contains(h.IncompleteNotice, "پروندهٔ پلتفرم");
            // the legacy contract has no case and passes both chokepoints
            int old = Contract(host, DateTimeOffset.Now.AddDays(-30));
            Assert.IsNull(await host.As(Eng1, async p => await EnGates.InstantiateRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), old, Ct)));
            (bool controlled, _, _) = await host.As(Eng1, async p => await EnGates.ProductionReleaseAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), old, Ct));
            Assert.IsFalse(controlled);
        }

        [TestMethod]
        public async Task The_Overview_Counts_Platforms_Debts_And_Waivers_And_Is_Refused_For_Others()
        {
            AppHost host = Started("ho5");
            Product(host);
            Refused(await host.As(Nobody, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            EnOverviewDto o = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.IsTrue(o.ControlStarted);
            Assert.AreEqual(1, o.PlatformsTotal);
            Assert.AreEqual(1, o.PlatformsIncomplete);
            Assert.AreEqual(0, o.OpenDebts);
            (int contract, int caseId) = await NewDesign(host);
            o = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.CasesByClassification.Single(x => x.Key == (int)EnClassification.New).Count);
            Assert.AreEqual(1, o.CasesByStatus.Single(x => x.Key == (int)EnCaseStatus.Awarded).Count);
            Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            o = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.HandoversWaiting.Single(x => x.Key == (int)EnHandoverStatus.Draft).Count);
            // The real dashboard goes through the ledger, which runs the finance seed. That seed keeps a process-wide "done" flag (FinanceSeed, F2Seed): once it is set by this host, a later test whose own
            // database needs the seeded rate table (the attendance tests of the office module) no longer gets it. The flags are put back as they were, so this test leaves the process as it found it.
            var seedFlags = new[] { typeof(Services.NasbNiro.Finance.FinanceSeed), typeof(Services.NasbNiro.Finance.FinanceSeed).Assembly.GetType("Services.NasbNiro.Finance.F2Seed") }
                .Where(t => t != null).Select(t => t.GetField("_done", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)).Where(f => f != null).ToList();
            Assert.IsTrue(seedFlags.Count >= 1, "the finance seed flag was renamed: this test must put it back after the dashboard call");
            var before = seedFlags.Select(f => f.GetValue(null)).ToList();
            try
            {
                var dash = Ok(await host.As(Ceo, p => p.GetRequiredService<Services.NasbNiro.Finance.ICeoDashboardService>().GetAsync(Ct)));
                Assert.AreEqual(1, dash.EnPlatformsIncomplete);
                Assert.AreEqual(1, dash.EnPlatformsTotal);
                Assert.AreEqual(0, dash.EnOpenDebts);
            }
            finally { for (int i = 0; i < seedFlags.Count; i++) seedFlags[i].SetValue(null, before[i]); }
        }

        [TestMethod]
        public async Task The_Engineering_Day_Moved_Later_Makes_Everything_Before_It_Legacy_Like_The_Package_G_Cut_Over()
        {
            AppHost host = Started("ho6");
            int contract = Contract(host);
            Assert.IsFalse(await host.As(Eng1, async p => await EnControl.ContractIsLegacyAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)), "after the cut-over instant of Package G the contract is controlled");
            host.Write(u => { AppHost.Add(u, new Entities.Model.NasbNiro.Quality.QualitySetting { Key = EnSettingKeys.ControlStartDate, Value = 14990101, Title = "t", Source = "t" }); u.SaveChanges(); }, Seeder);
            Assert.IsTrue(await host.As(Eng1, async p => await EnControl.ContractIsLegacyAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)), "the day set for the engineering control moved later: the contract is legacy");
            Assert.IsFalse(await host.As(Eng1, async p => await EnControl.ActiveAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct)), "a start in the future: the agent steps that are not tied to a record find nothing yet");
            Assert.IsNotNull(Services.NasbNiro.Integrity.SafetyProfile.Find(Entities.Model.NasbNiro.Integrity.SafetyStore.Quality, EnSettingKeys.ControlStartDate));
            Assert.IsTrue(Services.NasbNiro.Integrity.SafetyProfile.IsWeaker(Services.NasbNiro.Integrity.SafetyProfile.Find(Entities.Model.NasbNiro.Integrity.SafetyStore.Quality, EnSettingKeys.ControlStartDate), "14990101"));
        }

        [TestMethod]
        public void The_Engineering_E1_Catalogue_Has_Thirty_Nine_Steps_Of_The_Engineering_Agent_On_The_Right_Stations()
        {
            AppHost host = new AppHost("EnCat_" + Guid.NewGuid().ToString("N"));
            List<Services.NasbNiro.Agents.IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Where(s => s is Services.NasbNiro.Agents.Steps.EnStep).ToList())).GetAwaiter().GetResult();
            Assert.AreEqual(43, steps.Count);
            Assert.IsTrue(steps.All(s => s.AgentCode == (s.Code == "ENG.STANDARD.GAP" ? "AG-MASTER" : s.Code == "ENG.DIST.DRAFT" || s.Code == "ENG.DIST.NOTIFY" ? "AG-INIT" : "AG-ENG") && s.Code.StartsWith("ENG.") && s.Code.Length <= 60), "the main agent owns the standard-gap report, the project-initiation agent (PMO side) the two distribution steps, the engineering agent every other step");
            Assert.AreEqual(16, steps.Count(s => s.Station == 1));
            Assert.AreEqual(1, steps.Count(s => s.Station == 3));
            Assert.AreEqual(26, steps.Count(s => s.Station == 5));
            Assert.AreEqual(14, steps.Count(s => s.Kind == Entities.Model.NasbNiro.Agents.AgentStepKind.Agent));
            Assert.AreEqual(43, steps.Select(s => s.Code).Distinct().Count());
        }

        [TestMethod]
        public async Task Each_Unit_Gets_Its_Own_Notice_The_Pmo_May_Edit_It_Until_The_Release_And_The_Release_Needs_Every_Notice()
        {
            AppHost host = Started("dist1");
            EnSaveReferenceDto dto = Ref("VDDR-U", ("DWG", true, false), ("DATASHEET", true, false));
            dto.Lines[0].Units = "production, qc"; dto.Lines[1].Units = "Nope";
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(dto, Ct)), "شناخته‌شده");
            dto.Lines[1].Units = null;
            EnReferenceListDto list = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(dto, Ct)));
            Assert.AreEqual("Qc,Production", list.Lines[0].Units);
            Assert.IsTrue(string.IsNullOrEmpty(list.Lines[1].Units), "empty = all units");
            Ok(await RefAction(host, Eng1, list.Id, "submit"));
            Ok(await RefAction(host, Mgr1, list.Id, "approve"));
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "هیچ محصولی مطابق نیست"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-U"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = c.Id));
            Assert.AreEqual("Qc,Production", h.Lines.Single(l => l.DocumentType == "DWG").Units);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            // the line shows who it is for
            EnHandoverDto asProduction = Ok(await Get(host, Prod1, h.Id)), asQa = Ok(await Get(host, Qa1, h.Id)), asPmo = Ok(await Get(host, Pmo1, h.Id));
            Assert.IsTrue(asProduction.Lines.All(l => l.ForMyUnit));
            Assert.IsFalse(asQa.Lines.Single(l => l.DocumentType == "DWG").ForMyUnit);
            Assert.IsTrue(asQa.Lines.Single(l => l.DocumentType == "DATASHEET").ForMyUnit);
            Assert.IsTrue(asPmo.Lines.All(l => !l.ForMyUnit), "the PMO has no unit of its own");
            // no rows yet: no release
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "ابلاغ ممکن نیست");
            Refused(await Act(host, Eng1, "draft-distribution", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Qa1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto rows = Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Refused(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id), "قبلاً");
            Assert.AreEqual(6, rows.Distribution.Count);
            Assert.IsTrue(rows.Distribution.All(d => d.Status == (int)EnDistStatus.Draft && !string.IsNullOrWhiteSpace(d.ScopeNote)));
            EnDistributionDto prod = rows.Distribution.Single(d => d.Unit == (int)EnUnit.Production), qa = rows.Distribution.Single(d => d.Unit == (int)EnUnit.Qa);
            Assert.AreEqual(2, prod.DocumentCount);
            Assert.AreEqual(1, qa.DocumentCount);
            StringAssert.Contains(prod.ScopeNote, "مدرک DWG");
            Assert.IsFalse(qa.ScopeNote.Contains("مدرک DWG"), "the quality assurance notice lists only its own document");
            // only the PMO edits a notice, never to empty, never an AI seat
            ClaimsPrincipal seatPmo = await SeatAsync(host, "AG-ENG", "PMO");
            Refused(await Act(host, Eng1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "متن تازه"; }));
            Refused(await Act(host, Mgr1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "متن تازه"; }));
            Refused(await Act(host, seatPmo, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "متن تازه"; }));
            Refused(await Act(host, Pmo1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "  "; }), "خالی");
            Refused(await Act(host, Pmo1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = new string('x', 2001); }), "2000");
            EnHandoverDto edited = Ok(await Act(host, Pmo1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "متن تازهٔ PMO برای تضمین کیفیت"; }));
            Assert.AreEqual("متن تازهٔ PMO برای تضمین کیفیت", edited.Distribution.Single(d => d.Unit == (int)EnUnit.Qa).ScopeNote);
            // a notice that is empty in the data blocks the release
            host.Write(u => { u.Set<EnDistribution>().First(d => d.Id == qa.Id).ScopeNote = ""; u.SaveChanges(); }, Seeder);
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "ابلاغ ممکن نیست");
            host.Write(u => { u.Set<EnDistribution>().First(d => d.Id == qa.Id).ScopeNote = "متن تازهٔ PMO برای تضمین کیفیت"; u.SaveChanges(); }, Seeder);
            EnHandoverDto released = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.IsTrue(released.Distribution.All(d => d.Status == (int)EnDistStatus.Notified && !d.CanEditScope));
            Refused(await Act(host, Pmo1, "edit-scope", d => { d.DistributionId = qa.Id; d.ScopeNote = "پس از ابلاغ"; }));
            // the notification is sent by the agent step; before it a unit cannot confirm the receipt
            Refused(await Act(host, Qa1, "acknowledge", d => d.DistributionId = qa.Id), "هنوز ارسال نشده");
            Refused(await Act(host, Eng1, "distribute", d => d.Id = h.Id), "PMO");
            Ok(await Act(host, Pmo1, "distribute", d => d.Id = h.Id));
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = h.Id), "قبلاً");
            Ok(await Act(host, Qa1, "acknowledge", d => d.DistributionId = qa.Id));
        }

        [TestMethod]
        public async Task The_Handover_Agent_Steps_Discover_Run_And_Do_Not_Repeat()
        {
            AppHost host = Started("ho4");
            (int contract, int caseId) = await NewDesign(host);
            ClaimsPrincipal seat = await SeatAsync(host), initSeat = await SeatAsync(host, "AG-INIT", "PLANNING");
            DateTime today = DateTime.Today;
            ClaimsPrincipal Owner(string code) => code == "ENG.DIST.DRAFT" || code == "ENG.DIST.NOTIFY" ? initSeat : seat;   // the two distribution steps belong to the PMO-side agent
            async Task<List<Services.NasbNiro.Agents.AgentWorkItem>> Discover(string code) => await host.As(Owner(code), async p =>
            {
                Services.NasbNiro.Agents.IAgentStep step = p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == code);
                return await step.DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), today, Ct);
            });
            async Task<Services.NasbNiro.Agents.AgentStepResult> Run(string code, Services.NasbNiro.Agents.AgentWorkItem item) => await host.As(Owner(code), async p =>
            {
                Services.NasbNiro.Agents.IAgentStep step = p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == code);
                return await step.RunAsync(item, p, Ct);
            });
            var draft = await Discover("ENG.HANDOVER.DRAFT");
            Assert.AreEqual(1, draft.Count);
            Assert.IsTrue((await Run("ENG.HANDOVER.DRAFT", draft[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done);
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.DRAFT")).Count, "an open handover is not drafted again");
            // ENG-E1f: the draft is incomplete; it still goes to the PMO (the submit gate is open) and the CEO is not asked during the rounds
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.COMPLETE")).Count);
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.SUBMIT")).Count);
            StringAssert.Contains((await Discover("ENG.HANDOVER.SUBMIT"))[0].Summary, "ناقص");
            Assert.AreEqual(0, (await Discover("ENG.DEBT.SETTLE")).Count);
            Assert.AreEqual(0, (await Discover("ENG.DIST.NOTIFY")).Count);
            int handover = host.Read(u => u.Set<EnHandover>().Single().Id);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = handover));
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.SUBMIT")).Count);
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.COMPLETE")).Count, "no permit gate while the PMO still reviews");
            Assert.AreEqual(0, (await Discover("ENG.WAIVER.DECIDE")).Count);
            var accept = await Discover("ENG.HANDOVER.ACCEPT");
            Assert.AreEqual(1, accept.Count);
            StringAssert.Contains(accept[0].Summary, "ناقص");
            Assert.AreEqual(0, (await Discover("ENG.DEBT.SUPPLY")).Count, "no debt before the release");
            Ok(await Act(host, Pmo1, "accept", d => d.Id = handover));
            // accepted and still incomplete: the manager's permit gate appears, the PMO's release gate says why it waits
            var ask = await Discover("ENG.HANDOVER.COMPLETE");
            Assert.AreEqual(1, ask.Count);
            StringAssert.Contains(ask[0].Summary, "مجوز اضطرار");
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.RELEASE")).Count, "ENG-E1f fix G3: locked, so it is not the PMO's turn (the waiting work is the manager's permit request)");
            Assert.AreEqual("ENGINEERING", ask[0].GateRoles, "the manager roles");
            Ok(await Request(host, Mgr1, handover, "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(24)));
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.COMPLETE")).Count, "the permit is asked once");
            Assert.AreEqual(1, (await Discover("ENG.WAIVER.DECIDE")).Count);
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Refused(await Act(host, Pmo2, "release", d => d.Id = handover), "ابلاغ به اجرا قفل است");
            Ok(await Decide(host, Ceo, waiver, "approve"));
            Assert.AreEqual(0, (await Discover("ENG.WAIVER.DECIDE")).Count);
            var distDraft = await Discover("ENG.DIST.DRAFT");
            Assert.AreEqual(1, distDraft.Count);
            Assert.IsTrue((await Run("ENG.DIST.DRAFT", distDraft[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done);
            Assert.AreEqual(0, (await Discover("ENG.DIST.DRAFT")).Count, "the rows are built once");
            Assert.AreEqual(0, (await Discover("ENG.DIST.NOTIFY")).Count, "nothing is notified before the release");
            var open = await Discover("ENG.HANDOVER.RELEASE");
            Assert.AreEqual(1, open.Count);
            Assert.IsFalse(open[0].Summary.Contains("قفل"), "the permit is approved: the gate no longer says it waits");
            Ok(await Act(host, Pmo2, "release", d => d.Id = handover));
            Assert.AreEqual(2, (await Discover("ENG.DEBT.SUPPLY")).Count, "the debts exist from the release");
            var notify = await Discover("ENG.DIST.NOTIFY");
            Assert.AreEqual(1, notify.Count);
            Assert.IsTrue((await Run("ENG.DIST.NOTIFY", notify[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done);
            Assert.AreEqual(0, (await Discover("ENG.DIST.NOTIFY")).Count);
            Assert.AreEqual(6, (await Discover("ENG.DIST.ACK")).Count);
            Deliverable(host, contract, "DWG");
            var settle = await Discover("ENG.DEBT.SETTLE");
            Assert.AreEqual(1, settle.Count);
            Assert.AreEqual(1, (await Discover("ENG.DEBT.SUPPLY")).Count);
            Assert.IsTrue((await Run("ENG.DEBT.SETTLE", settle[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done);
            Assert.AreEqual(0, (await Discover("ENG.DEBT.SETTLE")).Count);
            Assert.IsTrue((await Run("ENG.DEBT.SETTLE", settle[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done, "a second run is harmless");
        }

        // ================================================================ fix round A

        private const string LongReason = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده";

        private static async Task<(int Contract, int CaseId, EnHandoverDto Draft)> CompleteDraftAsync(AppHost host)
        {
            (int contract, int caseId) = await NewDesign(host);
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(0, h.MissingRequiredCount);
            return (contract, caseId, h);
        }

        private static void Edit(AppHost host, int caseId, Action<EnCase> change) =>
            host.Write(u => { change(u.Set<EnCase>().Single(x => x.Id == caseId)); u.SaveChanges(); }, Seeder);

        [TestMethod]
        public async Task Submit_Accept_And_Release_Each_Re_Check_That_The_Classification_Approval_Is_Still_In_Force()
        {
            AppHost host = Started("fixc2");
            (int contract, int caseId, EnHandoverDto h) = await CompleteDraftAsync(host);
            EnCase before = host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId));
            const string fragment = "تصویب نوع پرونده معتبر نیست";

            // the CEO signature, the manager signature, the classification hash and the gap hash: each one alone stops the submit
            Edit(host, caseId, c => c.CeoApprovedAt = null);
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), fragment);
            Assert.IsFalse(Ok(await Get(host, Mgr1, h.Id)).CanSubmit, "the button says the same as the service");
            Edit(host, caseId, c => { c.CeoApprovedAt = before.CeoApprovedAt; c.EngConfirmedByUserId = null; });
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), fragment);
            Edit(host, caseId, c => { c.EngConfirmedByUserId = before.EngConfirmedByUserId; c.ClassificationHash = "0000"; });
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), fragment);
            Edit(host, caseId, c => { c.ClassificationHash = before.ClassificationHash; c.ApprovedGapHash = "a-gap-that-never-existed"; });
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), fragment);
            Edit(host, caseId, c => c.ApprovedGapHash = null);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));

            // accept
            Edit(host, caseId, c => c.CeoApprovedByUserId = null);
            Refused(await Act(host, Pmo1, "accept", d => d.Id = h.Id), fragment);
            Edit(host, caseId, c => c.CeoApprovedByUserId = before.CeoApprovedByUserId);
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));

            // release
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Edit(host, caseId, c => c.ClassificationHash = "1111");
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), fragment);
            Assert.IsFalse(Ok(await Get(host, Pmo2, h.Id)).CanRelease);
            Edit(host, caseId, c => c.ClassificationHash = before.ClassificationHash);
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
        }

        [TestMethod]
        public async Task A_Stale_Standard_Gap_Report_After_The_Approval_Stops_The_Handover_And_The_Tender_Release()
        {
            AppHost host = Started("fixh6");
            await ApprovedList(host, "BASE-X", ("DWG", true, false));
            (int contract, int caseId, EnHandoverDto h) = await CompleteDraftAsync(host);
            int baseRev = host.Read(u => u.Set<EnReferenceList>().Single(l => l.Code == "BASE-X" && l.Status == EnRefStatus.Approved).Revision);
            int targetRev = host.Read(u => u.Set<EnReferenceList>().Single(l => l.Code == "VDDR-H" && l.Status == EnRefStatus.Approved).Revision);
            host.Write(u =>
            {
                AppHost.Add(u, new EnStandardGap { Number = "ESG-T-1", CaseId = caseId, BaseCode = "BASE-X", TargetCode = "VDDR-H", BaseRevision = baseRev, TargetRevision = targetRev, Status = EnGapStatus.Confirmed, ContentHash = "gap-1", CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.Set<EnCase>().Single(x => x.Id == caseId).ApprovedGapHash = "gap-1";
                u.SaveChanges();
            }, Seeder);
            Assert.IsTrue(Ok(await Get(host, Mgr1, h.Id)).CanSubmit, "the report still matches the approval");
            // a new approved revision of the base list after the approval: the report is out of date
            host.Write(u => { u.Set<EnReferenceList>().Single(l => l.Code == "BASE-X" && l.Status == EnRefStatus.Approved).Revision = baseRev + 1; u.SaveChanges(); }, Seeder);
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), "تصویب نوع پرونده معتبر نیست");
            host.Write(u => { u.Set<EnReferenceList>().Single(l => l.Code == "BASE-X" && l.Status == EnRefStatus.Approved).Revision = baseRev; u.Set<EnStandardGap>().Single().Status = EnGapStatus.Void; u.SaveChanges(); }, Seeder);
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), "تصویب نوع پرونده معتبر نیست");   // a voided report
        }

        [TestMethod]
        public async Task A_Reclassification_And_A_Standard_Change_Void_The_Open_Handover_And_The_Agent_Drafts_A_New_One_After_The_New_Approval()
        {
            AppHost host = Started("fixvoid");
            (int contract, int caseId, EnHandoverDto h) = await CompleteDraftAsync(host);
            await ApprovedList(host, "OTHER-STD", ("DWG", true, false), ("DATASHEET", true, false));
            // the manager sends the case back for a new decision: the open draft is voided with a Persian reason, in the same save
            Ok(await CaseAct(host, Mgr1, caseId, "reclassify", d => d.Note = "دلیل تغییر نوع پرونده برای آزمون"));
            EnHandover voided = host.Read(u => u.Set<EnHandover>().Single(x => x.Id == h.Id));
            Assert.AreEqual(EnHandoverStatus.Void, voided.Status);
            StringAssert.Contains(voided.VoidReason, "تصمیم تازه");
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.IsTrue(host.Read(u => u.Set<EnCaseEvent>().Any(e => e.CaseId == caseId && e.Kind == "handover-void")));
            // the new decision round, then the agent drafts round 2
            Ok(await CaseAct(host, Mgr2, caseId, "confirm"));
            Ok(await CaseAct(host, Ceo, caseId, "approve"));
            EnHandoverDto second = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(2, second.Round);
            // a changed standard is a new decision round too: the open draft is voided
            Ok(await SetStandard(host, Eng1, caseId, "OTHER-STD", note: "کارفرما استاندارد را در دیتاشیت اصلاح کرد"));
            Assert.AreEqual(EnHandoverStatus.Void, host.Read(u => u.Set<EnHandover>().Single(x => x.Id == second.Id)).Status);
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, Ok(await GetCase(host, Mgr1, caseId)).Status);
        }

        [TestMethod]
        public async Task The_Waiver_Deadline_Is_In_Hours_Counts_From_The_Release_And_The_Ceo_May_Only_Lower_It()
        {
            AppHost host = Started("fixhours");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Refused(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, Reason = LongReason, DebtHours = 72 }, Ct)), "پس از پایان رفت‌وبرگشت");   // ENG-E1f: not in the draft
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Func<int, Task<Common.Results.ResponseDto<EnWaiverDto>>> ask = hours => host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, Reason = LongReason, DebtHours = hours }, Ct));
            Refused(await ask(0), "ساعت");
            Refused(await ask(73), "72");
            EnWaiverDto w = Ok(await ask(72));
            Assert.AreEqual(72, w.DebtHours);
            Assert.IsNull(w.DebtDueAt, "no deadline before the CEO approves");
            // the request waits a month: the deadline still starts at the approval
            host.Write(u => { foreach (EnWaiver x in u.Set<EnWaiver>().ToList()) x.RequestedAt = DateTimeOffset.Now.AddDays(-30); u.SaveChanges(); }, Seeder);
            Refused(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = w.Id, Decision = "approve", DebtHours = 73 }, Ct)), "کوتاه‌تر");   // raising is refused
            EnWaiverDto done = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = w.Id, Decision = "approve", DebtHours = 24 }, Ct)));
            Assert.AreEqual(24, done.DebtHours);
            Assert.IsNull(done.DebtDueAt, "the approval no longer starts the clock: no instant before the release");
            // ENG-E1f fix G2: the clock starts at the release to execution; the approved 24 office hours are kept
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            DateTimeOffset before = DateTimeOffset.Now.AddMinutes(-1);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            DateTimeOffset after = DateTimeOffset.Now.AddMinutes(1);
            EnDebtDto first = rel.Debts[0];
            Assert.IsTrue(first.DueAt >= Services.NasbNiro.Office.OfficeClock.Add(before, 24) && first.DueAt <= Services.NasbNiro.Office.OfficeClock.Add(after, 24), "release time + 24 OFFICE hours");
            Assert.IsTrue(first.DueAt > DateTimeOffset.Now, "never already in the past");
            Assert.IsTrue(Math.Abs((rel.Waiver.DebtDueAt.Value - first.DueAt).TotalSeconds) < 1, "the waiver shows the same instant after the release");
        }

        [TestMethod]
        public async Task The_Debt_Of_A_System_Bom_Or_Opc_Line_Settles_When_The_Bom_Is_Released_And_The_Routing_Approved()
        {
            AppHost host = Started("fixsys");
            (int contract, int caseId) = await NewDesign(host);
            int product = Product(host);
            Edit(host, caseId, c => c.ProductId = product);
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(2, h.MissingRequiredCount, "the BOM and the OPC line of the system are missing");
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, Reason = LongReason, DebtHours = 48 }, Ct)));
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await Decide(host, Ceo, waiver, "approve"));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto s = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual(2, s.Debts.Count);
            Assert.AreEqual(0, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))), "nothing is settled without the proof");
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { ProductId = product, Code = "BOM-FIX", AssemblyPartNumber = "SYS", Title = "BOM", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
                u.SaveChanges();
            }, Seeder);
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))), "the released BOM settles the BOM debt only");
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbOpcRouting { ProductId = product, PartNumber = "SYS", Title = "OPC", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Approved, PreparedByUserId = 1 });
                u.SaveChanges();
            }, Seeder);
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))));
            Assert.IsTrue(host.Read(u => u.Set<EnDocumentDebt>().All(d => d.Status == EnDebtStatus.Settled)));
        }

        [TestMethod]
        public async Task A_Waiver_On_An_Unconfirmed_Dossier_Creates_A_Dossier_Debt_That_Settles_When_The_Manager_Confirms()
        {
            AppHost host = Started("fixdoss");
            await ApprovedList(host, "VDDR-P", ("DWG", true, false));
            int product = Product(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "VDDR-P"));
            Ok(await Supply(host, Eng1, d.Lines.Single(l => l.DocumentType == "DWG").Id, File(host, 2111)));
            host.Write(u =>
            {
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { ProductId = product, Code = "BOM-D", AssemblyPartNumber = "SYS", Title = "BOM", Revision = 1, Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
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
            Assert.AreEqual(0, h.MissingRequiredCount, "no line is missing: only the dossier confirmation locks the handover");
            StringAssert.Contains(h.LockMessage, "قفل");
            Ok(await Act(host, Mgr1, "submit", x => x.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", x => x.Id = h.Id));   // the PMO accepts the package with an unconfirmed dossier; only the release is locked
            Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, Reason = LongReason, DebtHours = 24 }, Ct)));
            Ok(await Decide(host, Ceo, host.Read(u => u.Set<EnWaiver>().Single().Id), "approve"));
            Ok(await Act(host, Pmo1, "draft-distribution", x => x.Id = h.Id));
            EnHandoverDto s = Ok(await Act(host, Pmo2, "release", x => x.Id = h.Id));
            EnDebtDto debt = s.Debts.Single();
            Assert.AreEqual(EnDebtTypes.DossierConfirm, debt.DocumentType);
            Assert.IsNull(debt.HandoverLineId);
            Assert.AreEqual(0, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))), "the dossier is not confirmed yet");
            Ok(await Dossier(host, Eng1, product, "complete", code: "VDDR-P"));
            Ok(await Dossier(host, Mgr1, product, "confirm", code: "VDDR-P"));
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(Ct))));
            Assert.AreEqual(EnDebtStatus.Settled, host.Read(u => u.Set<EnDocumentDebt>().Single().Status));
        }
    }
}
