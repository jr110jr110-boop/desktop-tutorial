using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using static MsTests.EnKit;
using static MsTests.EngineeringStandardsTests;

namespace MsTests
{
    /// <summary>ENG-E1g part 2: the five agent steps of the project MDL (discovery, gate info, seat action, registration).</summary>
    [TestClass]
    public class EngineeringProjectMdlStepsTests
    {
        private static readonly string[] Codes = { "ENG.PMDL.DRAFT", "ENG.PMDL.CHECK", "ENG.PMDL.APPROVE", "ENG.PMDL.PERMIT.DECIDE", "ENG.PMDL.CLIENT" };
        private const string PermitReason = "پروژهٔ خاص با مهلت کوتاه کارفرما؛ فهرست مدارک پروژه همزمان با اسناد مناقصه آماده نمی‌شود و مدیرعامل باید تصمیم بگیرد";

        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static async Task<EnCaseDto> Releasable(AppHost host)
        {
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), false);
            c = Ok(await CaseAct(host, await SeatAsync(host), c.Id, "draft-tender"));
            Ok(await host.As(Mgr2, p => p.GetRequiredService<Services.NasbNiro.ProjectControl.IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "فهرست اجزا", DocumentType = "CONCEPT" }, Ct)));
            return Ok(await CaseAct(host, Eng1, c.Id, "save-concept", d => d.ConceptNote = "اجزای اصلی تعریف شده و ابعاد پوسته فرض شده است"));
        }

        [TestMethod]
        public void The_Five_Project_Mdl_Steps_Are_Registered_With_The_Right_Kind_Station_Roles_And_The_72_Hour_Rule()
        {
            AppHost host = new AppHost("EnPmdlCat_" + Guid.NewGuid().ToString("N"));
            List<IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<IAgentStep>().Where(s => Codes.Contains(s.Code)).ToList())).GetAwaiter().GetResult();
            Assert.AreEqual(5, steps.Count);
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-ENG" && s.Station == 1 && s is IOfficeDeadlineStep o && o.OfficeDeadlineHours == Services.NasbNiro.Office.OfficeClock.StopLimitHours));
            Assert.AreEqual(AgentStepKind.Agent, steps.Single(s => s.Code == "ENG.PMDL.DRAFT").Kind);
            Assert.AreEqual(AgentStepKind.HumanGate, steps.Single(s => s.Code == "ENG.PMDL.CHECK").Kind);
            Assert.AreEqual(AgentStepKind.HumanGate, steps.Single(s => s.Code == "ENG.PMDL.APPROVE").Kind);
            Assert.AreEqual(AgentStepKind.HumanGate, steps.Single(s => s.Code == "ENG.PMDL.PERMIT.DECIDE").Kind);
            Assert.AreEqual(AgentStepKind.Input, steps.Single(s => s.Code == "ENG.PMDL.CLIENT").Kind);
            AgentWorkItem w = new AgentWorkItem { TargetType = "EnProjectMdl", TargetId = 1, TargetNumber = "PMDL-1" };
            Assert.AreEqual("ENGINEERING,ENGINEER", steps.Single(s => s.Code == "ENG.PMDL.CHECK").GateInfo(w).Roles);
            Assert.AreEqual("ENGINEERING", steps.Single(s => s.Code == "ENG.PMDL.APPROVE").GateInfo(w).Roles);
            Assert.AreEqual("CEO", steps.Single(s => s.Code == "ENG.PMDL.PERMIT.DECIDE").GateInfo(w).Roles);
            Assert.AreEqual("SALES,COMMERCIAL", steps.Single(s => s.Code == "ENG.PMDL.CLIENT").GateInfo(w).Roles);
        }

        [TestMethod]
        public async Task The_Steps_Follow_The_Project_Mdl_From_The_Draft_To_The_Client_And_Only_The_Seat_Drafts()
        {
            AppHost host = Started("pms1");
            EnCaseDto c = await Releasable(host);
            var seat = await SeatAsync(host);
            // nothing but the draft is open
            AgentWorkItem draft = (await Discover(host, seat, "ENG.PMDL.DRAFT")).Single();
            Assert.AreEqual(c.Id, draft.TargetId);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CHECK")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.APPROVE")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.PERMIT.DECIDE")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CLIENT")).Count);
            // the seat drafts: the draft step is empty, the check gate opens with the writers' roles
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.PMDL.DRAFT", draft)).Outcome);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count);
            AgentWorkItem check = (await Discover(host, seat, "ENG.PMDL.CHECK")).Single();
            Assert.AreEqual("ENGINEERING,ENGINEER", check.GateRoles);
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, c.Id));
            Assert.AreEqual(v.Mdl.Id, check.TargetId);
            // the second run of the same item is refused by the service (a live list exists)
            Assert.AreNotEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.PMDL.DRAFT", draft)).Outcome);
            // check -> approve gate (manager roles only)
            Ok(await MdlAct(host, Eng2, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CHECK")).Count);
            AgentWorkItem approve = (await Discover(host, seat, "ENG.PMDL.APPROVE")).Single();
            Assert.AreEqual("ENGINEERING", approve.GateRoles);
            v = Ok(await MdlView(host, Eng1, c.Id));
            Ok(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.APPROVE")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CLIENT")).Count, "the package is not released yet");
            Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            AgentWorkItem client = (await Discover(host, seat, "ENG.PMDL.CLIENT")).Single();
            Assert.AreEqual("SALES,COMMERCIAL", client.GateRoles);
            Assert.AreEqual(v.Mdl.Id, client.TargetId);
            Ok(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = v.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۳"; d.DocumentId = File(host, 2132); }));
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CLIENT")).Count, "recorded: nothing left");
        }

        [TestMethod]
        public async Task The_Permit_Gate_Opens_For_A_Requested_Permit_And_An_Approved_Permit_Stops_The_Draft()
        {
            AppHost host = Started("pms2");
            EnCaseDto c = await Releasable(host);
            var seat = await SeatAsync(host);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count);
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            AgentWorkItem gate = (await Discover(host, seat, "ENG.PMDL.PERMIT.DECIDE")).Single();
            Assert.AreEqual("CEO", gate.GateRoles);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count, "a requested permit does not stop the draft");
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Mgr1, c.Id));
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }));
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.PERMIT.DECIDE")).Count);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count, "an approved permit: no project list is drafted");
        }

        [TestMethod]
        public async Task A_Case_Without_An_Approved_Standard_List_Or_Of_Another_Kind_Gets_No_Draft_Task()
        {
            AppHost host = Started("pms3");
            var seat = await SeatAsync(host);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count, "no case at all");
            EnCaseDto c = await Releasable(host);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count);
            // a voided list lets the agent draft the next revision
            AgentWorkItem item = (await Discover(host, seat, "ENG.PMDL.DRAFT")).Single();
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.PMDL.DRAFT", item)).Outcome);
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Mgr1, c.Id));
            Ok(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "فهرست باید دوباره از فهرست استاندارد ساخته شود"; }));
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.PMDL.CHECK")).Count);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.PMDL.DRAFT")).Count);
        }
    }
}
