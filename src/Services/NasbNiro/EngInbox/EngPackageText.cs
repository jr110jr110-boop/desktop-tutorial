using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>Text and number normalisation of the package workbook: Persian and Arabic digits, ي/ی and ك/ک, the half space, Arabic decimal and thousands separators, Shamsi dates.</summary>
    public static class EngText
    {
        /// <summary>Headers and sheet names are matched by this text: trimmed, one space between words, ي/ی and ك/ک unified, the half space read as a space, lower case.</summary>
        public static string Norm(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char raw in text)
            {
                char c = raw;
                if (c == 'ي' || c == 'ى') c = 'ی';       // Arabic yeh, alef maksura -> Persian yeh
                else if (c == 'ك') c = 'ک';                    // Arabic kaf -> Persian keheh
                else if (c == '‌' || c == '‍' || c == ' ' || c == '‏' || c == '‎' || c == '﻿' || c == '\t') c = ' ';
                else if (c == 'ـ') continue;                        // tatweel
                sb.Append(c);
            }
            string s = Digits(sb.ToString()).Trim().TrimEnd(':', '：').Trim().ToLowerInvariant();
            return Regex.Replace(s, @"\s+", " ");
        }

        /// <summary>Persian (۰-۹) and Arabic-Indic (٠-٩) digits to Latin digits.</summary>
        public static string Digits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The text of a cell for a person: trimmed, the digits left as written.</summary>
        internal static string CellText(XlsxCell cell)
        {
            string t = cell?.AsText();
            return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        }

        /// <summary>A decimal from a cell or a text ("۱٫۵", "1,5", "2/5", "1٬250", 3). False when it is not a plain number.</summary>
        internal static bool TryDecimal(XlsxCell cell, out decimal value)
        {
            value = 0;
            if (cell == null) return false;
            if (cell.Number.HasValue)
            {
                double d = cell.Number.Value;
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e15) return false;
                value = Math.Round((decimal)d, 6);
                return true;
            }
            return TryDecimal(cell.Text, out value);
        }

        public static bool TryDecimal(string text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = Digits(text.Trim()).Replace("٬", "").Replace(" ", "").Replace(" ", "");
            s = s.Replace('٫', '.');                                // Arabic decimal separator
            if (Regex.IsMatch(s, @"^\d{1,3}(,\d{3})+(\.\d+)?$")) s = s.Replace(",", "");   // 1,250.5 thousands
            else if (Regex.IsMatch(s, @"^[+-]?\d+,\d+$")) s = s.Replace(',', '.');           // 1,5 decimal comma
            else if (Regex.IsMatch(s, @"^[+-]?\d+/\d{1,6}$")) s = s.Replace('/', '.');       // 2/5 Persian slash decimal
            if (!Regex.IsMatch(s, @"^[+-]?(\d+(\.\d*)?|\.\d+)$")) return false;
            if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal d)) return false;
            if (Math.Abs(d) > 1e15m) return false;
            value = Math.Round(d, 6);
            return true;
        }

        internal static bool TryInt(XlsxCell cell, out int value)
        {
            value = 0;
            if (!TryDecimal(cell, out decimal d)) return false;
            if (d != Math.Truncate(d) || d < int.MinValue || d > int.MaxValue) return false;
            value = (int)d;
            return true;
        }

        public static bool TryInt(string text, out int value)
        {
            value = 0;
            if (!TryDecimal(text, out decimal d) || d != Math.Truncate(d) || d < int.MinValue || d > int.MaxValue) return false;
            value = (int)d;
            return true;
        }

        /// <summary>بله/خیر (also yes/no, true/false, 1/0). An empty cell is "no". Null when the value is none of them.</summary>
        internal static bool? YesNo(XlsxCell cell)
        {
            string t = Norm(CellText(cell));
            if (t.Length == 0) return false;
            if (cell.Number.HasValue && string.IsNullOrEmpty(cell.Text)) return cell.Number.Value == 1 ? true : cell.Number.Value == 0 ? false : (bool?)null;
            switch (t)
            {
                case "بله": case "آری": case "اری": case "yes": case "y": case "true": case "1": case "x": return true;
                case "خیر": case "نه": case "no": case "n": case "false": case "0": case "-": return false;
                default: return null;
            }
        }

        /// <summary>A date: Shamsi (1405/08/25, ۱۴۰۵-۰۸-۲۵) or ISO (2026-11-16), or an Excel date cell. Null when it is neither.</summary>
        internal static DateTime? Date(XlsxCell cell)
        {
            if (cell == null) return null;
            if (cell.Number.HasValue && string.IsNullOrEmpty(cell.Text))
            {
                if (!cell.IsDateFormat) return null;
                double serial = cell.Number.Value + (cell.Date1904 ? 1462 : 0);
                if (serial < 1 || serial > 2958465) return null;
                try { return DateTime.FromOADate(serial).Date; } catch (ArgumentException) { return null; }
            }
            return Date(cell.Text);
        }

        public static DateTime? Date(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match m = Regex.Match(Digits(text.Trim()), @"^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})$");
            if (!m.Success) return null;
            int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            try
            {
                if (y >= 1200 && y <= 1700) return new PersianCalendar().ToDateTime(y, mo, d, 0, 0, 0, 0).Date;
                if (y >= 1700 && y <= 2500) return new DateTime(y, mo, d);
            }
            catch (ArgumentOutOfRangeException) { }
            return null;
        }

        public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();

        public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data ?? Array.Empty<byte>())).ToLowerInvariant();

        /// <summary>The decimal as the hash and the database see it: four places, invariant.</summary>
        public static string Num(decimal d) => Math.Round(d, 4).ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>The unit names of the package and what they are stored as (Persian). English aliases are accepted.</summary>
        private static readonly Dictionary<string, string> Units = BuildUnits();

        private static Dictionary<string, string> BuildUnits()
        {
            var map = new Dictionary<string, string>();
            void Add(string canonical, params string[] aliases) { map[Norm(canonical)] = canonical; foreach (string a in aliases) map[Norm(a)] = canonical; }
            Add("عدد", "ea", "pcs", "pc", "each", "no", "nos");
            Add("کیلوگرم", "kg", "کیلو");
            Add("گرم", "g", "gr");
            Add("تن", "ton", "t");
            Add("متر", "m", "mtr");
            Add("سانتی‌متر", "cm", "سانتیمتر", "سانتی متر");
            Add("میلی‌متر", "mm", "میلیمتر", "میلی متر");
            Add("لیتر", "l", "lit", "liter", "litre");
            Add("متر مربع", "m2", "مترمربع", "m²");
            Add("متر مکعب", "m3", "مترمکعب", "m³");
            Add("ست", "set");
            Add("جفت", "pair", "pr");
            Add("دست");
            Add("مجموعه", "assy", "assembly");
            Add("شاخه");
            Add("ورق");
            Add("رول", "roll");
            Add("بسته", "pack", "pkg");
            return map;
        }

        /// <summary>The Persian name of a unit of the list above; null when the unit is unknown.</summary>
        public static string CanonicalUnit(string text) => Units.TryGetValue(Norm(text), out string u) ? u : null;

        public static IReadOnlyCollection<string> UnitNames => Units.Values.Distinct().ToList();

        /// <summary>The make or buy value: ساخت / خرید / تامین توسط مشتری / پیمانکاری (also make, buy, purchase, MPL, PL ...). Null when unknown.</summary>
        public static EngMakeBuy? MakeBuy(string text)
        {
            switch (Norm(text))
            {
                case "ساخت": case "make": case "m": case "mpl": case "made": return EngMakeBuy.Make;
                case "خرید": case "buy": case "purchase": case "b": case "p": case "pl": case "purchased": return EngMakeBuy.Purchase;
                case "تامین مشتری": case "تامین توسط مشتری": case "تأمین مشتری": case "مشتری": case "customer": case "customer supplied": case "cs": return EngMakeBuy.CustomerSupplied;
                case "پیمانکاری": case "پیمانکار": case "برون سپاری": case "subcontract": case "sub": return EngMakeBuy.Subcontract;
                default: return null;
            }
        }

        public static string MakeBuyText(EngMakeBuy m)
        {
            switch (m)
            {
                case EngMakeBuy.Make: return "ساخت";
                case EngMakeBuy.Purchase: return "خرید";
                case EngMakeBuy.CustomerSupplied: return "تامین توسط مشتری";
                default: return "پیمانکاری";
            }
        }
    }
}
