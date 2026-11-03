using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Files;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.DocumentDtos;
using WebApi.Filters;

namespace MsTests
{
    /// <summary>
    /// R1t D8: who may read and who may change a stored file. The decision itself (<see cref="FileAccessPolicy"/>) is tested without a database; the service is tested on the in-memory
    /// database with the real owner records and a small stand-in for the office module's own rules (letters, leave requests, meetings).
    /// </summary>
    [TestClass]
    public class FileAccessR1Tests
    {
        private const int Uploader = 61, Party = 62, Stranger = 63, PmoUser = 64, Accountant = 65, PayrollUser = 66, QcUser = 67;

        // ------------------------------------------------------------------------------------------ the decision

        private static FileFacts Facts(int? uploader, params FileOwner[] owners)
        {
            var facts = new FileFacts { Exists = true, UploadedByUserId = uploader };
            facts.Owners.AddRange(owners);
            return facts;
        }

        private static FileOwner Letter(bool may) => new FileOwner("OfLetter", FileOwnerRule.Letter, may);

        private static FileOwner Record(bool may, string kind = "QcRecord") => new FileOwner(kind, FileOwnerRule.Record, may);

        private static readonly FileCaller Nobody = new FileCaller(0, false);
        private static readonly FileCaller Uploading = new FileCaller(Uploader, false);
        private static readonly FileCaller Other = new FileCaller(Stranger, false);
        private static readonly FileCaller Pmo = new FileCaller(PmoUser, true);

        [TestMethod]
        public void Read_Of_A_Missing_File_Is_Not_Found()
        {
            Assert.AreEqual(FileVerdictKind.NotFound, FileAccessPolicy.Read(Uploading, new FileFacts { Exists = false }).Kind);
            Assert.AreEqual(FileVerdictKind.NotFound, FileAccessPolicy.Change(Uploading, new FileFacts { Exists = false }, false).Kind);
        }

        [TestMethod]
        public void A_File_That_No_Record_Holds_Is_Read_By_Its_Uploader_And_The_Pmo_Only()
        {
            FileFacts loose = Facts(Uploader);
            Assert.IsTrue(FileAccessPolicy.Read(Uploading, loose).IsAllowed);
            Assert.IsTrue(FileAccessPolicy.Read(Pmo, loose).IsAllowed);
            FileVerdict refused = FileAccessPolicy.Read(Other, loose);
            Assert.AreEqual(FileVerdictKind.Denied, refused.Kind);
            Assert.IsFalse(refused.Owned);
            StringAssert.Contains(refused.Message, "مجوز");
            // nobody signed in, and a file with no recorded uploader (older files): only the PMO
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Nobody, Facts(null)).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Other, Facts(null)).Kind);
            Assert.IsTrue(FileAccessPolicy.Read(Pmo, Facts(null)).IsAllowed);
        }

        [TestMethod]
        public void A_File_Of_A_Record_Is_Read_By_Whoever_The_Records_Module_Lets_In_And_By_The_Uploader()
        {
            Assert.IsTrue(FileAccessPolicy.Read(Other, Facts(Uploader, Record(true))).IsAllowed);
            // a reader of one of two records is enough
            Assert.IsTrue(FileAccessPolicy.Read(Other, Facts(Uploader, Record(false, "A"), Record(true, "B"))).IsAllowed);
            // the module refuses the caller: the PMO has no extra right to an owned file, the uploader keeps his own upload
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Other, Facts(Uploader, Record(false))).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Pmo, Facts(Uploader, Record(false))).Kind);
            Assert.IsTrue(FileAccessPolicy.Read(Uploading, Facts(Uploader, Record(false))).IsAllowed);
            Assert.IsTrue(FileAccessPolicy.Read(Other, Facts(Uploader, Record(false))).Owned);
        }

        [TestMethod]
        public void A_Letters_File_Is_Decided_By_The_Letters_Rule_Alone()
        {
            // a party reads it
            FileVerdict party = FileAccessPolicy.Read(Other, Facts(Uploader, Letter(true)));
            Assert.IsTrue(party.IsAllowed);
            Assert.IsTrue(party.LetterFile);
            // a stranger does not; neither do the uploader (not a party of the letter any more), the PMO, nor a reader of another record that holds the same file
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Other, Facts(Uploader, Letter(false))).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Uploading, Facts(Uploader, Letter(false))).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Pmo, Facts(Uploader, Letter(false))).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Read(Other, Facts(Uploader, Letter(false), Record(true))).Kind);
            // two letters hold the file: a party of either reads it
            Assert.IsTrue(FileAccessPolicy.Read(Other, Facts(Uploader, Letter(false), Letter(true))).IsAllowed);
            // the refusal says nothing about the letter
            FileVerdict refused = FileAccessPolicy.Read(Other, Facts(Uploader, Letter(false)));
            Assert.AreEqual(FileText.NoReadAccess, refused.Message);
        }

        [TestMethod]
        public void A_Loose_File_Changes_Only_By_Its_Uploader_Or_The_Pmo()
        {
            Assert.IsTrue(FileAccessPolicy.Change(Uploading, Facts(Uploader), false).IsAllowed);
            Assert.IsTrue(FileAccessPolicy.Change(Pmo, Facts(Uploader), false).IsAllowed);
            FileVerdict refused = FileAccessPolicy.Change(Other, Facts(Uploader), false);
            Assert.AreEqual(FileVerdictKind.Denied, refused.Kind);
            // the same words as the document service uses, so one text whichever layer refuses
            Assert.AreEqual("این فایل را فقط کاربری که بارگذاری کرده یا مدیر دفتر مدیریت پروژه می‌تواند تغییر دهد.", refused.Message);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Change(Nobody, Facts(null), false).Kind);
        }

        [TestMethod]
        public void A_File_Of_A_Record_Changes_Only_For_A_Reader_Of_That_Record_And_A_Locked_File_Is_Left_To_The_Service()
        {
            Assert.IsTrue(FileAccessPolicy.Change(Other, Facts(Uploader, Record(true)), false).IsAllowed);
            FileVerdict refused = FileAccessPolicy.Change(Pmo, Facts(Uploader, Record(false)), false);
            Assert.AreEqual(FileVerdictKind.Denied, refused.Kind);
            StringAssert.Contains(refused.Message, "پیوست شده است");
            // locked: the guard lets the request reach the document service, which refuses it with the reason of the lock (for anybody, even a stranger)
            FileVerdict locked = FileAccessPolicy.Change(Other, Facts(Uploader, Letter(false)), true);
            Assert.IsTrue(locked.IsAllowed);
            Assert.IsTrue(locked.Locked);
        }

        // ------------------------------------------------------------------------------------------ the service on the database

        private sealed class TestUser : IInventoryUserContext
        {
            private readonly HashSet<string> _roles;

            public TestUser(int id, params string[] roles)
            {
                UserId = id;
                _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
            }

            public int UserId { get; }

            public bool IsInRole(string role) => !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _roles.Contains(r.Trim()));
        }

        /// <summary>The office rules, answered from lists: the letters a user is a party of, the leave requests and meetings a user may see.</summary>
        private sealed class FakeOffice : IFileOfficeRules
        {
            public readonly Dictionary<int, HashSet<int>> LetterParties = new Dictionary<int, HashSet<int>>();
            public readonly Dictionary<int, HashSet<int>> LeaveViewers = new Dictionary<int, HashSet<int>>();
            public readonly Dictionary<int, HashSet<int>> MeetingViewers = new Dictionary<int, HashSet<int>>();
            public int Asking;

            private static bool In(Dictionary<int, HashSet<int>> map, int key, int user) => map.TryGetValue(key, out HashSet<int> users) && users.Contains(user);

            public Task<bool> CanReadLetterFileAsync(int letterId, Guid documentId, CancellationToken ct) => Task.FromResult(In(LetterParties, letterId, Asking));

            public Task<bool> CanReadLeaveRequestAsync(int leaveRequestId, CancellationToken ct) => Task.FromResult(In(LeaveViewers, leaveRequestId, Asking));

            public Task<bool> CanReadMeetingAsync(int meetingId, CancellationToken ct) => Task.FromResult(In(MeetingViewers, meetingId, Asking));
        }

        private static AppHost NewHost() => FinanceTestSupport.NewHost("FileAccessR1");

        private static Guid NewDocument(AppHost host, int? uploader, string name = "scan")
        {
            Guid id = Guid.Empty;
            host.Write(uow =>
            {
                Document d = new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + ".txt", ".txt").SetUploader(uploader).SetName(name).SetDescription("توضیح " + name).SetCode("T");
                AppHost.Add(uow, d);
                uow.SaveChanges();
                id = d.Id;
            });
            return id;
        }

        private static void Seed<T>(AppHost host, T entity) where T : class => host.Write(uow =>
        {
            AppHost.Add(uow, entity);
            uow.SaveChanges();
        });

        private static FileVerdict Read(AppHost host, FakeOffice office, int user, string[] roles, Guid id)
        {
            office.Asking = user;
            return host.Read(uow => new FileAccessService(uow, new TestUser(user, roles), office).ReadAsync(id).GetAwaiter().GetResult());
        }

        private static FileVerdict Change(AppHost host, FakeOffice office, int user, string[] roles, Guid id)
        {
            office.Asking = user;
            return host.Read(uow => new FileAccessService(uow, new TestUser(user, roles), office).ChangeAsync(id).GetAwaiter().GetResult());
        }

        private static readonly string[] None = new string[0];

        [TestMethod]
        public void Service_A_Loose_File_Belongs_To_Its_Uploader_And_The_Pmo_Until_It_Is_Attached()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            Assert.IsTrue(Read(host, office, Uploader, None, id).IsAllowed);
            Assert.IsTrue(Read(host, office, PmoUser, new[] { "PMO" }, id).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, Stranger, new[] { "QC", "FINANCIAL" }, id).Kind);
            Assert.AreEqual(FileVerdictKind.NotFound, Read(host, office, Uploader, None, Guid.NewGuid()).Kind);
            Assert.IsTrue(Change(host, office, Uploader, None, id).IsAllowed);
            Assert.IsTrue(Change(host, office, PmoUser, new[] { "PMO" }, id).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Change(host, office, Stranger, new[] { "QC" }, id).Kind);
        }

        [TestMethod]
        public void Service_A_Voucher_Attachment_Is_Read_By_The_Ledger_Readers_And_Not_By_Others()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            var voucher = new FnVoucher { Status = FnVoucherStatus.Draft };
            Seed(host, voucher);
            Seed(host, new FnVoucherAttachment { VoucherId = voucher.Id, DocumentId = id, Title = "فاکتور", AddedByUserId = Uploader, AddedAt = DateTimeOffset.Now });

            Assert.IsTrue(Read(host, office, Accountant, new[] { "FINANCIAL" }, id).IsAllowed);
            Assert.IsTrue(Read(host, office, Accountant, new[] { "AUDITOR" }, id).IsAllowed);
            Assert.IsTrue(Read(host, office, Uploader, None, id).IsAllowed, "the uploader keeps his own upload");
            FileVerdict refused = Read(host, office, QcUser, new[] { "QC", "ENGINEERING" }, id);
            Assert.AreEqual(FileVerdictKind.Denied, refused.Kind);
            Assert.IsTrue(refused.Owned);
            // PMO is a ledger reader, a payroll-only user is not
            Assert.IsTrue(Read(host, office, PmoUser, new[] { "PMO" }, id).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, PayrollUser, new[] { "PAYROLL" }, id).Kind);
            // a draft voucher is not locked
            Assert.IsFalse(Change(host, office, Accountant, new[] { "FINANCIAL" }, id).Locked);
        }

        [TestMethod]
        public void Service_Legacy_Modules_Without_A_Read_Rule_Keep_Their_Files_Open_To_Signed_In_Users()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid contractFile = NewDocument(host, Uploader);
            Seed(host, new Entities.Model.NasbNiro.Contracts.ContractFile { ContractId = 1, DocumentId = contractFile });
            Assert.IsTrue(Read(host, office, Stranger, new[] { "QC" }, contractFile).IsAllowed);

            Guid photo = NewDocument(host, null);
            Seed(host, new Entities.Identity.User { UserName = "photo.user", ProfilePhotoId = photo });
            Assert.IsTrue(Read(host, office, Stranger, None, photo).IsAllowed);
        }

        [TestMethod]
        public void Service_A_Secret_Letters_Attachment_Is_Read_By_The_Parties_Only()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            var letter = new OfLetter { Status = OfLetterStatus.Sent, Classification = OfClassification.Secret, Number = "S-1" };
            Seed(host, letter);
            Seed(host, new OfLetterAttachment { LetterId = letter.Id, DocumentId = id });
            office.LetterParties[letter.Id] = new HashSet<int> { Party };

            FileVerdict party = Read(host, office, Party, None, id);
            Assert.IsTrue(party.IsAllowed);
            Assert.IsTrue(party.LetterFile);
            foreach ((int user, string[] roles) in new[] { (Stranger, new[] { "QC" }), (PmoUser, new[] { "PMO", "SECRETARIAT" }), (Uploader, None), (Accountant, new[] { "FINANCEMANAGER" }) })
            {
                FileVerdict refused = Read(host, office, user, roles, id);
                Assert.AreEqual(FileVerdictKind.Denied, refused.Kind, "user " + user);
                Assert.IsTrue(refused.LetterFile);
                Assert.AreEqual(FileText.NoReadAccess, refused.Message);
            }
            // the letter has left the draft state: the file is locked for everybody
            Assert.IsTrue(Change(host, office, Party, None, id).Locked);
            Assert.IsTrue(Change(host, office, Stranger, None, id).Locked);
        }

        [TestMethod]
        public void Service_A_Draft_Letters_Attachment_Changes_Only_For_Somebody_Who_May_Read_The_Draft()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            var letter = new OfLetter { Status = OfLetterStatus.Draft, Classification = OfClassification.Normal };
            Seed(host, letter);
            Seed(host, new OfLetterAttachment { LetterId = letter.Id, DocumentId = id });
            office.LetterParties[letter.Id] = new HashSet<int> { Uploader };

            FileVerdict writer = Change(host, office, Uploader, None, id);
            Assert.IsTrue(writer.IsAllowed);
            Assert.IsFalse(writer.Locked);
            FileVerdict pmo = Change(host, office, PmoUser, new[] { "PMO" }, id);
            Assert.AreEqual(FileVerdictKind.Denied, pmo.Kind);
            StringAssert.Contains(pmo.Message, "پیوست شده است");
            Assert.AreEqual(FileVerdictKind.Denied, Change(host, office, Stranger, new[] { "QC" }, id).Kind);
        }

        [TestMethod]
        public void Service_Leave_Evidence_And_Meeting_Evidence_Follow_The_Office_Rules()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid leaveFile = NewDocument(host, Uploader), decisionFile = NewDocument(host, Uploader);
            var leave = new OfLeaveRequest { Status = OfLeaveStatus.Submitted, AttachmentDocumentId = leaveFile };
            Seed(host, leave);
            office.LeaveViewers[leave.Id] = new HashSet<int> { Party };
            var decision = new OfDecision { MeetingId = 77, EvidenceDocumentId = decisionFile };
            Seed(host, decision);
            office.MeetingViewers[77] = new HashSet<int> { Party };

            Assert.IsTrue(Read(host, office, Party, None, leaveFile).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, Stranger, new[] { "HR" }, leaveFile).Kind);
            Assert.IsTrue(Read(host, office, Party, None, decisionFile).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, Stranger, None, decisionFile).Kind);
            // a submitted request is not locked, an approved one is
            Assert.IsFalse(Change(host, office, Party, None, leaveFile).Locked);
        }

        [TestMethod]
        public void Service_The_File_Of_A_Payroll_Package_Goes_To_The_Payroll_Readers_Only()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid payrollFile = NewDocument(host, null), salesFile = NewDocument(host, null);
            Seed(host, new FnOutboxPackage { Kind = FnOutboxKind.PayrollInsuranceList, FileDocumentId = payrollFile });
            Seed(host, new FnOutboxPackage { Kind = FnOutboxKind.SalesInvoicesTaxpayer, FileDocumentId = salesFile });

            Assert.IsTrue(Read(host, office, PayrollUser, new[] { "PAYROLL" }, payrollFile).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, Accountant, new[] { "FINANCIAL" }, payrollFile).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, Read(host, office, PmoUser, new[] { "PMO" }, payrollFile).Kind);
            Assert.IsTrue(Read(host, office, Accountant, new[] { "FINANCIAL" }, salesFile).IsAllowed);
        }

        // ------------------------------------------------------------------------------------------ the locks

        private static async Task<string> LockReasonAsync(AppHost host, Guid id)
        {
            string reason = null;
            await Task.Run(() => host.Read(uow => { reason = FileLocks.ReasonAsync(uow, id).GetAwaiter().GetResult(); return 0; }));
            return reason;
        }

        [TestMethod]
        public async Task Locks_A_Final_Voucher_An_Approved_Settlement_A_Clearance_And_An_Approved_Leave_Request_Hold_Their_Files()
        {
            AppHost host = NewHost();
            Guid draftVoucherFile = NewDocument(host, Uploader), finalVoucherFile = NewDocument(host, Uploader), settlementFile = NewDocument(host, Uploader), draftSettlementFile = NewDocument(host, Uploader),
                clearanceFile = NewDocument(host, Uploader), approvedLeaveFile = NewDocument(host, Uploader), recordedLeaveFile = NewDocument(host, Uploader), rejectedLeaveFile = NewDocument(host, Uploader);

            var draft = new FnVoucher { Status = FnVoucherStatus.Draft };
            var final = new FnVoucher { Status = FnVoucherStatus.Final };
            Seed(host, draft);
            Seed(host, final);
            Seed(host, new FnVoucherAttachment { VoucherId = draft.Id, DocumentId = draftVoucherFile, Title = "a", AddedByUserId = 1, AddedAt = DateTimeOffset.Now });
            Seed(host, new FnVoucherAttachment { VoucherId = final.Id, DocumentId = finalVoucherFile, Title = "b", AddedByUserId = 1, AddedAt = DateTimeOffset.Now });

            var posted = new FnPettyCashSettlement { Status = FnDocStatus.Posted };
            var open = new FnPettyCashSettlement { Status = FnDocStatus.Draft };
            Seed(host, posted);
            Seed(host, open);
            Seed(host, new FnPettyCashSettlementLine { SettlementId = posted.Id, DocumentId = settlementFile });
            Seed(host, new FnPettyCashSettlementLine { SettlementId = open.Id, DocumentId = draftSettlementFile });

            Seed(host, new FnInsuranceClearance { DocumentId = clearanceFile, ProviderId = 1 });
            Seed(host, new OfLeaveRequest { Status = OfLeaveStatus.Approved, AttachmentDocumentId = approvedLeaveFile });
            Seed(host, new OfLeaveRequest { Status = OfLeaveStatus.Recorded, AttachmentDocumentId = recordedLeaveFile });
            Seed(host, new OfLeaveRequest { Status = OfLeaveStatus.Rejected, AttachmentDocumentId = rejectedLeaveFile });

            Assert.IsNull(await LockReasonAsync(host, draftVoucherFile));
            Assert.IsNull(await LockReasonAsync(host, draftSettlementFile));
            Assert.IsNull(await LockReasonAsync(host, rejectedLeaveFile));
            Assert.IsNull(await LockReasonAsync(host, NewDocument(host, Uploader)));
            Assert.AreEqual(FileLocks.VoucherText, await LockReasonAsync(host, finalVoucherFile));
            Assert.AreEqual(FileLocks.SettlementText, await LockReasonAsync(host, settlementFile));
            Assert.AreEqual(FileLocks.ClearanceText, await LockReasonAsync(host, clearanceFile));
            Assert.AreEqual(FileLocks.LeaveText, await LockReasonAsync(host, approvedLeaveFile));
            Assert.AreEqual(FileLocks.LeaveText, await LockReasonAsync(host, recordedLeaveFile));

            // every lock ends with the same words, so a caller (and the smoke) recognises it
            foreach (string text in new[] { FileLocks.LetterText, FileLocks.ItpText, FileLocks.VoucherText, FileLocks.SettlementText, FileLocks.ClearanceText, FileLocks.LeaveText })
                StringAssert.EndsWith(text, "تغییر یا حذف آن مجاز نیست.");

            // through the service: a locked file passes the guard (the document service gives the reason) and is marked locked
            var office = new FakeOffice();
            Assert.IsTrue(Change(host, office, Stranger, None, finalVoucherFile).Locked);
            Assert.IsTrue(Change(host, office, Stranger, None, clearanceFile).Locked);
        }

        // ------------------------------------------------------------------------------------------ the registry

        [TestMethod]
        public void Registry_Lists_Every_Column_Of_The_Model_That_Points_At_A_File()
        {
            AppHost host = NewHost();
            var pattern = new Regex(@"Document|PhotoId$|CharterFileId$|^(TechnicalDoc|FinancialDoc|TimeSheet|LogShitId)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var textPattern = new Regex(@"(Attachment|Document|File)Ids$", RegexOptions.CultureInvariant);
            var known = new HashSet<string>(FileOwners.Simple.Select(p => p.Kind).Concat(FileOwners.Special), StringComparer.Ordinal);

            var missing = new List<string>();
            foreach (IEntityType entity in host.Model.GetEntityTypes())
            {
                if (entity.ClrType == null || entity.ClrType == typeof(Document) || entity.IsOwned()) continue;
                foreach (IProperty property in entity.GetProperties())
                {
                    if (property.IsShadowProperty() || property.Name == "Id") continue;
                    bool guidColumn = (property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?)) && pattern.IsMatch(property.Name);
                    bool listColumn = property.ClrType == typeof(string) && textPattern.IsMatch(property.Name);
                    if ((guidColumn || listColumn) && !known.Contains(entity.ClrType.Name)) missing.Add(entity.ClrType.Name + "." + property.Name);
                }
            }
            Assert.AreEqual(0, missing.Count, "a column that points at a file is not in FileOwners (add it with the read rule of its module): " + string.Join(", ", missing));

            // and every kind in the registry is a record of the model (a renamed class would otherwise silently stop protecting its files)
            var modelNames = new HashSet<string>(host.Model.GetEntityTypes().Where(e => e.ClrType != null).Select(e => e.ClrType.Name), StringComparer.Ordinal);
            string[] stale = known.Where(k => !modelNames.Contains(k)).ToArray();
            Assert.AreEqual(0, stale.Length, "kinds that are not in the model any more: " + string.Join(", ", stale));
        }

        [TestMethod]
        public void Registry_Every_Module_Rule_Is_A_Real_Role_List()
        {
            foreach (FileOwnerProbe probe in FileOwners.Simple)
            {
                if (probe.Roles == null) continue;
                Assert.IsTrue(probe.Roles.Split(',').All(r => r.Trim().Length > 0 && r.Trim() == r.Trim().ToUpperInvariant()), probe.Kind + ": " + probe.Roles);
            }
            Assert.AreEqual(FileOwners.Simple.Count, FileOwners.Simple.Select(p => p.Kind + "|" + p.Roles).Distinct().Count());
        }

        // ------------------------------------------------------------------------------------------ the hash

        [TestMethod]
        public async Task Sha256_Of_A_File_Is_The_Base64_Of_Its_Bytes_And_A_Missing_File_Has_None()
        {
            string path = Path.Combine(Path.GetTempPath(), "r1t-d8-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(path, new byte[] { 0x61, 0x62, 0x63 });   // "abc"
            try
            {
                Assert.AreEqual("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", await FileIntegrity.Sha256Async(path));
                File.WriteAllBytes(path, new byte[] { 0x61, 0x62, 0x64 });
                Assert.AreNotEqual("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", await FileIntegrity.Sha256Async(path));
            }
            finally { File.Delete(path); }
            Assert.IsNull(await FileIntegrity.Sha256Async(path));
            Assert.IsNull(await FileIntegrity.Sha256Async(null));
        }

        [TestMethod]
        public void A_Replacement_Writes_A_Row_Of_The_Trail_With_The_Hash_Before_And_After()
        {
            AppHost host = NewHost();
            Guid id = NewDocument(host, Uploader, "scan-1");
            host.Write(uow =>
            {
                Document d = uow.Set<Document>().First(x => x.Id == id);
                FileIntegrity.AuditReplaced(uow, d, "OLD-HASH", "NEW-HASH");
                d.SetSha256("NEW-HASH");
                uow.SaveChanges();
            }, AppHost.Principal(Uploader, "uploader.one"));

            FnAuditLog row = host.Read(uow => uow.Set<FnAuditLog>().Single(a => a.Action == "FileReplaced"));
            Assert.AreEqual("Document", row.EntityType);
            Assert.AreEqual(Uploader, row.UserId);
            StringAssert.Contains(row.Summary, "scan-1");
            StringAssert.Contains(row.BeforeJson, "OLD-HASH");
            StringAssert.Contains(row.AfterJson, "NEW-HASH");
            StringAssert.Contains(row.AfterJson, id.ToString());
            Assert.AreEqual("NEW-HASH", host.Read(uow => uow.Set<Document>().Single(x => x.Id == id).Sha256));
        }

        // ------------------------------------------------------------------------------------------ the guard

        private sealed class FixedAccess : IFileAccessService
        {
            private readonly FileVerdict _verdict;
            public int Asked;

            public FixedAccess(FileVerdict verdict) => _verdict = verdict;

            public Task<FileVerdict> ReadAsync(Guid documentId, CancellationToken cancellationToken = default)
            {
                Asked++;
                return Task.FromResult(_verdict);
            }

            public Task<FileVerdict> ChangeAsync(Guid documentId, CancellationToken cancellationToken = default)
            {
                Asked++;
                return Task.FromResult(_verdict);
            }
        }

        private static async Task<(IActionResult Result, bool Reached, int Asked)> RunGuard(FileGuardKind kind, string argument, object value, FileVerdict verdict)
        {
            var access = new FixedAccess(verdict);
            var services = new ServiceCollection();
            services.AddSingleton<IFileAccessService>(access);
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object> { [argument] = value }, controller: new object());
            bool reached = false;
            await new FileGuardAttribute(kind, argument).OnActionExecutionAsync(context, () =>
            {
                reached = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            });
            return (context.Result, reached, access.Asked);
        }

        [TestMethod]
        public async Task Guard_Download_Refused_Is_403_With_The_Standard_Body_And_An_Unknown_File_Is_404()
        {
            (IActionResult result, bool reached, int asked) = await RunGuard(FileGuardKind.Download, "DocumentId", Guid.NewGuid().ToString(), FileVerdict.Deny(FileText.NoReadAccess, true, true));
            Assert.IsFalse(reached);
            Assert.AreEqual(1, asked);
            var refused = (ObjectResult)result;
            Assert.AreEqual(403, refused.StatusCode);
            var body = (ResponseDto<string>)refused.Value;
            Assert.IsFalse(body.Success);
            StringAssert.Contains(body.Message, "مجوز");

            (IActionResult missing, bool missingReached, _) = await RunGuard(FileGuardKind.Download, "DocumentId", Guid.NewGuid().ToString(), FileVerdict.Missing());
            Assert.IsFalse(missingReached);
            Assert.IsInstanceOfType(missing, typeof(NotFoundObjectResult));

            (IActionResult allowed, bool allowedReached, _) = await RunGuard(FileGuardKind.Download, "DocumentId", Guid.NewGuid().ToString(), FileVerdict.Allow());
            Assert.IsNull(allowed);
            Assert.IsTrue(allowedReached);
        }

        [TestMethod]
        public async Task Guard_File_Information_Of_A_Refused_File_Carries_No_Name_Or_Description()
        {
            (IActionResult result, bool reached, _) = await RunGuard(FileGuardKind.Info, "fileId", Guid.NewGuid(), FileVerdict.Deny(FileText.NoReadAccess, true, true));
            Assert.IsFalse(reached);
            var ok = (OkObjectResult)result;
            var body = (ResponseDto<ResponseFileDto>)ok.Value;
            Assert.IsFalse(body.Success);
            Assert.IsNull(body.Data);
            Assert.AreEqual(403, (int)body.HttpStatusCode);

            // an unknown file goes on to the action, which answers "not found" in its own shape
            (IActionResult missing, bool missingReached, _) = await RunGuard(FileGuardKind.Info, "fileId", Guid.NewGuid(), FileVerdict.Missing());
            Assert.IsNull(missing);
            Assert.IsTrue(missingReached);
        }

        [TestMethod]
        public async Task Guard_Change_Reads_The_Id_From_A_Query_Value_Or_A_Body_Object_And_Refuses_In_The_Usual_Shape()
        {
            (IActionResult replace, bool replaceReached, _) = await RunGuard(FileGuardKind.Replace, "documentId", Guid.NewGuid(), FileVerdict.Deny(FileText.NotTheUploader));
            Assert.IsFalse(replaceReached);
            Assert.IsFalse(((ResponseDto<string>)((OkObjectResult)replace).Value).Success);

            (IActionResult edit, bool editReached, int asked) = await RunGuard(FileGuardKind.Edit, "updateDocumentDescriptionDto", new UpdateDocumentDto { Id = Guid.NewGuid() }, FileVerdict.Deny(FileText.OwnedChange, true));
            Assert.IsFalse(editReached);
            Assert.AreEqual(1, asked);
            Assert.IsFalse(((ResponseDto<bool>)((OkObjectResult)edit).Value).Success);

            // no usable id: the action reports it, the guard does not ask
            (IActionResult none, bool noneReached, int noneAsked) = await RunGuard(FileGuardKind.Download, "DocumentId", "not-a-guid", FileVerdict.Deny("x"));
            Assert.IsNull(none);
            Assert.IsTrue(noneReached);
            Assert.AreEqual(0, noneAsked);
        }

        [TestMethod]
        public void Guard_Is_On_Every_Documents_Action_That_Reads_Or_Changes_A_Stored_File()
        {
            var expected = new Dictionary<string, FileGuardKind>
            {
                ["DownloadAsync"] = FileGuardKind.Download,
                ["GetFileInfoAsync"] = FileGuardKind.Info,
                ["ChangeAsync"] = FileGuardKind.Replace,
                ["UpdateDocumentDescriptionAsync"] = FileGuardKind.Edit,
            };
            foreach (KeyValuePair<string, FileGuardKind> pair in expected)
            {
                System.Reflection.MethodInfo method = typeof(WebApi.Controllers.DocumentsController).GetMethod(pair.Key);
                Assert.IsNotNull(method, pair.Key);
                var guard = (FileGuardAttribute)Attribute.GetCustomAttribute(method, typeof(FileGuardAttribute));
                Assert.IsNotNull(guard, pair.Key + " has no FileGuard");
                Assert.AreEqual(pair.Value, guard.Kind, pair.Key);
                Assert.IsTrue(method.GetParameters().Any(p => p.Name == guard.Argument), pair.Key + ": the guard reads an argument that the action does not have (" + guard.Argument + ")");
            }
        }
    }
}
