using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;

namespace Services.NasbNiro.Production
{
    // Pure rules of release R1p (production audit findings D1, D6-D8, D29, D36-D39, D43). They hold no state and touch no database, so the unit tests can run them with plain objects.

    /// <summary>
    /// D7: the quantity an operation has to report. It is the good quantity of the operation before it (the first operation: the order quantity less what an approved NCR scrapped),
    /// not the full order quantity again after earlier scrap. A rework operation does the rejected quantity of the operation it reworks.
    /// </summary>
    internal static class MfQuantityRules
    {
        /// <summary>The order quantity less the pieces an approved scrap decision wrote off (D6).</summary>
        public static decimal EffectiveOrderQuantity(decimal orderQuantity, decimal writtenOff) => Math.Max(0, orderQuantity - writtenOff);

        private static bool Same(MfOperation a, MfOperation b) => ReferenceEquals(a, b) || (a.Id != 0 && a.Id == b.Id);

        /// <param name="operations">All operations of the order (any order of the list; the chain order is applied here).</param>
        /// <param name="op">The operation asked about.</param>
        /// <param name="orderQuantity">
        /// The quantity the order was drafted for (<c>MfProductionOrder.Quantity</c>, NOT reduced by a write-off): the first operation always had the whole quantity in front of it, and the operation that scrapped
        /// pieces keeps good + scrap equal to that quantity; the operations after it see the reduced good quantity through the chain.
        /// </param>
        public static decimal Expected(IEnumerable<MfOperation> operations, MfOperation op, decimal orderQuantity)
        {
            List<MfOperation> chain = MfFlow.Chain(operations);
            int index = chain.FindIndex(o => Same(o, op));
            return index < 0 ? orderQuantity : ExpectedAt(chain, index, orderQuantity, 0);
        }

        private static decimal ExpectedAt(List<MfOperation> chain, int index, decimal orderQuantity, int depth)
        {
            MfOperation op = chain[index];
            if (op.IsRework)
            {
                int originIndex = chain.FindIndex(o => o.Id != 0 && o.Id == op.ReworkOfOperationId);
                if (originIndex < 0) return orderQuantity;
                MfOperation origin = chain[originIndex];
                if (origin.ReworkQty > 0) return origin.ReworkQty;
                return depth > 8 ? orderQuantity : ExpectedAt(chain, originIndex, orderQuantity, depth + 1);
            }
            // the nearest operation before this one that was not skipped (a skipped step passes the pieces on untouched)
            for (int i = index - 1; i >= 0; i--)
            {
                if (chain[i].Status == MfOperationStatus.Skipped) continue;
                return chain[i].GoodQty;
            }
            return orderQuantity;
        }
    }

    /// <summary>
    /// D29: OEE after ISO 22400-2. Planned busy time = planned time less planned maintenance. Availability = run time / planned busy time,
    /// performance = ideal cycle time x produced count / run time, quality = good / produced, OEE = availability x performance x quality.
    /// The loss categories inside the planned busy time are breakdown, setup, waiting, other booked time (rework, cleaning) and idle (nothing booked, nothing broken).
    /// </summary>
    internal static class MfOeeMath
    {
        public sealed class Input
        {
            public decimal PlannedHours;
            public decimal PlannedMaintenanceHours;
            public decimal BreakdownHours;
            public decimal RunHours;
            public decimal SetupHours;
            public decimal WaitingHours;
            public decimal OtherBookedHours;

            /// <summary>Ideal cycle time x produced count (good + scrap), in hours.</summary>
            public decimal StandardHours;

            public decimal GoodQty;
            public decimal ScrapQty;
        }

        public sealed class Result
        {
            public decimal PlannedBusyHours;
            public decimal IdleHours;
            public decimal AvailabilityPercent;
            public decimal? PerformancePercent;
            public decimal? QualityPercent;
            public decimal? OeePercent;
        }

        public static decimal Pct(decimal part, decimal whole) => whole <= 0 ? 0 : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);

        public static Result Compute(Input i)
        {
            var r = new Result { PlannedBusyHours = Math.Max(0, i.PlannedHours - i.PlannedMaintenanceHours) };
            decimal produced = i.GoodQty + i.ScrapQty;
            r.AvailabilityPercent = Pct(i.RunHours, r.PlannedBusyHours);
            r.PerformancePercent = i.RunHours > 0 ? Pct(i.StandardHours, i.RunHours) : (decimal?)null;
            r.QualityPercent = produced > 0 ? Pct(i.GoodQty, produced) : (decimal?)null;
            // A x P x Q written as one division, so the rounded parts never drift: (run / busy) x (standard / run) x (good / produced) = standard x good / (produced x busy)
            r.OeePercent = i.RunHours > 0 && produced > 0 && r.PlannedBusyHours > 0
                ? Math.Round(i.StandardHours * i.GoodQty / (produced * r.PlannedBusyHours) * 100m, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
            r.IdleHours = Math.Max(0, r.PlannedBusyHours - i.BreakdownHours - i.RunHours - i.SetupHours - i.WaitingHours - i.OtherBookedHours);
            return r;
        }

        /// <summary>The figure the dashboards showed before ISO 22400-2: availability from downtime only, so a centre that ran 40 of 176 hours still had 100 % availability. Kept one release as <c>OeeLegacy</c>.</summary>
        public static decimal? Legacy(decimal plannedHours, decimal downtimeHours, decimal runHours, decimal standardHours, decimal goodQty, decimal scrapQty)
        {
            decimal availability = Pct(Math.Max(0, plannedHours - downtimeHours), plannedHours);
            decimal? performance = runHours > 0 ? Pct(standardHours, runHours) : (decimal?)null;
            decimal? quality = goodQty + scrapQty > 0 ? Pct(goodQty, goodQty + scrapQty) : (decimal?)null;
            return performance.HasValue && quality.HasValue
                ? Math.Round(availability / 100m * performance.Value / 100m * quality.Value / 100m * 100m, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
        }

        /// <summary>
        /// D37: MTTR on clock hours. The mean of (repair end - repair start) of the breakdowns that started in the period and were repaired by its end; a repair is not cut to the shift hours
        /// (a machine that broke at 16:00 and was repaired at 08:00 next morning was down 16 hours, not zero).
        /// </summary>
        public static decimal? Mttr(IEnumerable<(DateTime Start, DateTime? End)> breakdowns, DateTime periodStart, DateTime periodEnd)
        {
            List<double> hours = breakdowns.Where(b => b.End.HasValue && b.Start.Date >= periodStart.Date && b.End.Value <= periodEnd && b.End.Value >= b.Start)
                .Select(b => (b.End.Value - b.Start).TotalHours).ToList();
            return hours.Count == 0 ? (decimal?)null : Math.Round((decimal)hours.Sum() / hours.Count, 2, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>R1p D29: time intervals of one machine, joined (two people working the same machine at the same time use the machine once).</summary>
    internal static class MfIntervals
    {
        public static List<(DateTime Start, DateTime End)> Union(IEnumerable<(DateTime Start, DateTime End)> items)
        {
            var sorted = items.Where(i => i.End > i.Start).OrderBy(i => i.Start).ThenBy(i => i.End).ToList();
            var result = new List<(DateTime Start, DateTime End)>();
            foreach (var item in sorted)
            {
                if (result.Count > 0 && item.Start <= result[result.Count - 1].End)
                {
                    var last = result[result.Count - 1];
                    if (item.End > last.End) result[result.Count - 1] = (last.Start, item.End);
                }
                else result.Add(item);
            }
            return result;
        }

        /// <summary>What is left of <paramref name="from"/> when <paramref name="taken"/> (both joined already) is cut out.</summary>
        public static List<(DateTime Start, DateTime End)> Subtract(List<(DateTime Start, DateTime End)> from, List<(DateTime Start, DateTime End)> taken)
        {
            var result = new List<(DateTime Start, DateTime End)>();
            foreach (var interval in from)
            {
                DateTime cursor = interval.Start;
                foreach (var cut in taken.Where(t => t.End > interval.Start && t.Start < interval.End).OrderBy(t => t.Start))
                {
                    if (cut.Start > cursor) result.Add((cursor, cut.Start));
                    if (cut.End > cursor) cursor = cut.End;
                }
                if (cursor < interval.End) result.Add((cursor, interval.End));
            }
            return result;
        }

        public static decimal Hours(IEnumerable<(DateTime Start, DateTime End)> items) => (decimal)items.Sum(i => (i.End - i.Start).TotalHours);
    }

    /// <summary>
    /// R1p D29: the time a work centre spent running, setting up, waiting or on other booked work, counted as MACHINE time. The bookings of one machine are joined per kind, and a moment of the machine counts for
    /// one kind only (run before setup before other work before waiting), so two welders on one machine do not book it twice and the loss categories add up to the machine's busy time.
    /// Bookings without a machine at a manual work centre (one that does not require a machine) count their own hours: every person is a resource of his own; at a centre that requires a machine they count nothing.
    /// </summary>
    internal static class MfMachineTime
    {
        public sealed class Row
        {
            public int? MachineId;
            public MfBookingKind Kind;
            public DateTime Start;
            public DateTime End;
            public decimal Hours;

            /// <summary>
            /// R1p D43 (round 3): the work centre of the booking has machines to choose from and requires one. A booking of such a centre that names no machine (an old row) is NOT machine time: it is left out, never counted
            /// at its person-hours.
            /// </summary>
            public bool RequiresMachine;
        }

        public sealed class Result
        {
            public decimal Run;
            public decimal Setup;
            public decimal Waiting;
            public decimal Other;
        }

        public static Result Of(IEnumerable<Row> rows)
        {
            var result = new Result();
            List<Row> list = rows.ToList();
            foreach (var machine in list.Where(r => r.MachineId.HasValue).GroupBy(r => r.MachineId.Value))
            {
                List<(DateTime Start, DateTime End)> Kind(Func<MfBookingKind, bool> pick) => MfIntervals.Union(machine.Where(r => pick(r.Kind)).Select(r => (r.Start, r.End)));
                var run = Kind(k => k == MfBookingKind.Run);
                var setup = MfIntervals.Subtract(Kind(k => k == MfBookingKind.Setup), run);
                var other = MfIntervals.Subtract(Kind(k => k != MfBookingKind.Run && k != MfBookingKind.Setup && k != MfBookingKind.Waiting), MfIntervals.Union(run.Concat(setup)));
                var waiting = MfIntervals.Subtract(Kind(k => k == MfBookingKind.Waiting), MfIntervals.Union(run.Concat(setup).Concat(other)));
                result.Run += MfIntervals.Hours(run);
                result.Setup += MfIntervals.Hours(setup);
                result.Other += MfIntervals.Hours(other);
                result.Waiting += MfIntervals.Hours(waiting);
            }
            foreach (Row r in list.Where(r => !r.MachineId.HasValue && !r.RequiresMachine))
            {
                if (r.Kind == MfBookingKind.Run) result.Run += r.Hours;
                else if (r.Kind == MfBookingKind.Setup) result.Setup += r.Hours;
                else if (r.Kind == MfBookingKind.Waiting) result.Waiting += r.Hours;
                else result.Other += r.Hours;
            }
            return result;
        }
    }

    /// <summary>
    /// One booking (production) or one closed order (service) in the labour pool of a month: the plain values the publication gate and the variance need.
    /// Only payroll-rated people are in it: a person without an approved payslip is costed at the standard rate and has no variance.
    /// </summary>
    internal sealed class MfPoolItem
    {
        public int Id;

        /// <summary>The payroll-rated people behind the item and the hours each of them worked on it (one person for a booking, the engineers for an order).</summary>
        public Dictionary<int, decimal> PersonHours = new Dictionary<int, decimal>();

        public decimal Hours => PersonHours.Values.Sum();

        /// <summary>Hours x the own hourly payroll cost of the people, unrounded.</summary>
        public decimal Cost;

        /// <summary>What the days or the close of the order absorbed for the item at the STANDARD rate, whole rial.</summary>
        public decimal Absorbed;

        public static MfPoolItem Of(int id, int employeeId, decimal hours, decimal cost, decimal absorbed) =>
            new MfPoolItem { Id = id, PersonHours = new Dictionary<int, decimal> { { employeeId, hours } }, Cost = cost, Absorbed = absorbed };
    }

    /// <summary>What the publication gate found in the payroll-rated hours of a month.</summary>
    internal sealed class MfGateResult
    {
        /// <summary>Distinct payroll-rated people with hours.</summary>
        public int People;

        public decimal Hours;

        /// <summary>The largest share of one person in the hours of the pool (0 to 1).</summary>
        public decimal LargestShare;

        /// <summary>The share of the two biggest people together.</summary>
        public decimal TopTwoShare;

        public bool EnoughPeople;
        public bool Dominated;

        public bool Passed => EnoughPeople && !Dominated && Hours > 0;
    }

    /// <summary>The month's variance of one module (production or service): the pool of the month, or the carry, or the flagged quarter-end line.</summary>
    internal sealed class MfPoolResult
    {
        /// <summary>Always <see cref="MfLabourPooling.CompanyKey"/>: one pool per module and month.</summary>
        public string Key;

        public int People;
        public decimal Hours;
        public decimal Cost;
        public decimal Absorbed;

        /// <summary>Cost less absorbed (whole rial): the one company-level line.</summary>
        public decimal Variance;

        /// <summary>Cost / hours, four decimals: the actual average rate, shown to the payroll readers only. Null for a protected pool.</summary>
        public decimal? Rate;

        /// <summary>Posted only because the quarter ended: the gate (people, dominance) was not passed.</summary>
        public bool Protected;

        public List<int> ItemIds = new List<int>();
    }

    internal sealed class MfPoolPlan
    {
        /// <summary>The pool to post; null when the month is carried or empty.</summary>
        public MfPoolResult Pool;

        /// <summary>What the gate found (null for an empty month).</summary>
        public MfGateResult Gate;

        /// <summary>The items that wait for the next month (the gate was not passed and the quarter does not end).</summary>
        public List<int> CarriedItemIds = new List<int>();
    }

    /// <summary>
    /// ★ R1p D1/D2 (round 3): the settings of the publication gate of the monthly labour variance. The month's variance (actual payroll cost of the payroll-rated people with hours less what was absorbed at the standard
    /// rate) is published as an ordinary line only when at least <see cref="MinPeople"/> payroll-rated people have hours in the pool AND nobody dominates it (the largest person has at most <see cref="MaxShare"/> of the hours
    /// and the two largest together at most <see cref="MaxTopTwoShare"/>). Otherwise the month is carried and, at the end of a Persian quarter, posted as one line flagged as protected.
    /// </summary>
    internal sealed class MfLabourGateSettings
    {
        public const string MinPeopleKey = "Production:PoolMinPeople";
        public const string MaxShareKey = "Production:PoolMaxShare";
        public const string MaxTopTwoShareKey = "Production:PoolMaxTopTwoShare";

        public const int DefaultMinPeople = 5;

        /// <summary>The smallest group the earlier releases accepted; a setting below it is raised to it.</summary>
        public const int FloorMinPeople = 3;

        public const decimal DefaultMaxShare = 0.50m;
        public const decimal DefaultMaxTopTwoShare = 0.80m;

        public int MinPeople = DefaultMinPeople;
        public decimal MaxShare = DefaultMaxShare;
        public decimal MaxTopTwoShare = DefaultMaxTopTwoShare;

        private static decimal Share(string text, decimal fallback) =>
            decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal v) && v > 0m && v <= 1m ? v : fallback;

        /// <summary>The settings in the configuration; an unset or unusable value is the default.</summary>
        public static MfLabourGateSettings Read(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            var settings = new MfLabourGateSettings();
            if (configuration == null) return settings;
            if (int.TryParse(configuration[MinPeopleKey], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int people) && people > 0)
                settings.MinPeople = Math.Max(people, FloorMinPeople);
            settings.MaxShare = Share(configuration[MaxShareKey], DefaultMaxShare);
            settings.MaxTopTwoShare = Share(configuration[MaxTopTwoShareKey], DefaultMaxTopTwoShare);
            return settings;
        }
    }

    /// <summary>
    /// R1p D1 and D2 (round 3): the monthly labour variance of a module. Labour is ABSORBED at a standard rate that finance sets (<see cref="MfLabourRates.Standard"/>), so nothing posted per work order, WBS node, work centre,
    /// day or service order is anybody's pay. What is left is the VARIANCE of the month: the actual payroll cost of the payroll-rated people who have hours in the month, less what was absorbed for those hours at the standard
    /// rate. It is posted once per module and month at company level, and only when it cannot be worked back to a person: see <see cref="MfLabourGateSettings"/> (at least five payroll-rated people, nobody over half of
    /// the hours, the two largest together over four fifths). A month that fails the gate is carried to the next open month and joins its pool; at the end of a Persian quarter (Khordad, Shahrivar, Azar, Esfand) the
    /// accumulated variance is posted anyway as one company line flagged as protected, which only PAYROLL, FINANCEMANAGER and CEO read.
    /// </summary>
    internal static class MfLabourPooling
    {
        /// <summary>The pool key of every module's pool (a module has one pool a month).</summary>
        public const string CompanyKey = "*";

        /// <summary>Khordad, Shahrivar, Azar and Esfand end the Persian quarters.</summary>
        public static bool IsQuarterEnd(int period) => period % 100 == 3 || period % 100 == 6 || period % 100 == 9 || period % 100 == 12;

        /// <summary>The distinct payroll-rated people with hours behind the items.</summary>
        public static int People(IEnumerable<MfPoolItem> items) => HoursOfPeople(items).Count(kv => kv.Value > 0);

        private static Dictionary<int, decimal> HoursOfPeople(IEnumerable<MfPoolItem> items)
        {
            var hours = new Dictionary<int, decimal>();
            foreach (MfPoolItem item in items)
                foreach (var person in item.PersonHours)
                    hours[person.Key] = hours.GetValueOrDefault(person.Key) + person.Value;
            return hours;
        }

        /// <summary>The actual average rate: pool cost / pool hours, four decimals (null without hours). Payroll readers only.</summary>
        public static decimal? Rate(decimal cost, decimal hours) => hours <= 0 ? (decimal?)null : Math.Round(cost / hours, 4, MidpointRounding.AwayFromZero);

        /// <summary>The publication gate: enough people, and nobody dominates the hours.</summary>
        public static MfGateResult Gate(IEnumerable<MfPoolItem> items, MfLabourGateSettings settings)
        {
            settings ??= new MfLabourGateSettings();
            List<decimal> hours = HoursOfPeople(items).Values.Where(h => h > 0).OrderByDescending(h => h).ToList();
            decimal total = hours.Sum();
            var result = new MfGateResult { People = hours.Count, Hours = total };
            if (total > 0)
            {
                result.LargestShare = hours[0] / total;
                result.TopTwoShare = hours.Take(2).Sum() / total;
            }
            result.EnoughPeople = hours.Count >= settings.MinPeople;
            // compared on the hours themselves (no division), so the boundary is exact: exactly half is allowed
            result.Dominated = total > 0 && (hours[0] > settings.MaxShare * total || hours.Take(2).Sum() > settings.MaxTopTwoShare * total);
            return result;
        }

        private static MfPoolResult Build(List<MfPoolItem> list, MfGateResult gate, bool isProtected)
        {
            decimal hours = list.Sum(i => i.Hours), cost = list.Sum(i => i.Cost), absorbed = list.Sum(i => i.Absorbed);
            decimal rounded = Math.Round(cost, 0, MidpointRounding.AwayFromZero);
            return new MfPoolResult
            {
                Key = CompanyKey, People = gate.People, Hours = hours, Cost = rounded, Absorbed = absorbed, Variance = rounded - absorbed, Rate = isProtected ? (decimal?)null : Rate(cost, hours),
                Protected = isProtected, ItemIds = list.Select(i => i.Id).OrderBy(i => i).ToList(),
            };
        }

        /// <summary>The month of a module: the pool when the gate is passed, the carry when it is not, the flagged quarter-end line when the quarter ends.</summary>
        public static MfPoolPlan Plan(IEnumerable<MfPoolItem> items, int period, MfLabourGateSettings settings)
        {
            var plan = new MfPoolPlan();
            List<MfPoolItem> all = items.ToList();
            if (all.Count == 0) return plan;
            plan.Gate = Gate(all, settings);
            if (plan.Gate.Passed) plan.Pool = Build(all, plan.Gate, false);
            else if (IsQuarterEnd(period)) plan.Pool = Build(all, plan.Gate, true);
            else plan.CarriedItemIds.AddRange(all.Select(i => i.Id));
            return plan;
        }
    }

    /// <summary>
    /// R1p D1/D2: the two lines of the month-end variance of a module. Under-absorbed (variance above zero): the labour-rate-variance account is debited and the absorbed-labour account credited; over-absorbed: the other
    /// way round. The lines carry the protected flag of the pool and no work order, WBS node or day (production: no cost centre either; after-sales: its own cost centre).
    /// </summary>
    internal static class MfLabourVariance
    {
        public static List<Entities.Model.NasbNiro.Finance.AccountingEventLine> Lines(decimal variance, string absorbedRole, string costCenter, bool isProtected, string description)
        {
            var lines = new List<Entities.Model.NasbNiro.Finance.AccountingEventLine>();
            if (variance == 0) return lines;
            lines.Add(new Entities.Model.NasbNiro.Finance.AccountingEventLine { AccountRole = global::Services.NasbNiro.Finance.AccountRoles.LabourRateVariance, Debit = Math.Max(variance, 0), Credit = Math.Max(-variance, 0), CostCenter = costCenter, LabourProtected = isProtected, Description = description });
            lines.Add(new Entities.Model.NasbNiro.Finance.AccountingEventLine { AccountRole = absorbedRole, Debit = Math.Max(-variance, 0), Credit = Math.Max(variance, 0), CostCenter = costCenter, LabourProtected = isProtected, Description = description });
            return lines;
        }
    }

    /// <summary>
    /// R1p D1 and D2 (round 3): the rate a day or a service order absorbs labour at. A STANDARD rate that finance sets and that is not derived from anybody's pay: the standard rate of the work centre
    /// (<see cref="MfWorkCenter.LabourRateRial"/>, maintained per Persian year; the after-sales labour work centre has one too), else the setting <c>Production:StandardLabourRate</c>; when neither exists the
    /// run refuses and names the centre. The same rate applies to a person with a payslip and one without, so the amounts posted per work order, WBS node and day are hours x a standard and show nothing of any pay.
    /// </summary>
    internal static class MfLabourRates
    {
        /// <summary>What a day absorbs for a booking: hours x the standard rate, to the whole rial, half away from zero. The month-end variance is the payroll cost less the sum of these.</summary>
        public static decimal Absorbed(decimal hours, decimal rate) => Math.Round(hours * rate, 0, MidpointRounding.AwayFromZero);

        /// <summary>The standard labour rate of a centre: its own rate, else the configured fallback; null when neither is set (positive values only).</summary>
        public static decimal? Standard(decimal? workCenter, decimal? configured) => workCenter.HasValue && workCenter.Value > 0 ? workCenter : configured.HasValue && configured.Value > 0 ? configured : null;
    }

    /// <summary>
    /// R1p H3 (round 3): the read-side mask for rows stored by the EARLIER release, which absorbed labour at the employee's own hourly pay. A run or posting of that release shows one person's pay when only one or two
    /// of the people behind it were paid through payroll; it is returned as hidden to every reader but PAYROLL, FINANCEMANAGER and CEO. The new release never writes such a row: it absorbs at the standard rate, sets
    /// <see cref="MfCostRun.LabourProtected"/> to false on every run and keeps the counts only as information, so none of these rules trips on a row it wrote. The CEO overview and drill use the same helper.
    /// </summary>
    internal static class MfLegacyLabour
    {
        /// <summary>The smallest group of payroll-rated people whose labour amount the earlier release published.</summary>
        public const int MinEmployees = 3;

        /// <summary>The earlier release's group rule: payroll-rated people behind the amount, fewer than three of them (employees and payroll-rated people both count).</summary>
        public static bool Group(int employees, int payrollEmployees) => payrollEmployees > 0 && Math.Min(employees, payrollEmployees) < MinEmployees;

        /// <summary>A run of the earlier release that held at least one protected group (the flag it stored). A run of this release is never protected.</summary>
        public static bool IsProtected(MfCostRun run) => run != null && run.LabourProtected;

        /// <summary>A posting of the earlier release: its run was flagged and its own group had one or two payroll-rated people.</summary>
        public static bool IsProtected(bool runProtected, int employees, int payrollEmployees) => runProtected && Group(employees, payrollEmployees);

        public static bool IsProtected(MfCostRun run, MfCostPosting posting) => run != null && posting != null && IsProtected(run.LabourProtected, posting.EmployeeCount, posting.PayrollEmployeeCount);

        /// <summary>The amount for a reader: empty when the row is protected and the reader is not a payroll reader.</summary>
        public static decimal? Labour(decimal amount, bool isProtected, bool seesPayroll) => isProtected && !seesPayroll ? (decimal?)null : amount;
    }

    /// <summary>
    /// D1: who reads payroll-derived figures and the names behind booked hours. A seat of the AI (or the evaluation user, which acts with the CEO role but whose text other people read) is never one of them:
    /// it gets the masked form, like in the after-sales module.
    /// </summary>
    internal static class MfPrivacy
    {
        /// <summary>Bookings with the employee's name next to the hours: the shop management and the payroll roles (a booking's own employee sees himself as well).</summary>
        public const string NamedRoles = "PRODUCTIONMANAGER,SHOPSUPERVISOR,PAYROLL,FINANCEMANAGER,CEO";

        /// <summary>The roles that see a labour amount that could show one person's pay.</summary>
        public const string PayrollRoles = "PAYROLL,FINANCEMANAGER,CEO";

        /// <summary>Who may open the list of bookings at all: the production readers, the terminal workers (their own bookings) and payroll.</summary>
        public const string BookingReaders = ProductionRoles.MasterReaders + ",PAYROLL";

        public static bool SeesNames(global::Services.NasbNiro.Inventory.IInventoryUserContext user) => user.IsInRole(NamedRoles) && !global::Services.NasbNiro.Ai.AiActing.IsActing;

        public static bool SeesPayroll(global::Services.NasbNiro.Inventory.IInventoryUserContext user) => user.IsInRole(PayrollRoles) && !global::Services.NasbNiro.Ai.AiActing.IsActing;
    }

    /// <summary>The planned serial numbers of an order (one per line, or separated by a comma or a semicolon).</summary>
    internal static class MfSerialRules
    {
        public static List<string> Planned(string text) =>
            (text ?? string.Empty).Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        /// <summary>The serial numbers as the one-per-line text the non-conformance report keeps (the field of the NCR is "one serial per line").</summary>
        public static string PerLine(IEnumerable<string> serials) => string.Join("\n", serials);

        /// <summary>
        /// R1p D6/D33: which serial numbers a write-off of <paramref name="quantity"/> pieces takes. Nothing planned: nothing to track (empty list). The scrap of every planned piece needs no names; a part of them needs exactly
        /// that many serials, all of them among the planned ones. The persian reason comes back instead of a list when the serials cannot be told.
        /// </summary>
        public static (List<string> Scrapped, string Error) Scrapped(List<string> planned, string named, decimal quantity)
        {
            if (planned == null || planned.Count == 0) return (new List<string>(), null);
            if (quantity <= 0 || quantity != Math.Floor(quantity)) return (null, "تعداد قطعه‌ی سریال‌دار باید عدد صحیح و بیشتر از صفر باشد.");
            int count = (int)quantity;
            List<string> typed = Planned(named);
            if (typed.Count == 0)
            {
                if (count == planned.Count) return (planned.ToList(), null);
                return (null, $"کالا سریال‌دار است؛ شماره سریال {count} قطعه‌ی ضایعاتی را وارد کنید (هر سریال در یک خط). سریال‌های برنامه‌ریزی‌شده‌ی دستور: {string.Join("، ", planned)}.");
            }
            List<string> unknown = typed.Where(t => !planned.Any(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0) return (null, "این سریال‌ها جزو سریال‌های برنامه‌ریزی‌شده‌ی دستور نیستند یا قبلاً ضایعات شده‌اند: " + string.Join("، ", unknown) + ".");
            if (typed.Count != count) return (null, $"تعداد سریال‌های ضایعاتی ({typed.Count}) با تعداد ضایعات ({count}) برابر نیست.");
            return (typed.Select(t => planned.First(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase))).ToList(), null);
        }

        /// <summary>The planned serials that stay after a write-off, in the planned order.</summary>
        public static List<string> Remaining(List<string> planned, IEnumerable<string> scrapped)
        {
            var gone = new HashSet<string>(scrapped ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return planned.Where(p => !gone.Contains(p)).ToList();
        }

        /// <summary>
        /// The planned serials of the pieces that replace the scrapped ones: the scrapped serial with "-R" (and a number when that is taken as well), so the new piece is traceable to the one it replaces.
        /// ★ provisional naming; the planner can change them while the replacement order is a draft.
        /// </summary>
        public static List<string> Replacements(IEnumerable<string> scrapped, Func<string, bool> taken)
        {
            var result = new List<string>();
            foreach (string serial in scrapped)
            {
                string candidate = serial + "-R";
                int n = 1;
                while (taken(candidate) || result.Any(r => string.Equals(r, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    n++;
                    candidate = serial + "-R" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                result.Add(candidate);
            }
            return result;
        }

        /// <summary>The serial numbers as one text that fits a column of <paramref name="max"/> characters: whole serials only, with " …" at the end when some did not fit; null when none fits.</summary>
        public static string Joined(IEnumerable<string> serials, int max)
        {
            var kept = new List<string>();
            int length = 0;
            bool cut = false;
            foreach (string s in serials)
            {
                int add = (kept.Count == 0 ? 0 : 2) + s.Length;
                if (length + add > max - 2) { cut = true; break; }
                kept.Add(s);
                length += add;
            }
            return kept.Count == 0 ? null : string.Join(", ", kept) + (cut ? " …" : string.Empty);
        }
    }

    /// <summary>R1p D1: names of the monthly variance of the production labour (the Persian month arithmetic is the one of the after-sales month-end run).</summary>
    internal static class MfLabourMonth
    {
        public const string SourceType = "MfLabourMonth";

        public static string Number(int period) => "MLM-" + period.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static int Of(DateTime day) => global::Services.NasbNiro.AfterSales.SvLabourMonth.Of(day);
        public static DateTime End(int period) => global::Services.NasbNiro.AfterSales.SvLabourMonth.End(period);
        public static string Text(int period) => global::Services.NasbNiro.AfterSales.SvLabourMonth.Text(period);
        public static int NextOpen(int period, IEnumerable<int> posted) => global::Services.NasbNiro.AfterSales.SvLabourMonth.NextOpen(period, posted);

        /// <summary>The month whose pool takes a booking of <paramref name="day"/>: its own month, or the first later month that is not closed yet (a closed month is never reopened).</summary>
        public static int Assign(DateTime day, IEnumerable<int> closed) => global::Services.NasbNiro.AfterSales.SvLabourMonth.Assign(day, closed);
    }

    /// <summary>D39 and D1: who may change or read time bookings.</summary>
    internal static class MfBookingRules
    {
        /// <summary>The bookings of a completed, closed or cancelled order are frozen: the shop floor does not edit them; a supervisor adds a correction booking with a reason.</summary>
        public static bool IsFrozen(MfOrderStatus status) => status == MfOrderStatus.Completed || status == MfOrderStatus.Closed || status == MfOrderStatus.Cancelled;

        /// <summary>Whether the booking shows the person: the named roles, or the booking's own employee. Everybody else sees the hours without the name.</summary>
        public static bool SeesName(bool inNamedRole, int? readerEmployeeId, int bookingEmployeeId) => inNamedRole || (readerEmployeeId.HasValue && readerEmployeeId.Value == bookingEmployeeId);

        /// <summary>R1p D39: a correction booking counts (cost run, hours, OEE, attendance check) only after a production manager other than its creator approved it.</summary>
        public static IQueryable<MfTimeBooking> Effective(IQueryable<MfTimeBooking> bookings) => bookings.Where(b => !b.IsCorrection || b.CorrectionApprovedByUserId != null);

        /// <summary>R1p D39: the days a correction may reach back (setting Production:MaxBackdateDays, ★ 7). Null when the start is inside the limit.</summary>
        public static string BackdateRefusal(DateTime start, DateTime today, int maxDays)
        {
            if (maxDays < 0) maxDays = 0;
            if (start.Date >= today.Date.AddDays(-maxDays)) return null;
            return $"ثبت زمان اصلاحی حداکثر {maxDays} روز به عقب ممکن است؛ تاریخ شروع این ثبت زمان بیشتر از این گذشته است.";
        }

        /// <summary>
        /// R1p review 9: a correction cannot add more hours to a person's day than the attendance records leave unbooked (the minutes he was present, less the minutes already booked that day).
        /// Null when it fits; a day without any attendance record takes no correction.
        /// </summary>
        public static string AttendanceRefusal(int attendedMinutes, int bookedMinutes, int correctionMinutes)
        {
            if (attendedMinutes <= 0) return "برای این پرسنل در روز ثبت زمان اصلاحی تردد ثبت نشده است؛ زمان اصلاحی فقط برای روزی که حضور ثبت شده ممکن است.";
            if (bookedMinutes + correctionMinutes <= attendedMinutes) return null;
            return $"مجموع زمان ثبت‌شده‌ی این پرسنل در آن روز ({MfFlow.Num(MfText.HoursOf(bookedMinutes))} ساعت) با این ثبت زمان اصلاحی ({MfFlow.Num(MfText.HoursOf(correctionMinutes))} ساعت) از حضور او ({MfFlow.Num(MfText.HoursOf(attendedMinutes))} ساعت) بیشتر می‌شود.";
        }

        /// <summary>The persian reason why a correction cannot be approved by this user, or null.</summary>
        public static string ApprovalRefusal(MfTimeBooking booking, int approverUserId)
        {
            if (!booking.IsCorrection) return "این ثبت زمان اصلاحی نیست.";
            if (booking.CorrectionApprovedByUserId.HasValue) return "این ثبت زمان اصلاحی قبلاً تأیید شده است.";
            if (booking.CreatorUserId == approverUserId) return "ثبت زمان اصلاحی را شخص دیگری غیر از ثبت‌کننده‌ی آن باید تأیید کند (تفکیک وظایف).";
            return null;
        }

        /// <summary>
        /// R1p D7/D38: the quantities booked on a step are final once the step is done and QC accepted them, an NCR decided about them, or they went through by a concession, and once the next step started working on them.
        /// Returns the persian reason, or null when the quantities of the step may still change.
        /// </summary>
        public static string QuantityFrozen(MfOperation op, IEnumerable<MfOperation> operations)
        {
            bool settled = op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Rejected;
            if (op.QcAccepted && op.Status == MfOperationStatus.Done)
                return "تعداد این مرحله پس از پذیرش بازرسی QC تغییر نمی‌کند؛ برای تغییر، بازرسی دوباره (گزارش عدم انطباق) لازم است.";
            if (op.AcceptedByConcession)
                return "این مرحله با ارفاق ادامه یافته است و تعداد آن تغییر نمی‌کند.";
            if (op.NcrId.HasValue && settled)
                return "برای این مرحله گزارش عدم انطباق ثبت شده و تصمیم آن اعمال شده یا در جریان است؛ تعداد آن با ثبت زمان تغییر نمی‌کند.";
            List<MfOperation> chain = MfFlow.Chain(operations);
            int index = chain.FindIndex(o => ReferenceEquals(o, op) || (o.Id != 0 && o.Id == op.Id));
            if (index >= 0 && op.Status == MfOperationStatus.Done)
            {
                MfOperation next = chain.Skip(index + 1).FirstOrDefault(o => o.Status != MfOperationStatus.Skipped);
                if (next != null && (next.StartedAt.HasValue || next.Status == MfOperationStatus.InProgress || next.Status == MfOperationStatus.Paused || next.Status == MfOperationStatus.QcHold
                                     || next.Status == MfOperationStatus.Done || next.Status == MfOperationStatus.Rejected))
                    return "مرحله بعد از این مرحله شروع شده است و با تعداد آن کار می‌کند؛ تعداد این مرحله دیگر تغییر نمی‌کند.";
            }
            return null;
        }
    }

    /// <summary>D38 and D43: terminal rules.</summary>
    internal static class MfTerminalRules
    {
        /// <summary>
        /// The machine is mandatory at the terminal for a work centre that has machines to choose from (<see cref="MfWorkCenter.RequiresMachine"/>). A waiting booking is exempt: nothing runs on a machine while somebody waits.
        /// </summary>
        public static bool MachineRequired(bool requiresMachine, int availableMachines, MfBookingKind kind) => requiresMachine && availableMachines > 0 && kind != MfBookingKind.Waiting;

        /// <summary>The acceptance of an early QC request no longer holds when the quantities changed after it (D38).</summary>
        public static bool AcceptanceStillHolds(MfOperation op) =>
            op.QcAccepted && (!op.QcAcceptedGoodQty.HasValue
                || (op.QcAcceptedGoodQty == op.GoodQty && op.QcAcceptedScrapQty == op.ScrapQty && op.QcAcceptedReworkQty == op.ReworkQty));
    }

    /// <summary>D6 and D8: what an NCR has to say before the production order may follow it.</summary>
    internal static class MfNcrRules
    {
        private static bool Decided(NcrStatus status) => status == NcrStatus.Approved || status == NcrStatus.Executed || status == NcrStatus.Closed;

        /// <summary>The persian reason why the NCR does not allow the given disposition, or null when it does.</summary>
        public static string Check(NonConformance ncr, NcrDisposition wanted)
        {
            string name = wanted == NcrDisposition.Concession ? "ارفاقی" : wanted == NcrDisposition.Scrap ? "ضایعات" : "دوباره‌کاری";
            if (ncr.Disposition != wanted) return $"تصمیم این گزارش عدم انطباق «{name}» نیست.";
            if (wanted == NcrDisposition.Rework ? !(ncr.Status == NcrStatus.Approved || ncr.Status == NcrStatus.Executed) : !Decided(ncr.Status))
                return $"تصمیم «{name}» این گزارش عدم انطباق هنوز با امضای کیفیت، مهندسی و تولید تأیید نشده است؛ دستور تولید تا آن زمان ادامه نمی‌یابد.";
            // R1p D8: one report allows one rework operation; if that rework fails inspection too, the inspection raises a new report on it and the decision (concession or scrap) is taken on that one
            if (wanted == NcrDisposition.Rework && ncr.ReworkOperationId.HasValue)
                return "برای این گزارش عدم انطباق قبلاً عملیات دوباره‌کاری ساخته شده است و یک گزارش فقط یک بار دوباره‌کاری می‌دهد؛ اگر دوباره‌کاری هم در بازرسی رد شد، بازرسی برای آن گزارش عدم انطباق تازه می‌سازد و تصمیم (ارفاق یا ضایعات) روی آن گرفته می‌شود.";
            return null;
        }
    }

    /// <summary>R1p D19: the release check of the ITP. A work order without any ITP is refused (before, it passed), and so is one whose ITP the client has not approved.</summary>
    internal static class MfReleaseRules
    {
        /// <param name="exists">The work order has an ITP at all.</param>
        /// <param name="status">Status of its newest revision (ignored when there is none).</param>
        /// <param name="number">Number of that ITP (ignored when there is none).</param>
        /// <param name="workOrderCode">Code of the work order, named in the refusal when it has no ITP.</param>
        public static (bool Passed, string Message) Itp(bool exists, Entities.Model.NasbNiro.Quality.ItpStatus status, string number, string workOrderCode)
        {
            if (!exists)
                return (false, $"برای دستور کار {workOrderCode} برنامه بازرسی و آزمون (ITP) ساخته نشده است؛ بدون ITP دستور تولید صادر نمی‌شود. ITP را از قالب ماژول کیفیت بسازید و به تأیید کارفرما برسانید.");
            bool approved = status == Entities.Model.NasbNiro.Quality.ItpStatus.ClientApproved || status == Entities.Model.NasbNiro.Quality.ItpStatus.InUse;
            return (approved, approved ? $"ITP شماره {number} تأیید شده است." : $"ITP شماره {number} هنوز تأیید کارفرما را ندارد.");
        }
    }
}
