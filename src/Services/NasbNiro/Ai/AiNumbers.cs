using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services.NasbNiro.Ai
{
    /// <summary>One numeric token of a text: its canonical form, and the parts when it was written with thousands separators.</summary>
    public sealed class AiNumberToken
    {
        public string Canonical { get; set; }

        /// <summary>The groups of "1,234,567" as separate numbers (empty for a plain number).</summary>
        public List<string> Parts { get; set; } = new List<string>();
        public string Original { get; set; }
    }

    /// <summary>Digit and token handling shared by the answer verifier and the speech normaliser (pure, no state).</summary>
    public static class AiNumbers
    {
        // 1,234,567.89 (any of , ٬ ،) or a plain 123.45. "/", "-" and ":" are not part of a number: dates and times split into tokens.
        private static readonly Regex TokenRegex = new Regex(@"\d+(?:[,٬،]\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

        public static List<AiNumberToken> Tokens(string text, bool dropListMarkers)
        {
            string normalized = NormalizeDigits(text);
            if (dropListMarkers) normalized = ListMarker.Replace(normalized, string.Empty);
            var result = new List<AiNumberToken>();
            foreach (Match m in TokenRegex.Matches(normalized))
            {
                string value = m.Value;
                var token = new AiNumberToken { Original = value };
                if (value.IndexOfAny(new[] { ',', '٬', '،' }) >= 0)
                {
                    string merged = value.Replace(",", string.Empty).Replace("٬", string.Empty).Replace("،", string.Empty);
                    token.Canonical = Canonical(merged);
                    int dot = value.IndexOf('.');
                    string integerPart = dot >= 0 ? value.Substring(0, dot) : value;
                    foreach (string part in integerPart.Split(new[] { ',', '٬', '،' })) token.Parts.Add(Canonical(part));
                    if (dot >= 0 && token.Parts.Count > 0) token.Parts[token.Parts.Count - 1] = Canonical(integerPart.Split(new[] { ',', '٬', '،' }).Last() + value.Substring(dot));
                }
                else token.Canonical = Canonical(value);
                result.Add(token);
            }
            return result;
        }

        /// <summary>Adds every number of a JSON text (numbers as such, strings tokenised). Property names are not numbers.</summary>
        public static void CollectAllowed(string jsonOrText, HashSet<string> allowed)
        {
            if (string.IsNullOrEmpty(jsonOrText)) return;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonOrText);
                Walk(doc.RootElement, allowed);
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
                foreach (string p in t.Parts) allowed.Add(p);
            }
        }

        private static void Walk(JsonElement el, HashSet<string> allowed)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    string raw = el.GetRawText();
                    if (raw.IndexOfAny(new[] { 'e', 'E' }) >= 0 && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d))
                        raw = d.ToString("0.############################", CultureInfo.InvariantCulture);
                    if (raw.StartsWith("-", StringComparison.Ordinal)) raw = raw.Substring(1);
                    allowed.Add(Canonical(raw));
                    break;
                case JsonValueKind.String:
                    AddText(el.GetString(), allowed);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in el.EnumerateArray()) Walk(item, allowed);
                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty p in el.EnumerateObject()) Walk(p.Value, allowed);
                    break;
            }
        }
    }
}
