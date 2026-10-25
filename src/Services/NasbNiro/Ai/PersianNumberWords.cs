using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Replaces every number of a text by its words (so a speech engine reads it): dates yyyy/mm/dd become «روز d ماه‌نام سال yyyy».</summary>
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

        // a date first (so its parts are not read as three amounts), then any number with optional separators and fraction
        private static readonly Regex DateRegex = new Regex(@"(?<![\d.])(\d{4})/(\d{1,2})/(\d{1,2})(?![\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex NumberRegex = new Regex(@"\d+(?:[,٬،]\d{3})*(?:\.\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                return "روز " + ToWords(day) + " " + Months[month - 1] + " سال " + ToWords(year);
            });
            return NumberRegex.Replace(s, m =>
            {
                string value = m.Value.Replace(",", string.Empty).Replace("٬", string.Empty).Replace("،", string.Empty);
                int dot = value.IndexOf('.');
                string integer = dot >= 0 ? value.Substring(0, dot) : value;
                if (!ulong.TryParse(integer, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) || n > long.MaxValue) return m.Value;
                string words = ToWords((long)n);
                if (dot < 0) return words;
                string fraction = value.Substring(dot + 1).TrimEnd('0');
                if (fraction.Length == 0) return words;
                var digits = new List<string>();
                foreach (char c in fraction) digits.Add(c == '0' ? "صفر" : Units[c - '0']);
                return words + " ممیز " + string.Join(" ", digits);
            });
        }
    }
}
