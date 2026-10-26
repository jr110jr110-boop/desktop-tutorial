using System.Collections.Generic;
using System.Linq;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>
    /// Deep copies of the overview objects. The cache keeps its own copy and hands out a copy on every hit, so a caller that changes what it received
    /// (the analysis adds to a section, a test edits a figure) can never change what the next reader gets.
    /// </summary>
    public static class CeoCopy
    {
        public static CeoOverviewDto Of(CeoOverviewDto o) => o == null ? null : new CeoOverviewDto
        {
            AsOf = o.AsOf, AsOfText = o.AsOfText, GeneratedAt = o.GeneratedAt, GeneratedInMs = o.GeneratedInMs, Cached = o.Cached,
            Headline = o.Headline?.Select(Of).ToList() ?? new List<CeoKpiDto>(),
            Sections = o.Sections?.Select(Of).ToList() ?? new List<CeoSectionDto>(),
            Alerts = o.Alerts?.Select(Of).ToList() ?? new List<CeoAlertDto>(),
            Pulse = o.Pulse?.Select(Of).ToList() ?? new List<CeoPulseDto>(),
        };

        public static CeoSectionDto Of(CeoSectionDto s) => s == null ? null : new CeoSectionDto
        {
            Key = s.Key, Title = s.Title, AlertCount = s.AlertCount, Link = s.Link, AnalysisPrompt = s.AnalysisPrompt, Note = s.Note,
            Kpis = s.Kpis?.Select(Of).ToList() ?? new List<CeoKpiDto>(),
            Charts = s.Charts?.Select(Of).ToList() ?? new List<CeoChartDto>(),
        };

        public static CeoKpiDto Of(CeoKpiDto k) => k == null ? null : new CeoKpiDto
        {
            Key = k.Key, Title = k.Title, Value = k.Value, Unit = k.Unit, ValueText = k.ValueText, ShortText = k.ShortText, Delta = k.Delta, DeltaText = k.DeltaText, DeltaGood = k.DeltaGood,
            Trend = k.Trend == null ? null : new List<decimal>(k.Trend), TrendLabels = k.TrendLabels == null ? null : new List<string>(k.TrendLabels),
            Status = k.Status, Hidden = k.Hidden, Link = k.Link, DrillKey = k.DrillKey, DrillParams = Of(k.DrillParams), Note = k.Note,
        };

        public static CeoChartDto Of(CeoChartDto c) => c == null ? null : new CeoChartDto
        {
            Key = c.Key, Title = c.Title, Kind = c.Kind, Unit = c.Unit, Note = c.Note, Link = c.Link, DrillKey = c.DrillKey, DrillParams = Of(c.DrillParams), Hidden = c.Hidden,
            Labels = c.Labels == null ? new List<string>() : new List<string>(c.Labels),
            Series = c.Series?.Select(Of).ToList() ?? new List<CeoChartSeriesDto>(),
        };

        public static CeoChartSeriesDto Of(CeoChartSeriesDto s) => s == null ? null : new CeoChartSeriesDto
        {
            Name = s.Name, Role = s.Role, Values = s.Values == null ? new List<decimal>() : new List<decimal>(s.Values),
        };

        public static CeoAlertDto Of(CeoAlertDto a) => a == null ? null : new CeoAlertDto { Severity = a.Severity, Text = a.Text, Link = a.Link };

        public static CeoPulseDto Of(CeoPulseDto p) => p == null ? null : new CeoPulseDto
        {
            Key = p.Key, Title = p.Title, Status = p.Status, DangerCount = p.DangerCount, WarnCount = p.WarnCount, Link = p.Link,
        };

        public static Dictionary<string, string> Of(Dictionary<string, string> d) => d == null ? null : new Dictionary<string, string>(d);
    }
}
