using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Services.NasbNiro.Ai
{
    // ------------------------------------------------------------------------------------------ chat

    public class LlmToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ArgumentsJson { get; set; }
    }

    public class LlmMessage
    {
        /// <summary>system, user, assistant or tool.</summary>
        public string Role { get; set; }
        public string Content { get; set; }

        /// <summary>Tool message: the call it answers.</summary>
        public string ToolCallId { get; set; }

        /// <summary>Assistant message: the tool calls it made.</summary>
        public List<LlmToolCall> ToolCalls { get; set; }
    }

    public class LlmToolSpec
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>The JSON schema of the arguments.</summary>
        public string SchemaJson { get; set; }
    }

    public class LlmResult
    {
        /// <summary>False: no endpoint answered (the caller degrades gracefully).</summary>
        public bool Ok { get; set; }
        public string Content { get; set; }
        public List<LlmToolCall> ToolCalls { get; set; } = new List<LlmToolCall>();
        public string Model { get; set; }
        public string Error { get; set; }
        public int LatencyMs { get; set; }
    }

    public interface ILlmClient
    {
        Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolSpec> tools, CancellationToken ct);
    }

    public interface IEmbeddingClient
    {
        /// <summary>Null when no embeddings endpoint answered.</summary>
        Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
    }

    public class EmbeddingResult
    {
        public List<float[]> Vectors { get; set; } = new List<float[]>();
        public string Model { get; set; }
    }

    public interface ISpeechToText
    {
        /// <summary>Null text: no speech endpoint answered.</summary>
        Task<SttResult> TranscribeAsync(byte[] audio, string fileName, string contentType, CancellationToken ct);
    }

    public class SttResult
    {
        public bool Ok { get; set; }
        public string Text { get; set; }
        public string Model { get; set; }
    }

    public interface ITextToSpeech
    {
        Task<TtsResult> SynthesizeAsync(string text, CancellationToken ct);
    }

    public class TtsResult
    {
        public bool Ok { get; set; }
        public byte[] Audio { get; set; }
        public string Model { get; set; }
    }

    // ------------------------------------------------------------------------------------------ the failover over endpoints

    internal sealed class AiEndpointException : Exception
    {
        public AiEndpointException(string message) : base(message)
        {
        }
    }

    /// <summary>Calls the enabled endpoints of a kind by priority; a failure is recorded on the endpoint row and in the audit and the next one is tried.</summary>
    public interface IAiEndpointRunner
    {
        Task<AiRunResult<T>> RunAsync<T>(AiEndpointKind kind, Func<AiModelEndpoint, HttpClient, CancellationToken, Task<T>> call, CancellationToken ct);
    }

    public class AiRunResult<T>
    {
        public bool Ok { get; set; }
        public T Value { get; set; }
        public AiModelEndpoint Endpoint { get; set; }
        public string Error { get; set; }
        public int LatencyMs { get; set; }
    }

    public class AiEndpointRunner : IAiEndpointRunner
    {
        public const string ClientName = "ai-local";

        private readonly IUnitOfWork _uow;
        private readonly IHttpClientFactory _http;
        private readonly IAiAudit _audit;

        public AiEndpointRunner(IUnitOfWork uow, IHttpClientFactory http, IAiAudit audit)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public async Task<AiRunResult<T>> RunAsync<T>(AiEndpointKind kind, Func<AiModelEndpoint, HttpClient, CancellationToken, Task<T>> call, CancellationToken ct)
        {
            List<AiModelEndpoint> endpoints = await _uow.Set<AiModelEndpoint>().Where(e => e.Kind == kind && e.Enabled).OrderBy(e => e.Priority).ThenBy(e => e.Id).ToListAsync(ct);
            string lastError = endpoints.Count == 0 ? "no enabled endpoint" : null;
            foreach (AiModelEndpoint ep in endpoints)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, ep.TimeoutSeconds)));
                try
                {
                    HttpClient client = _http.CreateClient(ClientName);
                    T value = await call(ep, client, timeout.Token);
                    sw.Stop();
                    await RecordAsync(ep, true, null, ct);
                    return new AiRunResult<T> { Ok = true, Value = value, Endpoint = ep, LatencyMs = (int)sw.ElapsedMilliseconds };
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is AiEndpointException || ex is JsonException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException)
                {
                    string error = ex is OperationCanceledException ? $"timeout after {ep.TimeoutSeconds} s" : ex.Message;
                    lastError = $"{ep.Name}: {error}";
                    await RecordAsync(ep, false, error, ct);
                    await _audit.LogAsync(AiAuditKinds.Failover, $"{kind} endpoint {ep.Name} ({ep.ModelName}) failed: {error}", ct, model: ep.ModelName);
                }
            }
            return new AiRunResult<T> { Ok = false, Error = lastError };
        }

        private async Task RecordAsync(AiModelEndpoint ep, bool ok, string error, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            // a healthy endpoint is written at most every half minute
            if (ok && ep.LastHealthOk == true && ep.LastHealthAt.HasValue && now - ep.LastHealthAt.Value < TimeSpan.FromSeconds(30)) return;
            ep.LastHealthAt = now;
            ep.LastHealthOk = ok;
            ep.LastError = ok ? null : AiText.Cut(error, 1000);
            await _uow.SaveChangesAsync(ct);
        }

        public static string Join(AiModelEndpoint ep, string path) => ep.Url.TrimEnd('/') + path;

        public static async Task<string> PostJsonAsync(HttpClient client, string url, string json, CancellationToken ct)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(url, content, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new AiEndpointException($"HTTP {(int)response.StatusCode}: {AiText.Cut(body, 300)}");
            return body;
        }
    }

    // ------------------------------------------------------------------------------------------ chat completions

    /// <summary>OpenAI-compatible chat completions with function tools; temperature 0.</summary>
    public class LlmClient : ILlmClient
    {
        private readonly IAiEndpointRunner _runner;

        public LlmClient(IAiEndpointRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public async Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolSpec> tools, CancellationToken ct)
        {
            AiRunResult<LlmResult> run = await _runner.RunAsync(AiEndpointKind.Chat, async (ep, client, token) =>
            {
                string body = BuildRequest(ep.ModelName, messages, tools);
                string response = await AiEndpointRunner.PostJsonAsync(client, AiEndpointRunner.Join(ep, "/v1/chat/completions"), body, token);
                LlmResult parsed = Parse(response);
                if (string.IsNullOrEmpty(parsed.Model)) parsed.Model = ep.ModelName;
                return parsed;
            }, ct);
            if (!run.Ok) return new LlmResult { Ok = false, Error = run.Error };
            run.Value.Ok = true;
            run.Value.LatencyMs = run.LatencyMs;
            return run.Value;
        }

        public static string BuildRequest(string model, IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolSpec> tools)
        {
            var root = new JsonObject { ["model"] = model };
            var array = new JsonArray();
            foreach (LlmMessage m in messages)
            {
                var o = new JsonObject { ["role"] = m.Role };
                if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    o["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content;
                    var calls = new JsonArray();
                    foreach (LlmToolCall c in m.ToolCalls)
                        calls.Add(new JsonObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson ?? "{}" } });
                    o["tool_calls"] = calls;
                }
                else o["content"] = m.Content ?? string.Empty;
                if (m.Role == "tool") o["tool_call_id"] = m.ToolCallId;
                array.Add(o);
            }
            root["messages"] = array;
            if (tools != null && tools.Count > 0)
            {
                var specs = new JsonArray();
                foreach (LlmToolSpec t in tools)
                {
                    JsonNode schema;
                    try { schema = JsonNode.Parse(t.SchemaJson ?? "{\"type\":\"object\",\"properties\":{}}"); }
                    catch (JsonException) { schema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{}}"); }
                    specs.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = schema } });
                }
                root["tools"] = specs;
                root["tool_choice"] = "auto";
            }
            root["temperature"] = 0;
            return root.ToJsonString();
        }

        public static LlmResult Parse(string response)
        {
            using JsonDocument doc = JsonDocument.Parse(response);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                throw new AiEndpointException("the response has no choices");
            JsonElement message = choices[0].GetProperty("message");
            var result = new LlmResult();
            if (root.TryGetProperty("model", out JsonElement model) && model.ValueKind == JsonValueKind.String) result.Model = model.GetString();
            if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) result.Content = content.GetString();
            if (message.TryGetProperty("tool_calls", out JsonElement calls) && calls.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out JsonElement fn)) continue;
                    string args = "{}";
                    if (fn.TryGetProperty("arguments", out JsonElement a))
                        args = a.ValueKind == JsonValueKind.String ? a.GetString() : a.GetRawText();
                    if (string.IsNullOrWhiteSpace(args)) args = "{}";
                    string id = call.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : "call_" + (++index).ToString(CultureInfo.InvariantCulture);
                    result.ToolCalls.Add(new LlmToolCall { Id = id, Name = fn.GetProperty("name").GetString(), ArgumentsJson = args });
                }
            }
            return result;
        }
    }

    // ------------------------------------------------------------------------------------------ embeddings

    public class EmbeddingClient : IEmbeddingClient
    {
        private readonly IAiEndpointRunner _runner;

        public EmbeddingClient(IAiEndpointRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
        {
            if (inputs == null || inputs.Count == 0) return new EmbeddingResult();
            AiRunResult<EmbeddingResult> run = await _runner.RunAsync(AiEndpointKind.Embeddings, async (ep, client, token) =>
            {
                var body = new JsonObject { ["model"] = ep.ModelName };
                var array = new JsonArray();
                foreach (string s in inputs) array.Add(s);
                body["input"] = array;
                string response = await AiEndpointRunner.PostJsonAsync(client, AiEndpointRunner.Join(ep, "/v1/embeddings"), body.ToJsonString(), token);
                using JsonDocument doc = JsonDocument.Parse(response);
                var items = new List<(int Index, float[] Vector)>();
                foreach (JsonElement item in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    int index = item.TryGetProperty("index", out JsonElement idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : items.Count;
                    var vector = new List<float>();
                    foreach (JsonElement v in item.GetProperty("embedding").EnumerateArray()) vector.Add((float)v.GetDouble());
                    items.Add((index, vector.ToArray()));
                }
                if (items.Count != inputs.Count) throw new AiEndpointException("the number of vectors differs from the number of inputs");
                return new EmbeddingResult { Vectors = items.OrderBy(i => i.Index).Select(i => i.Vector).ToList(), Model = ep.ModelName };
            }, ct);
            return run.Ok ? run.Value : null;
        }
    }

    // ------------------------------------------------------------------------------------------ speech

    public class SpeechToTextClient : ISpeechToText
    {
        private readonly IAiEndpointRunner _runner;

        public SpeechToTextClient(IAiEndpointRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public async Task<SttResult> TranscribeAsync(byte[] audio, string fileName, string contentType, CancellationToken ct)
        {
            AiRunResult<string> run = await _runner.RunAsync(AiEndpointKind.Stt, async (ep, client, token) =>
            {
                using var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(audio);
                file.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
                form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "audio.wav" : fileName);
                form.Add(new StringContent(ep.ModelName), "model");
                form.Add(new StringContent("fa"), "language");
                using HttpResponseMessage response = await client.PostAsync(AiEndpointRunner.Join(ep, "/v1/audio/transcriptions"), form, token);
                string body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode) throw new AiEndpointException($"HTTP {(int)response.StatusCode}: {AiText.Cut(body, 300)}");
                using JsonDocument doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String) throw new AiEndpointException("the response has no text");
                return text.GetString();
            }, ct);
            return run.Ok ? new SttResult { Ok = true, Text = run.Value, Model = run.Endpoint.ModelName } : new SttResult { Ok = false };
        }
    }

    public class TextToSpeechClient : ITextToSpeech
    {
        private readonly IAiEndpointRunner _runner;
        private readonly IConfiguration _configuration;

        public TextToSpeechClient(IAiEndpointRunner runner, IConfiguration configuration)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _configuration = configuration;
        }

        public async Task<TtsResult> SynthesizeAsync(string text, CancellationToken ct)
        {
            string voice = _configuration?[AiConfigKeys.TtsVoice];
            if (string.IsNullOrWhiteSpace(voice)) voice = "default";
            AiRunResult<byte[]> run = await _runner.RunAsync(AiEndpointKind.Tts, async (ep, client, token) =>
            {
                var body = new JsonObject { ["model"] = ep.ModelName, ["input"] = text, ["voice"] = voice, ["response_format"] = "wav" };
                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(AiEndpointRunner.Join(ep, "/v1/audio/speech"), content, token);
                if (!response.IsSuccessStatusCode) throw new AiEndpointException($"HTTP {(int)response.StatusCode}: {AiText.Cut(await response.Content.ReadAsStringAsync(token), 300)}");
                byte[] audio = await response.Content.ReadAsByteArrayAsync(token);
                if (audio.Length < 44) throw new AiEndpointException("the audio is empty");
                return audio;
            }, ct);
            return run.Ok ? new TtsResult { Ok = true, Audio = run.Value, Model = run.Endpoint.ModelName } : new TtsResult { Ok = false };
        }
    }
}
