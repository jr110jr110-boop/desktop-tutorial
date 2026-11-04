using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts;
using Services.NasbNiro;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Files;
using Services.NasbNiro.Procurement;
using Services.Services;
using ViewModels.Exceptions;
using ViewModels.NasbNiro.AttachmentDtos;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.DocumentDtos;

namespace MsTests
{
    /// <summary>
    /// PB3: files attached to a record through the whole chain: the upload allow-list and the size cap, remove before approval and void/supersede after it (never a physical delete),
    /// the purchase request's technical documents passed down by link with the read rights of the downstream record, QC never seeing prices, and the request dossier.
    /// </summary>
    [TestClass]
    public class AttachmentPb3Tests
    {
        // users
        private const int Requester = 11, Planner = 12, QcUser = 13, Storekeeper = 14, Buyer = 15, Accountant = 16, Stranger = 17, PmoUser = 18, Approver = 19;
        private static readonly string[] RequesterRoles = { "ENGINEERING" };
        private static readonly string[] PlannerRoles = { "PLANNING" };
        private static readonly string[] QcRoles = { "QC" };
        private static readonly string[] StoreRoles = { "WAREHOUSE" };
        private static readonly string[] BuyerRoles = { "COMMERCIAL" };
        private static readonly string[] AccountRoles = { "FINANCIAL" };
        private static readonly string[] StrangerRoles = { "SALES" };
        private static readonly string[] PmoRoles = { "PMO" };

        private static AppHost NewHost() => FinanceTestSupport.NewHost("AttachmentPb3");

        private sealed class StubDocuments : IDocumentService
        {
            public Task<Document> GetDocumentByIdAsync(Guid documentId) => throw new NotSupportedException();
            public Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto) => Task.FromResult(Guid.NewGuid());
            public Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId) => throw new NotSupportedException();
            public Task<bool> DeleteDocumentAsync(Guid image) => throw new InvalidOperationException("an attachment must never delete a stored file");
            public Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto) => Task.FromResult(true);
            public Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto) => throw new NotSupportedException();
            public Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => Task.FromResult(false);
            public Task<string> GetFileHashAsync(Guid documentId) => Task.FromResult<string>(null);
        }

        private static T Run<T>(AppHost host, int user, string[] roles, Func<AttachmentService, Task<T>> call) =>
            host.Read(uow => call(new AttachmentService(uow, new TestUser(user, roles), new StubDocuments(), null)).GetAwaiter().GetResult());

        private static T Dossier<T>(AppHost host, int user, string[] roles, Func<DossierService, Task<T>> call) =>
            host.Read(uow =>
            {
                var me = new TestUser(user, roles);
                return call(new DossierService(uow, me, new AttachmentService(uow, me, new StubDocuments(), null))).GetAwaiter().GetResult();
            });

        private static byte[] Pdf(int extra = 0) => Encoding.ASCII.GetBytes("%PDF-1.4 test" + new string('x', extra));

        private static IFormFile File(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", name);

        private static UploadAttachmentForm Form(AttachmentOwnerType type, int owner, string name = "drawing.pdf", AttachmentKind kind = AttachmentKind.Drawing, byte[] bytes = null, int? supersedes = null) =>
            new UploadAttachmentForm { OwnerType = type, OwnerId = owner, Kind = kind, Title = "عنوان " + name, Revision = "A", File = File(bytes ?? Pdf(), name), SupersedesId = supersedes };

        private static T Seed<T>(AppHost host, T entity) where T : class
        {
            host.Write(uow => { AppHost.Add(uow, entity); uow.SaveChanges(); });
            return entity;
        }

        private sealed class Ids
        {
            public int Provider, Request, Line, Inquiry, Quote, Order, Receipt, Release, Inspection, Ncr, Invoice, Payment;
        }

        /// <summary>One purchase request and everything bought for it, with the numbers the people know them by.</summary>
        private static Ids SeedChain(AppHost host, PurchaseRequestStatus requestStatus = PurchaseRequestStatus.Draft, string prefix = "T1")
        {
            var ids = new Ids();
            ids.Provider = Seed(host, new Provider { Name = "تامین‌کننده نمونه " + prefix }).Id;
            var pr = Seed(host, new PurchaseRequest { Number = "PR-" + prefix, Status = requestStatus, RequestDate = DateTime.Today, Description = "ورق فولادی", CreatorUserId = Requester });
            ids.Request = pr.Id;
            ids.Line = Seed(host, new PurchaseRequestLine { PurchaseRequestId = pr.Id, RowNumber = 1, Description = "ورق", Unit = "عدد", Quantity = 2 }).Id;
            var inq = Seed(host, new PurchaseInquiry { Number = "INQ-" + prefix, PurchaseRequestId = pr.Id, Status = InquiryStatus.Open, CreatorUserId = Buyer });
            ids.Inquiry = inq.Id;
            var quote = Seed(host, new SupplierQuote { InquiryId = inq.Id, ProviderId = ids.Provider, QuoteNumber = "Q-" + prefix, QuoteDate = DateTime.Today, TotalAmount = 1000m });
            ids.Quote = quote.Id;
            Seed(host, new SupplierQuoteLine { QuoteId = quote.Id, RequestLineId = ids.Line, Quantity = 2, UnitPrice = 500m });
            var po = Seed(host, new PurchaseOrder { Number = "PO-" + prefix, Status = PurchaseOrderStatus.Draft, ProviderId = ids.Provider, InquiryId = inq.Id, OrderDate = DateTime.Today, CreatorUserId = Buyer });
            ids.Order = po.Id;
            Seed(host, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, RequestLineId = ids.Line, Quantity = 2, UnitPrice = 500m });
            var receipt = Seed(host, new InventoryDocument { Type = InventoryDocumentType.PurchaseReceipt, Number = "RC-" + prefix, Status = InventoryDocumentStatus.Draft, PurchaseOrderId = po.Id, WarehouseId = 1, DocumentDate = DateTime.Today, CreatorUserId = Storekeeper });
            ids.Receipt = receipt.Id;
            var release = Seed(host, new InventoryDocument { Type = InventoryDocumentType.QcRelease, Number = "QR-" + prefix, Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today, CreatorUserId = Storekeeper });
            ids.Release = release.Id;
            var insp = Seed(host, new QcInspection { Number = "QI-" + prefix, ReceiptDocumentId = receipt.Id, ReleaseDocumentId = release.Id, ItemId = 1, WarehouseId = 1, Status = InspectionStatus.Pending, CreatorUserId = QcUser });
            ids.Inspection = insp.Id;
            ids.Ncr = Seed(host, new NonConformance { Number = "NCR-" + prefix, InspectionId = insp.Id, ReceiptDocumentId = receipt.Id, ItemId = 1, ReportDate = DateTime.Today, Status = NcrStatus.Open, CreatorUserId = QcUser }).Id;
            var inv = Seed(host, new SupplierInvoice { Number = "INV-" + prefix, InvoiceNumber = "F-77-" + prefix, PurchaseOrderId = po.Id, ProviderId = ids.Provider, InvoiceDate = DateTime.Today, TotalAmount = 1000m, Status = SupplierInvoiceStatus.Draft, CreatorUserId = Accountant });
            ids.Invoice = inv.Id;
            ids.Payment = Seed(host, new PaymentRequest { Number = "PAY-" + prefix, PurchaseOrderId = po.Id, InvoiceId = inv.Id, ProviderId = ids.Provider, Amount = 1000m, RequestDate = DateTime.Today, Status = PaymentRequestStatus.Draft, CreatorUserId = Buyer }).Id;
            return ids;
        }

        private static void SetStatus<T>(AppHost host, int id, Action<T> change) where T : class
        {
            host.Write(uow => { T row = uow.Set<T>().Find(id); change(row); uow.SaveChanges(); });
        }

        // ------------------------------------------------------------------------------------------ allow-list, size, signature

        [TestMethod]
        public void AllowList_Accepts_Exactly_The_Agreed_Types()
        {
            foreach (string ext in new[] { "xlsx", "xls", "docx", "doc", "pdf", "jpg", "jpeg", "png", "tif", "tiff", "dwg", "dxf", "step", "stp", "txt", "csv", "msg", "eml", "heic", "heif" })
            {
                Assert.IsTrue(AttachmentRules.IsAllowed("file." + ext, out _), ext);
                Assert.IsTrue(AttachmentRules.IsAllowed("FILE." + ext.ToUpperInvariant(), out _), "upper case " + ext);
            }
            foreach (string name in new[] { "virus.exe", "a.zip", "a.html", "a.js", "a.bat", "a.pdf.exe", "noextension", "a.svg", "a.dll", "a.ps1" })
                Assert.IsFalse(AttachmentRules.IsAllowed(name, out _), name);
            // a zip travels only with a letter
            Assert.IsFalse(AttachmentRules.IsAllowed("mail.zip", false, out _));
            Assert.IsTrue(AttachmentRules.IsAllowed("mail.zip", true, out _));
            Assert.IsFalse(AttachmentRules.IsAllowed("run.exe", true, out _), "the letter context does not widen anything else");
        }

        [TestMethod]
        public void Signature_Check_Matches_The_Extension_And_Refuses_Disguised_Files()
        {
            byte[] Bytes(params byte[] b) => b.Concat(new byte[40]).ToArray();
            Assert.IsTrue(AttachmentRules.MatchesSignature(".pdf", Pdf(), Pdf().Length));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".jpg", Bytes(0xFF, 0xD8, 0xFF, 0xE0), 44));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".png", Bytes(0x89, 0x50, 0x4E, 0x47), 44));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".tif", Bytes(0x49, 0x49, 0x2A, 0x00), 44));
            byte[] ooxml = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(new byte[26]).Concat(Encoding.ASCII.GetBytes("[Content_Types].xml")).ToArray();
            Assert.IsTrue(AttachmentRules.MatchesSignature(".xlsx", ooxml, ooxml.Length));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".docx", ooxml, ooxml.Length));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".xls", ooxml, ooxml.Length));
            Assert.IsFalse(AttachmentRules.MatchesSignature(".xlsx", Bytes(0x50, 0x4B, 0x03, 0x04), 44), "a plain ZIP (jar, apk) is not an Office file");
            Assert.IsTrue(AttachmentRules.MatchesSignature(".doc", Bytes(0xD0, 0xCF, 0x11, 0xE0), 44));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".dwg", Encoding.ASCII.GetBytes("AC1027" + new string('x', 20)), 26));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".stp", Encoding.ASCII.GetBytes("ISO-10303-21;\nHEADER;"), 21));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".dxf", Encoding.ASCII.GetBytes("  0\nSECTION\n  2\nHEADER"), 22));
            // an executable renamed to .pdf / .xlsx / .jpg is not accepted
            byte[] exe = Bytes(0x4D, 0x5A, 0x90, 0x00);
            foreach (string ext in new[] { ".pdf", ".xlsx", ".jpg", ".png", ".docx", ".dwg", ".stp" })
                Assert.IsFalse(AttachmentRules.MatchesSignature(ext, exe, 44), ext);
            Assert.IsFalse(AttachmentRules.MatchesSignature(".dxf", exe, 44), "a binary file with NUL bytes is not a text DXF");
            Assert.IsFalse(AttachmentRules.MatchesSignature(".pdf", new byte[0], 0));

            // PDF: %PDF at the start (after a BOM or white space), never an executable with a PDF later (polyglot)
            byte[] polyglot = Encoding.ASCII.GetBytes("MZ......%PDF-1.4 hidden");
            Assert.IsFalse(AttachmentRules.MatchesSignature(".pdf", polyglot, polyglot.Length));
            byte[] junkBefore = Encoding.ASCII.GetBytes("junk%PDF-1.4");
            Assert.IsFalse(AttachmentRules.MatchesSignature(".pdf", junkBefore, junkBefore.Length));
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF, 0x0A }.Concat(Pdf()).ToArray();
            Assert.IsTrue(AttachmentRules.MatchesSignature(".pdf", bom, bom.Length));
            // DXF: a SECTION opens it; HTML or script named .dxf is refused
            foreach (string notDxf in new[] { "<html><script>alert(1)</script></html>", "@echo off\r\ndel *.*", "just some text" })
                Assert.IsFalse(AttachmentRules.MatchesSignature(".dxf", Encoding.ASCII.GetBytes(notDxf), notDxf.Length), notDxf);
            byte[] dxfComment = Encoding.ASCII.GetBytes("999\r\nmade by cad\r\n0\r\nSECTION\r\n2\r\nHEADER");
            Assert.IsTrue(AttachmentRules.MatchesSignature(".dxf", dxfComment, dxfComment.Length));
            // executables are refused under every name, text formats included
            foreach (string ext in new[] { ".txt", ".csv", ".eml", ".msg", ".heic", ".zip" })
                Assert.IsFalse(AttachmentRules.MatchesSignature(ext, exe, 44), ext);
            byte[] elf = Bytes(0x7F, 0x45, 0x4C, 0x46);
            Assert.IsFalse(AttachmentRules.MatchesSignature(".txt", elf, 44));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".txt", Encoding.UTF8.GetBytes("سلام hello"), Encoding.UTF8.GetBytes("سلام hello").Length));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".csv", Encoding.ASCII.GetBytes("a,b\n1,2"), 7));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".msg", Bytes(0xD0, 0xCF, 0x11, 0xE0), 44));
            Assert.IsTrue(AttachmentRules.MatchesSignature(".heic", Bytes(0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c'), 52));
        }

        [TestMethod]
        public void Upload_Refuses_A_Wrong_Type_A_Disguised_File_And_A_File_Over_The_Cap_In_Persian()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);

            ResponseDto<AttachmentDto> exe = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "tool.exe"), CancellationToken.None));
            Assert.IsFalse(exe.Success);
            StringAssert.Contains(exe.Message, "پذیرفته نمی‌شود");
            StringAssert.Contains(exe.Message, ".pdf");

            ResponseDto<AttachmentDto> disguised = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "fake.pdf", bytes: Encoding.ASCII.GetBytes("MZ this is not a pdf")), CancellationToken.None));
            Assert.IsFalse(disguised.Success);
            StringAssert.Contains(disguised.Message, "هم‌خوان");

            ResponseDto<AttachmentDto> empty = Run(host, Requester, RequesterRoles, s => s.UploadAsync(new UploadAttachmentForm { OwnerType = AttachmentOwnerType.PurchaseRequest, OwnerId = c.Request, Kind = AttachmentKind.Drawing, File = File(new byte[0], "a.pdf") }, CancellationToken.None));
            Assert.IsFalse(empty.Success);

            // the cap is a setting (default 50 MB): 1 MB makes a 1.5 MB file too large
            host.Write(uow =>
            {
                ProcurementSettingKeys.LoadAsync(uow, CancellationToken.None).GetAwaiter().GetResult();
                uow.Set<ProcurementSetting>().Single(x => x.Key == AttachmentSettingKeys.MaxMegabytes).Value = 1m;
                uow.SaveChanges();
            });
            ResponseDto<AttachmentDto> big = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, bytes: Pdf(1_500_000)), CancellationToken.None));
            Assert.IsFalse(big.Success);
            StringAssert.Contains(big.Message, "حجم فایل");
            StringAssert.Contains(big.Message, "1 مگابایت");
            ResponseDto<AttachmentDto> small = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request), CancellationToken.None));
            Assert.IsTrue(small.Success, small.Message);

            Assert.AreEqual(50, AttachmentRules.DefaultMaxMegabytes);
            Assert.AreEqual(50m, ProcurementSettingKeys.Defaults().Single(d => d.Key == AttachmentSettingKeys.MaxMegabytes).Value);
        }

        // ------------------------------------------------------------------------------------------ remove, void, supersede

        [TestMethod]
        public void Before_Approval_Only_The_Uploader_Removes_And_It_Is_Logged_The_File_Stays()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto a = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request), CancellationToken.None)).Data;
            Assert.IsTrue(a.CanRemove);
            Assert.IsFalse(a.Locked);

            // another writer of the module cannot remove somebody else's file
            ResponseDto<bool> other = Run(host, PmoUser, PmoRoles, s => s.RemoveAsync(a.Id, CancellationToken.None));
            Assert.IsFalse(other.Success);
            StringAssert.Contains(other.Message, "بارگذاری کرده");
            // a stranger from another department sees nothing and does nothing
            Assert.IsFalse(Run(host, Stranger, StrangerRoles, s => s.RemoveAsync(a.Id, CancellationToken.None)).Success);
            // a colleague of the maker may not touch the request's files at all while it is a draft
            Assert.IsFalse(Run(host, Planner, PlannerRoles, s => s.RemoveAsync(a.Id, CancellationToken.None)).Success);

            Assert.IsTrue(Run(host, Requester, RequesterRoles, s => s.RemoveAsync(a.Id, CancellationToken.None)).Success);
            Assert.AreEqual(0, Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data.Items.Count);
            // the row and its document id stay in the table; the log has the removal
            Assert.AreEqual(1, host.Read(uow => uow.Set<RecordAttachment>().IgnoreQueryFilters().Count(x => x.Id == a.Id && x.RemovedAt != null && x.DocumentId == a.DocumentId)));
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(l => l.EntityType == "RecordAttachment" && l.Action == "Removed" && l.EntityId == a.Id)));
        }

        [TestMethod]
        public void After_Approval_The_File_Can_Only_Be_Voided_With_A_Reason_Or_Superseded_Never_Removed()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto a = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request), CancellationToken.None)).Data;
            SetStatus<PurchaseRequest>(host, c.Request, r => r.Status = PurchaseRequestStatus.Approved);

            ResponseDto<bool> remove = Run(host, Requester, RequesterRoles, s => s.RemoveAsync(a.Id, CancellationToken.None));
            Assert.IsFalse(remove.Success);
            StringAssert.Contains(remove.Message, "ابطال");
            AttachmentPanelDto panel = Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data;
            Assert.IsTrue(panel.Locked);
            Assert.IsFalse(panel.Items.Single().CanRemove);
            Assert.IsFalse(panel.Items.Single().CanVoid, "the maker no longer changes the files of a request that was approved");
            AttachmentPanelDto planning = Run(host, Planner, PlannerRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data;
            Assert.IsTrue(planning.Items.Single().CanVoid);
            Assert.IsTrue(planning.Items.Single().CanSupersede);
            Assert.IsFalse(planning.Items.Single().CanRemove, "nobody removes a file of an approved request");

            // no reason: refused. A user outside the module: refused. The module's writer with a reason: voided, with user and time
            Assert.IsFalse(Run(host, Requester, RequesterRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = a.Id, Reason = "  " }, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Stranger, StrangerRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = a.Id, Reason = "اشتباه بود" }, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, QcUser, new[] { "FINANCIAL" }, s => s.VoidAsync(new VoidAttachmentDto { Id = a.Id, Reason = "اشتباه بود" }, CancellationToken.None)).Success, "finance reads the request but does not write its files");
            ResponseDto<AttachmentDto> voided = Run(host, Planner, PlannerRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = a.Id, Reason = "نقشه ابطال و بازنگری شد" }, CancellationToken.None));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.IsTrue(voided.Data.Voided);
            Assert.AreEqual("نقشه ابطال و بازنگری شد", voided.Data.VoidReason);
            Assert.IsFalse(voided.Data.Current);
            Assert.IsFalse(Run(host, Planner, PlannerRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = a.Id, Reason = "دوباره" }, CancellationToken.None)).Success, "a voided file is not voided twice");

            RecordAttachment row = host.Read(uow => uow.Set<RecordAttachment>().Single(x => x.Id == a.Id));
            Assert.IsTrue(row.Voided);
            Assert.AreEqual(Planner, row.VoidedByUserId);
            Assert.IsNotNull(row.VoidedAt);
            Assert.IsFalse(row.Deleted, "voiding is not deleting");
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(l => l.EntityType == "RecordAttachment" && l.Action == "Voided" && l.EntityId == a.Id)));
            // the history still lists the voided file
            Assert.AreEqual(1, Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data.Items.Count(i => i.Voided));
        }

        [TestMethod]
        public void A_New_Revision_Supersedes_The_Old_One_And_The_Chain_Is_Kept()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto a = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "rev-a.pdf"), CancellationToken.None)).Data;
            SetStatus<PurchaseRequest>(host, c.Request, r => r.Status = PurchaseRequestStatus.Approved);
            ResponseDto<AttachmentDto> b = Run(host, Planner, PlannerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "rev-b.pdf", supersedes: a.Id), CancellationToken.None));
            Assert.IsTrue(b.Success, b.Message);

            AttachmentPanelDto panel = Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data;
            Assert.AreEqual(2, panel.Items.Count);
            AttachmentDto old = panel.Items.Single(i => i.Id == a.Id);
            Assert.AreEqual(b.Data.Id, old.SupersededById);
            Assert.IsFalse(old.Current);
            Assert.IsTrue(panel.Items.Single(i => i.Id == b.Data.Id).Current);
            Assert.IsFalse(old.CanVoid, "the old revision is history");

            Assert.IsFalse(Run(host, Planner, PlannerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "rev-c.pdf", supersedes: a.Id), CancellationToken.None)).Success, "an old revision is superseded once");
            // the downstream records see only the revision in force
            AttachmentPanelDto order = Run(host, Buyer, BuyerRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseOrder, c.Order, CancellationToken.None)).Data;
            Assert.AreEqual(b.Data.Id, order.Propagated.Single().Items.Single().Id);
        }

        [TestMethod]
        public async Task A_Stored_File_Held_By_An_Attachment_Cannot_Be_Deleted_Physically()
        {
            AppHost host = NewHost();
            Guid doc = Guid.Empty;
            host.Write(uow =>
            {
                Document d = new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + ".pdf", ".pdf").SetUploader(Requester).SetName("scan").SetCode("T").SetDescription("d");
                AppHost.Add(uow, d);
                uow.SaveChanges();
                doc = d.Id;
                AppHost.Add(uow, new RecordAttachment { OwnerType = AttachmentOwnerType.PurchaseRequest, OwnerId = 1, DocumentId = d.Id, Kind = AttachmentKind.Drawing, UploadedByUserId = Requester, UploadedAt = DateTimeOffset.Now });
                uow.SaveChanges();
            });
            Guid id = doc;
            await Assert.ThrowsAsync<DomainInvalidException>(async () =>
            {
                using (IServiceScope scope = host.Provider.CreateScope())
                {
                    var service = new DocumentService(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), new TestWebHostEnvironment(), NullLogger<DocumentService>.Instance, new HttpContextAccessor());
                    await service.DeleteDocumentAsync(id);
                }
            });
            Assert.IsTrue(host.Read(uow => uow.Set<Document>().Any(d => d.Id == id)), "the document row is still there");
        }

        [TestMethod]
        public void Every_Owner_Type_Has_Roles_And_A_Persian_Name()
        {
            foreach (AttachmentOwnerType type in Enum.GetValues(typeof(AttachmentOwnerType)))
                Assert.AreNotEqual("رکورد", AttachmentText.OwnerTitle(type), type.ToString());
            foreach (AttachmentKind kind in Enum.GetValues(typeof(AttachmentKind)))
                Assert.IsFalse(string.IsNullOrWhiteSpace(AttachmentText.KindTitle(kind)), kind.ToString());
            Assert.AreEqual(14, Enum.GetValues(typeof(AttachmentKind)).Length);
            // a record that does not exist for an owner type gets no panel (unknown id)
            AppHost host = NewHost();
            foreach (AttachmentOwnerType type in Enum.GetValues(typeof(AttachmentOwnerType)))
                Assert.IsFalse(Run(host, PmoUser, PmoRoles, s => s.GetPanelAsync(type, 999999, CancellationToken.None)).Success, type.ToString());
        }

        [TestMethod]
        public void Every_Owner_Record_Takes_Attachments_From_The_Roles_Of_Its_Module()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            var cases = new (AttachmentOwnerType Type, int Id, int User, string[] Roles)[]
            {
                (AttachmentOwnerType.PurchaseRequest, c.Request, Requester, RequesterRoles),
                (AttachmentOwnerType.PurchaseRequestLine, c.Line, Requester, RequesterRoles),
                (AttachmentOwnerType.Inquiry, c.Inquiry, Buyer, BuyerRoles),
                (AttachmentOwnerType.Quote, c.Quote, Buyer, BuyerRoles),
                (AttachmentOwnerType.PurchaseOrder, c.Order, Buyer, BuyerRoles),
                (AttachmentOwnerType.WarehouseDocument, c.Receipt, Storekeeper, StoreRoles),
                (AttachmentOwnerType.WarehouseDocument, c.Release, Storekeeper, StoreRoles),
                (AttachmentOwnerType.QcInspection, c.Inspection, QcUser, QcRoles),
                (AttachmentOwnerType.Ncr, c.Ncr, QcUser, QcRoles),
                (AttachmentOwnerType.SupplierInvoice, c.Invoice, Accountant, AccountRoles),
                (AttachmentOwnerType.PaymentRequest, c.Payment, Accountant, AccountRoles),
            };
            foreach (var k in cases)
            {
                ResponseDto<AttachmentDto> ok = Run(host, k.User, k.Roles, s => s.UploadAsync(Form(k.Type, k.Id), CancellationToken.None));
                Assert.IsTrue(ok.Success, k.Type + ": " + ok.Message);
                ResponseDto<AttachmentDto> no = Run(host, Stranger, StrangerRoles, s => s.UploadAsync(Form(k.Type, k.Id), CancellationToken.None));
                Assert.IsFalse(no.Success, k.Type + " must refuse a user of another department");
                Assert.IsFalse(Run(host, Stranger, StrangerRoles, s => s.GetPanelAsync(k.Type, k.Id, CancellationToken.None)).Success, k.Type + " must not show its files to another department");
            }
            // a payment slip is attached by finance, not by the buyer
            ResponseDto<AttachmentDto> slip = Run(host, Buyer, BuyerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PaymentRequest, c.Payment, "slip.pdf", AttachmentKind.PaymentSlip), CancellationToken.None));
            Assert.IsFalse(slip.Success);
            Assert.IsTrue(Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PaymentRequest, c.Payment, "slip.pdf", AttachmentKind.PaymentSlip), CancellationToken.None)).Success);
        }

        // ------------------------------------------------------------------------------------------ propagation

        [TestMethod]
        public void Technical_Documents_Of_The_Request_Show_On_Every_Downstream_Record_As_Links()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto drawing = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "bracket.pdf"), CancellationToken.None)).Data;
            AttachmentDto lineSpec = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequestLine, c.Line, "spec.pdf", AttachmentKind.Specification), CancellationToken.None)).Data;
            // a price file on the request is not a technical document and is never passed down
            Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "proforma.pdf", AttachmentKind.Invoice), CancellationToken.None));
            // a voided file is not passed down
            AttachmentDto wrong = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "wrong.pdf"), CancellationToken.None)).Data;
            SetStatus<PurchaseRequest>(host, c.Request, r => r.Status = PurchaseRequestStatus.Approved);
            Assert.IsTrue(Run(host, Planner, PlannerRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = wrong.Id, Reason = "فایل اشتباه" }, CancellationToken.None)).Success);

            var downstream = new (AttachmentOwnerType Type, int Id, int User, string[] Roles)[]
            {
                (AttachmentOwnerType.Inquiry, c.Inquiry, Buyer, BuyerRoles),
                (AttachmentOwnerType.Quote, c.Quote, Buyer, BuyerRoles),
                (AttachmentOwnerType.PurchaseOrder, c.Order, Buyer, BuyerRoles),
                (AttachmentOwnerType.WarehouseDocument, c.Receipt, Storekeeper, StoreRoles),
                (AttachmentOwnerType.WarehouseDocument, c.Release, Storekeeper, StoreRoles),
                (AttachmentOwnerType.QcInspection, c.Inspection, QcUser, QcRoles),
                (AttachmentOwnerType.Ncr, c.Ncr, QcUser, QcRoles),
                (AttachmentOwnerType.SupplierInvoice, c.Invoice, Accountant, AccountRoles),
                (AttachmentOwnerType.PaymentRequest, c.Payment, Accountant, AccountRoles),
            };
            foreach (var d in downstream)
            {
                AttachmentPanelDto panel = Run(host, d.User, d.Roles, s => s.GetPanelAsync(d.Type, d.Id, CancellationToken.None)).Data;
                Assert.IsNotNull(panel, d.Type.ToString());
                PropagatedAttachmentsDto group = panel.Propagated.Single();
                Assert.AreEqual("PR-T1", group.SourceNumber, d.Type.ToString());
                StringAssert.Contains(group.SourceTitle, "مدارک فنی درخواست");
                CollectionAssert.AreEquivalent(new[] { drawing.Id, lineSpec.Id }, group.Items.Select(i => i.Id).ToList(), d.Type + ": the drawing and the line spec, nothing else");
                Assert.IsTrue(group.Items.All(i => i.DocumentId == (i.Id == drawing.Id ? drawing.DocumentId : lineSpec.DocumentId)), "the same stored file, not a copy");
                Assert.IsTrue(group.Items.All(i => !i.CanVoid && !i.CanRemove && !i.CanSupersede), "read-only on the downstream screen");
                Assert.AreEqual(0, panel.Items.Count(i => i.OwnerType == AttachmentOwnerType.PurchaseRequest), "own attachments and propagated ones stay apart");
            }
            // links, never copies: one row for each file in the table
            Assert.AreEqual(4, host.Read(uow => uow.Set<RecordAttachment>().Count()));
            // the request itself shows no propagated section; a stranger sees no panel at all
            Assert.AreEqual(0, Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data.Propagated.Count);
            Assert.IsFalse(Run(host, Stranger, StrangerRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseOrder, c.Order, CancellationToken.None)).Success);
        }

        [TestMethod]
        public void A_Record_Bought_For_One_Line_Shows_That_Lines_Documents_Only()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            int line2 = Seed(host, new PurchaseRequestLine { PurchaseRequestId = c.Request, RowNumber = 2, Description = "لوله", Unit = "عدد", Quantity = 1 }).Id;
            AttachmentDto header = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "header.pdf"), CancellationToken.None)).Data;
            AttachmentDto first = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequestLine, c.Line, "l1.pdf"), CancellationToken.None)).Data;
            AttachmentDto second = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequestLine, line2, "l2.pdf"), CancellationToken.None)).Data;
            // the order buys line 1 only
            CollectionAssert.AreEquivalent(new[] { header.Id, first.Id },
                Run(host, Buyer, BuyerRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseOrder, c.Order, CancellationToken.None)).Data.Propagated.Single().Items.Select(i => i.Id).ToList());
            Assert.IsFalse(Run(host, Buyer, BuyerRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseOrder, c.Order, CancellationToken.None)).Data.Propagated.Single().Items.Any(i => i.Id == second.Id));
        }

        // ------------------------------------------------------------------------------------------ read rights of the stored files, prices

        private static Guid NewDocument(AppHost host, int uploader)
        {
            Guid id = Guid.Empty;
            host.Write(uow =>
            {
                Document d = new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + ".pdf", ".pdf").SetUploader(uploader).SetName("scan").SetDescription("d").SetCode("T");
                AppHost.Add(uow, d);
                uow.SaveChanges();
                id = d.Id;
            });
            return id;
        }

        private sealed class NoOffice : IFileOfficeRules
        {
            public Task<bool> CanReadLetterFileAsync(int letterId, Guid documentId, CancellationToken cancellationToken) => Task.FromResult(false);
            public Task<bool> CanReadLeaveRequestAsync(int leaveRequestId, CancellationToken cancellationToken) => Task.FromResult(false);
            public Task<bool> CanReadMeetingAsync(int meetingId, CancellationToken cancellationToken) => Task.FromResult(false);
        }

        private static FileVerdict ReadFile(AppHost host, int user, string[] roles, Guid id) =>
            host.Read(uow => new FileAccessService(uow, new TestUser(user, roles), new NoOffice()).ReadAsync(id).GetAwaiter().GetResult());

        [TestMethod]
        public void File_Read_Follows_The_Chain_The_Technical_File_Goes_Downstream_The_Price_File_Does_Not()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            Guid drawing = NewDocument(host, Requester), proforma = NewDocument(host, Requester), invoiceImage = NewDocument(host, Accountant), qcReport = NewDocument(host, QcUser);
            host.Write(uow =>
            {
                Func<AttachmentOwnerType, int, Guid, AttachmentKind, RecordAttachment> make = (t, o, d, k) =>
                    AppHost.Add(uow, new RecordAttachment { OwnerType = t, OwnerId = o, DocumentId = d, Kind = k, FileName = "f.pdf", UploadedByUserId = 1, UploadedAt = DateTimeOffset.Now });
                make(AttachmentOwnerType.PurchaseRequest, c.Request, drawing, AttachmentKind.Drawing);
                make(AttachmentOwnerType.PurchaseRequest, c.Request, proforma, AttachmentKind.Invoice);
                make(AttachmentOwnerType.SupplierInvoice, c.Invoice, invoiceImage, AttachmentKind.Invoice);
                make(AttachmentOwnerType.QcInspection, c.Inspection, qcReport, AttachmentKind.QcReport);
                uow.SaveChanges();
            });

            // the drawing of the request: everyone who reads a record downstream of it (the shop floor and the warehouse included), nobody outside
            foreach ((int user, string[] roles) in new[] { (Requester, RequesterRoles), (QcUser, QcRoles), (Storekeeper, StoreRoles), (Buyer, BuyerRoles), (Accountant, AccountRoles), (PmoUser, PmoRoles), (80, new[] { "ASSEMBLER" }) })
                Assert.IsTrue(ReadFile(host, user, roles, drawing).IsAllowed, "technical file, " + string.Join(",", roles));
            Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, Stranger, StrangerRoles, drawing).Kind);

            // a file that carries prices: the price readers only, never QC or the warehouse (on the request and on the invoice alike)
            foreach (Guid priceFile in new[] { proforma, invoiceImage })
            {
                Assert.IsTrue(ReadFile(host, Accountant, AccountRoles, priceFile).IsAllowed);
                Assert.IsTrue(ReadFile(host, Buyer, BuyerRoles, priceFile).IsAllowed);
                Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, QcUser, QcRoles, priceFile).Kind, "QC never sees prices");
                Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, Storekeeper, StoreRoles, priceFile).Kind);
                Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, Stranger, StrangerRoles, priceFile).Kind);
            }
            // the QC report is read by the people of the inspection, and finance (who read the receipt), not by another department
            Assert.IsTrue(ReadFile(host, QcUser, QcRoles, qcReport).IsAllowed);
            Assert.IsTrue(ReadFile(host, Storekeeper, StoreRoles, qcReport).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, Stranger, StrangerRoles, qcReport).Kind);
        }

        [TestMethod]
        public void Qc_Does_Not_See_Price_Files_In_Any_Panel_And_Finance_Does()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "invoice.pdf", AttachmentKind.Invoice), CancellationToken.None));
            Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "cert.pdf", AttachmentKind.Certificate), CancellationToken.None));
            Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "proforma.pdf", AttachmentKind.Invoice), CancellationToken.None));

            AttachmentPanelDto finance = Run(host, Accountant, AccountRoles, s => s.GetPanelAsync(AttachmentOwnerType.SupplierInvoice, c.Invoice, CancellationToken.None)).Data;
            Assert.AreEqual(2, finance.Items.Count);
            AttachmentPanelDto qc = Run(host, QcUser, QcRoles, s => s.GetPanelAsync(AttachmentOwnerType.SupplierInvoice, c.Invoice, CancellationToken.None)).Data;
            CollectionAssert.AreEqual(new[] { AttachmentKind.Certificate }, qc.Items.Select(i => i.Kind).ToList());
            Assert.AreEqual(0, qc.Propagated.SelectMany(g => g.Items).Count(i => AttachmentRules.CarriesPrices(i.Kind)));
            AttachmentPanelDto request = Run(host, QcUser, QcRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data;
            Assert.AreEqual(0, request.Items.Count, "the proforma on the request is a price file");

            // the dossier shows no amounts to QC and shows them to finance
            DossierDto forQc = Dossier(host, QcUser, QcRoles, s => s.GetAsync("PO-T1", CancellationToken.None)).Data;
            Assert.IsTrue(forQc.Steps.SelectMany(s => s.Records).All(r => r.Amount == null), "no amount for QC");
            Assert.AreEqual(0, forQc.Steps.SelectMany(s => s.Records).SelectMany(r => r.Attachments).Count(a => AttachmentRules.CarriesPrices(a.Kind)));
            DossierDto forFinance = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PO-T1", CancellationToken.None)).Data;
            Assert.IsTrue(forFinance.Steps.SelectMany(s => s.Records).Any(r => r.Amount.HasValue && r.Amount > 0));
            Assert.AreEqual(2, forFinance.Steps.SelectMany(s => s.Records).SelectMany(r => r.Attachments).Count(a => AttachmentRules.CarriesPrices(a.Kind)));
        }

        // ------------------------------------------------------------------------------------------ the single-file columns stay readable

        [TestMethod]
        public void The_Old_Single_File_Columns_Show_In_The_Panel_Beside_The_New_Attachments()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            Guid report = NewDocument(host, QcUser), invoice = NewDocument(host, Accountant);
            SetStatus<QcInspection>(host, c.Inspection, i => i.ReportDocumentId = report);
            SetStatus<SupplierInvoice>(host, c.Invoice, i => i.DocumentId = invoice);
            Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.QcInspection, c.Inspection, "second.pdf", AttachmentKind.MeasurementSheet), CancellationToken.None));

            AttachmentPanelDto panel = Run(host, QcUser, QcRoles, s => s.GetPanelAsync(AttachmentOwnerType.QcInspection, c.Inspection, CancellationToken.None)).Data;
            Assert.AreEqual(2, panel.Items.Count);
            Assert.IsTrue(panel.Items.Single(i => i.Legacy).DocumentId == report);
            Assert.IsFalse(panel.Items.Single(i => i.Legacy).CanVoid);
            // the old invoice image keeps its price rule
            Assert.AreEqual(1, Run(host, Accountant, AccountRoles, s => s.GetPanelAsync(AttachmentOwnerType.SupplierInvoice, c.Invoice, CancellationToken.None)).Data.Items.Count(i => i.Legacy));
            Assert.AreEqual(0, Run(host, QcUser, QcRoles, s => s.GetPanelAsync(AttachmentOwnerType.SupplierInvoice, c.Invoice, CancellationToken.None)).Data.Items.Count(i => i.Legacy));
        }

        // ------------------------------------------------------------------------------------------ the request dossier

        [TestMethod]
        public void Dossier_By_Any_Number_Lists_The_Whole_Chain_Step_By_Step_With_Its_Files()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host, PurchaseRequestStatus.Approved);
            Run(host, Planner, PlannerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "bracket.pdf"), CancellationToken.None));
            Run(host, Planner, PlannerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequestLine, c.Line, "line.pdf", AttachmentKind.Specification), CancellationToken.None));
            Run(host, Buyer, BuyerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.Quote, c.Quote, "offer.pdf", AttachmentKind.Other), CancellationToken.None));
            Run(host, Buyer, BuyerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseOrder, c.Order, "po.pdf", AttachmentKind.Form), CancellationToken.None));
            Run(host, Storekeeper, StoreRoles, s => s.UploadAsync(Form(AttachmentOwnerType.WarehouseDocument, c.Receipt, "waybill.pdf", AttachmentKind.DeliveryNote), CancellationToken.None));
            Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.QcInspection, c.Inspection, "qc1.pdf", AttachmentKind.QcReport), CancellationToken.None));
            Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.QcInspection, c.Inspection, "qc2.xlsx", AttachmentKind.MeasurementSheet, bytes: new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }.Concat(Encoding.ASCII.GetBytes("[Content_Types].xml")).ToArray()), CancellationToken.None));
            Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.Ncr, c.Ncr, "ncr.jpg", AttachmentKind.Photo, bytes: new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }), CancellationToken.None));
            Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "inv.pdf", AttachmentKind.Invoice), CancellationToken.None));
            Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PaymentRequest, c.Payment, "slip.pdf", AttachmentKind.PaymentSlip), CancellationToken.None));

            string[] expectedSteps = { "PurchaseRequest", "Inquiry", "Quote", "PurchaseOrder", "TemporaryReceipt", "QcInspection", "Ncr", "FinalReceipt", "SupplierInvoice", "PaymentRequest" };
            foreach (string number in new[] { "PO-T1", "PR-T1", "INQ-T1", "RC-T1", "QR-T1", "QI-T1", "NCR-T1", "INV-T1", "PAY-T1" })
            {
                ResponseDto<DossierDto> r = Dossier(host, Accountant, AccountRoles, s => s.GetAsync(number, CancellationToken.None));
                Assert.IsTrue(r.Success, number + ": " + r.Message);
                CollectionAssert.AreEqual(expectedSteps, r.Data.Steps.Select(s => s.Code).ToList(), "steps found by " + number);
                Assert.AreEqual(10, r.Data.StepCount, number);
                Assert.AreEqual(10, r.Data.RecordCount, number);
                Assert.AreEqual(10, r.Data.AttachmentCount, number + ": 2 on the request (header and line), offer, order, waybill, 2 inspection files, ncr photo, invoice, slip is 10 - the final receipt has none");
            }
            DossierDto d = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PO-T1", CancellationToken.None)).Data;
            DossierRecordDto pr = d.Steps.Single(s => s.Code == "PurchaseRequest").Records.Single();
            Assert.AreEqual("PR-T1", pr.Number);
            Assert.AreEqual("تایید شده", pr.Status);
            Assert.AreEqual(2, pr.Attachments.Count, "header and line files together");
            Assert.IsTrue(d.Steps.Single(s => s.Code == "QcInspection").Records.Single().Attachments.Count == 2);
            Assert.IsTrue(d.Steps.Single(s => s.Code == "PaymentRequest").Records.Single().Attachments.Single().Kind == AttachmentKind.PaymentSlip);
            Assert.AreEqual("/procurement/orders/" + c.Order, d.Steps.Single(s => s.Code == "PurchaseOrder").Records.Single().Route);
            Assert.AreEqual("درخواست خرید", Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PR-T1", CancellationToken.None)).Data.FoundAs);

            // an unknown number, an empty one, and a user outside the chain
            Assert.AreEqual(false, Dossier(host, Accountant, AccountRoles, s => s.GetAsync("NO-SUCH", CancellationToken.None)).Success);
            Assert.AreEqual(false, Dossier(host, Accountant, AccountRoles, s => s.GetAsync(" ", CancellationToken.None)).Success);
            Assert.AreEqual(false, Dossier(host, Stranger, StrangerRoles, s => s.GetAsync("PO-T1", CancellationToken.None)).Success);
        }

        [TestMethod]
        public void Dossier_Offers_The_Payment_Approval_To_The_Approver_Who_Is_Not_The_Maker()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host, PurchaseRequestStatus.Approved, "T2");
            bool Can(int user, string[] roles) => Dossier(host, user, roles, s => s.GetAsync("PO-T2", CancellationToken.None)).Data.Steps.Single(s => s.Code == "PaymentRequest").Records.Single().CanApprovePayment;
            Assert.IsTrue(Can(PmoUser, PmoRoles));
            Assert.IsFalse(Can(Buyer, new[] { "COMMERCIAL", "PMO" }), "the maker (the buyer who wrote the request) never approves it");
            Assert.IsFalse(Can(Accountant, AccountRoles), "finance pays; the PMO deputy and the CEO approve");
            SetStatus<PaymentRequest>(host, c.Payment, p => p.Status = PaymentRequestStatus.Approved);
            Assert.IsFalse(Can(PmoUser, PmoRoles), "an approved request is not offered again");
        }

        [TestMethod]
        public void Dossier_From_A_Petty_Cash_Settlement_Shows_Its_Lines_And_Receipt()
        {
            AppHost host = NewHost();
            var receipt = Seed(host, new InventoryDocument { Type = InventoryDocumentType.PurchaseReceipt, Number = "RC-PC1", Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today });
            var settlement = Seed(host, new FnPettyCashSettlement { Number = "PCS-1", PettyCashId = 1, Date = DateTime.Today, Status = FnDocStatus.Draft, TotalAmount = 500m, CreatedByUserId = Accountant });
            var line = Seed(host, new FnPettyCashSettlementLine { SettlementId = settlement.Id, RowNumber = 1, Description = "خرید متفرقه", InventoryDocumentId = receipt.Id, Amount = 500m });
            Assert.IsTrue(Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PettyCashSettlementLine, line.Id, "factor.pdf", AttachmentKind.Invoice), CancellationToken.None)).Success);

            foreach (string number in new[] { "PCS-1", "RC-PC1" })
            {
                DossierDto d = Dossier(host, Accountant, AccountRoles, s => s.GetAsync(number, CancellationToken.None)).Data;
                CollectionAssert.AreEqual(new[] { "TemporaryReceipt", "PettyCash" }, d.Steps.Select(s => s.Code).ToList(), number);
                Assert.AreEqual(1, d.Steps.Single(s => s.Code == "PettyCash").Records.Single().Attachments.Count);
            }
        }

        // ------------------------------------------------------------------------------------------ legacy detach becomes void, bank links

        [TestMethod]
        public void Legacy_Detach_Voids_The_File_With_A_Reason_And_Never_Deletes_It()
        {
            AppHost host = NewHost();
            int contract = Seed(host, new ContractEntity { Code = "C-PB3", Title = "قرارداد نمونه" }).Id;
            Guid doc = NewDocument(host, PmoUser);
            host.Write(uow => { AppHost.Add(uow, new ContractFile { ContractId = contract, DocumentId = doc }); uow.SaveChanges(); });

            ResponseDto<bool> noReason = host.Read(uow => Contracts(uow, host, PmoUser, PmoRoles).DetachFileFromContractAsync(contract, doc, null, CancellationToken.None).AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(noReason.Success);
            StringAssert.Contains(noReason.Message, "دلیل");
            Assert.AreEqual(1, host.Read(uow => uow.Set<ContractFile>().Count(f => f.ContractId == contract)), "nothing changes without a reason");

            ResponseDto<bool> done = host.Read(uow => Contracts(uow, host, PmoUser, PmoRoles).DetachFileFromContractAsync(contract, doc, "فایل نسخه قدیمی قرارداد بود", CancellationToken.None).AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(0, host.Read(uow => uow.Set<ContractFile>().Count(f => f.ContractId == contract)));
            RecordAttachment kept = host.Read(uow => uow.Set<RecordAttachment>().Single(a => a.OwnerType == AttachmentOwnerType.Contract && a.OwnerId == contract));
            Assert.IsTrue(kept.Voided);
            Assert.AreEqual(doc, kept.DocumentId);
            Assert.AreEqual("فایل نسخه قدیمی قرارداد بود", kept.VoidReason);
            Assert.IsTrue(host.Read(uow => uow.Set<Document>().Any(d => d.Id == doc)), "the stored file is kept");
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(l => l.EntityType == "Contract" && l.EntityId == contract && l.Action == "FileVoided")));
            Assert.AreEqual(PmoUser, kept.UploadedByUserId, "the row names the uploader of the stored file, not the person who voided it");
        }

        [TestMethod]
        public void Editing_A_Draft_Request_Keeps_The_Line_Ids_So_Line_Attachments_Survive()
        {
            AppHost host = NewHost();
            var me = AppHost.Principal(Requester, "req", "ENGINEERING");
            SavePurchaseRequestLineDto L(string d, int? id = null, decimal q = 1) => new SavePurchaseRequestLineDto { Id = id, Description = d, Unit = "عدد", Quantity = q };
            SavePurchaseRequestDto Req(int? id, params SavePurchaseRequestLineDto[] lines) => new SavePurchaseRequestDto
            { Id = id, Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Service, RequestDate = DateTime.Today, Lines = lines.ToList() };
            Task<ResponseDto<PurchaseRequestDto>> Save(SavePurchaseRequestDto d) => host.As(me, p => p.GetRequiredService<IProcurementService>().SaveRequestAsync(d, CancellationToken.None));

            var first = Save(Req(null, L("الف"), L("ب"))).GetAwaiter().GetResult();
            Assert.IsTrue(first.Success, first.Message);
            int lineA = first.Data.Lines[0].Id, lineB = first.Data.Lines[1].Id;
            Seed(host, new RecordAttachment { OwnerType = AttachmentOwnerType.PurchaseRequestLine, OwnerId = lineA, DocumentId = Guid.NewGuid(), Kind = AttachmentKind.Drawing, Title = "t", FileName = "a.pdf", UploadedAt = DateTimeOffset.Now, UploadedByUserId = Requester });

            // edit: line A keeps its id and gets new text, line B (no files) is dropped, a new line is added
            var edited = Save(Req(first.Data.Id, L("الف ویرایش", lineA, 5), L("ج"))).GetAwaiter().GetResult();
            Assert.IsTrue(edited.Success, edited.Message);
            Assert.AreEqual(2, edited.Data.Lines.Count);
            Assert.AreEqual(lineA, edited.Data.Lines[0].Id, "the line keeps its id");
            Assert.AreEqual("الف ویرایش", edited.Data.Lines[0].Description);
            Assert.AreEqual(5m, edited.Data.Lines[0].Quantity);
            Assert.IsFalse(edited.Data.Lines.Any(l => l.Id == lineB));
            Assert.AreEqual(1, host.Read(uow => uow.Set<RecordAttachment>().Count(a => a.OwnerType == AttachmentOwnerType.PurchaseRequestLine && a.OwnerId == lineA)));

            // a line that carries a file cannot be dropped silently
            var dropped = Save(Req(first.Data.Id, L("فقط ج"))).GetAwaiter().GetResult();
            Assert.IsFalse(dropped.Success);
            StringAssert.Contains(dropped.Message, "ابطال");
        }

        // ---------------------------------------------------------------------------- the generic Documents upload takes the same list and cap

        private static ResponseDto<string> GenericUpload(AppHost host, IFormFile file, out bool stored, string code = "T")
        {
            bool wasStored = false;
            ResponseDto<string> refusal = null;
            host.Read(uow =>
            {
                var me = new TestUser(Requester, RequesterRoles);
                var controller = new WebApi.Controllers.DocumentsController(new CountingDocuments(() => wasStored = true), null, new AttachmentService(uow, me, new StubDocuments(), null))
                { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() } };
                var result = controller.UploadAsync(new UploadFileDto { File = file, Name = "x", Description = "x", Code = code }).GetAwaiter().GetResult();
                object value = ((Microsoft.AspNetCore.Mvc.OkObjectResult)result).Value;
                refusal = value as ResponseDto<string>;
                return 0;
            });
            stored = wasStored;
            return refusal;
        }

        private sealed class CountingDocuments : IDocumentService
        {
            private readonly Action _onUpload;
            private readonly Action _onChange;
            public CountingDocuments(Action onUpload, Action onChange = null) { _onUpload = onUpload; _onChange = onChange; }
            public Task<Document> GetDocumentByIdAsync(Guid documentId) => throw new NotSupportedException();
            public Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto) { _onUpload(); return Task.FromResult(Guid.NewGuid()); }
            public Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId) => throw new NotSupportedException();
            public Task<bool> DeleteDocumentAsync(Guid image) => throw new NotSupportedException();
            public Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto) { _onChange?.Invoke(); return Task.FromResult(ResponseDtoHelper.Ok("ok")); }
            public Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => throw new NotSupportedException();
            public Task<string> GetFileHashAsync(Guid documentId) => throw new NotSupportedException();
        }

        [TestMethod]
        public void Generic_Upload_Refuses_Types_Outside_The_List_And_Disguised_Files()
        {
            AppHost host = NewHost();
            foreach (string name in new[] { "run.exe", "page.html", "x.zip", "noext" })
            {
                ResponseDto<string> r = GenericUpload(host, File(Pdf(), name), out bool stored);
                Assert.IsNotNull(r, name);
                Assert.IsFalse(r.Success, name);
                Assert.IsFalse(stored, name + " must not reach the document service");
            }
            ResponseDto<string> fake = GenericUpload(host, File(Encoding.ASCII.GetBytes("MZ not a pdf"), "fake.pdf"), out bool fakeStored);
            Assert.IsFalse(fake.Success);
            Assert.IsFalse(fakeStored);
            StringAssert.Contains(fake.Message, "هم‌خوان");
        }

        [TestMethod]
        public void Generic_Upload_Refuses_Over_The_Size_Cap_And_Takes_The_Legitimate_Files()
        {
            AppHost host = NewHost();
            var big = new FormFile(new MemoryStream(Pdf()), 0, 51L * 1024 * 1024, "File", "big.pdf");
            ResponseDto<string> over = GenericUpload(host, big, out bool overStored);
            Assert.IsFalse(over.Success);
            Assert.IsFalse(overStored);
            StringAssert.Contains(over.Message, "مگابایت");

            // a setting raises or lowers the cap
            host.Write(uow =>
            {
                ProcurementSetting row = uow.Set<ProcurementSetting>().FirstOrDefault(x => x.Key == AttachmentSettingKeys.MaxMegabytes);
                if (row == null) uow.Set<ProcurementSetting>().Add(new ProcurementSetting { Key = AttachmentSettingKeys.MaxMegabytes, Value = 1m, Title = "t" });
                else row.Value = 1m;
                uow.SaveChanges();
            });
            var twoMb = new FormFile(new MemoryStream(Pdf()), 0, 2L * 1024 * 1024, "File", "two.pdf");
            Assert.IsFalse(GenericUpload(host, twoMb, out _).Success);

            // weighbridge photos, certificates, drawings, sheets: within the list and stored
            var photoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0 };
            foreach ((string name, byte[] bytes) in new[] { ("weighbridge.jpg", photoBytes), ("cert.pdf", Pdf()), ("part.dxf", Encoding.ASCII.GetBytes("0\nSECTION\n")), ("sheet.xlsx", new byte[] { 0x50, 0x4B, 3, 4, 0, 0 }.Concat(Encoding.ASCII.GetBytes("[Content_Types].xml")).ToArray()) })
            {
                ResponseDto<string> ok = GenericUpload(host, File(bytes, name), out bool stored);
                Assert.IsNull(ok, name + " is stored: the service answered, not the check");
                Assert.IsTrue(stored, name);
            }
        }

        // ---------------------------------------------------------------------------- review fixes: record-level write rule (H2)

        [TestMethod]
        public void Purchase_Request_Files_Follow_The_Request_Edit_Rule_Creator_Or_Pmo_While_A_Draft_Planning_Or_Pmo_After()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);   // a draft made by Requester
            // a colleague with the same role is not the maker: refused while it is a draft; the maker and the PMO may
            Assert.IsFalse(Run(host, 90, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "other.pdf"), CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Storekeeper, StoreRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequestLine, c.Line, "wh.pdf"), CancellationToken.None)).Success);
            AttachmentDto mine = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "mine.pdf"), CancellationToken.None)).Data;
            Assert.IsNotNull(mine);
            Assert.IsTrue(Run(host, PmoUser, PmoRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "pmo.pdf"), CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, 90, RequesterRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = mine.Id, Reason = "نمی‌توانم" }, CancellationToken.None)).Success, "a colleague cannot void the maker's drawing");

            // sent / approved: the maker no longer changes files; planning and the PMO void or replace, with a reason
            SetStatus<PurchaseRequest>(host, c.Request, r => r.Status = PurchaseRequestStatus.Approved);
            Assert.IsFalse(Run(host, Requester, RequesterRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = mine.Id, Reason = "پشیمان شدم" }, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "late.pdf", supersedes: mine.Id), CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Storekeeper, StoreRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = mine.Id, Reason = "انبار" }, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "qc.pdf", supersedes: mine.Id), CancellationToken.None)).Success);
            ResponseDto<AttachmentDto> byPlanner = Run(host, Planner, PlannerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "rev.pdf", supersedes: mine.Id), CancellationToken.None));
            Assert.IsTrue(byPlanner.Success, byPlanner.Message);
            // the panel tells each person only what they may do
            AttachmentPanelDto asMaker = Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data;
            Assert.IsFalse(asMaker.CanWrite);
            Assert.IsTrue(asMaker.Items.All(i => !i.CanVoid && !i.CanSupersede && !i.CanRemove));
            // a request that was only sent (not yet approved) is locked as well: an approver never approves on a file that is removed afterwards
            SetStatus<PurchaseRequest>(host, c.Request, r => r.Status = PurchaseRequestStatus.Submitted);
            Assert.IsTrue(Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, c.Request, CancellationToken.None)).Data.Locked);
        }

        [TestMethod]
        public void Ncr_Files_Are_Voided_After_The_Report_Is_Past_Open_By_Quality_Or_The_Pmo_Only()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto photo = Run(host, QcUser, QcRoles, s => s.UploadAsync(Form(AttachmentOwnerType.Ncr, c.Ncr, "p.jpg", AttachmentKind.Photo, bytes: new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }), CancellationToken.None)).Data;
            SetStatus<NonConformance>(host, c.Ncr, n => n.Status = NcrStatus.Approved);
            Assert.IsFalse(Run(host, Storekeeper, StoreRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = photo.Id, Reason = "انبار" }, CancellationToken.None)).Success, "the warehouse may add while the report is open, not change it afterwards");
            Assert.IsTrue(Run(host, QcUser, QcRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = photo.Id, Reason = "عکس اشتباه" }, CancellationToken.None)).Success);
        }

        // ---------------------------------------------------------------------------- review fixes: price files (M2), supersede (M3)

        [TestMethod]
        public void Money_Files_Are_Voided_Replaced_And_Removed_By_Finance_Only_And_Keep_Their_Kind()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto slip = Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "slip.pdf", AttachmentKind.PaymentSlip), CancellationToken.None)).Data;
            // the buyer writes on the invoice record but not on its money files
            Assert.IsFalse(Run(host, Buyer, BuyerRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = slip.Id, Reason = "خریدار" }, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Buyer, BuyerRoles, s => s.RemoveAsync(slip.Id, CancellationToken.None)).Success);
            Assert.IsFalse(Run(host, Buyer, BuyerRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "swap.pdf", AttachmentKind.Other, supersedes: slip.Id), CancellationToken.None)).Success);
            // finance replaces it; whatever kind is sent, the new file keeps the kind
            ResponseDto<AttachmentDto> next = Run(host, Accountant, AccountRoles, s => s.UploadAsync(Form(AttachmentOwnerType.SupplierInvoice, c.Invoice, "slip2.pdf", AttachmentKind.Other, supersedes: slip.Id), CancellationToken.None));
            Assert.IsTrue(next.Success, next.Message);
            Assert.AreEqual(AttachmentKind.PaymentSlip, next.Data.Kind, "a payment slip cannot become another kind through a replacement");
            // the receipt-side user never learns the title of a money file through a refused call (no DTO comes back)
            ResponseDto<AttachmentDto> peek = Run(host, QcUser, QcRoles, s => s.VoidAsync(new VoidAttachmentDto { Id = slip.Id, Reason = "نگاه" }, CancellationToken.None));
            Assert.IsFalse(peek.Success);
            Assert.IsNull(peek.Data);
        }

        [TestMethod]
        public void A_Replacement_Is_One_Save_And_A_File_Has_One_Successor()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            AttachmentDto a = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "v1.pdf"), CancellationToken.None)).Data;
            ResponseDto<AttachmentDto> b = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "v2.pdf", supersedes: a.Id), CancellationToken.None));
            Assert.IsTrue(b.Success, b.Message);
            RecordAttachment old = host.Read(uow => uow.Set<RecordAttachment>().Single(x => x.Id == a.Id));
            Assert.AreEqual(b.Data.Id, old.SupersededById, "the link and the new row were saved together");
            ResponseDto<AttachmentDto> again = Run(host, Requester, RequesterRoles, s => s.UploadAsync(Form(AttachmentOwnerType.PurchaseRequest, c.Request, "v2b.pdf", supersedes: a.Id), CancellationToken.None));
            Assert.IsFalse(again.Success);
            StringAssert.Contains(again.Message, "جایگزین");
            Assert.IsNull(host.Read(uow => uow.Set<RecordAttachment>().Single(x => x.Id == b.Data.Id).SupersededById), "the new file is the end of the chain");
            Assert.AreEqual(1, host.Read(uow => uow.Set<RecordAttachment>().Count(x => x.SupersededById != null)), "exactly one link in the chain");
            Assert.AreEqual(2, host.Read(uow => uow.Set<RecordAttachment>().Count(x => x.OwnerType == AttachmentOwnerType.PurchaseRequest && x.OwnerId == c.Request)), "the refused replacement stored no row");
        }

        // ---------------------------------------------------------------------------- review fixes: the dossier (H1, M8, M9, L1)

        private static int SeedTreasury(AppHost host, Ids c, string number)
        {
            int pay = Seed(host, new FnPayment { Number = number, Date = DateTime.Today, Status = FnDocStatus.Posted, NetAmount = 1000m, GrossAmount = 1000m, CreatedByUserId = Accountant, Description = "پرداخت", PayeeName = "x" }).Id;
            Seed(host, new FnPaymentRequestLink { PaymentId = pay, PaymentRequestId = c.Payment, Amount = 1000m });
            return pay;
        }

        [TestMethod]
        public void Dossier_Leaves_Out_The_Steps_The_Caller_May_Not_Read_And_Only_Counts_Them()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            SeedTreasury(host, c, "TP-T1");

            // finance sees every step; QC reads the procurement and the warehouse steps but not the treasury payment
            DossierDto fin = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PO-T1", CancellationToken.None)).Data;
            Assert.IsTrue(fin.Steps.Any(s => s.Code == "TreasuryPayment"));
            Assert.AreEqual(0, fin.HiddenStepCount);
            ResponseDto<DossierDto> qc = Dossier(host, QcUser, QcRoles, s => s.GetAsync("PO-T1", CancellationToken.None));
            Assert.IsTrue(qc.Success, qc.Message);
            Assert.IsFalse(qc.Data.Steps.Any(s => s.Code == "TreasuryPayment"));
            Assert.AreEqual(1, qc.Data.HiddenStepCount);
            Assert.AreEqual("1 مرحله بدون دسترسی", qc.Data.HiddenStepsText);
            Assert.IsFalse(qc.Data.Steps.SelectMany(s => s.Records).Any(r => r.Number == "TP-T1"), "no detail of the hidden step");

            // the starting record itself must be readable: QC asking for a treasury number gets the standard refusal, not even a "found"
            ResponseDto<DossierDto> refused = Dossier(host, QcUser, QcRoles, s => s.GetAsync("TP-T1", CancellationToken.None));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "مجوز");
            // an unknown number is a plain not found
            Assert.IsFalse(Dossier(host, QcUser, QcRoles, s => s.GetAsync("NOPE-1", CancellationToken.None)).Success);
            // security reads the warehouse side only: no procurement steps for a payment number, refused outright
            Assert.IsFalse(Dossier(host, 91, new[] { "SECURITY" }, s => s.GetAsync("PAY-T1", CancellationToken.None)).Success);
        }

        [TestMethod]
        public void Dossier_Number_Lookup_Is_Exact_Deterministic_And_Offers_Choices_When_Ambiguous()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            // the supplier's own invoice number is not searched unless asked for
            ResponseDto<DossierDto> byOwn = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("F-77-T1", CancellationToken.None));
            Assert.IsFalse(byOwn.Success, "the supplier's own number is not one of our numbers");
            ResponseDto<DossierDto> asked = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("F-77-T1", CancellationToken.None, "SupplierInvoiceNumber"));
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.AreEqual(1, asked.Data.Steps.Count(s => s.Code == "SupplierInvoice"));

            // two suppliers use the same invoice number: the caller is offered both, in a fixed order, and picks one by our own number
            int provider2 = Seed(host, new Provider { Name = "تامین‌کننده دوم" }).Id;
            Seed(host, new SupplierInvoice { Number = "INV-T1B", InvoiceNumber = "F-77-T1", PurchaseOrderId = c.Order, ProviderId = provider2, InvoiceDate = DateTime.Today, TotalAmount = 5m });
            ResponseDto<DossierDto> twice = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("F-77-T1", CancellationToken.None, "SupplierInvoiceNumber"));
            Assert.IsTrue(twice.Success);
            Assert.AreEqual(0, twice.Data.Steps.Count);
            CollectionAssert.AreEqual(new[] { "INV-T1", "INV-T1B" }, twice.Data.Choices.Select(x => x.Number).ToList());

            // the same text on two kinds of record: choices, then the kind picks one
            Seed(host, new PaymentRequest { Number = "PO-T1", PurchaseOrderId = c.Order, ProviderId = c.Provider, Amount = 1m, RequestDate = DateTime.Today, Status = PaymentRequestStatus.Draft, CreatorUserId = Buyer });
            ResponseDto<DossierDto> both = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PO-T1", CancellationToken.None));
            Assert.IsTrue(both.Success);
            Assert.AreEqual(2, both.Data.Choices.Count);
            Assert.AreEqual(0, both.Data.Steps.Count);
            ResponseDto<DossierDto> order = Dossier(host, Accountant, AccountRoles, s => s.GetAsync("PO-T1", CancellationToken.None, "PurchaseOrder"));
            Assert.IsTrue(order.Success);
            Assert.AreEqual(0, order.Data.Choices.Count);
            Assert.IsTrue(order.Data.Steps.Count > 3);
        }

        [TestMethod]
        public void Payment_Cannot_Be_Approved_From_The_Dossier_By_The_Person_Who_Confirmed_The_Deductions()
        {
            AppHost host = NewHost();
            Ids c = SeedChain(host);
            SetStatus<PaymentRequest>(host, c.Payment, p => { p.Status = PaymentRequestStatus.Draft; p.DeductionsProposedAt = DateTimeOffset.Now; p.DeductionsConfirmedAt = DateTimeOffset.Now; p.DeductionsConfirmedByUserId = Approver; });
            bool Can(int user) => Dossier(host, user, PmoRoles, s => s.GetAsync("PAY-T1", CancellationToken.None)).Data.Steps.Single(s => s.Code == "PaymentRequest").Records.Single().CanApprovePayment;
            Assert.IsFalse(Can(Approver), "the button would only fail: the confirmer of the deductions cannot approve");
            Assert.IsTrue(Can(PmoUser));
        }

        // ---------------------------------------------------------------------------- review fixes: file read by chain (M1)

        [TestMethod]
        public void A_Request_Drawing_Is_Read_By_The_Shop_Floor_Only_Through_A_Downstream_Record()
        {
            AppHost host = NewHost();
            Ids chain = SeedChain(host);   // request -> order -> receipt exist
            Guid withChain = NewDocument(host, Requester), alone = NewDocument(host, Requester);
            int lonely = Seed(host, new PurchaseRequest { Number = "PR-ALONE", Status = PurchaseRequestStatus.Draft, RequestDate = DateTime.Today, CreatorUserId = Requester }).Id;
            host.Write(uow =>
            {
                AppHost.Add(uow, new RecordAttachment { OwnerType = AttachmentOwnerType.PurchaseRequest, OwnerId = chain.Request, DocumentId = withChain, Kind = AttachmentKind.Drawing, FileName = "a.pdf", UploadedByUserId = Requester, UploadedAt = DateTimeOffset.Now });
                AppHost.Add(uow, new RecordAttachment { OwnerType = AttachmentOwnerType.PurchaseRequest, OwnerId = lonely, DocumentId = alone, Kind = AttachmentKind.Drawing, FileName = "b.pdf", UploadedByUserId = Requester, UploadedAt = DateTimeOffset.Now });
                uow.SaveChanges();
            });
            string[] security = { "SECURITY" }, shop = { "SHOPSUPERVISOR" };
            Assert.IsTrue(ReadFile(host, 92, security, withChain).IsAllowed, "a receipt of the order exists");
            Assert.IsTrue(ReadFile(host, 93, shop, withChain).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, 92, security, alone).Kind, "a draft request with nothing downstream is not for the shop floor");
            Assert.AreEqual(FileVerdictKind.Denied, ReadFile(host, 93, shop, alone).Kind);
            Assert.IsTrue(ReadFile(host, Buyer, BuyerRoles, alone).IsAllowed, "the procurement readers read every request's files");
        }

        // ---------------------------------------------------------------------------- review fixes: line history (M4), links (L7), charter (M7)

        [TestMethod]
        public void A_Dropped_Line_Keeps_Its_Voided_History_On_The_Request_And_A_Line_With_A_File_Is_Refused()
        {
            AppHost host = NewHost();
            var me = AppHost.Principal(Requester, "req", "ENGINEERING");
            SavePurchaseRequestLineDto L(string d, int? id = null) => new SavePurchaseRequestLineDto { Id = id, Description = d, Unit = "عدد", Quantity = 1 };
            SavePurchaseRequestDto Req(int? id, params SavePurchaseRequestLineDto[] lines) => new SavePurchaseRequestDto { Id = id, Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Service, RequestDate = DateTime.Today, Lines = lines.ToList() };
            Task<ResponseDto<PurchaseRequestDto>> Save(SavePurchaseRequestDto d) => host.As(me, p => p.GetRequiredService<IProcurementService>().SaveRequestAsync(d, CancellationToken.None));

            var first = Save(Req(null, L("الف"), L("ب"))).GetAwaiter().GetResult().Data;
            int lineB = first.Lines[1].Id;
            Seed(host, new RecordAttachment { OwnerType = AttachmentOwnerType.PurchaseRequestLine, OwnerId = lineB, DocumentId = Guid.NewGuid(), Kind = AttachmentKind.Drawing, Title = "نقشه", FileName = "b.pdf", UploadedAt = DateTimeOffset.Now, UploadedByUserId = Requester, Voided = true, VoidReason = "اشتباه", VoidedAt = DateTimeOffset.Now, VoidedByUserId = Requester });

            // duplicate ids in one save are refused
            var dup = Save(Req(first.Id, L("الف", first.Lines[0].Id), L("تکرار", first.Lines[0].Id))).GetAwaiter().GetResult();
            Assert.IsFalse(dup.Success);

            // dropping line B: only a voided file is on it, so it goes, and its history moves to the request with a note
            var saved = Save(Req(first.Id, L("الف", first.Lines[0].Id))).GetAwaiter().GetResult();
            Assert.IsTrue(saved.Success, saved.Message);
            RecordAttachment kept = host.Read(uow => uow.Set<RecordAttachment>().Single(a => a.DocumentId != Guid.Empty && a.Voided && a.Title.Contains("نقشه")));
            Assert.AreEqual(AttachmentOwnerType.PurchaseRequest, kept.OwnerType);
            Assert.AreEqual(first.Id, kept.OwnerId);
            StringAssert.Contains(kept.Title, "ردیف حذف‌شده 2");
            AttachmentPanelDto panel = Run(host, Requester, RequesterRoles, s => s.GetPanelAsync(AttachmentOwnerType.PurchaseRequest, first.Id, CancellationToken.None)).Data;
            Assert.AreEqual(1, panel.Items.Count(i => i.Voided), "the history is still reachable from the request");
        }

        [TestMethod]
        public void Bank_Link_Takes_Only_The_Latest_Revision_Of_Each_Document()
        {
            var bank = new List<EquipmentProcessFile>
            {
                new EquipmentProcessFile { DocumentId = Guid.NewGuid(), Description = "نقشه ماشین‌کاری", major = 1, minor = 0 },
                new EquipmentProcessFile { DocumentId = Guid.NewGuid(), Description = "نقشه ماشین‌کاری", major = 2, minor = 1 },
                new EquipmentProcessFile { DocumentId = Guid.NewGuid(), Description = "نقشه ماشین‌کاری", major = 2, minor = 0 },
                new EquipmentProcessFile { DocumentId = Guid.NewGuid(), Description = "دستورالعمل", major = 1, minor = 0 },
            };
            List<ProcessFile> links = BankDocumentLinks.Build(bank, 5, DateTime.Now);
            Assert.AreEqual(2, links.Count);
            Assert.IsTrue(links.Any(l => l.Major == 2 && l.Minor == 1));
            Assert.IsTrue(links.Any(l => l.Description.Contains("دستورالعمل")));
        }

        [TestMethod]
        public void Replacing_The_Project_Charter_Keeps_The_Old_File_As_A_Voided_Attachment()
        {
            AppHost host = NewHost();
            Guid oldCharter = NewDocument(host, PmoUser);
            int contract = Seed(host, new ContractEntity { Code = "C-CH", Title = "قرارداد", ProjectCharterFileId = oldCharter }).Id;
            // the stub document service throws if a stored file is ever deleted
            ResponseDto<string> r = host.Read(uow => Contracts(uow, host, PmoUser, PmoRoles).AttachProjectCharterFileAsync(
                new ViewModels.NasbNiro.ContractDtos.AttachProjectCharterFileDto { ContractId = contract, File = File(Pdf(), "charter2.pdf"), Name = "charter", Description = "منشور", Code = "INV" }, CancellationToken.None).AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(r.Success, r.Message);
            RecordAttachment kept = host.Read(uow => uow.Set<RecordAttachment>().Single(a => a.OwnerType == AttachmentOwnerType.Contract && a.OwnerId == contract));
            Assert.IsTrue(kept.Voided);
            Assert.AreEqual(oldCharter, kept.DocumentId);
            Assert.AreNotEqual(oldCharter, host.Read(uow => uow.Set<ContractEntity>().Single(x => x.Id == contract).ProjectCharterFileId.Value));
        }

        [TestMethod]
        public void Generic_Upload_Takes_The_New_Types_And_A_Zip_Only_For_Letters()
        {
            AppHost host = NewHost();
            byte[] zip = { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 };
            Assert.IsFalse(GenericUpload(host, File(zip, "mail.zip"), out bool s1).Success);
            Assert.IsFalse(s1);
            ResponseDto<string> letter = GenericUpload(host, File(zip, "mail.zip"), out bool s2, "OFC");
            Assert.IsNull(letter);
            Assert.IsTrue(s2, "a letter attachment may be a zip");
            ResponseDto<string> exeAsZip = GenericUpload(host, File(new byte[] { 0x4D, 0x5A, 0x90, 0 }, "setup.zip"), out bool s3, "OFC");
            Assert.IsFalse(exeAsZip.Success);
            Assert.IsFalse(s3);
            foreach ((string name, byte[] bytes) in new[] { ("note.txt", Encoding.UTF8.GetBytes("یادداشت")), ("list.csv", Encoding.ASCII.GetBytes("a,b")), ("mail.eml", Encoding.ASCII.GetBytes("From: a@b")), ("phone.heic", new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' }) })
            {
                Assert.IsNull(GenericUpload(host, File(bytes, name), out bool stored), name);
                Assert.IsTrue(stored, name);
            }
        }

        [TestMethod]
        public void Change_File_Faces_The_Same_Check_So_A_Pdf_Cannot_Be_Swapped_For_An_Executable()
        {
            AppHost host = NewHost();
            bool changed = false;
            host.Read(uow =>
            {
                var me = new TestUser(Requester, RequesterRoles);
                var controller = new WebApi.Controllers.DocumentsController(new CountingDocuments(() => { }, () => changed = true), null, new AttachmentService(uow, me, new StubDocuments(), null))
                { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() } };
                var result = (Microsoft.AspNetCore.Mvc.OkObjectResult)controller.ChangeAsync(Guid.NewGuid(), File(new byte[] { 0x4D, 0x5A, 0x90, 0, 1, 2 }, "evil.exe")).GetAwaiter().GetResult();
                var refusal = (ResponseDto<string>)result.Value;
                Assert.IsFalse(refusal.Success);
                var fake = (ResponseDto<string>)((Microsoft.AspNetCore.Mvc.OkObjectResult)controller.ChangeAsync(Guid.NewGuid(), File(new byte[] { 0x4D, 0x5A, 0x90, 0, 1, 2 }, "evil.pdf")).GetAwaiter().GetResult()).Value;
                Assert.IsFalse(fake.Success);
                return 0;
            });
            Assert.IsFalse(changed, "the bytes were never handed to the document service");
        }

        private static ContractService Contracts(IUnitOfWork uow, AppHost host, int user, string[] roles)
        {
            var me = new TestUser(user, roles);
            return new ContractService(NullLogger<ContractService>.Instance, uow, new StubDocuments(), host.Provider.GetRequiredService<IMapper>(), new AttachmentService(uow, me, new StubDocuments(), null));
        }

        [TestMethod]
        public void Bank_Documents_Reach_The_Project_Process_As_Links_To_The_Same_Files()
        {
            Guid doc1 = Guid.NewGuid(), doc2 = Guid.NewGuid();
            var bank = new List<EquipmentProcessFile>
            {
                new EquipmentProcessFile { DocumentId = doc1, Description = "نقشه ماشین‌کاری", major = 2, minor = 1 },
                new EquipmentProcessFile { DocumentId = doc2, Description = "دستورالعمل", major = 1, minor = 0 },
            };
            List<ProcessFile> links = BankDocumentLinks.Build(bank, 5, new DateTime(2026, 1, 1));
            Assert.AreEqual(2, links.Count);
            CollectionAssert.AreEqual(new[] { doc1, doc2 }, links.Select(l => l.DocumentId).ToList(), "the same stored files");
            StringAssert.Contains(links[0].Description, "بازنگری 2.1");
            StringAssert.Contains(links[0].Description, "نقشه ماشین‌کاری");
            Assert.AreEqual(2, links[0].Major);
            Assert.AreEqual(1, links[0].Minor);
            Assert.AreEqual(5, links[0].UserId);
            Assert.AreEqual(0, BankDocumentLinks.Build(null, 5, DateTime.Now).Count);
        }
    }
}
