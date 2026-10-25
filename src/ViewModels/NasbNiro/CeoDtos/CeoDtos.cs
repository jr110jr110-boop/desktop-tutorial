using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.CeoDtos
{
    // CEO executive dashboard (release D1, part A1): the overview, its nine sections and the snapshot action.
    // Money is in rials (decimal); the "...Text" members carry the Persian display form (Shamsi dates, Persian digits).

    /// <summary>The whole screen of the CEO: eight headline figures, nine sections, the alerts of the company and one pulse light per section.</summary>
    public class CeoOverviewDto
    {
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>Milliseconds the figures took to compute; for a cached answer the time of the first computation.</summary>
        public int GeneratedInMs { get; set; }

        /// <summary>True when the answer came from the 120-second cache.</summary>
        public bool Cached { get; set; }

        /// <summary>Exactly eight, in this order: cash, receivablesOpen, payablesOverdue, salesBacklog, activeProjects, lateProductionOrders, openNcrs, serviceSlaBreaches.</summary>
        public List<CeoKpiDto> Headline { get; set; } = new List<CeoKpiDto>();

        /// <summary>Nine, in this order: finance, sales, projects, production, quality, inventory, procurement, service, office.</summary>
        public List<CeoSectionDto> Sections { get; set; } = new List<CeoSectionDto>();

        /// <summary>The alerts of the existing CEO dashboard (severity danger, warning or info).</summary>
        public List<CeoAlertDto> Alerts { get; set; } = new List<CeoAlertDto>();

        /// <summary>One light per section: danger when any KPI of the section is danger, warn when any is warn, else ok.</summary>
        public List<CeoPulseDto> Pulse { get; set; } = new List<CeoPulseDto>();
    }

    /// <summary>One figure: its value, how it moved, a small trend and where to look deeper.</summary>
    public class CeoKpiDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>Null when the figure is hidden (payroll privacy) or has no value yet.</summary>
        public decimal? Value { get; set; }

        /// <summary>rial, count, percent, days or hours.</summary>
        public string Unit { get; set; }

        public string ValueText { get; set; }

        /// <summary>Large rial amounts in words: "۴۸٫۲ میلیارد ریال". Null for other units.</summary>
        public string ShortText { get; set; }

        /// <summary>Change against the previous month, same unit; null when the previous month is unknown.</summary>
        public decimal? Delta { get; set; }

        public string DeltaText { get; set; }

        /// <summary>True when the change is favourable, false when not, null when there is no change or no opinion.</summary>
        public bool? DeltaGood { get; set; }

        /// <summary>Up to twelve monthly points, oldest first; null when no point exists.</summary>
        public List<decimal> Trend { get; set; }

        /// <summary>One label per trend point: the Persian month and year ("مهر ۱۴۰۵").</summary>
        public List<string> TrendLabels { get; set; }

        /// <summary>ok, warn, danger or neutral.</summary>
        public string Status { get; set; } = CeoStatus.Neutral;

        /// <summary>True when the value is masked: the reader may not see payroll-based figures.</summary>
        public bool Hidden { get; set; }

        /// <summary>The front route that shows the records behind the figure.</summary>
        public string Link { get; set; }

        /// <summary>A key of the drill catalogue (api/v1/ceo/drill).</summary>
        public string DrillKey { get; set; }

        public string Note { get; set; }
    }

    public class CeoSectionDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public List<CeoKpiDto> Kpis { get; set; } = new List<CeoKpiDto>();
        public List<CeoChartDto> Charts { get; set; } = new List<CeoChartDto>();

        /// <summary>KPIs of the section whose status is warn or danger.</summary>
        public int AlertCount { get; set; }

        public string Link { get; set; }

        /// <summary>The Persian question the "تحلیل" button of the section sends to the analyst.</summary>
        public string AnalysisPrompt { get; set; }

        /// <summary>Empty normally; a Persian sentence when the section could not be computed now (it then has no KPIs and no charts, the other sections are not affected).</summary>
        public string Note { get; set; }
    }

    public class CeoChartDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>line, bar, stackedBar, hbar or donut.</summary>
        public string Kind { get; set; }

        public string Unit { get; set; }
        public List<string> Labels { get; set; } = new List<string>();
        public List<CeoChartSeriesDto> Series { get; set; } = new List<CeoChartSeriesDto>();
        public string Note { get; set; }
        public string Link { get; set; }
        public string DrillKey { get; set; }

        /// <summary>True when the chart is masked for this reader (labels and series are then empty).</summary>
        public bool Hidden { get; set; }
    }

    public class CeoChartSeriesDto
    {
        public string Name { get; set; }
        public List<decimal> Values { get; set; } = new List<decimal>();

        /// <summary>primary, secondary, positive, negative, neutral or accent.</summary>
        public string Role { get; set; } = CeoRole.Primary;
    }

    public class CeoAlertDto
    {
        /// <summary>danger, warning or info.</summary>
        public string Severity { get; set; }

        public string Text { get; set; }
        public string Link { get; set; }
    }

    public class CeoPulseDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>ok, warn, danger or neutral.</summary>
        public string Status { get; set; } = CeoStatus.Neutral;

        public int DangerCount { get; set; }
        public int WarnCount { get; set; }
        public string Link { get; set; }
    }

    /// <summary>Result of the "snapshot now" action: how many figures were stored for today.</summary>
    public class CeoSnapshotResultDto
    {
        /// <summary>Today in Shamsi, for example "1405/08/03".</summary>
        public string DateText { get; set; }

        public int Keys { get; set; }
    }

    /// <summary>
    /// A spoken summary for the voice mode (api/v1/ceo/speech): a Persian sentence list built from the figures (no language model is involved) and,
    /// when a text-to-speech endpoint answers, its audio.
    /// </summary>
    public class CeoSpeechDto
    {
        /// <summary>The sentences exactly as they are spoken: numbers, dates and percent signs are already words ("چهل و هشت میلیارد و دویست میلیون ریال").</summary>
        public string Text { get; set; }

        /// <summary>The audio (WAV) of <see cref="Text"/> as base64; null when no speech endpoint answered (the front then uses the speech of the browser, or only shows the text).</summary>
        public string AudioBase64 { get; set; }

        /// <summary>True when there is no audio; the text is complete and correct anyway.</summary>
        public bool Degraded { get; set; }
    }

    /// <summary>The status words of a KPI or a pulse light (used by the overview and its tests).</summary>
    public static class CeoStatus
    {
        public const string Ok = "ok";
        public const string Warn = "warn";
        public const string Danger = "danger";
        public const string Neutral = "neutral";
    }

    /// <summary>The roles a chart series may play.</summary>
    public static class CeoRole
    {
        public const string Primary = "primary";
        public const string Secondary = "secondary";
        public const string Positive = "positive";
        public const string Negative = "negative";
        public const string Neutral = "neutral";
        public const string Accent = "accent";
    }
}
