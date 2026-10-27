using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The numbers of one section of the CEO dashboard (or the headline numbers of all sections) as one object for the AI: Persian titles, values, units and the drill key of each number.
    /// The tool ceo_overview and the analysis both read them here. The registered implementation is <see cref="CeoOverviewSectionNumbersProvider"/> (the real overview of part A1).
    /// </summary>
    public interface ICeoSectionNumbersProvider
    {
        /// <param name="sectionKey">One of <see cref="CeoModules.Sections"/>; null or empty for the headline numbers of all sections.</param>
        Task<object> GetAsync(string sectionKey, CancellationToken ct);
    }

    /// <summary>One number of a section.</summary>
    public sealed class CeoNumberDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>Empty when the number is hidden for the caller's role (payroll privacy) or does not exist yet.</summary>
        public decimal? Value { get; set; }

        /// <summary>rial, count, percent, days, hours or score.</summary>
        public string Unit { get; set; }

        /// <summary>ok, warn, danger or neutral, as the screen shows it.</summary>
        public string Status { get; set; }

        /// <summary>The change against the previous month in words, as the screen shows it.</summary>
        public string DeltaText { get; set; }

        /// <summary>The drill that lists the records behind the number (a key of the catalogue). Only in the answer for one section.</summary>
        public string DrillKey { get; set; }

        /// <summary>The parameters to give <see cref="DrillKey"/> so that the drill lists exactly the records the number counts (for example the status of the purchase requests). Only in the answer for one section.</summary>
        public Dictionary<string, string> DrillParams { get; set; }

        /// <summary>Only in the answer for one section.</summary>
        public string Note { get; set; }
        public bool? Hidden { get; set; }
    }

    /// <summary>The totals of one chart of a section (the answer for one section only).</summary>
    public sealed class CeoChartNumbersDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Unit { get; set; }

        /// <summary>
        /// The sum of every series over the whole chart (all its months or all its groups) in the unit of the chart. Given only for a chart whose values add up (rial or count, not a line);
        /// the sum of percentages or of monthly averages means nothing, so such a chart carries no totals.
        /// </summary>
        public List<CeoSeriesTotalDto> Totals { get; set; }
        public string DrillKey { get; set; }
        public Dictionary<string, string> DrillParams { get; set; }
        public string Note { get; set; }

        /// <summary>True when the chart is masked for the caller (payroll privacy): it carries no totals.</summary>
        public bool? Hidden { get; set; }
    }

    public sealed class CeoSeriesTotalDto
    {
        public string Name { get; set; }
        public decimal Total { get; set; }
    }

    public sealed class CeoSectionNumbersDto
    {
        public string Section { get; set; }
        public string Title { get; set; }

        /// <summary>The day the figures are for, in Shamsi (the answer for one section only; the answer for the company has it once on the object).</summary>
        public string AsOfText { get; set; }

        /// <summary>The section's own note (for instance that it could not be computed), when it has one.</summary>
        public string Note { get; set; }
        public List<CeoNumberDto> Items { get; set; } = new List<CeoNumberDto>();

        /// <summary>The totals of the charts of the section (the answer for one section only).</summary>
        public List<CeoChartNumbersDto> Charts { get; set; }
    }

    /// <summary>The answer for the whole company: the first figures of every section (the headline figures of the screen come first in their sections), kept short enough for the 6000 characters of a tool result.</summary>
    public sealed class CeoOverviewNumbersDto
    {
        public string AsOfText { get; set; }
        public List<CeoSectionNumbersDto> Sections { get; set; } = new List<CeoSectionNumbersDto>();
    }


    public interface ICeoAnalysisService
    {
        /// <summary>The narrative analysis of a section (or of the company) with verified numbers (CEO and PMO).</summary>
        Task<ResponseDto<AiChatResponseDto>> AnalyzeAsync(CeoAnalyzeDto dto, CancellationToken ct);
    }

    /// <summary>
    /// POST api/v1/ceo/analyze: the existing chat loop as the signed-in user with the prompt ANALYSIS. The numbers of the section (the tool ceo_overview) are read before the first model
    /// turn and put into the conversation as a tool result, so the verifier of the chat accepts exactly those numbers (and those of the drills the model asks for).
    /// </summary>
    public class CeoAnalysisService : ICeoAnalysisService
    {
        private readonly IInventoryUserContext _user;
        private readonly IAiAnalysisRunner _runner;

        public CeoAnalysisService(IInventoryUserContext user, IAiAnalysisRunner runner)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>The question that is asked when the CEO presses the analysis button without a question.</summary>
        public static string DefaultQuestion(string section) => section == null
            ? "وضعیت کل شرکت را تحلیل کن؛ با دوره‌ی قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌های اصلی را بگو."
            : "وضعیت بخش «" + CeoModules.Title(section) + "» را تحلیل کن؛ با دوره‌ی قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌های اصلی را بگو.";

        public async Task<ResponseDto<AiChatResponseDto>> AnalyzeAsync(CeoAnalyzeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<AiChatResponseDto>(CeoText.NotAllowed);
            string section = DrillText.Clean(dto?.SectionKey)?.ToLowerInvariant();
            if (section == "all") section = null;
            if (section != null && !CeoModules.Sections.Contains(section)) return ResponseDtoHelper.BadRequest<AiChatResponseDto>(CeoText.UnknownSection);

            var request = new AiAnalysisRequest
            {
                SectionKey = section,
                Question = DrillText.Clean(dto?.Question) ?? DefaultQuestion(section),
                ConversationId = dto?.ConversationId,
            };
            request.PreCalls.Add(new AiPreCall { Tool = AiCeoTools.OverviewTool, ArgumentsJson = section == null ? "{}" : "{\"section\":\"" + section + "\"}" });
            return await _runner.RunAnalysisAsync(request, ct);
        }
    }
}
