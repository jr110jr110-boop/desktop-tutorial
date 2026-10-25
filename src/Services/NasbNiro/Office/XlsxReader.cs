using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Services.NasbNiro.Office
{
    /// <summary>One cell of a worksheet: the text, or the number when the cell is numeric (a date or a time is a number with a date format).</summary>
    internal sealed class XlsxCell
    {
        public string Text { get; set; }
        public double? Number { get; set; }

        /// <summary>The number format of the cell is a date, a time or both.</summary>
        public bool IsDateFormat { get; set; }

        /// <summary>Text for people and for text parsing: the string, or the number written without a thousands separator.</summary>
        public string AsText() => Text ?? (Number.HasValue ? Number.Value.ToString("0.###############", CultureInfo.InvariantCulture) : null);
    }

    internal sealed class XlsxRow
    {
        public int RowNumber { get; set; }
        public Dictionary<int, XlsxCell> Cells { get; } = new Dictionary<int, XlsxCell>();

        public XlsxCell Cell(int column) => Cells.TryGetValue(column, out XlsxCell c) ? c : null;
    }

    internal sealed class XlsxSheet
    {
        public string Name { get; set; }
        public List<string> SheetNames { get; set; } = new List<string>();
        public List<XlsxRow> Rows { get; } = new List<XlsxRow>();
        public int MaxColumn { get; set; }
    }

    internal sealed class XlsxFormatException : Exception
    {
        public XlsxFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// A small reader for .xlsx files (SpreadsheetML in a zip package) without any library: sheets, shared and inline strings, numbers and the date/time number formats.
    /// Formulas are read as the value Excel stored. Meant for the clock-event export of the attendance software; it is not a general spreadsheet engine.
    /// </summary>
    internal static class XlsxReader
    {
        public const int MaxRows = 60000;
        private const long MaxEntryBytes = 200L * 1024 * 1024;

        public static string ColumnLetter(int index)
        {
            var sb = new StringBuilder();
            for (int n = index + 1; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)('A' + (n - 1) % 26));
            return sb.ToString();
        }

        /// <summary>"A" to 0, "AB" to 27; -1 when the text is not a column letter.</summary>
        public static int ColumnIndex(string letters)
        {
            if (string.IsNullOrWhiteSpace(letters)) return -1;
            int n = 0;
            foreach (char ch in letters.Trim().ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z') return -1;
                n = n * 26 + (ch - 'A' + 1);
                if (n > 16384) return -1;
            }
            return n - 1;
        }

        private static XmlReader Open(Stream stream) =>
            XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false, IgnoreComments = true });

        private static ZipArchiveEntry Find(ZipArchive zip, string path)
        {
            string p = path.TrimStart('/');
            return zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, p, StringComparison.OrdinalIgnoreCase));
        }

        private static Stream OpenEntry(ZipArchiveEntry e)
        {
            if (e.Length > MaxEntryBytes) throw new XlsxFormatException("فایل اکسل بیش از حد بزرگ است.");
            return e.Open();
        }

        private static string RelationshipId(XmlReader r)
        {
            if (!r.HasAttributes) return null;
            for (int i = 0; i < r.AttributeCount; i++)
            {
                r.MoveToAttribute(i);
                if (r.LocalName == "id" && r.NamespaceURI.Contains("relationships")) { string v = r.Value; r.MoveToElement(); return v; }
            }
            r.MoveToElement();
            return null;
        }

        /// <summary>The names of the worksheets in the order of the workbook.</summary>
        public static List<string> SheetNames(byte[] data)
        {
            using var zip = OpenZip(data);
            return Workbook(zip).Select(s => s.Name).ToList();
        }

        private static ZipArchive OpenZip(byte[] data)
        {
            if (data == null || data.Length < 4 || data[0] != 0x50 || data[1] != 0x4B) throw new XlsxFormatException("فایل، یک فایل اکسل (xlsx) نیست. فایل قدیمی xls را در اکسل با قالب xlsx ذخیره کنید.");
            try
            {
                var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read);
                if (zip.Entries.Count > 5000) throw new XlsxFormatException("ساختار فایل اکسل نامعتبر است.");
                return zip;
            }
            catch (InvalidDataException) { throw new XlsxFormatException("فایل، یک فایل اکسل (xlsx) معتبر نیست."); }
        }

        private sealed class SheetRef
        {
            public string Name;
            public string Path;
        }

        private static List<SheetRef> Workbook(ZipArchive zip)
        {
            ZipArchiveEntry wb = Find(zip, "xl/workbook.xml") ?? throw new XlsxFormatException("فایل اکسل نامعتبر است (کتاب کار پیدا نشد).");
            var sheets = new List<(string Name, string RelId)>();
            using (Stream s = OpenEntry(wb))
            using (XmlReader r = Open(s))
                while (r.Read())
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                        sheets.Add((r.GetAttribute("name"), RelationshipId(r)));
            var targets = new Dictionary<string, string>(StringComparer.Ordinal);
            ZipArchiveEntry rels = Find(zip, "xl/_rels/workbook.xml.rels");
            if (rels != null)
                using (Stream s = OpenEntry(rels))
                using (XmlReader r = Open(s))
                    while (r.Read())
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                        {
                            string id = r.GetAttribute("Id"), target = r.GetAttribute("Target");
                            if (id != null && target != null) targets[id] = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target;
                        }
            var result = new List<SheetRef>();
            int n = 0;
            foreach (var sh in sheets)
            {
                n++;
                string path = sh.RelId != null && targets.TryGetValue(sh.RelId, out string t) ? t : "xl/worksheets/sheet" + n + ".xml";
                result.Add(new SheetRef { Name = sh.Name ?? ("Sheet" + n), Path = path });
            }
            if (result.Count == 0) throw new XlsxFormatException("فایل اکسل هیچ برگه‌ای ندارد.");
            return result;
        }

        private static List<string> SharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            ZipArchiveEntry e = Find(zip, "xl/sharedStrings.xml");
            if (e == null) return list;
            using Stream s = OpenEntry(e);
            using XmlReader r = Open(s);
            StringBuilder cur = null;
            int phonetic = 0;
            bool advance = true;
            while (true)
            {
                // ReadElementContentAsString already moves to the node after the element: that node must not be skipped
                if (advance && !r.Read()) break;
                advance = true;
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "si") { cur = new StringBuilder(); phonetic = 0; if (r.IsEmptyElement) { list.Add(""); cur = null; } }
                    else if (r.LocalName == "rPh") phonetic++;
                    else if (r.LocalName == "t" && cur != null && phonetic == 0 && !r.IsEmptyElement) { cur.Append(r.ReadElementContentAsString()); advance = false; }
                }
                else if (r.NodeType == XmlNodeType.EndElement)
                {
                    if (r.LocalName == "si" && cur != null) { list.Add(cur.ToString()); cur = null; }
                    else if (r.LocalName == "rPh") phonetic--;
                }
            }
            return list;
        }

        /// <summary>True when an Excel number format shows a date or a time (built-in ids 14-22 and 45-47, or a custom code with d, m, y, h or s outside quotes and brackets).</summary>
        internal static bool IsDateFormatCode(int id, string code)
        {
            if ((id >= 14 && id <= 22) || (id >= 45 && id <= 47)) return true;
            if (string.IsNullOrEmpty(code)) return false;
            bool quoted = false, bracket = false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (c == '\\') { i++; continue; }
                if (c == '[') { bracket = true; continue; }
                if (c == ']') { bracket = false; continue; }
                if (bracket) continue;
                char l = char.ToLowerInvariant(c);
                if (l == 'd' || l == 'y' || l == 'h' || l == 's' || l == 'm') return true;
            }
            return false;
        }

        /// <summary>Per cell style index: whether its number format is a date or time.</summary>
        private static List<bool> DateStyles(ZipArchive zip)
        {
            var result = new List<bool>();
            ZipArchiveEntry e = Find(zip, "xl/styles.xml");
            if (e == null) return result;
            var codes = new Dictionary<int, string>();
            using Stream s = OpenEntry(e);
            using XmlReader r = Open(s);
            bool inCellXfs = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "numFmt")
                    {
                        if (int.TryParse(r.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out int id)) codes[id] = r.GetAttribute("formatCode");
                    }
                    else if (r.LocalName == "cellXfs") inCellXfs = true;
                    else if (r.LocalName == "xf" && inCellXfs)
                    {
                        int id = int.TryParse(r.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0;
                        codes.TryGetValue(id, out string code);
                        result.Add(IsDateFormatCode(id, code));
                    }
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
            }
            return result;
        }

        /// <summary>Reads one worksheet (by name; null or empty = the first). Throws <see cref="XlsxFormatException"/> with a Persian message when the file cannot be read.</summary>
        public static XlsxSheet Read(byte[] data, string sheetName, int maxRows = MaxRows)
        {
            using ZipArchive zip = OpenZip(data);
            List<SheetRef> sheets = Workbook(zip);
            SheetRef pick = string.IsNullOrWhiteSpace(sheetName) ? sheets[0] : sheets.FirstOrDefault(x => string.Equals(x.Name, sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (pick == null) throw new XlsxFormatException("برگه «" + sheetName + "» در فایل پیدا نشد.");
            ZipArchiveEntry entry = Find(zip, pick.Path) ?? throw new XlsxFormatException("برگه «" + pick.Name + "» در فایل پیدا نشد.");
            List<string> shared = SharedStrings(zip);
            List<bool> dateStyles = DateStyles(zip);
            var sheet = new XlsxSheet { Name = pick.Name, SheetNames = sheets.Select(x => x.Name).ToList() };

            using Stream s = OpenEntry(entry);
            using XmlReader r = Open(s);
            XlsxRow row = null;
            int autoRow = 0;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row") { if (row != null && row.Cells.Count > 0) sheet.Rows.Add(row); row = null; continue; }
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.LocalName == "row")
                {
                    autoRow++;
                    int rn = int.TryParse(r.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : autoRow;
                    autoRow = rn;
                    if (sheet.Rows.Count >= maxRows) throw new XlsxFormatException("برگه بیش از " + maxRows.ToString("N0", CultureInfo.InvariantCulture) + " سطر دارد؛ فایل را بازه‌ای کوچک‌تر خروجی بگیرید.");
                    row = new XlsxRow { RowNumber = rn };
                    if (r.IsEmptyElement) { row = null; }
                }
                else if (r.LocalName == "c" && row != null)
                {
                    string reference = r.GetAttribute("r");
                    string type = r.GetAttribute("t");
                    int style = int.TryParse(r.GetAttribute("s"), NumberStyles.None, CultureInfo.InvariantCulture, out int st) ? st : 0;
                    int column = row.Cells.Count == 0 ? 0 : row.Cells.Keys.Max() + 1;
                    if (reference != null)
                    {
                        int letters = 0;
                        while (letters < reference.Length && char.IsLetter(reference[letters])) letters++;
                        int idx = ColumnIndex(reference.Substring(0, letters));
                        if (idx >= 0) column = idx;
                    }
                    string value = null;
                    bool inline = false;
                    if (!r.IsEmptyElement)
                    {
                        int depth = r.Depth;
                        var inlineText = new StringBuilder();
                        bool advance = true;
                        while (true)
                        {
                            if (advance && !r.Read()) break;
                            advance = true;
                            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth && r.LocalName == "c") break;
                            if (r.NodeType != XmlNodeType.Element) continue;
                            if (r.LocalName == "v") { value = r.ReadElementContentAsString(); advance = false; }
                            else if (r.LocalName == "is") inline = true;
                            else if (r.LocalName == "t" && inline) { inlineText.Append(r.ReadElementContentAsString()); advance = false; }
                        }
                        if (inline) value = inlineText.ToString();
                    }
                    if (value == null) continue;
                    var cell = new XlsxCell();
                    switch (type)
                    {
                        case "s":
                            cell.Text = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int si) && si >= 0 && si < shared.Count ? shared[si] : null;
                            break;
                        case "str":
                        case "inlineStr":
                        case "e":
                            cell.Text = value;
                            break;
                        case "b":
                            cell.Text = value == "1" ? "TRUE" : "FALSE";
                            break;
                        default:
                            if (inline) cell.Text = value;
                            else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                            {
                                cell.Number = num;
                                cell.IsDateFormat = style >= 0 && style < dateStyles.Count && dateStyles[style];
                            }
                            else cell.Text = value;
                            break;
                    }
                    if (cell.Text != null && cell.Text.Trim().Length == 0 && !cell.Number.HasValue) continue;
                    row.Cells[column] = cell;
                    if (column + 1 > sheet.MaxColumn) sheet.MaxColumn = column + 1;
                }
            }
            return sheet;
        }
    }

    /// <summary>Turns cells and texts of the attendance export into dates, times and directions. Pure: used by the import and checked by unit tests.</summary>
    internal static class ClockCellParser
    {
        private static readonly char[] DateSeparators = { '/', '-', '.', '٫', '⁄' };

        /// <summary>Date from text: Persian or Gregorian year first (1405/05/20, 2026-08-11), day first with a four digit year (20/05/1405), or eight digits (14050520).</summary>
        public static bool TryDateText(string text, out DateTime date)
        {
            date = default;
            string t = OfficeText.Digits(text ?? "").Trim();
            if (t.Length == 0) return false;
            int cut = t.IndexOfAny(new[] { ' ', 'T', ' ' });
            if (cut > 0) t = t.Substring(0, cut);
            if (t.Length == 8 && t.All(char.IsDigit))
            {
                int y8 = int.Parse(t.Substring(0, 4), CultureInfo.InvariantCulture), m8 = int.Parse(t.Substring(4, 2), CultureInfo.InvariantCulture), d8 = int.Parse(t.Substring(6, 2), CultureInfo.InvariantCulture);
                return Make(y8, m8, d8, out date);
            }
            string[] parts = t.Split(DateSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts.Any(p => !p.All(char.IsDigit))) return false;
            int a = int.Parse(parts[0], CultureInfo.InvariantCulture), b = int.Parse(parts[1], CultureInfo.InvariantCulture), c = int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (parts[0].Length == 4) return Make(a, b, c, out date);
            if (parts[2].Length == 4)
            {
                // day/month/year; a month above 12 means the first number is the month (US order)
                int day = a, month = b;
                if (month > 12 && day <= 12) { day = b; month = a; }
                return Make(c, month, day, out date);
            }
            return false;
        }

        private static bool Make(int y, int m, int d, out DateTime date)
        {
            date = default;
            if (m < 1 || m > 12 || d < 1 || d > 31) return false;
            try
            {
                if (y >= 1300 && y < 1700) date = new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0);
                else if (y >= 1900 && y < 2200) date = new DateTime(y, m, d);
                else return false;
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        private static DateTime? FromSerial(double serial)
        {
            if (serial < 1 || serial > 2958465) return null;
            try { return DateTime.FromOADate(serial); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>The date of a cell: a date-formatted number, a number like 14050520, or a text.</summary>
        public static bool TryDate(XlsxCell cell, out DateTime date)
        {
            date = default;
            if (cell == null) return false;
            if (cell.Number.HasValue)
            {
                double n = cell.Number.Value;
                if (n >= 13000101 && n <= 22001231 && Math.Abs(n - Math.Round(n)) < 1e-9) return TryDateText(((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture), out date);
                DateTime? d = FromSerial(n);
                if (d == null) return false;
                // a number that is only a time (below 1) is no date; a plain number needs a date format to count
                if (!cell.IsDateFormat && n < 20000) return false;
                date = d.Value.Date;
                return true;
            }
            return TryDateText(cell.Text, out date);
        }

        /// <summary>Minutes since midnight from a time text (08:05, 8:05:30, ۰۸:۰۵); a trailing date part is ignored.</summary>
        public static bool TryTimeText(string text, out int minuteOfDay)
        {
            minuteOfDay = 0;
            string t = OfficeText.Digits(text ?? "").Trim();
            if (t.Length == 0) return false;
            int space = t.LastIndexOfAny(new[] { ' ', 'T', ' ' });
            if (space >= 0 && t.IndexOf(':', space) >= 0) t = t.Substring(space + 1);
            bool pm = false, am = false;
            string upper = t.ToUpperInvariant();
            if (upper.EndsWith("PM", StringComparison.Ordinal)) { pm = true; t = t.Substring(0, t.Length - 2).Trim(); }
            else if (upper.EndsWith("AM", StringComparison.Ordinal)) { am = true; t = t.Substring(0, t.Length - 2).Trim(); }
            string[] parts = t.Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)) return false;
            if (pm && h < 12) h += 12;
            if (am && h == 12) h = 0;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minuteOfDay = h * 60 + m;
            return true;
        }

        /// <summary>The time of day of a cell: the fraction of a number, or a time text. Seconds are dropped (the clock event has minute resolution).</summary>
        public static bool TryTime(XlsxCell cell, out int minuteOfDay)
        {
            minuteOfDay = 0;
            if (cell == null) return false;
            if (cell.Number.HasValue)
            {
                double n = cell.Number.Value;
                double fraction = n - Math.Floor(n);
                // a whole number is no time unless it is a date-time with the time at midnight
                if (fraction < 1e-9 && !cell.IsDateFormat) return false;
                if (n < 0) return false;
                minuteOfDay = Math.Min(1439, (int)Math.Floor(fraction * 1440 + 1e-4));
                return true;
            }
            return TryTimeText(cell.Text, out minuteOfDay);
        }

        /// <summary>A date and time in one cell.</summary>
        public static bool TryDateTime(XlsxCell cell, out DateTime value)
        {
            value = default;
            if (cell == null) return false;
            if (cell.Number.HasValue)
            {
                if (!cell.IsDateFormat && cell.Number.Value < 20000) return false;
                if (!TryDate(cell, out DateTime d) || !TryTime(cell, out int m)) return false;
                value = d.AddMinutes(m);
                return true;
            }
            string t = OfficeText.Digits(cell.Text ?? "").Trim();
            if (!TryDateText(t, out DateTime date)) return false;
            if (!TryTimeText(t, out int minute)) return false;
            value = date.AddMinutes(minute);
            return true;
        }

        private static HashSet<string> Set(string csv, params string[] defaults)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string x in (string.IsNullOrWhiteSpace(csv) ? string.Join(",", defaults) : csv).Split(new[] { ',', '،', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                set.Add(OfficeText.Digits(x).Trim());
            return set;
        }

        /// <summary>The direction written in the direction column; Unknown when the text is in neither list.</summary>
        public static Entities.Model.NasbNiro.Office.OfClockDirection Direction(string text, string inValues, string outValues)
        {
            string t = OfficeText.Digits(text ?? "").Trim();
            if (t.Length == 0) return Entities.Model.NasbNiro.Office.OfClockDirection.Unknown;
            if (Set(inValues, "ورود", "in", "i", "1", "check in", "checkin", "c/in").Contains(t)) return Entities.Model.NasbNiro.Office.OfClockDirection.In;
            if (Set(outValues, "خروج", "out", "o", "2", "check out", "checkout", "c/out").Contains(t)) return Entities.Model.NasbNiro.Office.OfClockDirection.Out;
            return Entities.Model.NasbNiro.Office.OfClockDirection.Unknown;
        }

        /// <summary>The personnel number without spaces and, for a number made of digits, without leading zeros (so card 00123 and personnel code 123 agree).</summary>
        public static string CodeKey(string code)
        {
            string t = OfficeText.Digits(code ?? "").Trim();
            if (t.Length == 0) return "";
            string trimmed = t.TrimStart('0');
            return t.All(char.IsDigit) ? (trimmed.Length == 0 ? "0" : trimmed) : t.ToUpperInvariant();
        }
    }
}
