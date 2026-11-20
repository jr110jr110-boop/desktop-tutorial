using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1: the standard of a project is set by the client (data sheet); nobody in the company chooses it and the agent never assumes it. A platform has one dossier per standard.</summary>
    [TestClass]
    public class EngineeringStandardsTests
    {
        internal static async Task<AppHost> TwoStandardsAsync(string name)
        {
            AppHost host = NewHost(name);
            StartControl(host);
            await ApprovedList(host, "API-611", ("PID", true, true), ("DATASHEET", true, false));
            await ApprovedList(host, "API-612", ("PID", true, true), ("DATASHEET", true, false), ("ROTORDYN", true, false), ("NOISE", false, false));
            return host;
        }

        internal static Task<List<AgentWorkItem>> Discover(AppHost host, ClaimsPrincipal seat, string code) => host.As(seat, async p =>
            await p.GetServices<IAgentStep>().Single(x => x.Code == code).DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), DateTime.Today, Ct));

        internal static Task<AgentStepResult> Run(AppHost host, ClaimsPrincipal seat, string code, AgentWorkItem item) => host.As(seat, async p =>
            await p.GetServices<IAgentStep>().Single(x => x.Code == code).RunAsync(item, p, Ct));

        private static Task<ResponseDto<EnDossierDto>> Native(AppHost host, ClaimsPrincipal who, int product, string code, string basis = "دیتاشیت پروژهٔ اول، بند ۳") =>
            NativeWith(host, who, product, code, basis, File(host, 2111));

        private static Task<ResponseDto<EnDossierDto>> NativeWith(AppHost host, ClaimsPrincipal who, int product, string code, string basis, Guid? file) =>
            host.As(who, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", StandardAttachmentId = file, ReferenceCode = code, StandardBasis = basis }, Ct));

        [TestMethod]
        public async Task A_Product_Without_A_Dossier_Gets_A_Native_Standard_Task_Only_People_Can_Close()
        {
            AppHost host = await TwoStandardsAsync("std1");
            int product = Product(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.PLATFORM.STANDARD")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PLATFORM.ASSESS")).Count, "nothing to assess: the standard of the platform is not known");
            EnPlatformListDto row = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetPlatformsAsync(new EnPlatformQuery(), Ct))).Single();
            Assert.IsTrue(row.NativeStandardMissing);
            Assert.AreEqual(0, row.Status, "NotAssessed");
            Refused(await Native(host, seat, product, "API-611"), "هوش مصنوعی");                        // an AI seat never records it
            Refused(await Native(host, Eng1, product, "API-611", "ab"), "5 نویسه");              // evidence required
            Refused(await Native(host, Nobody, product, "API-611"));
            EnDossierDto d = Ok(await Native(host, Eng1, product, " api-611 "));
            Assert.AreEqual("API-611", d.ReferenceCode, "trimmed and upper-cased");
            Refused(await Native(host, Eng1, product, "API-611"), "از پیش");
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PLATFORM.STANDARD")).Count);
            AgentWorkItem item = (await Discover(host, seat, "ENG.PLATFORM.ASSESS")).Single();
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.PLATFORM.ASSESS", item)).Outcome);
            d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, "API-611", Ct)));
            Assert.AreEqual(2, d.Lines.Count, "the lines of the Approved list of its own standard only");
            Refused(await Native(host, Eng1, product, "ANY-CODE-1", "دیتاشیت پروژهٔ دوم"), "فهرست MDL تاییدشده");   // chosen from the approved lists, not typed
            await ApprovedList(host, "ANY-CODE-1", ("PID", true, true));
            Ok(await Native(host, Eng1, product, "ANY-CODE-1", "دیتاشیت پروژهٔ دوم"));
            Assert.AreEqual(2, host.Read(u => u.Set<EnPlatformDossier>().Count(x => x.ProductId == product)));
        }

        [TestMethod]
        public async Task Two_Standards_Make_Two_Dossiers_And_Matching_Lines_Reuse_The_Files()
        {
            AppHost host = await TwoStandardsAsync("std2");
            int product = Product(host);
            Ok(await Native(host, Eng1, product, "API-611"));
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "API-611"));
            Guid file = File(host, 2111);
            Ok(await Supply(host, Eng1, d.Lines.Single(l => l.DocumentType == "PID").Id, file));
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, true, product, "API-612");
            Assert.AreEqual("API-612", c.ReferenceCode);
            Assert.AreEqual(2, host.Read(u => u.Set<EnPlatformDossier>().Count(x => x.ProductId == product)), "one dossier per standard");
            EnDossierDto d2 = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, "API-612", Ct)));
            Assert.AreEqual(4, d2.Lines.Count);
            Assert.AreEqual(1, d2.OtherDossiers.Count);
            EnDossierLineDto pid = d2.Lines.Single(l => l.DocumentType == "PID");
            Assert.AreEqual(1, pid.State, "reused: nothing asked twice");
            Assert.AreEqual(file, pid.DocumentId);
            Assert.AreEqual(3, d2.Lines.Count(l => l.State == 0), "the rest are Missing");
            Assert.AreEqual("دیتاشیت کارفرما، بند ۲٫۱", d2.StandardBasis, "the basis of the case is kept on the dossier");
            Assert.IsFalse(await host.As(Eng1, p => EnPlatformRules.IsCompleteAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), product, "API-612", Ct)));
            Refused(await Dossier(host, Eng1, product, "confirm", code: null), "استاندارد پرونده را مشخص کنید");
        }

        [TestMethod]
        public async Task The_Point_A_Warning_And_The_Lock_Use_The_Dossier_Of_The_Standard_Of_The_Case()
        {
            AppHost host = await TwoStandardsAsync("std3");
            int product = Product(host);
            Ok(await Native(host, Eng1, product, "API-611"));
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "API-611"));
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required)) Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            Ok(await Dossier(host, Eng1, product, "complete", code: "API-611"));
            Ok(await Dossier(host, Mgr1, product, "confirm", code: "API-611"));
            Assert.IsTrue(await host.As(Eng1, p => EnPlatformRules.IsCompleteAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), product, "API-611", Ct)));
            EnCaseDto c611 = await ApprovedCase(host, Inquiry(host), true, product, "API-611");
            Assert.IsFalse(c611.PlatformIncomplete, "complete under the standard of the case");
            EnCaseDto c612 = await ApprovedCase(host, Inquiry(host), true, product, "API-612");
            Assert.IsTrue(c612.PlatformIncomplete, "the dossier of another standard does not count");
            Assert.IsTrue(c612.PlatformMissingRequired > 0);
        }

        [TestMethod]
        public async Task A_Single_Approved_List_Is_Not_Assumed_And_The_Confirm_Is_Refused_Until_The_Client_Standard_Is_Recorded()
        {
            AppHost host = NewHost("std4");
            StartControl(host);
            await ApprovedList(host, "API-611", ("PID", true, true));
            ClaimsPrincipal seat = await SeatAsync(host);
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.CASE.STANDARD")).Count, "open while the standard is empty");
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm"), "استاندارد پروژه");
            Refused(await SetStandard(host, seat, c.Id, "API-611"), "هوش مصنوعی");
            Refused(await SetStandard(host, Nobody, c.Id, "API-611"));
            Refused(await SetStandard(host, Eng1, c.Id, "API-611", "ab"), "5 نویسه");
            Refused(await SetStandard(host, Eng1, c.Id, "", "دیتاشیت کارفرما، بند ۲"));
            c = Ok(await SetStandard(host, Sales1, c.Id, " x-2012 "));
            Assert.AreEqual("X-2012", c.ReferenceCode, "any code the client accepts, trimmed and upper-cased");
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.CASE.STANDARD")).Count);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingCeoApproval, c.Status);
        }

        [TestMethod]
        public async Task Changing_The_Standard_After_The_Confirmation_Is_A_New_Decision_Round_With_A_Reason()
        {
            AppHost host = NewHost("std5");
            StartControl(host);
            await ApprovedList(host);
            int inquiry = Inquiry(host);
            EnCaseDto c = await ApprovedCase(host, inquiry, false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Refused(await SetStandard(host, Eng1, c.Id, "API-612"), "دلیل تغییر استاندارد");
            c = Ok(await SetStandard(host, Eng1, c.Id, "API-612", "دیتاشیت کارفرما، بند ۲٫۲", "کارفرما استاندارد ۶۱۲ را الزامی کرد"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            Assert.IsNull(c.CeoApprovedByName, "the CEO approval is void");
            c = Ok(await CaseAct(host, Mgr2, c.Id, "confirm"));
            c = Ok(await CaseAct(host, Ceo, c.Id, "approve"));
            Assert.AreEqual("API-612", c.ReferenceCode);
        }
    }
}
