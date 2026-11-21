using System;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 fix round B1: tender documents added by a person, the document risk on the case, the flags of the case and the handover, the overview hours, the debt standard code, the deviation product filter.</summary>
    [TestClass]
    public class EngineeringFixB1Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static Task<Common.Results.ResponseDto<EnCaseDto>> Add(AppHost host, System.Security.Claims.ClaimsPrincipal who, EnCaseDto c, Guid? file, string title = "نقشهٔ مفهومی اولیه") =>
            CaseAct(host, who, c.Id, "add-tender-document", d => { d.DocumentId = file; d.Title = title; d.RowVersion = c.RowVersion; });

        [TestMethod]
        public async Task A_Person_Adds_An_Uploaded_File_To_The_Tender_Package_Through_The_Tender_Service_And_Cannot_Release_It()
        {
            AppHost host = Started("b1tender");
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            var seat = await SeatAsync(host);
            Assert.IsTrue(Ok(await GetCase(host, Eng1, c.Id)).CanAddTenderDocument);
            Assert.IsFalse(Ok(await GetCase(host, Sales1, c.Id)).CanAddTenderDocument);
            Assert.IsFalse(Ok(await GetCase(host, seat, c.Id)).CanAddTenderDocument);
            Refused(await Add(host, seat, c, File(host, 1)), "انسان");
            Refused(await Add(host, Sales1, c, File(host, 2131)));
            Refused(await Add(host, Eng1, c, null), "فایل مدرک");
            Refused(await Add(host, Eng1, c, File(host, 2111), "ab"), "عنوان");
            Refused(await Add(host, Eng1, c, Guid.NewGuid()), "پیدا نشد");
            Refused(await Add(host, Eng1, c, File(host, 2112)), "خود شما");               // somebody else's upload
            Refused(await Add(host, Eng1, c, File(host, 2111, ".exe")));                 // a type the documents module refuses
            Assert.IsNull(Ok(await GetCase(host, Eng1, c.Id)).TenderPackageId);          // nothing was created by the refusals
            Guid mine = File(host, 2111);
            c = Ok(await Add(host, Eng1, c, mine));
            Assert.IsNotNull(c.TenderPackageId);                                         // the package is created through the tender service
            Assert.AreEqual(1, c.TenderDocumentCount);
            Assert.AreEqual(1, c.TenderDocuments.Count);
            Assert.AreEqual(mine, c.TenderDocuments[0].DocumentId);
            Assert.AreEqual("نقشهٔ مفهومی اولیه", c.TenderDocuments[0].Title);
            Assert.IsFalse(c.TenderDocuments[0].AddedByAgent);
            Refused(await Add(host, Eng1, c, mine), "قبلاً");
            // the manager who added a document does not release the package (maker is not checker); the other manager does
            c = Ok(await Add(host, Mgr2, c, File(host, 2102), "فهرست اجزای طرح"));
            c = Ok(await CaseAct(host, Eng1, c.Id, "save-concept", d => d.ConceptNote = "اجزای اصلی تعریف شده و ابعاد پوسته فرض شده است"));
            Refused(await CaseAct(host, Mgr2, c.Id, "release-tender"), "شخص دیگری");
            await ApprovedMdl(host, c.Id);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.IsFalse(c.CanAddTenderDocument);
            Refused(await Add(host, Eng1, c, File(host, 2111)), "پس از ابلاغ");
            // a document that reaches the package after the release (here straight through the tender service) voids the release
            Ok(await host.As(Eng2, p => p.GetRequiredService<IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "نقشهٔ تکمیلی", DocumentType = "DWG" }, Ct)));
            Assert.IsTrue(Ok(await GetCase(host, Eng1, c.Id)).TenderStale);
            c = Ok(await CaseAct(host, Eng1, c.Id, "revalidate-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
        }

        [TestMethod]
        public async Task The_Document_Risk_Of_A_Case_Is_The_Early_Warning_Rule_And_The_List_Shows_It_For_The_Page()
        {
            AppHost host = Started("b1risk");
            await ApprovedList(host, "RISK-OK", ("DWG", true, false));
            EnCaseDto ok = await ApprovedCase(host, Inquiry(host, number: "INQ-B1-A"), false, code: "RISK-OK");
            EnCaseDto risky = await ApprovedCase(host, Inquiry(host, number: "INQ-B1-B"), false, code: "RISK-NONE");
            host.Write(u => { foreach (var l in u.Set<EnReferenceList>().Where(l => l.Code == "RISK-NONE").ToList()) l.Status = EnRefStatus.Draft; u.SaveChanges(); });   // approved only to pass the CEO gate; the risk is a standard whose list is not approved
            Assert.IsNull(Ok(await GetCase(host, Eng1, ok.Id)).DocumentRisk);
            EnCaseDto r = Ok(await GetCase(host, Eng1, risky.Id));
            StringAssert.Contains(r.DocumentRisk, "فهرست MDL تاییدشده");
            Assert.IsFalse(r.DocumentRiskAwarded);                                        // not awarded yet: the CEO is not told
            var page = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetCasesAsync(new EnCaseQuery(), Ct)));
            Assert.IsNull(page.Single(x => x.Id == ok.Id).DocumentRisk);
            Assert.AreEqual(r.DocumentRisk, page.Single(x => x.Id == risky.Id).DocumentRisk);
            host.Write(u => { u.Set<EnCase>().Single(x => x.Id == risky.Id).Status = EnCaseStatus.Awarded; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Ok(await GetCase(host, Eng1, risky.Id)).DocumentRiskAwarded);
        }

        [TestMethod]
        public async Task The_Flags_Of_The_Registration_The_Refresh_And_The_New_Question_Follow_The_Rules_The_Actions_Enforce()
        {
            AppHost host = Started("b1flags");
            int newCase = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            int product = Product(host, "99.77.00.000.3");
            EnCaseDto c = Ok(await GetCase(host, Mgr1, newCase));
            Assert.IsFalse(c.CanProposeRegistration);
            StringAssert.Contains(c.ProposeRegistrationBlockedReason, "ابلاغ");
            Assert.IsTrue(c.CanAskClarification);
            Assert.IsFalse(Ok(await GetCase(host, Sales1, newCase)).CanAskClarification);   // sales reads the case but writes no engineering question
            EngineeringPart3Tests.ReleaseHandover(host, newCase, product, "REG-B1");
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, newCase)).CanProposeRegistration);
            Assert.IsFalse(Ok(await GetCase(host, Eng1, newCase)).CanProposeRegistration);
            Assert.IsFalse(Ok(await GetCase(host, Ceo, newCase)).CanProposeRegistration);
            Assert.IsFalse(Ok(await GetCase(host, await SeatAsync(host), newCase)).CanProposeRegistration);
            Refused(await host.As(Mgr1, p => p.GetRequiredService<IEnRegistrationService>().ProposeAsync(new EnRegistrationProposeDto { CaseId = newCase, ProductId = product, ReferenceCode = "REG-B1", StandardBasis = "دیتاشیت ۱۲ بند ۴", Rationale = "این طرح برای چند پروژهٔ دیگر هم قابل استفاده است" }, Ct)), "فهرست MDL تاییدشده");   // M3: the standard is chosen from the approved lists
            await ApprovedList(host, "REG-B1", ("PID", true, true));
            var propose = await host.As(Mgr1, p => p.GetRequiredService<IEnRegistrationService>().ProposeAsync(new EnRegistrationProposeDto { CaseId = newCase, ProductId = product, ReferenceCode = "REG-B1", StandardBasis = "دیتاشیت ۱۲ بند ۴", Rationale = "این طرح برای چند پروژهٔ دیگر هم قابل استفاده است" }, Ct));
            Ok(propose);
            c = Ok(await GetCase(host, Mgr1, newCase));
            Assert.IsFalse(c.CanProposeRegistration);                                        // a proposal of the case already waits for the CEO
            StringAssert.Contains(c.ProposeRegistrationBlockedReason, "منتظر تصمیم");
            host.Write(u => { u.Set<EnCase>().Single(x => x.Id == newCase).Status = EnCaseStatus.Cancelled; u.SaveChanges(); }, Seeder);
            Assert.IsFalse(Ok(await GetCase(host, Eng1, newCase)).CanAskClarification);
        }

        [TestMethod]
        public async Task The_Handover_Refresh_Flag_Is_The_Predicate_Of_The_Action_And_The_Overview_Debt_And_Deviation_Additions_Work()
        {
            AppHost host = Started("b1misc");
            int caseId = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            int product = Product(host, "99.66.00.000.3"), other = Product(host, "99.66.00.000.4");
            int contract = host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).ContractId.Value);
            host.Write(u => { u.Set<EnCase>().Single(x => x.Id == caseId).ReferenceCode = "DEBT-STD"; u.SaveChanges(); }, Seeder);
            int handover = 0, waiver = 0;
            host.Write(u =>
            {
                EnHandover h = AppHost.Add(u, new EnHandover { Number = "EHO-B1", CaseId = caseId, ContractId = contract, Round = 1, Status = EnHandoverStatus.Draft, PreparedByUserId = 1001, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); handover = h.Id;
                EnWaiver w = AppHost.Add(u, new EnWaiver { Number = "EW-B1", HandoverId = h.Id, CaseId = caseId, Reason = "دلیل", Status = EnWaiverStatus.Approved, RequestedByUserId = 2101, RequestedAt = DateTimeOffset.Now.AddHours(-3), CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); waiver = w.Id;
                AppHost.Add(u, new EnDocumentDebt { WaiverId = w.Id, CaseId = caseId, ContractId = contract, DocumentType = "DWG", Title = "نقشه", DueAt = DateTimeOffset.Now.AddHours(10), Status = EnDebtStatus.Open, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                AppHost.Add(u, new EnDeviation { Number = "DEV-B1-1", Kind = EnDeviationKind.NotApplicable, TargetType = "EnPlatformDocument", TargetId = 1, CaseId = caseId, ProductId = product, StandardCode = "S", Requirement = "r", Instruction = "i", RequestedByUserId = 2111, RequestedAt = DateTimeOffset.Now.AddHours(-5), Status = EnDeviationStatus.Requested, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                AppHost.Add(u, new EnDeviation { Number = "DEV-B1-2", Kind = EnDeviationKind.NotApplicable, TargetType = "EnPlatformDocument", TargetId = 2, CaseId = caseId, ProductId = other, StandardCode = "S", Requirement = "r", Instruction = "i", RequestedByUserId = 2111, RequestedAt = DateTimeOffset.Now.AddHours(-2), Status = EnDeviationStatus.ManagerApproved, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                AppHost.Add(u, new EnPlatformRegistration { Number = "EPR-B1", CaseId = caseId, ProductId = product, ReferenceCode = "DEBT-STD", Status = EnRegStatus.Proposed, ProposedByUserId = 2101, ProposedAt = DateTimeOffset.Now.AddHours(-7), CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);
            EnHandoverDto dto = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(handover, Ct)));
            Assert.IsTrue(dto.CanRefresh);
            Assert.IsNull(dto.RefreshBlockedReason);
            Assert.IsFalse(Ok(await host.As(Pmo1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(handover, Ct))).CanRefresh);
            host.Write(u => { u.Set<EnHandover>().Single(x => x.Id == handover).Status = EnHandoverStatus.Submitted; u.SaveChanges(); }, Seeder);
            dto = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(handover, Ct)));
            Assert.IsFalse(dto.CanRefresh);
            StringAssert.Contains(dto.RefreshBlockedReason, "پیش‌نویس");
            Refused(await EngineeringHandoverTests.Act(host, Eng1, "refresh", d => { d.Id = handover; }), "پیش‌نویس");   // the action refuses with the same text

            var debts = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnHandoverService>().GetDebtsAsync(new EnDebtQuery(), Ct)));
            Assert.AreEqual("DEBT-STD", debts.Single().ReferenceCode);

            var all = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnDeviationService>().GetDeviationsAsync(new EnDeviationQuery(), Ct)));
            Assert.AreEqual(2, all.Count);
            var filtered = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnDeviationService>().GetDeviationsAsync(new EnDeviationQuery { ProductId = other }, Ct)));
            Assert.AreEqual("DEV-B1-2", filtered.Single().Number);

            EnOverviewDto o = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.DeviationsAwaitingManager);
            Assert.AreEqual(1, o.DeviationsAwaitingCeo);
            Assert.AreEqual(2, o.DeviationsAwaiting);
            // the waiting clocks are OFFICE hours (employer rule 14): compare with the office clock, not with calendar hours (one hour of tolerance for the time the test takes)
            DateTimeOffset nowB1 = DateTimeOffset.Now;
            Assert.AreEqual(Math.Floor(Services.NasbNiro.Office.OfficeClock.Between(nowB1.AddHours(-5), nowB1)), o.OldestDeviationWaitingHours, 1);
            Assert.AreEqual(Math.Floor(Services.NasbNiro.Office.OfficeClock.Between(nowB1.AddHours(-7), nowB1)), o.OldestRegistrationWaitingHours, 1);
        }
    }
}
