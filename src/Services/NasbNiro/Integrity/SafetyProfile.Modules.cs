using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.OpsData;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.Integrity
{
    // Fix round (H4, M4): the keys that the modules own and the profile only lists. Kept in its own file so that a later package adds its keys with one more block.
    public static partial class SafetyProfile
    {
        /// <summary>The AI seats that only draft or narrate (a brief, a weekly report, a reorder proposal, a comparison, an NCR draft, an office draft): one PMO or CEO user may switch them OFF at once with a reason.</summary>
        public static readonly IReadOnlyList<string> DraftingSeats = new[] { "CEO-BRIEF", "PMO-WEEKLY", "WH-REORDER", "PROC-COMPARE", "QC-NCR-ASSIST", "OFFICE-DRAFT" };

        /// <summary>★ A seat switched off at once must be confirmed by a second person within this time, else the CEO is told.</summary>
        public static readonly TimeSpan SeatOffConfirmWithin = TimeSpan.FromHours(24);

        /// <summary>★ A request nobody decided is closed after this time.</summary>
        public static readonly TimeSpan RequestExpiresAfter = TimeSpan.FromHours(72);

        /// <summary>True for a drafting seat. A seat that is itself a control (OPS-DATA, FIN-ANOMALY, the future internal auditor) and any seat not listed is not.</summary>
        public static bool IsDraftingSeat(string code) => DraftingSeats.Contains((code ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

        private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static IEnumerable<SafetySettingSpec> ModuleSpecs()
        {
            const SafetyStore Pr = SafetyStore.Procurement, Fn = SafetyStore.Finance, Q = SafetyStore.Quality;
            var list = new List<SafetySettingSpec>();

            // procurement: the amounts above which the controls apply; a higher amount means fewer purchases are controlled
            void Amount(string key, decimal strict, string title) => list.Add(new SafetySettingSpec { Store = Pr, Key = key, Kind = SafetyKind.NumberAtMost, Strict = Num(strict), Min = 0, Max = 1_000_000_000_000m, Title = title });
            Amount("MediumPurchaseFrom", 10_000_000m, "حد خرید متوسط (تایید مدیرعامل) از (ریال)");
            Amount("TwoQuotesFrom", 30_000_000m, "حد الزام دو استعلام از (ریال)");
            Amount("ThreeQuotesFrom", 50_000_000m, "حد الزام سه استعلام از (ریال)");
            Amount("PettyCashLimit", 50_000_000m, "سقف خرید تنخواه بدون سفارش خرید (ریال)");
            Amount("ContractRequiredFrom", 150_000_000m, "حد الزام قرارداد کتبی از (ریال)");
            Amount("CommitteeRequiredFrom", 150_000_000m, "حد الزام کمیته انتخاب تامین‌کننده از (ریال)");

            // finance: who takes the second step of a period close
            list.Add(new SafetySettingSpec { Store = Fn, Key = "PeriodClose.CloseBy", Kind = SafetyKind.TextMustEqual, Strict = "CEO", Allowed = new[] { "CEO", "FINANCEMANAGER" }, Title = "بستن نهایی دوره قفل‌شده را مدیرعامل انجام دهد" });

            // sales: the ceiling of a proposal the sales department approves by itself (no strict value is known: any change needs two persons, and a higher ceiling is the weaker one)
            list.Add(new SafetySettingSpec { Store = SafetyStore.Sales, Key = "ProposalApprovalThresholdRial", Kind = SafetyKind.NumberAtMost, Strict = null, Min = 0, Max = 1_000_000_000_000m, Title = "سقف پیشنهاد قیمتی که فروش خودش تایید می‌کند (ریال)", Note = "مقدار سخت‌گیرانه ثابت ندارد؛ هر تغییر دو نفر می‌خواهد." });

            // quality
            list.Add(new SafetySettingSpec { Store = Q, Key = QualitySettingKeys.MaxBackdateDays, Kind = SafetyKind.NumberAtMost, Strict = "7", Min = 0, Max = 30, Title = "حداکثر تاخیر ثبت بازرسی و گزارش (روز)" });
            list.Add(new SafetySettingSpec { Store = Q, Key = QualitySettingKeys.MajorNcrCommitteeMin, Kind = SafetyKind.NumberAtLeast, Strict = "3", Min = 2, Max = 10, Title = "حداقل اعضای کمیته برای عدم انطباق عمده" });
            list.Add(new SafetySettingSpec { Store = Q, Key = QualitySettingKeys.WeldContinuityMonths, Kind = SafetyKind.NumberAtMost, Strict = "6", Min = 1, Max = 12, Title = "حداکثر وقفه جوشکاری در یک فرایند (ماه)" });

            // PB8: the gates of the operational data control and of the engineering readiness (the lists are the module's own catalogs, so a new rule is covered at once)
            list.AddRange(OpsSpecs());
            return list;
        }

        private static SafetySettingSpec Ops(string key, SafetyKind kind, decimal strict, string title, string family, bool whole = true)
        {
            QualitySettingRules.TryGetBounds(key, out int min, out int max);
            return new SafetySettingSpec { Store = SafetyStore.Quality, Key = key, Kind = kind, Strict = Num(strict), Min = min, Max = max, Whole = whole, Title = title, Family = family };
        }

        private static IEnumerable<SafetySettingSpec> OpsSpecs()
        {
            // the master switches and the rule switches must stay on
            yield return Ops(OpsRuleCatalog.MasterSwitch, SafetyKind.FlagMustBeOn, 1, "کنترل داده عملیاتی: کلید کلی روشن بماند", "ops-switch");
            yield return Ops(OpsReadinessCatalog.MasterSwitch, SafetyKind.FlagMustBeOn, 1, "کنترل آمادگی اطلاعات مهندسی: کلید کلی روشن بماند", "ops-switch");
            foreach (OpsRuleDef r in OpsRuleCatalog.All)
            {
                string t = r.Code + ": ";
                yield return Ops(OpsRuleCatalog.Key(r.Code, "Enabled"), SafetyKind.FlagMustBeOn, 1, t + "قاعده فعال بماند", "ops-switch");
                yield return Ops(OpsRuleCatalog.Key(r.Code, "Warn"), SafetyKind.NumberAtMost, r.Warn, t + "آستانه هشدار از مقدار پیش‌فرض بالاتر نرود", "ops-threshold");
                // a block threshold: 0 ("never blocks") or a value above the default weakens the gate
                yield return Ops(OpsRuleCatalog.Key(r.Code, "Block"), SafetyKind.ThresholdZeroOff, r.Block, t + "آستانه مسدودسازی صفر یا بالاتر از مقدار پیش‌فرض نشود", "ops-threshold");
                if (r.MinHistory > 0) yield return Ops(OpsRuleCatalog.Key(r.Code, "MinHistory"), SafetyKind.NumberAtMost, r.MinHistory, t + "حداقل سابقه بالاتر از مقدار پیش‌فرض نرود", "ops-threshold");
                if (r.Floor > 0) yield return Ops(OpsRuleCatalog.Key(r.Code, "Floor"), SafetyKind.NumberAtMost, r.Floor, t + "کف پیام بالاتر از مقدار پیش‌فرض نرود", "ops-threshold");
            }
            yield return Ops(OpsRuleCatalog.SeatLookbackDays, SafetyKind.NumberAtLeast, 7, "صندلی OPS-DATA: بازبینی دست‌کم هفت روز اخیر", "ops-threshold");
            yield return Ops(OpsRuleCatalog.SeatRepeatCount, SafetyKind.NumberAtMost, 4, "صندلی OPS-DATA: تعداد تکرار از مقدار پیش‌فرض بالاتر نرود", "ops-threshold");
            yield return Ops(OpsRuleCatalog.SeatDayHours, SafetyKind.NumberAtMost, 16, "صندلی OPS-DATA: سقف ساعت روزانه از مقدار پیش‌فرض بالاتر نرود", "ops-threshold");
            yield return Ops(OpsRuleCatalog.ExceptionMargin, SafetyKind.NumberAtMost, 25, "حاشیه استثنای زمان از مقدار پیش‌فرض بالاتر نرود", "ops-threshold");
            foreach (ReadinessDef d in OpsReadinessCatalog.All)
            {
                yield return Ops(OpsReadinessCatalog.Key(d.Code, "Enabled"), SafetyKind.FlagMustBeOn, 1, d.Code + ": مورد آمادگی فعال بماند", "ops-switch");
                if (d.Hard) yield return Ops(OpsReadinessCatalog.Key(d.Code, "Hard"), SafetyKind.FlagMustBeOn, 1, d.Code + ": مورد آمادگی مسدودکننده بماند", "ops-switch");
            }
            yield return Ops(OpsReadinessCatalog.DueDays, SafetyKind.NumberAtMost, 7, "آمادگی اطلاعات مهندسی: مهلت تکمیل از مقدار پیش‌فرض بیشتر نشود", "ops-threshold");
            yield return Ops(OpsReadinessCatalog.EscalateDays, SafetyKind.NumberAtMost, 3, "آمادگی اطلاعات مهندسی: مهلت ارجاع به مدیرعامل از مقدار پیش‌فرض بیشتر نشود", "ops-threshold");
        }
    }
}
