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
            Assert.AreEqual(FileVerdictKind.NotFound, FileAccessPolicy.Change(Uploading, new FileFacts { Exists = false }).Kind);
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
        public void A_Change_Is_Checked_Like_A_Read_First_So_That_A_Stranger_Learns_Nothing_About_The_File()
        {
            // a loose file: the uploader and the PMO pass (the document service then applies the uploader rule), a stranger is turned away with the access text
            Assert.IsTrue(FileAccessPolicy.Change(Uploading, Facts(Uploader)).IsAllowed);
            Assert.IsTrue(FileAccessPolicy.Change(Pmo, Facts(Uploader)).IsAllowed);
            FileVerdict refused = FileAccessPolicy.Change(Other, Facts(Uploader));
            Assert.AreEqual(FileVerdictKind.Denied, refused.Kind);
            Assert.AreEqual(FileText.NoReadAccess, refused.Message);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Change(Nobody, Facts(null)).Kind);
            // an owned or sent file: somebody who may not read it gets the same text, not the lock text that names the kind of record (L1)
            FileVerdict letter = FileAccessPolicy.Change(Other, Facts(Uploader, Letter(false)));
            Assert.AreEqual(FileVerdictKind.Denied, letter.Kind);
            Assert.AreEqual(FileText.NoReadAccess, letter.Message);
            Assert.AreEqual(FileVerdictKind.Denied, FileAccessPolicy.Change(Pmo, Facts(Uploader, Record(false))).Kind);
            // a reader of the record passes the guard; the document service then refuses the change of an owned file (R1a2 referrer lock), so a read role never opens a change (H3)
            Assert.IsTrue(FileAccessPolicy.Change(Other, Facts(Uploader, Record(true))).IsAllowed);
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
        public void Service_A_Record_That_Is_Not_In_The_Read_Map_Fails_Closed()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            // an AUDITOR is a ledger reader: the voucher attachment is readable, but the same user does not read a file of a record type nobody mapped (here: the quality record is mapped, a type removed from the map would not be)
            var voucher = new FnVoucher { Status = FnVoucherStatus.Draft };
            Seed(host, voucher);
            Seed(host, new FnVoucherAttachment { VoucherId = voucher.Id, DocumentId = id, Title = "x", AddedByUserId = Uploader, AddedAt = DateTimeOffset.Now });
            Assert.IsTrue(Read(host, office, Accountant, new[] { "AUDITOR" }, id).IsAllowed);
            Assert.IsFalse(FileOwners.TryRoles("SomeNewRecordType", "DocumentId", out string none), "an unmapped type has no roles: the service lets in the uploader and the PMO only");
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
            // a stranger's change request is turned away before any lock text is built (L1)
            Assert.AreEqual(FileVerdictKind.Denied, Change(host, office, Stranger, None, id).Kind);
        }

        [TestMethod]
        public void Service_A_Draft_Letters_Attachment_Passes_The_Change_Check_Only_For_Somebody_Who_May_Read_The_Draft()
        {
            AppHost host = NewHost();
            var office = new FakeOffice();
            Guid id = NewDocument(host, Uploader);
            var letter = new OfLetter { Status = OfLetterStatus.Draft, Classification = OfClassification.Normal };
            Seed(host, letter);
            Seed(host, new OfLetterAttachment { LetterId = letter.Id, DocumentId = id });
            office.LetterParties[letter.Id] = new HashSet<int> { Uploader };

            Assert.IsTrue(Change(host, office, Uploader, None, id).IsAllowed);
            Assert.AreEqual(FileVerdictKind.Denied, Change(host, office, PmoUser, new[] { "PMO" }, id).Kind);
            Assert.AreEqual(FileVerdictKind.Denied, Change(host, office, Stranger, new[] { "QC" }, id).Kind);
        }

        // ------------------------------------------------------------------------------------------ the registry

        [TestMethod]
        public void Registry_Every_Record_Type_That_Holds_A_File_Has_A_Read_Rule_Or_Is_Handled_By_The_Service()
        {
            AppHost host = NewHost();
            var special = new HashSet<string>(FileOwners.Special, StringComparer.Ordinal);
            var holders = DocumentLinks_Of(host).Select(l => l.EntityType.Name).Distinct().ToList();
            var unmapped = holders.Where(n => !special.Contains(n) && !FileOwners.ReadRoles.Keys.Any(k => k == n || k.StartsWith(n + ".", StringComparison.Ordinal))).ToList();
            // the unmapped types are not a hole: the service fails closed for them (uploader and PMO only); the list is pinned so that a new file column gets a decision
            CollectionAssert.AreEquivalent(new string[0], unmapped, "add the read rule of the module for: " + string.Join(", ", unmapped));

            // every name in the map is a record of the model (a renamed class would otherwise silently stop protecting its files)
            var modelNames = new HashSet<string>(host.Model.GetEntityTypes().Where(e => e.ClrType != null).Select(e => e.ClrType.Name), StringComparer.Ordinal);
            string[] stale = FileOwners.ReadRoles.Keys.Select(k => k.Split('.')[0]).Concat(special).Where(k => !modelNames.Contains(k)).ToArray();
            Assert.AreEqual(0, stale.Length, "kinds that are not in the model any more: " + string.Join(", ", stale));
        }

        private static IReadOnlyList<Services.Services.DocumentLink> DocumentLinks_Of(AppHost host) => Services.Services.DocumentLinks.Of(host.Model);

        [TestMethod]
        public void Registry_Every_Module_Rule_Is_A_Real_Role_List()
        {
            foreach (var pair in FileOwners.ReadRoles)
            {
                if (pair.Value == null) continue;
                Assert.IsTrue(pair.Value.Split(',').All(r => r.Trim().Length > 0 && r.Trim() == r.Trim().ToUpperInvariant()), pair.Key + ": " + pair.Value);
            }
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
            (IActionResult result, bool reached, int asked) = await RunGuard(FileGuardKind.Download, "DocumentId", Guid.NewGuid().ToString(), FileVerdict.Deny(FileText.NoReadAccess, true, false));
            Assert.IsFalse(reached);
            Assert.AreEqual(1, asked);
            var refused = (ObjectResult)result;
            Assert.AreEqual(403, refused.StatusCode);
            var body = (ResponseDto<string>)refused.Value;
            Assert.IsFalse(body.Success);
            StringAssert.Contains(body.Message, "مجوز");

            // a refused letter file answers like the letter endpoint does: not found (L1)
            (IActionResult letterFile, bool letterReached, _) = await RunGuard(FileGuardKind.Download, "DocumentId", Guid.NewGuid().ToString(), FileVerdict.Deny(FileText.NoReadAccess, true, true));
            Assert.IsFalse(letterReached);
            Assert.IsInstanceOfType(letterFile, typeof(NotFoundObjectResult));

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
            (IActionResult replace, bool replaceReached, _) = await RunGuard(FileGuardKind.Replace, "documentId", Guid.NewGuid(), FileVerdict.Deny(FileText.NoReadAccess));
            Assert.IsFalse(replaceReached);
            Assert.IsFalse(((ResponseDto<string>)((OkObjectResult)replace).Value).Success);

            (IActionResult edit, bool editReached, int asked) = await RunGuard(FileGuardKind.Edit, "updateDocumentDescriptionDto", new UpdateDocumentDto { Id = Guid.NewGuid() }, FileVerdict.Deny(FileText.NoReadAccess, true));
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
