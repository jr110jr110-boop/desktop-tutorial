using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// R1a, project control: a claim is notified only after two different people approved it (the PMO deputy and the design and manufacturing
    /// deputy, who is not the PMO deputy), an activity is started or held only by its unit, planning or the PMO, the evaluation of the payment
    /// milestones is not open to every signed-in user, and a guarantee is released through its own action (date, note and a second person) or expires by itself.
    /// R1a2 adds the cancellation as an event of its own (the same rules, two people), an expiry date that moves only through an extension with its evidence, and approvals of a claim that are tied to a quantified claim and to different people.
    /// </summary>
    [TestClass]
    public class ProjectControlR1aTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(12, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal PmoAndEngineer = AppHost.Principal(13, "pmo.and.engineering", "PMO", "ENGINEERING");
        private static readonly ClaimsPrincipal Engineering = AppHost.Principal(21, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(22, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Construction = AppHost.Principal(23, "construction.manager", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Planning = AppHost.Principal(24, "planning.manager", "PLANNING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(25, "qc.manager", "QC");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(26, "commercial.manager", "COMMERCIAL");
        private static readonly ClaimsPrincipal FinanceManager = AppHost.Principal(27, "finance.manager", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(28, "accountant", "FINANCIAL");

        private static readonly Lazy<AppHost> SharedHost = new Lazy<AppHost>(() => new AppHost("PcR1a_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => SharedHost.Value;

        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "تفکیک وظایف";

        private static Dictionary<string, JsonElement> Fields(string json)
        {
            Assert.IsNotNull(json, "the audit row has no values");
            using (JsonDocument doc = JsonDocument.Parse(json))
                return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        private static List<FnAuditLog> Modified(string entityType, int entityId) =>
            H.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == entityType && l.EntityId == entityId && l.Action == "Modified").OrderBy(l => l.Id).ToList());

        private static T Reload<T>(int id) where T : Entities.Model.BaseEntity => H.Read(u => u.Set<T>().Single(x => x.Id == id));

        // ------------------------------------------------------------------------------------------------ claims

        private static int NewClaim(PcClaimStatus status = PcClaimStatus.Quantified)
        {
            int id = 0;
            H.Write(u =>
            {
                var c = AppHost.Add(u, new PcClaim { ContractId = 1, Number = "CL-" + Guid.NewGuid().ToString("N").Substring(0, 6), Direction = PcClaimDirection.AgainstClient, Status = status, DaysQuantified = 5 });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<PcClaimDto>> ClaimAction(ClaimsPrincipal who, int id, string action) =>
            H.As(who, p => p.GetRequiredService<IPcBaselineService>().ClaimActionAsync(new PcClaimActionDto { Id = id, Action = action }, CancellationToken.None));

        [TestMethod]
        public async Task ThePmoDeputyAndTheDesignDeputyApproveAClaimInEitherOrder()
        {
            int pmoFirst = NewClaim();
            ResponseDto<PcClaimDto> a1 = await ClaimAction(Pmo1, pmoFirst, "approve-pmo");
            Assert.IsTrue(a1.Success, a1.Message);
            ResponseDto<PcClaimDto> a2 = await ClaimAction(Engineering, pmoFirst, "approve-design");
            Assert.IsTrue(a2.Success, a2.Message);
            PcClaim stored = Reload<PcClaim>(pmoFirst);
            Assert.IsTrue(stored.PmoApproved);
            Assert.IsTrue(stored.DesignManufacturingApproved);
            Assert.AreEqual(21, stored.ReviewedByUserId, "the reviewer id is the last approver");

            int designFirst = NewClaim();
            Assert.IsTrue((await ClaimAction(ProjectManager, designFirst, "approve-design")).Success, "the project manager is the other design and manufacturing deputy");
            Assert.IsTrue((await ClaimAction(Pmo2, designFirst, "approve-pmo")).Success);
            PcClaim other = Reload<PcClaim>(designFirst);
            Assert.IsTrue(other.PmoApproved && other.DesignManufacturingApproved);

            // the audit trail keeps both approvals with their own approver
            List<FnAuditLog> rows = Modified(nameof(PcClaim), pmoFirst);
            Assert.AreEqual(2, rows.Count, "one trail row per approval");
            Assert.AreEqual(11, rows[0].UserId);
            Assert.AreEqual("true", Fields(rows[0].AfterJson)["PmoApproved"].ToString().ToLowerInvariant());
            Assert.AreEqual(21, rows[1].UserId);
            Assert.AreEqual(11, Fields(rows[1].BeforeJson)["ReviewedByUserId"].GetInt32());
            Assert.AreEqual(21, Fields(rows[1].AfterJson)["ReviewedByUserId"].GetInt32());
        }

        [TestMethod]
        public async Task TheDesignApprovalIsNotForThePmoOrForConstructionAndThePmoApprovalIsNotForTheDesignSide()
        {
            int id = NewClaim();
            ResponseDto<PcClaimDto> pmoAsDesign = await ClaimAction(Pmo1, id, "approve-design");
            Assert.IsFalse(pmoAsDesign.Success);
            Assert.IsTrue(pmoAsDesign.Message.Contains(NotAllowed), pmoAsDesign.Message);
            ResponseDto<PcClaimDto> constructionAsDesign = await ClaimAction(Construction, id, "approve-design");
            Assert.IsFalse(constructionAsDesign.Success);
            Assert.IsTrue(constructionAsDesign.Message.Contains(NotAllowed), constructionAsDesign.Message);
            ResponseDto<PcClaimDto> engineeringAsPmo = await ClaimAction(Engineering, id, "approve-pmo");
            Assert.IsFalse(engineeringAsPmo.Success);
            Assert.IsTrue(engineeringAsPmo.Message.Contains(NotAllowed), engineeringAsPmo.Message);

            PcClaim stored = Reload<PcClaim>(id);
            Assert.IsFalse(stored.PmoApproved);
            Assert.IsFalse(stored.DesignManufacturingApproved);
            Assert.IsNull(stored.ReviewedByUserId);
        }

        [TestMethod]
        public async Task OnePersonWithBothRolesCannotGiveBothApprovals()
        {
            int pmoFirst = NewClaim();
            Assert.IsTrue((await ClaimAction(PmoAndEngineer, pmoFirst, "approve-pmo")).Success);
            ResponseDto<PcClaimDto> second = await ClaimAction(PmoAndEngineer, pmoFirst, "approve-design");
            Assert.IsFalse(second.Success);
            Assert.IsTrue(second.Message.Contains(SameUser), second.Message);
            Assert.IsFalse(Reload<PcClaim>(pmoFirst).DesignManufacturingApproved);

            int designFirst = NewClaim();
            Assert.IsTrue((await ClaimAction(PmoAndEngineer, designFirst, "approve-design")).Success);
            ResponseDto<PcClaimDto> other = await ClaimAction(PmoAndEngineer, designFirst, "approve-pmo");
            Assert.IsFalse(other.Success);
            Assert.IsTrue(other.Message.Contains(SameUser), other.Message);
            Assert.IsFalse(Reload<PcClaim>(designFirst).PmoApproved);

            // a second person completes either claim
            Assert.IsTrue((await ClaimAction(Engineering, pmoFirst, "approve-design")).Success);
            Assert.IsTrue((await ClaimAction(Pmo1, designFirst, "approve-pmo")).Success);
        }

        [TestMethod]
        public async Task ARepeatedApprovalIsRefusedAndTheFirstApproverStays()
        {
            int id = NewClaim();
            Assert.IsTrue((await ClaimAction(Pmo1, id, "approve-pmo")).Success);
            ResponseDto<PcClaimDto> again = await ClaimAction(Pmo2, id, "approve-pmo");
            Assert.IsFalse(again.Success, "a second PMO user cannot overwrite the first approval");
            Assert.AreEqual(11, Reload<PcClaim>(id).ReviewedByUserId);

            Assert.IsTrue((await ClaimAction(Engineering, id, "approve-design")).Success);
            ResponseDto<PcClaimDto> design = await ClaimAction(ProjectManager, id, "approve-design");
            Assert.IsFalse(design.Success);
            Assert.AreEqual(21, Reload<PcClaim>(id).ReviewedByUserId, "the design approver stays");
        }

        [TestMethod]
        public async Task AClaimIsNotifiedOnlyAfterBothApprovals()
        {
            int id = NewClaim(PcClaimStatus.Quantified);
            ResponseDto<PcClaimDto> none = await ClaimAction(Pmo1, id, "notify");
            Assert.IsFalse(none.Success);
            Assert.IsTrue(none.Message.Contains("تایید PMO"), none.Message);

            Assert.IsTrue((await ClaimAction(Pmo1, id, "approve-pmo")).Success);
            Assert.IsFalse((await ClaimAction(Pmo1, id, "notify")).Success, "the PMO approval alone is not enough");

            Assert.IsTrue((await ClaimAction(Engineering, id, "approve-design")).Success);
            ResponseDto<PcClaimDto> notified = await ClaimAction(Pmo1, id, "notify");
            Assert.IsTrue(notified.Success, notified.Message);
            Assert.AreEqual(PcClaimStatus.Notified, Reload<PcClaim>(id).Status);
        }

        // ------------------------------------------------------------------------------------------------ claims, R1a2 (H4)

        private static int NewContract()
        {
            int id = 0;
            H.Write(u => { var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract" }); u.SaveChanges(); id = c.Id; }, Seeder);
            return id;
        }

        private static Task<ResponseDto<PcClaimDto>> SaveClaim(ClaimsPrincipal who, PcClaimDto dto) =>
            H.As(who, p => p.GetRequiredService<IPcBaselineService>().SaveClaimAsync(dto, CancellationToken.None));

        private static Task<ResponseDto<PcClaimDto>> Quantify(ClaimsPrincipal who, int id, int days = 7, decimal amount = 1000m) =>
            H.As(who, p => p.GetRequiredService<IPcBaselineService>().ClaimActionAsync(new PcClaimActionDto { Id = id, Action = "quantify", Days = days, Amount = amount }, CancellationToken.None));

        /// <summary>A claim registered and quantified through the service, so that its creator and quantifier are recorded the way a real flow records them.</summary>
        private static async Task<int> RegisteredClaim(ClaimsPrincipal creator, ClaimsPrincipal quantifier)
        {
            int contract = NewContract();
            ResponseDto<PcClaimDto> saved = await SaveClaim(creator, new PcClaimDto { ContractId = contract, Direction = PcClaimDirection.AgainstClient, Issue = "late drawings", Basis = "clause 12" });
            Assert.IsTrue(saved.Success, saved.Message);
            ResponseDto<PcClaimDto> quantified = await Quantify(quantifier, saved.Data.Id.Value);
            Assert.IsTrue(quantified.Success, quantified.Message);
            return saved.Data.Id.Value;
        }

        [TestMethod]
        public async Task AClaimIsApprovedOnlyOnItsQuantifiedAmount()
        {
            foreach (PcClaimStatus status in new[] { PcClaimStatus.Issue, PcClaimStatus.Notified, PcClaimStatus.Negotiating, PcClaimStatus.Settled, PcClaimStatus.Withdrawn })
            {
                int id = NewClaim(status);
                ResponseDto<PcClaimDto> pmo = await ClaimAction(Pmo1, id, "approve-pmo");
                Assert.IsFalse(pmo.Success, status.ToString());
                Assert.IsTrue(pmo.Message.Contains("ابتدا ادعا برآورد شود"), pmo.Message);
                ResponseDto<PcClaimDto> design = await ClaimAction(Engineering, id, "approve-design");
                Assert.IsFalse(design.Success, status.ToString());
                Assert.IsTrue(design.Message.Contains("ابتدا ادعا برآورد شود"), design.Message);
                PcClaim stored = Reload<PcClaim>(id);
                Assert.IsFalse(stored.PmoApproved || stored.DesignManufacturingApproved, "nothing was approved in status " + status);
            }
        }

        [TestMethod]
        public async Task TheCreatorAndTheQuantifierOfAClaimCannotApproveIt()
        {
            // the PMO user who registered the claim cannot approve it as the PMO; another PMO user can
            int byPmo = await RegisteredClaim(Pmo1, Planning);
            ResponseDto<PcClaimDto> creator = await ClaimAction(Pmo1, byPmo, "approve-pmo");
            Assert.IsFalse(creator.Success);
            Assert.IsTrue(creator.Message.Contains(SameUser), creator.Message);
            Assert.IsTrue((await ClaimAction(Pmo2, byPmo, "approve-pmo")).Success);

            // the project manager who registered it cannot give the design approval
            int byPm = await RegisteredClaim(ProjectManager, Planning);
            ResponseDto<PcClaimDto> pm = await ClaimAction(ProjectManager, byPm, "approve-design");
            Assert.IsFalse(pm.Success);
            Assert.IsTrue(pm.Message.Contains(SameUser), pm.Message);
            Assert.IsTrue((await ClaimAction(Engineering, byPm, "approve-design")).Success);

            // the PMO user who quantified it cannot approve it, the project manager who quantified it cannot approve for the design side
            int quantifiedByPmo = await RegisteredClaim(Planning, Pmo1);
            Assert.AreEqual(11, Reload<PcClaim>(quantifiedByPmo).QuantifiedByUserId);
            Assert.IsFalse((await ClaimAction(Pmo1, quantifiedByPmo, "approve-pmo")).Success);
            Assert.IsTrue((await ClaimAction(Pmo2, quantifiedByPmo, "approve-pmo")).Success);
            int quantifiedByPm = await RegisteredClaim(Planning, ProjectManager);
            Assert.IsFalse((await ClaimAction(ProjectManager, quantifiedByPm, "approve-design")).Success);
            Assert.IsTrue((await ClaimAction(Engineering, quantifiedByPm, "approve-design")).Success);
            Assert.IsFalse(Reload<PcClaim>(quantifiedByPm).PmoApproved);
        }

        [TestMethod]
        public async Task EachApprovalKeepsItsApproverAndDateAndTheTrailShowsThem()
        {
            int id = await RegisteredClaim(Planning, Planning);
            Assert.IsTrue((await ClaimAction(Pmo1, id, "approve-pmo")).Success);
            Assert.IsTrue((await ClaimAction(ProjectManager, id, "approve-design")).Success);
            PcClaim stored = Reload<PcClaim>(id);
            Assert.AreEqual(11, stored.PmoApprovedByUserId);
            Assert.AreEqual(22, stored.DesignApprovedByUserId);
            Assert.AreEqual(24, stored.QuantifiedByUserId);
            Assert.AreEqual(24, stored.CreatorUserId);
            Assert.IsTrue(stored.PmoApprovedAt.HasValue && stored.DesignApprovedAt.HasValue);
            Assert.AreEqual(22, stored.ReviewedByUserId, "the reviewer stays the last approver");

            List<FnAuditLog> rows = Modified(nameof(PcClaim), id);
            Assert.AreEqual(11, Fields(rows.Single(r => r.AfterJson.Contains("PmoApprovedByUserId")).AfterJson)["PmoApprovedByUserId"].GetInt32());
            Assert.AreEqual(22, Fields(rows.Single(r => r.AfterJson.Contains("DesignApprovedByUserId")).AfterJson)["DesignApprovedByUserId"].GetInt32());
        }

        [TestMethod]
        public async Task ANewQuantificationOrAnEditStartsTheApprovalsAgainAndTheClientIsToldOnlyAfterTheNewOnes()
        {
            int id = await RegisteredClaim(Planning, Planning);
            Assert.IsTrue((await ClaimAction(Pmo1, id, "approve-pmo")).Success);
            Assert.IsTrue((await ClaimAction(Engineering, id, "approve-design")).Success);

            // the amount changes after both approvals: the approvals are gone, the claim cannot be notified
            ResponseDto<PcClaimDto> again = await Quantify(Commercial, id, 30, 9000m);
            Assert.IsTrue(again.Success, again.Message);
            PcClaim requantified = Reload<PcClaim>(id);
            Assert.IsFalse(requantified.PmoApproved || requantified.DesignManufacturingApproved);
            Assert.IsNull(requantified.PmoApprovedByUserId);
            Assert.IsNull(requantified.DesignApprovedByUserId);
            Assert.IsNull(requantified.ReviewedByUserId);
            Assert.IsNull(requantified.ReviewedAt);
            Assert.AreEqual(26, requantified.QuantifiedByUserId, "the new quantifier is remembered");
            Assert.AreEqual(9000m, requantified.AmountQuantified);
            ResponseDto<PcClaimDto> early = await ClaimAction(Planning, id, "notify");
            Assert.IsFalse(early.Success);
            Assert.IsTrue(early.Message.Contains("ابلاغ ادعا نیازمند تایید"), early.Message);

            // approvals given again, then an edit of the text takes one of them back
            Assert.IsTrue((await ClaimAction(Pmo1, id, "approve-pmo")).Success);
            ResponseDto<PcClaimDto> same = await SaveClaim(Planning, new PcClaimDto { Id = id, ContractId = requantified.ContractId, Direction = requantified.Direction, Issue = requantified.Issue, Basis = requantified.Basis });
            Assert.IsTrue(same.Success, same.Message);
            Assert.IsTrue(Reload<PcClaim>(id).PmoApproved, "a save that changes nothing keeps the approval");
            ResponseDto<PcClaimDto> edited = await SaveClaim(Planning, new PcClaimDto { Id = id, ContractId = requantified.ContractId, Direction = requantified.Direction, Issue = "a different issue", Basis = requantified.Basis });
            Assert.IsTrue(edited.Success, edited.Message);
            PcClaim afterEdit = Reload<PcClaim>(id);
            Assert.IsFalse(afterEdit.PmoApproved);
            Assert.IsNull(afterEdit.PmoApprovedByUserId);
            Assert.IsNull(afterEdit.ReviewedByUserId);
            Assert.AreEqual(PcClaimStatus.Quantified, afterEdit.Status);

            // two fresh approvals and the claim goes out; after that its text is fixed
            Assert.IsTrue((await ClaimAction(Pmo2, id, "approve-pmo")).Success);
            Assert.IsTrue((await ClaimAction(ProjectManager, id, "approve-design")).Success);
            Assert.IsTrue((await ClaimAction(Planning, id, "notify")).Success);
            ResponseDto<PcClaimDto> notified = await SaveClaim(Planning, new PcClaimDto { Id = id, ContractId = requantified.ContractId, Direction = requantified.Direction, Issue = "changed after it was sent", Basis = requantified.Basis });
            Assert.IsFalse(notified.Success);
            Assert.IsTrue(notified.Message.Contains("ادعای ابلاغ‌شده ویرایش نمی‌شود"), notified.Message);
            Assert.AreEqual("a different issue", Reload<PcClaim>(id).Issue);
        }

        // ------------------------------------------------------------------------------------------------ activities

        private static int NewActivity(string unit, PcActivityStatus status = PcActivityStatus.NotStarted)
        {
            int id = 0;
            H.Write(u =>
            {
                var a = AppHost.Add(u, new PcActivity { ContractId = 1, Code = "A-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "activity", ResponsibleUnit = unit, Status = status, DurationDays = 3 });
                u.SaveChanges();
                id = a.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<PcActivityDto>> ActivityAction(ClaimsPrincipal who, int id, string action, string reason = null) =>
            H.As(who, p => p.GetRequiredService<IPcScheduleService>().ActivityActionAsync(new PcActivityActionDto { Id = id, Action = action, Reason = reason, Date = new DateTime(2026, 3, 1) }, CancellationToken.None));

        [TestMethod]
        public async Task AnActivityIsStartedByItsUnitPlanningOrThePmoOnly()
        {
            int byOwner = NewActivity("ENGINEERING");
            ResponseDto<PcActivityDto> other = await ActivityAction(Construction, byOwner, "start");
            Assert.IsFalse(other.Success, "another unit manager does not start it");
            Assert.IsTrue(other.Message.Contains(NotAllowed), other.Message);
            Assert.AreEqual(PcActivityStatus.NotStarted, Reload<PcActivity>(byOwner).Status);

            ResponseDto<PcActivityDto> owner = await ActivityAction(Engineering, byOwner, "start");
            Assert.IsTrue(owner.Success, owner.Message);
            PcActivity started = Reload<PcActivity>(byOwner);
            Assert.AreEqual(PcActivityStatus.InProgress, started.Status);
            Assert.AreEqual(new DateTime(2026, 3, 1), started.ActualStart);

            int byPlanning = NewActivity("ENGINEERING");
            Assert.IsTrue((await ActivityAction(Planning, byPlanning, "start")).Success, "planning starts the activity of any unit");
            int byPmo = NewActivity("CONSTRUCTION");
            Assert.IsTrue((await ActivityAction(Pmo1, byPmo, "start")).Success, "the PMO starts the activity of any unit");
        }

        [TestMethod]
        public async Task HoldAndResumeFollowTheSameRule()
        {
            int id = NewActivity("CONSTRUCTION", PcActivityStatus.InProgress);
            ResponseDto<PcActivityDto> foreignHold = await ActivityAction(Engineering, id, "hold", "waiting for drawings");
            Assert.IsFalse(foreignHold.Success);
            Assert.IsTrue(foreignHold.Message.Contains(NotAllowed), foreignHold.Message);
            Assert.AreEqual(PcActivityStatus.InProgress, Reload<PcActivity>(id).Status);

            Assert.IsTrue((await ActivityAction(Construction, id, "hold", "waiting for drawings")).Success, "its own unit holds it");
            PcActivity held = Reload<PcActivity>(id);
            Assert.AreEqual(PcActivityStatus.OnHold, held.Status);
            Assert.AreEqual("waiting for drawings", held.OnHoldReason);

            ResponseDto<PcActivityDto> foreignResume = await ActivityAction(Engineering, id, "resume");
            Assert.IsFalse(foreignResume.Success);
            Assert.IsTrue((await ActivityAction(Planning, id, "resume")).Success, "planning resumes any activity");
            Assert.AreEqual(PcActivityStatus.NotStarted, Reload<PcActivity>(id).Status, "no actual start yet: back to not started");

            // an activity without a responsible unit belongs to planning and the PMO only
            int noUnit = NewActivity(null);
            Assert.IsFalse((await ActivityAction(Construction, noUnit, "start")).Success);
            Assert.IsTrue((await ActivityAction(Pmo2, noUnit, "start")).Success);
        }

        [TestMethod]
        public async Task AStartedActivityLeavesATrailRowWithTheUser()
        {
            int id = NewActivity("QC");
            Assert.IsTrue((await ActivityAction(Qc, id, "start")).Success);
            List<FnAuditLog> rows = Modified(nameof(PcActivity), id);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(25, rows[0].UserId);
            Assert.AreEqual("NotStarted", Fields(rows[0].BeforeJson)["Status"].GetString());
            Assert.AreEqual("InProgress", Fields(rows[0].AfterJson)["Status"].GetString());
        }

        private const string UnitChangeText = "تعیین یا تغییر واحد مسئول با برنامه‌ریزی یا PMO است";

        private static Task<ResponseDto<PcActivityDto>> SaveActivity(ClaimsPrincipal who, PcActivityDto dto) =>
            H.As(who, p => p.GetRequiredService<IPcScheduleService>().SaveActivityAsync(dto, CancellationToken.None));

        private static Task<ResponseDto<PcWbsNodeDto>> SaveWbsNode(ClaimsPrincipal who, PcWbsNodeDto dto) =>
            H.As(who, p => p.GetRequiredService<IPcScheduleService>().SaveWbsNodeAsync(dto, CancellationToken.None));

        [TestMethod]
        public async Task OnlyPlanningAndThePmoSetTheResponsibleUnitOfAnActivity()
        {
            int contract = NewContract();

            // a project manager creates an activity without a unit (planning gives it one later) but cannot create one that is already his own
            ResponseDto<PcActivityDto> own = await SaveActivity(ProjectManager, new PcActivityDto { ContractId = contract, Title = "own", ResponsibleUnit = "PROJECTMANAGER", DurationDays = 2 });
            Assert.IsFalse(own.Success, "a project manager does not create an activity for his own role");
            Assert.IsTrue(own.Message.Contains(NotAllowed) && own.Message.Contains(UnitChangeText), own.Message);
            ResponseDto<PcActivityDto> other = await SaveActivity(ProjectManager, new PcActivityDto { ContractId = contract, Title = "other", ResponsibleUnit = "CONSTRUCTION", DurationDays = 2 });
            Assert.IsFalse(other.Success, "nor one for another unit");
            Assert.AreEqual(0, H.Read(u => u.Set<PcActivity>().Count(x => x.ContractId == contract)), "nothing was saved by the refused calls");

            ResponseDto<PcActivityDto> blank = await SaveActivity(ProjectManager, new PcActivityDto { ContractId = contract, Title = "no unit yet", ResponsibleUnit = "  ", DurationDays = 2 });
            Assert.IsTrue(blank.Success, blank.Message);
            int id = blank.Data.Id.Value;
            Assert.IsNull(Reload<PcActivity>(id).ResponsibleUnit);

            // the project manager cannot give it to himself afterwards, and so cannot start it
            ResponseDto<PcActivityDto> claim = await SaveActivity(ProjectManager, new PcActivityDto { Id = id, ContractId = contract, Title = "no unit yet", ResponsibleUnit = "projectmanager", DurationDays = 2 });
            Assert.IsFalse(claim.Success, "the unit is not set by a project manager, whatever the case of the letters");
            Assert.IsTrue(claim.Message.Contains(UnitChangeText), claim.Message);
            Assert.IsNull(Reload<PcActivity>(id).ResponsibleUnit);
            ResponseDto<PcActivityDto> start = await ActivityAction(ProjectManager, id, "start");
            Assert.IsFalse(start.Success, "an activity nobody owns is started by planning and the PMO only");
            Assert.IsTrue(start.Message.Contains(NotAllowed), start.Message);

            // planning gives it to the project manager: now he starts it, which is planning's decision
            ResponseDto<PcActivityDto> given = await SaveActivity(Planning, new PcActivityDto { Id = id, ContractId = contract, Title = "no unit yet", ResponsibleUnit = " projectmanager ", DurationDays = 2 });
            Assert.IsTrue(given.Success, given.Message);
            Assert.AreEqual("PROJECTMANAGER", Reload<PcActivity>(id).ResponsibleUnit);
            Assert.IsTrue((await ActivityAction(ProjectManager, id, "start")).Success);

            // the project manager still edits everything else; the unit as it is stored (any case, padded) is not a change
            ResponseDto<PcActivityDto> same = await SaveActivity(ProjectManager, new PcActivityDto { Id = id, ContractId = contract, Title = "renamed by the project manager", ResponsibleUnit = "ProjectManager", DurationDays = 5 });
            Assert.IsTrue(same.Success, same.Message);
            PcActivity renamed = Reload<PcActivity>(id);
            Assert.AreEqual("renamed by the project manager", renamed.Title);
            Assert.AreEqual(5, renamed.DurationDays);
            Assert.AreEqual("PROJECTMANAGER", renamed.ResponsibleUnit);

            // ... but neither takes the unit away nor moves it to another one
            ResponseDto<PcActivityDto> cleared = await SaveActivity(ProjectManager, new PcActivityDto { Id = id, ContractId = contract, Title = renamed.Title, ResponsibleUnit = null, DurationDays = 5 });
            Assert.IsFalse(cleared.Success);
            ResponseDto<PcActivityDto> moved = await SaveActivity(ProjectManager, new PcActivityDto { Id = id, ContractId = contract, Title = renamed.Title, ResponsibleUnit = "QC", DurationDays = 5 });
            Assert.IsFalse(moved.Success);
            Assert.AreEqual("PROJECTMANAGER", Reload<PcActivity>(id).ResponsibleUnit);

            // planning and the PMO move it
            Assert.IsTrue((await SaveActivity(Pmo1, new PcActivityDto { Id = id, ContractId = contract, Title = renamed.Title, ResponsibleUnit = "qc", DurationDays = 5 })).Success);
            Assert.AreEqual("QC", Reload<PcActivity>(id).ResponsibleUnit);
            Assert.IsTrue((await SaveActivity(Planning, new PcActivityDto { ContractId = contract, Title = "planned", ResponsibleUnit = "ENGINEERING", DurationDays = 1 })).Success);

            // the change of the unit is on the trail with the person who made it
            List<FnAuditLog> rows = Modified(nameof(PcActivity), id);
            Assert.IsTrue(rows.Any(r => r.UserId == 24 && Fields(r.AfterJson).TryGetValue("ResponsibleUnit", out JsonElement unit) && unit.GetString() == "PROJECTMANAGER"), "planning's assignment is audited");
            Assert.IsTrue(rows.Any(r => r.UserId == 11 && Fields(r.AfterJson).TryGetValue("ResponsibleUnit", out JsonElement unit) && unit.GetString() == "QC"), "the PMO's move is audited");
        }

        [TestMethod]
        public async Task OnlyPlanningAndThePmoSetTheResponsibleUnitOfAWbsNode()
        {
            int contract = NewContract();
            ResponseDto<PcWbsNodeDto> refused = await SaveWbsNode(ProjectManager, new PcWbsNodeDto { ContractId = contract, Code = "1", Title = "root", NodeType = PcWbsNodeType.Project, ResponsibleUnit = "PROJECTMANAGER" });
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains(NotAllowed) && refused.Message.Contains(UnitChangeText), refused.Message);
            Assert.AreEqual(0, H.Read(u => u.Set<PcWbsNode>().Count(x => x.ContractId == contract)));

            ResponseDto<PcWbsNodeDto> created = await SaveWbsNode(ProjectManager, new PcWbsNodeDto { ContractId = contract, Code = "1", Title = "root", NodeType = PcWbsNodeType.Project });
            Assert.IsTrue(created.Success, created.Message);
            int id = created.Data.Id.Value;
            ResponseDto<PcWbsNodeDto> claim = await SaveWbsNode(ProjectManager, new PcWbsNodeDto { Id = id, ContractId = contract, Code = "1", Title = "root", NodeType = PcWbsNodeType.Project, ResponsibleUnit = "CONSTRUCTION" });
            Assert.IsFalse(claim.Success);
            Assert.IsNull(Reload<PcWbsNode>(id).ResponsibleUnit);

            Assert.IsTrue((await SaveWbsNode(Planning, new PcWbsNodeDto { Id = id, ContractId = contract, Code = "1", Title = "root", NodeType = PcWbsNodeType.Project, ResponsibleUnit = "construction" })).Success);
            Assert.AreEqual("CONSTRUCTION", Reload<PcWbsNode>(id).ResponsibleUnit);
            ResponseDto<PcWbsNodeDto> renamed = await SaveWbsNode(ProjectManager, new PcWbsNodeDto { Id = id, ContractId = contract, Code = "1", Title = "root renamed", NodeType = PcWbsNodeType.Project, ResponsibleUnit = "Construction" });
            Assert.IsTrue(renamed.Success, renamed.Message);
            Assert.AreEqual("root renamed", Reload<PcWbsNode>(id).Title);
            Assert.AreEqual("CONSTRUCTION", Reload<PcWbsNode>(id).ResponsibleUnit);
        }

        // ------------------------------------------------------------------------------------------------ guarantees

        private static int _guaranteeSource = 9000;

        private static int NewGuarantee(PcGuaranteeStatus status, DateTime? expiry = null, int? contractId = 1)
        {
            int id = 0;
            int source = Interlocked.Increment(ref _guaranteeSource);
            H.Write(u =>
            {
                var g = AppHost.Add(u, new PcGuaranteeInfo
                {
                    SourceKind = PcGuaranteeSource.ContractGuaranty, SourceId = source, ContractId = contractId, GuaranteeType = PcGuaranteeType.Performance,
                    IssuerKind = PcGuaranteeIssuer.Bank, ReleaseTrigger = PcGuaranteeRelease.FinalDelivery, Status = status, ExpiryDate = expiry, CurrencyCode = "IRR", AmountDecimal = 1000m,
                });
                u.SaveChanges();
                id = g.Id;
            }, Seeder);
            return id;
        }

        private static PcGuaranteeDto SaveDto(int id, PcGuaranteeStatus status, DateTime? expiry = null) => new PcGuaranteeDto
        {
            Id = id, GuaranteeType = PcGuaranteeType.Performance, IssuerKind = PcGuaranteeIssuer.Bank, ReleaseTrigger = PcGuaranteeRelease.FinalDelivery, Status = status, CurrencyCode = "IRR", AmountDecimal = 1000m, ExpiryDate = expiry,
        };

        private static Task<ResponseDto<PcGuaranteeDto>> Save(ClaimsPrincipal who, PcGuaranteeDto dto) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().SaveGuaranteeAsync(dto, CancellationToken.None));

        private static Task<ResponseDto<PcGuaranteeDto>> Release(ClaimsPrincipal who, int id, DateTime date, string note) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().ReleaseGuaranteeAsync(new PcGuaranteeReleaseDto { GuaranteeId = id, ReleaseDate = date, Note = note }, CancellationToken.None));

        private static Task<ResponseDto<PcGuaranteeDto>> Event(ClaimsPrincipal who, int id, PcGuaranteeEventKind kind, DateTime? newExpiry = null, string note = null, Guid? documentId = null, DateTime? date = null) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().AddGuaranteeEventAsync(
                new PcGuaranteeEventSaveDto { GuaranteeId = id, Kind = kind, EventDate = date ?? DateTime.Today, NewExpiryDate = newExpiry, Note = note, DocumentId = documentId }, CancellationToken.None));

        /// <summary>R1a2 (M2): the release needs two people. The first call only records the request; a different user of the roles confirms with the second call.</summary>
        private static async Task<ResponseDto<PcGuaranteeDto>> ReleaseByTwo(ClaimsPrincipal asker, ClaimsPrincipal confirmer, int id, DateTime date, string note)
        {
            ResponseDto<PcGuaranteeDto> asked = await Release(asker, id, date, note);
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.AreNotEqual(PcGuaranteeStatus.Released, asked.Data.Status, "the first call only records the request");
            return await Release(confirmer, id, date, note);
        }

        private static Guid NewDocument()
        {
            Guid id = Guid.Empty;
            H.Write(u =>
            {
                var d = AppHost.Add(u, new Entities.Model.Document("uploads/bank-letter.pdf", "application/pdf").SetName("bank letter"));
                u.SaveChanges();
                id = d.Id;
            }, Seeder);
            return id;
        }

        /// <summary>Moves the request of the first person back in time (the clock cannot be set in these tests).</summary>
        private static void AgeRequest(int id, int days) =>
            H.Write(u => { u.Set<PcGuaranteeInfo>().Single(g => g.Id == id).ReleaseRequestedAt = DateTime.Now.AddDays(-days); u.SaveChanges(); }, Seeder);

        private static int EventCount(int id) => H.Read(u => u.Set<PcGuaranteeEvent>().Count(e => e.GuaranteeInfoId == id));

        [TestMethod]
        public void TheStatusRuleOfTheSaveCallIsTotal()
        {
            foreach (PcGuaranteeStatus current in Enum.GetValues(typeof(PcGuaranteeStatus)))
            {
                Assert.IsNull(PcCommercialService.StatusChangeProblem(current, current), current + " to itself is always accepted (the form sends the current status back)");
                Assert.IsNull(PcCommercialService.StatusChangeProblem(current, 0), "no status in the request keeps the current one");
                if (current != PcGuaranteeStatus.Released)
                    Assert.IsNotNull(PcCommercialService.StatusChangeProblem(current, PcGuaranteeStatus.Released), current + " to Released must go through the release action");
                if (current != PcGuaranteeStatus.Expired)
                    Assert.IsNotNull(PcCommercialService.StatusChangeProblem(current, PcGuaranteeStatus.Expired), current + " to Expired is automatic");
                if (current != PcGuaranteeStatus.Cancelled)
                    Assert.IsNotNull(PcCommercialService.StatusChangeProblem(current, PcGuaranteeStatus.Cancelled), current + " to Cancelled must go through the cancellation event (R1a2, M2)");
            }
            // a released, cancelled or expired guarantee leaves its status only through the release, the cancellation or an extension event, never through a status in the save call
            foreach (PcGuaranteeStatus closed in new[] { PcGuaranteeStatus.Released, PcGuaranteeStatus.Cancelled, PcGuaranteeStatus.Expired })
                foreach (PcGuaranteeStatus target in new[] { PcGuaranteeStatus.Requested, PcGuaranteeStatus.Issued, PcGuaranteeStatus.Active, PcGuaranteeStatus.ExtensionPending })
                    Assert.IsNotNull(PcCommercialService.StatusChangeProblem(closed, target), closed + " is not opened again by a status change (" + target + ")");
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Requested, PcGuaranteeStatus.Issued));
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Issued, PcGuaranteeStatus.Active));
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Active, PcGuaranteeStatus.ExtensionPending));
        }

        [TestMethod]
        public async Task TheSaveCallRefusesReleasedExpiredCancelledAndReopening()
        {
            DateTime expiry = DateTime.Today.AddDays(30);
            int id = NewGuarantee(PcGuaranteeStatus.Active, expiry);

            ResponseDto<PcGuaranteeDto> released = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Released, expiry));
            Assert.IsFalse(released.Success);
            Assert.IsTrue(released.Message.Contains("آزادسازی"), released.Message);
            ResponseDto<PcGuaranteeDto> expired = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Expired, expiry));
            Assert.IsFalse(expired.Success);
            Assert.IsTrue(expired.Message.Contains("خودکار"), expired.Message);
            ResponseDto<PcGuaranteeDto> cancelled = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Cancelled, expiry));
            Assert.IsFalse(cancelled.Success, "one person typing in a status must not cancel a guarantee");
            Assert.IsTrue(cancelled.Message.Contains("رویداد «ابطال»"), cancelled.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);
            Assert.AreEqual(0, EventCount(id));

            // the form sends the current status and the current expiry back: accepted, and the other fields change
            PcGuaranteeDto edit = SaveDto(id, PcGuaranteeStatus.Active, expiry);
            edit.AmountDecimal = 2500m;
            ResponseDto<PcGuaranteeDto> same = await Save(Commercial, edit);
            Assert.IsTrue(same.Success, same.Message);
            Assert.AreEqual(2500m, Reload<PcGuaranteeInfo>(id).AmountDecimal);
            Assert.AreEqual(expiry, Reload<PcGuaranteeInfo>(id).ExpiryDate);

            // a released guarantee stays released
            Assert.IsTrue((await ReleaseByTwo(Commercial, FinanceManager, id, DateTime.Today, "letter 123")).Success);
            ResponseDto<PcGuaranteeDto> reopen = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Active, expiry));
            Assert.IsFalse(reopen.Success);
            Assert.IsTrue(reopen.Message.Contains("دوباره باز نمی‌شود"), reopen.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, Reload<PcGuaranteeInfo>(id).Status);
        }

        [TestMethod]
        public async Task TheReleaseActionNeedsTheRoleTheDateTheNoteAndASecondPerson()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));

            foreach (ClaimsPrincipal who in new[] { Qc, Accountant, Engineering, Construction })
            {
                ResponseDto<PcGuaranteeDto> refused = await Release(who, id, DateTime.Today, "letter 1");
                Assert.IsFalse(refused.Success, "a role outside the list releases nothing, and does not even ask for it");
                Assert.IsTrue(refused.Message.Contains(NotAllowed), refused.Message);
            }
            ResponseDto<PcGuaranteeDto> future = await Release(Commercial, id, DateTime.Today.AddDays(1), "letter 1");
            Assert.IsFalse(future.Success);
            Assert.IsTrue(future.Message.Contains("آینده"), future.Message);
            ResponseDto<PcGuaranteeDto> noDate = await Release(Commercial, id, default, "letter 1");
            Assert.IsFalse(noDate.Success);
            ResponseDto<PcGuaranteeDto> noNote = await Release(Commercial, id, DateTime.Today, "   ");
            Assert.IsFalse(noNote.Success);
            Assert.IsTrue(noNote.Message.Contains("توضیح"), noNote.Message);
            PcGuaranteeInfo untouched = Reload<PcGuaranteeInfo>(id);
            Assert.AreEqual(PcGuaranteeStatus.Active, untouched.Status, "nothing was released");
            Assert.IsNull(untouched.ReleaseRequestedByUserId, "a refused call records no request either");
            Assert.AreEqual(0, Modified(nameof(PcGuaranteeInfo), id).Count);

            // first call: only the request is recorded
            const string letter = "client letter 77/ب dated 1405/01/05";
            DateTime date = DateTime.Today.AddDays(-2);
            ResponseDto<PcGuaranteeDto> asked = await Release(Commercial, id, date, letter);
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.IsTrue(asked.Message.Contains("منتظر تأیید"), asked.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, asked.Data.Status);
            Assert.AreEqual(26, asked.Data.ReleaseRequestedByUserId);
            Assert.IsNotNull(asked.Data.ReleaseRequestedAt);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);
            Assert.AreEqual(0, EventCount(id), "no release event yet");

            // the one who asked cannot confirm his own request
            ResponseDto<PcGuaranteeDto> self = await Release(Commercial, id, date, letter);
            Assert.IsFalse(self.Success);
            Assert.IsTrue(self.Message.Contains(SameUser), self.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);

            // a different user of the roles confirms with his own call
            ResponseDto<PcGuaranteeDto> done = await Release(FinanceManager, id, date, letter);
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, done.Data.Status);
            Assert.AreEqual(26, done.Data.ReleaseRequestedByUserId, "the DTO and the row keep who asked");

            // the event row carries the date, the note and the one who confirmed
            PcGuaranteeEvent ev = H.Read(u => u.Set<PcGuaranteeEvent>().Single(e => e.GuaranteeInfoId == id && e.Kind == PcGuaranteeEventKind.Release));
            Assert.AreEqual(date, ev.EventDate);
            Assert.AreEqual(letter, ev.Note);
            Assert.AreEqual(27, ev.CreatedByUserId);

            // the audit trail has the request (with the one who asked) and the status change (with the one who confirmed)
            List<FnAuditLog> rows = Modified(nameof(PcGuaranteeInfo), id);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(26, rows[0].UserId);
            Assert.AreEqual(26, Fields(rows[0].AfterJson)["ReleaseRequestedByUserId"].GetInt32());
            Assert.AreEqual(27, rows[1].UserId);
            Assert.AreEqual("Active", Fields(rows[1].BeforeJson)["Status"].GetString());
            Assert.AreEqual("Released", Fields(rows[1].AfterJson)["Status"].GetString());

            // released once: not twice
            ResponseDto<PcGuaranteeDto> twice = await Release(Pmo1, id, DateTime.Today, "again");
            Assert.IsFalse(twice.Success);
            Assert.IsTrue(twice.Message.Contains("قبلاً آزاد شده"), twice.Message);
        }

        [TestMethod]
        public async Task OnlyAnIssuedGuaranteeCanBeReleasedAndTheThreeRolesCanAskAndConfirm()
        {
            int requested = NewGuarantee(PcGuaranteeStatus.Requested);
            int cancelled = NewGuarantee(PcGuaranteeStatus.Cancelled);
            Assert.IsFalse((await Release(Pmo1, requested, DateTime.Today, "letter")).Success, "not issued yet");
            Assert.IsFalse((await Release(Pmo1, cancelled, DateTime.Today, "letter")).Success, "cancelled guarantees are not released");
            Assert.IsFalse((await Release(Pmo1, 987654, DateTime.Today, "letter")).Success, "unknown guarantee");
            Assert.IsNull(Reload<PcGuaranteeInfo>(requested).ReleaseRequestedByUserId, "a refused call records no request");
            Assert.IsNull(Reload<PcGuaranteeInfo>(cancelled).ReleaseRequestedByUserId);

            foreach ((ClaimsPrincipal asker, ClaimsPrincipal confirmer, PcGuaranteeStatus status) in new[]
            {
                (Pmo1, Commercial, PcGuaranteeStatus.Active), (Commercial, FinanceManager, PcGuaranteeStatus.Issued), (FinanceManager, Pmo2, PcGuaranteeStatus.ExtensionPending), (Pmo2, Pmo1, PcGuaranteeStatus.Expired),
            })
            {
                int id = NewGuarantee(status, DateTime.Today.AddDays(-3));
                ResponseDto<PcGuaranteeDto> r = await ReleaseByTwo(asker, confirmer, id, DateTime.Today, "letter");
                Assert.IsTrue(r.Success, status + ": " + r.Message);
                Assert.AreEqual(PcGuaranteeStatus.Released, Reload<PcGuaranteeInfo>(id).Status);
            }

            // somebody outside the three roles cannot confirm a request of the others either
            int pending = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(10));
            Assert.IsTrue((await Release(Commercial, pending, DateTime.Today, "letter")).Success);
            foreach (ClaimsPrincipal outsider in new[] { Accountant, Qc, Engineering })
                Assert.IsFalse((await Release(outsider, pending, DateTime.Today, "letter")).Success);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(pending).Status);
        }

        [TestMethod]
        public async Task ARequestOfTheFirstPersonIsKeptSevenDaysAndThenForgotten()
        {
            // six days later the request is still there: a different user confirms it
            int six = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));
            Assert.IsTrue((await Release(Pmo1, six, DateTime.Today, "letter 6")).Success);
            AgeRequest(six, 6);
            ResponseDto<PcGuaranteeDto> kept = await Release(FinanceManager, six, DateTime.Today, "letter 6");
            Assert.IsTrue(kept.Success, kept.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, kept.Data.Status);

            // eight days later the old request counts for nothing: the next call, even by another user, only records a new one
            int eight = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));
            Assert.IsTrue((await Release(Commercial, eight, DateTime.Today, "letter 8")).Success);
            AgeRequest(eight, 8);
            ResponseDto<PcGuaranteeDto> late = await Release(FinanceManager, eight, DateTime.Today, "letter 8");
            Assert.IsTrue(late.Success, late.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, late.Data.Status, "a forgotten request is not a confirmation");
            Assert.AreEqual(27, late.Data.ReleaseRequestedByUserId, "the new request belongs to the new asker");
            Assert.AreEqual(0, EventCount(eight));
            // the first asker may now confirm the new request
            ResponseDto<PcGuaranteeDto> done = await Release(Commercial, eight, DateTime.Today, "letter 8");
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, done.Data.Status);
        }

        [TestMethod]
        public async Task TheCancellationNeedsTheSameRolesDateNoteAndSecondPersonAsTheRelease()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));

            foreach (ClaimsPrincipal who in new[] { Qc, Accountant, Engineering, Construction, Planning })
            {
                ResponseDto<PcGuaranteeDto> refused = await Event(who, id, PcGuaranteeEventKind.Cancellation, note: "bank letter 9");
                Assert.IsFalse(refused.Success, "the cancellation is not for the FINANCIAL role or any other role outside the three");
                Assert.IsTrue(refused.Message.Contains(NotAllowed), refused.Message);
            }
            ResponseDto<PcGuaranteeDto> noDate = await Event(Commercial, id, PcGuaranteeEventKind.Cancellation, note: "bank letter 9", date: default(DateTime));
            Assert.IsFalse(noDate.Success);
            Assert.IsTrue(noDate.Message.Contains("تاریخ ابطال"), noDate.Message);
            ResponseDto<PcGuaranteeDto> future = await Event(Commercial, id, PcGuaranteeEventKind.Cancellation, note: "bank letter 9", date: DateTime.Today.AddDays(1));
            Assert.IsFalse(future.Success);
            Assert.IsTrue(future.Message.Contains("آینده"), future.Message);
            ResponseDto<PcGuaranteeDto> noNote = await Event(Commercial, id, PcGuaranteeEventKind.Cancellation, note: "  ");
            Assert.IsFalse(noNote.Success);
            Assert.IsTrue(noNote.Message.Contains("توضیح"), noNote.Message);
            PcGuaranteeInfo untouched = Reload<PcGuaranteeInfo>(id);
            Assert.AreEqual(PcGuaranteeStatus.Active, untouched.Status);
            Assert.IsNull(untouched.ReleaseRequestedByUserId, "a refused call records no request");
            Assert.AreEqual(0, EventCount(id));

            // first call: the request only
            DateTime date = DateTime.Today.AddDays(-1);
            ResponseDto<PcGuaranteeDto> asked = await Event(Commercial, id, PcGuaranteeEventKind.Cancellation, note: "client letter 12", date: date);
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.IsTrue(asked.Message.Contains("منتظر تأیید"), asked.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, asked.Data.Status);
            Assert.AreEqual(26, asked.Data.ReleaseRequestedByUserId);
            Assert.AreEqual(0, EventCount(id), "no cancellation event yet");

            // the one who asked cannot confirm
            ResponseDto<PcGuaranteeDto> self = await Event(Commercial, id, PcGuaranteeEventKind.Cancellation, note: "client letter 12", date: date);
            Assert.IsFalse(self.Success);
            Assert.IsTrue(self.Message.Contains(SameUser), self.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);

            // a different user confirms
            ResponseDto<PcGuaranteeDto> done = await Event(Pmo1, id, PcGuaranteeEventKind.Cancellation, note: "client letter 12 (confirmed)", date: date);
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(PcGuaranteeStatus.Cancelled, done.Data.Status);
            PcGuaranteeEvent ev = H.Read(u => u.Set<PcGuaranteeEvent>().Single(e => e.GuaranteeInfoId == id));
            Assert.AreEqual(PcGuaranteeEventKind.Cancellation, ev.Kind);
            Assert.AreEqual(date, ev.EventDate);
            Assert.AreEqual("client letter 12 (confirmed)", ev.Note);
            Assert.AreEqual(11, ev.CreatedByUserId);

            List<FnAuditLog> rows = Modified(nameof(PcGuaranteeInfo), id);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(26, rows[0].UserId);
            Assert.AreEqual(11, rows[1].UserId);
            Assert.AreEqual("Active", Fields(rows[1].BeforeJson)["Status"].GetString());
            Assert.AreEqual("Cancelled", Fields(rows[1].AfterJson)["Status"].GetString());

            // a cancelled guarantee is closed: no second cancellation, no release, no reopening through the save call
            ResponseDto<PcGuaranteeDto> again = await Event(FinanceManager, id, PcGuaranteeEventKind.Cancellation, note: "once more", date: date);
            Assert.IsFalse(again.Success);
            Assert.IsTrue(again.Message.Contains("بسته شده"), again.Message);
            Assert.IsFalse((await Release(FinanceManager, id, DateTime.Today, "letter")).Success);
            ResponseDto<PcGuaranteeDto> reopen = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Active, DateTime.Today.AddDays(30)));
            Assert.IsFalse(reopen.Success);
            Assert.IsTrue(reopen.Message.Contains("دوباره باز نمی‌شود"), reopen.Message);
            Assert.AreEqual(PcGuaranteeStatus.Cancelled, Reload<PcGuaranteeInfo>(id).Status);
        }

        [TestMethod]
        public async Task TheGenericEventCallDoesNotWriteReleaseOrExpiry()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));
            ResponseDto<PcGuaranteeDto> release = await Event(Commercial, id, PcGuaranteeEventKind.Release);
            Assert.IsFalse(release.Success);
            Assert.IsTrue(release.Message.Contains("guarantees/release"), release.Message);
            ResponseDto<PcGuaranteeDto> expiry = await Event(Commercial, id, PcGuaranteeEventKind.Expiry);
            Assert.IsFalse(expiry.Success);
            Assert.AreEqual(0, EventCount(id), "no event row was left behind");
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);

            // the other kinds still work (an extension with its evidence)
            ResponseDto<PcGuaranteeDto> extension = await Event(Commercial, id, PcGuaranteeEventKind.Extension, DateTime.Today.AddDays(90), note: "bank addendum 4");
            Assert.IsTrue(extension.Success, extension.Message);
            Assert.AreEqual(DateTime.Today.AddDays(90), Reload<PcGuaranteeInfo>(id).ExpiryDate);
        }

        [TestMethod]
        public async Task AnIssuedGuaranteeChangesItsExpiryOnlyThroughAnExtensionWithEvidence()
        {
            DateTime expiry = DateTime.Today.AddDays(30);
            int id = NewGuarantee(PcGuaranteeStatus.Active, expiry);

            // the save call: another date, an earlier date and no date are refused; the same date (and other fields) is accepted
            foreach (DateTime? other in new DateTime?[] { expiry.AddDays(30), expiry.AddDays(-5), null })
            {
                ResponseDto<PcGuaranteeDto> moved = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Active, other));
                Assert.IsFalse(moved.Success, "expiry " + other);
                Assert.IsTrue(moved.Message.Contains("رویداد «تمدید»"), moved.Message);
            }
            Assert.AreEqual(expiry, Reload<PcGuaranteeInfo>(id).ExpiryDate);
            PcGuaranteeDto sameDate = SaveDto(id, PcGuaranteeStatus.Active, expiry.AddHours(5));
            sameDate.AmountDecimal = 4000m;
            ResponseDto<PcGuaranteeDto> accepted = await Save(Commercial, sameDate);
            Assert.IsTrue(accepted.Success, accepted.Message);
            Assert.AreEqual(4000m, Reload<PcGuaranteeInfo>(id).AmountDecimal);
            Assert.AreEqual(expiry, Reload<PcGuaranteeInfo>(id).ExpiryDate);

            // the extension: a note is required, the new date must be later, an unknown letter is refused
            ResponseDto<PcGuaranteeDto> noNote = await Event(Commercial, id, PcGuaranteeEventKind.Extension, expiry.AddDays(60), note: "  ");
            Assert.IsFalse(noNote.Success);
            Assert.IsTrue(noNote.Message.Contains("توضیح"), noNote.Message);
            ResponseDto<PcGuaranteeDto> notLater = await Event(Commercial, id, PcGuaranteeEventKind.Extension, expiry, note: "bank addendum 5");
            Assert.IsFalse(notLater.Success);
            ResponseDto<PcGuaranteeDto> unknown = await Event(Commercial, id, PcGuaranteeEventKind.Extension, expiry.AddDays(60), note: "bank addendum 5", documentId: Guid.NewGuid());
            Assert.IsFalse(unknown.Success);
            Assert.IsTrue(unknown.Message.Contains("فایل نامه"), unknown.Message);
            Assert.AreEqual(expiry, Reload<PcGuaranteeInfo>(id).ExpiryDate);
            Assert.AreEqual(0, EventCount(id));

            // with a note alone, and with the note and the letter
            Assert.IsTrue((await Event(Commercial, id, PcGuaranteeEventKind.Extension, expiry.AddDays(60), note: "bank addendum 5")).Success);
            Assert.AreEqual(expiry.AddDays(60), Reload<PcGuaranteeInfo>(id).ExpiryDate);
            Guid letter = NewDocument();
            ResponseDto<PcGuaranteeDto> withLetter = await Event(Pmo1, id, PcGuaranteeEventKind.Extension, expiry.AddDays(120), note: "bank addendum 6", documentId: letter);
            Assert.IsTrue(withLetter.Success, withLetter.Message);
            Assert.AreEqual(expiry.AddDays(120), Reload<PcGuaranteeInfo>(id).ExpiryDate);
            PcGuaranteeEvent ev = H.Read(u => u.Set<PcGuaranteeEvent>().Single(e => e.GuaranteeInfoId == id && e.DocumentId == letter));
            Assert.AreEqual("bank addendum 6", ev.Note);
            Assert.AreEqual(11, ev.CreatedByUserId);
            Assert.AreEqual(expiry.AddDays(120), ev.NewExpiryDate);

            // the audit trail shows each new date with the user who extended
            List<FnAuditLog> rows = Modified(nameof(PcGuaranteeInfo), id).Where(r => Fields(r.AfterJson).ContainsKey("ExpiryDate")).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(26, rows[0].UserId);
            Assert.AreEqual(11, rows[1].UserId);
        }

        [TestMethod]
        public async Task ADateThatIsStillMissingMayBeFilledOnceAndAGuaranteeNotYetIssuedChangesFreely()
        {
            int missing = NewGuarantee(PcGuaranteeStatus.Active, null);
            DateTime date = DateTime.Today.AddDays(40);
            Assert.IsTrue((await Save(Commercial, SaveDto(missing, PcGuaranteeStatus.Active, date))).Success, "an old row whose date the parser could not read gets it by hand");
            Assert.AreEqual(date, Reload<PcGuaranteeInfo>(missing).ExpiryDate);
            Assert.IsFalse((await Save(Commercial, SaveDto(missing, PcGuaranteeStatus.Active, date.AddDays(10)))).Success, "once it is there it moves only by an extension");

            int requested = NewGuarantee(PcGuaranteeStatus.Requested, DateTime.Today.AddDays(10));
            Assert.IsTrue((await Save(Commercial, SaveDto(requested, PcGuaranteeStatus.Requested, DateTime.Today.AddDays(50)))).Success, "nothing was issued yet: the date is still a plan");
            Assert.AreEqual(DateTime.Today.AddDays(50), Reload<PcGuaranteeInfo>(requested).ExpiryDate);
        }

        [TestMethod]
        public async Task AnExpiredGuaranteeIsNotBroughtBackByTheSaveCallButAnExtensionRevivesIt()
        {
            DateTime expiry = DateTime.Today.AddDays(-5);
            int id = NewGuarantee(PcGuaranteeStatus.Active, expiry);
            Assert.IsTrue((await H.AsSystem(p => p.GetRequiredService<IPcCommercialService>().ExpireGuaranteesAsync(CancellationToken.None))) >= 1);
            Assert.AreEqual(PcGuaranteeStatus.Expired, Reload<PcGuaranteeInfo>(id).Status);

            foreach (PcGuaranteeStatus target in new[] { PcGuaranteeStatus.Active, PcGuaranteeStatus.Issued, PcGuaranteeStatus.ExtensionPending, PcGuaranteeStatus.Requested })
            {
                ResponseDto<PcGuaranteeDto> back = await Save(Commercial, SaveDto(id, target, expiry));
                Assert.IsFalse(back.Success, "expired to " + target);
                Assert.IsTrue(back.Message.Contains("رویداد «تمدید»"), back.Message);
            }
            // a later date through the save call is refused as well: the date is what the bank holds
            ResponseDto<PcGuaranteeDto> later = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Expired, DateTime.Today.AddDays(90)));
            Assert.IsFalse(later.Success);
            Assert.AreEqual(PcGuaranteeStatus.Expired, Reload<PcGuaranteeInfo>(id).Status);
            Assert.AreEqual(expiry, Reload<PcGuaranteeInfo>(id).ExpiryDate);

            // the extension without evidence is refused, with it the guarantee is active again
            Assert.IsFalse((await Event(Commercial, id, PcGuaranteeEventKind.Extension, DateTime.Today.AddDays(90))).Success);
            ResponseDto<PcGuaranteeDto> revived = await Event(Commercial, id, PcGuaranteeEventKind.Extension, DateTime.Today.AddDays(90), note: "bank addendum 7 dated 1405/02/01");
            Assert.IsTrue(revived.Success, revived.Message);
            PcGuaranteeInfo stored = Reload<PcGuaranteeInfo>(id);
            Assert.AreEqual(PcGuaranteeStatus.Active, stored.Status);
            Assert.AreEqual(DateTime.Today.AddDays(90), stored.ExpiryDate);
            Assert.AreEqual(2, EventCount(id), "the expiry and the extension are both in the history");
        }

        [TestMethod]
        public async Task TheDailyJobExpiresOverdueGuaranteesOnceAndLeavesTheRestAlone()
        {
            // a contract of its own so that the alert list below only holds what this test made
            const int contract = 777;
            int overdueActive = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(-1), contract);
            int overdueIssued = NewGuarantee(PcGuaranteeStatus.Issued, DateTime.Today.AddDays(-30), contract);
            int overdueExtension = NewGuarantee(PcGuaranteeStatus.ExtensionPending, DateTime.Today.AddDays(-2), contract);
            int futureActive = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(10), contract);
            int todayActive = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today, contract);
            int released = NewGuarantee(PcGuaranteeStatus.Released, DateTime.Today.AddDays(-50), contract);
            int cancelled = NewGuarantee(PcGuaranteeStatus.Cancelled, DateTime.Today.AddDays(-50), contract);
            int noDate = NewGuarantee(PcGuaranteeStatus.Active, null, contract);

            // a user outside the guarantee roles starts nothing
            int byQc = await H.As(Qc, p => p.GetRequiredService<IPcCommercialService>().ExpireGuaranteesAsync(CancellationToken.None));
            Assert.AreEqual(0, byQc);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(overdueActive).Status);

            // the server job has no user; what is due in the whole database (other tests share the host) is what it expires
            DateTime today = DateTime.Today;
            int due = H.Read(u => u.Set<PcGuaranteeInfo>().Count(i => !i.Deleted && i.ExpiryDate != null && i.ExpiryDate < today
                && (i.Status == PcGuaranteeStatus.Issued || i.Status == PcGuaranteeStatus.Active || i.Status == PcGuaranteeStatus.ExtensionPending)));
            Assert.IsTrue(due >= 3, "the three overdue guarantees of this test are due");
            int expiredCount = await H.AsSystem(p => p.GetRequiredService<IPcCommercialService>().ExpireGuaranteesAsync(CancellationToken.None));
            Assert.AreEqual(due, expiredCount);
            foreach (int id in new[] { overdueActive, overdueIssued, overdueExtension })
            {
                Assert.AreEqual(PcGuaranteeStatus.Expired, Reload<PcGuaranteeInfo>(id).Status);
                PcGuaranteeEvent ev = H.Read(u => u.Set<PcGuaranteeEvent>().Single(e => e.GuaranteeInfoId == id));
                Assert.AreEqual(PcGuaranteeEventKind.Expiry, ev.Kind);
                Assert.AreEqual(0, ev.CreatedByUserId, "the job is user 0");
                Assert.IsTrue(ev.Note.Contains("انقضای خودکار"), ev.Note);
            }
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(futureActive).Status);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(todayActive).Status, "a guarantee expires the day after its expiry date");
            Assert.AreEqual(PcGuaranteeStatus.Released, Reload<PcGuaranteeInfo>(released).Status);
            Assert.AreEqual(PcGuaranteeStatus.Cancelled, Reload<PcGuaranteeInfo>(cancelled).Status);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(noDate).Status);
            Assert.AreEqual(0, H.Read(u => u.Set<PcGuaranteeEvent>().Count(e => e.GuaranteeInfoId == released || e.GuaranteeInfoId == cancelled || e.GuaranteeInfoId == futureActive)));

            // the status change is in the audit trail as the job (user 0)
            List<FnAuditLog> rows = Modified(nameof(PcGuaranteeInfo), overdueActive);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(0, rows[0].UserId);
            Assert.AreEqual("Active", Fields(rows[0].BeforeJson)["Status"].GetString());
            Assert.AreEqual("Expired", Fields(rows[0].AfterJson)["Status"].GetString());

            // idempotent: the second run finds nothing
            Assert.AreEqual(0, await H.AsSystem(p => p.GetRequiredService<IPcCommercialService>().ExpireGuaranteesAsync(CancellationToken.None)));

            // an expired guarantee that was not released or extended stays an alert (level 0), a released one does not
            List<PcGuaranteeAlertDto> alerts = await H.As(Pmo1, p => p.GetRequiredService<IPcCommercialService>().GuaranteeAlertsAsync(contract, CancellationToken.None));
            Assert.IsTrue(alerts.Any(a => a.GuaranteeId == overdueActive && a.AlertLevel == 0), "the expired guarantee is still listed");
            Assert.IsFalse(alerts.Any(a => a.GuaranteeId == released || a.GuaranteeId == cancelled));
        }

        [TestMethod]
        public async Task AnExpiredGuaranteeCanStillBeReleasedByTheClientLetter()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(-5));
            Assert.IsTrue((await H.AsSystem(p => p.GetRequiredService<IPcCommercialService>().ExpireGuaranteesAsync(CancellationToken.None))) >= 1);
            Assert.AreEqual(PcGuaranteeStatus.Expired, Reload<PcGuaranteeInfo>(id).Status);
            ResponseDto<PcGuaranteeDto> released = await ReleaseByTwo(Commercial, FinanceManager, id, DateTime.Today, "client letter");
            Assert.IsTrue(released.Success, released.Message);
            Assert.AreEqual(2, EventCount(id), "the expiry and the release are both in the history");
        }

        // ------------------------------------------------------------------------------------------------ the controllers

        private static string RolesOf(Type controller, string action)
        {
            MethodInfo method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, controller.Name + "." + action + " was renamed: update this test");
            return string.Join(",", method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Roles).Where(r => !string.IsNullOrWhiteSpace(r)));
        }

        private static string[] Set(string roles) => roles.Split(',').Select(r => r.Trim()).OrderBy(r => r, StringComparer.Ordinal).ToArray();

        [TestMethod]
        public void TheEvaluationAndTheReleaseCarryTheirRoleLists()
        {
            Type controller = typeof(WebApi.Controllers.ProjectControl.ProjectControlCommercialController);
            CollectionAssert.AreEqual(Set("PMO,PLANNING,COMMERCIAL,FINANCIAL"), Set(RolesOf(controller, "EvaluateMilestonesAsync")),
                "the evaluation of the payment milestones starts the invoicing flow: not for every signed-in user");
            CollectionAssert.AreEqual(Set("COMMERCIAL,FINANCEMANAGER,PMO"), Set(RolesOf(controller, "ReleaseGuaranteeAsync")));

            MethodInfo release = controller.GetMethod("ReleaseGuaranteeAsync");
            HttpPostAttribute post = release.GetCustomAttribute<HttpPostAttribute>();
            Assert.IsNotNull(post, "the release is a POST");
            Assert.AreEqual("guarantees/release", post.Template);
        }
    }
}
