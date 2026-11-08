using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Entities.Model.NasbNiro.Integrity;

namespace Services.NasbNiro.Integrity
{
    /// <summary>How the strict value of a setting is compared with another value.</summary>
    public enum SafetyKind
    {
        /// <summary>A number 0/1 (1 = the control is on): weaker when lower than the strict value.</summary>
        FlagMustBeOn = 1,

        /// <summary>A number 0/1 (0 = off is strict, for example SMS and e-mail gateways): weaker when higher.</summary>
        FlagMustBeOff = 2,

        /// <summary>The text true / false (strict is "true" or "false").</summary>
        BoolText = 3,

        /// <summary>A number that must not be raised above the strict value (tolerances, ceilings).</summary>
        NumberAtMost = 4,

        /// <summary>A number that must not be lowered below the strict value (minimum counts and days).</summary>
        NumberAtLeast = 5,

        /// <summary>
        /// A threshold where 0 means "never" (a block threshold): weaker when it is 0 although the strict value is above 0, or when it is above the strict value. A strict value of 0 (the rule only warns) has nothing weaker.
        /// </summary>
        ThresholdZeroOff = 6,

        /// <summary>A text that must equal the strict text (for example the role of the second step of a period close); the allowed texts are listed in <see cref="SafetySettingSpec.Allowed"/>.</summary>
        TextMustEqual = 7,
    }

    /// <summary>One security-relevant setting (or, with a * in the key, a family of them). A change of it needs a second person.</summary>
    public sealed class SafetySettingSpec
    {
        public SafetyStore Store { get; set; }

        /// <summary>The key in its table; "*" matches any characters (for example <c>OpsData.*.Enabled</c>).</summary>
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>The value of the strict production profile, as text.</summary>
        public string Strict { get; set; }
        public SafetyKind Kind { get; set; }

        /// <summary>Allowed values for a number (the range check of the change request).</summary>
        public decimal Min { get; set; }
        public decimal Max { get; set; } = 1_000_000_000m;
        public bool Whole { get; set; } = true;

        /// <summary>For a text kind: the values a change may name.</summary>
        public string[] Allowed { get; set; }

        /// <summary>The key family a setting belongs to, for the checks of its own module (for example "ops-rule").</summary>
        public string Family { get; set; }

        /// <summary>A Persian note shown beside the deviation, for example the transition period.</summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// Package D: the strict production profile («پروفایل سخت‌گیرانه تولید»). A registry that every module can add its own security-relevant setting keys to
    /// (<see cref="Register"/>; a family of keys uses * in the key). A setting listed here is changed only by a request with a reason that a second person (PMO or CEO, not the requester, never an AI seat) approves;
    /// the save endpoints of the modules call <see cref="Refusal"/> and refuse a direct change. Defaults are not changed by the profile: it only shows the strict value and the deviation.
    /// </summary>
    public static partial class SafetyProfile
    {
        private static readonly object Gate = new object();
        private static readonly List<SafetySettingSpec> Specs = new List<SafetySettingSpec>();

        public const string RefusalText = "این تنظیم امنیتی است و مستقیم تغییر نمی‌کند؛ از صفحه «پروفایل سخت‌گیرانه تولید» درخواست تغییر با ذکر دلیل ثبت کنید تا نفر دوم (PMO یا مدیرعامل) تایید کند.";

        static SafetyProfile()
        {
            const SafetyStore Pc = SafetyStore.ProjectControl, Pb = SafetyStore.ProductBank, Pr = SafetyStore.Procurement, Fn = SafetyStore.Finance;
            Register(new[]
            {
                new SafetySettingSpec { Store = Pb, Key = "BomSignatures:DistinctReviewers", Kind = SafetyKind.BoolText, Strict = "true", Title = "امضاکنندگان BOM چهار نفر متفاوت باشند (تصمیم D13)" },
                new SafetySettingSpec { Store = Pb, Key = "DemoData:Enabled", Kind = SafetyKind.BoolText, Strict = "false", Title = "نصب داده‌های نمونه در نصب تولید خاموش باشد" },
                new SafetySettingSpec { Store = Pc, Key = "EdmsRequireRevision", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "ترنسمیتال باید به بازنگری ثبت‌شده ارجاع دهد", Note = "در دوره گذار ۰ است تا اسناد قدیمی ثبت شوند" },
                new SafetySettingSpec { Store = Pc, Key = "EdmsReviewRequired", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "بازنگری سند پیش از تایید توسط نفر دوم" },
                new SafetySettingSpec { Store = Pc, Key = "NotifySmsEnabled", Kind = SafetyKind.FlagMustBeOff, Strict = "0", Min = 0, Max = 1, Title = "پیامک از طریق درگاه بیرونی خاموش باشد (کارکرد مستقل از اینترنت)" },
                new SafetySettingSpec { Store = Pc, Key = "NotifyEmailEnabled", Kind = SafetyKind.FlagMustBeOff, Strict = "0", Min = 0, Max = 1, Title = "ایمیل از طریق سرور بیرونی خاموش باشد (کارکرد مستقل از اینترنت)" },
                new SafetySettingSpec { Store = Pr, Key = "EnforcePurchaseOrder", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "الزام سفارش خرید برای رسید خرید" },
                new SafetySettingSpec { Store = Pr, Key = "InquiryPlanningRequired", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "تایید بودجه و زمان تحویل استعلام توسط برنامه‌ریزی" },
                new SafetySettingSpec { Store = Pr, Key = "ReceiptTolerancePercent", Kind = SafetyKind.NumberAtMost, Strict = "2", Min = 0, Max = 100, Whole = false, Title = "رواداری مقدار دریافت نسبت به سفارش (درصد)" },
                new SafetySettingSpec { Store = Pr, Key = "InvoicePriceTolerancePercent", Kind = SafetyKind.NumberAtMost, Strict = "1", Min = 0, Max = 100, Whole = false, Title = "رواداری قیمت فاکتور نسبت به سفارش (درصد)" },
                new SafetySettingSpec { Store = Fn, Key = "CloseChecklist.Enforce", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "الزام فهرست کنترل بستن دوره مالی" },
                new SafetySettingSpec { Store = SafetyStore.AiSeat, Key = "*", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "صندلی هوش مصنوعی فعال بماند", Note = "روشن یا خاموش کردن هر صندلی تصمیم است" },
            });
            Register(ModuleSpecs());
            Register(EngChangeSpecs());
        }

        /// <summary>Adds the specs of a module (once; a spec with the same store and key replaces the earlier one). Package 8 adds its gate keys with one call.</summary>
        public static void Register(IEnumerable<SafetySettingSpec> specs)
        {
            if (specs == null) return;
            lock (Gate)
                foreach (SafetySettingSpec spec in specs.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)))
                {
                    Specs.RemoveAll(x => x.Store == spec.Store && x.Key == spec.Key);
                    Specs.Add(spec);
                }
        }

        /// <summary>A module that has a plain list of key names registers them with the same strict value and kind.</summary>
        public static void RegisterKeys(SafetyStore store, IEnumerable<string> keys, SafetyKind kind, string strict, string titlePrefix = null, decimal min = 0, decimal max = 1)
        {
            if (keys == null) return;
            Register(keys.Select(k => new SafetySettingSpec { Store = store, Key = k, Kind = kind, Strict = strict, Min = min, Max = max, Title = (titlePrefix ?? "") + k }));
        }

        public static IReadOnlyList<SafetySettingSpec> All()
        {
            lock (Gate) return Specs.ToList();
        }

        private static bool Matches(string pattern, string key) =>
            pattern.Contains('*') ? Regex.IsMatch(key, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);

        /// <summary>The spec of a setting (an exact key wins over a pattern), or null when the setting is not security-relevant.</summary>
        public static SafetySettingSpec Find(SafetyStore store, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (Gate)
                return Specs.Where(s => s.Store == store && !s.Key.Contains('*') && Matches(s.Key, key)).FirstOrDefault()
                       ?? Specs.Where(s => s.Store == store && s.Key.Contains('*') && Matches(s.Key, key)).FirstOrDefault();
        }

        /// <summary>For the save endpoints of the modules: the Persian refusal when the key is security-relevant, else null.</summary>
        public static string Refusal(SafetyStore store, string key) => Find(store, key) == null ? null : RefusalText;

        private static bool TryNumber(string v, out decimal n) => decimal.TryParse((v ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out n);

        /// <summary>True when the value differs from the strict one in the direction that weakens the control.</summary>
        public static bool IsWeaker(SafetySettingSpec spec, string value)
        {
            if (spec == null) return false;
            switch (spec.Kind)
            {
                case SafetyKind.BoolText: return !string.Equals((value ?? "").Trim(), spec.Strict, StringComparison.OrdinalIgnoreCase);
                case SafetyKind.FlagMustBeOn: return TryNumber(value, out decimal on) && TryNumber(spec.Strict, out decimal sOn) && on < sOn;
                case SafetyKind.FlagMustBeOff: return TryNumber(value, out decimal off) && TryNumber(spec.Strict, out decimal sOff) && off > sOff;
                case SafetyKind.NumberAtMost: return TryNumber(value, out decimal hi) && TryNumber(spec.Strict, out decimal sHi) && hi > sHi;
                case SafetyKind.NumberAtLeast: return TryNumber(value, out decimal lo) && TryNumber(spec.Strict, out decimal sLo) && lo < sLo;
                case SafetyKind.ThresholdZeroOff: return TryNumber(value, out decimal th) && TryNumber(spec.Strict, out decimal sTh) && sTh > 0 && (th == 0 || th > sTh);
                case SafetyKind.TextMustEqual: return spec.Strict != null && !string.Equals((value ?? "").Trim(), spec.Strict, StringComparison.OrdinalIgnoreCase);
                default: return false;
            }
        }

        /// <summary>Normalises and checks a proposed value; returns the text to store, or null and a Persian error.</summary>
        public static string Normalize(SafetySettingSpec spec, string value, out string error)
        {
            error = null;
            string v = (value ?? "").Trim();
            if (spec.Kind == SafetyKind.TextMustEqual)
            {
                string t = v.ToUpperInvariant();
                if (spec.Allowed == null || !spec.Allowed.Contains(t, StringComparer.OrdinalIgnoreCase)) { error = "مقدار باید یکی از این‌ها باشد: " + string.Join("، ", spec.Allowed ?? new string[0]); return null; }
                return t;
            }
            if (spec.Kind == SafetyKind.BoolText)
            {
                v = v.ToLowerInvariant();
                if (v != "true" && v != "false") { error = "مقدار این تنظیم فقط true یا false است."; return null; }
                return v;
            }
            if (!TryNumber(v, out decimal n)) { error = "مقدار باید عدد باشد."; return null; }
            if (n < spec.Min || n > spec.Max || (spec.Whole && n != decimal.Truncate(n))) { error = "مقدار باید بین " + spec.Min.ToString("0.####", CultureInfo.InvariantCulture) + " و " + spec.Max.ToString("0.####", CultureInfo.InvariantCulture) + (spec.Whole ? " و عدد صحیح" : "") + " باشد."; return null; }
            return n.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>True when two stored values are the same value (numbers compare as numbers).</summary>
        public static bool SameValue(string a, string b) =>
            TryNumber(a, out decimal x) && TryNumber(b, out decimal y) ? x == y : string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
