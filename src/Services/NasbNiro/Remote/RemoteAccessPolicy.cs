using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Services.NasbNiro.Remote
{
    /// <summary>
    /// Production release P2 part D: secure remote access without the internet dependency. The server answers the company network (LAN) as before; from outside it answers only the
    /// company VPN, only the roles allowed to work remotely, and by default read-only (plus signing in and the assistant). The open internet gets nothing, even when a firewall rule is
    /// wrong. Settings (appsettings, section Remote):
    ///   Remote:Enabled          true/false (default false: nothing changes)
    ///   Remote:LanNetworks      CIDR list of the company network (default 10/8, 172.16/12, 192.168/16; the loopback is always local)
    ///   Remote:VpnNetworks      CIDR list of the VPN address pool (empty: no remote access at all)
    ///   Remote:Roles            the roles that may work remotely (default CEO, PRODUCTIONMANAGER, SHOPSUPERVISOR, PMO, PLANNING, MAINTENANCE)
    ///   Remote:ReadOnly         true/false (default true)
    ///   Remote:WritePaths       the write addresses that stay open remotely (default: sign-in, sign-out, token, password, the assistant)
    /// Pure: the decision depends only on the settings, the address, the method, the path, the sign-in and the roles, so it is unit tested.
    /// </summary>
    public sealed class RemoteAccessPolicy
    {
        public enum Verdict
        {
            /// <summary>From the company network: as before.</summary>
            Local = 0,

            /// <summary>From the VPN and allowed.</summary>
            Remote = 1,

            /// <summary>Not from the company network or the VPN.</summary>
            OutsideNetwork = 2,

            /// <summary>From the VPN, but the role may not work remotely.</summary>
            RoleNotAllowed = 3,

            /// <summary>From the VPN, a write while remote access is read-only.</summary>
            ReadOnly = 4,
        }

        public const string ItemKey = "pmis.remote";

        public static readonly string[] DefaultLan = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };
        public static readonly string[] DefaultRoles = { "CEO", "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "PMO", "PLANNING", "MAINTENANCE" };

        public static readonly string[] DefaultWritePaths =
        {
            "/api/v1/accounts/login", "/api/v1/accounts/logout", "/api/v1/accounts/refresh-token", "/api/v1/accounts/change-password", "/api/v1/accounts/change-temporary-password",
            "/api/v1/ai/chat", "/api/v1/ai/voice", "/api/v1/ceo/analyze",
        };

        public bool Enabled { get; private set; }
        public bool ReadOnly { get; private set; } = true;
        public IReadOnlyList<(uint Network, uint Mask)> Lan { get; private set; }
        public IReadOnlyList<(uint Network, uint Mask)> Vpn { get; private set; }
        public IReadOnlyList<string> Roles { get; private set; }
        public IReadOnlyList<string> WritePaths { get; private set; }

        /// <summary>The settings that could not be read (Persian); a wrong setting never opens anything: a bad VPN entry is left out.</summary>
        public List<string> Problems { get; } = new List<string>();

        public static RemoteAccessPolicy From(IConfiguration configuration)
        {
            var p = new RemoteAccessPolicy();
            IConfigurationSection s = configuration?.GetSection("Remote");
            p.Enabled = string.Equals(s?["Enabled"], "true", StringComparison.OrdinalIgnoreCase);
            p.ReadOnly = !string.Equals(s?["ReadOnly"], "false", StringComparison.OrdinalIgnoreCase);
            List<string> lan = List(s, "LanNetworks");
            p.Lan = Parse(lan.Count > 0 ? lan : DefaultLan.ToList(), p.Problems);
            p.Vpn = Parse(List(s, "VpnNetworks"), p.Problems);
            List<string> roles = List(s, "Roles");
            p.Roles = (roles.Count > 0 ? roles : DefaultRoles.ToList()).Select(r => r.Trim().ToUpperInvariant()).Where(r => r.Length > 0).Distinct().ToList();
            List<string> paths = List(s, "WritePaths");
            p.WritePaths = (paths.Count > 0 ? paths : DefaultWritePaths.ToList()).Select(x => x.Trim().TrimEnd('/').ToLowerInvariant()).Where(x => x.StartsWith("/", StringComparison.Ordinal)).Distinct().ToList();
            return p;
        }

        /// <summary>A list setting: children (Remote:VpnNetworks:0 ...) or one text with ',' or ';'.</summary>
        private static List<string> List(IConfigurationSection s, string key)
        {
            var list = new List<string>();
            IConfigurationSection x = s?.GetSection(key);
            if (x == null) return list;
            if (!string.IsNullOrWhiteSpace(x.Value)) list.AddRange(x.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            list.AddRange(x.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
            return list;
        }

        private static List<(uint, uint)> Parse(IEnumerable<string> entries, List<string> problems)
        {
            var list = new List<(uint, uint)>();
            foreach (string e in entries)
            {
                if (TryCidr(e, out uint n, out uint m)) list.Add((n, m));
                else problems.Add($"بازه شبکه «{e}» در تنظیمات Remote نامعتبر است و نادیده گرفته شد.");
            }
            return list;
        }

        /// <summary>"a.b.c.d" or "a.b.c.d/n" (full dotted IPv4 only).</summary>
        public static bool TryCidr(string text, out uint network, out uint mask)
        {
            network = 0; mask = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            int slash = t.IndexOf('/');
            string ipText = slash < 0 ? t : t.Substring(0, slash);
            int prefix = 32;
            if (slash >= 0 && (!int.TryParse(t.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix < 0 || prefix > 32)) return false;
            string[] parts = ipText.Split('.');
            if (parts.Length != 4 || !parts.All(x => x.Length > 0 && x.Length <= 3 && x.All(char.IsAsciiDigit))) return false;
            if (!IPAddress.TryParse(ipText, out IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            network = ToUInt(ip) & mask;
            return true;
        }

        private static uint ToUInt(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static bool In(uint value, IEnumerable<(uint Network, uint Mask)> list) => list.Any(x => (value & x.Mask) == x.Network);

        /// <summary>local, vpn or outside for an address (an IPv4-mapped IPv6 address is unwrapped; the loopback is local; any other IPv6 address is outside).</summary>
        public string Zone(IPAddress remote)
        {
            if (remote == null) return "local";   // in-process tests and the server's own calls have no address
            if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
            if (IPAddress.IsLoopback(remote)) return "local";
            if (remote.AddressFamily != AddressFamily.InterNetwork) return "outside";
            uint v = ToUInt(remote);
            if (In(v, Vpn)) return "vpn";   // the VPN pool is often a part of a private range: it is checked first
            if (In(v, Lan)) return "local";
            return "outside";
        }

        public static bool IsRead(string method) =>
            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

        /// <summary>The decision for one request. <paramref name="roles"/> are the roles of the signed-in user (empty when not signed in).</summary>
        public Verdict Decide(IPAddress remote, string method, string path, bool signedIn, IEnumerable<string> roles)
        {
            if (!Enabled) return Verdict.Local;
            string zone = Zone(remote);
            if (zone == "local") return Verdict.Local;
            if (zone == "outside") return Verdict.OutsideNetwork;
            string p = (path ?? "/").TrimEnd('/').ToLowerInvariant();
            bool api = p.StartsWith("/api/", StringComparison.Ordinal);
            bool openWrite = WritePaths.Any(w => p == w);
            // pages and files of the front end, and signing in, are open to the VPN; the API checks the token itself
            if (!api) return IsRead(method) ? Verdict.Remote : Verdict.ReadOnly;
            if (signedIn && !(roles ?? Enumerable.Empty<string>()).Any(r => Roles.Contains((r ?? string.Empty).ToUpperInvariant()))) return Verdict.RoleNotAllowed;
            if (ReadOnly && !IsRead(method) && !openWrite) return Verdict.ReadOnly;
            return Verdict.Remote;
        }

        public static string Message(Verdict v) => v switch
        {
            Verdict.OutsideNetwork => "دسترسی به سامانه فقط از شبکه داخلی شرکت یا VPN شرکت ممکن است.",
            Verdict.RoleNotAllowed => "نقش شما برای کار با سامانه از بیرون شرکت (VPN) مجاز نیست.",
            Verdict.ReadOnly => "دسترسی از بیرون شرکت (VPN) فقط برای مشاهده است؛ ثبت و تأیید را از داخل شرکت انجام دهید.",
            _ => null,
        };
    }
}
