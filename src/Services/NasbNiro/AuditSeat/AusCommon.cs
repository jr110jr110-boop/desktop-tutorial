using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.AuditSeat
{
    /// <summary>Roles of the AI internal auditor (package 9). The AUDITOR role is read-only everywhere else; here it may start a run and enter the findings of its own process audits, and nothing more.</summary>
    public static class AusRoles
    {
        public const string Auditor = "AUDITOR";
        public const string Qa = "QA";
        public const string Pmo = "PMO";
        public const string Ceo = "CEO";

        /// <summary>Everybody who reads the findings, the reports and the catalogue.</summary>
        public const string Readers = "AUDITOR,QA,PMO,CEO,PLANNING,PROJECTMANAGER,QC,ENGINEERING,FINANCEMANAGER,PRODUCTIONMANAGER,COMMERCIAL,DCC";

        /// <summary>A run on demand and the process audits.</summary>
        public const string Runners = "AUDITOR,QA,PMO,CEO";

        /// <summary>The roles that propose an action for a finding (the owner role of the rule decides the rest).</summary>
        public const string Actioners = "AUDITOR,QA,PMO,CEO,PLANNING,PROJECTMANAGER,QC,ENGINEERING,ENGINEER,FINANCEMANAGER,FINANCIAL,PRODUCTIONMANAGER,SHOPSUPERVISOR,COMMERCIAL,DCC,WAREHOUSE,WHSUPERVISOR,CONSTRUCTION,MAINTENANCE";

        /// <summary>The three who close a finding, in this order.</summary>
        public static readonly string[] ClosureChain = { Qa, Pmo, Ceo };
    }

    /// <summary>The processes the catalogue covers (the units the programme audits) with the role that answers for each.</summary>
    public sealed class AusProcess
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string OwnerRole { get; set; }
        public string OwnerUnit { get; set; }
    }

    public static class AusProcesses
    {
        public static readonly IReadOnlyList<AusProcess> All = new List<AusProcess>
        {
            new AusProcess { Code = "INTEGRITY", Title = "یکپارچگی سوابق و امنیت اطلاعات", OwnerRole = "PMO", OwnerUnit = "مدیریت پروژه و فناوری" },
            new AusProcess { Code = "GOVERNANCE", Title = "کنترل دو نفره و استثناها", OwnerRole = "PMO", OwnerUnit = "مدیریت پروژه" },
            new AusProcess { Code = "QUALITY", Title = "کنترل کیفیت، عدم انطباق و اقدام اصلاحی", OwnerRole = "QA", OwnerUnit = "تضمین کیفیت" },
            new AusProcess { Code = "CALIBRATION", Title = "تجهیزات اندازه‌گیری و کالیبراسیون", OwnerRole = "QC", OwnerUnit = "کنترل کیفیت" },
            new AusProcess { Code = "PERSONNEL", Title = "صلاحیت و آموزش پرسنل", OwnerRole = "QA", OwnerUnit = "تضمین کیفیت" },
            new AusProcess { Code = "SPECIAL", Title = "فرایندهای ویژه، جوشکاری و گواهی مواد", OwnerRole = "QC", OwnerUnit = "کنترل کیفیت" },
            new AusProcess { Code = "DESIGN", Title = "کنترل طراحی و پیکربندی", OwnerRole = "ENGINEERING", OwnerUnit = "مهندسی" },
            new AusProcess { Code = "CHANGE", Title = "مدیریت تغییرات مهندسی", OwnerRole = "ENGINEERING", OwnerUnit = "مهندسی" },
            new AusProcess { Code = "DOCUMENT", Title = "کنترل مدارک و نقشه‌ها", OwnerRole = "DCC", OwnerUnit = "کنترل مدارک" },
            new AusProcess { Code = "PRODUCTION", Title = "تولید و بازرسی در فرایند", OwnerRole = "PRODUCTIONMANAGER", OwnerUnit = "تولید" },
            new AusProcess { Code = "PROCUREMENT", Title = "خرید و ارزیابی تامین‌کننده", OwnerRole = "COMMERCIAL", OwnerUnit = "بازرگانی" },
            new AusProcess { Code = "PROJECT", Title = "کنترل پروژه (مبنا، EVM، ریسک)", OwnerRole = "PMO", OwnerUnit = "مدیریت پروژه" },
            new AusProcess { Code = "FINANCE", Title = "واسط مالی و سپیدار", OwnerRole = "FINANCEMANAGER", OwnerUnit = "مالی" },
            new AusProcess { Code = "AI", Title = "هوش مصنوعی و صندلی‌ها", OwnerRole = "PMO", OwnerUnit = "مدیریت پروژه و فناوری" },
            new AusProcess { Code = "AUDIT", Title = "برنامه ممیزی و بازنگری مدیریت", OwnerRole = "QA", OwnerUnit = "تضمین کیفیت" },
        };

        public static AusProcess Find(string code) => All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        public static string TitleOf(string code) => Find(code)?.Title ?? code;
    }

    /// <summary>
    /// The settings of the audit (rows of QualitySetting, lazily seeded; every one is a security-relevant switch: it changes only by the two-person route of the strict profile).
    /// The ★ defaults are strict: zero tolerance unless a standard or a company procedure names a threshold.
    /// </summary>
    public static class AusSettingKeys
    {
        public const string Prefix = "Audit.";
        public const string Master = "Audit.Enabled";
        public const string DueMajor = "Audit.Due.Major";
        public const string DueMinor = "Audit.Due.Minor";
        public const string DueObservation = "Audit.Due.Observation";
        public const string RecurrenceWindow = "Audit.RecurrenceWindowDays";
        public const string WeeklyDay = "Audit.Report.WeeklyDay";
        public const string MonthlyDay = "Audit.Report.MonthlyDay";
        public const string RuleBudgetSeconds = "Audit.RuleBudgetSeconds";
        public const string MaxHits = "Audit.MaxHitsPerRule";
        public const string FullPassDays = "Audit.FullPassDays";

        // thresholds of rules (a standard or a procedure names none: the ★ value is the company's reading)
        public const string RestoreTestDays = "Audit.T.RestoreTestDays";
        public const string ChainRunHours = "Audit.T.ChainRunHours";
        public const string EvmStaleDays = "Audit.T.EvmStaleDays";
        public const string RiskStaleDays = "Audit.T.RiskStaleDays";
        public const string SupplierEvalDays = "Audit.T.SupplierEvaluationDays";
        public const string ManagementReviewDays = "Audit.T.ManagementReviewDays";
        public const string GateExceptionMax = "Audit.T.GateExceptionMax";
        public const string GateExceptionWindow = "Audit.T.GateExceptionWindowDays";
        public const string BackupStaleDays = "Audit.T.BackupStaleDays";
        public const string ExceptionPendingDays = "Audit.T.ExceptionPendingDays";
        public const string LegacyBomFreezeDay = "Audit.T.LegacyBomFreezeDay";

        public static string RuleKey(string code) => Prefix + "Rule." + code + ".Enabled";

        private sealed class Def
        {
            public string Key; public decimal Value; public int Min; public int Max; public string Title; public string Source;
        }

        private const string SrcD = "★ تصمیم مدیرعامل D11/D12 (سخت‌گیری صفر)";
        private const string SrcIso = "★ پیشنهاد PMIS بر پایه ISO 19011 / ISO 9001";

        private static readonly List<Def> Table = new List<Def>
        {
            new Def { Key = Master, Value = 1, Min = 0, Max = 1, Title = "ممیز داخلی هوشمند: کلید کلی (۱=روشن، ۰=خاموش)", Source = SrcD },
            new Def { Key = DueMajor, Value = 7, Min = 1, Max = 365, Title = "مهلت اقدام برای یافته عمده (روز)", Source = SrcD },
            new Def { Key = DueMinor, Value = 30, Min = 1, Max = 365, Title = "مهلت اقدام برای یافته جزئی (روز)", Source = SrcD },
            new Def { Key = DueObservation, Value = 60, Min = 1, Max = 365, Title = "مهلت اقدام برای مشاهده (روز)", Source = SrcD },
            new Def { Key = RecurrenceWindow, Value = 90, Min = 1, Max = 1000, Title = "پنجره تکرار در همان فرایند: یافته تکراری شدیدتر می‌شود (روز)", Source = SrcD },
            new Def { Key = WeeklyDay, Value = 5, Min = 0, Max = 6, Title = "روز گزارش هفتگی (۰=شنبه ... ۶=جمعه)", Source = SrcD },
            new Def { Key = MonthlyDay, Value = 1, Min = 1, Max = 28, Title = "روز ماه شمسی برای گزارش ماهانه مدیرعامل", Source = SrcD },
            new Def { Key = RuleBudgetSeconds, Value = 60, Min = 5, Max = 600, Title = "بودجه زمانی هر قاعده در یک اجرا (ثانیه)", Source = SrcIso },
            new Def { Key = MaxHits, Value = 2000, Min = 100, Max = 20000, Title = "سقف یافته هر قاعده در یک اجرا (فراتر از آن یافته‌ی «اجرای ناقص» ثبت می‌شود)", Source = SrcIso },
            new Def { Key = FullPassDays, Value = 7, Min = 1, Max = 31, Title = "هر چند روز یک بار قاعده‌های رویدادی همه سابقه را مرور کنند", Source = SrcIso },
            new Def { Key = RestoreTestDays, Value = 35, Min = 1, Max = 365, Title = "حداکثر سن آزمون بازیابی پشتیبان (روز)", Source = "تصمیم بسته D (۳۵ روز)" },
            new Def { Key = ChainRunHours, Value = 36, Min = 6, Max = 720, Title = "حداکثر فاصله دو راستی‌آزمایی زنجیره ممیزی (ساعت)", Source = "تصمیم بسته D (اجرای شبانه)" },
            new Def { Key = EvmStaleDays, Value = 7, Min = 1, Max = 90, Title = "حداکثر کهنگی تاریخ داده EVM پروژه فعال (روز)", Source = "EIA-748 GL22 (ماهانه) سخت‌گیرانه تر: هفتگی ★" },
            new Def { Key = RiskStaleDays, Value = 30, Min = 1, Max = 365, Title = "حداکثر کهنگی مرور ثبت ریسک پروژه فعال (روز)", Source = "ISO 21502 7.11 ★" },
            new Def { Key = SupplierEvalDays, Value = 365, Min = 30, Max = 1100, Title = "حداکثر فاصله ارزیابی دوره‌ای تامین‌کننده فعال (روز)", Source = "WI-PO-003 ★ سالانه" },
            new Def { Key = ManagementReviewDays, Value = 365, Min = 30, Max = 1100, Title = "حداکثر فاصله دو بازنگری مدیریت (روز)", Source = "ISO 9001 9.3 ★ سالانه" },
            new Def { Key = GateExceptionMax, Value = 3, Min = 1, Max = 100, Title = "بیشینه استثنای تاییدشده یک قاعده کنترل داده در پنجره (بیش از آن یافته)", Source = SrcIso },
            new Def { Key = GateExceptionWindow, Value = 30, Min = 1, Max = 365, Title = "پنجره شمارش استثناهای کنترل داده (روز)", Source = SrcIso },
            new Def { Key = BackupStaleDays, Value = 2, Min = 1, Max = 30, Title = "حداکثر سن پشتیبان مورد استفاده آزمون بازیابی هنگام آزمون (روز)", Source = "ISO 27001 A.8.13 ★" },
            new Def { Key = ExceptionPendingDays, Value = 3, Min = 1, Max = 60, Title = "حداکثر انتظار درخواست استثنا برای تصمیم (روز)", Source = SrcIso },
            new Def { Key = LegacyBomFreezeDay, Value = 0, Min = 0, Max = 99999999, Title = "روز انجماد BOM قدیمی (عدد شمسی yyyyMMdd؛ صفر = روز نخستین اجرای ممیزی)", Source = "برنامه بسته A2" },
        };

        /// <summary>Every key of the audit that the strict profile lists, with the kind of change that weakens it.</summary>
        public static IEnumerable<string> SwitchKeys => new[] { Master }.Concat(AusCatalog.All.Select(r => RuleKey(r.Code)));

        public static IEnumerable<QualitySetting> Defaults()
        {
            foreach (Def d in Table) yield return new QualitySetting { Key = d.Key, Value = d.Value, Title = d.Title, Source = d.Source };
            foreach (AusRuleDef r in AusCatalog.All)
                yield return new QualitySetting { Key = RuleKey(r.Code), Value = 1, Title = r.Code + ": قاعده ممیزی فعال بماند", Source = SrcD };
        }

        public static decimal DefaultOf(string key) => Table.FirstOrDefault(d => d.Key == key)?.Value ?? 0m;

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            Def d = Table.FirstOrDefault(x => x.Key == key);
            if (d != null) { min = d.Min; max = d.Max; return true; }
            if (key.StartsWith(Prefix + "Rule.", StringComparison.Ordinal) && key.EndsWith(".Enabled", StringComparison.Ordinal))
            {
                string code = key.Substring((Prefix + "Rule.").Length, key.Length - (Prefix + "Rule.").Length - ".Enabled".Length);
                if (AusCatalog.Find(code) != null) { min = 0; max = 1; return true; }
            }
            return false;
        }

        public static IEnumerable<(string Key, decimal Strict, int Min, int Max, string Title, string Kind)> Strict()
        {
            yield return (Master, 1, 0, 1, "ممیز داخلی هوشمند: کلید کلی روشن بماند", "on");
            yield return (DueMajor, 7, 1, 365, "مهلت یافته عمده از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (DueMinor, 30, 1, 365, "مهلت یافته جزئی از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (DueObservation, 60, 1, 365, "مهلت مشاهده از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (RecurrenceWindow, 90, 1, 1000, "پنجره تکرار کوتاه‌تر از مقدار پیش‌فرض نشود", "atleast");
            yield return (WeeklyDay, 5, 0, 6, "روز گزارش هفتگی دیرتر از مقدار پیش‌فرض نشود", "atmost");
            yield return (MonthlyDay, 1, 1, 28, "روز گزارش ماهانه دیرتر از مقدار پیش‌فرض نشود", "atmost");
            yield return (RuleBudgetSeconds, 60, 5, 600, "بودجه زمانی قاعده کوتاه‌تر از مقدار پیش‌فرض نشود", "atleast");
            yield return (MaxHits, 2000, 100, 20000, "سقف یافته هر قاعده کمتر از مقدار پیش‌فرض نشود", "atleast");
            yield return (FullPassDays, 7, 1, 31, "فاصله مرور کامل از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (RestoreTestDays, 35, 1, 365, "سن مجاز آزمون بازیابی از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (ChainRunHours, 36, 6, 720, "فاصله مجاز راستی‌آزمایی زنجیره از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (EvmStaleDays, 7, 1, 90, "کهنگی مجاز تاریخ داده EVM از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (RiskStaleDays, 30, 1, 365, "کهنگی مجاز ثبت ریسک از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (SupplierEvalDays, 365, 30, 1100, "فاصله ارزیابی تامین‌کننده از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (ManagementReviewDays, 365, 30, 1100, "فاصله بازنگری مدیریت از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (GateExceptionMax, 3, 1, 100, "سقف استثنای تاییدشده از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (GateExceptionWindow, 30, 1, 365, "پنجره شمارش استثنا کوتاه‌تر از مقدار پیش‌فرض نشود", "atleast");
            yield return (BackupStaleDays, 2, 1, 30, "سن مجاز پشتیبان از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (ExceptionPendingDays, 3, 1, 60, "انتظار مجاز درخواست استثنا از مقدار پیش‌فرض بیشتر نشود", "atmost");
            yield return (LegacyBomFreezeDay, 0, 0, 99999999, "روز انجماد BOM قدیمی جابه‌جا نشود (صفر = روز نخستین اجرای ممیزی)", "atmost");
        }
    }

    /// <summary>The pure rules of findings: the severity ladder, the due date, the number.</summary>
    public static class AusRules
    {
        public static AusSeverity Raise(AusSeverity s) => s >= AusSeverity.Major ? AusSeverity.Major : (AusSeverity)((int)s + 1);

        public static string SeverityText(AusSeverity s) => s == AusSeverity.Major ? "عمده" : s == AusSeverity.Minor ? "جزئی" : "مشاهده";

        public static string StatusText(AusFindingStatus s)
        {
            switch (s)
            {
                case AusFindingStatus.Open: return "باز - منتظر اقدام مسئول";
                case AusFindingStatus.ActionProposed: return "اقدام پیشنهاد شد - منتظر بازبینی خودکار";
                case AusFindingStatus.Rechecked: return "بازبینی خودکار قبول - منتظر پیشنهاد QA";
                case AusFindingStatus.QaProposed: return "پیشنهاد بستن QA - منتظر PMO";
                case AusFindingStatus.PmoProposed: return "پیشنهاد بستن PMO - منتظر تایید مدیرعامل";
                case AusFindingStatus.Closed: return "بسته";
                case AusFindingStatus.Voided: return "ابطال شده";
                default: return s.ToString();
            }
        }

        public static bool IsTerminal(AusFindingStatus s) => s == AusFindingStatus.Closed || s == AusFindingStatus.Voided;

        public static int DueDays(AusSeverity s, IReadOnlyDictionary<string, decimal> settings)
        {
            string key = s == AusSeverity.Major ? AusSettingKeys.DueMajor : s == AusSeverity.Minor ? AusSettingKeys.DueMinor : AusSettingKeys.DueObservation;
            decimal v = settings != null && settings.TryGetValue(key, out decimal x) ? x : AusSettingKeys.DefaultOf(key);
            return (int)Math.Max(1, v);
        }

        public static DateTime DueDate(DateTime detected, AusSeverity s, IReadOnlyDictionary<string, decimal> settings) => detected.Date.AddDays(DueDays(s, settings));

        public static string Number(int persianYear, int sequence) => "AF-" + persianYear.ToString("0000", CultureInfo.InvariantCulture) + "-" + sequence.ToString("0000", CultureInfo.InvariantCulture);

        public static int PersianYear(DateTime d) => new PersianCalendar().GetYear(d);

        /// <summary>The identity of a deviation: the same object and sub key give the same key.</summary>
        public static string ObjectKey(string type, int id, string sub)
        {
            string k = type + "|" + id.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(sub) ? "" : "|" + sub);
            return k.Length <= 200 ? k : k.Substring(0, 150) + "#" + AusEvidence.Sha256Hex(k).Substring(0, 16);
        }

        /// <summary>
        /// Who may not close (or approve the void of) a finding at a step: the owner, the person who proposed the action, the people who already signed an earlier step, and an AI seat.
        /// Returns the Persian reason or null.
        /// </summary>
        public static string ClosureRefusal(int userId, bool isAi, AusFinding f, string step)
        {
            if (isAi) return "هوش مصنوعی هیچ یافته‌ای را نمی‌بندد و تایید نمی‌کند؛ یک کاربر انسانی باید این کار را بکند.";
            if (f.OwnerUserId == userId) return "مسئول یافته نمی‌تواند خودش آن را ببندد یا ابطال کند.";
            if (f.ActionByUserId == userId) return "کسی که اقدام اصلاحی را پیشنهاد داده است نمی‌تواند آن را ببندد یا ابطال کند.";
            if (step == "pmo" && f.QaUserId == userId) return "پیشنهاد بستن QA و PMO باید از دو نفر متفاوت باشد.";
            if (step == "ceo" && (f.QaUserId == userId || f.PmoUserId == userId)) return "تایید نهایی مدیرعامل باید از نفر سوم (غیر از QA و PMO) باشد.";
            if (step == "void-pmo" && f.VoidQaUserId == userId) return "ابطال باید توسط سه نفر متفاوت QA، PMO و مدیرعامل تایید شود.";
            if (step == "void-ceo" && (f.VoidQaUserId == userId || f.VoidPmoUserId == userId)) return "ابطال باید توسط سه نفر متفاوت QA، PMO و مدیرعامل تایید شود.";
            return null;
        }
    }
}
