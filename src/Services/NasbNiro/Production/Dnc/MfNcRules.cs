using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Entities.Model.NasbNiro.Production;

namespace Services.NasbNiro.Production.Dnc
{
    public enum NcSeverity { Error = 1, Warning = 2 }

    public sealed class NcFinding
    {
        public NcSeverity Severity { get; init; }

        /// <summary>1-based line of the file; 0 for the whole file.</summary>
        public int Line { get; init; }

        public string Code { get; init; }
        public string Text { get; init; }

        /// <summary>The stored form: "E|12|AXIS|text".</summary>
        public string Stored => (Severity == NcSeverity.Error ? "E" : "W") + "|" + Line.ToString(CultureInfo.InvariantCulture) + "|" + Code + "|" + Text;
    }

    /// <summary>What a machine can take (from <see cref="MfMachineConnection"/>); null fields are not checked.</summary>
    public sealed class NcProfile
    {
        public MfNcControllerFamily Family { get; init; }
        public bool RequirePercent { get; init; }
        public int? MaxBytes { get; init; }
        public string Axes { get; init; }
        public int? MaxSpindleRpm { get; init; }
        public int? MaxFeedMmPerMin { get; init; }
        public int? ToolCapacity { get; init; }
        public bool IsLathe { get; init; }

        /// <summary>The name used in messages, for example the machine title.</summary>
        public string Name { get; init; }

        public static NcProfile Of(MfMachineConnection c, string name) => c == null ? new NcProfile { Name = name } : new NcProfile
        {
            Family = c.ControllerFamily, RequirePercent = c.RequirePercent, MaxBytes = c.MaxProgramBytes, Axes = c.Axes, MaxSpindleRpm = c.MaxSpindleRpm,
            MaxFeedMmPerMin = c.MaxFeedMmPerMin, ToolCapacity = c.ToolCapacity, IsLathe = c.IsLathe, Name = name,
        };
    }

    public sealed class NcAnalysis
    {
        public List<NcFinding> Findings { get; } = new List<NcFinding>();
        public string ProgramNumber { get; set; }
        public int LineCount { get; set; }
        public int SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public int ErrorCount => Findings.Count(f => f.Severity == NcSeverity.Error);
        public int WarningCount => Findings.Count(f => f.Severity == NcSeverity.Warning);

        /// <summary>The findings as stored on the revision (at most 4000 characters; the counts stay exact).</summary>
        public string Stored()
        {
            var sb = new StringBuilder();
            foreach (NcFinding f in Findings)
            {
                string line = f.Stored;
                if (sb.Length + line.Length + 1 > 3900) { sb.Append("W|0|MORE|یافته‌های بیشتری وجود دارد؛ فقط موارد اول نگه داشته شد."); break; }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }

    public sealed class NcDiff
    {
        public int Added { get; set; }
        public int Removed { get; set; }

        /// <summary>Changed blocks with two lines of context: "  12 text", "- 13 text", "+ 13 text", "…" between blocks. Empty when the change is too large to show.</summary>
        public List<string> Lines { get; } = new List<string>();

        public bool Truncated { get; set; }

        /// <summary>The files were too different to compare line by line in the time budget: the counts are those of the lines without an equal line anywhere in the other file.</summary>
        public bool Approximate { get; set; }
    }

    /// <summary>
    /// The pure rules of the DNC: the check of a program against the machine it goes to, the program number, the program name on the control, the line endings, the difference of two revisions and the start gate.
    /// No database, no clock: everything is an input (unit tested in DncTests).
    /// </summary>
    public static class MfNcRules
    {
        /// <summary>★ provisional decision: the largest program the library stores (4 MB).</summary>
        public const int GlobalMaxBytes = 4 * 1024 * 1024;

        private static readonly Regex IsoWord = new Regex(@"([A-Z])\s*([-+]?(?:\d+\.?\d*|\.\d+))", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IsoProgram = new Regex(@"^\s*[O:]\s*(\d{1,8})", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TncBegin = new Regex(@"^\s*\d*\s*BEGIN\s+PGM\s+(\S+)\s+(MM|INCH)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex TncEnd = new Regex(@"^\s*\d*\s*END\s+PGM\s+(\S+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex TncBlockNumber = new Regex(@"^\s*(\d+)\s", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TncAxis = new Regex(@"^I?([XYZABCUVW])[+-]?(?:\d|\.\d|Q)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TncToolCall = new Regex(@"\bTOOL\s+(?:CALL|DEF)\s+(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex TncSpeed = new Regex(@"\bS\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TncFeed = new Regex(@"(?:^|\s)F(\d+(?:\.\d+)?)(?=\s|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>()));

        private static bool IsHeidenhainConversational(MfNcControllerFamily f) => f == MfNcControllerFamily.HeidenhainConversational;

        /// <summary>The lines of the file (CR LF, LF and CR all end a line); the bytes are read as Latin-1 so every byte stays one character.</summary>
        public static List<string> Lines(byte[] bytes)
        {
            string text = Encoding.Latin1.GetString(bytes ?? Array.Empty<byte>());
            var lines = new List<string>(text.Length / 20 + 1);
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\r' && c != '\n') continue;
                lines.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            if (start < text.Length) lines.Add(text.Substring(start));
            return lines;
        }

        /// <summary>The content with the line endings of the machine, ending with one line end. The stored content is never changed; this is what goes out.</summary>
        public static byte[] Normalise(byte[] bytes, MfNcLineEnding ending)
        {
            string eol = ending switch { MfNcLineEnding.Lf => "\n", MfNcLineEnding.Cr => "\r", _ => "\r\n" };
            List<string> lines = Lines(bytes);
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            var sb = new StringBuilder();
            foreach (string l in lines) sb.Append(l).Append(eol);
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        /// <summary>The program number or name in the file: O1234 / :1234 (ISO) or the name of BEGIN PGM (Heidenhain). Null when there is none.</summary>
        public static string ProgramNumberOf(IReadOnlyList<string> lines, MfNcControllerFamily family)
        {
            int look = Math.Min(lines.Count, 20);
            for (int i = 0; i < look; i++)
            {
                string l = lines[i];
                Match tnc = TncBegin.Match(l);
                if (tnc.Success) return tnc.Groups[1].Value;
                if (IsHeidenhainConversational(family)) continue;
                string code = StripComments(l, family).ToUpperInvariant();
                Match iso = IsoProgram.Match(code);
                if (iso.Success) return "O" + iso.Groups[1].Value;
            }
            return null;
        }

        /// <summary>The line without comments: "( … )" for ISO controls, everything after ";" for Heidenhain conversational and ISO.</summary>
        public static string StripComments(string line, MfNcControllerFamily family)
        {
            if (string.IsNullOrEmpty(line)) return line ?? "";
            var sb = new StringBuilder(line.Length);
            int depth = 0;
            foreach (char c in line)
            {
                if (c == ';' && depth == 0) break;
                if (!IsHeidenhainConversational(family))
                {
                    if (c == '(') { depth++; continue; }
                    if (c == ')' && depth > 0) { depth--; continue; }
                    if (depth > 0) continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void Add(NcAnalysis a, NcSeverity s, int line, string code, string text) =>
            a.Findings.Add(new NcFinding { Severity = s, Line = line, Code = code, Text = text });

        private static decimal Num(string s) => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d) ? d : 0m;

        /// <summary>The whole part of a word value, or -1 when it does not fit an int (a malformed word such as G123456789012 is not a G code; it must never throw).</summary>
        private static int Whole(decimal v) => v > int.MaxValue || v < int.MinValue ? -1 : (int)Math.Truncate(v);

        /// <summary>The check of a program against one machine. Errors stop the submission and the transfer; warnings are shown.</summary>
        public static NcAnalysis Analyse(byte[] bytes, NcProfile profile)
        {
            profile ??= new NcProfile();
            bytes ??= Array.Empty<byte>();
            var a = new NcAnalysis { SizeBytes = bytes.Length, Sha256 = Sha256Hex(bytes) };
            if (bytes.Length == 0) { Add(a, NcSeverity.Error, 0, "EMPTY", "فایل برنامه خالی است."); return a; }
            if (bytes.Length > GlobalMaxBytes) Add(a, NcSeverity.Error, 0, "SIZE", $"حجم فایل ({bytes.Length:N0} بایت) بیش از حد مجاز کتابخانه ({GlobalMaxBytes:N0} بایت) است.");
            else if (profile.MaxBytes.HasValue && bytes.Length > profile.MaxBytes.Value)
                Add(a, NcSeverity.Error, 0, "SIZE", $"حجم فایل ({bytes.Length:N0} بایت) بیش از ظرفیت حافظه کنترلر {profile.Name} ({profile.MaxBytes.Value:N0} بایت) است.");

            List<string> lines = Lines(bytes);
            a.LineCount = lines.Count;

            // characters: old controls take plain ASCII only (a Persian letter in a comment stops the transfer on the control)
            int badLine = 0, controlLine = 0;
            for (int i = 0; i < lines.Count && (badLine == 0 || controlLine == 0); i++)
                foreach (char c in lines[i])
                {
                    if (c > 126 && badLine == 0) badLine = i + 1;
                    else if (c < 32 && c != '\t' && controlLine == 0) controlLine = i + 1;
                }
            if (badLine > 0) Add(a, NcSeverity.Error, badLine, "ASCII", "فایل نویسه غیر ASCII (مثلاً حروف فارسی یا علامت خاص) دارد؛ کنترلر آن را نمی‌پذیرد. نخستین مورد در خط " + badLine + ".");
            if (controlLine > 0) Add(a, NcSeverity.Warning, controlLine, "CONTROL", "فایل نویسه کنترلی دارد (خط " + controlLine + ")؛ مطمئن شوید کنترلر آن را می‌پذیرد.");

            a.ProgramNumber = ProgramNumberOf(lines, profile.Family);
            if (IsHeidenhainConversational(profile.Family)) CheckHeidenhain(a, lines, profile);
            else CheckIso(a, lines, profile);
            return a;
        }

        private static int FirstContent(IReadOnlyList<string> lines) { for (int i = 0; i < lines.Count; i++) if (lines[i].Trim().Length > 0) return i; return -1; }

        private static int LastContent(IReadOnlyList<string> lines) { for (int i = lines.Count - 1; i >= 0; i--) if (lines[i].Trim().Length > 0) return i; return -1; }

        private static void CheckHeidenhain(NcAnalysis a, List<string> lines, NcProfile p)
        {
            int first = FirstContent(lines), last = LastContent(lines);
            Match begin = first >= 0 ? TncBegin.Match(lines[first]) : Match.Empty;
            Match end = last >= 0 ? TncEnd.Match(lines[last]) : Match.Empty;
            if (!begin.Success) Add(a, NcSeverity.Error, first + 1, "BEGIN", "برنامه هایدن‌هاین باید با «BEGIN PGM نام MM» شروع شود.");
            if (!end.Success) Add(a, NcSeverity.Error, last + 1, "END", "برنامه هایدن‌هاین باید با «END PGM نام» تمام شود.");
            if (begin.Success && end.Success && !string.Equals(begin.Groups[1].Value, end.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                Add(a, NcSeverity.Error, last + 1, "PGMNAME", $"نام برنامه در BEGIN PGM ({begin.Groups[1].Value}) و END PGM ({end.Groups[1].Value}) یکی نیست.");

            string axes = (p.Axes ?? "").ToUpperInvariant();
            int expected = 0;
            bool numberingReported = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i];
                if (raw.Trim().Length == 0) continue;
                Match bn = TncBlockNumber.Match(raw);
                if (!numberingReported && (!bn.Success || !int.TryParse(bn.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n != expected))
                {
                    Add(a, NcSeverity.Warning, i + 1, "BLOCKNO", $"شماره بلوک‌ها از خط {i + 1} پیوسته نیست (انتظار {expected})؛ کنترلر هنگام دریافت ممکن است خطا بدهد.");
                    numberingReported = true;
                }
                expected++;
                string line = StripComments(raw, p.Family).ToUpperInvariant();
                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                bool toolLine = line.Contains("TOOL CALL", StringComparison.Ordinal) || line.Contains("TOOL DEF", StringComparison.Ordinal);
                if (axes.Length > 0 && !toolLine && !line.Contains("CYCL DEF", StringComparison.Ordinal) && !line.Contains("FN ", StringComparison.Ordinal))
                {
                    foreach (string t in tokens.Skip(1))
                    {
                        Match ax = TncAxis.Match(t);
                        if (ax.Success && axes.IndexOf(ax.Groups[1].Value[0]) < 0)
                        {
                            Add(a, NcSeverity.Error, i + 1, "AXIS", $"خط {i + 1}: محور {ax.Groups[1].Value} روی ماشین {p.Name} وجود ندارد (محورهای ماشین: {axes}).");
                            break;
                        }
                    }
                }
                if (toolLine)
                {
                    Match tool = TncToolCall.Match(line);
                    if (tool.Success && p.ToolCapacity.HasValue && int.TryParse(tool.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int tn) && tn > p.ToolCapacity.Value)
                        Add(a, NcSeverity.Error, i + 1, "TOOL", $"خط {i + 1}: ابزار {tn} بیش از ظرفیت مخزن ابزار ماشین {p.Name} ({p.ToolCapacity.Value}) است.");
                    Match speed = TncSpeed.Match(line);
                    if (speed.Success && p.MaxSpindleRpm.HasValue && Num(speed.Groups[1].Value) > p.MaxSpindleRpm.Value)
                        Add(a, NcSeverity.Error, i + 1, "RPM", $"خط {i + 1}: دور اسپیندل {speed.Groups[1].Value} بیش از حداکثر دور ماشین {p.Name} ({p.MaxSpindleRpm.Value}) است.");
                }
                if (p.MaxFeedMmPerMin.HasValue)
                {
                    Match feed = TncFeed.Match(line);
                    if (feed.Success && Num(feed.Groups[1].Value) > p.MaxFeedMmPerMin.Value)
                        Add(a, NcSeverity.Warning, i + 1, "FEED", $"خط {i + 1}: پیشروی {feed.Groups[1].Value} بیش از حداکثر پیشروی ماشین {p.Name} ({p.MaxFeedMmPerMin.Value}) است.");
                }
            }
        }

        private static void CheckIso(NcAnalysis a, List<string> lines, NcProfile p)
        {
            int first = FirstContent(lines), last = LastContent(lines);
            if (p.RequirePercent)
            {
                if (first < 0 || !lines[first].TrimStart().StartsWith("%", StringComparison.Ordinal)) Add(a, NcSeverity.Error, first + 1, "PERCENT", "برنامه باید با نویسه % شروع شود (قالب نوار ISO).");
                if (last < 0 || last == first || lines[last].Trim() != "%") Add(a, NcSeverity.Error, last + 1, "PERCENT", "برنامه باید با یک خط % تمام شود (قالب نوار ISO).");
            }
            if (p.Family == MfNcControllerFamily.Fanuc && a.ProgramNumber != null && a.ProgramNumber.StartsWith("O", StringComparison.Ordinal))
            {
                int number = int.Parse(a.ProgramNumber.Substring(1), CultureInfo.InvariantCulture);
                if (number == 0 || number > 9999) Add(a, NcSeverity.Error, 0, "PGMNO", $"شماره برنامه {a.ProgramNumber} برای کنترلر فانوک معتبر نیست (۱ تا ۹۹۹۹).");
            }

            string axes = (p.Axes ?? "").ToUpperInvariant();
            if (p.IsLathe && axes.Length > 0)
            {
                // on a lathe U and W are the incremental X and Z
                if (axes.Contains('X')) axes += "U";
                if (axes.Contains('Z')) axes += "W";
            }
            bool surfaceSpeed = false;          // G96 (lathe): S is metres per minute, not revolutions
            bool feedPerRev = p.IsLathe;        // lathe default G99 (per revolution); mill default G94 (per minute)
            bool hasEnd = false, hasSubEnd = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = StripComments(lines[i], p.Family).ToUpperInvariant();
                if (line.Trim().Length == 0) continue;
                MatchCollection words = IsoWord.Matches(line);
                var g = new HashSet<int>();
                foreach (Match w in words) if (w.Groups[1].Value == "G") g.Add(Whole(Num(w.Groups[2].Value)));
                if (g.Contains(96)) surfaceSpeed = true;
                if (g.Contains(97)) surfaceSpeed = false;
                if (g.Contains(95) || (p.IsLathe && g.Contains(99))) feedPerRev = true;
                if (g.Contains(94) || (p.IsLathe && g.Contains(98))) feedPerRev = false;
                bool clamp = g.Contains(92) || (p.IsLathe && g.Contains(50));   // spindle speed clamp
                bool macro = g.Contains(65) || g.Contains(66) || g.Contains(10) || g.Contains(71) || g.Contains(72) || g.Contains(73) || g.Contains(76) || line.Contains('#');
                bool axisReported = false;
                foreach (Match w in words)
                {
                    char letter = w.Groups[1].Value[0];
                    decimal value = Num(w.Groups[2].Value);
                    switch (letter)
                    {
                        case 'M':
                            if (value == 30m || value == 2m) hasEnd = true;
                            if (value == 99m) hasSubEnd = true;
                            break;
                        case 'S':
                            if (p.MaxSpindleRpm.HasValue && (clamp || !surfaceSpeed) && value > p.MaxSpindleRpm.Value)
                                Add(a, NcSeverity.Error, i + 1, "RPM", $"خط {i + 1}: دور اسپیندل {w.Groups[2].Value} بیش از حداکثر دور ماشین {p.Name} ({p.MaxSpindleRpm.Value}) است.");
                            break;
                        case 'F':
                            if (p.MaxFeedMmPerMin.HasValue && !feedPerRev && value > p.MaxFeedMmPerMin.Value)
                                Add(a, NcSeverity.Warning, i + 1, "FEED", $"خط {i + 1}: پیشروی {w.Groups[2].Value} بیش از حداکثر پیشروی ماشین {p.Name} ({p.MaxFeedMmPerMin.Value}) است.");
                            break;
                        case 'T':
                            if (p.ToolCapacity.HasValue)
                            {
                                int tool = Whole(Math.Abs(value));
                                if (p.IsLathe)
                                {
                                    // T0101: tool 01, offset 01. A two-digit T on a lathe (T12) is tool+offset in a one-digit form on some controls and offset 12 of tool 0 on others: not checked
                                    if (w.Groups[2].Value.TrimStart('+', '-').Length < 3) break;
                                    tool /= 100;
                                }
                                if (tool > p.ToolCapacity.Value)
                                    Add(a, NcSeverity.Error, i + 1, "TOOL", $"خط {i + 1}: ابزار {tool} بیش از ظرفیت ابزار ماشین {p.Name} ({p.ToolCapacity.Value}) است.");
                            }
                            break;
                        default:
                            if (!axisReported && !macro && axes.Length > 0 && "XYZABCUVW".IndexOf(letter) >= 0 && axes.IndexOf(letter) < 0)
                            {
                                Add(a, NcSeverity.Error, i + 1, "AXIS", $"خط {i + 1}: محور {letter} روی ماشین {p.Name} وجود ندارد (محورهای ماشین: {(p.Axes ?? "").ToUpperInvariant()}).");
                                axisReported = true;
                            }
                            break;
                    }
                }
            }
            if (!hasEnd && !hasSubEnd) Add(a, NcSeverity.Error, last + 1, "NOEND", "پایان برنامه (M30 یا M02) پیدا نشد؛ برنامه ناقص است.");
            else if (!hasEnd) Add(a, NcSeverity.Warning, last + 1, "SUBPROGRAM", "این فایل زیربرنامه است (M99 بدون M30).");
        }

        // ------------------------------------------------------------------------------------------ program name on the control

        private static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9._\-]{1,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// The file extensions of CNC programs a link may read or write (review finding 1): Heidenhain .H / .I, Siemens .MPF / .SPF, the ISO tape forms and none at all (a Fanuc O-number).
        /// Nothing else (.json, .config, .dll ...) ever goes to or comes from a machine folder.
        /// </summary>
        public static readonly string[] NcExtensions = { ".H", ".I", ".NC", ".MPF", ".SPF", ".TAP", ".PRG", ".CNC", ".MIN", ".EIA", ".PIM", ".ISO", ".PGM", ".DNC", ".ANC", ".PIT" };

        /// <summary>True for an extension of <see cref="NcExtensions"/> (with or without the dot).</summary>
        public static bool IsNcExtension(string extension)
        {
            string e = (extension ?? "").Trim();
            if (e.Length == 0) return true;
            if (!e.StartsWith(".", StringComparison.Ordinal)) e = "." + e;
            return NcExtensions.Contains(e, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>A program name a control may hold: Latin letters, digits, dot, dash, underscore (1..32), not starting with a dot, and a CNC extension or none.</summary>
        public static bool IsNcFileName(string name)
        {
            if (name == null || !SafeName.IsMatch(name) || name.StartsWith(".", StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal)) return false;
            int dot = name.LastIndexOf('.');
            return dot < 0 || IsNcExtension(name.Substring(dot));
        }

        /// <summary>
        /// The name the program gets on the control: the format of the machine with "{number}" (the program number of the file without its letter), "{O}" (the number with its letter, for example O1234) and "{code}"
        /// (the program code); the file extension of the machine replaces the extension of the name unless the name already ends with it. Empty format: the file name. Error: null name and the Persian reason.
        /// </summary>
        public static (string Name, string Error) TargetName(string format, string extension, string programNumber, string programCode, string fileName)
        {
            string name;
            if (string.IsNullOrWhiteSpace(format)) name = fileName ?? "";
            else
            {
                string digits = programNumber == null ? null : new string(programNumber.Where(char.IsAsciiDigit).ToArray());
                if ((format.Contains("{number}") || format.Contains("{O}")) && string.IsNullOrEmpty(digits))
                    return (null, "شماره برنامه در فایل پیدا نشد، ولی قالب نام برنامه روی کنترلر به آن نیاز دارد.");
                name = format.Replace("{number}", digits ?? "").Replace("{O}", programNumber ?? "").Replace("{code}", programCode ?? "");
            }
            name = name.Trim();
            string ext = (extension ?? "").Trim();
            if (ext.Length > 0)
            {
                if (!ext.StartsWith(".", StringComparison.Ordinal)) ext = "." + ext;
                if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    // the extension of the machine replaces the one of the file (part.nc -> part.H)
                    int dot = name.LastIndexOf('.');
                    if (dot > 0) name = name.Substring(0, dot);
                    name += ext;
                }
            }
            if (!SafeName.IsMatch(name)) return (null, $"نام برنامه روی کنترلر ({name}) معتبر نیست: فقط حروف لاتین، رقم، نقطه، خط تیره و زیرخط و حداکثر ۳۲ نویسه.");
            if (!IsNcFileName(name)) return (null, $"نام برنامه روی کنترلر ({name}) پسوند برنامه CNC ندارد (مثلاً .H، .NC، .MPF یا بدون پسوند).");
            return (name, null);
        }

        // ------------------------------------------------------------------------------------------ difference of two revisions

        /// <summary>The largest edit distance for which the changed lines are listed (beyond it only the counts are given).</summary>
        public const int MaxListedDistance = 2000;

        public const int MaxDiffLines = 400;

        /// <summary>Line difference (Myers): the counts are exact; the listed blocks carry two lines of context and are cut at <see cref="MaxDiffLines"/> lines.</summary>
        public static NcDiff Diff(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
        {
            oldLines ??= Array.Empty<string>();
            newLines ??= Array.Empty<string>();
            var result = new NcDiff();
            int prefix = 0;
            while (prefix < oldLines.Count && prefix < newLines.Count && oldLines[prefix] == newLines[prefix]) prefix++;
            int suffix = 0;
            while (suffix < oldLines.Count - prefix && suffix < newLines.Count - prefix && oldLines[oldLines.Count - 1 - suffix] == newLines[newLines.Count - 1 - suffix]) suffix++;
            int n = oldLines.Count - prefix - suffix, m = newLines.Count - prefix - suffix;
            if (n == 0 && m == 0) return result;

            // edit script of the middle part: 0 = keep, -1 = removed old line, +1 = added new line
            List<(int Op, int Old, int New)> script = EditScript(oldLines, newLines, prefix, n, m, out int distance);
            if (script == null)
            {
                result.Truncated = true;
                if (distance >= 0)
                {
                    // too many changes to list: the exact counts from the distance (n + m - 2 LCS = distance, removed - added = n - m)
                    result.Removed = (distance + n - m) / 2;
                    result.Added = (distance - n + m) / 2;
                }
                else
                {
                    // the files are too different for the work budget: the counts of the lines that are not matched by an equal line (no order); marked approximate
                    var count = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = prefix; i < prefix + n; i++) count[oldLines[i]] = count.TryGetValue(oldLines[i], out int c) ? c + 1 : 1;
                    int matched = 0;
                    for (int i = prefix; i < prefix + m; i++)
                        if (count.TryGetValue(newLines[i], out int c) && c > 0) { count[newLines[i]] = c - 1; matched++; }
                    result.Removed = n - matched;
                    result.Added = m - matched;
                    result.Approximate = true;
                }
                return result;
            }
            result.Removed = script.Count(s => s.Op < 0);
            result.Added = script.Count(s => s.Op > 0);

            // the full sequence with the common prefix and suffix, then the blocks with context
            var all = new List<(int Op, int Old, int New)>();
            for (int i = 0; i < prefix; i++) all.Add((0, i, i));
            all.AddRange(script);
            for (int i = 0; i < suffix; i++) all.Add((0, oldLines.Count - suffix + i, newLines.Count - suffix + i));
            const int context = 2;
            var show = new bool[all.Count];
            for (int i = 0; i < all.Count; i++)
                if (all[i].Op != 0)
                    for (int k = Math.Max(0, i - context); k <= Math.Min(all.Count - 1, i + context); k++) show[k] = true;
            bool gap = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (!show[i]) { gap = result.Lines.Count > 0; continue; }
                if (gap) { result.Lines.Add("…"); gap = false; }
                if (result.Lines.Count >= MaxDiffLines) { result.Truncated = true; break; }
                var s = all[i];
                result.Lines.Add(s.Op == 0 ? "  " + (s.New + 1).ToString(CultureInfo.InvariantCulture) + " " + newLines[s.New]
                    : s.Op < 0 ? "- " + (s.Old + 1).ToString(CultureInfo.InvariantCulture) + " " + oldLines[s.Old]
                    : "+ " + (s.New + 1).ToString(CultureInfo.InvariantCulture) + " " + newLines[s.New]);
            }
            return result;
        }

        /// <summary>The work budget of one difference (steps of the Myers search); beyond it the counts are approximate.</summary>
        public const long DiffWorkBudget = 60_000_000;

        /// <summary>
        /// Myers O((N+M)D) on the middle part. Null when the distance is above <see cref="MaxListedDistance"/> (the exact distance is still returned) or when the work budget runs out (distance -1).
        /// The trace keeps only the window -d-1..d+1 of every round (about D² numbers in all).
        /// </summary>
        private static List<(int Op, int Old, int New)> EditScript(IReadOnlyList<string> a, IReadOnlyList<string> b, int offset, int n, int m, out int distance)
        {
            int max = n + m;
            int[] v = new int[2 * max + 3];
            var trace = new List<int[]>();
            distance = -1;
            bool keepTrace = true;
            long work = 0;
            for (int d = 0; d <= max; d++)
            {
                if (keepTrace)
                {
                    var snap = new int[2 * d + 3];
                    for (int k = -d - 1; k <= d + 1; k++)
                    {
                        int at = k + max;
                        snap[k + d + 1] = at >= 0 && at < v.Length ? v[at] : 0;
                    }
                    trace.Add(snap);
                }
                for (int k = -d; k <= d; k += 2)
                {
                    int x = (k == -d || (k != d && v[k - 1 + max] < v[k + 1 + max])) ? v[k + 1 + max] : v[k - 1 + max] + 1;
                    int y = x - k;
                    while (x < n && y < m && a[offset + x] == b[offset + y]) { x++; y++; work++; }
                    v[k + max] = x;
                    work++;
                    if (x >= n && y >= m)
                    {
                        distance = d;
                        if (!keepTrace) return null;
                        return Backtrack(trace, n, m, offset);
                    }
                }
                if (work > DiffWorkBudget) { distance = -1; return null; }
                if (d >= MaxListedDistance && keepTrace) { keepTrace = false; trace = null; }
            }
            return null;
        }

        private static List<(int Op, int Old, int New)> Backtrack(List<int[]> trace, int n, int m, int offset)
        {
            var script = new List<(int Op, int Old, int New)>();
            int x = n, y = m;
            for (int d = trace.Count - 1; d >= 0; d--)
            {
                int[] w = trace[d];
                int V(int k) => w[k + d + 1];
                int k = x - y;
                int prevK = (k == -d || (k != d && V(k - 1) < V(k + 1))) ? k + 1 : k - 1;
                int prevX = d == 0 ? 0 : V(prevK);
                int prevY = prevX - prevK;
                while (x > prevX && y > prevY) { x--; y--; script.Add((0, offset + x, offset + y)); }
                if (d > 0)
                {
                    if (x == prevX) { y--; script.Add((1, offset + x, offset + y)); }
                    else { x--; script.Add((-1, offset + x, offset + y)); }
                }
            }
            script.Reverse();
            return script;
        }

        // ------------------------------------------------------------------------------------------ the start gate

        /// <summary>The bookings that run a program on the machine: setup, run and rework (review finding 3).</summary>
        public static bool Machining(MfBookingKind kind) => kind == MfBookingKind.Run || kind == MfBookingKind.Setup || kind == MfBookingKind.Rework;

        public sealed class GateTransfer
        {
            public int Id { get; init; }
            public int MachineId { get; init; }
            public int? RevisionId { get; init; }
            public MfNcTransferStatus Status { get; init; }
            public DateTimeOffset? ConfirmedAt { get; init; }
        }

        /// <summary>
        /// May the operation start on the machine? Only a run or a setup of an operation whose routing step has an active program with an approved current revision is gated, and only on a machine that requires the
        /// confirmed program. Then the current revision must have been sent to this machine and confirmed on the control after it was approved. Returns the transfer that covers the start, or the Persian reason.
        /// </summary>
        public static (GateTransfer Transfer, string Error) StartGate(bool machineRequires, MfBookingKind kind, string programCode, int? currentRevisionId, DateTimeOffset? revisionApprovedAt,
            int machineId, IEnumerable<GateTransfer> transfers)
        {
            if (!machineRequires || !Machining(kind) || !currentRevisionId.HasValue) return (null, null);
            GateTransfer ok = (transfers ?? Enumerable.Empty<GateTransfer>())
                .Where(t => t.MachineId == machineId && t.RevisionId == currentRevisionId && t.Status == MfNcTransferStatus.Confirmed && t.ConfirmedAt.HasValue
                    && (!revisionApprovedAt.HasValue || t.ConfirmedAt.Value >= revisionApprovedAt.Value))
                .OrderByDescending(t => t.ConfirmedAt).FirstOrDefault();
            if (ok != null) return (ok, null);
            return (null, $"پیش از شروع، نسخه جاری برنامه {programCode} باید به این ماشین ارسال و دریافت آن روی کنترلر تأیید شود (DNC).");
        }
    }
}
