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
            Refused(await Ask(host, await SeatAsync(host, "AG-ENG", "ENGINEER"), EnUnit.Engineering, caseId));
            // a voided handover is not asked about
            SetStatus(host, h.Id, EnHandoverStatus.Void);
            Refused(await Ask(host, Eng1, EnUnit.Engineering, null, h.Id), "ابطال");
            // an unknown unit value is refused
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnInfoRequestService>().AskAsync(new EnInfoAskDto { CaseId = caseId, Unit = 8, Question = "پرسش با واحد ناشناخته برای آزمون" }, Ct)));
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
            Refused(await InfoAct(host, await SeatAsync(host, "AG-ENG", "PMO"), eng.Id, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));
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
        }
    }
}
