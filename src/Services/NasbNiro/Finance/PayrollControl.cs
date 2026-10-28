using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.OfficeDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Release R1f: the numbers that used to be literals in the payroll, attendance and leave code live in the rate table (every key with a source and a Verify flag, seeded as a starred default).
    // A run, a close or a request that needs a key which is absent refuses with a Persian message naming the key; nothing is assumed silently.

    /// <summary>One named number of the rate table: its starred default (seeded with Verify = true), the source text and the range the save accepts.</summary>
    internal sealed class PayrollKeySpec
    {
        public string Key;
        public decimal Default;
        public decimal Min;
        public decimal Max;
        public bool Integer;
        public string Source;
        public string Label;
    }

    internal static class PayrollKeys
    {
        public const string MonthDays = "PayrollMonthDays";
        public const string YearDays = "PayrollYearDays";
        public const string DefaultDayMinutes = "DefaultDayMinutes";
        public const string NightStart = "NightWindowStartMinute";
        public const string NightEnd = "NightWindowEndMinute";
        public const string OvertimeRequestMaxHours = "OvertimeRequestMaxHoursPerDay";
        public const string ChildAllowanceDays = "ChildAllowanceMinWageDays";
        public const string HolidayPremium = "HolidayPremiumPercent";
        public const string SickPayBasis = "SickPayBasis";
        public const string SickCompanyDays = "SickCompanyPaidDays";

        // long-standing keys
        public const string LeaveEntitlement = "LeaveEntitlementMonthly";
        public const string LeaveCarryMax = "LeaveCarryForwardMax";
        public const string MinimumDailyWage = "MinimumDailyWage";
        public const string BonusDaysPerYear = "BonusDaysPerYear";
        public const string BonusCapDays = "BonusCapDaysOfMinimumWage";
        public const string BonusGrossCapDays = "BonusGrossCapDaysOfMinimumWage";

        /// <summary>The sick-pay basis values: 0 = the company pays every sick day; 1 = the company pays the first SickCompanyPaidDays days of a month, social security the rest; 2 = social security pays from the first day.</summary>
        public const int SickAllByCompany = 0, SickCompanyThenSso = 1, SickAllBySso = 2;

        private const string Review = " — توسط مالی تایید شود";

        /// <summary>The keys added by release R1f. The values are starred defaults of the company (to be confirmed by finance; Verify stays on until then).</summary>
        public static readonly PayrollKeySpec[] Specs =
        {
            new PayrollKeySpec { Key = MonthDays, Default = 30m, Min = 28m, Max = 31m, Integer = true, Label = "تعداد روز ماه حقوقی",
                Source = "مبنای ماه حقوقی ۳۰ روز (تصمیم شرکت: ماه ۲۹، ۳۰ و ۳۱ روزه هر کدام ۳۰ روز محاسبه می‌شود) ★" + Review },
            new PayrollKeySpec { Key = YearDays, Default = 360m, Min = 360m, Max = 366m, Integer = true, Label = "تعداد روز سال حقوقی",
                Source = "مبنای سال حقوقی ۳۶۰ روز (دوازده ماه ۳۰ روزه) برای حق سنوات ★" + Review },
            new PayrollKeySpec { Key = DefaultDayMinutes, Default = 480m, Min = 60m, Max = 1440m, Integer = true, Label = "دقایق روز کاری بدون شیفت",
                Source = "روز کاری پرسنل بدون شیفت: ۸ ساعت (قانون کار ماده ۵۱) ★" + Review },
            new PayrollKeySpec { Key = NightStart, Default = 1320m, Min = 0m, Max = 1439m, Integer = true, Label = "آغاز ساعات شب (دقیقه از نیمه‌شب)",
                Source = "ساعات شب‌کاری از ۲۲:۰۰ (قانون کار ماده ۵۸) ★" + Review },
            new PayrollKeySpec { Key = NightEnd, Default = 360m, Min = 0m, Max = 1439m, Integer = true, Label = "پایان ساعات شب (دقیقه از نیمه‌شب)",
                Source = "ساعات شب‌کاری تا ۰۶:۰۰ (قانون کار ماده ۵۸) ★" + Review },
            new PayrollKeySpec { Key = OvertimeRequestMaxHours, Default = 4m, Min = 0.25m, Max = 24m, Integer = false, Label = "سقف ساعت اضافه‌کار در یک روز برای هر درخواست",
                Source = "حداکثر اضافه‌کار روزانه ۴ ساعت (قانون کار ماده ۵۹) ★" + Review },
            new PayrollKeySpec { Key = ChildAllowanceDays, Default = 3m, Min = 0m, Max = 100m, Integer = false, Label = "حق اولاد: ضریب حداقل مزد روزانه برای هر فرزند",
                Source = "حق اولاد: ۳ برابر حداقل مزد روزانه به ازای هر فرزند (مصوبه شورای عالی کار) ★" + Review },
            new PayrollKeySpec { Key = HolidayPremium, Default = 40m, Min = 0m, Max = 1000m, Integer = false, Label = "درصد فوق‌العاده کار در روز تعطیل",
                Source = "کار در روز تعطیل: ۴۰٪ بیش از مزد ساعتی (قانون کار ماده ۶۲) ★" + Review },
            new PayrollKeySpec { Key = SickPayBasis, Default = SickCompanyThenSso, Min = 0m, Max = 2m, Integer = true, Label = "مبنای پرداخت روزهای استعلاجی (۰ همه با شرکت، ۱ چند روز اول با شرکت و بقیه تامین اجتماعی، ۲ از روز اول تامین اجتماعی)",
                Source = "مرخصی استعلاجی: روزهای اول با کارفرما و بقیه با تامین اجتماعی (قانون تامین اجتماعی ماده ۴۱) ★" + Review },
            new PayrollKeySpec { Key = SickCompanyDays, Default = 3m, Min = 0m, Max = 31m, Integer = true, Label = "تعداد روزهای استعلاجی هر ماه که شرکت می‌پردازد",
                Source = "سه روز اول استعلاجی با شرکت، از روز چهارم تامین اجتماعی ★" + Review },
        };

        public static PayrollKeySpec Spec(string key) => Specs.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>A Persian message when a value saved under one of the R1f keys is outside its range, else null.</summary>
        public static string Validate(string key, decimal value)
        {
            PayrollKeySpec s = Spec(key);
            if (s == null) return null;
            string range = $"{s.Min.ToString("0.##", CultureInfo.InvariantCulture)} تا {s.Max.ToString("0.##", CultureInfo.InvariantCulture)}";
            if (value < s.Min || value > s.Max) return $"مقدار «{s.Key}» ({s.Label}) باید بین {range} باشد.";
            if (s.Integer && value != Math.Truncate(value)) return $"مقدار «{s.Key}» ({s.Label}) باید عدد صحیح باشد.";
            return null;
        }

        /// <summary>The seeds of the new keys (year 0, Verify on).</summary>
        public static IEnumerable<FnPayrollRateTable> Seeds() =>
            Specs.Select(s => new FnPayrollRateTable { Year = 0, Key = s.Key, Value = s.Default, Source = s.Source, Verify = true });
    }

    /// <summary>Loads the rate table of a year (the seeds are written first, so a fresh database has the starred defaults with their Verify flags).</summary>
    internal static class PayrollRateLoader
    {
        public static async Task<PayrollRates> LoadAsync(IUnitOfWork uow, int year, CancellationToken ct)
        {
            await F2Seed.EnsureAsync(uow, ct);
            return new PayrollRates(await uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => r.Year == 0 || r.Year == year).ToListAsync(ct), year);
        }
    }

    /// <summary>The Persian month a company starts to use the system (payroll and leave): the setting Payroll:GoLiveMonth, else the month of the first confirmed HR close.</summary>
    internal static class PayrollGoLive
    {
        /// <summary>Accepts 1405/08, 1405-8, 140508 (Persian digits too). The year must be 1300..1600.</summary>
        public static bool TryParse(string text, out int year, out int month)
        {
            year = 0; month = 0;
            string t = Digits(text ?? "")?.Trim();
            if (string.IsNullOrEmpty(t)) return false;
            string[] parts = t.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && parts[0].Length == 6) parts = new[] { parts[0].Substring(0, 4), parts[0].Substring(4, 2) };
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)) return false;
            return year >= 1300 && year <= 1600 && month >= 1 && month <= 12;
        }

        public static string Format(int year, int month) => year.ToString("0000", CultureInfo.InvariantCulture) + "/" + month.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>The month index (<see cref="PersianCal.Index(int,int)"/>) of the go-live month, or null while no month has been confirmed and nothing is set.</summary>
        public static async Task<int?> ResolveAsync(IUnitOfWork uow, CancellationToken ct)
        {
            string value = await uow.Set<FnSetting>().AsNoTracking().Where(s => s.Key == FinanceSeed.SettingGoLiveMonth).Select(s => s.Value).FirstOrDefaultAsync(ct);
            if (TryParse(value, out int y, out int m)) return PersianCal.Index(y, m);
            var first = await uow.Set<OfAttendanceMonth>().AsNoTracking().Where(x => x.Status == OfMonthStatus.Confirmed).OrderBy(x => x.Year).ThenBy(x => x.Month)
                .Select(x => new { x.Year, x.Month }).FirstOrDefaultAsync(ct);
            return first == null ? (int?)null : PersianCal.Index(first.Year, first.Month);
        }
    }

    /// <summary>
    /// THE leave accrual, used by the payroll run approval and by the office leave balance (one function, two callers). The entitlement of a year accrues from the latest of
    /// the hire month, the first month of the year and the go-live month; an approved opening balance of the employee replaces the go-live month (the accrual continues from the month of the opening balance).
    /// The monthly rate and the carry-forward limit come from the rate table; nothing is assumed when a key is missing.
    /// </summary>
    internal static class LeaveAccrual
    {
        /// <summary>The first month (1..12) of <paramref name="year"/> in which the employee accrues leave; 13 when he does not accrue in this year at all (hired later, or the go-live month is later).</summary>
        public static int FirstMonth(int year, DateTime hireDate, int? goLiveIndex, int? openingMonth)
        {
            int first = 1;
            int hireYear = PersianCal.Year(hireDate);
            if (hireYear > year) return 13;
            if (hireYear == year) first = Math.Max(first, PersianCal.Month(hireDate));
            if (openingMonth.HasValue) return Math.Max(first, openingMonth.Value);
            if (goLiveIndex.HasValue)
            {
                int yearStart = PersianCal.Index(year, 1);
                if (goLiveIndex.Value > PersianCal.Index(year, 12)) return 13;
                if (goLiveIndex.Value > yearStart) first = Math.Max(first, goLiveIndex.Value - yearStart + 1);
            }
            return first;
        }

        /// <summary>The entitlement (days) of the months after <paramref name="lastBooked"/> up to and including <paramref name="upToMonth"/>, counting only months from <paramref name="firstMonth"/> on.</summary>
        public static decimal Entitlement(decimal monthlyRate, int firstMonth, int lastBooked, int upToMonth)
        {
            int from = Math.Max(lastBooked + 1, firstMonth);
            int months = upToMonth - from + 1;
            return months > 0 && monthlyRate > 0m ? monthlyRate * months : 0m;
        }

        /// <summary>The days carried into the next year: what is left, at most the limit of the rate table, never below zero.</summary>
        public static decimal Carry(decimal carriedForward, decimal entitled, decimal used, decimal carryMax) =>
            Math.Max(0m, Math.Min(carriedForward + entitled - used, carryMax));

        public sealed class State
        {
            public decimal CarriedForward;
            public decimal Entitled;
            public decimal Used;
            public int LastMonth;
        }

        /// <summary>
        /// Books a payroll month: the balance of a new year starts from the carry-forward of the previous year, then the months not booked yet are added to the entitlement and the paid leave of the month to the use.
        /// <paramref name="current"/> is null when the year has no balance row yet.
        /// </summary>
        public static State Book(State current, State previousYear, decimal monthlyRate, decimal carryMax, int firstMonth, int month, decimal usedInMonth)
        {
            State b = current ?? new State { CarriedForward = previousYear == null ? 0m : Carry(previousYear.CarriedForward, previousYear.Entitled, previousYear.Used, carryMax) };
            if (b.LastMonth < month)
            {
                b.Entitled += Entitlement(monthlyRate, firstMonth, b.LastMonth, month);
                b.Used += usedInMonth;
                b.LastMonth = month;
            }
            return b;
        }
    }

    /// <summary>The inputs of the leave accrual of one year, loaded once: the rate-table keys, the go-live month and the approved opening balances.</summary>
    internal sealed class LeaveAccrualPlan
    {
        public int Year { get; private set; }
        public decimal MonthlyRate { get; private set; }
        public decimal CarryMax { get; private set; }
        public int? GoLiveIndex { get; private set; }
        public Dictionary<int, FnLeaveOpening> Openings { get; } = new Dictionary<int, FnLeaveOpening>();

        public int FirstMonth(FnEmployee e) =>
            LeaveAccrual.FirstMonth(Year, e.HireDate, GoLiveIndex, Openings.TryGetValue(e.Id, out FnLeaveOpening o) ? o.Month : (int?)null);

        /// <summary>Loads the plan; <c>Error</c> is a Persian message naming the missing key when the rate table lacks the entitlement or the carry-forward limit.</summary>
        public static async Task<(LeaveAccrualPlan Plan, string Error)> LoadAsync(IUnitOfWork uow, int year, IReadOnlyCollection<int> employeeIds, CancellationToken ct)
        {
            PayrollRates rates = await PayrollRateLoader.LoadAsync(uow, year, ct);
            string error = rates.Require(year, PayrollKeys.LeaveEntitlement, PayrollKeys.LeaveCarryMax);
            if (error != null) return (null, error);
            var plan = new LeaveAccrualPlan { Year = year, MonthlyRate = rates.Get(PayrollKeys.LeaveEntitlement), CarryMax = rates.Get(PayrollKeys.LeaveCarryMax) };
            plan.GoLiveIndex = await PayrollGoLive.ResolveAsync(uow, ct);
            List<int> ids = employeeIds.ToList();
            List<FnLeaveOpening> openings = await uow.Set<FnLeaveOpening>().AsNoTracking()
                .Where(o => o.Year == year && o.Status == FnChangeStatus.Approved && ids.Contains(o.EmployeeId)).OrderBy(o => o.Id).ToListAsync(ct);
            foreach (FnLeaveOpening o in openings) plan.Openings[o.EmployeeId] = o; // the latest approved opening of the employee wins
            return (plan, null);
        }
    }

    /// <summary>The wage effective at a month: the approved wage history, else the cached current daily wage of the employee.</summary>
    internal static class WageHistory
    {
        /// <summary>The first day of the Persian month of a date: the date a wage change is effective from.</summary>
        public static DateTime MonthStart(DateTime date) => PersianCal.Start(PersianCal.Year(date), PersianCal.Month(date));

        /// <summary>
        /// The daily wage in force at <paramref name="monthStart"/>: the latest history row not after it (a month before the first row uses the first row); the cached wage when the employee has no history yet.
        /// <paramref name="fromHistory"/> tells which of the two it was.
        /// </summary>
        public static decimal WageAt(IReadOnlyCollection<FnEmployeeWage> rows, decimal cachedDailyWage, DateTime monthStart, out bool fromHistory)
        {
            if (rows == null || rows.Count == 0) { fromHistory = false; return cachedDailyWage; }
            fromHistory = true;
            FnEmployeeWage pick = rows.Where(r => r.EffectiveFrom.Date <= monthStart.Date).OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id).FirstOrDefault()
                ?? rows.OrderBy(r => r.EffectiveFrom).ThenBy(r => r.Id).First();
            return pick.DailyWage;
        }

        public static async Task<Dictionary<int, List<FnEmployeeWage>>> LoadAsync(IUnitOfWork uow, IEnumerable<int> employeeIds, CancellationToken ct)
        {
            List<int> ids = employeeIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, List<FnEmployeeWage>>();
            List<FnEmployeeWage> rows = await uow.Set<FnEmployeeWage>().AsNoTracking().Where(w => ids.Contains(w.EmployeeId)).ToListAsync(ct);
            return rows.GroupBy(w => w.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        }
    }

    /// <summary>
    /// The protected-group rule of the payroll posting: the pay of fewer than three payroll-rated employees of one cost centre and work order must not be derivable from a work-order line,
    /// so such a group is posted at the cost-centre level only. The rule is self-contained: it does not depend on any production or after-sales costing code.
    /// </summary>
    internal static class PayrollProtection
    {
        /// <summary>A group of fewer distinct payroll-rated employees than this is protected.</summary>
        public const int MinEmployees = 3;

        public sealed class Part
        {
            public int EmployeeId;
            public string CostCenter;
            public int WorkOrderId;
            public decimal Amount;
        }

        /// <summary>The (cost centre, work order) groups of one month with fewer than three distinct payroll-rated employees.</summary>
        public static HashSet<(string CostCenter, int WorkOrderId)> ProtectedGroups(IEnumerable<Part> parts)
        {
            var result = new HashSet<(string, int)>();
            foreach (var g in parts.GroupBy(p => (p.CostCenter ?? "", p.WorkOrderId)))
            {
                int employees = g.Select(p => p.EmployeeId).Distinct().Count();
                if (employees < MinEmployees) result.Add(g.Key);
            }
            return result;
        }
    }

    /// <summary>
    /// The attendance numbers of a sheet (no money): the shape of a correction request, of the frozen snapshot of a month close and of the checks that a typed sheet, a correction and the HR close all pass.
    /// </summary>
    internal static class SheetNumbers
    {
        public static SheetNumbersDto Of(FnAttendanceSheet s) => new SheetNumbersDto
        {
            DaysWorked = s.DaysWorked, OvertimeHours = s.OvertimeHours, NightHours = s.NightHours, HolidayHours = s.HolidayHours, ShiftType = s.ShiftType,
            PaidLeaveDays = s.PaidLeaveDays, SickLeaveDays = s.SickLeaveDays, UnpaidLeaveDays = s.UnpaidLeaveDays, AbsenceDays = s.AbsenceDays, MissionDays = s.MissionDays,
        };

        public static void Apply(SheetNumbersDto n, FnAttendanceSheet s)
        {
            s.DaysWorked = n.DaysWorked; s.OvertimeHours = n.OvertimeHours; s.NightHours = n.NightHours; s.HolidayHours = n.HolidayHours; s.ShiftType = n.ShiftType;
            s.PaidLeaveDays = n.PaidLeaveDays; s.SickLeaveDays = n.SickLeaveDays; s.UnpaidLeaveDays = n.UnpaidLeaveDays; s.AbsenceDays = n.AbsenceDays; s.MissionDays = n.MissionDays;
        }

        public static string ToJson(SheetNumbersDto n) => JsonSerializer.Serialize(n);

        public static SheetNumbersDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new SheetNumbersDto();
            try { return JsonSerializer.Deserialize<SheetNumbersDto>(json) ?? new SheetNumbersDto(); }
            catch (JsonException) { return new SheetNumbersDto(); }
        }

        private static decimal Q(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public static bool Same(SheetNumbersDto a, SheetNumbersDto b) =>
            Q(a.DaysWorked) == Q(b.DaysWorked) && Q(a.OvertimeHours) == Q(b.OvertimeHours) && Q(a.NightHours) == Q(b.NightHours) && Q(a.HolidayHours) == Q(b.HolidayHours) && a.ShiftType == b.ShiftType
            && Q(a.PaidLeaveDays) == Q(b.PaidLeaveDays) && Q(a.SickLeaveDays) == Q(b.SickLeaveDays) && Q(a.UnpaidLeaveDays) == Q(b.UnpaidLeaveDays) && Q(a.AbsenceDays) == Q(b.AbsenceDays)
            && Q(a.MissionDays) == Q(b.MissionDays);

        private static string F(decimal v) => Q(v).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The canonical JSON of the sheets of a month (employee order, two decimals): the same numbers always give the same text, so its hash can be compared.</summary>
        public static string Snapshot(IEnumerable<KeyValuePair<int, SheetNumbersDto>> rows)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (KeyValuePair<int, SheetNumbersDto> kv in rows.OrderBy(r => r.Key))
            {
                SheetNumbersDto n = kv.Value;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"e\":").Append(kv.Key).Append(",\"d\":").Append(F(n.DaysWorked)).Append(",\"ot\":").Append(F(n.OvertimeHours)).Append(",\"n\":").Append(F(n.NightHours))
                    .Append(",\"h\":").Append(F(n.HolidayHours)).Append(",\"st\":").Append(n.ShiftType).Append(",\"pl\":").Append(F(n.PaidLeaveDays)).Append(",\"sl\":").Append(F(n.SickLeaveDays))
                    .Append(",\"ul\":").Append(F(n.UnpaidLeaveDays)).Append(",\"a\":").Append(F(n.AbsenceDays)).Append(",\"m\":").Append(F(n.MissionDays)).Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// A Persian message when the numbers cannot belong to the month, else null. <paramref name="overtimeCapHours"/> is the monthly cap of the attendance policy:
        /// the overtime and the holiday work sit in one bucket and share it.
        /// </summary>
        public static string Validate(SheetNumbersDto n, int year, int month, decimal overtimeCapHours)
        {
            if (n == null) return "ارقام کارکرد ارسال نشده است.";
            if (n.DaysWorked < 0 || n.DaysWorked > 31 || n.PaidLeaveDays < 0 || n.SickLeaveDays < 0 || n.MissionDays < 0 || n.UnpaidLeaveDays < 0 || n.AbsenceDays < 0
                || n.DaysWorked + n.PaidLeaveDays + n.SickLeaveDays + n.MissionDays > 31)
                return "روزهای کارکرد نامعتبر است (حداکثر ۳۱ روز).";
            int monthDays = PersianCal.DaysIn(year, month);
            if (n.DaysWorked + n.UnpaidLeaveDays + n.AbsenceDays > monthDays)
                return $"روزهای کارکرد و غیبت از تعداد روزهای این ماه ({monthDays} روز) بیشتر است.";
            if (n.OvertimeHours < 0 || n.NightHours < 0 || n.HolidayHours < 0 || n.NightHours > monthDays * 24m) return "ساعات کارکرد نامعتبر است.";
            if (n.OvertimeHours + n.HolidayHours > overtimeCapHours)
                return $"اضافه‌کار و کار در روز تعطیل روی هم از سقف ماهانه سیاست حضور و غیاب ({overtimeCapHours.ToString("0.##", CultureInfo.InvariantCulture)} ساعت) بیشتر است.";
            if (n.ShiftType < 0 || n.ShiftType > 3) return "نوع نوبت‌کاری نامعتبر است.";
            return null;
        }
    }

    /// <summary>The HR month close as the payroll sees it: payroll is calculated and approved only for a month whose close was prepared by HR and confirmed by a second person.</summary>
    internal static class HrClose
    {
        public static Task<OfAttendanceMonth> GetAsync(IUnitOfWork uow, int year, int month, CancellationToken ct) =>
            uow.Set<OfAttendanceMonth>().AsNoTracking().FirstOrDefaultAsync(m => m.Year == year && m.Month == month, ct);

        /// <summary>Null when the month is confirmed; otherwise the Persian refusal that tells what is missing.</summary>
        public static string Refusal(int year, int month, OfAttendanceMonth row)
        {
            string label = $"{year}/{month:00}";
            if (row == null) return $"حضور و غیاب ماه {label} هنوز توسط منابع انسانی بسته نشده است؛ ابتدا بستن ماه (آماده‌سازی) و سپس تایید آن لازم است.";
            if (row.Status == OfMonthStatus.Prepared) return $"بستن حضور و غیاب ماه {label} آماده شده اما هنوز تایید نشده است؛ پس از تایید مدیر مالی می‌توان حقوق را محاسبه کرد.";
            if (row.Status == OfMonthStatus.Rejected) return $"بستن حضور و غیاب ماه {label} رد شده است؛ منابع انسانی باید آن را دوباره آماده کند.";
            return null;
        }

        public static async Task<string> RefusalAsync(IUnitOfWork uow, int year, int month, CancellationToken ct) => Refusal(year, month, await GetAsync(uow, year, month, ct));
    }

    /// <summary>SHA-256 helpers for the frozen month snapshot and the payload hashes.</summary>
    internal static class PayrollHash
    {
        public static string Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
    }
}
