using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo
{
    // CEO executive dashboard (release D1, part A1, voice mode 7.2): deterministic spoken summaries of the headline, one section or the alerts.
    // The sentences are a template filled with the figures of the overview; no language model is involved, so they work while the model server is down.

    /// <summary>What a spoken summary is about (the "what" of api/v1/ceo/speech).</summary>
    public static class CeoSpeechKinds
    {
        public const string Headline = "headline";
        public const string Section = "section";
        public const string Alerts = "alerts";

        /// <summary>The latest daily report of the CEO (a note of the CEO brief seat): the CEO only, it is written with the CEO's data.</summary>
        public const string Brief = "brief";
    }

    /// <summary>
    /// The sentence lists of the spoken summaries, in display form (Persian digits, thousands separators, percent signs, Shamsi dates).
    /// The service turns them into words with the speech normaliser of the AI module. Pure: the same figures always give the same sentences.
    /// </summary>
    public static class CeoSpeechScript
    {
        /// <summary>At most this many alerts are read out (the worst first); the rest are announced as a count.</summary>
        public const int MaxAlerts = 10;

        public const string PreviousMonth = "ماه پیش";

        private const string Stop = ". ";

        private static readonly Regex LatinNote = new Regex(@"\s*\([A-Za-z ]+\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The headline: the date, one sentence for each of the eight figures, then the sections that are in danger or need attention.</summary>
        public static string Headline(CeoOverviewDto overview)
        {
            var sentences = new List<string>
            {
                string.IsNullOrWhiteSpace(overview?.AsOfText) ? "خلاصه‌ی وضعیت شرکت" : "خلاصه‌ی وضعیت شرکت در " + overview.AsOfText,
            };
            if (overview?.Headline != null) sentences.AddRange(overview.Headline.Where(k => k != null).Select(Kpi));
            sentences.AddRange(PulseSentences(overview?.Pulse));
            return Join(sentences);
        }

        /// <summary>One section: its name, how many of its figures are in danger or need attention, then one sentence for each figure.</summary>
        public static string Section(CeoSectionDto section)
        {
            var sentences = new List<string> { "بخش " + section?.Title };
            if (section == null) return Join(sentences);
            if (!string.IsNullOrWhiteSpace(section.Note))
            {
                sentences.Add(section.Note);
                return Join(sentences);
            }
            int danger = section.Kpis.Count(k => k.Status == CeoStatus.Danger);
            int warn = section.Kpis.Count(k => k.Status == CeoStatus.Warn);
            var counts = new List<string>();
            if (danger > 0) counts.Add(CeoText.Number(danger) + " شاخص در وضعیت خطر");
            if (warn > 0) counts.Add(CeoText.Number(warn) + " شاخص نیازمند توجه");
            sentences.Add(counts.Count == 0 ? "هیچ شاخصی از این بخش هشدار ندارد" : EnumerationOf(counts) + " است");
            sentences.AddRange(section.Kpis.Where(k => k != null).Select(Kpi));
            return Join(sentences);
        }

        /// <summary>The alerts, worst first: how many there are, then the first <see cref="MaxAlerts"/> with their severity.</summary>
        public static string Alerts(IEnumerable<CeoAlertDto> alerts)
        {
            List<CeoAlertDto> list = (alerts ?? Enumerable.Empty<CeoAlertDto>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Text)).ToList();
            if (list.Count == 0) return Join(new[] { "هشداری برای امروز ثبت نشده است" });

            int danger = list.Count(a => a.Severity == "danger");
            int warning = list.Count(a => a.Severity == "warning");
            int info = list.Count - danger - warning;
            var counts = new List<string>();
            if (danger > 0) counts.Add(CeoText.Number(danger) + " مورد خطر");
            if (warning > 0) counts.Add(CeoText.Number(warning) + " مورد هشدار");
            if (info > 0) counts.Add(CeoText.Number(info) + " مورد اطلاع");

            var sentences = new List<string> { CeoText.Number(list.Count) + " هشدار برای امروز ثبت شده است؛ " + EnumerationOf(counts) };
            foreach (CeoAlertDto alert in list.Take(MaxAlerts)) sentences.Add(SeverityWord(alert.Severity) + "، " + alert.Text.Trim());
            if (list.Count > MaxAlerts) sentences.Add(CeoText.Number(list.Count - MaxAlerts) + " هشدار دیگر را در داشبورد ببینید");
            return Join(sentences);
        }

        /// <summary>
        /// One figure: its name and value with the unit ("نقد و بانک ۴۸٬۲۰۰٬۰۰۰٬۰۰۰ ریال"), the change against the previous month when it is known
        /// ("، ۲٬۹۰۰٬۰۰۰٬۰۰۰ بیشتر از ماه پیش") and a word when it is in danger or needs attention. A masked figure is said to be confidential.
        /// Rial values and changes are said with three significant digits (۴۸٬۲۳۴٬۵۶۷٬۸۹۰ is "forty-eight billion two hundred million"): nobody listens to eleven digits.
        /// </summary>
        public static string Kpi(CeoKpiDto kpi)
        {
            if (kpi == null) return string.Empty;
            string title = SpokenTitle(kpi.Title);
            if (kpi.Hidden) return title + " محرمانه است";
            if (!kpi.Value.HasValue) return title + " در دسترس نیست";

            string sentence = title + " " + ValuePhrase(kpi);
            if (kpi.Delta.HasValue) sentence += "، " + DeltaPhrase(kpi.Delta.Value, kpi.Unit);
            if (kpi.Status == CeoStatus.Danger) sentence += "؛ در وضعیت خطر";
            else if (kpi.Status == CeoStatus.Warn) sentence += "؛ نیازمند توجه";
            return sentence;
        }

        /// <summary>The title as it is said: a Latin note in brackets ("(backlog)", "(OEE)", "(H)") is left out, a speech engine would spell it or garble it.</summary>
        public static string SpokenTitle(string title) => LatinNote.Replace(title ?? string.Empty, string.Empty).Trim();

        /// <summary>
        /// The value with its unit. A rial value is rounded to three significant digits (see <see cref="SpokenRial"/>); the survey score is said with one decimal ("۴٫۳ از ۵");
        /// any other value is the display text of the KPI, with "مورد" after a plain count.
        /// </summary>
        public static string ValuePhrase(CeoKpiDto kpi)
        {
            if (kpi.Value.HasValue)
            {
                if (kpi.Key == CeoKeys.ServiceSurveyScore) return CeoText.Number(kpi.Value.Value, 1) + " از " + CeoText.Number(5);
                if (kpi.Unit == CeoUnit.Rial) return CeoText.Rial(SpokenRial(kpi.Value.Value));
            }
            string text = string.IsNullOrWhiteSpace(kpi.ValueText) ? CeoText.ValueText(kpi.Value, kpi.Unit) : kpi.ValueText;
            if (kpi.Unit == CeoUnit.Count && text == CeoText.ValueText(kpi.Value, CeoUnit.Count)) text += " مورد";
            return text;
        }

        /// <summary>
        /// A rial amount rounded to three significant digits, half away from zero: ۴۸٬۲۳۴٬۵۶۷٬۸۹۰ becomes ۴۸٬۲۰۰٬۰۰۰٬۰۰۰, ۹۵۰ stays ۹۵۰. Used for what is said aloud;
        /// the screen keeps every digit.
        /// </summary>
        public static decimal SpokenRial(decimal value, int digits = 3)
        {
            if (value == 0m || digits < 1) return 0m;
            decimal abs = Math.Abs(value);
            int integerDigits = decimal.Truncate(abs).ToString(CultureInfo.InvariantCulture).Length;
            int decimals = digits - integerDigits;
            decimal rounded;
            if (decimals >= 0)
            {
                rounded = Math.Round(abs, Math.Min(decimals, 6), MidpointRounding.AwayFromZero);
            }
            else
            {
                decimal scale = 1m;
                for (int i = 0; i < -decimals; i++) scale *= 10m;
                rounded = Math.Round(abs / scale, 0, MidpointRounding.AwayFromZero) * scale;
            }
            return value < 0m ? -rounded : rounded;
        }

        /// <summary>
        /// The change against the previous month in words, without a sign. A rial change has no unit word (the value just said it) and, like the value, three significant digits:
        /// "۲٬۹۰۰٬۰۰۰٬۰۰۰ بیشتر از ماه پیش"; the other units keep theirs: "۳ مورد کمتر از ماه پیش", "۲٫۵٪ بیشتر از ماه پیش".
        /// </summary>
        public static string DeltaPhrase(decimal delta, string unit)
        {
            decimal amount = Math.Abs(delta);
            int decimals = unit == CeoUnit.Percent || unit == CeoUnit.Hours ? 1 : unit == CeoUnit.Rial || unit == CeoUnit.Days || amount == decimal.Truncate(amount) ? 0 : 2;

            // a change that rounds to nothing in the unit it is said in is no change
            if (Math.Round(amount, decimals, MidpointRounding.AwayFromZero) == 0m) return "بدون تغییر نسبت به " + PreviousMonth;
            string text;
            switch (unit)
            {
                case CeoUnit.Rial: text = CeoText.Number(SpokenRial(amount)); break;
                case CeoUnit.Percent: text = CeoText.Percent(amount); break;
                case CeoUnit.Days: text = CeoText.Number(amount) + " روز"; break;
                case CeoUnit.Hours: text = CeoText.Number(amount, 1) + " ساعت"; break;
                default: text = CeoText.Number(amount, decimals) + " مورد"; break;
            }
            return text + (delta > 0 ? " بیشتر از " : " کمتر از ") + PreviousMonth;
        }

        /// <summary>The latest daily report of the CEO: its date, then the text as the brief seat wrote it.</summary>
        public static string Brief(AiBriefDto brief)
        {
            string text = (brief?.Text ?? string.Empty).Trim();
            string title = string.IsNullOrWhiteSpace(brief?.DateText) ? "گزارش روزانه‌ی مدیرعامل" : "گزارش روزانه‌ی مدیرعامل در " + brief.DateText.Trim();
            return Join(new[] { title, text });
        }

        private static IEnumerable<string> PulseSentences(IEnumerable<CeoPulseDto> pulse)
        {
            List<CeoPulseDto> lights = (pulse ?? Enumerable.Empty<CeoPulseDto>()).Where(p => p != null).ToList();
            if (lights.Count == 0) yield break;
            List<string> danger = lights.Where(p => p.Status == CeoStatus.Danger).Select(p => p.Title).ToList();
            List<string> warn = lights.Where(p => p.Status == CeoStatus.Warn).Select(p => p.Title).ToList();
            if (danger.Count == 0 && warn.Count == 0)
            {
                yield return "هیچ بخشی هشدار ندارد";
                yield break;
            }
            if (danger.Count > 0) yield return "بخش‌های در وضعیت خطر: " + EnumerationOf(danger);
            if (warn.Count > 0) yield return "بخش‌های نیازمند توجه: " + EnumerationOf(warn);
        }

        private static string SeverityWord(string severity) => severity == "danger" ? "خطر" : severity == "warning" ? "هشدار" : "اطلاع";

        /// <summary>"الف"، "الف و ب"، "الف، ب و ج".</summary>
        private static string EnumerationOf(IReadOnlyList<string> items)
        {
            if (items.Count == 0) return string.Empty;
            if (items.Count == 1) return items[0];
            return string.Join("، ", items.Take(items.Count - 1)) + " و " + items[items.Count - 1];
        }

        private static string Join(IEnumerable<string> sentences)
            => string.Join(Stop, sentences.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().TrimEnd('.'))) + ".";
    }

    public interface ICeoSpeechService
    {
        /// <param name="what">headline, section, alerts or brief (the CEO only).</param>
        /// <param name="sectionKey">The section to read (finance ... office); needed when <paramref name="what"/> is section.</param>
        Task<ResponseDto<CeoSpeechDto>> GetAsync(string what, string sectionKey, CancellationToken ct);
    }

    /// <summary>
    /// api/v1/ceo/speech: the sentences of <see cref="CeoSpeechScript"/> for the figures the overview shows, their numbers in words (the speech normaliser of the AI module),
    /// and the audio of the text-to-speech endpoint when one answers within Ceo:SpeechTimeoutSeconds (default 20). When none does the answer is still complete: no audio and degraded = true.
    /// CEO and PMO; the brief (what = brief) is for the CEO only. A section is read from the cached overview when there is one, so the voice says what the screen shows.
    /// </summary>
    public sealed class CeoSpeechService : ICeoSpeechService
    {
        private readonly IInventoryUserContext _user;
        private readonly ICeoOverviewService _overview;
        private readonly IAiNoteService _notes;
        private readonly IPersianNumberWords _words;
        private readonly ITextToSpeech _tts;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CeoSpeechService> _logger;

        public CeoSpeechService(IInventoryUserContext user, ICeoOverviewService overview, IAiNoteService notes, IPersianNumberWords words, ITextToSpeech tts, IConfiguration configuration, ILogger<CeoSpeechService> logger)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _overview = overview ?? throw new ArgumentNullException(nameof(overview));
            _notes = notes ?? throw new ArgumentNullException(nameof(notes));
            _words = words ?? throw new ArgumentNullException(nameof(words));
            _tts = tts ?? throw new ArgumentNullException(nameof(tts));
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Seconds the text-to-speech call may take (Ceo:SpeechTimeoutSeconds, default 20): a summary is short, and a slow endpoint must not make the CEO wait for a screen that already has the text.</summary>
        internal const int DefaultBudgetSeconds = 20;

        private int BudgetSeconds
        {
            get
            {
                string text = _configuration?[CeoConfigKeys.SpeechTimeoutSeconds];
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0 ? s : DefaultBudgetSeconds;
            }
        }

        public async Task<ResponseDto<CeoSpeechDto>> GetAsync(string what, string sectionKey, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoSpeechDto>(CeoText.NotAllowed);

            string script;
            switch ((what ?? string.Empty).Trim().ToLowerInvariant())
            {
                case CeoSpeechKinds.Headline:
                {
                    ResponseDto<CeoOverviewDto> answer = await _overview.GetOverviewAsync(false, ct);
                    if (answer == null || !answer.Success || answer.Data == null) return Failed(answer?.Message, answer?.HttpStatusCode);
                    script = CeoSpeechScript.Headline(answer.Data);
                    break;
                }
                case CeoSpeechKinds.Alerts:
                {
                    ResponseDto<CeoOverviewDto> answer = await _overview.GetOverviewAsync(false, ct);
                    if (answer == null || !answer.Success || answer.Data == null) return Failed(answer?.Message, answer?.HttpStatusCode);
                    script = CeoSpeechScript.Alerts(answer.Data.Alerts);
                    break;
                }
                case CeoSpeechKinds.Section:
                {
                    if (string.IsNullOrWhiteSpace(sectionKey)) return ResponseDtoHelper.BadRequest<CeoSpeechDto>(CeoText.SpeechNeedsSection);
                    ResponseDto<CeoSectionDto> answer = await _overview.GetSectionAsync(sectionKey, ct);
                    if (answer == null || !answer.Success || answer.Data == null) return Failed(answer?.Message, answer?.HttpStatusCode);
                    script = CeoSpeechScript.Section(answer.Data);
                    break;
                }
                case CeoSpeechKinds.Brief:
                {
                    // the brief is written with the data only the CEO sees: the PMO, who reads the rest, does not read this
                    if (!_user.IsInRole(AiNoteTypes.Roles(AiNoteTypes.CeoBrief))) return ResponseDtoHelper.BadRequest<CeoSpeechDto>(CeoText.NotAllowed);
                    ResponseDto<AiBriefDto> answer = await _notes.LatestBriefAsync(ct);
                    if (answer == null || !answer.Success || answer.Data == null) return Failed(answer?.Message, answer?.HttpStatusCode);
                    script = CeoSpeechScript.Brief(answer.Data);
                    break;
                }
                default:
                    return ResponseDtoHelper.BadRequest<CeoSpeechDto>(CeoText.UnknownSpeech);
            }

            var dto = new CeoSpeechDto { Text = _words.SpeechNormalize(script) };
            byte[] audio = await SynthesizeAsync(dto.Text, ct);
            dto.AudioBase64 = audio == null ? null : Convert.ToBase64String(audio);
            dto.Degraded = audio == null;
            return ResponseDtoHelper.Ok(dto);
        }

        /// <summary>The audio of the text, or null when no text-to-speech endpoint answered in time. Never throws because of the endpoint.</summary>
        private async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct)
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));
                TtsResult tts = await _tts.SynthesizeAsync(text, budget.Token);
                return tts != null && tts.Ok && tts.Audio != null && tts.Audio.Length > 0 ? tts.Audio : null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CEO speech: the text-to-speech call failed; the answer has the text only.");
                return null;
            }
        }

        private static ResponseDto<CeoSpeechDto> Failed(string message, System.Net.HttpStatusCode? status)
            => status == System.Net.HttpStatusCode.NotFound
                ? ResponseDtoHelper.NotFound<CeoSpeechDto>(message ?? CeoText.UnknownSection)
                : ResponseDtoHelper.BadRequest<CeoSpeechDto>(message ?? CeoText.NotAllowed);
    }
}
