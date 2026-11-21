using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1e: the CEO's approval of a case stays closed until the MDL of the case's standard is approved.</summary>
    [TestClass]
    public class EngineeringE1eTests
    {
        private const string Fragment = "فهرست مدارک مرجع (MDL) استاندارد";

        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task The_Ceo_Cannot_Approve_A_New_Design_Until_The_Mdl_Of_Its_Standard_Is_Approved()
        {
            AppHost host = Started("e1e1");
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "مبنای آزمون برای طراحی جدید"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "E1E-STD"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));   // the manager's confirmation is not blocked
            c = Ok(await GetCase(host, Ceo, c.Id));
            // no list at all
            Assert.IsFalse(c.CanApprove);
            StringAssert.Contains(c.ApproveBlockedReason, Fragment);
            StringAssert.Contains(c.ApproveBlockedReason, "E1E-STD");
            Assert.AreEqual("برای استاندارد «E1E-STD» فهرست MDL تاییدشده‌ای نیست؛ تا تایید آن، تایید مدیرعامل روی این پرونده بسته است.", c.StandardNotice);
            StringAssert.Contains(Refused(await CaseAct(host, Ceo, c.Id, "approve")), Fragment);
            // a draft list
            EnReferenceListDto list = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("E1E-STD"), Ct)));
            StringAssert.Contains(Refused(await CaseAct(host, Ceo, c.Id, "approve")), Fragment);
            Assert.IsNotNull(Ok(await GetCase(host, Ceo, c.Id)).StandardNotice);
            // under review
            Ok(await RefAction(host, Eng1, list.Id, "submit"));
            StringAssert.Contains(Refused(await CaseAct(host, Ceo, c.Id, "approve")), Fragment);
            c = Ok(await GetCase(host, Ceo, c.Id));
            Assert.IsFalse(c.CanApprove);
            // approved: the gate opens, nothing is approved by itself
            Ok(await RefAction(host, Mgr1, list.Id, "approve"));
            c = Ok(await GetCase(host, Ceo, c.Id));
            Assert.AreEqual((int)Entities.Model.NasbNiro.Engineering.EnCaseStatus.AwaitingCeoApproval, c.Status);
            Assert.IsTrue(c.CanApprove, c.ApproveBlockedReason);
            Assert.IsNull(c.StandardNotice);
            Assert.IsNull(c.ApproveBlockedReason);
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual((int)Entities.Model.NasbNiro.Engineering.EnCaseStatus.TenderPreparation, c.Status);
        }

        [TestMethod]
        public async Task The_Ceo_Cannot_Approve_A_Platform_Case_Under_A_Standard_Without_An_Approved_Mdl()
        {
            AppHost host = Started("e1e2");
            int product = Product(host);
            await ApprovedList(host);   // the platform's own standard
            await NativeDossier(host, product);
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "مبنای آزمون برای پلتفرم"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "E1E-OTHER"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            StringAssert.Contains(Refused(await CaseAct(host, Ceo, c.Id, "approve")), Fragment);
            EnReferenceListDto list = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("E1E-OTHER"), Ct)));
            Ok(await RefAction(host, Eng1, list.Id, "submit"));
            StringAssert.Contains(Refused(await CaseAct(host, Ceo, c.Id, "approve")), Fragment);
            Ok(await RefAction(host, Mgr1, list.Id, "approve"));
            await ConfirmGapIfAny(host, c.Id);
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual((int)Entities.Model.NasbNiro.Engineering.EnClassification.Platform, c.Classification);
        }
    }
}
