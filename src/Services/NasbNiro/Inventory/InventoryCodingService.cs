using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    /// <summary>Segment names of the company coding (WI-WH-002).</summary>
    public static class CodingSchemes
    {
        public const string Group = "Group";
        public const string Assembly = "Assembly";
        public const string PartStatus = "PartStatus";
        public const string MaterialShape = "MaterialShape";
        public const string MaterialGrade = "MaterialGrade";
        public const string MaterialType = "MaterialType";
        public const string MaterialStatus = "MaterialStatus";
        public const string ProductType = "ProductType";
        public const string ModelPart = "ModelPart";
        public const string ModelMaterial = "ModelMaterial";
        public const string ToolCategory = "ToolCategory";
        public const string ConsumableCategory = "ConsumableCategory";
        public const string AssetCategory = "AssetCategory";

        public static readonly string[] All =
        {
            Group, Assembly, PartStatus, MaterialShape, MaterialGrade, MaterialType, MaterialStatus, ProductType,
            ModelPart, ModelMaterial, ToolCategory, ConsumableCategory, AssetCategory,
        };
    }

    /// <summary>Checks an item code against WI-WH-002 and splits it into its segments.</summary>
    public static class ItemCodeRules
    {
        public const string StandardPartGroup = "08";

        // MESC: 16.10.05.001.2 (group.subgroup.class.item.check) — written with dots
        private static readonly Regex Mesc = new Regex(@"^\d{2}\.\d{2}\.\d{2}\.\d{3}\.\d$", RegexOptions.Compiled);
        private static readonly Regex Special = new Regex(@"^SP_\d{3,6}_\d{3,8}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Digits = new Regex(@"^\d+$", RegexOptions.Compiled);

        public static bool IsMesc(string code) => code != null && Mesc.IsMatch(code);

        /// <summary>Codes are written without spaces; Persian digits are changed to Latin ones.</summary>
        public static string Normalize(string code)
        {
            code = InventoryText.Clean(code);
            if (code == null) return null;
            var chars = code.Where(c => !char.IsWhiteSpace(c)).Select(c =>
            {
                if (c >= '۰' && c <= '۹') return (char)('0' + (c - '۰'));
                if (c >= '٠' && c <= '٩') return (char)('0' + (c - '٠'));
                return char.ToUpperInvariant(c);
            });
            return new string(chars.ToArray());
        }

        private static bool Has(IReadOnlyCollection<CodingValue> values, string scheme, string code) =>
            values.Any(v => v.Scheme == scheme && v.IsActive && SameCode(v.Code, code));

        private static CodingValue Find(IReadOnlyCollection<CodingValue> values, string scheme, string code) =>
            values.FirstOrDefault(v => v.Scheme == scheme && v.IsActive && SameCode(v.Code, code));

        // numeric codes compare by value (the instruction writes "01" for a three-digit segment "001")
        private static bool SameCode(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            return int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int x) &&
                   int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out int y) && x == y;
        }

        /// <summary>Splits and checks the code. Errors make the code unusable for a new item; warnings are shown only.</summary>
        public static CodeCheckDto Check(string rawCode, IReadOnlyCollection<CodingValue> values)
        {
            var result = new CodeCheckDto { Code = Normalize(rawCode) };
            string code = result.Code;
            if (code == null)
            {
                result.Errors.Add(InventoryMessages.CodeRequired);
                return result;
            }

            void Segment(string title, int start, int length, string meaning = null)
            {
                if (code.Length >= start + length)
                    result.Segments.Add(new CodeSegmentDto { Title = title, Value = code.Substring(start, length), Meaning = meaning });
            }

            if (IsMesc(code))
            {
                result.GroupCode = StandardPartGroup;
                result.MescCode = code;
                result.BaseCode = code;
                result.Segments.Add(new CodeSegmentDto { Title = "کد MESC قطعه استاندارد", Value = code, Meaning = "گروه ۰۸ قطعات استاندارد" });
                return Finish(result, values);
            }
            if (Special.IsMatch(code))
            {
                result.GroupCode = "02";
                result.BaseCode = code;
                result.Segments.Add(new CodeSegmentDto { Title = "قطعه سفارشی", Value = code, Meaning = "SP_ + سریال توربین + کد مجموعه" });
                result.Warnings.Add("قطعه سفارشی (SP_): کد ساختاری کنترل نمی‌شود؛ شماره سریال توربین و کد مجموعه را بررسی کنید.");
                return Finish(result, values);
            }
            if (code.Length != 14)
            {
                result.Errors.Add($"کد باید ۱۴ کاراکتر باشد (این کد {code.Length} کاراکتر است)؛ قطعه استاندارد با کد MESC به شکل 16.10.05.001.2 ثبت می‌شود.");
                return result;
            }

            result.GroupCode = code.Substring(0, 2);
            CodingValue group = Find(values, CodingSchemes.Group, result.GroupCode);
            if (group == null)
            {
                result.Errors.Add($"دو رقم اول ({result.GroupCode}) گروه کالای تعریف‌شده‌ای نیست.");
                return result;
            }
            Segment("نوع کالا", 0, 2, group.Title);

            switch (result.GroupCode)
            {
                case "02": // part / casting: type | assembly | part | build type | drawing revision | part status
                {
                    if (!Digits.IsMatch(code)) { result.Errors.Add("کد قطعه فقط عدد است."); break; }
                    int assembly = int.Parse(code.Substring(2, 3), CultureInfo.InvariantCulture);
                    CodingValue range = values.Where(v => v.Scheme == CodingSchemes.Assembly && v.IsActive && v.RangeFrom <= assembly && v.RangeTo >= assembly)
                        .OrderBy(v => v.RangeTo - v.RangeFrom).FirstOrDefault();
                    Segment("مجموعه", 2, 3, range?.Title);
                    Segment("قطعه", 5, 3);
                    Segment("نوع ساخت", 8, 2);
                    Segment("ویرایش نقشه", 10, 2);
                    string status = code.Substring(12, 2);
                    CodingValue stage = Find(values, CodingSchemes.PartStatus, status);
                    Segment("وضعیت قطعه", 12, 2, stage?.Title);
                    if (range == null) result.Errors.Add($"شماره مجموعه {assembly:000} در هیچ محدوده مجموعه‌ای (جدول ۴) نیست.");
                    if (stage == null) result.Errors.Add($"وضعیت قطعه {status} در جدول ۳ تعریف نشده است.");
                    else if (!stage.IsStockable)
                        result.StageNotStockable = true;
                    result.StageCode = status;
                    result.BaseCode = code.Substring(0, 12);
                    break;
                }
                case "01": // material: type | shape | dimension | grade | material type | material status
                {
                    if (!Digits.IsMatch(code)) { result.Errors.Add("کد متریال فقط عدد است."); break; }
                    CodingValue shape = Find(values, CodingSchemes.MaterialShape, code.Substring(2, 3));
                    CodingValue grade = Find(values, CodingSchemes.MaterialGrade, code.Substring(8, 2));
                    CodingValue type = Find(values, CodingSchemes.MaterialType, code.Substring(10, 2));
                    CodingValue status = Find(values, CodingSchemes.MaterialStatus, code.Substring(12, 2));
                    Segment("مشخصه فیزیکی", 2, 3, shape?.Title);
                    Segment("مشخصه ابعادی", 5, 3);
                    Segment("جنس", 8, 2, grade?.Title);
                    Segment("نوع متریال", 10, 2, type?.Title);
                    Segment("وضعیت متریال", 12, 2, status?.Title);
                    if (shape == null) result.Errors.Add("مشخصه فیزیکی (رقم ۳ تا ۵) در جدول ۸ نیست.");
                    if (grade == null) result.Errors.Add("جنس (رقم ۹ و ۱۰) در جدول ۱۰ نیست.");
                    if (type == null) result.Warnings.Add("نوع متریال (رقم ۱۱ و ۱۲) در جدول ۱۱ نیست.");
                    if (status == null) result.Errors.Add("وضعیت متریال (رقم ۱۳ و ۱۴) در جدول ۱۲ نیست.");
                    result.StageCode = code.Substring(12, 2);
                    result.BaseCode = code.Substring(0, 12);
                    break;
                }
                case "03": // product: type | nth type | product type | size | option | century | year
                {
                    CodingValue product = Find(values, CodingSchemes.ProductType, code.Substring(3, 2));
                    Segment("چندمین نوع در پلتفرم", 2, 1);
                    Segment("نوع محصول", 3, 2, product?.Title);
                    Segment("سایز", 5, 3);
                    Segment("ویژگی خاص", 8, 2);
                    Segment("قرن ساخت", 10, 2);
                    Segment("سال ساخت", 12, 2);
                    if (!char.IsDigit(code[2])) result.Errors.Add("رقم ۳ (چندمین نوع) باید عدد باشد.");
                    if (product == null) result.Errors.Add("نوع محصول (کاراکتر ۴ و ۵) در جدول ۱۳ نیست (مثل TS، LB، GB).");
                    if (!Digits.IsMatch(code.Substring(10, 4))) result.Errors.Add("قرن و سال ساخت (کاراکتر ۱۱ تا ۱۴) عدد است.");
                    result.BaseCode = code;
                    break;
                }
                case "04": // casting pattern: type | part | M/P/C | part | material | build revision | drawing revision
                {
                    CodingValue part = Find(values, CodingSchemes.ModelPart, code.Substring(5, 1));
                    CodingValue material = Find(values, CodingSchemes.ModelMaterial, code.Substring(8, 2));
                    Segment("کد قطعه (مجموعه)", 2, 3);
                    Segment("بخش مدل", 5, 1, part?.Title);
                    Segment("کد قطعه", 6, 2);
                    Segment("جنس مدل", 8, 2, material?.Title);
                    Segment("رویژن ساخت", 10, 2);
                    Segment("رویژن نقشه", 12, 2);
                    if (part == null) result.Errors.Add("کاراکتر ۶ باید M (مدل)، P (قطعه آزاد) یا C (جعبه ماهیچه) باشد.");
                    if (material == null) result.Errors.Add("جنس مدل (کاراکتر ۹ و ۱۰) در جدول ۱۸ نیست.");
                    if (!Digits.IsMatch(code.Substring(2, 3) + code.Substring(6, 2) + code.Substring(10, 4))) result.Errors.Add("به‌جز کاراکتر ۶، کد مدل عدد است.");
                    result.BaseCode = code;
                    break;
                }
                case "05":
                case "07":
                case "12": // consumables, tools, instruments: type | category | kind | counter
                {
                    if (!Digits.IsMatch(code)) { result.Errors.Add("این کد فقط عدد است."); break; }
                    string scheme = result.GroupCode == "05" ? CodingSchemes.ConsumableCategory : CodingSchemes.ToolCategory;
                    CodingValue category = Find(values, scheme, code.Substring(2, 3));
                    Segment("دسته‌بندی", 2, 3, category?.Title);
                    Segment("نوع", 5, 3);
                    Segment("شمارنده", 8, 6);
                    if (category == null) result.Warnings.Add("دسته‌بندی (رقم ۳ تا ۵) هنوز در جدول تعریف نشده است؛ برنامه‌ریزی آن را اضافه کند.");
                    result.BaseCode = code;
                    break;
                }
                case "06": // asset: type | century | year | category | counter
                {
                    if (!Digits.IsMatch(code)) { result.Errors.Add("کد اموال فقط عدد است."); break; }
                    CodingValue category = Find(values, CodingSchemes.AssetCategory, code.Substring(6, 2));
                    Segment("قرن خرید", 2, 2);
                    Segment("سال خرید", 4, 2);
                    Segment("دسته‌بندی", 6, 2, category?.Title);
                    Segment("شمارنده", 8, 6);
                    if (category == null) result.Warnings.Add("دسته‌بندی اموال (رقم ۷ و ۸) در جدول نیست.");
                    result.BaseCode = code;
                    break;
                }
                case StandardPartGroup:
                    result.Errors.Add("قطعه استاندارد (گروه ۰۸) با کد MESC به شکل 16.10.05.001.2 ثبت می‌شود.");
                    break;
                default:
                    if (!Digits.IsMatch(code)) result.Errors.Add("این کد فقط عدد است.");
                    result.BaseCode = code;
                    break;
            }
            return Finish(result, values);
        }

        private static CodeCheckDto Finish(CodeCheckDto result, IReadOnlyCollection<CodingValue> values)
        {
            if (result.StageNotStockable)
                result.Warnings.Add($"★ وضعیت {result.StageCode} مرحله انبارشی نیست؛ فقط مراحل انبارشی (خام ریخته‌گری، رافینگ، ماشینکاری نهایی، قطعه نهایی، رنگ‌شده) کد جدا می‌گیرند و بقیه به‌صورت عملیات روی سریال/بچ ثبت می‌شوند.");
            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        /// <summary>Default tables of WI-WH-002-03 (with groups 08 and 12 added). Marked ★ where the company decision is provisional.</summary>
        public static IEnumerable<CodingValue> DefaultValues()
        {
            int order = 0;
            CodingValue V(string scheme, string code, string title, bool stockable = false, int? from = null, int? to = null, string note = null) =>
                new CodingValue { Scheme = scheme, Code = code, Title = title, IsStockable = stockable, RangeFrom = from, RangeTo = to, Description = note, SortOrder = ++order, IsActive = true };

            yield return V(CodingSchemes.Group, "01", "متریال");
            yield return V(CodingSchemes.Group, "02", "قطعات / ریخته‌گری");
            yield return V(CodingSchemes.Group, "03", "محصول");
            yield return V(CodingSchemes.Group, "04", "مدل");
            yield return V(CodingSchemes.Group, "05", "ملزومات مصرفی و اداری");
            yield return V(CodingSchemes.Group, "06", "اموال");
            yield return V(CodingSchemes.Group, "07", "ابزارآلات");
            yield return V(CodingSchemes.Group, "08", "قطعات استاندارد (کد MESC)", note: "افزوده به دستورالعمل");
            yield return V(CodingSchemes.Group, "12", "ابزار دقیق", note: "افزوده به دستورالعمل");

            string[,] statuses =
            {
                { "01", "مرحله رافینگ (Rough machining)" }, { "02", "مرحله ماشینکاری نهایی (Final machining)" }, { "03", "قطعه نهایی" },
                { "04", "عملیات حرارتی بعد از ساخت اولیه" }, { "05", "جوشکاری بدون عملیات حرارتی" }, { "06", "عملیات حرارتی بعد از جوشکاری" },
                { "07", "عملیات حرارتی قطعه خام" }, { "08", "هیدروتست بعد از عملیات حرارتی" }, { "09", "هیدروتست بعد از رافینگ" },
                { "10", "قطعه خام ریخته‌گری" }, { "12", "قطعه خام سندبلاست‌شده" }, { "13", "سندبلاست قبل از رنگ" }, { "14", "فرز (Milling)" },
                { "15", "Joint machining" }, { "16", "آبکاری کروم (Chrome plating)" }, { "17", "سوراخکاری (Drilling)" },
                { "18", "برش وایر/واترجت/لیزر" }, { "19", "تراش (Lathing)" }, { "20", "سنگ‌زنی (Grinding)" }, { "21", "قطعه رنگ‌شده" },
                { "22", "دنده‌زنی (Tapping)" }, { "23", "Spring forming" }, { "24", "آنودایز (Anodizing)" }, { "25", "سنباده‌زنی" },
                { "26", "دریل سوراخکاری" }, { "27", "خمش (Bending)" }, { "28", "هیدروتست نهایی" }, { "29", "Spiral rolling" },
                { "30", "Chromate coating" }, { "31", "Final grinding" }, { "32", "Run-out test" }, { "33", "بابیت‌ریزی" },
                { "34", "فرز ثانویه" }, { "35", "تراش ثانویه" }, { "36", "سندبلاست اولیه" }, { "37", "Spark" },
                { "38", "Support leg machining" }, { "39", "Punching" }, { "40", "بالانس (Balancing)" }, { "41", "رافینگ ثانویه" },
                { "42", "Hobbing" }, { "43", "قطعه NDT شده" },
            };
            var stockable = new HashSet<string> { "10", "01", "02", "03", "21" };
            for (int i = 0; i < statuses.GetLength(0); i++)
            {
                bool isStock = stockable.Contains(statuses[i, 0]);
                yield return V(CodingSchemes.PartStatus, statuses[i, 0], statuses[i, 1], isStock,
                    note: isStock ? "★ مرحله انبارشی (موقت؛ قابل تغییر)" : "★ عملیات مسیر ساخت روی سریال/بچ (موقت)");
            }

            object[,] assemblies =
            {
                { "Casing", 101, 129 }, { "Rotor", 130, 159 }, { "Valve", 160, 189 }, { "Hand valve", 190, 219 },
                { "Journal bearing", 220, 249 }, { "Thrust bearing", 250, 279 }, { "Fix blade", 280, 309 }, { "Gland", 310, 339 },
                { "Carbon ring", 340, 359 }, { "Nozzle", 360, 389 }, { "Sentinel valve", 390, 399 }, { "Oil cooler", 400, 409 },
                { "Gov. disk ass.", 410, 419 }, { "Reversing chamber", 420, 429 }, { "Gov. linkage", 430, 449 }, { "Gov. slide ass.", 450, 459 },
                { "Trip linkage", 460, 479 }, { "General", 480, 499 }, { "Gov. weight ass.", 500, 509 }, { "Coupling", 510, 529 },
                { "Mechanical box", 530, 544 }, { "Gov. connection", 545, 559 }, { "Standard part", 900, 999 },
            };
            for (int i = 0; i < assemblies.GetLength(0); i++)
            {
                int from = (int)assemblies[i, 1], to = (int)assemblies[i, 2];
                yield return V(CodingSchemes.Assembly, from.ToString("000", CultureInfo.InvariantCulture), (string)assemblies[i, 0], from: from, to: to);
            }

            string[] shapes = { "میلگرد", "تسمه", "ورق", "شش‌پر", "لوله", "چهارپر", "ورق سوراخ‌دار", "واشر", "پیچ متری", "پین" };
            for (int i = 0; i < shapes.Length; i++) yield return V(CodingSchemes.MaterialShape, (i + 1).ToString("000", CultureInfo.InvariantCulture), shapes[i]);

            string[] grades =
            {
                "MO40", "Al 6061", "Carbon PTFE 25%", "Phosphor bronze", "C26000 brass", "SS 304", "Brass", "SS 316", "SS 420", "ST37",
                "ASTM B152", "CK45", "SS 410", "VCN 150", "Carbon steel", "Ck 10", "UNS S41003", "Copper", "AL bronze",
            };
            for (int i = 0; i < grades.Length; i++) yield return V(CodingSchemes.MaterialGrade, (i + 1).ToString("00", CultureInfo.InvariantCulture), grades[i]);

            yield return V(CodingSchemes.MaterialType, "01", "متریال خام");
            yield return V(CodingSchemes.MaterialStatus, "01", "متریال خام");
            yield return V(CodingSchemes.MaterialStatus, "02", "متریال NDT شده");
            yield return V(CodingSchemes.MaterialStatus, "03", "عملیات حرارتی شده");
            yield return V(CodingSchemes.MaterialStatus, "04", "برش خورده و تخصیص به قطعه");

            string[,] products =
            {
                { "TS", "توربین (TS)" }, { "TT", "توربین (TT)" }, { "TF", "توربین (TF)" }, { "TL", "توربین (TL)" }, { "TG", "توربین (TG)" },
                { "TZ", "توربین (TZ)" }, { "TB", "توربین (TB)" }, { "TO", "توربین (TO)" }, { "LB", "Lube oil" }, { "GB", "Gearbox" },
                { "UC", "Unit control system" }, { "BP", "Base plate" }, { "CP", "Coupling" }, { "SC", "Surface condenser" }, { "PU", "Pump" },
                { "EG", "Electrical generator" },
            };
            for (int i = 0; i < products.GetLength(0); i++) yield return V(CodingSchemes.ProductType, products[i, 0], products[i, 1]);

            yield return V(CodingSchemes.ModelPart, "M", "مدل");
            yield return V(CodingSchemes.ModelPart, "P", "قطعه آزاد");
            yield return V(CodingSchemes.ModelPart, "C", "جعبه ماهیچه");
            yield return V(CodingSchemes.ModelMaterial, "01", "AL");
            yield return V(CodingSchemes.ModelMaterial, "02", "W (چوب)");
            yield return V(CodingSchemes.ModelMaterial, "03", "AR");
            yield return V(CodingSchemes.ModelMaterial, "04", "Y");

            yield return V(CodingSchemes.ToolCategory, "001", "قلاویز");
            yield return V(CodingSchemes.ToolCategory, "002", "گیج فشار");
            yield return V(CodingSchemes.ToolCategory, "003", "گیج دما");
            yield return V(CodingSchemes.ToolCategory, "004", "سوهان");
            yield return V(CodingSchemes.ToolCategory, "005", "مته");
            yield return V(CodingSchemes.ConsumableCategory, "001", "پرینتر");
            yield return V(CodingSchemes.ConsumableCategory, "002", "خودکار");
            yield return V(CodingSchemes.ConsumableCategory, "003", "کاغذ");
            yield return V(CodingSchemes.AssetCategory, "01", "اداری");
            yield return V(CodingSchemes.AssetCategory, "02", "ماشین‌آلات صنعتی");
            yield return V(CodingSchemes.AssetCategory, "03", "گیج");
            yield return V(CodingSchemes.AssetCategory, "04", "ابزارآلات");
        }
    }

    internal static class InventoryCodingData
    {
        /// <summary>Lookup tables of the coding, seeded with the defaults of WI-WH-002 the first time they are needed.</summary>
        public static async Task<List<CodingValue>> LoadAsync(IUnitOfWork uow, int userId, CancellationToken cancellationToken)
        {
            DbSet<CodingValue> set = uow.Set<CodingValue>();
            List<CodingValue> values = await set.AsNoTracking().Where(v => !v.Deleted).ToListAsync(cancellationToken);
            if (values.Count > 0 || await set.AnyAsync(cancellationToken)) return values;
            DateTimeOffset now = DateTimeOffset.Now;
            var seed = ItemCodeRules.DefaultValues().ToList();
            foreach (CodingValue value in seed)
            {
                value.CreateDate = now;
                value.CreatorUserId = userId;
                set.Add(value);
            }
            try
            {
                await uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // another request seeded the tables at the same moment
                foreach (CodingValue value in seed) uow.Entry(value).State = EntityState.Detached;
            }
            return await set.AsNoTracking().Where(v => !v.Deleted).ToListAsync(cancellationToken);
        }
    }

    public interface IInventoryCodingService
    {
        Task<ResponseDto<List<CodingValueDto>>> GetValuesAsync(string scheme, CancellationToken cancellationToken);
        Task<ResponseDto<CodingValueDto>> SaveValueAsync(SaveCodingValueDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<CodeCheckDto>> CheckCodeAsync(string code, int? itemId, CancellationToken cancellationToken);
        Task<ResponseDto<List<ItemRequestDto>>> GetRequestsAsync(ItemRequestStatus? status, CancellationToken cancellationToken);
        Task<ResponseDto<ItemRequestDto>> SaveRequestAsync(SaveItemRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItemRequestDto>> ReviewRequestAsync(ReviewItemRequestDto dto, CancellationToken cancellationToken);

        /// <summary>Lookup tables, seeded with the defaults of WI-WH-002 the first time they are needed.</summary>
        Task<List<CodingValue>> LoadValuesAsync(CancellationToken cancellationToken);
    }

    public class InventoryCodingService : IInventoryCodingService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryMasterService _master;

        public InventoryCodingService(IUnitOfWork uow, IInventoryUserContext user, IInventoryMasterService master)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _master = master ?? throw new ArgumentNullException(nameof(master));
        }

        private DbSet<CodingValue> Values => _uow.Set<CodingValue>();

        public Task<List<CodingValue>> LoadValuesAsync(CancellationToken cancellationToken) =>
            InventoryCodingData.LoadAsync(_uow, _user.UserId, cancellationToken);

        private static CodingValueDto ToDto(CodingValue v) => new CodingValueDto
        {
            Id = v.Id, Scheme = v.Scheme, Code = v.Code, Title = v.Title, Model = v.Model, RangeFrom = v.RangeFrom, RangeTo = v.RangeTo,
            IsStockable = v.IsStockable, SortOrder = v.SortOrder, IsActive = v.IsActive, Description = v.Description,
        };

        public async Task<ResponseDto<List<CodingValueDto>>> GetValuesAsync(string scheme, CancellationToken cancellationToken)
        {
            List<CodingValue> values = await LoadValuesAsync(cancellationToken);
            scheme = InventoryText.Clean(scheme);
            return ResponseDtoHelper.Ok(values.Where(v => scheme == null || v.Scheme == scheme)
                .OrderBy(v => v.Scheme).ThenBy(v => v.SortOrder).ThenBy(v => v.Code).Select(ToDto).ToList());
        }

        public async Task<ResponseDto<CodingValueDto>> SaveValueAsync(SaveCodingValueDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<CodingValueDto>(InventoryMessages.InvalidData);
            string scheme = InventoryText.Clean(dto.Scheme);
            string code = ItemCodeRules.Normalize(dto.Code);
            string title = InventoryText.Clean(dto.Title);
            if (scheme == null || !CodingSchemes.All.Contains(scheme)) return ResponseDtoHelper.BadRequest<CodingValueDto>("جدول کدینگ نامعتبر است.");
            if (code == null) return ResponseDtoHelper.BadRequest<CodingValueDto>(InventoryMessages.CodeRequired);
            if (title == null) return ResponseDtoHelper.BadRequest<CodingValueDto>(InventoryMessages.TitleRequired);
            if (scheme == CodingSchemes.Assembly && (!dto.RangeFrom.HasValue || !dto.RangeTo.HasValue || dto.RangeFrom > dto.RangeTo))
                return ResponseDtoHelper.BadRequest<CodingValueDto>("محدوده شماره مجموعه (از / تا) را درست وارد کنید.");
            await LoadValuesAsync(cancellationToken);
            string model = InventoryText.Clean(dto.Model);
            if (await Values.AnyAsync(v => !v.Deleted && v.Scheme == scheme && v.Model == model && v.Code == code && v.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<CodingValueDto>(InventoryMessages.DuplicateCode + code);

            CodingValue value;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                value = await Values.FirstOrDefaultAsync(v => v.Id == dto.Id.Value && !v.Deleted, cancellationToken);
                if (value == null) return ResponseDtoHelper.NotFound<CodingValueDto>(InventoryMessages.NotFound);
            }
            else
            {
                value = new CodingValue { CreateDate = now, CreatorUserId = _user.UserId };
                Values.Add(value);
            }
            value.Scheme = scheme;
            value.Code = code;
            value.Title = title;
            value.Model = model;
            value.RangeFrom = dto.RangeFrom;
            value.RangeTo = dto.RangeTo;
            value.IsStockable = dto.IsStockable;
            value.SortOrder = dto.SortOrder;
            value.IsActive = dto.IsActive;
            value.Description = InventoryText.Clean(dto.Description);
            value.ModifyDate = now;
            value.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(value));
        }

        public async Task<ResponseDto<CodeCheckDto>> CheckCodeAsync(string code, int? itemId, CancellationToken cancellationToken)
        {
            List<CodingValue> values = await LoadValuesAsync(cancellationToken);
            CodeCheckDto check = ItemCodeRules.Check(code, values);
            if (check.Code == null) return ResponseDtoHelper.Ok(check);

            IQueryable<InventoryItem> others = _uow.Set<InventoryItem>().Where(i => !i.Deleted && i.Id != (itemId ?? 0));
            if (await others.AnyAsync(i => i.Code == check.Code, cancellationToken))
                check.Errors.Add(InventoryMessages.DuplicateCode + check.Code);
            if (check.MescCode != null && await others.AnyAsync(i => i.MescCode == check.MescCode, cancellationToken))
                check.Errors.Add("قطعه‌ای با همین کد MESC قبلاً ثبت شده است.");
            if (check.BaseCode != null && check.StageCode != null)
            {
                check.OtherStages = await others.Where(i => i.BaseCode == check.BaseCode)
                    .OrderBy(i => i.StageCode).Select(i => i.Code + " — " + i.Title).Take(20).ToListAsync(cancellationToken);
            }
            check.IsValid = check.Errors.Count == 0;
            return ResponseDtoHelper.Ok(check);
        }

        // ------------------------------------------------------------------------- item requests

        private IQueryable<ItemRequestDto> RequestProjection(IQueryable<ItemRequest> source) => source.Select(r => new ItemRequestDto
        {
            Id = r.Id, Number = r.Number, Title = r.Title, GroupCode = r.GroupCode, ProposedCode = r.ProposedCode,
            TechnicalCode = r.TechnicalCode, Material = r.Material, Standard = r.Standard, MescCode = r.MescCode, Unit = r.Unit,
            TrackingMode = r.TrackingMode, Specification = r.Specification, Reason = r.Reason, Status = r.Status,
            ReviewedAt = r.ReviewedAt, ReviewNote = r.ReviewNote, ItemId = r.ItemId, ItemCode = r.Item.Code,
            CreateDate = r.CreateDate, CreatorUserId = r.CreatorUserId, ReviewedByUserId = r.ReviewedByUserId,
        });

        private async Task<List<ItemRequestDto>> WithNamesAsync(List<ItemRequestDto> rows, CancellationToken cancellationToken)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                rows.SelectMany(r => new int?[] { r.CreatorUserId, r.ReviewedByUserId }), cancellationToken);
            foreach (ItemRequestDto row in rows)
            {
                row.CreatorName = names.Name(row.CreatorUserId);
                row.ReviewedByName = names.Name(row.ReviewedByUserId);
            }
            return rows;
        }

        public async Task<ResponseDto<List<ItemRequestDto>>> GetRequestsAsync(ItemRequestStatus? status, CancellationToken cancellationToken)
        {
            IQueryable<ItemRequest> requests = _uow.Set<ItemRequest>().Where(r => !r.Deleted);
            if (status.HasValue) requests = requests.Where(r => r.Status == status.Value);
            List<ItemRequestDto> rows = await RequestProjection(requests.OrderByDescending(r => r.Id)).Take(500).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await WithNamesAsync(rows, cancellationToken));
        }

        private async Task<ItemRequestDto> GetRequestAsync(int id, CancellationToken cancellationToken)
        {
            List<ItemRequestDto> rows = await RequestProjection(_uow.Set<ItemRequest>().Where(r => r.Id == id)).ToListAsync(cancellationToken);
            return (await WithNamesAsync(rows, cancellationToken)).FirstOrDefault();
        }

        public async Task<ResponseDto<ItemRequestDto>> SaveRequestAsync(SaveItemRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>(InventoryMessages.InvalidData);
            string title = InventoryText.Clean(dto.Title);
            if (title == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>(InventoryMessages.TitleRequired);
            if (InventoryText.Clean(dto.Unit) == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>(InventoryMessages.UnitRequired);
            string group = ItemCodeRules.Normalize(dto.GroupCode);
            if (group == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>("گروه کالا را انتخاب کنید.");
            if (group == ItemCodeRules.StandardPartGroup && InventoryText.Clean(dto.MescCode) == null)
                return ResponseDtoHelper.BadRequest<ItemRequestDto>("برای قطعه استاندارد کد MESC را وارد کنید.");
            if (group == "02" && InventoryText.Clean(dto.TechnicalCode) == null)
                return ResponseDtoHelper.BadRequest<ItemRequestDto>("برای قطعه، شماره نقشه را وارد کنید.");

            ItemRequest request;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                request = await _uow.Set<ItemRequest>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (request == null) return ResponseDtoHelper.NotFound<ItemRequestDto>(InventoryMessages.NotFound);
                if (request.Status != ItemRequestStatus.Submitted) return ResponseDtoHelper.BadRequest<ItemRequestDto>("درخواست بررسی‌شده قابل ویرایش نیست.");
                if (request.CreatorUserId != _user.UserId && !_user.IsInRole(InventoryRoles.RolePlanning))
                    return ResponseDtoHelper.BadRequest<ItemRequestDto>(InventoryMessages.NotAllowed);
            }
            else
            {
                request = new ItemRequest
                {
                    Status = ItemRequestStatus.Submitted,
                    CreateDate = now,
                    CreatorUserId = _user.UserId,
                    Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.ItemRequest, "IR", now.DateTime, cancellationToken),
                };
                _uow.Set<ItemRequest>().Add(request);
            }
            request.Title = title;
            request.GroupCode = group;
            request.ProposedCode = ItemCodeRules.Normalize(dto.ProposedCode);
            request.TechnicalCode = InventoryText.Clean(dto.TechnicalCode);
            request.Material = InventoryText.Clean(dto.Material);
            request.Standard = InventoryText.Clean(dto.Standard);
            request.MescCode = ItemCodeRules.Normalize(dto.MescCode);
            request.Unit = InventoryText.Clean(dto.Unit);
            request.TrackingMode = dto.TrackingMode;
            request.Specification = InventoryText.Clean(dto.Specification);
            request.Reason = InventoryText.Clean(dto.Reason);
            request.ModifyDate = now;
            request.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await GetRequestAsync(request.Id, cancellationToken));
        }

        public async Task<ResponseDto<ItemRequestDto>> ReviewRequestAsync(ReviewItemRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>(InventoryMessages.InvalidData);
            ItemRequest request = await _uow.Set<ItemRequest>().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
            if (request == null) return ResponseDtoHelper.NotFound<ItemRequestDto>(InventoryMessages.NotFound);
            if (request.Status != ItemRequestStatus.Submitted) return ResponseDtoHelper.BadRequest<ItemRequestDto>("این درخواست قبلاً بررسی شده است.");
            DateTimeOffset now = DateTimeOffset.Now;

            if (!dto.Approve)
            {
                string note = InventoryText.Clean(dto.Note);
                if (note == null) return ResponseDtoHelper.BadRequest<ItemRequestDto>("علت رد درخواست را بنویسید.");
                request.Status = ItemRequestStatus.Rejected;
                request.ReviewNote = note;
            }
            else
            {
                string code = ItemCodeRules.Normalize(dto.Code) ?? request.ProposedCode ??
                              (request.GroupCode == ItemCodeRules.StandardPartGroup ? request.MescCode : null);
                ResponseDto<InventoryItemDto> created = await _master.SaveItemAsync(new SaveInventoryItemDto
                {
                    Code = code,
                    Title = request.Title,
                    Unit = request.Unit,
                    TrackingMode = request.TrackingMode,
                    TechnicalCode = request.TechnicalCode,
                    Material = request.Material,
                    Standard = request.Standard,
                    Specification = request.Specification,
                    MescCode = request.MescCode,
                    IsActive = true,
                    RequiresIncomingInspection = Quality.ItemQualityRules.DefaultInspection(request.GroupCode),
                }, cancellationToken);
                if (!created.Success) return ResponseDtoHelper.BadRequest<ItemRequestDto>(created.Message);
                request.Status = ItemRequestStatus.Approved;
                request.ItemId = created.Data.Id;
                request.ReviewNote = InventoryText.Clean(dto.Note);
            }
            request.ReviewedAt = now;
            request.ReviewedByUserId = _user.UserId;
            request.ModifyDate = now;
            request.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await GetRequestAsync(request.Id, cancellationToken));
        }
    }
}
