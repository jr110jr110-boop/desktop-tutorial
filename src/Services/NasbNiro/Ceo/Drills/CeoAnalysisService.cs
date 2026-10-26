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
    /// The tool ceo_overview and the analysis both read them here. The registered implementation is <see cref="CeoOverviewSectionNumbersProvider"/> (the real overview of part A1);
    /// (the provisional mapping of the older CEO dashboard that part A2 shipped with was removed at integration).
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

        /// <summary>ok, warn, danger or neutral, as the screen shows it (null for the provisional provider).</summary>
        public string Status { get; set; }

        /// <summary>The change against the previous month in words, as the screen shows it.</summary>
        public string DeltaText { get; set; }

        /// <summary>The drill that lists the records behind the number (a key of the catalogue).</summary>
        public string DrillKey { get; set; }
        public string Note { get; set; }
        public bool? Hidden { get; set; }
    }

    public sealed class CeoSectionNumbersDto
    {
        public string Section { get; set; }
        public string Title { get; set; }
        public string AsOfText { get; set; }

        /// <summary>The section's own note (for instance that it could not be computed), when it has one.</summary>
        public string Note { get; set; }
        public List<CeoNumberDto> Items { get; set; } = new List<CeoNumberDto>();
    }

    public sealed class CeoOverviewNumbersDto
    {
        public string AsOfText { get; set; }

        /// <summary>The eight headline figures of the screen (null for the provisional provider).</summary>
        public List<CeoNumberDto> Headline { get; set; }
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
            if (!_user.IsInRole(CeoDrillRoles.Readers)) return ResponseDtoHelper.BadRequest<AiChatResponseDto>(CeoDrillText.NotAllowed);
            string section = DrillText.Clean(dto?.SectionKey)?.ToLowerInvariant();
            if (section == "all") section = null;
            if (section != null && !CeoModules.Sections.Contains(section))
                return ResponseDtoHelper.BadRequest<AiChatResponseDto>("بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections));

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
