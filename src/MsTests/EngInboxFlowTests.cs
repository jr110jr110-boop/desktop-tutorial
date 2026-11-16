using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngInbox;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.EngInbox;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.EngInboxDtos;
using ViewModels.NasbNiro.ProductBankDtos;

namespace MsTests
{
    /// <summary>ENG-F1: the ingest service, the apply step, the roles and the maker-checker rule, on the in-memory host with an in-memory source.</summary>
    [TestClass]
    public class EngInboxFlowTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Eng = AppHost.Principal(2101, "eng.operator", "ENGINEERING");
        private static readonly ClaimsPrincipal Engineer = AppHost.Principal(2102, "engineer.one", "ENGINEER");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(2103, "qc.one", "QC");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(2104, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(2105, "com.one", "COMMERCIAL");

        // the preparer of the sample (a user of the system; the originator) and a second person who has the same roles
        private const int OriginatorId = 2001, OtherId = 2002, ThirdId = 2003;
        private static readonly ClaimsPrincipal Originator = AppHost.Principal(OriginatorId, EngPackageWriter.SampleOriginator, "ENGINEERING", "PMO", "QC", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Other = AppHost.Principal(OtherId, "eng.other", "ENGINEERING", "PMO", "QC", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Third = AppHost.Principal(ThirdId, "eng.third", "ENGINEERING", "PMO", "QC", "CONSTRUCTION");

        private sealed class Env
        {
            public AppHost Host;
            public EngMemorySource Source;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name, bool workCenters = true)
        {
            var src = new EngMemorySource();
            AppHost host = AppHost.WithServices("EngFlow_" + name + "_" + Guid.NewGuid().ToString("N"), s => { s.RemoveAll<IEngineeringSource>(); s.AddSingleton<IEngineeringSource>(src); }).WithoutReadinessGate();
            var e = new Env { Host = host, Source = src, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                AppHost.Add(u, new User { Id = OriginatorId, UserName = EngPackageWriter.SampleOriginator, Email = "o@example.com", IsActive = true });
                AppHost.Add(u, new User { Id = OtherId, UserName = "eng.other", Email = "p@example.com", IsActive = true });
                AppHost.Add(u, new User { Id = ThirdId, UserName = "eng.third", Email = "q@example.com", IsActive = true });
                AppHost.Add(u, new User { Id = 2999, UserName = "gone.user", Email = "g@example.com", IsActive = false });
                if (workCenters)
                {
                    AppHost.Add(u, new MfWorkCenter { Code = "MF-LATHE", Title = "تراش" });
                    AppHost.Add(u, new MfWorkCenter { Code = "MF-QCB", Title = "بازرسی" });
                }
                u.SaveChanges();
            }, Seeder);
            return e;
        }

        private static EngSamplePackage Pkg(Action<EngTables> change = null, int version = 1)
        {
            EngTables t = EngPackageWriter.SampleTables(version);
            change?.Invoke(t);
            return new EngSamplePackage { FolderName = "pkg-v" + version, WorkbookName = EngPackageWriter.WorkbookFileName, Files = t.Files, Workbook = EngPackageWriter.Build(t) };
        }

        private static void SetInfo(EngTables t, string key, object value) => t.Info.Single(r => (string)r[0] == key)[1] = value;

        private static Task<ResponseDto<EngInboxScanResultDto>> Scan(Env e, ClaimsPrincipal who = null) => e.Host.As(who ?? Eng, p => p.GetRequiredService<IEngInboxService>().ScanAsync(Ct));

        private static List<EngInboxPackage> Packages(Env e) => e.Host.Read(u => u.Set<EngInboxPackage>().AsNoTracking().OrderBy(p => p.Id).ToList());

        private static int Count<T>(Env e) where T : class => e.Host.Read(u => u.Set<T>().AsNoTracking().Count());

        // ---- the agents (the master makes the tasks, the engineering agent runs them)
        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++)
            {
                AiSeatJobResult m = await RunSeat(e, "AG-MASTER"); Assert.AreEqual(AiSeatRunStatus.Succeeded, m.Status, m.Error);
                AiSeatJobResult g = await RunSeat(e, "AG-ENG"); Assert.AreEqual(AiSeatRunStatus.Succeeded, g.Status, g.Error);
            }
        }

        // =============================================================================================== ingest

        [TestMethod]
        public async Task A_Valid_Package_Is_Ingested_With_Its_Exact_Line_Counts_And_The_Originator()
        {
            Env e = await NewEnv("ingest");
            e.Source.Add("p1", Pkg());
            var r = await Scan(e);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(1, r.Data.Found); Assert.AreEqual(1, r.Data.Ingested); Assert.AreEqual(0, r.Data.Rejected); Assert.AreEqual(0, r.Data.Duplicates); Assert.AreEqual(0, r.Data.Errors);
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.Ingested, p.Status);
            Assert.AreEqual(OriginatorId, p.OriginatorUserId);
            Assert.AreEqual(12, p.BomCount); Assert.AreEqual(13, p.OpcCount); Assert.AreEqual(5, p.DocCount);
            Assert.AreEqual(12, Count<EngInboxBomLine>(e)); Assert.AreEqual(13, Count<EngInboxOpcLine>(e)); Assert.AreEqual(5, Count<EngInboxDocRef>(e));
            Assert.AreEqual(1, e.Source.Marks.Count);
            Assert.AreEqual(EngSourceOutcome.Ingested, e.Source.Marks[0].Outcome);
            Assert.AreEqual("_Ingested/p1", p.StoredPath);
        }

        [TestMethod]
        public async Task The_Same_Content_Again_Is_A_Duplicate_And_Makes_No_New_Row()
        {
            Env e = await NewEnv("dup");
            e.Source.Add("p1", Pkg());
            await Scan(e);
            e.Source.Add("p2", Pkg());
            var r = await Scan(e);
            Assert.AreEqual(1, r.Data.Duplicates); Assert.AreEqual(0, r.Data.Ingested); Assert.AreEqual(0, r.Data.Rejected);
            Assert.AreEqual(1, Packages(e).Count);
            Assert.AreEqual(EngSourceOutcome.Duplicate, e.Source.Marks.Last().Outcome);
        }

        [TestMethod]
        public async Task The_Same_Version_With_Other_Content_Is_Rejected_As_A_Version_Conflict()
        {
            Env e = await NewEnv("conflict");
            e.Source.Add("p1", Pkg());
            await Scan(e);
            e.Source.Add("p2", Pkg(t => t.Bom[6][4] = 3));   // the quantity of a line
            var r = await Scan(e);
            Assert.AreEqual(1, r.Data.Rejected);
            EngInboxPackage rej = Packages(e).Single(p => p.Status == EngInboxStatus.Rejected);
            Assert.AreEqual("VersionConflict", rej.RejectCode);
            Assert.IsTrue(EngInboxService.FindingsOf(rej.FindingsJson).Any(f => f.Message == EngInboxText.VersionConflict));
            Assert.AreEqual(0, e.Host.Read(u => u.Set<EngInboxBomLine>().Count(l => l.PackageId == rej.Id)), "a rejected package keeps no lines");
        }

        [TestMethod]
        public async Task A_Lower_Version_Is_Rejected_And_A_Higher_Version_Supersedes_The_Lower_One()
        {
            Env e = await NewEnv("versions");
            e.Source.Add("p2", Pkg(null, 2));
            await Scan(e);
            e.Source.Add("p1", Pkg(null, 1));
            var low = await Scan(e);
            Assert.AreEqual(1, low.Data.Rejected);
            Assert.AreEqual("OlderVersion", Packages(e).Single(p => p.Status == EngInboxStatus.Rejected).RejectCode);
            e.Source.Add("p3", Pkg(null, 3));
            var high = await Scan(e);
            Assert.AreEqual(1, high.Data.Ingested);
            List<EngInboxPackage> all = Packages(e);
            EngInboxPackage v2 = all.Single(p => p.PackageVersion == 2 && p.Status != EngInboxStatus.Rejected);
            EngInboxPackage v3 = all.Single(p => p.PackageVersion == 3);
            Assert.AreEqual(EngInboxStatus.Superseded, v2.Status);
            Assert.AreEqual(v3.Id, v2.SupersededByPackageId);
            Assert.AreEqual(EngInboxStatus.Ingested, v3.Status);
        }

        [TestMethod]
        public async Task A_Preparer_Who_Is_Not_An_Active_User_Is_Refused_And_The_Rejected_Package_Keeps_No_Lines()
        {
            Env e = await NewEnv("originator");
            foreach (object who in new object[] { "someone.unknown", "gone.user", null })
            {
                Env x = await NewEnv("originator2");
                x.Source.Add("p", Pkg(t => SetInfo(t, "تهیه‌کننده", who)));
                var r = await Scan(x);
                Assert.AreEqual(1, r.Data.Rejected, who as string);
                EngInboxPackage p = Packages(x).Single();
                Assert.AreEqual(EngInboxStatus.Rejected, p.Status);
                Assert.IsNull(p.OriginatorUserId);
                Assert.IsTrue(EngInboxService.FindingsOf(p.FindingsJson).Any(f => f.Message == EngInboxText.OriginatorUnknown && f.Severity == "error"), who as string);
                Assert.AreEqual(0, Count<EngInboxBomLine>(x) + Count<EngInboxOpcLine>(x) + Count<EngInboxDocRef>(x));
            }
        }

        [TestMethod]
        public async Task A_Failed_Move_Is_Reported_Does_Not_Mark_The_Package_As_Moved_And_Makes_No_Second_Rejected_Row()
        {
            Env e = await NewEnv("move");
            e.Source.FailMoves = true;
            e.Source.Add("bad", Pkg(t => SetInfo(t, "تهیه‌کننده", "someone.unknown")));
            await Scan(e);
            await Scan(e);
            await Scan(e);
            List<EngInboxPackage> rows = Packages(e);
            Assert.AreEqual(1, rows.Count, "the same rejected folder is stored once");
            Assert.IsNull(rows[0].StoredPath, "a failed move is not a stored path");
            StringAssert.Contains(rows[0].StatusNote, "جابه‌جا نشد");
            // a good package whose move fails is ingested once and then a duplicate of itself, never twice
            Env g = await NewEnv("move2");
            g.Source.FailMoves = true;
            g.Source.Add("good", Pkg());
            await Scan(g);
            var again = await Scan(g);
            Assert.AreEqual(1, Packages(g).Count);
            Assert.AreEqual(1, again.Data.Duplicates);
        }

        [TestMethod]
        public async Task An_Over_Long_Field_Is_A_Finding_With_Its_Row_And_The_Rejected_Row_Is_Stored_Cut_To_The_Column()
        {
            Env e = await NewEnv("long");
            string longCode = new string('P', 45);
            e.Source.Add("p", Pkg(t => { SetInfo(t, "کد پلتفرم", longCode); t.Bom[2][3] = new string('x', 350); }));
            var r = await Scan(e);
            Assert.AreEqual(1, r.Data.Rejected); Assert.AreEqual(0, r.Data.Errors);
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.Rejected, p.Status);
            Assert.AreEqual(40, p.PlatformCode.Length);
            var f = EngInboxService.FindingsOf(p.FindingsJson).Where(x => x.Severity == "error").ToList();
            Assert.IsTrue(f.Any(x => x.Sheet == "BOM" && x.Row == 4 && x.Message == "«شرح» حداکثر ۳۰۰ نویسه است."), string.Join(" | ", f.Select(x => x.Sheet + ":" + x.Row + ":" + x.Message)));
            Assert.IsTrue(f.Any(x => x.Sheet == "مشخصات" && x.Message.StartsWith("«کد پلتفرم» بیش از 40 نویسه است (45 نویسه)", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task A_Second_Scan_While_One_Runs_Is_Refused_Without_Scanning()
        {
            Env e = await NewEnv("busy");
            e.Source.Add("p1", Pkg());
            e.Source.Gate = new TaskCompletionSource<bool>();
            Task<ResponseDto<EngInboxScanResultDto>> first = Scan(e);
            await Task.Delay(300);
            var second = await Scan(e);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(EngInboxText.ScanBusy, second.Data.Note);
            Assert.AreEqual(0, second.Data.Found);
            e.Source.Gate.SetResult(true);
            var done = await first;
            Assert.AreEqual(1, done.Data.Ingested);
        }

        // =============================================================================================== roles and dismiss

        [TestMethod]
        public async Task Only_Readers_Read_And_Only_The_Engineering_Unit_And_The_Pmo_Scan_Or_Dismiss()
        {
            Env e = await NewEnv("roles");
            e.Source.Add("p", Pkg(t => SetInfo(t, "تهیه‌کننده", "someone.unknown")));
            await Scan(e);
            EngInboxPackage rej = Packages(e).Single();
            var denied = await e.Host.As(Commercial, p => p.GetRequiredService<IEngInboxService>().GetStatusAsync(Ct));
            Assert.IsFalse(denied.Success); Assert.AreEqual(EngInboxText.NotAllowed, denied.Message);
            foreach (ClaimsPrincipal who in new[] { Engineer, Qc, Ceo })
            {
                var scan = await Scan(e, who);
                Assert.IsFalse(scan.Success); Assert.AreEqual(EngInboxText.NotAllowed, scan.Message, who.Identity.Name);
                var dis = await e.Host.As(who, p => p.GetRequiredService<IEngInboxService>().DismissAsync(new EngInboxDismissDto { Id = rej.Id, Reason = "علت کافی برای کنار گذاشتن بسته", RowVersion = Convert.ToBase64String(rej.RowVersion ?? new byte[8]) }, Ct));
                Assert.IsFalse(dis.Success); Assert.AreEqual(EngInboxText.NotAllowed, dis.Message, who.Identity.Name);
            }
            var read = await e.Host.As(Ceo, p => p.GetRequiredService<IEngInboxService>().GetStatusAsync(Ct));
            Assert.IsTrue(read.Success);
            Assert.IsFalse(read.Data.CanScan); Assert.IsFalse(read.Data.CanDismiss);
        }

        [TestMethod]
        public async Task Dismiss_Needs_A_Reason_A_Row_Version_And_A_Fresh_One()
        {
            Env e = await NewEnv("dismiss");
            e.Source.Add("p", Pkg(t => SetInfo(t, "تهیه‌کننده", "someone.unknown")));
            await Scan(e);
            e.Host.Write(u => { EngInboxPackage x = u.Set<EngInboxPackage>().Single(); x.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; u.SaveChanges(); }, Seeder);   // the in-memory database makes no row versions
            EngInboxPackage rej = Packages(e).Single();
            var listed = await e.Host.As(Eng, p => p.GetRequiredService<IEngInboxService>().GetPackagesAsync(new EngInboxQuery(), Ct));
            string rv = listed.Data.Single().RowVersion;
            Assert.IsFalse(string.IsNullOrEmpty(rv), "the list carries the row version");
            Task<ResponseDto<EngInboxPackageDto>> Dismiss(string reason, string version) => e.Host.As(Eng, p => p.GetRequiredService<IEngInboxService>().DismissAsync(new EngInboxDismissDto { Id = rej.Id, Reason = reason, RowVersion = version }, Ct));
            Assert.AreEqual(EngInboxText.ReasonTooShort, (await Dismiss("کوتاه", rv)).Message);
            Assert.AreEqual(EngInboxText.RowVersionRequired, (await Dismiss("علت کافی برای کنار گذاشتن بسته", "")).Message);
            Assert.AreEqual(EngInboxText.RowVersionRequired, (await Dismiss("علت کافی برای کنار گذاشتن بسته", null)).Message);
            Assert.AreEqual(EngInboxText.Concurrent, (await Dismiss("علت کافی برای کنار گذاشتن بسته", Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }))).Message, "a stale row version");
            var ok = await Dismiss("علت کافی برای کنار گذاشتن بسته", rv);
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(EngInboxStatus.Dismissed, ok.Data.Status);
            Assert.AreEqual(EngInboxText.DismissStatus, (await Dismiss("علت کافی برای کنار گذاشتن بسته", ok.Data.RowVersion)).Message);
        }

        // =============================================================================================== apply (the agent is the maker)

        private static async Task<Env> Applied(string name, EngSamplePackage pkg = null)
        {
            Env e = await NewEnv(name);
            e.Source.Add("p1", pkg ?? Pkg());
            await Scan(e);
            await Settle(e);
            return e;
        }

        [TestMethod]
        public async Task Apply_Makes_The_Product_One_Bom_Per_Assembly_And_The_Routing_All_Under_Review_By_The_Agent_And_Records_Them()
        {
            Env e = await Applied("apply");
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.Applied, p.Status, p.ApplyBlockReason);
            int seat = e.Seats["AG-ENG"].UserId;
            List<PbBomHeader> boms = e.Host.Read(u => u.Set<PbBomHeader>().AsNoTracking().OrderBy(h => h.Code).ToList());
            CollectionAssert.AreEqual(new[] { "TST-1000-BOM", "TST-1100-BOM", "TST-1200-BOM" }, boms.Select(b => b.Code).ToArray());
            Assert.IsTrue(boms.All(b => b.Status == PbBomStatus.UnderReview && b.PreparedByUserId == seat));
            PbOpcRouting routing = e.Host.Read(u => u.Set<PbOpcRouting>().AsNoTracking().Single());
            Assert.AreEqual("TST-1000", routing.PartNumber);
            Assert.AreEqual(PbRoutingStatus.UnderReview, routing.Status);
            Assert.AreEqual(seat, routing.PreparedByUserId);
            PbProduct product = e.Host.Read(u => u.Set<PbProduct>().AsNoTracking().Single());
            Assert.AreEqual("TST-1000", product.Code);
            List<EngInboxAppliedItem> items = e.Host.Read(u => u.Set<EngInboxAppliedItem>().AsNoTracking().OrderBy(a => a.Kind).ThenBy(a => a.EntityId).ToList());
            Assert.AreEqual(5, items.Count);
            Assert.AreEqual(1, items.Count(a => a.Kind == EngInboxAppliedKind.Product && a.EntityId == product.Id));
            Assert.AreEqual(3, items.Count(a => a.Kind == EngInboxAppliedKind.BomHeader));
            Assert.AreEqual(1, items.Count(a => a.Kind == EngInboxAppliedKind.Routing && a.EntityId == routing.Id));
            Assert.IsTrue(items.All(a => a.PackageId == p.Id));
            Assert.AreEqual(product.Id, p.ProductId);
            Assert.AreEqual(routing.Id, p.RoutingId);
            Assert.AreEqual(13, e.Host.Read(u => u.Set<PbOpcStep>().Count(s => s.RoutingId == routing.Id)));
        }

        [TestMethod]
        public async Task A_Second_Run_And_The_Same_Content_Under_A_Higher_Version_Create_Nothing_New()
        {
            Env e = await Applied("twice");
            int boms = Count<PbBomHeader>(e), routings = Count<PbOpcRouting>(e), products = Count<PbProduct>(e), items = Count<EngInboxAppliedItem>(e);
            await Settle(e);
            Assert.AreEqual(boms, Count<PbBomHeader>(e)); Assert.AreEqual(routings, Count<PbOpcRouting>(e)); Assert.AreEqual(products, Count<PbProduct>(e)); Assert.AreEqual(items, Count<EngInboxAppliedItem>(e));
            // version 2 with identical content (QC-kind stages included): the comparison sees no difference, nothing new, no false block
            e.Source.Add("p2", Pkg(null, 2));
            await Scan(e);
            await Settle(e);
            List<EngInboxPackage> all = Packages(e);
            EngInboxPackage v2 = all.Single(p => p.PackageVersion == 2);
            Assert.AreEqual(EngInboxStatus.Applied, v2.Status, v2.ApplyBlockReason);
            StringAssert.Contains(v2.StatusNote, "بدون تغییر");
            Assert.AreEqual(boms, Count<PbBomHeader>(e)); Assert.AreEqual(routings, Count<PbOpcRouting>(e)); Assert.AreEqual(products, Count<PbProduct>(e));
        }

        private static async Task AssertBlockedByChange(string name, Action<EngTables> change, string expectedStart)
        {
            Env e = await Applied(name);
            int boms = Count<PbBomHeader>(e), routings = Count<PbOpcRouting>(e), items = Count<EngInboxAppliedItem>(e);
            // a person has taken the first revision over (released it): a change is an ECO matter, never a silent new draft
            e.Host.Write(u => { foreach (PbBomHeader h in u.Set<PbBomHeader>().ToList()) h.Status = PbBomStatus.Released; foreach (PbOpcRouting r in u.Set<PbOpcRouting>().ToList()) r.Status = PbRoutingStatus.Approved; u.SaveChanges(); }, Seeder);
            e.Source.Add("p2", Pkg(change, 2));
            await Scan(e);
            await Settle(e);
            EngInboxPackage v2 = Packages(e).Single(p => p.PackageVersion == 2);
            Assert.AreEqual(EngInboxStatus.ApplyBlocked, v2.Status, name);
            StringAssert.StartsWith(v2.ApplyBlockReason, expectedStart, name + ": " + v2.ApplyBlockReason);
            StringAssert.Contains(v2.ApplyBlockReason, "ECO", name);
            Assert.AreEqual(boms, Count<PbBomHeader>(e), name); Assert.AreEqual(routings, Count<PbOpcRouting>(e), name); Assert.AreEqual(items, Count<EngInboxAppliedItem>(e), name);
        }

        [TestMethod]
        public Task A_Drawing_Revision_Only_Change_Of_A_Released_Bom_Is_Blocked_With_The_Eco_Text() =>
            AssertBlockedByChange("drawing", t => t.Bom[2][9] = "B", "BOM TST-1100-BOM بازنگری");

        [TestMethod]
        public Task A_Quantity_Change_Of_A_Released_Bom_Is_Blocked_With_The_Eco_Text() =>
            AssertBlockedByChange("qty", t => t.Bom[10][4] = 2, "BOM TST-1000-BOM بازنگری");

        [TestMethod]
        public Task A_Run_Time_Only_Change_Of_An_Approved_Routing_Is_Blocked_With_The_Eco_Text() =>
            AssertBlockedByChange("run", t => t.Opc[0][5] = 121, "چارت OPC TST-1000 بازنگری");

        [TestMethod]
        public async Task An_Unknown_Work_Centre_Blocks_The_Package_With_The_Exact_Reason_And_Creates_Nothing()
        {
            Env e = await NewEnv("nowc", workCenters: false);
            e.Host.Write(u => { AppHost.Add(u, new MfWorkCenter { Code = "MF-LATHE", Title = "تراش" }); u.SaveChanges(); }, Seeder);
            e.Source.Add("p1", Pkg());
            await Scan(e);
            await Settle(e);
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.ApplyBlocked, p.Status);
            StringAssert.StartsWith(p.ApplyBlockReason, "مرکز کار با این کدها در سیستم تعریف نشده است: MF-QCB.");
            Assert.AreEqual(0, Count<PbBomHeader>(e) + Count<PbOpcRouting>(e) + Count<PbProduct>(e) + Count<EngInboxAppliedItem>(e));
            Assert.AreEqual(1, p.ApplyAttempts >= 1 ? 1 : 0);
        }

        [TestMethod]
        public async Task A_Block_On_The_Second_Assembly_Leaves_Nothing_From_This_Run()
        {
            Env e = await NewEnv("second");
            // the second assembly (TST-1100) already has a released BOM with other content
            e.Host.Write(u =>
            {
                PbBomHeader h = AppHost.Add(u, new PbBomHeader { Code = "TST-1100-BOM", Revision = 0, Status = PbBomStatus.Released, AssemblyPartNumber = "TST-1100", PreparedByUserId = OtherId });
                u.SaveChanges();
                AppHost.Add(u, new PbBomLine { BomHeaderId = h.Id, SortOrder = 1, PartNumber = "TST-9999", PartName = "other", Qty = 1, Unit = "عدد", MakeOrBuy = PbMakeOrBuy.Make });
                u.SaveChanges();
            }, Seeder);
            e.Source.Add("p1", Pkg());
            await Scan(e);
            await Settle(e);
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.ApplyBlocked, p.Status);
            StringAssert.StartsWith(p.ApplyBlockReason, "BOM TST-1100-BOM بازنگری");
            Assert.AreEqual(1, Count<PbBomHeader>(e), "only the one that was there");
            Assert.AreEqual(0, Count<PbProduct>(e) + Count<PbOpcRouting>(e) + Count<EngInboxAppliedItem>(e));
        }

        [TestMethod]
        public async Task A_Blocked_Package_Does_Not_Open_A_New_Apply_Task_Every_Cycle_And_A_Switched_Off_Agent_Leaves_It_Waiting()
        {
            Env e = await NewEnv("tasks", workCenters: false);
            e.Host.Write(u => { AppHost.Add(u, new MfWorkCenter { Code = "MF-LATHE", Title = "تراش" }); u.SaveChanges(); }, Seeder);
            e.Source.Add("p1", Pkg());
            await Scan(e);
            await Settle(e);
            int applyTasks() => e.Host.Read(u => u.Set<AgentTask>().Count(t => t.StepCode == EngInboxCodes.Apply));
            int first = applyTasks();
            Assert.AreEqual(1, first);
            await Settle(e); await Settle(e);
            Assert.AreEqual(first, applyTasks(), "the retry waits for its time; no new task every cycle");
            // a fix task for the blocked package exists for people (the input step), none that asks a person to apply
            Assert.AreEqual(1, e.Host.Read(u => u.Set<AgentTask>().Count(t => t.StepCode == EngInboxCodes.Fix && t.State < AgentTaskState.Done)));
            Assert.AreEqual(0, e.Host.Read(u => u.Set<AgentTask>().Count(t => t.StepCode == EngInboxCodes.Apply && t.IsFallback)));

            // the apply step switched off: a new ingested package stays Ingested and no task for a person appears
            Env o = await NewEnv("off");
            o.Host.Write(u => { u.Set<AgentStepSetting>().Add(new AgentStepSetting { StepCode = EngInboxCodes.Apply, Enabled = false, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            o.Source.Add("p1", Pkg());
            await Scan(o);
            await Settle(o);
            Assert.AreEqual(EngInboxStatus.Ingested, Packages(o).Single().Status);
            Assert.AreEqual(0, o.Host.Read(u => u.Set<AgentTask>().Count(t => t.StepCode == EngInboxCodes.Apply)));
            var status = await o.Host.As(Eng, p => p.GetRequiredService<IEngInboxService>().GetStatusAsync(Ct));
            Assert.IsNotNull(status.Data.ApplyPausedReason);
        }

        [TestMethod]
        public async Task A_Project_Package_Is_Linked_To_Its_Contract_And_Work_Order()
        {
            Env e = await NewEnv("project");
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-ENG", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-20) });
                u.SaveChanges();
                AppHost.Add(u, new WorkOrder { Code = "WO-ENG", Title = "پمپ یک", Count = 1, ContractId = c.Id, EquipmentId = 7 });
                u.SaveChanges();
            }, Seeder);
            e.Source.Add("p1", Pkg(t =>
            {
                SetInfo(t, "نوع بسته", "پروژه");
                t.Info.RemoveAll(r => (string)r[0] == "کد پلتفرم" || (string)r[0] == "استاندارد");
                t.Info.Add(new object[] { "کد قرارداد", "C-ENG", null });
                t.Info.Add(new object[] { "کد پروژه", "WO-ENG", null });
            }));
            var scan = await Scan(e);
            Assert.AreEqual(1, scan.Data.Ingested, string.Join(" | ", Packages(e).SelectMany(p => EngInboxService.FindingsOf(p.FindingsJson)).Select(f => f.Sheet + ":" + f.Row + ":" + f.Message)));
            await Settle(e);
            EngInboxPackage p = Packages(e).Single();
            Assert.AreEqual(EngInboxStatus.Applied, p.Status, p.ApplyBlockReason);
            Assert.IsNotNull(p.LinkedContractId);
            Assert.IsNotNull(p.LinkedWorkOrderId);
            Assert.AreEqual(0, Count<PbBomHeader>(e) + Count<PbOpcRouting>(e), "a project package makes no product bank record");
        }

        // =============================================================================================== maker is not checker

        private static async Task<(int Bom, int Routing)> OwnedIds(Env e)
        {
            await Task.CompletedTask;
            return e.Host.Read(u => (u.Set<PbBomHeader>().Where(h => h.Code == "TST-1000-BOM").Select(h => h.Id).Single(), u.Set<PbOpcRouting>().Select(r => r.Id).Single()));
        }

        [TestMethod]
        public async Task The_Originator_Of_A_Package_Cannot_Check_Approve_Sign_Release_Or_Publish_What_The_Agent_Made_And_Another_Person_Can()
        {
            Env e = await Applied("maker");
            Assert.AreEqual(EngInboxStatus.Applied, Packages(e).Single().Status);
            var (bomId, routingId) = await OwnedIds(e);
            string msg = EngInboxText.OriginatorCannotCheck;

            // routing: check
            var viaOriginator = await e.Host.As(Originator, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = routingId }, Ct));
            Assert.IsFalse(viaOriginator.Success); Assert.AreEqual(msg, viaOriginator.Message);
            var checkedBy = await e.Host.As(Other, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = routingId }, Ct));
            Assert.IsTrue(checkedBy.Success, checkedBy.Message);
            // routing: approve
            var approveOriginator = await e.Host.As(Originator, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = routingId }, Ct));
            Assert.IsFalse(approveOriginator.Success); Assert.AreEqual(msg, approveOriginator.Message);
            var approved = await e.Host.As(Third, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = routingId }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            // routing: publish
            var publishOriginator = await e.Host.As(Originator, p => p.GetRequiredService<IPbRoutingService>().PublishToMesAsync(new PbOpcActionDto { Id = routingId }, Ct));
            Assert.IsFalse(publishOriginator.Success); Assert.AreEqual(msg, publishOriginator.Message);

            // BOM: sign (the first step is the control signature)
            var signOriginator = await e.Host.As(Originator, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = bomId, Step = PbBomSignature.Checked }, Ct));
            Assert.IsFalse(signOriginator.Success); Assert.AreEqual(msg, signOriginator.Message);
            var signOther = await e.Host.As(Other, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = bomId, Step = PbBomSignature.Checked }, Ct));
            Assert.IsTrue(signOther.Success, signOther.Message);
            // BOM: release (a BOM that is approved)
            e.Host.Write(u => { u.Set<PbBomHeader>().Single(h => h.Id == bomId).Status = PbBomStatus.Approved; u.SaveChanges(); }, Seeder);
            var releaseOriginator = await e.Host.As(Originator, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = bomId }, Ct));
            Assert.IsFalse(releaseOriginator.Success); Assert.AreEqual(msg, releaseOriginator.Message);
        }

        [TestMethod]
        public async Task An_Ai_Seat_Is_Refused_Outside_The_Apply_Step_On_Save_And_Submit_And_Inside_It_On_Every_Approval_Line()
        {
            Env e = await Applied("ai");
            var (bomId, routingId) = await OwnedIds(e);
            AiActingUser seat = e.Seats["AG-ENG"];
            var refusals = new List<string>();
            async Task Inside(string step)
            {
                AiSeatJobResult r = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "AG-ENG", async sp =>
                {
                    using (step == null ? null : AgentStepContext.Enter(step))
                    {
                        var bom = sp.GetRequiredService<IPbBomService>();
                        var routing = sp.GetRequiredService<IPbRoutingService>();
                        refusals.Add((await bom.SaveAsync(new SavePbBomDto { AssemblyPartNumber = "TST-2000", Title = "x", Lines = new List<SavePbBomLineDto>() }, Ct)).Message);
                        refusals.Add((await bom.SubmitAsync(new PbBomActionDto { Id = bomId }, Ct)).Message);
                        refusals.Add((await routing.SaveAsync(new SavePbOpcDto { PartNumber = "TST-2000", Steps = new List<SavePbOpcStepDto>() }, Ct)).Message);
                        refusals.Add((await routing.SubmitAsync(new PbOpcActionDto { Id = routingId }, Ct)).Message);
                        refusals.Add((await bom.SignAsync(new PbSignDto { Id = bomId, Step = PbBomSignature.Checked }, Ct)).Message);
                        refusals.Add((await bom.ReleaseAsync(new PbBomActionDto { Id = bomId }, Ct)).Message);
                        refusals.Add((await routing.CheckAsync(new PbOpcActionDto { Id = routingId }, Ct)).Message);
                        refusals.Add((await routing.ApproveAsync(new PbOpcActionDto { Id = routingId }, Ct)).Message);
                        refusals.Add((await routing.PublishToMesAsync(new PbOpcActionDto { Id = routingId }, Ct)).Message);
                    }
                    return AiSeatJobResult.Done("ok");
                });
                Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            }
            await Inside(null);
            Assert.AreEqual(9, refusals.Count);
            Assert.IsTrue(refusals.All(m => m == PbMessages.AiNeverApproves), "outside the apply step every call is refused: " + string.Join(" | ", refusals));
            refusals.Clear();
            await Inside(EngInboxCodes.Apply);
            Assert.AreEqual(9, refusals.Count);
            // inside the apply step the maker calls (save, submit) get past the AI rule; the approval lines never do
            Assert.AreNotEqual(PbMessages.AiNeverApproves, refusals[0]);
            Assert.AreNotEqual(PbMessages.AiNeverApproves, refusals[1]);
            Assert.AreNotEqual(PbMessages.AiNeverApproves, refusals[2]);
            Assert.AreNotEqual(PbMessages.AiNeverApproves, refusals[3]);
            for (int i = 4; i < 9; i++) Assert.AreEqual(PbMessages.AiNeverApproves, refusals[i], "line " + i);
        }
    }
}
