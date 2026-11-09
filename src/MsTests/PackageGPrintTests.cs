using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.GoLive;
using ViewModels.NasbNiro.GoLiveDtos;

namespace MsTests
{
    /// <summary>Package G, fix round 1 (H3, M8): the controlled copy is made on the server from the stored file of a revision; the print is recorded; what cannot be stamped is refused.</summary>
    [TestClass]
    public class PackageGPrintTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Operator = AppHost.Principal(70, "operator.one", "OPERATOR");
        private static readonly ClaimsPrincipal Operator2 = AppHost.Principal(71, "operator.two", "OPERATOR");
        private static readonly ClaimsPrincipal Sales = AppHost.Principal(90, "user.one", "SALES");

        private static AppHost Host(string name)
        {
            var h = new AppHost("PKGP_" + name + "_" + Guid.NewGuid().ToString("N"));
            h.Write(u => { foreach (int id in new[] { 70, 71 }) AppHost.Add(u, new User { Id = id, UserName = "print.user" + id, Email = "print.user" + id + "@example.com", IsActive = true }); u.SaveChanges(); }, Seeder);
            return h;
        }

        private static PdfStampImage Img(int w, int h) => new PdfStampImage { Width = w, Height = h, Rgba = Enumerable.Range(0, w * h * 4).Select(i => (byte)(i % 251)).ToArray() };

        /// <summary>A minimal one-page PDF; <paramref name="pageExtra"/> goes into the page dictionary (for the geometry refusals).</summary>
        internal static byte[] Pdf(string pageExtra = "", string mediaBox = "/MediaBox [0 0 595 842]")
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            void Obj(string body) { offsets.Add(sb.Length); sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n"); }
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 " + mediaBox + " >>");
            Obj("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /ProcSet [/PDF] >> " + pageExtra + " >>");
            string content = "0 0 m 100 100 l S";
            Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            int xref = sb.Length;
            sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        private static (int Rev, Guid File) Revision(AppHost h, PcDocStatus status, byte[] bytes, string tag)
        {
            int contract = 0, doc = 0, rev = 0;
            var file = Guid.NewGuid();
            h.Write(u =>
            {
                contract = AppHost.Add(u, new ContractEntity { Code = "C-" + tag, Title = "c", Status = ContractStatus.Current }).Id;
                u.SaveChanges();
                doc = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-" + tag, Title = "t" }).Id;
                u.SaveChanges();
                rev = AppHost.Add(u, new PcDocumentRevision { DeliverableId = doc, Code = "B", Sequence = 2, Status = status, ReleasedAt = status == PcDocStatus.Released ? DateTimeOffset.Now.AddDays(-3) : (DateTimeOffset?)null }).Id;
                u.SaveChanges();
                AppHost.Add(u, new PcDocumentRevisionFile { RevisionId = rev, DocumentId = file, FileName = "a.pdf", Role = PcRevisionFileRole.Pdf });
                u.SaveChanges();
            }, Seeder);
            AppHost.Files[file] = bytes;
            return (rev, file);
        }

        private static Task<T> With<T>(AppHost h, ClaimsPrincipal who, Func<IGlPrintService, Task<T>> call) => h.As(who, sp => call(sp.GetRequiredService<IGlPrintService>()));

        [TestMethod]
        public async Task A_Controlled_Copy_Is_Numbered_Recorded_And_Stamped_From_The_Stored_File_Of_A_Revision_In_Force()
        {
            AppHost h = Host("ok");
            var r = Revision(h, PcDocStatus.Released, Pdf(), "OK");
            var p1 = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
            Assert.IsTrue(p1.Success, p1.Message);
            Assert.IsTrue(p1.Data.Controlled);
            Assert.AreEqual(1, p1.Data.CopyNumber);
            StringAssert.Contains(p1.Data.Header, "DWG-OK");
            StringAssert.Contains(p1.Data.Header, "نسخه کنترل‌شده");
            var p2 = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
            Assert.AreEqual(2, p2.Data.CopyNumber, "every controlled copy of a revision has its own number");
            Assert.AreEqual(50, p1.Data.MaxMegabytes);

            var out1 = await With(h, Operator, s => s.StampAsync(p1.Data.PrintId, Img(40, 6), Img(40, 3), Ct));
            Assert.AreEqual(200, out1.Status, out1.Error);
            Assert.IsTrue(out1.Pdf.Length > Pdf().Length);
            var log = h.Read(u => u.Set<GlPrintLog>().Single(x => x.Id == p1.Data.PrintId));
            Assert.IsNotNull(log.StampedAt);
            Assert.AreEqual(64, log.SourceSha256.Length);
            Assert.AreEqual(64, log.OutputSha256.Length);
            Assert.AreNotEqual(log.SourceSha256, log.OutputSha256);
            Assert.AreEqual(70, log.PrintedByUserId);
            Assert.IsTrue(h.Read(u => u.Set<FnAuditLog>().Any(a => a.EntityType == "GlPrintLog" && a.EntityId == p1.Data.PrintId)), "the print is part of the audit chain");
            var again = await With(h, Operator, s => s.StampAsync(p1.Data.PrintId, Img(40, 6), Img(40, 3), Ct));
            Assert.AreEqual(400, again.Status, "one print is stamped once");
        }

        [TestMethod]
        public async Task A_Revision_That_Is_Not_In_Force_Or_Asked_As_Uncontrolled_Is_Stamped_Uncontrolled_And_Never_As_Controlled()
        {
            AppHost h = Host("unc");
            var draft = Revision(h, PcDocStatus.Draft, Pdf(), "UN1");
            var p = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = draft.Rev }, Ct));
            Assert.IsTrue(p.Success, p.Message);
            Assert.IsFalse(p.Data.Controlled);
            Assert.AreEqual(0, p.Data.CopyNumber);
            StringAssert.Contains(p.Data.Header, "کنترل‌نشده");
            Assert.AreEqual(200, (await With(h, Operator, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct))).Status);
            var released = Revision(h, PcDocStatus.Released, Pdf(), "UN2");
            var asked = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = released.Rev, Uncontrolled = true }, Ct));
            Assert.IsFalse(asked.Data.Controlled);
        }

        [TestMethod]
        public async Task The_Print_Is_Refused_For_Other_Roles_An_Ai_Seat_Another_User_A_Superseded_Revision_And_A_Missing_Revision()
        {
            AppHost h = Host("deny");
            var r = Revision(h, PcDocStatus.Released, Pdf(), "DN");
            Assert.IsFalse((await With(h, Sales, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct))).Success);
            Assert.IsFalse((await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = 9999 }, Ct))).Success);
            var ai = await AiActing.RunAsync(h.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 92, UserName = "seat.qa", Roles = new[] { "OPERATOR" } }, "QA", sp => sp.GetRequiredService<IGlPrintService>().PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
            Assert.IsFalse(ai.Success);

            var p = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
            Assert.AreEqual(400, (await With(h, Operator2, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct))).Status, "another user cannot use somebody's prepared print");
            h.Write(u => { u.Set<PcDocumentRevision>().Single(x => x.Id == r.Rev).Status = PcDocStatus.Superseded; u.SaveChanges(); }, Seeder);
            var late = await With(h, Operator, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct));
            Assert.AreEqual(400, late.Status, "a revision superseded after the prepare is no longer printed as controlled");
            Assert.IsNull(h.Read(u => u.Set<GlPrintLog>().Single(x => x.Id == p.Data.PrintId)).StampedAt);
        }

        [TestMethod]
        public async Task A_File_That_Is_Too_Large_Not_A_Pdf_Or_Rotated_Is_Refused_With_422_So_The_Screen_Falls_Back_To_The_Html_Print()
        {
            AppHost h = Host("422");
            async Task<GlStampOutcome> Try(byte[] bytes, string tag)
            {
                var r = Revision(h, PcDocStatus.Released, bytes, tag);
                var p = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
                Assert.IsTrue(p.Success, p.Message);
                return await With(h, Operator, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct));
            }
            Assert.AreEqual(200, (await Try(Pdf(), "G0")).Status);
            Assert.AreEqual(422, (await Try(Encoding.ASCII.GetBytes("this is plain text and not a pdf file at all"), "G1")).Status);
            var rotated = await Try(Pdf("/Rotate 90"), "G2");
            Assert.AreEqual(422, rotated.Status);
            StringAssert.Contains(rotated.Error, "چرخانده");
            Assert.AreEqual(422, (await Try(Pdf("", "/MediaBox 9 0 R"), "G3")).Status, "an indirect MediaBox");
            Assert.AreEqual(422, (await Try(Pdf("/CropBox [10 10 500 800]"), "G4")).Status, "a CropBox that differs");
            Assert.AreEqual(200, (await Try(Pdf("/CropBox [0 0 595 842]"), "G5")).Status, "a CropBox equal to the MediaBox is fine");
            Assert.AreEqual(200, (await Try(Pdf("/Rotate 0"), "G6")).Status);

            // the size limit is a setting
            h.Write(u => { AppHost.Add(u, new QualitySetting { Key = GlSettingKeys.StampMaxMb, Value = 1, Title = "t", Source = "Quality" }); u.SaveChanges(); }, Seeder);
            byte[] big = new byte[1_200_000];
            Array.Copy(Pdf(), big, Pdf().Length);
            var tooBig = await Try(big, "G7");
            Assert.AreEqual(422, tooBig.Status);
            StringAssert.Contains(tooBig.Error, "مگابایت");
        }

        [TestMethod]
        public void The_Stamper_Refuses_A_Rotated_Page_An_Indirect_Media_Box_And_A_Different_Crop_Box()
        {
            Assert.IsTrue(PdfStamper.Stamp(Pdf(), Img(40, 6), Img(40, 3)).Length > 0);
            Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(Pdf("/Rotate 270"), Img(40, 6), Img(40, 3)));
            Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(Pdf("", "/MediaBox 7 0 R"), Img(40, 6), Img(40, 3)));
            Assert.ThrowsExactly<PdfStampException>(() => PdfStamper.Stamp(Pdf("/CropBox [5 5 500 800]"), Img(40, 6), Img(40, 3)));
        }

        [TestMethod]
        public async Task Only_One_Stamp_Runs_At_A_Time_Per_User()
        {
            AppHost h = Host("gate");
            var r = Revision(h, PcDocStatus.Released, Pdf(), "GT");
            var p = await With(h, Operator, s => s.PrepareAsync(new GlPrintPrepareDto { RevisionId = r.Rev }, Ct));
            // two stamps of the same print at once: exactly one wins, the other is refused (429 while the first runs, or 400 once it is done)
            var both = await Task.WhenAll(With(h, Operator, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct)), With(h, Operator, s => s.StampAsync(p.Data.PrintId, Img(40, 6), Img(40, 3), Ct)));
            Assert.AreEqual(1, both.Count(x => x.Status == 200), string.Join(",", both.Select(x => x.Status)));
        }
    }
}
