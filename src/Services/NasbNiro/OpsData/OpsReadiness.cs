using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.OpsDataDtos;

namespace Services.NasbNiro.OpsData
{
    public static class ReadinessCodes
    {
        public const string Bom = "RD-BOM";
        public const string Wbs = "RD-WBS";
        public const string Opc = "RD-OPC";
        public const string Item = "RD-ITEM";
        public const string Drawing = "RD-DRAWING";
        public const string Itp = "RD-ITP";
        public const string Spec = "RD-SPEC";
        public const string Design = "RD-DESIGN";   // Package A1
        public const string Weight = "RS-WEIGHT";
        public const string Mdr = "RS-MDR";
        public const string ItpClient = "RS-ITPCLIENT";
        public const string Schedule = "RS-SCHEDULE";
        public const string LongLead = "RS-LONGLEAD";
        public const string Stage = "RS-STAGE";
        /// <summary>Package A2: open engineering change orders (in approval or approved, not yet closed) that touch the parts of the project; soft, listed only when there are some.</summary>
        public const string Eco = "RS-ECO";
    }

    public sealed class ReadinessDef
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>ENGINEERING, DCC, PLANNING or QC.</summary>
        public string Owner { get; set; }
        public bool Hard { get; set; }

        /// <summary>The item needs the work order (the contract's documents), not only the product.</summary>
        public bool ProjectLevel { get; set; }
    }

    public sealed class ReadinessSetting
    {
        public bool Enabled { get; set; }
        public bool Hard { get; set; }
    }

    public static class OpsReadinessCatalog
    {
        public const string Prefix = "OpsReady.";
        public const string MasterSwitch = "OpsReady.Enabled";
        public const string DueDays = "OpsReady.DueDays";
        public const string EscalateDays = "OpsReady.EscalateDays";
        public const string Source = "★ تصمیم مدیرعامل (کنترل آمادگی اطلاعات مهندسی شروع پروژه)";

        public static readonly IReadOnlyList<ReadinessDef> All = new List<ReadinessDef>
        {
            new ReadinessDef { Code = ReadinessCodes.Bom, Title = "BOM منتشرشده (Released) برای محصول", Owner = "ENGINEERING", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Wbs, Title = "الگوی WBS تأییدشده برای محصول", Owner = "PLANNING", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Opc, Title = "مسیر ساخت (OPC) تأییدشده برای هر قطعه‌ی ساخت، با مرکز کار و زمان استاندارد در هر عملیات", Owner = "ENGINEERING", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Item, Title = "کد کالا (WI-WH-002) و واحد سنجش برای هر ردیف BOM (با ضریب تبدیل اگر واحد خرید فرق دارد)", Owner = "DCC", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Drawing, Title = "نقشه‌ی منتشرشده (Released) در سامانه‌ی مدارک مهندسی برای هر قطعه‌ی ساخت", Owner = "DCC", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Itp, Title = "مشخصه‌های ITP برای هر عملیات بازرسی/کنترل کیفیت مسیر ساخت", Owner = "QC", Hard = true, ProjectLevel = true },
            new ReadinessDef { Code = ReadinessCodes.Spec, Title = "مشخصه‌ی فنی / جنس برای هر قطعه‌ی خریدنی", Owner = "ENGINEERING", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Design, Title = "کنترل طراحی: برنامه طراحی تاییدشده، کامل بودن ورودی‌ها و بازبینی تایید طراحی (Verification) پذیرفته برای BOM منتشرشده", Owner = "ENGINEERING", Hard = true },
            new ReadinessDef { Code = ReadinessCodes.Weight, Title = "وزن واحد کالاهای فلزی", Owner = "ENGINEERING", Hard = false },
            new ReadinessDef { Code = ReadinessCodes.Mdr, Title = "فهرست مدارک مهندسی قرارداد (MDR/MDL)", Owner = "DCC", Hard = false, ProjectLevel = true },
            new ReadinessDef { Code = ReadinessCodes.ItpClient, Title = "ITP تأییدشده‌ی کارفرما", Owner = "QC", Hard = false, ProjectLevel = true },
            new ReadinessDef { Code = ReadinessCodes.Schedule, Title = "خط پایه‌ی برنامه‌ی زمان‌بندی (تأییدشده)", Owner = "PLANNING", Hard = false, ProjectLevel = true },
            new ReadinessDef { Code = ReadinessCodes.LongLead, Title = "علامت‌گذاری اقلام با زمان تأمین طولانی (زمان تأمین کالا ثبت شده)", Owner = "PLANNING", Hard = false },
            new ReadinessDef { Code = ReadinessCodes.Stage, Title = "کد مرحله‌ی OPC برای هر مرحله‌ی قطعه‌ی ساخت", Owner = "ENGINEERING", Hard = false },
            new ReadinessDef { Code = ReadinessCodes.Eco, Title = "دستور تغییر مهندسی (ECO) بازِ متاثر از این محصول", Owner = "ENGINEERING", Hard = false, ProjectLevel = true },
        };

        public static ReadinessDef Find(string code) => All.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));

        public static string OwnerTitle(string owner)
        {
            switch (owner)
            {
                case "ENGINEERING": return "مهندسی";
                case "DCC": return "کنترل مدارک (DCC)";
                case "PLANNING": return "برنامه‌ریزی";
                case "QC": return "کنترل کیفیت";
                default: return owner;
            }
        }

        /// <summary>The roles notified for an owner unit.</summary>
        public static string OwnerRoles(string owner)
        {
            switch (owner)
            {
                case "ENGINEERING": return "ENGINEERING,ENGINEER";
                case "DCC": return "DCC,ENGINEERING";
                case "PLANNING": return "PLANNING";
                case "QC": return "QC,QA";
                default: return "PMO";
            }
        }

        public static string Key(string code, string part) => Prefix + code + "." + part;

        public static IEnumerable<Entities.Model.NasbNiro.Quality.QualitySetting> Defaults()
        {
            foreach (ReadinessDef d in All)
            {
                yield return new Entities.Model.NasbNiro.Quality.QualitySetting { Key = Key(d.Code, "Enabled"), Value = 1, Title = d.Code + ": فعال (۱=بله، ۰=خیر)", Source = Source };
                yield return new Entities.Model.NasbNiro.Quality.QualitySetting { Key = Key(d.Code, "Hard"), Value = d.Hard ? 1 : 0, Title = d.Code + ": مسدودکننده (۱=سخت: شروع را متوقف می‌کند، ۰=نرم: فقط تذکر)", Source = Source };
            }
            yield return new Entities.Model.NasbNiro.Quality.QualitySetting { Key = MasterSwitch, Value = 1, Title = "کنترل آمادگی اطلاعات مهندسی: کلید کلی (۱=روشن، ۰=خاموش)", Source = Source };
            yield return new Entities.Model.NasbNiro.Quality.QualitySetting { Key = DueDays, Value = 7, Title = "آمادگی اطلاعات مهندسی: مهلت تکمیل موارد ناقص بعد از شروع (روز)", Source = Source };
            yield return new Entities.Model.NasbNiro.Quality.QualitySetting { Key = EscalateDays, Value = 3, Title = "آمادگی اطلاعات مهندسی: ارجاع به مدیرعامل پس از این تعداد روز تأخیر", Source = Source };
        }

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            if (key == MasterSwitch) { max = 1; return true; }
            if (key == DueDays) { min = 1; max = 90; return true; }
            if (key == EscalateDays) { min = 1; max = 60; return true; }
            string part = key.Substring(key.LastIndexOf('.') + 1);
            string code = key.Substring(Prefix.Length, key.Length - Prefix.Length - part.Length - 1);
            if (Find(code) == null || (part != "Enabled" && part != "Hard")) return false;
            max = 1;
            return true;
        }

        public static ReadinessSetting SettingOf(IReadOnlyDictionary<string, decimal> values, ReadinessDef d)
        {
            bool master = values == null || !values.TryGetValue(MasterSwitch, out decimal m) || m != 0;
            decimal Get(string part, decimal fallback) => values != null && values.TryGetValue(Key(d.Code, part), out decimal v) ? v : fallback;
            return new ReadinessSetting { Enabled = master && Get("Enabled", 1) != 0, Hard = Get("Hard", d.Hard ? 1 : 0) != 0 };
        }

        public static int Days(IReadOnlyDictionary<string, decimal> values, string key, int fallback) => values != null && values.TryGetValue(key, out decimal v) && v > 0 ? (int)v : fallback;
    }

    // ================================================================================================== facts

    public sealed class ReadinessPart
    {
        public string PartNumber { get; set; }
        public string Name { get; set; }
        public PbMakeOrBuy MakeOrBuy { get; set; }
        public bool HasItem { get; set; }
        public bool HasUnit { get; set; }

        /// <summary>The BOM unit differs from the stock unit of the item and no conversion exists.</summary>
        public bool ConversionMissing { get; set; }
        public bool HasSpec { get; set; }
        public bool IsMetal { get; set; }
        public bool MetalWithoutWeight { get; set; }
        public bool LeadTimeMissing { get; set; }
        public bool HasReleasedDrawing { get; set; }
        public string Link { get; set; }
    }

    public sealed class ReadinessRoutingFact
    {
        public string PartNumber { get; set; }
        public bool Approved { get; set; }
        public int OperationSteps { get; set; }
        public int QcPoints { get; set; }
        public List<string> StepsWithoutWorkCentre { get; set; } = new List<string>();
        public List<string> StepsWithoutHours { get; set; } = new List<string>();
        public List<string> StepsWithoutStage { get; set; } = new List<string>();
    }

    public sealed class ReadinessFacts
    {
        public int ProductId { get; set; }
        public int? WorkOrderId { get; set; }
        public bool BomReleased { get; set; }
        public bool WbsApproved { get; set; }
        public List<ReadinessPart> Parts { get; set; } = new List<ReadinessPart>();
        public Dictionary<string, ReadinessRoutingFact> Routings { get; set; } = new Dictionary<string, ReadinessRoutingFact>();
        public string ProductLink { get; set; }

        /// <summary>Package A1: the design control applies (Design.VerificationRequired) and what is missing of it for the released BOM (empty = complete).</summary>
        public bool DesignRequired { get; set; }
        public List<string> DesignProblems { get; set; } = new List<string>();

        /// <summary>Fix round 1 (H6): the released BOM predates the design control and has no adoption; the item is then Soft with the note.</summary>
        public bool DesignLegacy { get; set; }

        // project level (only with a work order)
        public int ItpActivities { get; set; }
        public int ItpActivitiesWithCharacteristics { get; set; }
        public bool ItpExists { get; set; }
        public bool ItpClientApproved { get; set; }
        public int MdrCount { get; set; }
        public List<string> OpenEcos { get; set; } = new List<string>();
        public bool ScheduleBaselineApproved { get; set; }
    }

    public sealed class ReadinessResult
    {
        public List<ReadinessItemDto> Items { get; set; } = new List<ReadinessItemDto>();
        public int Percent { get; set; }
        public int HardMissing => Items.Count(i => i.Hard && (i.Status == "Missing" || i.Status == "Partial"));
        public int SoftMissing => Items.Count(i => !i.Hard && (i.Status == "Missing" || i.Status == "Partial"));
        public List<ReadinessItemDto> HardGaps => Items.Where(i => i.Hard && (i.Status == "Missing" || i.Status == "Partial")).ToList();
    }

    // ================================================================================================== pure evaluation

    public static class OpsReadiness
    {
        public const string Ok = "OK";
        public const string MissingStatus = "Missing";
        public const string Partial = "Partial";
        public const string NotApplicable = "NotApplicable";

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static ReadinessItemDto Make(ReadinessDef d, ReadinessSetting s, int total, List<ReadinessOffenderDto> offenders)
        {
            int missing = offenders.Count;
            string status = missing == 0 ? Ok : (total > 0 && missing >= total ? MissingStatus : Partial);
            if (total == 0 && missing > 0) status = MissingStatus;
            return new ReadinessItemDto
            {
                Code = d.Code, Title = d.Title, Status = status, Hard = s.Hard, Severity = s.Hard ? "Hard" : "Soft", Owner = d.Owner, OwnerTitle = OpsReadinessCatalog.OwnerTitle(d.Owner),
                Total = total, MissingCount = missing, Offenders = offenders.Take(30).ToList(),
            };
        }

        private static ReadinessOffenderDto Off(string text, string link = null) => new ReadinessOffenderDto { Text = text, Link = link };

        private static string PartText(ReadinessPart p) => string.IsNullOrWhiteSpace(p.Name) ? p.PartNumber : p.PartNumber + " - " + p.Name;

        /// <summary>The checklist of a product (and a work order, when the facts have one): which items are complete, partial or missing, and what exactly is missing.</summary>
        public static ReadinessResult Evaluate(ReadinessFacts f, IReadOnlyDictionary<string, decimal> settings)
        {
            var result = new ReadinessResult();
            bool project = f.WorkOrderId.HasValue;
            List<ReadinessPart> lines = f.Parts;
            List<ReadinessPart> make = lines.Where(p => p.MakeOrBuy == PbMakeOrBuy.Make && !string.IsNullOrWhiteSpace(p.PartNumber)).GroupBy(p => p.PartNumber).Select(g => g.First()).ToList();
            List<ReadinessPart> buy = lines.Where(p => p.MakeOrBuy == PbMakeOrBuy.Purchase).ToList();
            List<ReadinessRoutingFact> routings = make.Where(p => f.Routings.ContainsKey(p.PartNumber)).Select(p => f.Routings[p.PartNumber]).ToList();

            foreach (ReadinessDef d in OpsReadinessCatalog.All)
            {
                ReadinessSetting s = OpsReadinessCatalog.SettingOf(settings, d);
                if (!s.Enabled) continue;
                if (d.ProjectLevel && !project) continue;
                var off = new List<ReadinessOffenderDto>();
                ReadinessItemDto item;
                switch (d.Code)
                {
                    case ReadinessCodes.Bom:
                        if (!f.BomReleased) off.Add(Off("برای این محصول BOM منتشرشده (Released) وجود ندارد.", f.ProductLink));
                        item = Make(d, s, 1, off);
                        break;
                    case ReadinessCodes.Wbs:
                        if (!f.WbsApproved) off.Add(Off("برای این محصول الگوی WBS تأییدشده وجود ندارد.", f.ProductLink));
                        item = Make(d, s, 1, off);
                        break;
                    case ReadinessCodes.Opc:
                        foreach (ReadinessPart p in make)
                        {
                            if (!f.Routings.TryGetValue(p.PartNumber, out ReadinessRoutingFact r) || !r.Approved) { off.Add(Off(PartText(p) + ": مسیر ساخت تأییدشده ندارد.", f.ProductLink)); continue; }
                            if (r.OperationSteps == 0) { off.Add(Off(PartText(p) + ": مسیر ساخت هیچ عملیاتی ندارد.", f.ProductLink)); continue; }
                            var why = new List<string>();
                            if (r.StepsWithoutWorkCentre.Count > 0) why.Add("بدون مرکز کار: " + string.Join("، ", r.StepsWithoutWorkCentre.Take(5)));
                            if (r.StepsWithoutHours.Count > 0) why.Add("بدون زمان استاندارد: " + string.Join("، ", r.StepsWithoutHours.Take(5)));
                            if (why.Count > 0) off.Add(Off(PartText(p) + ": " + string.Join("؛ ", why), f.ProductLink));
                        }
                        item = Make(d, s, make.Count, off);
                        break;
                    case ReadinessCodes.Item:
                        foreach (ReadinessPart p in lines)
                        {
                            if (!p.HasItem) off.Add(Off(PartText(p) + ": کد کالا (انبار) ندارد.", f.ProductLink));
                            else if (!p.HasUnit) off.Add(Off(PartText(p) + ": واحد سنجش ندارد.", f.ProductLink));
                            else if (p.ConversionMissing) off.Add(Off(PartText(p) + ": واحد BOM با واحد انبار فرق دارد و ضریب تبدیل ثبت نشده است.", f.ProductLink));
                        }
                        item = Make(d, s, lines.Count, off);
                        break;
                    case ReadinessCodes.Drawing:
                        foreach (ReadinessPart p in make.Where(p => !p.HasReleasedDrawing)) off.Add(Off(PartText(p) + ": نقشه‌ی منتشرشده (Released) به این قطعه پیوند نشده است.", f.ProductLink));
                        item = Make(d, s, make.Count, off);
                        break;
                    case ReadinessCodes.Itp:
                    {
                        int qc = routings.Sum(r => r.QcPoints);
                        if (qc > 0)
                        {
                            if (!f.ItpExists) off.Add(Off($"{N(qc)} نقطه‌ی بازرسی در مسیر ساخت هست ولی ITP برای دستور کار ثبت نشده است."));
                            else
                            {
                                int gap = Math.Max(0, qc - f.ItpActivitiesWithCharacteristics);
                                if (gap > 0) off.Add(Off($"{N(qc)} نقطه‌ی بازرسی در مسیر ساخت هست ولی فقط {N(f.ItpActivitiesWithCharacteristics)} فعالیت ITP مشخصه (پارامتر) دارد."));
                            }
                        }
                        item = Make(d, s, qc == 0 ? 0 : 1, off);
                        if (qc == 0) item.Status = Ok;
                        break;
                    }
                    case ReadinessCodes.Spec:
                        foreach (ReadinessPart p in buy.Where(p => !p.HasSpec)) off.Add(Off(PartText(p) + ": مشخصه‌ی فنی یا جنس ندارد.", f.ProductLink));
                        item = Make(d, s, buy.Count, off);
                        break;
                    case ReadinessCodes.Design:
                        if (!f.DesignRequired || !f.BomReleased) { item = Make(d, s, 0, off); item.Status = NotApplicable; break; }
                        foreach (string problem in f.DesignProblems) off.Add(Off(problem, f.ProductLink));
                        item = Make(d, s, Math.Max(1, off.Count), off);
                        if (f.DesignLegacy && off.Count > 0) { item.Hard = false; item.Severity = "Soft"; }   // legacy, not design-verified: visible, not blocking
                        break;
                    case ReadinessCodes.Weight:
                        foreach (ReadinessPart p in lines.Where(p => p.MetalWithoutWeight)) off.Add(Off(PartText(p) + ": وزن واحد کالا ثبت نشده است.", f.ProductLink));
                        item = Make(d, s, lines.Count(p => p.IsMetal), off);
                        break;
                    case ReadinessCodes.Mdr:
                        if (f.MdrCount == 0) off.Add(Off("فهرست مدارک مهندسی (MDR/MDL) برای قرارداد ثبت نشده است."));
                        item = Make(d, s, 1, off);
                        break;
                    case ReadinessCodes.ItpClient:
                        if (!f.ItpClientApproved) off.Add(Off(f.ItpExists ? "ITP هنوز به تأیید کارفرما نرسیده است." : "ITP برای دستور کار ثبت نشده است."));
                        item = Make(d, s, 1, off);
                        break;
                    case ReadinessCodes.Schedule:
                        if (!f.ScheduleBaselineApproved) off.Add(Off("خط پایه‌ی تأییدشده‌ی برنامه‌ی زمان‌بندی قرارداد ثبت نشده است."));
                        item = Make(d, s, 1, off);
                        break;
                    case ReadinessCodes.LongLead:
                        foreach (ReadinessPart p in buy.Where(p => p.LeadTimeMissing)) off.Add(Off(PartText(p) + ": زمان تأمین کالا ثبت نشده است.", f.ProductLink));
                        item = Make(d, s, buy.Count, off);
                        break;
                    case ReadinessCodes.Stage:
                        foreach (ReadinessPart p in make.Where(p => f.Routings.TryGetValue(p.PartNumber, out ReadinessRoutingFact r) && r.StepsWithoutStage.Count > 0))
                            off.Add(Off(PartText(p) + ": مراحل بدون کد مرحله: " + string.Join("، ", f.Routings[p.PartNumber].StepsWithoutStage.Take(5)), f.ProductLink));
                        item = Make(d, s, make.Count, off);
                        break;
                    case ReadinessCodes.Eco:
                        if (f.OpenEcos.Count == 0) continue;   // no open order touches the project: the item does not appear and the percentage does not move
                        foreach (string eco in f.OpenEcos) off.Add(Off(eco, f.ProductLink));
                        item = Make(d, s, f.OpenEcos.Count, off);
                        break;
                    default:
                        continue;
                }
                result.Items.Add(item);
            }
            result.Percent = Percent(result.Items);
            return result;
        }

        /// <summary>OK counts one, Partial a half, Missing nothing; an item that does not apply is left out.</summary>
        public static int Percent(IReadOnlyList<ReadinessItemDto> items)
        {
            var counted = items.Where(i => i.Status != NotApplicable).ToList();
            if (counted.Count == 0) return 100;
            double score = counted.Sum(i => i.Status == Ok ? 1.0 : (i.Status == Partial ? 0.5 : 0.0));
            return (int)Math.Round(score / counted.Count * 100.0, MidpointRounding.AwayFromZero);
        }

        public static string Message(IReadOnlyList<ReadinessItemDto> gaps, string heading)
        {
            var sb = new StringBuilder(heading);
            foreach (ReadinessItemDto g in gaps)
            {
                sb.Append("\n• [").Append(g.Code).Append("] ").Append(g.Title).Append(" — مسئول: ").Append(g.OwnerTitle).Append(" (").Append(N(g.MissingCount)).Append(" مورد)");
                foreach (ReadinessOffenderDto o in g.Offenders.Take(3)) sb.Append("\n    - ").Append(o.Text);
                if (g.Offenders.Count > 3) sb.Append("\n    - و ").Append(N(g.MissingCount - 3)).Append(" مورد دیگر");
            }
            return sb.ToString();
        }
    }
}
