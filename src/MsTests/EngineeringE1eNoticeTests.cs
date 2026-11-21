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
    public class EngineeringE1eNoticeTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task The_Notice_Shows_Only_While_The_Ceo_Approval_Is_Still_Ahead()
        {
            AppHost host = Started("e1e3");
            // spare parts never pass the CEO: no notice even with a code that has no list
            int spare = EngineeringPart3Tests.Seed(host, EnClassification.Platform, EnCaseStatus.Classifying);
            host.Write(u => { EnCase x = u.Set<EnCase>().Single(k => k.Id == spare); x.Kind = EnCaseKind.SpareParts; x.ReferenceCode = "E1E-SP"; u.SaveChanges(); }, Seeder);
            Assert.IsNull(Ok(await GetCase(host, Ceo, spare)).StandardNotice);
            // a turbine case before the CEO: present
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "مبنای آزمون برای طراحی جدید"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "E1E-AHEAD"));
            Assert.IsNotNull(Ok(await GetCase(host, Eng1, c.Id)).StandardNotice);
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Assert.IsNotNull(Ok(await GetCase(host, Ceo, c.Id)).StandardNotice);
            await ApprovedList(host, "E1E-AHEAD");
            Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            // after the CEO approved: null even when the code later has no approved list (legacy data)
            host.Write(u => { u.Set<EnCase>().Single(k => k.Id == c.Id).ReferenceCode = "E1E-NOLIST"; u.SaveChanges(); }, Seeder);
            Assert.IsNull(Ok(await GetCase(host, Ceo, c.Id)).StandardNotice);
        }
    }
}
