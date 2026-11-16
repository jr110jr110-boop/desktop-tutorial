using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.EngInbox;

namespace MsTests
{
    /// <summary>A test-only source: the packages are in memory, the outcomes are recorded.</summary>
    public sealed class EngMemorySource : IEngineeringSource
    {
        public List<EngPackageContent> Packages { get; } = new List<EngPackageContent>();
        public List<(string Id, EngSourceOutcome Outcome, string Note)> Marks { get; } = new List<(string, EngSourceOutcome, string)>();
        public bool Configured { get; set; } = true;

        /// <summary>True: every move out of the inbox fails (as a locked folder would).</summary>
        public bool FailMoves { get; set; }

        /// <summary>When set, the first listing waits for it (a scan that is still running).</summary>
        public System.Threading.Tasks.TaskCompletionSource<bool> Gate { get; set; }
        public string Name => "Memory";
        public bool IsConfigured => Configured;
        public string Location => "memory://";
        public string NotConfiguredReason => "در دسترس نیست.";

        public async Task<IReadOnlyList<EngPackageHandle>> ListReadyAsync(CancellationToken ct)
        {
            if (Gate != null) await Gate.Task;
            return Packages.Where(p => !Marks.Any(m => m.Id == p.Handle.Id && !m.Note.StartsWith("FAILED", StringComparison.Ordinal))).Select(p => p.Handle).ToList();
        }

        public Task<string> SignatureAsync(EngPackageHandle handle, CancellationToken ct)
        {
            EngPackageContent c = Packages.Single(p => p.Handle.Id == handle.Id);
            return Task.FromResult(EngText.Sha256Hex(c.Workbook).Substring(0, 40) + "/" + c.Files.Count + "/" + c.Files.Values.Sum(f => f.Size));
        }

        public Task<EngPackageContent> ReadAsync(EngPackageHandle handle, CancellationToken ct) => Task.FromResult(Packages.Single(p => p.Handle.Id == handle.Id));

        public Task<string> MarkAsync(EngPackageHandle handle, EngSourceOutcome outcome, string note, CancellationToken ct)
        {
            if (FailMoves) { Marks.Add((handle.Id, outcome, "FAILED " + note)); throw new EngSourceMoveException("پوشهٔ بسته جابه‌جا نشد (آزمون)."); }
            Marks.Add((handle.Id, outcome, note));
            return Task.FromResult("_" + outcome + "/" + handle.Id);
        }

        public EngPackageContent Add(string id, EngSamplePackage s)
        {
            var c = new EngPackageContent { Handle = new EngPackageHandle { Id = id, FolderName = id, RelativePath = "Platforms/X/Y/" + id }, WorkbookName = s.WorkbookName, Workbook = s.Workbook, Files = EngPackageWriter.FactsOf(s) };
            Packages.Add(c);
            return c;
        }
    }

    /// <summary>ENG-F1 part 2: the folder source on a temporary directory.</summary>
    [TestClass]
    public class EngInboxSourceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        private static string NewRoot() => Path.Combine(Path.GetTempPath(), "eng-inbox-" + Guid.NewGuid().ToString("N"));

        internal static string Drop(string root, string area, string l1, string l2, string name, EngSamplePackage s)
        {
            string dir = Path.Combine(root, area, l1, l2, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, s.WorkbookName), s.Workbook);
            foreach (KeyValuePair<string, byte[]> f in s.Files) File.WriteAllBytes(Path.Combine(dir, f.Key), f.Value);
            return dir;
        }

        [TestMethod]
        public void The_Folders_And_The_Templates_Are_Created_And_Never_Overwritten()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                Assert.IsTrue(src.IsConfigured);
                Assert.AreEqual("", src.NotConfiguredReason);
                Assert.AreEqual(root, src.Location);
                foreach (string d in new[] { "Platforms", "Projects", "_Processed", "_Rejected", "_Templates" }) Assert.IsTrue(Directory.Exists(Path.Combine(root, d)), d);
                string empty = Path.Combine(root, "_Templates", "قالب-خالی-ENG-PACKAGE.xlsx");
                string sample = Path.Combine(root, "_Templates", "نمونه-TEST-PLT-01", "pkg-v1", "ENG-PACKAGE.xlsx");
                Assert.IsTrue(File.Exists(empty));
                Assert.IsTrue(File.Exists(sample));
                File.WriteAllText(empty, "edited by a person");
                new EngFolderSource(root, TimeSpan.Zero).Ensure();
                Assert.AreEqual("edited by a person", File.ReadAllText(empty), "an existing template is not regenerated");
                File.Delete(sample);
                new EngFolderSource(root, TimeSpan.Zero).Ensure();
                Assert.IsTrue(File.Exists(sample), "a missing sample is regenerated");
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task A_Root_That_Cannot_Be_Made_Is_Not_Configured_And_Nothing_Throws()
        {
            string file = Path.Combine(Path.GetTempPath(), "eng-inbox-file-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(file, "x");
            try
            {
                var src = new EngFolderSource(Path.Combine(file, "inbox"), TimeSpan.Zero);
                Assert.IsFalse(src.IsConfigured);
                StringAssert.Contains(src.NotConfiguredReason, "پوشهٔ صندوق ورودی در دسترس نیست");
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count);
                var relative = new EngFolderSource("relative/inbox", TimeSpan.Zero);
                Assert.IsFalse(relative.IsConfigured, "a relative root is refused");
                Assert.IsFalse(new EngFolderSource("", TimeSpan.Zero).IsConfigured);
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public async Task A_Package_Is_Ready_Only_After_The_Stable_Time_And_A_Change_Starts_It_Again()
        {
            string root = NewRoot();
            try
            {
                DateTime now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
                var src = new EngFolderSource(root, TimeSpan.FromSeconds(20), null, () => now);
                string dir = Drop(root, "Platforms", "TEST-PLT-01", "API 610", "pkg-v1", EngPackageWriter.Sample());
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count, "first sight");
                now = now.AddSeconds(19);
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count, "19 seconds");
                now = now.AddSeconds(2);
                IReadOnlyList<EngPackageHandle> ready = await src.ListReadyAsync(Ct);
                Assert.AreEqual(1, ready.Count);
                Assert.AreEqual("Platforms/TEST-PLT-01/API 610/pkg-v1", ready[0].RelativePath);
                Assert.AreEqual(EngPackageKind.Platform, ready[0].LayoutKind);
                Assert.AreEqual("TEST-PLT-01", ready[0].LayoutPlatform);
                Assert.AreEqual("API 610", ready[0].LayoutStandard);
                // a file that changes (another copy arrives) starts the time again
                File.AppendAllText(Path.Combine(dir, "DS-TST-01.txt"), "more");
                now = now.AddSeconds(1);
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count, "changed");
                now = now.AddSeconds(21);
                Assert.AreEqual(1, (await src.ListReadyAsync(Ct)).Count);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task A_File_That_Is_Still_Open_Is_Not_Ready_And_An_Empty_Or_Document_Only_Folder_Is_Not_Ready()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                string dir = Drop(root, "Platforms", "P", "S", "pkg", EngPackageWriter.Sample());
                Directory.CreateDirectory(Path.Combine(root, "Platforms", "P", "S", "empty"));
                string docsOnly = Path.Combine(root, "Platforms", "P", "S", "docs-only");
                Directory.CreateDirectory(docsOnly);
                File.WriteAllText(Path.Combine(docsOnly, "a.pdf"), "x");
                using (new FileStream(Path.Combine(dir, "ENG-PACKAGE.xlsx"), FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count, "the workbook is held open");
                IReadOnlyList<EngPackageHandle> ready = await src.ListReadyAsync(Ct);
                Assert.AreEqual(1, ready.Count);
                Assert.AreEqual("pkg", ready[0].FolderName);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task A_Link_Is_Never_Followed_Listed_Or_Read()
        {
            if (OperatingSystem.IsWindows()) return;   // creating a symbolic link needs a privilege there
            string root = NewRoot();
            string outside = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                Assert.IsTrue(src.IsConfigured);
                // a package folder outside the root, reached through a link
                Drop(outside, "Platforms", "P", "S", "pkg", EngPackageWriter.Sample());
                Directory.CreateDirectory(Path.Combine(root, "Platforms", "LINKED"));
                Directory.CreateSymbolicLink(Path.Combine(root, "Platforms", "LINKED", "S"), Path.Combine(outside, "Platforms", "P", "S"));
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count, "a linked folder is skipped");
                // a link to the workbook inside a real package folder
                string dir = Path.Combine(root, "Platforms", "P", "S", "pkg2");
                Directory.CreateDirectory(dir);
                File.CreateSymbolicLink(Path.Combine(dir, "ENG-PACKAGE.xlsx"), Path.Combine(outside, "Platforms", "P", "S", "pkg", "ENG-PACKAGE.xlsx"));
                EngPackageHandle h = (await src.ListReadyAsync(Ct)).Single();
                EngPackageContent c = await src.ReadAsync(h, Ct);
                Assert.IsNull(c.Workbook);
                StringAssert.Contains(c.Problems.Single(), "میان‌بر");
            }
            finally { Directory.Delete(root, true); Directory.Delete(outside, true); }
        }

        [TestMethod]
        public async Task Reading_Gives_The_Workbook_And_The_Name_Size_And_Sha256_Of_Every_Other_File()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                EngSamplePackage s = EngPackageWriter.Sample();
                Drop(root, "Platforms", "TEST-PLT-01", "API 610", "pkg-v1", s);
                EngPackageContent c = await src.ReadAsync((await src.ListReadyAsync(Ct)).Single(), Ct);
                Assert.AreEqual(0, c.Problems.Count);
                Assert.AreEqual("ENG-PACKAGE.xlsx", c.WorkbookName);
                CollectionAssert.AreEqual(s.Workbook, c.Workbook);
                Assert.AreEqual(4, c.Files.Count);
                Assert.AreEqual(s.Files["DS-TST-01.txt"].Length, c.Files["DS-TST-01.txt"].Size);
                Assert.AreEqual(EngText.Sha256Hex(s.Files["DS-TST-01.txt"]), c.Files["DS-TST-01.txt"].Sha256);
                EngParsedPackage p = EngPackageParser.Parse(c.Workbook, c.WorkbookName, c.Files);
                Assert.AreEqual(0, p.Findings.Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task Two_Workbooks_Or_No_Workbook_Or_A_Macro_Workbook_Are_Problems_Not_Packages()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                string two = Drop(root, "Platforms", "P", "S", "two", EngPackageWriter.Sample());
                File.WriteAllBytes(Path.Combine(two, "other.xlsx"), EngPackageWriter.Template());
                string macro = Path.Combine(root, "Platforms", "P", "S", "macro");
                Directory.CreateDirectory(macro);
                File.WriteAllBytes(Path.Combine(macro, "ENG-PACKAGE.xlsm"), EngPackageWriter.Sample().Workbook);
                var ready = (await src.ListReadyAsync(Ct)).ToDictionary(h => h.FolderName);
                EngPackageContent a = await src.ReadAsync(ready["two"], Ct);
                StringAssert.Contains(a.Problems.Single(), "فقط یک فایل اکسل");
                EngPackageContent b = await src.ReadAsync(ready["macro"], Ct);
                StringAssert.Contains(b.Problems.Single(), "xlsx");
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task A_Handle_That_Leaves_The_Root_Is_Refused()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                foreach (string bad in new[] { "../outside", "Platforms/../../x", "/etc", "Platforms/P/S/../../../..", "C:\\Windows" })
                {
                    var h = new EngPackageHandle { Id = bad, RelativePath = bad, FolderName = "x" };
                    EngPackageContent c = await src.ReadAsync(h, Ct);
                    Assert.IsNull(c.Workbook, bad);
                    Assert.AreEqual(1, c.Problems.Count, bad);
                    EngSourceMoveException moved = null;
                    try { await src.MarkAsync(h, EngSourceOutcome.Ingested, "x", Ct); }
                    catch (EngSourceMoveException ex) { moved = ex; }
                    Assert.IsNotNull(moved, bad + ": a move that cannot happen is reported, not hidden");
                }
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public async Task A_Package_Is_Moved_To_Processed_Or_Rejected_With_Its_Note_And_Names_Never_Collide()
        {
            string root = NewRoot();
            try
            {
                var src = new EngFolderSource(root, TimeSpan.Zero);
                string dir = Drop(root, "Platforms", "P", "S", "pkg", EngPackageWriter.Sample());
                EngPackageHandle h = (await src.ListReadyAsync(Ct)).Single();
                string stored = await src.MarkAsync(h, EngSourceOutcome.Rejected, "خطا: نمونه", Ct);
                Assert.IsTrue(stored.StartsWith("_Rejected/", StringComparison.Ordinal), stored);
                Assert.IsFalse(Directory.Exists(dir), "the folder left the inbox");
                string note = Path.Combine(root, stored.Replace('/', Path.DirectorySeparatorChar), "دلایل-رد.txt");
                byte[] bytes = File.ReadAllBytes(note);
                CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "UTF-8 with a byte order mark");
                Assert.AreEqual("خطا: نمونه", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
                Assert.IsTrue(File.Exists(Path.Combine(root, stored.Replace('/', Path.DirectorySeparatorChar), "ENG-PACKAGE.xlsx")));
                // the same name again in the same second gets another folder
                Drop(root, "Platforms", "P", "S", "pkg", EngPackageWriter.Sample());
                string again = await src.MarkAsync((await src.ListReadyAsync(Ct)).Single(), EngSourceOutcome.Ingested, "ok", Ct);
                Assert.IsTrue(again.StartsWith("_Processed/", StringComparison.Ordinal), again);
                Assert.AreEqual("ok", File.ReadAllText(Path.Combine(root, again.Replace('/', Path.DirectorySeparatorChar), "یادداشت.txt")).TrimStart('\uFEFF'));
                Assert.AreEqual(0, (await src.ListReadyAsync(Ct)).Count);
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void The_Default_Folder_Is_The_Sibling_Of_The_Application_Folder_And_The_Api_Source_Is_Not_Configured()
        {
            Assert.AreEqual(Path.Combine("D:" + Path.DirectorySeparatorChar + "INNCO", "Engineering-Inbox"), EngSourceFactory.DefaultFolder("D:" + Path.DirectorySeparatorChar + "INNCO" + Path.DirectorySeparatorChar + "app").Replace('/', Path.DirectorySeparatorChar).Replace("D:INNCO", "D:" + Path.DirectorySeparatorChar + "INNCO"));
            Assert.AreEqual("/opt/innco/Engineering-Inbox", EngSourceFactory.DefaultFolder("/opt/innco/app/").Replace('\\', '/'));
            var api = new EngApiSource(null);
            Assert.AreEqual("Api", api.Name);
            Assert.IsFalse(api.IsConfigured);
            Assert.AreEqual(0, api.ListReadyAsync(Ct).GetAwaiter().GetResult().Count);
        }
    }
}
