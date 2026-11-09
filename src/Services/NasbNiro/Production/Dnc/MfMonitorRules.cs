using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>The pure rules of the live monitoring (no database, no clock; unit tested in MonitorTests).</summary>
    public static class MfMonitorRules
    {
        /// <summary>Failed polls in a row before a machine counts as offline.</summary>
        public const int OfflineAfterFailures = 3;

        public static int Interval(MfMachineMonitor m) => m?.PollSeconds is >= 2 and <= 3600 ? m.PollSeconds.Value : 10;

        /// <summary>The value of input n after the inversion mask.</summary>
        public static bool Input(IReadOnlyList<bool> inputs, int? index, int invertMask)
        {
            if (!index.HasValue || inputs == null || index.Value < 0 || index.Value >= inputs.Count) return false;
            bool raw = inputs[index.Value];
            return index.Value < 31 && (invertMask & (1 << index.Value)) != 0 ? !raw : raw;
        }

        /// <summary>The state of a machine from its stack-light inputs: alarm wins, then running, then idle (power on or no power input), else off.</summary>
        public static MfMachineState FromInputs(IReadOnlyList<bool> inputs, MfMachineMonitor m)
        {
            if (Input(inputs, m.AlarmInput, m.InvertMask)) return MfMachineState.Alarm;
            if (Input(inputs, m.RunInput, m.InvertMask)) return MfMachineState.Running;
            if (!m.PowerInput.HasValue) return MfMachineState.Idle;
            return Input(inputs, m.PowerInput, m.InvertMask) ? MfMachineState.Idle : MfMachineState.Off;
        }

        /// <summary>How many inputs a poll reads (up to the highest mapped input).</summary>
        public static int InputCount(MfMachineMonitor m) => new[] { m.RunInput, m.AlarmInput, m.PowerInput }.Where(i => i.HasValue).Select(i => i.Value + 1).DefaultIfEmpty(1).Max();

        public sealed class Span
        {
            public MfMachineState State { get; init; }
            public DateTime StartAt { get; init; }
            public DateTime? EndAt { get; init; }
        }

        /// <summary>Minutes per state inside [from, to) (an open period runs to <paramref name="now"/>); periods are cut to the window.</summary>
        public static Dictionary<MfMachineState, double> Minutes(IEnumerable<Span> spans, DateTime from, DateTime to, DateTime now)
        {
            var result = Enum.GetValues<MfMachineState>().ToDictionary(s => s, _ => 0d);
            foreach (Span s in spans ?? Enumerable.Empty<Span>())
            {
                DateTime start = s.StartAt < from ? from : s.StartAt;
                DateTime end = s.EndAt ?? now;
                if (end > to) end = to;
                if (end > start) result[s.State] += (end - start).TotalMinutes;
            }
            return result;
        }

        /// <summary>Running share of the time the machine was observed (offline time is not observed time); null when nothing was observed.</summary>
        public static decimal? RunPercent(Dictionary<MfMachineState, double> minutes)
        {
            double observed = minutes.Where(kv => kv.Key != MfMachineState.Offline).Sum(kv => kv.Value);
            if (observed <= 0) return null;
            return Math.Round((decimal)(minutes[MfMachineState.Running] / observed * 100), 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The machine and the shop floor disagree: an open run/setup booking on a machine that has been idle or off for the given minutes ("booked but not running"), or a machine running that long with no open booking
        /// ("running without a booking"). Null when they agree or the disagreement is shorter.
        /// </summary>
        public static string Mismatch(MfMachineState state, double minutesInState, bool openRunBooking, int limitMinutes)
        {
            if (minutesInState < Math.Max(1, limitMinutes)) return null;
            if (openRunBooking && (state == MfMachineState.Idle || state == MfMachineState.Off)) return "booked-idle";
            if (!openRunBooking && state == MfMachineState.Running) return "running-unbooked";
            return null;
        }
    }

    /// <summary>Reads the inputs of a Modbus TCP module. Implementations never throw for a network failure: the Persian reason is in the result.</summary>
    public interface IMfModbusReader
    {
        Task<(bool[] Inputs, string Error)> ReadAsync(string host, int port, byte unit, MfModbusFunction function, int start, int count, CancellationToken ct);
    }

    /// <summary>A small Modbus TCP client (functions 1 and 2, one request per connection), without any outside package.</summary>
    public sealed class ModbusTcpReader : IMfModbusReader
    {
        private static int _transaction;

        public async Task<(bool[] Inputs, string Error)> ReadAsync(string host, int port, byte unit, MfModbusFunction function, int start, int count, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(host)) return (null, "نشانی ماژول ورودی تعریف نشده است.");
            if (count < 1 || count > 2000 || start < 0 || start > 65535) return (null, "محدوده ورودی‌ها نامعتبر است.");
            try
            {
                using var client = new TcpClient { NoDelay = true };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(host, port <= 0 ? 502 : port, cts.Token);
                NetworkStream s = client.GetStream();
                ushort id = (ushort)Interlocked.Increment(ref _transaction);
                byte[] request =
                {
                    (byte)(id >> 8), (byte)id, 0, 0, 0, 6, unit, (byte)function,
                    (byte)(start >> 8), (byte)start, (byte)(count >> 8), (byte)count,
                };
                await s.WriteAsync(request, cts.Token);
                byte[] header = await ReadExactAsync(s, 9, cts.Token);
                if (header[0] != request[0] || header[1] != request[1]) return (null, "پاسخ ماژول ورودی به این درخواست نیست.");
                if ((header[7] & 0x80) != 0) return (null, "ماژول ورودی خطای Modbus برگرداند (کد " + header[8] + ").");
                if (header[7] != (byte)function) return (null, "پاسخ Modbus نامعتبر است.");
                int bytes = header[8];
                if (bytes != (count + 7) / 8) return (null, "طول پاسخ Modbus نامعتبر است.");
                byte[] data = await ReadExactAsync(s, bytes, cts.Token);
                var inputs = new bool[count];
                for (int i = 0; i < count; i++) inputs[i] = (data[i / 8] & (1 << (i % 8))) != 0;
                return (inputs, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return (null, NcLinkText.Network(ex));
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream s, int n, CancellationToken ct)
        {
            var buffer = new byte[n];
            int read = 0;
            while (read < n)
            {
                int r = await s.ReadAsync(buffer.AsMemory(read, n - read), ct);
                if (r == 0) throw new System.IO.IOException("ارتباط ماژول ورودی قطع شد.");
                read += r;
            }
            return buffer;
        }
    }
}
