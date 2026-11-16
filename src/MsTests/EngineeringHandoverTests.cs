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

        private static Task<ResponseDto<EnHandoverDto>> Act(AppHost host, ClaimsPrincipal who, string action, Action<EnHandoverActionDto> set = null)
        {
            EnHandoverActionDto dto = new EnHandoverActionDto { Action = action };
            set?.Invoke(dto);
            return host.As(who, p => p.GetRequiredService<IEnHandoverService>().HandoverActionAsync(dto, Ct));
        }

        private static Task<ResponseDto<EnWaiverDto>> Request(AppHost host, ClaimsPrincipal who, int handover, string reason, DateTimeOffset due) =>
            host.As(who, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = handover, Reason = reason, DebtDueAt = due }, Ct));

        private static Task<ResponseDto<EnWaiverDto>> Decide(AppHost host, ClaimsPrincipal who, int waiver, string decision, string note = null) =>
            host.As(who, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = decision, Note = note }, Ct));

        private static Task<ResponseDto<EnHandoverDto>> Get(AppHost host, ClaimsPrincipal who, int id) => host.As(who, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct));

        private static int Deliverable(AppHost host, int contract, string type, PcIssueState state = PcIssueState.Approved, bool file = true)
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
        private static async Task<(int Contract, int CaseId)> NewDesign(AppHost host)
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
            Refused(await Act(host, Eng1, "draft", d => d.CaseId = caseId), "تحویل باز");
            // the lock: the manager cannot submit and the button says why
            h = Ok(await Get(host, Mgr1, h.Id));
            Assert.IsFalse(h.CanSubmit);
            StringAssert.Contains(h.SubmitBlockedReason, "قفل");
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), "قفل");
            Refused(await Act(host, Eng1, "submit", d => d.Id = h.Id));

            // the waiver request: the manager only, a real reason, a future date within the maximum
            DateTimeOffset due = DateTimeOffset.Now.AddHours(72);   // exactly the ceiling is accepted
            Refused(await Request(host, Eng1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", due));
            Refused(await Request(host, Mgr1, h.Id, "کوتاه", due), "50");
            Refused(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(-1)), "آینده");
            Refused(await Request(host, Mgr1, h.Id, new string((char)0x645, 49), due), "50");   // 49 characters
            Refused(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(73)), "72");
            EnWaiverDto w = Ok(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", due));
            StringAssert.StartsWith(w.Number, "EWV-");
            Assert.AreEqual(2, w.MissingLines.Count);
            Refused(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", due), "معافیت باز");
            // only the CEO decides, and still the lock holds until then
            Refused(await Decide(host, Mgr2, w.Id, "approve"), "مدیرعامل");
            Refused(await Act(host, Mgr1, "submit", d => d.Id = h.Id), "قفل");
            Refused(await Decide(host, Ceo, w.Id, "reject", "کوتاه"), "15");
            Refused(await Decide(host, Ceo, w.Id, "maybe"), "نامعتبر");
            w = Ok(await Decide(host, Ceo, w.Id, "approve"));
            Assert.AreEqual((int)EnWaiverStatus.Approved, w.Status);
            Refused(await Decide(host, Ceo, w.Id, "approve"), "در انتظار");

            // a document that was missing at the request and is missing now is covered; a line that gets lost afterwards is not
            int dwg = Deliverable(host, contract, "DWG");
            SetState(host, dwg, PcIssueState.FirstIssue);
            Assert.IsTrue(Ok(await Get(host, Mgr1, h.Id)).CanSubmit, "the same missing set is covered");
            Ok(await Act(host, Eng1, "refresh", d => d.Id = h.Id));
            Assert.AreEqual(2, Ok(await Get(host, Mgr1, h.Id)).MissingRequiredCount);

            EnHandoverDto s = Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual((int)EnHandoverStatus.Submitted, s.Status);
            Assert.IsFalse(s.Complete);
            Assert.AreEqual(2, s.Debts.Count);
            Assert.IsTrue(s.Debts.All(d => d.Status == (int)EnDebtStatus.Open && d.DueAt == due));
            Assert.AreEqual((int)EnCaseStatus.HandedOver, Ok(await GetCase(host, Mgr1, caseId)).Status);
            // the waiver is spent: a new handover round needs a new one
            Refused(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", due));

            // the PMO side: not the commercial, not the submitter; the return needs a reason and voids the debts of the round
            Refused(await Act(host, Comm1, "accept", d => d.Id = h.Id));
            Refused(await Act(host, Mgr1, "accept", d => d.Id = h.Id));
            Refused(await Act(host, Pmo1, "return", d => { d.Id = h.Id; d.Note = "کوتاه"; }), "15");
            EnHandoverDto r = Ok(await Act(host, Pmo1, "return", d => { d.Id = h.Id; d.Note = "نقشه‌های اجرایی هنوز کامل نیست"; }));
            Assert.AreEqual((int)EnHandoverStatus.Returned, r.Status);
            Assert.IsTrue(Ok(await Get(host, Mgr1, h.Id)).Debts.All(d => d.Status == (int)EnDebtStatus.Void));
            EnHandoverDto r2 = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.AreEqual(2, r2.Round);

            // round two: the drawing now exists (approved with a file), the datasheet does not
            SetState(host, dwg, PcIssueState.Approved);
            Ok(await Act(host, Eng1, "refresh", d => d.Id = r2.Id));
            r2 = Ok(await Get(host, Mgr1, r2.Id));
            Assert.AreEqual(1, r2.MissingRequiredCount);
            EnWaiverDto w2 = Ok(await Request(host, Mgr1, r2.Id, "مدرک برگه داده تا پایان هفته از تامین‌کننده می‌رسد", due));
            Assert.AreEqual(1, w2.MissingLines.Count);
            Ok(await Decide(host, Ceo, w2.Id, "approve"));
            // the drawing is withdrawn after the approval: the snapshot no longer covers the missing set
            SetState(host, dwg, PcIssueState.FirstIssue);
            Refused(await Act(host, Mgr1, "submit", d => d.Id = r2.Id), "پوشش");
            SetState(host, dwg, PcIssueState.Approved);
            s = Ok(await Act(host, Mgr1, "submit", d => d.Id = r2.Id));
            Assert.AreEqual(1, s.Debts.Count);

            // accept and release: the releaser did not submit or prepare; only the PMO releases
            Ok(await Act(host, Pmo1, "accept", d => d.Id = r2.Id));
            Refused(await Act(host, Eng1, "release", d => d.Id = r2.Id));
            Refused(await Act(host, Mgr1, "release", d => d.Id = r2.Id));
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = r2.Id));
            Assert.AreEqual((int)EnHandoverStatus.Released, rel.Status);
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
            EnHandoverDto dist = Ok(await Act(host, Eng1, "distribute", d => d.Id = r2.Id));
            Assert.AreEqual(6, dist.Distribution.Count);
            Refused(await Act(host, Eng1, "distribute", d => d.Id = r2.Id), "قبلاً");
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
            EnHandoverDto h = Ok(await Act(host, Mgr1, "draft", d => d.CaseId = caseId));
            Ok(await Request(host, Mgr1, h.Id, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(24)));
            host.Write(u => { foreach (EnWaiver w in u.Set<EnWaiver>().ToList()) { w.RequestedAt = DateTimeOffset.Now.AddDays(-30); } u.SaveChanges(); }, Seeder);
            ClaimsPrincipal seat = await SeatAsync(host);
            List<Services.NasbNiro.Agents.AgentWorkItem> gate = await host.As(seat, async p => await p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == "ENG.WAIVER.DECIDE").DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), DateTime.Today, Ct));
            Assert.AreEqual(1, gate.Count, "thirty days later the gate is still open");
            EnOverviewDto o = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.WaiversAwaitingCeo);
            Assert.IsTrue(o.OldestWaiverWaitingHours >= 719 && o.OldestWaiverWaitingHours <= 721);
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
            ClaimsPrincipal seatPmo = await SeatAsync(host, "AG-ENG", "PMO");
            Refused(await Act(host, seatPmo, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Refused(await Act(host, seatPmo, "release", d => d.Id = h.Id));
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            EnHandoverDto dist = Ok(await Act(host, seat, "distribute", d => d.Id = h.Id));
            ClaimsPrincipal seatQa = await SeatAsync(host, "AG-ENG", "QA");
            Refused(await Act(host, seatQa, "acknowledge", d => d.DistributionId = dist.Distribution.First().Id));
        }

        [TestMethod]
        public async Task A_Platform_Handover_Is_Locked_While_The_Dossier_Is_Unconfirmed_And_Legacy_Contracts_Have_No_Case()
        {
            AppHost host = Started("ho3");
            await ApprovedList(host, "VDDR-P", ("DWG", true, false));
            int product = Product(host);
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-P"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = c.Id));
            h = Ok(await Get(host, Mgr1, h.Id));
            Assert.IsFalse(h.CanSubmit);
            StringAssert.Contains(h.LockMessage, "قفل");
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
            var dash = Ok(await host.As(Ceo, p => p.GetRequiredService<Services.NasbNiro.Finance.ICeoDashboardService>().GetAsync(Ct)));
            Assert.AreEqual(1, dash.EnPlatformsIncomplete);
            Assert.AreEqual(1, dash.EnPlatformsTotal);
            Assert.AreEqual(0, dash.EnOpenDebts);
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
        public void The_Engineering_E1_Catalogue_Has_Thirty_Five_Steps_Of_The_Engineering_Agent_On_The_Right_Stations()
        {
            AppHost host = new AppHost("EnCat_" + Guid.NewGuid().ToString("N"));
            List<Services.NasbNiro.Agents.IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Where(s => s is Services.NasbNiro.Agents.Steps.EnStep).ToList())).GetAwaiter().GetResult();
            Assert.AreEqual(35, steps.Count);
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-ENG" && s.Code.StartsWith("ENG.") && s.Code.Length <= 60));
            Assert.AreEqual(15, steps.Count(s => s.Station == 1));
            Assert.AreEqual(1, steps.Count(s => s.Station == 3));
            Assert.AreEqual(19, steps.Count(s => s.Station == 5));
            Assert.AreEqual(12, steps.Count(s => s.Kind == Entities.Model.NasbNiro.Agents.AgentStepKind.Agent));
            Assert.AreEqual(35, steps.Select(s => s.Code).Distinct().Count());
        }

        [TestMethod]
        public async Task The_Handover_Agent_Steps_Discover_Run_And_Do_Not_Repeat()
        {
            AppHost host = Started("ho4");
            (int contract, int caseId) = await NewDesign(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            DateTime today = DateTime.Today;
            async Task<List<Services.NasbNiro.Agents.AgentWorkItem>> Discover(string code) => await host.As(seat, async p =>
            {
                Services.NasbNiro.Agents.IAgentStep step = p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == code);
                return await step.DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), today, Ct);
            });
            async Task<Services.NasbNiro.Agents.AgentStepResult> Run(string code, Services.NasbNiro.Agents.AgentWorkItem item) => await host.As(seat, async p =>
            {
                Services.NasbNiro.Agents.IAgentStep step = p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Single(x => x.Code == code);
                return await step.RunAsync(item, p, Ct);
            });
            var draft = await Discover("ENG.HANDOVER.DRAFT");
            Assert.AreEqual(1, draft.Count);
            Assert.IsTrue((await Run("ENG.HANDOVER.DRAFT", draft[0])).Outcome == Services.NasbNiro.Agents.AgentStepOutcome.Done);
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.DRAFT")).Count, "an open handover is not drafted again");
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.COMPLETE")).Count);
            Assert.AreEqual(0, (await Discover("ENG.HANDOVER.SUBMIT")).Count);
            Assert.AreEqual(0, (await Discover("ENG.DEBT.SETTLE")).Count);
            Assert.AreEqual(0, (await Discover("ENG.DIST.NOTIFY")).Count);
            int handover = host.Read(u => u.Set<EnHandover>().Single().Id);
            Ok(await Request(host, Mgr1, handover, "دلیل کافی برای این معافیت اضطراری نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DateTimeOffset.Now.AddHours(24)));
            Assert.AreEqual(1, (await Discover("ENG.WAIVER.DECIDE")).Count);
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await Decide(host, Ceo, waiver, "approve"));
            Assert.AreEqual(0, (await Discover("ENG.WAIVER.DECIDE")).Count);
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.SUBMIT")).Count);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = handover));
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.ACCEPT")).Count);
            Assert.AreEqual(2, (await Discover("ENG.DEBT.SUPPLY")).Count);
            Ok(await Act(host, Pmo1, "accept", d => d.Id = handover));
            Assert.AreEqual(1, (await Discover("ENG.HANDOVER.RELEASE")).Count);
            Ok(await Act(host, Pmo2, "release", d => d.Id = handover));
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
    }
}
