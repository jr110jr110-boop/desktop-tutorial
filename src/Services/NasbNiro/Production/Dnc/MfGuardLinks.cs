using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;

namespace Services.NasbNiro.Production.Dnc
{
    // Production release P2 part E: the links of the guarded machining process. One session reads the control; one writer drives the start-enable relay.

    /// <summary>What the control reports at one moment (null: the control did not answer that item).</summary>
    public sealed class ControlSnapshot
    {
        public MfExecMode? ExecMode { get; set; }
        public MfProgramState? ProgramState { get; set; }
        public int? FeedOverride { get; set; }
        public int? SpindleOverride { get; set; }
        public string SelectedProgram { get; set; }
        public int? BlockNumber { get; set; }
        public int? ToolNumber { get; set; }
    }

    /// <summary>An open conversation with one control (LSV2 in production, a fake in the tests).</summary>
    public interface IMfControlSession : IDisposable
    {
        /// <summary>Control model and NC software (null: unknown).</summary>
        string Identity { get; }

        Task<ControlSnapshot> ReadAsync(CancellationToken ct);

        /// <summary>The file from the control; null when it is not there.</summary>
        Task<byte[]> ReadFileAsync(string path, long maxBytes, CancellationToken ct);

        /// <summary>Last-change time of a file on the control (null: missing or not answered).</summary>
        Task<DateTimeOffset?> FileTimeAsync(string path, CancellationToken ct);

        /// <summary>What each item answered, in Persian (for the capability test).</summary>
        Task<List<string>> CapabilitiesAsync(string programFolder, CancellationToken ct);
    }

    public interface IMfControlLinkFactory
    {
        /// <summary>Opens a session to the control of the monitor's data link; throws with a Persian message when it cannot.</summary>
        Task<IMfControlSession> OpenAsync(MfMachineMonitor monitor, string plcPassword, CancellationToken ct);
    }

    public interface IMfCoilWriter
    {
        /// <summary>Writes one coil (Modbus function 05); null = done, else the Persian error.</summary>
        Task<string> WriteAsync(string host, int port, byte unit, int coil, bool on, CancellationToken ct);
    }

    /// <summary>The pure rules of the guard (unit tested).</summary>
    public static class MfGuardRules
    {
        public static readonly string[] PlcKeys = { "exec", "state", "feed", "spindle", "tool", "block" };
        private static readonly Regex PlcEntry = new Regex(@"^(?<k>[a-z]+)=(?<t>[WM])(?<n>\d{1,5})$", RegexOptions.CultureInvariant);

        /// <summary>Gate codes a production manager can open once.</summary>
        public static readonly string[] Gates = { "program", "tools", "prove-out", "interlock" };

        /// <summary>Feed override at or below this (percent) during a run counts as a prove-out at reduced feed.</summary>
        public const int ProveOutFeedMax = 50;

        public const decimal DefaultRadiusTolerance = 0.02m;
        public const decimal DefaultLengthTolerance = 1.0m;

        /// <summary>"exec=W100;state=W102;feed=W104" into key -&gt; (type, number); null and the Persian error when an entry is wrong.</summary>
        public static (Dictionary<string, (char Type, int Number)> Map, string Error) ParsePlcMap(string text)
        {
            var map = new Dictionary<string, (char, int)>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text)) return (map, null);
            foreach (string raw in text.Split(new[] { ';', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Match m = PlcEntry.Match(raw.ToLowerInvariant().Replace(" ", string.Empty).Replace("=w", "=W").Replace("=m", "=M"));
                if (!m.Success || !PlcKeys.Contains(m.Groups["k"].Value))
                    return (null, $"بند «{raw}» نشانی PLC نامعتبر است؛ نمونه درست: exec=W100;state=W102;feed=W104 (کلیدها: {string.Join("، ", PlcKeys)}).");
                if (m.Groups["t"].Value == "M" && m.Groups["k"].Value != "state") return (null, $"«{raw}»: فقط وضعیت برنامه را می‌توان از مارکر خواند؛ بقیه از Word خوانده می‌شود.");
                map[m.Groups["k"].Value] = (m.Groups["t"].Value[0], int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));
            }
            return (map, null);
        }

        public static MfExecMode Exec(Lsv2ExecState s) => s switch
        {
            Lsv2ExecState.Manual => MfExecMode.Manual,
            Lsv2ExecState.Mdi => MfExecMode.Mdi,
            Lsv2ExecState.SingleStep => MfExecMode.SingleBlock,
            Lsv2ExecState.Automatic => MfExecMode.Automatic,
            Lsv2ExecState.PassReferences => MfExecMode.Other,
            _ => MfExecMode.Unknown,
        };

        public static MfProgramState Program(Lsv2PgmState s) => s switch
        {
            Lsv2PgmState.Started => MfProgramState.Running,
            Lsv2PgmState.Stopped => MfProgramState.Stopped,
            Lsv2PgmState.Finished => MfProgramState.Finished,
            Lsv2PgmState.Cancelled => MfProgramState.Interrupted,
            Lsv2PgmState.Interrupted => MfProgramState.Interrupted,
            Lsv2PgmState.Error => MfProgramState.Error,
            Lsv2PgmState.ErrorCleared => MfProgramState.Stopped,
            Lsv2PgmState.Idle => MfProgramState.Idle,
            _ => MfProgramState.Unknown,
        };

        public static string Text(MfExecMode m) => m switch
        {
            MfExecMode.Manual => "دستی",
            MfExecMode.Mdi => "MDI",
            MfExecMode.SingleBlock => "تک‌بلوک",
            MfExecMode.Automatic => "خودکار",
            MfExecMode.Other => "دیگر",
            _ => "نامعلوم",
        };

        public static string Text(MfProgramState s) => s switch
        {
            MfProgramState.Running => "در حال اجرا",
            MfProgramState.Stopped => "متوقف",
            MfProgramState.Finished => "پایان‌یافته",
            MfProgramState.Interrupted => "قطع‌شده",
            MfProgramState.Error => "خطا",
            MfProgramState.Idle => "آماده",
            _ => "نامعلوم",
        };

        /// <summary>
        /// The text of a program in a form two copies can be compared by: upper case, one space between words, no empty lines, no line-end differences. A control may re-space the blocks when it
        /// stores them; a changed value, axis or block is still a difference.
        /// </summary>
        public static string Canonical(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => Regex.Replace(l.Trim(), @"\s+", " ").ToUpperInvariant())
                .Where(l => l.Length > 0);
            return string.Join("\n", lines);
        }

        public static string CanonicalSha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(Encoding.Latin1.GetString(bytes ?? Array.Empty<byte>())))));

        /// <summary>The first line (1-based, of the approved copy) where the two copies differ; 0 when they are the same.</summary>
        public static int FirstDifference(byte[] approved, byte[] onControl)
        {
            string[] a = Canonical(Encoding.Latin1.GetString(approved ?? Array.Empty<byte>())).Split('\n');
            string[] b = Canonical(Encoding.Latin1.GetString(onControl ?? Array.Empty<byte>())).Split('\n');
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i + 1;
            return a.Length == b.Length ? 0 : n + 1;
        }

        /// <summary>The path of a program on the control: the folder plus the name it was sent under.</summary>
        public static string ProgramPath(string folder, string name)
        {
            string f = string.IsNullOrWhiteSpace(folder) ? "TNC:\\" : folder.Trim().Replace('/', '\\');
            if (!f.EndsWith("\\", StringComparison.Ordinal)) f += "\\";
            return f + name;
        }

        /// <summary>A folder or file path on the control: drive letters, letters, digits, dot, dash, underscore and backslash only (no "..").</summary>
        public static bool IsControlPath(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 120 && !path.Contains("..", StringComparison.Ordinal)
            && Regex.IsMatch(path, @"^[A-Za-z0-9_\-\.:\\/]+$", RegexOptions.CultureInvariant);

        public sealed class ToolRow
        {
            public int Number { get; init; }
            public string Name { get; init; }
            public decimal? L { get; init; }
            public decimal? R { get; init; }
            public decimal? DL { get; init; }
            public decimal? DR { get; init; }
        }

        private static decimal? Dec(string s) =>
            decimal.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d) ? d : null;

        /// <summary>
        /// The HEIDENHAIN tool table (TOOL.T): a "BEGIN TOOL .T" line, a header line whose column names start where the values start, then one fixed-width line per tool and "[END]".
        /// </summary>
        public static List<ToolRow> ParseToolTable(string text)
        {
            var rows = new List<ToolRow>();
            if (string.IsNullOrEmpty(text)) return rows;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int h = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*T\s+NAME\s", RegexOptions.CultureInvariant));
            if (h < 0) return rows;
            string header = lines[h];
            var cols = Regex.Matches(header, @"\S+").Select(m => (Name: m.Value.ToUpperInvariant(), Start: m.Index)).ToList();
            string Cell(string line, string name)
            {
                int i = cols.FindIndex(c => c.Name == name);
                if (i < 0 || cols[i].Start >= line.Length) return null;
                int end = i + 1 < cols.Count ? Math.Min(cols[i + 1].Start, line.Length) : line.Length;
                return line.Substring(cols[i].Start, end - cols[i].Start).Trim();
            }
            foreach (string line in lines.Skip(h + 1))
            {
                if (line.TrimStart().StartsWith("[END]", StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrWhiteSpace(line) || !int.TryParse(Cell(line, "T"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)) continue;
                rows.Add(new ToolRow { Number = t, Name = Cell(line, "NAME"), L = Dec(Cell(line, "L")), R = Dec(Cell(line, "R")), DL = Dec(Cell(line, "DL")), DR = Dec(Cell(line, "DR")) });
            }
            return rows;
        }

        /// <summary>
        /// The CAM tool list against the tool table of the control: every tool of the program must be in the table, measured (L &gt; 0), with the radius of the CAM tool within the tolerance
        /// (R + DR against diameter / 2), and long enough for the length the program assumes. Empty list = all good; otherwise one Persian line per tool.
        /// </summary>
        public static List<string> CheckTools(IEnumerable<MfCamTool> cam, IReadOnlyCollection<ToolRow> table, decimal radiusTolerance, decimal lengthTolerance)
        {
            var problems = new List<string>();
            foreach (MfCamTool t in cam.OrderBy(x => x.ToolNumber))
            {
                ToolRow row = table.FirstOrDefault(r => r.Number == t.ToolNumber);
                string name = $"ابزار {t.ToolNumber}" + (string.IsNullOrWhiteSpace(t.Name) ? string.Empty : $" ({t.Name})");
                if (row == null) { problems.Add($"{name} در جدول ابزار ماشین نیست."); continue; }
                if (!row.L.HasValue || row.L.Value <= 0) { problems.Add($"{name}: طول ابزار در جدول ماشین اندازه‌گیری نشده است (L = 0)."); continue; }
                if (t.Diameter.HasValue)
                {
                    decimal actual = (row.R ?? 0) + (row.DR ?? 0), wanted = t.Diameter.Value / 2m;
                    if (Math.Abs(actual - wanted) > radiusTolerance)
                        problems.Add($"{name}: شعاع در ماشین {actual.ToString("0.###", CultureInfo.InvariantCulture)} ولی در برنامه {wanted.ToString("0.###", CultureInfo.InvariantCulture)} است (تلرانس {radiusTolerance.ToString("0.###", CultureInfo.InvariantCulture)}).");
                }
                if (t.Length.HasValue && (row.L.Value + (row.DL ?? 0)) < t.Length.Value - lengthTolerance)
                    problems.Add($"{name}: طول ابزار در ماشین {(row.L.Value + (row.DL ?? 0)).ToString("0.###", CultureInfo.InvariantCulture)} از طول لازم برنامه {t.Length.Value.ToString("0.###", CultureInfo.InvariantCulture)} کوتاه‌تر است.");
            }
            return problems;
        }

        /// <summary>The CSV tool list of a CAM system: a header naming the tool number column (T / Tool / Number / شماره) and optionally Name, Diameter (D), Length (L); separators , ; or tab.</summary>
        public static (List<MfCamTool> Tools, string Error) ParseCamToolList(string csv)
        {
            var tools = new List<MfCamTool>();
            if (string.IsNullOrWhiteSpace(csv)) return (tools, "فهرست ابزار خالی است.");
            string[] lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            char sep = lines[0].Contains('\t') ? '\t' : lines[0].Contains(';') ? ';' : ',';
            string[] head = lines[0].Split(sep).Select(x => x.Trim().Trim('"').ToUpperInvariant()).ToArray();
            int Col(params string[] names) => Array.FindIndex(head, h => names.Contains(h));
            int ct = Col("T", "TOOL", "NUMBER", "TOOL NUMBER", "NO", "شماره"), cn = Col("NAME", "DESCRIPTION", "نام"), cd = Col("D", "DIA", "DIAMETER", "قطر"), cl = Col("L", "LENGTH", "GAUGE LENGTH", "OVERHANG", "طول");
            if (ct < 0) return (null, "ستون شماره ابزار (T یا Tool) در سطر عنوان فهرست ابزار نیست.");
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(sep).Select(x => x.Trim().Trim('"')).ToArray();
                if (ct >= c.Length || !int.TryParse(c[ct], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0 || n > 32767)
                    return (null, $"سطر {i + 1} فهرست ابزار شماره ابزار درست ندارد.");
                if (tools.Any(t => t.ToolNumber == n)) continue;   // the same tool in several operations
                tools.Add(new MfCamTool
                {
                    ToolNumber = n, Name = cn >= 0 && cn < c.Length ? (c[cn].Length > 200 ? c[cn][..200] : c[cn]) : null,
                    Diameter = cd >= 0 && cd < c.Length ? Dec(c[cd]) : null, Length = cl >= 0 && cl < c.Length ? Dec(c[cl]) : null,
                });
            }
            if (tools.Count == 0) return (null, "فهرست ابزار هیچ ابزاری ندارد.");
            if (tools.Count > 500) return (null, "فهرست ابزار بیش از ۵۰۰ ابزار دارد.");
            return (tools, null);
        }

        /// <summary>The result file of the measuring cycles: one "CODE=VALUE" (or "CODE;VALUE") a line; other lines are ignored.</summary>
        public static List<(string Code, decimal Value)> ParseResults(string text)
        {
            var list = new List<(string, decimal)>();
            foreach (string line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                Match m = Regex.Match(line.Trim(), @"^(?<c>[A-Za-z][A-Za-z0-9_\-]{0,49})\s*[=;:]\s*(?<v>[+\-]?\d+(\.\d+)?)$", RegexOptions.CultureInvariant);
                if (m.Success) list.Add((m.Groups["c"].Value.ToUpperInvariant(), decimal.Parse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture)));
            }
            return list;
        }

        public sealed class Sample
        {
            public DateTime At { get; init; }
            public MfExecMode? Exec { get; init; }
            public MfProgramState? State { get; init; }
            public int? Feed { get; init; }
        }

        /// <summary>
        /// Was the first run of a revision proven? While the program ran, the control was at least once in single block, or ran with the feed override at or below
        /// <see cref="ProveOutFeedMax"/> percent. The samples are the values at the start of the booking and every change after it, in time order.
        /// </summary>
        public static bool ProveOutObserved(IEnumerable<Sample> samples)
        {
            MfExecMode exec = MfExecMode.Unknown;
            MfProgramState state = MfProgramState.Unknown;
            int? feed = null;
            foreach (Sample s in samples.OrderBy(x => x.At))
            {
                if (s.Exec.HasValue) exec = s.Exec.Value;
                if (s.State.HasValue) state = s.State.Value;
                if (s.Feed.HasValue) feed = s.Feed;
                bool running = state == MfProgramState.Running;
                if (exec == MfExecMode.SingleBlock && (running || state == MfProgramState.Stopped)) return true;
                if (running && feed.HasValue && feed.Value <= ProveOutFeedMax) return true;
            }
            return false;
        }
    }

    // ============================================================================================ LSV2 session

    /// <summary>The production session: LSV2 over TCP, with the PLC map for what an older control does not answer itself.</summary>
    public sealed class Lsv2ControlSession : IMfControlSession
    {
        private readonly Lsv2Client _client;
        private readonly Dictionary<string, (char Type, int Number)> _map;
        private readonly string _password;

        private Lsv2ControlSession(Lsv2Client client, Dictionary<string, (char, int)> map, string password)
        {
            _client = client; _map = map; _password = password;
        }

        public static async Task<Lsv2ControlSession> OpenAsync(string host, int port, string plcMap, string password, CancellationToken ct)
        {
            var (map, error) = MfGuardRules.ParsePlcMap(plcMap);
            if (error != null) throw new Lsv2Exception(error);
            var client = new Lsv2Client();
            try
            {
                await client.ConnectAsync(host, port, ct);
            }
            catch (Exception ex) when (ex is not Lsv2Exception)
            {
                client.Dispose();
                throw new Lsv2Exception(NcLinkText.Network(ex));
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return new Lsv2ControlSession(client, map, password);
        }

        public string Identity => _client.Version == null ? null : $"{_client.Version.Control} / NC {_client.Version.NcSoftware}".Trim();

        private async Task<short?> WordAsync(string key, CancellationToken ct)
        {
            if (!_map.TryGetValue(key, out var a) || a.Type != 'W') return null;
            var w = await _client.ReadWordsAsync(a.Number, 1, _password, ct);
            return w is { Count: 1 } ? w[0] : null;
        }

        public async Task<ControlSnapshot> ReadAsync(CancellationToken ct)
        {
            var snap = new ControlSnapshot();
            Lsv2ExecState? exec = await _client.ExecStateAsync(ct);
            if (exec.HasValue) snap.ExecMode = MfGuardRules.Exec(exec.Value);
            else if (await WordAsync("exec", ct) is short we && Enum.IsDefined(typeof(Lsv2ExecState), (int)we)) snap.ExecMode = MfGuardRules.Exec((Lsv2ExecState)we);
            Lsv2PgmState? pgm = await _client.ProgramStateAsync(ct);
            if (pgm.HasValue) snap.ProgramState = MfGuardRules.Program(pgm.Value);
            else if (_map.TryGetValue("state", out var st))
            {
                if (st.Type == 'W' && await WordAsync("state", ct) is short ws && Enum.IsDefined(typeof(Lsv2PgmState), (int)ws)) snap.ProgramState = MfGuardRules.Program((Lsv2PgmState)ws);
                else if (st.Type == 'M' && await _client.ReadMarkersAsync(st.Number, 1, _password, ct) is { Count: 1 } m) snap.ProgramState = m[0] ? MfProgramState.Running : MfProgramState.Idle;
            }
            Lsv2Overrides ov = await _client.OverridesAsync(ct);
            if (ov != null) { snap.FeedOverride = (int)Math.Round(ov.Feed); snap.SpindleOverride = (int)Math.Round(ov.Spindle); }
            else
            {
                if (await WordAsync("feed", ct) is short wf) snap.FeedOverride = wf;
                if (await WordAsync("spindle", ct) is short wsp) snap.SpindleOverride = wsp;
            }
            Lsv2SelectedProgram sel = await _client.SelectedProgramAsync(ct);
            if (sel != null) { snap.SelectedProgram = sel.Main; snap.BlockNumber = sel.Line; }
            else if (await WordAsync("block", ct) is short wb) snap.BlockNumber = wb;
            Lsv2Tool tool = await _client.CurrentToolAsync(ct);
            if (tool != null) snap.ToolNumber = tool.Number;
            else if (await WordAsync("tool", ct) is short wt) snap.ToolNumber = wt;
            return snap;
        }

        public Task<byte[]> ReadFileAsync(string path, long maxBytes, CancellationToken ct) => _client.ReadFileAsync(path, maxBytes, ct);

        public async Task<DateTimeOffset?> FileTimeAsync(string path, CancellationToken ct) => (await _client.FileInfoAsync(path, ct))?.Time;

        public async Task<List<string>> CapabilitiesAsync(string programFolder, CancellationToken ct)
        {
            var list = new List<string> { "کنترلر: " + (Identity ?? "نامعلوم") };
            ControlSnapshot s = await ReadAsync(ct);
            void Add(string what, bool ok) => list.Add((ok ? "✔ " : "✘ ") + what);
            Add("حالت کار (دستی، تک‌بلوک، خودکار)" + (s.ExecMode.HasValue ? ": " + MfGuardRules.Text(s.ExecMode.Value) : string.Empty), s.ExecMode.HasValue);
            Add("وضعیت برنامه (اجرا، توقف، خطا)" + (s.ProgramState.HasValue ? ": " + MfGuardRules.Text(s.ProgramState.Value) : string.Empty), s.ProgramState.HasValue);
            Add("درصد override پیشروی" + (s.FeedOverride.HasValue ? ": " + s.FeedOverride : string.Empty), s.FeedOverride.HasValue);
            Add("برنامه انتخاب‌شده" + (s.SelectedProgram != null ? ": " + s.SelectedProgram : string.Empty), s.SelectedProgram != null);
            Add("ابزار جاری" + (s.ToolNumber.HasValue ? ": " + s.ToolNumber : string.Empty), s.ToolNumber.HasValue);
            bool table = await _client.FileInfoAsync("TNC:\\TOOL.T", ct) != null;
            Add("جدول ابزار (TOOL.T) برای گیت ابزار", table);
            Add("خواندن فایل برنامه از کنترلر برای گیت هویت برنامه", await _client.LoginAsync("FILE", null, ct));
            if (_client.LastError != null) list.Add("آخرین پاسخ خطای کنترلر: " + _client.LastError);
            return list;
        }

        public void Dispose() => _client.Dispose();
    }

    public sealed class MfControlLinkFactory : IMfControlLinkFactory
    {
        public async Task<IMfControlSession> OpenAsync(MfMachineMonitor monitor, string plcPassword, CancellationToken ct)
        {
            if (monitor == null || monitor.DataLink != MfDataLinkKind.Lsv2Tcp) throw new Lsv2Exception("برای این ماشین اتصال داده LSV2 تعریف نشده است.");
            if (string.IsNullOrWhiteSpace(monitor.DataHost)) throw new Lsv2Exception("نشانی کنترلر تعریف نشده است.");
            return await Lsv2ControlSession.OpenAsync(monitor.DataHost, monitor.DataPort ?? Lsv2Client.DefaultPort, monitor.PlcMap, plcPassword, ct);
        }
    }

    /// <summary>Modbus function 05 (write single coil), one request per connection, no outside package.</summary>
    public sealed class ModbusCoilWriter : IMfCoilWriter
    {
        private static int _transaction;

        public async Task<string> WriteAsync(string host, int port, byte unit, int coil, bool on, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(host)) return "نشانی ماژول قفل استارت تعریف نشده است.";
            if (coil < 0 || coil > 65535) return "شماره خروجی رله نامعتبر است.";
            try
            {
                using var client = new TcpClient { NoDelay = true };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(host, port <= 0 ? 502 : port, cts.Token);
                NetworkStream s = client.GetStream();
                ushort id = (ushort)Interlocked.Increment(ref _transaction);
                byte[] request = { (byte)(id >> 8), (byte)id, 0, 0, 0, 6, unit, 5, (byte)(coil >> 8), (byte)coil, (byte)(on ? 0xFF : 0x00), 0x00 };
                await s.WriteAsync(request, cts.Token);
                var answer = new byte[12];
                int read = 0;
                while (read < 9)
                {
                    int r = await s.ReadAsync(answer.AsMemory(read, 12 - read), cts.Token);
                    if (r == 0) break;
                    read += r;
                    if (read >= 9 && (answer[7] & 0x80) != 0) break;
                    if (read == 12) break;
                }
                if (read < 9) return "ماژول قفل استارت پاسخ کامل نداد.";
                if ((answer[7] & 0x80) != 0) return "ماژول قفل استارت خطای Modbus برگرداند (کد " + answer[8] + ").";
                if (answer[7] != 5 || read < 12 || answer[10] != request[10]) return "ماژول قفل استارت فرمان را تأیید نکرد.";
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkText.Network(ex);
            }
        }
    }
}
