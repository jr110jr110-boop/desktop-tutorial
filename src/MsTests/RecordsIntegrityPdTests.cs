using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.Procurement;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Package D: the quality records are in the audit chain, the chain is verified on schedule and on demand, the restore test result is read, and the strict production profile needs a second person.</summary>
    [TestClass]
    public class RecordsIntegrityPdTests
    {
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(50, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(51, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(37, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Qa = AppHost.Principal(60, "qa.one", "QA");
        private static readonly ClaimsPrincipal Store = AppHost.Principal(70, "wh.one", "WAREHOUSE");
        private static readonly ClaimsPrincipal Seat = AppHost.Principal(80, "seat.ceo-brief", "PMO");

        private static AppHost Host(IDictionary<string, string> extra = null)
        {
            var settings = new Dictionary<string, string> { ["Identity:AuditHmacKey"] = "pd-test-audit-key-not-a-secret" };
            if (extra != null) foreach (var kv in extra) settings[kv.Key] = kv.Value;
            return new AppHost("pd_" + Guid.NewGuid().ToString("N"), settings);
        }

        private static int SaveRecord(AppHost host, string number, QcResult result = QcResult.Accept)
        {
            int id = 0;
            host.Write(uow =>
            {
                var r = AppHost.Add(uow, new QcRecord { Number = number, Type = QcRecordType.InProcess, InspectorUserId = 60, Quantity = 1, Result = result, RecordDate = DateTime.Today });
                uow.SaveChanges();
                id = r.Id;
            }, Qa);
            return id;
        }

        private static List<FnAuditLog> Trail(AppHost host, string type, int? id = null) =>
            host.Read(uow => uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == type && (id == null || a.EntityId == id)).OrderBy(a => a.Id).ToList());

        private sealed class RawLogContext : DbContext
        {
            public RawLogContext(DbContextOptions options) : base(options) { }
            public DbSet<FnAuditLog> Logs { get; set; }
            protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<FnAuditLog>().ToTable("FnAuditLogs").HasKey(x => x.Id);
        }

        private static void Tamper(AppHost host, Action<RawLogContext> change)
        {
            using (var raw = new RawLogContext(host.RawOptions<RawLogContext>())) { change(raw); raw.SaveChanges(); }
        }

        // ------------------------------------------------------------------------------------------ the audited map

        [TestMethod]
        public void A_Quality_Record_Is_Audited_When_It_Is_Added_And_When_An_Audited_Field_Changes()
        {
            AppHost host = Host();
            int id = SaveRecord(host, "QIR-1405-00001");
            // a new row has no id yet when the entry is written: its summary is the record number
            List<FnAuditLog> added = Trail(host, nameof(QcRecord)).Where(a => a.Summary.EndsWith("QIR-1405-00001")).ToList();
            Assert.AreEqual(1, added.Count);
            Assert.AreEqual("Added", added[0].Action);
            Assert.AreEqual(60, added[0].UserId);
            Assert.IsNotNull(added[0].Seq, "the entry is in the chain");

            host.Write(uow => { uow.Set<QcRecord>().First(r => r.Id == id).Result = QcResult.Reject; uow.SaveChanges(); }, Qa);
            List<FnAuditLog> all = Trail(host, nameof(QcRecord));
            Assert.AreEqual(2, all.Count, "the change wrote exactly one more entry");
            Assert.AreEqual("Modified", all[1].Action);
            Assert.AreEqual(id, all[1].EntityId);
            StringAssert.Contains(all[1].BeforeJson, "Result");
            StringAssert.Contains(all[1].AfterJson, "Result");

            host.Write(uow => { uow.Set<QcRecord>().First(r => r.Id == id).Result = QcResult.Reject; uow.SaveChanges(); }, Qa);
            Assert.AreEqual(2, Trail(host, nameof(QcRecord)).Count, "a save without a change writes nothing");
        }

        [TestMethod]
        public void Every_New_Audited_Type_Has_A_Persian_Label()
        {
            foreach (Type type in new[] { typeof(QcRecord), typeof(QcMeasurement), typeof(TestReport), typeof(MaterialCertificate), typeof(CalibrationEvent), typeof(MeasuringEquipment), typeof(Qualification), typeof(Wps), typeof(Pqr), typeof(WeldLog),
                typeof(Audit), typeof(AuditFinding), typeof(ManagementReview), typeof(ControlledDocument), typeof(Entities.Model.NasbNiro.Procurement.SupplierEvaluation), typeof(Entities.Model.NasbNiro.Inventory.InventoryLot), typeof(Entities.Model.NasbNiro.Inventory.SerialUnit),
                typeof(Entities.Model.NasbNiro.Inventory.GenealogyLink), typeof(Entities.Model.NasbNiro.Inventory.InventoryDocument), typeof(SafetySettingChange), typeof(RestoreTestResult) })
            {
                string label = AuditEntityLabels.Of(type.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), type.Name + " has no label");
                Assert.IsTrue(label.Any(c => c >= '؀' && c <= 'ۿ'), type.Name + " label is not Persian: " + label);
            }
        }

        [TestMethod]
        public async Task A_Direct_Edit_Of_A_Quality_Entry_Is_Found_By_The_Verification_And_Named()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-00002");
            SaveRecord(host, "QIR-1405-00003");
            AuditChainVerifyDto before = FinanceTestSupport.Ok(await host.As(FinanceTestSupport.Auditor, sp => sp.GetRequiredService<IAuditChainService>().VerifyAsync(null, null, null, null, CancellationToken.None)));
            Assert.IsTrue(before.Ok, before.Message);
            Assert.AreEqual(2, before.Checked);

            // somebody edits the stored entry of the record by SQL: the result "accept" is made to look like it was never "reject"
            Tamper(host, raw => raw.Logs.Single(r => r.EntityType == nameof(QcRecord) && r.Summary.EndsWith("QIR-1405-00002")).AfterJson = "{\"Result\":\"Reject\"}");
            AuditChainVerifyDto after = FinanceTestSupport.Ok(await host.As(FinanceTestSupport.Auditor, sp => sp.GetRequiredService<IAuditChainService>().VerifyAsync(null, null, null, null, CancellationToken.None)));
            Assert.IsFalse(after.Ok);
            Assert.AreEqual(nameof(QcRecord), after.FirstBreak.EntityType);
            Assert.AreEqual(1L, after.FirstBreak.Seq);
        }

        // ------------------------------------------------------------------------------------------ the verification job

        /// <summary>A full pass is queued to the background job (M8): the call answers Running, the test waits until it is done.</summary>
        private static async Task<AuditChainRunDto> RunFull(AppHost host, ClaimsPrincipal who)
        {
            AuditChainRunDto run = FinanceTestSupport.Ok(await Run(host, who, "full"));
            Assert.AreEqual("Running", run.Status, "the answer is the queued run");
            for (int i = 0; i < 600 && run.Status == "Running"; i++)
            {
                await Task.Delay(100);
                run = FinanceTestSupport.Ok(await host.As(who, sp => sp.GetRequiredService<IAuditIntegrityService>().GetRunAsync(run.Id, CancellationToken.None)));
            }
            return run;
        }

        private static Task<ResponseDto<AuditChainRunDto>> Run(AppHost host, ClaimsPrincipal who, string mode) =>
            host.As(who, sp => sp.GetRequiredService<IAuditIntegrityService>().RunOnDemandAsync(new AuditChainRunRequestDto { Mode = mode }, CancellationToken.None));

        [TestMethod]
        public async Task The_First_Run_Takes_The_Whole_Chain_And_The_Next_One_Only_What_Is_New()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-00010");
            SaveRecord(host, "QIR-1405-00011");
            AuditChainRunDto first = FinanceTestSupport.Ok(await Run(host, Pmo1, "incremental"));
            Assert.AreEqual("Ok", first.Status);
            Assert.AreEqual("Full", first.Kind, "no checkpoint yet: the first run is the whole chain");
            Assert.AreEqual(2, first.EntriesChecked);
            Assert.AreEqual(2L, first.HeadSeq);
            Assert.IsTrue(first.Manual);

            SaveRecord(host, "QIR-1405-00012");
            AuditChainRunDto second = FinanceTestSupport.Ok(await Run(host, Ceo, "incremental"));
            Assert.AreEqual("Incremental", second.Kind);
            Assert.AreEqual("Ok", second.Status);
            Assert.AreEqual(2, second.EntriesChecked, "the entry of the first run itself (the history of the verification is in the chain) and the one new record");
            Assert.AreEqual(3L, second.FromSeq);
            Assert.AreEqual(4L, second.HeadSeq);

            AuditChainRunDto none = FinanceTestSupport.Ok(await Run(host, Qa, "incremental"));
            Assert.AreEqual(1, none.EntriesChecked, "only the entry of the previous run itself");

            AuditChainRunDto full = await RunFull(host, Pmo1);
            Assert.AreEqual("Full", full.Kind);
            Assert.AreEqual(7, full.EntriesChecked, "3 records, 3 earlier runs and the queued run itself");
            Assert.AreEqual(1L, full.FromSeq);

            ResponseDto<PagedList<AuditChainRunDto>> history = await host.As(Pmo1, sp => sp.GetRequiredService<IAuditIntegrityService>().GetRunsAsync(new AuditChainRunQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None));
            Assert.AreEqual(4, FinanceTestSupport.Ok(history).Count());
            Assert.AreEqual("Full", FinanceTestSupport.Ok(history).First().Kind, "newest first");
        }

        [TestMethod]
        public async Task The_Chain_Check_Is_Refused_To_Other_Roles_And_To_Unknown_Modes()
        {
            AppHost host = Host();
            Assert.IsFalse((await Run(host, Store, "incremental")).Success);
            Assert.IsFalse((await Run(host, AppHost.Principal(90, "anonymous.one"), "full")).Success);
            Assert.IsFalse((await Run(host, Pmo1, "sometimes")).Success);
            Assert.IsFalse((await host.As(Store, sp => sp.GetRequiredService<IAuditIntegrityService>().GetRunsAsync(new AuditChainRunQuery(), CancellationToken.None))).Success);
        }

        [TestMethod]
        public async Task A_Break_Is_Recorded_With_The_First_Broken_Entry_And_Alerts_Qa_Pmo_And_Ceo()
        {
            AppHost host = Host();
            int id = SaveRecord(host, "QIR-1405-00020");
            SaveRecord(host, "QIR-1405-00021");
            Assert.AreEqual("Ok", (await RunFull(host, Pmo1)).Status);
            SaveRecord(host, "QIR-1405-00022");
            Tamper(host, raw => raw.Logs.Single(r => r.Seq == 5).Summary = "edited by hand");

            AuditChainRunDto broken = FinanceTestSupport.Ok(await Run(host, Pmo1, "incremental"));
            Assert.AreEqual("Broken", broken.Status);
            Assert.AreEqual(5L, broken.BreakSeq);
            Assert.AreEqual(nameof(QcRecord), broken.BreakEntityType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(broken.BreakEntityTypeText));
            Assert.IsNotNull(broken.BreakRowId);
            Assert.IsNotNull(broken.BreakAt);

            List<OfInboxItem> alerts = host.Read(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey.StartsWith("integrity:break")).ToList());
            CollectionAssert.AreEquivalent(new[] { "CEO", "PMO", "QA" }, alerts.Select(a => a.RecipientRole).ToArray());
            Assert.IsTrue(alerts.All(a => a.Link == "/integrity" && a.Title.Contains("5")));

            // the same break found again does not pile up alerts
            Assert.AreEqual("Broken", (await RunFull(host, Pmo1)).Status);
            Assert.AreEqual(3, host.Read(uow => uow.Set<OfInboxItem>().Count(i => i.SourceKey.StartsWith("integrity:break"))));

            IntegritySummaryDto summary = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));
            Assert.AreEqual("Broken", summary.ChainStatus);
            Assert.IsTrue(summary.Red);
            Assert.IsNotNull(summary.LastGoodChainRun);
        }

        [TestMethod]
        public async Task The_Scheduled_Run_Makes_A_Full_Pass_The_First_Time_And_Once_Per_Night()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-00030");
            var at = new DateTime(2026, 11, 4, 2, 31, 0);   // a Wednesday
            Assert.IsTrue(IntegrityHostedService.Due(at, null));
            Assert.IsFalse(IntegrityHostedService.Due(at.Date.AddHours(2), null), "before 02:30");
            Assert.IsFalse(IntegrityHostedService.Due(at, new DateTimeOffset(at.AddMinutes(-1))), "already run tonight");
            Assert.IsTrue(IntegrityHostedService.Due(at, new DateTimeOffset(at.AddDays(-1))));
            Assert.AreEqual(new TimeSpan(2, 30, 0), IntegrityHostedService.RunAt);

            AuditChainRunDto first = await host.AsSystem(sp => sp.GetRequiredService<IAuditIntegrityService>().RunScheduledAsync(at, CancellationToken.None));
            Assert.AreEqual("Full", first.Kind);
            Assert.IsFalse(first.Manual);
            SaveRecord(host, "QIR-1405-00031");
            AuditChainRunDto next = await host.AsSystem(sp => sp.GetRequiredService<IAuditIntegrityService>().RunScheduledAsync(at.AddDays(1), CancellationToken.None));
            Assert.AreEqual("Incremental", next.Kind);
            Assert.AreEqual(2, next.EntriesChecked, "the entry of the first run and the new record");
            // Friday: the weekly full pass
            DateTime friday0 = DateTime.Today.AddDays(2);
            while (friday0.DayOfWeek != DayOfWeek.Friday) friday0 = friday0.AddDays(1);
            AuditChainRunDto friday = await host.AsSystem(sp => sp.GetRequiredService<IAuditIntegrityService>().RunScheduledAsync(friday0.AddHours(2).AddMinutes(31), CancellationToken.None));
            Assert.AreEqual("Full", friday.Kind);
        }

        [TestMethod]
        public void The_Job_Is_Switched_Off_By_Its_Setting()
        {
            var off = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Integrity:Jobs"] = "false" }).Build();
            Assert.IsFalse(IntegrityHostedService.Enabled(off));
            Assert.IsTrue(IntegrityHostedService.Enabled(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        }

        // ------------------------------------------------------------------------------------------ the restore test result

        private static string WriteResult(string folder, string runId, bool success, DateTime startedAt)
        {
            string json = "{\"runId\":\"" + runId + "\",\"startedAt\":\"" + startedAt.ToString("o") + "\",\"finishedAt\":\"" + startedAt.AddMinutes(4).ToString("o") + "\",\"success\":" + (success ? "true" : "false") +
                ",\"backupFile\":\"IRNNCO_20261101.bak.enc\",\"backupTakenAt\":\"" + startedAt.AddHours(-2).ToString("o") + "\",\"migrationsMatch\":true,\"chainOk\":" + (success ? "true" : "false") + ",\"chainEntriesChecked\":120,\"scratchDropped\":true," +
                "\"rowCounts\":{\"FnAuditLogs\":120,\"InvItems\":15},\"checks\":[{\"name\":\"restore\",\"passed\":true,\"detail\":\"ok\"},{\"name\":\"audit-chain\",\"passed\":" + (success ? "true" : "false") + ",\"detail\":\"entries 120\"}],\"message\":\"x\"}";
            string path = Path.Combine(folder, "restore-test-" + runId + ".json");
            File.WriteAllText(path, json);
            return path;
        }

        [TestMethod]
        public async Task The_Restore_Test_Result_Is_Read_Once_And_Shown_On_The_Summary_With_The_35_Day_Rule()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pd-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                AppHost host = Host(new Dictionary<string, string> { ["RestoreTest:ResultDirectory"] = folder });
                IntegritySummaryDto never = FinanceTestSupport.Ok(await host.As(Ceo, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));
                Assert.AreEqual("Never", never.RestoreStatus);
                Assert.IsTrue(never.Red);
                Assert.AreEqual(35, never.RestoreWarnDays);

                WriteResult(folder, "rt-20261001030000", true, DateTime.Now.AddDays(-40));
                File.WriteAllText(Path.Combine(folder, "restore-test-broken.json"), "{ not json");
                File.WriteAllText(Path.Combine(folder, "other.txt"), "ignored");
                RestoreImportDto import = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IRestoreTestService>().ImportOnDemandAsync(CancellationToken.None)));
                Assert.AreEqual(1, import.Imported);
                IntegritySummaryDto stale = FinanceTestSupport.Ok(await host.As(Ceo, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));
                Assert.AreEqual("Stale", stale.RestoreStatus, "a good test older than 35 days is red");
                Assert.IsTrue(stale.Red);

                WriteResult(folder, "rt-20261101030000", true, DateTime.Now.AddDays(-3).AddHours(-1));
                RestoreImportDto again = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IRestoreTestService>().ImportOnDemandAsync(CancellationToken.None)));
                Assert.AreEqual(1, again.Imported, "only the new file");
                RestoreImportDto idempotent = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IRestoreTestService>().ImportOnDemandAsync(CancellationToken.None)));
                Assert.AreEqual(0, idempotent.Imported);
                Assert.AreEqual(2, host.Read(uow => uow.Set<RestoreTestResult>().Count()));

                IntegritySummaryDto ok = FinanceTestSupport.Ok(await host.As(Ceo, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));
                Assert.AreEqual("Ok", ok.RestoreStatus);
                Assert.IsNotNull(ok.LastRestoreSuccessAtText);
                Assert.AreEqual(3, ok.DaysSinceRestoreSuccess);

                WriteResult(folder, "rt-20261105030000", false, DateTime.Now.AddDays(-1));
                await host.As(Pmo1, sp => sp.GetRequiredService<IRestoreTestService>().ImportOnDemandAsync(CancellationToken.None));
                IntegritySummaryDto failed = FinanceTestSupport.Ok(await host.As(Ceo, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));
                Assert.AreEqual("Failed", failed.RestoreStatus, "the newest test failed after the last good one");
                Assert.IsTrue(failed.Red);

                Assert.IsFalse((await host.As(Store, sp => sp.GetRequiredService<IRestoreTestService>().ImportOnDemandAsync(CancellationToken.None))).Success);
                ResponseDto<PagedList<RestoreTestDto>> list = await host.As(Pmo1, sp => sp.GetRequiredService<IRestoreTestService>().GetResultsAsync(new PaginationParams { PageNumber = 1, PageSize = 10 }, CancellationToken.None));
                RestoreTestDto newest = FinanceTestSupport.Ok(list).First();
                Assert.AreEqual("rt-20261105030000", newest.RunId);
                Assert.AreEqual(2, newest.Checks.Count);
                Assert.AreEqual(120, newest.RowCounts["FnAuditLogs"]);
            }
            finally { Directory.Delete(folder, true); }
        }

        // ------------------------------------------------------------------------------------------ the strict profile

        [TestMethod]
        public void The_Profile_Knows_Its_Settings_Their_Strict_Value_And_Which_Direction_Weakens()
        {
            SafetySettingSpec edms = SafetyProfile.Find(SafetyStore.ProjectControl, "EdmsRequireRevision");
            Assert.IsNotNull(edms);
            Assert.AreEqual("1", edms.Strict);
            Assert.IsTrue(SafetyProfile.IsWeaker(edms, "0"));
            Assert.IsFalse(SafetyProfile.IsWeaker(edms, "1"));
            SafetySettingSpec tolerance = SafetyProfile.Find(SafetyStore.Procurement, "ReceiptTolerancePercent");
            Assert.IsTrue(SafetyProfile.IsWeaker(tolerance, "10"));
            Assert.IsFalse(SafetyProfile.IsWeaker(tolerance, "1"));
            SafetySettingSpec distinct = SafetyProfile.Find(SafetyStore.ProductBank, "BomSignatures:DistinctReviewers");
            Assert.IsTrue(SafetyProfile.IsWeaker(distinct, "false"));
            Assert.IsFalse(SafetyProfile.IsWeaker(distinct, "true"));
            Assert.IsNull(SafetyProfile.Find(SafetyStore.ProjectControl, "RedThreshold"), "ordinary settings are not in the profile");
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.AiSeat, "CEO-BRIEF"), "every AI seat is");

            Assert.IsNull(SafetyProfile.Normalize(edms, "2", out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.AreEqual("1", SafetyProfile.Normalize(edms, " 1.0 ", out _));
            Assert.IsNull(SafetyProfile.Normalize(distinct, "maybe", out _));
            Assert.AreEqual("true", SafetyProfile.Normalize(distinct, "TRUE", out _));
            Assert.IsTrue(SafetyProfile.SameValue("1", "1.00"));
            Assert.IsTrue(SafetyProfile.SameValue("True", "true"));
        }

        [TestMethod]
        public void Another_Module_Adds_Its_Own_Keys_With_One_Call()
        {
            SafetyProfile.RegisterKeys(SafetyStore.ProductBank, new[] { "PdTest.OpsReady.Enabled", "PdTest.OpsData.*.Enabled" }, SafetyKind.FlagMustBeOn, "1", "گیت داده");
            SafetySettingSpec exact = SafetyProfile.Find(SafetyStore.ProductBank, "PdTest.OpsReady.Enabled");
            Assert.IsNotNull(exact);
            Assert.AreEqual("1", exact.Strict);
            SafetySettingSpec wild = SafetyProfile.Find(SafetyStore.ProductBank, "PdTest.OpsData.Weld.Enabled");
            Assert.IsNotNull(wild, "a wildcard key covers a family");
            Assert.IsNull(SafetyProfile.Find(SafetyStore.ProductBank, "PdTest.Other"));
            int before = SafetyProfile.All().Count;
            SafetyProfile.RegisterKeys(SafetyStore.ProductBank, new[] { "PdTest.OpsReady.Enabled" }, SafetyKind.FlagMustBeOn, "1", "گیت داده");
            Assert.AreEqual(before, SafetyProfile.All().Count, "registering twice does not duplicate");
            Assert.IsNotNull(SafetyProfile.Refusal(SafetyStore.ProductBank, "PdTest.OpsReady.Enabled"));
        }

        private static Task<ResponseDto<SafetyChangeDto>> Request(AppHost host, ClaimsPrincipal who, string store, string key, string value, string reason = "دلیل آزمون سخت‌گیرانه") =>
            host.As(who, sp => sp.GetRequiredService<ISafetySettingsService>().RequestAsync(new SafetyRequestDto { Store = store, Key = key, Value = value, Reason = reason }, CancellationToken.None));

        private static Task<ResponseDto<SafetyChangeDto>> Decide(AppHost host, ClaimsPrincipal who, int id, bool approve, string note = null) =>
            host.As(who, sp => sp.GetRequiredService<ISafetySettingsService>().DecideAsync(new SafetyDecisionDto { Id = id, Approve = approve, Note = note }, CancellationToken.None));

        private static decimal PcValue(AppHost host, string key) => host.Read(uow => uow.Set<PcSetting>().AsNoTracking().Where(s => s.Key == key).OrderByDescending(s => s.Id).Select(s => (decimal?)s.Value).FirstOrDefault()) ?? -1m;

        [TestMethod]
        public async Task A_Security_Relevant_Setting_Changes_Only_With_A_Second_Person_And_A_Reason()
        {
            AppHost host = Host();
            Assert.AreEqual(-1m, PcValue(host, "EdmsRequireRevision"), "no row yet: the default 0 applies");

            // the settings page of the module does not change it
            ResponseDto<List<PcSettingDto>> direct = await host.As(Pmo1, sp => sp.GetRequiredService<IPcProjectService>().SaveSettingsAsync(
                new PcSettingsSaveDto { Items = new List<PcSettingValueDto> { new PcSettingValueDto { Key = "EdmsRequireRevision", Value = 1 } } }, CancellationToken.None));
            Assert.IsFalse(direct.Success);
            StringAssert.Contains(direct.Message, "پروفایل سخت‌گیرانه");

            Assert.IsFalse((await Request(host, Store, "ProjectControl", "EdmsRequireRevision", "1")).Success, "the storekeeper cannot request");
            Assert.IsFalse((await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1", "x")).Success, "a reason is needed");
            Assert.IsFalse((await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "0")).Success, "no change");
            Assert.IsFalse((await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "7")).Success, "a value out of range");
            Assert.IsFalse((await Request(host, Pmo1, "ProjectControl", "RedThreshold", "10")).Success, "not in the profile");
            Assert.IsFalse((await Request(host, Pmo1, "Nowhere", "EdmsRequireRevision", "1")).Success);

            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            Assert.AreEqual("Pending", request.Status);
            Assert.AreEqual("0", request.OldValue);
            Assert.IsFalse(request.Weakening);
            Assert.IsFalse((await Request(host, Pmo2, "ProjectControl", "EdmsRequireRevision", "1")).Success, "one open request per setting");
            List<OfInboxItem> inbox = host.Read(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "safety:" + request.Id).ToList());
            CollectionAssert.AreEquivalent(new[] { "CEO", "PMO" }, inbox.Select(i => i.RecipientRole).ToArray());

            ResponseDto<SafetyChangeDto> self = await Decide(host, Pmo1, request.Id, true);
            Assert.IsFalse(self.Success, "the requester cannot approve his own request");
            StringAssert.Contains(self.Message, "تفکیک وظایف");
            Assert.IsFalse((await Decide(host, Store, request.Id, true)).Success);
            Assert.AreEqual(-1m, PcValue(host, "EdmsRequireRevision"), "nothing changed yet");

            // an AI seat user (by its name in the user table) cannot approve either, whatever roles its token carries
            host.Write(uow => { uow.Set<User>().Add(AppHost.Add(uow, new User { Id = 80, UserName = "seat.ceo-brief" })); uow.SaveChanges(); });
            ResponseDto<SafetyChangeDto> bySeat = await Decide(host, Seat, request.Id, true);
            Assert.IsFalse(bySeat.Success);
            Assert.AreEqual(-1m, PcValue(host, "EdmsRequireRevision"));

            Assert.IsFalse((await Decide(host, Ceo, request.Id, false)).Success, "a rejection needs a note");
            SafetyChangeDto approved = FinanceTestSupport.Ok(await Decide(host, Ceo, request.Id, true, "تایید"));
            Assert.AreEqual("Approved", approved.Status);
            Assert.AreEqual(50, approved.RequestedByUserId);
            Assert.AreEqual(37, approved.DecidedByUserId);
            Assert.AreEqual(1m, PcValue(host, "EdmsRequireRevision"), "the value is applied");
            Assert.IsFalse((await Decide(host, Pmo2, request.Id, true)).Success, "decided once");
            Assert.AreEqual(0, host.Read(uow => uow.Set<OfInboxItem>().Count(i => i.SourceKey == "safety:" + request.Id && i.ClosedAt == null)), "the alert is closed");

            // both the request and the decision are in the audit chain
            List<FnAuditLog> trail = Trail(host, nameof(SafetySettingChange));   // the new row has no id yet: its summary is the key
            Assert.AreEqual(1, trail.Count(t => t.Action == "Added" && t.Summary.EndsWith("EdmsRequireRevision")));
            Assert.AreEqual(1, trail.Count(t => t.Action == "Modified" && t.EntityId == request.Id));
            Assert.IsTrue(Trail(host, nameof(PcSetting)).Count >= 1, "the setting row itself is in the trail");
            AuditChainVerifyDto chain = FinanceTestSupport.Ok(await host.As(FinanceTestSupport.Auditor, sp => sp.GetRequiredService<IAuditChainService>().VerifyAsync(null, null, null, null, CancellationToken.None)));
            Assert.IsTrue(chain.Ok, chain.Message);

            // putting it back is a weakening: flagged, and again a second person
            SafetyChangeDto back = FinanceTestSupport.Ok(await Request(host, Ceo, "ProjectControl", "EdmsRequireRevision", "0", "بازگشت به دوره گذار"));
            Assert.IsTrue(back.Weakening);
            FinanceTestSupport.Ok(await Decide(host, Pmo1, back.Id, true));
            Assert.AreEqual(0m, PcValue(host, "EdmsRequireRevision"));

            // cancel and reject
            SafetyChangeDto third = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            Assert.IsFalse((await host.As(Pmo2, sp => sp.GetRequiredService<ISafetySettingsService>().CancelAsync(third.Id, CancellationToken.None))).Success, "only the requester cancels");
            Assert.AreEqual("Cancelled", FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<ISafetySettingsService>().CancelAsync(third.Id, CancellationToken.None))).Status);
            SafetyChangeDto fourth = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            Assert.AreEqual("Rejected", FinanceTestSupport.Ok(await Decide(host, Pmo2, fourth.Id, false, "ثبت بازنگری اسناد هنوز تمام نشده")).Status);
            Assert.AreEqual(0m, PcValue(host, "EdmsRequireRevision"));
            PagedList<SafetyChangeDto> all = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<ISafetySettingsService>().GetChangesAsync(new SafetyChangeQuery { PageNumber = 1, PageSize = 20 }, CancellationToken.None)));
            Assert.AreEqual(4, all.Count());
        }

        [TestMethod]
        public async Task The_Profile_And_The_Deviation_Check_List_What_Differs_From_The_Strict_Values()
        {
            AppHost host = Host();
            ResponseDto<SafetyDeviationsDto> refused = await host.As(Store, sp => sp.GetRequiredService<ISafetySettingsService>().GetDeviationsAsync(CancellationToken.None));
            Assert.IsFalse(refused.Success);

            SafetyDeviationsDto dev = FinanceTestSupport.Ok(await host.As(FinanceTestSupport.Auditor, sp => sp.GetRequiredService<ISafetySettingsService>().GetDeviationsAsync(CancellationToken.None)));
            SafetyItemDto edms = dev.Items.Single(i => i.Key == "EdmsRequireRevision");
            Assert.IsTrue(edms.Deviates && edms.Weaker);
            Assert.AreEqual("0", edms.Current);
            Assert.AreEqual("1", edms.Strict);
            Assert.AreEqual(dev.Deviations, dev.Items.Count);
            Assert.IsTrue(dev.Total >= dev.Deviations);
            Assert.IsFalse(dev.Items.Any(i => i.Key == "EnforcePurchaseOrder"), "the default is on, as strict");
            Assert.IsFalse(dev.Items.Any(i => i.Key == "BomSignatures:DistinctReviewers"), "the default is on since D13 (Package 8), as strict");

            List<SafetyItemDto> profile = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<ISafetySettingsService>().GetProfileAsync(CancellationToken.None)));
            Assert.IsTrue(profile.Count >= dev.Items.Count);
            Assert.IsTrue(profile.Any(i => i.Key == "EnforcePurchaseOrder" && !i.Deviates));

            // the pending request shows on the profile
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            List<SafetyItemDto> withPending = FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<ISafetySettingsService>().GetProfileAsync(CancellationToken.None)));
            Assert.AreEqual(request.Id, withPending.Single(i => i.Key == "EdmsRequireRevision").PendingRequestId);
        }

        [TestMethod]
        public async Task The_Save_Paths_Of_The_Modules_Refuse_A_Registered_Setting_But_Keep_The_Others()
        {
            AppHost host = Host();
            // the purchase-order rule: not directly (off), the same value is not a change
            ResponseDto<ProcurementSettingDto> off = await host.As(Pmo1, sp => sp.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = "EnforcePurchaseOrder", Value = 0 }, CancellationToken.None));
            Assert.IsFalse(off.Success);
            StringAssert.Contains(off.Message, "پروفایل سخت‌گیرانه");
            Assert.IsTrue((await host.As(Pmo1, sp => sp.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = "EnforcePurchaseOrder", Value = 1 }, CancellationToken.None))).Success);
            Assert.IsTrue((await host.As(Pmo1, sp => sp.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = "MaxAdvancePercent", Value = 20 }, CancellationToken.None))).Success, "an ordinary setting is saved as before");

            // with the second person it works
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Pmo1, "Procurement", "EnforcePurchaseOrder", "0", "راه‌اندازی اولیه انبار"));
            Assert.IsTrue(request.Weakening);
            FinanceTestSupport.Ok(await Decide(host, Ceo, request.Id, true));
            decimal value = host.Read(uow => uow.Set<Entities.Model.NasbNiro.Procurement.ProcurementSetting>().AsNoTracking().Single(s => s.Key == "EnforcePurchaseOrder").Value);
            Assert.AreEqual(0m, value);
        }
    }
}
