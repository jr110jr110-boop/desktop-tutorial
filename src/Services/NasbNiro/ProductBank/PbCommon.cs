using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>Roles of the product bank. Reads need a signed-in user only (like the item master); every change names its roles.</summary>
    public static class PbRoles
    {
        /// <summary>Item coding by part number (stage items, mapping drafts): engineering and planning.</summary>
        public const string Coding = "PMO,PLANNING,ENGINEERING,ENGINEER";

        /// <summary>Writes a BOM, an OPC routing or a template draft.</summary>
        public const string Writers = "PMO,ENGINEERING,ENGINEER";

        /// <summary>Signature of the BOM block: checked, QC checked, manufacturing checked, approved. Each one is a different person.</summary>
        public const string CheckRoles = "ENGINEERING,ENGINEER";
        public const string QcRoles = "QC,QA";
        public const string ManufacturingRoles = "CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR";
        public const string ApproveRoles = "ENGINEERING,PMO";
        public const string ReleaseRoles = "ENGINEERING,PMO";

        /// <summary>Everyone who can sign a step of the BOM block or send a revision back to the preparer.</summary>
        public const string Reviewers = "PMO,ENGINEERING,ENGINEER,QC,QA,CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR";

        /// <summary>Approves a mapping (engineering) and a template / deviation (engineering, and a second person of planning).</summary>
        public const string Engineering = "ENGINEERING,PMO";
        public const string Planning = "PLANNING,PMO";

        /// <summary>Requests a project deviation.</summary>
        public const string Requesters = "PMO,ENGINEERING,ENGINEER,PLANNING,PROJECTMANAGER,CONSTRUCTION,PRODUCTIONMANAGER";

        /// <summary>Installs the demonstration data of the company's own product.</summary>
        public const string Admin = "PMO";
    }

    public static class PbMessages
    {
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string AiNeverApproves = "تایید و امضا با هوش مصنوعی انجام نمی‌شود؛ یک کاربر انسانی باید آن را انجام دهد.";
        public const string NotFound = "پیدا نشد.";
        public const string OnlyDraft = "فقط نسخه پیش‌نویس قابل ویرایش است؛ برای تغییر نسخه جدید بسازید.";
        public const string SameUser = "تهیه‌کننده یا سازندهٔ سند آن را کنترل، تایید یا منتشر نمی‌کند؛ امضاکننده باید فرد دیگری باشد.";
        public const string SameUserReviewers = "طبق تنظیم شرکت، کنترل‌کننده، کنترل کیفیت، کنترل ساخت و تاییدکننده باید افراد متفاوت باشند و شما قبلاً امضا کرده‌اید.";
        public const string PartNumberRequired = "شماره قطعه را وارد کنید.";
        public const string StageUnknown = "کد مرحله در فهرست مراحل نیست یا غیرفعال است.";
        public const string Concurrent = "همزمان کس دیگری همین سند را تغییر داد؛ صفحه را دوباره بارگذاری کنید.";
        public const string PartNumberShape = "شماره قطعه فقط حرف و عدد انگلیسی، نقطه، خط تیره و اسلش دارد، حداکثر ۴۰ نشانه است و به «نشانه + دو حرف» (شبیه کد مرحله) ختم نمی‌شود.";
    }

    /// <summary>The keys of PbSetting (★ provisional company decisions, editable by planning).</summary>
    public static class PbSettingKeys
    {
        /// <summary>Text between the part number and the stage code of a stage item (16.11.02.001.2-RL).</summary>
        public const string StageSeparator = "StageSeparator";

        /// <summary>Confidence (0-100) below which a suggested mapping is not even proposed.</summary>
        public const string MinSuggestConfidence = "MinSuggestConfidence";

        /// <summary>★ true = the checked, QC checked, manufacturing checked and approved signatures of a BOM must also differ from each other. The preparer always differs from every other signer. Default false (company practice: one person may sign several of the four).</summary>
        public const string DistinctReviewers = "BomSignatures:DistinctReviewers";

        /// <summary>★ true = the demonstration data of the company's own product may be installed (a demo or test installation). Off in production installs.</summary>
        public const string DemoData = "DemoData:Enabled";

        /// <summary>Marker of the one-time adoption of existing items (their code or MESC code is a part number) as part items.</summary>
        public const string AdoptMarker = "Backfill:AdoptPartNumbers";

        public static bool IsBool(string value) => value == "true" || value == "false";

        public static IEnumerable<PbSetting> Defaults()
        {
            yield return new PbSetting { Key = StageSeparator, Value = "-", Title = "جداکننده شماره قطعه و کد مرحله", Source = "★ تصمیم برنامه (به تایید شرکت)" };
            yield return new PbSetting { Key = MinSuggestConfidence, Value = "50", Title = "کمترین اطمینان پیشنهاد نگاشت خودکار (درصد)", Source = "★ تصمیم برنامه (به تایید شرکت)" };
            yield return new PbSetting { Key = DistinctReviewers, Value = "false", Title = "امضاکنندگان کنترل، کنترل کیفیت، کنترل ساخت و تایید BOM افراد متفاوت باشند", Source = "★ روال شرکت: یک نفر می‌تواند چند امضا بزند (به تایید شرکت)؛ تهیه‌کننده همیشه با همه امضاکنندگان متفاوت است" };
            yield return new PbSetting { Key = DemoData, Value = "false", Title = "نصب داده‌های نمونه (محصول FY) مجاز باشد", Source = "★ فقط در نصب آزمایشی یا نمایشی روشن شود؛ در نصب اصلی خاموش است" };
        }

        public static async Task<Dictionary<string, string>> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, string> result = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<PbSetting> rows = await uow.Set<PbSetting>().AsNoTracking().ToListAsync(ct);
            foreach (PbSetting row in rows) if (row.Value != null) result[row.Key] = row.Value;
            return result;
        }
    }

    /// <summary>The stage dictionary of the OPC charts: two letters each, no duplicates (RM stood for raw material and for rough machining; they are RW and RG here). ★ to be confirmed by the company.</summary>
    public static class PbStageDefaults
    {
        public static IEnumerable<PbStage> All()
        {
            int n = 0;
            PbStage S(string code, string fa, string en, PbStageKind kind, bool stockable = false) =>
                new PbStage { Code = code, TitleFa = fa, TitleEn = en, Kind = kind, IsStockable = stockable, SortOrder = ++n, IsActive = true };
            yield return S("PO", "سفارش و خرید", "Purchase order", PbStageKind.Operation);
            yield return S("RW", "ماده اولیه تحویل‌شده", "Raw / as received", PbStageKind.Storage, true);
            yield return S("CV", "بررسی گواهی و ابعاد ماده تامین‌کننده", "Check vendor material certificate and dimension", PbStageKind.Qc);
            yield return S("SP", "نمونه‌برداری", "Sampling", PbStageKind.Operation);
            yield return S("CA", "ریخته‌گری", "Casting", PbStageKind.Operation, true);
            yield return S("CT", "برش", "Cutting", PbStageKind.Operation, true);
            yield return S("PN", "پانچ", "Punching", PbStageKind.Operation);
            yield return S("WC", "وایرکات", "Wire cut", PbStageKind.Operation);
            yield return S("SB", "شات‌بلاست / سندبلاست", "Shot blast / sand blast", PbStageKind.Operation);
            yield return S("HT", "عملیات حرارتی", "Heat treatment", PbStageKind.Operation, true);
            yield return S("NI", "عملیات حرارتی نیتراید", "Nitriding heat treatment", PbStageKind.Operation);
            yield return S("RG", "ماشین‌کاری خشن", "Rough machining", PbStageKind.Operation, true);
            yield return S("RL", "تراش خشن", "Rough lathing", PbStageKind.Operation, true);
            yield return S("LA", "تراش", "Lathing", PbStageKind.Operation);
            yield return S("SL", "تراش نیمه‌نهایی", "Semi-final lathing", PbStageKind.Operation, true);
            yield return S("FL", "تراش نهایی", "Final lathing", PbStageKind.Operation, true);
            yield return S("ML", "فرزکاری", "Milling", PbStageKind.Operation, true);
            yield return S("GR", "سنگ‌زنی", "Grinding", PbStageKind.Operation);
            yield return S("FG", "سنگ‌زنی نهایی", "Final grinding", PbStageKind.Operation);
            yield return S("DR", "سوراخ‌کاری و رزوه", "Drilling and threading", PbStageKind.Operation);
            yield return S("SD", "سوپر دریل", "Super drill", PbStageKind.Operation);
            yield return S("JM", "ماشین‌کاری مفصل (جوینت)", "Joint machining", PbStageKind.Operation);
            yield return S("MC", "ماشین‌کاری", "Machining", PbStageKind.Operation, true);
            yield return S("FM", "ماشین‌کاری نهایی", "Final machining", PbStageKind.Operation, true);
            yield return S("WE", "جوشکاری", "Welding", PbStageKind.Operation);
            yield return S("FO", "شکل‌دهی (فنر)", "Forming", PbStageKind.Operation);
            yield return S("BL", "بالانس", "Balancing", PbStageKind.Operation);
            yield return S("SA", "مونتاژ انقباضی", "Shrink assembly", PbStageKind.Operation);
            yield return S("AS", "مونتاژ", "Assembly", PbStageKind.Operation, true);
            yield return S("HY", "تست هیدرو", "Hydro test", PbStageKind.Operation);
            yield return S("PK", "بسته‌بندی و محافظت", "Packing and preservation", PbStageKind.Operation, true);
            yield return S("QC", "کنترل کیفیت", "Quality control", PbStageKind.Qc);
            yield return S("NT", "آزمون غیرمخرب", "NDT", PbStageKind.Qc);
            yield return S("TH", "آزمون کشش و سختی", "Tensile and hardness test", PbStageKind.Qc);
            yield return S("RT", "آزمون ران‌اوت و ابعادی", "Run-out and dimensional test", PbStageKind.Qc);
            yield return S("TR", "حمل و جابه‌جایی", "Transportation", PbStageKind.Transport);
            yield return S("ST", "انبار", "Storage", PbStageKind.Storage);
            yield return S("IH", "توقف بازرسی", "Inspection hold", PbStageKind.InspectionHold);
            yield return S("FN", "قطعه نهایی", "Finished part", PbStageKind.Storage, true);
        }
    }

    public static class PbText
    {
        public static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Part numbers are written without spaces, with Latin digits, in upper case.</summary>
        public static string PartNumber(string value) => ItemCodeRules.Normalize(value);

        public static string Stage(string value)
        {
            value = Clean(value);
            return value?.ToUpperInvariant();
        }

        public static string Revision(int revision) => revision.ToString("00", CultureInfo.InvariantCulture);

        public static string Num(decimal? value) => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";

        public static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
        }

        /// <summary>The words of a title in lower case without punctuation, for the match between item descriptions and part names.</summary>
        public static List<string> Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var sb = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            return sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1 && w != "fy" && w != "3fy" && w != "قطعه" && w != "خام" && w != "ریخته" && w != "گری" && w != "قطعه").Distinct().ToList();
        }

        /// <summary>0 to 100: the share of the words of the shorter text found in the longer one, with a bonus for the same material.</summary>
        public static decimal Similarity(string a, string b, string materialA = null, string materialB = null)
        {
            List<string> wa = Words(a), wb = Words(b);
            if (wa.Count == 0 || wb.Count == 0) return 0;
            int common = wa.Count(x => wb.Contains(x));
            decimal share = 100m * common / Math.Min(wa.Count, wb.Count);
            decimal union = 100m * common / wa.Union(wb).Count();
            decimal score = (share + union) / 2m;
            if (!string.IsNullOrWhiteSpace(materialA) && !string.IsNullOrWhiteSpace(materialB))
            {
                List<string> ma = Words(materialA), mb = Words(materialB);
                if (ma.Count > 0 && mb.Count > 0 && ma.Any(x => mb.Contains(x))) score = Math.Min(100m, score + 10m);
            }
            return Math.Round(Math.Min(100m, score), 2);
        }
    }

    /// <summary>The rule "code = part number + separator + stage".</summary>
    public static class PbCodeRules
    {
        public static string StageItemCode(string partNumber, string stage, string separator) =>
            PbText.PartNumber(partNumber) + (separator ?? "-") + PbText.Stage(stage);

        public const int MaxTitle = 300;
        public const int MaxPartNumber = 40;
        private static readonly System.Text.RegularExpressions.Regex ShapeRx = new System.Text.RegularExpressions.Regex(@"^[A-Z0-9][A-Z0-9._/\-]{0,39}$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex LooksLikeStageRx = new System.Text.RegularExpressions.Regex(@"[^A-Z0-9][A-Z]{2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The Persian error when a part number is not acceptable anywhere in the module (BOM line, OPC step, mapping, coding): the shape, at most 40 characters (so that part number + separator + stage fits the 50-character item code), and it must not end with a separator-like character and two letters, which would read as a stage code.</summary>
        public static string ShapeError(string partNumber)
        {
            if (partNumber == null) return PbMessages.PartNumberRequired;
            if (!ShapeRx.IsMatch(partNumber) || LooksLikeStageRx.IsMatch(partNumber)) return PbMessages.PartNumberShape;
            return null;
        }

        public static string StageItemTitle(string partTitle, PbStage stage)
        {
            string title = (partTitle ?? "").Trim() + " — " + stage.TitleFa;
            return title.Length <= MaxTitle ? title : title.Substring(0, MaxTitle);
        }

        /// <summary>The group code of an item made from a part number: 16.xx parts are made (02), the numbered standard parts are bought (08).</summary>
        public static string GroupOf(string partNumber)
        {
            string pn = PbText.PartNumber(partNumber) ?? "";
            if (pn.StartsWith("16.", StringComparison.Ordinal)) return "02";
            return "08";
        }
    }
}
