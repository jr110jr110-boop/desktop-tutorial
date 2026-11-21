using System;
using System.Linq;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.SalesDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1: the case, its classification chain (maker is never checker, the CEO decides last, a seat never decides), the tender package and its release, the legacy mode and the clarifications through commercial.</summary>
    [TestClass]
    public class EngineeringCaseTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task A_Case_Opens_Only_For_Controlled_Work_And_Only_Once()
        {
            AppHost host = Started("case1");
            int inquiry = Inquiry(host);
            Refused(await CaseAct(host, Qa1, null, "open", d => d.InquiryId = inquiry));
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            StringAssert.StartsWith(c.Number, "ENG-");
            Assert.AreEqual((int)EnCaseStatus.Classifying, c.Status);
            Refused(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry), "وجود دارد");
            EnCaseDto spare = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.SpareParts)));   // spare parts and service are controlled too
            Assert.AreEqual((int)EnCaseKind.SpareParts, spare.Kind);
            Assert.AreEqual((int)EnCaseKind.Turbine, c.Kind);
            Refused(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, SlInquiryStatus.Lost)), "باز");
            Refused(await CaseAct(host, Eng1, null, "open", d => { d.InquiryId = inquiry; d.ContractId = 5; }), "یکی از");
            // an old inquiry (before the control started) is legacy: no case
            Refused(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.SteamTurbine, DateTimeOffset.Now.AddDays(-30))), "قدیمی");
            // a direct contract with a work order gets a case; one without a work order does not
            Refused(await CaseAct(host, Eng1, null, "open", d => d.ContractId = Contract(host, null, false)), "دستور کار");
            EnCaseDto k = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = Contract(host)));
            Assert.IsNotNull(k.ContractId);
        }

        [TestMethod]
        public async Task The_Classification_Chain_Needs_Three_Different_People_And_No_Seat()
        {
            AppHost host = Started("case2");
            int product = Product(host);
            await ApprovedList(host);   // the standard of a platform dossier is chosen from the approved lists
            await NativeDossier(host, product);
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Refused(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.Basis = "مبنای آزمون برای پلتفرم"; }), "محصول");
            Refused(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "کوتاه"; }), "مبنای");
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "مبنای آزمون برای پلتفرم"; }));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-TEST"));
            Refused(await CaseAct(host, Eng1, c.Id, "confirm"));                                  // an engineer is not a manager
            var seatMgr = await SeatAsync(host, "AG-ENG", "ENGINEERING");
            Refused(await CaseAct(host, seatMgr, c.Id, "confirm"), "انسان");
            Refused(await CaseAct(host, Ceo, c.Id, "approve"), "منتظر تصویب");
            // a manager who changes the proposal becomes an editor: another manager confirms
            c = Ok(await CaseAct(host, Mgr1, c.Id, "change", d => { d.Classification = 1; d.ProductId = product; d.WithChanges = true; d.ChangeNote = "پره‌ها تغییر می‌کند"; d.Note = "تغییر لازم برای مشتری است"; }));
            Assert.IsTrue(c.ProposedWithChanges);
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm"), "شخص دیگری");
            Ok(await CaseAct(host, Mgr2, c.Id, "confirm"));
            Refused(await CaseAct(host, Mgr1, c.Id, "approve"));                                  // not the CEO
            var seatCeo = await SeatAsync(host, "AG-ENG", "CEO");
            Refused(await CaseAct(host, seatCeo, c.Id, "approve"), "انسان");
            Refused(await CaseAct(host, Ceo, c.Id, "return", d => d.Note = "x"), "15");
            c = Ok(await CaseAct(host, Ceo, c.Id, "return", d => d.Note = "تغییر پره‌ها باید توجیه فنی داشته باشد"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            Assert.AreEqual("تغییر پره‌ها باید توجیه فنی داشته باشد", c.ReturnReason);
            Ok(await CaseAct(host, Mgr2, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Assert.AreEqual((int)EnClassification.Platform, c.Classification);
            Assert.AreEqual(product, c.ProductId);
            Assert.IsTrue(c.PlatformIncomplete, "point A: a warning only");
            Assert.IsNotNull(c.PlatformWarning);
            Assert.IsTrue(c.Events.Count >= 6);
        }

        [TestMethod]
        public async Task A_Contract_Case_Goes_Straight_To_Awarded_And_A_Reclassification_Needs_A_New_Round()
        {
            AppHost host = Started("case3");
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "هیچ محصولی مطابق نیست"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-TEST"));
            await EnsureMdl(host, "VDDR-TEST");
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual((int)EnCaseStatus.Awarded, c.Status);
            Refused(await CaseAct(host, Mgr1, c.Id, "reclassify", d => d.Note = "کوتاه"), "15");
            Refused(await CaseAct(host, Eng1, c.Id, "reclassify", d => d.Note = "مشتری پلتفرم موجود را پذیرفت"));
            c = Ok(await CaseAct(host, Mgr1, c.Id, "reclassify", d => d.Note = "مشتری پلتفرم موجود را پذیرفت"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm"), "شخص دیگری");
            Ok(await CaseAct(host, Mgr2, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.IsTrue(host.Read(u => u.Set<EnCaseEvent>().Count(e => e.CaseId == c.Id && e.Kind == "reclassify-requested")) == 1);
        }

        [TestMethod]
        public async Task A_New_Design_Tender_Package_Needs_A_Concept_Note_A_Document_And_A_Manager_Who_Did_Not_Make_It_And_A_Change_Voids_The_Release()
        {
            AppHost host = Started("tender1");
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            var seat = await SeatAsync(host);
            c = Ok(await CaseAct(host, seat, c.Id, "draft-tender"));
            Assert.IsNotNull(c.TenderPackageId);
            Refused(await CaseAct(host, Mgr1, c.Id, "release-tender"), "سند مفهومی");
            // Mgr2 adds a conceptual document through the tender package service: he made the package, so he does not release it
            Ok(await host.As(Mgr2, p => p.GetRequiredService<IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "فهرست اجزا", DocumentType = "CONCEPT" }, Ct)));
            Refused(await CaseAct(host, Mgr1, c.Id, "release-tender"), "یادداشت طراحی مفهومی");
            Refused(await CaseAct(host, Eng1, c.Id, "save-concept", d => d.ConceptNote = "کوتاه"), "یادداشت");
            c = Ok(await CaseAct(host, Eng1, c.Id, "save-concept", d => d.ConceptNote = "اجزای اصلی تعریف شده و ابعاد پوسته فرض شده است"));
            Refused(await CaseAct(host, Mgr2, c.Id, "release-tender"), "شخص دیگری");
            Refused(await CaseAct(host, Eng1, c.Id, "release-tender"));
            Refused(await CaseAct(host, seat, c.Id, "release-tender"));
            await ApprovedMdl(host, c.Id);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.IsFalse(c.TenderStale);
            // draft is idempotent and not allowed after the release; nothing changed: revalidation keeps the release
            Refused(await CaseAct(host, seat, c.Id, "draft-tender"), "مرحلهٔ تهیهٔ");
            c = Ok(await CaseAct(host, seat, c.Id, "revalidate-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            // a document added after the release voids it
            Ok(await host.As(Eng2, p => p.GetRequiredService<IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "نقشهٔ تکمیلی", DocumentType = "DWG" }, Ct)));
            Assert.IsTrue(Ok(await GetCase(host, Eng1, c.Id)).TenderStale);
            c = Ok(await CaseAct(host, seat, c.Id, "revalidate-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Assert.IsNull(c.TenderReleasedAtText);
        }

        [TestMethod]
        public async Task A_Platform_Package_Gets_The_Tender_Stage_Documents_From_The_Dossier_Idempotently()
        {
            AppHost host = Started("tender2");
            int product = Product(host);
            await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            Ok(await Supply(host, Eng1, d.Lines.First(l => l.DocumentType == "DWG").Id, File(host, 2111)));
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, true, product);
            var seat = await SeatAsync(host);
            c = Ok(await CaseAct(host, seat, c.Id, "draft-tender"));
            Assert.AreEqual(1, c.TenderDocumentCount, "the drawing; the BOM of the tender stage is not available yet");
            StringAssert.Contains(c.PlatformWarning, "مدرک BOM");
            c = Ok(await CaseAct(host, seat, c.Id, "draft-tender"));
            Assert.AreEqual(1, c.TenderDocumentCount, "idempotent");
            await ApprovedMdl(host, c.Id);
            c = Ok(await CaseAct(host, Mgr2, c.Id, "release-tender"));                         // a platform needs no minimum
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
        }

        [TestMethod]
        public async Task The_Award_Of_A_Won_Inquiry_Keeps_The_Decision_In_Force_And_Never_Repairs_A_Stale_One()
        {
            AppHost host = Started("awardbind");
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, false);
            int contract = Contract(host);
            host.Write(u =>
            {
                SlEstimate e = AppHost.Add(u, new SlEstimate { InquiryId = inquiry, Number = "EST-AW", Revision = 1, Status = SlEstimateStatus.Final, TotalCost = 100m, CreateDate = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new SlProposal { InquiryId = inquiry, EstimateId = e.Id, Number = "PRP-AW", Revision = 1, Price = 500m, Status = SlProposalStatus.Won, ContractId = contract, CreateDate = DateTimeOffset.Now, CreatorUserId = 1001, ModifyDate = DateTimeOffset.Now });
                u.Set<SlInquiry>().Single(i => i.Id == inquiry).Status = SlInquiryStatus.Won;
                u.SaveChanges();
            }, Seeder);
            c = Ok(await CaseAct(host, Eng1, c.Id, "award"));
            Assert.AreEqual((int)EnCaseStatus.Awarded, c.Status);
            EnCase row = host.Read(u => u.Set<EnCase>().First(x => x.Id == c.Id));
            Assert.AreEqual(contract, row.ContractId);
            Assert.AreEqual(Services.NasbNiro.Engineering.EnCaseRules.ClassHash(row), row.ClassificationHash, "the link of the contract must not put the approved decision out of force");
            Assert.IsNull(await host.As(Eng1, async p => await Services.NasbNiro.Engineering.EnDecision.RefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), row, Ct)));

            // a decision that was stale before the award stays stale: the award never re-binds changed content
            int inquiry2 = Inquiry(host, number: "INQ-AW-2");
            EnCaseDto c2 = await ApprovedCase(host, inquiry2, false);
            int contract2 = Contract(host);
            host.Write(u =>
            {
                SlEstimate e = AppHost.Add(u, new SlEstimate { InquiryId = inquiry2, Number = "EST-AW2", Revision = 1, Status = SlEstimateStatus.Final, TotalCost = 100m, CreateDate = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new SlProposal { InquiryId = inquiry2, EstimateId = e.Id, Number = "PRP-AW2", Revision = 1, Price = 500m, Status = SlProposalStatus.Won, ContractId = contract2, CreateDate = DateTimeOffset.Now, CreatorUserId = 1001, ModifyDate = DateTimeOffset.Now });
                u.Set<SlInquiry>().Single(i => i.Id == inquiry2).Status = SlInquiryStatus.Won;
                u.Set<EnCase>().Single(x => x.Id == c2.Id).ProposedChangeNote = "متن پس از تصویب دست‌کاری شده است";
                u.SaveChanges();
            }, Seeder);
            Ok(await CaseAct(host, Eng1, c2.Id, "award"));
            EnCase row2 = host.Read(u => u.Set<EnCase>().First(x => x.Id == c2.Id));
            Assert.AreNotEqual(Services.NasbNiro.Engineering.EnCaseRules.ClassHash(row2), row2.ClassificationHash);
        }

        [TestMethod]
        public async Task The_Proposal_Approval_Is_Refused_Until_The_Technical_Package_Is_Released_And_A_Legacy_Inquiry_Passes()
        {
            AppHost host = Started("choke1");
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, false);
            int proposal = 0;
            host.Write(u =>
            {
                SlEstimate e = AppHost.Add(u, new SlEstimate { InquiryId = inquiry, Number = "EST-1", Revision = 1, Status = SlEstimateStatus.Final, TotalCost = 100m, CreateDate = DateTimeOffset.Now });
                u.SaveChanges();
                SlProposal p = AppHost.Add(u, new SlProposal { InquiryId = inquiry, EstimateId = e.Id, Number = "PRP-1", Revision = 1, Price = 500m, Status = SlProposalStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = 1001, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); proposal = p.Id;
            }, Seeder);
            string message = Refused(await host.As(Ceo, p => p.GetRequiredService<ISlProposalService>().ProposalActionAsync(new SlProposalActionDto { Id = proposal, Action = "approve" }, Ct)));
            StringAssert.Contains(message, "بسته فنی مهندسی برای این استعلام هنوز به بازرگانی ابلاغ نشده است");
            EnGateStateDto gate = Ok(await host.As(Sales1, p => p.GetRequiredService<IEnCaseService>().GetGateStateAsync(inquiry, null, Ct)));
            Assert.IsTrue(gate.Controlled);
            Assert.IsFalse(gate.Passed);
            var seat = await SeatAsync(host);
            Ok(await CaseAct(host, seat, c.Id, "draft-tender"));
            int package = Ok(await GetCase(host, Eng1, c.Id)).TenderPackageId.Value;
            Ok(await host.As(Eng2, p => p.GetRequiredService<IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = package, Title = "فهرست اجزا", DocumentType = "CONCEPT" }, Ct)));
            Ok(await CaseAct(host, Eng2, c.Id, "save-concept", d => d.ConceptNote = "اجزای اصلی تعریف شده و ابعاد پوسته فرض شده است"));
            await ApprovedMdl(host, c.Id);
            Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            var approved = await host.As(Ceo, p => p.GetRequiredService<ISlProposalService>().ProposalActionAsync(new SlProposalActionDto { Id = proposal, Action = "approve" }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            // a legacy inquiry (created before the control started) passes with its note
            int old = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.SteamTurbine, DateTimeOffset.Now.AddDays(-40));
            EnGateStateDto legacy = Ok(await host.As(Sales1, p => p.GetRequiredService<IEnCaseService>().GetGateStateAsync(old, null, Ct)));
            Assert.IsTrue(legacy.Legacy);
            Assert.IsTrue(legacy.Passed);
            StringAssert.Contains(legacy.LegacyNote, "قدیمی");
        }

        [TestMethod]
        public async Task Nothing_Is_Controlled_Before_The_Control_Starts()
        {
            AppHost host = NewHost("legacy0");   // no activation recorded, the day setting is 0
            int inquiry = Inquiry(host);
            Refused(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry), "قدیمی");
            Assert.IsTrue(Ok(await host.As(Sales1, p => p.GetRequiredService<IEnCaseService>().GetGateStateAsync(inquiry, null, Ct))).Legacy);
        }

        [TestMethod]
        public async Task The_Charter_Approval_Waits_For_Engineering_To_Confirm_The_Current_Content()
        {
            AppHost host = Started("charter1");
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "هیچ محصولی مطابق نیست" ; }));
            Ok(await SetStandard(host, Eng1, c.Id, "VDDR-TEST"));
            await EnsureMdl(host, "VDDR-TEST");
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Refused(await CaseAct(host, Mgr1, c.Id, "charter-confirm"), "منشور پروژه هنوز ثبت نشده");
            host.Write(u => { AppHost.Add(u, new PcProjectCharter { ContractId = contract, ScopeDescription = "ساخت یک توربین", ManagerAcceptedAt = DateTimeOffset.Now, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            string blocked = Refused(await host.As(Ceo, p => p.GetRequiredService<IPcProjectService>().CharterActionAsync("approve", new PcApproveDto { ContractId = contract }, Ct)));
            StringAssert.Contains(blocked, "منشور پروژه هنوز توسط مهندسی تایید نشده است");
            Refused(await CaseAct(host, Eng1, c.Id, "charter-confirm"));
            Refused(await CaseAct(host, Mgr1, c.Id, "charter-return", d => d.Note = "کوتاه"), "15");
            c = Ok(await CaseAct(host, Mgr1, c.Id, "charter-return", d => d.Note = "محدودهٔ کار با قرارداد هم‌خوان نیست"));
            Assert.AreEqual((int)EnCharterOutcome.Returned, c.CharterReviewOutcome);
            StringAssert.Contains(Refused(await host.As(Ceo, p => p.GetRequiredService<IPcProjectService>().CharterActionAsync("approve", new PcApproveDto { ContractId = contract }, Ct))), "برگردانده");
            host.Write(u => { u.Set<PcProjectCharter>().First().ScopeDescription = "ساخت یک توربین با شرح اصلاح‌شده"; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CharterStale);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "charter-confirm"));
            Assert.AreEqual((int)EnCharterOutcome.Confirmed, c.CharterReviewOutcome);
            Assert.IsNull(await host.As(Ceo, async p => await Services.NasbNiro.Engineering.EnGates.CharterApproveRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)), "confirmed content passes");
            // a change after the confirmation voids it
            host.Write(u => { u.Set<PcProjectCharter>().First().ScopeDescription = "ساخت دو توربین"; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await host.As(Ceo, async p => await Services.NasbNiro.Engineering.EnGates.CharterApproveRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), contract, Ct)));
        }

        [TestMethod]
        public async Task The_Sent_And_Answer_Fields_Of_A_Clarification_Are_Commercial_Only_And_A_Reask_Follows_The_Answered_One()
        {
            AppHost host = Started("clar1");
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            EnClarificationDto q = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnClarificationService>().SaveClarificationAsync(new EnSaveClarificationDto { CaseId = c.Id, Question = "فشار بخار ورودی چقدر است؟" }, Ct)));
            StringAssert.StartsWith(q.Number, "ECQ-");
            Refused(await host.As(Nobody, p => p.GetRequiredService<IEnClarificationService>().SaveClarificationAsync(new EnSaveClarificationDto { CaseId = c.Id, Question = "x" }, Ct)));
            Refused(await Act(host, Comm1, q.Id, "send", d => d.ClientLetterRef = "N-1"), "فقط پرسش نزد بازرگانی");
            Ok(await Act(host, Eng1, q.Id, "submit"));
            Refused(await Act(host, Eng1, q.Id, "send", d => d.ClientLetterRef = "N-1"), "بازرگانی");
            Refused(await Act(host, Eng1, q.Id, "answer", d => d.Answer = "ده بار"), "بازرگانی");
            Refused(await Act(host, Comm1, q.Id, "send"), "شمارهٔ نامه");
            Ok(await Act(host, Comm1, q.Id, "send", d => d.ClientLetterRef = "N-1405/77"));
            Refused(await Act(host, Eng1, q.Id, "answer", d => d.Answer = "ده بار"), "بازرگانی");
            Refused(await Act(host, Comm1, q.Id, "answer"), "پاسخ");
            Refused(await Act(host, Comm1, q.Id, "close"));
            q = Ok(await Act(host, Comm1, q.Id, "answer", d => d.Answer = "ده بار"));
            Assert.AreEqual("N-1405/77", q.ClientLetterRef);
            Refused(await Act(host, Comm1, q.Id, "close"));
            EnClarificationDto next = Ok(await Act(host, Eng1, q.Id, "reask", d => d.Question = "دمای بخار ورودی چقدر است؟"));
            Assert.AreEqual(q.Id, next.FollowsId);
            Assert.AreEqual((int)EnClarStatus.WithCommercial, next.Status);
            Assert.AreEqual((int)EnClarStatus.Closed, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnClarificationService>().GetClarificationAsync(q.Id, Ct))).Status);
            Refused(await Act(host, Eng1, next.Id, "void", d => d.Note = "کوتاه"), "15");
            Ok(await Act(host, Eng1, next.Id, "void", d => d.Note = "پرسش تکراری بود و دیگر لازم نیست"));
        }

        private static Task<ResponseDto<EnClarificationDto>> Act(AppHost host, System.Security.Claims.ClaimsPrincipal who, int id, string action, Action<EnClarificationActionDto> set = null)
        {
            EnClarificationActionDto dto = new EnClarificationActionDto { Id = id, Action = action };
            set?.Invoke(dto);
            return host.As(who, p => p.GetRequiredService<IEnClarificationService>().ClarificationActionAsync(dto, Ct));
        }
    }
}
