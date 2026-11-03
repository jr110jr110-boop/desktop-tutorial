using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model;
using Entities.Model.NasbNiro.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;

namespace MsTests
{
    /// <summary>
    /// R1a2 (L10), the office writes that were open to every signed-in user: a meeting is created by the secretariat, the PMO, the CEO or somebody who holds
    /// (or, through an approved delegation, acts for) a position of the organisation chart; and the picture of a signature must be a file the user uploaded himself.
    /// </summary>
    [TestClass]
    public class OfficeAccessR1a2Tests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Outsider = AppHost.Principal(41, "outsider", "WAREHOUSE");
        private static readonly ClaimsPrincipal Secretariat = AppHost.Principal(42, "secretariat", "SECRETARIAT");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(43, "ceo", "CEO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(44, "pmo", "PMO");
        private static readonly ClaimsPrincipal Holder = AppHost.Principal(31, "holder", "ENGINEERING");
        private static readonly ClaimsPrincipal FormerHolder = AppHost.Principal(32, "former.holder", "ENGINEERING");
        private static readonly ClaimsPrincipal Deputy = AppHost.Principal(33, "deputy", "WAREHOUSE");
        private static readonly ClaimsPrincipal PendingDeputy = AppHost.Principal(34, "pending.deputy", "WAREHOUSE");
        private static readonly ClaimsPrincipal ExpiredDeputy = AppHost.Principal(35, "expired.deputy", "WAREHOUSE");

        private static readonly Lazy<AppHost> SharedHost = new Lazy<AppHost>(() => new AppHost("OfficeR1a2_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => SharedHost.Value;

        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string CreateText = "جلسه را دبیرخانه، مدیرعامل، PMO یا دارندگان سمت در چارت سازمانی ایجاد می‌کنند";
        private const string OwnFileText = "تصویر امضا باید فایلی باشد که خودتان بارگذاری کرده‌اید";

        /// <summary>One unit, the position of user 31, a position that is no longer active (user 32) and three delegations of the first one (current, pending, expired).</summary>
        private static readonly Lazy<int> ChairPosition = new Lazy<int>(() =>
        {
            int chair = 0;
            H.Write(u =>
            {
                var unit = AppHost.Add(u, new OfUnit { Code = "T1", Title = "test unit" });
                u.SaveChanges();
                var held = AppHost.Add(u, new OfPosition { Title = "engineering manager", UnitId = unit.Id, UserId = 31, IsActive = true });
                AppHost.Add(u, new OfPosition { Title = "former manager", UnitId = unit.Id, UserId = 32, IsActive = false });
                u.SaveChanges();
                DateTime today = DateTime.Today;
                AppHost.Add(u, new OfDelegation { PositionId = held.Id, DeputyUserId = 33, FromDate = today.AddDays(-1), ToDate = today.AddDays(1), IsActive = true, Status = OfDelegationStatus.Approved, Reason = "current" });
                AppHost.Add(u, new OfDelegation { PositionId = held.Id, DeputyUserId = 34, FromDate = today.AddDays(-1), ToDate = today.AddDays(1), IsActive = true, Status = OfDelegationStatus.Pending, Reason = "pending" });
                AppHost.Add(u, new OfDelegation { PositionId = held.Id, DeputyUserId = 35, FromDate = today.AddDays(-10), ToDate = today.AddDays(-2), IsActive = true, Status = OfDelegationStatus.Approved, Reason = "expired" });
                u.SaveChanges();
                chair = held.Id;
            }, Seeder);
            return chair;
        });

        private static Task<ResponseDto<MeetingDto>> SaveMeeting(ClaimsPrincipal who, string title, int? id = null) =>
            H.As(who, p => p.GetRequiredService<IMeetingService>().SaveMeetingAsync(
                new SaveMeetingDto { Id = id, Title = title, StartAt = new DateTime(2026, 5, 1, 10, 0, 0), ChairPositionId = ChairPosition.Value }, CancellationToken.None));

        private static bool Exists(string title) => H.Read(u => u.Set<OfMeeting>().Any(m => m.Title == title));

        [TestMethod]
        public async Task ASignedInUserWithoutARoleOrAPositionCreatesNoMeeting()
        {
            foreach (ClaimsPrincipal who in new[] { Outsider, FormerHolder, PendingDeputy, ExpiredDeputy })
            {
                string title = "refused " + who.Identity.Name;
                ResponseDto<MeetingDto> saved = await SaveMeeting(who, title);
                Assert.IsFalse(saved.Success, who.Identity.Name + " must not create a meeting");
                Assert.IsTrue(saved.Message.Contains(NotAllowed) && saved.Message.Contains(CreateText), saved.Message);
                Assert.IsFalse(Exists(title), "nothing is saved by the refused call");
            }
        }

        [TestMethod]
        public async Task TheSecretariatThePmoTheCeoAndThePositionHoldersCreateMeetings()
        {
            foreach (ClaimsPrincipal who in new[] { Secretariat, Pmo, Ceo, Holder, Deputy })
            {
                string title = "allowed " + who.Identity.Name;
                ResponseDto<MeetingDto> saved = await SaveMeeting(who, title);
                Assert.IsTrue(saved.Success, who.Identity.Name + ": " + saved.Message);
                int creator = int.Parse(who.FindFirst(ClaimTypes.NameIdentifier).Value);
                Assert.AreEqual(creator, H.Read(u => u.Set<OfMeeting>().Single(m => m.Title == title).CreatorUserId), "the creator is recorded");
            }
        }

        [TestMethod]
        public async Task EditingIsStillDecidedByTheCreatorTheChairAndTheSecretary()
        {
            ResponseDto<MeetingDto> created = await SaveMeeting(Secretariat, "edited by others");
            Assert.IsTrue(created.Success, created.Message);
            int id = created.Data.Id;

            ResponseDto<MeetingDto> stranger = await SaveMeeting(Outsider, "taken over", id);
            Assert.IsFalse(stranger.Success, "somebody who cannot even see the meeting does not edit it");
            Assert.AreEqual("edited by others", H.Read(u => u.Set<OfMeeting>().Single(m => m.Id == id).Title));

            ResponseDto<MeetingDto> chair = await SaveMeeting(Holder, "renamed by the chair", id);
            Assert.IsTrue(chair.Success, chair.Message);
            ResponseDto<MeetingDto> creator = await SaveMeeting(Secretariat, "renamed by the creator", id);
            Assert.IsTrue(creator.Success, creator.Message);
            Assert.AreEqual("renamed by the creator", H.Read(u => u.Set<OfMeeting>().Single(m => m.Id == id).Title));
        }

        // ------------------------------------------------------------------------------------------------ signature picture

        private static Guid NewFile(int? uploader)
        {
            Guid id = Guid.Empty;
            H.Write(u =>
            {
                Document d = AppHost.Add(u, new Document("UploadedDocuments/signature.png", "image/png").SetUploadedBy(uploader));
                u.SaveChanges();
                id = d.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<MyOfficeDto>> SaveSignature(ClaimsPrincipal who, Guid document) =>
            H.As(who, p => p.GetRequiredService<IOrgService>().SaveSignatureAsync(new SaveSignatureDto { DocumentId = document }, CancellationToken.None));

        private static Guid? StoredSignature(int userId) =>
            H.Read(u => u.Set<OfUserSignature>().Where(s => s.UserId == userId).Select(s => (Guid?)s.ImageDocumentId).FirstOrDefault());

        [TestMethod]
        public async Task OnlyAFileTheUserUploadedBecomesHisSignature()
        {
            Guid ownFile = NewFile(41);
            ResponseDto<MyOfficeDto> own = await SaveSignature(Outsider, ownFile);
            Assert.IsTrue(own.Success, own.Message);
            Assert.IsTrue(own.Data.HasSignature);
            Assert.AreEqual(ownFile, own.Data.SignatureDocumentId);

            // another person's picture, the id of which the caller learnt somewhere
            ResponseDto<MyOfficeDto> foreign = await SaveSignature(Secretariat, ownFile);
            Assert.IsFalse(foreign.Success, "somebody else's file is not my signature");
            Assert.IsTrue(foreign.Message.Contains(OwnFileText), foreign.Message);
            Assert.IsNull(StoredSignature(42), "no signature row was written for the caller");
            Assert.AreEqual(ownFile, StoredSignature(41), "the signature of the owner is untouched");

            // a file from before the uploader was recorded is not accepted either
            Guid oldFile = NewFile(null);
            Assert.IsFalse((await SaveSignature(Secretariat, oldFile)).Success);
            Assert.IsNull(StoredSignature(42));

            // a file that does not exist, and no file at all
            Assert.IsFalse((await SaveSignature(Secretariat, Guid.NewGuid())).Success);
            Assert.IsFalse((await SaveSignature(Secretariat, Guid.Empty)).Success);
            Assert.IsNull(StoredSignature(42));

            // his own picture works for him
            Guid mine = NewFile(42);
            Assert.IsTrue((await SaveSignature(Secretariat, mine)).Success);
            Assert.AreEqual(mine, StoredSignature(42));
        }

        [TestMethod]
        public async Task ASavedSignatureCanBeSavedAgainAndReplacedByANewOwnFile()
        {
            Guid first = NewFile(33);
            Assert.IsTrue((await SaveSignature(Deputy, first)).Success);
            Assert.IsTrue((await SaveSignature(Deputy, first)).Success, "saving the same picture again changes nothing and is allowed");
            Assert.AreEqual(first, StoredSignature(33));

            // the file that is the current signature of somebody is not available to anybody else
            ResponseDto<MyOfficeDto> taken = await SaveSignature(Holder, first);
            Assert.IsFalse(taken.Success);
            Assert.IsTrue(taken.Message.Contains(OwnFileText), taken.Message);
            Assert.IsNull(StoredSignature(31));

            Guid second = NewFile(33);
            Assert.IsTrue((await SaveSignature(Deputy, second)).Success);
            Assert.AreEqual(second, StoredSignature(33));
            Assert.AreEqual(1, H.Read(u => u.Set<OfUserSignature>().Count(s => s.UserId == 33)), "one signature row per user");
        }
    }
}
