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
    /// changes and drill keys the screen shows, so an analysis and the screen never disagree. One section gives all its figures; the whole company gives the headline
    /// and the first <see cref="HeadlinePerSection"/> figures of every section (the tool output is shaped to a few thousand characters).
    /// </summary>
    public class CeoOverviewSectionNumbersProvider : ICeoSectionNumbersProvider
    {
        /// <summary>How many figures of a section the whole-company answer carries.</summary>
        public const int HeadlinePerSection = 4;

        private readonly ICeoOverviewService _overview;
        private readonly IInventoryUserContext _user;

        public CeoOverviewSectionNumbersProvider(ICeoOverviewService overview, IInventoryUserContext user)
        {
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<object> GetAsync(string sectionKey, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoDrillRoles.Readers)) throw new AiToolForbiddenException();
            string section = DrillText.Clean(sectionKey)?.ToLowerInvariant();
            if (section != null && !CeoModules.Sections.Contains(section))
                throw new AiToolFailureException("بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections));

            if (section != null)
            {
                CeoSectionDto s = AiToolShaper.Unwrap(await _overview.GetSectionAsync(section, ct));
                return Build(s, null, int.MaxValue);
            }

            CeoOverviewDto o = AiToolShaper.Unwrap(await _overview.GetOverviewAsync(false, ct));
            return new CeoOverviewNumbersDto
            {
                AsOfText = o.AsOfText,
                Headline = o.Headline.Select(Number).ToList(),
                Sections = o.Sections.Select(s => Build(s, o.AsOfText, HeadlinePerSection)).ToList(),
            };
        }

        private static CeoSectionNumbersDto Build(CeoSectionDto s, string asOf, int take) => new CeoSectionNumbersDto
        {
            Section = s.Key,
            Title = s.Title,
            AsOfText = take == int.MaxValue ? asOf : null,
            Note = s.Note,
            Items = s.Kpis.Take(take).Select(Number).ToList(),
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
            Note = k.Hidden ? null : k.Note,
            Hidden = k.Hidden ? true : (bool?)null,
        };
    }
}
