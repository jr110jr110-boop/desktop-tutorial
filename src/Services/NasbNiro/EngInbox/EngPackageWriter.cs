using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Services.NasbNiro.Finance;
using FinSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>The complete fictional reference package (a small pump-like assembly) with its document files.</summary>
    public sealed class EngSamplePackage
    {
        public string FolderName { get; set; }
        public string WorkbookName { get; set; }
        public byte[] Workbook { get; set; }

        /// <summary>The referenced document files by file name.</summary>
        public Dictionary<string, byte[]> Files { get; set; } = new Dictionary<string, byte[]>();
    }

    /// <summary>The cells of the five sheets before they are written (the unit tests change them to make a broken package).</summary>
    public sealed class EngTables
    {
        public List<object[]> Info { get; set; } = new List<object[]>();
        public List<object[]> Bom { get; set; } = new List<object[]>();
        public List<object[]> Opc { get; set; } = new List<object[]>();
        public List<object[]> Docs { get; set; } = new List<object[]>();
        public List<object[]> Mdl { get; set; } = new List<object[]>();
        public string[] BomHead { get; set; }
        public string[] OpcHead { get; set; }
        public string[] DocHead { get; set; }
        public string[] MdlHead { get; set; }
        public Dictionary<string, byte[]> Files { get; set; } = new Dictionary<string, byte[]>();
    }

    /// <summary>
    /// Writes the empty template and the reference sample of the package workbook with the in-house xlsx writer (SimpleXlsx; no library). The sample is fictional ("TEST-PLT-01", part numbers TST-xxxx) and passes the validation with no
    /// error and no warning. Its work-centre codes are MF-LATHE and MF-QCB (the two the smoke data creates); an installation replaces them with its own codes.
    /// </summary>
    public static class EngPackageWriter
    {
        public const string SamplePlatformCode = "TEST-PLT-01";
        public const string SampleStandard = "API 610";
        public const string WorkbookFileName = "ENG-PACKAGE.xlsx";

        private static readonly string[] BomHead = { "سطح", "کد قطعه والد", "کد قطعه", "شرح", "تعداد", "واحد", "ساخت/خرید", "متریال", "شماره نقشه", "بازنگری نقشه", "یادداشت" };
        private static readonly string[] OpcHead = { "کد قطعه", "شماره عملیات", "شرح عملیات", "مرکز کاری", "زمان آماده‌سازی (دقیقه)", "زمان اجرا (دقیقه)", "بازرسی", "فرایند ویژه", "پیش‌نیاز", "ایستگاه" };
        private static readonly string[] DocHead = { "شماره مدرک", "عنوان", "بازنگری", "نوع", "نام فایل", "الزامی" };
        private static readonly string[] MdlHead = { "شماره مدرک", "عنوان", "نوع", "تاریخ تعهد" };

        private static FinSheet Sheet(string name, string[] head, IEnumerable<object[]> rows)
        {
            var s = new FinSheet { Name = name };
            s.Header.AddRange(head);
            s.Rows.AddRange(rows);
            return s;
        }

        private static FinSheet InfoSheet(IEnumerable<object[]> rows) => Sheet(EngPackageParser.SheetInfo, new[] { "مورد", "مقدار", "توضیح" }, rows);

        /// <summary>The empty template: the headers and one explanation row (marked ※, skipped by the reader) per sheet.</summary>
        public static byte[] Template()
        {
            var info = new List<object[]>
            {
                new object[] { "نوع بسته", null, "پلتفرم یا پروژه" },
                new object[] { "کد پلتفرم", null, "برای بستهٔ پلتفرم الزامی؛ حرف و عدد لاتین، مثلا TEST-PLT-01" },
                new object[] { "استاندارد", null, "برای بستهٔ پلتفرم الزامی؛ مثلا API 611" },
                new object[] { "نسخه بسته", null, "عدد صحیح ۱ یا بیشتر؛ برای هر تغییر محتوا یکی بالاتر" },
                new object[] { "کد قرارداد", null, "فقط بستهٔ پروژه" },
                new object[] { "کد پروژه", null, "فقط بستهٔ پروژه" },
                new object[] { "عنوان", null, "عنوان بسته" },
                new object[] { "تهیه‌کننده", null, "نام تهیه‌کنندهٔ بسته (متن آزاد)" },
                new object[] { "تاریخ", null, "شمسی (۱۴۰۵/۰۸/۲۵) یا میلادی (2026-11-16)" },
                new object[] { "قالب", 1, "شمارهٔ قالب فایل؛ اکنون 1" },
            };
            return SimpleXlsx.Build(new List<FinSheet>
            {
                InfoSheet(info),
                Sheet(EngPackageParser.SheetBom, BomHead, new[] { new object[] { "※ هر سطر یک قطعه. ریشه والد ندارد. سطح هر قطعه یکی بیشتر از والد است. ساخت/خرید: ساخت، خرید، تامین توسط مشتری، پیمانکاری. واحد: عدد، کیلوگرم، متر ..." } }),
                Sheet(EngPackageParser.SheetOpc, OpcHead, new[] { new object[] { "※ هر سطر یک عملیات یک قطعه. مرکز کاری: کد مرکز کار. زمان‌ها به دقیقه. بازرسی و فرایند ویژه: بله یا خیر. پیش‌نیاز: شماره عملیات‌های همان قطعه با ویرگول. ایستگاه (اختیاری): کد دو حرفی مثل MC یا QC." } }),
                Sheet(EngPackageParser.SheetDocs, DocHead, new[] { new object[] { "※ هر سطر یک مدرک. نام فایل: فقط نام فایلِ داخل پوشهٔ همین بسته (بدون مسیر). الزامی: بله یا خیر." } }),
                Sheet(EngPackageParser.SheetMdl, MdlHead, new[] { new object[] { "※ فقط بستهٔ پروژه: فهرست مدارکی که مشتری در جلسهٔ شروع نهایی کرده است." } }),
            });
        }

        private static object[] B(int level, string parent, string code, string desc, decimal qty, string unit, string mb, string material, string drawing, string rev, string note) =>
            new object[] { level, parent, code, desc, qty, unit, mb, material, drawing, rev, note };

        private static object[] O(string part, int op, string desc, string wc, decimal setup, decimal run, string insp, string special, string pred, string stage) =>
            new object[] { part, op, desc, wc, setup, run, insp, special, pred, stage };

        /// <summary>The complete reference package (12 parts over 3 levels, 13 operations, 5 documents). The same content every time.</summary>
        public static EngSamplePackage Sample(int version = 1)
        {
            EngTables t = SampleTables(version);
            return new EngSamplePackage { FolderName = "pkg-v" + version, WorkbookName = WorkbookFileName, Files = t.Files, Workbook = Build(t) };
        }

        /// <summary>The workbook of the tables; the named sheets are left out (MDL is written only when it has rows).</summary>
        public static byte[] Build(EngTables t, params string[] omit)
        {
            var sheets = new List<FinSheet>();
            bool Skip(string n) => omit != null && omit.Contains(n);
            if (!Skip(EngPackageParser.SheetInfo)) sheets.Add(InfoSheet(t.Info));
            if (!Skip(EngPackageParser.SheetBom)) sheets.Add(Sheet(EngPackageParser.SheetBom, t.BomHead, t.Bom));
            if (!Skip(EngPackageParser.SheetOpc)) sheets.Add(Sheet(EngPackageParser.SheetOpc, t.OpcHead, t.Opc));
            if (!Skip(EngPackageParser.SheetDocs)) sheets.Add(Sheet(EngPackageParser.SheetDocs, t.DocHead, t.Docs));
            if (t.Mdl.Count > 0 && !Skip(EngPackageParser.SheetMdl)) sheets.Add(Sheet(EngPackageParser.SheetMdl, t.MdlHead, t.Mdl));
            return SimpleXlsx.Build(sheets);
        }

        public static EngTables SampleTables(int version = 1)
        {
            var files = new Dictionary<string, byte[]>
            {
                ["GA-TST-1000.txt"] = Encoding.UTF8.GetBytes("Sample general arrangement drawing placeholder for TEST-PLT-01 (fictional).\n"),
                ["DWG-TST-1110.txt"] = Encoding.UTF8.GetBytes("Sample casing drawing placeholder (fictional).\n"),
                ["DS-TST-01.txt"] = Encoding.UTF8.GetBytes("Sample data sheet placeholder (fictional).\n"),
                ["QP-TST-01.txt"] = Encoding.UTF8.GetBytes("Sample inspection plan placeholder (fictional).\n"),
            };
            var info = new List<object[]>
            {
                new object[] { "نوع بسته", "پلتفرم", null },
                new object[] { "کد پلتفرم", SamplePlatformCode, null },
                new object[] { "استاندارد", SampleStandard, null },
                new object[] { "نسخه بسته", version, null },
                new object[] { "عنوان", "پمپ نمونهٔ آزمایشی (فرضی) TEST-PLT-01", null },
                new object[] { "تهیه‌کننده", "واحد مهندسی (نمونه)", null },
                new object[] { "تاریخ", "1405/08/25", null },
                new object[] { "قالب", 1, null },
            };
            var bom = new List<object[]>
            {
                B(1, null, "TST-1000", "مجموعهٔ پمپ نمونه", 1, "عدد", "ساخت", null, "GA-TST-1000", "A", "ریشه"),
                B(2, "TST-1000", "TST-1100", "مجموعهٔ پوسته", 1, "عدد", "ساخت", null, null, null, null),
                B(3, "TST-1100", "TST-1110", "بدنهٔ پوسته", 1, "عدد", "ساخت", "فولاد ریخته‌گری", "DWG-TST-1110", "A", null),
                B(3, "TST-1100", "TST-1120", "درپوش پوسته", 1, "عدد", "ساخت", "فولاد ریخته‌گری", null, null, null),
                B(3, "TST-1100", "TST-1130", "واشر پوسته", 2, "عدد", "خرید", "گرافیت", null, null, null),
                B(2, "TST-1000", "TST-1200", "مجموعهٔ روتور", 1, "عدد", "ساخت", null, null, null, null),
                B(3, "TST-1200", "TST-1210", "شفت", 1, "عدد", "ساخت", "فولاد زنگ‌نزن", null, null, null),
                B(3, "TST-1200", "TST-1220", "پروانه", 1, "عدد", "ساخت", "فولاد زنگ‌نزن", null, null, null),
                B(3, "TST-1200", "TST-1230", "خار شفت", 1, "عدد", "خرید", "فولاد", null, null, null),
                B(2, "TST-1000", "TST-1300", "محفظهٔ یاتاقان", 1, "عدد", "خرید", null, null, null, null),
                B(2, "TST-1000", "TST-1400", "مکانیکال سیل", 1, "ست", "خرید", null, null, null, null),
                B(2, "TST-1000", "TST-1500", "شاسی پایه", 1, "عدد", "ساخت", "فولاد ساختمانی", null, null, null),
            };
            var opc = new List<object[]>
            {
                O("TST-1110", 10, "ماشین‌کاری خشن بدنه", "MF-LATHE", 30, 120, "خیر", "خیر", null, "RG"),
                O("TST-1110", 20, "ماشین‌کاری نهایی بدنه", "MF-LATHE", 20, 90, "خیر", "خیر", "10", "FM"),
                O("TST-1110", 30, "بازرسی ابعادی بدنه", "MF-QCB", 5, 30, "بله", "خیر", "20", "QC"),
                O("TST-1120", 10, "ماشین‌کاری درپوش", "MF-LATHE", 20, 60, "خیر", "خیر", null, "MC"),
                O("TST-1120", 20, "بازرسی درپوش", "MF-QCB", 5, 15, "بله", "خیر", "10", "QC"),
                O("TST-1210", 10, "تراش خشن شفت", "MF-LATHE", 25, 70, "خیر", "خیر", null, "RL"),
                O("TST-1210", 20, "تراش نهایی شفت", "MF-LATHE", 15, 50, "خیر", "خیر", "10", "FL"),
                O("TST-1210", 30, "بازرسی ران‌اوت شفت", "MF-QCB", 5, 20, "بله", "خیر", "20", "RT"),
                O("TST-1220", 10, "ماشین‌کاری پروانه", "MF-LATHE", 30, 100, "خیر", "خیر", null, "MC"),
                O("TST-1500", 10, "برش و جوشکاری شاسی", "MF-LATHE", 15, 180, "خیر", "بله", null, "WE"),
                O("TST-1100", 10, "مونتاژ پوسته", "MF-LATHE", 10, 45, "خیر", "خیر", null, "AS"),
                O("TST-1200", 10, "مونتاژ روتور", "MF-LATHE", 10, 60, "خیر", "خیر", null, "AS"),
                O("TST-1000", 10, "مونتاژ نهایی پمپ", "MF-LATHE", 15, 120, "خیر", "خیر", null, "AS"),
            };
            var docs = new List<object[]>
            {
                new object[] { "GA-TST-1000", "نقشهٔ کلی پمپ", "A", "نقشه", "GA-TST-1000.txt", "بله" },
                new object[] { "DWG-TST-1110", "نقشهٔ ساخت بدنهٔ پوسته", "A", "نقشه", "DWG-TST-1110.txt", "بله" },
                new object[] { "DS-TST-01", "برگهٔ داده (Data Sheet)", "0", "داده", "DS-TST-01.txt", "خیر" },
                new object[] { "QP-TST-01", "برنامهٔ بازرسی", "0", "کیفیت", "QP-TST-01.txt", "بله" },
                new object[] { "MAN-TST-01", "دفترچهٔ راهنما (فایل جداگانه ارسال می‌شود)", "0", "راهنما", null, "خیر" },
            };
            return new EngTables { Info = info, Bom = bom, Opc = opc, Docs = docs, BomHead = BomHead.ToArray(), OpcHead = OpcHead.ToArray(), DocHead = DocHead.ToArray(), MdlHead = MdlHead.ToArray(), Files = files };
        }

        /// <summary>The sample as one zip: the folder of the package with the workbook and its files (unzip it into Platforms/TEST-PLT-01/API 610/).</summary>
        public static byte[] SampleZip(int version = 1)
        {
            EngSamplePackage s = Sample(version);
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    void Add(string name, byte[] data)
                    {
                        ZipArchiveEntry e = zip.CreateEntry(s.FolderName + "/" + name, CompressionLevel.Optimal);
                        e.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (Stream st = e.Open()) st.Write(data, 0, data.Length);
                    }
                    Add(s.WorkbookName, s.Workbook);
                    foreach (KeyValuePair<string, byte[]> f in s.Files.OrderBy(x => x.Key, StringComparer.Ordinal)) Add(f.Key, f.Value);
                }
                return ms.ToArray();
            }
        }

        /// <summary>The file facts the parser needs for a sample (what the folder source reads from the disk).</summary>
        public static Dictionary<string, EngFileInfo> FactsOf(EngSamplePackage s) =>
            s.Files.ToDictionary(f => f.Key, f => new EngFileInfo { Name = f.Key, Size = f.Value.Length, Sha256 = EngText.Sha256Hex(f.Value) }, StringComparer.Ordinal);
    }
}
