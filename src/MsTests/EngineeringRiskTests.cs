using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;
using static MsTests.EngineeringStandardsTests;

namespace MsTests
{
    /// <summary>ENG-E1 S2: the early warning. A case whose documents are at risk is a task for engineering; from Awarded on it is a permanent alarm in the CEO inbox that only the removal of the risk closes.</summary>
    [TestClass]
    public class EngineeringRiskTests
    {
        private static bool Open(AppHost host, string key) => host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == key && i.Status < OfInboxStatus.Done));

        private static async Task<(int Case, int Product)> AwardedPlatformAsync(AppHost host)
        {
            await ApprovedList(host);
            int product = Product(host);
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "توربین ساخته‌شده با همین مشخصات"; }));
            Ok(await SetStandard(host, Eng1, c.Id, Std));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual((int)EnCaseStatus.Awarded, c.Status);
            return (c.Id, product);
        }

        [TestMethod]
        public async Task An_Incomplete_Platform_Is_A_Task_At_Classification_Approval_And_A_Ceo_Alarm_From_Award_Until_The_Dossier_Is_Complete()
        {
            AppHost host = NewHost("risk1");
            StartControl(host);
            Assert.AreEqual(0, (await Discover(host, await SeatAsync(host), "ENG.RISK.WATCH")).Count, "nothing for legacy records or an empty system");
            // an inquiry case: at the CEO approval of the classification the task opens, but the CEO is not told before the award
            await ApprovedList(host);
            int product = Product(host);
            EnCaseDto inquiryCase = await ApprovedCase(host, Inquiry(host), true, product);
            var s = await SeatAsync(host);
            List<AgentWorkItem> watch = await Discover(host, s, "ENG.RISK.WATCH");
            Assert.AreEqual(1, watch.Count);
            Assert.AreEqual(inquiryCase.Id, watch[0].TargetId);
            Assert.AreEqual(0, (await Discover(host, s, "ENG.RISK.ESCALATE")).Count, "not Awarded yet: no CEO item");
            Assert.AreEqual(1, Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).CasesAtDocumentRisk);
            Assert.AreEqual(0, Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).AwardedCasesAtDocumentRisk);
        }

        [TestMethod]
        public async Task An_Awarded_Case_At_Risk_Is_A_Permanent_Ceo_Item_That_Only_A_Complete_Dossier_Closes()
        {
            AppHost host = NewHost("risk2");
            StartControl(host);
            var seat = await SeatAsync(host);
            (int caseId, int product) = await AwardedPlatformAsync(host);
            string key = "EN-RISK-" + caseId;
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.RISK.WATCH")).Count);
            AgentWorkItem esc = (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single();
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.RISK.ESCALATE", esc)).Outcome);
            Assert.IsTrue(Open(host, key), "the CEO inbox item is open");
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.RISK.ESCALATE")).Count, "idempotent");
            EnOverviewDto o = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.CasesAtDocumentRisk);
            Assert.AreEqual(1, o.AwardedCasesAtDocumentRisk);
            // the person reads it and marks it done: the alarm comes back while the risk exists
            host.Write(u => { foreach (OfInboxItem i in u.Set<OfInboxItem>().Where(x => x.SourceKey == key)) i.Status = OfInboxStatus.Done; u.SaveChanges(); });
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.RISK.ESCALATE")).Count);
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.RISK.ESCALATE", (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single())).Outcome);
            Assert.IsTrue(Open(host, key));
            // the dossier of the standard is completed and confirmed: the risk is gone, the task and the item close
            await CompletePlatform(host, product);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.RISK.WATCH")).Count);
            AgentWorkItem close = (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single();
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.RISK.ESCALATE", close)).Outcome);
            Assert.IsFalse(Open(host, key));
            Assert.AreEqual(0, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).AwardedCasesAtDocumentRisk);
        }

        [TestMethod]
        public async Task A_Standard_Without_An_Approved_List_Is_A_Risk_And_An_Overdue_Debt_Is_A_Ceo_Item_Until_Settled()
        {
            AppHost host = NewHost("risk3");
            StartControl(host);
            var seat = await SeatAsync(host);
            await ApprovedList(host);
            int contract = Contract(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "هیچ محصولی مطابق نیست"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "NO-LIST-STD"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            List<AgentWorkItem> watch = await Discover(host, seat, "ENG.RISK.WATCH");
            Assert.AreEqual(1, watch.Count);
            StringAssert.Contains(watch[0].Summary, "NO-LIST-STD");
            int debt = 0;
            host.Write(u => { var d = AppHost.Add(u, new EnDocumentDebt { WaiverId = 1, HandoverLineId = 1, CaseId = c.Id, ContractId = contract, DocumentType = "DWG", Title = "نقشه", DueAt = DateTimeOffset.Now.AddHours(-3), Status = EnDebtStatus.Open, CreateDate = DateTimeOffset.Now }); u.SaveChanges(); debt = d.Id; });
            List<AgentWorkItem> items = await Discover(host, seat, "ENG.RISK.ESCALATE");
            Assert.IsTrue(items.Any(i => i.TargetNumber == "EN-DEBT-" + debt));
            foreach (AgentWorkItem i in items) await Run(host, seat, "ENG.RISK.ESCALATE", i);
            Assert.IsTrue(Open(host, "EN-DEBT-" + debt));
            host.Write(u => { u.Set<EnDocumentDebt>().Single(d => d.Id == debt).Status = EnDebtStatus.Settled; u.SaveChanges(); });
            AgentWorkItem closing = (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single(i => i.TargetNumber == "EN-DEBT-" + debt);
            await Run(host, seat, "ENG.RISK.ESCALATE", closing);
            Assert.IsFalse(Open(host, "EN-DEBT-" + debt));
        }
    }
}
