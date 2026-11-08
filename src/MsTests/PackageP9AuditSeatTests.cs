using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.AuditSeat;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>
    /// Package 9 (AI internal auditor, CEO decisions D11 and D12): the catalogue, the run, the findings and their closure chain (QA, PMO, CEO: three distinct persons, never the owner, the actioner or an AI),
    /// recurrence, the read-only role of the seat, the reports, the programme and the process audits. The rules themselves are in <see cref="PackageP9RuleTests"/>.
    /// </summary>
    [TestClass]
    public class PackageP9AuditSeatTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        internal static readonly ClaimsPrincipal Auditor = AppHost.Principal(301, "p9.auditor", "AUDITOR");
        internal static readonly ClaimsPrincipal Qa1 = AppHost.Principal(302, "p9.qa1", "QA");
        internal static readonly ClaimsPrincipal Qa2 = AppHost.Principal(303, "p9.qa2", "QA");
        internal static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(304, "p9.pmo1", "PMO");
        internal static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(305, "p9.pmo2", "PMO");
        internal static readonly ClaimsPrincipal Ceo1 = AppHost.Principal(306, "p9.ceo1", "CEO");
        internal static readonly ClaimsPrincipal Ceo2 = AppHost.Principal(307, "p9.ceo2", "CEO");
        internal static readonly ClaimsPrincipal Owner = AppHost.Principal(308, "p9.owner", "ENGINEERING");
        internal static readonly ClaimsPrincipal QaPmo = AppHost.Principal(309, "p9.qapmo", "QA", "PMO");
        internal static readonly ClaimsPrincipal QaCeo = AppHost.Principal(310, "p9.qaceo", "QA", "CEO");
        internal static readonly ClaimsPrincipal Auditee = AppHost.Principal(311, "p9.auditee", "QC");
        internal static readonly ClaimsPrincipal Auditor2 = AppHost.Principal(312, "p9.auditor2", "AUDITOR");
        internal static readonly ClaimsPrincipal Stranger = AppHost.Principal(313, "p9.stranger", "WAREHOUSE");

        internal static AppHost NewHost()
        {
            var host = new AppHost("p9_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313 }) AppHost.Add(u, new User { Id = id, UserName = "p9.user" + id, Email = "p9.user" + id + "@example.com", IsActive = true });
                int Role(string n) => u.Set<Role>().Single(x => x.Name == n).Id;
                foreach (var (uid, role) in new[] { (301, "AUDITOR"), (312, "AUDITOR"), (302, "QA"), (303, "QA"), (309, "QA"), (310, "QA"), (304, "PMO"), (305, "PMO"), (309, "PMO"), (306, "CEO"), (307, "CEO"), (310, "CEO") })
                    u.Set<UserRole>().Add(new UserRole { UserId = uid, RoleId = Role(role) });
                u.SaveChanges();
            });
            return host;
        }

        private static Task<T> As<T>(AppHost host, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => host.As(who, call);
        private static IAusFindingService Findings(IServiceProvider p) => p.GetRequiredService<IAusFindingService>();

        internal static int SeedFinding(AppHost host, AusFindingStatus status, int? owner = 308, int? actioner = null, string rule = "AUD-INT-002", AusSeverity severity = AusSeverity.Major, string key = null, Action<AusFinding> tweak = null)
        {
            int id = 0;
            host.Write(u =>
            {
                int seq = (u.Set<AusFinding>().Select(f => (int?)f.Sequence).Max() ?? 0) + 1;
                AusRuleDef def = AusCatalog.Find(rule);
                var f = AppHost.Add(u, new AusFinding
                {
                    Number = AusRules.Number(1405, seq), Year = 1405, Sequence = seq, Source = AusFindingSource.Rule, RuleCode = rule, Standard = def.Standard, Clause = def.Clause, Procedure = def.Procedure, ProcessCode = def.ProcessCode,
                    Severity = severity, BaseSeverity = severity, Title = def.Title, Description = "seed", ObjectType = "Seed", ObjectId = seq, ObjectKey = key ?? ("Seed|" + seq), EvidenceJson = "{}", EvidenceSha256 = "00", OwnerUserId = owner, OwnerRole = "ENGINEERING",
                    DueDate = DateTime.Today.AddDays(7), DetectedAt = DateTimeOffset.Now.AddDays(-2), LastSeenAt = DateTimeOffset.Now, SeenCount = 1, Status = status, ActionByUserId = actioner,
                });
                if (status >= AusFindingStatus.Rechecked && status != AusFindingStatus.Voided) { f.RecheckAt = DateTimeOffset.Now; f.RecheckPassed = true; f.RecheckSha256 = "ab"; f.RootCause = "x"; f.Correction = "x"; f.CorrectiveAction = "x"; }
                if (status >= AusFindingStatus.QaProposed) { f.QaUserId = 302; f.QaAt = DateTimeOffset.Now; }
                if (status >= AusFindingStatus.PmoProposed) { f.PmoUserId = 304; f.PmoAt = DateTimeOffset.Now; }
                tweak?.Invoke(f);
                u.SaveChanges();
                id = f.Id;
            });
            return id;
        }

        internal static AusFinding Get(AppHost host, int id) => host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(f => f.Id == id));
        private static List<AusFindingEvent> Events(AppHost host, int id) => host.Read(u => u.Set<AusFindingEvent>().AsNoTracking().Where(e => e.FindingId == id).OrderBy(e => e.Id).ToList());
        private static Task<ResponseDto<AusFindingDto>> Step(AppHost host, ClaimsPrincipal who, int id, string step, string note = "یادداشت کافی برای این مرحله", bool refuse = false) =>
            As(host, who, p => Findings(p).StepAsync(new AusStepDto { FindingId = id, Step = step, Note = note, Refuse = refuse }, Ct));

        // ------------------------------------------------------------------------------------------ catalogue

        [TestMethod]
        public void Catalogue_Has_At_Least_Forty_Rules_With_Complete_Metadata_And_Unique_Codes()
        {
            IReadOnlyList<AusRuleDef> all = AusCatalog.All;
            Assert.IsTrue(all.Count >= 40, "rules: " + all.Count);
            Assert.AreEqual(all.Count, all.Select(r => r.Code).Distinct().Count());
            foreach (AusRuleDef r in all)
            {
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(r.Code, "^AUD-[A-Z]{2,3}-\\d{3}$"), r.Code);
                Assert.IsTrue(r.Title.Any(ch => ch >= '؀' && ch <= 'ۿ'), r.Code + " title is Persian");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Standard), r.Code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Clause), r.Code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Procedure), r.Code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.QuerySummary), r.Code);
                Assert.IsNotNull(AusProcesses.Find(r.ProcessCode), r.Code + " process");
                Assert.IsTrue(Enum.IsDefined(typeof(AusSeverity), r.Severity), r.Code);
                Assert.IsNotNull(r.Query, r.Code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.ResolveOwnerRole()), r.Code + " owner role");
            }
            Assert.IsTrue(AusProcesses.All.All(p => all.Any(r => r.ProcessCode == p.Code)), "every process has a rule");
        }

        [TestMethod]
        public void Every_Rule_Switch_And_Setting_Is_A_Registered_Safety_Setting_With_Bounds_And_A_Default()
        {
            foreach (AusRuleDef r in AusCatalog.All)
            {
                string key = AusSettingKeys.RuleKey(r.Code);
                SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, key);
                Assert.IsNotNull(spec, key);
                Assert.AreEqual(SafetyKind.FlagMustBeOn, spec.Kind, key);
                Assert.IsTrue(AusSettingKeys.TryGetBounds(key, out int min, out int max) && min == 0 && max == 1, key);
                Assert.IsTrue(QualitySettingKeys.Defaults().Any(d => d.Key == key && d.Value == 1), key);
            }
            foreach (var s in AusSettingKeys.Strict())
            {
                Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, s.Key), s.Key);
                Assert.IsTrue(QualitySettingKeys.Defaults().Any(d => d.Key == s.Key), s.Key);
            }
            // the ★ defaults of the brief
            Assert.AreEqual(7m, AusSettingKeys.DefaultOf(AusSettingKeys.DueMajor));
            Assert.AreEqual(30m, AusSettingKeys.DefaultOf(AusSettingKeys.DueMinor));
            Assert.AreEqual(60m, AusSettingKeys.DefaultOf(AusSettingKeys.DueObservation));
            Assert.AreEqual(90m, AusSettingKeys.DefaultOf(AusSettingKeys.RecurrenceWindow));
            Assert.AreEqual(35m, AusSettingKeys.DefaultOf(AusSettingKeys.RestoreTestDays));
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, AusSettingKeys.Master));
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.AiSeat, "AUDIT"), "switching the seat off is a two-person change (control seat)");
            Assert.IsFalse(SafetyProfile.IsDraftingSeat("AUDIT"));
        }

        [TestMethod]
        public async Task A_Rule_Disable_Or_Longer_Due_Days_Is_Refused_By_The_Quality_Setting_Route_And_Needs_The_Two_Person_Route()
        {
            AppHost host = NewHost();
            string key = AusSettingKeys.RuleKey("AUD-INT-002");
            var direct = await As(host, Pmo1, p => p.GetRequiredService<IQualityDashboardService>().SaveSettingAsync(new SaveQualitySettingDto { Key = key, Value = 0 }, Ct));
            Assert.IsFalse(direct.Success, "a safety key is not saved directly");
            var due = await As(host, Pmo1, p => p.GetRequiredService<IQualityDashboardService>().SaveSettingAsync(new SaveQualitySettingDto { Key = AusSettingKeys.DueMajor, Value = 20 }, Ct));
            Assert.IsFalse(due.Success);
            // the deviation shows in the strict profile when it is stored
            host.Write(u => { AppHost.Add(u, new QualitySetting { Key = key, Value = 0, Title = "t", Source = "t" }); u.SaveChanges(); });
            var dev = await As(host, Auditor, p => p.GetRequiredService<ISafetySettingsService>().GetDeviationsAsync(Ct));
            Assert.IsTrue(dev.Data.Items.Any(i => i.Key == key && i.Deviates && i.Weaker));
        }

        // ------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void Severity_Ladder_Due_Dates_Numbers_And_Keys_Are_Pure_And_Exact()
        {
            Assert.AreEqual(AusSeverity.Minor, AusRules.Raise(AusSeverity.Observation));
            Assert.AreEqual(AusSeverity.Major, AusRules.Raise(AusSeverity.Minor));
            Assert.AreEqual(AusSeverity.Major, AusRules.Raise(AusSeverity.Major));
            var st = new Dictionary<string, decimal>();
            DateTime d = new DateTime(2026, 5, 1, 15, 0, 0);
            Assert.AreEqual(d.Date.AddDays(7), AusRules.DueDate(d, AusSeverity.Major, st));
            Assert.AreEqual(d.Date.AddDays(30), AusRules.DueDate(d, AusSeverity.Minor, st));
            Assert.AreEqual(d.Date.AddDays(60), AusRules.DueDate(d, AusSeverity.Observation, st));
            st[AusSettingKeys.DueMajor] = 3;
            Assert.AreEqual(d.Date.AddDays(3), AusRules.DueDate(d, AusSeverity.Major, st));
            Assert.AreEqual("AF-1405-0042", AusRules.Number(1405, 42));
            Assert.AreEqual("A|5|x", AusRules.ObjectKey("A", 5, "x"));
            string longKey = AusRules.ObjectKey("A", 5, new string('k', 400));
            Assert.IsTrue(longKey.Length <= 200);
            Assert.AreEqual(longKey, AusRules.ObjectKey("A", 5, new string('k', 400)), "stable");
            Assert.AreNotEqual(longKey, AusRules.ObjectKey("A", 5, new string('k', 399) + "z"));
            foreach (AusFindingStatus s in Enum.GetValues(typeof(AusFindingStatus))) Assert.IsFalse(string.IsNullOrWhiteSpace(AusRules.StatusText(s)));
        }

        // ------------------------------------------------------------------------------------------ the run

        [TestMethod]
        public async Task An_On_Demand_Run_On_An_Empty_Database_Gives_A_Known_Finding_With_Evidence_And_Hash_And_No_Rule_Fails()
        {
            AppHost host = NewHost();
            var res = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(0, res.Data.RulesFailed, string.Join("; ", res.Data.Rules.Where(r => r.Status != "ok" && r.Status != "disabled").Select(r => r.Code + "=" + r.Status)));
            Assert.AreEqual(AusCatalog.All.Count, res.Data.RulesRun);
            AusFinding f = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-INT-002" && x.ObjectKey == "RestoreTestResult|0|never"));
            Assert.AreEqual(AusSeverity.Major, f.Severity);
            Assert.AreEqual(AusFindingStatus.Open, f.Status);
            Assert.AreEqual(DateTime.Today.AddDays(7), f.DueDate);
            Assert.AreEqual("A.8.13", f.Clause);
            Assert.AreEqual("INTEGRITY", f.ProcessCode);
            Assert.AreEqual(AusEvidence.Sha256Hex(f.EvidenceJson), f.EvidenceSha256);
            StringAssert.Contains(f.EvidenceJson, "\"rule\":\"AUD-INT-002\"");
            Assert.IsTrue(f.Number.StartsWith("AF-" + AusRules.PersianYear(DateTime.Now) + "-"));
            Assert.AreEqual("detected", Events(host, f.Id).Single().Kind);
            // the minor and observation levels
            AusFinding minor = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-MR-001"));
            Assert.AreEqual(AusSeverity.Minor, minor.Severity);
            Assert.AreEqual(DateTime.Today.AddDays(30), minor.DueDate);
            // every number is unique and sequential
            List<int> seqs = host.Read(u => u.Set<AusFinding>().OrderBy(x => x.Sequence).Select(x => x.Sequence).ToList());
            CollectionAssert.AreEqual(Enumerable.Range(1, seqs.Count).ToList(), seqs);
            // a rule state row for every rule
            Assert.AreEqual(AusCatalog.All.Count, host.Read(u => u.Set<AusRuleState>().Count()));
        }

        [TestMethod]
        public async Task A_Second_Run_Sees_The_Same_Findings_Again_And_Creates_Nothing_New_And_Closes_Nothing_Even_When_The_Condition_Is_Gone()
        {
            AppHost host = NewHost();
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            int count = host.Read(u => u.Set<AusFinding>().Count());
            var second = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.AreEqual(0, second.Data.NewFindings);
            Assert.AreEqual(count, host.Read(u => u.Set<AusFinding>().Count()));
            Assert.AreEqual(2, host.Read(u => u.Set<AusFinding>().Single(f => f.RuleCode == "AUD-INT-002").SeenCount));
            // the owner fixes the data: a successful restore test now exists; the finding stays Open (no auto-close, zero tolerance)
            host.Write(u => { AppHost.Add(u, new RestoreTestResult { RunId = "r1", StartedAt = DateTimeOffset.Now, Success = true, BackupFile = "b.bak", BackupTakenAt = DateTimeOffset.Now }); u.SaveChanges(); });
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.AreEqual(AusFindingStatus.Open, host.Read(u => u.Set<AusFinding>().Single(f => f.RuleCode == "AUD-INT-002").Status));
            Assert.AreEqual(count, host.Read(u => u.Set<AusFinding>().Count()), "nothing is deleted");
        }

        [TestMethod]
        public async Task The_Run_Is_Refused_For_Other_Roles_And_For_An_Ai_Seat_And_The_Master_Switch_Stops_It()
        {
            AppHost host = NewHost();
            Assert.IsFalse((await As(host, Stranger, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct))).Success);
            var seat = new AiActingUser { UserId = 99, UserName = "seat.audit", Roles = new[] { "AUDITOR" } };
            var viaAi = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "AUDIT", sp => sp.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.IsFalse(viaAi.Success, "the on-demand endpoint is for people; the seat runs through its job");
            host.Write(u => { AppHost.Add(u, new QualitySetting { Key = AusSettingKeys.Master, Value = 0, Title = "t", Source = "t" }); u.SaveChanges(); });
            var off = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.AreEqual("Disabled", off.Data.Status);
            Assert.AreEqual(0, host.Read(u => u.Set<AusFinding>().Count()));
        }

        [TestMethod]
        public async Task A_Disabled_Rule_Does_Not_Run_And_Its_Existing_Findings_Stay()
        {
            AppHost host = NewHost();
            host.Write(u => { AppHost.Add(u, new QualitySetting { Key = AusSettingKeys.RuleKey("AUD-MR-001"), Value = 0, Title = "t", Source = "t" }); u.SaveChanges(); });
            var run = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.AreEqual("disabled", run.Data.Rules.Single(r => r.Code == "AUD-MR-001").Status);
            Assert.AreEqual(0, host.Read(u => u.Set<AusFinding>().Count(f => f.RuleCode == "AUD-MR-001")));
            Assert.AreEqual(AusCatalog.All.Count - 1, run.Data.RulesRun);
            var rules = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().GetRulesAsync(Ct));
            Assert.IsFalse(rules.Data.Single(r => r.Code == "AUD-MR-001").Enabled);
            Assert.IsTrue(rules.Data.Single(r => r.Code == "AUD-INT-002").Enabled);
        }

        [TestMethod]
        public async Task A_Major_Finding_Alerts_The_Owner_At_Once_And_The_Roles_Of_The_Closure_Chain()
        {
            AppHost host = NewHost();
            // a rule with a person as owner: a safety change decided by its own requester
            host.Write(u => { AppHost.Add(u, new SafetySettingChange { Store = SafetyStore.Quality, Key = "k", RequestedByUserId = 308, DecidedByUserId = 308, DecidedAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Approved, NewValue = "1", OldValue = "0", Reason = "r" }); u.SaveChanges(); });
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding f = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-MC-002"));
            Assert.AreEqual(308, f.OwnerUserId);
            Assert.AreEqual(AusSeverity.Major, f.Severity);
            List<OfInboxItem> items = host.Read(u => u.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "aus:" + f.Id).ToList());
            Assert.IsTrue(items.Any(i => i.RecipientUserId == 308), "the owner is told");
            foreach (string role in new[] { "QA", "PMO", "CEO" }) Assert.IsTrue(items.Any(i => i.RecipientRole == role), "role " + role + " is told");
            Assert.IsFalse(items.Any(i => i.RecipientRole == "AUDITOR"), "a plain auditor is not in the closure chain");
            Assert.IsTrue(items.All(i => i.Priority == OfUrgency.Immediate && i.Kind == OfInboxKind.Alert));
        }

        // ------------------------------------------------------------------------------------------ owner's action and the automatic re-check

        [TestMethod]
        public async Task The_Owner_Proposes_An_Action_The_Rule_Re_Runs_And_The_Finding_Goes_Back_To_Open_Until_The_Deviation_Is_Gone()
        {
            AppHost host = NewHost();
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            int id = host.Read(u => u.Set<AusFinding>().Single(f => f.RuleCode == "AUD-INT-002").Id);
            var action = new AusActionDto { FindingId = id, RootCause = "اسکریپت آزمون بازیابی اجرا نمی‌شد", Correction = "آزمون بازیابی دستی اجرا شد", CorrectiveAction = "زمان‌بندی ماهانه با هشدار" };
            // the owner of the INTEGRITY process is the PMO role (nobody is named): a QA person is not the owner
            Assert.IsFalse((await As(host, Stranger, p => Findings(p).ProposeActionAsync(action, Ct))).Success);
            Assert.IsFalse((await As(host, Pmo1, p => Findings(p).ProposeActionAsync(new AusActionDto { FindingId = id, RootCause = "کوتاه", Correction = action.Correction, CorrectiveAction = action.CorrectiveAction }, Ct))).Success, "root cause needs 10 characters");
            Assert.IsFalse((await As(host, Pmo1, p => Findings(p).ProposeActionAsync(new AusActionDto { FindingId = id, RootCause = action.RootCause, Correction = action.Correction, CorrectiveAction = action.CorrectiveAction, CapaId = 777 }, Ct))).Success, "unknown CAPA");

            var first = await As(host, Pmo1, p => Findings(p).ProposeActionAsync(action, Ct));
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual((int)AusFindingStatus.Open, first.Data.StatusValue, "the deviation is still there: the re-check failed");
            Assert.AreEqual(false, Get(host, id).RecheckPassed);
            CollectionAssert.AreEqual(new[] { "detected", "action", "recheck-fail" }, Events(host, id).Select(e => e.Kind).ToArray());

            host.Write(u => { AppHost.Add(u, new RestoreTestResult { RunId = "r2", StartedAt = DateTimeOffset.Now, Success = true, BackupFile = "b.bak", BackupTakenAt = DateTimeOffset.Now }); u.SaveChanges(); });
            var second = await As(host, Pmo1, p => Findings(p).ProposeActionAsync(action, Ct));
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual((int)AusFindingStatus.Rechecked, second.Data.StatusValue);
            AusFinding f = Get(host, id);
            Assert.AreEqual(true, f.RecheckPassed);
            Assert.AreEqual(304, f.ActionByUserId);
            Assert.IsFalse(string.IsNullOrEmpty(f.RecheckSha256));
        }

        [TestMethod]
        public async Task An_Ai_Seat_Cannot_Propose_An_Action_For_The_Owner()
        {
            AppHost host = NewHost();
            int id = SeedFinding(host, AusFindingStatus.Open, owner: 99);
            var seat = new AiActingUser { UserId = 99, UserName = "seat.audit", Roles = new[] { "AUDITOR", "ENGINEERING" } };
            var r = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "AUDIT", sp => sp.GetRequiredService<IAusFindingService>().ProposeActionAsync(new AusActionDto { FindingId = id, RootCause = "ریشه علت کافی", Correction = "اصلاح کافی است", CorrectiveAction = "اقدام اصلاحی کافی" }, Ct));
            Assert.IsFalse(r.Success);
            Assert.AreEqual(AusFindingStatus.Open, Get(host, id).Status);
        }

        // ------------------------------------------------------------------------------------------ the closure chain

        [TestMethod]
        public async Task The_Full_Closure_Chain_Qa_Then_Pmo_Then_Ceo_Three_Distinct_Persons_Closes_And_Records_Everybody()
        {
            AppHost host = NewHost();
            int id = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308, actioner: 308);
            Assert.AreEqual((int)AusFindingStatus.QaProposed, (await Step(host, Qa1, id, "qa")).Data.StatusValue);
            Assert.AreEqual((int)AusFindingStatus.PmoProposed, (await Step(host, Pmo1, id, "pmo")).Data.StatusValue);
            var done = await Step(host, Ceo1, id, "ceo");
            Assert.IsTrue(done.Success, done.Message);
            AusFinding f = Get(host, id);
            Assert.AreEqual(AusFindingStatus.Closed, f.Status);
            Assert.AreEqual(302, f.QaUserId); Assert.AreEqual(304, f.PmoUserId); Assert.AreEqual(306, f.CeoUserId);
            Assert.IsNotNull(f.ClosedAt);
            CollectionAssert.AreEqual(new[] { "qa", "pmo", "ceo" }, Events(host, id).Select(e => e.Kind).ToArray());
            // a closed finding cannot be touched again
            Assert.IsFalse((await Step(host, Qa2, id, "qa")).Success);
        }

        [TestMethod]
        public async Task The_Owner_The_Actioner_And_An_Ai_Can_Sign_No_Step()
        {
            AppHost host = NewHost();
            // the owner of the finding is the QA user 302
            int a = SeedFinding(host, AusFindingStatus.Rechecked, owner: 302);
            var byOwner = await Step(host, Qa1, a, "qa");
            Assert.IsFalse(byOwner.Success);
            StringAssert.Contains(byOwner.Message, "مسئول");
            // the person who proposed the action is the PMO user 304
            int b = SeedFinding(host, AusFindingStatus.QaProposed, owner: 308, actioner: 304);
            var byActioner = await Step(host, Pmo1, b, "pmo");
            Assert.IsFalse(byActioner.Success);
            StringAssert.Contains(byActioner.Message, "اقدام");
            // an actioner of an earlier round (event) is refused too
            int c = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308);
            host.Write(u => { u.Set<AusFindingEvent>().Add(new AusFindingEvent { FindingId = c, At = DateTimeOffset.Now, Kind = "action", UserId = 303, FromStatus = AusFindingStatus.Open, ToStatus = AusFindingStatus.ActionProposed, Note = "n" }); u.SaveChanges(); });
            Assert.IsFalse((await Step(host, Qa2, c, "qa")).Success, "an earlier actioner");
            // an AI seat, even with the QA role
            int d = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308);
            var seat = new AiActingUser { UserId = 99, UserName = "seat.audit", Roles = new[] { "QA", "PMO", "CEO" } };
            var viaAi = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "AUDIT", sp => sp.GetRequiredService<IAusFindingService>().StepAsync(new AusStepDto { FindingId = d, Step = "qa", Note = "یادداشت کافی" }, Ct));
            Assert.IsFalse(viaAi.Success);
            StringAssert.Contains(viaAi.Message, "هوش مصنوعی");
            Assert.AreEqual(AusFindingStatus.Rechecked, Get(host, d).Status);
        }

        [TestMethod]
        public async Task The_Same_Person_Cannot_Sign_Twice_Qa_Pmo_And_Ceo_Must_Be_Three_Different_Persons()
        {
            AppHost host = NewHost();
            int id = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308);
            Assert.IsTrue((await Step(host, QaPmo, id, "qa")).Success);
            var pmoSame = await Step(host, QaPmo, id, "pmo");
            Assert.IsFalse(pmoSame.Success);
            StringAssert.Contains(pmoSame.Message, "دو نفر متفاوت");
            Assert.AreEqual(AusFindingStatus.QaProposed, Get(host, id).Status);
            Assert.IsTrue((await Step(host, Pmo1, id, "pmo")).Success);
            // the CEO is also a QA person who signed the first step
            int id2 = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308);
            Assert.IsTrue((await Step(host, QaCeo, id2, "qa")).Success);
            Assert.IsTrue((await Step(host, Pmo2, id2, "pmo")).Success);
            var ceoSame = await Step(host, QaCeo, id2, "ceo");
            Assert.IsFalse(ceoSame.Success);
            StringAssert.Contains(ceoSame.Message, "نفر سوم");
            Assert.AreEqual(AusFindingStatus.PmoProposed, Get(host, id2).Status);
            Assert.IsTrue((await Step(host, Ceo2, id2, "ceo")).Success);
        }

        [TestMethod]
        public async Task Closure_Needs_A_Passed_Automatic_Re_Check_And_The_Steps_Keep_Their_Order_And_Roles()
        {
            AppHost host = NewHost();
            foreach (AusFindingStatus s in new[] { AusFindingStatus.Open, AusFindingStatus.ActionProposed })
            {
                int id = SeedFinding(host, s, owner: 308);
                var r = await Step(host, Qa1, id, "qa");
                Assert.IsFalse(r.Success, s.ToString());
                StringAssert.Contains(r.Message, "بازبینی");
                Assert.AreEqual(s, Get(host, id).Status);
            }
            // Rechecked but failed is not enough
            int failed = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308, tweak: f => f.RecheckPassed = false);
            Assert.IsFalse((await Step(host, Qa1, failed, "qa")).Success);
            // order and roles
            int rc = SeedFinding(host, AusFindingStatus.Rechecked, owner: 308);
            Assert.IsFalse((await Step(host, Pmo1, rc, "pmo")).Success, "PMO before QA");
            Assert.IsFalse((await Step(host, Ceo1, rc, "ceo")).Success, "CEO before QA and PMO");
            Assert.IsFalse((await Step(host, Pmo1, rc, "qa")).Success, "a PMO user is not QA");
            Assert.IsFalse((await Step(host, Qa1, rc, "pmo")).Success, "a QA user is not PMO");
            Assert.IsFalse((await Step(host, Stranger, rc, "qa")).Success);
            Assert.IsFalse((await Step(host, Auditor, rc, "qa")).Success, "the auditor role closes nothing");
            Assert.IsFalse((await Step(host, Qa1, rc, "whatever")).Success);
        }

        [TestMethod]
        public async Task A_Refusal_At_Any_Step_Returns_The_Finding_To_Open_With_The_Reason_And_Clears_The_Chain()
        {
            AppHost host = NewHost();
            foreach (var (status, who, step) in new[] { (AusFindingStatus.Rechecked, Qa1, "qa"), (AusFindingStatus.QaProposed, Pmo1, "pmo"), (AusFindingStatus.PmoProposed, Ceo1, "ceo") })
            {
                int id = SeedFinding(host, status, owner: 308);
                Assert.IsFalse((await Step(host, who, id, step, "کوتاه", refuse: true)).Success, "the reason needs 10 characters");
                var r = await Step(host, who, id, step, "شواهد اقدام اصلاحی کافی نیست", refuse: true);
                Assert.IsTrue(r.Success, r.Message);
                AusFinding f = Get(host, id);
                Assert.AreEqual(AusFindingStatus.Open, f.Status, step);
                Assert.IsNull(f.QaUserId); Assert.IsNull(f.PmoUserId); Assert.IsNull(f.CeoUserId); Assert.IsNull(f.RecheckPassed);
                AusFindingEvent last = Events(host, id).Last();
                Assert.AreEqual("refused", last.Kind);
                StringAssert.Contains(last.Note, "شواهد اقدام اصلاحی کافی نیست");
            }
        }

        [TestMethod]
        public async Task A_Finding_That_Comes_Back_On_The_Way_To_Closure_Is_Reopened_By_The_Next_Run()
        {
            AppHost host = NewHost();
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            int id = host.Read(u => u.Set<AusFinding>().Single(f => f.RuleCode == "AUD-MR-001").Id);
            host.Write(u =>
            {
                AusFinding f = u.Set<AusFinding>().Single(x => x.Id == id);
                f.Status = AusFindingStatus.QaProposed; f.RecheckPassed = true; f.QaUserId = 302; f.QaAt = DateTimeOffset.Now;
                u.SaveChanges();
            });
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding after = Get(host, id);
            Assert.AreEqual(AusFindingStatus.Open, after.Status);
            Assert.IsNull(after.QaUserId);
            Assert.AreEqual("reopened", Events(host, id).Last().Kind);
        }

        [TestMethod]
        public async Task A_Void_Takes_Qa_Pmo_And_The_Ceo_With_A_Reason_And_A_Refusal_Clears_It_And_The_Row_Stays()
        {
            AppHost host = NewHost();
            int id = SeedFinding(host, AusFindingStatus.Open, owner: 308);
            Assert.IsFalse((await Step(host, Qa1, id, "void-qa", "کوتاه")).Success, "reason needs 15 characters");
            Assert.IsFalse((await Step(host, Pmo1, id, "void-pmo")).Success, "QA starts the void");
            Assert.IsTrue((await Step(host, Qa1, id, "void-qa", "این یافته از اساس انحراف نبوده است")).Success);
            Assert.IsFalse((await Step(host, Qa2, id, "void-pmo")).Success, "a QA user is not PMO");
            var sameAsQa = await Step(host, QaPmo, id, "void-pmo");
            Assert.IsTrue(sameAsQa.Success, "a different person with the PMO role");
            Assert.IsFalse((await Step(host, QaPmo, id, "void-ceo")).Success, "not the CEO role");
            AusFinding mid = Get(host, id);
            Assert.AreEqual(309, mid.VoidPmoUserId);
            Assert.AreEqual(AusFindingStatus.Open, mid.Status, "still open until the third person agrees");
            Assert.IsTrue((await Step(host, Ceo1, id, "void-ceo", "تایید ابطال")).Success);
            AusFinding f = Get(host, id);
            Assert.AreEqual(AusFindingStatus.Voided, f.Status);
            Assert.AreEqual(302, f.VoidQaUserId); Assert.AreEqual(309, f.VoidPmoUserId); Assert.AreEqual(306, f.VoidCeoUserId);
            Assert.IsNotNull(f.VoidedAt);
            Assert.IsFalse(f.Deleted);

            int id2 = SeedFinding(host, AusFindingStatus.Open, owner: 308);
            Assert.IsTrue((await Step(host, Qa1, id2, "void-qa", "ابطال بدون دلیل کافی نیست")).Success);
            Assert.IsTrue((await Step(host, Pmo1, id2, "void-pmo", "رد ابطال: شواهد ناکافی", refuse: true)).Success);
            AusFinding r = Get(host, id2);
            Assert.IsNull(r.VoidQaUserId); Assert.IsNull(r.VoidReason); Assert.AreEqual(AusFindingStatus.Open, r.Status);
            // the owner may not take part in the void
            int id3 = SeedFinding(host, AusFindingStatus.Open, owner: 302);
            Assert.IsFalse((await Step(host, Qa1, id3, "void-qa", "ابطال توسط مسئول یافته")).Success);
        }

        [TestMethod]
        public void The_Finding_Service_Has_No_Delete_Or_Automatic_Close_Operation()
        {
            string[] names = typeof(IAusFindingService).GetMethods().Select(m => m.Name).ToArray();
            foreach (string n in names) Assert.IsFalse(n.Contains("Delete") || n.Contains("Remove") || n.Contains("AutoClose") || n.Contains("Drop"), n);
            foreach (Type t in new[] { typeof(IAusRunService), typeof(IAusReportService), typeof(IAusProgramService), typeof(IAusAuditService) })
                foreach (MethodInfo m in t.GetMethods()) Assert.IsFalse(m.Name.Contains("Delete") || m.Name.Contains("Remove"), t.Name + "." + m.Name);
        }

        // ------------------------------------------------------------------------------------------ recurrence

        [TestMethod]
        public async Task A_Deviation_That_Returns_After_Closure_Creates_A_Linked_Finding_One_Level_Higher()
        {
            AppHost host = NewHost();
            // a closed minor finding of the same rule and the same object
            int old = SeedFinding(host, AusFindingStatus.Closed, owner: 308, rule: "AUD-MR-001", severity: AusSeverity.Minor, key: "ManagementReview|0|overdue", tweak: f => { f.ClosedAt = DateTimeOffset.Now.AddDays(-5); f.QaUserId = 302; f.PmoUserId = 304; f.CeoUserId = 306; });
            var run = await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding f2 = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-MR-001" && x.Id != old));
            Assert.AreEqual(old, f2.RecurrenceOfId);
            Assert.AreEqual(1, f2.RecurrenceCount);
            Assert.AreEqual(AusSeverity.Major, f2.Severity, "minor + recurrence = major");
            Assert.AreEqual(AusSeverity.Minor, f2.BaseSeverity);
            Assert.AreEqual(DateTime.Today.AddDays(7), f2.DueDate, "the due date follows the raised severity");
            Assert.IsTrue(run.Data.Recurrences >= 1);
            Assert.AreEqual(AusFindingStatus.Closed, Get(host, old).Status, "the closed record is never changed");
            StringAssert.Contains(Events(host, f2.Id).Single().Note, "تکرار");
            // the detail shows the link both ways
            var detail = await As(host, Qa1, p => Findings(p).GetOneAsync(f2.Id, Ct));
            Assert.AreEqual(old, detail.Data.Related.Single().Id);
        }

        [TestMethod]
        public async Task The_Same_Rule_On_Another_Object_Within_The_Window_Is_A_Recurrence_And_Outside_The_Window_It_Is_Not()
        {
            AppHost host = NewHost();
            int inside = SeedFinding(host, AusFindingStatus.Closed, rule: "AUD-AI-001", severity: AusSeverity.Observation, key: "AiMessage|999", tweak: f => f.ClosedAt = DateTimeOffset.Now.AddDays(-10));
            host.Write(u => { AppHost.Add(u, new AiMessage { ConversationId = 5, Role = AiMessageRole.Assistant, Verified = false, Text = "x", CreatedAt = DateTimeOffset.Now }); u.SaveChanges(); });
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding f = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-AI-001" && x.Id != inside));
            Assert.AreEqual(inside, f.RecurrenceOfId);
            Assert.AreEqual(AusSeverity.Minor, f.Severity, "observation + recurrence = minor");

            AppHost host2 = NewHost();
            int outside = SeedFinding(host2, AusFindingStatus.Closed, rule: "AUD-AI-001", severity: AusSeverity.Observation, key: "AiMessage|999", tweak: x => x.ClosedAt = DateTimeOffset.Now.AddDays(-200));
            host2.Write(u => { AppHost.Add(u, new AiMessage { ConversationId = 5, Role = AiMessageRole.Assistant, Verified = false, Text = "x", CreatedAt = DateTimeOffset.Now }); u.SaveChanges(); });
            await As(host2, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding g = host2.Read(u => u.Set<AusFinding>().AsNoTracking().Single(x => x.RuleCode == "AUD-AI-001" && x.Id != outside));
            Assert.IsNull(g.RecurrenceOfId);
            Assert.AreEqual(AusSeverity.Observation, g.Severity);
            Assert.AreEqual(DateTime.Today.AddDays(60), g.DueDate);
        }

        // ------------------------------------------------------------------------------------------ the seat is read-only

        [TestMethod]
        public void The_Audit_Seat_Is_Defined_With_The_Read_Only_Auditor_Role_And_No_Model()
        {
            AiSeatDefinition seat = AiSeatCatalog.Find("AUDIT");
            CollectionAssert.AreEqual(new[] { "AUDITOR" }, seat.Roles);
            Assert.IsFalse(seat.UsesModel);
            Assert.AreEqual(AuditorWriteGuard.SeatUserName, seat.UserName);
            Assert.IsNotNull(AiSchedule.Parse(seat.Schedule));
        }

        [TestMethod]
        public void No_Writing_Role_List_Contains_The_Auditor_Role_Only_Reading_Lists_Do()
        {
            // every constant of the services that names AUDITOR is a list of readers (or the audit module's own, which the closure chain and the services guard again)
            string[] readingNames = { "Readers", "ReadRoles", "DocReaders", "CommercialReaders", "Roles", "Procurement", "Warehouse", "Quality", "Finance", "Forecast", "SensitiveRoles", "Actioners", "Runners", "Auditor",
                "Names", "Prices", "PriceRoles", "ValueRoles", "DocumentRoles", "Auditors" };   // price and name visibility lists and the finance auditor (read-only by its own comment) are reading lists
            var offenders = new List<string>();
            foreach (Type t in typeof(IAusRunService).Assembly.GetTypes())
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(x => x.FieldType == typeof(string) || x.FieldType == typeof(string[])))
                {
                    string text = f.FieldType == typeof(string) ? f.GetValue(null) as string : string.Join(",", (f.GetValue(null) as string[]) ?? new string[0]);
                    if (text == null || !text.Split(',').Select(s => s.Trim()).Contains("AUDITOR")) continue;
                    if (!readingNames.Contains(f.Name) && !f.Name.EndsWith("Readers", StringComparison.Ordinal) && !f.Name.EndsWith("Read", StringComparison.Ordinal)) offenders.Add(t.FullName + "." + f.Name);
                }
            Assert.AreEqual(0, offenders.Count, string.Join(", ", offenders));
        }

        [TestMethod]
        public void No_Controller_Write_Action_Names_The_Auditor_Role_Except_The_Audit_Controller()
        {
            var offenders = new List<string>();
            foreach (Type t in typeof(WebApi.Controllers.AuditSeatController).Assembly.GetTypes().Where(x => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(x) && !x.IsAbstract))
            {
                if (t == typeof(WebApi.Controllers.AuditSeatController)) continue;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    bool write = m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.HttpPostAttribute || a is Microsoft.AspNetCore.Mvc.HttpPutAttribute || a is Microsoft.AspNetCore.Mvc.HttpDeleteAttribute || a is Microsoft.AspNetCore.Mvc.HttpPatchAttribute);
                    if (!write) continue;
                    // all the Authorize attributes (method and class) must pass, so the action is open to the auditor only when every list names it
                    var roles = m.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Concat(t.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()).Select(a => a.Roles).Where(r => !string.IsNullOrEmpty(r)).ToList();
                    if (roles.Count > 0 && roles.All(r => r.Split(',').Select(s => s.Trim()).Contains("AUDITOR"))) offenders.Add(t.Name + "." + m.Name);
                }
            }
            // the Sepidar outbox actions sit behind a class-level reader list that names the auditor and the service checks the writer roles again (proved in the next test);
            // SetDefault and SaveView only store the signed-in user's own preference (default legal entity, own saved view), not business data
            string[] legacy = { "SepidarOutboxController.GenerateAsync", "SepidarOutboxController.ActionAsync", "SepidarOutboxController.AckAsync", "LegalEntitiesController.SetDefaultAsync", "StatementsController.SaveViewAsync" };
            Assert.AreEqual(0, offenders.Except(legacy).Count(), string.Join(", ", offenders.Except(legacy)));
        }

        [TestMethod]
        public async Task The_Auditor_Role_Is_Refused_By_The_Services_Behind_The_Reader_Gates_And_By_The_Writing_Services_Of_The_Modules()
        {
            AppHost host = NewHost();
            var a = await As(host, Auditor, p => p.GetRequiredService<Services.NasbNiro.Finance.ISepidarOutboxService>().GenerateAsync(new ViewModels.NasbNiro.FinanceDtos.GenerateOutboxDto(), Ct));
            Assert.IsFalse(a.Success); StringAssert.Contains(a.Message, "مجوز");
            var d = await As(host, Auditor, p => p.GetRequiredService<IQualityDashboardService>().SaveSettingAsync(new SaveQualitySettingDto { Key = "NcrMinorDueDays", Value = 10 }, Ct));
            Assert.IsFalse(d.Success);
            var e = await As(host, Auditor, p => p.GetRequiredService<ISafetySettingsService>().RequestAsync(new ViewModels.NasbNiro.IntegrityDtos.SafetyRequestDto { Store = "Quality", Key = AusSettingKeys.RuleKey("AUD-INT-002"), Value = "0", Reason = "تلاش ممیز برای خاموش کردن قاعده" }, Ct));
            Assert.IsFalse(e.Success, "an auditor cannot even request the weakening of the audit");
        }

        [TestMethod]
        public async Task The_Audit_Seat_User_Cannot_Write_Any_Table_Outside_Its_Own_Findings_Runs_Reports_Logs_And_Notifications()
        {
            AppHost host = NewHost();
            var seat = new AiActingUser { UserId = 99, UserName = "seat.audit", Roles = new[] { "AUDITOR" } };
            var scopes = host.Provider.GetRequiredService<IServiceScopeFactory>();
            // a write to a table of another module is refused in the data layer
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => AiActing.RunAsync(scopes, seat, "AUDIT", async sp =>
            {
                var uow = sp.GetRequiredService<DataLayer.Context.IUnitOfWork>();
                AppHost.Add(uow, new RestoreTestResult { RunId = "x", StartedAt = DateTimeOffset.Now });
                await uow.SaveChangesAsync(Ct);
                return 1;
            }));
            StringAssert.Contains(ex.Message, "RestoreTestResult");
            Assert.AreEqual(0, host.Read(u => u.Set<RestoreTestResult>().Count()));
            // the same write by a person is fine, and the seat writes its own tables
            host.Write(u => { AppHost.Add(u, new RestoreTestResult { RunId = "y", StartedAt = DateTimeOffset.Now }); u.SaveChanges(); }, Pmo1);
            Assert.AreEqual(1, host.Read(u => u.Set<RestoreTestResult>().Count()));
            Assert.IsTrue(AuditorWriteGuard.Allowed(typeof(AusFinding)) && AuditorWriteGuard.Allowed(typeof(AiNote)) && AuditorWriteGuard.Allowed(typeof(OfInboxItem)));
            Assert.IsFalse(AuditorWriteGuard.Allowed(typeof(QualitySetting)) || AuditorWriteGuard.Allowed(typeof(User)));
        }

        // ------------------------------------------------------------------------------------------ the seat job

        [TestMethod]
        public async Task The_Seat_Job_Runs_Without_The_Model_Writes_Findings_And_The_Daily_Digest_And_Is_Idempotent()
        {
            AppHost host = NewHost();
            var seat = new AiActingUser { UserId = 99, UserName = "seat.audit", Roles = new[] { "AUDITOR" } };
            var scopes = host.Provider.GetRequiredService<IServiceScopeFactory>();
            Func<Task<AiSeatJobResult>> run = () => AiActing.RunAsync(scopes, seat, "AUDIT", sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == "AUDIT").RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));
            AiSeatJobResult r = await run();
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            Assert.IsTrue(host.Read(u => u.Set<AusFinding>().Count()) > 0);
            Assert.AreEqual(AusRunKind.Daily, host.Read(u => u.Set<AusRun>().Single().Kind));
            Assert.AreEqual(1, host.Read(u => u.Set<AusReport>().Count(x => x.Kind == AusReportKind.Daily)));
            Assert.AreEqual("template", host.Read(u => u.Set<AusReport>().First().NarrativeSource), "no model is configured: the template text");
            int findings = host.Read(u => u.Set<AusFinding>().Count());
            await run();
            Assert.AreEqual(findings, host.Read(u => u.Set<AusFinding>().Count()), "a second run finds nothing new");
            Assert.AreEqual(1, host.Read(u => u.Set<AusReport>().Count(x => x.Kind == AusReportKind.Daily)), "one digest per day");
            Assert.AreEqual(2, host.Read(u => u.Set<AusRun>().Count()));
        }

        [TestMethod]
        public async Task A_Rule_That_Fails_Or_Is_Truncated_Is_Reported_As_A_Finding_Of_The_Audit_Itself()
        {
            AppHost host = NewHost();
            // a hit ceiling of 100 (the lowest the setting allows) with 101 unverified AI answers
            host.Write(u =>
            {
                AppHost.Add(u, new QualitySetting { Key = AusSettingKeys.MaxHits, Value = 100, Title = "t", Source = "t" });
                for (int i = 0; i < 101; i++) AppHost.Add(u, new AiMessage { ConversationId = 1, Role = AiMessageRole.Assistant, Verified = false, Text = "x", CreatedAt = DateTimeOffset.Now });
                u.SaveChanges();
            });
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            Assert.AreEqual("truncated", host.Read(u => u.Set<AusRuleState>().Single(s => s.RuleCode == "AUD-AI-001").LastStatus));
            Assert.AreEqual(100, host.Read(u => u.Set<AusFinding>().Count(f => f.RuleCode == "AUD-AI-001")));
            await As(host, Qa1, p => p.GetRequiredService<IAusRunService>().RunNowAsync(Ct));
            AusFinding sys = host.Read(u => u.Set<AusFinding>().AsNoTracking().Single(f => f.RuleCode == "AUD-SYS-001" && f.ObjectKey.Contains("AUD-AI-001")));
            Assert.AreEqual(AusSeverity.Major, sys.Severity);
        }
    }
}
