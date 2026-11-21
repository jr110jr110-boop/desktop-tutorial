using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1b: fixes from the real click test (button flags that follow the server, engineering asks the PMO, small texts).</summary>
    [TestClass]
    public class EngineeringE1bTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static void SetStatus(AppHost host, int handover, EnHandoverStatus status) =>
            host.Write(u => { u.Set<EnHandover>().First(x => x.Id == handover).Status = status; u.SaveChanges(); }, Seeder);

        private static async Task<EnHandoverDto> GetAs(AppHost host, ClaimsPrincipal who, int id) =>
            Ok(await host.As(who, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct)));

        // ------------------------------------------------------------------ B1

        [TestMethod]
        public async Task B1_The_Distribution_Flags_Follow_The_Role_The_Status_And_The_Rows_Exactly_As_The_Actions_Do()
        {
            AppHost host = Started("e1bflags");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            ClaimsPrincipal init = await SeatAsync(host, "AG-INIT", "PLANNING");

            // draft status: nobody may build rows yet, with the status reason
            foreach (ClaimsPrincipal who in new[] { Mgr1, Eng1, Pmo1, init })
            {
                EnHandoverDto v = await GetAs(host, who, h.Id);
                Assert.IsFalse(v.CanDraftDistribution); Assert.IsFalse(v.CanDistribute);
                Assert.IsFalse(string.IsNullOrWhiteSpace(v.DraftDistributionBlockedReason)); Assert.IsFalse(string.IsNullOrWhiteSpace(v.DistributeBlockedReason));
            }

            SetStatus(host, h.Id, EnHandoverStatus.Accepted);
            EnHandoverDto mgr = await GetAs(host, Mgr1, h.Id);
            Assert.IsFalse(mgr.CanDraftDistribution);
            StringAssert.Contains(mgr.DraftDistributionBlockedReason, "PMO");
            Assert.IsFalse((await GetAs(host, Eng1, h.Id)).CanDraftDistribution);
            Assert.IsFalse((await GetAs(host, await SeatAsync(host, "AG-ENG", "ENGINEER"), h.Id)).CanDraftDistribution);
            Assert.IsTrue((await GetAs(host, Pmo1, h.Id)).CanDraftDistribution);
            Assert.IsTrue((await GetAs(host, init, h.Id)).CanDraftDistribution);
            Assert.IsFalse((await GetAs(host, Pmo1, h.Id)).CanDistribute);   // not released yet
            // the action agrees with the flag: refused for the same people, with the same PMO reason
            Refused(await Act(host, Mgr1, "draft-distribution", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Eng1, "draft-distribution", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, await SeatAsync(host, "AG-ENG", "ENGINEER"), "draft-distribution", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = h.Id), "ابلاغ‌شده");

            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto afterDraft = await GetAs(host, Pmo1, h.Id);
            Assert.IsFalse(afterDraft.CanDraftDistribution);
            StringAssert.Contains(afterDraft.DraftDistributionBlockedReason, "قبلاً");

            SetStatus(host, h.Id, EnHandoverStatus.Released);
            Assert.IsFalse((await GetAs(host, Mgr1, h.Id)).CanDistribute);
            Assert.IsFalse((await GetAs(host, Eng1, h.Id)).CanDistribute);
            Assert.IsFalse((await GetAs(host, Ceo, h.Id)).CanDistribute);
            Assert.IsTrue((await GetAs(host, Pmo1, h.Id)).CanDistribute);
            Assert.IsTrue((await GetAs(host, init, h.Id)).CanDistribute);
            Refused(await Act(host, Ceo, "distribute", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Mgr1, "distribute", d => d.Id = h.Id), "PMO");

            Ok(await Act(host, Pmo1, "distribute", d => d.Id = h.Id));
            EnHandoverDto done = await GetAs(host, Pmo1, h.Id);
            Assert.IsFalse(done.CanDistribute);
            StringAssert.Contains(done.DistributeBlockedReason, "قبلاً انجام شده");
            Assert.IsFalse(done.CanDraftDistribution);
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = h.Id), "قبلاً انجام شده");   // the action agrees with the flag
        }

        [TestMethod]
        public async Task B1_A_Released_Handover_Without_Rows_Cannot_Be_Distributed_And_The_Flag_Says_So()
        {
            AppHost host = Started("e1bnorows");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            SetStatus(host, h.Id, EnHandoverStatus.Released);
            EnHandoverDto v = await GetAs(host, Pmo1, h.Id);
            Assert.IsFalse(v.CanDistribute);
            StringAssert.Contains(v.DistributeBlockedReason, "ساخته نشده");
            Refused(await Act(host, Pmo1, "distribute", d => d.Id = h.Id), "ساخته نشده");
        }
    
        // ------------------------------------------------------------------ B2

        private static Task<Common.Results.ResponseDto<EnInfoRequestDto>> Ask(AppHost host, ClaimsPrincipal who, EnUnit unit, int? caseId, int? handoverId = null, string question = "ضخامت ورق در نقشه مشخص نیست، لطفاً مشخص کنید") =>
            host.As(who, p => p.GetRequiredService<IEnInfoRequestService>().AskAsync(new EnInfoAskDto { CaseId = caseId, HandoverId = handoverId, Unit = (int)unit, Question = question }, Ct));

        private static Task<Common.Results.ResponseDto<EnInfoRequestDto>> InfoAct(AppHost host, ClaimsPrincipal who, int id, string action, string text = null) =>
            host.As(who, p => p.GetRequiredService<IEnInfoRequestService>().ActionAsync(new EnInfoActionDto { Id = id, Action = action, Answer = text }, Ct));

        private static async Task<int[]> ListIds(AppHost host, ClaimsPrincipal who) =>
            Ok(await host.As(who, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestsAsync(new EnInfoQuery { PageSize = 100 }, Ct))).Select(x => x.Id).ToArray();

        [TestMethod]
        public async Task B2_Engineering_Asks_The_Pmo_About_A_Contract_Case_And_About_A_Not_Yet_Released_Handover()
        {
            AppHost host = Started("e1bask");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));   // a draft: not released

            EnInfoRequestDto onCase = Ok(await Ask(host, Eng1, EnUnit.Engineering, caseId));
            Assert.AreEqual((int)EnUnit.Engineering, onCase.Unit);
            Assert.AreEqual("مهندسی", onCase.UnitText);
            Assert.AreEqual((int)EnInfoStatus.Open, onCase.Status);
            Assert.AreEqual(contract, onCase.ContractId);
            EnInfoRequestDto onHandover = Ok(await Ask(host, Mgr1, EnUnit.Engineering, null, h.Id));
            Assert.AreEqual(h.Id, onHandover.HandoverId);

            // the six units still ask only about a released handover (QA on the draft is refused)
            Refused(await Ask(host, Qa1, EnUnit.Qa, null, h.Id), "ابلاغ شده");
            // engineering cannot ask in the name of another unit, and another unit not in the name of engineering
            Refused(await Ask(host, Eng1, EnUnit.Qa, caseId), "همان واحد");
            Refused(await Ask(host, Qa1, EnUnit.Engineering, caseId), "همان واحد");
            // a seat never asks
            Refused(await Ask(host, await SeatAsync(host, "AG-ENG", "ENGINEER"), EnUnit.Engineering, caseId), EnText.AiNever);
            // a voided handover is not asked about
            SetStatus(host, h.Id, EnHandoverStatus.Void);
            Refused(await Ask(host, Eng1, EnUnit.Engineering, null, h.Id), "ابطال");
            // an unknown unit value is refused
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnInfoRequestService>().AskAsync(new EnInfoAskDto { CaseId = caseId, Unit = 8, Question = "پرسش با واحد ناشناخته برای آزمون" }, Ct)), EnText.InvalidData);
        }

        [TestMethod]
        public async Task B2_Engineering_Reads_Only_Its_Own_Questions_The_Pmo_Answers_The_Asker_Closes_And_Voids()
        {
            AppHost host = Started("e1bread");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            SetStatus(host, h.Id, EnHandoverStatus.Released);
            EnInfoRequestDto eng = Ok(await Ask(host, Eng1, EnUnit.Engineering, caseId));
            EnInfoRequestDto qa = Ok(await Ask(host, Qa1, EnUnit.Qa, null, h.Id));

            CollectionAssert.AreEquivalent(new[] { eng.Id }, await ListIds(host, Mgr1));
            CollectionAssert.AreEquivalent(new[] { eng.Id }, await ListIds(host, Eng2));
            CollectionAssert.AreEquivalent(new[] { qa.Id }, await ListIds(host, Qa1));
            CollectionAssert.AreEquivalent(new[] { eng.Id, qa.Id }, await ListIds(host, Pmo1));
            Refused(await host.As(Mgr1, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(qa.Id, Ct)));
            Refused(await host.As(Qa1, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(eng.Id, Ct)));
            Refused(await InfoAct(host, Mgr1, qa.Id, "close"));

            // the answer is the PMO's alone, never an engineer's or a seat's
            Refused(await InfoAct(host, Eng1, eng.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));
            Refused(await InfoAct(host, await SeatAsync(host, "AG-ENG", "PMO"), eng.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"), EnText.AiNever);
            Ok(await InfoAct(host, Pmo1, eng.Id, "answer", "ضخامت ورق ده میلی‌متر است و در نقشه اصلاح می‌شود"));
            // closing is the asking unit's: QA cannot close the engineering question, the PMO cannot either
            Refused(await InfoAct(host, Qa1, eng.Id, "close"));
            Refused(await InfoAct(host, Pmo1, eng.Id, "close"));
            Assert.AreEqual((int)EnInfoStatus.Closed, Ok(await InfoAct(host, Mgr1, eng.Id, "close")).Status);

            // the withdrawal is the asker's
            EnInfoRequestDto second = Ok(await Ask(host, Eng1, EnUnit.Engineering, caseId));
            Refused(await InfoAct(host, Eng2, second.Id, "void", "پرسش اشتباه ثبت شده بود"));
            Assert.AreEqual((int)EnInfoStatus.Void, Ok(await InfoAct(host, Eng1, second.Id, "void", "پرسش اشتباه ثبت شده بود")).Status);
        }

        [TestMethod]
        public async Task B2_Engineering_Is_Never_A_Distribution_Or_Document_Unit()
        {
            AppHost host = Started("e1bunits");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            SetStatus(host, h.Id, EnHandoverStatus.Accepted);
            EnHandoverDto drafted = Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Assert.AreEqual(6, drafted.Distribution.Count);
            Assert.IsFalse(drafted.Distribution.Any(d => d.Unit == (int)EnUnit.Engineering));
            Assert.IsFalse(host.Read(u => u.Set<EnDistribution>().Any(d => d.Unit == EnUnit.Engineering)));

            string message = null;
            Assert.IsNull(Services.NasbNiro.Engineering.EnUnits.Normalize("Qa,Engineering", out message));
            StringAssert.Contains(message, "مهندسی");
            Assert.AreEqual("Qc,Production", Services.NasbNiro.Engineering.EnUnits.Normalize("Production,Qc", out message));
            Assert.IsNull(message);
            Assert.AreEqual(6, Services.NasbNiro.Engineering.EnUnits.All.Length);
            Assert.AreEqual(7, Services.NasbNiro.Engineering.EnUnits.Askers.Length);
            Assert.IsNull(Services.NasbNiro.Engineering.EnUnits.Normalize("7", out message));   // the numeric form is no unit either
            Assert.IsNotNull(message);
            Assert.IsNull(Services.NasbNiro.Engineering.EnUnits.Normalize("Nope", out message));
            StringAssert.Contains(message, "تضمین کیفیت");   // Persian unit names in the message
            Assert.IsFalse(message.Contains("Engineering"), message);

            // the path a user hits: saving a reference list whose line names Engineering as a unit
            EnSaveReferenceDto save = Ref("VDDR-UNITS");
            save.Lines[0].Units = "Qa,Engineering";
            Refused(await host.As(Mgr1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(save, Ct)), "مهندسی");
        }

        [TestMethod]
        public void L1_An_Unknown_Unit_Value_Is_Never_Shown_As_Planning()
        {
            Assert.AreEqual("برنامه‌ریزی", EnText.Unit(EnUnit.Planning));
            Assert.AreEqual("نامشخص", EnText.Unit((EnUnit)0));
            Assert.AreEqual("نامشخص", EnText.Unit((EnUnit)8));
            foreach (EnUnit u in Services.NasbNiro.Engineering.EnUnits.Askers) Assert.AreNotEqual("نامشخص", EnText.Unit(u));
        }

        // ------------------------------------------------------------------ fix round: maker is not checker (H1), released-only through the case (M1)

        [TestMethod]
        public async Task H1_The_Asker_Never_Answers_His_Own_Question_And_The_Answerer_Never_Closes_It()
        {
            AppHost host = Started("e1bmaker");
            (int contract, int caseId) = await NewDesign(host);
            ClaimsPrincipal both = AppHost.Principal(2171, "eng.and.pmo", "ENGINEERING", "PMO");
            EnInfoRequestDto q = Ok(await Ask(host, both, EnUnit.Engineering, caseId));
            EnInfoRequestDto seen = Ok(await host.As(both, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(q.Id, Ct)));
            Assert.IsFalse(seen.CanAnswer);
            StringAssert.Contains(seen.AnswerBlockedReason, "پرسنده");
            Refused(await InfoAct(host, both, q.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"), EnText.SameUser);

            // another PMO answers; the asker (also engineering) may close, since he did not answer
            EnInfoRequestDto answered = Ok(await InfoAct(host, Pmo1, q.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));
            Assert.IsTrue(Ok(await host.As(both, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(q.Id, Ct))).CanClose);

            // the other way round: the one who answered is also in the asking unit, he cannot close
            EnInfoRequestDto q2 = Ok(await Ask(host, Eng1, EnUnit.Engineering, caseId));
            Ok(await InfoAct(host, both, q2.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));   // he did not ask this one
            EnInfoRequestDto v2 = Ok(await host.As(both, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(q2.Id, Ct)));
            Assert.IsFalse(v2.CanClose);
            StringAssert.Contains(v2.CloseBlockedReason, "پاسخ‌دهنده");
            Refused(await InfoAct(host, both, q2.Id, "close"), EnText.SameUser);
            Assert.AreEqual((int)EnInfoStatus.Closed, Ok(await InfoAct(host, Eng1, q2.Id, "close")).Status);
        }

        [TestMethod]
        public async Task M1_The_Six_Units_Ask_Only_About_A_Released_Handover_Also_Through_The_Case()
        {
            AppHost host = Started("e1bm1");
            (int contract, int caseId) = await NewDesign(host);
            Refused(await Ask(host, Qa1, EnUnit.Qa, caseId), "ابلاغ شده");   // a contract case, no released handover
            Ok(await Ask(host, Eng1, EnUnit.Engineering, caseId));           // engineering keeps "case with a contract"
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Refused(await Ask(host, Qa1, EnUnit.Qa, caseId), "ابلاغ شده");   // a draft is not released
            SetStatus(host, h.Id, EnHandoverStatus.Released);
            Assert.AreEqual((int)EnUnit.Qa, Ok(await Ask(host, Qa1, EnUnit.Qa, caseId)).Unit);
        }
    
        // ------------------------------------------------------------------ B3

        [TestMethod]
        public void B3_The_Waiver_Snapshot_Keys_Become_Lines_With_A_Type_And_A_Title()
        {
            var snap = new EnHandoverRules.Snapshot { Lines = new System.Collections.Generic.List<string> { "DWG|نقشه مونتاژ", "NOPIPE", "DOC|عنوان|با خط" } };
            var lines = EnHandoverRules.MissingLines(snap);
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("DWG", lines[0].DocType); Assert.AreEqual("نقشه مونتاژ", lines[0].Title);
            Assert.AreEqual(string.Empty, lines[1].DocType); Assert.AreEqual("NOPIPE", lines[1].Title);
            Assert.AreEqual("DOC", lines[2].DocType); Assert.AreEqual("عنوان|با خط", lines[2].Title);
            Assert.AreEqual(0, EnHandoverRules.MissingLines(EnHandoverRules.Parse(null)).Count);
        }

        [TestMethod]
        public async Task B3_The_Waiver_Dto_Carries_The_Parsed_Lines_And_Still_The_Raw_Keys()
        {
            AppHost host = Started("e1bwaiver");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));   // ENG-E1f: the permit is asked only after the PMO accepted
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            EnWaiverDto w = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = h.Id, DebtHours = 48, Reason = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده" }, Ct)));
            Assert.IsTrue(w.MissingLines.Count > 0);
            Assert.AreEqual(w.MissingSnapshot.Count, w.MissingLines.Count);
            for (int i = 0; i < w.MissingLines.Count; i++)
            {
                Assert.AreEqual(w.MissingSnapshot[i], w.MissingLines[i].DocType + "|" + w.MissingLines[i].Title);
                Assert.IsFalse(string.IsNullOrWhiteSpace(w.MissingLines[i].DocType));
                Assert.IsFalse(string.IsNullOrWhiteSpace(w.MissingLines[i].Title));
            }
        }

        [TestMethod]
        public void B3_The_Risk_Text_Says_What_Is_Left_And_Never_Zero_Missing_Documents()
        {
            var some = new EnRiskRow { Number = "EC-1", Missing = 3, DaysLeft = 10 };
            some.Reasons.Add("مدارک پلتفرم برای استاندارد X کامل و تایید نشده است");
            StringAssert.Contains(some.Summary(), "مدرک الزامی کم است");
            var none = new EnRiskRow { Number = "EC-1", Missing = 0, DaysLeft = 10 };
            none.Reasons.Add("مدارک الزامی پلتفرم برای استاندارد X موجود است و فقط تایید کامل‌بودن پرونده توسط مدیر مهندسی مانده است");
            string text = none.Summary();
            StringAssert.Contains(text, "فقط تایید کامل‌بودن");
            Assert.IsFalse(text.Contains("مدرک الزامی کم است"), text);
            Assert.IsFalse(text.Contains("۰ مدرک") || text.Contains("0 مدرک"), text);
        }

        [TestMethod]
        public async Task B3_A_Dossier_With_All_Required_Documents_But_No_Confirmation_Reads_As_A_Confirmation_Risk()
        {
            AppHost host = Started("e1brisk");
            await ApprovedList(host);
            int product = Product(host);
            await NativeDossier(host, product);
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, Std));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            var rows = await host.AsSystem(p => EnRisk.ComputeAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct, new[] { c.Id }));
            Assert.AreEqual(1, rows.Count);
            Assert.IsTrue(rows[0].Missing > 0);
            StringAssert.Contains(rows[0].Summary(), "مدرک الزامی کم است");

            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required && x.State == 0).ToList()) Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            Ok(await Dossier(host, Eng1, product, "complete"));   // all required documents are there; nobody has confirmed yet
            rows = await host.AsSystem(p => EnRisk.ComputeAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct, new[] { c.Id }));
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(0, rows[0].Missing);
            string text = rows[0].Summary();
            StringAssert.Contains(text, "فقط تایید کامل‌بودن");
            Assert.IsFalse(text.Contains("مدرک الزامی کم است"), text);
        }

        // ------------------------------------------------------------------ fix round: the dossier wording (M2, M3)

        [TestMethod]
        public void M2_The_Dossier_Wording_Is_Chosen_By_The_Real_State()
        {
            var noDossier = EnDossierWording.Of(EnDossierStatus.NotAssessed, 0);   // pure choice: NotAssessed
            Assert.AreEqual(EnDossierWord.NotAssessed, noDossier.Word);
            StringAssert.Contains(noDossier.Reason("API 611"), "ارزیابی نشده");
            Assert.AreEqual(EnDossierWord.IncompleteNoMissing, EnDossierWording.Of(EnDossierStatus.Incomplete, 0).Word);
            StringAssert.Contains(EnDossierWording.Of(EnDossierStatus.Incomplete, 0).Reason("X"), "کامل اعلام نشده");
            Assert.AreEqual(EnDossierWord.AwaitingConfirmation, EnDossierWording.Of(EnDossierStatus.Completing, 0).Word);
            StringAssert.Contains(EnDossierWording.Of(EnDossierStatus.Completing, 0).Reason("X"), "فقط تایید کامل‌بودن");
            Assert.AreEqual(EnDossierWord.StaleConfirmation, EnDossierWording.Of(EnDossierStatus.Complete, 0).Word);
            StringAssert.Contains(EnDossierWording.Of(EnDossierStatus.Complete, 0).Reason("X"), "دیگر معتبر نیست");
            EnDossierWording some = EnDossierWording.Of(EnDossierStatus.Incomplete, 4);
            Assert.AreEqual(EnDossierWord.MissingDocuments, some.Word);
            StringAssert.Contains(some.Warning("X"), "4 مدرک الزامی کم است");
            // the warning of the case page never counts zero missing documents
            foreach (EnDossierWording w in new[] { noDossier, EnDossierWording.Of(EnDossierStatus.Incomplete, 0), EnDossierWording.Of(EnDossierStatus.Completing, 0), EnDossierWording.Of(EnDossierStatus.Complete, 0) })
                Assert.IsFalse(w.Warning("X").Contains("مدرک الزامی کم است"), w.Warning("X"));
            StringAssert.Contains(EnDossierWording.CeoAlertHint, "تایید پرونده");
            Assert.IsFalse(EnDossierWording.CeoAlertHint.Contains("مدرک الزامی کم است"));
        }

        [TestMethod]
        public async Task M2_No_Dossier_And_A_Not_Assessed_Or_An_Incomplete_Dossier_Do_Not_Read_As_A_Confirmation_Risk()
        {
            AppHost host = Started("e1bm2");
            await ApprovedList(host);
            int product = Product(host);
            await NativeDossier(host, product);   // a dossier of the standard, not assessed yet
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, Std));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            var rows = await host.AsSystem(p => EnRisk.ComputeAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct, new[] { c.Id }));
            Assert.AreEqual(1, rows.Count);
            string text = rows[0].Summary();
            StringAssert.Contains(text, "ارزیابی نشده");   // not assessed
            Assert.IsFalse(text.Contains("فقط تایید کامل‌بودن") || text.Contains("مدرک الزامی کم است"), text);
            EnCaseDto page = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetCaseAsync(c.Id, Ct)));
            StringAssert.Contains(page.PlatformWarning, "ارزیابی نشده");
            Assert.IsFalse(page.PlatformWarning.Contains("مدرک الزامی کم است"), page.PlatformWarning);

            // assessed: documents are missing, the count is shown
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            page = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetCaseAsync(c.Id, Ct)));
            StringAssert.Contains(page.PlatformWarning, "مدرک الزامی کم است");

            // all documents there but «complete» not declared (a dossier that went back to Incomplete): the text says it must be declared
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required && x.State == 0).ToList()) Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            rows = await host.AsSystem(p => EnRisk.ComputeAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct, new[] { c.Id }));
            text = rows[0].Summary();
            StringAssert.Contains(text, "کامل اعلام نشده");
            Assert.IsFalse(text.Contains("فقط تایید کامل‌بودن") || text.Contains("مدرک الزامی کم است"), text);
        }
    }
}
