using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The numbers the AI reads (tool ceo_overview and the analysis), taken from the real overview of the CEO dashboard (release D1 part A1): the same figures, statuses,
    /// changes and drill keys the screen shows, so an analysis and the screen never disagree.
    /// <list type="bullet">
    /// <item>One section gives all its figures in full (key, title, value, unit, status, change, the drill key with the parameters that make the drill list exactly the records the figure counts, note),
    /// the day of the figures and the totals of its charts.</item>
    /// <item>The whole company gives the first <see cref="HeadlinePerSection"/> figures of every section, each as key, title, value, unit, status and change only: all nine sections have to fit the
    /// 6000 characters of a tool result, because the shaper would otherwise cut every list to three items and drop sections. The eight headline figures of the screen come first in their sections
    /// (cash, receivablesOpen and payablesOverdue are the first three of the finance section and the others are first in theirs), so they are all present without a second list of them.</item>
    /// </list>
    /// </summary>
    public class CeoOverviewSectionNumbersProvider : ICeoSectionNumbersProvider
    {
        /// <summary>How many figures of a section the whole-company answer carries (the finance section has three headline figures).</summary>
        public const int HeadlinePerSection = 3;

        private readonly ICeoOverviewService _overview;
        private readonly IInventoryUserContext _user;

        public CeoOverviewSectionNumbersProvider(ICeoOverviewService overview, IInventoryUserContext user)
        {
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<object> GetAsync(string sectionKey, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) throw new AiToolForbiddenException();
            string section = DrillText.Clean(sectionKey)?.ToLowerInvariant();
            if (section != null && !CeoModules.Sections.Contains(section)) throw new AiToolFailureException(CeoText.UnknownSection);

            if (section != null)
            {
                // a section is taken from the cached overview of today when there is one and computed now otherwise, so the day of its figures is today either way
                CeoSectionDto s = AiToolShaper.Unwrap(await _overview.GetSectionAsync(section, ct));
                return Full(s, CeoText.Date(DateTime.Today));
            }

            CeoOverviewDto o = AiToolShaper.Unwrap(await _overview.GetOverviewAsync(false, ct));
            return new CeoOverviewNumbersDto { AsOfText = o.AsOfText, Sections = o.Sections.Select(Compact).ToList() };
        }

        private static CeoSectionNumbersDto Full(CeoSectionDto s, string asOfText) => new CeoSectionNumbersDto
        {
            Section = s.Key,
            Title = s.Title,
            AsOfText = asOfText,
            Note = s.Note,
            Items = s.Kpis.Select(Number).ToList(),
            Charts = s.Charts.Select(ChartNumbers).ToList(),
        };

        /// <summary>The first figures of a section, the headline ones first, each without note and drill key.</summary>
        private static CeoSectionNumbersDto Compact(CeoSectionDto s) => new CeoSectionNumbersDto
        {
            Section = s.Key,
            Title = s.Title,
            Note = s.Note,
            Items = s.Kpis.OrderBy(k => CeoKeys.Headline.Contains(k.Key) ? 0 : 1).Take(HeadlinePerSection).Select(Brief).ToList(),
        };

        /// <summary>One figure as the AI reads it: a hidden figure (payroll privacy) carries no value and no change.</summary>
        public static CeoNumberDto Number(CeoKpiDto k) => new CeoNumberDto
        {
            Key = k.Key,
            Title = k.Title,
            Value = k.Hidden ? null : k.Value,
            Unit = k.Unit,
            Status = k.Status,
            DeltaText = k.Hidden ? null : k.DeltaText,
            DrillKey = k.DrillKey,
            DrillParams = k.DrillParams,
            Note = k.Hidden ? null : k.Note,
            Hidden = k.Hidden ? true : (bool?)null,
        };

        /// <summary>The short form of a figure for the answer about the whole company.</summary>
        public static CeoNumberDto Brief(CeoKpiDto k) => new CeoNumberDto
        {
            Key = k.Key,
            Title = k.Title,
            Value = k.Hidden ? null : k.Value,
            Unit = k.Unit,
            Status = k.Status,
            DeltaText = k.Hidden ? null : k.DeltaText,
            Hidden = k.Hidden ? true : (bool?)null,
        };

        /// <summary>The totals of a chart; a masked chart has none, and neither has a chart whose values do not add up (percentages, a line of averages).</summary>
        public static CeoChartNumbersDto ChartNumbers(CeoChartDto c)
        {
            var dto = new CeoChartNumbersDto { Key = c.Key, Title = c.Title, Unit = c.Unit, DrillKey = c.DrillKey, DrillParams = c.DrillParams };
            if (c.Hidden)
            {
                dto.Hidden = true;
                return dto;
            }
            dto.Note = c.Note;
            if ((c.Unit == CeoUnit.Rial || c.Unit == CeoUnit.Count) && c.Kind != CeoKind.Line)
                dto.Totals = c.Series.Select(x => new CeoSeriesTotalDto { Name = x.Name, Total = x.Values.Sum() }).ToList();
            return dto;
        }
    }
}
