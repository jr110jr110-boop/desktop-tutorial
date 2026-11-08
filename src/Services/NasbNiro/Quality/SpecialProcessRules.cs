using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Quality
{
    // Package C: pure rules (no database). The services call them; the tests call them directly.

    /// <summary>Limits of a parameter (a row of the qualification, of the master or of a material specification) as the judge sees them.</summary>
    public sealed class JudgeLimit
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Required { get; set; } = true;
        public CertValueKind Kind { get; set; }
    }

    public sealed class JudgeValue
    {
        public string Name { get; set; }
        public decimal? Value { get; set; }
        public CertValueKind Kind { get; set; }
    }

    public sealed class JudgedRow
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public CertValueKind Kind { get; set; }
        public decimal Value { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Pass { get; set; }
    }

    public sealed class Judgement
    {
        public List<JudgedRow> Rows { get; } = new List<JudgedRow>();
        public List<string> Missing { get; } = new List<string>();
        public List<string> Failed { get; } = new List<string>();

        /// <summary>Values whose name matches no limit (they are kept but judge nothing).</summary>
        public List<string> Unknown { get; } = new List<string>();

        public bool Pass => Missing.Count == 0 && Failed.Count == 0;

        public string Text()
        {
            var parts = new List<string>();
            if (Failed.Count > 0) parts.Add("خارج از حد: " + string.Join("، ", Failed));
            if (Missing.Count > 0) parts.Add("مقدار ثبت نشده: " + string.Join("، ", Missing));
            return parts.Count == 0 ? "همه مقادیر در حد مجاز است." : string.Join("؛ ", parts) + ".";
        }
    }

    public static class SpecialProcessRules
    {
        public static string NameKey(string name) => CharacteristicRules.Key(name);

        /// <summary>The key of a material specification family: grade and standard, letters and digits only (so "EN 10025-2" and "EN10025 2" are the same).</summary>
        public static string SpecKey(string grade, string standard) => CharacteristicRules.Key(grade) + "|" + CharacteristicRules.Key(standard);

        /// <summary>A heat number compared as printed: trimmed, upper case, inner white space removed. Hyphens and zeros are significant (zero tolerance).</summary>
        public static string HeatKey(string heat)
        {
            if (string.IsNullOrWhiteSpace(heat)) return string.Empty;
            return new string(heat.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        /// <summary>A value is inside its limits when it is not below the minimum and not above the maximum; a limit that is not set does not restrict.</summary>
        public static bool Inside(decimal value, decimal? min, decimal? max) => (!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value);

        /// <summary>The server's verdict of the recorded values against the limits: a value outside is a failure; a required limit without a value is missing.</summary>
        public static Judgement Judge(IEnumerable<JudgeLimit> limits, IEnumerable<JudgeValue> values)
        {
            var result = new Judgement();
            List<JudgeValue> given = (values ?? Enumerable.Empty<JudgeValue>()).Where(v => v != null).ToList();
            var used = new HashSet<JudgeValue>();
            foreach (JudgeLimit l in (limits ?? Enumerable.Empty<JudgeLimit>()))
            {
                JudgeValue v = given.FirstOrDefault(x => x.Kind == l.Kind && NameKey(x.Name) == NameKey(l.Name));
                if (v == null || !v.Value.HasValue)
                {
                    if (l.Required) result.Missing.Add(l.Name);
                    continue;
                }
                used.Add(v);
                bool pass = Inside(v.Value.Value, l.Min, l.Max);
                result.Rows.Add(new JudgedRow { Name = l.Name, Unit = l.Unit, Kind = l.Kind, Value = v.Value.Value, Min = l.Min, Max = l.Max, Pass = pass });
                if (!pass) result.Failed.Add($"{l.Name} = {Num(v.Value.Value)}{(string.IsNullOrEmpty(l.Unit) ? "" : " " + l.Unit)} (حد {Range(l.Min, l.Max)})");
            }
            foreach (JudgeValue v in given.Where(x => !used.Contains(x)))
                if (v.Value.HasValue) result.Unknown.Add(v.Name);
            return result;
        }

        /// <summary>Fix round 1 (Low): the columns are decimal(18,4); a value from 10^14 on would overflow the column (a 500), so it is refused with a message first.</summary>
        public const decimal MaxNumber = 100000000000000m;

        public static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static string Range(decimal? min, decimal? max) =>
            min.HasValue && max.HasValue ? $"{Num(min.Value)} تا {Num(max.Value)}" : min.HasValue ? $"حداقل {Num(min.Value)}" : max.HasValue ? $"حداکثر {Num(max.Value)}" : "بدون حد";

        /// <summary>Days left until <paramref name="validTo"/> counted from <paramref name="today"/> (negative = expired).</summary>
        public static int DaysLeft(DateTime validTo, DateTime today) => (int)(validTo.Date - today.Date).TotalDays;

        /// <summary>True when the period covers the day.</summary>
        public static bool Covers(DateTime from, DateTime to, DateTime on) => from.Date <= on.Date && on.Date <= to.Date;

        /// <summary>The survey of a furnace: Null when a survey of the required kind was done on or before the day and is not due before it; otherwise the Persian reason.</summary>
        public static string SurveyProblem(SpQualEquipment machine, DateTime on, string machineTitle)
        {
            if (machine == null) return null;
            if (machine.SurveyKind == SpSurveyKind.None || !machine.SurveyDate.HasValue || !machine.SurveyDueDate.HasValue)
                return $"برای «{machineTitle}» بررسی یکنواختی دما (TUS) یا آزمون دقت سیستم (SAT) ثبت نشده است.";
            if (machine.SurveyDate.Value.Date > on.Date) return $"بررسی دمای «{machineTitle}» پس از تاریخ اجرا انجام شده است.";
            if (machine.SurveyDueDate.Value.Date < on.Date) return $"سررسید بررسی دمای «{machineTitle}» ({(global::Services.NasbNiro.Finance.FiscalCalendars.Text(machine.SurveyDueDate.Value))}) گذشته است.";
            return null;
        }

        /// <summary>The same person cannot both record and review a run; neither can the operator's own user.</summary>
        public static bool ReviewerAllowed(int reviewerUserId, int recorderUserId, int? operatorUserId) => reviewerUserId != recorderUserId && (!operatorUserId.HasValue || reviewerUserId != operatorUserId.Value);
    }

    /// <summary>The qualified range of a welder against the WPS (ASME IX): P-No and group, filler F-No, position, thickness range and diameter range. Process and continuity are checked by the existing rules.</summary>
    public static class WeldRangeRules
    {
        private static List<string> Tokens(string text) => QualityText.SplitList(text).Select(t => t.Trim().ToUpperInvariant()).Where(t => t.Length > 0).Distinct().ToList();

        private static void Subset(List<string> problems, string label, string wpsText, string welderText)
        {
            List<string> wps = Tokens(wpsText);
            if (wps.Count == 0) return;
            List<string> welder = Tokens(welderText);
            if (welder.Count == 0) { problems.Add($"{label}: WPS {string.Join("، ", wps)} دارد ولی در گواهی جوشکار ثبت نشده است"); return; }
            List<string> outside = wps.Where(t => !welder.Contains(t)).ToList();
            if (outside.Count > 0) problems.Add($"{label} {string.Join("، ", outside)} (WPS) خارج از دامنه جوشکار ({string.Join("، ", welder)}) است");
        }

        private static void Span(List<string> problems, string label, string unit, decimal? wpsMin, decimal? wpsMax, decimal? welderMin, decimal? welderMax)
        {
            if (!wpsMin.HasValue && !wpsMax.HasValue) return;
            decimal low = wpsMin ?? wpsMax.Value, high = wpsMax ?? wpsMin.Value;
            if (!welderMin.HasValue && !welderMax.HasValue) { problems.Add($"{label}: WPS {SpecialProcessRules.Range(wpsMin, wpsMax)} {unit} دارد ولی دامنه آن در گواهی جوشکار ثبت نشده است"); return; }
            if (welderMax.HasValue && high > welderMax.Value) problems.Add($"{label} WPS تا {SpecialProcessRules.Num(high)} {unit} از حداکثر جوشکار ({SpecialProcessRules.Num(welderMax.Value)}) بیشتر است");
            if (welderMin.HasValue && low < welderMin.Value) problems.Add($"{label} WPS از {SpecialProcessRules.Num(low)} {unit} از حداقل جوشکار ({SpecialProcessRules.Num(welderMin.Value)}) کمتر است");
        }

        /// <summary>The mismatches between the qualified range of a welder and the WPS, in Persian; empty = inside the range.</summary>
        public static List<string> Mismatches(Qualification q, Wps wps)
        {
            var problems = new List<string>();
            if (q == null || wps == null) return problems;
            Subset(problems, "P-No", wps.BasePNo, q.RangePNos);
            Subset(problems, "گروه", wps.BaseGroupNo, q.RangeGroupNos);
            Subset(problems, "F-No فیلر", wps.FillerFNo, q.RangeFNos);
            Subset(problems, "وضعیت جوشکاری", wps.Positions, q.RangePositions);
            Span(problems, "ضخامت", "mm", wps.ThicknessMin, wps.ThicknessMax, q.RangeThicknessMin, q.RangeThicknessMax);
            Span(problems, "قطر", "mm", wps.DiameterMin, wps.DiameterMax, q.RangeDiameterMin, q.RangeDiameterMax);
            return problems;
        }

        public static string Message(string who, string process, Wps wps, List<string> problems) =>
            $"دامنه صلاحیت «{who}» برای WPS {wps.Number}/{wps.Revision} ({process}) کافی نیست: " + string.Join("؛ ", problems) + ".";
    }

    /// <summary>The verdict on one EN 10204 certificate (pure).</summary>
    public sealed class CertificateEvaluation
    {
        public CertRowStatus Status { get; set; }
        public List<string> Problems { get; } = new List<string>();
        public Judgement Judgement { get; set; }
        public string Text => Problems.Count == 0 ? "گواهی با مشخصات ماده هم‌خوان است." : string.Join(" ", Problems);
    }

    public static class CertificateRules
    {
        public static bool NeedsValues(CertificateType type) => type == CertificateType.T31 || type == CertificateType.T32;

        public static string TypeText(CertificateType type) => type == CertificateType.T22 ? "2.2" : type == CertificateType.T31 ? "3.1" : "3.2";

        /// <summary>
        /// Everything the receipt gate asks of a certificate of type 3.1 or 3.2: the heat number equals the lot's, the grade is the one of an approved specification, every required value is present and inside the
        /// limits, a 3.2 names the inspector's organisation and the inspector (name or stamp), a 3.1 names the manufacturer's authorised signatory. A 2.2 certificate is not judged here.
        /// </summary>
        public static CertificateEvaluation Evaluate(CertificateType type, string heat, string lotHeat, string grade, string standard, MaterialSpec spec, IEnumerable<JudgeValue> values,
            string inspectorOrganisation, string inspectorName, string inspectorStamp, string manufacturerSignatory, IReadOnlyCollection<MaterialSpec> required = null)
        {
            var result = new CertificateEvaluation { Status = CertRowStatus.NotJudged };
            if (!NeedsValues(type)) return result;
            string t = TypeText(type);

            if (string.IsNullOrWhiteSpace(heat)) result.Problems.Add($"شماره ذوب روی گواهی {t} وارد نشده است.");
            if (string.IsNullOrWhiteSpace(lotHeat)) result.Problems.Add("بچ شماره ذوب ندارد؛ ردیابی ذوب با گواهی ممکن نیست.");
            else if (!string.IsNullOrWhiteSpace(heat) && SpecialProcessRules.HeatKey(heat) != SpecialProcessRules.HeatKey(lotHeat))
                result.Problems.Add($"شماره ذوب گواهی ({heat.Trim()}) با شماره ذوب بچ ({lotHeat.Trim()}) برابر نیست.");
            if (string.IsNullOrWhiteSpace(grade)) result.Problems.Add("گرید ماده روی گواهی وارد نشده است.");

            if (type == CertificateType.T32)
            {
                if (string.IsNullOrWhiteSpace(inspectorOrganisation)) result.Problems.Add("گواهی 3.2 باید سازمان بازرس (نهاد بازرسی) را داشته باشد.");
                if (string.IsNullOrWhiteSpace(inspectorName) && string.IsNullOrWhiteSpace(inspectorStamp)) result.Problems.Add("گواهی 3.2 باید نام یا شماره مهر بازرس را داشته باشد.");
            }
            else if (string.IsNullOrWhiteSpace(manufacturerSignatory)) result.Problems.Add("گواهی 3.1 باید نام نماینده مجاز سازنده (امضاکننده) را داشته باشد.");

            // Fix round 1 (H2): when the item (or its BOM line) requires specifications, the certificate must be of one of them: the same grade AND the same standard; another approved grade never passes
            if (required != null && required.Count > 0 && (spec == null || !required.Any(x => x.Id == spec.Id)))
            {
                string wanted = string.Join(" ؛ ", required.Select(x => x.Grade + (string.IsNullOrWhiteSpace(x.Standard) ? string.Empty : " / " + x.Standard)));
                string given = (grade ?? string.Empty).Trim() + (string.IsNullOrWhiteSpace(standard) ? string.Empty : " / " + standard.Trim());
                result.Problems.Add($"گرید/استاندارد گواهی ({(given.Length == 0 ? "نامشخص" : given)}) همان مشخصات ماده لازم این کالا نیست؛ گرید لازم: {wanted}.");
                result.Status = CertRowStatus.Fail;
                return result;
            }
            if (spec == null)
            {
                if (!string.IsNullOrWhiteSpace(grade)) result.Problems.Add($"برای گرید «{grade.Trim()}»{(string.IsNullOrWhiteSpace(standard) ? "" : " / " + standard.Trim())} مشخصات ماده تاییدشده‌ای نیست؛ مهندسی باید آن را تعریف و تایید کند.");
                result.Status = CertRowStatus.Fail;
                return result;
            }
            if (CharacteristicRules.Key(grade) != CharacteristicRules.Key(spec.Grade) || CharacteristicRules.Key(standard) != CharacteristicRules.Key(spec.Standard))
                result.Problems.Add($"گرید/استاندارد گواهی ({grade}{(string.IsNullOrWhiteSpace(standard) ? string.Empty : " / " + standard.Trim())}) با مشخصات ماده ({spec.Grade}{(string.IsNullOrWhiteSpace(spec.Standard) ? string.Empty : " / " + spec.Standard)}) یکی نیست.");

            var limits = spec.Limits.OrderBy(l => l.SortOrder).Select(l => new JudgeLimit { Name = l.Name, Unit = l.Unit, Min = l.Min, Max = l.Max, Required = l.Required, Kind = l.Kind }).ToList();
            Judgement j = SpecialProcessRules.Judge(limits, values);
            result.Judgement = j;
            if (j.Failed.Count > 0) result.Problems.Add("مقدار خارج از حد مشخصات: " + string.Join("، ", j.Failed) + ".");
            if (j.Missing.Count > 0) result.Problems.Add("مقدار الزامی ثبت نشده: " + string.Join("، ", j.Missing) + ".");
            result.Status = result.Problems.Count == 0 ? CertRowStatus.Pass : CertRowStatus.Fail;
            return result;
        }
    }
}
