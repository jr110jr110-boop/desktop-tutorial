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
    public class EngineeringE1eCodeTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task A_List_Code_Is_Compared_Normalised_On_Both_Sides_In_All_Four_Places_And_New_Lists_Are_Stored_Upper_Case()
        {
            AppHost host = Started("e1e4");
            // a legacy lower-case row (stored before the normalisation) and a case under the upper-case code
            host.Write(u => { AppHost.Add(u, new EnReferenceList { Code = "e1e-low", Title = "قدیمی", Revision = 1, Status = EnRefStatus.Approved, PreparedByUserId = 1001, ApprovedAt = DateTimeOffset.Now, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "مبنای آزمون برای طراحی جدید"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "e1e-low"));   // stored as E1E-LOW
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Assert.AreEqual("E1E-LOW", Ok(await GetCase(host, Ceo, c.Id)).ReferenceCode);
            // 1 the DTO flag, 2 the notice, 3 the gate text of the agent step
            EnCaseDto shown = Ok(await GetCase(host, Ceo, c.Id));
            Assert.IsTrue(shown.CanApprove, shown.ApproveBlockedReason);
            Assert.IsNull(shown.StandardNotice);
            AgentWorkItem gate = (await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.CASE.APPROVE")).Single(i => i.TargetId == c.Id);
            Assert.IsFalse(gate.Summary.Contains("MDL"), gate.Summary);
            // 4 the approve action
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, Ok(await CaseAct(host, Ceo, c.Id, "approve")).Status);
            // a new list is stored normalised on the JSON path and on the CSV path
            Assert.AreEqual("API-611", Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("  api-611 "), Ct))).Code);
            byte[] csv = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("docType,title,discipline,required,tender,units\nDWG,نقشه,,1,1,\n")).ToArray();
            string csvCode = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "vddr-csv", Title = "فهرست از فایل" }, csv, Ct, "x.csv"))).Code;
            Assert.AreEqual("VDDR-CSV", csvCode);
        }
    }
}
