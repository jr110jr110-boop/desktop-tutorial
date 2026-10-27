using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;

namespace Services.NasbNiro.Ai
{
    /// <summary>The service says the user may not do this (or the tool's role list does not contain the user's roles).</summary>
    public sealed class AiToolForbiddenException : Exception
    {
        public AiToolForbiddenException(string message = null) : base(message ?? AiText.ToolForbidden)
        {
        }
    }

    /// <summary>The service answered with a rule violation or a not-found message; the model gets it as an error result.</summary>
    public sealed class AiToolFailureException : Exception
    {
        public AiToolFailureException(string message) : base(message)
        {
        }
    }

    /// <summary>The request itself is refused (for example an unknown drill key): the model gets an error result and the call is recorded as not allowed.</summary>
    public sealed class AiToolRejectedException : Exception
    {
        public AiToolRejectedException(string message) : base(message)
        {
        }
    }

    /// <summary>Where the links and citations of the items of a list come from: the JSON property that holds the list, the record kind and the front-end path.</summary>
    public class AiItemLinkRule
    {
        public string Property { get; set; }
        public string Type { get; set; }

        /// <summary>The Persian word put in front of the number or title in the citation.</summary>
        public string TitlePrefix { get; set; }

        /// <summary>A path with {property} holes, for example /procurement/orders/{id}.</summary>
        public string Template { get; set; }
        public string IdKey { get; set; } = "id";

        /// <summary>When set, the type of the citation is read from this property of the item (a list of mixed records).</summary>
        public string TypeKey { get; set; }
    }

    /// <summary>What a tool delegate may return instead of a plain object: the data plus citations it already knows.</summary>
    public class AiToolOutput
    {
        public object Data { get; set; }
        public List<AiCitationDto> Citations { get; set; } = new List<AiCitationDto>();

        /// <summary>A thing the front end should do after the answer (the ui_* tools): the registry puts it into the result of the call and the chat into the response.</summary>
        public AiActionDto Action { get; set; }
    }

    public class AiToolContext
    {
        public IServiceProvider Services { get; set; }
        public IToolRegistry Registry { get; set; }
        public IInventoryUserContext User { get; set; }

        public T Get<T>() => Services.GetRequiredService<T>();
    }

    /// <summary>One read-only tool the model may call. The delegate calls an existing service in the request scope, so its role checks and row filters apply.</summary>
    public class AiToolDefinition
    {
        /// <summary>snake_case English.</summary>
        public string Name { get; set; }

        /// <summary>The short Persian title (used in the fixed fallback text).</summary>
        public string Title { get; set; }
        public string Description { get; set; }
        public string SchemaJson { get; set; } = "{\"type\":\"object\",\"properties\":{}}";

        /// <summary>Comma separated; empty = every signed-in user.</summary>
        public string RequiredRoles { get; set; }
        public string CitationType { get; set; } = "Dashboard";

        /// <summary>The page of the front end the result belongs to.</summary>
        public string PageLink { get; set; }
        public List<AiItemLinkRule> ItemLinks { get; set; } = new List<AiItemLinkRule>();
        public Func<AiToolContext, JsonElement, CancellationToken, Task<object>> Run { get; set; }
    }

    public class AiToolResult
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public bool Allowed { get; set; }

        /// <summary>The text the model receives (compact JSON, camelCase, at most 6000 characters).</summary>
        public string Json { get; set; }
        public string Hash { get; set; }
        public int Chars { get; set; }
        public int DurationMs { get; set; }
        public List<AiCitationDto> Citations { get; set; } = new List<AiCitationDto>();

        /// <summary>Set only by a ui_* tool that accepted its arguments; null for every other call and for a refused one.</summary>
        public AiActionDto Action { get; set; }
    }

    public interface IToolRegistry
    {
        /// <summary>The tools whose required roles contain a role of the caller.</summary>
        IReadOnlyList<AiToolDefinition> Offered();
        AiToolDefinition Find(string name);
        Task<AiToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct);
    }

    /// <summary>Serialises, cleans and shortens a tool result (pure, so it is unit tested).</summary>
    public static class AiToolShaper
    {
        public const int MaxChars = 6000;
        public const int ListLimit = 10;

        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = 64,
        };

        // personal and banking data never goes to the model
        private static readonly string[] Hidden =
        {
            "iban", "nationalid", "nationalcode", "economiccode", "password", "token", "rowversion", "secret",
            "phone", "mobile", "address", "salary", "wage", "accountnumber",
        };
        private static readonly Regex DateOnly = new Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DateTimeText = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly string[] NumberKeys = { "number", "code", "documentNumber", "projectCode", "itemCode", "no" };
        private static readonly string[] TitleKeys = { "title", "name", "itemTitle", "providerName", "contractTitle", "customerName", "text" };

        public static JsonNode ToNode(object data)
        {
            if (data is JsonNode node) return node.DeepClone();
            return JsonSerializer.SerializeToNode(data, Options) ?? new JsonObject();
        }

        /// <summary>Removes hidden properties, turns ISO dates into Shamsi text (the model must only see Shamsi dates).</summary>
        public static JsonNode Clean(JsonNode node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (string key in o.Select(p => p.Key).ToList())
                    {
                        string lower = key.ToLowerInvariant();
                        if (Hidden.Any(h => lower.Contains(h))) { o.Remove(key); continue; }
                        JsonNode child = o[key];
                        JsonNode cleaned = Clean(child);
                        if (!ReferenceEquals(cleaned, child)) o[key] = cleaned;
                    }
                    return o;
                case JsonArray a:
                    for (int i = 0; i < a.Count; i++)
                    {
                        JsonNode child = a[i];
                        JsonNode cleaned = Clean(child);
                        if (!ReferenceEquals(cleaned, child)) a[i] = cleaned;
                    }
                    return a;
                case JsonValue v when v.TryGetValue(out string text):
                    string shamsi = ShamsiText(text);
                    return shamsi == null ? node : JsonValue.Create(shamsi);
                default:
                    return node;
            }
        }

        /// <summary>The first day of the Persian calendar (622-03-22): earlier dates (an empty DateTime) are left as they are.</summary>
        private static readonly DateTime FirstPersianDay = new DateTime(622, 3, 22);

        public static string ShamsiText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 10 || text.Length > 40) return null;
            try
            {
                if (DateOnly.IsMatch(text) && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                    return d < FirstPersianDay ? null : InventoryText.PersianDate(d);
                if (DateTimeText.IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset dto))
                {
                    DateTime local = dto.LocalDateTime;
                    if (local < FirstPersianDay) return null;
                    string date = InventoryText.PersianDate(local);
                    return local.TimeOfDay == TimeSpan.Zero ? date : date + " " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException)
            {
                // one odd date never breaks a tool
            }
            return null;
        }

        /// <summary>Lists longer than the limit keep their first items; the property next to them says how many there were (totalCount for a root list).</summary>
        public static JsonNode Limit(JsonNode node, int limit)
        {
            if (node is JsonArray root)
            {
                int total = root.Count;
                var wrapped = new JsonObject { ["totalCount"] = total };
                JsonArray items = CutArray(root, limit);
                wrapped["items"] = items;
                LimitChildren(wrapped, limit);
                return wrapped;
            }
            if (node is JsonObject o) LimitChildren(o, limit);
            return node;
        }

        private static JsonArray CutArray(JsonArray source, int limit)
        {
            var result = new JsonArray();
            foreach (JsonNode item in source.Take(limit).ToList()) result.Add(item?.DeepClone());
            return result;
        }

        private static void LimitChildren(JsonObject o, int limit)
        {
            foreach (string key in o.Select(p => p.Key).ToList())
            {
                JsonNode child = o[key];
                if (child is JsonArray a)
                {
                    foreach (JsonNode item in a) if (item is JsonObject io) LimitChildren(io, limit);
                    if (a.Count > limit)
                    {
                        int total = a.Count;
                        o[key] = CutArray(a, limit);
                        if (!o.ContainsKey(key + "TotalCount") && key != "items") o[key + "TotalCount"] = total;
                    }
                }
                else if (child is JsonObject c) LimitChildren(c, limit);
            }
        }

        /// <summary>Adds a "link" to the items of the lists named by the rules and returns the citations.</summary>
        public static List<AiCitationDto> AddLinks(JsonNode root, AiToolDefinition tool)
        {
            var citations = new List<AiCitationDto>();
            if (root is not JsonObject obj) return citations;
            if (!string.IsNullOrEmpty(tool.PageLink))
            {
                if (!obj.ContainsKey("link")) obj["link"] = tool.PageLink;
                citations.Add(new AiCitationDto { Type = tool.CitationType, Id = tool.Name, Title = tool.Title, Link = tool.PageLink });
            }
            foreach (AiItemLinkRule rule in tool.ItemLinks)
            {
                foreach (JsonArray array in FindArrays(obj, rule.Property, 0))
                {
                    foreach (JsonNode item in array)
                    {
                        if (item is not JsonObject io) continue;
                        string id = Scalar(io, rule.IdKey);
                        if (id == null) continue;
                        string link = Regex.Replace(rule.Template, @"\{(\w+)\}", m => Scalar(io, m.Groups[1].Value) ?? string.Empty);
                        if (!io.ContainsKey("link")) io["link"] = link;
                        if (citations.Count < 12)
                            citations.Add(new AiCitationDto { Type = (rule.TypeKey != null ? Scalar(io, rule.TypeKey) : null) ?? rule.Type, Id = id, Title = CitationTitle(io, rule.TitlePrefix), Link = link });
                    }
                }
            }
            return citations;
        }

        private static IEnumerable<JsonArray> FindArrays(JsonObject o, string property, int depth)
        {
            foreach (KeyValuePair<string, JsonNode> p in o.ToList())
            {
                if (p.Value is JsonArray a && string.Equals(p.Key, property, StringComparison.OrdinalIgnoreCase)) yield return a;
                else if (p.Value is JsonObject child && depth < 2)
                    foreach (JsonArray inner in FindArrays(child, property, depth + 1)) yield return inner;
            }
        }

        private static string Scalar(JsonObject o, string key)
        {
            foreach (KeyValuePair<string, JsonNode> p in o)
            {
                if (!string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase) || p.Value is not JsonValue v) continue;
                if (v.TryGetValue(out string s)) return string.IsNullOrWhiteSpace(s) ? null : s;
                return v.ToJsonString(Options).Trim('"');
            }
            return null;
        }

        private static string CitationTitle(JsonObject o, string prefix)
        {
            string a = NumberKeys.Select(k => Scalar(o, k)).FirstOrDefault(x => x != null);
            string b = TitleKeys.Select(k => Scalar(o, k)).FirstOrDefault(x => x != null && x != a);
            string title = (prefix + " " + a).Trim();
            if (b != null) title = (title + " — " + AiText.Cut(b, 80)).Trim(' ', '—');
            return AiText.Cut(title, 160);
        }

        /// <summary>Compact text of at most <see cref="MaxChars"/> characters: lists shrink first, then long strings, then trailing properties.</summary>
        public static string Fit(JsonNode cleaned, List<AiCitationDto> citationsOut, AiToolDefinition tool)
        {
            int[] limits = { ListLimit, 5, 3, 2, 1 };
            JsonNode last = null;
            foreach (int limit in limits)
            {
                JsonNode candidate = Limit(cleaned.DeepClone(), limit);
                List<AiCitationDto> citations = AddLinks(candidate, tool);
                string text = candidate.ToJsonString(Options);
                last = candidate;
                citationsOut.Clear();
                citationsOut.AddRange(citations);
                if (text.Length <= MaxChars) return text;
            }
            if (last is JsonObject o)
            {
                ShortenStrings(o, 200);
                string text = o.ToJsonString(Options);
                if (text.Length <= MaxChars) return text;
                var keep = new JsonObject();
                foreach (KeyValuePair<string, JsonNode> p in o.ToList())
                {
                    keep[p.Key] = p.Value?.DeepClone();
                    if (keep.ToJsonString(Options).Length > MaxChars - 40) { keep.Remove(p.Key); break; }
                }
                keep["truncated"] = true;
                return keep.ToJsonString(Options);
            }
            return last?.ToJsonString(Options) ?? "{}";
        }

        private static void ShortenStrings(JsonNode node, int max)
        {
            if (node is JsonObject o)
            {
                foreach (string key in o.Select(p => p.Key).ToList())
                {
                    if (o[key] is JsonValue v && v.TryGetValue(out string s) && s.Length > max) o[key] = s.Substring(0, max);
                    else ShortenStrings(o[key], max);
                }
            }
            else if (node is JsonArray a)
            {
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue(out string s) && s.Length > max) a[i] = s.Substring(0, max);
                    else ShortenStrings(a[i], max);
                }
            }
        }

        public static string ForbiddenJson(string message = null) =>
            new JsonObject { ["forbidden"] = true, ["message"] = message ?? AiText.ToolForbidden }.ToJsonString(Options);

        public static string ErrorJson(string message) =>
            new JsonObject { ["error"] = true, ["message"] = message }.ToJsonString(Options);

        /// <summary>Unwraps a service answer: data on success, forbidden for a "no permission" message, a failure for any other message.</summary>
        public static T Unwrap<T>(ResponseDto<T> response)
        {
            if (response == null) throw new AiToolFailureException("پاسخی دریافت نشد.");
            if (response.Success) return response.Data;
            string message = response.Message;
            if (message != null && message.Contains("مجوز")) throw new AiToolForbiddenException();
            throw new AiToolFailureException(string.IsNullOrWhiteSpace(message) ? "درخواست انجام نشد." : message);
        }
    }

    public class ToolRegistry : IToolRegistry
    {
        // the A1 tools, the tools of the CEO dashboard and the UI action tools (release D1)
        private static readonly IReadOnlyList<AiToolDefinition> All = AiTools.Build().Concat(AiCeoTools.Build()).Concat(AiUiTools.Build()).ToList();

        public const int MaxNameLength = 50;

        private readonly IServiceProvider _services;
        private readonly IInventoryUserContext _user;
        private readonly ILogger<ToolRegistry> _logger;

        public ToolRegistry(IServiceProvider services, IInventoryUserContext user, ILogger<ToolRegistry> logger = null)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _logger = logger;
        }

        /// <summary>A tool name from the model, safe to store: at most 50 characters, "?" when empty.</summary>
        public static string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? "?" : AiText.Cut(name.Trim(), MaxNameLength);

        public static IReadOnlyList<AiToolDefinition> Definitions => All;

        public IReadOnlyList<AiToolDefinition> Offered() =>
            All.Where(t => string.IsNullOrWhiteSpace(t.RequiredRoles) || _user.IsInRole(t.RequiredRoles)).ToList();

        public AiToolDefinition Find(string name) => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        public async Task<AiToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct)
        {
            name = SafeName(name);
            AiToolDefinition tool = Find(name);
            if (tool == null) return UnknownResult(name);
            var result = new AiToolResult { Name = name, Title = tool.Title, Allowed = true };
            var sw = Stopwatch.StartNew();
            string json;
            var citations = new List<AiCitationDto>();
            if (!string.IsNullOrWhiteSpace(tool.RequiredRoles) && !_user.IsInRole(tool.RequiredRoles))
            {
                result.Allowed = false;
                json = AiToolShaper.ForbiddenJson();
            }
            else
            {
                try
                {
                    using JsonDocument args = ParseArguments(argumentsJson);
                    var context = new AiToolContext { Services = _services, Registry = this, User = _user };
                    object data = await tool.Run(context, args.RootElement, ct);
                    List<AiCitationDto> extra = null;
                    if (data is AiToolOutput output) { extra = output.Citations; result.Action = output.Action; data = output.Data; }
                    JsonNode node = AiToolShaper.Clean(AiToolShaper.ToNode(data));
                    json = AiToolShaper.Fit(node, citations, tool);
                    if (extra != null) foreach (AiCitationDto c in extra) if (citations.Count < 12 && !citations.Any(x => x.Link == c.Link && x.Type == c.Type && x.Id == c.Id)) citations.Add(c);
                }
                catch (AiToolForbiddenException ex)
                {
                    result.Allowed = false;
                    json = AiToolShaper.ForbiddenJson(ex.Message);
                }
                catch (AiToolFailureException ex)
                {
                    json = AiToolShaper.ErrorJson(ex.Message);
                }
                catch (AiToolRejectedException ex)
                {
                    result.Allowed = false;
                    json = AiToolShaper.ErrorJson(ex.Message);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    json = AiToolShaper.ErrorJson("اجرای ابزار با خطا روبه‌رو شد.");
                    result.Title = tool.Title;
                    result.Action = null;
                    result.Citations = new List<AiCitationDto>();
                    _logger?.LogError(ex, "AI tool {Tool} failed", name);
                }
            }
            sw.Stop();
            // the text that is stored (the context turns ي and ك into ی and ک) is the text that is hashed: ResultHash is the SHA-256 of AiMessage.Text
            json = AiText.YeKe(json);
            result.Json = json;
            result.Chars = json.Length;
            result.Hash = AiText.Sha256Hex(json);
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            result.Citations = citations;
            return result;
        }

        /// <summary>The result of a call to a tool that does not exist (or is not available in this run): not allowed, with the fixed error text; hashed like any other result.</summary>
        public static AiToolResult UnknownResult(string name)
        {
            string safe = SafeName(name);
            string json = AiText.YeKe(AiToolShaper.ErrorJson(AiText.UnknownTool));
            return new AiToolResult { Name = safe, Title = safe, Allowed = false, Json = json, Chars = json.Length, Hash = AiText.Sha256Hex(json), DurationMs = 0, Citations = new List<AiCitationDto>() };
        }

        private static JsonDocument ParseArguments(string argumentsJson)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
                doc.Dispose();
            }
            catch (JsonException)
            {
            }
            return JsonDocument.Parse("{}");
        }
    }

    /// <summary>
    /// The registry of a run that has no screen in front of it (the analysis of the CEO dashboard, a seat, the evaluation): the UI action tools are neither offered nor executed.
    /// A model that calls one anyway gets the answer for a tool that does not exist, so it cannot ask the front end for anything and cannot claim it did.
    /// </summary>
    public sealed class NonInteractiveTools : IToolRegistry
    {
        private readonly IToolRegistry _inner;

        public NonInteractiveTools(IToolRegistry inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<AiToolDefinition> Offered() => _inner.Offered().Where(t => !AiUiTools.IsUiTool(t.Name)).ToList();

        public AiToolDefinition Find(string name) => AiUiTools.IsUiTool(ToolRegistry.SafeName(name)) ? null : _inner.Find(name);

        public Task<AiToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct) =>
            AiUiTools.IsUiTool(ToolRegistry.SafeName(name)) ? Task.FromResult(ToolRegistry.UnknownResult(name)) : _inner.ExecuteAsync(name, argumentsJson, ct);
    }
}
