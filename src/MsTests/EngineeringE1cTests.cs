using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1c: fixes from stage 2 of the real click test.</summary>
    [TestClass]
    public class EngineeringE1cTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task C2_Question_Carries_Case_And_Contract_Also_For_Engineering_Without_Handover()
        {
            AppHost host = Started("e1cinfo");
            (int contract, int caseId) = await NewDesign(host);
            EnInfoRequestDto q = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnInfoRequestService>().AskAsync(new EnInfoAskDto { CaseId = caseId, Unit = (int)Entities.Model.NasbNiro.Engineering.EnUnit.Engineering, Question = "ضخامت ورق در نقشه مشخص نیست، لطفاً مشخص کنید" }, Ct)));
            EnInfoRequestDto listed = Ok(await host.As(Pmo1, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestsAsync(new EnInfoQuery { PageSize = 100 }, Ct))).Single(x => x.Id == q.Id);
            Assert.IsNull(listed.HandoverId);
            Assert.AreEqual(caseId, listed.CaseId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(listed.CaseNumber));
            Assert.AreEqual(contract, listed.ContractId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(listed.ContractCode));
            Assert.AreEqual("ساخت توربین", listed.ContractTitle);
        }
    }
}
