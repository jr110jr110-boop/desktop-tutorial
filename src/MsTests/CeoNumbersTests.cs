using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Ceo;
using Services.NasbNiro.Ceo.Drills;
using ViewModels.NasbNiro.CeoDtos;

namespace MsTests
{
    /// <summary>
    /// The numbers the AI reads from the CEO dashboard (tool ceo_overview): the answer for the whole company has to fit the 6000 characters of a tool result as it is, because the shaper
    /// would otherwise cut every list to three items and drop sections (review item 2 of part A2). The tool is executed through the real registry, so the shaper, the privacy filter and the hash are the real ones.
    /// </summary>
    [TestClass]
    public class CeoNumbersTests
    {
        /// <summary>A change text of 60 characters: longer than any the overview builds (the longest is about 35), so the size checks have room to spare.</summary>
        private static readonly string LongDelta = "۴۸٫۲ میلیارد ریال بیشتر از ماه قبل (بدون احتساب سفارش‌های لغوشده)".Substring(0, 60);

        private static readonly string[] Statuses = { CeoStatus.Ok, CeoStatus.Warn, CeoStatus.Danger, CeoStatus.Neutral };

        private static readonly string[] ItemKeys = { "key", "title", "value", "unit", "status", "deltaText" };

        private sealed class FakeOverview : ICeoOverviewService
        {
            public CeoOverviewDto Overview { get; set; }
            public List<string> Calls { get; } = new List<string>();

            public Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct)
            {
                Calls.Add("overview:" + refresh);
                return Task.FromResult(ResponseDtoHelper.Ok(Overview));
            }

            public Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct)
            {
                Calls.Add("section:" + key);
                CeoSectionDto section = Overview.Sections.FirstOrDefault(s => s.Key == key);
                return Task.FromResult(section == null ? ResponseDtoHelper.NotFound<CeoSectionDto>(CeoText.UnknownSection) : ResponseDtoHelper.Ok(section));
            }
        }

        private static CeoKpiDto Kpi(CeoKpiSpec spec, int n)
        {
            decimal value;
            switch (spec.Unit)
            {
                case CeoUnit.Rial: value = 48_200_000_000m + n * 1_234_567_000m; break;
                case CeoUnit.Percent: value = 62.5m; break;
                case CeoUnit.Days: value = 14m; break;
                case CeoUnit.Hours: value = 36.5m; break;
                default: value = 7 + n; break;
            }
            return new CeoKpiDto
            {
                Key = spec.Key, Title = spec.Title, Value = value, Unit = spec.Unit, ValueText = "-", Delta = 1m, DeltaText = LongDelta, DeltaGood = true,
                Status = Statuses[n % Statuses.Length], Link = spec.Link, DrillKey = spec.DrillKey, DrillParams = spec.DrillParams?.Invoke(DateTime.Today),
                Note = "یادداشت شاخص: این عدد از رکوردهای امروز سامانه محاسبه شده و با جزئیات شاخص برابر است.",
            };
        }

        private static List<CeoChartDto> Charts(string section) => new List<CeoChartDto>
        {
            new CeoChartDto
            {
                Key = section + "ByMonth", Title = "روند ماهانه", Kind = CeoKind.Bar, Unit = CeoUnit.Rial, Labels = Enumerable.Range(1, 12).Select(i => "ماه " + i).ToList(),
                Series = new List<CeoChartSeriesDto>
                {
                    new CeoChartSeriesDto { Name = "دریافتی", Values = Enumerable.Range(1, 12).Select(i => i * 1000m).ToList() },
                    new CeoChartSeriesDto { Name = "پرداختی", Values = Enumerable.Range(1, 12).Select(i => i * 10m).ToList() },
                },
                DrillKey = "fin.revenue_by_month", DrillParams = new Dictionary<string, string> { ["from"] = "2026-03-21", ["to"] = "2026-10-26" },
            },
            new CeoChartDto
            {
                Key = section + "Trend", Title = "میانگین ماهانه", Kind = CeoKind.Line, Unit = CeoUnit.Count, Labels = new List<string> { "الف", "ب" },
                Series = new List<CeoChartSeriesDto> { new CeoChartSeriesDto { Name = "میانگین", Values = new List<decimal> { 4m, 5m } } },
            },
            new CeoChartDto
            {
                Key = section + "Share", Title = "سهم", Kind = CeoKind.Donut, Unit = CeoUnit.Percent, Labels = new List<string> { "الف", "ب" },
                Series = new List<CeoChartSeriesDto> { new CeoChartSeriesDto { Name = "سهم", Values = new List<decimal> { 60m, 40m } } },
            },
            new CeoChartDto { Key = section + "Masked", Title = "پنهان", Kind = CeoKind.Bar, Unit = CeoUnit.Rial, Hidden = true },
        };

        /// <summary>Nine sections with six figures each: the figures of the catalogue with their real Persian titles, keys and drills (the thin sections are filled with the longest titles of the others).</summary>
        private static CeoOverviewDto RealisticOverview()
        {
            var overview = new CeoOverviewDto { AsOf = DateTime.Today, AsOfText = CeoText.Date(DateTime.Today), GeneratedAt = DateTimeOffset.Now };
            List<CeoKpiSpec> longest = CeoKpiSpecs.All.OrderByDescending(s => s.Title.Length).ToList();
            int n = 0;
            foreach (string section in CeoKeys.Sections)
            {
                var kpis = CeoKpiSpecs.Of(section).Select(spec => Kpi(spec, n++)).ToList();
                for (int i = 0; kpis.Count < 6; i++)
                {
                    CeoKpiSpec filler = longest.First(s => s.Section != section);
                    longest.Remove(filler);
                    CeoKpiDto extra = Kpi(filler, n++);
                    extra.Key = "extra" + section + i;
                    kpis.Add(extra);
                }
                overview.Sections.Add(new CeoSectionDto { Key = section, Title = CeoModules.Title(section), Kpis = kpis, Charts = Charts(section), Link = "/ceo", AnalysisPrompt = "تحلیل" });
            }
            // the eight headline figures of the screen are the same objects as the figures of their sections
            overview.Headline = CeoKeys.Headline.Select(key => overview.Sections.SelectMany(s => s.Kpis).First(k => k.Key == key)).ToList();
            return overview;
        }

        private static ToolRegistry Registry(FakeOverview overview, params string[] roles)
        {
            var user = new CeoFakeUser(roles);
            return new ToolRegistry(new CeoOneService(typeof(ICeoSectionNumbersProvider), new CeoOverviewSectionNumbersProvider(overview, user)), user);
        }

        private static async Task<(AiToolResult Result, JsonDocument Json)> Ask(ToolRegistry registry, string arguments)
        {
            AiToolResult result = await registry.ExecuteAsync("ceo_overview", arguments, CancellationToken.None);
            return (result, JsonDocument.Parse(result.Json));
        }

        private static List<string> Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();

        // ------------------------------------------------------------------------------------------ the whole company

        [TestMethod]
        public async Task The_Whole_Company_Fits_One_Tool_Result_With_All_Nine_Sections_And_The_Eight_Headline_Figures()
        {
            Assert.AreEqual(60, LongDelta.Length);
            var fake = new FakeOverview { Overview = RealisticOverview() };
            Assert.AreEqual(9, fake.Overview.Sections.Count);
            Assert.IsTrue(fake.Overview.Sections.All(s => s.Kpis.Count == 6));

            var (result, json) = await Ask(Registry(fake, "CEO"), "{}");
            using (json)
            {
                Assert.IsTrue(result.Allowed, result.Json);
                Assert.IsTrue(result.Json.Length < 6000, "the answer is " + result.Json.Length + " characters");
                Assert.AreEqual(result.Json.Length, result.Chars);
                // nothing was cut: the shaper marks a cut list with the count of its items and a cut result with «truncated»
                Assert.IsFalse(result.Json.Contains("TotalCount"), result.Json);
                Assert.IsFalse(result.Json.Contains("\"truncated\""), result.Json);

                JsonElement root = json.RootElement;
                CollectionAssert.AreEqual(new[] { "asOfText", "sections", "link" }, Names(root), "no second list of the headline figures");
                Assert.AreEqual(fake.Overview.AsOfText, root.GetProperty("asOfText").GetString());
                Assert.AreEqual(CeoLinks.Overview, root.GetProperty("link").GetString());
                Assert.AreEqual("/ceo", root.GetProperty("link").GetString());

                JsonElement sections = root.GetProperty("sections");
                CollectionAssert.AreEqual(CeoKeys.Sections, sections.EnumerateArray().Select(s => s.GetProperty("section").GetString()).ToArray());
                var keys = new List<string>();
                foreach (JsonElement section in sections.EnumerateArray())
                {
                    CollectionAssert.AreEqual(new[] { "section", "title", "items" }, Names(section));
                    JsonElement items = section.GetProperty("items");
                    Assert.AreEqual(CeoOverviewSectionNumbersProvider.HeadlinePerSection, items.GetArrayLength(), section.GetProperty("section").GetString());
                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        // per figure: key, title, value, unit, status and the change; no note, no drill, no links
                        CollectionAssert.AreEqual(ItemKeys, Names(item), item.GetProperty("key").GetString());
                        Assert.AreEqual(LongDelta, item.GetProperty("deltaText").GetString());
                        keys.Add(item.GetProperty("key").GetString());
                    }
                }
                foreach (string headline in CeoKeys.Headline) CollectionAssert.Contains(keys, headline, "the headline figure «" + headline + "» is in the answer");
                Assert.AreEqual(27, keys.Count);
                CollectionAssert.AllItemsAreUnique(keys);
            }
            CollectionAssert.AreEqual(new[] { "overview:False" }, fake.Calls, "the cached overview of today, never a computation of its own");
        }

        [TestMethod]
        public async Task The_Headline_Figures_Come_First_In_Their_Sections()
        {
            // finance has three of them, the other sections one; the figure of the section that is not a headline one never pushes a headline one out
            var fake = new FakeOverview { Overview = RealisticOverview() };
            foreach (CeoSectionDto section in fake.Overview.Sections) section.Kpis = section.Kpis.AsEnumerable().Reverse().ToList();

            var (result, json) = await Ask(Registry(fake, "PMO"), "{}");
            using (json)
            {
                var keys = json.RootElement.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("key").GetString()).ToList();
                foreach (string headline in CeoKeys.Headline) CollectionAssert.Contains(keys, headline, "reversed sections: " + result.Json);
                Assert.IsTrue(result.Json.Length < 6000, "length " + result.Json.Length);
            }
        }

        [TestMethod]
        public async Task A_Section_That_Could_Not_Be_Computed_Is_Told_With_Its_Note_And_Figures_Without_Values()
        {
            var fake = new FakeOverview { Overview = RealisticOverview() };
            CeoSectionDto broken = fake.Overview.Sections.First(s => s.Key == CeoKeys.Quality);
            broken.Note = "محاسبه‌ی این بخش اکنون ممکن نشد.";
            foreach (CeoKpiDto kpi in broken.Kpis) { kpi.Value = null; kpi.DeltaText = null; kpi.Status = CeoStatus.Neutral; }
            broken.Charts = new List<CeoChartDto>();

            var (result, json) = await Ask(Registry(fake, "CEO"), "{}");
            using (json)
            {
                Assert.IsTrue(result.Json.Length < 6000);
                JsonElement quality = json.RootElement.GetProperty("sections").EnumerateArray().First(s => s.GetProperty("section").GetString() == CeoKeys.Quality);
                Assert.AreEqual(broken.Note, quality.GetProperty("note").GetString());
                foreach (JsonElement item in quality.GetProperty("items").EnumerateArray())
                    CollectionAssert.AreEqual(new[] { "key", "title", "unit", "status" }, Names(item));
            }
        }

        // ------------------------------------------------------------------------------------------ one section

        [TestMethod]
        public async Task One_Section_Gives_Every_Figure_In_Full_With_The_Day_And_The_Totals_Of_Its_Charts()
        {
            var fake = new FakeOverview { Overview = RealisticOverview() };
            ToolRegistry registry = Registry(fake, "CEO");
            string today = CeoText.Date(DateTime.Today);

            foreach (string section in CeoKeys.Sections)
            {
                string before = CeoText.Date(DateTime.Today);
                var (result, json) = await Ask(registry, "{\"section\":\"" + section + "\"}");
                using (json)
                {
                    Assert.IsTrue(result.Allowed, section + ": " + result.Json);
                    Assert.IsTrue(result.Json.Length < 6000, section + ": " + result.Json.Length + " characters");
                    Assert.IsFalse(result.Json.Contains("TotalCount") || result.Json.Contains("\"truncated\""), section + ": " + result.Json);

                    JsonElement root = json.RootElement;
                    Assert.AreEqual(section, root.GetProperty("section").GetString());
                    Assert.AreEqual(CeoModules.Title(section), root.GetProperty("title").GetString());
                    string asOf = root.GetProperty("asOfText").GetString();
                    Assert.IsTrue(asOf == before || asOf == CeoText.Date(DateTime.Today), "the day of the figures is today: " + asOf + " / " + today);
                    Assert.AreEqual("/ceo", root.GetProperty("link").GetString());

                    JsonElement items = root.GetProperty("items");
                    CeoSectionDto source = fake.Overview.Sections.First(s => s.Key == section);
                    CollectionAssert.AreEqual(source.Kpis.Select(k => k.Key).ToArray(), items.EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToArray(), "all six figures, in order");
                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        foreach (string name in ItemKeys.Concat(new[] { "drillKey", "note" })) Assert.IsTrue(item.TryGetProperty(name, out _), section + "/" + item.GetProperty("key").GetString() + " has no «" + name + "»");
                        Assert.AreEqual(LongDelta, item.GetProperty("deltaText").GetString());
                    }

                    // the totals of the charts: a chart of rials or counts that is not a line adds up per series; the others (a line of averages, percentages) carry none; a masked chart says so
                    JsonElement charts = root.GetProperty("charts");
                    Assert.AreEqual(4, charts.GetArrayLength());
                    JsonElement byMonth = charts[0], trend = charts[1], share = charts[2], masked = charts[3];
                    Assert.AreEqual(section + "ByMonth", byMonth.GetProperty("key").GetString());
                    JsonElement totals = byMonth.GetProperty("totals");
                    Assert.AreEqual(2, totals.GetArrayLength());
                    Assert.AreEqual(78000m, totals[0].GetProperty("total").GetDecimal());
                    Assert.AreEqual(780m, totals[1].GetProperty("total").GetDecimal());
                    Assert.AreEqual("fin.revenue_by_month", byMonth.GetProperty("drillKey").GetString());
                    Assert.IsFalse(trend.TryGetProperty("totals", out _), "a line of averages has no total");
                    Assert.IsFalse(share.TryGetProperty("totals", out _), "percentages have no total");
                    Assert.IsTrue(masked.GetProperty("hidden").GetBoolean());
                    Assert.IsFalse(masked.TryGetProperty("totals", out _));
                }
            }
            CollectionAssert.AreEqual(CeoKeys.Sections.Select(s => "section:" + s).ToArray(), fake.Calls);
        }

        [TestMethod]
        public async Task A_Hidden_Figure_Carries_No_Value_No_Change_And_No_Note_In_Either_Answer()
        {
            var fake = new FakeOverview { Overview = RealisticOverview() };
            CeoKpiDto cash = fake.Overview.Sections.First(s => s.Key == CeoKeys.Finance).Kpis.First(k => k.Key == CeoKeys.Cash);
            cash.Hidden = true;
            cash.Value = 123456789m;
            cash.Note = "secret note 987654321";
            ToolRegistry registry = Registry(fake, "CEO");

            foreach (string arguments in new[] { "{}", "{\"section\":\"finance\"}" })
            {
                var (result, json) = await Ask(registry, arguments);
                using (json)
                {
                    Assert.IsFalse(result.Json.Contains("123456789") || result.Json.Contains("987654321"), result.Json);
                    JsonElement finance = arguments == "{}"
                        ? json.RootElement.GetProperty("sections").EnumerateArray().First(s => s.GetProperty("section").GetString() == CeoKeys.Finance)
                        : json.RootElement;
                    JsonElement item = finance.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == CeoKeys.Cash);
                    Assert.IsTrue(item.GetProperty("hidden").GetBoolean());
                    Assert.IsFalse(item.TryGetProperty("value", out _));
                    Assert.IsFalse(item.TryGetProperty("deltaText", out _));
                    Assert.IsFalse(item.TryGetProperty("note", out _));
                }
            }
        }

        // ------------------------------------------------------------------------------------------ who may ask, what may be asked

        [TestMethod]
        public async Task Only_The_CEO_And_The_PMO_Read_The_Numbers_And_An_Unknown_Section_Is_An_Error_Not_The_Whole_Company()
        {
            var fake = new FakeOverview { Overview = RealisticOverview() };

            foreach (string role in new[] { "HR", "Storekeeper", "Finance" })
            {
                var (denied, json) = await Ask(Registry(fake, role), "{}");
                using (json)
                {
                    Assert.IsFalse(denied.Allowed, role);
                    Assert.IsTrue(json.RootElement.GetProperty("forbidden").GetBoolean(), role);
                }
            }
            CollectionAssert.AreEqual(new string[0], fake.Calls, "a reader without the role touches nothing");

            foreach (string role in new[] { "CEO", "PMO" })
            {
                var (allowed, json) = await Ask(Registry(fake, role), "{\"section\":\"Quality\"}");
                using (json)
                {
                    Assert.IsTrue(allowed.Allowed, role);
                    Assert.AreEqual(CeoKeys.Quality, json.RootElement.GetProperty("section").GetString(), "the key of a section is not case sensitive");
                }
            }

            fake.Calls.Clear();
            var (unknown, unknownJson) = await Ask(Registry(fake, "CEO"), "{\"section\":\"nothing\"}");
            using (unknownJson)
            {
                Assert.IsTrue(unknownJson.RootElement.GetProperty("error").GetBoolean());
                Assert.AreEqual(AiText.YeKe(CeoText.UnknownSection), unknownJson.RootElement.GetProperty("message").GetString());
                Assert.IsFalse(unknown.Json.Contains("sections"), "no numbers of the company in the answer to a wrong key");
            }
            CollectionAssert.AreEqual(new string[0], fake.Calls);
        }
    }
}
