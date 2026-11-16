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
    /// <summary>ENG-E1: the standard basis is both API 611 and API 612. Several approved Codes may exist; a human (the engineering manager) chooses the Code per platform and per new-design case, no rule decides.</summary>
    [TestClass]
    public class EngineeringStandardsTests
    {
        private static async Task<AppHost> TwoStandardsAsync(string name)
        {
            AppHost host = NewHost(name);
            StartControl(host);
            await ApprovedList(host, "API-611", ("PID", true, true), ("DATASHEET", true, false));
            await ApprovedList(host, "API-612", ("PID", true, true), ("DATASHEET", true, false), ("ROTORDYN", true, false), ("NOISE", false, false));
            return host;
        }

        private static Task<ResponseDto<EnDossierDto>> Act(AppHost host, ClaimsPrincipal who, int productId, string action, string code = null, string note = null) =>
            host.As(who, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId, Action = action, ReferenceCode = code, Note = note }, Ct));

        private static Task<List<AgentWorkItem>> Discover(AppHost host, ClaimsPrincipal seat, string code) => host.As(seat, async p =>
            await p.GetServices<IAgentStep>().Single(x => x.Code == code).DiscoverAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), DateTime.Today, Ct));

        private static Task<AgentStepResult> Run(AppHost host, ClaimsPrincipal seat, string code, AgentWorkItem item) => host.As(seat, async p =>
            await p.GetServices<IAgentStep>().Single(x => x.Code == code).RunAsync(item, p, Ct));

        [TestMethod]
        public async Task Two_Approved_Codes_Make_An_Assign_Task_And_The_Assess_Waits()
        {
            AppHost host = await TwoStandardsAsync("std1");
            int product = Product(host);
            ClaimsPrincipal seat = await SeatAsync(host);

            List<AgentWorkItem> assess = await Discover(host, seat, "ENG.PLATFORM.ASSESS");
            Assert.AreEqual(1, assess.Count);
            AgentStepResult run = await Run(host, seat, "ENG.PLATFORM.ASSESS", assess[0]);
            Assert.AreEqual(AgentStepOutcome.Done, run.Outcome);
            EnDossierDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, Ct)));
            Assert.AreEqual(0, d.Status, "the dossier waits, not assessed");
            Assert.AreEqual(0, d.Lines.Count, "no lines before the manager chooses the standard");
            Assert.IsNull(d.ReferenceCode);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PLATFORM.ASSESS")).Count, "the agent does not assess again; it waits for the choice");

            List<AgentWorkItem> assign = await Discover(host, seat, "ENG.PLATFORM.ASSIGN");
            Assert.AreEqual(1, assign.Count);
            IAgentStep step = host.AsSystem(p => Task.FromResult(p.GetServices<IAgentStep>().Single(x => x.Code == "ENG.PLATFORM.ASSIGN"))).GetAwaiter().GetResult();
            Assert.AreEqual(Entities.Model.NasbNiro.Agents.AgentStepKind.Input, step.Kind);

            Assert.IsFalse((await Act(host, Eng1, product, "assess")).Data.Lines.Any(), "an engineer's assess does not build lines either");
            Assert.IsFalse((await Act(host, Eng1, product, "assign", "API-611")).Success, "an engineer does not choose the standard");
            Assert.IsFalse((await Act(host, seat, product, "assign", "API-611")).Success, "a seat never chooses it");
            Assert.IsFalse((await Act(host, Mgr1, product, "assign", "API-999")).Success, "a Code without an approved list is refused");
            Assert.IsFalse((await Act(host, Mgr1, product, "assign", null)).Success);
        }

        [TestMethod]
        public async Task Assign_Builds_The_Lines_From_The_Chosen_List_Only()
        {
            AppHost host = await TwoStandardsAsync("std2");
            int product = Product(host);
            Ok(await Act(host, Eng1, product, "assess"));
            EnDossierDto d = Ok(await Act(host, Mgr1, product, "assign", "API-612"));
            Assert.AreEqual("API-612", d.ReferenceCode);
            Assert.AreEqual(0, d.Status);
            d = Ok(await Act(host, Eng1, product, "assess"));
            Assert.AreEqual(4, d.Lines.Count, "API-612 has four lines");
            Assert.AreEqual(3, d.Lines.Count(l => l.Required));
            Assert.AreEqual(1, d.Status, "incomplete until the engineers supply the documents");
            Assert.IsFalse(d.Stale);
            Assert.AreEqual(0, (await Discover(host, await SeatAsync(host), "ENG.PLATFORM.ASSIGN")).Count, "chosen: no assign task");
        }

        [TestMethod]
        public async Task Changing_The_Standard_Needs_A_Reason_And_Voids_The_Confirmation()
        {
            AppHost host = await TwoStandardsAsync("std3");
            int product = Product(host);
            Ok(await Act(host, Eng1, product, "assess"));
            Ok(await Act(host, Mgr2, product, "assign", "API-611"));
            EnDossierDto d = Ok(await Act(host, Eng1, product, "assess"));
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required && x.State == 0).ToList()) Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            Ok(await Act(host, Eng1, product, "complete"));
            d = Ok(await Act(host, Mgr1, product, "confirm"));
            Assert.AreEqual(3, d.Status, "confirmed complete");
            Assert.IsTrue(await host.As(Eng1, p => EnPlatformRules.IsCompleteAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), product, Ct)));

            Assert.IsFalse((await Act(host, Mgr1, product, "assign", "API-612")).Success, "a change without a reason is refused");
            Assert.IsFalse((await Act(host, Mgr1, product, "assign", "API-612", "کوتاه")).Success);
            d = Ok(await Act(host, Mgr1, product, "assign", "API-612", "کارفرما استاندارد ۶۱۲ را برای این پلتفرم الزامی کرد"));
            Assert.AreEqual("API-612", d.ReferenceCode);
            Assert.AreEqual(0, d.Status, "the confirmation is void; the agent re-builds the lines");
            Assert.IsFalse(await host.As(Eng1, p => EnPlatformRules.IsCompleteAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), product, Ct)));
            d = Ok(await Act(host, Eng1, product, "assess"));
            Assert.AreEqual(4, d.Lines.Count);
            Assert.AreEqual(1, d.Status);
        }

        [TestMethod]
        public async Task A_New_Design_Confirm_Is_Refused_Without_A_Reference_Code_When_Two_Codes_Are_Approved()
        {
            AppHost host = await TwoStandardsAsync("std4");
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 2; d.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            ResponseDto<EnCaseDto> r = await CaseAct(host, Mgr1, c.Id, "confirm");
            Assert.IsFalse(r.Success);
            StringAssert.Contains(r.Message, "استاندارد مرجع");
            StringAssert.Contains(r.Message, "API-611");
            Assert.IsFalse((await CaseAct(host, Mgr1, c.Id, "confirm", d => d.ReferenceCode = "API-999")).Success);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "confirm", d => d.ReferenceCode = "API-612"));
            Assert.AreEqual("API-612", c.ReferenceCode);
        }

        [TestMethod]
        public async Task A_Platform_Case_Needs_No_Code_And_A_Single_Approved_Code_Is_Used_And_Recorded()
        {
            AppHost host = NewHost("std5");
            StartControl(host);
            await ApprovedList(host, "API-611", ("PID", true, true));
            int product = Product(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            AgentWorkItem item = (await Discover(host, seat, "ENG.PLATFORM.ASSESS")).Single();
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.PLATFORM.ASSESS", item)).Outcome);
            EnDossierDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, Ct)));
            Assert.AreEqual("API-611", d.ReferenceCode, "the agent records the only approved Code");
            Assert.AreEqual(1, d.Lines.Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PLATFORM.ASSIGN")).Count);

            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", x => x.InquiryId = inquiry));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", x => { x.Classification = 2; x.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            c = Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            Assert.AreEqual("API-611", c.ReferenceCode, "one approved Code: recorded without a choice");
        }
    }
}
