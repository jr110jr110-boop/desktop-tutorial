using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Entities.Model.NasbNiro.Agents;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// Priority 4, phase 4c: the load of the resources over all projects, the over-allocation alerts and the levelling proposals. The pure rules (spreading, capacity, runs) and the services with the schedule behind them.
    /// </summary>
    [TestClass]
    public class PcResourceLoadTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(1012, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Outsider = AppHost.Principal(1501, "outsider", "OPERATOR");

        // ------------------------------------------------------------------------------------------------ the pure rules

        [TestMethod]
        public void Spreading_Sums_Exactly_To_The_Quantity_For_Every_Distribution()
        {
            foreach (PcResourceDistribution d in new[] { PcResourceDistribution.Uniform, PcResourceDistribution.FrontLoaded, PcResourceDistribution.BackLoaded })
                foreach (int days in new[] { 1, 2, 3, 7, 11, 23 })
                    foreach (decimal q in new[] { 10m, 7.5m, 100m, 0.0001m, 33.3333m, 1234.5678m })
                    {
                        decimal[] shares = PcLoadRules.Spread(q, d, days);
                        Assert.AreEqual(days, shares.Length);
                        Assert.AreEqual(q, shares.Sum(), d + " " + days + " " + q);
                    }
            CollectionAssert.AreEqual(new[] { 2m, 2m, 2m, 2m, 2m }, PcLoadRules.Spread(10m, PcResourceDistribution.Uniform, 5));
            CollectionAssert.AreEqual(new[] { 4m, 3m, 2m, 1m }, PcLoadRules.Spread(10m, PcResourceDistribution.FrontLoaded, 4));
            CollectionAssert.AreEqual(new[] { 1m, 2m, 3m, 4m }, PcLoadRules.Spread(10m, PcResourceDistribution.BackLoaded, 4));
            // the rounding remainder goes to the last day: 10 / 3 = 3.3333 + 3.3333 + 3.3334
            CollectionAssert.AreEqual(new[] { 3.3333m, 3.3333m, 3.3334m }, PcLoadRules.Spread(10m, PcResourceDistribution.Uniform, 3));
            Assert.AreEqual(0, PcLoadRules.Spread(5m, PcResourceDistribution.Uniform, 0).Length);
        }

        [TestMethod]
        public void Holidays_And_Days_Off_Are_Not_Working_Days_Of_The_Spread()
        {
            // Saturday to Thursday work, Friday is off, and one Sunday is a holiday
            DateTime sat = NextSaturday();
            var calendar = new PcWorkCalendar(PcWorkCalendar.MaskOf(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday), new[] { sat.AddDays(1) }, null);
            List<DateTime> days = PcLoadRules.WorkingDays(calendar, sat, sat.AddDays(7));
            CollectionAssert.AreEqual(new[] { sat, sat.AddDays(2), sat.AddDays(3), sat.AddDays(4), sat.AddDays(5), sat.AddDays(7) }, days);
        }

        [TestMethod]
        public void Capacity_Follows_The_Resource_Calendar_And_The_Unit()
        {
            Assert.AreEqual(15m, PcLoadRules.DayCapacity(2m, PcResourceUnit.Hour, true, 7.5m));
            Assert.AreEqual(16m, PcLoadRules.DayCapacity(2m, PcResourceUnit.Hour, true, 8m), "the day length of the resource calendar");
            Assert.AreEqual(2m, PcLoadRules.DayCapacity(2m, PcResourceUnit.Day, true, 8m), "a day or an each resource ignores the day length");
            Assert.AreEqual(2m, PcLoadRules.DayCapacity(2m, PcResourceUnit.Each, true, 8m));
            Assert.AreEqual(0m, PcLoadRules.DayCapacity(2m, PcResourceUnit.Hour, false, 8m), "a day off of the resource has no capacity");
        }

        [TestMethod]
        public void Runs_Merge_Consecutive_Over_Days_And_A_Day_Off_Does_Not_Break_One()
        {
            DateTime d0 = new DateTime(2030, 1, 5);
            var load = new Dictionary<DateTime, decimal> { [d0] = 5m, [d0.AddDays(1)] = 4m, [d0.AddDays(2)] = 4m, [d0.AddDays(4)] = 7m, [d0.AddDays(5)] = 9m, [d0.AddDays(8)] = 6m };
            // capacity 3 every day but d0+3 (a day off: neither load nor capacity) and d0+6
            decimal Cap(DateTime d) => d == d0.AddDays(3) || d == d0.AddDays(6) ? 0m : 3m;
            List<PcLoadRules.OverRun> runs = PcLoadRules.Runs(load, Cap, d0, d0.AddDays(9));
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual(d0, runs[0].From); Assert.AreEqual(d0.AddDays(5), runs[0].To, "the day off in the middle does not split the run");
            Assert.AreEqual(9m, runs[0].PeakLoad); Assert.AreEqual(3m, runs[0].PeakCapacity);
            Assert.AreEqual(d0.AddDays(8), runs[1].From); Assert.AreEqual(d0.AddDays(8), runs[1].To);
            // a day within the capacity splits a run
            load[d0.AddDays(1)] = 1m;
            runs = PcLoadRules.Runs(load, Cap, d0, d0.AddDays(9));
            Assert.AreEqual(3, runs.Count);
        }

        // ------------------------------------------------------------------------------------------------ the services

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
            public int Resource;
        }

        private static DateTime NextSaturday()
        {
            DateTime d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
            return d;
        }

        /// <summary>The working day with the given index counted from the Saturday (Friday is off).</summary>
        private static DateTime Work(int index)
        {
            DateTime d = NextSaturday();
            int n = 0;
            while (true) { if (d.DayOfWeek != DayOfWeek.Friday) { if (n == index) return d; n++; } d = d.AddDays(1); }
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("ResLoad_" + name + "_" + Guid.NewGuid().ToString("N"));
            var env = new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                PcResource r = AppHost.Add(u, new PcResource { Code = "R-WELD", Title = "جوشکار", Kind = PcResourceKind.Labour, Unit = PcResourceUnit.Day, Capacity = 2m, ResponsibleUnit = "ZZZ" });
                u.SaveChanges();
                env.Resource = r.Id;
            }, Seeder);
            return env;
        }

        /// <summary>A live contract whose activities start on the Saturday; the activities of the spec are (code, duration, predecessor code or null). Runs the plan so the dates are those of the schedule.</summary>
        private static Task<(int Contract, Dictionary<string, int> Activities)> NewProject(Env e, string code, params (string Code, int Days, string After)[] spec) => NewProjectOn(e, code, null, DateTimeOffset.Now.AddDays(-5), spec);

        private static async Task<(int Contract, Dictionary<string, int> Activities)> NewProjectOn(Env e, string code, int? calendarId, DateTimeOffset contractDate, params (string Code, int Days, string After)[] spec)
        {
            int contract = 0;
            var ids = new Dictionary<string, int>();
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = code, Title = "پروژه " + code, Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = contractDate, StartDate = DateTimeOffset.Now.Date, Price = 1_000_000m });
                u.SaveChanges();
                contract = c.Id;
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000m, CalendarId = calendarId, ScheduleStartDate = NextSaturday(), ContractDeliveryDate = NextSaturday().AddDays(400), ProjectCode = "P-" + code });
                int sort = 1;
                foreach (var s in spec) { PcActivity a = AppHost.Add(u, new PcActivity { ContractId = c.Id, Code = s.Code, Title = "فعالیت " + s.Code, DurationDays = s.Days, Weight = 1m, ResponsibleUnit = "ZZZ", Sort = sort++, SourceType = PcSourceType.Manual, ProgressMethod = PcProgressMethod.Manual }); u.SaveChanges(); ids[s.Code] = a.Id; }
                foreach (var s in spec.Where(x => x.After != null)) u.Set<PcDependency>().Add(new PcDependency { ContractId = c.Id, PredecessorId = ids[s.After], SuccessorId = ids[s.Code], Type = PcDependencyType.FS, LagDays = 0, Origin = PcDependencyOrigin.Manual });
                u.SaveChanges();
            }, Seeder);
            ResponseDto<PcCpmRunDto> run = await e.Host.As(Planner, p => p.GetRequiredService<IPcScheduleService>().RunCpmAsync(new PcCpmRunRequestDto { ContractId = contract, Mirror = false }, Ct));
            Assert.IsTrue(run.Success, run.Message);
            return (contract, ids);
        }

        /// <summary>An approved resource plan of the contract that assigns the quantity of the resource to the activity.</summary>
        private static int ApprovePlan(Env e, int contract, params (int Activity, decimal Quantity, PcResourceDistribution Distribution)[] rows)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                var plan = new PcResourcePlan { ContractId = contract, Version = 1, Status = PcResourcePlanStatus.Approved, MakerUserId = 1051, EditorUserIds = ",1051,", ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1081, Assignments = new List<PcActivityResource>() };
                foreach (var r in rows) plan.Assignments.Add(new PcActivityResource { ActivityId = r.Activity, ResourceId = e.Resource, Quantity = r.Quantity, Distribution = r.Distribution, Source = PcAssignmentSource.Human, EditedByUserId = 1051, EditedAt = DateTimeOffset.Now });
                AppHost.Add(u, plan);
                u.SaveChanges();
                id = plan.Id;
            }, Seeder);
            return id;
        }

        private static Task<T> AsAgent<T>(Env e, string step, Func<IServiceProvider, Task<T>> body) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-PLAN"], "AG-PLAN", async sp => { using (AgentStepContext.Enter(step)) return await body(sp); });

        private static Task<ResponseDto<PcLoadCheckResultDto>> Check(Env e) => AsAgent(e, "RES.LOAD.CHECK", sp => sp.GetRequiredService<IPcResourceLoadService>().CheckOverloadsAsync(Ct));
        private static Task<ResponseDto<int>> Propose(Env e, int overload) => AsAgent(e, "RES.LEVEL.PROPOSE", sp => sp.GetRequiredService<IPcResourceLoadService>().ProposeLevelingAsync(overload, Ct));
        private static Task<T> As<T>(Env e, ClaimsPrincipal who, Func<IPcResourceLoadService, Task<T>> body) => e.Host.As(who, p => body(p.GetRequiredService<IPcResourceLoadService>()));
        private static List<PcResourceOverload> Overloads(Env e) => e.Host.Read(u => u.Set<PcResourceOverload>().AsNoTracking().OrderBy(o => o.Id).ToList());
        private static List<PcLevelingProposal> Proposals(Env e) => e.Host.Read(u => u.Set<PcLevelingProposal>().AsNoTracking().OrderBy(o => o.Id).ToList());
        private static PcActivity Activity(Env e, int id) => e.Host.Read(u => u.Set<PcActivity>().AsNoTracking().Single(a => a.Id == id));

        /// <summary>Two projects whose activities load the role resource at once: A1 (5 days, 10) and B1 (5 days, 10) against the capacity 2, and in the second project a longer activity B2 that gives B1 its float.</summary>
        private static async Task<(int A, int B, int A1, int B1, int B2)> TwoProjects(Env e, bool withFloat)
        {
            var a = await NewProject(e, "CA", ("A1", 5, null));
            var b = withFloat ? await NewProject(e, "CB", ("B1", 5, null), ("B2", 12, null)) : await NewProject(e, "CB", ("B1", 5, null));
            ApprovePlan(e, a.Contract, (a.Activities["A1"], 10m, PcResourceDistribution.Uniform));
            ApprovePlan(e, b.Contract, (b.Activities["B1"], 10m, PcResourceDistribution.Uniform));
            return (a.Contract, b.Contract, a.Activities["A1"], b.Activities["B1"], withFloat ? b.Activities["B2"] : 0);
        }

        [TestMethod]
        public async Task The_Histogram_Adds_The_Projects_Per_Day_And_Week_And_Names_The_Activities()
        {
            Env e = await NewEnv("hist");
            var p = await TwoProjects(e, true);
            DateTime from = Work(0), to = Work(9);
            ResponseDto<PcResourceLoadDto> daily = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = e.Resource, From = from, To = to, Bucket = "day" }, Ct));
            Assert.IsTrue(daily.Success, daily.Message);
            PcResourceLoadRowDto row = daily.Data.Resources.Single();
            row.Buckets.RemoveAll(b => b.Load == 0m);
            Assert.AreEqual(5, row.Buckets.Count, "five working days carry load");
            for (int i = 0; i < 5; i++)
            {
                PcLoadBucketDto b = row.Buckets[i];
                Assert.AreEqual(Work(i), b.Start);
                Assert.AreEqual(4m, b.Load); Assert.AreEqual(2m, b.Capacity); Assert.AreEqual(2m, b.Over);
                CollectionAssert.AreEqual(new[] { "A1", "B1" }, b.Items.Select(x => x.ActivityCode).OrderBy(x => x, StringComparer.Ordinal).ToArray());
                Assert.AreEqual(2m, b.Items.Single(x => x.ActivityCode == "A1").Load);
            }
            Assert.AreEqual(4m, row.PeakLoad);

            // the week bucket adds its days; the week starts on Saturday
            ResponseDto<PcResourceLoadDto> weekly = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = e.Resource, From = from, To = to, Bucket = "week" }, Ct));
            Assert.IsTrue(weekly.Success, weekly.Message);
            PcLoadBucketDto w = weekly.Data.Resources.Single().Buckets.First();
            Assert.AreEqual(NextSaturday(), w.Start);
            Assert.AreEqual(20m, w.Load);

            // more than 370 days is refused in Persian; an outsider cannot read
            ResponseDto<PcResourceLoadDto> wide = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { From = from, To = from.AddDays(371) }, Ct));
            Assert.IsFalse(wide.Success);
            Assert.IsFalse(string.IsNullOrWhiteSpace(wide.Message));
        }

        [TestMethod]
        public async Task A_Draft_Plan_Is_Only_Counted_With_The_Optional_Layer_And_Is_Flagged()
        {
            Env e = await NewEnv("draft");
            var a = await NewProject(e, "CD", ("D1", 5, null));
            e.Host.Write(u =>
            {
                var plan = new PcResourcePlan { ContractId = a.Contract, Version = 1, Status = PcResourcePlanStatus.Draft, MakerUserId = 1051, EditorUserIds = ",1051,", Assignments = new List<PcActivityResource>() };
                plan.Assignments.Add(new PcActivityResource { ActivityId = a.Activities["D1"], ResourceId = e.Resource, Quantity = 20m, Distribution = PcResourceDistribution.Uniform, Source = PcAssignmentSource.Human, EditedByUserId = 1051, EditedAt = DateTimeOffset.Now });
                AppHost.Add(u, plan); u.SaveChanges();
            }, Seeder);
            var q = new PcResourceLoadQuery { ResourceId = e.Resource, From = Work(0), To = Work(9) };
            ResponseDto<PcResourceLoadDto> without = await As(e, Planner, s => s.GetLoadAsync(q, Ct));
            Assert.AreEqual(0, without.Data.Resources.SelectMany(r => r.Buckets).Count(b => b.Load > 0m));
            q.IncludeOpen = true;
            ResponseDto<PcResourceLoadDto> with = await As(e, Planner, s => s.GetLoadAsync(q, Ct));
            PcLoadBucketDto first = with.Data.Resources.Single().Buckets.First();
            Assert.AreEqual(4m, first.Load); Assert.AreEqual(4m, first.DraftLoad); Assert.IsTrue(first.Items.Single().IsDraft);
            // a draft never opens an alert
            ResponseDto<PcLoadCheckResultDto> check = await Check(e);
            Assert.IsTrue(check.Success, check.Message);
            Assert.AreEqual(0, Overloads(e).Count);
        }

        [TestMethod]
        public async Task The_Agent_Check_Opens_One_Overload_Across_Projects_Is_Idempotent_And_Resolves_When_The_Plan_Changes()
        {
            Env e = await NewEnv("check");
            var p = await TwoProjects(e, true);
            // a person cannot run the check; an agent outside its step cannot either
            ResponseDto<PcLoadCheckResultDto> human = await As(e, Planner, s => s.CheckOverloadsAsync(Ct));
            Assert.IsFalse(human.Success);
            string outside = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-PLAN"], "AG-PLAN", async sp => (await sp.GetRequiredService<IPcResourceLoadService>().CheckOverloadsAsync(Ct)).Message);
            Assert.AreEqual(PcText.NotAllowed, outside);

            ResponseDto<PcLoadCheckResultDto> first = await Check(e);
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(1, first.Data.Opened); Assert.AreEqual(0, first.Data.Updated); Assert.AreEqual(0, first.Data.Resolved); Assert.AreEqual(1, first.Data.Open);
            PcResourceOverload o = Overloads(e).Single();
            Assert.AreEqual(e.Resource, o.ResourceId);
            Assert.AreEqual(Work(0), o.FromDate); Assert.AreEqual(Work(4), o.ToDate);
            Assert.AreEqual(4m, o.PeakLoad); Assert.AreEqual(2m, o.PeakCapacity);
            Assert.AreEqual(PcOverloadStatus.Open, o.Status);

            // a second run changes nothing
            ResponseDto<PcLoadCheckResultDto> again = await Check(e);
            Assert.AreEqual(0, again.Data.Opened); Assert.AreEqual(0, again.Data.Updated); Assert.AreEqual(0, again.Data.Resolved); Assert.AreEqual(1, again.Data.Open);
            Assert.AreEqual(1, Overloads(e).Count);

            // the quantity of the second project falls: the run keeps its dates and the peak falls (3 = 2 + 1), then it falls to what the capacity carries and the run goes
            e.Host.Write(u => { foreach (PcActivityResource r in u.Set<PcActivityResource>().Where(x => x.ActivityId == p.B1)) r.Quantity = 5m; u.SaveChanges(); }, Seeder);
            ResponseDto<PcLoadCheckResultDto> less = await Check(e);
            Assert.AreEqual(0, less.Data.Opened); Assert.AreEqual(1, less.Data.Updated); Assert.AreEqual(0, less.Data.Resolved); Assert.AreEqual(1, less.Data.Open);
            Assert.AreEqual(3m, Overloads(e).Single().PeakLoad);
            e.Host.Write(u => { foreach (PcActivityResource r in u.Set<PcActivityResource>().Where(x => x.ActivityId == p.B1)) r.Quantity = 0m; u.SaveChanges(); }, Seeder);
            ResponseDto<PcLoadCheckResultDto> gone = await Check(e);
            Assert.AreEqual(0, gone.Data.Opened); Assert.AreEqual(1, gone.Data.Resolved); Assert.AreEqual(0, gone.Data.Open);
            o = Overloads(e).Single();
            Assert.AreEqual(PcOverloadStatus.Resolved, o.Status);
            Assert.IsNotNull(o.ResolvedAt);
        }

        [TestMethod]
        public async Task Only_The_Pmo_Accepts_An_Overload_With_A_Reason_Never_A_Seat_Or_The_Planner()
        {
            Env e = await NewEnv("accept");
            await TwoProjects(e, true);
            await Check(e);
            int id = Overloads(e).Single().Id;

            ResponseDto<PcResourceOverloadDto> planner = await As(e, Planner, s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "لازم است" }, Ct));
            Assert.IsFalse(planner.Success); Assert.AreEqual(PcLoadText.AcceptNeedsPmo, planner.Message);
            ResponseDto<PcResourceOverloadDto> outsider = await As(e, Outsider, s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "لازم است" }, Ct));
            Assert.IsFalse(outsider.Success);
            string seat = await AsAgent(e, "RES.LOAD.CHECK", async sp => (await sp.GetRequiredService<IPcResourceLoadService>().AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "لازم است" }, Ct)).Message);
            Assert.AreEqual(PcLoadText.AiDecision, seat);
            ResponseDto<PcResourceOverloadDto> noReason = await As(e, Pmo, s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "  " }, Ct));
            Assert.IsFalse(noReason.Success); Assert.AreEqual(PcLoadText.AcceptReason, noReason.Message);
            Assert.AreEqual(PcOverloadStatus.Open, Overloads(e).Single().Status);

            // the maker or editor of a plan that loads the resource is not the one who accepts (maker is not checker)
            ResponseDto<PcResourceOverloadDto> maker = await As(e, AppHost.Principal(1051, "planner.pmo", "PMO"), s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "لازم است" }, Ct));
            Assert.IsFalse(maker.Success); Assert.AreEqual(PcLoadText.AcceptMaker, maker.Message);

            ResponseDto<PcResourceOverloadDto> ok = await As(e, Pmo, s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = id, Reason = "کار فوری مشتری است" }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            PcResourceOverload o = Overloads(e).Single();
            Assert.AreEqual(PcOverloadStatus.Accepted, o.Status); Assert.AreEqual(1011, o.AcceptedByUserId); Assert.AreEqual("کار فوری مشتری است", o.AcceptReason);

            // an accepted run is not reopened by the next check
            ResponseDto<PcLoadCheckResultDto> check = await Check(e);
            Assert.AreEqual(0, check.Data.Opened); Assert.AreEqual(0, check.Data.Open); Assert.AreEqual(1, check.Data.Accepted);
        }

        [TestMethod]
        public async Task A_Move_Within_The_Float_Removes_The_Overload_And_Needs_A_Second_Person()
        {
            Env e = await NewEnv("float");
            var p = await TwoProjects(e, true);
            await Check(e);
            PcResourceOverload o = Overloads(e).Single();

            ResponseDto<int> proposed = await Propose(e, o.Id);
            Assert.IsTrue(proposed.Success, proposed.Message);
            PcLevelingProposal prop = Proposals(e).Single();
            Assert.AreEqual(prop.Id, proposed.Data);
            Assert.AreEqual(p.B1, prop.ActivityId, "B1 has the float; A1 is critical");
            Assert.AreEqual(PcLevelingKind.ShiftWithinFloat, prop.Kind);
            Assert.AreEqual(5, prop.ShiftDays); Assert.AreEqual(0, prop.CriticalImpactDays);
            Assert.AreEqual(Work(0), prop.CurrentStart); Assert.AreEqual(Work(5), prop.ProposedStart); Assert.AreEqual(Work(9), prop.ProposedFinish);
            Assert.IsTrue(prop.PreparedByAgent); Assert.IsFalse(prop.NeedsChangeRequest);
            Assert.AreEqual(PcLevelingStatus.Proposed, prop.Status);
            // a second ask makes no second proposal
            Assert.AreEqual(0, (await Propose(e, o.Id)).Data);
            Assert.AreEqual(1, Proposals(e).Count);

            // an agent seat applies nothing; the outsider cannot either; the schedule is untouched until a person applies
            string seat = await AsAgent(e, "RES.LEVEL.PROPOSE", async sp => (await sp.GetRequiredService<IPcResourceLoadService>().ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "apply" }, Ct)).Message);
            Assert.AreEqual(PcLoadText.AiDecision, seat);
            ResponseDto<PcLevelingProposalDto> outsider = await As(e, Outsider, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "apply" }, Ct));
            Assert.IsFalse(outsider.Success);
            Assert.AreEqual(Work(0), Activity(e, p.B1).EarlyStart);

            // the planner applies through the schedule: the activity now starts later
            ResponseDto<PcLevelingProposalDto> applied = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "apply" }, Ct));
            Assert.IsTrue(applied.Success, applied.Message);
            Assert.AreEqual(PcLevelingStatus.Applied, applied.Data.Status); Assert.IsTrue(applied.Data.PendingSecondPerson);
            PcActivity moved = Activity(e, p.B1);
            Assert.AreEqual(Work(5), moved.EarlyStart); Assert.AreEqual(PcConstraint.StartNoEarlierThan, moved.ConstraintType); Assert.AreEqual(Work(5), moved.ConstraintDate);

            // the applier does not confirm; the planner role does not confirm; another PMO does
            ResponseDto<PcLevelingProposalDto> self = await As(e, AppHost.Principal(1051, "planner.pmo", "PMO"), s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "confirm" }, Ct));
            Assert.IsFalse(self.Success); Assert.AreEqual(PcLoadText.SameUser, self.Message);
            ResponseDto<PcLevelingProposalDto> notPmo = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "confirm" }, Ct));
            Assert.IsFalse(notPmo.Success); Assert.AreEqual(PcLoadText.ConfirmRoles, notPmo.Message);
            ResponseDto<PcLevelingProposalDto> confirmed = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "confirm" }, Ct));
            Assert.IsTrue(confirmed.Success, confirmed.Message);
            Assert.IsFalse(confirmed.Data.PendingSecondPerson); Assert.AreEqual(1011, confirmed.Data.ConfirmedByUserId);

            // the next check finds no over-allocation any more (recomputed from the moved dates)
            ResponseDto<PcLoadCheckResultDto> check = await Check(e);
            Assert.AreEqual(1, check.Data.Resolved); Assert.AreEqual(0, check.Data.Open);
            Assert.AreEqual(PcOverloadStatus.Resolved, Overloads(e).Single().Status);
        }

        [TestMethod]
        public async Task A_Refusal_By_The_Second_Person_Takes_The_Date_Change_Back()
        {
            Env e = await NewEnv("refuse");
            var p = await TwoProjects(e, true);
            await Check(e);
            int overload = Overloads(e).Single().Id;
            await Propose(e, overload);
            int proposal = Proposals(e).Single().Id;
            Assert.IsTrue((await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "apply" }, Ct))).Success);
            Assert.AreEqual(Work(5), Activity(e, p.B1).EarlyStart);

            ResponseDto<PcLevelingProposalDto> noReason = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "refuse", Reason = " " }, Ct));
            Assert.IsFalse(noReason.Success); Assert.AreEqual(PcLoadText.RefuseReason, noReason.Message);
            ResponseDto<PcLevelingProposalDto> refused = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "refuse", Reason = "منبع جایگزین نیست" }, Ct));
            Assert.IsTrue(refused.Success, refused.Message);
            Assert.AreEqual(PcLevelingStatus.Rejected, refused.Data.Status); Assert.IsTrue(refused.Data.Reverted);
            PcActivity back = Activity(e, p.B1);
            Assert.AreEqual(Work(0), back.EarlyStart);
            Assert.AreEqual(PcConstraint.None, back.ConstraintType);
            Assert.IsNull(back.ConstraintDate);
            // the overload is still open and the next proposal does not repeat the refused activity
            await Check(e);
            Assert.AreEqual(1, Overloads(e).Count(x => x.Status == PcOverloadStatus.Open));
            Assert.IsTrue((await Propose(e, overload)).Success);
            PcLevelingProposal next = Proposals(e).Single(x => x.Status == PcLevelingStatus.Proposed);
            Assert.AreNotEqual(p.B1, next.ActivityId, "the refused activity is not proposed again");
            Assert.AreEqual(p.A1, next.ActivityId);
        }

        [TestMethod]
        public async Task The_Planner_Rejects_A_Proposal_With_A_Reason()
        {
            Env e = await NewEnv("reject");
            await TwoProjects(e, true);
            await Check(e);
            await Propose(e, Overloads(e).Single().Id);
            int proposal = Proposals(e).Single().Id;
            ResponseDto<PcLevelingProposalDto> none = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "reject" }, Ct));
            Assert.IsFalse(none.Success); Assert.AreEqual(PcLoadText.RejectReason, none.Message);
            ResponseDto<PcLevelingProposalDto> rejected = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "reject", Reason = "تحویل زودتر لازم است" }, Ct));
            Assert.IsTrue(rejected.Success, rejected.Message);
            Assert.AreEqual(PcLevelingStatus.Rejected, rejected.Data.Status);
            ResponseDto<PcLevelingProposalDto> unknown = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = proposal, Action = "explode" }, Ct));
            Assert.IsFalse(unknown.Success);
            // the next proposal for the same overload moves another activity (here none is left within the float, so the move is beyond it)
            ResponseDto<int> next = await Propose(e, Overloads(e).Single().Id);
            Assert.IsTrue(next.Success, next.Message);
            Assert.AreEqual(1, Proposals(e).Count(x => x.Status == PcLevelingStatus.Proposed));
            Assert.AreNotEqual(Proposals(e).First().ActivityId, Proposals(e).Last().ActivityId);
        }

        [TestMethod]
        public async Task Without_Float_The_Proposal_Delays_And_Reports_The_Exact_Critical_Impact()
        {
            Env e = await NewEnv("beyond");
            var p = await TwoProjects(e, false);
            await Check(e);
            await Propose(e, Overloads(e).Single().Id);
            PcLevelingProposal prop = Proposals(e).Single();
            Assert.AreEqual(PcLevelingKind.DelayBeyondFloat, prop.Kind);
            Assert.AreEqual(5, prop.ShiftDays);
            Assert.AreEqual(5, prop.CriticalImpactDays, "the project finish moves by the shift when the activity is critical");
            Assert.AreEqual(p.A1, prop.ActivityId, "equal impact and shift: the first activity");
            StringAssert.Contains(prop.Note, "5 روز کاری");
        }

        [TestMethod]
        public async Task A_Started_Activity_Is_Never_Proposed()
        {
            Env e = await NewEnv("started");
            var p = await TwoProjects(e, true);
            await Check(e);
            // B1 has started: it is the only one with float, and A1 is critical; with B1 started the agent moves A1 beyond the float, never B1
            e.Host.Write(u => { PcActivity b1 = u.Set<PcActivity>().Single(a => a.Id == p.B1); b1.ActualStart = DateTime.Today; b1.Status = PcActivityStatus.InProgress; u.SaveChanges(); }, Seeder);
            await Propose(e, Overloads(e).Single().Id);
            PcLevelingProposal prop = Proposals(e).Single();
            Assert.AreEqual(p.A1, prop.ActivityId);
            Assert.AreNotEqual(p.B1, prop.ActivityId);

            // with both started there is nothing to propose
            Env f = await NewEnv("started2");
            var q = await TwoProjects(f, true);
            await Check(f);
            f.Host.Write(u => { foreach (PcActivity a in u.Set<PcActivity>().Where(a => a.Id == q.A1 || a.Id == q.B1)) { a.ActualStart = DateTime.Today; a.Status = PcActivityStatus.InProgress; } u.SaveChanges(); }, Seeder);
            ResponseDto<int> none = await Propose(f, Overloads(f).Single().Id);
            Assert.IsTrue(none.Success, none.Message);
            Assert.AreEqual(0, none.Data);
            Assert.AreEqual(0, Proposals(f).Count);
            Assert.IsNotNull(Overloads(f).Single().NoProposalAt);
        }

        [TestMethod]
        public async Task The_Paged_Lists_Carry_The_Proposals_And_The_Readers_See_Them()
        {
            Env e = await NewEnv("lists");
            await TwoProjects(e, true);
            await Check(e);
            await Propose(e, Overloads(e).Single().Id);
            ResponseDto<PagedList<PcResourceOverloadDto>> list = await As(e, Planner, s => s.GetOverloadsAsync(new PcResourceOverloadQuery { PageNumber = 1, PageSize = 10 }, Ct));
            Assert.IsTrue(list.Success, list.Message);
            Assert.AreEqual(1, list.Data.Count);
            Assert.AreEqual(1, list.Data[0].Proposals.Count);
            Assert.AreEqual("R-WELD", list.Data[0].ResourceCode);
            ResponseDto<PagedList<PcLevelingProposalDto>> props = await As(e, Planner, s => s.GetProposalsAsync(new PcLevelingProposalQuery { PageNumber = 1, PageSize = 10, Status = PcLevelingStatus.Proposed }, Ct));
            Assert.AreEqual(1, props.Data.Count);
            Assert.AreEqual(5, props.Data[0].ShiftDays);
        }

        private static async Task Run(Env e, string code)
        {
            AiSeatJobResult r = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
        }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-PLAN"); }
            await Run(e, "AG-MASTER");
        }

        private static List<Entities.Model.NasbNiro.Agents.AgentTask> Open(Env e, string step) =>
            e.Host.Read(u => u.Set<Entities.Model.NasbNiro.Agents.AgentTask>().AsNoTracking().Where(t => t.StepCode == step && t.State < Entities.Model.NasbNiro.Agents.AgentTaskState.Done).OrderBy(t => t.Id).ToList());

        [TestMethod]
        public async Task The_Steps_Open_The_Alert_The_Proposal_And_The_Two_Gates_And_Close_Them_As_The_State_Changes()
        {
            Env e = await NewEnv("steps");
            var p = await TwoProjects(e, true);
            await Settle(e);
            PcResourceOverload o = Overloads(e).Single();
            PcLevelingProposal prop = Proposals(e).Single();
            Assert.AreEqual(PcLevelingStatus.Proposed, prop.Status);

            var resolve = Open(e, "RES.LOAD.RESOLVE").Single();
            Assert.AreEqual(o.Id, resolve.TargetId);
            Assert.AreEqual("/project-control/resources?tab=overloads&overload=" + o.Id, resolve.Link);
            StringAssert.Contains((resolve.Title ?? "") + (resolve.Summary ?? ""), "R-WELD");
            var decide = Open(e, "RES.LEVEL.DECIDE").Single();
            Assert.AreEqual(prop.Id, decide.TargetId);
            Assert.AreEqual("/project-control/resources?tab=overloads&proposal=" + prop.Id, decide.Link);
            Assert.AreEqual(0, Open(e, "RES.LEVEL.CONFIRM").Count);
            Assert.AreEqual(0, Open(e, "RES.LEVEL.PROPOSE").Count, "the agent step is done once the proposal exists");

            // the planner applies: the decision gate closes and the confirm gate (PMO) opens
            Assert.IsTrue((await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "apply" }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.LEVEL.DECIDE").Count);
            var confirm = Open(e, "RES.LEVEL.CONFIRM").Single();
            Assert.AreEqual(prop.Id, confirm.TargetId);
            Assert.AreEqual("/project-control/resources?tab=overloads&proposal=" + prop.Id, confirm.Link);

            // the second person confirms: the gate closes and the check resolves the overload and its alert
            Assert.IsTrue((await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = prop.Id, Action = "confirm" }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.LEVEL.CONFIRM").Count);
            Assert.AreEqual(PcOverloadStatus.Resolved, Overloads(e).Single().Status);
            Assert.AreEqual(0, Open(e, "RES.LOAD.RESOLVE").Count);
        }

        private static void Lock(Env e, params int[] contracts) =>
            e.Host.Write(u => { foreach (PcContractTerms t in u.Set<PcContractTerms>().Where(t => contracts.Contains(t.ContractId))) { t.CurrentBaselineId = 999; t.ModifyDate = DateTimeOffset.Now; } u.SaveChanges(); }, Seeder);

        private static void Unlock(Env e, params int[] contracts) =>
            e.Host.Write(u => { foreach (PcContractTerms t in u.Set<PcContractTerms>().Where(t => contracts.Contains(t.ContractId))) { t.CurrentBaselineId = null; t.ModifyDate = DateTimeOffset.Now; } u.SaveChanges(); }, Seeder);

        private static string Version(Env e, int proposal, byte[] bytes)
        {
            e.Host.Write(u => { u.Set<PcLevelingProposal>().Single(p => p.Id == proposal).RowVersion = bytes; u.SaveChanges(); }, Seeder);
            return Convert.ToBase64String(bytes);
        }

        // ------------------------------------------------------------------------------------------------ review round: the running contracts, the lock, the transaction, the matching

        [TestMethod]
        public async Task A_Contract_Of_Any_Age_That_Is_Running_Carries_Its_Load()
        {
            Env e = await NewEnv("old");
            var a = await NewProjectOn(e, "OLD-A", null, DateTimeOffset.Now.AddDays(-1100), ("A1", 5, null));
            var b = await NewProjectOn(e, "OLD-B", null, DateTimeOffset.Now.AddDays(-900), ("B1", 5, null));
            ApprovePlan(e, a.Contract, (a.Activities["A1"], 10m, PcResourceDistribution.Uniform));
            ApprovePlan(e, b.Contract, (b.Activities["B1"], 10m, PcResourceDistribution.Uniform));
            ResponseDto<PcResourceLoadDto> load = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = e.Resource, From = Work(0), To = Work(4) }, Ct));
            Assert.AreEqual(4m, load.Data.Resources.Single().Buckets.First().Load, "the 400 day window of the agents' initiation does not apply to the load");
            Assert.AreEqual(1, (await Check(e)).Data.Opened);
            // a contract that is not running (finished) is left out
            e.Host.Write(u => { foreach (ContractEntity c in u.Set<ContractEntity>().Where(c => c.Code == "OLD-B")) c.Status = ContractStatus.Done; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(1, (await Check(e)).Data.Resolved);
        }

        [TestMethod]
        public async Task A_Locked_Project_Does_Not_Block_An_Unlocked_Alternative()
        {
            Env e = await NewEnv("lock1");
            var p = await TwoProjects(e, true);
            await Check(e);
            // B is locked (it has the float), A is not: the unlocked A1 is proposed (beyond its float), not B1 which could not be applied
            Lock(e, p.B);
            await Propose(e, Overloads(e).Single().Id);
            PcLevelingProposal first = Proposals(e).Single();
            Assert.AreEqual(p.A1, first.ActivityId); Assert.IsFalse(first.NeedsChangeRequest); Assert.AreEqual(PcLevelingKind.DelayBeyondFloat, first.Kind);
            StringAssert.Contains(first.Note, "شروع نه زودتر از");
        }

        [TestMethod]
        public async Task A_Proposal_That_Waits_For_A_Change_Request_Is_Looked_At_Again_When_An_Alternative_Appears()
        {
            Env e = await NewEnv("lock2");
            var p = await TwoProjects(e, true);
            await Check(e);
            int overload = Overloads(e).Single().Id;
            // both are locked: nothing unlocked can clear the run; the best locked move is proposed and flagged, and it cannot be applied
            Lock(e, p.A, p.B);
            await Propose(e, overload);
            PcLevelingProposal flagged = Proposals(e).Single();
            Assert.AreEqual(p.B1, flagged.ActivityId); Assert.IsTrue(flagged.NeedsChangeRequest); Assert.AreEqual(PcLevelingKind.ShiftWithinFloat, flagged.Kind);
            ResponseDto<PcLevelingProposalDto> refused = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = flagged.Id, Action = "apply" }, Ct));
            Assert.IsFalse(refused.Success); Assert.AreEqual(PcLoadText.NeedsChangeRequest, refused.Message);
            Assert.AreEqual(Work(0), Activity(e, p.B1).EarlyStart, "nothing changed");
            Assert.AreEqual(PcLoadText.NeedsChangeRequest, (await As(e, Planner, s => s.GetProposalAsync(flagged.Id, Ct))).Data.ApplyBlockedReason);
            // while nothing moved the flagged proposal is kept
            Assert.AreEqual(0, (await Propose(e, overload)).Data);
            Assert.AreEqual(1, Proposals(e).Count);
            // the lock of A goes: the agent looks again and the unlocked activity replaces the flagged one
            await Task.Delay(30);
            Unlock(e, p.A);
            Assert.IsTrue((await Propose(e, overload)).Data > 0);
            Assert.AreEqual(PcLevelingStatus.Superseded, Proposals(e).Single(x => x.Id == flagged.Id).Status);
            PcLevelingProposal again = Proposals(e).Last();
            Assert.AreEqual(p.A1, again.ActivityId); Assert.IsFalse(again.NeedsChangeRequest);
        }

        [TestMethod]
        public async Task The_Lock_Is_Read_At_Apply_Time_Not_From_The_Stored_Flag()
        {
            Env e = await NewEnv("lock3");
            var p = await TwoProjects(e, true);
            await Check(e);
            Lock(e, p.A, p.B);
            await Propose(e, Overloads(e).Single().Id);
            PcLevelingProposal flagged = Proposals(e).Single();
            Assert.IsTrue(flagged.NeedsChangeRequest);
            // the baseline is gone (the change request that allows the edit has been through): the same proposal applies
            Unlock(e, p.A, p.B);
            ResponseDto<PcLevelingProposalDto> applied = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = flagged.Id, Action = "apply" }, Ct));
            Assert.IsTrue(applied.Success, applied.Message);
            Assert.AreEqual(Work(5), Activity(e, p.B1).EarlyStart);
            Assert.IsFalse(Proposals(e).Single().NeedsChangeRequest);
        }

        [TestMethod]
        public async Task A_Stale_Row_Version_Stops_Apply_And_Refuse_Before_Anything_Changes()
        {
            Env e = await NewEnv("version");
            var p = await TwoProjects(e, true);
            await Check(e);
            await Propose(e, Overloads(e).Single().Id);
            int id = Proposals(e).Single().Id;
            string good = Version(e, id, new byte[] { 1, 2, 3 });
            string old = Convert.ToBase64String(new byte[] { 4, 5, 6 });

            ResponseDto<PcLevelingProposalDto> stale = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "apply", RowVersion = old }, Ct));
            Assert.IsFalse(stale.Success); Assert.AreEqual(PcLoadText.Concurrency, stale.Message);
            ResponseDto<PcLevelingProposalDto> junk = await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "apply", RowVersion = "***not base64***" }, Ct));
            Assert.IsFalse(junk.Success); Assert.AreEqual(PcLoadText.Concurrency, junk.Message);
            Assert.AreEqual(Work(0), Activity(e, p.B1).EarlyStart, "the schedule is untouched");
            Assert.AreEqual(PcConstraint.None, Activity(e, p.B1).ConstraintType);
            Assert.AreEqual(PcLevelingStatus.Proposed, Proposals(e).Single().Status);

            Assert.IsTrue((await As(e, Planner, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "apply", RowVersion = good }, Ct))).Success);
            Assert.AreEqual(Work(5), Activity(e, p.B1).EarlyStart);
            ResponseDto<PcLevelingProposalDto> staleRefuse = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "refuse", Reason = "لازم نیست", RowVersion = old }, Ct));
            Assert.IsFalse(staleRefuse.Success); Assert.AreEqual(PcLoadText.Concurrency, staleRefuse.Message);
            Assert.AreEqual(Work(5), Activity(e, p.B1).EarlyStart, "the refusal did not revert anything");
            Assert.AreEqual(PcLevelingStatus.Applied, Proposals(e).Single().Status);
            // the overload accept takes a bad version without an error too
            int o = Overloads(e).Single().Id;
            Assert.AreEqual(PcLoadText.Concurrency, (await As(e, Pmo, s => s.AcceptOverloadAsync(new PcOverloadAcceptDto { Id = o, Reason = "x", RowVersion = "%%%" }, Ct))).Message);
        }

        [TestMethod]
        public async Task An_Accepted_Row_Keeps_Only_Its_Own_Dates_And_Peak_And_An_Open_Row_Is_Never_Resolved_By_It()
        {
            Env e = await NewEnv("match");
            var a = await NewProject(e, "MA", ("A1", 8, null));
            var b = await NewProject(e, "MB", ("B1", 8, null));
            ApprovePlan(e, a.Contract, (a.Activities["A1"], 16m, PcResourceDistribution.Uniform));
            int planB = ApprovePlan(e, b.Contract, (b.Activities["B1"], 16m, PcResourceDistribution.Uniform));
            // an accepted row for the first days and an open row for the last days; today's run (peak 4, capacity 2) covers all eight days
            e.Host.Write(u =>
            {
                AppHost.Add(u, new PcResourceOverload { ResourceId = e.Resource, FromDate = Work(0), ToDate = Work(3), PeakLoad = 4m, PeakCapacity = 2m, Status = PcOverloadStatus.Accepted, DetectedAt = DateTimeOffset.Now, AcceptedByUserId = 1011, AcceptedAt = DateTimeOffset.Now, AcceptReason = "قبلا پذیرفته شد" });
                AppHost.Add(u, new PcResourceOverload { ResourceId = e.Resource, FromDate = Work(5), ToDate = Work(7), PeakLoad = 4m, PeakCapacity = 2m, Status = PcOverloadStatus.Open, DetectedAt = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);
            ResponseDto<PcLoadCheckResultDto> r = await Check(e);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(0, r.Data.Opened); Assert.AreEqual(1, r.Data.Updated); Assert.AreEqual(0, r.Data.Resolved);
            List<PcResourceOverload> rows = Overloads(e);
            Assert.AreEqual(2, rows.Count);
            PcResourceOverload open = rows.Single(x => x.Status == PcOverloadStatus.Open);
            Assert.AreEqual(Work(0), open.FromDate); Assert.AreEqual(Work(7), open.ToDate, "the open row took the whole run");
            PcResourceOverload accepted = rows.Single(x => x.Status == PcOverloadStatus.Accepted);
            Assert.AreEqual(Work(0), accepted.FromDate); Assert.AreEqual(Work(3), accepted.ToDate); Assert.AreEqual("قبلا پذیرفته شد", accepted.AcceptReason);

            // an accepted row that covers the run and is not lower than it takes the run and nothing opens; a higher peak opens a new row and leaves the accepted one as it was
            Env f = await NewEnv("match2");
            var a2 = await NewProject(f, "MA", ("A1", 5, null));
            var b2 = await NewProject(f, "MB", ("B1", 5, null));
            ApprovePlan(f, a2.Contract, (a2.Activities["A1"], 10m, PcResourceDistribution.Uniform));
            ApprovePlan(f, b2.Contract, (b2.Activities["B1"], 10m, PcResourceDistribution.Uniform));
            f.Host.Write(u => { AppHost.Add(u, new PcResourceOverload { ResourceId = f.Resource, FromDate = Work(0), ToDate = Work(6), PeakLoad = 4m, PeakCapacity = 2m, Status = PcOverloadStatus.Accepted, DetectedAt = DateTimeOffset.Now, AcceptedByUserId = 1011, AcceptedAt = DateTimeOffset.Now, AcceptReason = "ok" }); u.SaveChanges(); }, Seeder);
            Assert.AreEqual(0, (await Check(f)).Data.Opened, "inside the accepted dates, peak not higher");
            f.Host.Write(u => { foreach (PcActivityResource x in u.Set<PcActivityResource>().Where(x => x.ActivityId == b2.Activities["B1"])) x.Quantity = 20m; u.SaveChanges(); }, Seeder);
            ResponseDto<PcLoadCheckResultDto> higher = await Check(f);
            Assert.AreEqual(1, higher.Data.Opened, "peak 6 is higher than the accepted 4: the excess is a new open row");
            Assert.AreEqual(PcOverloadStatus.Accepted, Overloads(f).First().Status);
            Assert.AreEqual(Work(6), Overloads(f).First().ToDate);
            Assert.AreEqual(6m, Overloads(f).Single(x => x.Status == PcOverloadStatus.Open).PeakLoad);
        }

        [TestMethod]
        public async Task A_Full_Time_Assignment_On_An_Eight_Hour_Calendar_Is_Not_An_Overload()
        {
            Env e = await NewEnv("hours");
            int calendar = 0;
            e.Host.Write(u =>
            {
                PcCalendar c = AppHost.Add(u, new PcCalendar { Name = "8h", WorkingWeekdays = 95, HoursPerDay = 8m, UseCompanyHolidays = false });
                PcResource r = u.Set<PcResource>().Single(x => x.Id == e.Resource);
                r.Unit = PcResourceUnit.Hour; r.Capacity = 1m;
                u.SaveChanges();
                calendar = c.Id;
            }, Seeder);
            var a = await NewProjectOn(e, "HRS", calendar, DateTimeOffset.Now.AddDays(-5), ("A1", 5, null));
            ApprovePlan(e, a.Contract, (a.Activities["A1"], 40m, PcResourceDistribution.Uniform));   // 5 days of 8 hours, one person
            ResponseDto<PcResourceLoadDto> load = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = e.Resource, From = Work(0), To = Work(4) }, Ct));
            PcLoadBucketDto first = load.Data.Resources.Single().Buckets.First();
            Assert.AreEqual(8m, first.Load); Assert.AreEqual(8m, first.Capacity, "the day length is the one of the project calendar");
            Assert.AreEqual(0, (await Check(e)).Data.Opened);
            // 9 hours a day is an overload
            e.Host.Write(u => { foreach (PcActivityResource x in u.Set<PcActivityResource>()) x.Quantity = 45m; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(1, (await Check(e)).Data.Opened);
        }

        [TestMethod]
        public async Task A_Run_That_Passed_Lapses_A_Run_That_Went_Resolves_And_Materials_And_Subcontracts_Never_Overload()
        {
            Env e = await NewEnv("lapse");
            e.Host.Write(u =>
            {
                AppHost.Add(u, new PcResourceOverload { ResourceId = e.Resource, FromDate = DateTime.Today.AddDays(-12), ToDate = DateTime.Today.AddDays(-8), PeakLoad = 4m, PeakCapacity = 2m, Status = PcOverloadStatus.Open, DetectedAt = DateTimeOffset.Now.AddDays(-20) });
                AppHost.Add(u, new PcResourceOverload { ResourceId = e.Resource, FromDate = DateTime.Today.AddDays(5), ToDate = DateTime.Today.AddDays(9), PeakLoad = 4m, PeakCapacity = 2m, Status = PcOverloadStatus.Open, DetectedAt = DateTimeOffset.Now.AddDays(-2) });
                // a load exists (a material over its "capacity"): it must not raise an alert
                PcResource mat = AppHost.Add(u, new PcResource { Code = "R-MAT", Title = "ورق", Kind = PcResourceKind.Material, Unit = PcResourceUnit.Day, Capacity = 1m, ResponsibleUnit = "ZZZ" });
                PcResource sub = AppHost.Add(u, new PcResource { Code = "R-SUB", Title = "پیمانکار", Kind = PcResourceKind.Subcontract, Unit = PcResourceUnit.Day, Capacity = 1m, ResponsibleUnit = "ZZZ" });
                u.SaveChanges();
            }, Seeder);
            var a = await NewProject(e, "LA", ("A1", 5, null));
            var b = await NewProject(e, "LB", ("B1", 5, null));
            e.Host.Write(u =>
            {
                List<PcResource> specials = u.Set<PcResource>().Where(x => x.Kind == PcResourceKind.Material || x.Kind == PcResourceKind.Subcontract).ToList();
                foreach (var proj in new[] { a, b })
                {
                    var plan = new PcResourcePlan { ContractId = proj.Contract, Version = 1, Status = PcResourcePlanStatus.Approved, MakerUserId = 1051, EditorUserIds = ",1051,", ApprovedAt = DateTimeOffset.Now, Assignments = new List<PcActivityResource>() };
                    foreach (PcResource res in specials)
                        plan.Assignments.Add(new PcActivityResource { ActivityId = proj.Activities.Values.Single(), ResourceId = res.Id, Quantity = 50m, Distribution = PcResourceDistribution.Uniform, Source = PcAssignmentSource.Human, EditedByUserId = 1051, EditedAt = DateTimeOffset.Now });
                    AppHost.Add(u, plan);
                }
                u.SaveChanges();
            }, Seeder);
            ResponseDto<PcLoadCheckResultDto> r = await Check(e);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(0, r.Data.Opened, "no alert for a material or a subcontract");
            Assert.AreEqual(1, r.Data.Lapsed); Assert.AreEqual(1, r.Data.Resolved);
            List<PcResourceOverload> rows = Overloads(e);
            Assert.AreEqual(PcOverloadStatus.Lapsed, rows[0].Status);
            Assert.AreEqual(PcOverloadStatus.Resolved, rows[1].Status);
        }

        [TestMethod]
        public async Task An_Activity_With_No_Working_Day_Puts_Its_Whole_Quantity_On_The_Next_Working_Day()
        {
            Env e = await NewEnv("zero");
            var a = await NewProject(e, "ZA", ("A1", 5, null));
            ApprovePlan(e, a.Contract, (a.Activities["A1"], 10m, PcResourceDistribution.Uniform));
            DateTime friday = Work(5).AddDays(1);
            Assert.AreEqual(DayOfWeek.Friday, friday.DayOfWeek);
            e.Host.Write(u => { PcActivity act = u.Set<PcActivity>().Single(x => x.Id == a.Activities["A1"]); act.EarlyStart = friday; act.EarlyFinish = friday; u.SaveChanges(); }, Seeder);
            ResponseDto<PcResourceLoadDto> load = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = e.Resource, From = Work(4), To = Work(7) }, Ct));
            List<PcLoadBucketDto> loaded = load.Data.Resources.Single().Buckets.Where(b => b.Load > 0).ToList();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(Work(6), loaded[0].Start, "the Saturday after the day off");
            Assert.AreEqual(10m, loaded[0].Load, "the quantity is not lost");
        }

        [TestMethod]
        public async Task The_Second_Person_Can_Close_A_Change_That_Cannot_Be_Reverted_And_The_Dtos_Carry_The_Reasons_Of_The_Caller()
        {
            Env e = await NewEnv("close");
            var p = await TwoProjects(e, true);
            await Check(e);
            await Propose(e, Overloads(e).Single().Id);
            int id = Proposals(e).Single().Id;
            // the DTOs say who may do what: the planner may apply and cannot accept; the PMO may accept
            int oid = Overloads(e).Single().Id;
            ResponseDto<PcResourceOverloadDto> asPlannerR = await As(e, Planner, s => s.GetOverloadAsync(oid, Ct));
            Assert.IsTrue(asPlannerR.Success, asPlannerR.Message);
            PcResourceOverloadDto asPlanner = asPlannerR.Data;
            Assert.AreEqual(PcLoadText.AcceptNeedsPmo, asPlanner.AcceptBlockedReason);
            Assert.IsNull((await As(e, Pmo, s => s.GetOverloadAsync(asPlanner.Id, Ct))).Data.AcceptBlockedReason);
            PcLevelingProposalDto prop = (await As(e, Planner, s => s.GetProposalAsync(id, Ct))).Data;
            Assert.IsNull(prop.ApplyBlockedReason); Assert.IsNull(prop.RejectBlockedReason); Assert.AreEqual(PcLoadText.ConfirmRoles, prop.ConfirmBlockedReason);

            // a PMO applies: the same person is told they cannot confirm it
            ClaimsPrincipal applier = AppHost.Principal(1013, "pmo.three", "PMO");
            Assert.IsTrue((await As(e, applier, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "apply" }, Ct))).Success);
            Assert.AreEqual(PcLoadText.SameUser, (await As(e, applier, s => s.GetProposalAsync(id, Ct))).Data.ConfirmBlockedReason);
            Assert.IsNull((await As(e, Pmo, s => s.GetProposalAsync(id, Ct))).Data.ConfirmBlockedReason);
            Assert.AreEqual(PcLoadText.SameUser, (await As(e, applier, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "confirm" }, Ct))).Message, "the applier who is also a PMO does not confirm");

            // while the revert is possible the second person refuses, not closes
            ResponseDto<PcLevelingProposalDto> early = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "close", Reason = "لازم است" }, Ct));
            Assert.IsFalse(early.Success); Assert.AreEqual(PcLoadText.RevertPossible, early.Message);
            // somebody changes the constraint again: the revert is impossible
            e.Host.Write(u => { PcActivity b1 = u.Set<PcActivity>().Single(x => x.Id == p.B1); b1.ConstraintDate = b1.ConstraintDate.Value.AddDays(1); u.SaveChanges(); }, Seeder);
            ResponseDto<PcLevelingProposalDto> refuse = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "refuse", Reason = "x" }, Ct));
            Assert.IsFalse(refuse.Success); Assert.AreEqual(PcLoadText.RevertChanged, refuse.Message);
            Assert.IsNull((await As(e, Pmo, s => s.GetProposalAsync(id, Ct))).Data.CloseBlockedReason);
            Assert.AreEqual(PcLoadText.SameUser, (await As(e, applier, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "close", Reason = "x" }, Ct))).Message);
            Assert.AreEqual(PcLoadText.CloseReason, (await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "close", Reason = " " }, Ct))).Message);
            ResponseDto<PcLevelingProposalDto> closed = await As(e, Pmo, s => s.ProposalActionAsync(new PcLevelingActionDto { Id = id, Action = "close", Reason = "قید را برنامه‌ریز عوض کرد" }, Ct));
            Assert.IsTrue(closed.Success, closed.Message);
            Assert.AreEqual(PcLevelingStatus.ClosedUnreverted, closed.Data.Status); Assert.IsFalse(closed.Data.Reverted); Assert.IsFalse(closed.Data.PendingSecondPerson);
            Assert.AreEqual(1011, closed.Data.RejectedByUserId);
        }

        [TestMethod]
        public async Task The_Lists_Are_Paged_The_Items_Come_Only_For_One_Resource_And_One_Row_Is_Read_By_Id()
        {
            Env e = await NewEnv("paging");
            await TwoProjects(e, true);
            ResponseDto<PcResourceLoadDto> all = await As(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { From = Work(0), To = Work(9) }, Ct));
            Assert.IsTrue(all.Data.Resources.Single().Buckets.All(b => b.Items.Count == 0), "no activities when no resource is named");
            Assert.IsTrue(all.Data.Resources.Single().Buckets.All(b => b.Load > 0));
            await Check(e);
            for (int i = 0; i < 4; i++) await Propose(e, Overloads(e).Single().Id);
            ResponseDto<PagedList<PcResourceOverloadDto>> page = await As(e, Planner, s => s.GetOverloadsAsync(new PcResourceOverloadQuery { PageNumber = 1, PageSize = 1, Status = PcOverloadStatus.Open }, Ct));
            Assert.AreEqual(1, page.Data.Count); Assert.AreEqual(1, page.Data.TotalCount);
            PcResourceOverloadDto one = (await As(e, Planner, s => s.GetOverloadAsync(page.Data[0].Id, Ct))).Data;
            Assert.AreEqual(one.Id, page.Data[0].Id);
            Assert.AreEqual(1, one.Proposals.Count);
            Assert.IsFalse((await As(e, Planner, s => s.GetOverloadAsync(99999, Ct))).Success);
            Assert.IsFalse((await As(e, Planner, s => s.GetProposalAsync(99999, Ct))).Success);
            Assert.IsFalse((await As(e, Outsider, s => s.GetProposalAsync(one.Proposals[0].Id, Ct))).Success);
        }
    }
}
