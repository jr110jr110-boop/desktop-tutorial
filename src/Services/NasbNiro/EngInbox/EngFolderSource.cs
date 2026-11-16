using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>
    /// The folder source. Layout under the inbox root: Platforms/&lt;platform code&gt;/&lt;standard&gt;/&lt;package folder&gt;/ and Projects/&lt;contract code&gt;/&lt;project code&gt;/&lt;package folder&gt;/, and _Processed, _Rejected, _Templates.
    /// A package folder holds exactly one *.xlsx workbook and the files it refers to. It is ready when every file in it has kept its size and modification time for the stable time AND the workbook can be opened for exclusive read
    /// (the copy has finished). A package folder is MOVED to _Processed or _Rejected after the ingest (a rejection carries دلایل-رد.txt, UTF-8 with BOM). Path safety: every path is canonicalised and must stay under the root; a folder or
    /// a file that is a link (reparse point, symbolic link) is never followed, read or listed. The class is registered once (a singleton): it remembers what it saw of each package between two scans.
    /// </summary>
    public sealed class EngFolderSource : IEngineeringSource
    {
        public const string PlatformsFolder = "Platforms";
        public const string ProjectsFolder = "Projects";
        public const string ProcessedFolder = "_Processed";
        public const string RejectedFolder = "_Rejected";
        public const string TemplatesFolder = "_Templates";
        public const string RejectNoteFile = "دلایل-رد.txt";
        public const string ProcessedNoteFile = "یادداشت.txt";
        public const int MaxFilesPerPackage = 2000;

        private readonly string _root;
        private readonly TimeSpan _stable;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _now;
        private readonly ConcurrentDictionary<string, Seen> _seen = new ConcurrentDictionary<string, Seen>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private DateTime _checkedAt = DateTime.MinValue;
        private string _problem;

        private sealed class Seen
        {
            public string Signature;
            public DateTime Since;
        }

        public EngFolderSource(string root, TimeSpan stable, ILogger logger = null, Func<DateTime> now = null)
        {
            _root = string.IsNullOrWhiteSpace(root) ? null : root.Trim();
            _stable = stable < TimeSpan.Zero ? TimeSpan.Zero : stable;
            _logger = logger;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public string Name => "Folder";
        public string Location => _root ?? "";

        public bool IsConfigured => Ensure() == null;

        public string NotConfiguredReason => Ensure() == null ? "" : "پوشهٔ صندوق ورودی در دسترس نیست؛ مدیر سیستم مسیر را بررسی کند.";

        // ------------------------------------------------------------------------------------------------ the folders

        /// <summary>Creates the root, its sub-folders and the templates when they are missing. Returns null when the structure is usable, else the technical reason (for the log; the page shows the Persian sentence).</summary>
        public string Ensure()
        {
            lock (_gate)
            {
                if (_now() - _checkedAt < TimeSpan.FromSeconds(30) && _checkedAt != DateTime.MinValue) return _problem;
                _checkedAt = _now();
                _problem = EnsureCore();
                if (_problem != null) _logger?.LogWarning("Engineering inbox folder is not available ({Reason}). پوشه صندوق ورودی مهندسی در دسترس نیست؛ مسیر و دسترسی نوشتن را بررسی کنید: {Path}", _problem, _root);
                return _problem;
            }
        }

        private string EnsureCore()
        {
            if (_root == null) return "no path";
            try
            {
                string full = Path.GetFullPath(_root);
                if (!Path.IsPathRooted(_root)) return "the path is not absolute";
                if (IsLink(full) && Directory.Exists(full)) return "the root is a link";
                Directory.CreateDirectory(full);
                foreach (string sub in new[] { PlatformsFolder, ProjectsFolder, ProcessedFolder, RejectedFolder, TemplatesFolder })
                {
                    string p = Path.Combine(full, sub);
                    if (Directory.Exists(p) && IsLink(p)) return sub + " is a link";
                    Directory.CreateDirectory(p);
                }
                WriteTemplatesIfMissing(Path.Combine(full, TemplatesFolder));
                string probe = Path.Combine(full, TemplatesFolder, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return ex.GetType().Name;
            }
        }

        /// <summary>The empty template and the sample package are regenerated when they are missing (the engineering unit copies from there; an edit of those files is never overwritten).</summary>
        private static void WriteTemplatesIfMissing(string templates)
        {
            string empty = Path.Combine(templates, "قالب-خالی-ENG-PACKAGE.xlsx");
            if (!File.Exists(empty)) File.WriteAllBytes(empty, EngPackageWriter.Template());
            EngSamplePackage sample = EngPackageWriter.Sample();
            string dir = Path.Combine(templates, "نمونه-" + EngPackageWriter.SamplePlatformCode, sample.FolderName);
            if (!File.Exists(Path.Combine(dir, sample.WorkbookName)))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, sample.WorkbookName), sample.Workbook);
                foreach (KeyValuePair<string, byte[]> f in sample.Files) File.WriteAllBytes(Path.Combine(dir, f.Key), f.Value);
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.ReparsePoint) != 0) return true;
                FileSystemInfo info = (a & FileAttributes.Directory) != 0 ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                return info.LinkTarget != null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return true; }
        }

        /// <summary>True when the full path lies strictly under the root (canonical comparison).</summary>
        private bool UnderRoot(string full)
        {
            string root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(full);
            return path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------------------------------------ listing

        public Task<IReadOnlyList<EngPackageHandle>> ListReadyAsync(CancellationToken ct)
        {
            var result = new List<EngPackageHandle>();
            if (Ensure() != null) return Task.FromResult<IReadOnlyList<EngPackageHandle>>(result);
            try
            {
                string root = Path.GetFullPath(_root);
                var found = new List<(string Dir, EngPackageHandle Handle)>();
                foreach (string area in new[] { PlatformsFolder, ProjectsFolder })
                    foreach (string l1 in Children(Path.Combine(root, area), ct))
                        foreach (string l2 in Children(l1, ct))
                            foreach (string pkg in Children(l2, ct))
                            {
                                var h = new EngPackageHandle
                                {
                                    FolderName = Path.GetFileName(pkg), Id = Rel(root, pkg), RelativePath = Rel(root, pkg),
                                    LayoutKind = area == PlatformsFolder ? EngPackageKind.Platform : EngPackageKind.Project,
                                };
                                if (area == PlatformsFolder) { h.LayoutPlatform = Path.GetFileName(l1); h.LayoutStandard = Path.GetFileName(l2); }
                                else { h.LayoutContract = Path.GetFileName(l1); h.LayoutProject = Path.GetFileName(l2); }
                                found.Add((pkg, h));
                            }
                var live = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (dir, handle) in found.OrderBy(f => f.Handle.RelativePath, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    live.Add(handle.Id);
                    if (IsReady(dir, handle.Id)) result.Add(handle);
                }
                foreach (string gone in _seen.Keys.Where(k => !live.Contains(k)).ToList()) _seen.TryRemove(gone, out _);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Engineering inbox folder could not be listed. فهرست پوشه صندوق ورودی خوانده نشد.");
            }
            return Task.FromResult<IReadOnlyList<EngPackageHandle>>(result);
        }

        /// <summary>The sub-folders of a folder that are real folders (a link is skipped and never followed) and whose name is not hidden or a system folder (_ or .).</summary>
        private IEnumerable<string> Children(string folder, CancellationToken ct)
        {
            if (!Directory.Exists(folder) || IsLink(folder)) yield break;
            IEnumerable<string> dirs;
            try { dirs = Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.Ordinal).ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { yield break; }
            foreach (string d in dirs)
            {
                ct.ThrowIfCancellationRequested();
                string name = Path.GetFileName(d);
                if (name.StartsWith("_", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (IsLink(d) || !UnderRoot(d)) { _logger?.LogWarning("Engineering inbox: a link or outside folder was skipped: {Name}", name); continue; }
                yield return d;
            }
        }

        private static string Rel(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

        private IEnumerable<string> FilesOf(string dir)
        {
            foreach (string f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith(".", StringComparison.Ordinal) || name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) continue;
                if (name == "Thumbs.db" || name == "desktop.ini") continue;
                yield return f;
            }
        }

        private bool IsReady(string dir, string id)
        {
            List<string> files;
            try { files = FilesOf(dir).ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            if (files.Count == 0) return false;
            if (!files.Any(f => IsWorkbookLike(f))) return false;     // an empty or document-only folder: the engineer is still copying
            var sig = new StringBuilder();
            foreach (string f in files)
            {
                FileInfo fi = new FileInfo(f);
                sig.Append(fi.Name).Append('|').Append(fi.Length.ToString(CultureInfo.InvariantCulture)).Append('|').Append(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            string signature = sig.ToString();
            DateTime now = _now();
            Seen seen = _seen.AddOrUpdate(id, _ => new Seen { Signature = signature, Since = now }, (_, old) => old.Signature == signature ? old : new Seen { Signature = signature, Since = now });
            if (now - seen.Since < _stable) return false;
            foreach (string f in files)
            {
                try { using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None)) { } }   // still being written or held open by a program: not ready
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            }
            return true;
        }

        private static bool IsWorkbookLike(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".xlsx" || ext == ".xlsm" || ext == ".xls" || ext == ".xlsb";
        }

        // ------------------------------------------------------------------------------------------------ reading

        public async Task<EngPackageContent> ReadAsync(EngPackageHandle handle, CancellationToken ct)
        {
            var content = new EngPackageContent { Handle = handle };
            string dir = Resolve(handle, content);
            if (dir == null) return content;
            try
            {
                List<string> all = FilesOf(dir).ToList();
                if (all.Count > MaxFilesPerPackage) { content.Problems.Add("پوشهٔ بسته بیش از " + MaxFilesPerPackage.ToString(CultureInfo.InvariantCulture) + " فایل دارد."); return content; }
                var books = all.Where(IsWorkbookLike).ToList();
                if (books.Count > 1) { content.Problems.Add("پوشهٔ بسته باید فقط یک فایل اکسل داشته باشد؛ " + books.Count.ToString(CultureInfo.InvariantCulture) + " فایل اکسل پیدا شد: " + string.Join("، ", books.Select(Path.GetFileName))); return content; }
                if (books.Count == 0) { content.Problems.Add("فایل اکسل (xlsx) در پوشهٔ بسته نیست."); return content; }
                string book = books[0];
                content.WorkbookName = Path.GetFileName(book);
                if (IsLink(book)) { content.Problems.Add("فایل اکسل یک میان‌بر (لینک) است و خوانده نمی‌شود."); return content; }
                if (!string.Equals(Path.GetExtension(book), ".xlsx", StringComparison.OrdinalIgnoreCase)) { content.Problems.Add("فقط فایل xlsx پذیرفته می‌شود؛ فایل «" + content.WorkbookName + "» پذیرفته نیست."); return content; }
                long size = new FileInfo(book).Length;
                if (size > EngPackageLimits.MaxWorkbookBytes) { content.Problems.Add("حجم فایل اکسل بیش از ۲۰ مگابایت است."); return content; }
                content.Workbook = await File.ReadAllBytesAsync(book, ct);
                foreach (string f in all)
                {
                    ct.ThrowIfCancellationRequested();
                    if (f == book) continue;
                    if (IsLink(f)) { content.Problems.Add("فایل «" + Path.GetFileName(f) + "» یک میان‌بر (لینک) است و خوانده نمی‌شود."); continue; }
                    content.Files[Path.GetFileName(f)] = await FactsAsync(f, ct);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                content.Problems.Add("پوشهٔ بسته خوانده نشد (دسترسی یا قفل بودن فایل).");
            }
            return content;
        }

        private static async Task<EngFileInfo> FactsAsync(string path, CancellationToken ct)
        {
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                byte[] buffer = new byte[81920];
                int n;
                long total = 0;
                while ((n = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0) { sha.AppendData(buffer, 0, n); total += n; }
                return new EngFileInfo { Name = Path.GetFileName(path), Size = total, Sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() };
            }
        }

        /// <summary>The full path of a package folder, only when it is a real folder under the root (never a link, never outside).</summary>
        private string Resolve(EngPackageHandle handle, EngPackageContent content)
        {
            if (handle == null || string.IsNullOrWhiteSpace(handle.Id) || Ensure() != null) { content?.Problems.Add("پوشهٔ بسته در دسترس نیست."); return null; }
            if (handle.Id.Contains("..") || Path.IsPathRooted(handle.Id)) { content?.Problems.Add("مسیر بسته نامعتبر است."); return null; }
            string full;
            try { full = Path.GetFullPath(Path.Combine(_root, handle.Id.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { content?.Problems.Add("مسیر بسته نامعتبر است."); return null; }
            if (!UnderRoot(full)) { content?.Problems.Add("مسیر بسته بیرون از صندوق ورودی است."); return null; }
            if (!Directory.Exists(full) || IsLink(full)) { content?.Problems.Add("پوشهٔ بسته پیدا نشد یا یک میان‌بر (لینک) است."); return null; }
            // every folder on the way is a real folder too
            string walk = full;
            string root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar);
            while (walk.Length > root.Length)
            {
                if (IsLink(walk)) { content?.Problems.Add("مسیر بسته از یک میان‌بر (لینک) می‌گذرد."); return null; }
                walk = Path.GetDirectoryName(walk);
                if (walk == null) break;
            }
            return full;
        }

        // ------------------------------------------------------------------------------------------------ moving out of the inbox

        public Task<string> MarkAsync(EngPackageHandle handle, EngSourceOutcome outcome, string note, CancellationToken ct)
        {
            var scratch = new EngPackageContent();
            string dir = Resolve(handle, scratch);
            if (dir == null) return Task.FromResult("");
            try
            {
                string root = Path.GetFullPath(_root);
                bool rejected = outcome == EngSourceOutcome.Rejected;
                string area = Path.Combine(root, rejected ? RejectedFolder : ProcessedFolder);
                string name = _now().ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + SafeName(Path.GetFileName(dir));
                string target = Path.Combine(area, name);
                for (int i = 2; Directory.Exists(target); i++) target = Path.Combine(area, name + "-" + i.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(area);
                Directory.Move(dir, target);
                string noteFile = Path.Combine(target, rejected ? RejectNoteFile : ProcessedNoteFile);
                if (!string.IsNullOrWhiteSpace(note) || rejected) File.WriteAllText(noteFile, note ?? "", new UTF8Encoding(true));
                _seen.TryRemove(handle.Id, out _);
                return Task.FromResult(Rel(root, target));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Engineering inbox: the package folder could not be moved. پوشه بسته جابه‌جا نشد: {Id}", handle.Id);
                return Task.FromResult("");
            }
        }

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "") sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
            string s = sb.ToString().Trim('.', '_');
            return s.Length == 0 ? "package" : (s.Length > 80 ? s.Substring(0, 80) : s);
        }
    }
}
