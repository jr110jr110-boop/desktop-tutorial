using System;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 fix round B2: the Medium and Low findings of the independent review (distribution by the PMO side only, the stale dossier at accept, the debt of a new line, case cancel and reopen, tender makers, in-place reference lines, the gap service).</summary>
    [TestClass]
    public class EngineeringFixB2Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task M11_The_Notices_To_The_Units_Go_Only_Through_The_Pmo_Side_And_Never_Without_Rows()
        {
            AppHost host = Started("b2dist");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            host.Write(u => { u.Set<EnHandover>().First(x => x.Id == h.Id).Status = EnHandoverStatus.Released; u.SaveChanges(); }, Seeder);   // a handover released before the notice rows existed
            Refused(await Act(host, Eng1, "distribute", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Mgr1, "distribute", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, await SeatAsync(host), "distribute", d => d.Id = h.Id), "PMO");   // the engineering agent seat has no PMO role
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = h.Id), "ساخته نشده");
            Assert.AreEqual(0, host.Read(u => u.Set<EnDistribution>().Count(x => x.HandoverId == h.Id)), "no notice row is made out of nothing");
            Refused(await Act(host, Eng1, "draft-distribution", d => d.Id = h.Id), "PMO");
        }

        [TestMethod]
        public async Task M11_The_Two_Distribution_Steps_Belong_To_The_Project_Initiation_Agent_Whose_Seat_Has_No_Approving_Role()
        {
            AppHost host = new AppHost("EnB2Seat_" + Guid.NewGuid().ToString("N"));
            var steps = host.AsSystem(p => Task.FromResult(p.GetServices<Services.NasbNiro.Agents.IAgentStep>().Where(s => s.Code == "ENG.DIST.DRAFT" || s.Code == "ENG.DIST.NOTIFY").ToList())).GetAwaiter().GetResult();
            Assert.AreEqual(2, steps.Count);
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-INIT"));
            Assert.IsTrue(steps.All(s => s.GateInfo(new Services.NasbNiro.Agents.AgentWorkItem { TargetId = 1 }).Roles == EnRoles.Pmo));
            CollectionAssert.DoesNotContain(Services.NasbNiro.Ai.AiSeatCatalog.Find("AG-INIT").Roles, "PMO");   // round B3: no agent seat holds an approving role
            CollectionAssert.DoesNotContain(Services.NasbNiro.Ai.AiSeatCatalog.Find("AG-ENG").Roles, "PMO");
        }

        [TestMethod]
        public async Task M13_A_Submitted_Handover_Is_Accepted_But_Not_Released_After_The_Platform_Dossier_Lost_Its_Confirmation()
        {
            AppHost host = Started("b2stale");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            Ok(await Act(host, Eng1, "refresh", d => d.Id = h.Id));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            int product = Product(host);
            // the case is a platform whose dossier is not confirmed (it was returned after the submit)
            host.Write(u =>
            {
                EnCase c = u.Set<EnCase>().First(x => x.Id == caseId);
                c.ProposedClassification = EnClassification.Platform; c.Classification = EnClassification.Platform; c.ProposedProductId = product; c.ProductId = product;
                c.ClassificationHash = Services.NasbNiro.Engineering.EnCaseRules.ClassHash(c);
                u.SaveChanges();
            }, Seeder);
            // ENG-E1f: the PMO may accept (the content it reviewed); the release to execution is the locked step
            Assert.IsTrue(Ok(await host.As(Pmo1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(h.Id, Ct))).CanAccept);
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto shown = Ok(await host.As(Pmo2, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(h.Id, Ct)));
            Assert.IsFalse(shown.CanRelease);
            StringAssert.Contains(shown.ReleaseBlockedReason, "ابلاغ به اجرا قفل است");
            Assert.IsFalse(shown.Complete, "completeness is read live: the dossier lost its confirmation");
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "ابلاغ به اجرا قفل است");
            // a permit that covered the dossier (the order before ENG-E1f: used at the submit) keeps the release open
            host.Write(u => { u.Set<EnHandover>().First(x => x.Id == h.Id).WaiverId = 1; u.SaveChanges(); }, Seeder);
            Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
        }

        [TestMethod]
        public async Task M5_A_Debt_Of_A_Line_That_The_Submit_Added_Points_To_That_Line_When_The_Release_Creates_It()
        {
            AppHost host = Started("b2debt");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            // the draft lost a line row (as if the source appeared later): the submit refreshes the lines and adds it with no Id yet
            host.Write(u => { EnHandoverLine gone = u.Set<EnHandoverLine>().First(l => l.HandoverId == h.Id && l.DocumentType == "DWG"); u.Set<EnHandoverLine>().Remove(gone); u.SaveChanges(); }, Seeder);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, Reason = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده", DebtHours = 24 }, Ct)));
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = "approve" }, Ct)));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto s = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual(2, s.Debts.Count);
            var lineIds = host.Read(u => u.Set<EnHandoverLine>().Where(l => l.HandoverId == h.Id).Select(l => l.Id).ToList());
            var debtLines = host.Read(u => u.Set<EnDocumentDebt>().Select(d => d.HandoverLineId).ToList());
            Assert.IsTrue(debtLines.All(x => x.HasValue && x.Value > 0 && lineIds.Contains(x.Value)), "every debt points to a saved line of the handover");
        }

        [TestMethod]
        public async Task M10_A_Case_Of_A_Live_Contract_Is_Not_Cancelled_And_A_Cancelled_Case_Is_Opened_Again()
        {
            AppHost host = Started("b2cancel");
            (int contract, int caseId) = await NewDesign(host);
            string reason = "دلیل کافی برای لغو این پرونده نوشته شده است";
            Refused(await CaseAct(host, Mgr1, caseId, "cancel", d => d.Note = reason), "جاری");
            host.Write(u => { u.Set<ContractEntity>().First(x => x.Id == contract).Status = ContractStatus.Done; u.SaveChanges(); }, Seeder);
            Ok(await CaseAct(host, Mgr1, caseId, "cancel", d => d.Note = reason));
            Assert.AreEqual((int)EnCaseStatus.Cancelled, Ok(await GetCase(host, Mgr1, caseId)).Status);
            EnCaseDto again = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Assert.AreNotEqual(caseId, again.Id);
            Refused(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract), "وجود دارد");   // one live case per contract
            // an inquiry-stage case (no contract) is cancelled by the manager with a reason, and a new case may follow
            int inquiry = Inquiry(host);
            EnCaseDto q = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Refused(await CaseAct(host, Eng1, q.Id, "cancel", d => d.Note = reason));
            Refused(await CaseAct(host, Mgr1, q.Id, "cancel", d => d.Note = "کوتاه"), "دلیل");
            Ok(await CaseAct(host, Mgr1, q.Id, "cancel", d => d.Note = reason));
            Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
        }

        [TestMethod]
        public async Task M3_The_Person_Who_Supplied_A_Dossier_File_In_The_Tender_Package_Cannot_Release_It()
        {
            AppHost host = Started("b2maker");
            int product = Product(host);
            await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            Ok(await Supply(host, Mgr1, d.Lines.First(l => l.DocumentType == "DWG").Id, File(host, 2101)));   // the manager supplied the drawing
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), true, product);
            c = Ok(await CaseAct(host, await SeatAsync(host), c.Id, "draft-tender"));
            Assert.AreEqual(1, c.TenderDocumentCount);
            Refused(await CaseAct(host, Mgr1, c.Id, "release-tender"), "شخص دیگری");
            Assert.IsFalse(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            c = Ok(await CaseAct(host, Mgr2, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
        }

        [TestMethod]
        public async Task M1_M7_Approving_Keeps_One_Approved_List_And_Editing_A_Draft_Updates_Lines_In_Place()
        {
            AppHost host = Started("b2ref");
            EnReferenceListDto a = await ApprovedList(host, "VDDR-B2", ("DWG", true, false), ("NOTE", false, false));
            EnReferenceListDto rev = Ok(await RefAction(host, Eng1, a.Id, "revise"));
            Assert.AreEqual(2, rev.Revision);
            // a never-submitted draft: a removed line is deleted, kept lines keep their Id
            int dwgId = rev.Lines.Single(l => l.DocumentType == "DWG").Id;
            EnSaveReferenceDto edit = Ref("VDDR-B2", ("DWG", true, false), ("OPC", true, false));
            edit.Id = rev.Id; edit.RowVersion = rev.RowVersion;
            edit.Lines[0].Id = dwgId;
            EnReferenceListDto saved = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(edit, Ct)));
            Assert.AreEqual(2, saved.Lines.Count);
            Assert.AreEqual(dwgId, saved.Lines.Single(l => l.DocumentType == "DWG").Id, "updated in place");
            Assert.AreEqual(0, host.Read(u => u.Set<EnReferenceLine>().IgnoreQueryFilters().Count(l => l.ListId == rev.Id && l.Voided)));
            Assert.AreEqual(2, host.Read(u => u.Set<EnReferenceLine>().IgnoreQueryFilters().Count(l => l.ListId == rev.Id)));
            // submit, return: the list was submitted once, so a removed line is kept and flagged
            Ok(await RefAction(host, Eng1, rev.Id, "submit"));
            Ok(await RefAction(host, Mgr1, rev.Id, "return", "دلیل برگشت برای ویرایش فهرست نوشته شد"));
            EnReferenceListDto back = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetListAsync(rev.Id, Ct)));
            EnSaveReferenceDto second = Ref("VDDR-B2", ("DWG", true, false));
            second.Id = rev.Id; second.RowVersion = back.RowVersion;
            saved = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(second, Ct)));
            Assert.AreEqual(1, saved.Lines.Count, "the voided line is hidden from every read");
            Assert.AreEqual(1, host.Read(u => u.Set<EnReferenceLine>().IgnoreQueryFilters().Count(l => l.ListId == rev.Id && l.Voided)), "kept for the audit trail");
            // the approval supersedes the old revision and leaves exactly one Approved list
            Ok(await RefAction(host, Eng1, rev.Id, "submit"));
            Ok(await RefAction(host, Mgr1, rev.Id, "approve"));
            Assert.AreEqual(1, host.Read(u => u.Set<EnReferenceList>().Count(l => l.Code == "VDDR-B2" && l.Status == EnRefStatus.Approved)));
            Assert.AreEqual(EnRefStatus.Superseded, host.Read(u => u.Set<EnReferenceList>().First(l => l.Id == a.Id).Status));
        }

        [TestMethod]
        public async Task M12_The_Gap_Build_Is_A_Service_Method_With_A_Role_Check()
        {
            AppHost host = Started("b2gap");
            (int contract, int caseId) = await NewDesign(host);
            Refused(await host.As(Sales1, p => p.GetRequiredService<IEnStandardGapService>().BuildAsync(caseId, Ct)));
            Refused(await host.As(Nobody, p => p.GetRequiredService<IEnStandardGapService>().BuildAsync(caseId, Ct)));
            Assert.IsTrue(Ok(await host.As(Eng1, p => p.GetRequiredService<IEnStandardGapService>().BuildAsync(caseId, Ct))).Length > 0);
            var master = await SeatAsync(host, "AG-MASTER");
            Assert.IsTrue((await host.As(master, p => p.GetRequiredService<IEnStandardGapService>().BuildAsync(caseId, Ct))).Success);
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnStandardGapService>().BuildAsync(987654, Ct)), "پیدا نشد");
        }

        [TestMethod]
        public async Task L3_L5_A_Change_Of_The_Standard_Evidence_Alone_Sends_The_Case_Back_And_The_List_Code_Is_In_The_Hash()
        {
            AppHost host = Started("b2evid");
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Refused(await SetStandard(host, Eng1, c.Id, Std, "دیتاشیت دیگر، بند ۹"), "دلیل");   // an evidence change needs a reason like a code change
            c = Ok(await SetStandard(host, Eng1, c.Id, Std, "دیتاشیت دیگر، بند ۹", "دلیل کافی برای تغییر مستند استاندارد"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            EnCase before = host.Read(u => u.Set<EnCase>().First(x => x.Id == c.Id));
            string h1 = Services.NasbNiro.Engineering.EnCaseRules.ClassHash(before);
            before.HandoverListCode = "OTHER-LIST";
            Assert.AreNotEqual(h1, Services.NasbNiro.Engineering.EnCaseRules.ClassHash(before));
        }

        [TestMethod]
        public async Task L9_The_Seat_Ids_Are_Read_In_A_Stable_Order()
        {
            AppHost host = Started("b2seats");
            await SeatAsync(host);
            int[] ids = await host.AsSystem(async p => await EnActors.SeatIdsAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct));
            CollectionAssert.AreEqual(ids.OrderBy(x => x).ToArray(), ids);
            Assert.IsTrue(ids.Length > 0);
        }
    }
}
