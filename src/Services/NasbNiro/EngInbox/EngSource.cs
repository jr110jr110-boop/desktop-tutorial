using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.EngInbox
{
    // ENG-F1: where the engineering packages come from. Today a folder on the server; later the Engineering software. The ingest service talks to IEngineeringSource only, so replacing the folder changes nothing downstream.
    // Configuration (appsettings.json, section Engineering):
    //   Engineering:Source         Folder (default) | Api
    //   Engineering:InboxFolder    the inbox root. Default: the folder "Engineering-Inbox" beside the application folder (the sibling of "app" in the install folder, so an update of the program never touches it)
    //   Engineering:StableSeconds  a package is ready only when its files have not changed for this long (default 20)

    public static class EngConfigKeys
    {
        public const string Source = "Engineering:Source";
        public const string InboxFolder = "Engineering:InboxFolder";
        public const string StableSeconds = "Engineering:StableSeconds";
        public const int DefaultStableSeconds = 20;
        public const string DefaultFolderName = "Engineering-Inbox";

        public static int StableSecondsOf(IConfiguration configuration)
        {
            string text = configuration?[StableSeconds];
            return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0 && n <= 3600 ? n : DefaultStableSeconds;
        }
    }

    /// <summary>The step code of the apply step (a service of the product bank lets the agent that runs it make the BOM and routing drafts: AgentMaker).</summary>
    public static class EngInboxCodes
    {
        public const string Ingest = "ENG.INBOX.INGEST";
        public const string Fix = "ENG.INBOX.FIX";
        public const string Apply = "ENG.INBOX.APPLY";
        public const string PageLink = "/engineering/inbox";
    }

    /// <summary>Who may do what on the engineering inbox.</summary>
    public static class EngInboxRoles
    {
        /// <summary>Scan now, dismiss a package and the gate task of a rejected or blocked package: the engineering unit (and the PMO).</summary>
        public const string Operators = "ENGINEERING,PMO";

        /// <summary>Everyone who can read BOMs of the product bank today, plus planning, the CEO and the auditor. No price exists in this data.</summary>
        public const string Readers = "PMO,ENGINEERING,ENGINEER,PLANNING,QC,QA,CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR,CEO,AUDITOR";
    }

    public enum EngSourceOutcome { Ingested = 1, Duplicate = 2, Rejected = 3 }

    /// <summary>One package waiting in the source. <see cref="Id"/> is opaque to the service (the folder source uses the relative path of the package folder).</summary>
    public sealed class EngPackageHandle
    {
        public string Id { get; set; }
        public string FolderName { get; set; }

        /// <summary>The package folder relative to the inbox root, with "/" (also what is stored as OriginalPath).</summary>
        public string RelativePath { get; set; }

        /// <summary>What the layout of the folders says (Platforms/&lt;platform&gt;/&lt;standard&gt;/..., Projects/&lt;contract&gt;/&lt;project&gt;/...). The workbook is the truth; a difference is a warning.</summary>
        public EngPackageKind? LayoutKind { get; set; }
        public string LayoutPlatform { get; set; }
        public string LayoutStandard { get; set; }
        public string LayoutContract { get; set; }
        public string LayoutProject { get; set; }
    }

    /// <summary>What the source read of a package: the workbook bytes and the facts of the other files. <see cref="Problems"/> are Persian reasons the package cannot be read at all (two workbooks, a macro file, a link, too big).</summary>
    public sealed class EngPackageContent
    {
        public EngPackageHandle Handle { get; set; }
        public string WorkbookName { get; set; }
        public byte[] Workbook { get; set; }
        public Dictionary<string, EngFileInfo> Files { get; set; } = new Dictionary<string, EngFileInfo>(StringComparer.Ordinal);
        public List<string> Problems { get; set; } = new List<string>();
    }

    public interface IEngineeringSource
    {
        /// <summary>Folder or Api.</summary>
        string Name { get; }

        /// <summary>False: the source cannot be read (no folder, no access, no client yet). The ingest step then does nothing and is no error.</summary>
        bool IsConfigured { get; }

        /// <summary>The place a person looks at (the folder path), shown on the page; empty when there is none.</summary>
        string Location { get; }

        /// <summary>The Persian reason when <see cref="IsConfigured"/> is false.</summary>
        string NotConfiguredReason { get; }

        /// <summary>The packages that are complete and have not changed for the stable time, oldest first (the service takes a bounded number).</summary>
        Task<IReadOnlyList<EngPackageHandle>> ListReadyAsync(CancellationToken ct);

        Task<EngPackageContent> ReadAsync(EngPackageHandle handle, CancellationToken ct);

        /// <summary>Takes the package out of the inbox (the folder source moves the folder to _Processed or _Rejected and writes the note there). Returns where it went, relative to the root (empty when it cannot be moved).</summary>
        Task<string> MarkAsync(EngPackageHandle handle, EngSourceOutcome outcome, string note, CancellationToken ct);
    }

    /// <summary>
    /// THE PLACE for the future Engineering software client. It is empty on purpose: that software does not exist yet. When it does, write the client here (the address and credentials from configuration under
    /// Engineering:Api only, never in code or in a log), set <see cref="IsConfigured"/> to true when they are present, return the packages that software has released from <see cref="ListReadyAsync"/> as handles,
    /// build the same <see cref="EngPackageContent"/> (the workbook bytes in the same format, or fill the parsed content directly) in <see cref="ReadAsync"/>, and tell the software the outcome in <see cref="MarkAsync"/>.
    /// Nothing downstream changes: the ingest service, the apply step and the pages work on the source interface only.
    /// </summary>
    public sealed class EngApiSource : IEngineeringSource
    {
        public EngApiSource(IConfiguration configuration)
        {
            // the settings are read here only when the client is written; nothing is read or logged now
        }

        public string Name => "Api";
        public bool IsConfigured => false;
        public string Location => "";
        public string NotConfiguredReason => "اتصال به نرم‌افزار مهندسی هنوز تنظیم نشده است.";

        public Task<IReadOnlyList<EngPackageHandle>> ListReadyAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<EngPackageHandle>>(Array.Empty<EngPackageHandle>());

        public Task<EngPackageContent> ReadAsync(EngPackageHandle handle, CancellationToken ct) =>
            Task.FromResult(new EngPackageContent { Handle = handle, Problems = { NotConfiguredReason } });

        public Task<string> MarkAsync(EngPackageHandle handle, EngSourceOutcome outcome, string note, CancellationToken ct) => Task.FromResult("");
    }

    public static class EngSourceFactory
    {
        /// <summary>The default inbox root: the folder "Engineering-Inbox" beside the application folder (the sibling of "app" in the install folder).</summary>
        public static string DefaultFolder(string contentRoot)
        {
            string root = string.IsNullOrWhiteSpace(contentRoot) ? AppContext.BaseDirectory : contentRoot;
            string trimmed = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string parent = System.IO.Path.GetDirectoryName(trimmed);
            return System.IO.Path.Combine(string.IsNullOrEmpty(parent) ? trimmed : parent, EngConfigKeys.DefaultFolderName);
        }

        /// <summary>The source that the configuration names; an empty or unknown value is the folder.</summary>
        public static IEngineeringSource Create(IConfiguration configuration, IHostEnvironment environment, ILoggerFactory loggers)
        {
            string name = (configuration?[EngConfigKeys.Source] ?? "").Trim();
            if (string.Equals(name, "Api", StringComparison.OrdinalIgnoreCase)) return new EngApiSource(configuration);
            string folder = configuration?[EngConfigKeys.InboxFolder];
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder(environment?.ContentRootPath);
            return new EngFolderSource(folder.Trim(), TimeSpan.FromSeconds(EngConfigKeys.StableSecondsOf(configuration)), loggers?.CreateLogger<EngFolderSource>());
        }
    }
}
