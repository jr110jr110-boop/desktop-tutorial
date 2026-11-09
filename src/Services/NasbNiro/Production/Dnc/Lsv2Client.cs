// LSV2 is the data protocol of HEIDENHAIN TNC controls (the protocol of TNCremo). The telegram layout, the command and response codes and the payload formats used here follow the
// open-source pyLSV2 project (MIT License, Copyright (c) 2020 - 2026 drunsinn, https://github.com/drunsinn/pyLSV2), re-implemented in C# for PMIS.
// Only reading is implemented: PMIS never writes to the control through this link (programs go through the DNC transfer links).
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>Execution mode of the control (LSV2 R_RI 23).</summary>
    public enum Lsv2ExecState { Manual = 0, Mdi = 1, PassReferences = 2, SingleStep = 3, Automatic = 4, Undefined = 5 }

    /// <summary>State of the active program (LSV2 R_RI 26).</summary>
    public enum Lsv2PgmState { Started = 0, Stopped = 1, Finished = 2, Cancelled = 3, Interrupted = 4, Error = 5, ErrorCleared = 6, Idle = 7, Undefined = 8 }

    public sealed class Lsv2Exception : Exception
    {
        public Lsv2Exception(string message) : base(message) { }
    }

    /// <summary>What the control answered about itself.</summary>
    public sealed class Lsv2Version
    {
        public string Control { get; init; }
        public string NcSoftware { get; init; }
        public string Plc { get; init; }
        public string Options { get; init; }
    }

    public sealed class Lsv2Overrides
    {
        public decimal Feed { get; init; }
        public decimal Spindle { get; init; }
        public decimal Rapid { get; init; }
    }

    public sealed class Lsv2SelectedProgram
    {
        public int Line { get; init; }
        public string Main { get; init; }
        public string Current { get; init; }
    }

    public sealed class Lsv2Tool
    {
        public int Number { get; init; }
        public decimal? Length { get; init; }
        public decimal? Radius { get; init; }
    }

    /// <summary>The PLC memory areas the control reports (R_PR), needed to read markers and words by number.</summary>
    public sealed class Lsv2SystemParameters
    {
        public uint MarkersStart { get; init; }
        public uint Markers { get; init; }
        public uint WordsStart { get; init; }
        public uint Words { get; init; }
        public uint MaxBlockLength { get; init; }
    }

    /// <summary>One LSV2 conversation over TCP (port 19000 by default). Not thread-safe: one link per poll.</summary>
    public sealed class Lsv2Client : IDisposable
    {
        public const int DefaultPort = 19000;

        // commands and responses (4 ASCII characters)
        public const string Login = "A_LG", Logout = "A_LO", SystemCommand = "C_CC", ReadVersion = "R_VR", ReadParameters = "R_PR", ReadRunInfo = "R_RI", ReadFile = "R_FL", ReadFileInfo = "R_FI",
            ReadMemory = "R_MB";
        public const string Ok = "T_OK", Error = "T_ER", FileDone = "T_FD", BadData = "T_BD", VersionAnswer = "S_VR", ParametersAnswer = "S_PR", RunInfoAnswer = "S_RI", FileBlock = "S_FL",
            FileInfoAnswer = "S_FI", MemoryAnswer = "S_MB";

        // run information numbers (R_RI)
        public const ushort ExecStateInfo = 23, SelectedProgramInfo = 24, OverrideInfo = 25, ProgramStateInfo = 26, CurrentToolInfo = 51;

        private readonly TcpClient _tcp;
        private NetworkStream _stream;
        private readonly int _timeoutMs;
        private readonly HashSet<string> _logins = new HashSet<string>();
        private int _buffer = 256;
        private Lsv2SystemParameters _parameters;

        public Lsv2Client(int timeoutSeconds = 10)
        {
            _timeoutMs = Math.Clamp(timeoutSeconds, 1, 60) * 1000;
            _tcp = new TcpClient { ReceiveTimeout = _timeoutMs, SendTimeout = _timeoutMs };
        }

        public Lsv2Version Version { get; private set; }

        /// <summary>The last error code the control answered (T_ER: type and code), for the capability report.</summary>
        public string LastError { get; private set; }

        public async Task ConnectAsync(string host, int port, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeoutMs);
            await _tcp.ConnectAsync(host, port > 0 ? port : DefaultPort, cts.Token);
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = _timeoutMs;
            _stream.WriteTimeout = _timeoutMs;
            if (!await LoginAsync("INSPECT", null, ct)) throw new Lsv2Exception("کنترلر ورود INSPECT را نپذیرفت.");
            Version = await ReadVersionAsync(ct);
            _parameters = await ReadParametersAsync(ct);
            // a bigger block when the control allows it (fewer telegrams for a program)
            (int size, ushort command) = (_parameters?.MaxBlockLength ?? 0) switch
            {
                >= 4096 => (4096, (ushort)7),
                >= 2048 => (2048, (ushort)5),
                >= 1024 => (1024, (ushort)3),
                >= 512 => (512, (ushort)4),
                _ => (256, (ushort)0),
            };
            if (command != 0 && await CommandOkAsync(SystemCommand, BigEndian16(command), ct)) _buffer = size;
        }

        // ------------------------------------------------------------------------------------------ telegrams

        /// <summary>Sends one telegram (4-byte big-endian payload length, 4 command characters, payload) and reads the answer when one is expected.</summary>
        public async Task<(string Response, byte[] Content)> TelegramAsync(string command, byte[] payload, bool waitForAnswer, CancellationToken ct)
        {
            if (_stream == null) throw new Lsv2Exception("اتصال به کنترلر باز نیست.");
            payload ??= Array.Empty<byte>();
            var telegram = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(telegram, (uint)payload.Length);
            Encoding.ASCII.GetBytes(command, 0, 4, telegram, 4);
            payload.CopyTo(telegram, 8);
            if (telegram.Length >= _buffer && command != ReadFile && command != Ok) throw new Lsv2Exception("درخواست از ظرفیت بافر کنترلر بزرگ‌تر است.");
            await _stream.WriteAsync(telegram, ct);
            if (!waitForAnswer) return (null, Array.Empty<byte>());
            byte[] head = await ReadExactAsync(8, ct);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (length > 1_000_000) throw new Lsv2Exception("پاسخ کنترلر نامعتبر است.");
            string response = Encoding.ASCII.GetString(head, 4, 4);
            byte[] content = length == 0 ? Array.Empty<byte>() : await ReadExactAsync((int)length, ct);
            if (response == Error || response == BadData)
                LastError = content.Length >= 2 ? $"{response} {content[0]}/{content[1]}" : response;
            return (response, content);
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
        {
            var buffer = new byte[count];
            int read = 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeoutMs);
            while (read < count)
            {
                int n = await _stream.ReadAsync(buffer.AsMemory(read, count - read), cts.Token);
                if (n == 0) throw new Lsv2Exception("کنترلر اتصال را بست.");
                read += n;
            }
            return buffer;
        }

        private async Task<bool> CommandOkAsync(string command, byte[] payload, CancellationToken ct) => (await TelegramAsync(command, payload, true, ct)).Response == Ok;

        public static byte[] Text(string text) => Encoding.Latin1.GetBytes(text + "\0");

        private static byte[] BigEndian16(ushort value)
        {
            var b = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, value);
            return b;
        }

        private static string Latin(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes).TrimEnd('\0').TrimEnd();

        // ------------------------------------------------------------------------------------------ requests

        /// <summary>Asks for an access level (INSPECT, FILE, DNC, PLCDEBUG ...); a password where the control needs one (never stored in the database).</summary>
        public async Task<bool> LoginAsync(string login, string password, CancellationToken ct)
        {
            if (_logins.Contains(login)) return true;
            byte[] payload = Text(login);
            if (!string.IsNullOrEmpty(password)) payload = payload.Concat(Text(password)).ToArray();
            bool ok = await CommandOkAsync(Login, payload, ct);
            if (ok) _logins.Add(login);
            return ok;
        }

        public async Task<Lsv2Version> ReadVersionAsync(CancellationToken ct)
        {
            var (response, content) = await TelegramAsync(ReadVersion, null, true, ct);
            if (response != VersionAnswer) return null;
            string[] parts = Encoding.Latin1.GetString(content).TrimEnd('\0').Split('\0');
            return new Lsv2Version
            {
                Control = parts.Length > 0 ? parts[0].Trim() : null, NcSoftware = parts.Length > 1 ? parts[1].Trim() : null, Plc = parts.Length > 2 ? parts[2].Trim() : null,
                Options = parts.Length > 3 ? parts[^1].Trim() : null,
            };
        }

        public async Task<Lsv2SystemParameters> ReadParametersAsync(CancellationToken ct)
        {
            var (response, c) = await TelegramAsync(ReadParameters, null, true, ct);
            if (response != ParametersAnswer || c.Length < 120) return null;
            uint U(int index) => BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(index * 4, 4));
            // layout "!14L8B8L2BH4B2L2HL": markers 0/1, inputs 2/3, outputs 4/5, counters 6/7, timers 8/9, words 10/11, strings 12/13 (4 bytes each);
            // the maximum block length is the 2-byte value after 14L, 8B, 8L and 2B (byte 98)
            const int blockOffset = 14 * 4 + 8 + 8 * 4 + 2;
            return new Lsv2SystemParameters
            {
                MarkersStart = U(0), Markers = U(1), WordsStart = U(10), Words = U(11), MaxBlockLength = BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(blockOffset, 2)),
            };
        }

        private async Task<byte[]> RunInfoAsync(ushort info, CancellationToken ct)
        {
            var (response, content) = await TelegramAsync(ReadRunInfo, BigEndian16(info), true, ct);
            return response == RunInfoAnswer ? content : null;
        }

        /// <summary>Execution mode; null when the control does not answer it (an older control without the DNC login).</summary>
        public async Task<Lsv2ExecState?> ExecStateAsync(CancellationToken ct)
        {
            if (!await LoginAsync("DNC", null, ct)) return null;
            byte[] c = await RunInfoAsync(ExecStateInfo, ct);
            return c is { Length: >= 2 } ? (Lsv2ExecState)Math.Min((int)BinaryPrimitives.ReadUInt16BigEndian(c), (int)Lsv2ExecState.Undefined) : null;
        }

        public async Task<Lsv2PgmState?> ProgramStateAsync(CancellationToken ct)
        {
            if (!await LoginAsync("DNC", null, ct)) return null;
            byte[] c = await RunInfoAsync(ProgramStateInfo, ct);
            return c is { Length: >= 2 } ? (Lsv2PgmState)Math.Min((int)BinaryPrimitives.ReadUInt16BigEndian(c), (int)Lsv2PgmState.Undefined) : null;
        }

        public async Task<Lsv2Overrides> OverridesAsync(CancellationToken ct)
        {
            if (!await LoginAsync("DNC", null, ct)) return null;
            byte[] c = await RunInfoAsync(OverrideInfo, ct);
            if (c is not { Length: >= 12 }) return null;
            return new Lsv2Overrides
            {
                Feed = BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(0, 4)) / 100m, Spindle = BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(4, 4)) / 100m,
                Rapid = BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(8, 4)) / 100m,
            };
        }

        public async Task<Lsv2SelectedProgram> SelectedProgramAsync(CancellationToken ct)
        {
            if (!await LoginAsync("DNC", null, ct)) return null;
            byte[] c = await RunInfoAsync(SelectedProgramInfo, ct);
            if (c is not { Length: >= 5 }) return null;
            string[] names = Encoding.Latin1.GetString(c, 4, c.Length - 4).Split('\0');
            return new Lsv2SelectedProgram { Line = (int)BinaryPrimitives.ReadUInt32BigEndian(c), Main = names.Length > 0 ? names[0].Trim() : null, Current = names.Length > 1 ? names[1].Trim() : null };
        }

        public async Task<Lsv2Tool> CurrentToolAsync(CancellationToken ct)
        {
            if (!await LoginAsync("DNC", null, ct)) return null;
            byte[] c = await RunInfoAsync(CurrentToolInfo, ct);
            if (c is not { Length: >= 8 }) return null;
            return new Lsv2Tool
            {
                Number = (int)BinaryPrimitives.ReadUInt32BigEndian(c), Length = c.Length >= 16 ? (decimal)BinaryPrimitives.ReadDoubleLittleEndian(c.AsSpan(8, 8)) : null,
                Radius = c.Length >= 24 ? (decimal)BinaryPrimitives.ReadDoubleLittleEndian(c.AsSpan(16, 8)) : null,
            };
        }

        /// <summary>Size and date of a file on the control; null when it does not exist.</summary>
        public async Task<(uint Size, DateTimeOffset Time)?> FileInfoAsync(string path, CancellationToken ct)
        {
            if (!await LoginAsync("FILE", null, ct)) return null;
            var (response, c) = await TelegramAsync(ReadFileInfo, Text(path.Replace('/', '\\')), true, ct);
            if (response != FileInfoAnswer || c.Length < 8) return null;
            return (BinaryPrimitives.ReadUInt32BigEndian(c), DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(4, 4))));
        }

        /// <summary>
        /// Reads a file from the control. A text file comes in blocks whose lines end with a NUL byte; they are turned back into CR LF, as the control shows them. Null when the file is missing.
        /// </summary>
        public async Task<byte[]> ReadFileAsync(string path, long maxBytes, CancellationToken ct)
        {
            if (!await LoginAsync("FILE", null, ct)) throw new Lsv2Exception("کنترلر ورود FILE را نپذیرفت.");
            byte[] payload = Text(path.Replace('/', '\\')).Append((byte)0x00).ToArray();   // 0x00: text transfer
            var (response, content) = await TelegramAsync(ReadFile, payload, true, ct);
            if (response != FileBlock) return null;
            using var output = new MemoryStream();
            while (true)
            {
                foreach (byte b in content)
                {
                    if (b == 0) { output.WriteByte(13); output.WriteByte(10); }
                    else output.WriteByte(b);
                }
                if (output.Length > maxBytes) throw new Lsv2Exception("فایل روی کنترلر از ظرفیت مجاز برنامه بزرگ‌تر است.");
                (response, content) = await TelegramAsync(Ok, null, true, ct);
                if (response == FileDone) break;
                if (response != FileBlock) throw new Lsv2Exception("خواندن فایل از کنترلر نیمه‌کاره ماند.");
            }
            return output.ToArray();
        }

        /// <summary>Reads PLC words (16 bit, little-endian on the control) from word number <paramref name="first"/>; needs the PLC login (its password lives in the server settings).</summary>
        public async Task<List<short>> ReadWordsAsync(int first, int count, string plcPassword, CancellationToken ct)
        {
            if (_parameters == null || first < 0 || count < 1 || first + count > _parameters.Words) return null;
            if (!await LoginAsync("PLCDEBUG", plcPassword, ct)) return null;
            var payload = new byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(payload, _parameters.WordsStart + (uint)first * 2);
            payload[4] = (byte)(count * 2);
            var (response, c) = await TelegramAsync(ReadMemory, payload, true, ct);
            if (response != MemoryAnswer || c.Length < count * 2) return null;
            var list = new List<short>();
            for (int i = 0; i < count; i++) list.Add(BinaryPrimitives.ReadInt16LittleEndian(c.AsSpan(i * 2, 2)));
            return list;
        }

        /// <summary>Reads PLC markers (one byte each, 0 or 1) from marker number <paramref name="first"/>.</summary>
        public async Task<List<bool>> ReadMarkersAsync(int first, int count, string plcPassword, CancellationToken ct)
        {
            if (_parameters == null || first < 0 || count < 1 || first + count > _parameters.Markers) return null;
            if (!await LoginAsync("PLCDEBUG", plcPassword, ct)) return null;
            var payload = new byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(payload, _parameters.MarkersStart + (uint)first);
            payload[4] = (byte)count;
            var (response, c) = await TelegramAsync(ReadMemory, payload, true, ct);
            if (response != MemoryAnswer || c.Length < count) return null;
            return c.Take(count).Select(b => b != 0).ToList();
        }

        public void Dispose()
        {
            try
            {
                if (_stream != null) _stream.Write(new byte[] { 0, 0, 0, 0, (byte)'A', (byte)'_', (byte)'L', (byte)'O' });   // log out of everything
            }
            catch (Exception) { /* the control may already have closed the link */ }
            _stream?.Dispose();
            _tcp.Dispose();
        }
    }
}
