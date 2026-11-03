using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>
    /// R1t (D23, D25): the titles of the inbox items of Confidential and Secret letters, and who changes the attendance configuration. The real services on the in-memory database.
    /// </summary>
    [TestClass]
    public class OfficeInboxR1Tests
    {
        private const int Sender = 101, Recipient = 102, Target = 103, Deputy = 104, Stranger = 105, Hr = 106, Pmo = 107;
        private const string SecretWord = "SECRETSUBJECTWORD", ConfidentialWord = "CONFIDENTIALSUBJECTWORD";

        private static ClaimsPrincipal Who(int id, params string[] roles) => AppHost.Principal(id, "user" + id, roles);

        private sealed class World
        {
            public AppHost Host;
            public int P1, P2, P3;
            public int SecretId, ConfidentialId;
        }

        private static OfLetter Letter(OfClassification classification, string subject, string number, DateTime date, int sender, int recipient) => new OfLetter
        {
            Kind = OfLetterKind.Outgoing, Number = number, Date = date, Subject = subject, Classification = classification, Status = OfLetterStatus.Sent,
            SenderPositionId = sender, CreatorUserId = Sender,
            Recipients = new List<OfLetterRecipient> { new OfLetterRecipient { Type = OfRecipientType.To, PositionId = recipient } },
            Approvals = new List<OfLetterApproval>(), Referrals = new List<OfReferral>(), Attachments = new List<OfLetterAttachment>(), Links = new List<OfRecordLink>(),
        };

        /// <summary>
        /// Three positions (the sender, the recipient, and the position the letter is referred to) with their holders, a deputy of the third position whose delegation starts today, a Secret
        /// and a Confidential letter (both sent, dated five days ago, so before the delegation started).
        /// </summary>
        private static World Build(string name)
        {
            var w = new World { Host = new AppHost("r1t-office-" + name) };
            DateTime today = DateTime.Today;
            w.Host.Write(uow =>
            {
                OfUnit unit = AppHost.Add(uow, new OfUnit { Code = "T1", Title = "unit", IsActive = true });
                uow.SaveChanges();
                OfPosition p1 = AppHost.Add(uow, new OfPosition { Title = "sender position", UnitId = unit.Id, UserId = Sender, IsActive = true });
                OfPosition p2 = AppHost.Add(uow, new OfPosition { Title = "recipient position", UnitId = unit.Id, UserId = Recipient, IsActive = true });
                OfPosition p3 = AppHost.Add(uow, new OfPosition { Title = "target position", UnitId = unit.Id, UserId = Target, IsActive = true });
                uow.SaveChanges();
                AppHost.Add(uow, new OfDelegation { PositionId = p3.Id, DeputyUserId = Deputy, FromDate = today, ToDate = today.AddDays(30), IsActive = true, Status = OfDelegationStatus.Approved });
                OfLetter secret = AppHost.Add(uow, Letter(OfClassification.Secret, SecretWord, "OUT-S-1", today.AddDays(-5), p1.Id, p2.Id));
                OfLetter confidential = AppHost.Add(uow, Letter(OfClassification.Confidential, ConfidentialWord, "OUT-C-1", today.AddDays(-5), p1.Id, p2.Id));
                uow.SaveChanges();
                w.P1 = p1.Id; w.P2 = p2.Id; w.P3 = p3.Id; w.SecretId = secret.Id; w.ConfidentialId = confidential.Id;
            });
            return w;
        }

        private static Task<ResponseDto<LetterDto>> Refer(World w, int fromUser, int letterId, int toPosition) =>
            w.Host.As(Who(fromUser), sp => sp.GetRequiredService<ILetterService>().ReferAsync(
                new ReferLetterDto { LetterId = letterId, ToPositionIds = new List<int> { toPosition }, Instruction = "please act", Action = OfReferralAction.ForAction }, CancellationToken.None));

        private static async Task<List<InboxItemDto>> Inbox(World w, int user, string contains = null, params string[] roles)
        {
            ResponseDto<PagedList<InboxItemDto>> r = await w.Host.As(Who(user, roles), sp => sp.GetRequiredService<IInboxService>().GetInboxAsync(
                new InboxQuery { PageSize = 100, Contains = contains, IncludeClosed = true }, CancellationToken.None));
            Assert.IsTrue(r.Success, r.Message);
            return r.Data.ToList();
        }

        // ------------------------------------------------------------------------------------------ pure functions

        [TestMethod]
        public void A_Secret_Letter_Is_Named_By_Its_Classification_And_Number_Only()
        {
            var secret = new OfLetter { Classification = OfClassification.Secret, Number = " OUT-9 ", Subject = SecretWord };
            Assert.AreEqual("نامه سری شماره OUT-9", LetterService.InboxLabel(secret));
            Assert.IsFalse(LetterService.InboxLabel(secret).Contains(SecretWord));
            Assert.AreEqual("نامه سری (هنوز شماره ندارد)", LetterService.InboxLabel(new OfLetter { Classification = OfClassification.Secret, Number = null, Subject = SecretWord }));
        }

        [TestMethod]
        public void Normal_And_Confidential_Letters_Are_Named_By_Their_Subject_At_Creation()
        {
            Assert.AreEqual("a subject", LetterService.InboxLabel(new OfLetter { Classification = OfClassification.Normal, Number = "N-1", Subject = "a subject" }));
            Assert.AreEqual("a subject", LetterService.InboxLabel(new OfLetter { Classification = OfClassification.Confidential, Number = "N-1", Subject = "a subject" }));
        }

        [TestMethod]
        public void Register_Name_Of_A_Confidential_Letter_Says_Confidential()
        {
            Assert.AreEqual("نامه محرمانه شماره C-5", LetterService.InboxRegisterName(OfClassification.Confidential, "C-5"));
            Assert.AreEqual("نامه محرمانه (هنوز شماره ندارد)", LetterService.InboxRegisterName(OfClassification.Confidential, "  "));
        }

        // ------------------------------------------------------------------------------------------ the inbox

        [TestMethod]
        public async Task A_Referral_Of_A_Secret_Letter_Puts_Only_The_Number_In_The_Inbox_Of_The_Target()
        {
            World w = Build("secret-title");
            ResponseDto<LetterDto> refer = await Refer(w, Recipient, w.SecretId, w.P3);
            Assert.IsTrue(refer.Success, refer.Message);

            // the stored title never carries the subject
            string stored = w.Host.Read(uow => uow.Set<OfInboxItem>().Single(i => i.SourceKey.StartsWith("referral:")).Title);
            Assert.IsFalse(stored.Contains(SecretWord), stored);
            Assert.IsTrue(stored.Contains("OUT-S-1"), stored);

            // the holder of the target position reads the letter (he is a party through the referral) and sees the number-only title
            List<InboxItemDto> mine = await Inbox(w, Target);
            Assert.AreEqual(1, mine.Count);
            Assert.IsTrue(mine[0].Title.Contains("OUT-S-1"), mine[0].Title);
            Assert.IsFalse(mine[0].Title.Contains(SecretWord));
        }

        [TestMethod]
        public async Task The_Close_Alert_Of_A_Secret_Referral_Names_The_Letter_By_Its_Number_Only()
        {
            World w = Build("secret-close");
            Assert.IsTrue((await Refer(w, Recipient, w.SecretId, w.P3)).Success);
            int referralId = w.Host.Read(uow => uow.Set<OfReferral>().Single().Id);
            ResponseDto<LetterDto> close = await w.Host.As(Who(Target), sp => sp.GetRequiredService<ILetterService>().ReferralActionAsync(
                new ReferralActionDto { Id = referralId, Action = "close", Text = "done" }, CancellationToken.None));
            Assert.IsTrue(close.Success, close.Message);

            List<InboxItemDto> alerts = (await Inbox(w, Recipient)).Where(i => i.Kind == OfInboxKind.Alert).ToList();
            Assert.AreEqual(1, alerts.Count);
            Assert.IsFalse(alerts[0].Title.Contains(SecretWord), alerts[0].Title);
            Assert.IsTrue(alerts[0].Title.Contains("OUT-S-1"), alerts[0].Title);
        }

        [TestMethod]
        public async Task A_Deputy_Who_May_Not_Read_The_Secret_Letter_Sees_Only_The_Register_Name_And_Cannot_Search_The_Subject()
        {
            World w = Build("secret-deputy");
            Assert.IsTrue((await Refer(w, Recipient, w.SecretId, w.P3)).Success);

            // the delegation started today, the letter is five days old: the deputy does not read it (the deputy rule), yet the position's item reaches his inbox
            List<InboxItemDto> deputy = await Inbox(w, Deputy);
            Assert.AreEqual(1, deputy.Count, "the item of the position reaches the deputy");
            Assert.AreEqual("نامه سری شماره OUT-S-1", deputy[0].Title);

            // an old item (made before this rule) may still carry the subject: the inbox masks it, and the search does not find the word
            w.Host.Write(uow =>
            {
                OfInboxItem item = uow.Set<OfInboxItem>().Single();
                item.Title = "ارجاع برای اقدام: " + SecretWord;
                uow.SaveChanges();
            });
            Assert.AreEqual("نامه سری شماره OUT-S-1", (await Inbox(w, Deputy))[0].Title, "the stored subject is masked for the deputy");
            Assert.AreEqual(0, (await Inbox(w, Deputy, SecretWord)).Count, "and cannot be searched by the deputy");

            // the holder of the position reads the letter: he sees the stored text and finds it by search
            List<InboxItemDto> holder = await Inbox(w, Target);
            Assert.IsTrue(holder[0].Title.Contains(SecretWord), "a party reads the title as stored");
            Assert.AreEqual(1, (await Inbox(w, Target, SecretWord)).Count);
        }

        [TestMethod]
        public async Task A_Deputy_Whose_Delegation_Began_Before_The_Letter_Is_A_Party_And_Reads_The_Title()
        {
            World w = Build("secret-deputy-before");
            // the letter is dated today, after the delegation started: the deputy is a party of it
            w.Host.Write(uow =>
            {
                OfLetter l = uow.Set<OfLetter>().Single(x => x.Id == w.SecretId);
                l.Date = DateTime.Today;
                uow.SaveChanges();
            });
            Assert.IsTrue((await Refer(w, Recipient, w.SecretId, w.P3)).Success);
            w.Host.Write(uow =>
            {
                OfInboxItem item = uow.Set<OfInboxItem>().Single();
                item.Title = "ارجاع برای اقدام: " + SecretWord;
                uow.SaveChanges();
            });
            Assert.IsTrue((await Inbox(w, Deputy))[0].Title.Contains(SecretWord), "the deputy may read this letter, so the title is not masked");
        }

        [TestMethod]
        public async Task A_Confidential_Letter_Item_Shows_The_Subject_Only_To_Those_Who_Read_The_Letter()
        {
            World w = Build("confidential");
            Assert.IsTrue((await Refer(w, Recipient, w.ConfidentialId, w.P3)).Success);
            string stored = w.Host.Read(uow => uow.Set<OfInboxItem>().Single(i => i.SourceKey.StartsWith("referral:")).Title);
            Assert.IsTrue(stored.Contains(ConfidentialWord), "a Confidential subject is stored for the parties");

            // the target (a party) and the deputy (a party of a Confidential letter whatever the dates) read it
            Assert.IsTrue((await Inbox(w, Target))[0].Title.Contains(ConfidentialWord));
            Assert.IsTrue((await Inbox(w, Deputy))[0].Title.Contains(ConfidentialWord));

            // an item addressed to somebody who is no party (a task by role, for example) is masked
            w.Host.Write(uow =>
            {
                AppHost.Add(uow, new OfInboxItem
                {
                    RecipientUserId = Stranger, Kind = OfInboxKind.Task, Title = "task: " + ConfidentialWord, SourceModule = "office", SourceType = "letter", SourceId = w.ConfidentialId,
                    SourceKey = "letter:" + w.ConfidentialId + ":x", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                uow.SaveChanges();
            });
            List<InboxItemDto> stranger = await Inbox(w, Stranger);
            Assert.AreEqual(1, stranger.Count);
            Assert.AreEqual("نامه محرمانه شماره OUT-C-1", stranger[0].Title);
            Assert.AreEqual(0, (await Inbox(w, Stranger, ConfidentialWord)).Count, "the stranger cannot find it by the subject either");

            // the secretariat reads Confidential letters in full once they are numbered
            w.Host.Write(uow =>
            {
                AppHost.Add(uow, new OfInboxItem
                {
                    RecipientRole = "SECRETARIAT", Kind = OfInboxKind.Task, Title = "task: " + ConfidentialWord, SourceModule = "office", SourceType = "letter", SourceId = w.ConfidentialId,
                    SourceKey = "letter:" + w.ConfidentialId + ":y", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                uow.SaveChanges();
            });
            List<InboxItemDto> secretariat = await Inbox(w, 108, null, "SECRETARIAT");
            Assert.AreEqual(1, secretariat.Count);
            Assert.IsTrue(secretariat[0].Title.Contains(ConfidentialWord), "the secretariat reads a Confidential letter");
        }

        [TestMethod]
        public async Task The_Secretariat_Does_Not_Read_A_Secret_Subject_From_The_Inbox()
        {
            World w = Build("secretariat-secret");
            w.Host.Write(uow =>
            {
                AppHost.Add(uow, new OfInboxItem
                {
                    RecipientRole = "SECRETARIAT", Kind = OfInboxKind.Task, Title = "task: " + SecretWord, SourceModule = "office", SourceType = "letter", SourceId = w.SecretId,
                    SourceKey = "letter:" + w.SecretId + ":dispatch", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                uow.SaveChanges();
            });
            List<InboxItemDto> items = await Inbox(w, 108, null, "SECRETARIAT");
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("نامه سری شماره OUT-S-1", items[0].Title, "register data only, like the letter itself");
        }

        [TestMethod]
        public async Task Items_Of_Normal_Letters_And_Of_Other_Modules_Are_Not_Touched()
        {
            World w = Build("normal");
            int normalId = 0;
            w.Host.Write(uow =>
            {
                OfLetter l = AppHost.Add(uow, Letter(OfClassification.Normal, "plain subject", "OUT-N-1", DateTime.Today.AddDays(-1), w.P1, w.P2));
                uow.SaveChanges();
                normalId = l.Id;
                AppHost.Add(uow, new OfInboxItem
                {
                    RecipientUserId = Stranger, Kind = OfInboxKind.Task, Title = "plain: plain subject", SourceModule = "office", SourceType = "letter", SourceId = l.Id,
                    SourceKey = "letter:" + l.Id + ":p", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                AppHost.Add(uow, new OfInboxItem
                {
                    RecipientUserId = Stranger, Kind = OfInboxKind.Task, Title = "voucher " + SecretWord, SourceModule = "finance", SourceType = "voucher", SourceId = w.SecretId,
                    SourceKey = "voucher:1", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                uow.SaveChanges();
            });
            List<InboxItemDto> items = await Inbox(w, Stranger);
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items.Any(i => i.Title == "plain: plain subject"), "a Normal letter keeps its title");
            Assert.IsTrue(items.Any(i => i.Title == "voucher " + SecretWord), "an item of another module is not a letter item, even when its source id equals a letter id");
            Assert.AreEqual(1, (await Inbox(w, Stranger, "plain subject")).Count);
            Assert.IsTrue(normalId > 0);
        }

        // ------------------------------------------------------------------------------------------ D25: attendance configuration

        [TestMethod]
        public async Task Shifts_The_Policy_And_Devices_Are_Changed_By_HR_Only_And_The_PMO_Reads()
        {
            var host = new AppHost("r1t-office-attendance");
            ClaimsPrincipal hr = Who(Hr, "HR"), pmo = Who(Pmo, "PMO"), ceo = Who(109, "CEO");
            var shift = new SaveShiftDto { Title = "office", Start = "08:00", End = "16:00", BreakMinutes = 30, WorkDays = new List<int> { 6, 0, 1, 2, 3 } };

            foreach (ClaimsPrincipal who in new[] { pmo, ceo })
            {
                Assert.IsFalse((await host.As(who, sp => sp.GetRequiredService<IAttendanceService>().SaveShiftAsync(shift, CancellationToken.None))).Success, "a shift is not saved by PMO or CEO");
                if (who == pmo) Assert.IsFalse((await host.As(who, sp => sp.GetRequiredService<IAttendanceService>().SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 10 }, CancellationToken.None))).Success, "the policy is not saved by the PMO (the CEO may: only the CEO switches the two controls off, R1f)");
                if (who == ceo) Assert.IsFalse((await host.As(who, sp => sp.GetRequiredService<IAttendanceService>().SaveDeviceAsync(new SaveDeviceDto { Code = "GATE-1", Title = "gate" }, CancellationToken.None))).Success, "a device is not registered by the CEO (HR and the PMO do, R1f)");
                Assert.IsFalse((await host.As(who, sp => sp.GetRequiredService<IAttendanceService>().SaveAssignmentAsync(new SaveShiftAssignmentDto { EmployeeId = 1, ShiftId = 1, FromDate = DateTime.Today }, CancellationToken.None))).Success, "a shift assignment is not saved by PMO or CEO");
            }

            ResponseDto<ShiftDto> saved = await host.As(hr, sp => sp.GetRequiredService<IAttendanceService>().SaveShiftAsync(shift, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await host.As(hr, sp => sp.GetRequiredService<IAttendanceService>().SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 10 }, CancellationToken.None))).Success);
            Assert.IsTrue((await host.As(hr, sp => sp.GetRequiredService<IAttendanceService>().SaveDeviceAsync(new SaveDeviceDto { Code = "GATE-1", Title = "gate" }, CancellationToken.None))).Success);

            // the PMO reads: the policy, the shifts and the devices
            Assert.AreEqual(10, (await host.As(pmo, sp => sp.GetRequiredService<IAttendanceService>().GetPolicyAsync(CancellationToken.None))).Data.GraceMinutes);
            Assert.AreEqual(1, (await host.As(pmo, sp => sp.GetRequiredService<IAttendanceService>().GetShiftsAsync(CancellationToken.None))).Data.Count);
            ResponseDto<List<DeviceDto>> devices = await host.As(pmo, sp => sp.GetRequiredService<IAttendanceService>().GetDevicesAsync(CancellationToken.None));
            Assert.IsTrue(devices.Success, devices.Message);
            Assert.AreEqual(1, devices.Data.Count);
        }
    }
}
