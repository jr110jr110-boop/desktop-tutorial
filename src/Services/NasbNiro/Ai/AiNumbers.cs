using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services.NasbNiro.Ai
{
    /// <summary>One numeric token of a text, reduced to its merged normalised value (no part matching anywhere).</summary>
    public sealed class AiNumberToken
    {
        /// <summary>The value the token stands for: sign kept, separators removed, scale word applied ("5 میلیارد" is 5000000000), no leading or trailing zeros.</summary>
        public string Canonical { get; set; }

        /// <summary>The written number without its scale word (but with the sign): "5" for "5 میلیارد".</summary>
        public string Plain { get; set; }

        /// <summary>The token is followed by درصد, % or ٪ (the number itself is what has to exist in the results).</summary>
        public bool Percent { get; set; }
        public string Original { get; set; }
    }

    /// <summary>Digit and token handling shared by the answer verifier and the speech normaliser (pure, no state).</summary>
    public static class AiNumbers
    {
        // [-]1,234,567.89 (any of , ٬ ،) or a plain 123.45, optionally followed by a scale word or a percent sign.
        // "/", "-" and ":" inside a text are not part of a number: dates and times split into tokens. A minus counts as a sign only at the start of a text
        // or after a space or a bracket.
        private static readonly Regex TokenRegex = new Regex(
            @"(?<sign>(?<=^|[\s(\[])[-−])?(?<num>\d+(?:[,٬،]\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:[ \t ‌]*(?<scale>(?:هزار|میلیون|میلیارد)(?!\p{L})|%|٪|درصد(?!\p{L})))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex ListMarker = new Regex(@"^[ \t]*\d{1,2}[.)\-–][ \t]", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>Persian (۰-۹) and Arabic-Indic (٠-٩) digits to ASCII; the Arabic decimal separator to ".".</summary>
        public static string NormalizeDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else if (c == '٫') sb.Append('.');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Leading zeros and trailing fraction zeros removed: "08" is "8", "12.50" is "12.5", "7.0" is "7".</summary>
        public static string Canonical(string number)
        {
            string integer = number;
            string fraction = string.Empty;
            int dot = number.IndexOf('.');
            if (dot >= 0)
            {
                integer = number.Substring(0, dot);
                fraction = number.Substring(dot + 1).TrimEnd('0');
            }
            integer = integer.TrimStart('0');
            if (integer.Length == 0) integer = "0";
            return fraction.Length == 0 ? integer : integer + "." + fraction;
        }

        private static string Signed(string canonical, bool negative) => negative && canonical != "0" ? "-" + canonical : canonical;

        private static decimal? ScaleOf(string scale)
        {
            switch (scale)
            {
                case "هزار": return 1_000m;
                case "میلیون": return 1_000_000m;
                case "میلیارد": return 1_000_000_000m;
                default: return null;
            }
        }

        public static List<AiNumberToken> Tokens(string text, bool dropListMarkers)
        {
            string normalized = NormalizeDigits(text);
            if (dropListMarkers) normalized = ListMarker.Replace(normalized, string.Empty);
            var result = new List<AiNumberToken>();
            foreach (Match m in TokenRegex.Matches(normalized))
            {
                string merged = m.Groups["num"].Value.Replace(",", string.Empty).Replace("٬", string.Empty).Replace("،", string.Empty);
                bool negative = m.Groups["sign"].Success;
                string plain = Canonical(merged);
                var token = new AiNumberToken { Original = m.Value.Trim(), Plain = Signed(plain, negative) };
                string scale = m.Groups["scale"].Success ? m.Groups["scale"].Value : null;
                decimal? factor = ScaleOf(scale);
                if (factor.HasValue)
                {
                    if (decimal.TryParse(merged, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && Math.Abs(value) < 79_000_000_000m)
                        token.Canonical = Signed(Canonical((value * factor.Value).ToString("0.############################", CultureInfo.InvariantCulture)), negative);
                    else
                        token.Canonical = plain + "x" + scale; // too big to compute: never equal to a number of a result
                }
                else
                {
                    token.Canonical = token.Plain;
                    token.Percent = scale != null;
                }
                result.Add(token);
            }
            return result;
        }

        /// <summary>Adds every number of a JSON text (numbers as such, strings and property names tokenised). The message of an error or forbidden result is not read: it may echo the arguments.</summary>
        public static void CollectAllowed(string jsonOrText, HashSet<string> allowed)
        {
            if (string.IsNullOrEmpty(jsonOrText)) return;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonOrText);
                Walk(doc.RootElement, allowed, true);
            }
            catch (JsonException)
            {
                AddText(jsonOrText, allowed);
            }
        }

        public static void AddText(string text, HashSet<string> allowed)
        {
            foreach (AiNumberToken t in Tokens(text, false))
            {
                allowed.Add(t.Canonical);
                allowed.Add(t.Plain);
            }
        }

        /// <summary>The value of a JSON number as written, without exponent, sign kept.</summary>
        public static string JsonNumber(JsonElement el)
        {
            string raw = el.GetRawText();
            if (raw.IndexOfAny(new[] { 'e', 'E' }) >= 0 && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d))
                raw = d.ToString("0.############################", CultureInfo.InvariantCulture);
            bool negative = raw.StartsWith("-", StringComparison.Ordinal);
            if (negative) raw = raw.Substring(1);
            return Signed(Canonical(raw), negative);
        }

        private static void Walk(JsonElement el, HashSet<string> allowed, bool root)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    allowed.Add(JsonNumber(el));
                    break;
                case JsonValueKind.String:
                    AddText(el.GetString(), allowed);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in el.EnumerateArray()) Walk(item, allowed, false);
                    break;
                case JsonValueKind.Object:
                    bool failed = root && (Flag(el, "error") || Flag(el, "forbidden"));
                    foreach (JsonProperty p in el.EnumerateObject())
                    {
                        // digits of a key ("payablesDue30") are legitimate to mention: "due in 30 days"
                        AddText(p.Name, allowed);
                        if (failed && p.NameEquals("message")) continue;
                        Walk(p.Value, allowed, false);
                    }
                    break;
            }
        }

        private static bool Flag(JsonElement el, string name) => el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
    }
}
