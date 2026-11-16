using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProductBank;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>
    /// Reads and validates the workbook of an engineering package. Pure (no database, no clock, no network): everything that needs the database (work-centre codes, the contract, the product bank) is checked at apply time.
    /// Every finding is collected (sheet, row, Persian message, error or warning); nothing stops at the first one. Headers are matched by normalised text, never by column position.
    /// </summary>
    public static class EngPackageParser
    {
        public const string SheetInfo = "مشخصات";
        public const string SheetBom = "BOM";
        public const string SheetOpc = "OPC";
        public const string SheetDocs = "مدارک";
        public const string SheetMdl = "MDL";

        private static readonly string[] InfoNames = { "مشخصات", "info" };
        private static readonly string[] BomNames = { "bom" };
        private static readonly string[] OpcNames = { "opc" };
        private static readonly string[] DocNames = { "مدارک", "documents" };
        private static readonly string[] MdlNames = { "mdl" };

        // logical column -> accepted header texts (normalised when compared)
        internal static readonly Dictionary<string, string[]> BomHeaders = new Dictionary<string, string[]>
        {
            ["level"] = new[] { "سطح", "level" },
            ["parent"] = new[] { "کد قطعه والد", "کد والد", "parent", "parent code", "parent part code" },
            ["code"] = new[] { "کد قطعه", "part code", "part", "part number" },
            ["desc"] = new[] { "شرح", "description", "desc" },
            ["qty"] = new[] { "تعداد", "qty", "quantity" },
            ["unit"] = new[] { "واحد", "unit", "uom" },
            ["mb"] = new[] { "ساخت/خرید", "ساخت خرید", "ساخت یا خرید", "make/buy", "make buy", "make or buy" },
            ["material"] = new[] { "متریال", "material" },
            ["drawing"] = new[] { "شماره نقشه", "drawing", "drawing no", "drawing number" },
            ["drawingRev"] = new[] { "بازنگری نقشه", "drawing rev", "drawing revision" },
            ["note"] = new[] { "یادداشت", "note", "notes", "remark" },
        };

        internal static readonly Dictionary<string, string[]> OpcHeaders = new Dictionary<string, string[]>
        {
            ["code"] = new[] { "کد قطعه", "part code", "part", "part number" },
            ["op"] = new[] { "شماره عملیات", "operation", "operation no", "operation number", "op" },
            ["desc"] = new[] { "شرح عملیات", "operation description", "description", "desc" },
            ["wc"] = new[] { "مرکز کاری", "مرکز کار", "work center", "work centre", "workcenter" },
            ["setup"] = new[] { "زمان آماده سازی", "زمان آماده‌سازی", "زمان آماده سازی (دقیقه)", "setup", "setup time", "setup min" },
            ["run"] = new[] { "زمان اجرا", "زمان اجرا (دقیقه)", "run", "run time", "run min" },
            ["insp"] = new[] { "بازرسی", "inspection" },
            ["special"] = new[] { "فرایند ویژه", "فرآیند ویژه", "special process", "special" },
            ["pred"] = new[] { "پیش نیاز", "پیش‌نیاز", "predecessors", "predecessor", "prerequisite" },
            ["stage"] = new[] { "ایستگاه", "کد ایستگاه", "stage", "stage code" },
        };

        internal static readonly Dictionary<string, string[]> DocHeaders = new Dictionary<string, string[]>
        {
            ["number"] = new[] { "شماره مدرک", "document number", "doc no", "number" },
            ["title"] = new[] { "عنوان", "title" },
            ["rev"] = new[] { "بازنگری", "revision", "rev" },
            ["type"] = new[] { "نوع", "type" },
            ["file"] = new[] { "نام فایل", "file", "file name", "filename" },
            ["required"] = new[] { "الزامی", "required", "mandatory" },
        };

        internal static readonly Dictionary<string, string[]> MdlHeaders = new Dictionary<string, string[]>
        {
            ["number"] = new[] { "شماره مدرک", "document number", "doc no", "number" },
            ["title"] = new[] { "عنوان", "title" },
            ["type"] = new[] { "نوع", "type" },
            ["date"] = new[] { "تاریخ تعهد", "commit date", "commitment date", "due date" },
        };

        internal static readonly Dictionary<string, string[]> InfoKeys = new Dictionary<string, string[]>
        {
            ["kind"] = new[] { "نوع بسته", "package kind", "kind", "type" },
            ["platform"] = new[] { "کد پلتفرم", "platform code", "platform" },
            ["standard"] = new[] { "استاندارد", "standard" },
            ["version"] = new[] { "نسخه بسته", "package version", "version" },
            ["contract"] = new[] { "کد قرارداد", "contract code", "contract" },
            ["project"] = new[] { "کد پروژه", "project code", "project" },
            ["title"] = new[] { "عنوان", "title" },
            ["prepared"] = new[] { "تهیه کننده", "تهیه‌کننده", "prepared by", "preparedby", "author" },
            ["date"] = new[] { "تاریخ", "date" },
            ["schema"] = new[] { "قالب", "schemaversion", "schema version", "schema" },
        };

        private static readonly Regex CodeText = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,39}$", RegexOptions.Compiled);
        private static readonly Regex StageText = new Regex(@"^[A-Za-z]{2}$", RegexOptions.Compiled);

        /// <summary>The cells of the first column that start with this sign are explanation rows (the template has one per sheet) and are skipped.</summary>
        public const string ExplanationSign = "※";

        // ------------------------------------------------------------------------------------------------ entry

        public static EngParsedPackage Parse(byte[] workbook, string fileName, IReadOnlyDictionary<string, EngFileInfo> files)
        {
            var p = new EngParsedPackage();
            if (workbook == null || workbook.Length == 0) { p.Error(null, 0, "فایل اکسل بسته خالی است."); return p; }
            string ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
            if (ext == ".xlsm" || ext == ".xls" || ext == ".xlsb") { p.Error(null, 0, "فقط فایل xlsx پذیرفته می‌شود؛ فایل «" + ext + "» (ماکرو یا قدیمی) پذیرفته نیست."); return p; }
            if (ext != ".xlsx") { p.Error(null, 0, "پسوند فایل باید xlsx باشد."); return p; }
            if (workbook.Length > EngPackageLimits.MaxWorkbookBytes) { p.Error(null, 0, "حجم فایل اکسل بیش از ۲۰ مگابایت است."); return p; }
            if (!GuardZip(workbook, p)) return p;
            List<string> names;
            try { names = XlsxReader.SheetNames(workbook); }
            catch (XlsxFormatException ex) { p.Error(null, 0, ex.Message); return p; }

            string Find(string[] aliases) => names.FirstOrDefault(n => aliases.Contains(EngText.Norm(n)));
            string info = Find(InfoNames), bom = Find(BomNames), opc = Find(OpcNames), docs = Find(DocNames), mdl = Find(MdlNames);
            ScanFormulas(workbook, p, new[] { info, bom, opc, docs, mdl });   // the findings are in; the sheets are still read to report everything
            if (info == null) p.Error(SheetInfo, 0, "برگه «مشخصات» (Info) در فایل نیست.");
            if (bom == null) p.Error(SheetBom, 0, "برگه «BOM» در فایل نیست.");
            if (opc == null) p.Error(SheetOpc, 0, "برگه «OPC» در فایل نیست.");
            if (docs == null) p.Error(SheetDocs, 0, "برگه «مدارک» (Documents) در فایل نیست.");

            if (info != null) ReadInfo(workbook, info, p);
            if (bom != null) ReadBom(workbook, bom, p);
            if (opc != null) ReadOpc(workbook, opc, p);
            if (docs != null) ReadDocs(workbook, docs, files, p);
            if (mdl != null) ReadMdl(workbook, mdl, p);

            LengthChecks(p);
            CrossChecks(p);
            if (!p.HasErrors) p.ContentHash = EngPackageHash.Of(p, files);
            return p;
        }

        // ------------------------------------------------------------------------------------------------ column lengths (B1)

        /// <summary>The longest text each stored column takes (EngInboxConfigurations). A longer text is a finding with the sheet and the row, never a database error. The checks of the sheet readers already cover the standard, the title, the BOM description, material and note, the OPC description and work centre, the document number and the file name; the ones below are the rest.</summary>
        public static class Max
        {
            public const int PlatformCode = 40, Standard = 60, ContractCode = 60, ProjectCode = 60, Title = 300, PreparedBy = 200, BomParent = 60, BomPart = 60, Description = 300, Unit = 30, Material = 200, DrawingNumber = 100,
                DrawingRevision = 30, Note = 500, WorkCenter = 50, Predecessors = 200, Stage = 2, DocNumber = 100, DocRevision = 30, DocType = 60, FileName = 200;
        }

        private static void LengthChecks(EngParsedPackage p)
        {
            void Check(string sheet, int row, string label, string value, int max)
            {
                if (value != null && value.Length > max) p.Error(sheet, row, "«" + label + "» بیش از " + max.ToString(CultureInfo.InvariantCulture) + " نویسه است (" + value.Length.ToString(CultureInfo.InvariantCulture) + " نویسه)؛ کوتاه‌تر بنویسید.");
            }
            EngInfo i = p.Info;
            Check(SheetInfo, 0, "کد پلتفرم", i.PlatformCode, Max.PlatformCode);
            Check(SheetInfo, 0, "کد قرارداد", i.ContractCode, Max.ContractCode);
            Check(SheetInfo, 0, "کد پروژه", i.ProjectCode, Max.ProjectCode);
            Check(SheetInfo, 0, "تهیه‌کننده", i.PreparedBy, Max.PreparedBy);
            foreach (EngBomRow b in p.Bom)
            {
                Check(SheetBom, b.SourceRow, "کد پدر", b.ParentCode, Max.BomParent);
                Check(SheetBom, b.SourceRow, "کد قطعه", b.PartCode, Max.BomPart);
                Check(SheetBom, b.SourceRow, "واحد", b.Unit, Max.Unit);
                Check(SheetBom, b.SourceRow, "شمارهٔ نقشه", b.DrawingNumber, Max.DrawingNumber);
                Check(SheetBom, b.SourceRow, "ویرایش نقشه", b.DrawingRevision, Max.DrawingRevision);
            }
            foreach (EngOpcRow o in p.Opc)
            {
                Check(SheetOpc, o.SourceRow, "کد قطعه", o.PartCode, Max.BomPart);
                Check(SheetOpc, o.SourceRow, "پیش‌نیازها", string.Join(",", o.Predecessors), Max.Predecessors);
                Check(SheetOpc, o.SourceRow, "ایستگاه", o.StageCode, Max.Stage);
            }
            foreach (EngDocRow d in p.Docs)
            {
                Check(SheetDocs, d.SourceRow, "عنوان", d.Title, Max.Description);
                Check(SheetDocs, d.SourceRow, "ویرایش", d.Revision, Max.DocRevision);
                Check(SheetDocs, d.SourceRow, "نوع", d.DocType, Max.DocType);
            }
            foreach (EngMdlRow m in p.Mdl)
            {
                Check(SheetMdl, m.SourceRow, "شمارهٔ مدرک", m.Number, Max.DocNumber);
                Check(SheetMdl, m.SourceRow, "عنوان", m.Title, Max.Description);
                Check(SheetMdl, m.SourceRow, "نوع", m.DocType, Max.DocType);
            }
        }

        // ------------------------------------------------------------------------------------------------ zip guard and formulas

        private static bool GuardZip(byte[] data, EngParsedPackage p)
        {
            if (data.Length < 4 || data[0] != 0x50 || data[1] != 0x4B) { p.Error(null, 0, "فایل، یک فایل اکسل (xlsx) نیست."); return false; }
            try
            {
                using (var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read))
                {
                    if (zip.Entries.Count > EngPackageLimits.MaxZipEntries) { p.Error(null, 0, "ساختار فایل اکسل بیش از حد بزرگ است (تعداد اجزای فایل)."); return false; }
                    long total = 0;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        total += e.Length;
                        if (total > EngPackageLimits.MaxUncompressedBytes) { p.Error(null, 0, "حجم بازشدهٔ فایل اکسل بیش از ۲۰۰ مگابایت است."); return false; }
                        if (e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) { p.Error(null, 0, "فایل ماکرو دارد؛ فقط xlsx بدون ماکرو پذیرفته می‌شود."); return false; }
                    }
                }
            }
            catch (InvalidDataException) { p.Error(null, 0, "فایل، یک فایل اکسل (xlsx) معتبر نیست."); return false; }
            return true;
        }

        /// <summary>A formula is never evaluated: the value Excel stored is used. A formula cell that has no stored value is an error (the file was not calculated or saved by Excel). Only the five known sheets are scanned.</summary>
        private static bool ScanFormulas(byte[] data, EngParsedPackage p, IEnumerable<string> sheetNames)
        {
            try
            {
                var cells = XlsxReader.FormulaCellsWithoutValue(data, new HashSet<string>(sheetNames.Where(n => n != null), StringComparer.Ordinal), 20);
                foreach (var (sheet, reference) in cells)
                    p.Error(sheet, RowOf(reference), "سلول " + reference + " فرمول دارد ولی مقدار ذخیره‌شده ندارد؛ فایل را در اکسل باز کنید، ذخیره کنید یا فرمول را به مقدار تبدیل کنید (فرمول هرگز محاسبه نمی‌شود).");
                return cells.Count == 0;
            }
            catch (XlsxFormatException) { return true; /* the readers report a broken file */ }
        }

        private static int RowOf(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return 0;
            int i = 0;
            while (i < reference.Length && char.IsLetter(reference[i])) i++;
            return int.TryParse(reference.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        // ------------------------------------------------------------------------------------------------ sheets

        private static XlsxSheet Open(byte[] data, string sheet, int maxRows, EngParsedPackage p, string label)
        {
            try { return XlsxReader.Read(data, sheet, maxRows); }
            catch (XlsxFormatException ex)
            {
                string limit = (maxRows - 2).ToString("N0", CultureInfo.InvariantCulture);
                bool tooLong = ex.Message.StartsWith("برگه بیش از", StringComparison.Ordinal) && ex.Message.Contains("سطر");
                p.Error(label, 0, tooLong ? "برگه بیش از " + limit + " سطر دارد." : ex.Message);
                return null;
            }
        }

        /// <summary>The first row of the sheet is the header. Returns the logical column of every header found; a missing required one is an error.</summary>
        private static Dictionary<string, int> Headers(XlsxSheet sheet, Dictionary<string, string[]> aliases, string[] required, string label, EngParsedPackage p)
        {
            var map = new Dictionary<string, int>();
            XlsxRow header = sheet.Rows.OrderBy(r => r.RowNumber).FirstOrDefault();
            if (header == null) { p.Error(label, 0, "برگه خالی است (ردیف عنوان ستون‌ها نیست)."); return null; }
            foreach (KeyValuePair<int, XlsxCell> c in header.Cells)
            {
                string h = EngText.Norm(c.Value.AsText());
                foreach (KeyValuePair<string, string[]> a in aliases)
                    if (!map.ContainsKey(a.Key) && a.Value.Any(x => EngText.Norm(x) == h)) { map[a.Key] = c.Key; break; }
            }
            bool ok = true;
            foreach (string r in required)
                if (!map.ContainsKey(r)) { ok = false; p.Error(label, header.RowNumber, "ستون الزامی «" + aliases[r][0] + "» در برگه پیدا نشد."); }
            return ok ? map : null;
        }

        private static IEnumerable<XlsxRow> DataRows(XlsxSheet sheet)
        {
            XlsxRow header = sheet.Rows.OrderBy(r => r.RowNumber).FirstOrDefault();
            foreach (XlsxRow r in sheet.Rows.OrderBy(r => r.RowNumber))
            {
                if (r == header) continue;
                XlsxCell first = r.Cells.OrderBy(c => c.Key).Select(c => c.Value).FirstOrDefault();
                string t = EngText.CellText(first);
                if (t != null && t.StartsWith(ExplanationSign, StringComparison.Ordinal)) continue;
                if (r.Cells.Values.All(c => EngText.CellText(c) == null)) continue;
                yield return r;
            }
        }

        private static string Text(XlsxRow r, Dictionary<string, int> h, string key) => h.TryGetValue(key, out int col) ? EngText.CellText(r.Cell(col)) : null;

        private static void ReadInfo(byte[] data, string name, EngParsedPackage p)
        {
            XlsxSheet sheet = Open(data, name, 500, p, SheetInfo);
            if (sheet == null) return;
            var values = new Dictionary<string, (string Text, XlsxCell Cell, int Row)>();
            foreach (XlsxRow r in DataRows(sheet))
            {
                var cells = r.Cells.OrderBy(c => c.Key).Select(c => c.Value).ToList();
                if (cells.Count < 2) continue;
                string key = EngText.Norm(cells[0].AsText());
                string logical = InfoKeys.Where(k => k.Value.Any(a => EngText.Norm(a) == key)).Select(k => k.Key).FirstOrDefault();
                if (logical == null || values.ContainsKey(logical)) continue;
                values[logical] = (EngText.CellText(cells[1]), cells[1], r.RowNumber);
            }
            string T(string k) => values.TryGetValue(k, out var v) ? v.Text : null;
            int RowOfKey(string k) => values.TryGetValue(k, out var v) ? v.Row : 0;
            EngInfo i = p.Info;

            string kind = EngText.Norm(T("kind"));
            if (kind.Length == 0) p.Error(SheetInfo, RowOfKey("kind"), "«نوع بسته» را بنویسید (پلتفرم یا پروژه).");
            else if (kind == "پلتفرم" || kind == "platform") i.Kind = EngPackageKind.Platform;
            else if (kind == "پروژه" || kind == "project") i.Kind = EngPackageKind.Project;
            else p.Error(SheetInfo, RowOfKey("kind"), "«نوع بسته» باید «پلتفرم» یا «پروژه» باشد.");

            i.PlatformCode = Clean(T("platform"));
            i.Standard = Clean(T("standard"));
            i.ContractCode = Clean(T("contract"));
            i.ProjectCode = Clean(T("project"));
            i.Title = Clean(T("title"));
            i.PreparedBy = Clean(T("prepared"));

            if (i.PlatformCode == null && i.Kind == EngPackageKind.Platform) p.Error(SheetInfo, RowOfKey("platform"), "«کد پلتفرم» برای بستهٔ پلتفرم الزامی است.");
            if (i.Standard == null && i.Kind == EngPackageKind.Platform) p.Error(SheetInfo, RowOfKey("standard"), "«استاندارد» (مثلا API 611) برای بستهٔ پلتفرم الزامی است.");
            if (i.PlatformCode != null && !CodeText.IsMatch(i.PlatformCode)) p.Error(SheetInfo, RowOfKey("platform"), "«کد پلتفرم» فقط حرف و عدد لاتین، نقطه، خط تیره و زیرخط (حداکثر ۴۰ نویسه) دارد.");
            if (i.Standard != null && (i.Standard.Length > 60 || i.Standard.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)) p.Error(SheetInfo, RowOfKey("standard"), "«استاندارد» حداکثر ۶۰ نویسه است و نویسه‌های / \\ : * ? \" < > | ندارد.");
            if (i.Kind == EngPackageKind.Project)
            {
                if (i.ContractCode == null) p.Error(SheetInfo, RowOfKey("contract"), "«کد قرارداد» برای بستهٔ پروژه الزامی است.");
                if (i.ProjectCode == null) p.Error(SheetInfo, RowOfKey("project"), "«کد پروژه» برای بستهٔ پروژه الزامی است.");
            }
            if (i.ContractCode != null && !CodeText.IsMatch(i.ContractCode.Replace("/", "-"))) p.Error(SheetInfo, RowOfKey("contract"), "«کد قرارداد» نامعتبر است.");
            if (i.ProjectCode != null && !CodeText.IsMatch(i.ProjectCode.Replace("/", "-"))) p.Error(SheetInfo, RowOfKey("project"), "«کد پروژه» نامعتبر است.");
            if (i.Title == null) p.Error(SheetInfo, RowOfKey("title"), "«عنوان» بسته را بنویسید.");
            else if (i.Title.Length > 300) p.Error(SheetInfo, RowOfKey("title"), "«عنوان» حداکثر ۳۰۰ نویسه است.");
            if (i.PreparedBy == null) p.Warn(SheetInfo, RowOfKey("prepared"), "«تهیه‌کننده» خالی است.");

            if (!values.TryGetValue("version", out var ver) || ver.Text == null) p.Error(SheetInfo, RowOfKey("version"), "«نسخه بسته» (عدد صحیح ۱ یا بیشتر) را بنویسید.");
            else if (!EngText.TryInt(ver.Cell, out int vn) || vn < 1 || vn > 9999) p.Error(SheetInfo, ver.Row, "«نسخه بسته» باید عدد صحیح ۱ تا ۹۹۹۹ باشد.");
            else i.PackageVersion = vn;

            if (values.TryGetValue("date", out var dv) && dv.Text != null)
            {
                DateTime? d = EngText.Date(dv.Cell);
                if (d == null) p.Error(SheetInfo, dv.Row, "«تاریخ» باید شمسی (مثل ۱۴۰۵/۰۸/۲۵) یا میلادی (مثل 2026-11-16) باشد.");
                else i.Date = d;
            }

            if (!values.TryGetValue("schema", out var sv) || sv.Text == null) p.Error(SheetInfo, RowOfKey("schema"), "«قالب» (شماره قالب فایل، اکنون 1) را بنویسید.");
            else if (!EngText.TryInt(sv.Cell, out int sn) || sn != EngPackageLimits.SupportedSchemaVersion) p.Error(SheetInfo, sv.Row, "«قالب» باید " + EngPackageLimits.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) + " باشد (قالب دیگری پشتیبانی نمی‌شود).");
            else i.SchemaVersion = sn;
        }

        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static void ReadBom(byte[] data, string name, EngParsedPackage p)
        {
            XlsxSheet sheet = Open(data, name, EngPackageLimits.MaxBomRows + 2, p, SheetBom);
            if (sheet == null) return;
            Dictionary<string, int> h = Headers(sheet, BomHeaders, new[] { "level", "parent", "code", "desc", "qty", "unit", "mb" }, SheetBom, p);
            if (h == null) return;
            int count = 0;
            foreach (XlsxRow r in DataRows(sheet))
            {
                if (++count > EngPackageLimits.MaxBomRows) { p.Error(SheetBom, r.RowNumber, "BOM بیش از ۲۰٬۰۰۰ سطر دارد."); break; }
                int row = r.RowNumber;
                bool ok = true;
                var b = new EngBomRow { SourceRow = row };
                if (!EngText.TryInt(r.Cell(h["level"]), out int level) || level < 0 || level > 50) { p.Error(SheetBom, row, "«سطح» باید عدد صحیح (۰ تا ۵۰) باشد."); ok = false; } else b.Level = level;
                b.ParentCode = Clean(Text(r, h, "parent"))?.ToUpperInvariant();
                b.PartCode = Clean(Text(r, h, "code"))?.ToUpperInvariant();
                if (b.PartCode == null) { p.Error(SheetBom, row, "«کد قطعه» خالی است."); ok = false; }
                else
                {
                    string shape = PbCodeRules.ShapeError(b.PartCode);
                    if (shape != null) { p.Error(SheetBom, row, "کد قطعه «" + b.PartCode + "»: " + shape); ok = false; }
                }
                if (b.ParentCode != null && PbCodeRules.ShapeError(b.ParentCode) != null) { p.Error(SheetBom, row, "کد قطعه والد «" + b.ParentCode + "» نامعتبر است."); ok = false; }
                b.Description = Clean(Text(r, h, "desc"));
                if (b.Description == null) { p.Error(SheetBom, row, "«شرح» خالی است."); ok = false; }
                else if (b.Description.Length > 300) { p.Error(SheetBom, row, "«شرح» حداکثر ۳۰۰ نویسه است."); ok = false; }
                if (!EngText.TryDecimal(r.Cell(h["qty"]), out decimal qty)) { p.Error(SheetBom, row, "«تعداد» عدد نیست."); ok = false; }
                else if (qty <= 0) { p.Error(SheetBom, row, "«تعداد» باید بیشتر از صفر باشد."); ok = false; }
                else if (qty > 99999999m) { p.Error(SheetBom, row, "«تعداد» بیش از حد بزرگ است."); ok = false; }
                else b.Qty = Math.Round(qty, 4);
                string unit = Clean(Text(r, h, "unit"));
                if (unit == null) { p.Error(SheetBom, row, "«واحد» خالی است."); ok = false; }
                else if (EngText.CanonicalUnit(unit) == null) { p.Error(SheetBom, row, "واحد «" + unit + "» شناخته‌شده نیست (مثلا عدد، کیلوگرم، متر، لیتر، ست)."); ok = false; }
                else b.Unit = EngText.CanonicalUnit(unit);
                string mb = Clean(Text(r, h, "mb"));
                EngMakeBuy? mbv = mb == null ? null : EngText.MakeBuy(mb);
                if (mb == null) { p.Error(SheetBom, row, "«ساخت/خرید» خالی است."); ok = false; }
                else if (mbv == null) { p.Error(SheetBom, row, "«ساخت/خرید»: مقدار «" + mb + "» نامعتبر است (ساخت، خرید، تامین توسط مشتری، پیمانکاری)."); ok = false; }
                else b.MakeBuy = mbv.Value;
                b.Material = Clean(Text(r, h, "material"));
                b.DrawingNumber = Clean(Text(r, h, "drawing"));
                b.DrawingRevision = Clean(Text(r, h, "drawingRev"));
                b.Note = Clean(Text(r, h, "note"));
                if (b.Material != null && b.Material.Length > 200) { p.Error(SheetBom, row, "«متریال» حداکثر ۲۰۰ نویسه است."); ok = false; }
                if (b.Note != null && b.Note.Length > 500) { p.Error(SheetBom, row, "«یادداشت» حداکثر ۵۰۰ نویسه است."); ok = false; }
                if (ok || b.PartCode != null) p.Bom.Add(b);
            }
        }

        private static void ReadOpc(byte[] data, string name, EngParsedPackage p)
        {
            XlsxSheet sheet = Open(data, name, EngPackageLimits.MaxOpcRows + 2, p, SheetOpc);
            if (sheet == null) return;
            Dictionary<string, int> h = Headers(sheet, OpcHeaders, new[] { "code", "op", "desc", "wc", "setup", "run" }, SheetOpc, p);
            if (h == null) return;
            int count = 0;
            foreach (XlsxRow r in DataRows(sheet))
            {
                if (++count > EngPackageLimits.MaxOpcRows) { p.Error(SheetOpc, r.RowNumber, "OPC بیش از ۵۰٬۰۰۰ سطر دارد."); break; }
                int row = r.RowNumber;
                var o = new EngOpcRow { SourceRow = row };
                o.PartCode = Clean(Text(r, h, "code"))?.ToUpperInvariant();
                if (o.PartCode == null) p.Error(SheetOpc, row, "«کد قطعه» خالی است.");
                if (!EngText.TryInt(r.Cell(h["op"]), out int op) || op <= 0 || op > 99999) p.Error(SheetOpc, row, "«شماره عملیات» باید عدد صحیح مثبت باشد."); else o.OperationNumber = op;
                o.Description = Clean(Text(r, h, "desc"));
                if (o.Description == null) p.Error(SheetOpc, row, "«شرح عملیات» خالی است.");
                else if (o.Description.Length > 300) p.Error(SheetOpc, row, "«شرح عملیات» حداکثر ۳۰۰ نویسه است.");
                o.WorkCenterCode = Clean(Text(r, h, "wc"));
                if (o.WorkCenterCode == null) p.Error(SheetOpc, row, "«مرکز کاری» (کد) خالی است.");
                else if (o.WorkCenterCode.Length > 50) p.Error(SheetOpc, row, "«مرکز کاری» حداکثر ۵۰ نویسه است.");
                if (!EngText.TryDecimal(r.Cell(h["setup"]), out decimal setup)) p.Error(SheetOpc, row, "«زمان آماده‌سازی» عدد (دقیقه) نیست.");
                else if (setup < 0) p.Error(SheetOpc, row, "«زمان آماده‌سازی» منفی است.");
                else o.SetupMinutes = Math.Round(setup, 2);
                if (!EngText.TryDecimal(r.Cell(h["run"]), out decimal run)) p.Error(SheetOpc, row, "«زمان اجرا» عدد (دقیقه) نیست.");
                else if (run < 0) p.Error(SheetOpc, row, "«زمان اجرا» منفی است.");
                else o.RunMinutes = Math.Round(run, 2);
                if (h.TryGetValue("insp", out int ic)) { bool? v = EngText.YesNo(r.Cell(ic)); if (v == null) p.Error(SheetOpc, row, "«بازرسی» باید «بله» یا «خیر» باشد."); else o.IsInspection = v.Value; }
                if (h.TryGetValue("special", out int sc)) { bool? v = EngText.YesNo(r.Cell(sc)); if (v == null) p.Error(SheetOpc, row, "«فرایند ویژه» باید «بله» یا «خیر» باشد."); else o.IsSpecialProcess = v.Value; }
                string stage = Clean(Text(r, h, "stage"));
                if (stage != null) { if (!StageText.IsMatch(stage)) p.Error(SheetOpc, row, "«ایستگاه» باید کد دو حرفی لاتین باشد (مثلا MC، QC)."); else o.StageCode = stage.ToUpperInvariant(); }
                string pred = Clean(Text(r, h, "pred"));
                if (pred != null)
                    foreach (string part in Regex.Split(EngText.Digits(pred), @"[,;،؛\s]+").Where(x => x.Length > 0))
                    {
                        if (!EngText.TryInt(part, out int pn) || pn <= 0) { p.Error(SheetOpc, row, "«پیش‌نیاز»: «" + part + "» شمارهٔ عملیات نیست."); continue; }
                        if (!o.Predecessors.Contains(pn)) o.Predecessors.Add(pn);
                    }
                p.Opc.Add(o);
            }
        }

        private static void ReadDocs(byte[] data, string name, IReadOnlyDictionary<string, EngFileInfo> files, EngParsedPackage p)
        {
            XlsxSheet sheet = Open(data, name, EngPackageLimits.MaxDocRows + 2, p, SheetDocs);
            if (sheet == null) return;
            Dictionary<string, int> h = Headers(sheet, DocHeaders, new[] { "number", "title" }, SheetDocs, p);
            if (h == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XlsxRow r in DataRows(sheet))
            {
                int row = r.RowNumber;
                var d = new EngDocRow { SourceRow = row, Number = Clean(Text(r, h, "number")), Title = Clean(Text(r, h, "title")), Revision = Clean(Text(r, h, "rev")), DocType = Clean(Text(r, h, "type")), FileName = Clean(Text(r, h, "file")) };
                if (d.Number == null) p.Error(SheetDocs, row, "«شماره مدرک» خالی است.");
                else if (d.Number.Length > 100) p.Error(SheetDocs, row, "«شماره مدرک» حداکثر ۱۰۰ نویسه است.");
                if (d.Title == null) p.Error(SheetDocs, row, "«عنوان» مدرک خالی است.");
                if (h.TryGetValue("required", out int rc)) { bool? v = EngText.YesNo(r.Cell(rc)); if (v == null) p.Error(SheetDocs, row, "«الزامی» باید «بله» یا «خیر» باشد."); else d.Required = v.Value; }
                if (d.Number != null && !seen.Add(d.Number + "#" + (d.Revision ?? ""))) p.Error(SheetDocs, row, "مدرک «" + d.Number + "» با همین بازنگری تکراری است.");
                if (d.FileName != null)
                {
                    string bad = FileNameProblem(d.FileName);
                    if (bad != null) p.Error(SheetDocs, row, "«نام فایل»: " + bad);
                    else if (files != null)
                    {
                        EngFileInfo f = files.TryGetValue(d.FileName, out EngFileInfo exact) ? exact : files.Values.FirstOrDefault(x => string.Equals(x.Name, d.FileName, StringComparison.OrdinalIgnoreCase));
                        if (f == null)
                        {
                            if (d.Required) p.Error(SheetDocs, row, "فایل الزامی «" + d.FileName + "» در پوشهٔ بسته نیست.");
                            else p.Warn(SheetDocs, row, "فایل «" + d.FileName + "» در پوشهٔ بسته نیست.");
                        }
                        else { d.FileSize = f.Size; d.FileSha256 = f.Sha256; }
                    }
                }
                else if (d.Required) p.Error(SheetDocs, row, "مدرک الزامی «" + (d.Number ?? "") + "» نام فایل ندارد.");
                p.Docs.Add(d);
            }
        }

        /// <summary>Null when the name is a plain file name inside the package folder; else the Persian reason.</summary>
        public static string FileNameProblem(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "نام فایل خالی است.";
            if (name.Length > 200) return "نام فایل بیش از ۲۰۰ نویسه است.";
            if (name.IndexOf('\0') >= 0 || name.Any(char.IsControl)) return "نام فایل نویسهٔ نامعتبر دارد.";
            if (name.Contains('/') || name.Contains('\\')) return "نام فایل نباید مسیر باشد (فقط نام فایلِ داخل پوشهٔ بسته).";
            if (name.Contains("..")) return "نام فایل نباید «..» داشته باشد.";
            if (Path.IsPathRooted(name) || (name.Length >= 2 && name[1] == ':')) return "نام فایل نباید مسیر کامل باشد.";
            if (name.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0) return "نام فایل نویسهٔ غیرمجاز دارد.";
            if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal)) return "نام فایل نباید با نقطه شروع یا با نقطه/فاصله تمام شود.";
            return null;
        }

        private static void ReadMdl(byte[] data, string name, EngParsedPackage p)
        {
            XlsxSheet sheet = Open(data, name, EngPackageLimits.MaxDocRows + 2, p, SheetMdl);
            if (sheet == null) return;
            Dictionary<string, int> h = Headers(sheet, MdlHeaders, new[] { "number", "title" }, SheetMdl, p);
            if (h == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XlsxRow r in DataRows(sheet))
            {
                int row = r.RowNumber;
                var m = new EngMdlRow { SourceRow = row, Number = Clean(Text(r, h, "number")), Title = Clean(Text(r, h, "title")), DocType = Clean(Text(r, h, "type")) };
                if (m.Number == null) p.Error(SheetMdl, row, "«شماره مدرک» خالی است.");
                else if (!seen.Add(m.Number)) p.Error(SheetMdl, row, "شمارهٔ مدرک «" + m.Number + "» در MDL تکراری است.");
                if (m.Title == null) p.Error(SheetMdl, row, "«عنوان» مدرک خالی است.");
                if (h.TryGetValue("date", out int dc) && EngText.CellText(r.Cell(dc)) != null)
                {
                    m.CommitDate = EngText.Date(r.Cell(dc));
                    if (m.CommitDate == null) p.Error(SheetMdl, row, "«تاریخ تعهد» نامعتبر است (شمسی یا میلادی).");
                }
                p.Mdl.Add(m);
            }
        }

        // ------------------------------------------------------------------------------------------------ rules across the sheets

        private static void CrossChecks(EngParsedPackage p)
        {
            if (p.Mdl.Count > 0 && p.Info.Kind == EngPackageKind.Platform) p.Warn(SheetMdl, 0, "برگهٔ MDL فقط برای بستهٔ پروژه است؛ در بستهٔ پلتفرم نادیده گرفته می‌شود.");
            if (p.Bom.Count == 0)
            {
                if (!p.Findings.Any(f => f.IsError && f.Sheet == SheetBom)) p.Error(SheetBom, 0, "BOM هیچ سطری ندارد.");
                return;
            }

            // duplicates under one parent, the roots, the parents
            var codes = new HashSet<string>(p.Bom.Select(b => b.PartCode).Where(c => c != null), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngBomRow b in p.Bom)
                if (b.PartCode != null && !seen.Add((b.ParentCode ?? "") + "\u0001" + b.PartCode))
                    p.Error(SheetBom, b.SourceRow, "قطعهٔ «" + b.PartCode + "» زیر همین والد (" + (b.ParentCode ?? "ریشه") + ") تکراری است.");
            var roots = p.Bom.Where(b => b.ParentCode == null).ToList();
            if (roots.Count == 0) p.Error(SheetBom, 0, "ریشهٔ BOM پیدا نشد (سطری که کد والد ندارد).");
            else if (roots.Count > 1) foreach (EngBomRow r in roots.Skip(1)) p.Error(SheetBom, r.SourceRow, "BOM فقط یک ریشه دارد؛ «" + r.PartCode + "» والد ندارد ولی ریشهٔ دیگری هم هست («" + roots[0].PartCode + "»).");
            foreach (EngBomRow b in p.Bom.Where(b => b.ParentCode != null && !codes.Contains(b.ParentCode)))
                p.Error(SheetBom, b.SourceRow, "قطعهٔ والد «" + b.ParentCode + "» در BOM نیست.");
            foreach (EngBomRow b in p.Bom.Where(b => b.ParentCode != null && b.ParentCode == b.PartCode))
                p.Error(SheetBom, b.SourceRow, "قطعه والد خودش است.");

            // cycle: walk the parent edges (a code may have several parents)
            var parentsOf = p.Bom.Where(b => b.ParentCode != null && b.PartCode != null && b.ParentCode != b.PartCode).GroupBy(b => b.PartCode).ToDictionary(g => g.Key, g => g.Select(b => b.ParentCode).Distinct().ToList());
            var cyclic = new HashSet<string>(StringComparer.Ordinal);
            FindCycles(parentsOf, parentsOf.Keys.OrderBy(x => x, StringComparer.Ordinal), cyclic);
            foreach (EngBomRow b in p.Bom.Where(b => b.PartCode != null && cyclic.Contains(b.PartCode)).GroupBy(b => b.PartCode).Select(g => g.First()))
                p.Error(SheetBom, b.SourceRow, "حلقه در BOM: «" + b.PartCode + "» (مستقیم یا غیرمستقیم) زیرمجموعهٔ خودش است.");

            // levels (relative to the root)
            if (roots.Count == 1 && cyclic.Count == 0)
            {
                var levelOf = p.Bom.Where(b => b.PartCode != null).GroupBy(b => b.PartCode).ToDictionary(g => g.Key, g => g.First().Level);
                foreach (EngBomRow b in p.Bom.Where(b => b.ParentCode != null && levelOf.ContainsKey(b.ParentCode)))
                    if (b.Level != levelOf[b.ParentCode] + 1) p.Error(SheetBom, b.SourceRow, "«سطح» " + b.Level.ToString(CultureInfo.InvariantCulture) + " با سطح والد (" + levelOf[b.ParentCode].ToString(CultureInfo.InvariantCulture) + ") سازگار نیست؛ باید یکی بیشتر از والد باشد.");
            }

            // OPC against the BOM
            var makeCodes = new HashSet<string>(p.Bom.Where(b => b.MakeBuy == EngMakeBuy.Make && b.PartCode != null).Select(b => b.PartCode), StringComparer.Ordinal);
            foreach (EngOpcRow o in p.Opc.Where(o => o.PartCode != null && !codes.Contains(o.PartCode)))
                p.Error(SheetOpc, o.SourceRow, "قطعهٔ «" + o.PartCode + "» در BOM نیست.");
            var opSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngOpcRow o in p.Opc.Where(o => o.PartCode != null && o.OperationNumber > 0))
                if (!opSeen.Add(o.PartCode + "#" + o.OperationNumber.ToString(CultureInfo.InvariantCulture)))
                    p.Error(SheetOpc, o.SourceRow, "شمارهٔ عملیات " + o.OperationNumber.ToString(CultureInfo.InvariantCulture) + " برای قطعهٔ «" + o.PartCode + "» تکراری است.");
            foreach (var g in p.Opc.Where(o => o.PartCode != null).GroupBy(o => o.PartCode))
            {
                var ops = g.Where(o => o.OperationNumber > 0).GroupBy(o => o.OperationNumber).ToDictionary(x => x.Key, x => x.First());
                foreach (EngOpcRow o in g)
                {
                    foreach (int pre in o.Predecessors)
                    {
                        if (pre == o.OperationNumber) p.Error(SheetOpc, o.SourceRow, "عملیات " + o.OperationNumber.ToString(CultureInfo.InvariantCulture) + " پیش‌نیاز خودش است.");
                        else if (!ops.ContainsKey(pre)) p.Error(SheetOpc, o.SourceRow, "پیش‌نیاز " + pre.ToString(CultureInfo.InvariantCulture) + " در عملیات‌های قطعهٔ «" + g.Key + "» نیست.");
                    }
                }
                // cycle among the operations of one part
                var edges = ops.ToDictionary(x => x.Key.ToString(CultureInfo.InvariantCulture), x => x.Value.Predecessors.Where(q => ops.ContainsKey(q) && q != x.Key).Select(q => q.ToString(CultureInfo.InvariantCulture)).ToList());
                var cyc = new HashSet<string>(StringComparer.Ordinal);
                FindCycles(edges, edges.Keys.OrderBy(x => x, StringComparer.Ordinal), cyc);
                foreach (string k in cyc.OrderBy(x => x, StringComparer.Ordinal).Take(1))
                    p.Error(SheetOpc, ops[int.Parse(k, CultureInfo.InvariantCulture)].SourceRow, "پیش‌نیازهای عملیات‌های قطعهٔ «" + g.Key + "» حلقه دارند.");
            }
            var withOpc = new HashSet<string>(p.Opc.Where(o => o.PartCode != null).Select(o => o.PartCode), StringComparer.Ordinal);
            foreach (EngBomRow b in p.Bom.Where(b => b.PartCode != null && makeCodes.Contains(b.PartCode) && !withOpc.Contains(b.PartCode)).GroupBy(b => b.PartCode).Select(g => g.First()))
                p.Warn(SheetBom, b.SourceRow, "قطعهٔ ساختنی «" + b.PartCode + "» در OPC عملیاتی ندارد.");
        }

        /// <summary>Marks every node that lies on a cycle (iterative depth-first search: a hostile file with a very long chain cannot overflow the stack).</summary>
        private static void FindCycles(Dictionary<string, List<string>> edges, IEnumerable<string> starts, HashSet<string> cyclic)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string start in starts)
            {
                if (state.ContainsKey(start)) continue;
                var path = new List<string>();
                var iter = new List<int>();
                state[start] = 1; path.Add(start); iter.Add(0);
                while (path.Count > 0)
                {
                    string node = path[path.Count - 1];
                    int i = iter[iter.Count - 1];
                    List<string> next = edges.TryGetValue(node, out List<string> n) ? n : null;
                    if (next == null || i >= next.Count) { state[node] = 2; path.RemoveAt(path.Count - 1); iter.RemoveAt(iter.Count - 1); continue; }
                    iter[iter.Count - 1] = i + 1;
                    string to = next[i];
                    if (!state.TryGetValue(to, out int s)) { state[to] = 1; path.Add(to); iter.Add(0); }
                    else if (s == 1) { int at = path.IndexOf(to); for (int k = Math.Max(at, 0); k < path.Count; k++) cyclic.Add(path[k]); }
                }
            }
        }
    }
}
