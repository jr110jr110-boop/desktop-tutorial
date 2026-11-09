using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.GoLive;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;
using ViewModels.NasbNiro.IntegrityDtos;

namespace MsTests
{
    /// <summary>Package G (go-live cut-over): settings and safety registration, the record-drift scope, the cut-over instant, the adoption rules, the CEO inbox and the PDF stamper.</summary>
    [TestClass]
    public class PackageGTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(50, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Eng1 = AppHost.Principal(21, "eng.one", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(22, "eng.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Qa1 = AppHost.Principal(60, "qa.one", "QA");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(37, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Plain = AppHost.Principal(90, "user.one", "SALES");

        private static AppHost Host() => new AppHost("pg_" + Guid.NewGuid().ToString("N"), new Dictionary<string, string> { ["Identity:AuditHmacKey"] = "pg-test-audit-key-not-a-secret" });

        // ------------------------------------------------------------------------------------------ settings and safety profile

        [TestMethod]
        public void Every_Go_Live_Setting_Has_A_Default_Bounds_And_A_Strict_Safety_Spec()
        {
            string[] keys = { GlSettingKeys.ControlStartDate, GlSettingKeys.LegacyReasonMinChars, GlSettingKeys.PoCiteDrawing, GlSettingKeys.TransmittalRevisionFromEdms, GlSettingKeys.StampMaxMb };
            Dictionary<string, decimal> defaults = QualitySettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value);
            foreach (string k in keys)
            {
                Assert.IsTrue(defaults.ContainsKey(k), k);
                Assert.IsTrue(QualitySettingRules.TryGetBounds(k, out _, out _), k);
                SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, k);
                Assert.IsNotNull(spec, "safety spec " + k);
            }
            Assert.AreEqual(0m, defaults[GlSettingKeys.ControlStartDate], "0 = the recorded activation instant");
            Assert.AreEqual(1m, defaults[GlSettingKeys.PoCiteDrawing]);
            Assert.AreEqual(1m, defaults[GlSettingKeys.TransmittalRevisionFromEdms]);
            Assert.AreEqual(50m, defaults[GlSettingKeys.StampMaxMb]);
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, GlSettingKeys.StampMaxMb), "80"), "a bigger stamp limit is a weakening");
            Assert.IsFalse(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, GlSettingKeys.StampMaxMb), "20"));
        }

        [TestMethod]
        public void Moving_The_Cut_Over_Day_Later_Is_A_Weakening()
        {
            SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, GlSettingKeys.ControlStartDate);
            Assert.IsTrue(SafetyProfile.IsWeaker(spec, "14091231"));
            Assert.IsFalse(SafetyProfile.IsWeaker(spec, "0"));
        }

        [TestMethod]
        public void The_Engineering_Masters_And_The_New_Tables_Are_In_The_Record_Drift_Scope_With_A_Written_Reason()
        {
            string[] names = { "PbBomHeader", "PbBomLine", "PbOpcRouting", "PbOpcStep", "PbProjectBaseline", "PbProjectBomLine", "PbProjectRouting", "PcDocumentRevision", "PcDocumentRevisionFile", "Itp", "ItpActivity", "NonConformance", "Capa", "ReleaseNote",
                "MfRoutingRevision", "MfOperationStandard", "MfProductionOrder", "MfOperation", "MfOrderComponent", "PurchaseOrderLine", "GlActivation", "GlLegacyUse", "GlCharAdoption", "GlConfigAdoption" };
            foreach (string n in names)
            {
                Assert.IsNotNull(AuditEntityLabels.TypeOf(n), "not in the drift scope: " + n);
                var t = AuditEntityLabels.TypeOf(n);
                var trail = typeof(AuditEntityLabels).Assembly.GetType("DataLayer.Context.FinanceAuditTrail");
                var audited = (System.Collections.IDictionary)trail.GetField("Audited", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                Assert.IsTrue(audited.Contains(t), "no audited columns (the drift check would have nothing to compare): " + n);
            }
        }

        // ------------------------------------------------------------------------------------------ cut-over instant

        [TestMethod]
        public void Without_A_Recorded_Start_Nothing_Is_Legacy_And_The_Start_Is_Recorded_Once()
        {
            AppHost host = Host();
            DateTimeOffset before = host.Read(uow => CutOver.StartAsync(uow, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(DateTimeOffset.MinValue, before);
            Assert.IsFalse(CutOver.IsLegacy(DateTimeOffset.Now.AddYears(-1), before));
            bool first = false, second = true;
            host.Write(uow => { first = CutOver.EnsureRecordedAsync(uow, null, Ct).GetAwaiter().GetResult(); });
            host.Write(uow => { second = CutOver.EnsureRecordedAsync(uow, null, Ct).GetAwaiter().GetResult(); });
            Assert.IsTrue(first);
            Assert.IsFalse(second);
            DateTimeOffset after = host.Read(uow => CutOver.StartAsync(uow, Ct).GetAwaiter().GetResult());
            Assert.IsTrue(after > DateTimeOffset.MinValue);
            Assert.IsTrue(CutOver.IsLegacy(after.AddDays(-1), after));
            Assert.IsFalse(CutOver.IsLegacy(after.AddDays(1), after));
        }

        [TestMethod]
        public void The_Legacy_Register_Keeps_One_Row_Per_Kind_And_Object()
        {
            AppHost host = Host();
            host.Write(uow =>
            {
                GlLegacyRegister.NoteAsync(uow, GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5, null, null, "RN-1", "n", 21, Ct).GetAwaiter().GetResult();
                GlLegacyRegister.NoteAsync(uow, GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5, null, null, "RN-1", "n", 21, Ct).GetAwaiter().GetResult();
                uow.SaveChanges();
            });
            host.Write(uow => { GlLegacyRegister.NoteAsync(uow, GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5, null, null, "RN-1", "n", 21, Ct).GetAwaiter().GetResult(); uow.SaveChanges(); });
            Assert.AreEqual(1, host.Read(uow => uow.Set<GlLegacyUse>().Count()));
        }

        // ------------------------------------------------------------------------------------------ record drift on the new tables

        private sealed class RawContext : DbContext
        {
            public RawContext(DbContextOptions options) : base(options) { }
            public DbSet<GlLegacyUse> Uses { get; set; }
            protected override void OnModelCreating(ModelBuilder builder) { builder.Entity<GlLegacyUse>().ToTable("GlLegacyUses").HasKey(x => x.Id); }
        }

        [TestMethod]
        public async Task A_Direct_Change_Of_A_Legacy_Use_Row_Is_Found_By_The_Record_Drift_Check()
        {
            AppHost host = Host();
            int id = 0;
            host.Write(uow =>
            {
                GlLegacyRegister.NoteAsync(uow, GlLegacyKind.QcAcceptWithoutList, "QcRecord:1", null, 3, 4, "QIR-1", "evidence in the note", 21, Ct).GetAwaiter().GetResult();
                uow.SaveChanges();
                id = uow.Set<GlLegacyUse>().Single().Id;
            }, Eng1);
            using (var raw = new RawContext(host.RawOptions<RawContext>())) { raw.Uses.Single(u => u.Id == id).Note = "edited by hand"; raw.SaveChanges(); }
            AuditChainRunDto run = Services_Ok(await host.As(Pmo1, sp => sp.GetRequiredService<IAuditIntegrityService>().RunOnDemandAsync(new AuditChainRunRequestDto { Mode = "incremental" }, Ct)));
            Assert.AreEqual("Broken", run.Status);
            Assert.AreEqual("record-drift", run.BreakKind);
            Assert.AreEqual(nameof(GlLegacyUse), run.BreakEntityType);
            Assert.AreEqual("Note", run.BreakColumn);
        }

        private static T Services_Ok<T>(Common.Results.ResponseDto<T> r) { Assert.IsTrue(r.Success, r.Message); return r.Data; }

        // ------------------------------------------------------------------------------------------ adoption rules (no data needed)

        [TestMethod]
        public void An_Ai_Seat_And_A_Wrong_Role_Cannot_Request_An_Adoption()
        {
            AppHost host = Host();
            var wrong = host.As(Plain, sp => sp.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(new GlSaveConfigAdoptionDto { WorkOrderId = 1, ProductId = 1, BomHeaderId = 1, Reason = new string('x', 30), EvidenceRef = "evidence", ComparisonNote = new string('y', 30) }, Ct)).GetAwaiter().GetResult();
            Assert.IsFalse(wrong.Success);
            var wrongChar = host.As(Qa1, sp => sp.GetRequiredService<IGlCharAdoptionService>().RequestAsync(new GlSaveCharAdoptionDto { RevisionId = 1, Reason = new string('x', 30), EvidenceRef = "evidence" }, Ct)).GetAwaiter().GetResult();
            Assert.IsFalse(wrongChar.Success, "QA is the checker, never the maker");
            var empty = host.As(Eng1, sp => sp.GetRequiredService<IGlConfigAdoptionService>().RequestAsync(new GlSaveConfigAdoptionDto(), Ct)).GetAwaiter().GetResult();
            Assert.IsFalse(empty.Success);
        }

        [TestMethod]
        public void The_Ceo_Inbox_Is_Read_Only_Grouped_And_Closed_To_Other_Roles()
        {
            AppHost host = Host();
            var denied = host.As(Plain, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct)).GetAwaiter().GetResult();
            Assert.IsFalse(denied.Success);
            var ok = host.As(Ceo, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct)).GetAwaiter().GetResult();
            Assert.IsTrue(ok.Success);
            CollectionAssert.AreEqual(new[] { "findings", "finding-void", "acceptances", "readiness", "safety", "chain", "inbox" }, ok.Data.Groups.Select(g => g.Key).ToArray());
            Assert.AreEqual(0, ok.Data.Total);
            // there is no bulk approve: the service has no write method at all
            Assert.IsFalse(typeof(IGoLiveOverviewService).GetMethods().Any(m => m.Name.Contains("Approve", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void The_Overview_And_The_Revision_Precheck_Answer_On_An_Empty_Database()
        {
            AppHost host = Host();
            var o = host.As(Pmo1, sp => sp.GetRequiredService<IGoLiveOverviewService>().OverviewAsync(Ct)).GetAwaiter().GetResult();
            Assert.IsTrue(o.Success);
            Assert.AreEqual(0, o.Data.LegacyWorkOrders);
            var p = host.As(Pmo1, sp => sp.GetRequiredService<IGoLiveOverviewService>().RevisionPreCheckAsync(Ct)).GetAwaiter().GetResult();
            Assert.IsTrue(p.Success);
            Assert.IsFalse(p.Data.CurrentlyRequired, "the default of EdmsRequireRevision is not changed");
            Assert.AreEqual(0, p.Data.WouldBreak);
        }

        // ------------------------------------------------------------------------------------------ PDF stamper

        private static byte[] MinimalPdf(int pages)
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            void Obj(string body) { offsets.Add(sb.Length); sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n"); }
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i * 2) + " 0 R")) + "] /Count " + pages + " /MediaBox [0 0 595 842] >>");
            for (int i = 0; i < pages; i++)
            {
                Obj($"<< /Type /Page /Parent 2 0 R /Contents {4 + i * 2} 0 R /Resources << /ProcSet [/PDF] >> >>");
                string content = "0 0 m 100 100 l S";
                Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            }
            int xref = sb.Length;
            sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        private static PdfStampImage Img(int w, int h) => new PdfStampImage { Width = w, Height = h, Rgba = Enumerable.Range(0, w * h * 4).Select(i => (byte)(i % 251)).ToArray() };

        [TestMethod]
        public void The_Stamper_Appends_An_Incremental_Update_That_Draws_The_Header_And_Footer_On_Every_Page()
        {
            byte[] pdf = MinimalPdf(2);
            byte[] stamped = PdfStamper.Stamp(pdf, Img(40, 6), Img(40, 3));
            Assert.IsTrue(stamped.Length > pdf.Length);
            CollectionAssert.AreEqual(pdf, stamped.Take(pdf.Length).ToArray(), "the original bytes are untouched");
            string text = Encoding.Latin1.GetString(stamped, pdf.Length, stamped.Length - pdf.Length);
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(text, "/GlStampH Do").Count);
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(text, "/GlStampF Do").Count);
            Assert.IsTrue(text.Contains("/SMask"));
            Assert.IsTrue(text.Contains("/Prev "));
            Assert.IsTrue(text.TrimEnd().EndsWith("%%EOF"));
            // the new cross-reference offsets point at the objects they name
            string whole = Encoding.Latin1.GetString(stamped);
            int sx = whole.LastIndexOf("startxref", StringComparison.Ordinal);
            int xrefAt = int.Parse(whole.Substring(sx + 9).Trim().Split('\n')[0]);
            Assert.AreEqual("xref", whole.Substring(xrefAt, 4));
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(whole.Substring(xrefAt), @"(\d+) 1\n(\d{10}) 00000 n"))
                Assert.AreEqual(m.Groups[1].Value + " 0 obj", whole.Substring(int.Parse(m.Groups[2].Value), (m.Groups[1].Value + " 0 obj").Length));
            // the stamped file can be stamped again (its own update chain is readable)
            byte[] twice = PdfStamper.Stamp(stamped, Img(40, 6), Img(40, 3));
            Assert.IsTrue(twice.Length > stamped.Length);
        }

        [TestMethod]
        public void The_Stamper_Refuses_What_It_Cannot_Read_With_A_Persian_Message()
        {
            Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(Encoding.ASCII.GetBytes("not a pdf at all, plain text of some length"), Img(4, 2), Img(4, 2)));
            byte[] xrefStream = Encoding.Latin1.GetBytes("%PDF-1.5\n1 0 obj\n<< /Type /XRef /Size 2 >>\nstream\nxxxx\nendstream\nendobj\nstartxref\n9\n%%EOF\n");
            var ex = Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(xrefStream, Img(4, 2), Img(4, 2)));
            Assert.IsTrue(ex.Message.Contains("PDF"));
            Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(MinimalPdf(1), new PdfStampImage { Width = 4, Height = 2, Rgba = new byte[3] }, Img(4, 2)));
        }

        [TestMethod]
        public void The_Controlled_Copy_Texts_Name_Document_Revision_Release_Date_And_The_Uncontrolled_Footer()
        {
            string h = GlStampText.Header("DWG-1", "B", "1405/03/02", "1405/04/01 10:30", "eng.one");
            foreach (string part in new[] { "نسخه کنترل‌شده", "DWG-1", "B", "1405/03/02", "1405/04/01 10:30", "eng.one" }) Assert.IsTrue(h.Contains(part), part);
            Assert.AreEqual("نسخه کنترل‌نشده پس از چاپ", GlStampText.Footer);
        }
    }
}
