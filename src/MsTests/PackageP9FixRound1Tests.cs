using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AuditSeat;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace MsTests
{
    /// <summary>Package 9, fix round 1: re-check modes, suppression, horizon and backlog, systemic findings, accepted deviations, the report periods and recipients, limits, escalation.</summary>
    [TestClass]
    public class PackageP9FixRound1Tests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static AppHost NewHost() => PackageP9AuditSeatTests.NewHost();

        private static void SelfDecided(AppHost host, int n, int daysAgo = 0)
        {
            host.Write(u =>
            {
                for (int i = 0; i < n; i++)
                    AppHost.Add(u, new SafetySettingChange { Store = SafetyStore.Quality, Key = "k" + i + "_" + daysAgo, RequestedByUserId = 308, DecidedByUserId = 308, DecidedAt = DateTimeOffset.Now.AddDays(-daysAgo), Status = SafetyChangeStatus.Approved, NewValue = "1", OldValue = "0", Reason = "r" });
                u.SaveChanges();
            });
        }

        private static List<AusFinding> Findings(AppHost host, string rule) => host.Read(u => u.Set<AusFinding>().AsNoTracking().Where(f => f.RuleCode == rule).OrderBy(f => f.Id).ToList());

        [TestMethod]
        public void The_Defaults_Without_A_Cited_Standard_Are_Zero_Tolerance_And_The_Proposal_Is_Shown_As_The_Star_Value()
        {
            foreach (string k in new[] { AusSettingKeys.ChainRunHours, AusSettingKeys.RiskStaleDays, AusSettingKeys.GateExceptionMax, AusSettingKeys.BackupStaleDays, AusSettingKeys.ExceptionPendingDays })
                Assert.AreEqual(0m, AusSettingKeys.DefaultOf(k), k);
            Assert.AreEqual(0m, AusSettingKeys.DefaultOf(AusSettingKeys.HorizonDate));
            Assert.AreEqual(3m, AusSettingKeys.DefaultOf(AusSettingKeys.SystemicMinObjects));
        }

        [TestMethod]
        public async Task A_Closed_Or_Voided_Event_Finding_Is_Not_Created_Again_By_The_Next_Run()
        {
            AppHost host = NewHost();
            SelfDecided(host, 1);
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            AusFinding f = Findings(host, "AUD-MC-002").Single();
            Assert.AreEqual("Event", AusCatalog.Find("AUD-MC-002").Mode.ToString());
            host.Write(u => { var x = u.Set<AusFinding>().Single(y => y.Id == f.Id); x.Status = AusFindingStatus.Closed; x.ClosedAt = DateTimeOffset.Now; u.SaveChanges(); });
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(1, Findings(host, "AUD-MC-002").Count, "the same past event does not return");
            host.Write(u => { var x = u.Set<AusFinding>().Single(y => y.Id == f.Id); x.Status = AusFindingStatus.Voided; u.SaveChanges(); });
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(1, Findings(host, "AUD-MC-002").Count, "a voided finding is suppressed too");
        }

        [TestMethod]
        public async Task Events_Before_The_Horizon_Make_One_Backlog_Finding_And_Not_One_Each()
        {
            AppHost host = NewHost();
            SelfDecided(host, 5, daysAgo: 60);
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            List<AusFinding> all = Findings(host, "AUD-MC-002");
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("Backlog", all[0].ObjectType);
            Assert.AreEqual(AusSeverity.Minor, all[0].Severity);
        }

        [TestMethod]
        public async Task Three_Objects_Of_One_Rule_Make_One_Systemic_Major_Finding()
        {
            AppHost host = NewHost();
            SelfDecided(host, 3);
            var first = await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(0, first.Data.Recurrences, "nothing was closed before, and a systemic finding is no recurrence");
            Assert.AreEqual(1, first.Data.Systemic);
            List<AusFinding> all = Findings(host, "AUD-MC-002");
            Assert.AreEqual(3, all.Count(f => f.ObjectType != "Systemic"));
            AusFinding sys = all.Single(f => f.ObjectType == "Systemic");
            Assert.AreEqual(AusSeverity.Major, sys.Severity);
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(1, Findings(host, "AUD-MC-002").Count(f => f.ObjectType == "Systemic"), "one, not one per run");
        }

        [TestMethod]
        public async Task The_Run_Always_Ends_And_An_Overdue_Finding_Is_Escalated_Without_A_Second_Finding()
        {
            AppHost host = NewHost();
            int id = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, tweak: f => f.DueDate = DateTime.Today.AddDays(-3));
            var run = await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreNotEqual((int)AusRunStatus.Running, run.Data.StatusValue);
            Assert.IsNotNull(run.Data.FinishedAtText);
            Assert.IsNotNull(PackageP9AuditSeatTests.Get(host, id).EscalatedAt);
            Assert.AreEqual(0, Findings(host, "AUD-SYS-002").Count);
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(1, host.Read(u => u.Set<AusFindingEvent>().Count(e => e.FindingId == id && e.Kind == "escalated")), "escalated once");
        }

        [TestMethod]
        public async Task An_Accepted_Deviation_Needs_Three_Distinct_People_Hides_The_Finding_And_Expires()
        {
            AppHost host = NewHost();
            const string key = "RestoreTestResult|0|never";
            var propose = new AusAcceptanceProposeDto { RuleCode = "AUD-INT-002", ObjectKey = key, ObjectNumber = "n", Reason = "دلیل کافی برای پذیرش موقت این انحراف", ExpiresOn = DateTime.Today.AddDays(30) };
            Assert.IsFalse((await host.As(PackageP9AuditSeatTests.Pmo1, p => p.GetRequiredService<IAusAcceptanceService>().ProposeAsync(propose, Ct))).Success, "only QA proposes");
            var made = await host.As(PackageP9AuditSeatTests.QaPmo, p => p.GetRequiredService<IAusAcceptanceService>().ProposeAsync(propose, Ct));
            Assert.IsTrue(made.Success, made.Message);
            int id = made.Data.Id;
            Task<Common.Results.ResponseDto<AusAcceptanceDto>> Step(System.Security.Claims.ClaimsPrincipal who, string step, bool refuse = false) =>
                host.As(who, p => p.GetRequiredService<IAusAcceptanceService>().StepAsync(new AusAcceptanceStepDto { AcceptanceId = id, Step = step, Refuse = refuse, Note = "یادداشت کافی" }, Ct));
            Assert.IsFalse((await Step(PackageP9AuditSeatTests.QaPmo, "pmo")).Success, "the proposer cannot agree");
            Assert.IsFalse((await Step(PackageP9AuditSeatTests.Ceo1, "ceo")).Success, "not yet the CEO turn");
            Assert.IsTrue((await Step(PackageP9AuditSeatTests.Pmo1, "pmo")).Success);
            var ceo = await Step(PackageP9AuditSeatTests.Ceo1, "ceo");
            Assert.IsTrue(ceo.Success, ceo.Message);
            Assert.IsTrue(ceo.Data.Valid);
            var run = await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(0, Findings(host, "AUD-INT-002").Count, "accepted: no finding");
            Assert.AreEqual(1, run.Data.Rules.Single(r => r.Code == "AUD-INT-002").Accepted);
            host.Write(u => { u.Set<AusAcceptance>().Single().ExpiresOn = DateTime.Today.AddDays(-1); u.SaveChanges(); });
            await PackageP9AuditSeatTests.RunNow(host, PackageP9AuditSeatTests.Qa1);
            Assert.AreEqual(1, Findings(host, "AUD-INT-002").Count, "after the expiry it is a finding again");
        }

        [TestMethod]
        public async Task A_Report_Ends_At_The_Previous_Midnight_Goes_To_All_Three_And_Missed_Days_Are_Caught_Up()
        {
            AppHost host = NewHost();
            var one = await host.As(PackageP9AuditSeatTests.Qa1, p => p.GetRequiredService<IAusReportService>().GenerateAsync(new AusReportGenerateDto { Kind = 1, Day = DateTime.Today.AddDays(-3) }, Ct));
            Assert.IsTrue(one.Success, one.Message);
            Assert.AreEqual("CEO,PMO,QA", one.Data.Recipients);
            Assert.AreEqual(AusEvidence.Day(DateTime.Today.AddDays(-4)), one.Data.PeriodToText);
            var again = await host.As(PackageP9AuditSeatTests.Qa1, p => p.GetRequiredService<IAusReportService>().GenerateAsync(new AusReportGenerateDto { Kind = 1, Day = DateTime.Today.AddDays(-3) }, Ct));
            Assert.AreEqual(one.Data.Id, again.Data.Id, "the same period gives the stored report");
            await host.As(PackageP9AuditSeatTests.Qa1, p => p.GetRequiredService<IAusReportService>().GenerateDueAsync(DateTime.Today, false, Ct));
            Assert.AreEqual(4, host.Read(u => u.Set<AusReport>().Count(r => r.Kind == AusReportKind.Daily)), "the three missed days were made");
            Assert.IsTrue(host.Read(u => u.Set<AusReport>().Where(r => r.Kind == AusReportKind.Daily).ToList()).All(r => r.PeriodTo < DateTime.Today));
        }

        [TestMethod]
        public async Task A_Void_Request_Is_Withdrawn_Only_By_Its_Own_Requester_And_Long_Input_Is_Refused_Not_Crashed()
        {
            AppHost host = NewHost();
            int id = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, owner: 308);
            Assert.IsTrue((await host.As(PackageP9AuditSeatTests.Qa1, p => p.GetRequiredService<IAusFindingService>().StepAsync(new AusStepDto { FindingId = id, Step = "void-qa", Note = "این یافته از اساس انحراف نبوده است" }, Ct))).Success);
            var other = await host.As(PackageP9AuditSeatTests.Qa2, p => p.GetRequiredService<IAusFindingService>().StepAsync(new AusStepDto { FindingId = id, Step = "void-qa", Refuse = true, Note = "دلیل رد کافی است" }, Ct));
            Assert.IsFalse(other.Success, "another QA cannot clear the request");
            var own = await host.As(PackageP9AuditSeatTests.Qa1, p => p.GetRequiredService<IAusFindingService>().StepAsync(new AusStepDto { FindingId = id, Step = "void-qa", Refuse = true, Note = "دلیل رد کافی است" }, Ct));
            Assert.IsTrue(own.Success, own.Message);
            var longAction = await host.As(PackageP9AuditSeatTests.Owner, p => p.GetRequiredService<IAusFindingService>().ProposeActionAsync(new AusActionDto { FindingId = id, RootCause = new string('x', 5000), Correction = "اصلاح کافی است", CorrectiveAction = "اقدام اصلاحی کافی" }, Ct));
            Assert.IsFalse(longAction.Success);
        }

        [TestMethod]
        public async Task A_Role_That_Only_Owns_Findings_Sees_Exactly_Its_Own_And_Never_Those_Of_Others()
        {
            AppHost host = NewHost();
            int mine = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, owner: 313);       // the WAREHOUSE user owns this one
            int others = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, owner: 308);
            var list = await host.As(PackageP9AuditSeatTests.Stranger, p => p.GetRequiredService<IAusFindingService>().GetAsync(new AusFindingQuery(), Ct));
            Assert.IsTrue(list.Success);
            CollectionAssert.AreEqual(new[] { mine }, list.Data.Select(x => x.Id).ToArray());
            Assert.IsFalse((await host.As(PackageP9AuditSeatTests.Stranger, p => p.GetRequiredService<IAusFindingService>().GetOneAsync(others, Ct))).Success, "the detail of another owner's finding is refused");
            Assert.IsTrue((await host.As(PackageP9AuditSeatTests.Stranger, p => p.GetRequiredService<IAusFindingService>().GetOneAsync(mine, Ct))).Success);
            var none = PackageP9AuditSeatTests.NewHost();
            PackageP9AuditSeatTests.SeedFinding(none, AusFindingStatus.Open, owner: 308);
            var empty = await none.As(PackageP9AuditSeatTests.Stranger, p => p.GetRequiredService<IAusFindingService>().GetAsync(new AusFindingQuery(), Ct));
            Assert.AreEqual(0, empty.Data.Count, "an owner role without own findings sees an empty list");
            var nobody = AppHost.Principal(399, "p9.nobody", "SALES");
            Assert.IsFalse((await none.As(nobody, p => p.GetRequiredService<IAusFindingService>().GetAsync(new AusFindingQuery(), Ct))).Success, "a role outside the audit roles is refused");
        }
    }
}
