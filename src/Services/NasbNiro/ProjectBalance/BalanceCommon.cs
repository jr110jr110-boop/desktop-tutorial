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
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectBalance
{
    /// <summary>Roles of the project material balance. Reads need a signed-in user; every change names its roles.</summary>
    public static class BalanceRoles
    {
        /// <summary>Runs the balance, makes the draft purchase requests, edits and asks to release reservations, sets the settings.</summary>
        public const string Planning = "PMO,PLANNING";

        /// <summary>Proposes a substitute; approves it (never the proposer).</summary>
        public const string SubstituteProposers = "PMO,PLANNING,ENGINEERING,ENGINEER";
        public const string SubstituteApprovers = "ENGINEERING,PMO";
    }

    public static class BalanceMessages
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string NotFound = "پیدا نشد.";
        public const string AiNeverApproves = "تایید و آزادسازی با هوش مصنوعی انجام نمی‌شود؛ یک کاربر انسانی باید آن را انجام دهد.";
        public const string SameUser = "درخواست‌کننده یا ویرایش‌کننده خودش تایید نمی‌کند؛ تایید باید با فرد دیگری باشد.";
        public const string NoBaseline = "برای این پروژه هنوز از بانک محصول BOM گرفته نشده است؛ ابتدا «گرفتن از بانک» را بزنید.";
        public const string Concurrent = "همزمان کس دیگری همین رکورد را تغییر داد؛ صفحه را دوباره بارگذاری کنید.";
        public const string ReasonRequired = "دلیل تغییر را بنویسید.";
    }

    /// <summary>The settings of the balance (★ provisional company decisions). They are kept in the PbSettings table with the "Balance:" prefix.</summary>
    public static class BalanceSettingKeys
    {
        public const string WarehouseKinds = "Balance:WarehouseKinds";
        public const string ReservationHard = "Balance:ReservationHard";
        public const string ReserveOnRun = "Balance:ReserveOnRun";
        public const string UseSubstitutes = "Balance:UseSubstitutes";
        public const string LeadMetalDays = "Balance:LeadTimeMetalDays";
        public const string LeadConsumableDays = "Balance:LeadTimeConsumableDays";
        public const string LeadDefaultDays = "Balance:LeadTimeDefaultDays";
        public const string MetalGroups = "Balance:MetalGroupCodes";
        public const string ConsumableGroups = "Balance:ConsumableGroupCodes";
        public const string AutoRunOnChange = "Balance:AutoRunOnChange";
        public const string AutoDraftRequests = "Balance:AutoDraftRequests";
        public const string NightlyEnabled = "Balance:NightlyEnabled";
        public const string KitJobEnabled = "Balance:KitJobEnabled";
        public const string KitDaysAhead = "Balance:KitDaysAhead";
        public const string RequestGrouping = "Balance:RequestGrouping";
        public const string FloatUrgentDays = "Balance:FloatUrgentDays";
        public const string FloatHighDays = "Balance:FloatHighDays";
        public const string LinkDocuments = "Balance:LinkDocuments";
        public const string ProcuredPercent = "Balance:ProcuredPercentMethod";

        public static bool IsBool(string v) => v == "true" || v == "false";

        public static IEnumerable<PbSetting> Defaults()
        {
            PbSetting S(string key, string value, string title, string source) => new PbSetting { Key = key, Value = value, Title = title, Source = source };
            const string assumed = "★ تصمیم برنامه (به تایید شرکت)";
            const string answers = "★ جدول پاسخ‌های شرکت (به تایید شرکت)";
            yield return S(WarehouseKinds, "0,1,2,3,4", "انواع انباری که موجودی آن در بالانس شمرده می‌شود (۰ عمومی، ۱ مواد اولیه، ۲ قطعات، ۳ در جریان ساخت، ۴ کالای ساخته‌شده؛ قرنطینه، ضایعات، پیمانکار، اموال مشتری و مدل‌ها شمرده نمی‌شود)", assumed);
            yield return S(ReservationHard, "true", "رزرو موجودی برای پروژه «سخت» باشد (حواله به پروژه دیگر تا آزادسازی تاییدشده رد می‌شود)؛ false = رزرو نرم (فقط در برنامه‌ریزی کم می‌شود)", "★ پیش‌فرض سخت؛ پاسخ شرکت در انتظار است");
            yield return S(ReserveOnRun, "true", "اجرای بالانس موجودی آزاد را برای پروژه رزرو کند", assumed);
            yield return S(UseSubstitutes, "true", "اگر کالای اصلی کم است، جایگزین‌های تاییدشده در بالانس استفاده شود", assumed);
            yield return S(LeadMetalDays, "30", "زمان تامین فلز و مواد اولیه (روز)", answers);
            yield return S(LeadConsumableDays, "7", "زمان تامین اقلام مصرفی (روز)", answers);
            yield return S(LeadDefaultDays, "30", "زمان تامین سایر اقلام (روز)", assumed);
            yield return S(MetalGroups, "01", "کد گروه کالاهای فلزی و مواد اولیه (دو رقم اول کد)", assumed);
            yield return S(ConsumableGroups, "05", "کد گروه کالاهای مصرفی (دو رقم اول کد)", assumed);
            yield return S(AutoRunOnChange, "true", "پس از پایه‌ریزی پروژه، انحراف BOM تاییدشده و ساخت WBS، بالانس خودکار اجرا شود", assumed);
            yield return S(AutoDraftRequests, "true", "اجرای خودکار بالانس، پیش‌نویس درخواست خرید کمبودها را هم بسازد یا به‌روز کند (هرگز ارسال نمی‌کند)", assumed);
            yield return S(NightlyEnabled, "true", "کار شبانه بالانس همه پروژه‌های جاری (فقط وقتی کارهای زمان‌بندی‌شده روشن است)", assumed);
            yield return S(KitJobEnabled, "true", "کار شبانه پیش‌نویس درخواست کیت پای‌کار فعالیت‌های هفتهٔ آینده", assumed);
            yield return S(KitDaysAhead, "7", "کیت پای‌کار فعالیت‌هایی که تا این تعداد روز دیگر شروع می‌شوند", answers);
            yield return S(RequestGrouping, "ItemGroup", "گروه‌بندی پیش‌نویس درخواست خرید: ItemGroup (گروه کالا)، LeadClass (طبقه تامین: فلز / مصرفی / سایر) یا None (یک درخواست)", assumed);
            yield return S(FloatUrgentDays, "0", "شناوری فعالیت تدارک تا این روز = فوری", assumed);
            yield return S(FloatHighDays, "10", "شناوری فعالیت تدارک تا این روز = زیاد", assumed);
            yield return S(LinkDocuments, "true", "مدارک فنی BOM و نقشه قطعات به درخواست خرید پیوند شود", assumed);
            yield return S(ProcuredPercent, "true", "فعالیت‌های تدارک (P) در WBS ساخته‌شده از بانک با روش پیشرفت «درصد تامین‌شده» ساخته شوند", assumed);
        }

        public static async Task<BalanceSettings> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, string> values = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<PbSetting> rows = await uow.Set<PbSetting>().AsNoTracking().Where(s => s.Key.StartsWith("Balance:")).ToListAsync(ct);
            foreach (PbSetting row in rows) if (row.Value != null) values[row.Key] = row.Value;
            return BalanceSettings.From(values);
        }

        public static async Task<Dictionary<string, string>> LoadRawAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, string> values = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<PbSetting> rows = await uow.Set<PbSetting>().AsNoTracking().Where(s => s.Key.StartsWith("Balance:")).ToListAsync(ct);
            foreach (PbSetting row in rows) if (row.Value != null) values[row.Key] = row.Value;
            return values;
        }

        /// <summary>The Persian refusal for a value the setting cannot take, or null.</summary>
        public static string Validate(string key, string value)
        {
            switch (key)
            {
                case ReservationHard: case ReserveOnRun: case UseSubstitutes: case AutoRunOnChange: case AutoDraftRequests: case NightlyEnabled: case KitJobEnabled: case LinkDocuments: case ProcuredPercent:
                    return IsBool(value) ? null : "مقدار این تنظیم فقط true یا false است.";
                case LeadMetalDays: case LeadConsumableDays: case LeadDefaultDays: case KitDaysAhead:
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d >= 0 && d <= 365 ? null : "عددی بین ۰ و ۳۶۵ روز وارد کنید.";
                case FloatUrgentDays: case FloatHighDays:
                    return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int f) && f >= -365 && f <= 365 ? null : "عددی بین ۳۶۵- و ۳۶۵ روز وارد کنید.";
                case WarehouseKinds:
                    return !string.IsNullOrWhiteSpace(value) && value.Split(',').All(x => int.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int k) && Enum.IsDefined(typeof(Entities.Model.NasbNiro.Inventory.WarehouseKind), k) && k != (int)Entities.Model.NasbNiro.Inventory.WarehouseKind.Quarantine)
                        ? null : "فهرست عددهای نوع انبار را با ویرگول بنویسید؛ قرنطینه هرگز شمرده نمی‌شود.";
                case MetalGroups: case ConsumableGroups:
                    return !string.IsNullOrWhiteSpace(value) && value.Split(',').All(x => x.Trim().Length == 2 && x.Trim().All(char.IsDigit)) ? null : "کدهای دو رقمی گروه را با ویرگول بنویسید.";
                case RequestGrouping:
                    return value == "ItemGroup" || value == "LeadClass" || value == "None" ? null : "یکی از ItemGroup، LeadClass یا None.";
                default:
                    return "تنظیم پیدا نشد.";
            }
        }
    }

    /// <summary>The typed settings of one run.</summary>
    public sealed class BalanceSettings
    {
        public HashSet<int> WarehouseKinds { get; private set; } = new HashSet<int> { 0, 1, 2, 3, 4 };
        public bool ReservationHard { get; private set; } = true;
        public bool ReserveOnRun { get; private set; } = true;
        public bool UseSubstitutes { get; private set; } = true;
        public int LeadMetalDays { get; private set; } = 30;
        public int LeadConsumableDays { get; private set; } = 7;
        public int LeadDefaultDays { get; private set; } = 30;
        public HashSet<string> MetalGroups { get; private set; } = new HashSet<string> { "01" };
        public HashSet<string> ConsumableGroups { get; private set; } = new HashSet<string> { "05" };
        public bool AutoRunOnChange { get; private set; } = true;
        public bool AutoDraftRequests { get; private set; } = true;
        public bool NightlyEnabled { get; private set; } = true;
        public bool KitJobEnabled { get; private set; } = true;
        public int KitDaysAhead { get; private set; } = 7;
        public string RequestGrouping { get; private set; } = "ItemGroup";
        public int FloatUrgentDays { get; private set; }
        public int FloatHighDays { get; private set; } = 10;
        public bool LinkDocuments { get; private set; } = true;
        public bool ProcuredPercent { get; private set; } = true;

        public static BalanceSettings From(Dictionary<string, string> v)
        {
            string Get(string key) => v.TryGetValue(key, out string x) ? x : null;
            bool B(string key, bool d) => Get(key) is string s && (s == "true" || s == "false") ? s == "true" : d;
            int I(string key, int d) => int.TryParse(Get(key), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : d;
            HashSet<string> G(string key, string d) => new HashSet<string>((Get(key) ?? d).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            var s2 = new BalanceSettings
            {
                ReservationHard = B(BalanceSettingKeys.ReservationHard, true),
                ReserveOnRun = B(BalanceSettingKeys.ReserveOnRun, true),
                UseSubstitutes = B(BalanceSettingKeys.UseSubstitutes, true),
                LeadMetalDays = I(BalanceSettingKeys.LeadMetalDays, 30),
                LeadConsumableDays = I(BalanceSettingKeys.LeadConsumableDays, 7),
                LeadDefaultDays = I(BalanceSettingKeys.LeadDefaultDays, 30),
                MetalGroups = G(BalanceSettingKeys.MetalGroups, "01"),
                ConsumableGroups = G(BalanceSettingKeys.ConsumableGroups, "05"),
                AutoRunOnChange = B(BalanceSettingKeys.AutoRunOnChange, true),
                AutoDraftRequests = B(BalanceSettingKeys.AutoDraftRequests, true),
                NightlyEnabled = B(BalanceSettingKeys.NightlyEnabled, true),
                KitJobEnabled = B(BalanceSettingKeys.KitJobEnabled, true),
                KitDaysAhead = I(BalanceSettingKeys.KitDaysAhead, 7),
                RequestGrouping = Get(BalanceSettingKeys.RequestGrouping) is string g && (g == "ItemGroup" || g == "LeadClass" || g == "None") ? g : "ItemGroup",
                FloatUrgentDays = I(BalanceSettingKeys.FloatUrgentDays, 0),
                FloatHighDays = I(BalanceSettingKeys.FloatHighDays, 10),
                LinkDocuments = B(BalanceSettingKeys.LinkDocuments, true),
                ProcuredPercent = B(BalanceSettingKeys.ProcuredPercent, true),
            };
            if (Get(BalanceSettingKeys.WarehouseKinds) is string kinds)
            {
                var set = new HashSet<int>(kinds.Split(',').Select(x => int.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int k) ? k : -1).Where(k => k >= 0 && k != 5));
                if (set.Count > 0) s2.WarehouseKinds = set;
            }
            return s2;
        }

        /// <summary>The settings as they were for a run (stored with the run).</summary>
        public string ToJson() => JsonSerializer.Serialize(new
        {
            warehouseKinds = WarehouseKinds.OrderBy(x => x).ToArray(), reservationHard = ReservationHard, reserveOnRun = ReserveOnRun, useSubstitutes = UseSubstitutes,
            leadMetalDays = LeadMetalDays, leadConsumableDays = LeadConsumableDays, leadDefaultDays = LeadDefaultDays, metalGroups = MetalGroups.OrderBy(x => x).ToArray(),
            consumableGroups = ConsumableGroups.OrderBy(x => x).ToArray(), requestGrouping = RequestGrouping, floatUrgentDays = FloatUrgentDays, floatHighDays = FloatHighDays,
        });

        /// <summary>Lead time of an item by the first two characters of its group code.</summary>
        public int LeadDays(string groupCode)
        {
            string g = groupCode != null && groupCode.Length >= 2 ? groupCode.Substring(0, 2) : null;
            if (g != null && ConsumableGroups.Contains(g)) return LeadConsumableDays;
            if (g != null && MetalGroups.Contains(g)) return LeadMetalDays;
            return LeadDefaultDays;
        }

        /// <summary>"Metal", "Consumable" or "Other": the supplier class of an item.</summary>
        public string LeadClass(string groupCode)
        {
            string g = groupCode != null && groupCode.Length >= 2 ? groupCode.Substring(0, 2) : null;
            if (g != null && ConsumableGroups.Contains(g)) return "Consumable";
            if (g != null && MetalGroups.Contains(g)) return "Metal";
            return "Other";
        }

        /// <summary>The group of the draft purchase request an item belongs to.</summary>
        public string GroupKey(string groupCode)
        {
            switch (RequestGrouping)
            {
                case "None": return "ALL";
                case "LeadClass": return LeadClass(groupCode);
                default: return groupCode != null && groupCode.Length >= 2 ? groupCode.Substring(0, 2) : "00";
            }
        }
    }

    /// <summary>One row of demand of one item (a part of the project that needs it).</summary>
    public sealed class DemandRow
    {
        public decimal Required { get; set; }
        public DateTime? UseBy { get; set; }
    }

    /// <summary>One expected delivery of an open purchase order line.</summary>
    public sealed class PoArrival
    {
        public DateTime? Date { get; set; }
        public decimal Quantity { get; set; }
    }

    /// <summary>What covers one row of demand.</summary>
    public sealed class RowAllocation
    {
        public decimal Issued { get; set; }
        public decimal ReservedExisting { get; set; }
        public decimal ReservedNew { get; set; }

        /// <summary>Covered by free stock (equals <see cref="ReservedNew"/> when the run reserves).</summary>
        public decimal FromFree { get; set; }
        public decimal Pr { get; set; }
        public decimal Po { get; set; }
        public decimal Short { get; set; }
        public decimal Reserved => ReservedExisting + ReservedNew;
    }

    /// <summary>The arithmetic of the balance, kept apart from the database so it can be tested to the digit.</summary>
    public static class BalanceRules
    {
        public static decimal Round(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

        /// <summary>The scrap factor of a BOM line: the entered one, else gross / net - 1 from the weights, else zero.</summary>
        public static decimal ScrapFactor(decimal? entered, decimal? net, decimal? gross)
        {
            if (entered.HasValue && entered.Value > 0) return entered.Value;
            if (net.HasValue && gross.HasValue && net.Value > 0 && gross.Value > net.Value) return gross.Value / net.Value - 1m;
            return 0m;
        }

        /// <summary>Gross requirement (before scrap) and the scrap quantity of a base quantity.</summary>
        public static (decimal Gross, decimal Scrap) Requirement(decimal baseQty, decimal factor)
        {
            decimal gross = Round(baseQty);
            return (gross, Round(baseQty * Math.Max(0m, factor)));
        }

        public static bool IsWeightUnit(string unit)
        {
            string u = (unit ?? "").Trim().ToLowerInvariant();
            return u == "kg" || u == "کیلوگرم" || u == "کیلو" || u == "کگ";
        }

        /// <summary>0 low, 1 normal, 2 high, 3 urgent.</summary>
        public static int Priority(int? totalFloat, bool critical, int urgentDays, int highDays)
        {
            if (critical) return 3;
            if (!totalFloat.HasValue) return 1;
            if (totalFloat.Value <= urgentDays) return 3;
            if (totalFloat.Value <= highDays) return 2;
            return totalFloat.Value <= Math.Max(highDays, 1) * 3 ? 1 : 0;
        }

        /// <summary>
        /// Covers the rows of one item, earliest first: what has been issued to the project, then the stock reserved for the project, then free stock (reserved now when the run reserves), then the open purchase requests, then the
        /// purchase order lines that arrive by the day the row is used. What is left is the shortage. The pools are shared by the rows in order, so the part that is needed first is covered first.
        /// </summary>
        public static List<RowAllocation> Allocate(IReadOnlyList<DemandRow> rows, decimal issued, decimal reservedExisting, decimal free, bool reserveNew, decimal prQty, IEnumerable<PoArrival> pos)
        {
            decimal issuedLeft = Math.Max(0m, issued), reservedLeft = Math.Max(0m, reservedExisting), freeLeft = Math.Max(0m, free), prLeft = Math.Max(0m, prQty);
            List<PoArrival> arrivals = (pos ?? Enumerable.Empty<PoArrival>()).Where(p => p.Quantity > 0).OrderBy(p => p.Date ?? DateTime.MinValue).Select(p => new PoArrival { Date = p.Date, Quantity = p.Quantity }).ToList();
            var result = new List<RowAllocation>();
            foreach (DemandRow row in rows)
            {
                var a = new RowAllocation();
                decimal need = Math.Max(0m, row.Required);
                decimal take = Math.Min(need, issuedLeft); a.Issued = take; issuedLeft -= take; need -= take;
                take = Math.Min(need, reservedLeft); a.ReservedExisting = take; reservedLeft -= take; need -= take;
                take = Math.Min(need, freeLeft); a.FromFree = take; if (reserveNew) a.ReservedNew = take; freeLeft -= take; need -= take;
                take = Math.Min(need, prLeft); a.Pr = take; prLeft -= take; need -= take;
                foreach (PoArrival p in arrivals)
                {
                    if (need <= 0) break;
                    if (p.Quantity <= 0) continue;
                    if (row.UseBy.HasValue && p.Date.HasValue && p.Date.Value.Date > row.UseBy.Value.Date) continue;
                    decimal t = Math.Min(need, p.Quantity);
                    a.Po += t; p.Quantity -= t; need -= t;
                }
                a.Short = Round(need);
                result.Add(a);
            }
            return result;
        }

        /// <summary>A shortage of the item covered by a substitute: how much of the shortage (item units) and how many substitute units.</summary>
        public static (decimal Covered, decimal SubstituteUnits) Substitute(decimal shortage, decimal ratio, decimal substituteFree)
        {
            if (shortage <= 0 || ratio <= 0 || substituteFree <= 0) return (0m, 0m);
            decimal covered = Math.Min(shortage, Round(substituteFree / ratio));
            decimal units = Math.Min(Round(covered * ratio), substituteFree);
            return (covered, units);
        }

        public static string Hash(IEnumerable<string> parts)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", parts))).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
