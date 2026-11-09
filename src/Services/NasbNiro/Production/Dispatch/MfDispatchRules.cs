using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Services.NasbNiro.Production.Dispatch
{
    /// <summary>
    /// The pure rules of the production AI seat (MF-DISPATCH): the critical ratio of a step, the order of a work-centre queue and the load level of a centre. No database, no clock:
    /// the same input gives the same plan, so every rule is unit tested and every proposal can be explained.
    /// </summary>
    public static class MfDispatchRules
    {
        public const string StageRunning = "running";
        public const string StageReady = "ready";
        public const string StageUpcoming = "upcoming";

        public const string RiskLate = "late-risk";
        public const string RiskAt = "at-risk";
        public const string RiskOnTime = "on-time";
        public const string RiskNoDate = "no-date";

        public const string LevelBottleneck = "bottleneck";
        public const string LevelBusy = "busy";
        public const string LevelNormal = "normal";
        public const string LevelIdle = "idle";

        /// <summary>A critical ratio below this is "late unless it moves up".</summary>
        public const decimal LateRatio = 1m;

        /// <summary>A critical ratio below this (and at least <see cref="LateRatio"/>) has little to spare.</summary>
        public const decimal AtRiskRatio = 1.2m;

        /// <summary>Load above this share of capacity is a bottleneck.</summary>
        public const decimal BottleneckPercent = 100m;

        /// <summary>Load above this share of capacity is busy.</summary>
        public const decimal BusyPercent = 85m;

        public const int DefaultHorizonDays = 10;

        /// <summary>One step of an order as the rules see it.</summary>
        public sealed class Step
        {
            public int OperationId { get; init; }
            public int OrderId { get; init; }
            public string OrderNumber { get; init; }
            public int Sequence { get; init; }
            public int? WorkCenterId { get; init; }
            public string Stage { get; init; }
            public decimal HoursLeft { get; init; }
            public DateTime? NeedDate { get; init; }
            public DateTime? ScheduledStart { get; init; }

            /// <summary>The CPM priority of the order (1 = most urgent; 0 = none).</summary>
            public int Priority { get; init; }

            /// <summary>Working days from today to the need date (inclusive of today, 0 when the date is past).</summary>
            public decimal? DaysToNeed { get; init; }

            /// <summary>Working days the rest of the order (this step and the later ones, with the move days) still needs.</summary>
            public decimal DaysOfWork { get; init; }

            public bool KitShortage { get; init; }
        }

        /// <summary>Working days left divided by working days of work; null without a need date. A step with nothing left to do gets the days left over a tenth of a day.</summary>
        public static decimal? CriticalRatio(decimal? daysToNeed, decimal daysOfWork)
        {
            if (!daysToNeed.HasValue) return null;
            decimal work = Math.Max(daysOfWork, 0.1m);
            return Math.Round(Math.Max(0m, daysToNeed.Value) / work, 2);
        }

        public static decimal? Slack(decimal? daysToNeed, decimal daysOfWork) => daysToNeed.HasValue ? Math.Round(daysToNeed.Value - daysOfWork, 1) : null;

        public static string Risk(decimal? ratio) =>
            !ratio.HasValue ? RiskNoDate : ratio.Value < LateRatio ? RiskLate : ratio.Value < AtRiskRatio ? RiskAt : RiskOnTime;

        /// <summary>
        /// The order of a work-centre queue: what runs now stays on top (a machine is not stopped for another job); then the steps that may start, the late ones first by critical ratio,
        /// then by the CPM priority of the order, the earliest need date and the scheduler's start; the steps that wait for an earlier one come last in the same order. A kit shortage pushes a
        /// ready step behind the ready steps that have their material (it cannot be set up anyway).
        /// </summary>
        public static List<Step> Order(IEnumerable<Step> steps) =>
            steps.OrderBy(s => StageRank(s.Stage))
                .ThenBy(s => s.Stage == StageReady && s.KitShortage ? 1 : 0)
                .ThenBy(s => s.Stage == StageRunning ? 0m : Ratio(s))
                .ThenBy(s => s.Priority > 0 ? s.Priority : int.MaxValue)
                .ThenBy(s => s.NeedDate ?? DateTime.MaxValue)
                .ThenBy(s => s.ScheduledStart ?? DateTime.MaxValue)
                .ThenBy(s => s.OrderNumber, StringComparer.Ordinal)
                .ThenBy(s => s.Sequence)
                .ToList();

        private static decimal Ratio(Step s) => CriticalRatio(s.DaysToNeed, s.DaysOfWork) ?? decimal.MaxValue;

        private static int StageRank(string stage) => stage == StageRunning ? 0 : stage == StageReady ? 1 : 2;

        /// <summary>The level of a work centre from its load (demand over capacity in the horizon) and the work that may start now.</summary>
        public static string Level(decimal demandHours, decimal capacityHours, int readyCount, int runningCount)
        {
            if (capacityHours <= 0) return demandHours > 0 ? LevelBottleneck : LevelIdle;
            decimal load = demandHours / capacityHours * 100m;
            if (load > BottleneckPercent) return LevelBottleneck;
            if (load > BusyPercent) return LevelBusy;
            return readyCount == 0 && runningCount == 0 && demandHours <= 0 ? LevelIdle : LevelNormal;
        }

        public static decimal? LoadPercent(decimal demandHours, decimal capacityHours) => capacityHours > 0 ? Math.Round(demandHours / capacityHours * 100m, 1) : null;

        /// <summary>The Persian reason of a place in the queue.</summary>
        public static string Reason(Step s, int rank)
        {
            decimal? cr = CriticalRatio(s.DaysToNeed, s.DaysOfWork);
            string place = "رتبه " + Fa(rank);
            if (s.Stage == StageRunning) return place + ": هم‌اکنون روی ماشین است و تا پایان ادامه می‌یابد.";
            var parts = new List<string>();
            if (s.Stage == StageUpcoming) parts.Add("منتظر پایان مرحله قبل");
            if (cr.HasValue)
            {
                decimal slack = Slack(s.DaysToNeed, s.DaysOfWork) ?? 0;
                parts.Add(cr.Value < LateRatio
                    ? $"نسبت بحرانی {Fa(cr.Value)}؛ با این نوبت {Fa(Math.Abs(slack))} روز کاری دیر می‌شود"
                    : cr.Value < AtRiskRatio ? $"نسبت بحرانی {Fa(cr.Value)}؛ فقط {Fa(slack)} روز کاری فرصت دارد" : $"نسبت بحرانی {Fa(cr.Value)}؛ {Fa(slack)} روز کاری فرصت دارد");
            }
            else parts.Add("تاریخ نیاز ندارد");
            if (s.Priority > 0) parts.Add("اولویت پروژه " + Fa(s.Priority));
            if (s.KitShortage) parts.Add("کیت مواد کامل نیست");
            return place + ": " + string.Join("، ", parts) + ".";
        }

        /// <summary>Persian digits for a number (up to two decimals, no trailing zeros).</summary>
        public static string Fa(decimal value)
        {
            string text = Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
            var chars = text.Select(ch => ch >= '0' && ch <= '9' ? (char)('۰' + (ch - '0')) : ch == '.' ? '٫' : ch == '-' ? '−' : ch);
            return new string(chars.ToArray());
        }

        public static string Fa(int value) => Fa((decimal)value);
    }
}
