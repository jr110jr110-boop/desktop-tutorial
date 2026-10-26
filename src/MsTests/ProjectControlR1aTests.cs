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
    /// milestones is not open to every signed-in user, and a guarantee is released through its own action (date and note) or expires by itself.
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

        private static Task<ResponseDto<PcGuaranteeDto>> Event(ClaimsPrincipal who, int id, PcGuaranteeEventKind kind, DateTime? newExpiry = null) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().AddGuaranteeEventAsync(new PcGuaranteeEventSaveDto { GuaranteeId = id, Kind = kind, EventDate = DateTime.Today, NewExpiryDate = newExpiry }, CancellationToken.None));

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
            }
            foreach (PcGuaranteeStatus target in new[] { PcGuaranteeStatus.Requested, PcGuaranteeStatus.Issued, PcGuaranteeStatus.Active, PcGuaranteeStatus.ExtensionPending, PcGuaranteeStatus.Cancelled })
                Assert.IsNotNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Released, target), "a released guarantee is not opened again (" + target + ")");
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Requested, PcGuaranteeStatus.Issued));
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Active, PcGuaranteeStatus.Cancelled));
            Assert.IsNull(PcCommercialService.StatusChangeProblem(PcGuaranteeStatus.Expired, PcGuaranteeStatus.Active), "an expired guarantee can be extended back to active");
        }

        [TestMethod]
        public async Task TheSaveCallRefusesReleasedExpiredAndReopening()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));

            ResponseDto<PcGuaranteeDto> released = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Released, DateTime.Today.AddDays(30)));
            Assert.IsFalse(released.Success);
            Assert.IsTrue(released.Message.Contains("آزادسازی"), released.Message);
            ResponseDto<PcGuaranteeDto> expired = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Expired, DateTime.Today.AddDays(30)));
            Assert.IsFalse(expired.Success);
            Assert.IsTrue(expired.Message.Contains("خودکار"), expired.Message);
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);

            // the form sends the current status back: accepted, and the other fields change
            ResponseDto<PcGuaranteeDto> same = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Active, DateTime.Today.AddDays(60)));
            Assert.IsTrue(same.Success, same.Message);
            Assert.AreEqual(DateTime.Today.AddDays(60), Reload<PcGuaranteeInfo>(id).ExpiryDate);

            // a released guarantee stays released
            Assert.IsTrue((await Release(Commercial, id, DateTime.Today, "letter 123")).Success);
            ResponseDto<PcGuaranteeDto> reopen = await Save(Commercial, SaveDto(id, PcGuaranteeStatus.Active, DateTime.Today.AddDays(60)));
            Assert.IsFalse(reopen.Success);
            Assert.IsTrue(reopen.Message.Contains("دوباره باز نمی‌شود"), reopen.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, Reload<PcGuaranteeInfo>(id).Status);
        }

        [TestMethod]
        public async Task TheReleaseActionNeedsTheRoleTheDateAndTheNote()
        {
            int id = NewGuarantee(PcGuaranteeStatus.Active, DateTime.Today.AddDays(30));

            foreach (ClaimsPrincipal who in new[] { Qc, Accountant, Engineering, Construction })
            {
                ResponseDto<PcGuaranteeDto> refused = await Release(who, id, DateTime.Today, "letter 1");
                Assert.IsFalse(refused.Success, "a role outside the list releases nothing");
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
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status, "nothing was released");

            ResponseDto<PcGuaranteeDto> done = await Release(FinanceManager, id, DateTime.Today.AddDays(-2), "client letter 77/ب dated 1405/01/05");
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, done.Data.Status);

            // the event row carries the date, the note and the releaser
            PcGuaranteeEvent ev = H.Read(u => u.Set<PcGuaranteeEvent>().Single(e => e.GuaranteeInfoId == id && e.Kind == PcGuaranteeEventKind.Release));
            Assert.AreEqual(DateTime.Today.AddDays(-2), ev.EventDate);
            Assert.AreEqual("client letter 77/ب dated 1405/01/05", ev.Note);
            Assert.AreEqual(27, ev.CreatedByUserId);

            // the audit trail has the status change with the releaser
            List<FnAuditLog> rows = Modified(nameof(PcGuaranteeInfo), id);
            FnAuditLog row = rows.Single(r => r.AfterJson.Contains("Released"));
            Assert.AreEqual(27, row.UserId);
            Assert.AreEqual("Active", Fields(row.BeforeJson)["Status"].GetString());

            // released once: not twice
            ResponseDto<PcGuaranteeDto> twice = await Release(Pmo1, id, DateTime.Today, "again");
            Assert.IsFalse(twice.Success);
            Assert.IsTrue(twice.Message.Contains("قبلاً آزاد شده"), twice.Message);
        }

        [TestMethod]
        public async Task OnlyAnIssuedGuaranteeCanBeReleasedAndTheThreeRolesCan()
        {
            int requested = NewGuarantee(PcGuaranteeStatus.Requested);
            int cancelled = NewGuarantee(PcGuaranteeStatus.Cancelled);
            Assert.IsFalse((await Release(Pmo1, requested, DateTime.Today, "letter")).Success, "not issued yet");
            Assert.IsFalse((await Release(Pmo1, cancelled, DateTime.Today, "letter")).Success, "cancelled guarantees are not released");
            Assert.IsFalse((await Release(Pmo1, 987654, DateTime.Today, "letter")).Success, "unknown guarantee");

            foreach ((ClaimsPrincipal who, PcGuaranteeStatus status) in new[]
            {
                (Pmo1, PcGuaranteeStatus.Active), (Commercial, PcGuaranteeStatus.Issued), (FinanceManager, PcGuaranteeStatus.ExtensionPending), (Pmo2, PcGuaranteeStatus.Expired),
            })
            {
                int id = NewGuarantee(status, DateTime.Today.AddDays(-3));
                ResponseDto<PcGuaranteeDto> r = await Release(who, id, DateTime.Today, "letter");
                Assert.IsTrue(r.Success, status + ": " + r.Message);
                Assert.AreEqual(PcGuaranteeStatus.Released, Reload<PcGuaranteeInfo>(id).Status);
            }
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
            Assert.AreEqual(0, H.Read(u => u.Set<PcGuaranteeEvent>().Count(e => e.GuaranteeInfoId == id)), "no event row was left behind");
            Assert.AreEqual(PcGuaranteeStatus.Active, Reload<PcGuaranteeInfo>(id).Status);

            // the other kinds still work
            ResponseDto<PcGuaranteeDto> extension = await Event(Commercial, id, PcGuaranteeEventKind.Extension, DateTime.Today.AddDays(90));
            Assert.IsTrue(extension.Success, extension.Message);
            Assert.AreEqual(DateTime.Today.AddDays(90), Reload<PcGuaranteeInfo>(id).ExpiryDate);
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
            ResponseDto<PcGuaranteeDto> released = await Release(Commercial, id, DateTime.Today, "client letter");
            Assert.IsTrue(released.Success, released.Message);
            Assert.AreEqual(2, H.Read(u => u.Set<PcGuaranteeEvent>().Count(e => e.GuaranteeInfoId == id)), "the expiry and the release are both in the history");
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
