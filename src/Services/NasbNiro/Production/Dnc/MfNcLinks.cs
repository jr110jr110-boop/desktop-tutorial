using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>Where and how to reach a control. The secret comes from the server settings, never from the database.</summary>
    public sealed class NcLinkTarget
    {
        public string Host { get; init; }
        public int? Port { get; init; }
        public string RemotePath { get; init; }
        public string User { get; init; }
        public string Password { get; init; }
        public int? BaudRate { get; init; }
        public MfNcControllerFamily Family { get; init; }

        /// <summary>Seconds a serial receive waits for the operator to start the output on the control.</summary>
        public int ReceiveWaitSeconds { get; init; } = 120;

        /// <summary>Seconds of silence that end a serial receive once data has come.</summary>
        public int ReceiveIdleSeconds { get; init; } = 8;

        public int TimeoutSeconds { get; init; } = 20;

        /// <summary>
        /// The folders of the server settings (Dnc:FolderRoots) a network-folder link may use; null = not checked (the link used on its own, as in the unit tests). The service always passes them, so a link
        /// never reads or writes outside the machine folders the administrator allowed (never the application folder, whatever a link editor typed).
        /// </summary>
        public IReadOnlyList<string> FolderRoots { get; init; }

        /// <summary>Largest file a receive reads (bytes); 0 = the global limit.</summary>
        public long MaxBytes { get; init; }
    }

    /// <summary>The address and path rules of the machine links (review findings 1 and 9): LAN addresses only, no control characters, folders only under the allowed roots.</summary>
    public static class NcLan
    {
        /// <summary>True when the text holds a control character (CR or LF in an FTP folder would become extra FTP commands).</summary>
        public static bool HasControl(string text) => text != null && text.Any(char.IsControl);

        /// <summary>A literal IPv4 address of the LAN (10/8, 172.16/12, 192.168/16, 169.254/16); the loopback only when the server settings allow it (Dnc:AllowLoopback, for the tests).</summary>
        public static bool IsLanHost(string host, bool allowLoopback)
        {
            if (string.IsNullOrWhiteSpace(host) || !System.Net.IPAddress.TryParse(host.Trim(), out System.Net.IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            if (host.Trim().Count(ch => ch == '.') != 3) return false;   // "10.1" is parsed as an address too; only the full dotted form is taken
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 127) return allowLoopback;
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        /// <summary>The full form of a folder path with one trailing separator, or null when it is not an absolute path.</summary>
        public static string FullFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || HasControl(path)) return null;
            try
            {
                if (!Path.IsPathRooted(path)) return null;
                string full = Path.GetFullPath(path.Trim());
                return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
            }
            catch (Exception) { return null; }
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>True when the folder is one of the roots or inside one of them.</summary>
        public static bool UnderRoot(string folder, IEnumerable<string> roots)
        {
            string full = FullFolder(folder);
            if (full == null || roots == null) return false;
            foreach (string root in roots)
            {
                string r = FullFolder(root);
                if (r != null && full.StartsWith(r, PathComparison)) return true;
            }
            return false;
        }

        /// <summary>True when the folder is the application folder, inside it or holds it (a link must never touch the server's own files).</summary>
        public static bool TouchesApplication(string folder)
        {
            string full = FullFolder(folder), app = FullFolder(AppContext.BaseDirectory);
            if (full == null || app == null) return true;
            return full.StartsWith(app, PathComparison) || app.StartsWith(full, PathComparison);
        }

        /// <summary>The full path of the file in the folder, or null when the name would leave the folder.</summary>
        public static string FileIn(string folder, string name)
        {
            string full = FullFolder(folder);
            if (full == null || string.IsNullOrEmpty(name) || HasControl(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == "..") return null;
            string path = Path.GetFullPath(Path.Combine(full, name));
            return string.Equals(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar, full, PathComparison) ? path : null;
        }
    }

    public sealed class NcLinkResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; }
        public byte[] Bytes { get; init; }

        public static NcLinkResult Success(string message = null, byte[] bytes = null) => new NcLinkResult { Ok = true, Message = message, Bytes = bytes };
        public static NcLinkResult Fail(string message) => new NcLinkResult { Ok = false, Message = message };
    }

    /// <summary>A way to put a program on a control and to fetch one from it. Implementations never throw for a network failure: the Persian reason is in the result.</summary>
    public interface IMfNcLink
    {
        MfNcLinkKind Kind { get; }
        Task<NcLinkResult> SendAsync(NcLinkTarget target, string name, byte[] bytes, CancellationToken ct);
        Task<NcLinkResult> ReceiveAsync(NcLinkTarget target, string name, CancellationToken ct);
        Task<NcLinkResult> TestAsync(NcLinkTarget target, CancellationToken ct);
    }

    public interface IMfNcLinkFactory
    {
        /// <summary>Null for the link kinds the server does not drive (manual, edge gateway).</summary>
        IMfNcLink For(MfNcLinkKind kind);
    }

    public class MfNcLinkFactory : IMfNcLinkFactory
    {
        public virtual IMfNcLink For(MfNcLinkKind kind) => kind switch
        {
            MfNcLinkKind.NetworkFolder => new NetworkFolderLink(),
            MfNcLinkKind.Ftp => new FtpLink(),
            MfNcLinkKind.SerialGateway => new SerialGatewayLink(),
            _ => null,
        };
    }

    internal static class NcLinkText
    {
        public static string Network(Exception ex) => ex switch
        {
            SocketException se => "اتصال برقرار نشد (" + se.SocketErrorCode + "). نشانی، درگاه و روشن‌بودن دستگاه را بررسی کنید.",
            TimeoutException => "پاسخی در زمان مقرر نیامد.",
            OperationCanceledException => "پاسخی در زمان مقرر نیامد.",
            IOException io => "خطای ارتباط: " + io.Message,
            UnauthorizedAccessException => "سرور اجازه نوشتن یا خواندن در این مسیر را ندارد.",
            _ => "خطای ارتباط: " + ex.Message,
        };
    }

    // ------------------------------------------------------------------------------------------ network folder

    /// <summary>The control (or the PC at the machine) reads a shared folder. The file is written under a temporary name and renamed, so the control never sees half a program.</summary>
    public sealed class NetworkFolderLink : IMfNcLink
    {
        public MfNcLinkKind Kind => MfNcLinkKind.NetworkFolder;

        private static string Folder(NcLinkTarget t) => string.IsNullOrWhiteSpace(t?.RemotePath) ? null : t.RemotePath.Trim();

        /// <summary>Null, or why this folder may not be used: outside the allowed roots of the server settings, or the application's own folder.</summary>
        private static string Refused(NcLinkTarget t, string folder)
        {
            if (NcLan.FullFolder(folder) == null) return "مسیر پوشه ماشین باید مسیر کامل باشد.";
            if (t.FolderRoots == null) return null;
            if (NcLan.TouchesApplication(folder) || !NcLan.UnderRoot(folder, t.FolderRoots)) return "پوشه ماشین در فهرست پوشه‌های مجاز تنظیمات سرور (Dnc:FolderRoots) نیست.";
            return null;
        }

        public async Task<NcLinkResult> SendAsync(NcLinkTarget target, string name, byte[] bytes, CancellationToken ct)
        {
            string folder = Folder(target);
            if (folder == null) return NcLinkResult.Fail("مسیر پوشه اشتراکی ماشین تعریف نشده است.");
            string refused = Refused(target, folder);
            if (refused != null) return NcLinkResult.Fail(refused);
            string path = NcLan.FileIn(folder, name);
            if (path == null) return NcLinkResult.Fail("نام فایل برنامه برای پوشه ماشین مجاز نیست.");
            try
            {
                if (!Directory.Exists(folder)) return NcLinkResult.Fail("پوشه اشتراکی ماشین در دسترس نیست: " + folder);
                string temp = path + ".pmis-tmp";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, true);
                return NcLinkResult.Success("فایل در پوشه ماشین نوشته شد.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        public async Task<NcLinkResult> ReceiveAsync(NcLinkTarget target, string name, CancellationToken ct)
        {
            string folder = Folder(target);
            if (folder == null) return NcLinkResult.Fail("مسیر پوشه اشتراکی ماشین تعریف نشده است.");
            string refused = Refused(target, folder);
            if (refused != null) return NcLinkResult.Fail(refused);
            string path = NcLan.FileIn(folder, name);
            if (path == null) return NcLinkResult.Fail("نام فایل برنامه برای پوشه ماشین مجاز نیست.");
            try
            {
                if (!File.Exists(path)) return NcLinkResult.Fail("فایل " + name + " در پوشه ماشین پیدا نشد.");
                long max = target.MaxBytes > 0 ? target.MaxBytes : MfNcRules.GlobalMaxBytes;
                if (new FileInfo(path).Length > max) return NcLinkResult.Fail("فایل " + name + " از ظرفیت مجاز برنامه بزرگ‌تر است و خوانده نمی‌شود.");
                return NcLinkResult.Success(null, await File.ReadAllBytesAsync(path, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        public Task<NcLinkResult> TestAsync(NcLinkTarget target, CancellationToken ct)
        {
            string folder = Folder(target);
            if (folder == null) return Task.FromResult(NcLinkResult.Fail("مسیر پوشه اشتراکی ماشین تعریف نشده است."));
            string refused = Refused(target, folder);
            if (refused != null) return Task.FromResult(NcLinkResult.Fail(refused));
            try
            {
                return Task.FromResult(Directory.Exists(folder) ? NcLinkResult.Success("پوشه در دسترس است.") : NcLinkResult.Fail("پوشه اشتراکی ماشین در دسترس نیست: " + folder));
            }
            catch (Exception ex)
            {
                return Task.FromResult(NcLinkResult.Fail(NcLinkText.Network(ex)));
            }
        }
    }

    // ------------------------------------------------------------------------------------------ FTP (passive, binary)

    /// <summary>A small passive-mode FTP client (USER, PASS, TYPE I, CWD, PASV, STOR, RETR, QUIT): enough for the FTP server of a control, without any outside package.</summary>
    public sealed class FtpLink : IMfNcLink
    {
        public MfNcLinkKind Kind => MfNcLinkKind.Ftp;

        private static readonly Regex Pasv = new Regex(@"(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)", RegexOptions.CultureInvariant);

        private sealed class Session : IDisposable
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly StreamReader _reader;
            private readonly int _timeoutMs;
            public string Host { get; }

            private Session(TcpClient client, string host, int timeoutMs)
            {
                _client = client;
                Host = host;
                _timeoutMs = timeoutMs;
                _stream = client.GetStream();
                _stream.ReadTimeout = timeoutMs;
                _stream.WriteTimeout = timeoutMs;
                _reader = new StreamReader(_stream, Encoding.ASCII, false, 1024, true);
            }

            public static async Task<Session> OpenAsync(NcLinkTarget t, CancellationToken ct)
            {
                var client = new TcpClient();
                int timeout = Math.Max(3, t.TimeoutSeconds) * 1000;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(timeout);
                    await client.ConnectAsync(t.Host, t.Port ?? 21, cts.Token);
                }
                return new Session(client, t.Host, timeout);
            }

            /// <summary>One reply (multi-line replies "123-" … "123 " are read to their end).</summary>
            public async Task<(int Code, string Text)> ReadAsync(CancellationToken ct)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeoutMs);
                string line = await _reader.ReadLineAsync(cts.Token) ?? throw new IOException("ارتباط FTP قطع شد.");
                if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out int code)) throw new IOException("پاسخ نامعتبر FTP: " + line);
                if (line.Length > 3 && line[3] == '-')
                {
                    string end = line.Substring(0, 3) + " ";
                    string next;
                    do { next = await _reader.ReadLineAsync(cts.Token) ?? throw new IOException("ارتباط FTP قطع شد."); }
                    while (!next.StartsWith(end, StringComparison.Ordinal));
                }
                return (code, line);
            }

            public async Task<(int Code, string Text)> CommandAsync(string command, CancellationToken ct)
            {
                byte[] data = Encoding.ASCII.GetBytes(command + "\r\n");
                await _stream.WriteAsync(data, ct);
                return await ReadAsync(ct);
            }

            public async Task<TcpClient> PassiveAsync(CancellationToken ct)
            {
                var (code, text) = await CommandAsync("PASV", ct);
                if (code != 227) throw new IOException("سرور FTP حالت غیرفعال (PASV) را نپذیرفت: " + text);
                Match m = Pasv.Match(text);
                if (!m.Success) throw new IOException("پاسخ PASV نامعتبر است: " + text);
                int port = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * 256 + int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
                // the address of the reply is ignored (a control behind NAT answers with its inner address): the data channel goes to the same host
                var data = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeoutMs);
                await data.ConnectAsync(Host, port, cts.Token);
                return data;
            }

            public void Dispose()
            {
                _reader.Dispose();
                _stream.Dispose();
                _client.Dispose();
            }
        }

        private static async Task<Session> LoginAsync(NcLinkTarget t, CancellationToken ct)
        {
            Session s = await Session.OpenAsync(t, ct);
            try
            {
                var (code, text) = await s.ReadAsync(ct);
                if (code != 220) throw new IOException("سرور FTP آماده نیست: " + text);
                (code, text) = await s.CommandAsync("USER " + (string.IsNullOrEmpty(t.User) ? "anonymous" : t.User), ct);
                if (code == 331) (code, text) = await s.CommandAsync("PASS " + (t.Password ?? ""), ct);
                if (code != 230) throw new IOException("ورود به سرور FTP پذیرفته نشد (نام کاربری یا رمز را در تنظیمات سرور بررسی کنید).");
                (code, text) = await s.CommandAsync("TYPE I", ct);
                if (code != 200) throw new IOException("حالت دودویی FTP پذیرفته نشد: " + text);
                if (NcLan.HasControl(t.RemotePath) || NcLan.HasControl(t.User) || NcLan.HasControl(t.Password)) throw new IOException("مسیر یا اعتبارنامه FTP نویسه کنترلی دارد و پذیرفته نمی‌شود.");
                if (!string.IsNullOrWhiteSpace(t.RemotePath))
                {
                    (code, text) = await s.CommandAsync("CWD " + t.RemotePath.Trim(), ct);
                    if (code != 250) throw new IOException("پوشه " + t.RemotePath + " روی کنترلر پیدا نشد.");
                }
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        private static async Task QuitAsync(Session s, CancellationToken ct)
        {
            try { await s.CommandAsync("QUIT", ct); } catch (Exception) { /* the session is closed anyway */ }
        }

        public async Task<NcLinkResult> SendAsync(NcLinkTarget target, string name, byte[] bytes, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی FTP کنترلر تعریف نشده است.");
            try
            {
                using Session s = await LoginAsync(target, ct);
                using (TcpClient data = await s.PassiveAsync(ct))
                {
                    var (code, text) = await s.CommandAsync("STOR " + name, ct);
                    if (code != 150 && code != 125) return NcLinkResult.Fail("کنترلر فایل را نپذیرفت: " + text);
                    using (NetworkStream ds = data.GetStream()) await ds.WriteAsync(bytes, ct);
                }
                var (end, endText) = await s.ReadAsync(ct);
                await QuitAsync(s, ct);
                return end == 226 || end == 250 ? NcLinkResult.Success("فایل روی کنترلر ذخیره شد.") : NcLinkResult.Fail("ارسال کامل نشد: " + endText);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        public async Task<NcLinkResult> ReceiveAsync(NcLinkTarget target, string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی FTP کنترلر تعریف نشده است.");
            try
            {
                using Session s = await LoginAsync(target, ct);
                byte[] bytes;
                using (TcpClient data = await s.PassiveAsync(ct))
                {
                    var (code, text) = await s.CommandAsync("RETR " + name, ct);
                    if (code != 150 && code != 125) return NcLinkResult.Fail("فایل " + name + " روی کنترلر خوانده نشد: " + text);
                    using var ms = new MemoryStream();
                    using (NetworkStream ds = data.GetStream())
                    {
                        byte[] buffer = new byte[8192];
                        int read;
                        while ((read = await ds.ReadAsync(buffer, ct)) > 0)
                        {
                            ms.Write(buffer, 0, read);
                            if (ms.Length > MfNcRules.GlobalMaxBytes) return NcLinkResult.Fail("فایل کنترلر بزرگ‌تر از حد مجاز کتابخانه است.");
                        }
                    }
                    bytes = ms.ToArray();
                }
                var (end, endText) = await s.ReadAsync(ct);
                await QuitAsync(s, ct);
                return end == 226 || end == 250 ? NcLinkResult.Success(null, bytes) : NcLinkResult.Fail("دریافت کامل نشد: " + endText);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        public async Task<NcLinkResult> TestAsync(NcLinkTarget target, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی FTP کنترلر تعریف نشده است.");
            try
            {
                using Session s = await LoginAsync(target, ct);
                await QuitAsync(s, ct);
                return NcLinkResult.Success("ورود به سرور FTP کنترلر موفق بود.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }
    }

    // ------------------------------------------------------------------------------------------ serial-to-Ethernet converter

    /// <summary>
    /// RS-232 / RS-422 of an old control behind a serial-to-Ethernet converter in TCP server mode. Sending streams the program paced to the baud rate (10 bits a character) and stops while the control sends
    /// XOFF (DC3) until XON (DC1). The operator first puts the control in "read" (input) mode. Receiving waits for the operator to start the output ("punch") on the control and ends after a pause in the data,
    /// after the closing "%" of an ISO program or after "END PGM" of a Heidenhain program.
    /// </summary>
    public sealed class SerialGatewayLink : IMfNcLink
    {
        public MfNcLinkKind Kind => MfNcLinkKind.SerialGateway;

        private const byte Xon = 0x11, Xoff = 0x13;
        private const int Chunk = 32;

        private static async Task<TcpClient> ConnectAsync(NcLinkTarget t, CancellationToken ct)
        {
            var client = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(3, t.TimeoutSeconds) * 1000);
            await client.ConnectAsync(t.Host, t.Port ?? 4001, cts.Token);
            return client;
        }

        public async Task<NcLinkResult> SendAsync(NcLinkTarget target, string name, byte[] bytes, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی مبدل سریال ماشین تعریف نشده است.");
            int baud = target.BaudRate is > 0 ? target.BaudRate.Value : 9600;
            double charsPerMs = baud / 10.0 / 1000.0;
            try
            {
                using TcpClient client = await ConnectAsync(target, ct);
                NetworkStream s = client.GetStream();
                bool paused = false;
                DateTime pausedSince = DateTime.UtcNow;
                byte[] control = new byte[64];
                int sent = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (sent < bytes.Length)
                {
                    while (s.DataAvailable)
                    {
                        int r = await s.ReadAsync(control, ct);
                        for (int i = 0; i < r; i++)
                        {
                            if (control[i] == Xoff) { paused = true; pausedSince = DateTime.UtcNow; }
                            else if (control[i] == Xon) paused = false;
                        }
                    }
                    if (paused)
                    {
                        if ((DateTime.UtcNow - pausedSince).TotalSeconds > Math.Max(30, target.TimeoutSeconds * 3)) return NcLinkResult.Fail("کنترلر ارسال را متوقف کرد (XOFF) و ادامه نداد. حافظه یا حالت دریافت کنترلر را بررسی کنید.");
                        await Task.Delay(20, ct);
                        clock.Restart();
                        continue;
                    }
                    int n = Math.Min(Chunk, bytes.Length - sent);
                    await s.WriteAsync(bytes.AsMemory(sent, n), ct);
                    sent += n;
                    // pace to the line speed so the converter's buffer never overflows
                    double due = n / charsPerMs;
                    double elapsed = clock.Elapsed.TotalMilliseconds;
                    if (due > elapsed) await Task.Delay(TimeSpan.FromMilliseconds(due - elapsed), ct);
                    clock.Restart();
                }
                await s.FlushAsync(ct);
                return NcLinkResult.Success("برنامه از راه درگاه سریال ارسال شد (" + bytes.Length.ToString(CultureInfo.InvariantCulture) + " بایت).");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        /// <summary>True when the data received so far is a complete program (an ISO program between two "%" or a Heidenhain program up to END PGM).</summary>
        internal static bool Complete(List<byte> data, MfNcControllerFamily family)
        {
            if (data.Count < 3) return false;
            string text = Encoding.Latin1.GetString(data.ToArray());
            if (family == MfNcControllerFamily.HeidenhainConversational)
                return Regex.IsMatch(text, @"END\s+PGM\s+\S+[^\r\n]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            int first = text.IndexOf('%');
            if (first < 0) return false;
            int second = text.IndexOf('%', first + 1);
            return second > first && text.Substring(first, second - first).Contains('\n');
        }

        public async Task<NcLinkResult> ReceiveAsync(NcLinkTarget target, string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی مبدل سریال ماشین تعریف نشده است.");
            try
            {
                using TcpClient client = await ConnectAsync(target, ct);
                NetworkStream s = client.GetStream();
                var data = new List<byte>();
                byte[] buffer = new byte[4096];
                DateTime started = DateTime.UtcNow, last = DateTime.UtcNow;
                while (true)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(500);
                    int read = 0;
                    try { read = await s.ReadAsync(buffer, cts.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { read = -1; }
                    if (read == 0) break;   // the converter closed the connection
                    if (read > 0)
                    {
                        for (int i = 0; i < read; i++) if (buffer[i] != Xon && buffer[i] != Xoff && buffer[i] != 0) data.Add(buffer[i]);
                        last = DateTime.UtcNow;
                        if (data.Count > MfNcRules.GlobalMaxBytes) return NcLinkResult.Fail("داده دریافتی بزرگ‌تر از حد مجاز کتابخانه است.");
                        if (Complete(data, target.Family)) break;
                        continue;
                    }
                    if (data.Count == 0)
                    {
                        if ((DateTime.UtcNow - started).TotalSeconds > target.ReceiveWaitSeconds) return NcLinkResult.Fail("داده‌ای از کنترلر نیامد. خروجی برنامه (Punch/Output) را روی کنترلر شروع کنید و دوباره درخواست دهید.");
                    }
                    else if ((DateTime.UtcNow - last).TotalSeconds > target.ReceiveIdleSeconds) break;
                }
                if (data.Count == 0) return NcLinkResult.Fail("داده‌ای از کنترلر نیامد.");
                return NcLinkResult.Success(null, data.ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }

        public async Task<NcLinkResult> TestAsync(NcLinkTarget target, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target?.Host)) return NcLinkResult.Fail("نشانی مبدل سریال ماشین تعریف نشده است.");
            try
            {
                using TcpClient client = await ConnectAsync(target, ct);
                return NcLinkResult.Success("اتصال به مبدل سریال برقرار شد (روشن‌بودن و حالت دریافت کنترلر را جداگانه بررسی کنید).");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return NcLinkResult.Fail(NcLinkText.Network(ex));
            }
        }
    }
}
