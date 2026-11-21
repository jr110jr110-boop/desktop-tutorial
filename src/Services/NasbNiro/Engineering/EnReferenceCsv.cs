using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Engineering
{
    /// <summary>
    /// The CSV file of a reference list (the only way a human enters the lines in a test release: a file, not typing).
    /// UTF-8, header row docType,title,discipline,required,tender,units and optionally clientSubmittal, clause, note (after the six; a file without them keeps the stored values of a matched line).
    /// The separator is «,» or «;» (read from the header row: Excel in Persian or German locales saves «;»). Units are Persian or code names separated by «;» «،» «|» or «,» (quote the cell when it holds the file separator). A cell with the separator or a line break is quoted with ".
    /// Every refusal names the row of the file (the header is row 1).
    /// </summary>
    public static class EnReferenceCsv
    {
        public const int MaxBytes = 512 * 1024;
        public const int MaxErrorsShown = 10;
        public static readonly string[] Header = { "docType", "title", "discipline", "required", "tender", "units" };
        public static readonly string[] Optional = { "clientSubmittal", "clause", "note" };
        public static readonly string HeaderText = string.Join(",", Header);

        /// <summary>Which optional columns the file has (a column that is absent never overwrites the stored value of a matched line).</summary>
        public sealed class Columns { public bool ClientSubmittal; public bool Clause; public bool Note; }

        private static string Fa(int n) => new string(n.ToString().Select(c => c >= '0' && c <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());

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
        internal static List<(int Line, List<string> Cells)> Records(string text, char sep = ',')
        {
            List<(int, List<string>)> result = new List<(int, List<string>)>();
            List<string> cells = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false, wasQuoted = false;
            int line = 1, start = 1;
            void EndRecord()
            {
                cells.Add(cell.ToString()); cell.Clear(); wasQuoted = false;
                if (!cells.All(x => x.Trim().Length == 0)) result.Add((start, cells));   // an empty line and a line of only separators (Excel writes them below the data) are skipped
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
                else if (c == sep) { cells.Add(cell.ToString()); cell.Clear(); wasQuoted = false; }
                else if (c == '\r') { }
                else if (c == '\n') { EndRecord(); line++; start = line; }
                else cell.Append(c);
            }
            if (quoted) { cells.Add(cell.ToString()); result.Add((start, cells)); result.Add((-1, new List<string>())); return result; }   // an unclosed quote is reported by the caller
            if (cell.Length > 0 || cells.Count > 0) EndRecord();
            return result;
        }

        /// <summary>A generated file neutralises a cell that starts with = + - @ tab or CR by one leading apostrophe (a spreadsheet would run it as a formula); the reader strips that single apostrophe, so a downloaded file read back gives the original text.</summary>
        public static string Plain(string s) => s != null && s.Length >= 2 && s[0] == '\'' && ("=+-@\t\r".IndexOf(s[1]) >= 0 || (s[1] == '\'' && s.Length > 2 && "=+-@\t\r".IndexOf(s[2]) >= 0)) ? s.Substring(1) : s;

        /// <summary>The generator side of <see cref="Plain"/>.</summary>
        public static string Neutralise(string s) => !string.IsNullOrEmpty(s) && ("=+-@\t\r".IndexOf(s[0]) >= 0 || (s[0] == '\'' && s.Length > 1 && "=+-@\t\r".IndexOf(s[1]) >= 0)) ? "'" + s : s;

        /// <summary>The lines of the file, or the Persian refusal (row numbered, at most <see cref="MaxErrorsShown"/> shown).</summary>
        public static string Parse(byte[] bytes, out List<EnReferenceLineDto> lines, out Columns columns)
        {
            lines = new List<EnReferenceLineDto>(); columns = new Columns();
            if (bytes == null || bytes.Length == 0) return "فایل خالی است.";
            if (bytes.Length > MaxBytes) return "فایل بیش از ۵۰۰ کیلوبایت است.";
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (ArgumentException) { return "فایل باید با کدگذاری UTF-8 ذخیره شود (فایل CSV نمونه را ببینید)."; }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            string firstLine = text.Split('\n')[0];
            char sep = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
            List<(int Line, List<string> Cells)> records = Records(text, sep);
            if (records.Count > 0 && records[records.Count - 1].Line == -1) return "ردیف " + EnText.Num(records[records.Count - 2].Line) + ": گیومهٔ باز بسته نشده است.";
            if (records.Count == 0) return "فایل خالی است.";
            List<string> head = records[0].Cells.Select(x => x.Trim()).ToList();
            bool headOk = head.Count >= Header.Length && Enumerable.Range(0, Header.Length).All(i => string.Equals(head[i], Header[i], StringComparison.OrdinalIgnoreCase));
            int iClient = -1, iClause = -1, iNote = -1;
            for (int i = Header.Length; headOk && i < head.Count; i++)
            {
                if (string.Equals(head[i], Optional[0], StringComparison.OrdinalIgnoreCase) && iClient < 0) iClient = i;
                else if (string.Equals(head[i], Optional[1], StringComparison.OrdinalIgnoreCase) && iClause < 0) iClause = i;
                else if (string.Equals(head[i], Optional[2], StringComparison.OrdinalIgnoreCase) && iNote < 0) iNote = i;
                else headOk = false;
            }
            if (!headOk)
                return "ردیف ۱ (سرستون): سرستون فایل باید دقیقاً «" + HeaderText + "» باشد (ستون‌های اختیاری بعد از آن: " + string.Join("، ", Optional) + ")؛ در فایل «" + string.Join(",", head) + "» دیده شد.";
            columns = new Columns { ClientSubmittal = iClient >= 0, Clause = iClause >= 0, Note = iNote >= 0 };
            if (records.Count == 1) return "فایل هیچ ردیف مدرکی ندارد.";
            if (records.Count - 1 > 300) return "فایل بیش از ۳۰۰ ردیف دارد.";
            List<string> errors = new List<string>();
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach ((int line, List<string> c) in records.Skip(1))
            {
                string at = "ردیف " + EnText.Num(line) + ": ";
                if (c.Count != head.Count) { errors.Add(at + "تعداد ستون‌ها باید " + Fa(head.Count) + " باشد، " + Fa(c.Count) + " ستون دیده شد (متنی که جداکننده یا ویرگول دارد را داخل گیومه بگذارید)."); continue; }
                string type = Plain(c[0].Trim()), title = Plain(c[1].Trim()), discipline = Plain(c[2].Trim());
                int before = errors.Count;
                if (type.Length == 0) errors.Add(at + "نوع مدرک (docType) خالی است.");
                else if (type.Length > 100) errors.Add(at + "نوع مدرک بیش از ۱۰۰ نویسه است.");
                if (title.Length == 0) errors.Add(at + "عنوان (title) خالی است.");
                else if (title.Length > 300) errors.Add(at + "عنوان بیش از ۳۰۰ نویسه است.");
                if (discipline.Length > 100) errors.Add(at + "رشته بیش از ۱۰۰ نویسه است.");
                bool? required = Flag(c[3], false), tender = Flag(c[4], true);
                if (required == null) errors.Add(at + "مقدار ستون required باید «بله»، «خیر»، 1 یا 0 باشد (در فایل «" + c[3].Trim() + "»).");
                if (tender == null) errors.Add(at + "مقدار ستون tender باید «بله»، «خیر»، 1 یا 0 باشد (در فایل «" + c[4].Trim() + "»).");
                bool client = iClient >= 0 && Flag(c[iClient], false) == true;
                if (iClient >= 0 && Flag(c[iClient], false) == null) errors.Add(at + "مقدار ستون clientSubmittal باید «بله»، «خیر»، 1 یا 0 باشد (در فایل «" + c[iClient].Trim() + "»).");
                string clause = iClause >= 0 ? Plain(c[iClause].Trim()) : null, noteText = iNote >= 0 ? Plain(c[iNote].Trim()) : null;
                if (clause != null && clause.Length > 300) errors.Add(at + "بند استاندارد (clause) بیش از ۳۰۰ نویسه است.");
                if (noteText != null && noteText.Length > 500) errors.Add(at + "توضیح (note) بیش از ۵۰۰ نویسه است.");
                string units = null;
                if (c[5].Trim().Length > 0)
                {
                    List<string> codes = new List<string>(); bool bad = false;
                    foreach (string part in c[5].Split(new[] { ';', '؛', '،', '|', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
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
                    else { seen[key] = line; lines.Add(new EnReferenceLineDto { DocumentType = type, Discipline = discipline.Length == 0 ? null : discipline, Title = title, Required = required.Value, TenderStage = tender.Value, Units = units, ClientSubmittal = client, StandardClause = string.IsNullOrEmpty(clause) ? null : clause, Note = string.IsNullOrEmpty(noteText) ? null : noteText }); }
                }
            }
            if (errors.Count == 0) return null;
            lines = new List<EnReferenceLineDto>();
            string shown = string.Join("\n", errors.Take(MaxErrorsShown));
            return errors.Count > MaxErrorsShown ? shown + "\nو " + EnText.Num(errors.Count - MaxErrorsShown) + " خطای دیگر." : shown;
        }
    }
}
