using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Ceo;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.CeoDtos;

namespace MsTests
{
    /// <summary>
    /// The spoken summaries of the CEO dashboard (voice mode, contract 7.2): the sentence lists are fixed templates filled with the figures, their numbers are turned into words
    /// by the speech normaliser of the AI module, and the audio comes from the text-to-speech endpoint when one answers. No language model is involved.
    /// </summary>
    [TestClass]
    public class CeoSpeechTests
    {
        private static readonly PersianNumberWords Words = new PersianNumberWords();

        private static string Spoken(string script) => Words.SpeechNormalize(script);

        // ==================================================================================== fakes and builders

        private sealed class FakeUser : IInventoryUserContext
        {
            private readonly string[] _roles;

            public FakeUser(string roles) => _roles = roles.Split(',');

            public int UserId => 7;

            public bool IsInRole(string role) => !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _roles.Contains(r.Trim()));
        }

        private sealed class FakeOverview : ICeoOverviewService
        {
            public CeoOverviewDto Overview { get; set; }
            public ResponseDto<CeoOverviewDto> OverviewFailure { get; set; }
            public Dictionary<string, CeoSectionDto> Sections { get; } = new Dictionary<string, CeoSectionDto>();
            public List<string> Calls { get; } = new List<string>();

            public Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct)
            {
                Calls.Add("overview:" + refresh);
                return Task.FromResult(OverviewFailure ?? ResponseDtoHelper.Ok(Overview));
            }

            public Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct)
            {
                Calls.Add("section:" + key);
                string wanted = (key ?? string.Empty).Trim().ToLowerInvariant();
                return Task.FromResult(Sections.TryGetValue(wanted, out CeoSectionDto section) ? ResponseDtoHelper.Ok(section) : ResponseDtoHelper.NotFound<CeoSectionDto>(CeoText.UnknownSection));
            }
        }

        private sealed class FakeTts : ITextToSpeech
        {
            public Func<string, CancellationToken, Task<TtsResult>> Handler { get; set; } = (text, ct) => Task.FromResult(new TtsResult { Ok = true, Audio = new byte[] { 1, 2, 3 }, Model = "fake" });
            public List<string> Texts { get; } = new List<string>();

            public Task<TtsResult> SynthesizeAsync(string text, CancellationToken ct)
            {
                Texts.Add(text);
                return Handler(text, ct);
            }
        }

        private static CeoKpiDto Kpi(string key, string title, decimal? value, string unit, string status = CeoStatus.Ok, decimal? delta = null, bool hidden = false, string valueText = null)
        {
            var kpi = new CeoKpiDto { Key = key, Title = title, Value = value, Unit = unit, Status = status, Hidden = hidden };
            kpi.ValueText = valueText ?? (hidden ? CeoText.HiddenPayroll : CeoText.ValueText(value, unit));
            if (delta.HasValue)
            {
                kpi.Delta = delta;
                kpi.DeltaText = CeoText.DeltaText(delta.Value, unit);
            }
            return kpi;
        }

        private static CeoPulseDto Light(string key, string title, string status) => new CeoPulseDto { Key = key, Title = title, Status = status };

        private static CeoOverviewDto SampleOverview(params CeoPulseDto[] pulse)
        {
            var overview = new CeoOverviewDto { AsOf = new DateTime(2026, 10, 25), AsOfText = "1405/08/03" };
            overview.Headline.Add(Kpi("cash", "نقد و بانک", 48_200_000_000m, CeoUnit.Rial, delta: 2_900_000_000m));
            overview.Headline.Add(Kpi("receivablesOpen", "مطالبات باز", 30_000_000_000m, CeoUnit.Rial, CeoStatus.Danger));
            overview.Headline.Add(Kpi("payablesOverdue", "بدهی سررسیدگذشته", 0m, CeoUnit.Rial));
            overview.Headline.Add(Kpi("salesBacklog", "سفارش‌های در جریان", 75_000_000_000m, CeoUnit.Rial));
            overview.Headline.Add(Kpi("activeProjects", "پروژه‌های فعال", 9m, CeoUnit.Count));
            overview.Headline.Add(Kpi("lateProductionOrders", "دستورهای تولید دارای تأخیر", 7m, CeoUnit.Count, CeoStatus.Danger, delta: -3m));
            overview.Headline.Add(Kpi("openNcrs", "عدم انطباق‌های باز", 4m, CeoUnit.Count));
            overview.Headline.Add(Kpi("serviceSlaBreaches", "درخواست‌های خارج از SLA", 2m, CeoUnit.Count, CeoStatus.Warn));
            overview.Pulse.AddRange(pulse);
            return overview;
        }

        private static CeoSectionDto SampleSection(string key, string title, params CeoKpiDto[] kpis)
        {
            var section = new CeoSectionDto { Key = key, Title = title };
            section.Kpis.AddRange(kpis);
            return section;
        }

        private static CeoSpeechService Service(FakeOverview overview, FakeTts tts, string roles = "CEO", IConfiguration configuration = null)
            => new CeoSpeechService(new FakeUser(roles), overview, Words, tts, configuration, null);

        private static readonly Regex AnyDigit = new Regex(@"[0-9٠-٩۰-۹]");

        // ==================================================================================== the sentences

        [TestMethod]
        public void The_Example_Of_The_Contract_Is_Spoken_Exactly_The_Cash_And_Its_Change_In_Words()
        {
            string spoken = Spoken(CeoSpeechScript.Kpi(Kpi("cash", "نقد و بانک", 48_200_000_000m, CeoUnit.Rial, delta: 2_900_000_000m)));

            Assert.AreEqual("نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال، دو میلیارد و نهصد میلیون بیشتر از ماه پیش", spoken);
        }

        [TestMethod]
        public void Every_Unit_Says_Its_Unit_A_Fall_Says_Less_And_No_Change_Says_So()
        {
            Assert.AreEqual("پروژه‌های فعال دوازده مورد", Spoken(CeoSpeechScript.Kpi(Kpi("a", "پروژه‌های فعال", 12m, CeoUnit.Count))), "a plain count says what it counts");
            Assert.AreEqual("بهره‌وری بیست و پنج ممیز پنج درصد", Spoken(CeoSpeechScript.Kpi(Kpi("o", "بهره‌وری", 25.5m, CeoUnit.Percent))));
            Assert.AreEqual("تأخیر دوازده روز", Spoken(CeoSpeechScript.Kpi(Kpi("d", "تأخیر", 12m, CeoUnit.Days))));
            Assert.AreEqual("کار یک ممیز پنج ساعت", Spoken(CeoSpeechScript.Kpi(Kpi("h", "کار", 1.5m, CeoUnit.Hours))));
            Assert.AreEqual("نقد و بانک منفی پنج میلیون ریال", Spoken(CeoSpeechScript.Kpi(Kpi("c", "نقد و بانک", -5_000_000m, CeoUnit.Rial))), "a negative amount");
            Assert.AreEqual("میانگین رضایت چهار ممیز دو پنج از پنج", Spoken(CeoSpeechScript.Kpi(Kpi("s", "میانگین رضایت", 4.25m, CeoUnit.Count, valueText: "۴٫۲۵ از ۵"))), "a figure with a text of its own is not followed by «مورد»");

            Assert.AreEqual("الف سه مورد، سه مورد کمتر از ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 3m, CeoUnit.Count, delta: -3m))));
            Assert.AreEqual("الف سه مورد، بدون تغییر نسبت به ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 3m, CeoUnit.Count, delta: 0m))));
            Assert.AreEqual("الف سه میلیون ریال، بدون تغییر نسبت به ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 3_000_000m, CeoUnit.Rial, delta: 0.4m))), "less than a rial is no change");
            Assert.AreEqual("الف ده درصد، بدون تغییر نسبت به ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 10m, CeoUnit.Percent, delta: -0.04m))), "less than a tenth of a percent is no change");
            Assert.AreEqual("الف ده درصد، دو ممیز پنج درصد بیشتر از ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 10m, CeoUnit.Percent, delta: 2.5m))));
            Assert.AreEqual("الف ده روز، چهار روز بیشتر از ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 10m, CeoUnit.Days, delta: 4m))));
            Assert.AreEqual("الف سه میلیون ریال، یک میلیون و پانصد هزار کمتر از ماه پیش", Spoken(CeoSpeechScript.Kpi(Kpi("k", "الف", 3_000_000m, CeoUnit.Rial, delta: -1_500_000m))));
        }

        [TestMethod]
        public void A_Figure_In_Danger_Or_Needing_Attention_Says_So_A_Hidden_Or_Missing_One_Does_Not_Show_A_Number()
        {
            Assert.AreEqual("مطالبات باز سی میلیارد ریال؛ در وضعیت خطر", Spoken(CeoSpeechScript.Kpi(Kpi("r", "مطالبات باز", 30_000_000_000m, CeoUnit.Rial, CeoStatus.Danger))));
            Assert.AreEqual("درخواست‌ها دو مورد؛ نیازمند توجه", Spoken(CeoSpeechScript.Kpi(Kpi("r", "درخواست‌ها", 2m, CeoUnit.Count, CeoStatus.Warn))));
            Assert.AreEqual("درخواست‌ها دو مورد", Spoken(CeoSpeechScript.Kpi(Kpi("r", "درخواست‌ها", 2m, CeoUnit.Count, CeoStatus.Neutral))), "no rule, no word");
            Assert.AreEqual("هزینه‌ی گارانتی محرمانه است", Spoken(CeoSpeechScript.Kpi(Kpi("w", "هزینه‌ی گارانتی", null, CeoUnit.Rial, hidden: true))), "payroll privacy: the figure is never said");
            Assert.AreEqual("رضایت در دسترس نیست", Spoken(CeoSpeechScript.Kpi(Kpi("w", "رضایت", null, CeoUnit.Count))));
        }

        [TestMethod]
        public void A_Latin_Note_In_Brackets_Is_Left_Out_Of_The_Spoken_Title()
        {
            Assert.AreEqual("سفارش‌های در جریان", CeoSpeechScript.SpokenTitle("سفارش‌های در جریان (backlog)"));
            Assert.AreEqual("بهره‌وری کلی تجهیزات", CeoSpeechScript.SpokenTitle("بهره‌وری کلی تجهیزات (OEE)"));
            Assert.AreEqual("نقاط توقف منتظر رهاسازی", CeoSpeechScript.SpokenTitle("نقاط توقف (H) منتظر رهاسازی"));
            Assert.AreEqual("درخواست‌های خارج از SLA", CeoSpeechScript.SpokenTitle("درخواست‌های خارج از SLA"), "a Latin word outside brackets is kept");
            Assert.AreEqual("جریمه (تأخیر) دو", CeoSpeechScript.SpokenTitle("جریمه (تأخیر) دو"), "Persian words in brackets are kept");
            Assert.AreEqual(string.Empty, CeoSpeechScript.SpokenTitle(null));
            Assert.AreEqual("سفارش‌های در جریان صد میلیون ریال", Spoken(CeoSpeechScript.Kpi(Kpi("b", "سفارش‌های در جریان (backlog)", 100_000_000m, CeoUnit.Rial))));
        }

        [TestMethod]
        public void The_Headline_Names_The_Day_Reads_The_Eight_Figures_And_Points_To_The_Sections_In_Danger()
        {
            CeoOverviewDto overview = SampleOverview(Light("finance", "مالی", CeoStatus.Danger), Light("sales", "فروش", CeoStatus.Ok), Light("production", "تولید", CeoStatus.Danger),
                Light("quality", "کیفیت", CeoStatus.Warn), Light("service", "خدمات پس از فروش", CeoStatus.Warn), Light("office", "دفتر", CeoStatus.Warn));

            string[] sentences = Spoken(CeoSpeechScript.Headline(overview)).TrimEnd('.').Split(new[] { ". " }, StringSplitOptions.None);

            CollectionAssert.AreEqual(new[]
            {
                "خلاصه‌ی وضعیت شرکت در روز سوم آبان سال هزار و چهارصد و پنج",
                "نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال، دو میلیارد و نهصد میلیون بیشتر از ماه پیش",
                "مطالبات باز سی میلیارد ریال؛ در وضعیت خطر",
                "بدهی سررسیدگذشته صفر ریال",
                "سفارش‌های در جریان هفتاد و پنج میلیارد ریال",
                "پروژه‌های فعال نه مورد",
                "دستورهای تولید دارای تأخیر هفت مورد، سه مورد کمتر از ماه پیش؛ در وضعیت خطر",
                "عدم انطباق‌های باز چهار مورد",
                "درخواست‌های خارج از SLA دو مورد؛ نیازمند توجه",
                "بخش‌های در وضعیت خطر: مالی و تولید",
                "بخش‌های نیازمند توجه: کیفیت، خدمات پس از فروش و دفتر",
            }, sentences);
        }

        [TestMethod]
        public void The_Headline_Of_A_Calm_Company_Says_No_Section_Has_A_Warning()
        {
            CeoOverviewDto overview = SampleOverview(Light("finance", "مالی", CeoStatus.Ok), Light("sales", "فروش", CeoStatus.Neutral));

            string spoken = Spoken(CeoSpeechScript.Headline(overview));

            Assert.IsTrue(spoken.EndsWith("هیچ بخشی هشدار ندارد."), spoken);
            Assert.IsFalse(spoken.Contains("بخش‌های در وضعیت خطر"));
        }

        [TestMethod]
        public void A_Section_Counts_Its_Figures_In_Danger_And_Needing_Attention_Then_Reads_Each_One()
        {
            CeoSectionDto section = SampleSection("quality", "کیفیت",
                Kpi("openNcrs", "عدم انطباق‌های باز", 16m, CeoUnit.Count, CeoStatus.Danger), Kpi("overdueNcrs", "عدم انطباق‌های معوق", 3m, CeoUnit.Count, CeoStatus.Danger),
                Kpi("overdueCapas", "اقدام‌های اصلاحی معوق", 1m, CeoUnit.Count, CeoStatus.Warn), Kpi("calibrationOverdue", "کالیبراسیون معوق", 0m, CeoUnit.Count));

            string[] sentences = Spoken(CeoSpeechScript.Section(section)).TrimEnd('.').Split(new[] { ". " }, StringSplitOptions.None);

            CollectionAssert.AreEqual(new[]
            {
                "بخش کیفیت",
                "دو شاخص در وضعیت خطر و یک شاخص نیازمند توجه است",
                "عدم انطباق‌های باز شانزده مورد؛ در وضعیت خطر",
                "عدم انطباق‌های معوق سه مورد؛ در وضعیت خطر",
                "اقدام‌های اصلاحی معوق یک مورد؛ نیازمند توجه",
                "کالیبراسیون معوق صفر مورد",
            }, sentences);

            string calm = Spoken(CeoSpeechScript.Section(SampleSection("office", "دفتر", Kpi("lettersWaiting", "نامه‌ها", 2m, CeoUnit.Count))));
            Assert.AreEqual("بخش دفتر. هیچ شاخصی از این بخش هشدار ندارد. نامه‌ها دو مورد.", calm);
            string onlyWarn = Spoken(CeoSpeechScript.Section(SampleSection("office", "دفتر", Kpi("a", "الف", 6m, CeoUnit.Count, CeoStatus.Warn))));
            Assert.IsTrue(onlyWarn.Contains("یک شاخص نیازمند توجه است") && !onlyWarn.Contains("در وضعیت خطر"), onlyWarn);
        }

        [TestMethod]
        public void A_Section_That_Could_Not_Be_Computed_Is_Announced_With_Its_Note_Only()
        {
            var failed = new CeoSectionDto { Key = "finance", Title = "مالی", Note = "اطلاعات این بخش اکنون در دسترس نیست؛ بخش‌های دیگر بدون تغییر نمایش داده می‌شوند." };

            Assert.AreEqual("بخش مالی. اطلاعات این بخش اکنون در دسترس نیست؛ بخش‌های دیگر بدون تغییر نمایش داده می‌شوند.", Spoken(CeoSpeechScript.Section(failed)));
        }

        [TestMethod]
        public void The_Alerts_Are_Counted_By_Severity_Read_Worst_First_And_Cut_After_Ten()
        {
            var alerts = new List<CeoAlertDto>();
            for (int i = 1; i <= 3; i++) alerts.Add(new CeoAlertDto { Severity = "danger", Text = $"خطر شماره {i}" });
            for (int i = 1; i <= 4; i++) alerts.Add(new CeoAlertDto { Severity = "warning", Text = $"هشدار شماره {i}" });
            for (int i = 1; i <= 5; i++) alerts.Add(new CeoAlertDto { Severity = "info", Text = $"اطلاع شماره {i}" });

            string[] sentences = Spoken(CeoSpeechScript.Alerts(alerts)).TrimEnd('.').Split(new[] { ". " }, StringSplitOptions.None);

            Assert.AreEqual("دوازده هشدار برای امروز ثبت شده است؛ سه مورد خطر، چهار مورد هشدار و پنج مورد اطلاع", sentences[0]);
            CollectionAssert.AreEqual(new[] { "خطر، خطر شماره یک", "خطر، خطر شماره دو", "خطر، خطر شماره سه", "هشدار، هشدار شماره یک", "هشدار، هشدار شماره چهار" },
                new[] { sentences[1], sentences[2], sentences[3], sentences[4], sentences[7] });
            Assert.AreEqual("اطلاع، اطلاع شماره سه", sentences[10], "the tenth alert is the last one read");
            Assert.AreEqual("دو هشدار دیگر را در داشبورد ببینید", sentences[11]);
            Assert.AreEqual(12, sentences.Length);
        }

        [TestMethod]
        public void No_Alert_Is_Said_In_One_Short_Sentence()
        {
            Assert.AreEqual("هشداری برای امروز ثبت نشده است.", Spoken(CeoSpeechScript.Alerts(new List<CeoAlertDto>())));
            Assert.AreEqual("هشداری برای امروز ثبت نشده است.", Spoken(CeoSpeechScript.Alerts(null)));
            Assert.AreEqual("هشداری برای امروز ثبت نشده است.", Spoken(CeoSpeechScript.Alerts(new[] { new CeoAlertDto { Severity = "danger", Text = "  " } })), "an alert without a text is not an alert");
        }

        [TestMethod]
        public void Nothing_Is_Left_As_A_Digit_In_The_Spoken_Text()
        {
            // the texts of the CEO dashboard come with Latin digits, codes, percent signs and a Shamsi date
            var alerts = new[]
            {
                new CeoAlertDto { Severity = "danger", Text = "پروژه PRJ-1405-001: هزینه واقعی و تعهدشده 105٪ مبلغ قرارداد" },
                new CeoAlertDto { Severity = "warning", Text = "بار 2 هفته مرکز کار در چهار هفته آینده بیش از ۱۱۰٪ ظرفیت است" },
                new CeoAlertDto { Severity = "info", Text = "ارزیابی دوره‌ای 3 تامین‌کننده تا 1405/08/10 سررسید می‌شود" },
            };
            CeoOverviewDto overview = SampleOverview(Light("finance", "مالی", CeoStatus.Warn));
            overview.Headline.Add(Kpi("oee", "بهره‌وری", 61.25m, CeoUnit.Percent, delta: -1.5m));
            CeoSectionDto section = SampleSection("service", "خدمات", Kpi("sv", "میانگین", 4.5m, CeoUnit.Count, valueText: "۴٫۵ از ۵"), Kpi("h", "ساعت", 1234.5m, CeoUnit.Hours, delta: 10.5m));

            foreach (string script in new[] { CeoSpeechScript.Headline(overview), CeoSpeechScript.Section(section), CeoSpeechScript.Alerts(alerts) })
            {
                string spoken = Spoken(script);
                Assert.IsFalse(AnyDigit.IsMatch(spoken), "a digit is left in: " + spoken);
            }
            Assert.IsTrue(Spoken(CeoSpeechScript.Alerts(alerts)).Contains("صد و پنج درصد"), "105٪ is read as a percentage");
        }

        [TestMethod]
        public void The_Same_Figures_Always_Give_The_Same_Sentences()
        {
            CeoOverviewDto overview = SampleOverview(Light("finance", "مالی", CeoStatus.Danger));

            Assert.AreEqual(Spoken(CeoSpeechScript.Headline(overview)), Spoken(CeoSpeechScript.Headline(overview)));
            Assert.AreEqual(CeoSpeechScript.Headline(overview), CeoSpeechScript.Headline(overview));
        }

        // ==================================================================================== the service

        [TestMethod]
        public async Task The_Headline_Comes_With_The_Audio_Of_Exactly_The_Text_That_Is_Returned()
        {
            var overview = new FakeOverview { Overview = SampleOverview(Light("finance", "مالی", CeoStatus.Danger)) };
            var tts = new FakeTts { Handler = (text, ct) => Task.FromResult(new TtsResult { Ok = true, Audio = new byte[3244], Model = "mock" }) };

            ResponseDto<CeoSpeechDto> answer = await Service(overview, tts).GetAsync("headline", null, CancellationToken.None);

            Assert.IsTrue(answer.Success, answer.Message);
            Assert.IsFalse(answer.Data.Degraded);
            Assert.AreEqual(4328, answer.Data.AudioBase64.Length, "3244 bytes are 4328 base64 characters");
            CollectionAssert.AreEqual(new byte[3244], Convert.FromBase64String(answer.Data.AudioBase64));
            Assert.AreEqual(1, tts.Texts.Count);
            Assert.AreEqual(answer.Data.Text, tts.Texts[0], "the speech endpoint gets the returned text, numbers already in words");
            Assert.IsFalse(AnyDigit.IsMatch(answer.Data.Text));
            Assert.IsTrue(answer.Data.Text.Contains("نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال"));
            CollectionAssert.AreEqual(new[] { "overview:False" }, overview.Calls, "the headline is read from the cached overview, so it is what the screen shows");
        }

        [TestMethod]
        public async Task Without_A_Speech_Endpoint_The_Answer_Is_The_Complete_Text_Without_Audio()
        {
            var overview = new FakeOverview { Overview = SampleOverview(Light("finance", "مالی", CeoStatus.Danger)) };
            string expected = null;
            var handlers = new Dictionary<string, Func<string, CancellationToken, Task<TtsResult>>>
            {
                ["no endpoint answered"] = (text, ct) => Task.FromResult(new TtsResult { Ok = false }),
                ["no result at all"] = (text, ct) => Task.FromResult<TtsResult>(null),
                ["empty audio"] = (text, ct) => Task.FromResult(new TtsResult { Ok = true, Audio = new byte[0] }),
                ["no audio bytes"] = (text, ct) => Task.FromResult(new TtsResult { Ok = true, Audio = null }),
                ["the call failed"] = (text, ct) => throw new HttpRequestException("connection refused"),
                ["the call was refused"] = (text, ct) => throw new InvalidOperationException("no endpoint"),
            };
            foreach (var pair in handlers)
            {
                var tts = new FakeTts { Handler = pair.Value };

                ResponseDto<CeoSpeechDto> answer = await Service(overview, tts).GetAsync("headline", null, CancellationToken.None);

                Assert.IsTrue(answer.Success, pair.Key + ": " + answer.Message);
                Assert.IsNull(answer.Data.AudioBase64, pair.Key);
                Assert.IsTrue(answer.Data.Degraded, pair.Key);
                Assert.IsTrue(answer.Data.Text.Contains("نقد و بانک چهل و هشت میلیارد و دویست میلیون ریال"), pair.Key);
                expected ??= answer.Data.Text;
                Assert.AreEqual(expected, answer.Data.Text, pair.Key + ": the text does not depend on the audio");
            }
        }

        [TestMethod]
        public async Task A_Speech_Endpoint_That_Never_Answers_Is_Cut_Off_By_The_Voice_Budget()
        {
            var overview = new FakeOverview { Overview = SampleOverview() };
            var tts = new FakeTts { Handler = async (text, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; } };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { [AiConfigKeys.VoiceTimeoutSeconds] = "1" }).Build();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ResponseDto<CeoSpeechDto> answer = await Service(overview, tts, configuration: configuration).GetAsync("headline", null, CancellationToken.None);
            watch.Stop();

            Assert.IsTrue(answer.Success, answer.Message);
            Assert.IsTrue(answer.Data.Degraded);
            Assert.IsNull(answer.Data.AudioBase64);
            Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Data.Text));
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(30), "the budget of one second ended the wait (" + watch.Elapsed + ")");
        }

        [TestMethod]
        public async Task A_Caller_Who_Gives_Up_Gets_The_Cancellation_Not_A_Degraded_Answer()
        {
            var overview = new FakeOverview { Overview = SampleOverview() };
            var tts = new FakeTts { Handler = (text, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(new TtsResult { Ok = true, Audio = new byte[10] }); } };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool cancelled = false;
            try { await Service(overview, tts).GetAsync("headline", null, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }

            Assert.IsTrue(cancelled);
        }

        [TestMethod]
        public async Task A_Section_Is_Spoken_From_A_Fresh_Read_Of_That_Section_Only()
        {
            var overview = new FakeOverview { Overview = SampleOverview() };
            overview.Sections["quality"] = SampleSection("quality", "کیفیت", Kpi("openNcrs", "عدم انطباق‌های باز", 16m, CeoUnit.Count, CeoStatus.Danger));
            var tts = new FakeTts();

            ResponseDto<CeoSpeechDto> answer = await Service(overview, tts).GetAsync(" Section ", "Quality", CancellationToken.None);

            Assert.IsTrue(answer.Success, answer.Message);
            Assert.AreEqual("بخش کیفیت. یک شاخص در وضعیت خطر است. عدم انطباق‌های باز شانزده مورد؛ در وضعیت خطر.", answer.Data.Text);
            CollectionAssert.AreEqual(new[] { "section:Quality" }, overview.Calls, "the section is read on its own, the overview is not computed");
            Assert.IsFalse(answer.Data.Degraded);
            Assert.AreEqual(Convert.ToBase64String(new byte[] { 1, 2, 3 }), answer.Data.AudioBase64);
        }

        [TestMethod]
        public async Task The_Alerts_Are_Read_From_The_Cached_Overview()
        {
            var overview = new FakeOverview { Overview = SampleOverview() };
            overview.Overview.Alerts.Add(new CeoAlertDto { Severity = "danger", Text = "2 ماشین خراب است" });
            overview.Overview.Alerts.Add(new CeoAlertDto { Severity = "info", Text = "4 گزارش عدم انطباق باز است" });

            ResponseDto<CeoSpeechDto> answer = await Service(overview, new FakeTts()).GetAsync("alerts", null, CancellationToken.None);

            Assert.AreEqual("دو هشدار برای امروز ثبت شده است؛ یک مورد خطر و یک مورد اطلاع. خطر، دو ماشین خراب است. اطلاع، چهار گزارش عدم انطباق باز است.", answer.Data.Text);
            CollectionAssert.AreEqual(new[] { "overview:False" }, overview.Calls);
        }

        [TestMethod]
        public async Task The_Arguments_Are_Checked_Before_Anything_Is_Read_Or_Spoken()
        {
            var overview = new FakeOverview { Overview = SampleOverview() };
            overview.Sections["finance"] = SampleSection("finance", "مالی");
            var tts = new FakeTts();
            CeoSpeechService service = Service(overview, tts);

            ResponseDto<CeoSpeechDto> unknownKind = await service.GetAsync("brief", null, CancellationToken.None);
            ResponseDto<CeoSpeechDto> noKind = await service.GetAsync(null, null, CancellationToken.None);
            ResponseDto<CeoSpeechDto> noSection = await service.GetAsync("section", "  ", CancellationToken.None);

            foreach (ResponseDto<CeoSpeechDto> refused in new[] { unknownKind, noKind })
            {
                Assert.IsFalse(refused.Success);
                Assert.AreEqual(CeoText.UnknownSpeech, refused.Message);
                Assert.AreEqual(HttpStatusCode.BadRequest, refused.HttpStatusCode);
            }
            Assert.IsFalse(noSection.Success);
            Assert.AreEqual(CeoText.SpeechNeedsSection, noSection.Message);
            Assert.AreEqual(0, overview.Calls.Count, "no figure was read for a request that is wrong");
            Assert.AreEqual(0, tts.Texts.Count);

            ResponseDto<CeoSpeechDto> unknownSection = await service.GetAsync("section", "nothing", CancellationToken.None);
            Assert.IsFalse(unknownSection.Success);
            Assert.AreEqual(CeoText.UnknownSection, unknownSection.Message);
            Assert.AreEqual(HttpStatusCode.NotFound, unknownSection.HttpStatusCode);
            Assert.AreEqual(0, tts.Texts.Count, "nothing is spoken for a section that does not exist");
        }

        [TestMethod]
        public async Task Only_The_CEO_And_The_PMO_Are_Read_To()
        {
            foreach (string roles in new[] { "WAREHOUSE", "FINANCEMANAGER", "PAYROLL,PLANNING", "CEO", "PMO" })
            {
                var overview = new FakeOverview { Overview = SampleOverview() };
                var tts = new FakeTts();

                ResponseDto<CeoSpeechDto> answer = await Service(overview, tts, roles).GetAsync("headline", null, CancellationToken.None);

                bool allowed = roles == "CEO" || roles == "PMO";
                Assert.AreEqual(allowed, answer.Success, roles);
                if (allowed) continue;
                Assert.AreEqual(CeoText.NotAllowed, answer.Message, roles);
                Assert.AreEqual(0, overview.Calls.Count, roles + ": nothing is read for a reader who may not see it");
                Assert.AreEqual(0, tts.Texts.Count, roles);
            }

            var wrongKind = new FakeOverview { Overview = SampleOverview() };
            ResponseDto<CeoSpeechDto> storekeeper = await Service(wrongKind, new FakeTts(), "WAREHOUSE").GetAsync("nonsense", null, CancellationToken.None);
            Assert.AreEqual(CeoText.NotAllowed, storekeeper.Message, "the reader is checked before the arguments");
        }

        [TestMethod]
        public async Task A_Failed_Read_Is_Passed_On_And_Nothing_Is_Spoken()
        {
            var overview = new FakeOverview { OverviewFailure = ResponseDtoHelper.BadRequest<CeoOverviewDto>("خطا در خواندن") };
            var tts = new FakeTts();

            ResponseDto<CeoSpeechDto> answer = await Service(overview, tts).GetAsync("alerts", null, CancellationToken.None);

            Assert.IsFalse(answer.Success);
            Assert.AreEqual("خطا در خواندن", answer.Message);
            Assert.AreEqual(0, tts.Texts.Count);
        }

        [TestMethod]
        public void The_Kinds_Are_The_Three_Of_The_Contract()
        {
            // contract 7.2: what = headline | section | alerts; the front sends them as they are
            CollectionAssert.AreEqual(new[] { "headline", "section", "alerts" }, new[] { CeoSpeechKinds.Headline, CeoSpeechKinds.Section, CeoSpeechKinds.Alerts });
            Assert.AreEqual(10, CeoSpeechScript.MaxAlerts);
        }
    }
}
