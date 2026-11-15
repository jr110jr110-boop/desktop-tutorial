using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Services.NasbNiro.Finance.Sp
{
    // Sepidar transfer (per document): the pluggable "sender". Everything on the Orchestra side (kinds, queue, approval, retries) works without it; the sender is the one small piece that knows how to hand an envelope to the outside.
    // Configuration (appsettings.json, section Sepidar):
    //   Sepidar:Sender          None (default) | File | Api
    //   Sepidar:FileDropFolder  the folder of the File sender (an absolute path or a path relative to the application; no ".." segments)
    //   Sepidar:MaxAttempts     automatic attempts before a person must decide (default 5)
    //   Sepidar:Api:BaseUrl, Sepidar:Api:...  reserved for the real client (read from configuration only; never written in code and never logged)

    public static class SepidarConfigKeys
    {
        public const string Sender = "Sepidar:Sender";
        public const string FileDropFolder = "Sepidar:FileDropFolder";
        public const string MaxAttempts = "Sepidar:MaxAttempts";
        public const string ApiBaseUrl = "Sepidar:Api:BaseUrl";
        public const int DefaultMaxAttempts = 5;

        public static int MaxAttemptsOf(IConfiguration configuration)
        {
            string text = configuration?[MaxAttempts];
            return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 20 ? n : DefaultMaxAttempts;
        }
    }

    /// <summary>What the sender gets: the item as a neutral JSON snapshot and its idempotency key (item id + hash): the same envelope sent twice must have the effect of one send.</summary>
    public sealed class SepidarEnvelope
    {
        public int ItemId { get; set; }

        /// <summary>"{itemId}:{payloadHash}".</summary>
        public string IdempotencyKey { get; set; }
        public string TransferTypeCode { get; set; }
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public int SchemaVersion { get; set; }
        public string PayloadHash { get; set; }
        public string PayloadJson { get; set; }
        public bool IsCorrection { get; set; }

        /// <summary>The reference of the item that this one corrects (what the outside returned for it), when known.</summary>
        public string CorrectsExternalReference { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
    }

    public sealed class SpSendResult
    {
        public bool Success { get; set; }

        /// <summary>What the outside returned (the file name for the file drop).</summary>
        public string ExternalReference { get; set; }

        /// <summary>A short Persian text for a person (never a stack trace, a path of the server or a secret).</summary>
        public string Error { get; set; }

        public static SpSendResult Ok(string reference) => new SpSendResult { Success = true, ExternalReference = reference };
        public static SpSendResult Fail(string persianError) => new SpSendResult { Success = false, Error = persianError };
    }

    public interface ISepidarSender
    {
        string Name { get; }

        /// <summary>False: nothing can be sent yet. The send step then does nothing and approved items wait (this is not an error).</summary>
        bool IsConfigured { get; }

        Task<SpSendResult> SendAsync(SepidarEnvelope envelope, CancellationToken ct);
    }

    /// <summary>The default: no connection. Approved items wait in the queue.</summary>
    public sealed class NullSepidarSender : ISepidarSender
    {
        public string Name => "None";
        public bool IsConfigured => false;

        public Task<SpSendResult> SendAsync(SepidarEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(SpSendResult.Fail("اتصال به سپیدار تنظیم نشده است."));
    }

    /// <summary>
    /// Writes each envelope as one JSON file in the drop folder and returns the file name as the reference. The name is made of the item id and the first 16 characters of the hash only, so a repeated send of the same item
    /// writes the same file again (idempotent) and a name can never walk out of the folder. Useful offline and for the first hand-over by hand.
    /// </summary>
    public sealed class FileSepidarSender : ISepidarSender
    {
        private readonly string _folder;

        public FileSepidarSender(IConfiguration configuration)
        {
            _folder = configuration?[SepidarConfigKeys.FileDropFolder];
        }

        public FileSepidarSender(string folder)
        {
            _folder = folder;
        }

        public string Name => "File";

        public bool IsConfigured => ResolveFolder(_folder, out _) == null;

        /// <summary>Null when the folder setting is usable (the full path comes out), else the Persian reason.</summary>
        internal static string ResolveFolder(string folder, out string full)
        {
            full = null;
            if (string.IsNullOrWhiteSpace(folder)) return "پوشه تبادل فایل برای سپیدار تنظیم نشده است.";
            string trimmed = folder.Trim();
            if (trimmed.IndexOf('\0') >= 0 || trimmed.Split('/', '\\').Any(part => part == "..")) return "مسیر پوشه تبادل نامعتبر است.";
            try { full = Path.GetFullPath(trimmed); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return "مسیر پوشه تبادل نامعتبر است."; }
            return null;
        }

        /// <summary>The file name of an envelope: digits, a dash and hex only.</summary>
        internal static string FileNameOf(SepidarEnvelope e)
        {
            string hash = new string((e.PayloadHash ?? string.Empty).Where(Uri.IsHexDigit).Take(16).ToArray()).ToLowerInvariant();
            return "sepidar-" + e.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + hash + ".json";
        }

        public async Task<SpSendResult> SendAsync(SepidarEnvelope envelope, CancellationToken ct)
        {
            if (envelope == null) return SpSendResult.Fail("پاکت ارسال خالی است.");
            string problem = ResolveFolder(_folder, out string folder);
            if (problem != null) return SpSendResult.Fail(problem);
            string name = FileNameOf(envelope);
            string target = Path.GetFullPath(Path.Combine(folder, name));
            if (!string.Equals(Path.GetDirectoryName(target), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)) return SpSendResult.Fail("مسیر فایل نامعتبر است.");
            try
            {
                Directory.CreateDirectory(folder);
                string json = Render(envelope);
                // written to a temporary name first and then moved over the final name: a reader of the folder never sees half a file, and a repeat replaces the same file
                string temp = target + ".tmp";
                await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), ct);
                File.Move(temp, target, true);
                return SpSendResult.Ok(name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { return SpSendResult.Fail("دسترسی نوشتن در پوشه تبادل وجود ندارد."); }
            catch (IOException) { return SpSendResult.Fail("نوشتن فایل در پوشه تبادل ممکن نشد."); }
        }

        internal static string Render(SepidarEnvelope e)
        {
            JsonNode payload;
            try { payload = JsonNode.Parse(e.PayloadJson ?? "null"); }
            catch (JsonException) { payload = null; }
            var root = new JsonObject
            {
                ["idempotencyKey"] = e.IdempotencyKey,
                ["transferType"] = e.TransferTypeCode,
                ["sourceType"] = e.SourceType,
                ["sourceId"] = e.SourceId,
                ["sourceNumber"] = e.SourceNumber,
                ["schemaVersion"] = e.SchemaVersion,
                ["payloadHash"] = e.PayloadHash,
                ["isCorrection"] = e.IsCorrection,
                ["correctsExternalReference"] = e.CorrectsExternalReference,
                ["preparedAt"] = e.PreparedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["payload"] = payload,
            };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
    }

    /// <summary>
    /// THE PLACE for the real Sepidar API client. It is empty on purpose: the API is not delivered yet. When the owner delivers it, write the HTTP client here (base URL and credentials from configuration only, under
    /// Sepidar:Api; never in code and never in a log), set <see cref="IsConfigured"/> to true when they are present, and turn the envelope into the calls of the API. Use <see cref="SepidarEnvelope.IdempotencyKey"/>
    /// as the idempotency key of the call, and answer <see cref="SpSendResult.Fail"/> with a short Persian text on a refusal (never the raw response, a stack trace or a secret).
    /// </summary>
    public sealed class ApiSepidarSender : ISepidarSender
    {
        public ApiSepidarSender(IConfiguration configuration)
        {
            // the settings are read here only when the client is written; nothing is read or logged now
        }

        public string Name => "Api";
        public bool IsConfigured => false;

        public Task<SpSendResult> SendAsync(SepidarEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(SpSendResult.Fail("اتصال API سپیدار هنوز تنظیم نشده است."));
    }

    public static class SepidarSenderFactory
    {
        /// <summary>The sender that the configuration names; an empty or unknown value is None.</summary>
        public static ISepidarSender Create(IConfiguration configuration)
        {
            string kind = configuration?[SepidarConfigKeys.Sender]?.Trim();
            if (string.Equals(kind, "File", StringComparison.OrdinalIgnoreCase)) return new FileSepidarSender(configuration);
            if (string.Equals(kind, "Api", StringComparison.OrdinalIgnoreCase)) return new ApiSepidarSender(configuration);
            return new NullSepidarSender();
        }
    }
}
