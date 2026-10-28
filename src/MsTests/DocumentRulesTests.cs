using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts;
using Services.NasbNiro.Contracts;
using Services.Services;
using ViewModels.Exceptions;
using ViewModels.NasbNiro.DocumentDtos;

namespace MsTests
{
    /// <summary>
    /// R1a2 (H3): a stored file that any record refers to (a contract, a tender, a letter, a voucher, a profile photo ...) is changed or deleted only through the module that owns it,
    /// never through the generic Documents endpoint, whoever the caller is; a file nobody refers to yet is changed only by the user who uploaded it or by the PMO, and every
    /// replacement of the bytes leaves a row with the SHA-256 before and after.
    /// </summary>
    [TestClass]
    public class DocumentRulesTests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(7, "pmo.user", "PMO");
        private static readonly ClaimsPrincipal Uploader = AppHost.Principal(21, "uploader", "OPERATOR");
        private static readonly ClaimsPrincipal Stranger = AppHost.Principal(22, "stranger", "OPERATOR");
        private static readonly Lazy<AppHost> Shared = new Lazy<AppHost>(() => new AppHost("Documents_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => Shared.Value;

        // ------------------------------------------------------------------------------------------------ helpers

        private static DocumentService Service(IServiceProvider sp) => new DocumentService(
            sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<IWebHostEnvironment>(),
            sp.GetRequiredService<ILogger<DocumentService>>(), sp.GetRequiredService<IHttpContextAccessor>());

        private static IFormFile FormFile(string text, string fileName = "report.txt")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", fileName);
        }

        private static Task<Guid> UploadAsync(ClaimsPrincipal who, string text = "first bytes") =>
            H.As(who, sp => Service(sp).UploadDocumentAsync(new UploadFileDto { File = FormFile(text), Name = "report", Code = "R-1", Description = "test file" }));

        private static Task<ResponseDto<string>> ChangeAsync(ClaimsPrincipal who, Guid id, string text = "second bytes") =>
            H.As(who, sp => Service(sp).ChangeFileAsync(new ChangeFileDto { DocumentId = id, File = FormFile(text) }));

        private static Task<ResponseDto<bool>> EditAsync(ClaimsPrincipal who, Guid id) =>
            H.As(who, sp => Service(sp).UpdateDocumentByUserAsync(new UpdateDocumentDto { Id = id, Name = "renamed", Code = "R-2", Description = "edited" }));

        private static string StoredText(Guid id)
        {
            string path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "UploadedDocuments"), id + ".*").Single();
            return File.ReadAllText(path);
        }

        private static List<FnAuditLog> Trail(string entityType, string action = null) =>
            H.Read(uow => uow.Set<FnAuditLog>().Where(l => l.EntityType == entityType && (action == null || l.Action == action)).OrderBy(l => l.Id).ToList());

        private static void Save(Action<IUnitOfWork> change) => H.Write(uow => { change(uow); uow.SaveChanges(); }, Pmo);

        private static int NewContract(string code)
        {
            int id = 0;
            Save(uow => { var c = AppHost.Add(uow, new ContractEntity { Code = code, Title = "title of " + code }); uow.SaveChanges(); id = c.Id; });
            return id;
        }

        // ------------------------------------------------------------------------------------------------ a file nobody refers to yet

        [TestMethod]
        public async Task A_New_File_Records_Its_Uploader_And_Only_The_Uploader_Or_The_Pmo_Replaces_It()
        {
            Guid id = await UploadAsync(Uploader);
            Assert.AreEqual(21, H.Read(uow => uow.Set<Document>().Single(d => d.Id == id).UploadedByUserId));

            ResponseDto<string> stranger = await ChangeAsync(Stranger, id, "stranger bytes");
            Assert.IsFalse(stranger.Success);
            Assert.AreEqual(DocumentService.NotTheUploader, stranger.Message);
            Assert.AreEqual("first bytes", StoredText(id), "the refused replacement must not touch the stored bytes");

            ResponseDto<string> own = await ChangeAsync(Uploader, id, "uploader bytes");
            Assert.IsTrue(own.Success, own.Message);
            Assert.AreEqual("uploader bytes", StoredText(id));

            ResponseDto<string> pmo = await ChangeAsync(Pmo, id, "pmo bytes");
            Assert.IsTrue(pmo.Success, pmo.Message);
            Assert.AreEqual("pmo bytes", StoredText(id));
        }

        [TestMethod]
        public async Task The_Edit_Of_Name_And_Description_Follows_The_Same_Rule()
        {
            Guid id = await UploadAsync(Uploader);

            ResponseDto<bool> stranger = await EditAsync(Stranger, id);
            Assert.IsFalse(stranger.Success);
            Assert.AreEqual(DocumentService.NotTheUploader, stranger.Message);
            Assert.AreEqual("report", H.Read(uow => uow.Set<Document>().Single(d => d.Id == id).Name));

            Assert.IsTrue((await EditAsync(Uploader, id)).Success);
            Assert.AreEqual("renamed", H.Read(uow => uow.Set<Document>().Single(d => d.Id == id).Name));
            Assert.IsTrue((await EditAsync(Pmo, id)).Success);
        }

        [TestMethod]
        public async Task A_File_Without_An_Uploader_Is_Changed_Only_By_The_Pmo()
        {
            Guid id = await UploadAsync(Pmo);
            // a row from before the column existed
            Save(uow => uow.Set<Document>().Single(d => d.Id == id).SetUploadedBy(null));

            Assert.IsFalse((await ChangeAsync(Uploader, id)).Success);
            Assert.IsFalse((await EditAsync(Uploader, id)).Success);
            Assert.IsTrue((await ChangeAsync(Pmo, id)).Success);
            Assert.IsTrue((await EditAsync(Pmo, id)).Success);
        }

        [TestMethod]
        public async Task A_Call_Without_A_Signed_In_User_Changes_Nothing()
        {
            Guid id = await UploadAsync(Uploader);
            ResponseDto<string> anonymous = await H.As(null, sp => Service(sp).ChangeFileAsync(new ChangeFileDto { DocumentId = id, File = FormFile("anonymous") }));
            Assert.IsFalse(anonymous.Success);
            Assert.AreEqual("first bytes", StoredText(id));
        }

        [TestMethod]
        public async Task A_Missing_File_Is_Not_Found_And_An_Empty_Replacement_Is_Refused()
        {
            ResponseDto<string> missing = await ChangeAsync(Pmo, Guid.NewGuid());
            Assert.IsFalse(missing.Success);
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, missing.HttpStatusCode);

            Guid id = await UploadAsync(Uploader);
            ResponseDto<string> empty = await H.As(Uploader, sp => Service(sp).ChangeFileAsync(new ChangeFileDto { DocumentId = id, File = FormFile("") }));
            Assert.IsFalse(empty.Success);
            Assert.AreEqual("first bytes", StoredText(id), "an empty replacement must not wipe the file");
        }

        [TestMethod]
        public async Task The_Replacement_Of_The_Bytes_Is_In_The_Trail_With_Both_Hashes()
        {
            Guid id = await UploadAsync(Uploader);
            int before = Trail("Document", "FileReplaced").Count;
            Assert.IsTrue((await ChangeAsync(Uploader, id, "changed bytes")).Success);

            List<FnAuditLog> rows = Trail("Document", "FileReplaced");
            Assert.AreEqual(before + 1, rows.Count);
            FnAuditLog row = rows.Last();
            Assert.AreEqual(21, row.UserId);
            StringAssert.Contains(row.Summary, id.ToString());
            using (JsonDocument old = JsonDocument.Parse(row.BeforeJson))
            using (JsonDocument now = JsonDocument.Parse(row.AfterJson))
            {
                string oldHash = old.RootElement.GetProperty("sha256").GetString();
                string newHash = now.RootElement.GetProperty("sha256").GetString();
                Assert.IsFalse(string.IsNullOrEmpty(oldHash));
                Assert.IsFalse(string.IsNullOrEmpty(newHash));
                Assert.AreNotEqual(oldHash, newHash);
                Assert.AreEqual(id.ToString(), now.RootElement.GetProperty("documentId").GetString());
            }
        }

        [TestMethod]
        public async Task A_New_File_Is_Audited_With_Its_Uploader_And_A_Rename_Leaves_A_Row()
        {
            Guid id = await UploadAsync(Uploader);
            Assert.IsTrue((await EditAsync(Uploader, id)).Success);

            List<FnAuditLog> rows = Trail("Document").Where(l => l.Summary != null && l.Summary.Contains(id.ToString())).ToList();
            Assert.IsTrue(rows.Any(r => r.Action == "Added" && r.AfterJson.Contains("UploadedByUserId")), "the upload is in the trail with the uploader");
            FnAuditLog edit = rows.Single(r => r.Action == "Modified");
            StringAssert.Contains(edit.BeforeJson, "report");
            StringAssert.Contains(edit.AfterJson, "renamed");
            StringAssert.StartsWith(edit.Summary, "فایل");
        }

        // ------------------------------------------------------------------------------------------------ a file that a record refers to

        [TestMethod]
        public async Task A_Contract_File_Cannot_Be_Replaced_Edited_Or_Deleted_By_Anybody_And_The_Refusal_Names_The_Contract()
        {
            Guid id = await UploadAsync(Uploader);
            int contractId = NewContract("C-1405-77");
            Save(uow => AppHost.Add(uow, new ContractFile { ContractId = contractId, DocumentId = id }));

            foreach (ClaimsPrincipal who in new[] { Uploader, Stranger, Pmo })
            {
                ResponseDto<string> changed = await ChangeAsync(who, id, "forged");
                Assert.IsFalse(changed.Success);
                StringAssert.Contains(changed.Message, "قرارداد «C-1405-77»");
                StringAssert.Contains(changed.Message, "پیوست شده است");

                ResponseDto<bool> edited = await EditAsync(who, id);
                Assert.IsFalse(edited.Success);
                StringAssert.Contains(edited.Message, "C-1405-77");
            }
            Assert.AreEqual("first bytes", StoredText(id));
            Assert.AreEqual("report", H.Read(uow => uow.Set<Document>().Single(d => d.Id == id).Name));

            DomainInvalidException refused = await Assert.ThrowsExactlyAsync<DomainInvalidException>(() => H.As(Pmo, sp => Service(sp).DeleteDocumentAsync(id)));
            StringAssert.Contains(refused.Message, "C-1405-77");
            Assert.IsTrue(H.Read(uow => uow.Set<Document>().Any(d => d.Id == id)), "the file must still exist");
        }

        [TestMethod]
        public async Task Once_The_Owner_Removes_Its_Link_The_File_Follows_The_Loose_File_Rule_And_Can_Be_Deleted()
        {
            Guid id = await UploadAsync(Uploader);
            int contractId = NewContract("C-1405-78");
            Save(uow => AppHost.Add(uow, new ContractFile { ContractId = contractId, DocumentId = id }));
            Assert.IsFalse((await ChangeAsync(Uploader, id)).Success);

            Save(uow => uow.Set<ContractFile>().RemoveRange(uow.Set<ContractFile>().Where(f => f.DocumentId == id)));

            Assert.IsFalse((await ChangeAsync(Stranger, id)).Success, "a stranger is still refused");
            Assert.IsTrue((await ChangeAsync(Uploader, id, "after detach")).Success);
            Assert.IsTrue(await H.As(Pmo, sp => Service(sp).DeleteDocumentAsync(id)));
            Assert.IsFalse(H.Read(uow => uow.Set<Document>().Any(d => d.Id == id)));
        }

        [TestMethod]
        public async Task The_Deletion_Of_A_Missing_File_Is_A_Domain_Refusal_And_Not_A_Crash()
        {
            await Assert.ThrowsExactlyAsync<DomainInvalidException>(() => H.As(Pmo, sp => Service(sp).DeleteDocumentAsync(Guid.NewGuid())));
        }

        [TestMethod]
        public async Task A_File_Of_A_Draft_Letter_Is_Referenced_And_The_File_Of_A_Sent_Letter_Keeps_The_Letter_Message()
        {
            Guid draftFile = await UploadAsync(Uploader);
            Guid sentFile = await UploadAsync(Uploader);
            int draftId = 0, sentId = 0;
            Save(uow =>
            {
                var draft = AppHost.Add(uow, new OfLetter { Subject = "draft subject", Status = OfLetterStatus.Draft });
                var sent = AppHost.Add(uow, new OfLetter { Subject = "sent subject", Status = OfLetterStatus.Signed, Number = "N-1" });
                uow.SaveChanges();
                draftId = draft.Id; sentId = sent.Id;
                AppHost.Add(uow, new OfLetterAttachment { LetterId = draft.Id, DocumentId = draftFile, FileName = "a.txt" });
                AppHost.Add(uow, new OfLetterAttachment { LetterId = sent.Id, DocumentId = sentFile, FileName = "b.txt" });
            });

            ResponseDto<string> draftChange = await ChangeAsync(Uploader, draftFile);
            Assert.IsFalse(draftChange.Success);
            StringAssert.Contains(draftChange.Message, "نامه «");

            ResponseDto<string> sentChange = await ChangeAsync(Pmo, sentFile);
            Assert.IsFalse(sentChange.Success);
            StringAssert.Contains(sentChange.Message, "تغییر یا حذف آن مجاز نیست");
            Assert.IsTrue(draftId > 0 && sentId > 0);
        }

        [TestMethod]
        public async Task A_Profile_Photo_And_A_List_Of_Rfq_Files_Are_References_Too()
        {
            Guid photo = await UploadAsync(Uploader);
            Guid rfqFile = await UploadAsync(Uploader);
            Save(uow =>
            {
                var user = AppHost.Add(uow, new Entities.Identity.User { UserName = "photo.owner", NormalizedUserName = "PHOTO.OWNER", ProfilePhotoId = photo });
                AppHost.Add(uow, new SlInquiry { Number = "RFQ-1", AttachmentIds = Guid.NewGuid() + "," + rfqFile + "," + Guid.NewGuid() });
            });

            ResponseDto<string> photoChange = await ChangeAsync(Uploader, photo);
            Assert.IsFalse(photoChange.Success);
            StringAssert.Contains(photoChange.Message, "تصویر نمایه کاربر «photo.owner»");

            ResponseDto<string> rfqChange = await ChangeAsync(Uploader, rfqFile);
            Assert.IsFalse(rfqChange.Success);
            StringAssert.Contains(rfqChange.Message, "استعلام مشتری «RFQ-1»");
        }

        [TestMethod]
        public async Task Every_Column_Of_The_Model_That_Holds_A_File_Really_Locks_It()
        {
            IReadOnlyList<DocumentLink> links = DocumentLinks.Of(H.Model);
            var problems = new List<string>();
            foreach (DocumentLink link in links)
            {
                Guid id = await UploadAsync(Pmo, "bytes of " + link);
                try
                {
                    Save(uow =>
                    {
                        object record = Activator.CreateInstance(link.EntityType, nonPublic: true);
                        PropertyInfo property = link.EntityType.GetProperty(link.Property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        property.SetValue(record, link.IsText ? (object)("1," + id + ",2") : id);
                        if (DocumentLinks.LinkTables.TryGetValue(link.EntityType.Name, out DocumentLinks.ParentOwner owner))
                        {
                            // a link table needs the record it belongs to
                            object parent = Activator.CreateInstance(owner.ParentType, nonPublic: true);
                            typeof(AppHost).GetMethod(nameof(AppHost.Add)).MakeGenericMethod(owner.ParentType).Invoke(null, new[] { uow, parent });
                            uow.SaveChanges();
                            link.EntityType.GetProperty(owner.ForeignKey, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .SetValue(record, owner.ParentType.GetProperty(nameof(BaseEntity.Id)).GetValue(parent));
                        }
                        typeof(AppHost).GetMethod(nameof(AppHost.Add)).MakeGenericMethod(link.EntityType).Invoke(null, new[] { uow, record });
                    });
                }
                catch (Exception ex)
                {
                    problems.Add(link + ": the test record could not be saved: " + (ex.InnerException ?? ex).Message);
                    continue;
                }

                ResponseDto<string> changed = await ChangeAsync(Pmo, id);
                if (changed.Success) problems.Add(link + ": the file was replaced although a record refers to it");
                else if (!(changed.Message ?? string.Empty).Contains("پیوست شده است") && !(changed.Message ?? string.Empty).Contains("مجاز نیست"))
                    problems.Add(link + ": the refusal does not say why: " + changed.Message);
                if (StoredText(id) != "bytes of " + link) problems.Add(link + ": the stored bytes changed");

                try
                {
                    await H.As(Pmo, sp => Service(sp).DeleteDocumentAsync(id));
                    problems.Add(link + ": the file was deleted although a record refers to it");
                }
                catch (DomainInvalidException) { /* the wanted answer */ }
            }
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
            Assert.IsTrue(links.Count >= 40, "only " + links.Count + " columns found; the discovery of the file columns broke");
        }

        // ------------------------------------------------------------------------------------------------ the list of columns

        /// <summary>Guid columns of the model that are not references to a stored file. A new Guid column must be named like a file reference (it is then found and locks the file) or be listed here with the reason.</summary>
        private static readonly Dictionary<string, string> NotFileColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AccountingEvent.Key", "idempotency key of a posting event" },
        };

        [TestMethod]
        public void No_Guid_Column_Of_The_Model_Escapes_The_Search_For_Files()
        {
            IReadOnlyList<DocumentLink> links = DocumentLinks.Of(H.Model);
            var unknown = new List<string>();
            foreach (IEntityType type in H.Model.GetEntityTypes())
            {
                if (type.ClrType == typeof(Document)) continue;
                foreach (IProperty property in type.GetProperties())
                {
                    if (property.ClrType != typeof(Guid) && property.ClrType != typeof(Guid?)) continue;
                    if (property.Name == "Id") continue;
                    if (links.Any(l => l.EntityType == type.ClrType && l.Property == property.Name)) continue;
                    if (NotFileColumns.ContainsKey(type.ClrType.Name + "." + property.Name)) continue;
                    unknown.Add(type.ClrType.Name + "." + property.Name);
                }
            }
            Assert.AreEqual(0, unknown.Count, "Guid columns that the file lock does not look at (name them like a file reference, or add them to NotFileColumns with the reason): " + string.Join(", ", unknown));
            string[] stale = NotFileColumns.Keys.Where(k => !H.Model.GetEntityTypes().Any(t => t.ClrType.Name == k.Split('.')[0] && t.FindProperty(k.Split('.')[1]) != null)).ToArray();
            Assert.AreEqual(0, stale.Length, "entries of NotFileColumns that match no column: " + string.Join(", ", stale));
        }

        [TestMethod]
        public void The_Known_Owners_Of_Files_Are_Among_The_Discovered_Columns()
        {
            string[] expected =
            {
                "ContractFile.DocumentId", "TenderFile.DocumentId", "BuyOrderFile.DocumentId", "ProcessFile.DocumentId", "EquipmentProcessFile.DocumentId",
                "ContractEntity.ProjectCharterFileId", "OfLetterAttachment.DocumentId", "OfLetter.SignatureDocumentId", "OfLetter.DeliveryReceiptDocumentId",
                "OfUserSignature.ImageDocumentId", "User.ProfilePhotoId", "Inquiry.TechnicalDoc", "Inquiry.FinancialDoc", "BuyOrder.TimeSheet", "BuyOrder.LogShitId",
                "FnVoucherAttachment.DocumentId", "SupplierInvoice.DocumentId", "SlInquiry.AttachmentIds", "SlProposal.TechnicalDocumentId", "PcEngineeringDeliverable.DocumentId",
            };
            string[] found = DocumentLinks.Of(H.Model).Select(l => l.EntityType.Name + "." + l.Property).ToArray();
            string[] missing = expected.Where(e => !found.Contains(e)).ToArray();
            Assert.AreEqual(0, missing.Length, "no longer found: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void Every_Owner_Kind_Is_Persian_And_Names_A_Type_That_Exists()
        {
            var names = new HashSet<string>(H.Model.GetEntityTypes().Select(t => t.ClrType.Name));
            foreach (KeyValuePair<string, string> kind in DocumentLinks.Kinds)
            {
                Assert.IsTrue(names.Contains(kind.Key), kind.Key + " is not an entity of the model");
                Assert.IsTrue(kind.Value.Any(ch => ch >= '؀' && ch <= 'ۿ'), kind.Key + " has no Persian name");
            }
            foreach (KeyValuePair<string, DocumentLinks.ParentOwner> table in DocumentLinks.LinkTables)
            {
                Assert.IsTrue(names.Contains(table.Key), table.Key + " is not an entity of the model");
                Assert.IsNotNull(table.Value.ParentType.GetProperty(nameof(BaseEntity.Id)), table.Value.ParentType.Name + " needs an integer Id");
                Assert.IsNotNull(H.Model.GetEntityTypes().Single(t => t.ClrType.Name == table.Key).FindProperty(table.Value.ForeignKey), table.Key + "." + table.Value.ForeignKey);
            }
            // every discovered owner that is not a link table has a Persian kind of its own, so no refusal says "record of ..." in English
            string[] unnamed = DocumentLinks.Of(H.Model)
                .Where(l => !DocumentLinks.LinkTables.ContainsKey(l.EntityType.Name) && !DocumentLinks.Kinds.ContainsKey(l.EntityType.Name))
                .Select(l => l.EntityType.Name).Distinct().ToArray();
            Assert.AreEqual(0, unnamed.Length, "owners of files without a Persian name in DocumentLinks.Kinds: " + string.Join(", ", unnamed));
        }

        [TestMethod]
        public void The_Document_Is_Audited_With_The_Listed_Fields_And_A_Persian_Label()
        {
            Type trail = typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail", throwOnError: true);
            var audited = (Dictionary<Type, string[]>)trail.GetField("Audited", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var labels = (Dictionary<Type, string>)trail.GetField("Labels", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            CollectionAssert.AreEquivalent(new[] { "Path", "MimType", "Name", "Description", "Code", "UploadedByUserId" }, audited[typeof(Document)]);
            Assert.AreEqual("فایل", labels[typeof(Document)]);
        }

        // ------------------------------------------------------------------------------------------------ the owner actions

        [TestMethod]
        public async Task The_Tender_Action_Edits_Only_The_Files_Of_Tenders()
        {
            Guid tenderFile = Guid.NewGuid(), contractFile = Guid.NewGuid();
            int tenderId = 0, contractId = NewContract("C-1405-79");
            Save(uow =>
            {
                var tender = AppHost.Add(uow, new Tender { Code = "T-1" });
                uow.SaveChanges();
                tenderId = tender.Id;
                AppHost.Add(uow, new TenderFile { TenderId = tender.Id, DocumentId = tenderFile });
                AppHost.Add(uow, new ContractFile { ContractId = contractId, DocumentId = contractFile });
            });

            var dto = new UpdateDocumentDto { Id = tenderFile, Name = "n", Code = "c", Description = "d" };
            Assert.IsTrue(await H.As(Pmo, sp => sp.GetRequiredService<ITenderService>().UpdateFileToTenderAsync(dto, CancellationToken.None).AsTask()));

            await Assert.ThrowsExactlyAsync<DomainInvalidException>(() =>
                H.As(Pmo, sp => sp.GetRequiredService<ITenderService>().UpdateFileToTenderAsync(new UpdateDocumentDto { Id = contractFile, Name = "n", Code = "c", Description = "d" }, CancellationToken.None).AsTask()));
            await Assert.ThrowsExactlyAsync<DomainInvalidException>(() =>
                H.As(Pmo, sp => sp.GetRequiredService<ITenderService>().UpdateFileToTenderAsync(new UpdateDocumentDto { Id = Guid.NewGuid(), Name = "n", Code = "c", Description = "d" }, CancellationToken.None).AsTask()));
            Assert.IsTrue(tenderId > 0);
        }

        [TestMethod]
        public async Task The_Owner_Update_Still_Refuses_The_Files_Of_Letters()
        {
            Guid file = await UploadAsync(Uploader);
            Save(uow =>
            {
                var sent = AppHost.Add(uow, new OfLetter { Subject = "sent", Status = OfLetterStatus.Signed, Number = "N-2" });
                uow.SaveChanges();
                AppHost.Add(uow, new OfLetterAttachment { LetterId = sent.Id, DocumentId = file, FileName = "c.txt" });
            });
            await Assert.ThrowsExactlyAsync<DomainInvalidException>(() =>
                H.As(Pmo, sp => Service(sp).UpdateDocumentAsync(new UpdateDocumentDto { Id = file, Name = "n", Code = "c", Description = "d" })));
        }
    }
}
