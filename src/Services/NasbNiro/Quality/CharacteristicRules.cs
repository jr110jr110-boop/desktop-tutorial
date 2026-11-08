using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Quality
{
    /// <summary>A characteristic of a released list, free of the database (the rules below work on it).</summary>
    public sealed class CharLine
    {
        public int Id { get; set; }
        public int ListId { get; set; }
        public string No { get; set; }
        public string Name { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public string Unit { get; set; }
        public CharClass Class { get; set; }
        public EquipmentType InstrumentClass { get; set; }
        public string Method { get; set; }
        public int SamplePercent { get; set; }
        public int? OperationSeq { get; set; }
        public string StageCode2 { get; set; }
    }

    /// <summary>One value an inspector entered for a characteristic (after the server has matched it to the list).</summary>
    public sealed class CharValue
    {
        public int CharacteristicId { get; set; }
        public decimal Measured { get; set; }
    }

    /// <summary>
    /// Package B (H5): the pure rules of the characteristic lists. The inspector never types a nominal or a tolerance: they come from the released list of the drawing revision, the server decides pass or fail,
    /// and an acceptance needs every required characteristic (critical, major, or sampled) measured on enough pieces and inside its tolerance.
    /// </summary>
    public static class CharacteristicRules
    {
        public static bool Pass(decimal nominal, decimal tolPlus, decimal tolMinus, decimal measured) => measured >= nominal - tolMinus && measured <= nominal + tolPlus;

        public static bool Pass(CharLine line, decimal measured) => Pass(line.Nominal, line.TolPlus, line.TolMinus, measured);

        /// <summary>Critical and major characteristics are always required; a minor one is required when it is sampled (percent above zero).</summary>
        public static bool IsRequired(CharClass cls, int samplePercent) => cls == CharClass.Critical || cls == CharClass.Major || samplePercent > 0;

        public static bool IsRequired(CharLine line) => IsRequired(line.Class, line.SamplePercent);

        /// <summary>How many measurements a characteristic needs for <paramref name="quantity"/> pieces: the sample percentage of the pieces, rounded up, at least one; zero for a characteristic that is not required.</summary>
        public static int RequiredCount(CharClass cls, int samplePercent, decimal quantity)
        {
            if (!IsRequired(cls, samplePercent)) return 0;
            int percent = samplePercent <= 0 ? 100 : Math.Min(100, samplePercent);
            decimal pieces = quantity < 1m ? 1m : Math.Ceiling(quantity);
            int n = (int)Math.Ceiling(pieces * percent / 100m);
            return n < 1 ? 1 : n;
        }

        public static int RequiredCount(CharLine line, decimal quantity) => RequiredCount(line.Class, line.SamplePercent, quantity);

        public static string ClassText(CharClass c) => c == CharClass.Critical ? "بحرانی" : c == CharClass.Major ? "عمده" : "جزئی";

        public static string InstrumentText(EquipmentType t)
        {
            switch (t)
            {
                case EquipmentType.DimensionalHandTool: return "ابزار دستی ابعادی";
                case EquipmentType.Gauge: return "گیج";
                case EquipmentType.PressureGauge: return "فشارسنج";
                case EquipmentType.TemperatureInstrument: return "دماسنج";
                case EquipmentType.ElectricalMeter: return "ابزار الکتریکی";
                case EquipmentType.NdtEquipment: return "تجهیز NDT";
                case EquipmentType.TestStandInstrument: return "ابزار میز آزمون";
                default: return "هر نوع";
            }
        }

        /// <summary>Null when the instrument may measure the characteristic; "Other" in the list means any class.</summary>
        public static string InstrumentProblem(CharLine line, EquipmentType actual, string equipmentCode)
        {
            if (line.InstrumentClass == EquipmentType.Other || line.InstrumentClass == actual) return null;
            return $"مشخصه «{line.No} {line.Name}» باید با {InstrumentText(line.InstrumentClass)} اندازه‌گیری شود؛ ابزار {equipmentCode} از نوع {InstrumentText(actual)} است.";
        }

        /// <summary>Persian and Arabic digits to ASCII, upper case, letters and digits only: "Ø 50" and "ø50" are the same key.</summary>
        public static string Key(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.Trim())
            {
                char d = c;
                if (c >= '۰' && c <= '۹') d = (char)('0' + (c - '۰'));
                else if (c >= '٠' && c <= '٩') d = (char)('0' + (c - '٠'));
                if (char.IsLetterOrDigit(d)) sb.Append(char.ToUpperInvariant(d));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The line a typed characteristic means: by id when given, else by the number on the drawing, else by the name (exact after normalising). Null when it is not in the list.
        /// </summary>
        public static CharLine Find(IReadOnlyList<CharLine> lines, int? id, string text)
        {
            if (id.HasValue && id.Value > 0) return lines.FirstOrDefault(l => l.Id == id.Value);
            string k = Key(text);
            if (k.Length == 0) return null;
            CharLine byNo = lines.FirstOrDefault(l => Key(l.No) == k);
            if (byNo != null) return byNo;
            List<CharLine> byName = lines.Where(l => Key(l.Name) == k).ToList();
            return byName.Count == 1 ? byName[0] : null;
        }

        /// <summary>
        /// The required characteristics without enough measurements (their text, with the count that is still missing), in the order of the list. <paramref name="everyLine"/> (a first article):
        /// every characteristic of the list needs at least one measurement, whatever its class.
        /// </summary>
        public static List<string> Missing(IReadOnlyList<CharLine> lines, IReadOnlyList<CharValue> values, decimal quantity, bool everyLine)
        {
            var missing = new List<string>();
            foreach (CharLine l in lines)
            {
                int need = everyLine ? 1 : RequiredCount(l, quantity);
                if (need == 0) continue;
                int have = values.Count(v => v.CharacteristicId == l.Id);
                if (have < need) missing.Add($"{l.No} {l.Name} ({ClassText(l.Class)}: {have} از {need})");
            }
            return missing;
        }

        /// <summary>The characteristics that were measured outside their tolerance.</summary>
        public static List<string> Failed(IReadOnlyList<CharLine> lines, IReadOnlyList<CharValue> values)
        {
            var failed = new List<string>();
            foreach (CharValue v in values)
            {
                CharLine l = lines.FirstOrDefault(x => x.Id == v.CharacteristicId);
                if (l != null && !Pass(l, v.Measured)) failed.Add($"{l.No} {l.Name}: {v.Measured.ToString("0.####", CultureInfo.InvariantCulture)}");
            }
            return failed;
        }

        /// <summary>Which lines a record of this kind must cover. An in-process inspection at a routing step covers the lines of that step (the lines without a step when the step has none); every other record covers the whole list.</summary>
        public static List<CharLine> Scope(IReadOnlyList<CharLine> lines, QcRecordType type, int? operationSeq)
        {
            if (type == QcRecordType.InProcess && operationSeq.HasValue)
            {
                List<CharLine> atStep = lines.Where(l => l.OperationSeq == operationSeq.Value).ToList();
                if (atStep.Count > 0) return atStep;
                return lines.Where(l => !l.OperationSeq.HasValue).ToList();
            }
            return lines.ToList();
        }

        /// <summary>The checks on one line of a list being written. Null when it is acceptable, otherwise the Persian reason. The minimum sampling of a critical / major characteristic is the company setting.</summary>
        public static string LineProblem(string no, string name, decimal tolPlus, decimal tolMinus, CharClass cls, int samplePercent, int criticalMin, int majorMin, string rowText)
        {
            if (string.IsNullOrWhiteSpace(no)) return rowText + "شماره مشخصه (بالن نقشه) را وارد کنید.";
            if (no.Trim().Length > 30) return rowText + "شماره مشخصه بیش از ۳۰ نویسه است.";
            if (string.IsNullOrWhiteSpace(name)) return rowText + "نام مشخصه را وارد کنید.";
            if (name.Trim().Length > 200) return rowText + "نام مشخصه بیش از ۲۰۰ نویسه است.";
            if (tolPlus < 0 || tolMinus < 0) return rowText + "تلرانس‌ها را به صورت مقدار مثبت (+ و −) وارد کنید.";
            if (!Enum.IsDefined(typeof(CharClass), cls)) return rowText + "رده مشخصه (بحرانی، عمده، جزئی) نامعتبر است.";
            if (samplePercent < 0 || samplePercent > 100) return rowText + "درصد نمونه‌برداری باید بین ۰ و ۱۰۰ باشد.";
            if (cls == CharClass.Critical && samplePercent < criticalMin) return rowText + $"مشخصه بحرانی با کمتر از {criticalMin} درصد اندازه‌گیری نمی‌شود.";
            if (cls == CharClass.Major && samplePercent < majorMin) return rowText + $"مشخصه عمده با کمتر از {majorMin} درصد اندازه‌گیری نمی‌شود.";
            return null;
        }

        /// <summary>SHA-256 of the list content (the order of the rows is part of it).</summary>
        public static string Hash(IEnumerable<CharLine> lines)
        {
            var sb = new StringBuilder();
            foreach (CharLine l in lines)
                sb.Append(l.No).Append('|').Append(l.Name).Append('|').Append(Num(l.Nominal)).Append('|').Append(Num(l.TolPlus)).Append('|').Append(Num(l.TolMinus)).Append('|').Append(l.Unit)
                    .Append('|').Append((int)l.Class).Append('|').Append((int)l.InstrumentClass).Append('|').Append(l.Method).Append('|').Append(l.SamplePercent).Append('|')
                    .Append(l.OperationSeq?.ToString(CultureInfo.InvariantCulture)).Append('|').Append(l.StageCode2).Append('\n');
            return Sha256Hex(sb.ToString());
        }

        public static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    /// <summary>Pure rules of the first article inspection.</summary>
    public static class FaiRules
    {
        /// <summary>
        /// The configuration a first article covers: the in-force drawing revisions of the part (ids, sorted), the approved routing revision of the order and the BOM header of the project baseline.
        /// Any new released revision changes the text and so needs a new first article.
        /// </summary>
        public static string Fingerprint(IEnumerable<int> drawingRevisionIds, int? routingRevisionId, int? bomHeaderId)
        {
            string d = string.Join(",", (drawingRevisionIds ?? Enumerable.Empty<int>()).Distinct().OrderBy(x => x));
            return $"d:{d}|r:{routingRevisionId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|b:{bomHeaderId?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
        }

        /// <summary>The persons: the one who measured, the QC person who accepts and the QA person who approves must be three different people. Null when allowed.</summary>
        public static string SeparationProblem(int? measuredBy, int? acceptedBy, int me, bool isAccept)
        {
            if (isAccept)
            {
                if (measuredBy.HasValue && measuredBy.Value == me) return "پذیرش نمونه اول باید توسط بازرس QC دیگری غیر از اندازه‌گیرنده انجام شود (تفکیک وظایف).";
                return null;
            }
            if (measuredBy.HasValue && measuredBy.Value == me) return "تایید نمونه اول توسط QA نمی‌تواند با کسی باشد که اندازه‌گیری کرده است (تفکیک وظایف).";
            if (acceptedBy.HasValue && acceptedBy.Value == me) return "تایید QA نمی‌تواند با همان بازرسی باشد که نمونه اول را پذیرفته است (دو نفر متفاوت).";
            return null;
        }
    }
}
