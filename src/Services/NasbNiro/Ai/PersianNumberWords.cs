using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.NasbNiro.Ai
{
    public interface IPersianNumberWords
    {
        /// <summary>37365625 is «سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج»; 0 is «صفر»; negative numbers start with «منفی».</summary>
        string ToWords(long number);

        /// <summary>The amount in whole rials as words followed by « ریال».</summary>
        string RialText(decimal amount);

        /// <summary>Replaces every number of a text by its words (so a speech engine reads it): a sign becomes «منفی», a percent sign «درصد», dates yyyy/mm/dd «روز سوم آبان سال ...», times «چهارده و سی دقیقه»; codes such as PO-1405-0012 are read group by group.</summary>
        string SpeechNormalize(string text);
    }

    /// <summary>Numbers as Persian words for the text-to-speech and for amounts in words. Pure: no state, no culture.</summary>
    public class PersianNumberWords : IPersianNumberWords
    {
        private static readonly string[] Units = { "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه" };
        private static readonly string[] Teens = { "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };
        private static readonly string[] Tens = { "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };
        private static readonly string[] Hundreds = { "", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد" };
        public static readonly string[] Months =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
        };

        private const string And = " و ";

        // times and dates first (so their parts are not read as amounts), then codes (read group by group), then any number with optional sign, separators, fraction and percent sign
        private static readonly Regex TimeRegex = new Regex(@"(?<![\d:/.])(\d{1,2}):(\d{2})(?![\d:])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DateRegex = new Regex(@"(?<![\d.])(\d{4})/(\d{1,2})/(\d{1,2})(?![\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CodeRegex = new Regex(@"(?<![\p{L}\d])(?:[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)+|[A-Za-z]+\d+[A-Za-z0-9]*)(?![\p{L}\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex NumberRegex = new Regex(@"(?<sign>(?<=^|[\s(\[])[-−])?(?<num>\d+(?:[,٬،]\d{3})*(?:\.\d+)?)(?<pct>[ \t]*[%٪])?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex DigitsRegex = new Regex(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string Below1000(int n)
        {
            var parts = new List<string>();
            if (n >= 100) parts.Add(Hundreds[n / 100]);
            int rest = n % 100;
            if (rest >= 20)
            {
                parts.Add(Tens[rest / 10]);
                if (rest % 10 > 0) parts.Add(Units[rest % 10]);
            }
            else if (rest >= 10) parts.Add(Teens[rest - 10]);
            else if (rest > 0) parts.Add(Units[rest]);
            return string.Join(And, parts);
        }

        private static string Positive(ulong n)
        {
            // billions first (the count of milliards is itself spoken with the same rules: 1000 milliards is «هزار میلیارد»)
            var parts = new List<string>();
            ulong milliards = n / 1_000_000_000UL;
            int millions = (int)(n / 1_000_000UL % 1000UL);
            int thousands = (int)(n / 1000UL % 1000UL);
            int units = (int)(n % 1000UL);
            if (milliards > 0) parts.Add(Positive(milliards) + " میلیارد");
            if (millions > 0) parts.Add(Below1000(millions) + " میلیون");
            if (thousands > 0) parts.Add(thousands == 1 ? "هزار" : Below1000(thousands) + " هزار");
            if (units > 0) parts.Add(Below1000(units));
            return string.Join(And, parts);
        }

        public string ToWords(long number)
        {
            if (number == 0) return "صفر";
            if (number < 0) return "منفی " + Positive(number == long.MinValue ? (ulong)long.MaxValue + 1UL : (ulong)(-number));
            return Positive((ulong)number);
        }

        public string RialText(decimal amount)
        {
            decimal whole = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
            if (whole > long.MaxValue || whole < long.MinValue) throw new ArgumentOutOfRangeException(nameof(amount));
            return ToWords((long)whole) + " ریال";
        }

        /// <summary>The day of a month as an ordinal: «اول»، «دوم»، «سوم»، «سی و یکم».</summary>
        public string Ordinal(int day)
        {
            if (day == 1) return "اول";
            if (day == 30) return "سی‌ام";
            string words = ToWords(day);
            if (day % 10 == 3 && day != 13) return words.Substring(0, words.Length - 2) + "سوم";
            return words + "م";
        }

        private string DigitByDigit(string digits)
        {
            var parts = new List<string>();
            foreach (char c in digits) parts.Add(c == '0' ? "صفر" : Units[c - '0']);
            return string.Join(" ", parts);
        }

        /// <summary>A group of digits of a code: a plain number, or digit by digit when it has a leading zero or is long (serial numbers are not amounts).</summary>
        private string CodeGroup(string digits)
        {
            if (digits.Length > 1 && digits[0] == '0' || digits.Length > 9) return DigitByDigit(digits);
            return ToWords(long.Parse(digits, CultureInfo.InvariantCulture));
        }

        public string SpeechNormalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string s = AiNumbers.NormalizeDigits(text);
            s = DateRegex.Replace(s, m =>
            {
                int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (month < 1 || month > 12 || day < 1 || day > 31) return m.Value;
                return "روز " + Ordinal(day) + " " + Months[month - 1] + " سال " + ToWords(year);
            });
            s = TimeRegex.Replace(s, m =>
            {
                int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (hour > 23 || minute > 59) return m.Value;
                return minute == 0 ? ToWords(hour) : ToWords(hour) + And + ToWords(minute) + " دقیقه";
            });
            s = CodeRegex.Replace(s, m => Regex.Replace(DigitsRegex.Replace(m.Value, g => " " + CodeGroup(g.Value) + " ").Replace('-', ' '), @"\s+", " ").Trim());
            return NumberRegex.Replace(s, m =>
            {
                string value = m.Groups["num"].Value.Replace(",", string.Empty).Replace("٬", string.Empty).Replace("،", string.Empty);
                int dot = value.IndexOf('.');
                string integer = dot >= 0 ? value.Substring(0, dot) : value;
                string words;
                if (integer.Length > 1 && integer[0] == '0' || integer.Length > 15) words = DigitByDigit(integer);
                else if (ulong.TryParse(integer, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) && n <= long.MaxValue) words = ToWords((long)n);
                else return m.Value;
                if (dot >= 0)
                {
                    string fraction = value.Substring(dot + 1).TrimEnd('0');
                    if (fraction.Length > 0) words += " ممیز " + DigitByDigit(fraction);
                }
                if (m.Groups["sign"].Success && words != "صفر") words = "منفی " + words;
                if (m.Groups["pct"].Success) words += " درصد";
                return words;
            });
        }
    }

    /// <summary>The reverse of <see cref="PersianNumberWords"/> for the common forms: «پنج میلیارد»، «سی و هفت میلیون و ...»، «هزار میلیارد».</summary>
    public static class PersianNumberParser
    {
        private static readonly Dictionary<string, int> Small = BuildSmall();
        private static readonly Dictionary<string, long> Scales = new Dictionary<string, long> { ["هزار"] = 1_000, ["میلیون"] = 1_000_000, ["میلیارد"] = 1_000_000_000 };

        private static Dictionary<string, int> BuildSmall()
        {
            var d = new Dictionary<string, int> { ["صفر"] = 0 };
            string[] units = { "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه" };
            string[] teens = { "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };
            string[] tens = { "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };
            string[] hundreds = { "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد" };
            for (int i = 0; i < units.Length; i++) d[units[i]] = i + 1;
            for (int i = 0; i < teens.Length; i++) d[teens[i]] = 10 + i;
            for (int i = 0; i < tens.Length; i++) d[tens[i]] = (i + 2) * 10;
            for (int i = 0; i < hundreds.Length; i++) d[hundreds[i]] = (i + 1) * 100;
            return d;
        }

        /// <summary>The runs of spelled-out number words that contain a scale word (هزار، میلیون، میلیارد), with the value of each.</summary>
        public static List<KeyValuePair<string, string>> ScaledRuns(string text)
        {
            var found = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(text)) return found;
            string[] raw = text.Split(new[] { ' ', '\t', '\r', '\n', '\u200c' }, StringSplitOptions.RemoveEmptyEntries);
            var words = raw.Select(w => w.Trim('،', '؛', ':', '.', '(', ')', '«', '»', '؟', '?', '!', '"')).ToList();
            int i = 0;
            while (i < words.Count)
            {
                // a scale word right after digits ("5 میلیارد") belongs to the digits, which the number check reads
                if (!IsNumberWord(words[i]) || (Scales.ContainsKey(words[i]) && i > 0 && words[i - 1].Any(char.IsDigit))) { i++; continue; }
                int start = i;
                int end = i;
                while (end + 1 < words.Count)
                {
                    if (IsNumberWord(words[end + 1])) { end++; continue; }
                    if (words[end + 1] == "و" && end + 2 < words.Count && IsNumberWord(words[end + 2])) { end += 2; continue; }
                    break;
                }
                var run = words.Skip(start).Take(end - start + 1).ToList();
                if (run.Any(w => Scales.ContainsKey(w)))
                    found.Add(new KeyValuePair<string, string>(string.Join(" ", run), Value(run).ToString(CultureInfo.InvariantCulture)));
                i = end + 1;
            }
            return found;
        }

        private static bool IsNumberWord(string w) => Small.ContainsKey(w) || Scales.ContainsKey(w);

        private static decimal Value(List<string> run)
        {
            decimal big = 0, low = 0, current = 0;
            foreach (string w in run)
            {
                if (w == "و") continue;
                if (Small.TryGetValue(w, out int v)) { current += v; continue; }
                long scale = Scales[w];
                if (scale == 1000) { low += (current == 0 ? 1 : current) * 1000; current = 0; }
                else { big += (low + (current == 0 && low == 0 ? 1 : current)) * scale; low = 0; current = 0; }
            }
            return big + low + current;
        }
    }
}
