using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Production.Dispatch;
using Services.NasbNiro.Remote;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>Production release P2 part D: the planning seat MF-DISPATCH (rules, plan, notes) and the remote-access policy.</summary>
    [TestClass]
    public class DispatchTests
    {
        private static MfDispatchRules.Step S(int id, string stage, decimal? daysToNeed, decimal daysOfWork, int priority = 0, bool kit = false, string order = "PO-1") => new MfDispatchRules.Step
        {
            OperationId = id, OrderId = id, OrderNumber = order, Sequence = 10, WorkCenterId = 1, Stage = stage, HoursLeft = 8, DaysToNeed = daysToNeed, DaysOfWork = daysOfWork, Priority = priority, KitShortage = kit,
            NeedDate = daysToNeed.HasValue ? new DateTime(2026, 11, 20) : null,
        };

        // ------------------------------------------------------------------------------------------ rules

        [TestMethod]
        public void The_Critical_Ratio_Is_Days_Left_Over_Days_Of_Work()
        {
            Assert.AreEqual(0.5m, MfDispatchRules.CriticalRatio(2, 4));
            Assert.AreEqual(2m, MfDispatchRules.CriticalRatio(8, 4));
            Assert.AreEqual(0m, MfDispatchRules.CriticalRatio(-3, 4), "a need date in the past");
            Assert.AreEqual(20m, MfDispatchRules.CriticalRatio(2, 0), "nothing left: a tenth of a day");
            Assert.IsNull(MfDispatchRules.CriticalRatio(null, 4));
            Assert.AreEqual(-2m, MfDispatchRules.Slack(2, 4));
            Assert.AreEqual(MfDispatchRules.RiskLate, MfDispatchRules.Risk(0.99m));
            Assert.AreEqual(MfDispatchRules.RiskAt, MfDispatchRules.Risk(1.1m));
            Assert.AreEqual(MfDispatchRules.RiskOnTime, MfDispatchRules.Risk(1.2m));
            Assert.AreEqual(MfDispatchRules.RiskNoDate, MfDispatchRules.Risk(null));
        }

        [TestMethod]
        public void The_Queue_Keeps_The_Running_Job_And_Puts_The_Late_Ready_Job_First()
        {
            var ordered = MfDispatchRules.Order(new[]
            {
                S(1, MfDispatchRules.StageReady, 20, 4, priority: 1),          // plenty of time, top project priority
                S(2, MfDispatchRules.StageUpcoming, 1, 4),                    // late, but an earlier step is open
                S(3, MfDispatchRules.StageReady, 2, 4, priority: 5),           // late unless it moves up
                S(4, MfDispatchRules.StageRunning, 30, 1),                     // on the machine now
                S(5, MfDispatchRules.StageReady, 1, 4, kit: true),             // later still, but its material is missing
                S(6, MfDispatchRules.StageReady, null, 4, priority: 2),        // no need date
            }).Select(s => s.OperationId).ToArray();
            CollectionAssert.AreEqual(new[] { 4, 3, 1, 6, 5, 2 }, ordered);
        }

        [TestMethod]
        public void The_Level_Of_A_Centre_Follows_Its_Load()
        {
            Assert.AreEqual(MfDispatchRules.LevelBottleneck, MfDispatchRules.Level(90, 80, 1, 0));
            Assert.AreEqual(MfDispatchRules.LevelBusy, MfDispatchRules.Level(72, 80, 1, 0));
            Assert.AreEqual(MfDispatchRules.LevelNormal, MfDispatchRules.Level(40, 80, 1, 0));
            Assert.AreEqual(MfDispatchRules.LevelIdle, MfDispatchRules.Level(0, 80, 0, 0));
            Assert.AreEqual(MfDispatchRules.LevelBottleneck, MfDispatchRules.Level(5, 0, 0, 0), "work but no capacity at all");
            Assert.AreEqual(112.5m, MfDispatchRules.LoadPercent(90, 80));
            Assert.IsNull(MfDispatchRules.LoadPercent(5, 0));
        }

        [TestMethod]
        public void The_Reason_Is_Persian_With_Persian_Digits()
        {
            string late = MfDispatchRules.Reason(S(3, MfDispatchRules.StageReady, 2, 4, priority: 5), 2);
            StringAssert.StartsWith(late, "رتبه ۲:");
            StringAssert.Contains(late, "نسبت بحرانی ۰٫۵");
            StringAssert.Contains(late, "۲ روز کاری دیر می‌شود");
            StringAssert.Contains(late, "اولویت پروژه ۵");
            StringAssert.Contains(MfDispatchRules.Reason(S(4, MfDispatchRules.StageRunning, 30, 1), 1), "هم‌اکنون روی ماشین");
            StringAssert.Contains(MfDispatchRules.Reason(S(5, MfDispatchRules.StageReady, null, 1, kit: true), 3), "کیت مواد کامل نیست");
            Assert.AreEqual("−۱۲٫۵", MfDispatchRules.Fa(-12.5m));
        }

        // ------------------------------------------------------------------------------------------ the plan and the seat

        private sealed class User : IInventoryUserContext
        {
            public int UserId { get; set; } = 7;
            public string Roles { get; set; } = "PLANNING";
            public bool IsInRole(string role) => (role ?? "").Split(',').Select(r => r.Trim()).Any(r => Roles.Split(',').Contains(r));
        }

        private sealed class Inbox : INotificationService
        {
            public List<NotifyRequest> Sent { get; } = new List<NotifyRequest>();
            public Task NotifyAsync(NotifyRequest request, CancellationToken cancellationToken) { Sent.Add(request); return Task.CompletedTask; }
            public Task CloseAsync(string sourceKey, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task GateAsync(string sourceKey, bool waiting, NotifyRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        /// <summary>
        /// One milling centre (8 hours a day). Order A is needed in 2 days and still needs 40 hours here (late unless it moves up); order B is needed in 60 days; order C is on the machine.
        /// The 10-day demand (40 + 16 + 4 = 60 hours) is over the capacity of the working days in the horizon only when the centre works fewer than 8 of them; it is made a bottleneck for
        /// certain by a centre of 4 hours a day.
        /// </summary>
        private static async Task<(ServiceProvider Provider, User User, Inbox Inbox, DateTime Today)> SeedAsync()
        {
            var user = new User();
            var inbox = new Inbox();
            ServiceProvider provider = CeoOverviewServiceTests.InMemory(s =>
            {
                s.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                s.AddScoped<IInventoryUserContext>(_ => user);
                s.AddSingleton<INotificationService>(inbox);
                s.AddScoped<IAiNoteWriter, AiNoteWriter>();
                s.AddScoped<IMfDispatchService, MfDispatchService>();
                s.AddScoped<MfDispatchSeatJob>();
            });
            DateTime today = DateTime.Today;
            using IServiceScope scope = provider.CreateScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Set<MfWorkCenter>().Add(new MfWorkCenter { Id = 1, Code = "MILL", Title = "فرز CNC", Kind = MfWorkCenterKind.Machine, ShiftsPerDay = 1, HoursPerShift = 4, EfficiencyPercent = 100, UtilisationPercent = 100, ParallelResources = 1, IsActive = true });
            var near = new WorkOrder { Id = 1, Code = "W-1", Title = "w1", ContractId = 1 };
            near.SetStartDate(new DateTimeOffset(today.AddDays(2)));
            var far = new WorkOrder { Id = 2, Code = "W-2", Title = "w2", ContractId = 1 };
            far.SetStartDate(new DateTimeOffset(today.AddDays(60)));
            db.Set<WorkOrder>().AddRange(near, far);
            db.Set<MfProductionOrder>().AddRange(
                new MfProductionOrder { Id = 1, Number = "PO-A", WorkOrderId = 1, ContractId = 1, Quantity = 1, Status = MfOrderStatus.Released },
                new MfProductionOrder { Id = 2, Number = "PO-B", WorkOrderId = 2, ContractId = 1, Quantity = 1, Status = MfOrderStatus.Released },
                new MfProductionOrder { Id = 3, Number = "PO-C", WorkOrderId = 2, ContractId = 1, Quantity = 1, Status = MfOrderStatus.InProgress });
            db.Set<MfOperation>().AddRange(
                new MfOperation { Id = 11, OrderId = 1, Sequence = 10, Title = "فرز کاری", WorkCenterId = 1, RunHoursPlanned = 40, Status = MfOperationStatus.Ready },
                new MfOperation { Id = 21, OrderId = 2, Sequence = 10, Title = "فرز کاری", WorkCenterId = 1, RunHoursPlanned = 16, Status = MfOperationStatus.Ready },
                new MfOperation { Id = 22, OrderId = 2, Sequence = 20, Title = "سوراخ‌کاری", WorkCenterId = 1, RunHoursPlanned = 2, Status = MfOperationStatus.Waiting },
                new MfOperation { Id = 31, OrderId = 3, Sequence = 10, Title = "فرز کاری", WorkCenterId = 1, RunHoursPlanned = 4, Status = MfOperationStatus.InProgress });
            await db.SaveChangesAsync();
            return (provider, user, inbox, today);
        }

        [TestMethod]
        public async Task The_Plan_Ranks_The_Queue_Finds_The_Bottleneck_And_Proposes()
        {
            var (provider, user, _, today) = await SeedAsync();
            using (provider)
            {
                using IServiceScope scope = provider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMfDispatchService>();
                user.Roles = "OPERATOR";
                Assert.IsFalse((await service.GetPlanAsync(new AiDispatchQuery(), default)).Success, "an operator does not read the plan");
                user.Roles = "PLANNING";
                Assert.IsFalse((await service.GetPlanAsync(new AiDispatchQuery { HorizonDays = 61 }, default)).Success);
                DispatchPlanDto plan = (await service.GetPlanAsync(new AiDispatchQuery(), default)).Data;

                Assert.AreEqual(3, plan.OpenOrders);
                DispatchCenterDto mill = plan.Centers.Single(c => c.Code == "MILL");
                CollectionAssert.AreEqual(new[] { "PO-C/10", "PO-A/10", "PO-B/10", "PO-B/20" }, mill.Queue.Select(q => q.Name).ToArray());
                CollectionAssert.AreEqual(new[] { MfDispatchRules.StageRunning, MfDispatchRules.StageReady, MfDispatchRules.StageReady, MfDispatchRules.StageUpcoming }, mill.Queue.Select(q => q.Stage).ToArray());
                DispatchItemDto a = mill.Queue[1];
                Assert.AreEqual(MfDispatchRules.RiskLate, a.Risk, "40 hours at 4 hours a day cannot be done in 2 days");
                Assert.AreEqual(40m, a.HoursLeft);
                Assert.IsTrue(a.SlackDays < 0, "order A has no day to spare");
                Assert.IsTrue(a.CriticalRatio < 1m);
                Assert.AreEqual(MfDispatchRules.RiskOnTime, mill.Queue[2].Risk);
                Assert.AreEqual(2, mill.ReadyCount);
                Assert.AreEqual(1, mill.RunningCount);
                Assert.AreEqual(56m, mill.ReadyHours);
                Assert.AreEqual(1, mill.LateRiskCount);
                Assert.AreEqual(MfDispatchRules.LevelBottleneck, mill.Level, "more demand than a centre of 4 hours a day has in 10 days");
                Assert.IsNotNull(plan.BottleneckText);
                StringAssert.Contains(plan.BottleneckText, "گلوگاه");
                Assert.IsTrue(plan.Suggestions.Any(s => s.Kind == "bottleneck" && s.WorkCenterId == 1 && s.Severity == 1));
                Assert.IsTrue(plan.Suggestions.Any(s => s.Kind == "late-risk" && s.OperationId == 11 && s.Link == "/production/orders/1"));
                Assert.IsFalse(plan.Suggestions.Any(s => s.OperationId == 21 && s.Kind == "late-risk"), "order B has time");
                Assert.IsTrue(plan.Method.Contains("فقط پیشنهاد"));
                Assert.AreEqual(today, plan.AsOf);
            }
        }

        [TestMethod]
        public async Task The_Seat_Leaves_Notes_Once_And_Changes_Nothing()
        {
            var (provider, _, inbox, today) = await SeedAsync();
            using (provider)
            {
                async Task<AiSeatJobResult> Run()
                {
                    using IServiceScope scope = provider.CreateScope();
                    return await scope.ServiceProvider.GetRequiredService<MfDispatchSeatJob>().RunAsync(new AiSeatRunContext { AsOf = today, RunId = 1 }, default);
                }
                AiSeatJobResult first = await Run();
                Assert.AreEqual(AiSeatRunStatus.Succeeded, first.Status);
                StringAssert.Contains(first.Summary, "notes=3");
                AiSeatJobResult second = await Run();
                StringAssert.Contains(second.Summary, "notes=0", "the same day: nothing is repeated");

                using IServiceScope s2 = provider.CreateScope();
                ApplicationDbContext db = s2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                List<AiNote> notes = await db.Set<AiNote>().AsNoTracking().OrderBy(n => n.Id).ToListAsync();
                CollectionAssert.AreEqual(new[] { MfDispatchSeatJob.RulePlan, MfDispatchSeatJob.BottleneckRule(today), MfDispatchSeatJob.RuleLateRisk }, notes.Select(n => n.RuleCode).ToArray());
                Assert.IsTrue(notes.All(n => n.SeatCode == "MF-DISPATCH" && n.Verified));
                Assert.AreEqual(11, notes[2].TargetId);
                StringAssert.Contains(notes[0].Text, "سفارش باز: ۳");
                Assert.AreEqual(3, inbox.Sent.Count);
                Assert.IsTrue(inbox.Sent.All(n => n.Roles == MfDispatchSeatJob.Recipients));
                // nothing of the plan was written to the orders and operations
                Assert.IsTrue(await db.Set<MfOperation>().AllAsync(o => o.PlannedStart == null));
                Assert.AreEqual(MfOperationStatus.Ready, (await db.Set<MfOperation>().FirstAsync(o => o.Id == 11)).Status);
            }
        }

        [TestMethod]
        public async Task The_Floor_Overview_Shows_The_Numbers_And_What_Waits()
        {
            var (provider, user, _, _) = await SeedAsync();
            using (provider)
            {
                using IServiceScope scope = provider.CreateScope();
                ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 1, ProgramId = 1, Revision = 1, Status = MfNcRevisionStatus.Submitted, FileName = "1.h", Sha256 = "x", CreateDate = DateTimeOffset.Now });
                db.Set<MfOperation>().Add(new MfOperation { Id = 99, OrderId = 2, Sequence = 30, Title = "کنترل", WorkCenterId = 1, Status = MfOperationStatus.QcHold });
                await db.SaveChangesAsync();
                var service = scope.ServiceProvider.GetRequiredService<IMfDispatchService>();
                user.Roles = "PRODUCTIONMANAGER";
                FloorOverviewDto floor = (await service.GetFloorAsync(true, default)).Data;
                Assert.IsTrue(floor.Remote);
                CollectionAssert.AreEqual(new[] { "running", "alarms", "utilisation", "late" }, floor.Tiles.Select(t => t.Key).ToArray());
                Assert.AreEqual("—", floor.Tiles[0].ValueText, "no monitoring yet");
                CollectionAssert.AreEqual(new[] { "nc-revisions", "qc-hold" }, floor.Waiting.Select(w => w.Key).ToArray());
                Assert.AreEqual(1, floor.Waiting[0].Count);
                Assert.AreEqual("MILL", floor.Centers.Single().Code);
                Assert.IsTrue(floor.Centers.Single().Queue.All(q => q.Stage != MfDispatchRules.StageUpcoming));
                Assert.IsTrue(floor.Suggestions.Count > 0);
            }
        }

        // ------------------------------------------------------------------------------------------ remote access

        private static RemoteAccessPolicy Policy(params (string Key, string Value)[] settings)
        {
            var d = settings.ToDictionary(s => s.Key, s => s.Value);
            return RemoteAccessPolicy.From(new ConfigurationBuilder().AddInMemoryCollection(d).Build());
        }

        private static readonly IPAddress Lan = IPAddress.Parse("192.168.10.15");
        private static readonly IPAddress Vpn = IPAddress.Parse("10.8.0.6");
        private static readonly IPAddress Internet = IPAddress.Parse("185.20.30.40");

        [TestMethod]
        public void Remote_Access_Is_Off_By_Default()
        {
            RemoteAccessPolicy p = Policy();
            Assert.IsFalse(p.Enabled);
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(Internet, "POST", "/api/v1/production/dnc/revisions", true, new[] { "OPERATOR" }));
        }

        [TestMethod]
        public void Remote_Access_Serves_The_Lan_And_The_Vpn_Only_Read_Only_And_Only_The_Allowed_Roles()
        {
            RemoteAccessPolicy p = Policy(("Remote:Enabled", "true"), ("Remote:VpnNetworks", "10.8.0.0/24"));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(Lan, "POST", "/api/v1/production/dnc/revisions", true, new[] { "ENGINEER" }));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Loopback, "POST", "/api/v1/x", true, new string[0]));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Parse("10.9.0.6"), "GET", "/api/v1/x", true, new[] { "ENGINEER" }), "10/8 outside the VPN pool is the company network");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Parse("::ffff:192.168.10.15"), "GET", "/", false, null), "IPv4-mapped");

            Assert.AreEqual(RemoteAccessPolicy.Verdict.OutsideNetwork, p.Decide(Internet, "GET", "/", false, null), "the open internet gets nothing, not even the page");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.OutsideNetwork, p.Decide(IPAddress.Parse("2001:db8::1"), "GET", "/", false, null));

            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "GET", "/production/floor", false, null), "the page from the VPN");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "POST", "/api/v1/accounts/login", false, null), "signing in");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "GET", "/api/v1/production/dispatch/floor", true, new[] { "CEO" }));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "POST", "/api/v1/ai/voice", true, new[] { "CEO" }), "the voice assistant of the CEO");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.ReadOnly, p.Decide(Vpn, "POST", "/api/v1/production/dnc/revisions/action", true, new[] { "PRODUCTIONMANAGER" }), "no approval from outside");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.RoleNotAllowed, p.Decide(Vpn, "GET", "/api/v1/production/dispatch/floor", true, new[] { "OPERATOR" }));
            StringAssert.Contains(RemoteAccessPolicy.Message(RemoteAccessPolicy.Verdict.ReadOnly), "فقط برای مشاهده");
        }

        [TestMethod]
        public void Remote_Access_Knows_Ipv6_Lan_Proxies_And_The_Status_Address()
        {
            RemoteAccessPolicy p = Policy(("Remote:Enabled", "true"), ("Remote:VpnNetworks", "10.8.0.0/24"));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Parse("fe80::1c2b:3a4d%12"), "POST", "/api/v1/x", true, new[] { "ENGINEER" }), "IPv6 link-local on the LAN");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Parse("fd12:3456::9"), "POST", "/api/v1/x", true, new[] { "ENGINEER" }), "IPv6 unique-local on the LAN");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.OutsideNetwork, p.Decide(IPAddress.Loopback, "GET", "/", false, null, forwarded: true), "a proxy on the server hides the sender");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.Decide(IPAddress.Loopback, "GET", "/", false, null, forwarded: false));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "GET", "/api/v1/remote/status", true, new[] { "OPERATOR" }), "a refused role still learns why");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.OutsideNetwork, p.DecideNetwork(Internet), "the first gate: before swagger and CORS");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Local, p.DecideNetwork(Vpn));
            Assert.IsTrue(RemoteAccessPolicy.TryRange("2001:db8::/32", out var r6));
            Assert.IsTrue(r6.Contains(IPAddress.Parse("2001:db8:1::5").GetAddressBytes()));
            Assert.IsFalse(r6.Contains(IPAddress.Parse("2001:db9::5").GetAddressBytes()));
        }

        [TestMethod]
        public void A_Centre_With_Work_And_No_Capacity_Is_The_Worst()
        {
            var none = new DispatchCenterDto { DemandHours = 5, CapacityHours = 0, LoadPercent = null };
            var full = new DispatchCenterDto { DemandHours = 90, CapacityHours = 80, LoadPercent = 112.5m };
            var empty = new DispatchCenterDto { DemandHours = 0, CapacityHours = 0, LoadPercent = null };
            CollectionAssert.AreEqual(new[] { none, full, empty }, new[] { empty, full, none }.OrderByDescending(MfDispatchService.Severity).ToArray());
        }

        [TestMethod]
        public void Remote_Access_Settings_Are_Read_Strictly()
        {
            RemoteAccessPolicy p = Policy(("Remote:Enabled", "true"), ("Remote:VpnNetworks:0", "10.8.0.0/24"), ("Remote:VpnNetworks:1", "0.0.0.0/x"), ("Remote:ReadOnly", "false"), ("Remote:Roles", "ceo; pmo"));
            Assert.AreEqual(1, p.Vpn.Count, "the bad entry is left out, never opened");
            Assert.AreEqual(1, p.Problems.Count);
            CollectionAssert.AreEqual(new[] { "CEO", "PMO" }, p.Roles.ToArray());
            Assert.AreEqual(RemoteAccessPolicy.Verdict.Remote, p.Decide(Vpn, "POST", "/api/v1/pc/x", true, new[] { "PMO" }), "read-only switched off");
            Assert.AreEqual(RemoteAccessPolicy.Verdict.RoleNotAllowed, p.Decide(Vpn, "GET", "/api/v1/pc/x", true, new[] { "PRODUCTIONMANAGER" }));
            Assert.IsFalse(RemoteAccessPolicy.TryCidr("10.1/8", out _, out _));
            Assert.IsTrue(RemoteAccessPolicy.TryCidr("10.8.0.9/24", out uint n, out uint m));
            Assert.AreEqual(0x0A080000u, n);
            Assert.AreEqual(0xFFFFFF00u, m);
            RemoteAccessPolicy none = Policy(("Remote:Enabled", "true"));
            Assert.AreEqual(RemoteAccessPolicy.Verdict.OutsideNetwork, none.Decide(IPAddress.Parse("100.64.0.5"), "GET", "/", false, null), "no VPN pool: only the company network");
        }
    }
}
