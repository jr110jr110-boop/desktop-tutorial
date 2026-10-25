using System;
using System.Collections.Generic;
using System.Globalization;
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
        VerificationResult Verify(string answer, string question, IReadOnlyList<VerifierToolResult> toolResults);
    }

    /// <summary>
    /// Principle 1 of the AI module: every number of an answer must come from a tool result of this turn or from the user's question.
    /// Digits are normalised (Persian, Arabic-Indic), thousands separators removed, "/", "-" and ":" split tokens (so 1405/08/03 is three tokens),
    /// leading and trailing zeros are ignored ("08" = "8", "12.50" = "12.5"), a list marker at the start of a line is exempt.
    /// </summary>
    public class AnswerVerifier : IAnswerVerifier
    {
        public VerificationResult Verify(string answer, string question, IReadOnlyList<VerifierToolResult> toolResults)
        {
            toolResults ??= new List<VerifierToolResult>();
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (VerifierToolResult r in toolResults) AiNumbers.CollectAllowed(r.Json, allowed);
            AiNumbers.AddText(question ?? string.Empty, allowed);

            var offending = new List<string>();
            foreach (AiNumberToken t in AiNumbers.Tokens(answer ?? string.Empty, true))
            {
                if (allowed.Contains(t.Canonical)) continue;
                if (t.Parts.Count > 0 && t.Parts.All(allowed.Contains)) continue;
                if (!offending.Contains(t.Original)) offending.Add(t.Original);
            }
            if (offending.Count == 0) return new VerificationResult { Verified = true, Answer = answer };
            return new VerificationResult { Verified = false, Answer = Fallback(toolResults), OffendingTokens = offending };
        }

        /// <summary>The fixed template: one line per tool called, with up to six scalar fields of its result.</summary>
        public static string Fallback(IReadOnlyList<VerifierToolResult> toolResults)
        {
            var sb = new StringBuilder(AiText.Unverifiable);
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
                        if (p.Name == "link") continue;
                        string value;
                        switch (p.Value.ValueKind)
                        {
                            case JsonValueKind.Number: value = p.Value.GetRawText(); break;
                            case JsonValueKind.String: value = AiText.Cut(p.Value.GetString(), 80); break;
                            case JsonValueKind.True: value = "بله"; break;
                            case JsonValueKind.False: value = "خیر"; break;
                            default: continue;
                        }
                        list.Add(new KeyValuePair<string, string>(p.Name, value));
                    }
                }
            }
            catch (JsonException)
            {
            }
            return list;
        }
    }
}
