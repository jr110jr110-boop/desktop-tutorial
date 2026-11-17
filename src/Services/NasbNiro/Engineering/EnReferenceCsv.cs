using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Engineering
{
    /// <summary>
    /// The CSV file of a reference list (the only way a human enters the lines in a test release: a file, not typing).
    /// UTF-8, header row docType,title,discipline,required,tender,units; units are Persian or code names separated by «;». A cell with a comma or a line break is quoted with ".
    /// Every refusal names the row of the file (the header is row 1).
    /// </summary>
    internal static class EnReferenceCsv
    {
        public const int MaxBytes = 512 * 1024;
        public const int MaxErrorsShown = 10;
        public static readonly string[] Header = { "docType", "title", "discipline", "required", "tender", "units" };
        public static readonly string HeaderText = string.Join(",", Header);

        private static string Key(string s) => (s ?? string.Empty).Trim().Replace("‌", string.Empty).Replace(" ", string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').ToLowerInvariant();

        /// <summary>Persian name (also with or without the half space), code name or number 1..7 of a unit as a code name; null when unknown.</summary>
        private static string UnitCode(string part)
        {
            string k = Key(part);
            if (int.TryParse(ToAscii(k), out int n) && Enum.IsDefined(typeof(Entities.Model.NasbNiro.Engineering.EnUnit), n)) return ((Entities.Model.NasbNiro.Engineering.EnUnit)n).ToString();
            foreach (Entities.Model.NasbNiro.Engineering.EnUnit u in Enum.GetValues(typeof(Entities.Model.NasbNiro.Engineering.EnUnit)))
                if (Key(EnText.Unit(u)) == k || Key(u.ToString()) == k) return u.ToString();
            return null;
        }

        private static string ToAscii(string s)
        {
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s) b.Append(c >= '۰' && c <= '۹' ? (char)('0' + (c - '۰')) : c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠')) : c);
            return b.ToString();
        }

        private static bool? Flag(string s, bool blank)
        {
            string k = ToAscii(Key(s));
            if (k.Length == 0) return blank ? false : (bool?)null;
            if (k == "1" || k == "true" || k == "yes" || k == "بله") return true;
            if (k == "0" || k == "false" || k == "no" || k == "خیر") return false;
            return null;
        }

        /// <summary>Splits the text into records of cells (RFC 4180 quoting); each record carries the physical line it starts on.</summary>
        internal static List<(int Line, List<string> Cells)> Records(string text)
        {
            List<(int, List<string>)> result = new List<(int, List<string>)>();
            List<string> cells = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false, wasQuoted = false;
            int line = 1, start = 1;
            void EndRecord()
            {
                cells.Add(cell.ToString()); cell.Clear(); wasQuoted = false;
                if (!(cells.Count == 1 && cells[0].Trim().Length == 0)) result.Add((start, cells));
                cells = new List<string>();
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else { cell.Append(c); if (c == '\n') line++; }
                    continue;
                }
                if (c == '"' && cell.Length == 0 && !wasQuoted) { quoted = true; wasQuoted = true; }
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); wasQuoted = false; }
                else if (c == '\r') { }
                else if (c == '\n') { EndRecord(); line++; start = line; }
                else cell.Append(c);
            }
            if (quoted) { cells.Add(cell.ToString()); result.Add((start, cells)); result.Add((-1, new List<string>())); return result; }   // an unclosed quote is reported by the caller
            if (cell.Length > 0 || cells.Count > 0) EndRecord();
            return result;
        }

        /// <summary>The lines of the file, or the Persian refusal (row numbered, at most <see cref="MaxErrorsShown"/> shown).</summary>
        public static string Parse(byte[] bytes, out List<EnReferenceLineDto> lines)
        {
            lines = new List<EnReferenceLineDto>();
            if (bytes == null || bytes.Length == 0) return "فایل خالی است.";
            if (bytes.Length > MaxBytes) return "فایل بیش از ۵۰۰ کیلوبایت است.";
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (ArgumentException) { return "فایل باید با کدگذاری UTF-8 ذخیره شود (فایل CSV نمونه را ببینید)."; }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            List<(int Line, List<string> Cells)> records = Records(text);
            if (records.Count > 0 && records[records.Count - 1].Line == -1) return "ردیف " + EnText.Num(records[records.Count - 2].Line) + ": گیومهٔ باز بسته نشده است.";
            if (records.Count == 0) return "فایل خالی است.";
            List<string> head = records[0].Cells.Select(x => x.Trim()).ToList();
            if (head.Count != Header.Length || !head.Select((h, i) => string.Equals(h, Header[i], StringComparison.OrdinalIgnoreCase)).All(x => x))
                return "ردیف ۱ (سرستون): سرستون فایل باید دقیقاً «" + HeaderText + "» باشد؛ در فایل «" + string.Join(",", head) + "» دیده شد.";
            if (records.Count == 1) return "فایل هیچ ردیف مدرکی ندارد.";
            if (records.Count - 1 > 300) return "فایل بیش از ۳۰۰ ردیف دارد.";
            List<string> errors = new List<string>();
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach ((int line, List<string> c) in records.Skip(1))
            {
                string at = "ردیف " + EnText.Num(line) + ": ";
                if (c.Count != Header.Length) { errors.Add(at + "تعداد ستون‌ها باید ۶ باشد، " + EnText.Num(c.Count) + " ستون دیده شد (متنی که ویرگول دارد را داخل گیومه بگذارید)."); continue; }
                string type = c[0].Trim(), title = c[1].Trim(), discipline = c[2].Trim();
                int before = errors.Count;
                if (type.Length == 0) errors.Add(at + "نوع مدرک (docType) خالی است.");
                else if (type.Length > 100) errors.Add(at + "نوع مدرک بیش از ۱۰۰ نویسه است.");
                if (title.Length == 0) errors.Add(at + "عنوان (title) خالی است.");
                else if (title.Length > 300) errors.Add(at + "عنوان بیش از ۳۰۰ نویسه است.");
                if (discipline.Length > 100) errors.Add(at + "رشته بیش از ۱۰۰ نویسه است.");
                bool? required = Flag(c[3], false), tender = Flag(c[4], true);
                if (required == null) errors.Add(at + "مقدار ستون required باید «بله»، «خیر»، 1 یا 0 باشد (در فایل «" + c[3].Trim() + "»).");
                if (tender == null) errors.Add(at + "مقدار ستون tender باید «بله»، «خیر»، 1 یا 0 باشد (در فایل «" + c[4].Trim() + "»).");
                string units = null;
                if (c[5].Trim().Length > 0)
                {
                    List<string> codes = new List<string>(); bool bad = false;
                    foreach (string part in c[5].Split(new[] { ';', '؛' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        string code = UnitCode(part);
                        if (code == null) { errors.Add(at + "واحد «" + part + "» شناخته‌شده نیست (" + string.Join("، ", EnUnits.All.Select(x => EnText.Unit(x) + " = " + x)) + ")."); bad = true; break; }
                        codes.Add(code);
                    }
                    if (!bad)
                    {
                        units = EnUnits.Normalize(string.Join(",", codes), out string unitError);
                        if (unitError != null) errors.Add(at + unitError);
                    }
                }
                if (errors.Count == before)
                {
                    string key = EnDossierBuilder.TypeTitle(type, title);
                    if (seen.TryGetValue(key, out int first)) errors.Add(at + "این نوع و عنوان قبلاً در ردیف " + EnText.Num(first) + " آمده است.");
                    else { seen[key] = line; lines.Add(new EnReferenceLineDto { DocumentType = type, Discipline = discipline.Length == 0 ? null : discipline, Title = title, Required = required.Value, TenderStage = tender.Value, Units = units }); }
                }
            }
            if (errors.Count == 0) return null;
            lines = new List<EnReferenceLineDto>();
            string shown = string.Join("\n", errors.Take(MaxErrorsShown));
            return errors.Count > MaxErrorsShown ? shown + "\nو " + EnText.Num(errors.Count - MaxErrorsShown) + " خطای دیگر." : shown;
        }
    }
}
