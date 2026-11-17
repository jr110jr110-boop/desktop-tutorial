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
    }
}
