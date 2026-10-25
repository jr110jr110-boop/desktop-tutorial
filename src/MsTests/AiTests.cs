using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;

namespace MsTests
{
    /// <summary>The pure rules of the local AI: the numeric answer check, numbers in words, shaping of tool results, chunking, URL check, request building.</summary>
    [TestClass]
    public class AiTests
    {
        private static readonly AnswerVerifier Verifier = new AnswerVerifier();
        private static readonly PersianNumberWords Words = new PersianNumberWords();

        private static VerifierToolResult Tool(string json, string name = "ceo_dashboard", string title = "داشبورد مدیرعامل") =>
            new VerifierToolResult { Name = name, Title = title, Json = json };

        // ------------------------------------------------------------------------------------------ verifier

        [TestMethod]
        public void Verifier_Accepts_Persian_And_Arabic_Digits_And_Separators()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"stockValue\":1500000,\"open\":12}") };
            Assert.IsTrue(Verifier.Verify("ارزش موجودی ۱,۵۰۰,۰۰۰ ریال است.", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("ارزش موجودی ۱٬۵۰۰٬۰۰۰ ریال و ۱۲ مورد باز.", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("ارزش موجودی ١٥٠٠٠٠٠ ریال", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("ارزش موجودی 1500000 ریال", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_Rejects_A_Fabricated_Number_And_Replaces_The_Answer()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"stockValue\":1500000,\"open\":12,\"title\":\"x\"}") };
            VerificationResult r = Verifier.Verify("مبلغ کل 987654321 ریال است.", "داشبورد", tools);
            Assert.IsFalse(r.Verified);
            CollectionAssert.AreEqual(new List<string> { "987654321" }, r.OffendingTokens);
            Assert.IsTrue(r.Answer.StartsWith("پاسخ مدل قابل راستی‌آزمایی نبود؛ نتایج مستقیم سامانه:"));
            Assert.IsFalse(r.Answer.Contains("987654321"));
            StringAssert.Contains(r.Answer, "داشبورد مدیرعامل: ارزش موجودی: 1500000، باز: 12، عنوان: x");
        }

        [TestMethod]
        public void Verifier_Splits_Dates_Into_Tokens()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"due\":\"1405/08/03\"}") };
            Assert.IsTrue(Verifier.Verify("موعد: 1405/08/03", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("موعد: ۱۴۰۵/۰۸/۰۳", "", tools).Verified);
            VerificationResult wrong = Verifier.Verify("موعد: 1405/08/04", "", tools);
            Assert.IsFalse(wrong.Verified);
            CollectionAssert.AreEqual(new List<string> { "04" }, wrong.OffendingTokens);
            Assert.IsTrue(Verifier.Verify("ساعت 14:30", "", new List<VerifierToolResult> { Tool("{\"at\":\"14:30\"}") }).Verified);
        }

        [TestMethod]
        public void Verifier_Accepts_Numbers_Of_The_Question()
        {
            Assert.IsTrue(Verifier.Verify("سفارش 120 پیدا نشد.", "وضعیت سفارش 120 چیست؟", new List<VerifierToolResult>()).Verified);
            Assert.IsFalse(Verifier.Verify("سفارش 121 پیدا نشد.", "وضعیت سفارش 120 چیست؟", new List<VerifierToolResult>()).Verified);
        }

        [TestMethod]
        public void Verifier_Exempts_List_Markers_At_The_Start_Of_A_Line_Only()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"a\":5}") };
            Assert.IsTrue(Verifier.Verify("۱. نخست 5\n2) دوم\n3- سوم\n 4– چهارم", "", tools).Verified);
            VerificationResult r = Verifier.Verify("مورد 2. نخست", "", tools);
            Assert.IsFalse(r.Verified);
        }

        [TestMethod]
        public void Verifier_Compares_Decimals_After_Trimming_Zeros()
        {
            Assert.IsTrue(Verifier.Verify("نرخ 12.5 درصد", "", new List<VerifierToolResult> { Tool("{\"rate\":12.50}") }).Verified);
            Assert.IsTrue(Verifier.Verify("نرخ 12.50 درصد", "", new List<VerifierToolResult> { Tool("{\"rate\":12.5}") }).Verified);
            Assert.IsTrue(Verifier.Verify("نرخ ۱۲٫۵ درصد", "", new List<VerifierToolResult> { Tool("{\"rate\":12.5}") }).Verified);
            Assert.IsFalse(Verifier.Verify("نرخ 12.6 درصد", "", new List<VerifierToolResult> { Tool("{\"rate\":12.5}") }).Verified);
            Assert.IsTrue(Verifier.Verify("مبلغ 1500000.00", "", new List<VerifierToolResult> { Tool("{\"v\":1500000.0000}") }).Verified);
        }

        [TestMethod]
        public void Verifier_Answers_Without_Tools_And_Numbers_Are_Verified()
        {
            VerificationResult r = Verifier.Verify("سلام؛ چه کمکی از من برمی‌آید؟", "سلام", new List<VerifierToolResult>());
            Assert.IsTrue(r.Verified);
            Assert.AreEqual("سلام؛ چه کمکی از من برمی‌آید؟", r.Answer);
        }

        [TestMethod]
        public void Verifier_Does_Not_Glue_List_Items_Of_Tool_Json_Together()
        {
            // "1,2,3" in a JSON array must stay three numbers, not 123
            var tools = new List<VerifierToolResult> { Tool("{\"ids\":[1,2,3]}") };
            Assert.IsTrue(Verifier.Verify("شناسه‌ها 1 و 2 و 3", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("شناسه‌ها 1، 2، 3", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_Ignores_Leading_Zeros()
        {
            Assert.IsTrue(Verifier.Verify("ماه 8 سال 1405", "", new List<VerifierToolResult> { Tool("{\"d\":\"1405/08/03\"}") }).Verified);
        }

        // ------------------------------------------------------------------------------------------ numbers in words

        [TestMethod]
        public void Words_Scales_And_Joining()
        {
            Assert.AreEqual("سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج", Words.ToWords(37365625));
            Assert.AreEqual("صفر", Words.ToWords(0));
            Assert.AreEqual("منفی پنج", Words.ToWords(-5));
            Assert.AreEqual("یازده", Words.ToWords(11));
            Assert.AreEqual("صد و یک", Words.ToWords(101));
            Assert.AreEqual("نهصد و نود و نه", Words.ToWords(999));
            Assert.AreEqual("هزار", Words.ToWords(1000));
            Assert.AreEqual("هزار و یک", Words.ToWords(1001));
            Assert.AreEqual("دو هزار", Words.ToWords(2000));
            Assert.AreEqual("یک میلیون", Words.ToWords(1000000));
            Assert.AreEqual("یک میلیارد و دویست و سی و چهار میلیون و پانصد و شصت و هفت هزار و هشتصد و نود", Words.ToWords(1234567890));
            Assert.AreEqual("هزار میلیارد", Words.ToWords(1000000000000));
            Assert.AreEqual("پنج هزار و دویست میلیارد", Words.ToWords(5200000000000));
        }

        [TestMethod]
        public void Words_Rial_Text()
        {
            Assert.AreEqual("سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج ریال", Words.RialText(37365625m));
            Assert.AreEqual("صفر ریال", Words.RialText(0.4m));
            Assert.AreEqual("دو ریال", Words.RialText(1.5m));
        }

        [TestMethod]
        public void Words_Speech_Normalize_Numbers_And_Dates()
        {
            Assert.AreEqual("مبلغ سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج ریال", Words.SpeechNormalize("مبلغ 37,365,625 ریال"));
            Assert.AreEqual("مبلغ سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج ریال", Words.SpeechNormalize("مبلغ ۳۷٬۳۶۵٬۶۲۵ ریال"));
            Assert.AreEqual("موعد روز سوم آبان سال هزار و چهارصد و پنج است", Words.SpeechNormalize("موعد 1405/08/03 است"));
            Assert.AreEqual("موعد روز سوم آبان سال هزار و چهارصد و پنج است", Words.SpeechNormalize("موعد ۱۴۰۵/۰۸/۰۳ است"));
            Assert.AreEqual("نرخ دوازده ممیز پنج درصد", Words.SpeechNormalize("نرخ 12.5 درصد"));
            Assert.AreEqual("بدون عدد", Words.SpeechNormalize("بدون عدد"));
            Assert.AreEqual("روز بیست و نهم اسفند سال هزار و چهارصد", Words.SpeechNormalize("1400/12/29"));
        }

        // ------------------------------------------------------------------------------------------ shaping of tool results

        [TestMethod]
        public void Shaper_Cuts_Lists_To_Ten_With_Total_Count()
        {
            var items = Enumerable.Range(1, 25).Select(i => new { id = i, number = "PO-" + i }).ToList();
            var tool = new AiToolDefinition { Name = "t", Title = "ابزار", CitationType = "PurchaseOrder", ItemLinks = { new AiItemLinkRule { Property = "items", Type = "PurchaseOrder", TitlePrefix = "سفارش", Template = "/procurement/orders/{id}" } } };
            var citations = new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>();
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(new { totalCount = 25, items })), citations, tool);
            JsonNode node = JsonNode.Parse(json);
            Assert.AreEqual(10, node["items"].AsArray().Count);
            Assert.AreEqual(25, (int)node["totalCount"]);
            Assert.AreEqual("/procurement/orders/1", (string)node["items"][0]["link"]);
            Assert.AreEqual(10, citations.Count);
            Assert.AreEqual("PurchaseOrder", citations[0].Type);
            Assert.AreEqual("1", citations[0].Id);
            Assert.AreEqual("/procurement/orders/1", citations[0].Link);
            StringAssert.Contains(citations[0].Title, "PO-1");

            // a root list is wrapped with its total
            JsonNode rootList = JsonNode.Parse(AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(items)), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" }));
            Assert.AreEqual(25, (int)rootList["totalCount"]);
            Assert.AreEqual(10, rootList["items"].AsArray().Count);
        }

        [TestMethod]
        public void Shaper_Nested_Lists_Get_A_Total_And_Dates_Become_Shamsi()
        {
            var data = new { name = "x", at = new System.DateTime(2026, 10, 25), when = new System.DateTimeOffset(2026, 10, 25, 0, 0, 0, System.TimeZoneInfo.Local.GetUtcOffset(new System.DateTime(2026, 10, 25))), rows = Enumerable.Range(1, 14).ToList() };
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(data)), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" });
            JsonNode node = JsonNode.Parse(json);
            Assert.AreEqual(10, node["rows"].AsArray().Count);
            Assert.AreEqual(14, (int)node["rowsTotalCount"]);
            Assert.AreEqual("1405/08/03", (string)node["at"]);
            Assert.IsFalse(json.Contains("2026"));
        }

        [TestMethod]
        public void Shaper_Removes_Banking_And_Secret_Fields()
        {
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(new { name = "a", iban = "IR000000000000000000000000", supplierIban = "IR1", nationalId = "123", token = "t", rowVersion = "AAA" })), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" });
            Assert.AreEqual("{\"name\":\"a\"}", json);
            string more = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(new { name = "a", nationalCode = "1", phone = "2", mobile = "3", address = "x", salary = 1, wage = 2, accountNumber = "4" })), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" });
            Assert.AreEqual("{\"name\":\"a\"}", more);
        }

        [TestMethod]
        public void Shaper_Keeps_The_Result_Under_Six_Thousand_Characters()
        {
            var big = new { text = new string('م', 5000), rows = Enumerable.Range(1, 40).Select(i => new { id = i, note = new string('ب', 300) }).ToList(), tail = "end" };
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(big)), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" });
            Assert.IsTrue(json.Length <= AiToolShaper.MaxChars, "length " + json.Length);
            Assert.IsNotNull(JsonNode.Parse(json));
        }

        [TestMethod]
        public void Shaper_Forbidden_And_Error_Results()
        {
            Assert.AreEqual("{\"forbidden\":true,\"message\":\"شما به این اطلاعات دسترسی ندارید.\"}", AiToolShaper.ForbiddenJson());
            Assert.AreEqual("{\"error\":true,\"message\":\"x\"}", AiToolShaper.ErrorJson("x"));
            Assert.ThrowsExactly<AiToolForbiddenException>(() => AiToolShaper.Unwrap(Common.Results.ResponseDtoHelper.BadRequest<int>("شما مجوز این کار را ندارید")));
            Assert.ThrowsExactly<AiToolFailureException>(() => AiToolShaper.Unwrap(Common.Results.ResponseDtoHelper.NotFound<int>("پیدا نشد")));
            Assert.AreEqual(5, AiToolShaper.Unwrap(Common.Results.ResponseDtoHelper.Ok(5)));
        }

        [TestMethod]
        public void Tools_Have_Unique_Snake_Case_Names_And_Persian_Descriptions()
        {
            var names = ToolRegistry.Definitions.Select(t => t.Name).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count());
            foreach (AiToolDefinition t in ToolRegistry.Definitions)
            {
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(t.Name, "^[a-z]+(_[a-z]+)*$"), t.Name);
                Assert.IsTrue(t.Description.Any(c => c >= '؀' && c <= 'ۿ'), t.Name);
                Assert.IsNotNull(JsonNode.Parse(t.SchemaJson), t.Name);
            }
            foreach (string required in new[] { "ceo_dashboard", "cash_position", "items_below_minimum", "search_documents", "explain_alert", "finance_dashboard", "project_status" })
                CollectionAssert.Contains(names, required);
        }

        [TestMethod]
        public void Alert_Links_Map_To_Source_Tools()
        {
            Assert.AreEqual("items_below_minimum", AiTools.AlertSource("/inventory/stock"));
            Assert.AreEqual("expiring_guarantees", AiTools.AlertSource("/project-control/guarantees"));
            Assert.AreEqual("project_portfolio", AiTools.AlertSource("/project-control/portfolio"));
            Assert.IsNull(AiTools.AlertSource("/office/decisions"));
            Assert.IsNull(AiTools.AlertSource(null));
        }

        // ------------------------------------------------------------------------------------------ clients, retrieval, admin helpers

        [TestMethod]
        public void LlmClient_Builds_And_Parses_OpenAi_Messages()
        {
            var messages = new List<LlmMessage>
            {
                new LlmMessage { Role = "system", Content = "s" },
                new LlmMessage { Role = "user", Content = "داشبورد" },
                new LlmMessage { Role = "assistant", ToolCalls = new List<LlmToolCall> { new LlmToolCall { Id = "c1", Name = "ceo_dashboard", ArgumentsJson = "{}" } } },
                new LlmMessage { Role = "tool", ToolCallId = "c1", Content = "{\"a\":1}" },
            };
            var tools = new List<LlmToolSpec> { new LlmToolSpec { Name = "ceo_dashboard", Description = "d", SchemaJson = "{\"type\":\"object\",\"properties\":{}}" } };
            JsonNode body = JsonNode.Parse(LlmClient.BuildRequest("m", messages, tools));
            Assert.AreEqual("m", (string)body["model"]);
            Assert.AreEqual("auto", (string)body["tool_choice"]);
            Assert.AreEqual(0, (int)body["temperature"]);
            Assert.AreEqual("function", (string)body["tools"][0]["type"]);
            Assert.AreEqual("ceo_dashboard", (string)body["tools"][0]["function"]["name"]);
            Assert.AreEqual("c1", (string)body["messages"][2]["tool_calls"][0]["id"]);
            Assert.AreEqual("c1", (string)body["messages"][3]["tool_call_id"]);
            Assert.IsNull(JsonNode.Parse(LlmClient.BuildRequest("m", messages, null))["tools"]);

            LlmResult r = LlmClient.Parse("{\"model\":\"mock\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"x\",\"type\":\"function\",\"function\":{\"name\":\"cash_position\",\"arguments\":\"{}\"}}]}}]}");
            Assert.AreEqual("mock", r.Model);
            Assert.AreEqual(1, r.ToolCalls.Count);
            Assert.AreEqual("cash_position", r.ToolCalls[0].Name);
            LlmResult text = LlmClient.Parse("{\"choices\":[{\"message\":{\"content\":\"سلام\"}}]}");
            Assert.AreEqual("سلام", text.Content);
            Assert.AreEqual(0, text.ToolCalls.Count);
        }

        [TestMethod]
        public void Endpoint_Urls_Are_Checked()
        {
            Assert.AreEqual("http://127.0.0.1:5091", AiAdminService.CleanUrl("http://127.0.0.1:5091/"));
            Assert.AreEqual("https://ai.local:8443", AiAdminService.CleanUrl(" https://ai.local:8443 "));
            Assert.IsNull(AiAdminService.CleanUrl("ftp://x"));
            Assert.IsNull(AiAdminService.CleanUrl("http://user:pass@127.0.0.1:5091"));
            Assert.IsNull(AiAdminService.CleanUrl("http://127.0.0.1:5091/?key=1"));
            Assert.IsNull(AiAdminService.CleanUrl("not a url"));
            Assert.IsNull(AiAdminService.CleanUrl(""));
        }

        [TestMethod]
        public void Retrieval_Chunks_Overlap_And_Vectors_Round_Trip()
        {
            string text = string.Join(" ", Enumerable.Range(1, 400).Select(i => "کلمه" + i));
            List<string> chunks = RetrievalService.Chunk(text);
            Assert.IsTrue(chunks.Count > 2);
            Assert.IsTrue(chunks.All(c => c.Length <= RetrievalService.ChunkSize));
            Assert.AreEqual(0, RetrievalService.Chunk("   ").Count);
            Assert.AreEqual(1, RetrievalService.Chunk("کوتاه").Count);
            // the end of a chunk is repeated at the start of the next
            string tail = chunks[0].Substring(chunks[0].Length - 30);
            string lastWord = tail.Split(' ').Last();
            StringAssert.Contains(chunks[1], lastWord);

            float[] v = { 0.5f, -1.25f, 3f };
            CollectionAssert.AreEqual(v, RetrievalService.FromBytes(RetrievalService.ToBytes(v)));
            Assert.AreEqual(1.0, RetrievalService.Cosine(v, v), 1e-9);
            Assert.AreEqual(0.0, RetrievalService.Cosine(new float[] { 1, 0 }, new float[] { 0, 1 }), 1e-9);
            Assert.AreEqual(0.0, RetrievalService.Cosine(new float[] { 1, 0 }, new float[] { 1, 0, 0 }), 1e-9);
            Assert.IsTrue(RetrievalService.Lexical("روش کنترل اسناد", "روش اجرایی کنترل اسناد شرکت") > RetrievalService.Lexical("روش کنترل اسناد", "گزارش تولید"));
            Assert.AreEqual("/quality/documents/7", RetrievalService.Link(AiSourceTypes.ControlledDocument, 7));
            Assert.AreEqual("/office/meetings/9", RetrievalService.Link(AiSourceTypes.MeetingMinutes, 9));
            CollectionAssert.DoesNotContain(AiSourceTypes.All, "Letter");
        }

        // ------------------------------------------------------------------------------------------ review fixes: verifier

        [TestMethod]
        public void Verifier_Never_Matches_Parts_Of_A_Number()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"count\":5,\"zero\":0,\"generatedAt\":\"1405/08/03 12:52\"}") };
            foreach (string forged in new[] { "5,000,000", "12,052", "1405,008" })
            {
                VerificationResult r = Verifier.Verify("مقدار " + forged + " است.", "", tools);
                Assert.IsFalse(r.Verified, forged);
            }
            Assert.IsTrue(Verifier.Verify("تعداد 5 و تاریخ 1405/08/03 ساعت 12:52", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_A_Grouped_Number_Of_A_Result_Allows_Only_Its_Merged_Value()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"text\":\"مبلغ 1,500,000 ریال\"}") };
            Assert.IsTrue(Verifier.Verify("مبلغ 1500000", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("مبلغ ۱٬۵۰۰٬۰۰۰", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("مبلغ 500", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("مبلغ 1", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("مبلغ 0", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_Numbers_Of_Tool_Arguments_Do_Not_Count()
        {
            // the arguments are not part of the allowed set: only the results and the question are
            var tools = new List<VerifierToolResult> { Tool("{\"totalCount\":0,\"items\":[]}", "search_documents", "جست‌وجوی اسناد") };
            Assert.IsFalse(Verifier.Verify("کد 987654321 پیدا نشد", "کدی را پیدا کن", tools).Verified);
            Assert.IsTrue(Verifier.Verify("کد 987654321 پیدا نشد", "کد 987654321 را پیدا کن", tools).Verified);
            // an error or forbidden message may echo the arguments: it is not read
            var failed = new List<VerifierToolResult> { Tool("{\"error\":true,\"message\":\"کد 987654321 پیدا نشد\"}") };
            Assert.IsFalse(Verifier.Verify("کد 987654321", "", failed).Verified);
        }

        [TestMethod]
        public void Verifier_The_Template_Passes_Its_Own_Check()
        {
            var results = new List<VerifierToolResult>
            {
                Tool("{\"payablesDue30\":1250000,\"payablesDue60\":3,\"stockValue\":37365625.50,\"openOrders\":7,\"title\":\"گزارش 1405/08/03\",\"unknownThing\":99}", "finance_dashboard", "داشبورد مالی"),
                Tool("{\"summary\":{\"activeProjects\":4,\"lateActivities\":2},\"items\":[{\"name\":\"پروژه 12\",\"spi\":0.85}]}", "project_portfolio", "سبد پروژه‌ها"),
            };
            string fallback = AnswerVerifier.Fallback(results);
            Assert.IsTrue(Verifier.Verify(fallback, "", results).Verified, fallback);
            StringAssert.Contains(fallback, "بدهی‌ها سررسید سی: 1250000");
            Assert.IsFalse(fallback.Contains("payablesDue30"));
            Assert.IsFalse(fallback.Contains("unknownThing"));
            // the labels themselves have no digits
            foreach (string label in new[] { AiLabels.Label("payablesDue30"), AiLabels.Label("stockValue"), AiLabels.Label("openOrders") })
                Assert.IsFalse(label.Any(char.IsDigit), label);
            Assert.IsNull(AiLabels.Label("somethingStrange"));
            Assert.IsNull(AiLabels.Label("contractId"));
            // nothing to show
            StringAssert.EndsWith(AnswerVerifier.Fallback(new List<VerifierToolResult>()), "نتیجه‌ای از ابزارها در دسترس نیست.");
            Assert.IsTrue(Verifier.Verify(AnswerVerifier.Fallback(new List<VerifierToolResult>()), "", new List<VerifierToolResult>()).Verified);
        }

        [TestMethod]
        public void Verifier_Numeric_Strings_Are_Never_Cut_In_The_Template()
        {
            string longCode = "1405" + new string('7', 120);
            var results = new List<VerifierToolResult> { Tool("{\"title\":\"" + longCode + "\"}") };
            StringAssert.Contains(AnswerVerifier.Fallback(results), longCode);
            string longText = new string('م', 200);
            var text = new List<VerifierToolResult> { Tool("{\"title\":\"" + longText + "\"}") };
            Assert.IsFalse(AnswerVerifier.Fallback(text).Contains(longText));
        }

        [TestMethod]
        public void Verifier_Digits_Of_Keys_Are_Allowed()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"payablesDue30\":1250000}") };
            Assert.IsTrue(Verifier.Verify("بدهی سررسید ۳۰ روز آینده 1250000 ریال است.", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("بدهی سررسید ۴۵ روز آینده", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_Keeps_The_Sign()
        {
            Assert.IsFalse(Verifier.Verify("مانده -5", "", new List<VerifierToolResult> { Tool("{\"balance\":5}") }).Verified);
            Assert.IsFalse(Verifier.Verify("مانده 5", "", new List<VerifierToolResult> { Tool("{\"balance\":-5}") }).Verified);
            Assert.IsTrue(Verifier.Verify("مانده -5 ریال", "", new List<VerifierToolResult> { Tool("{\"balance\":-5}") }).Verified);
            Assert.IsTrue(Verifier.Verify("مانده (-5)", "", new List<VerifierToolResult> { Tool("{\"balance\":-5}") }).Verified);
            // a hyphen inside a date or a range is a separator, not a sign
            Assert.IsTrue(Verifier.Verify("از 1405-08-03", "", new List<VerifierToolResult> { Tool("{\"d\":\"1405/08/03\"}") }).Verified);
        }

        [TestMethod]
        public void Verifier_Scale_Words_Multiply_And_Percent_Needs_The_Number()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"amount\":5000000000,\"rate\":5}") };
            Assert.IsTrue(Verifier.Verify("مبلغ 5 میلیارد ریال", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("مبلغ 5 میلیون ریال", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("نرخ 5 میلیارد", "", new List<VerifierToolResult> { Tool("{\"rate\":5}") }).Verified);
            Assert.IsTrue(Verifier.Verify("نرخ 5 درصد", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("نرخ ۵٪ و 5%", "", tools).Verified);
            Assert.IsFalse(Verifier.Verify("نرخ 6 درصد", "", tools).Verified);
            Assert.IsTrue(Verifier.Verify("مبلغ 2.5 میلیون", "", new List<VerifierToolResult> { Tool("{\"a\":2500000}") }).Verified);
        }

        [TestMethod]
        public void Verifier_Spelled_Out_Numbers_With_A_Scale_Word_Are_Read_Back()
        {
            var tools = new List<VerifierToolResult> { Tool("{\"amount\":5000000000}") };
            Assert.IsTrue(Verifier.Verify("مبلغ پنج میلیارد ریال", "", tools).Verified);
            VerificationResult wrong = Verifier.Verify("مبلغ شش میلیارد ریال", "", tools);
            Assert.IsFalse(wrong.Verified);
            CollectionAssert.AreEqual(new List<string> { "شش میلیارد" }, wrong.OffendingTokens);
            Assert.IsTrue(Verifier.Verify("سی و هفت میلیون و سیصد و شصت و پنج هزار و ششصد و بیست و پنج", "", new List<VerifierToolResult> { Tool("{\"v\":37365625}") }).Verified);
            Assert.IsTrue(Verifier.Verify("هزار میلیارد", "", new List<VerifierToolResult> { Tool("{\"v\":1000000000000}") }).Verified);
            Assert.IsFalse(Verifier.Verify("هزار میلیارد", "", tools).Verified);
            // spelled-out numbers without a scale word are not read
            Assert.IsTrue(Verifier.Verify("پنج مورد", "", tools).Verified);
        }

        [TestMethod]
        public void Verifier_Earlier_Results_Of_The_Window_Allow_Follow_Ups()
        {
            var none = new List<VerifierToolResult>();
            Assert.IsFalse(Verifier.Verify("همان 1,500,000", "تکرار کن", none).Verified);
            Assert.IsTrue(Verifier.Verify("همان 1,500,000", "تکرار کن", none, new List<string> { "{\"stockValue\":1500000}" }).Verified);
            Assert.IsFalse(Verifier.Verify("همان 1,500,001", "تکرار کن", none, new List<string> { "{\"stockValue\":1500000}" }).Verified);
        }

        [TestMethod]
        public void Numbers_Merged_Value_Canonical_Forms()
        {
            Assert.AreEqual("1500000.5", AiNumbers.Tokens("1,500,000.50", false)[0].Canonical);
            Assert.AreEqual("-5", AiNumbers.Tokens("-5", false)[0].Canonical);
            Assert.AreEqual("0", AiNumbers.Tokens("-0", false)[0].Canonical);
            Assert.AreEqual("5000000000", AiNumbers.Tokens("5 میلیارد", false)[0].Canonical);
            Assert.AreEqual("5", AiNumbers.Tokens("5 میلیارد", false)[0].Plain);
            Assert.IsTrue(AiNumbers.Tokens("5٪", false)[0].Percent);
            Assert.AreEqual(3, AiNumbers.Tokens("1405/08/03", false).Count);
        }

        // ------------------------------------------------------------------------------------------ review fixes: words, shaper, helpers

        [TestMethod]
        public void Words_Speech_Sign_Percent_Time_Codes_And_Ordinals()
        {
            Assert.AreEqual("منفی پنج", Words.SpeechNormalize("-5"));
            Assert.AreEqual("مانده منفی پنج ریال", Words.SpeechNormalize("مانده -5 ریال"));
            Assert.AreEqual("پنج درصد", Words.SpeechNormalize("5%"));
            Assert.AreEqual("دوازده ممیز پنج درصد", Words.SpeechNormalize("12.5٪"));
            Assert.AreEqual("ساعت چهارده و سی دقیقه", Words.SpeechNormalize("ساعت 14:30"));
            Assert.AreEqual("ساعت نه", Words.SpeechNormalize("ساعت 9:00"));
            Assert.AreEqual("سفارش PO هزار و چهارصد و پنج صفر صفر یک دو", Words.SpeechNormalize("سفارش PO-1405-0012"));
            Assert.AreEqual("کالای صفر دو یک سه صفر صفر صفر نه صفر یک صفر صفر صفر سه", Words.SpeechNormalize("کالای 02130009010003"));
            Assert.AreEqual("اول", Words.Ordinal(1));
            Assert.AreEqual("دوم", Words.Ordinal(2));
            Assert.AreEqual("سوم", Words.Ordinal(3));
            Assert.AreEqual("سیزدهم", Words.Ordinal(13));
            Assert.AreEqual("بیست و یکم", Words.Ordinal(21));
            Assert.AreEqual("بیست و سوم", Words.Ordinal(23));
            Assert.AreEqual("سی‌ام", Words.Ordinal(30));
            Assert.AreEqual("سی و یکم", Words.Ordinal(31));
            Assert.IsFalse(Words.SpeechNormalize("سفارش PO-1405-0012 به مبلغ 37,365,625 در 1405/08/03 ساعت 14:30 با -5%").Any(char.IsDigit));
        }

        [TestMethod]
        public void Shaper_A_Default_Date_Never_Breaks_A_Tool()
        {
            Assert.IsNull(AiToolShaper.ShamsiText("0001-01-01T00:00:00"));
            Assert.IsNull(AiToolShaper.ShamsiText("0001-01-01"));
            Assert.AreEqual("1405/08/03", AiToolShaper.ShamsiText("2026-10-25"));
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(new { at = new System.DateTime(1, 1, 1), name = "x" })), new List<ViewModels.NasbNiro.AiDtos.AiCitationDto>(), new AiToolDefinition { Name = "t" });
            Assert.IsNotNull(JsonNode.Parse(json));
        }

        [TestMethod]
        public void Registry_Tool_Names_From_The_Model_Are_Made_Safe()
        {
            Assert.AreEqual("?", ToolRegistry.SafeName(null));
            Assert.AreEqual("?", ToolRegistry.SafeName("  "));
            Assert.AreEqual(50, ToolRegistry.SafeName(new string('a', 80)).Length);
            Assert.AreEqual("ceo_dashboard", ToolRegistry.SafeName(" ceo_dashboard "));
        }

        [TestMethod]
        public void Endpoint_Urls_Must_Be_On_The_Internal_Network()
        {
            foreach (string ok in new[] { "http://127.0.0.1:5091", "http://localhost:5091", "http://10.1.2.3:8000", "http://172.16.0.5", "http://172.31.255.1", "http://192.168.1.20:11434", "http://[::1]:5091", "http://[fd12:3456::1]:80", "http://llm-server:8000", "https://ai.local:8443", "http://gpu.company.LOCAL" })
                Assert.IsNotNull(AiAdminService.CleanUrl(ok), ok);
            foreach (string bad in new[] { "http://example.com", "https://api.openai.com/v1", "http://8.8.8.8", "http://172.32.0.1", "http://172.15.0.1", "http://192.169.1.1", "http://11.0.0.1", "http://[2001:db8::1]", "http://server.corp.example" })
            {
                Assert.IsNull(AiAdminService.CleanUrl(bad, out bool external), bad);
                Assert.IsTrue(external, bad);
            }
            Assert.IsNull(AiAdminService.CleanUrl("http://user:pw@127.0.0.1", out bool e2));
            Assert.IsFalse(e2);
        }

        [TestMethod]
        public void Endpoint_Breaker_Skips_A_Recently_Failed_Endpoint_Unless_All_Failed()
        {
            var now = new System.DateTimeOffset(2026, 10, 25, 12, 0, 0, System.TimeSpan.Zero);
            var minute = System.TimeSpan.FromSeconds(60);
            var down = new Entities.Model.NasbNiro.Ai.AiModelEndpoint { Id = 1, LastHealthOk = false, LastHealthAt = now.AddSeconds(-10) };
            var healed = new Entities.Model.NasbNiro.Ai.AiModelEndpoint { Id = 2, LastHealthOk = false, LastHealthAt = now.AddSeconds(-120) };
            var fresh = new Entities.Model.NasbNiro.Ai.AiModelEndpoint { Id = 3 };
            CollectionAssert.AreEqual(new[] { 2, 3 }, AiEndpointRunner.Candidates(new List<Entities.Model.NasbNiro.Ai.AiModelEndpoint> { down, healed, fresh }, now, minute).Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1 }, AiEndpointRunner.Candidates(new List<Entities.Model.NasbNiro.Ai.AiModelEndpoint> { down }, now, minute).Select(e => e.Id).ToArray());
        }

        [TestMethod]
        public void Voice_Uploads_Are_Checked_By_Media_Type_And_Signature()
        {
            byte[] wav = new byte[44];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
            byte[] webm = { 0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] ogg = System.Text.Encoding.ASCII.GetBytes("OggS\0\0\0\0\0\0\0\0\0");
            byte[] text = System.Text.Encoding.UTF8.GetBytes("this is just a text file");
            Assert.IsTrue(AiAudioCheck.IsAllowed("audio/wav", wav));
            Assert.IsTrue(AiAudioCheck.IsAllowed("audio/webm;codecs=opus", webm));
            Assert.IsTrue(AiAudioCheck.IsAllowed("audio/webm; codecs=opus", webm));
            Assert.IsTrue(AiAudioCheck.IsAllowed("audio/ogg", ogg));
            Assert.IsTrue(AiAudioCheck.IsAllowed(null, wav));
            Assert.IsFalse(AiAudioCheck.IsAllowed("text/plain", text));
            Assert.IsFalse(AiAudioCheck.IsAllowed("audio/wav", text));
            Assert.IsFalse(AiAudioCheck.IsAllowed("text/plain", wav));
            Assert.IsFalse(AiAudioCheck.IsAllowed("audio/wav", new byte[5]));
            Assert.IsFalse(AiAudioCheck.IsAllowed("not a type", wav));
            Assert.AreEqual("audio/webm", SpeechToTextClient.MediaType("audio/webm;codecs=opus").MediaType);
            Assert.AreEqual("application/octet-stream", SpeechToTextClient.MediaType("???").MediaType);
            Assert.AreEqual("application/octet-stream", SpeechToTextClient.MediaType(null).MediaType);
        }

        // ------------------------------------------------------------------------------------------ the chat loop with a fake model

        private sealed class NoServices : System.IServiceProvider
        {
            public object GetService(System.Type serviceType) => null;
        }

        private sealed class FakeUser : Services.NasbNiro.Inventory.IInventoryUserContext
        {
            private readonly string[] _roles;
            public FakeUser(params string[] roles) { _roles = roles; }
            public int UserId => 7;
            public bool IsInRole(string role) => role.Split(',').Any(r => _roles.Contains(r.Trim(), System.StringComparer.OrdinalIgnoreCase));
        }

        private sealed class FakeLlm : ILlmClient
        {
            private readonly System.Func<int, IReadOnlyList<LlmMessage>, IReadOnlyList<LlmToolSpec>, LlmResult> _next;
            public int Calls;
            public List<bool> ToolsOffered = new List<bool>();
            public FakeLlm(System.Func<int, IReadOnlyList<LlmMessage>, IReadOnlyList<LlmToolSpec>, LlmResult> next) { _next = next; }
            public Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolSpec> tools, CancellationToken ct)
            {
                Calls++;
                ToolsOffered.Add(tools != null);
                return Task.FromResult(_next(Calls, messages, tools));
            }
        }

        private static LlmResult Call(string name, string args = "{}") => new LlmResult
        {
            Ok = true, Model = "fake", ToolCalls = new List<LlmToolCall> { new LlmToolCall { Id = "c" + name, Name = name, ArgumentsJson = args } },
        };

        private static LlmResult Say(string text) => new LlmResult { Ok = true, Model = "fake", Content = text };

        private static ToolRegistry Registry(params string[] roles) => new ToolRegistry(new NoServices(), new FakeUser(roles));

        private static Task<AiLoopResult> Loop(ILlmClient llm, IToolRegistry registry, string question = "سلام", IReadOnlyList<string> earlier = null, int timeout = 30) =>
            AiChatLoop.RunAsync(llm, registry, Verifier, new List<LlmMessage> { new LlmMessage { Role = "user", Content = question } }, new List<LlmToolSpec>(), question, earlier ?? new List<string>(), timeout, CancellationToken.None);

        [TestMethod]
        public async Task Loop_Stops_After_Six_Rounds_And_Forces_An_Answer_Without_Tools()
        {
            var llm = new FakeLlm((n, m, t) => t == null ? Say("پاسخ نهایی") : Call("nope_tool"));
            AiLoopResult r = await Loop(llm, Registry("WH"));
            Assert.AreEqual(7, llm.Calls);
            CollectionAssert.AreEqual(new[] { true, true, true, true, true, true, false }, llm.ToolsOffered);
            Assert.AreEqual(6, r.Results.Count);
            Assert.AreEqual("پاسخ نهایی", r.Answer);
            Assert.IsTrue(r.Verified);
            Assert.IsFalse(r.Degraded);
            Assert.AreEqual("fake", r.Model);
        }

        [TestMethod]
        public async Task Loop_Unknown_Tool_Is_An_Error_Result_And_Not_Allowed()
        {
            var llm = new FakeLlm((n, m, t) => n == 1 ? Call(new string('x', 90)) : (n == 2 ? new LlmResult { Ok = true, Model = "fake", ToolCalls = new List<LlmToolCall> { new LlmToolCall { Id = "z", Name = null, ArgumentsJson = null } } } : Say("نتیجه‌ای نیست")));
            AiLoopResult r = await Loop(llm, Registry("WH"));
            Assert.AreEqual(2, r.Results.Count);
            Assert.AreEqual(50, r.Results[0].Result.Name.Length);
            Assert.AreEqual("?", r.Results[1].Result.Name);
            foreach (var x in r.Results)
            {
                Assert.IsFalse(x.Result.Allowed);
                Assert.AreEqual("{\"error\":true,\"message\":\"ابزار ناشناخته\"}", x.Result.Json);
                Assert.AreEqual(64, x.Result.Hash.Length);
            }
        }

        [TestMethod]
        public async Task Loop_Forbidden_Tool_Is_Refused_By_The_Registry_And_The_Answer_Is_Verified()
        {
            var llm = new FakeLlm((n, m, t) => n == 1 ? Call("cash_position") : Say(m.Last().Content.Contains("\"forbidden\":true") ? "شما به این اطلاعات دسترسی ندارید." : "؟"));
            AiLoopResult r = await Loop(llm, Registry("WH"), "نقدینگی");
            Assert.AreEqual(1, r.Results.Count);
            Assert.IsFalse(r.Results[0].Result.Allowed);
            Assert.AreEqual("{\"forbidden\":true,\"message\":\"شما به این اطلاعات دسترسی ندارید.\"}", r.Results[0].Result.Json);
            Assert.AreEqual("شما به این اطلاعات دسترسی ندارید.", r.Answer);
            Assert.IsTrue(r.Verified);
            Assert.IsNotNull(r.Verification);
        }

        [TestMethod]
        public async Task Loop_Degrades_When_No_Endpoint_Answers()
        {
            var llm = new FakeLlm((n, m, t) => new LlmResult { Ok = false, Error = "no enabled endpoint" });
            AiLoopResult r = await Loop(llm, Registry("WH"));
            Assert.IsTrue(r.Degraded);
            Assert.IsTrue(r.Verified);
            Assert.AreEqual("تحلیل هوشمند موقتاً در دسترس نیست؛ داشبورد اصلی همچنان در دسترس است.", r.Answer);
            Assert.AreEqual("no enabled endpoint", r.DegradedReason);
            Assert.IsNull(r.Verification);
        }

        [TestMethod]
        public async Task Loop_Degrades_On_Timeout()
        {
            var slow = new SlowLlm();
            AiLoopResult r = await Loop(slow, Registry("WH"), timeout: 1);
            Assert.IsTrue(r.Degraded);
            StringAssert.Contains(r.DegradedReason, "timeout");
        }

        private sealed class SlowLlm : ILlmClient
        {
            public async Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolSpec> tools, CancellationToken ct)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
                return null;
            }
        }

        [TestMethod]
        public async Task Loop_Verifier_Is_Applied_On_Every_Path()
        {
            // a plain answer with an invented number
            AiLoopResult plain = await Loop(new FakeLlm((n, m, t) => Say("مبلغ 987654321 ریال")), Registry("WH"));
            Assert.IsFalse(plain.Verified);
            StringAssert.StartsWith(plain.Answer, "پاسخ مدل قابل راستی‌آزمایی نبود");
            StringAssert.EndsWith(plain.Answer, "نتیجه‌ای از ابزارها در دسترس نیست.");

            // after a tool round
            AiLoopResult afterTool = await Loop(new FakeLlm((n, m, t) => n == 1 ? Call("nope_tool") : Say("مبلغ 987654321 ریال")), Registry("WH"));
            Assert.IsFalse(afterTool.Verified);
            Assert.IsFalse(afterTool.Answer.Contains("987654321"));

            // the forced last answer
            AiLoopResult forced = await Loop(new FakeLlm((n, m, t) => t == null ? Say("مبلغ 987654321 ریال") : Call("nope_tool")), Registry("WH"));
            Assert.IsFalse(forced.Verified);
            Assert.AreEqual(6, forced.Results.Count);

            // an empty answer is replaced by a fixed text
            AiLoopResult empty = await Loop(new FakeLlm((n, m, t) => Say("  ")), Registry("WH"));
            StringAssert.Contains(empty.Answer, "مدل پاسخی تولید نکرد");

            // a number of the question and of the earlier window passes
            AiLoopResult ok = await Loop(new FakeLlm((n, m, t) => Say("سفارش 120 و مبلغ 1,500,000")), Registry("WH"), "وضعیت سفارش 120", new List<string> { "{\"v\":1500000}" });
            Assert.IsTrue(ok.Verified);
        }
    }
}
