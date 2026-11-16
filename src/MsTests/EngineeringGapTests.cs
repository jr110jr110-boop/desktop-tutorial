using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 S4: the standard-gap report of a platform ordered under another standard.</summary>
    [TestClass]
    public class EngineeringGapTests
    {
        private static async Task<(AppHost Host, int Product, EnCaseDto Case)> ScenarioAsync(string name, string target)
        {
            AppHost host = await EngineeringStandardsTests.TwoStandardsAsync(name);
            int product = Product(host);
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = "API-611", StandardBasis = "دیتاشیت پروژهٔ اول، بند ۳" }, Ct)));
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "API-611"));
            Ok(await Supply(host, Eng1, d.Lines.Single(l => l.DocumentType == "PID").Id, File(host, 2111)));
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", x => x.InquiryId = Inquiry(host)));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", x => { x.Classification = 1; x.ProductId = product; x.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            Ok(await SetStandard(host, Eng1, c.Id, target));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            return (host, product, c);
        }

        [TestMethod]
        public async Task The_Gap_Is_Built_Once_With_Exact_Counts_And_The_Ceo_Approval_Waits_For_The_Managers_Confirmation()
        {
            var s = await ScenarioAsync("gap1", "API-612");
            AppHost host = s.Host;
            ClaimsPrincipal seat = await SeatAsync(host);
            Refused(await CaseAct(host, Ceo, s.Case.Id, "approve"), "گزارش تفاوت استاندارد");   // not even built yet
            var items = (await EngineeringStandardsTests.Discover(host, seat, "ENG.STANDARD.GAP")).Where(i => i.TargetId == s.Case.Id).ToList();
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, seat, "ENG.STANDARD.GAP", items[0])).Outcome);
            Assert.AreEqual(0, (await EngineeringStandardsTests.Discover(host, seat, "ENG.STANDARD.GAP")).Count(i => i.TargetId == s.Case.Id), "idempotent: one live report per case and revision pair");
            EnStandardGap g = host.Read(u => u.Set<EnStandardGap>().Single(x => x.CaseId == s.Case.Id));
            Assert.AreEqual("API-611", g.BaseCode); Assert.AreEqual("API-612", g.TargetCode);
            Assert.AreEqual(2, g.AddedCount); Assert.AreEqual(0, g.RemovedCount); Assert.AreEqual(0, g.RequiredChangedCount); Assert.AreEqual(1, g.ReusableCount);
            Assert.AreEqual(4, host.Read(u => u.Set<EnStandardGapLine>().Count(x => x.GapId == g.Id)));
            Assert.IsTrue(host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == "EN-GAP-" + g.Id && i.Status < OfInboxStatus.Done)), "a permanent alarm while it waits");
            Assert.AreEqual(1, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).StandardGapsOpen);
            EnCaseDto shown = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnCaseService>().GetCaseAsync(s.Case.Id, Ct)));
            Assert.AreEqual(g.Number, shown.StandardGap.Number);
            Refused(await CaseAct(host, Ceo, s.Case.Id, "approve"), "هنوز توسط مدیر مهندسی تایید نشده");
            EnGapDto dto = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnStandardGapService>().GetGapAsync(g.Id, Ct)));
            string note = "اثر بر مواد و طراحی بررسی شد؛ دو مدرک تازه لازم است.";
            Refused(await host.As(await SeatAsync(host, "AG-ENG", "ENGINEERING"), p => p.GetRequiredService<IEnStandardGapService>().ConfirmAsync(new EnGapConfirmDto { Id = g.Id, EngineeringImpactNote = note, RowVersion = dto.RowVersion }, Ct)), "هوش مصنوعی");
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnStandardGapService>().ConfirmAsync(new EnGapConfirmDto { Id = g.Id, EngineeringImpactNote = note, RowVersion = dto.RowVersion }, Ct)));
            Refused(await host.As(Mgr1, p => p.GetRequiredService<IEnStandardGapService>().ConfirmAsync(new EnGapConfirmDto { Id = g.Id, EngineeringImpactNote = "کوتاه", RowVersion = dto.RowVersion }, Ct)), "30");
            EnGapDto done = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnStandardGapService>().ConfirmAsync(new EnGapConfirmDto { Id = g.Id, EngineeringImpactNote = note, RowVersion = dto.RowVersion }, Ct)));
            Assert.AreEqual(1, done.Status);
            Assert.IsFalse(host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == "EN-GAP-" + g.Id && i.Status < OfInboxStatus.Done)));
            EnCaseDto approved = Ok(await CaseAct(host, Ceo, s.Case.Id, "approve"));
            Assert.AreEqual(host.Read(u => u.Set<EnStandardGap>().Single(x => x.Id == g.Id).ContentHash), host.Read(u => u.Set<EnCase>().Single(x => x.Id == approved.Id).ApprovedGapHash), "the approval binds to the gap hash");
        }

        [TestMethod]
        public async Task A_Target_Without_An_Approved_List_Gives_Zero_Lines_And_An_Unverified_Draft_Copy_That_Is_Never_Submitted()
        {
            var s = await ScenarioAsync("gap2", "API-613");
            AppHost host = s.Host;
            ClaimsPrincipal seat = await SeatAsync(host);
            AgentWorkItem item = (await EngineeringStandardsTests.Discover(host, seat, "ENG.STANDARD.GAP")).Single(i => i.TargetId == s.Case.Id);
            await EngineeringStandardsTests.Run(host, seat, "ENG.STANDARD.GAP", item);
            EnStandardGap g = host.Read(u => u.Set<EnStandardGap>().Single(x => x.CaseId == s.Case.Id));
            Assert.AreEqual(0, host.Read(u => u.Set<EnStandardGapLine>().Count(x => x.GapId == g.Id)));
            StringAssert.Contains(g.Summary, "هنوز تعریف نشده");
            EnReferenceList copy = host.Read(u => u.Set<EnReferenceList>().Single(l => l.Code == "API-613"));
            Assert.AreEqual(EnRefStatus.Draft, copy.Status, "never auto-submitted");
            StringAssert.Contains(copy.Note, "بررسی‌نشده");
            Assert.AreEqual(0, (await EngineeringStandardsTests.Discover(host, seat, "ENG.STANDARD.GAP")).Count(i => i.TargetId == s.Case.Id), "no second draft");
        }

        [TestMethod]
        public async Task A_Product_Without_Another_Standard_Needs_No_Gap_And_A_Plain_Case_Is_Not_Blocked()
        {
            AppHost host = await EngineeringStandardsTests.TwoStandardsAsync("gap3");
            int product = Product(host);
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), true, product, "API-611");
            Assert.AreEqual(0, host.Read(u => u.Set<EnStandardGap>().Count()));
            Assert.IsNotNull(c);
        }
    }
}
