using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    [TestClass]
    public class EngineeringE1eDraftTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task The_Reference_Draft_Step_Drafts_Again_When_The_Only_List_Of_The_Code_Is_Void_And_Never_For_A_New_Design()
        {
            AppHost host = await EngineeringStandardsTests.TwoStandardsAsync("e1e5");
            int product = Product(host);
            { Guid ds = File(host, 2111); Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", StandardAttachmentId = ds, ReferenceCode = "API-611", StandardBasis = "دیتاشیت پروژهٔ اول، بند ۳" }, Ct))); }
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "API-611"));
            Ok(await Supply(host, Eng1, d.Lines.First(l => l.Required).Id, File(host, 2111)));
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", x => x.InquiryId = Inquiry(host)));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", x => { x.Classification = 1; x.ProductId = product; x.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "API-614"));
            ClaimsPrincipal eng = await SeatAsync(host);
            AgentWorkItem first = (await EngineeringStandardsTests.Discover(host, eng, "ENG.REF.DRAFT")).Single(i => i.TargetType == "EnCase" && i.TargetId == c.Id);
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, eng, "ENG.REF.DRAFT", first)).Outcome);
            Assert.AreEqual(0, (await EngineeringStandardsTests.Discover(host, eng, "ENG.REF.DRAFT")).Count(i => i.TargetType == "EnCase" && i.TargetId == c.Id), "a draft exists: nothing to draft");
            int draftId = host.Read(u => u.Set<EnReferenceList>().Single(l => l.Code == "API-614").Id);
            Ok(await RefAction(host, Mgr1, draftId, "void", "محتوای پیش‌نویس درست نبود و باید دوباره ساخته شود"));
            Assert.AreEqual(EnRefStatus.Void, host.Read(u => u.Set<EnReferenceList>().Single(l => l.Id == draftId).Status));
            AgentWorkItem again = (await EngineeringStandardsTests.Discover(host, eng, "ENG.REF.DRAFT")).Single(i => i.TargetType == "EnCase" && i.TargetId == c.Id);   // a Void-only code is treated as absent
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, eng, "ENG.REF.DRAFT", again)).Outcome);
            Assert.AreEqual(1, host.Read(u => u.Set<EnReferenceList>().Count(l => l.Code == "API-614" && l.Status == EnRefStatus.Draft)));
            // a new-design case under a code with no list: the agent does not draft (it waits for the employer)
            EnCaseDto nd = Ok(await CaseAct(host, Eng1, null, "open", x => x.InquiryId = Inquiry(host)));
            Ok(await CaseAct(host, Eng1, nd.Id, "propose", x => { x.Classification = 2; x.Basis = "مبنای آزمون برای طراحی جدید"; }));
            Ok(await SetStandard(host, Eng1, nd.Id, "E1E-NEWDESIGN"));
            Assert.AreEqual(0, (await EngineeringStandardsTests.Discover(host, eng, "ENG.REF.DRAFT")).Count(i => i.TargetType == "EnCase" && i.TargetId == nd.Id));
        }
    }
}
