using System.Collections.Generic;
using System.Linq;
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
            StringAssert.Contains(r.Answer, "داشبورد مدیرعامل: stockValue: 1500000، open: 12، title: x");
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
            Assert.AreEqual("موعد روز سه آبان سال هزار و چهارصد و پنج است", Words.SpeechNormalize("موعد 1405/08/03 است"));
            Assert.AreEqual("موعد روز سه آبان سال هزار و چهارصد و پنج است", Words.SpeechNormalize("موعد ۱۴۰۵/۰۸/۰۳ است"));
            Assert.AreEqual("نرخ دوازده ممیز پنج درصد", Words.SpeechNormalize("نرخ 12.5 درصد"));
            Assert.AreEqual("بدون عدد", Words.SpeechNormalize("بدون عدد"));
            Assert.AreEqual("روز بیست و نه اسفند سال هزار و چهارصد", Words.SpeechNormalize("1400/12/29").Replace("چهارصد و صفر", "چهارصد"));
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
    }
}
