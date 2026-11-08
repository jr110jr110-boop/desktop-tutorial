using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.OpsData;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;
using Iv = Entities.Model.NasbNiro.Inventory;

namespace MsTests
{
    /// <summary>Package D fix round: the records themselves are checked against the trail, a break stays until a full pass is good, three persons accept a known break, the strict profile covers the gates of Package 8.</summary>
    [TestClass]
    public class RecordsIntegrityPdFixTests
    {
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(50, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(51, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(37, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Qa = AppHost.Principal(60, "qa.one", "QA");
        private static readonly ClaimsPrincipal QaPmo = AppHost.Principal(61, "qa.and.pmo", "QA", "PMO");
        private static readonly ClaimsPrincipal Planning = AppHost.Principal(71, "plan.one", "PLANNING");
        private static readonly ClaimsPrincipal SeatPmo = AppHost.Principal(80, "seat.ceo-brief", "PMO");

        private static AppHost Host() => new AppHost("pdfix_" + Guid.NewGuid().ToString("N"), new Dictionary<string, string> { ["Identity:AuditHmacKey"] = "pd-test-audit-key-not-a-secret" });

        private static int SaveRecord(AppHost host, string number)
        {
            int id = 0;
            host.Write(uow =>
            {
                var r = AppHost.Add(uow, new QcRecord { Number = number, Type = QcRecordType.InProcess, InspectorUserId = 60, Quantity = 1, Result = QcResult.Accept, RecordDate = DateTime.Today });
                uow.SaveChanges();
                id = r.Id;
            }, Qa);
            return id;
        }

        private static List<FnAuditLog> Trail(AppHost host, string type) => host.Read(uow => uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());

        /// <summary>Writes to the tables behind the back of the application (a person with access to the database).</summary>
        private sealed class RawContext : DbContext
        {
            public RawContext(DbContextOptions options) : base(options) { }
            public DbSet<FnAuditLog> Logs { get; set; }
            public DbSet<RetentionRule> Rules { get; set; }
            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<FnAuditLog>().ToTable("FnAuditLogs").HasKey(x => x.Id);
                builder.Entity<RetentionRule>().ToTable("QaRetentionRules").HasKey(x => x.Id);
            }
        }

        private static int SaveRule(AppHost host, string recordType, int years)
        {
            int id = 0;
            host.Write(uow =>
            {
                var r = new RetentionRule { RecordType = recordType, Years = years, Permanent = false, Source = "ISO 9001" };
                uow.Set<RetentionRule>().Add(r);
                uow.SaveChanges();
                id = r.Id;
            }, Qa);
            return id;
        }

        private static void Raw(AppHost host, Action<RawContext> change)
        {
            using (var raw = new RawContext(host.RawOptions<RawContext>())) { change(raw); raw.SaveChanges(); }
        }

        private static Task<ResponseDto<AuditChainRunDto>> Run(AppHost host, ClaimsPrincipal who, string mode) =>
            host.As(who, sp => sp.GetRequiredService<IAuditIntegrityService>().RunOnDemandAsync(new AuditChainRunRequestDto { Mode = mode }, CancellationToken.None));

        private static async Task<AuditChainRunDto> RunFull(AppHost host, ClaimsPrincipal who)
        {
            AuditChainRunDto run = FinanceTestSupport.Ok(await Run(host, who, "full"));
            Assert.AreEqual("Running", run.Status);
            for (int i = 0; i < 600 && run.Status == "Running"; i++)
            {
                await Task.Delay(100);
                run = FinanceTestSupport.Ok(await host.As(who, sp => sp.GetRequiredService<IAuditIntegrityService>().GetRunAsync(run.Id, CancellationToken.None)));
            }
            return run;
        }

        private static async Task<AuditChainRunDto> Incremental(AppHost host) => FinanceTestSupport.Ok(await Run(host, Pmo1, "incremental"));

        private static async Task<IntegritySummaryDto> Summary(AppHost host) =>
            FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IAuditIntegrityService>().GetSummaryAsync(CancellationToken.None)));

        // ------------------------------------------------------------------------------------------ H1: the records themselves

        [TestMethod]
        public void A_New_Record_Is_Audited_With_Its_Real_Row_Id_Even_With_A_Child_In_The_Same_Save()
        {
            AppHost host = Host();
            int id = SaveRecord(host, "QIR-1405-02001");
            FnAuditLog added = Trail(host, nameof(QcRecord)).Single(a => a.Action == "Added");
            Assert.AreEqual(id, added.EntityId, "the entry carries the key the database gave the row");
            Assert.IsNotNull(added.Seq);

            int parent = 0;
            host.Write(uow =>
            {
                var r = AppHost.Add(uow, new QcRecord { Number = "QIR-1405-02002", Type = QcRecordType.InProcess, InspectorUserId = 60, Quantity = 1, Result = QcResult.Accept, RecordDate = DateTime.Today });
                uow.Set<QcMeasurement>().Add(new QcMeasurement { Record = r, Characteristic = "OD", Nominal = 10m, TolPlus = 0.1m, TolMinus = 0.1m, Measured = 10.05m, Unit = "mm", Pass = true });
                uow.SaveChanges();
                parent = r.Id;
            }, Qa);
            FnAuditLog child = Trail(host, nameof(QcMeasurement)).Single(a => a.Action == "Added");
            Assert.IsNotNull(child.EntityId, "a child saved with its new parent also has its real id");
            Assert.AreEqual(parent, Trail(host, nameof(QcRecord)).Single(a => a.Action == "Added" && a.Summary.EndsWith("QIR-1405-02002")).EntityId);
            Assert.IsTrue(child.AfterJson.Contains("RecordId") || child.AfterJson.Contains("Characteristic"));
        }

        [TestMethod]
        public async Task An_Edit_Of_The_Record_Itself_Is_Found_With_Table_Row_And_Column_Without_Touching_The_Trail()
        {
            AppHost host = Host();
            int id = SaveRule(host, "NCR", 5);
            SaveRule(host, "ITP", 7);
            Assert.AreEqual("Ok", (await RunFull(host, Pmo1)).Status, "an honest history is good, the records agree with the trail");

            Raw(host, raw => raw.Rules.Single(r => r.Id == id).Years = 1);   // the audit trail is NOT touched, so the hash chain is intact

            AuditChainRunDto found = await Incremental(host);
            Assert.AreEqual("Broken", found.Status);
            Assert.AreEqual("record-drift", found.BreakKind);
            Assert.AreEqual(nameof(RetentionRule), found.BreakEntityType);
            Assert.AreEqual(id, found.BreakRowId);
            Assert.AreEqual("Years", found.BreakColumn);
            Assert.IsFalse(string.IsNullOrWhiteSpace(found.BreakEntityTypeText));
            List<OfInboxItem> alerts = host.Read(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey.StartsWith("integrity:break:record-drift")).ToList());
            CollectionAssert.AreEquivalent(new[] { "CEO", "PMO", "QA" }, alerts.Select(a => a.RecipientRole).ToArray());
            Assert.IsTrue(alerts.All(a => a.Title.Contains("Years") && a.Title.Contains(id.ToString())));
        }

        [TestMethod]
        public async Task A_Record_Removed_Behind_The_Application_Is_Found_And_An_Honest_Change_Is_Not_A_Finding()
        {
            AppHost host = Host();
            int a = SaveRule(host, "NCR", 5);
            int b = SaveRule(host, "ITP", 7);
            host.Write(uow => { uow.Set<RetentionRule>().First(r => r.Id == a).Years = 6; uow.SaveChanges(); }, Qa);   // an honest change is in the trail
            Assert.AreEqual("Ok", (await Incremental(host)).Status);

            Raw(host, raw => raw.Rules.Remove(raw.Rules.Single(r => r.Id == b)));
            AuditChainRunDto found = await Incremental(host);
            Assert.AreEqual("Broken", found.Status);
            Assert.AreEqual("record-drift", found.BreakKind);
            Assert.AreEqual(b, found.BreakRowId);
            Assert.AreEqual("(ردیف)", found.BreakColumn);
        }

        [TestMethod]
        public async Task A_Deleted_Record_With_A_Deleted_Entry_Is_No_Finding()
        {
            AppHost host = Host();
            int a = SaveRecord(host, "QIR-1405-02030");
            host.Write(uow => { uow.Set<QcRecord>().Remove(uow.Set<QcRecord>().First(r => r.Id == a)); uow.SaveChanges(); }, Qa);
            Assert.AreEqual("Ok", (await Incremental(host)).Status);
        }

        // ------------------------------------------------------------------------------------------ H2: the status and the acceptance

        [TestMethod]
        public async Task A_Break_Stays_Until_A_Later_Full_Pass_Is_Good_And_No_Run_Restarts_From_An_Older_Good_Run()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-02040");
            SaveRecord(host, "QIR-1405-02041");
            AuditChainRunDto good = await Incremental(host);
            Assert.AreEqual("Ok", good.Status);

            string original = Trail(host, nameof(QcRecord)).Single(a => a.Seq == 1).Summary;
            Raw(host, raw => raw.Logs.Single(r => r.Seq == 1).Summary = "edited by hand");   // before the head the good run recorded
            AuditChainRunDto broken = await RunFull(host, Pmo1);
            Assert.AreEqual("Broken", broken.Status);
            Assert.AreEqual(1L, broken.BreakSeq);

            AuditChainRunDto next = await Incremental(host);
            Assert.AreEqual("Full", next.Kind, "after a break every run is a full pass: the older good run is not a starting point");
            Assert.AreEqual("Broken", next.Status);
            Assert.AreEqual("Broken", (await Summary(host)).ChainStatus);

            // repaired: only a full pass clears the status
            Raw(host, raw => raw.Logs.Single(r => r.Seq == 1).Summary = original);
            Assert.AreEqual("Broken", (await Summary(host)).ChainStatus, "the repair alone does not clear it");
            AuditChainRunDto cleared = await Incremental(host);
            Assert.AreEqual("Full", cleared.Kind);
            Assert.AreEqual("Ok", cleared.Status);
            Assert.AreEqual("Ok", (await Summary(host)).ChainStatus);
        }

        private static Task<ResponseDto<ChainBreakAcceptanceDto>> Accept(AppHost host, ClaimsPrincipal who, int runId, string reason = "ردیف در بازیابی دستی اصلاح شد و قابل بازگشت نیست") =>
            host.As(who, sp => sp.GetRequiredService<IChainBreakAcceptanceService>().RequestAsync(new ChainBreakAcceptanceRequestDto { RunId = runId, Reason = reason }, CancellationToken.None));

        private static Task<ResponseDto<ChainBreakAcceptanceDto>> Sign(AppHost host, ClaimsPrincipal who, int id, string role) =>
            host.As(who, sp => sp.GetRequiredService<IChainBreakAcceptanceService>().SignAsync(new ChainBreakAcceptanceSignDto { Id = id, Role = role }, CancellationToken.None));

        [TestMethod]
        public async Task A_Known_Break_Is_Accepted_Only_By_Qa_Pmo_And_Ceo_As_Three_Distinct_Persons_And_Moves_The_Start_Of_The_Walk()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-02050");
            SaveRecord(host, "QIR-1405-02051");
            SaveRecord(host, "QIR-1405-02052");
            Raw(host, raw => raw.Logs.Single(r => r.Seq == 2).Summary = "edited by hand");
            AuditChainRunDto broken = await RunFull(host, Pmo1);
            Assert.AreEqual("Broken", broken.Status);
            Assert.AreEqual(2L, broken.BreakSeq);

            Assert.IsFalse((await Accept(host, Planning, broken.Id)).Success, "only QA, PMO or CEO ask");
            Assert.IsFalse((await Accept(host, Qa, broken.Id, "x")).Success, "a reason is needed");
            ChainBreakAcceptanceDto request = FinanceTestSupport.Ok(await Accept(host, Qa, broken.Id));
            Assert.AreEqual("Pending", request.Status);
            Assert.IsFalse((await Accept(host, Pmo1, broken.Id)).Success, "one request at a time");

            ChainBreakAcceptanceDto one = FinanceTestSupport.Ok(await Sign(host, Qa, request.Id, "QA"));
            Assert.AreEqual(1, one.Signatures);
            Assert.IsFalse((await Sign(host, Pmo1, request.Id, "QA")).Success, "the seat is taken");
            Assert.IsFalse((await Sign(host, Pmo1, request.Id, "CEO")).Success, "he does not hold the role of the seat");
            ChainBreakAcceptanceDto two = FinanceTestSupport.Ok(await Sign(host, QaPmo, request.Id, "PMO"));
            Assert.AreEqual(2, two.Signatures);
            Assert.IsFalse((await Sign(host, Qa, request.Id, "PMO")).Success);
            // one person holds the roles QA and PMO: he signed once, he cannot sign the third seat too (he has no CEO role) and the QA person cannot sign twice
            Assert.AreEqual("Pending", FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IChainBreakAcceptanceService>().GetAsync(new PaginationParams { PageNumber = 1, PageSize = 5 }, CancellationToken.None))).First().Status);

            ChainBreakAcceptanceDto three = FinanceTestSupport.Ok(await Sign(host, Ceo, request.Id, "CEO"));
            Assert.AreEqual("Approved", three.Status);
            Assert.AreEqual(3, three.Signatures);
            Assert.AreEqual(2L, three.AnchorSeq);

            Assert.AreEqual("Broken", (await Summary(host)).ChainStatus, "accepted, but the status clears only by a full pass");
            AuditChainRunDto after = await RunFull(host, Pmo1);
            Assert.AreEqual("Ok", after.Status, after.Message);
            Assert.AreEqual(3L, after.FromSeq, "the walk starts after the accepted place");
            Assert.AreEqual("Ok", (await Summary(host)).ChainStatus);

            // a new break after the anchor is still found
            Raw(host, raw => raw.Logs.Single(r => r.Seq == 3).Summary = "edited again");
            Assert.AreEqual("Broken", (await RunFull(host, Pmo1)).Status);
        }

        [TestMethod]
        public async Task The_Same_Person_Cannot_Fill_Two_Seats_And_An_Ai_Seat_Never_Signs()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-02060");
            SaveRecord(host, "QIR-1405-02061");
            Raw(host, raw => raw.Logs.Single(r => r.Seq == 1).Summary = "edited by hand");
            AuditChainRunDto broken = await RunFull(host, Pmo1);
            ChainBreakAcceptanceDto request = FinanceTestSupport.Ok(await Accept(host, Pmo1, broken.Id));
            host.Write(uow => { uow.Set<User>().Add(AppHost.Add(uow, new User { Id = 80, UserName = "seat.ceo-brief" })); uow.SaveChanges(); });
            Assert.IsFalse((await Sign(host, SeatPmo, request.Id, "PMO")).Success, "an AI seat never signs");
            FinanceTestSupport.Ok(await Sign(host, QaPmo, request.Id, "QA"));
            Assert.IsFalse((await Sign(host, QaPmo, request.Id, "PMO")).Success, "one person, one signature");
            Assert.AreEqual(1, FinanceTestSupport.Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IChainBreakAcceptanceService>().GetAsync(null, CancellationToken.None))).First().Signatures);
        }

        [TestMethod]
        public async Task A_Record_That_Differs_From_Its_Trail_Is_Reconciled_By_The_Acceptance()
        {
            AppHost host = Host();
            int id = SaveRule(host, "NCR", 5);
            Assert.AreEqual("Ok", (await Incremental(host)).Status);
            Raw(host, raw => raw.Rules.Single(r => r.Id == id).Years = 1);
            AuditChainRunDto drift = await Incremental(host);
            Assert.AreEqual("record-drift", drift.BreakKind);

            ChainBreakAcceptanceDto request = FinanceTestSupport.Ok(await Accept(host, Ceo, drift.Id));
            FinanceTestSupport.Ok(await Sign(host, Ceo, request.Id, "CEO"));
            FinanceTestSupport.Ok(await Sign(host, Qa, request.Id, "QA"));
            Assert.AreEqual("Approved", FinanceTestSupport.Ok(await Sign(host, Pmo1, request.Id, "PMO")).Status);
            Assert.IsTrue(Trail(host, nameof(RetentionRule)).Any(t => t.Action == "Modified" && t.EntityId == id && t.Summary.Contains(request.Id.ToString())), "the trail got the reconciling entry");

            AuditChainRunDto after = await RunFull(host, Pmo1);
            Assert.AreEqual("Ok", after.Status, after.Message);
            Assert.AreEqual("Ok", (await Summary(host)).ChainStatus);
        }

        // ------------------------------------------------------------------------------------------ M8 and the Lows

        [TestMethod]
        public async Task A_Full_Pass_By_Hand_Is_For_Pmo_Ceo_And_Qa_And_Runs_In_The_Background()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-02080");
            Assert.IsFalse((await Run(host, FinanceTestSupport.Auditor, "full")).Success, "the auditor reads, he does not start the heavy pass");
            Assert.IsTrue((await Run(host, FinanceTestSupport.Auditor, "incremental")).Success);
            AuditChainRunDto queued = FinanceTestSupport.Ok(await Run(host, Qa, "full"));
            Assert.AreEqual("Running", queued.Status);
            for (int i = 0; i < 600 && queued.Status == "Running"; i++)
            {
                await Task.Delay(100);
                queued = FinanceTestSupport.Ok(await host.As(Qa, sp => sp.GetRequiredService<IAuditIntegrityService>().GetRunAsync(queued.Id, CancellationToken.None)));
            }
            Assert.AreEqual("Ok", queued.Status);
            Assert.IsNotNull(queued.FinishedAt);
        }

        [TestMethod]
        public void A_Run_That_Failed_Is_Tried_Again_After_An_Hour_At_Most_Four_Times_A_Day()
        {
            var at = new DateTime(2026, 11, 4, 4, 0, 0);
            var failedAt = new DateTimeOffset(new DateTime(2026, 11, 4, 2, 31, 0));
            Assert.IsTrue(IntegrityHostedService.Due(at, failedAt, true, 1), "an hour has passed");
            Assert.IsFalse(IntegrityHostedService.Due(new DateTime(2026, 11, 4, 3, 0, 0), failedAt, true, 1), "not before an hour");
            Assert.IsFalse(IntegrityHostedService.Due(at, failedAt, false, 1), "a run that worked is not repeated");
            Assert.IsFalse(IntegrityHostedService.Due(at, failedAt, true, IntegrityHostedService.MaxRunsPerDay), "at most four a day");
        }

        [TestMethod]
        public async Task The_History_Of_The_Verification_And_The_Acceptances_Are_In_The_Chain()
        {
            AppHost host = Host();
            SaveRecord(host, "QIR-1405-02090");
            await Incremental(host);
            Assert.AreEqual(1, Trail(host, nameof(AuditChainRun)).Count(t => t.Action == "Added"));
            Assert.IsTrue(AuditEntityLabels.Of(nameof(ChainBreakAcceptance)) != null);
        }

        // ------------------------------------------------------------------------------------------ M6, M7

        [TestMethod]
        public void The_Added_Types_Of_The_Fix_Round_Are_In_The_Chain_With_Their_Row_Id()
        {
            AppHost host = Host();
            int complaint = 0, count = 0;
            host.Write(uow =>
            {
                var c = AppHost.Add(uow, new CustomerComplaint { Number = "CC-1405-001", ReceivedDate = DateTime.Today, ResponseDue = DateTime.Today.AddDays(7), Channel = "Phone", Description = "x" });
                var s = AppHost.Add(uow, new Iv.StockCount { Number = "SC-1405-001", CountDate = DateTime.Today, WarehouseId = 1, SnapshotAt = DateTimeOffset.Now });
                uow.SaveChanges();
                complaint = c.Id; count = s.Id;
            }, Qa);
            Assert.AreEqual(complaint, Trail(host, nameof(CustomerComplaint)).Single().EntityId);
            Assert.AreEqual(count, Trail(host, nameof(Iv.StockCount)).Single().EntityId);
            foreach (Type t in new[] { typeof(Iv.AssemblyLink), typeof(Iv.StockCount), typeof(Iv.StockCountLine), typeof(Iv.MaterialRequest), typeof(Iv.MaterialRequestLine), typeof(CustomerComplaint), typeof(RetentionRule), typeof(Iv.PartOperationFile) })
                Assert.IsNotNull(AuditEntityLabels.Of(t.Name), t.Name);
        }

        [TestMethod]
        public async Task A_Receipt_Of_A_Thousand_Serial_Numbers_Writes_One_Summary_Entry_And_Stays_Within_The_Lock_Time()
        {
            AppHost host = Host();
            var watch = Stopwatch.StartNew();
            host.Write(uow =>
            {
                var line = new Iv.InventoryDocumentLine { DocumentId = 5, RowNumber = 1, ItemId = 1, Quantity = 1000m };
                uow.Set<Iv.InventoryDocumentLine>().Add(line);
                for (int i = 1; i <= 1000; i++) uow.Set<Iv.InventoryDocumentLineSerial>().Add(new Iv.InventoryDocumentLineSerial { Line = line, SerialNumber = "SN-" + i.ToString("00000") });
                uow.SaveChanges();
            }, Qa);
            watch.Stop();
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(20), "the save of a thousand serials is far inside the 20 s lock timeout of the chain: " + watch.Elapsed);
            List<FnAuditLog> rows = Trail(host, nameof(Iv.InventoryDocumentLineSerial));
            Assert.AreEqual(1, rows.Count, "one summary entry for the document, not one per serial");
            StringAssert.Contains(rows[0].AfterJson, "\"count\":1000");
            StringAssert.Contains(rows[0].AfterJson, "sha256");
            Assert.IsNotNull(rows[0].Seq);
            Assert.AreEqual("Ok", (await Incremental(host)).Status);
        }

        // ------------------------------------------------------------------------------------------ H4, M4, M5

        private static Task<ResponseDto<SafetyChangeDto>> Request(AppHost host, ClaimsPrincipal who, string store, string key, string value, string reason = "دلیل آزمون سخت‌گیرانه") =>
            host.As(who, sp => sp.GetRequiredService<ISafetySettingsService>().RequestAsync(new SafetyRequestDto { Store = store, Key = key, Value = value, Reason = reason }, CancellationToken.None));

        private static Task<ResponseDto<SafetyChangeDto>> Decide(AppHost host, ClaimsPrincipal who, int id, bool approve, string note = null) =>
            host.As(who, sp => sp.GetRequiredService<ISafetySettingsService>().DecideAsync(new SafetyDecisionDto { Id = id, Approve = approve, Note = note }, CancellationToken.None));

        private static decimal QualityValue(AppHost host, string key) =>
            host.Read(uow => uow.Set<QualitySetting>().AsNoTracking().Where(s => s.Key == key).Select(s => (decimal?)s.Value).FirstOrDefault()) ?? QualitySettingKeys.Defaults().First(d => d.Key == key).Value;

        [TestMethod]
        public async Task The_Gates_Of_Package_8_Are_Strict_Settings_With_Their_Own_Kinds()
        {
            OpsRuleDef price = OpsRuleCatalog.Find(OpsRuleCodes.RcvPrice);
            string block = OpsRuleCatalog.Key(price.Code, "Block"), warn = OpsRuleCatalog.Key(price.Code, "Warn"), enabled = OpsRuleCatalog.Key(price.Code, "Enabled");
            Assert.IsTrue(price.Block > 0, "the test assumes a rule that blocks by default");
            SafetySettingSpec blockSpec = SafetyProfile.Find(SafetyStore.Quality, block);
            Assert.AreEqual(SafetyKind.ThresholdZeroOff, blockSpec.Kind);
            Assert.IsTrue(SafetyProfile.IsWeaker(blockSpec, "0"), "0 means never blocks");
            Assert.IsTrue(SafetyProfile.IsWeaker(blockSpec, (price.Block + 1).ToString()));
            Assert.IsFalse(SafetyProfile.IsWeaker(blockSpec, (price.Block - 1).ToString()));
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, enabled), "0"));
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, warn), (price.Warn + 1).ToString()));
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, OpsRuleCatalog.SeatLookbackDays), "3"), "the look-back must stay at seven days or more");
            Assert.IsFalse(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, OpsRuleCatalog.SeatLookbackDays), "14"));
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, OpsRuleCatalog.MasterSwitch));
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, OpsReadinessCatalog.MasterSwitch));
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, OpsReadinessCatalog.Key(OpsReadinessCatalog.All[0].Code, "Hard")));
            // every key of the module's own security list is covered
            foreach (string key in OpsSettingKeys.SecurityRelevant.Where(k => k.EndsWith(".Enabled") || k.EndsWith(".Block") || k.EndsWith(".Warn")))
                Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, key), key);

            AppHost host = Host();
            // the QA manager can no longer save the gate directly
            ResponseDto<List<QualitySettingDto>> direct = await host.As(Qa, sp => sp.GetRequiredService<IQualityDashboardService>().SaveSettingAsync(new SaveQualitySettingDto { Key = block, Value = 0 }, CancellationToken.None));
            Assert.IsFalse(direct.Success);
            StringAssert.Contains(direct.Message, "پروفایل سخت‌گیرانه");

            // a warning above the block is refused already at the request (the module's own relation), and a good request is a weakening that a second person decides
            Assert.IsFalse((await Request(host, Qa, "Quality", warn, (price.Block + 5).ToString())).Success, "warn must stay below block");
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Qa, "Quality", block, "0"));
            Assert.IsTrue(request.Weakening);
            Assert.IsFalse((await Decide(host, Qa, request.Id, true)).Success);
            FinanceTestSupport.Ok(await Decide(host, Ceo, request.Id, true));
            Assert.AreEqual(0m, QualityValue(host, block));
        }

        [TestMethod]
        public async Task The_Module_Checks_Are_Made_Again_At_The_Approval_And_The_Old_Value_Is_Rechecked()
        {
            AppHost host = Host();
            OpsRuleDef price = OpsRuleCatalog.Find(OpsRuleCodes.RcvPrice);
            string block = OpsRuleCatalog.Key(price.Code, "Block"), warn = OpsRuleCatalog.Key(price.Code, "Warn");
            // a request that was fine becomes invalid because the other threshold moved meanwhile (written directly, as a restore would)
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Qa, "Quality", block, (price.Warn + 1).ToString()));
            host.Write(uow => { uow.Set<QualitySetting>().Add(AppHost.Add(uow, new QualitySetting { Key = warn, Value = price.Warn + 20, Title = "x", Source = "test" })); uow.SaveChanges(); });
            ResponseDto<SafetyChangeDto> late = await Decide(host, Ceo, request.Id, true);
            Assert.IsFalse(late.Success, "block is not above warn any more");
            Assert.AreEqual("Pending", host.Read(uow => uow.Set<SafetySettingChange>().AsNoTracking().Single(c => c.Id == request.Id).Status.ToString()));

            // the value changed since the request: the request no longer describes the change
            SafetyChangeDto second = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            host.Write(uow =>
            {
                var row = uow.Set<Entities.Model.NasbNiro.ProjectControl.PcSetting>().FirstOrDefault(s => s.Key == "EdmsRequireRevision");
                if (row == null) uow.Set<Entities.Model.NasbNiro.ProjectControl.PcSetting>().Add(row = AppHost.Add(uow, new Entities.Model.NasbNiro.ProjectControl.PcSetting { Key = "EdmsRequireRevision", Title = "t", Source = "s" }));
                row.Value = 1m;
                uow.SaveChanges();
            });
            ResponseDto<SafetyChangeDto> stale = await Decide(host, Ceo, second.Id, true);
            Assert.IsFalse(stale.Success);
            StringAssert.Contains(stale.Message, "تغییر کرده");
        }

        [TestMethod]
        public async Task A_Request_Expires_After_72_Hours_One_Is_Open_Per_Key_And_Planning_Cannot_Ask_For_Demo_Data()
        {
            AppHost host = Host();
            Assert.IsFalse((await Request(host, Planning, "ProductBank", "DemoData:Enabled", "true")).Success, "the demo-data switch is the admin's");
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, Pmo1, "ProjectControl", "EdmsRequireRevision", "1"));
            Assert.IsFalse((await Request(host, Pmo2, "ProjectControl", "EdmsRequireRevision", "1")).Success, "one open request per key");
            host.Write(uow => { uow.Set<SafetySettingChange>().First(c => c.Id == request.Id).RequestedAt = DateTimeOffset.Now.AddHours(-73); uow.SaveChanges(); });
            ResponseDto<SafetyChangeDto> late = await Decide(host, Ceo, request.Id, true);
            Assert.IsFalse(late.Success);
            Assert.AreEqual("Expired", host.Read(uow => uow.Set<SafetySettingChange>().AsNoTracking().Single(c => c.Id == request.Id).Status.ToString()));
            Assert.AreEqual(0, host.Read(uow => uow.Set<OfInboxItem>().Count(i => i.SourceKey == "safety:" + request.Id && i.ClosedAt == null)), "the alert is closed");
            Assert.IsTrue((await Request(host, Pmo2, "ProjectControl", "EdmsRequireRevision", "1")).Success, "after the expiry a new request is possible");
        }

        [TestMethod]
        public async Task The_Approval_Alert_Does_Not_Reach_The_Requester_When_Other_Approvers_Exist()
        {
            AppHost host = Host();
            host.Write(uow =>
            {
                foreach (int id in new[] { 101, 102 })
                {
                    uow.Set<User>().Add(AppHost.Add(uow, new User { Id = id, UserName = "pmo.user" + id }));
                    uow.Set<UserRole>().Add(new UserRole { UserId = id, RoleId = RoleConstants.PMOResponseRole.Id });
                }
                uow.SaveChanges();
            });
            ClaimsPrincipal requester = AppHost.Principal(101, "pmo.user101", "PMO");
            SafetyChangeDto request = FinanceTestSupport.Ok(await Request(host, requester, "ProjectControl", "EdmsRequireRevision", "1"));
            List<OfInboxItem> items = host.Read(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "safety:" + request.Id).ToList());
            Assert.IsTrue(items.Any(i => i.RecipientUserId == 102));
            Assert.IsFalse(items.Any(i => i.RecipientUserId == 101), "the requester is not asked to approve his own request");
        }

        private static void Seats(AppHost host) => host.Write(uow =>
        {
            foreach (string code in new[] { "CEO-BRIEF", "OPS-DATA" })
                uow.Set<AiSeat>().Add(new AiSeat { Code = code, Title = code, ServiceUserName = "seat." + code.ToLowerInvariant(), Roles = "PMO", Schedule = "daily", PromptVersionCode = "v1", Enabled = true, OutputKind = "x" });
            uow.SaveChanges();
        });

        private static bool SeatOn(AppHost host, string code) => host.Read(uow => uow.Set<AiSeat>().AsNoTracking().Single(s => s.Code == code).Enabled);

        [TestMethod]
        public async Task A_Drafting_Seat_Is_Switched_Off_At_Once_And_Confirmed_Later_A_Control_Seat_Needs_Two_Persons()
        {
            AppHost host = Host();
            Seats(host);
            Assert.IsTrue(SafetyProfile.IsDraftingSeat("CEO-BRIEF"));
            Assert.IsFalse(SafetyProfile.IsDraftingSeat("OPS-DATA"));
            Assert.IsFalse(SafetyProfile.IsDraftingSeat("FIN-ANOMALY"));

            SafetyChangeDto off = FinanceTestSupport.Ok(await Request(host, Pmo1, "AiSeat", "CEO-BRIEF", "0", "خروجی نامناسب"));
            Assert.AreEqual("AppliedAwaitingConfirmation", off.Status);
            Assert.IsFalse(SeatOn(host, "CEO-BRIEF"), "switched off at once by one person");
            Assert.IsTrue(host.Read(uow => uow.Set<AiAuditEvent>().Any(e => e.SeatCode == "CEO-BRIEF")), "the switch is an event of the AI audit");
            Assert.IsFalse((await Decide(host, Pmo1, off.Id, true)).Success, "the second person is another one");
            Assert.AreEqual("Approved", FinanceTestSupport.Ok(await Decide(host, Ceo, off.Id, true)).Status);
            Assert.IsFalse(SeatOn(host, "CEO-BRIEF"));

            // switching ON is always two persons
            SafetyChangeDto on = FinanceTestSupport.Ok(await Request(host, Pmo1, "AiSeat", "CEO-BRIEF", "1"));
            Assert.AreEqual("Pending", on.Status);
            Assert.IsFalse(SeatOn(host, "CEO-BRIEF"));
            FinanceTestSupport.Ok(await Decide(host, Ceo, on.Id, true));
            Assert.IsTrue(SeatOn(host, "CEO-BRIEF"));

            // a control seat: two persons even to switch it off
            SafetyChangeDto control = FinanceTestSupport.Ok(await Request(host, Pmo1, "AiSeat", "OPS-DATA", "0"));
            Assert.AreEqual("Pending", control.Status);
            Assert.IsTrue(SeatOn(host, "OPS-DATA"));
            FinanceTestSupport.Ok(await Decide(host, Ceo, control.Id, true));
            Assert.IsFalse(SeatOn(host, "OPS-DATA"));
            Assert.IsTrue(host.Read(uow => uow.Set<AiAuditEvent>().Any(e => e.SeatCode == "OPS-DATA")), "an approved change of a seat is an event of the AI audit too");
        }

        [TestMethod]
        public async Task A_Seat_Switched_Off_At_Once_And_Not_Confirmed_In_24_Hours_Is_Flagged_To_The_Ceo_And_Rejection_Switches_It_On()
        {
            AppHost host = Host();
            Seats(host);
            SafetyChangeDto off = FinanceTestSupport.Ok(await Request(host, Pmo1, "AiSeat", "CEO-BRIEF", "0"));
            host.Write(uow => { uow.Set<SafetySettingChange>().First(c => c.Id == off.Id).ConfirmDueAt = DateTimeOffset.Now.AddMinutes(-5); uow.SaveChanges(); });
            await host.As(Pmo2, sp => sp.GetRequiredService<ISafetySettingsService>().SweepAsync(CancellationToken.None).ContinueWith(_ => true));
            Assert.IsNotNull(host.Read(uow => uow.Set<SafetySettingChange>().AsNoTracking().Single(c => c.Id == off.Id).FlaggedAt));
            List<OfInboxItem> flag = host.Read(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey == "safety-late:" + off.Id).ToList());
            CollectionAssert.AreEqual(new[] { "CEO" }, flag.Select(i => i.RecipientRole).ToArray());
            // a second person who does not agree switches the seat on again
            Assert.AreEqual("Rejected", FinanceTestSupport.Ok(await Decide(host, Ceo, off.Id, false, "خروجی صندلی لازم است")).Status);
            Assert.IsTrue(SeatOn(host, "CEO-BRIEF"));
        }
    }
}
