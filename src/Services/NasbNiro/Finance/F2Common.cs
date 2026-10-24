using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Finance
{
    /// <summary>Persian calendar helpers for monthly processes (depreciation, payroll, outbox). Fiscal periods are real dates; never Gregorian months.</summary>
    internal static class PersianCal
    {
        private static readonly PersianCalendar Cal = new PersianCalendar();

        public static int Year(DateTime d) => Cal.GetYear(d);
        public static int Month(DateTime d) => Cal.GetMonth(d);
        /// <summary>Days of a Persian month: 31 for the first six, 30 for the next five, 29 or 30 for Esfand by leap year.</summary>
        public static int DaysIn(int year, int month) => Cal.GetDaysInMonth(year, month);
        public static DateTime Start(int year, int month) => Cal.ToDateTime(year, month, 1, 0, 0, 0, 0);
        public static DateTime End(int year, int month) =>
            month >= 12 ? Cal.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0).AddDays(-1) : Cal.ToDateTime(year, month + 1, 1, 0, 0, 0, 0).AddDays(-1);

        /// <summary>Months since the Persian year 0 (year * 12 + month - 1): a number that can be compared and subtracted.</summary>
        public static int Index(DateTime d) => Year(d) * 12 + Month(d) - 1;
        public static int Index(int year, int month) => year * 12 + month - 1;
        public static DateTime StartOfIndex(int index) => Start(index / 12, index % 12 + 1);
        public static DateTime EndOfIndex(int index) => End(index / 12, index % 12 + 1);
        public static int YearOfIndex(int index) => index / 12;
        public static int MonthOfIndex(int index) => index % 12 + 1;
        public static int Season(int month) => (month - 1) / 3 + 1;
    }

    internal static class F2Page
    {
        /// <summary>A page that was cut in the database: the rows of the page plus the total.</summary>
        public static PagedList<T> Make<T>(List<T> rows, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(rows, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total;
            page.CurrentPage = number;
            page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        public static void Window(int pageNumber, int pageSize, int total, out int number, out int size)
        {
            size = pageSize <= 0 ? Math.Max(total, 1) : pageSize;
            number = pageNumber <= 0 ? 1 : pageNumber;
        }
    }

    internal static class IranianNationalCode
    {
        /// <summary>Ten digits with the standard check digit.</summary>
        public static bool Valid(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 10 || !code.All(c => c >= '0' && c <= '9')) return false;
            if (code.Distinct().Count() == 1) return false;
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += (code[i] - '0') * (10 - i);
            int r = sum % 11;
            int check = code[9] - '0';
            return r < 2 ? check == r : check == 11 - r;
        }
    }

    /// <summary>
    /// A tiny safe expression evaluator for payroll formulas: numbers, names, + - * / ( ), min, max, round, abs.
    /// Nothing is compiled and nothing is looked up by reflection: names are resolved only through the callback.
    /// </summary>
    public static class FormulaEvaluator
    {
        public sealed class FormulaException : Exception
        {
            public FormulaException(string message) : base(message) { }
        }

        private abstract class Node
        {
            public abstract decimal Eval(Func<string, decimal> variable);
            public abstract void Collect(ISet<string> names);
        }

        private sealed class Num : Node
        {
            private readonly decimal _value;
            public Num(decimal value) { _value = value; }
            public override decimal Eval(Func<string, decimal> variable) => _value;
            public override void Collect(ISet<string> names) { }
        }

        private sealed class Var : Node
        {
            private readonly string _name;
            public Var(string name) { _name = name; }
            public override decimal Eval(Func<string, decimal> variable) => variable(_name);
            public override void Collect(ISet<string> names) { names.Add(_name); }
        }

        private sealed class Neg : Node
        {
            private readonly Node _inner;
            public Neg(Node inner) { _inner = inner; }
            public override decimal Eval(Func<string, decimal> variable) => -_inner.Eval(variable);
            public override void Collect(ISet<string> names) { _inner.Collect(names); }
        }

        private sealed class Bin : Node
        {
            private readonly char _op;
            private readonly Node _left, _right;
            public Bin(char op, Node left, Node right) { _op = op; _left = left; _right = right; }
            public override decimal Eval(Func<string, decimal> variable)
            {
                decimal a = _left.Eval(variable), b = _right.Eval(variable);
                switch (_op)
                {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    default:
                        if (b == 0) throw new FormulaException("تقسیم بر صفر در فرمول.");
                        return a / b;
                }
            }
            public override void Collect(ISet<string> names) { _left.Collect(names); _right.Collect(names); }
        }

        private sealed class Call : Node
        {
            private readonly string _function;
            private readonly List<Node> _args;
            public Call(string function, List<Node> args) { _function = function; _args = args; }
            public override decimal Eval(Func<string, decimal> variable)
            {
                List<decimal> v = _args.Select(a => a.Eval(variable)).ToList();
                switch (_function)
                {
                    case "MIN": return v.Min();
                    case "MAX": return v.Max();
                    case "ABS": return Math.Abs(v[0]);
                    default:
                    {
                        int digits = v.Count > 1 ? (int)v[1] : 0;
                        return Math.Round(v[0], Math.Max(0, Math.Min(6, digits)), MidpointRounding.AwayFromZero);
                    }
                }
            }
            public override void Collect(ISet<string> names) { foreach (Node a in _args) a.Collect(names); }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;
            private int _depth;

            public Parser(string text) { _text = text; }

            public Node ParseAll()
            {
                Node n = ParseExpression();
                SkipSpaces();
                if (_pos < _text.Length) throw new FormulaException($"کاراکتر نامعتبر در فرمول در موقعیت {_pos + 1}.");
                return n;
            }

            private void SkipSpaces() { while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++; }

            private bool Peek(out char c)
            {
                SkipSpaces();
                if (_pos >= _text.Length) { c = '\0'; return false; }
                c = _text[_pos];
                return true;
            }

            private Node ParseExpression()
            {
                if (++_depth > 40) throw new FormulaException("فرمول بیش از حد تودرتو است.");
                Node left = ParseTerm();
                while (Peek(out char c) && (c == '+' || c == '-'))
                {
                    _pos++;
                    left = new Bin(c, left, ParseTerm());
                }
                _depth--;
                return left;
            }

            private Node ParseTerm()
            {
                Node left = ParseUnary();
                while (Peek(out char c) && (c == '*' || c == '/'))
                {
                    _pos++;
                    left = new Bin(c, left, ParseUnary());
                }
                return left;
            }

            private Node ParseUnary()
            {
                if (Peek(out char c) && (c == '-' || c == '+'))
                {
                    _pos++;
                    Node inner = ParseUnary();
                    return c == '-' ? new Neg(inner) : inner;
                }
                return ParsePrimary();
            }

            private Node ParsePrimary()
            {
                if (!Peek(out char c)) throw new FormulaException("فرمول ناقص است.");
                if (c == '(')
                {
                    _pos++;
                    Node inner = ParseExpression();
                    if (!Peek(out char close) || close != ')') throw new FormulaException("پرانتز بسته نشده است.");
                    _pos++;
                    return inner;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    int start = _pos;
                    while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
                    string token = _text.Substring(start, _pos - start);
                    if (!decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
                        throw new FormulaException($"عدد نامعتبر در فرمول: {token}");
                    return new Num(number);
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = _pos;
                    while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
                    string name = _text.Substring(start, _pos - start).ToUpperInvariant();
                    if (Peek(out char open) && open == '(')
                    {
                        if (name != "MIN" && name != "MAX" && name != "ROUND" && name != "ABS") throw new FormulaException($"تابع «{name}» مجاز نیست (min, max, round, abs).");
                        _pos++;
                        var args = new List<Node>();
                        if (Peek(out char next) && next == ')') throw new FormulaException($"تابع {name} آرگومان ندارد.");
                        while (true)
                        {
                            args.Add(ParseExpression());
                            if (!Peek(out char sep)) throw new FormulaException("فرمول ناقص است.");
                            _pos++;
                            if (sep == ')') break;
                            if (sep != ',') throw new FormulaException("جداکننده آرگومان نامعتبر است.");
                        }
                        if ((name == "ABS" && args.Count != 1) || (name == "ROUND" && args.Count > 2) || ((name == "MIN" || name == "MAX") && args.Count < 2))
                            throw new FormulaException($"تعداد آرگومان‌های تابع {name} نامعتبر است.");
                        return new Call(name, args);
                    }
                    return new Var(name);
                }
                throw new FormulaException($"کاراکتر نامعتبر در فرمول: {c}");
            }
        }

        private static string Normalize(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) throw new FormulaException("فرمول خالی است.");
            if (formula.Length > 500) throw new FormulaException("فرمول بیش از ۵۰۰ نویسه است.");
            string s = LedgerMath.Digits(formula).Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace('٫', '.');
            return s;
        }

        private static Node Parse(string formula) => new Parser(Normalize(formula)).ParseAll();

        /// <summary>Throws <see cref="FormulaException"/> when the formula is not valid.</summary>
        public static void Validate(string formula) { Parse(formula); }

        /// <summary>The names (upper case) used by the formula.</summary>
        public static HashSet<string> Names(string formula)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            Parse(formula).Collect(names);
            return names;
        }

        public static decimal Evaluate(string formula, Func<string, decimal> variable)
        {
            Node n = Parse(formula);
            try { return n.Eval(variable); }
            catch (OverflowException) { throw new FormulaException("نتیجه فرمول از حد مجاز بزرگ‌تر است."); }
            catch (InvalidOperationException) { throw new FormulaException("فرمول نامعتبر است."); }
        }
    }

    public sealed class XlsxSheet
    {
        public string Name { get; set; }
        public List<string> Header { get; set; } = new List<string>();
        public List<object[]> Rows { get; set; } = new List<object[]>();
    }

    /// <summary>A minimal .xlsx writer (one sheet per table, text and numbers only). No library is needed and the files open in Excel and LibreOffice.</summary>
    public static class SimpleXlsx
    {
        private static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
                if (c == '\uFFFE' || c == '\uFFFF') continue;
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Column(int index)
        {
            string name = "";
            int n = index + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                name = (char)('A' + rem) + name;
                n = (n - 1) / 26;
            }
            return name;
        }

        private static string SheetName(string name, int number, HashSet<string> used)
        {
            string s = string.IsNullOrWhiteSpace(name) ? "Sheet" + number : name;
            foreach (char bad in new[] { '[', ']', ':', '*', '?', '/', '\\' }) s = s.Replace(bad, '_');
            if (s.Length > 31) s = s.Substring(0, 31);
            string unique = s;
            int k = 2;
            while (!used.Add(unique.ToUpperInvariant())) unique = (s.Length > 28 ? s.Substring(0, 28) : s) + "_" + k++;
            return unique;
        }

        private static void AddEntry(ZipArchive zip, string path, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static string Cell(int row, int col, object value)
        {
            string r = Column(col) + row.ToString(CultureInfo.InvariantCulture);
            switch (value)
            {
                case null: return "";
                case string s: return $"<c r=\"{r}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(s)}</t></is></c>";
                case bool b: return $"<c r=\"{r}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>";
                case decimal d: return $"<c r=\"{r}\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>";
                case int i: return $"<c r=\"{r}\"><v>{i.ToString(CultureInfo.InvariantCulture)}</v></c>";
                case long l: return $"<c r=\"{r}\"><v>{l.ToString(CultureInfo.InvariantCulture)}</v></c>";
                case double db: return $"<c r=\"{r}\"><v>{db.ToString("R", CultureInfo.InvariantCulture)}</v></c>";
                case DateTime dt: return $"<c r=\"{r}\" t=\"inlineStr\"><is><t>{(dt.TimeOfDay == TimeSpan.Zero ? FiscalCalendars.Current.Text(dt) : FiscalCalendars.Current.TextTime(dt))}</t></is></c>";
                default: return $"<c r=\"{r}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(Convert.ToString(value, CultureInfo.InvariantCulture))}</t></is></c>";
            }
        }

        public static byte[] Build(IList<XlsxSheet> sheets, bool rightToLeft = true)
        {
            if (sheets == null || sheets.Count == 0) sheets = new List<XlsxSheet> { new XlsxSheet { Name = "Sheet1" } };
            var used = new HashSet<string>();
            var names = new List<string>();
            for (int i = 0; i < sheets.Count; i++) names.Add(SheetName(sheets[i].Name, i + 1, used));

            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var types = new StringBuilder();
                    types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                    types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                    types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                    types.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                    types.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                    for (int i = 0; i < sheets.Count; i++)
                        types.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    types.Append("</Types>");
                    AddEntry(zip, "[Content_Types].xml", types.ToString());

                    AddEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

                    var book = new StringBuilder();
                    book.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    book.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                    for (int i = 0; i < sheets.Count; i++)
                        book.Append($"<sheet name=\"{Escape(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                    book.Append("</sheets></workbook>");
                    AddEntry(zip, "xl/workbook.xml", book.ToString());

                    var rels = new StringBuilder();
                    rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                    for (int i = 0; i < sheets.Count; i++)
                        rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                    rels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                    rels.Append("</Relationships>");
                    AddEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

                    AddEntry(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                        "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs></styleSheet>");

                    for (int i = 0; i < sheets.Count; i++)
                    {
                        XlsxSheet sheet = sheets[i];
                        var sb = new StringBuilder();
                        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                        sb.Append(rightToLeft ? "<sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"/></sheetViews>" : "<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
                        sb.Append("<sheetData>");
                        int rowNumber = 1;
                        if (sheet.Header != null && sheet.Header.Count > 0)
                        {
                            sb.Append($"<row r=\"{rowNumber}\">");
                            for (int c = 0; c < sheet.Header.Count; c++) sb.Append(Cell(rowNumber, c, sheet.Header[c] ?? ""));
                            sb.Append("</row>");
                            rowNumber++;
                        }
                        foreach (object[] row in sheet.Rows)
                        {
                            sb.Append($"<row r=\"{rowNumber}\">");
                            for (int c = 0; c < row.Length; c++) sb.Append(Cell(rowNumber, c, row[c]));
                            sb.Append("</row>");
                            rowNumber++;
                        }
                        sb.Append("</sheetData></worksheet>");
                        AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", sb.ToString());
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
