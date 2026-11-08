using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Quality;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.OpsData
{
    // PB8: the plausibility rules and the baseline-conformance rules. Every rule is a PURE function over numbers already read from the database
    // (the evaluator reads them in batches), so the arithmetic is unit tested and no rule can touch the data. Deterministic: no language model.

    public static class OpsRuleCodes
    {
        // statistical plausibility
        public const string RcvQty = "R-RCV-QTY";
        public const string RcvWeight = "R-RCV-WEIGHT";
        public const string RcvPrice = "R-RCV-PRICE";
        public const string IssQty = "R-ISS-QTY";
        public const string IssCeil = "R-ISS-CEIL";
        public const string PrdTime = "R-PRD-TIME";
        public const string PrdScrap = "R-PRD-SCRAP";
        public const string QcReject = "R-QC-REJECT";
        public const string CntVar = "R-CNT-VAR";
        public const string Uom = "R-UOM";
        public const string Date = "R-DATE";
        public const string DateFwd = "R-DATE-FWD";
        public const string InvPrice = "R-INV-PRICE";

        // baseline conformance (CEO addition)
        public const string BlBomItem = "R-BL-BOM-ITEM";
        public const string BlBomQty = "R-BL-BOM-QTY";
        public const string BlOpcStage = "R-BL-OPC-STAGE";
        public const string BlOpcOp = "R-BL-OPC-OP";
        public const string BlItp = "R-BL-ITP";
        public const string BlSpec = "R-BL-SPEC";
        public const string BlWbs = "R-BL-WBS";

        // found only by the daily seat (cross-document)
        public const string SeatLotOverdraw = "S-LOT-OVERDRAW";
        public const string SeatCapacity = "S-CAPACITY";
        public const string SeatRepeat = "S-REPEAT";
    }

    /// <summary>Kinds of rule: plausibility compares with statistics, baseline compares with the approved BOM / OPC / ITP / WBS.</summary>
    public static class OpsRuleFamily
    {
        public const string Plausibility = "plausibility";
        public const string Baseline = "baseline";
        public const string Seat = "seat";
    }

    public sealed class OpsRuleDef
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Family { get; set; }
        public OpsUnit Unit { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>Threshold for a warning (percent, days or hours: see <see cref="Basis"/>).</summary>
        public decimal Warn { get; set; }

        /// <summary>Threshold for a block; 0 = the rule never blocks (it only warns).</summary>
        public decimal Block { get; set; }

        /// <summary>Fewest earlier records the statistic needs (0 = not statistical).</summary>
        public decimal MinHistory { get; set; }

        /// <summary>A floor under which the rule says nothing (rial, or minutes for the production time); 0 = none.</summary>
        public decimal Floor { get; set; }

        public string Basis { get; set; }

        /// <summary>The rule can block by its nature (a baseline mismatch); false = only the thresholds decide.</summary>
        public bool Binary { get; set; }
    }

    public sealed class OpsRuleSetting
    {
        public bool Enabled { get; set; } = true;
        public decimal Warn { get; set; }
        public decimal Block { get; set; }
        public decimal MinHistory { get; set; }
        public decimal Floor { get; set; }
    }

    public static class OpsRuleCatalog
    {
        public const string KeyPrefix = "OpsData.";
        public const string SeatLookbackDays = "OpsData.SeatLookbackDays";
        public const string SeatRepeatCount = "OpsData.SeatRepeatCount";
        public const string SeatDayHours = "OpsData.SeatDayHours";
        public const string MasterSwitch = "OpsData.Enabled";

        // ★ every default is an assumption the company confirms; the thresholds are loose enough for normal work and tight enough for a typo of a digit
        public static readonly IReadOnlyList<OpsRuleDef> All = new List<OpsRuleDef>
        {
            new OpsRuleDef { Code = OpsRuleCodes.RcvQty, Title = "مقدار رسید در برابر مانده سفارش خرید", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 5, Block = 0, Basis = "درصد بیشتر از مانده سفارش (سقف سخت رواداری سفارش خرید برقرار است)" },
            new OpsRuleDef { Code = OpsRuleCodes.RcvWeight, Title = "وزن رسید در برابر وزن واحد × مقدار و گواهی ذوب", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 10, Block = 100, Basis = "درصد اختلاف وزن" },
            new OpsRuleDef { Code = OpsRuleCodes.RcvPrice, Title = "فی رسید در برابر سه رسید آخر و میانگین موزون", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 50, Block = 300, MinHistory = 2, Basis = "درصد اختلاف (۱۰ برابر = ۹۰۰٪؛ یک‌دهم نیز ۹۰۰٪)" },
            new OpsRuleDef { Code = OpsRuleCodes.IssQty, Title = "مقدار حواله در برابر نیاز استاندارد (کیت BOM)", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 10, Block = 50, Basis = "درصد بیشتر از نیاز استاندارد" },
            new OpsRuleDef { Code = OpsRuleCodes.IssCeil, Title = "مصرف تجمعی در برابر سقف مصرف", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 0, Block = 25, Basis = "درصد بیشتر از سقف" },
            new OpsRuleDef { Code = OpsRuleCodes.PrdTime, Title = "زمان ثبت‌شده عملیات در برابر زمان استاندارد مسیر ساخت", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Production, Warn = 100, Block = 400, Floor = 30, Basis = "درصد بیشتر از استاندارد (۵ برابر = ۴۰۰٪)؛ کف مرجع به دقیقه" },
            new OpsRuleDef { Code = OpsRuleCodes.PrdScrap, Title = "ضایعات عملیات در برابر سابقه همان عملیات", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Production, Warn = 200, Block = 900, MinHistory = 3, Basis = "درصد بیشتر از ضایعات مورد انتظار" },
            new OpsRuleDef { Code = OpsRuleCodes.QcReject, Title = "مقدار رد بازرسی در برابر سابقه کالا و تامین‌کننده", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Quality, Warn = 200, Block = 0, MinHistory = 3, Basis = "درصد بیشتر از رد مورد انتظار (فقط هشدار: ردِ واقعی نباید معطل بماند)" },
            new OpsRuleDef { Code = OpsRuleCodes.CntVar, Title = "اختلاف شمارش با موجودی دفتری", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 10, Block = 50, Floor = 5_000_000m, Basis = "درصد اختلاف؛ کف ارزش اختلاف (ریال)" },
            new OpsRuleDef { Code = OpsRuleCodes.Uom, Title = "شک به اشتباه واحد (هزار برابر یا یک‌هزارم)", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 5, Block = 1, Basis = "رواداری درصد نسبت به ضریب ۱۰۰۰؛ مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.Date, Title = "تاریخ سند خیلی قبل از امروز", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 30, Block = 1095, Basis = "روز قبل از امروز" },
            new OpsRuleDef { Code = OpsRuleCodes.DateFwd, Title = "تاریخ سند در آینده", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Warehouse, Warn = 7, Block = 365, Basis = "روز بعد از امروز" },
            new OpsRuleDef { Code = OpsRuleCodes.InvPrice, Title = "فی فاکتور تامین‌کننده در برابر فی سفارش", Family = OpsRuleFamily.Plausibility, Unit = OpsUnit.Finance, Warn = 10, Block = 100, Basis = "درصد اختلاف (رواداری ۳ طرفه فاکتور همچنان برقرار است)" },

            new OpsRuleDef { Code = OpsRuleCodes.BlBomItem, Title = "کالا در BOM خط‌مبنای پروژه نیست", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Warehouse, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.BlBomQty, Title = "مقدار تجمعی بیش از مقدار BOM", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Warehouse, Warn = 0, Block = 5, Basis = "درصد رواداری بیش از مقدار BOM: درون آن هشدار، بیرون آن مسدود" },
            new OpsRuleDef { Code = OpsRuleCodes.BlOpcStage, Title = "مرحله OPC قطعه با مسیر ساخت نمی‌خواند", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Production, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.BlOpcOp, Title = "عملیات خارج از ترتیب یا مرکز کار مسیر ساخت", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Production, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.BlItp, Title = "نتیجه بازرسی نقاط الزامی ITP را نمی‌پوشاند", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Quality, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.BlSpec, Title = "مشخصات فنی / مدرک با خط‌مبنا نمی‌خواند", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Warehouse, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
            new OpsRuleDef { Code = OpsRuleCodes.BlWbs, Title = "فعالیت/WBS سند در خط‌مبنا نیست یا بسته است", Family = OpsRuleFamily.Baseline, Unit = OpsUnit.Warehouse, Block = 1, Binary = true, Basis = "مسدودکننده=۱ فقط هشدار=۰" },
        };

        public static OpsRuleDef Find(string code) => All.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));

        public static string TitleOf(string code)
        {
            switch (code)
            {
                case OpsRuleCodes.SeatLotOverdraw: return "مصرف بیش از دریافت یک بچ در چند سند";
                case OpsRuleCodes.SeatCapacity: return "ساعت ثبت‌شده بیش از ظرفیت روز";
                case OpsRuleCodes.SeatRepeat: return "تکرار پیاپی یک مقدار";
                default: return Find(code)?.Title ?? code;
            }
        }

        public static string Key(string code, string part) => KeyPrefix + code + "." + part;

        /// <summary>The company settings of the rules (merged into the quality settings table, so they have the same page, audit and range checks).</summary>
        public static IEnumerable<QualitySetting> Defaults()
        {
            foreach (OpsRuleDef r in All)
            {
                string t = r.Code + ": ";
                yield return new QualitySetting { Key = Key(r.Code, "Enabled"), Value = r.Enabled ? 1 : 0, Title = t + "فعال (۱=بله، ۰=خیر)", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
                yield return new QualitySetting { Key = Key(r.Code, "Warn"), Value = r.Warn, Title = t + "آستانه هشدار (" + r.Basis + ")", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
                yield return new QualitySetting { Key = Key(r.Code, "Block"), Value = r.Block, Title = t + "آستانه مسدودسازی (۰ = هرگز مسدود نمی‌کند)", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
                if (r.MinHistory > 0)
                    yield return new QualitySetting { Key = Key(r.Code, "MinHistory"), Value = r.MinHistory, Title = t + "حداقل تعداد سابقه", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
                if (r.Floor > 0)
                    yield return new QualitySetting { Key = Key(r.Code, "Floor"), Value = r.Floor, Title = t + "کف (ریال یا دقیقه: زیر آن پیامی داده نمی‌شود)", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
            }
            yield return new QualitySetting { Key = MasterSwitch, Value = 1, Title = "کنترل داده عملیاتی: کلید کلی (۱=روشن، ۰=خاموش)", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
            yield return new QualitySetting { Key = SeatLookbackDays, Value = 7, Title = "صندلی OPS-DATA: بازبینی سندهای تأییدشده در چند روز اخیر", Source = "★ تصمیم مدیرعامل ۱۴۰۵/۰۸/۱۷" };
            yield return new QualitySetting { Key = SeatRepeatCount, Value = 4, Title = "صندلی OPS-DATA: تکرار یک مقدار در این تعداد سند یک کاربر", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
            foreach (QualitySetting rd in OpsReadinessCatalog.Defaults()) yield return rd;
            yield return new QualitySetting { Key = SeatDayHours, Value = 16, Title = "صندلی OPS-DATA: حداکثر ساعت ثبت‌شده یک نفر در یک روز", Source = "★ پیشنهاد PMIS (کنترل داده عملیاتی)" };
        }

        /// <summary>Allowed range of every setting (the quality settings page checks it).</summary>
        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            if (OpsReadinessCatalog.TryGetBounds(key, out min, out max)) return true;
            if (key == null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
            if (key == MasterSwitch) { min = 0; max = 1; return true; }
            if (key == SeatLookbackDays) { min = 1; max = 60; return true; }
            if (key == SeatRepeatCount) { min = 3; max = 50; return true; }
            if (key == SeatDayHours) { min = 8; max = 24; return true; }
            string part = key.Substring(key.LastIndexOf('.') + 1);
            string code = key.Substring(KeyPrefix.Length, key.Length - KeyPrefix.Length - part.Length - 1);
            if (Find(code) == null) return false;
            switch (part)
            {
                case "Enabled": min = 0; max = 1; return true;
                case "Warn": min = 0; max = 100000; return true;
                case "Block": min = 0; max = 100000; return true;
                case "MinHistory": min = 1; max = 50; return true;
                case "Floor": min = 0; max = 2_000_000_000; return true;
                default: return false;
            }
        }

        /// <summary>The relations between the thresholds: a block threshold above the warning one (a binary or tolerance rule excepted).</summary>
        public static string CheckRelations(IDictionary<string, decimal> values)
        {
            foreach (OpsRuleDef r in All)
            {
                if (r.Binary || r.Code == OpsRuleCodes.Uom || r.Code == OpsRuleCodes.BlBomQty) continue;
                if (!values.TryGetValue(Key(r.Code, "Warn"), out decimal w) || !values.TryGetValue(Key(r.Code, "Block"), out decimal b)) continue;
                if (b > 0 && b <= w) return $"{r.Code}: آستانه مسدودسازی باید بیشتر از آستانه هشدار باشد (یا ۰ برای «هرگز»).";
            }
            return null;
        }

        public static OpsRuleSetting SettingOf(IReadOnlyDictionary<string, decimal> values, string code)
        {
            OpsRuleDef d = Find(code);
            decimal Get(string part, decimal fallback) => values != null && values.TryGetValue(Key(code, part), out decimal v) ? v : fallback;
            bool master = values == null || !values.TryGetValue(MasterSwitch, out decimal m) || m != 0;
            return new OpsRuleSetting
            {
                Enabled = master && Get("Enabled", d?.Enabled == false ? 0 : 1) != 0,
                Warn = Get("Warn", d?.Warn ?? 0), Block = Get("Block", d?.Block ?? 0), MinHistory = Get("MinHistory", d?.MinHistory ?? 0), Floor = Get("Floor", d?.Floor ?? 0),
            };
        }
    }

    public sealed class RuleFinding
    {
        public string RuleCode { get; set; }
        public OpsSeverity Severity { get; set; }
        public string Message { get; set; }

        /// <summary>The message without numbers, for readers who may not see prices.</summary>
        public string MessageMasked { get; set; }
        public decimal? Entered { get; set; }
        public decimal? Reference { get; set; }
        public decimal? DeviationPercent { get; set; }
        public bool IsMoney { get; set; }
        public int? LineId { get; set; }
        public int? RowNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemTitle { get; set; }

        /// <summary>The key a finding and an exception are matched by.</summary>
        public string Key => (LineId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" + RuleCode;
    }

    public static class OpsRules
    {
        private static string N(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string P(decimal v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);
        private static string Row(int? row) => row.HasValue ? $"ردیف {row.Value.ToString(CultureInfo.InvariantCulture)}: " : string.Empty;

        // ---------------------------------------------------------------------------------------------------- arithmetic

        /// <summary>Signed deviation of <paramref name="entered"/> from <paramref name="reference"/> in percent; null when the reference is zero or negative.</summary>
        public static decimal? Deviation(decimal entered, decimal reference) => reference > 0 ? Math.Round((entered - reference) / reference * 100m, 2) : (decimal?)null;

        /// <summary>The ratio of the larger to the smaller value minus one, in percent: ten times and one tenth both give 900. Null when a value is not positive.</summary>
        public static decimal? Fold(decimal a, decimal b)
        {
            if (a <= 0 || b <= 0) return null;
            decimal hi = Math.Max(a, b), lo = Math.Min(a, b);
            return Math.Round((hi / lo - 1m) * 100m, 2);
        }

        /// <summary>Severity of a deviation: Block at the block threshold (when it is above zero), Warning at the warning threshold; the deviation must be positive.</summary>
        public static OpsSeverity? Classify(decimal deviation, OpsRuleSetting s)
        {
            if (s == null || !s.Enabled || deviation <= 0) return null;
            if (s.Block > 0 && deviation >= s.Block) return OpsSeverity.Block;
            if (deviation >= s.Warn) return OpsSeverity.Warning;
            return null;
        }

        /// <summary>The factor (1000 or 0.001) by which <paramref name="entered"/> differs from <paramref name="reference"/> within the tolerance percent, otherwise null.</summary>
        public static decimal? UomFactor(decimal entered, decimal reference, decimal tolerancePercent)
        {
            if (entered <= 0 || reference <= 0) return null;
            decimal ratio = entered / reference;
            decimal tol = Math.Max(0, tolerancePercent) / 100m;
            if (Math.Abs(ratio - 1000m) <= 1000m * tol) return 1000m;
            if (Math.Abs(ratio - 0.001m) <= 0.001m * tol) return 0.001m;
            return null;
        }

        /// <summary>The expected count of bad pieces from a historical rate (at least one piece, so a rate near zero does not make every scrap an outlier).</summary>
        public static decimal ExpectedBad(decimal historicalRate, decimal total) => Math.Max(1m, Math.Round(historicalRate * total, 4));

        private static RuleFinding Make(string code, OpsSeverity sev, string message, string masked, decimal? entered, decimal? reference, decimal? dev, int? line, int? row, int? itemId, string itemTitle, bool money = false) =>
            new RuleFinding { RuleCode = code, Severity = sev, Message = message, MessageMasked = masked ?? message, Entered = entered, Reference = reference, DeviationPercent = dev, LineId = line, RowNumber = row, ItemId = itemId, ItemTitle = itemTitle, IsMoney = money };

        // ---------------------------------------------------------------------------------------------------- plausibility

        /// <summary>R-RCV-QTY: the receipt quantity of a purchase order line against what is still open on the order (the hard tolerance of the order stays in the document service).</summary>
        public static RuleFinding RcvQty(OpsRuleSetting s, int? line, int? row, int itemId, string title, string unit, decimal entered, decimal openQuantity)
        {
            if (s == null || !s.Enabled || openQuantity <= 0 || entered <= openQuantity) return null;
            decimal dev = Deviation(entered, openQuantity).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.RcvQty, sev.Value, $"{Row(row)}«{title}»: مقدار رسید {N(entered)} {unit} از مانده سفارش خرید ({N(openQuantity)} {unit}) {P(dev)}٪ بیشتر است؛ مقدار را با بارنامه تطبیق دهید.", null, entered, openQuantity, dev, line, row, itemId, title);
        }

        /// <summary>
        /// R-RCV-WEIGHT: a weighed weight is compared with the certificate weight, otherwise with unit weight × quantity; without a weighed weight the expected weight is compared
        /// with the certificate. Nothing to compare = no finding.
        /// </summary>
        public static RuleFinding RcvWeight(OpsRuleSetting s, int? line, int? row, int itemId, string title, decimal? weighedKg, decimal? certificateKg, decimal? expectedKg)
        {
            if (s == null || !s.Enabled) return null;
            decimal entered, reference; string against;
            if (weighedKg.HasValue && weighedKg.Value > 0)
            {
                entered = weighedKg.Value;
                if (certificateKg.HasValue && certificateKg.Value > 0) { reference = certificateKg.Value; against = "وزن گواهی ذوب"; }
                else if (expectedKg.HasValue && expectedKg.Value > 0) { reference = expectedKg.Value; against = "وزن واحد × مقدار"; }
                else return null;
            }
            else if (certificateKg.HasValue && certificateKg.Value > 0 && expectedKg.HasValue && expectedKg.Value > 0)
            {
                entered = expectedKg.Value; reference = certificateKg.Value; against = "وزن گواهی ذوب (در برابر وزن واحد × مقدار)";
            }
            else return null;
            decimal dev = Math.Abs(Deviation(entered, reference).Value);
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.RcvWeight, sev.Value, $"{Row(row)}«{title}»: وزن {N(entered)} کیلوگرم با {against} ({N(reference)} کیلوگرم) {P(dev)}٪ اختلاف دارد.", null, entered, reference, dev, line, row, itemId, title);
        }

        /// <summary>R-RCV-PRICE (and R-INV-PRICE through <paramref name="code"/>): a unit price against a reference price; ten times and one tenth are equally suspect.</summary>
        public static RuleFinding Price(string code, OpsRuleSetting s, int? line, int? row, int itemId, string title, decimal price, decimal reference, string referenceText)
        {
            if (s == null || !s.Enabled) return null;
            decimal? dev = Fold(price, reference);
            if (dev == null) return null;
            OpsSeverity? sev = Classify(dev.Value, s);
            if (sev == null) return null;
            return Make(code, sev.Value, $"{Row(row)}«{title}»: فی {N(price)} ریال با {referenceText} ({N(reference)} ریال) {P(dev.Value)}٪ اختلاف دارد.",
                $"{Row(row)}«{title}»: فی واردشده با {referenceText} اختلاف زیادی دارد.", price, reference, dev, line, row, itemId, title, true);
        }

        /// <summary>R-RCV-PRICE: the price typed on a line differs from the order price; the order price is taken at confirmation (a warning only, whatever the thresholds).</summary>
        public static RuleFinding TypedPriceDiffersFromOrder(OpsRuleSetting s, int? line, int? row, int itemId, string title, decimal typed, decimal orderPrice)
        {
            if (s == null || !s.Enabled || typed == orderPrice) return null;
            decimal? dev = Fold(typed, orderPrice);
            if (dev == null || dev.Value < 1m) return null;
            return Make(OpsRuleCodes.RcvPrice, OpsSeverity.Warning, $"{Row(row)}«{title}»: فی واردشده {N(typed)} ریال با فی سفارش خرید ({N(orderPrice)} ریال) فرق دارد؛ هنگام تأیید فی سفارش اعمال می‌شود.",
                $"{Row(row)}«{title}»: فی واردشده با فی سفارش خرید فرق دارد؛ هنگام تأیید فی سفارش اعمال می‌شود.", typed, orderPrice, dev, line, row, itemId, title, true);
        }

        /// <summary>R-ISS-QTY: the quantity issued so far to a kit (this document included) against the standard requirement of the BOM.</summary>
        public static RuleFinding IssueAgainstKit(OpsRuleSetting s, int? line, int? row, int itemId, string title, string unit, decimal cumulative, decimal standard)
        {
            if (s == null || !s.Enabled || standard <= 0 || cumulative <= standard) return null;
            decimal dev = Deviation(cumulative, standard).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.IssQty, sev.Value, $"{Row(row)}«{title}»: مجموع حواله‌شده {N(cumulative)} {unit} از نیاز استاندارد کیت ({N(standard)} {unit}) {P(dev)}٪ بیشتر است.", null, cumulative, standard, dev, line, row, itemId, title);
        }

        /// <summary>R-ISS-CEIL: the consumption of a work order (this document included) against the ceiling of the item.</summary>
        public static RuleFinding IssueAgainstCeiling(OpsRuleSetting s, int? line, int? row, int itemId, string title, string unit, decimal cumulative, decimal ceiling)
        {
            if (s == null || !s.Enabled || ceiling <= 0 || cumulative <= ceiling) return null;
            decimal dev = Deviation(cumulative, ceiling).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.IssCeil, sev.Value, $"{Row(row)}«{title}»: مصرف تجمعی {N(cumulative)} {unit} از سقف مصرف ({N(ceiling)} {unit}) {P(dev)}٪ بیشتر می‌شود.", null, cumulative, ceiling, dev, line, row, itemId, title);
        }

        /// <summary>R-PRD-TIME: the hours booked on an operation (setup or run) against the standard of the routing; the reference is never below the floor in hours.</summary>
        public static RuleFinding ProductionTime(OpsRuleSetting s, int? line, string operation, string kind, decimal actualHours, decimal plannedHours)
        {
            if (s == null || !s.Enabled || plannedHours < 0) return null;
            decimal reference = Math.Max(plannedHours, s.Floor / 60m);
            if (reference <= 0 || actualHours <= reference) return null;
            decimal dev = Deviation(actualHours, reference).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.PrdTime, sev.Value, $"عملیات «{operation}»: زمان {kind} ثبت‌شده {N(actualHours)} ساعت از استاندارد مسیر ساخت ({N(reference)} ساعت) {P(dev)}٪ بیشتر است.", null, actualHours, reference, dev, line, null, null, null);
        }

        /// <summary>R-PRD-SCRAP: scrap against the history of the same operation; needs the minimum number of finished operations.</summary>
        public static RuleFinding ProductionScrap(OpsRuleSetting s, int? line, string operation, decimal scrap, decimal total, decimal historicalRate, int historyCount)
        {
            if (s == null || !s.Enabled || historyCount < Math.Max(1, (int)s.MinHistory) || scrap <= 0 || total <= 0) return null;
            decimal expected = ExpectedBad(historicalRate, total);
            if (scrap <= expected) return null;
            decimal dev = Deviation(scrap, expected).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.PrdScrap, sev.Value, $"عملیات «{operation}»: ضایعات {N(scrap)} از {N(total)} عدد، در حالی که با سابقه {historyCount} عملیات مشابه (نرخ {P(historicalRate * 100m)}٪) حدود {N(expected)} عدد انتظار می‌رود ({P(dev)}٪ بیشتر).", null, scrap, expected, dev, line, null, null, null);
        }

        /// <summary>R-QC-REJECT: rejected quantity of an inspection against the reject rate of the item and supplier.</summary>
        public static RuleFinding QcRejected(OpsRuleSetting s, int? line, int itemId, string title, decimal rejected, decimal total, decimal historicalRate, int historyCount)
        {
            if (s == null || !s.Enabled || historyCount < Math.Max(1, (int)s.MinHistory) || rejected <= 0 || total <= 0) return null;
            decimal expected = ExpectedBad(historicalRate, total);
            if (rejected <= expected) return null;
            decimal dev = Deviation(rejected, expected).Value;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.QcReject, sev.Value, $"«{title}»: رد {N(rejected)} از {N(total)} عدد، در حالی که با سابقه {historyCount} بازرسی (نرخ {P(historicalRate * 100m)}٪) حدود {N(expected)} عدد انتظار می‌رود ({P(dev)}٪ بیشتر).", null, rejected, expected, dev, line, null, itemId, title);
        }

        /// <summary>R-CNT-VAR: counted quantity against the book quantity; differences worth less than the floor (rial) are ignored when the cost is known.</summary>
        public static RuleFinding CountVariance(OpsRuleSetting s, int? line, int? row, int itemId, string title, string unit, decimal book, decimal counted, decimal? unitCost)
        {
            if (s == null || !s.Enabled) return null;
            decimal diff = Math.Abs(counted - book);
            if (diff == 0) return null;
            decimal? value = unitCost.HasValue && unitCost.Value > 0 ? diff * unitCost.Value : (decimal?)null;
            if (value.HasValue && s.Floor > 0 && value.Value < s.Floor) return null;
            decimal dev = book > 0 ? Math.Round(diff / book * 100m, 2) : 100m;
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            string valueText = value.HasValue ? $" (ارزش اختلاف حدود {N(Math.Round(value.Value, 0))} ریال)" : string.Empty;
            string maskedText = value.HasValue ? " (ارزش اختلاف بالاست)" : string.Empty;
            return Make(OpsRuleCodes.CntVar, sev.Value, $"{Row(row)}«{title}»: شمارش {N(counted)} {unit} با موجودی دفتری {N(book)} {unit} {P(dev)}٪ اختلاف دارد{valueText}.",
                $"{Row(row)}«{title}»: شمارش با موجودی دفتری {P(dev)}٪ اختلاف دارد{maskedText}.", counted, book, dev, line, row, itemId, title, value.HasValue);
        }

        /// <summary>R-UOM: the value is about 1000 times or one thousandth of what is expected (kg and g, m and mm).</summary>
        public static RuleFinding UnitMismatch(OpsRuleSetting s, int? line, int? row, int itemId, string title, string what, decimal entered, decimal reference, bool money = false)
        {
            if (s == null || !s.Enabled) return null;
            decimal? factor = UomFactor(entered, reference, s.Warn);
            if (factor == null) return null;
            string how = factor.Value >= 1 ? "حدود ۱۰۰۰ برابر" : "حدود یک‌هزارم";
            OpsSeverity sev = s.Block > 0 ? OpsSeverity.Block : OpsSeverity.Warning;
            decimal dev = Deviation(entered, reference) ?? 0;
            return Make(OpsRuleCodes.Uom, sev, $"{Row(row)}«{title}»: {what} {N(entered)} {how} مقدار مرجع ({N(reference)}) است؛ احتمال اشتباه واحد (مثلاً کیلوگرم و گرم یا متر و میلی‌متر).",
                $"{Row(row)}«{title}»: {what} {how} مقدار مرجع است؛ احتمال اشتباه واحد.", entered, reference, Math.Abs(dev), line, row, itemId, title, money);
        }

        /// <summary>R-DATE: the document is dated long before today.</summary>
        public static RuleFinding DateBack(OpsRuleSetting s, DateTime documentDate, DateTime today)
        {
            if (s == null || !s.Enabled) return null;
            int days = (int)(today.Date - documentDate.Date).TotalDays;
            if (days <= 0) return null;
            OpsSeverity? sev = Classify(days, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.Date, sev.Value, $"تاریخ سند {InventoryText.PersianDate(documentDate)} است، {days} روز قبل از امروز ({InventoryText.PersianDate(today)}).", null, days, s.Warn, days, null, null, null, null);
        }

        /// <summary>R-DATE-FWD: the document is dated after today.</summary>
        public static RuleFinding DateForward(OpsRuleSetting s, DateTime documentDate, DateTime today)
        {
            if (s == null || !s.Enabled) return null;
            int days = (int)(documentDate.Date - today.Date).TotalDays;
            if (days <= 0) return null;
            OpsSeverity? sev = Classify(days, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.DateFwd, sev.Value, $"تاریخ سند {InventoryText.PersianDate(documentDate)} است، {days} روز بعد از امروز ({InventoryText.PersianDate(today)}).", null, days, s.Warn, days, null, null, null, null);
        }

        // ---------------------------------------------------------------------------------------------------- baseline conformance

        private static OpsSeverity BaselineSeverity(OpsRuleSetting s) => s.Block > 0 ? OpsSeverity.Block : OpsSeverity.Warning;

        /// <summary>R-BL-BOM-ITEM: the item is not in the BOM baseline of the work order (approved substitutes and approved deviations count as in it).</summary>
        public static RuleFinding BomItem(OpsRuleSetting s, int? line, int? row, int itemId, string title, string workOrder, bool inBaseline)
        {
            if (s == null || !s.Enabled || inBaseline) return null;
            return Make(OpsRuleCodes.BlBomItem, BaselineSeverity(s), $"{Row(row)}«{title}» در BOM خط‌مبنای پروژه {workOrder} نیست (نه به‌عنوان کالای اصلی، نه جایگزین تأییدشده، نه انحراف تأییدشده).", null, null, null, null, line, row, itemId, title);
        }

        /// <summary>R-BL-BOM-QTY: the cumulative quantity beyond the BOM quantity (plus the allowance): inside the tolerance a warning, beyond it a block.</summary>
        public static RuleFinding BomQuantity(OpsRuleSetting s, int? line, int? row, int itemId, string title, string unit, decimal cumulative, decimal bomQuantity)
        {
            if (s == null || !s.Enabled || bomQuantity <= 0 || cumulative <= bomQuantity) return null;
            decimal dev = Deviation(cumulative, bomQuantity).Value;
            // the warn threshold is the first deviation that is reported, the block threshold is the tolerance
            OpsSeverity? sev = Classify(dev, s);
            if (sev == null) return null;
            return Make(OpsRuleCodes.BlBomQty, sev.Value, $"{Row(row)}«{title}»: مقدار تجمعی {N(cumulative)} {unit} از مقدار BOM همراه با تلفات مجاز ({N(bomQuantity)} {unit}) {P(dev)}٪ بیشتر است.", null, cumulative, bomQuantity, dev, line, row, itemId, title);
        }

        /// <summary>R-BL-OPC-STAGE: the stage suffix of the part is not in its routing, or the earlier stage of the routing has not happened yet.</summary>
        public static RuleFinding OpcStage(OpsRuleSetting s, int? line, int? row, int itemId, string title, string partNumber, string stage, bool routingExists, bool stageInRouting, string previousStage, bool previousDone)
        {
            if (s == null || !s.Enabled || !routingExists) return null;
            if (!stageInRouting)
                return Make(OpsRuleCodes.BlOpcStage, BaselineSeverity(s), $"{Row(row)}«{title}»: پسوند مرحله «{stage}» در مسیر ساخت (OPC) قطعه {partNumber} نیست.", null, null, null, null, line, row, itemId, title);
            if (previousStage != null && !previousDone)
                return Make(OpsRuleCodes.BlOpcStage, BaselineSeverity(s), $"{Row(row)}«{title}»: مرحله {stage} قطعه {partNumber} ثبت شده در حالی که مرحله قبلی مسیر ساخت ({previousStage}) هنوز انجام یا تأیید نشده است.", null, null, null, null, line, row, itemId, title);
            return null;
        }

        /// <summary>R-BL-OPC-OP: an operation booked while an earlier (non-parallel) one is unfinished, or on a work centre other than the routing's.</summary>
        public static RuleFinding OpcOperation(OpsRuleSetting s, int? line, string operation, string orderNumber, IReadOnlyList<string> unfinishedBefore, bool workCentreMismatch, string routingWorkCentre, string usedWorkCentre)
        {
            if (s == null || !s.Enabled) return null;
            if (unfinishedBefore != null && unfinishedBefore.Count > 0)
                return Make(OpsRuleCodes.BlOpcOp, BaselineSeverity(s), $"عملیات «{operation}» دستور {orderNumber} خارج از ترتیب مسیر ساخت است؛ عملیات قبلی هنوز تمام نشده: {string.Join("، ", unfinishedBefore.Take(3))}.", null, null, null, null, line, null, null, null);
            if (workCentreMismatch)
                return Make(OpsRuleCodes.BlOpcOp, BaselineSeverity(s), $"عملیات «{operation}» دستور {orderNumber} روی مرکز کار «{usedWorkCentre}» ثبت می‌شود، ولی مسیر ساخت مرکز «{routingWorkCentre}» را تعیین کرده است.", null, null, null, null, line, null, null, null);
            return null;
        }

        /// <summary>R-BL-ITP: an acceptance without the mandatory characteristics of the ITP point, or with a value outside the baseline tolerance.</summary>
        public static RuleFinding ItpCoverage(OpsRuleSetting s, int? line, string activity, IReadOnlyList<string> missing, IReadOnlyList<string> outOfTolerance)
        {
            if (s == null || !s.Enabled) return null;
            if (missing != null && missing.Count > 0)
                return Make(OpsRuleCodes.BlItp, BaselineSeverity(s), $"پذیرش بازرسی «{activity}» بدون ثبت مشخصه‌های الزامی ITP ممکن نیست؛ ثبت‌نشده: {string.Join("، ", missing.Take(5))}.", null, null, null, null, line, null, null, null);
            if (outOfTolerance != null && outOfTolerance.Count > 0)
                return Make(OpsRuleCodes.BlItp, BaselineSeverity(s), $"مقدار اندازه‌گیری‌شده «{string.Join("، ", outOfTolerance.Take(5))}» خارج از تلرانس خط‌مبنای ITP است؛ نتیجه باید رد یا عدم انطباق باشد، نه قبول.", null, null, null, null, line, null, null, null);
            return null;
        }

        /// <summary>R-BL-SPEC: material grade / certificate against the technical spec of the BOM line.</summary>
        public static RuleFinding Spec(OpsRuleSetting s, int? line, int? row, int itemId, string title, string expected, string found, bool certificateMissing)
        {
            if (s == null || !s.Enabled) return null;
            if (certificateMissing)
                return Make(OpsRuleCodes.BlSpec, BaselineSeverity(s), $"{Row(row)}«{title}»: گواهی/شماره ذوب الزامی این کالا ثبت نشده است.", null, null, null, null, line, row, itemId, title);
            if (!string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(found) && Norm(expected) != Norm(found))
                return Make(OpsRuleCodes.BlSpec, BaselineSeverity(s), $"{Row(row)}«{title}»: جنس/استاندارد «{found}» با مشخصه BOM «{expected}» نمی‌خواند.", null, null, null, null, line, row, itemId, title);
            return null;
        }

        /// <summary>R-BL-SPEC (document revision): a superseded revision is a warning, an obsolete one a block.</summary>
        public static RuleFinding DocumentRevision(OpsRuleSetting s, int? line, string documentNumber, bool obsolete, bool superseded)
        {
            if (s == null || !s.Enabled || (!obsolete && !superseded)) return null;
            OpsSeverity sev = obsolete ? BaselineSeverity(s) : OpsSeverity.Warning;
            return Make(OpsRuleCodes.BlSpec, sev, obsolete ? $"مدرک {documentNumber} باطل (Obsolete) شده است و نباید مبنای کار باشد." : $"بازنگری استفاده‌شده از مدرک {documentNumber} با بازنگری آزادشده‌ی جدیدتری جایگزین شده است.", null, null, null, null, line, null, null, null);
        }

        /// <summary>R-BL-WBS: the document cites an activity that is not in the baseline, or one that is closed or cancelled.</summary>
        public static RuleFinding Wbs(OpsRuleSetting s, int? line, string activityTitle, bool exists, bool open)
        {
            if (s == null || !s.Enabled || (exists && open)) return null;
            return Make(OpsRuleCodes.BlWbs, BaselineSeverity(s), exists ? $"فعالیت «{activityTitle}» بسته یا لغو شده است؛ سند به آن نمی‌تواند ارجاع شود." : "فعالیت/گره WBS سند در خط‌مبنای پروژه وجود ندارد.", null, null, null, null, line, null, null, null);
        }

        private static string Norm(string s) => new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
