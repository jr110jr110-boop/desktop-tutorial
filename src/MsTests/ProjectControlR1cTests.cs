using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// R1c, project control: the figures of a data date come from the dated progress events, the planned value of an approved baseline is frozen, the penalty is counted once
    /// under one cap, the contract is the terms plus the approved changes (each counted once), the critical path method keeps a milestone that must start on a day at zero float
    /// and schedules an activity on its own calendar, and the change process (review by two people, frozen terms, variation orders, baselines from change requests) runs end to end.
    /// </summary>
    [TestClass]
    public class ProjectControlR1cTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(12, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Planning = AppHost.Principal(24, "planning.manager", "PLANNING");
        private static readonly ClaimsPrincipal Engineering = AppHost.Principal(21, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(22, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(26, "commercial.manager", "COMMERCIAL");
        private static readonly ClaimsPrincipal Commercial2 = AppHost.Principal(27, "commercial.two", "COMMERCIAL");
        private static readonly ClaimsPrincipal PmoAndCommercial = AppHost.Principal(30, "pmo.and.commercial", "PMO", "COMMERCIAL");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(31, "ceo", "CEO");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(28, "accountant", "FINANCIAL");
        private static readonly ClaimsPrincipal Accountant2 = AppHost.Principal(29, "accountant.two", "FINANCIAL");

        private const string SameUser = "تفکیک وظایف";
        private const string NotAllowed = "شما مجوز این کار را ندارید";

        private static AppHost NewHost(string name) => new AppHost("PcR1c_" + name + "_" + Guid.NewGuid().ToString("N"));

        private static int NewContract(AppHost host)
        {
            int id = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static DateTime Day(int offset) => DateTime.Today.AddDays(offset);

        // ------------------------------------------------------------------------------------------------ H4: the percent as known on a day

        private static PcProgressEvent Ev(int id, int activity, DateTime date, decimal percent) =>
            new PcProgressEvent { Id = id, ContractId = 1, ActivityId = activity, EventDate = date, Percent = percent, RecordedAt = DateTimeOffset.Now, RecordedByUserId = 0, Source = "engine" };

        [TestMethod]
        public void ThePercentAsOfADayIsTheLatestEventOnOrBeforeItAndNothingFromTheFuture()
        {
            var events = new List<PcProgressEvent>
            {
                Ev(1, 10, Day(-30), 10m), Ev(2, 10, Day(-20), 50m), Ev(3, 10, Day(-10), 80m),
                Ev(4, 11, Day(-20), 25m), Ev(5, 11, Day(-20), 30m),   // the same day: the later row counts
                Ev(6, 12, Day(5), 100m),                              // not yet happened
            };
            Dictionary<int, decimal> early = PcProgressEvents.AsOf(events, Day(-25));
            Assert.AreEqual(10m, early[10]);
            Assert.IsFalse(early.ContainsKey(11), "no event yet: not in the result");
            Dictionary<int, decimal> middle = PcProgressEvents.AsOf(events, Day(-20));
            Assert.AreEqual(50m, middle[10], "an event of the day itself counts");
            Assert.AreEqual(30m, middle[11], "two events on one day: the later one");
            Dictionary<int, decimal> late = PcProgressEvents.AsOf(events, Day(0));
            Assert.AreEqual(80m, late[10]);
            Assert.IsFalse(late.ContainsKey(12), "an event dated in the future is not known today");
            Assert.AreEqual(0, PcProgressEvents.AsOf(new List<PcProgressEvent>(), Day(0)).Count);
        }

        [TestMethod]
        public async Task EarnedValueOfAPastDataDateIsReadFromTheDatedEventsNotFromTheModulesOfToday()
        {
            AppHost host = NewHost("Ev");
            int contractId = 61, noEventsContract = 62;
            int a = 0, b = 0, c = 0;
            host.Write(u =>
            {
                var x = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "A", Title = "A", DurationDays = 20, BudgetAtCompletion = 1000m, Weight = 1m, EarlyStart = Day(-40), EarlyFinish = Day(-6), ProgressMethod = PcProgressMethod.Manual });
                var y = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "B", Title = "B", DurationDays = 20, BudgetAtCompletion = 3000m, Weight = 1m, EarlyStart = Day(-30), EarlyFinish = Day(-3), ProgressMethod = PcProgressMethod.Manual });
                var z = AppHost.Add(u, new PcActivity { ContractId = noEventsContract, Code = "Z", Title = "Z", DurationDays = 10, BudgetAtCompletion = 500m, Weight = 1m, EarlyStart = Day(-20), EarlyFinish = Day(-8), ProgressMethod = PcProgressMethod.Manual });
                u.SaveChanges();
                a = x.Id; b = y.Id; c = z.Id;
                DateTimeOffset now = DateTimeOffset.Now;
                foreach ((int act, int day, decimal pct) in new[] { (a, -30, 10m), (a, -20, 50m), (a, -10, 80m), (b, -20, 25m), (b, -8, 100m) })
                    u.Set<PcProgressEvent>().Add(new PcProgressEvent { ContractId = contractId, ActivityId = act, EventDate = Day(day), Percent = pct, RecordedAt = now, RecordedByUserId = 0, Source = "engine" });
                u.SaveChanges();
            }, Seeder);

            Task<PcEvmDto> Evm(int contract, int dayOffset) => host.As(Pmo1, p => p.GetRequiredService<IPcProgressService>().ComputeEvmAsync(contract, Day(dayOffset), CancellationToken.None));

            PcEvmDto d25 = await Evm(contractId, -25);
            Assert.IsTrue(d25.FromEvents);
            Assert.AreEqual(4000m, d25.Bac);
            Assert.AreEqual(100m, d25.Ev, "A was at 10 percent on that day, B had no event");
            PcEvmDto d20 = await Evm(contractId, -20);
            Assert.AreEqual(1250m, d20.Ev, "A 50 percent of 1000 and B 25 percent of 3000: the events of the day count");
            PcEvmDto d9 = await Evm(contractId, -9);
            Assert.AreEqual(1550m, d9.Ev, "A 80 percent, B still 25 percent");
            PcEvmDto d8 = await Evm(contractId, -8);
            Assert.AreEqual(3800m, d8.Ev, "B reached 100 percent that day");
            Assert.IsTrue(d25.Pv < d9.Pv && d9.Pv <= d8.Pv, "the planned value grows with the data date");

            // the modules of today say zero percent (nothing was reported there); a past date still shows what was known then
            PcEvmDto today = await Evm(contractId, 0);
            Assert.IsFalse(today.FromEvents, "today is read from the modules");
            Assert.AreEqual(0m, today.Ev);

            // a contract that has no event at all cannot answer for a past date
            PcEvmDto none = await Evm(noEventsContract, -3);
            Assert.AreEqual(0m, none.Ev);
            Assert.IsNotNull(none.Note);
            Assert.IsTrue(none.Note.Contains("رویداد"), none.Note);
        }

        // ------------------------------------------------------------------------------------------------ M4: the frozen planned value

        private static readonly DateTime Monday = new DateTime(2026, 3, 2);

        private static List<PcCurveItem> TwoItems(int secondCalendar = 0) => new List<PcCurveItem>
        {
            new PcCurveItem { ActivityId = 1, Start = Monday, Finish = Monday.AddDays(11), Budget = 1000m, Duration = 10, CalendarKey = 0 },
            secondCalendar == 0
                ? new PcCurveItem { ActivityId = 2, Start = Monday.AddDays(7), Finish = Monday.AddDays(18), Budget = 500m, Duration = 10, CalendarKey = 0 }
                : new PcCurveItem { ActivityId = 2, Start = Monday.AddDays(7), Finish = Monday.AddDays(13), Budget = 500m, Duration = 7, CalendarKey = secondCalendar },
        };

        [TestMethod]
        public void ThePlannedValueCurveSpreadsTheBudgetOverTheWorkingDaysOfTheCalendar()
        {
            PcCurve curve = PcCurve.Build(TwoItems(), key => PcWorkCalendar.MondayToFriday(), PcAccrualMode.Linear);
            Assert.AreEqual(1500m, curve.Total);
            Assert.AreEqual(0m, curve.Pv(Monday.AddDays(-1)), "nothing before the first activity");
            Assert.AreEqual(100m, curve.Pv(Monday), "one of ten working days");
            Assert.AreEqual(500m, curve.Pv(Monday.AddDays(4)), "five of ten working days");
            Assert.AreEqual(500m, curve.Pv(Monday.AddDays(6)), "the weekend adds nothing");
            Assert.AreEqual(950m, curve.Pv(Monday.AddDays(9)), "eight of ten working days of the first activity (800) and three of ten of the second (150)");
            Assert.AreEqual(1250m, curve.Pv(Monday.AddDays(11)), "the first activity is complete, the second is half done");
            Assert.AreEqual(1500m, curve.Pv(Monday.AddDays(60)), "after the end: the budget at completion");

            PcCurve atEnd = PcCurve.Build(TwoItems(), key => PcWorkCalendar.MondayToFriday(), PcAccrualMode.AtEnd);
            Assert.AreEqual(0m, atEnd.Pv(Monday.AddDays(4)), "accrual at the end of the activity: nothing before it");
            Assert.AreEqual(1000m, atEnd.Pv(Monday.AddDays(11)));
            Assert.AreEqual(1500m, atEnd.Pv(Monday.AddDays(18)));
        }

        [TestMethod]
        public void TheCurveReadBackFromTheStoredRowsIsTheCurveOfTheDayOfTheApprovalEvenIfAHolidayIsAddedLater()
        {
            List<PcCurveItem> items = TwoItems();
            PcCurve original = PcCurve.Build(items, key => PcWorkCalendar.MondayToFriday(), PcAccrualMode.Linear);
            List<PcBaselineCostPeriod> rows = original.ToRows(7);
            Assert.AreEqual(original.Days, rows.Count, "one row per day of the project range when every activity uses the calendar of the contract");
            Assert.IsTrue(rows.All(r => r.CalendarId == null && r.BaselineId == 7));

            PcCurve frozen = PcCurve.FromRows(rows, PcAccrualMode.Linear);
            for (int d = -2; d < 25; d++)
                Assert.AreEqual(original.Pv(Monday.AddDays(d)), frozen.Pv(Monday.AddDays(d)), "day " + d);
            Assert.AreEqual(original.Fraction(items[0], Monday.AddDays(9)), frozen.Fraction(items[0], Monday.AddDays(9)));

            // a holiday added to the calendar after the approval: a curve built again moves, the stored one does not
            var withHoliday = new PcWorkCalendar(PcWorkCalendar.MaskOf(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday), new[] { Monday.AddDays(2) }, null);
            PcCurve rebuilt = PcCurve.Build(items, key => withHoliday, PcAccrualMode.Linear);
            Assert.AreNotEqual(original.Pv(Monday.AddDays(4)), rebuilt.Pv(Monday.AddDays(4)), "the recomputed plan would have moved");
            Assert.AreEqual(500m, frozen.Pv(Monday.AddDays(4)), "the frozen plan did not");
        }

        [TestMethod]
        public void TheFrozenCurveKeepsTheOwnCalendarOfAnActivityAndStoresFourDecimals()
        {
            var sevenDays = new PcWorkCalendar(0x7F, null, null);
            Func<int, PcWorkCalendar> calendars = key => key == 7 ? sevenDays : PcWorkCalendar.MondayToFriday();
            List<PcCurveItem> items = TwoItems(secondCalendar: 7);
            PcCurve original = PcCurve.Build(items, calendars, PcAccrualMode.Linear);
            List<PcBaselineCostPeriod> rows = original.ToRows(3);
            Assert.IsTrue(rows.Any(r => r.CalendarId == 7), "the days of the own calendar are stored as well");
            Assert.IsTrue(rows.Where(r => r.CalendarId == 7).All(r => r.PlannedCumulative == 0m), "only the project rows carry the value");

            PcCurve frozen = PcCurve.FromRows(rows, PcAccrualMode.Linear);
            Assert.AreEqual(3m / 7m, frozen.Fraction(items[1], Monday.AddDays(9)), "three of seven days of the seven-day calendar");
            for (int d = 0; d < 20; d++)
                Assert.AreEqual(original.Pv(Monday.AddDays(d)), frozen.Pv(Monday.AddDays(d)), "day " + d);
            Assert.IsTrue(rows.Where(r => r.CalendarId == null).All(r => Math.Round(r.PlannedCumulative, 4) == r.PlannedCumulative), "the value has at most four decimals (the column has four)");
        }

        // ------------------------------------------------------------------------------------------------ H1: what a baseline is built from

        [TestMethod]
        public void ABaselineIsBuiltOnlyFromActivitiesWithABudgetAboveZeroAndABudgetOrAWeightEach()
        {
            Assert.IsNotNull(PcBaselineRows.CreationProblem(new List<PcActivity>()), "no activity");
            var zero = new List<PcActivity> { new PcActivity { Code = "A", BudgetAtCompletion = 0m, Weight = 1m }, new PcActivity { Code = "B", BudgetAtCompletion = 0m, Weight = 1m } };
            Assert.IsNotNull(PcBaselineRows.CreationProblem(zero), "the budget at completion is zero");
            var bare = new List<PcActivity> { new PcActivity { Code = "A", BudgetAtCompletion = 100m }, new PcActivity { Code = "NO-BUDGET", BudgetAtCompletion = 0m, Weight = 0m } };
            string problem = PcBaselineRows.CreationProblem(bare);
            Assert.IsNotNull(problem);
            Assert.IsTrue(problem.Contains("NO-BUDGET"), "the activity without budget and weight is named: " + problem);
            var ok = new List<PcActivity> { new PcActivity { Code = "A", BudgetAtCompletion = 100m, Weight = 0m }, new PcActivity { Code = "B", BudgetAtCompletion = 0m, Weight = 2m } };
            Assert.IsNull(PcBaselineRows.CreationProblem(ok));

            var rows = new List<PcBaselineActivity> { new PcBaselineActivity { Code = "A", Budget = 0m, Weight = 1m } };
            Assert.IsNotNull(PcBaselineRows.RowsProblem(rows), "the draft rows are checked again at the approval");
        }

        [TestMethod]
        public void ADraftBaselineDiffersFromThePlanWhenAnActivityWasChangedAddedOrRemoved()
        {
            DateTime s = Monday, f = Monday.AddDays(4);
            var live = new List<PcActivity>
            {
                new PcActivity { Id = 1, Code = "A", EarlyStart = s, EarlyFinish = f, DurationDays = 5, Weight = 1m, BudgetAtCompletion = 100m },
                new PcActivity { Id = 2, Code = "B", EarlyStart = s, EarlyFinish = f, DurationDays = 5, Weight = 1m, BudgetAtCompletion = 200m },
            };
            var rows = new List<PcBaselineActivity>
            {
                new PcBaselineActivity { ActivityId = 1, Code = "A", Start = s, Finish = f, DurationDays = 5, Weight = 1m, Budget = 100m },
                new PcBaselineActivity { ActivityId = 2, Code = "B", Start = s, Finish = f, DurationDays = 5, Weight = 1m, Budget = 200m },
            };
            Assert.AreEqual(0, PcBaselineRows.Differences(rows, live, null).Count, "the draft is the plan");
            live[1].BudgetAtCompletion = 250m;
            CollectionAssert.AreEqual(new[] { "B" }, PcBaselineRows.Differences(rows, live, null).ToArray(), "a changed budget");
            live[1].BudgetAtCompletion = 200m;
            live.Add(new PcActivity { Id = 3, Code = "C", EarlyStart = s, EarlyFinish = f, DurationDays = 5, BudgetAtCompletion = 1m });
            CollectionAssert.AreEqual(new[] { "C" }, PcBaselineRows.Differences(rows, live, null).ToArray(), "an activity added after the draft");
            live.RemoveAt(2); live.RemoveAt(0);
            CollectionAssert.AreEqual(new[] { "A" }, PcBaselineRows.Differences(rows, live, null).ToArray(), "an activity removed after the draft");
        }

        [TestMethod]
        public void ThePlanFieldsOfAnActivityAreTheOnesTheChangeProcessGuards()
        {
            var a = new PcActivity { Id = 1, Code = "A", Title = "old", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 100m, ResponsibleUnit = "PLANNING", SourceType = PcSourceType.Manual };
            PcActivityDto Same() => new PcActivityDto { Id = 1, Code = "A", Title = "old", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 100m, ResponsibleUnit = "PLANNING", SourceType = PcSourceType.Manual };
            Assert.IsFalse(PcPlanLock.ActivityPlanChanged(a, Same(), "A"));
            PcActivityDto retitled = Same(); retitled.Title = "new title"; retitled.ResponsibleUnit = "ENGINEERING";
            Assert.IsFalse(PcPlanLock.ActivityPlanChanged(a, retitled, "A"), "a title or the responsible unit is not the plan");
            PcActivityDto longer = Same(); longer.DurationDays = 6;
            Assert.IsTrue(PcPlanLock.ActivityPlanChanged(a, longer, "A"));
            PcActivityDto dearer = Same(); dearer.BudgetAtCompletion = 101m;
            Assert.IsTrue(PcPlanLock.ActivityPlanChanged(a, dearer, "A"));
            PcActivityDto heavier = Same(); heavier.Weight = 2m;
            Assert.IsTrue(PcPlanLock.ActivityPlanChanged(a, heavier, "A"));
            PcActivityDto constrained = Same(); constrained.ConstraintType = PcConstraint.StartNoEarlierThan; constrained.ConstraintDate = Monday;
            Assert.IsTrue(PcPlanLock.ActivityPlanChanged(a, constrained, "A"));
            Assert.IsTrue(PcPlanLock.ActivityPlanChanged(a, Same(), "A-2"), "the code");
        }

        [TestMethod]
        public void TheScenarioOfAChangeRequestIsCheckedAgainstThePlan()
        {
            var activities = new List<PcActivity>
            {
                new PcActivity { Id = 1, Code = "A", DurationDays = 5, BudgetAtCompletion = 100m, Status = PcActivityStatus.NotStarted },
                new PcActivity { Id = 2, Code = "B", DurationDays = 5, BudgetAtCompletion = 200m, Status = PcActivityStatus.InProgress, ActualStart = Monday, PercentCache = 30m },
            };
            var empty = new HashSet<int>();
            string Check(params PcChangeScenarioItemDto[] items) => PcScenario.Validate(items, activities, empty, empty, empty);

            Assert.IsNull(Check());
            Assert.IsNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 1, DurationDelta = 2, BudgetDelta = 50m }));
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 9 }), "an activity of another contract");
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 1, DurationDelta = -6 }), "a negative duration");
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 1, BudgetDelta = -101m }), "a negative budget");
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Remove, ActivityId = 2 }), "a started activity stays in the plan");
            Assert.IsNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Remove, ActivityId = 1 }));
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 1 }, new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Remove, ActivityId = 1 }), "twice in one scenario");
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "A", Title = "dup", DurationDays = 1 }), "the code exists");
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "N", Title = "new", DurationDays = 1, ProgressMethod = PcProgressMethod.SupplyReceipt }), "a new activity starts manual");
            Assert.IsNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "N", Title = "new", DurationDays = 3, BudgetAtCompletion = 10m, Links = new List<PcScenarioLinkDto> { new PcScenarioLinkDto { OtherActivityId = 1, OtherIsPredecessor = true } } }));
            Assert.IsNotNull(Check(new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "N", Title = "new", DurationDays = 3, Links = new List<PcScenarioLinkDto> { new PcScenarioLinkDto { OtherCode = "X", OtherIsPredecessor = true } } }), "a link to a code that nothing adds");

            Dictionary<int, PcActivity> byId = activities.ToDictionary(a => a.Id);
            decimal delta = PcScenario.CostDelta(new[]
            {
                new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = 1, BudgetDelta = 50m },
                new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "N", Title = "n", BudgetAtCompletion = 30m },
                new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Remove, ActivityId = 1 },
            }, byId);
            Assert.AreEqual(50m + 30m - 100m, delta, "the deltas, the added budgets, minus the removed budgets");
        }

        // ------------------------------------------------------------------------------------------------ L1, M2: the critical path method

        [TestMethod]
        public void AMilestoneThatMustStartOnADayHasZeroFloatAndIsCritical()
        {
            PcWorkCalendar calendar = PcWorkCalendar.MondayToFriday();
            var milestone = new CpmNode { Id = 1, Duration = 0, Constraint = PcConstraint.MustStartOn, ConstraintDate = new DateTime(2026, 3, 4) };
            CpmResult result = PcCpm.Run(new List<CpmNode> { milestone }, new List<CpmLink>(), calendar, Monday, false, null);
            Assert.AreEqual(0, result.Items[1].TotalFloat, "a milestone fixed on a day has no float (it was minus one before)");
            Assert.IsTrue(result.Items[1].Critical);
            Assert.AreEqual(new DateTime(2026, 3, 4), result.Items[1].EarlyFinish, "it is shown on the day it is fixed to");

            var task = new CpmNode { Id = 1, Duration = 2, Constraint = PcConstraint.MustStartOn, ConstraintDate = new DateTime(2026, 3, 4) };
            CpmResult withDuration = PcCpm.Run(new List<CpmNode> { task }, new List<CpmLink>(), PcWorkCalendar.MondayToFriday(), Monday, false, null);
            Assert.AreEqual(0, withDuration.Items[1].TotalFloat, "an activity with a duration was right already");

            // a predecessor that finishes earlier than the fixed day keeps its own float
            var before = new CpmNode { Id = 1, Duration = 3 };
            var fixedMilestone = new CpmNode { Id = 2, Duration = 0, Constraint = PcConstraint.MustStartOn, ConstraintDate = new DateTime(2026, 3, 5) };
            CpmResult network = PcCpm.Run(new List<CpmNode> { before, fixedMilestone }, new List<CpmLink> { new CpmLink { PredecessorId = 1, SuccessorId = 2, Type = PcDependencyType.FS } }, PcWorkCalendar.MondayToFriday(), Monday, false, null);
            Assert.AreEqual(0, network.Items[2].TotalFloat);
            Assert.AreEqual(1, network.Items[1].TotalFloat, "the milestone waits for Thursday, so the predecessor may slip one day");
        }

        [TestMethod]
        public void AnActivityOnItsOwnCalendarIsScheduledOnThatCalendar()
        {
            var sevenDays = new PcWorkCalendar(0x7F, null, null);
            CpmResult Run(PcWorkCalendar own)
            {
                var nodes = new List<CpmNode>
                {
                    new CpmNode { Id = 1, Duration = 7, Calendar = own },
                    new CpmNode { Id = 2, Duration = 1 },
                };
                return PcCpm.Run(nodes, new List<CpmLink> { new CpmLink { PredecessorId = 1, SuccessorId = 2, Type = PcDependencyType.FS } }, PcWorkCalendar.MondayToFriday(), Monday, false, null);
            }
            CpmResult onContractCalendar = Run(null), onOwnCalendar = Run(sevenDays);
            Assert.AreEqual(new DateTime(2026, 3, 10), onContractCalendar.Items[1].EarlyFinish, "seven working days Monday to Friday end on the Tuesday of the next week");
            Assert.AreEqual(new DateTime(2026, 3, 8), onOwnCalendar.Items[1].EarlyFinish, "seven days of a seven-day calendar end on the Sunday");
            Assert.IsTrue(onOwnCalendar.Items[2].EarlyStart < onContractCalendar.Items[2].EarlyStart, "the successor starts earlier");
            Assert.IsTrue(onOwnCalendar.ProjectFinish < onContractCalendar.ProjectFinish);
        }

        // ------------------------------------------------------------------------------------------------ H3: the contract as it stands

        [TestMethod]
        public void TheEffectiveContractCountsAnAmendmentOnceWhenAnApprovedVariationOrderCarriesIt()
        {
            var orders = new List<(int? AmendmentId, decimal Price, int Days)> { (null, 100m, 10), (5, 200m, 20) };
            var amendments = new List<(int Id, decimal Price, int Days)> { (5, 200m, 20), (6, 50m, 5) };
            PcEffectiveContract e = PcEffective.Build(9, 1000m, new DateTime(2026, 12, 1), orders, amendments);
            Assert.AreEqual(300m, e.VariationPrice);
            Assert.AreEqual(50m, e.AmendmentPrice, "amendment 5 is carried by an order: only amendment 6 counts on its own");
            Assert.AreEqual(35, e.DaysDelta);
            Assert.AreEqual(1350m, e.Value);
            Assert.AreEqual(new DateTime(2027, 1, 5), e.DeliveryDate);
            Assert.AreEqual(new DateTime(2026, 12, 1), e.BaseDeliveryDate);

            PcEffectiveContract none = PcEffective.Build(9, 1000m, null, new List<(int?, decimal, int)>(), new List<(int, decimal, int)>());
            Assert.AreEqual(1000m, none.Value);
            Assert.IsNull(none.DeliveryDate, "no date in the terms: no date after the change either");
        }

        [TestMethod]
        public async Task TheEffectiveContractReadsOnlyApprovedOrdersAndCeoApprovedAmendments()
        {
            AppHost host = NewHost("Effective");
            int contractId = NewContract(host);
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contractId, ProjectCode = "P1", ValueExVat = 1000m, ContractDeliveryDate = new DateTime(2026, 12, 1) });
                var carried = AppHost.Add(u, new PcContractAmendment { ContractId = contractId, Number = "AMD-1", Description = "x", PriceDelta = 100m, DaysDelta = 10, CeoApprovedAt = DateTimeOffset.Now });
                AppHost.Add(u, new PcContractAmendment { ContractId = contractId, Number = "AMD-2", Description = "x", PriceDelta = 50m, DaysDelta = 5, CeoApprovedAt = DateTimeOffset.Now });
                AppHost.Add(u, new PcContractAmendment { ContractId = contractId, Number = "AMD-3", Description = "not approved by the CEO", PriceDelta = 7000m, DaysDelta = 700 });
                u.SaveChanges();
                AppHost.Add(u, new PcVariationOrder { ContractId = contractId, Number = "VO-1", Description = "x", AmendmentId = carried.Id, PriceDelta = 100m, DaysDelta = 10, Status = PcVariationStatus.Approved });
                AppHost.Add(u, new PcVariationOrder { ContractId = contractId, Number = "VO-2", Description = "x", PriceDelta = 30m, DaysDelta = 3, Status = PcVariationStatus.Approved });
                AppHost.Add(u, new PcVariationOrder { ContractId = contractId, Number = "VO-3", Description = "a draft", PriceDelta = 900m, DaysDelta = 90, Status = PcVariationStatus.Draft });
                AppHost.Add(u, new PcVariationOrder { ContractId = contractId, Number = "VO-4", Description = "submitted", PriceDelta = 800m, DaysDelta = 80, Status = PcVariationStatus.Submitted });
                u.SaveChanges();
            }, Seeder);

            PcEffectiveContract e = host.Read(u => PcEffective.EffectiveContractAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(1000m, e.BaseValue);
            Assert.AreEqual(1180m, e.Value, "1000 + order 100 (carries amendment 1) + order 30 + amendment 2 (50)");
            Assert.AreEqual(18, e.DaysDelta);
            Assert.AreEqual(new DateTime(2026, 12, 19), e.DeliveryDate);

            Dictionary<int, decimal> deltas = host.Read(u => PcEffective.PriceDeltasAsync(u, new List<int> { contractId, contractId + 100 }, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(180m, deltas[contractId]);
            Assert.IsFalse(deltas.ContainsKey(contractId + 100), "a contract without a change is not in the result");

            // the same figure through the contract terms the screens read
            ResponseDto<PcContractTermsDto> terms = await host.As(Pmo1, p => p.GetRequiredService<IPcProjectService>().GetTermsAsync(contractId, CancellationToken.None));
            Assert.IsTrue(terms.Success, terms.Message);
            Assert.AreEqual(1000m, terms.Data.ValueExVat);
            Assert.AreEqual(1180m, terms.Data.CurrentValue);
            Assert.AreEqual(new DateTime(2026, 12, 19), terms.Data.CurrentDeliveryDate);
        }

        [TestMethod]
        public void AVariationOrderNeedsTheCeoWhenItsPriceChangeIsAboveTheThreshold()
        {
            Assert.IsFalse(PcCommercialService.VariationNeedsCeo(1_000_000m, 50_000m, 5m), "exactly five percent");
            Assert.IsTrue(PcCommercialService.VariationNeedsCeo(1_000_000m, 50_001m, 5m));
            Assert.IsTrue(PcCommercialService.VariationNeedsCeo(1_000_000m, -60_000m, 5m), "a reduction counts by its size");
            Assert.IsFalse(PcCommercialService.VariationNeedsCeo(1_000_000m, 0m, 5m), "a change of time alone");
            Assert.IsTrue(PcCommercialService.VariationNeedsCeo(0m, 1m, 5m), "no value on record: any price change is the CEO's");
        }

        // ------------------------------------------------------------------------------------------------ H5, M8, M16

        [TestMethod]
        public void ThePenaltyOfAContractIsCountedUnderOneCapWhateverTheNumberOfItems()
        {
            var items = new List<(decimal Base, int LateDays)> { (1_000_000m, 70), (1_000_000m, 140), (1_000_000m, 70) };
            // 0.5 percent a week, cap 10 percent of the contract value (100,000 rial): the items are served in order
            decimal[] capped = PcDelivery.PenaltyAmounts(1_000_000m, 0.5m, 10m, items, 0m);
            CollectionAssert.AreEqual(new[] { 50_000m, 50_000m, 0m }, capped, "the second item would be 100,000 on its own but only 50,000 is left under the cap");
            Assert.AreEqual(100_000m, capped.Sum());

            decimal[] withEarlier = PcDelivery.PenaltyAmounts(1_000_000m, 0.5m, 10m, items, 30_000m);
            CollectionAssert.AreEqual(new[] { 50_000m, 20_000m, 0m }, withEarlier, "what was decided before is part of the cap");
            Assert.AreEqual(70_000m, withEarlier.Sum());

            decimal[] noCap = PcDelivery.PenaltyAmounts(1_000_000m, 0.5m, null, items, 0m);
            CollectionAssert.AreEqual(new[] { 50_000m, 100_000m, 50_000m }, noCap, "no cap in the terms: each item on its own");

            decimal[] full = PcDelivery.PenaltyAmounts(1_000_000m, 0.5m, 10m, items, 100_000m);
            Assert.AreEqual(0m, full.Sum(), "the cap is used up");
        }

        [TestMethod]
        public void TheSinglePenaltyOfAnItemIsCappedOnItsOwnBaseToo()
        {
            Assert.AreEqual(0m, PcCommercialService.PenaltyAmount(1000m, 0.5m, 0, 10m));
            Assert.AreEqual(0m, PcCommercialService.PenaltyAmount(0m, 0.5m, 10, 10m));
            Assert.AreEqual(50m, PcCommercialService.PenaltyAmount(1000m, 0.5m, 70, 10m), "5 percent after ten weeks");
            Assert.AreEqual(100m, PcCommercialService.PenaltyAmount(1000m, 0.5m, 700, 10m), "capped at 10 percent of the base");
        }

        [TestMethod]
        public void AWorkOrderIsDeliveredOnlyWhenTheFullQuantityHasShipped()
        {
            DateTime d1 = new DateTime(2026, 5, 1), d2 = new DateTime(2026, 5, 20);
            var oneOfTwo = new List<(DateTime DeliveredAt, int ShipmentId, decimal? LineQuantity)> { (d1, 1, 4m) };
            Assert.IsNull(PcDelivery.FullDeliveryDate(10m, oneOfTwo), "four of ten is not delivered");
            var complete = new List<(DateTime DeliveredAt, int ShipmentId, decimal? LineQuantity)> { (d2, 2, 6m), (d1, 1, 4m) };
            Assert.AreEqual(d2, PcDelivery.FullDeliveryDate(10m, complete), "the day the last part arrived, whatever the order of the list");
            var short9 = new List<(DateTime DeliveredAt, int ShipmentId, decimal? LineQuantity)> { (d1, 1, 4m), (d2, 2, 5m) };
            Assert.IsNull(PcDelivery.FullDeliveryDate(10m, short9));
            var noLines = new List<(DateTime DeliveredAt, int ShipmentId, decimal? LineQuantity)> { (d1, 1, null) };
            Assert.AreEqual(d1, PcDelivery.FullDeliveryDate(10m, noLines), "a shipment without lines (older data) delivers the whole order");
            Assert.AreEqual(d1, PcDelivery.FullDeliveryDate(0m, noLines), "an order without a quantity counts as one unit");
        }

        [TestMethod]
        public void TheWarrantyEndsAtTheEarlierOfCommissioningPlusMonthsAndDeliveryPlusMonths()
        {
            DateTime commissioning = new DateTime(2026, 3, 1), delivery = new DateTime(2026, 1, 15);
            Assert.AreEqual(new DateTime(2027, 3, 1), PcWarranty.End(commissioning, delivery, 12, 18), "twelve months after commissioning come before eighteen after delivery");
            Assert.AreEqual(new DateTime(2027, 7, 15), PcWarranty.End(new DateTime(2026, 9, 1), delivery, 12, 18), "late commissioning: the delivery date decides");
            Assert.AreEqual(new DateTime(2027, 7, 15), PcWarranty.End(null, delivery, 12, 18), "not commissioned yet: the delivery date");
            Assert.AreEqual(new DateTime(2027, 3, 1), PcWarranty.End(commissioning, null, 12, 18));
            Assert.IsNull(PcWarranty.End(null, null, 12, 18));
            Assert.AreEqual(new DateTime(2027, 7, 15), PcWarranty.End(commissioning, delivery, 0, 18), "zero months after commissioning leaves that date out");
        }

        // ------------------------------------------------------------------------------------------------ M1

        [TestMethod]
        public void WhoEditedADraftIsRememberedAndIsNotItsChecker()
        {
            string ids = PcMaker.WithEditor(null, 5);
            Assert.AreEqual(",5,", ids);
            ids = PcMaker.WithEditor(ids, 5);
            Assert.AreEqual(",5,", ids, "listed once");
            ids = PcMaker.WithEditor(ids, 7);
            Assert.AreEqual(",5,7,", ids);
            Assert.AreEqual(",5,7,", PcMaker.WithEditor(ids, 0), "user zero is the server job");
            Assert.IsTrue(PcMaker.Touched(0, ids, 7));
            Assert.IsTrue(PcMaker.Touched(9, null, 9), "the creator");
            Assert.IsFalse(PcMaker.Touched(9, ids, 10));
            Assert.IsFalse(PcMaker.Touched(0, null, 0));
        }

        // ------------------------------------------------------------------------------------------------ H8 and H2: the review, the frozen terms and the amendment

        private static PcReviewDto FullReview(int contractId) => new PcReviewDto
        {
            ContractId = contractId, Kind = PcReviewKind.StandardProduct, RequirementsDefined = true, DifferencesResolved = true, DeliveryFeasible = true, StandardsItpAgreed = true,
            GuaranteesReviewed = true, PenaltiesReviewed = true, PaymentTermsReviewed = true, CurrencyReviewed = true,
        };

        private static Task<ResponseDto<PcReviewDto>> Accept(AppHost host, ClaimsPrincipal who, int reviewId, string slot = null) =>
            host.As(who, p => p.GetRequiredService<IPcProjectService>().ReviewActionAsync(new PcReviewActionDto { Id = reviewId, Action = "accept", Slot = slot }, CancellationToken.None));

        private static Task<ResponseDto<PcContractTermsDto>> SaveTerms(AppHost host, ClaimsPrincipal who, PcContractTermsDto dto) =>
            host.As(who, p => p.GetRequiredService<IPcProjectService>().SaveTermsAsync(dto, CancellationToken.None));

        [TestMethod]
        public async Task TheContractReviewIsAcceptedByTwoDifferentPeopleAndThenTheTermsAreFrozenUntilACeoApprovedAmendment()
        {
            AppHost host = NewHost("Review");
            int contractId = NewContract(host);
            var terms = new PcContractTermsDto
            {
                ContractId = contractId, ValueExVat = 1_000_000m, ContractDeliveryDate = new DateTime(2026, 12, 1), LatePenaltyPercentPerWeek = 0.5m, PenaltyCapPercent = 10m, GraceDays = 3,
                DataDateWeekday = 3, ClientReviewDays = 14, PenaltyBasis = PcPenaltyBasis.PerContract, WarrantyMonthsAfterCommissioning = 12, WarrantyMonthsAfterDelivery = 18,
            };
            Assert.IsTrue((await SaveTerms(host, Commercial, terms)).Success, "before the review the terms are drafted freely");
            terms.ValueExVat = 1_200_000m;
            Assert.IsTrue((await SaveTerms(host, Planning, terms)).Success, "... and corrected freely");

            // a project code before the review is refused
            ResponseDto<PcContractTermsDto> early = await host.As(Planning, p => p.GetRequiredService<IPcProjectService>().GenerateProjectCodeAsync(new GenerateProjectCodeDto { ContractId = contractId, Category = PcCategory.Manufacturing }, CancellationToken.None));
            Assert.IsFalse(early.Success);
            Assert.IsTrue(early.Message.Contains("بازنگری"), "no project code before the contract review is accepted: " + early.Message);

            ResponseDto<PcReviewDto> saved = await host.As(Commercial, p => p.GetRequiredService<IPcProjectService>().SaveReviewAsync(FullReview(contractId), CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            int reviewId = saved.Data.Id.Value;

            // a person who signs for the PMO cannot sign for the commercial unit as well; the unit's own role is needed
            ResponseDto<PcReviewDto> wrongRole = await Accept(host, Commercial, reviewId, "pmo");
            Assert.IsFalse(wrongRole.Success);
            Assert.IsTrue(wrongRole.Message.Contains(NotAllowed), wrongRole.Message);

            ResponseDto<PcReviewDto> first = await Accept(host, PmoAndCommercial, reviewId, "pmo");
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(PcReviewDecision.Pending, first.Data.Decision, "one signature is not an acceptance");
            Assert.AreEqual(30, first.Data.PmoAcceptedByUserId);
            ResponseDto<PcReviewDto> sameAgain = await Accept(host, PmoAndCommercial, reviewId, "commercial");
            Assert.IsFalse(sameAgain.Success);
            Assert.IsTrue(sameAgain.Message.Contains(SameUser), "the same person cannot sign both: " + sameAgain.Message);
            Assert.IsFalse((await host.As(Pmo1, p => p.GetRequiredService<IPcProjectService>().ReviewActionAsync(new PcReviewActionDto { Id = reviewId, Action = "decide", Decision = PcReviewDecision.Accept, Note = "ok" }, CancellationToken.None))).Success, "the CEO's decide action does not accept a review for anyone");

            // R1c (M1): the person who saved the review does not sign it, even with the right role
            ResponseDto<PcReviewDto> creatorSigns = await Accept(host, Commercial, reviewId);
            Assert.IsFalse(creatorSigns.Success);
            Assert.IsTrue(creatorSigns.Message.Contains("ثبت‌کننده یا ویرایش‌کننده"), "the creator of the review does not sign it: " + creatorSigns.Message);
            ResponseDto<PcReviewDto> second = await Accept(host, Commercial2, reviewId);
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual(PcReviewDecision.Accept, second.Data.Decision);
            Assert.AreEqual(27, second.Data.CommercialAcceptedByUserId);
            Assert.IsTrue(host.Read(u => PcGates.ContractReviewAcceptedAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult()));
            Assert.IsNotNull(host.Read(u => u.Set<PcContractTerms>().Single(t => t.ContractId == contractId).TermsFrozenAt), "the terms froze the day the review was accepted");

            // the audit trail keeps both signatures with their own user
            List<FnAuditLog> reviewRows = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(PcContractReview) && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.IsTrue(reviewRows.Any(r => r.UserId == 30), "the PMO signature");
            Assert.IsTrue(reviewRows.Any(r => r.UserId == 27), "the commercial signature");
            Assert.IsTrue(host.Read(u => u.Set<FnAuditLog>().Any(l => l.EntityType == nameof(PcContractTerms) && l.Action == "Modified" && l.AfterJson.Contains("TermsFrozenAt"))), "the freeze itself is in the trail");

            // now the project code is allowed
            ResponseDto<PcContractTermsDto> code = await host.As(Planning, p => p.GetRequiredService<IPcProjectService>().GenerateProjectCodeAsync(new GenerateProjectCodeDto { ContractId = contractId, Category = PcCategory.Manufacturing }, CancellationToken.None));
            Assert.IsTrue(code.Success, code.Message);

            // frozen: the value and the delivery date cannot be edited, the penalty terms need a document
            terms = (await host.As(Pmo1, p => p.GetRequiredService<IPcProjectService>().GetTermsAsync(contractId, CancellationToken.None))).Data;
            PcContractTermsDto edit = Clone(terms);
            edit.ValueExVat = 900_000m;
            ResponseDto<PcContractTermsDto> value = await SaveTerms(host, Commercial, edit);
            Assert.IsFalse(value.Success);
            Assert.IsTrue(value.Message.Contains("الحاقیه"), value.Message);
            edit = Clone(terms); edit.ContractDeliveryDate = new DateTime(2026, 11, 1);
            Assert.IsFalse((await SaveTerms(host, Commercial, edit)).Success, "the delivery date is frozen as well");
            edit = Clone(terms); edit.LatePenaltyPercentPerWeek = 1m;
            ResponseDto<PcContractTermsDto> noDocument = await SaveTerms(host, Commercial, edit);
            Assert.IsFalse(noDocument.Success);
            Assert.IsTrue(noDocument.Message.Contains("amendmentId"), noDocument.Message);
            edit = Clone(terms); edit.PenaltyBasis = PcPenaltyBasis.PerWorkOrder;
            Assert.IsFalse((await SaveTerms(host, Commercial, edit)).Success, "the basis of the penalty is a term too");
            edit = Clone(terms); edit.WarrantyMonthsAfterCommissioning = 6;
            Assert.IsFalse((await SaveTerms(host, Commercial, edit)).Success, "... and so are the warranty months");
            edit = Clone(terms); edit.Category = null; edit.ClientReportDayOfMonth = 5;
            Assert.IsTrue((await SaveTerms(host, Commercial, edit)).Success, "a field that is not a commercial term stays editable");

            // the amendment: made by the commercial unit, approved by the CEO
            ResponseDto<PcAmendmentDto> draft = await host.As(Commercial, p => p.GetRequiredService<IPcProjectService>().SaveAmendmentAsync(
                new PcAmendmentDto { ContractId = contractId, Type = PcAmendmentType.Price, Origin = PcAmendmentOrigin.Customer, Description = "more scope", PriceDelta = 100_000m, DaysDelta = 30 }, CancellationToken.None));
            Assert.IsTrue(draft.Success, draft.Message);
            int amendmentId = draft.Data.Id.Value;
            edit = Clone(terms); edit.LatePenaltyPercentPerWeek = 1m; edit.AmendmentId = amendmentId;
            ResponseDto<PcContractTermsDto> notApproved = await SaveTerms(host, Commercial, edit);
            Assert.IsFalse(notApproved.Success, "an amendment the CEO has not approved authorizes nothing");

            Assert.IsFalse((await host.As(Pmo1, p => p.GetRequiredService<IPcProjectService>().AmendmentActionAsync(new PcAmendmentActionDto { Id = amendmentId, Action = "ceo-approve" }, CancellationToken.None))).Success, "only the CEO approves an amendment");
            ResponseDto<PcAmendmentDto> approved = await host.As(Ceo, p => p.GetRequiredService<IPcProjectService>().AmendmentActionAsync(new PcAmendmentActionDto { Id = amendmentId, Action = "ceo-approve" }, CancellationToken.None));
            Assert.IsTrue(approved.Success, approved.Message);

            // the approval re-opens the review: no new baseline until it is accepted again
            string gate = host.Read(u => PcGates.RequireBaselineReviewAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(PcGates.ChangeReviewOpen, gate);

            ResponseDto<PcContractTermsDto> changed = await SaveTerms(host, Commercial, edit);
            Assert.IsTrue(changed.Success, changed.Message);
            Assert.AreEqual(1m, changed.Data.LatePenaltyPercentPerWeek);
            Assert.AreEqual(1_200_000m, changed.Data.ValueExVat, "the value of the terms is what was signed");
            Assert.AreEqual(1_300_000m, changed.Data.CurrentValue, "the current value is the terms plus the approved amendment");
            Assert.AreEqual(new DateTime(2026, 12, 31), changed.Data.CurrentDeliveryDate, "thirty days later");
            edit = Clone(changed.Data); edit.GraceDays = 9; edit.AmendmentId = amendmentId;
            ResponseDto<PcContractTermsDto> reused = await SaveTerms(host, Commercial, edit);
            Assert.IsFalse(reused.Success, "each amendment authorizes one change");

            // the re-review is accepted by two people again, with the items that concern the change
            List<PcContractReview> changeReviews = host.Read(u => u.Set<PcContractReview>().Where(r => r.ContractId == contractId && r.Stage == PcReviewStage.Change).ToList());
            Assert.AreEqual(1, changeReviews.Count);
            Assert.AreEqual(PcReviewDecision.ReviewRequired, changeReviews[0].Decision);
            Assert.AreEqual(amendmentId, changeReviews[0].AmendmentId);
            int changeReviewId = changeReviews[0].Id;
            ResponseDto<PcReviewDto> incomplete = await Accept(host, Pmo1, changeReviewId);
            Assert.IsFalse(incomplete.Success, "the items of the change are not ticked yet");
            PcReviewDto tick = new PcReviewDto { Id = changeReviewId, ContractId = contractId, Kind = PcReviewKind.StandardProduct, RequirementsDefined = true, DeliveryFeasible = true, PenaltiesReviewed = true, PaymentTermsReviewed = true };
            Assert.IsTrue((await host.As(Commercial, p => p.GetRequiredService<IPcProjectService>().SaveReviewAsync(tick, CancellationToken.None))).Success);
            Assert.IsTrue((await Accept(host, Pmo1, changeReviewId)).Success);
            Assert.AreEqual(PcGates.ChangeReviewOpen, host.Read(u => PcGates.RequireBaselineReviewAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult()), "one signature is not enough");
            Assert.IsTrue((await Accept(host, Commercial2, changeReviewId)).Success);
            Assert.IsNull(host.Read(u => PcGates.RequireBaselineReviewAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult()), "accepted again: baselines may be built");
        }

        private static PcContractTermsDto Clone(PcContractTermsDto t) => new PcContractTermsDto
        {
            ContractId = t.ContractId, ProjectCode = t.ProjectCode, Category = t.Category, CurrencyCode = t.CurrencyCode, ValueExVat = t.ValueExVat, VatPercent = t.VatPercent, AdvancePercent = t.AdvancePercent,
            RetentionPercent = t.RetentionPercent, LatePenaltyPercentPerWeek = t.LatePenaltyPercentPerWeek, PenaltyCapPercent = t.PenaltyCapPercent, GraceDays = t.GraceDays,
            ChangeProcedureAgreed = t.ChangeProcedureAgreed, ChangeProcedureDocumentId = t.ChangeProcedureDocumentId, ProjectManagerUserId = t.ProjectManagerUserId, CalendarId = t.CalendarId,
            ClientReportDayOfMonth = t.ClientReportDayOfMonth, DataDateWeekday = t.DataDateWeekday, ScheduleStartDate = t.ScheduleStartDate, ContractDeliveryDate = t.ContractDeliveryDate,
            ClientReportShowsIndices = t.ClientReportShowsIndices, AccrualMode = t.AccrualMode, ClientReviewDays = t.ClientReviewDays, ManagementReserve = t.ManagementReserve, AutoSnapshot = t.AutoSnapshot,
            PenaltyBasis = t.PenaltyBasis, WarrantyMonthsAfterCommissioning = t.WarrantyMonthsAfterCommissioning, WarrantyMonthsAfterDelivery = t.WarrantyMonthsAfterDelivery, RowVersion = t.RowVersion,
        };

        // ------------------------------------------------------------------------------------------------ H3: variation orders

        [TestMethod]
        public async Task AVariationOrderIsCreatedByCommercialApprovedByAnotherPersonOnABasisWithClientEvidenceAndByTheCeoAboveTheThreshold()
        {
            AppHost host = NewHost("Variation");
            int contractId = NewContract(host);
            int amendmentId = 0, changeRequestId = 0;
            Guid document = Guid.Empty;
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contractId, ProjectCode = "P2", ValueExVat = 1_000_000m, ContractDeliveryDate = new DateTime(2026, 12, 1) });
                var amendment = AppHost.Add(u, new PcContractAmendment { ContractId = contractId, Number = "AMD-1", Description = "x", PriceDelta = 50_000m, DaysDelta = 10, CeoApprovedAt = DateTimeOffset.Now, CeoApprovedByUserId = 31 });
                var cr = AppHost.Add(u, new PcChangeRequest { ContractId = contractId, Number = "CR-1", Description = "x", Reason = "x", Status = PcChangeStatus.Decided, Decision = PcChangeDecision.Approve });
                var file = new Entities.Model.Document("files/client-letter.pdf", "application/pdf");
                u.Set<Entities.Model.Document>().Add(file);
                u.SaveChanges();
                amendmentId = amendment.Id; changeRequestId = cr.Id; document = file.Id;
            }, Seeder);

            Task<ResponseDto<PcVariationDto>> Save(ClaimsPrincipal who, PcVariationDto dto) => host.As(who, p => p.GetRequiredService<IPcCommercialService>().SaveVariationAsync(dto, CancellationToken.None));
            Task<ResponseDto<PcVariationDto>> Act(ClaimsPrincipal who, PcVariationActionDto dto) => host.As(who, p => p.GetRequiredService<IPcCommercialService>().VariationActionAsync(dto, CancellationToken.None));

            // only the commercial unit creates
            ResponseDto<PcVariationDto> byPlanning = await Save(Planning, new PcVariationDto { ContractId = contractId, Description = "x", PriceDelta = 10m });
            Assert.IsFalse(byPlanning.Success);
            Assert.IsTrue(byPlanning.Message.Contains(NotAllowed), byPlanning.Message);

            // order 1: carries the approved amendment; created and sent by a person who is also a PMO deputy
            ResponseDto<PcVariationDto> vo1 = await Save(PmoAndCommercial, new PcVariationDto { ContractId = contractId, Description = "carries amendment 1", AmendmentId = amendmentId, PriceDelta = 50_000m, DaysDelta = 10 });
            Assert.IsTrue(vo1.Success, vo1.Message);
            Assert.AreEqual(PcVariationStatus.Draft, vo1.Data.Status);
            Assert.IsFalse((await Act(Pmo1, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-1" })).Success, "a draft is not approved");
            Assert.IsTrue((await Act(PmoAndCommercial, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "submit" })).Success);
            ResponseDto<PcVariationDto> self = await Act(PmoAndCommercial, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-1" });
            Assert.IsFalse(self.Success);
            Assert.IsTrue(self.Message.Contains(SameUser), "whoever created and sent the order does not approve it: " + self.Message);
            Assert.IsFalse((await Act(Commercial, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-1" })).Success, "the commercial unit does not approve");

            ResponseDto<PcVariationDto> noEvidence = await Act(Pmo1, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve" });
            Assert.IsFalse(noEvidence.Success);
            Assert.IsTrue(noEvidence.Message.Contains("clientLetterId") || noEvidence.Message.Contains("clientDocumentId"), noEvidence.Message);
            ResponseDto<PcVariationDto> missingFile = await Act(Pmo1, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve", ClientDocumentId = Guid.NewGuid(), Reference = "L-1" });
            Assert.IsFalse(missingFile.Success, "the file must exist in the document store");

            ResponseDto<PcVariationDto> ok1 = await Act(Pmo1, new PcVariationActionDto { Id = vo1.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-1" });
            Assert.IsTrue(ok1.Success, ok1.Message);
            Assert.AreEqual(PcVariationStatus.Approved, ok1.Data.Status);
            Assert.AreEqual("PMO", ok1.Data.ApprovedByRole);
            Assert.AreEqual(11, ok1.Data.ApprovedByUserId);
            Assert.AreEqual(30, ok1.Data.SubmittedByUserId);
            Assert.AreEqual(document, ok1.Data.ClientDocumentId);
            Assert.AreEqual("L-1", ok1.Data.ClientApprovalReference);

            // the amendment is counted once: through the order that carries it
            PcEffectiveContract afterOne = host.Read(u => PcEffective.EffectiveContractAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(1_050_000m, afterOne.Value, "1,000,000 + 50,000 once");
            Assert.AreEqual(10, afterOne.DaysDelta);

            // order 2: no basis at all, then a decided change request; the price is above five percent, so the PMO deputy may not approve
            ResponseDto<PcVariationDto> vo2 = await Save(Commercial, new PcVariationDto { ContractId = contractId, Description = "no basis", PriceDelta = 300_000m, DaysDelta = 0 });
            Assert.IsTrue(vo2.Success, vo2.Message);
            Assert.IsTrue((await Act(Commercial, new PcVariationActionDto { Id = vo2.Data.Id.Value, Action = "submit" })).Success);
            Assert.IsTrue(vo2.Data.NeedsCeo, "the screen is told the CEO is needed");
            ResponseDto<PcVariationDto> tooBig = await Act(Pmo1, new PcVariationActionDto { Id = vo2.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-2" });
            Assert.IsFalse(tooBig.Success);
            Assert.IsTrue(tooBig.Message.Contains("مدیرعامل"), tooBig.Message);
            ResponseDto<PcVariationDto> noBasis = await Act(Ceo, new PcVariationActionDto { Id = vo2.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-2" });
            Assert.IsFalse(noBasis.Success, "the CEO needs a basis as well");
            Assert.IsTrue(noBasis.Message.Contains("درخواست تغییر") || noBasis.Message.Contains("الحاقیه"), noBasis.Message);

            ResponseDto<PcVariationDto> withBasis = await Save(Commercial, new PcVariationDto { Id = vo2.Data.Id, ContractId = contractId, Description = "with a decided change request", ChangeRequestId = changeRequestId, PriceDelta = 300_000m, DaysDelta = 0 });
            Assert.IsFalse(withBasis.Success, "a submitted order is not edited");
            ResponseDto<PcVariationDto> vo3 = await Save(Commercial, new PcVariationDto { ContractId = contractId, Description = "with a decided change request", ChangeRequestId = changeRequestId, PriceDelta = 300_000m, DaysDelta = 5 });
            Assert.IsTrue(vo3.Success, vo3.Message);
            Assert.IsTrue((await Act(Commercial, new PcVariationActionDto { Id = vo3.Data.Id.Value, Action = "submit" })).Success);
            ResponseDto<PcVariationDto> ok3 = await Act(Ceo, new PcVariationActionDto { Id = vo3.Data.Id.Value, Action = "approve", ClientDocumentId = document, Reference = "L-3" });
            Assert.IsTrue(ok3.Success, ok3.Message);
            Assert.AreEqual("CEO", ok3.Data.ApprovedByRole);

            PcEffectiveContract afterThree = host.Read(u => PcEffective.EffectiveContractAsync(u, contractId, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(1_350_000m, afterThree.Value);
            Assert.AreEqual(15, afterThree.DaysDelta);

            // a rejection needs its reason
            ResponseDto<PcVariationDto> vo4 = await Save(Commercial, new PcVariationDto { ContractId = contractId, Description = "to reject", PriceDelta = 1_000m });
            Assert.IsTrue((await Act(Commercial, new PcVariationActionDto { Id = vo4.Data.Id.Value, Action = "submit" })).Success);
            Assert.IsFalse((await Act(Pmo2, new PcVariationActionDto { Id = vo4.Data.Id.Value, Action = "reject" })).Success);
            ResponseDto<PcVariationDto> rejected = await Act(Pmo2, new PcVariationActionDto { Id = vo4.Data.Id.Value, Action = "reject", Note = "the client did not agree" });
            Assert.IsTrue(rejected.Success, rejected.Message);
            Assert.AreEqual(PcVariationStatus.Rejected, rejected.Data.Status);

            // every step of the orders is in the audit trail with its user
            List<FnAuditLog> rows = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(PcVariationOrder) && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.IsTrue(rows.Any(r => r.UserId == 11 && r.AfterJson.Contains("ApprovedByUserId")), "the PMO approval");
            Assert.IsTrue(rows.Any(r => r.UserId == 31 && r.AfterJson.Contains("ApprovedByRole")), "the CEO approval");
        }

        // ------------------------------------------------------------------------------------------------ H1: the plan, the baseline and the change request, end to end

        private static async Task<ResponseDto<T>> Ok<T>(Task<ResponseDto<T>> call)
        {
            ResponseDto<T> r = await call;
            Assert.IsTrue(r.Success, r.Message);
            return r;
        }

        [TestMethod]
        public async Task TheBaselineFreezesThePlanAndOnlyAnApprovedChangeRequestWithAScenarioBuildsTheNextBaseline()
        {
            AppHost host = NewHost("Baseline");
            int contractId = NewContract(host);
            int a1 = 0, a2 = 0;
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contractId, ProjectCode = "P3", ValueExVat = 3_000_000m, ScheduleStartDate = new DateTime(2026, 3, 7), ContractDeliveryDate = new DateTime(2026, 12, 1), ManagementReserve = 0m });
                var x = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "A1", Title = "design", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 1000m, ResponsibleUnit = "PLANNING", Sort = 1, SourceType = PcSourceType.Manual, ProgressMethod = PcProgressMethod.Manual });
                var y = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "A2", Title = "build", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 2000m, ResponsibleUnit = "ENGINEERING", Sort = 2, SourceType = PcSourceType.Manual, ProgressMethod = PcProgressMethod.Manual });
                u.SaveChanges();
                a1 = x.Id; a2 = y.Id;
                u.Set<PcDependency>().Add(new PcDependency { ContractId = contractId, PredecessorId = a1, SuccessorId = a2, Type = PcDependencyType.FS, LagDays = 0, Origin = PcDependencyOrigin.Manual });
                u.SaveChanges();
            }, Seeder);

            IPcScheduleService Schedule(IServiceProvider p) => p.GetRequiredService<IPcScheduleService>();
            IPcBaselineService Baselines(IServiceProvider p) => p.GetRequiredService<IPcBaselineService>();

            // a baseline needs the accepted contract review
            ResponseDto<PcCpmRunDto> run = await Ok(host.As(Planning, p => Schedule(p).RunCpmAsync(new PcCpmRunRequestDto { ContractId = contractId, Mirror = false }, CancellationToken.None)));
            Assert.AreEqual(2, run.Data.Items.Count);
            ResponseDto<PcBaselineDto> refused = await host.As(Planning, p => Baselines(p).CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contractId, Reason = "first" }, CancellationToken.None));
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("بازنگری"), "no baseline before the review: " + refused.Message);
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractReview { ContractId = contractId, Kind = PcReviewKind.StandardProduct, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, PmoAcceptedByUserId = 11, CommercialAcceptedByUserId = 26, DecidedAt = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);

            // the original baseline: built by planning, signed by the two unit managers, approved by the PMO deputy
            ResponseDto<PcBaselineDto> draft = await Ok(host.As(Planning, p => Baselines(p).CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contractId, Reason = "first" }, CancellationToken.None)));
            int baselineId = draft.Data.Id;
            Assert.AreEqual(PcBaselineStatus.Draft, draft.Data.Status);
            Assert.AreEqual(3000m, draft.Data.Bac);
            Assert.IsTrue((await host.As(Pmo1, p => Baselines(p).BaselineActionAsync(new PcBaselineActionDto { Id = baselineId, Action = "approve" }, CancellationToken.None))).Success == false, "not before the managers signed");
            await Ok(host.As(Planning, p => Baselines(p).BaselineActionAsync(new PcBaselineActionDto { Id = baselineId, Action = "sign" }, CancellationToken.None)));
            ResponseDto<PcBaselineDto> signed = await Ok(host.As(Engineering, p => Baselines(p).BaselineActionAsync(new PcBaselineActionDto { Id = baselineId, Action = "sign" }, CancellationToken.None)));
            Assert.AreEqual(PcBaselineStatus.ManagersSigned, signed.Data.Status);
            ResponseDto<PcBaselineDto> byMaker = await host.As(PmoAndCommercial, p => Baselines(p).CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contractId }, CancellationToken.None));
            Assert.IsFalse(byMaker.Success, "a pending baseline exists");
            ResponseDto<PcBaselineDto> approved = await Ok(host.As(Pmo1, p => Baselines(p).BaselineActionAsync(new PcBaselineActionDto { Id = baselineId, Action = "approve" }, CancellationToken.None)));
            Assert.AreEqual(PcBaselineStatus.Approved, approved.Data.Status);
            Assert.AreEqual(baselineId, host.Read(u => u.Set<PcContractTerms>().Single(t => t.ContractId == contractId).CurrentBaselineId));

            // the planned value is stored with the baseline
            int storedRows = host.Read(u => u.Set<PcBaselineCostPeriod>().Count(r => r.BaselineId == baselineId));
            Assert.IsTrue(storedRows > 0, "the frozen planned-value curve");
            Assert.AreEqual(3000m, host.Read(u => u.Set<PcBaselineCostPeriod>().Where(r => r.BaselineId == baselineId && r.CalendarId == null).Max(r => r.PlannedCumulative)), "it ends at the budget at completion");

            // a baseline is only the first one from this route
            ResponseDto<PcBaselineDto> second = await host.As(Planning, p => Baselines(p).CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contractId, Reason = "again" }, CancellationToken.None));
            Assert.IsFalse(second.Success);
            Assert.IsTrue(second.Message.Contains("درخواست تغییر"), second.Message);

            // the plan is read only
            PcActivityDto live = (await Ok(host.As(Planning, p => Schedule(p).GetActivityAsync(a1, CancellationToken.None)))).Data;
            PcActivityDto longer = Dto(live); longer.DurationDays = 9;
            ResponseDto<PcActivityDto> editDuration = await host.As(Planning, p => Schedule(p).SaveActivityAsync(longer, CancellationToken.None));
            Assert.IsFalse(editDuration.Success);
            Assert.IsTrue(editDuration.Message.Contains("درخواست تغییر"), editDuration.Message);
            PcActivityDto dearer = Dto(live); dearer.BudgetAtCompletion = 5000m;
            Assert.IsFalse((await host.As(Planning, p => Schedule(p).SaveActivityAsync(dearer, CancellationToken.None))).Success, "the budget is the plan");
            PcActivityDto added = Dto(live); added.Id = null; added.Code = "NEW"; added.Title = "new";
            Assert.IsFalse((await host.As(Planning, p => Schedule(p).SaveActivityAsync(added, CancellationToken.None))).Success, "no new activity outside a change request");
            PcActivityDto retitled = Dto(live); retitled.Title = "design (revised title)";
            Assert.IsTrue((await host.As(Planning, p => Schedule(p).SaveActivityAsync(retitled, CancellationToken.None))).Success, "a title is not the plan");
            Assert.IsFalse((await host.As(Planning, p => Schedule(p).DeleteActivityAsync(a2, CancellationToken.None))).Success, "no deletion");
            Assert.IsFalse((await host.As(Planning, p => Schedule(p).SaveDependencyAsync(new PcDependencyDto { ContractId = contractId, PredecessorId = a2, SuccessorId = a1, Type = PcDependencyType.FS }, CancellationToken.None))).Success, "no new link");
            Assert.IsFalse((await host.As(Planning, p => Schedule(p).SaveWbsNodeAsync(new PcWbsNodeDto { ContractId = contractId, Code = "9", Title = "node", NodeType = PcWbsNodeType.Phase }, CancellationToken.None))).Success, "no new WBS node");

            // the figures read the frozen curve: a holiday added afterwards does not move the planned value
            DateTime probe = new DateTime(2026, 3, 11);
            PcEvmDto before = (await Ok(host.As(Pmo1, p => p.GetRequiredService<IPcProgressService>().GetEvmAsync(contractId, probe, CancellationToken.None)))).Data;
            Assert.IsTrue(before.PvFrozen);
            Assert.IsTrue(before.Pv > 0m);
            host.Write(u => { u.Set<Entities.Model.HolidaysByDate>().Add(Entities.Model.HolidaysByDate.From(new DateTimeOffset(new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc)))); u.SaveChanges(); }, Seeder);
            PcEvmDto afterHoliday = (await Ok(host.As(Pmo1, p => p.GetRequiredService<IPcProgressService>().GetEvmAsync(contractId, probe, CancellationToken.None)))).Data;
            Assert.AreEqual(before.Pv, afterHoliday.Pv, "the plan did not move");

            // the change request: a scenario that lengthens A2, adds A3 behind it and adds budget; the plan changes only when it is approved
            var scenario = new List<PcChangeScenarioItemDto>
            {
                new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Modify, ActivityId = a2, DurationDelta = 3, BudgetDelta = 500m },
                new PcChangeScenarioItemDto { Operation = PcScenarioOperation.Add, Code = "A3", Title = "extra test", DurationDays = 2, BudgetAtCompletion = 400m, Weight = 1m, ResponsibleUnit = "QC", Links = new List<PcScenarioLinkDto> { new PcScenarioLinkDto { OtherActivityId = a2, OtherIsPredecessor = true } } },
            };
            var request = new PcChangeDto { ContractId = contractId, Origin = PcChangeOrigin.ClientChangeOrder, Type = PcChangeType.Scope, Description = "client asked for more testing", Reason = "client letter", Scenario = scenario };
            ResponseDto<PcChangeDto> cr = await Ok(host.As(Planning, p => Baselines(p).SaveChangeAsync(request, CancellationToken.None)));
            int crId = cr.Data.Id.Value;
            Assert.AreEqual(2, cr.Data.Scenario.Count);
            await Ok(host.As(Planning, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "register" }, CancellationToken.None)));
            PcChangeDto analysed = (await Ok(host.As(Planning, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "analyze" }, CancellationToken.None)))).Data;
            Assert.AreEqual(900m, analysed.ImpactDeltaCost, "500 more on A2 and 400 for A3");
            Assert.IsTrue(analysed.ImpactDeltaDays > 0, "the end moves out");
            Assert.AreEqual(5, host.Read(u => u.Set<PcActivity>().Single(a => a.Id == a2).DurationDays), "analysis writes nothing to the plan");
            await Ok(host.As(ProjectManager, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "recommend", Note = "accept the extra test" }, CancellationToken.None)));

            // the requester does not decide; the board votes first
            Assert.IsFalse((await host.As(Planning, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "decide", Decision = PcChangeDecision.Approve, Note = "ok" }, CancellationToken.None))).Success);
            PcChangeDto current = (await Ok(host.As(Pmo1, p => Baselines(p).GetChangeAsync(crId, CancellationToken.None)))).Data;
            Assert.IsTrue(current.Board.Count >= 3);
            ResponseDto<PcChangeDto> early = await host.As(Pmo1, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "decide", Decision = PcChangeDecision.Approve, Note = "ok" }, CancellationToken.None));
            Assert.IsFalse(early.Success, "all members must vote first");
            foreach (PcChangeBoardDto member in current.Board)
            {
                ClaimsPrincipal voter = AppHost.Principal(100 + Math.Abs(member.Role.GetHashCode() % 800), "voter." + member.Role.ToLowerInvariant(), member.Role);
                await Ok(host.As(voter, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "vote", Vote = PcVote.Approve, BoardRole = member.Role }, CancellationToken.None)));
            }
            Assert.IsFalse((await host.As(ProjectManager, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "decide", Decision = PcChangeDecision.Approve, Note = "ok" }, CancellationToken.None))).Success, "the person who recommended does not decide");

            ResponseDto<PcChangeDto> decided = await Ok(host.As(Pmo1, p => Baselines(p).ChangeActionAsync(new PcChangeActionDto { Id = crId, Action = "decide", Decision = PcChangeDecision.Approve, Note = "approved" }, CancellationToken.None)));
            Assert.AreEqual(PcChangeStatus.Implementing, decided.Data.Status);
            Assert.IsNotNull(decided.Data.BaselineId, "the approved request produced the next baseline");

            // the next baseline is number 1, belongs to the change request and has the changed plan; the old one is superseded
            List<PcBaseline> all = host.Read(u => u.Set<PcBaseline>().Where(b => b.ContractId == contractId).OrderBy(b => b.Number).ToList());
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(PcBaselineStatus.Superseded, all[0].Status);
            Assert.AreEqual(PcBaselineStatus.Approved, all[1].Status);
            Assert.AreEqual(1, all[1].Number);
            Assert.AreEqual(crId, all[1].ChangeRequestId);
            Assert.AreEqual(3900m, all[1].Bac, "1000 + (2000 + 500) + 400");
            Assert.AreEqual(all[1].Id, host.Read(u => u.Set<PcContractTerms>().Single(t => t.ContractId == contractId).CurrentBaselineId));
            Assert.AreEqual(8, host.Read(u => u.Set<PcActivity>().Single(a => a.Id == a2).DurationDays), "the scenario was applied to the plan");
            Assert.IsTrue(host.Read(u => u.Set<PcActivity>().Any(a => a.ContractId == contractId && a.Code == "A3" && !a.Deleted)));
            List<PcBaselineActivityDto> rows = (await Ok(host.As(Pmo1, p => Baselines(p).GetBaselineActivitiesAsync(all[1].Id, CancellationToken.None)))).Data;
            CollectionAssert.AreEquivalent(new[] { "A1", "A2", "A3" }, rows.Select(r => r.Code).ToArray());
            Assert.IsTrue(rows.All(r => r.EffectiveWeight.HasValue), "the weights are frozen with the baseline");
            Assert.AreEqual(1m, rows.Sum(r => r.EffectiveWeight.Value), 0.0001m);
            Assert.IsTrue(host.Read(u => u.Set<PcBaselineCostPeriod>().Any(r => r.BaselineId == all[1].Id)), "the new baseline froze its own planned value");
            Assert.AreEqual(3000m, host.Read(u => u.Set<PcBaselineCostPeriod>().Where(r => r.BaselineId == all[0].Id && r.CalendarId == null).Max(r => r.PlannedCumulative)), "the old curve is untouched");
        }

        private static PcActivityDto Dto(PcActivityDto s) => new PcActivityDto
        {
            Id = s.Id, ContractId = s.ContractId, WbsNodeId = s.WbsNodeId, Code = s.Code, Title = s.Title, SourceType = s.SourceType, SourceId = s.SourceId, WorkOrderId = s.WorkOrderId,
            DurationDays = s.DurationDays, CalendarId = s.CalendarId, Phase = s.Phase, ResponsibleUnit = s.ResponsibleUnit, ConstraintType = s.ConstraintType, ConstraintDate = s.ConstraintDate,
            ProgressMethod = s.ProgressMethod, Weight = s.Weight, BudgetAtCompletion = s.BudgetAtCompletion, PurchaseOrderLineId = s.PurchaseOrderLineId, PurchaseRequestLineId = s.PurchaseRequestLineId,
            ItpActivityId = s.ItpActivityId, MilestoneEvent = s.MilestoneEvent, PlannedQuantity = s.PlannedQuantity, SerialItemId = s.SerialItemId, LeadTimeDays = s.LeadTimeDays, IsPackaging = s.IsPackaging,
        };

        // ------------------------------------------------------------------------------------------------ H4: dates

        [TestMethod]
        public async Task ABackDatedSnapshotNeedsAPlanningRequestAndTheApprovalOfAnotherPmoDeputyAndAFutureActualStartIsRefused()
        {
            AppHost host = NewHost("Snapshot");
            int contractId = NewContract(host);
            int activityId = 0;
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contractId, ProjectCode = "P4", ValueExVat = 1000m, DataDateWeekday = 3 });
                var a = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "S1", Title = "s", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 100m, ResponsibleUnit = "PLANNING", EarlyStart = Day(-30), EarlyFinish = Day(-20), ProgressMethod = PcProgressMethod.Manual });
                u.SaveChanges();
                activityId = a.Id;
            }, Seeder);
            IPcProgressService Progress(IServiceProvider p) => p.GetRequiredService<IPcProgressService>();

            DateTime regular = Day(0).AddDays(-((((int)Day(0).DayOfWeek) - 3 + 7) % 7));
            DateTime past = regular.AddDays(-7);

            ResponseDto<PcSnapshotDto> direct = await host.As(Planning, p => Progress(p).TakeSnapshotAsync(new PcSnapshotRequestDto { ContractId = contractId, DataDate = past }, CancellationToken.None));
            Assert.IsFalse(direct.Success, "a data date before the current weekly one needs an approved request");
            ResponseDto<PcSnapshotDto> future = await host.As(Planning, p => Progress(p).TakeSnapshotAsync(new PcSnapshotRequestDto { ContractId = contractId, DataDate = Day(3) }, CancellationToken.None));
            Assert.IsFalse(future.Success, "a data date in the future does not exist yet");

            ResponseDto<PcPastSnapshotDto> noReason = await host.As(Planning, p => Progress(p).RequestPastSnapshotAsync(new PcPastSnapshotRequestDto { ContractId = contractId, DataDate = past, Reason = " " }, CancellationToken.None));
            Assert.IsFalse(noReason.Success, "the request carries the reason");
            Assert.IsFalse((await host.As(Pmo1, p => Progress(p).RequestPastSnapshotAsync(new PcPastSnapshotRequestDto { ContractId = contractId, DataDate = regular, Reason = "x" }, CancellationToken.None))).Success, "the current weekly date is not in the past: no request needed");
            ResponseDto<PcPastSnapshotDto> requested = await host.As(Planning, p => Progress(p).RequestPastSnapshotAsync(new PcPastSnapshotRequestDto { ContractId = contractId, DataDate = past, Reason = "the week was reported late" }, CancellationToken.None));
            Assert.IsTrue(requested.Success, requested.Message);
            Assert.AreEqual(PcSnapshotRequestState.Requested, requested.Data.State);
            Assert.IsFalse((await host.As(Planning, p => Progress(p).RequestPastSnapshotAsync(new PcPastSnapshotRequestDto { ContractId = contractId, DataDate = past, Reason = "again" }, CancellationToken.None))).Success, "one open request per date");

            Assert.IsFalse((await host.As(Planning, p => Progress(p).TakeSnapshotAsync(new PcSnapshotRequestDto { ContractId = contractId, DataDate = past }, CancellationToken.None))).Success, "requested is not approved");
            Assert.IsFalse((await host.As(Planning, p => Progress(p).DecidePastSnapshotAsync(new PcPastSnapshotDecisionDto { Id = requested.Data.Id, Approve = true }, CancellationToken.None))).Success, "planning does not approve its own request");
            ResponseDto<PcPastSnapshotDto> approved = await host.As(Pmo1, p => Progress(p).DecidePastSnapshotAsync(new PcPastSnapshotDecisionDto { Id = requested.Data.Id, Approve = true }, CancellationToken.None));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PcSnapshotRequestState.Approved, approved.Data.State);

            ResponseDto<PcSnapshotDto> taken = await host.As(Planning, p => Progress(p).TakeSnapshotAsync(new PcSnapshotRequestDto { ContractId = contractId, DataDate = past }, CancellationToken.None));
            Assert.IsTrue(taken.Success, taken.Message);
            Assert.AreEqual(past, taken.Data.DataDate);
            PcSnapshotRequest used = host.Read(u => u.Set<PcSnapshotRequest>().Single(r => r.Id == requested.Data.Id));
            Assert.AreEqual(PcSnapshotRequestState.Used, used.State, "the approval is used once");
            Assert.AreEqual(taken.Data.Id, used.SnapshotId);
            Assert.IsFalse((await host.As(Planning, p => Progress(p).TakeSnapshotAsync(new PcSnapshotRequestDto { ContractId = contractId, DataDate = past }, CancellationToken.None))).Success, "a second snapshot of the same date");
            // the chain only grows: a date before the last snapshot is refused even as a request
            Assert.IsFalse((await host.As(Planning, p => Progress(p).RequestPastSnapshotAsync(new PcPastSnapshotRequestDto { ContractId = contractId, DataDate = past.AddDays(-7), Reason = "x" }, CancellationToken.None))).Success);

            // the trail names the planner, the deputy and the use of the approval
            List<FnAuditLog> trail = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(PcSnapshotRequest)).OrderBy(l => l.Id).ToList());
            Assert.IsTrue(trail.Any(l => l.Action == "Added" && l.UserId == 24));
            Assert.IsTrue(trail.Any(l => l.Action == "Modified" && l.UserId == 11 && l.AfterJson.Contains("Approved")));

            // an actual start is a fact: not in the future
            ResponseDto<PcActivityDto> tomorrow = await host.As(Planning, p => p.GetRequiredService<IPcScheduleService>().ActivityActionAsync(new PcActivityActionDto { Id = activityId, Action = "start", Date = Day(1) }, CancellationToken.None));
            Assert.IsFalse(tomorrow.Success);
            ResponseDto<PcActivityDto> today = await host.As(Planning, p => p.GetRequiredService<IPcScheduleService>().ActivityActionAsync(new PcActivityActionDto { Id = activityId, Action = "start", Date = Day(0) }, CancellationToken.None));
            Assert.IsTrue(today.Success, today.Message);

            // a cost is a fact too
            ResponseDto<PcCostEntryDto> futureCost = await host.As(Accountant, p => Progress(p).AddCostAsync(new PcCostEntrySaveDto { ContractId = contractId, Kind = CostKind.Material, Amount = 10m, Description = "x", EntryDate = Day(2) }, CancellationToken.None));
            Assert.IsFalse(futureCost.Success);
        }

        // ------------------------------------------------------------------------------------------------ M5: the actual cost

        [TestMethod]
        public async Task AManualCostCountsAsActualCostOnlyAfterAnotherFinancePersonChecked()
        {
            AppHost host = NewHost("Cost");
            int contractId = NewContract(host);
            IPcProgressService Progress(IServiceProvider p) => p.GetRequiredService<IPcProgressService>();
            host.Write(u =>
            {
                AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "C1", Title = "c", DurationDays = 5, Weight = 1m, BudgetAtCompletion = 1000m, EarlyStart = Day(-10), EarlyFinish = Day(-5), ProgressMethod = PcProgressMethod.Manual });
                // a cost the modules booked (counts), an after-sales order cost and a monthly labour cost (do not)
                u.Set<CostEntry>().Add(new CostEntry { EntryDate = Day(-6), Kind = CostKind.Material, ContractId = contractId, Amount = 200m, SourceType = "PurchaseReceipt", SourceId = 1, SourceNumber = "R-1", Description = "material", CreatedAt = DateTimeOffset.Now, CreatedByUserId = 0 });
                u.Set<CostEntry>().Add(new CostEntry { EntryDate = Day(-6), Kind = CostKind.Material, ContractId = contractId, Amount = 7000m, SourceType = Services.NasbNiro.AfterSales.SvCostSource.Order, SourceId = 2, SourceNumber = "SV-1", Description = "after sales", CreatedAt = DateTimeOffset.Now, CreatedByUserId = 0 });
                u.SaveChanges();
            }, Seeder);

            Task<PcEvmDto> Evm() => host.As(Pmo1, p => Progress(p).ComputeEvmAsync(contractId, Day(0), CancellationToken.None));
            Assert.AreEqual(200m, (await Evm()).Ac, "the after-sales cost is not project cost");

            ResponseDto<PcCostEntryDto> manual = await host.As(Accountant, p => Progress(p).AddCostAsync(new PcCostEntrySaveDto { ContractId = contractId, Kind = CostKind.Labour, Amount = 50m, Description = "hand entry", EntryDate = Day(-1) }, CancellationToken.None));
            Assert.IsTrue(manual.Success, manual.Message);
            Assert.IsTrue(manual.Data.NeedsCheck);
            Assert.IsFalse(manual.Data.Checked);
            Assert.AreEqual(200m, (await Evm()).Ac, "an unchecked manual entry does not count");

            ResponseDto<PcCostEntryDto> self = await host.As(Accountant, p => Progress(p).CheckCostAsync(new PcCostCheckDto { CostEntryId = manual.Data.Id }, CancellationToken.None));
            Assert.IsFalse(self.Success);
            Assert.IsTrue(self.Message.Contains(SameUser), "the person who entered it does not check it: " + self.Message);
            Assert.IsFalse((await host.As(Pmo1, p => Progress(p).CheckCostAsync(new PcCostCheckDto { CostEntryId = manual.Data.Id }, CancellationToken.None))).Success, "the check is for finance staff");
            ResponseDto<PcCostEntryDto> checkedBy = await host.As(Accountant2, p => Progress(p).CheckCostAsync(new PcCostCheckDto { CostEntryId = manual.Data.Id, Note = "invoice seen" }, CancellationToken.None));
            Assert.IsTrue(checkedBy.Success, checkedBy.Message);
            Assert.IsTrue(checkedBy.Data.Checked);
            Assert.AreEqual(29, checkedBy.Data.CheckedByUserId);
            Assert.AreEqual(250m, (await Evm()).Ac, "checked: the entry counts");
            Assert.IsFalse((await host.As(Accountant2, p => Progress(p).CheckCostAsync(new PcCostCheckDto { CostEntryId = manual.Data.Id }, CancellationToken.None))).Success, "checked once");
        }

        // ------------------------------------------------------------------------------------------------ H1, H5: the one-time upgrade of an install that already holds project-control data

        [TestMethod]
        public async Task TheUpgradeMarksOldRowsOnceAndLeavesLaterOnesToTheNewRules()
        {
            AppHost host = NewHost("Upgrade");
            int contractId = NewContract(host);
            int tenderReviewId = 0, manualCostId = 0, milestoneA = 0, milestoneB = 0, termsId = 0;
            host.Write(u =>
            {
                // rows as an install of before R1c holds them: the column default made every review a contract review
                var tender = AppHost.Add(u, new PcContractReview { ContractId = null, TenderCode = "T-1", Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Pending, CreateDate = DateTimeOffset.Now, CreatorUserId = 5 });
                var accepted = AppHost.Add(u, new PcContractReview { ContractId = contractId, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, DecidedAt = DateTimeOffset.Now.AddDays(-3), CreateDate = DateTimeOffset.Now.AddDays(-4), CreatorUserId = 5 });
                var terms = AppHost.Add(u, new PcContractTerms { ContractId = contractId, CurrencyCode = "IRR", ValueExVat = 1000m });
                PcClientResponseCode d = u.Set<PcClientResponseCode>().FirstOrDefault(c => c.Code == "D");
                if (d == null) u.Set<PcClientResponseCode>().Add(new PcClientResponseCode { Code = "D", Title = "rejected", CountsAsApproved = false, ResetsProgress = false });
                else { d.CountsAsApproved = false; d.ResetsProgress = false; }
                var cost = AppHost.Add(u, new CostEntry { EntryDate = Day(-2), Kind = CostKind.Labour, ContractId = contractId, Amount = 10m, SourceType = PcProgressService.ManualCostSource, SourceId = 1, SourceNumber = "M-1" });
                var m1 = AppHost.Add(u, new PcPaymentMilestone { ContractId = contractId, Seq = 1, Title = "a", Kind = PcMilestoneKind.Advance, Trigger = PcMilestoneTrigger.Date, Amount = 1m });
                var m2 = AppHost.Add(u, new PcPaymentMilestone { ContractId = contractId, Seq = 2, Title = "b", Kind = PcMilestoneKind.Progress, Trigger = PcMilestoneTrigger.Date, Amount = 1m });
                u.SaveChanges();
                tenderReviewId = tender.Id; manualCostId = cost.Id; milestoneA = m1.Id; milestoneB = m2.Id; termsId = terms.Id;
            }, Seeder);

            bool first = false, second = true;
            host.Write(u => { first = PcR1Upgrade.EnsureAsync(u, CancellationToken.None).GetAwaiter().GetResult(); }, Seeder);
            host.Write(u => { second = PcR1Upgrade.EnsureAsync(u, CancellationToken.None).GetAwaiter().GetResult(); }, Seeder);
            Assert.IsTrue(first, "the first run does the work");
            Assert.IsFalse(second, "the marker makes it run once");

            host.Write(u =>
            {
                Assert.AreEqual(PcReviewStage.Tender, u.Set<PcContractReview>().Single(r => r.Id == tenderReviewId).Stage, "a review without a contract is a tender review");
                Assert.IsNotNull(u.Set<PcContractTerms>().Single(t => t.Id == termsId).TermsFrozenAt, "the terms of a contract whose review was accepted are frozen");
                Assert.IsTrue(u.Set<PcClientResponseCode>().Single(c => c.Code == "D").ResetsProgress, "a rejected document earns no progress");
                PcCostCheck check = u.Set<PcCostCheck>().Single(c => c.CostEntryId == manualCostId);
                Assert.AreEqual(0, check.CheckedByUserId, "the system is the checker of the old manual entries");
                Assert.IsTrue(u.Set<PcPaymentMilestone>().Single(m => m.Id == milestoneB).IsFinal, "the highest sequence was the final statement");
                Assert.IsFalse(u.Set<PcPaymentMilestone>().Single(m => m.Id == milestoneA).IsFinal);
                // the settings screen lists only the default keys, so the marker never shows
                Assert.IsFalse(PcSettingKeys.Defaults().Any(d => d.Key == PcR1Upgrade.MarkerKey));
            }, Seeder);

            // an entry made after the upgrade needs the second person
            ResponseDto<PcCostEntryDto> later = await host.As(Accountant, p => p.GetRequiredService<IPcProgressService>().AddCostAsync(new PcCostEntrySaveDto { ContractId = contractId, Kind = CostKind.Labour, Amount = 5m, Description = "later", EntryDate = Day(-1) }, CancellationToken.None));
            Assert.IsTrue(later.Success, later.Message);
            Assert.IsTrue(later.Data.NeedsCheck);
            Assert.IsFalse(later.Data.Checked);
        }
    }
}
