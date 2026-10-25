using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Services.NasbNiro.Ai
{
    /// <summary>The result of one tool call, as the verifier and the fallback text need it.</summary>
    public class VerifierToolResult
    {
        public string Name { get; set; }

        /// <summary>The Persian title of the tool.</summary>
        public string Title { get; set; }
        public string Json { get; set; }
    }

    public class VerificationResult
    {
        public bool Verified { get; set; }

        /// <summary>The answer to show: the model's own when it passed, else the fixed template with the direct results.</summary>
        public string Answer { get; set; }
        public List<string> OffendingTokens { get; set; } = new List<string>();
    }

    public interface IAnswerVerifier
    {
        /// <param name="toolResults">The tool results of this turn (they are also listed in the fixed template).</param>
        /// <param name="earlierResults">Tool results (JSON) of earlier turns inside the memory window of the conversation: their numbers may be repeated.</param>
        VerificationResult Verify(string answer, string question, IReadOnlyList<VerifierToolResult> toolResults, IReadOnlyList<string> earlierResults = null);
    }

    /// <summary>
    /// Principle 1 of the AI module: every number of an answer must come from a tool result or from the user's question.
    /// A number is compared only by its merged normalised value on both sides (no part matching): digits are normalised (Persian, Arabic-Indic),
    /// thousands separators are removed, "/", "-" and ":" split tokens (so 1405/08/03 is three tokens), leading and trailing zeros are ignored
    /// ("08" = "8", "12.50" = "12.5"), the sign is kept (-5 is not 5), a scale word multiplies ("5 میلیارد" is 5000000000), a percent sign needs the number itself.
    /// A list marker at the start of a line is exempt. Spelled-out numbers that contain a scale word («پنج میلیارد») are read back to a value and checked the same way;
    /// other spelled-out numbers are not checked (★ the model is told to write digits).
    /// </summary>
    public class AnswerVerifier : IAnswerVerifier
    {
        public const string NoResults = "نتیجه‌ای از ابزارها در دسترس نیست.";

        public VerificationResult Verify(string answer, string question, IReadOnlyList<VerifierToolResult> toolResults, IReadOnlyList<string> earlierResults = null)
        {
            toolResults ??= new List<VerifierToolResult>();
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (VerifierToolResult r in toolResults) AiNumbers.CollectAllowed(r.Json, allowed);
            if (earlierResults != null) foreach (string json in earlierResults) AiNumbers.CollectAllowed(json, allowed);
            AiNumbers.AddText(question ?? string.Empty, allowed);

            var offending = new List<string>();
            foreach (AiNumberToken t in AiNumbers.Tokens(answer ?? string.Empty, true))
            {
                if (allowed.Contains(t.Canonical)) continue;
                if (t.Percent && allowed.Contains(t.Plain)) continue;
                if (!offending.Contains(t.Original)) offending.Add(t.Original);
            }
            foreach (KeyValuePair<string, string> run in PersianNumberParser.ScaledRuns(answer))
            {
                if (allowed.Contains(AiNumbers.Canonical(run.Value))) continue;
                if (!offending.Contains(run.Key)) offending.Add(run.Key);
            }
            if (offending.Count == 0) return new VerificationResult { Verified = true, Answer = answer };
            return new VerificationResult { Verified = false, Answer = Fallback(toolResults), OffendingTokens = offending };
        }

        /// <summary>The fixed template: one line per tool called, with up to six scalar fields of its result under Persian labels. It passes its own check.</summary>
        public static string Fallback(IReadOnlyList<VerifierToolResult> toolResults)
        {
            var sb = new StringBuilder(AiText.Unverifiable);
            if (toolResults == null || toolResults.Count == 0) return sb.Append('\n').Append(NoResults).ToString();
            foreach (VerifierToolResult r in toolResults)
            {
                sb.Append('\n').Append("- ").Append(string.IsNullOrWhiteSpace(r.Title) ? r.Name : r.Title).Append(": ");
                sb.Append(string.Join("، ", Scalars(r.Json, 6).Select(s => s.Key + ": " + s.Value)));
            }
            return sb.ToString();
        }

        private static List<KeyValuePair<string, string>> Scalars(string json, int max)
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json ?? "{}");
                JsonElement root = doc.RootElement;
                var levels = new List<JsonElement> { root };
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in root.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Object) levels.Add(p.Value);
                        else if (p.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement first in p.Value.EnumerateArray().Take(1)) if (first.ValueKind == JsonValueKind.Object) levels.Add(first);
                        }
                    }
                }
                foreach (JsonElement level in levels)
                {
                    if (level.ValueKind != JsonValueKind.Object) continue;
                    foreach (JsonProperty p in level.EnumerateObject())
                    {
                        if (list.Count >= max) return list;
                        string label = AiLabels.Label(p.Name);
                        if (label == null) continue; // a key without a Persian label is never printed
                        string value;
                        switch (p.Value.ValueKind)
                        {
                            case JsonValueKind.Number: value = AiNumbers.JsonNumber(p.Value); break;
                            case JsonValueKind.String:
                                string s = p.Value.GetString() ?? string.Empty;
                                // numbers are never cut; plain text is
                                value = s.Any(char.IsDigit) ? s : AiText.Cut(s, 80);
                                break;
                            case JsonValueKind.True: value = "بله"; break;
                            case JsonValueKind.False: value = "خیر"; break;
                            default: continue;
                        }
                        list.Add(new KeyValuePair<string, string>(label, value));
                    }
                }
            }
            catch (JsonException)
            {
            }
            return list;
        }
    }

    /// <summary>Persian labels (no digits) for the keys of the tool results, built word by word from the camelCase key. A key with an unknown word has no label.</summary>
    public static class AiLabels
    {
        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["total"] = "جمع", ["count"] = "تعداد", ["amount"] = "مبلغ", ["value"] = "ارزش", ["balance"] = "مانده", ["open"] = "باز", ["overdue"] = "معوق", ["late"] = "تأخیری",
            ["due"] = "سررسید", ["days"] = "روز", ["day"] = "روز", ["months"] = "ماه", ["month"] = "ماه", ["weeks"] = "هفته", ["week"] = "هفته", ["year"] = "سال", ["date"] = "تاریخ",
            ["stock"] = "موجودی", ["item"] = "کالا", ["items"] = "کالاها", ["min"] = "حداقل", ["max"] = "حداکثر", ["minimum"] = "حداقل", ["below"] = "زیر", ["payables"] = "بدهی‌ها",
            ["payable"] = "بدهی", ["receivables"] = "مطالبات", ["receivable"] = "مطالبه", ["received"] = "دریافتی", ["not"] = "بدون",
            ["invoiced"] = "فاکتورشده", ["purchase"] = "خرید", ["orders"] = "سفارش‌ها", ["order"] = "سفارش", ["requests"] = "درخواست‌ها", ["request"] = "درخواست", ["pending"] = "منتظر",
            ["approval"] = "تأیید", ["approved"] = "تأییدشده", ["waiting"] = "در انتظار", ["projects"] = "پروژه‌ها", ["project"] = "پروژه", ["contracts"] = "قراردادها", ["contract"] = "قرارداد",
            ["active"] = "فعال", ["closed"] = "بسته", ["done"] = "انجام‌شده", ["cash"] = "نقد", ["bank"] = "بانک", ["and"] = "و", ["net"] = "خالص", ["gross"] = "ناخالص", ["debit"] = "بدهکار",
            ["credit"] = "بستانکار", ["cost"] = "هزینه", ["costs"] = "هزینه‌ها", ["price"] = "قیمت", ["rate"] = "نرخ", ["percent"] = "درصد", ["progress"] = "پیشرفت", ["planned"] = "برنامه‌ای",
            ["actual"] = "واقعی", ["spi"] = "شاخص زمان", ["cpi"] = "شاخص هزینه", ["quality"] = "کیفیت", ["ncr"] = "گزارش عدم انطباق", ["capa"] = "اقدام اصلاحی", ["calibration"] = "کالیبراسیون",
            ["guarantees"] = "ضمانت‌نامه‌ها", ["guarantee"] = "ضمانت‌نامه", ["expiring"] = "رو به انقضا", ["changes"] = "تغییرات", ["change"] = "تغییر", ["sales"] = "فروش", ["service"] = "خدمات",
            ["production"] = "تولید", ["customers"] = "مشتریان", ["customer"] = "مشتری", ["suppliers"] = "تأمین‌کنندگان", ["supplier"] = "تأمین‌کننده", ["name"] = "نام", ["title"] = "عنوان",
            ["number"] = "شماره", ["code"] = "کد", ["status"] = "وضعیت", ["unit"] = "واحد", ["quantity"] = "مقدار", ["qty"] = "مقدار", ["on"] = "در", ["hand"] = "دست", ["available"] = "قابل‌استفاده",
            ["reserved"] = "رزروشده", ["warehouse"] = "انبار", ["finance"] = "مالی", ["inventory"] = "انبار", ["alerts"] = "هشدارها", ["alert"] = "هشدار", ["events"] = "رویدادها", ["milestones"] = "نقاط عطف",
            ["next"] = "بعدی", ["last"] = "آخرین", ["first"] = "اولین", ["current"] = "جاری", ["opening"] = "ابتدای دوره", ["closing"] = "پایان دوره", ["accounts"] = "حساب‌ها", ["account"] = "حساب",
            ["trial"] = "تراز", ["voucher"] = "سند", ["vouchers"] = "اسناد", ["documents"] = "مدارک", ["document"] = "مدرک", ["approvals"] = "تأییدها", ["tasks"] = "کارها", ["valid"] = "معتبر",
            ["expired"] = "منقضی", ["failed"] = "ناموفق", ["passed"] = "موفق", ["rejected"] = "ردشده", ["accepted"] = "پذیرفته‌شده", ["draft"] = "پیش‌نویس", ["paid"] = "پرداخت‌شده",
            ["unpaid"] = "پرداخت‌نشده", ["vat"] = "مالیات بر ارزش افزوده", ["tax"] = "مالیات", ["profit"] = "سود", ["margin"] = "حاشیه", ["revenue"] = "درآمد", ["income"] = "درآمد",
            ["expense"] = "هزینه", ["of"] = "از", ["by"] = "بر اساس", ["to"] = "تا", ["from"] = "از", ["in"] = "در", ["with"] = "با", ["without"] = "بدون", ["per"] = "به‌ازای", ["only"] = "فقط",
            ["is"] = "است", ["has"] = "دارد", ["id"] = null,
        };

        /// <summary>The Persian label of a JSON key, or null when a word of it is unknown (digits are spelled: due30 is «سررسید سی»). Never contains a digit.</summary>
        public static string Label(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Equals("link", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Id", StringComparison.Ordinal) || key.Equals("id", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = new List<string>();
            var words = new PersianNumberWords();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(key, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
            {
                if (char.IsDigit(m.Value[0]))
                {
                    if (m.Value.Length > 9) return null;
                    parts.Add(words.ToWords(long.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)));
                    continue;
                }
                if (!Words.TryGetValue(m.Value, out string w) || w == null) return null;
                parts.Add(w);
            }
            // "stockValue" is «ارزش موجودی» (the noun phrase is turned round); a key with digits keeps its order: "payablesDue30" is «بدهی‌ها سررسید سی»
            if (!key.Any(char.IsDigit)) parts.Reverse();
            string label = string.Join(" ", parts);
            return label.Length == 0 || label.Any(char.IsDigit) ? null : label;
        }
    }
}
