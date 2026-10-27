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

    /// <summary>
    /// D1 and D2: the privacy of a month of labour. A pool of labour (a work order, a cost centre) that fewer than three payroll-rated employees worked on would show one person's hourly pay
    /// when it is posted as an amount. Such a pool is not posted: it is carried to the next month and joins that month's pool; at a quarter end it is posted anyway (flagged as protected,
    /// so every reader but payroll, the finance manager and the CEO sees no amount). ★ provisional decision: the cap is the end of the Persian quarter.
    /// </summary>
    internal enum MfLabourMonthAction
    {
        /// <summary>Enough people behind the pool: post it as it is.</summary>
        Post = 0,

        /// <summary>Too few people: wait for the next month.</summary>
        Carry = 1,

        /// <summary>Too few people but the quarter ends: post it flagged as protected.</summary>
        PostProtected = 2,
    }

    internal static class MfLabourCarry
    {
        /// <summary>Khordad, Shahrivar, Azar and Esfand end the Persian quarters.</summary>
        public static bool IsQuarterEnd(int period) => period % 100 == 3 || period % 100 == 6 || period % 100 == 9 || period % 100 == 12;

        public static MfLabourMonthAction Decide(int employees, int payrollEmployees, int period) =>
            !MfCostMask.IsProtected(employees, payrollEmployees) ? MfLabourMonthAction.Post
            : IsQuarterEnd(period) ? MfLabourMonthAction.PostProtected
            : MfLabourMonthAction.Carry;
    }

    /// <summary>One booking whose labour waits for the monthly aggregate posting (the plain values the planner needs).</summary>
    internal sealed class MfPendingBooking
    {
        public int BookingId;
        public int WorkOrderId;
        public int? ContractId;
        public int? WbsNodeId;
        public string CostCenter;
        public int EmployeeId;

        /// <summary>The hourly rate of the employee came from an approved payslip.</summary>
        public bool FromPayroll;

        public decimal Labour;
    }

    /// <summary>A labour amount of the monthly aggregate posting: per work order (and WBS node), or per cost centre when the work order alone was too small.</summary>
    internal sealed class MfLabourPlanGroup
    {
        public int? WorkOrderId;
        public int? ContractId;
        public int? WbsNodeId;

        /// <summary>The cost centre of a cost-centre group (no work order); null for a work-order group.</summary>
        public string CostCenter;

        public decimal Amount;

        /// <summary>Posted only because the quarter ended: fewer than three payroll-rated employees are behind it, so only PAYROLL, FINANCEMANAGER and CEO read the amount.</summary>
        public bool Protected;

        public List<int> BookingIds = new List<int>();
    }

    internal sealed class MfLabourPlan
    {
        public List<MfLabourPlanGroup> Groups = new List<MfLabourPlanGroup>();

        /// <summary>The bookings that wait for the next month.</summary>
        public List<int> CarriedBookingIds = new List<int>();
    }

    /// <summary>
    /// D1: what the monthly aggregate posting does with the pool of a month. Level 1: every (work order, WBS node) with enough people behind it (three payroll-rated employees) is posted on its own.
    /// Level 2: what is left is pooled per cost centre; a cost centre with enough people is posted as one amount without a work order. Level 3: a cost centre that is still too small is carried to the next
    /// month (and joins that month's pool) - except at the end of a Persian quarter, when its work-order groups are posted anyway, flagged as protected (★ provisional cap).
    /// </summary>
    internal static class MfLabourPlanner
    {
        private static int Employees(IEnumerable<MfPendingBooking> g) => g.Select(b => b.EmployeeId).Distinct().Count();
        private static int PayrollEmployees(IEnumerable<MfPendingBooking> g) => g.Where(b => b.FromPayroll).Select(b => b.EmployeeId).Distinct().Count();
        private static decimal Rounded(IEnumerable<MfPendingBooking> g) => Math.Round(g.Sum(b => b.Labour), 0, MidpointRounding.AwayFromZero);

        private static MfLabourPlanGroup WorkOrderGroup(IGrouping<(int WorkOrderId, int? WbsNodeId), MfPendingBooking> g, bool isProtected) => new MfLabourPlanGroup
        {
            WorkOrderId = g.Key.WorkOrderId, ContractId = g.Select(b => b.ContractId).FirstOrDefault(c => c.HasValue), WbsNodeId = g.Key.WbsNodeId, Amount = Rounded(g), Protected = isProtected,
            BookingIds = g.Select(b => b.BookingId).OrderBy(i => i).ToList(),
        };

        public static MfLabourPlan Plan(IEnumerable<MfPendingBooking> pool, int period)
        {
            var plan = new MfLabourPlan();
            var rest = new List<MfPendingBooking>();
            foreach (var g in pool.GroupBy(b => (b.WorkOrderId, b.WbsNodeId)).OrderBy(g => g.Key.WorkOrderId).ThenBy(g => g.Key.WbsNodeId ?? 0))
            {
                if (MfLabourCarry.Decide(Employees(g), PayrollEmployees(g), period) == MfLabourMonthAction.Post) plan.Groups.Add(WorkOrderGroup(g, false));
                else rest.AddRange(g);
            }
            foreach (var cc in rest.GroupBy(b => b.CostCenter ?? MfLabourMonth.DefaultCostCenter).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                switch (MfLabourCarry.Decide(Employees(cc), PayrollEmployees(cc), period))
                {
                    case MfLabourMonthAction.Post:
                        plan.Groups.Add(new MfLabourPlanGroup { CostCenter = cc.Key, Amount = Rounded(cc), BookingIds = cc.Select(b => b.BookingId).OrderBy(i => i).ToList() });
                        break;
                    case MfLabourMonthAction.PostProtected:
                        foreach (var g in cc.GroupBy(b => (b.WorkOrderId, b.WbsNodeId)).OrderBy(g => g.Key.WorkOrderId).ThenBy(g => g.Key.WbsNodeId ?? 0)) plan.Groups.Add(WorkOrderGroup(g, true));
                        break;
                    default:
                        plan.CarriedBookingIds.AddRange(cc.Select(b => b.BookingId));
                        break;
                }
            }
            return plan;
        }
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

    /// <summary>D1: names of the monthly aggregate posting of protected production labour (the Persian month arithmetic is the one of the after-sales monthly posting).</summary>
    internal static class MfLabourMonth
    {
        public const string SourceType = "MfLabourMonth";

        public static string Number(int period) => "MLM-" + period.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static int Of(DateTime day) => global::Services.NasbNiro.AfterSales.SvLabourMonth.Of(day);
        public static DateTime End(int period) => global::Services.NasbNiro.AfterSales.SvLabourMonth.End(period);
        public static string Text(int period) => global::Services.NasbNiro.AfterSales.SvLabourMonth.Text(period);
        public static int NextOpen(int period, IEnumerable<int> posted) => global::Services.NasbNiro.AfterSales.SvLabourMonth.NextOpen(period, posted);

        /// <summary>The cost centre that takes a booking's labour when its work order alone is protected: the one of the work centre, else the production cost centre.</summary>
        public const string DefaultCostCenter = "PRODUCTION";
    }

    /// <summary>D39 and D1: who may change or read time bookings.</summary>
    internal static class MfBookingRules
    {
        /// <summary>The bookings of a completed, closed or cancelled order are frozen: the shop floor does not edit them; a supervisor adds a correction booking with a reason.</summary>
        public static bool IsFrozen(MfOrderStatus status) => status == MfOrderStatus.Completed || status == MfOrderStatus.Closed || status == MfOrderStatus.Cancelled;

        /// <summary>Whether the booking shows the person: the named roles, or the booking's own employee. Everybody else sees the hours without the name.</summary>
        public static bool SeesName(bool inNamedRole, int? readerEmployeeId, int bookingEmployeeId) => inNamedRole || (readerEmployeeId.HasValue && readerEmployeeId.Value == bookingEmployeeId);
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
