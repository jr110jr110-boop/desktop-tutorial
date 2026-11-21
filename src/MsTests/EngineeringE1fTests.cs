using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1f: the CEO permit comes only after the rounds between engineering and the PMO; the lock sits at the release to execution.</summary>
    [TestClass]
    public class EngineeringE1fTests
    {
        private const string Reason = "دلیل کافی برای این مجوز اضطرار نوشته شده است و مدارک هنوز از تامین‌کننده نرسیده";
        private const string PermitMessage = "مجوز اضطرار فقط پس از پایان رفت‌وبرگشت مدارک با PMO (پذیرش PMO) درخواست می‌شود؛ در طول رفت‌وبرگشت از مدیرعامل تاییدی گرفته نمی‌شود.";

        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static async Task<EnHandoverDto> GetAs(AppHost host, ClaimsPrincipal who, int id) =>
            Ok(await host.As(who, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct)));

        private static async Task<string> AskPermit(AppHost host, int handover, int hours = 24)
        {
            var r = await host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().RequestWaiverAsync(new EnWaiverRequestDto { HandoverId = handover, Reason = Reason, DebtHours = hours }, Ct));
            return r.Success ? null : r.Message;
        }

        [TestMethod]
        public async Task The_Permit_Request_Is_Refused_With_The_New_Text_In_Every_Status_But_Accepted_And_The_Flag_Says_The_Same()
        {
            AppHost host = Started("e1f_permit");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            // Draft
            Assert.AreEqual(PermitMessage, await AskPermit(host, h.Id));
            Assert.AreEqual(PermitMessage, (await GetAs(host, Mgr1, h.Id)).RequestWaiverBlockedReason);
            // Submitted (incomplete goes to the PMO)
            EnHandoverDto s = Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual((int)EnHandoverStatus.Submitted, s.Status);
            Assert.AreEqual(PermitMessage, await AskPermit(host, h.Id));
            Assert.IsFalse((await GetAs(host, Mgr1, h.Id)).CanRequestWaiver);
            // Returned
            Ok(await Act(host, Pmo1, "return", d => { d.Id = h.Id; d.Note = "نقشه‌های اجرایی هنوز کامل نیست"; }));
            Assert.AreEqual(PermitMessage, await AskPermit(host, h.Id));
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()), "the CEO was never asked");
            // a new round: submit, accept (incomplete), and only now the permit is possible, once
            EnHandoverDto r2 = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = r2.Id));
            Assert.AreEqual(PermitMessage, await AskPermit(host, r2.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = r2.Id));
            EnHandoverDto acc = await GetAs(host, Mgr1, r2.Id);
            Assert.IsTrue(acc.CanRequestWaiver, acc.RequestWaiverBlockedReason);
            Assert.IsNull(await AskPermit(host, r2.Id));
            Assert.IsFalse((await GetAs(host, Mgr1, r2.Id)).CanRequestWaiver, "one open permit at a time");
            StringAssert.Contains(await AskPermit(host, r2.Id), "مجوز اضطرار باز");
        }

        [TestMethod]
        public async Task A_Complete_Package_Needs_No_Permit_And_Releases_And_Its_Notice_Is_Null()
        {
            AppHost host = Started("e1f_complete");
            (int contract, int caseId) = await NewDesign(host);
            Deliverable(host, contract, "DWG"); Deliverable(host, contract, "DATASHEET");
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Assert.IsTrue(h.Complete); Assert.IsNull(h.IncompleteNotice); Assert.IsNull(h.LockMessage);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            EnHandoverDto acc = await GetAs(host, Mgr1, h.Id);
            Assert.IsFalse(acc.CanRequestWaiver);
            StringAssert.Contains(acc.RequestWaiverBlockedReason, "لازم نیست");
            StringAssert.Contains(await AskPermit(host, h.Id), "لازم نیست");
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Assert.IsTrue((await GetAs(host, Pmo2, h.Id)).CanRelease);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual((int)EnHandoverStatus.Released, rel.Status);
            Assert.AreEqual(0, rel.Debts.Count);
            Assert.AreEqual(0, host.Read(u => u.Set<EnWaiver>().Count()));
        }

        [TestMethod]
        public async Task The_Release_Flag_And_The_Action_Say_The_Same_Lock_Text_With_And_Without_A_Permit()
        {
            AppHost host = Started("e1f_release");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            EnHandoverDto shown = await GetAs(host, Pmo2, h.Id);
            Assert.IsFalse(shown.CanRelease);
            string refused = Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "ابلاغ به اجرا قفل است");
            Assert.AreEqual(shown.ReleaseBlockedReason, refused, "the flag and the action give the same text");
            StringAssert.Contains(refused, "2 مدرک الزامی هنوز موجود نیست");
            StringAssert.Contains(refused, "پس از پذیرش PMO، مدیر مهندسی مجوز اضطرار می‌خواهد و فقط مدیرعامل تصمیم می‌گیرد.");
            // a REQUESTED permit does not open the lock; the decision of the CEO does
            Assert.IsNull(await AskPermit(host, h.Id));
            Assert.IsFalse((await GetAs(host, Pmo2, h.Id)).CanRelease);
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "قفل");
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = "approve" }, Ct)));
            Assert.IsTrue((await GetAs(host, Pmo2, h.Id)).CanRelease);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual(2, rel.Debts.Count);
            Assert.IsTrue(rel.Waiver.Used);
        }

        [TestMethod]
        public async Task A_Rejected_Permit_Keeps_The_Release_Locked_And_A_New_Request_Is_Possible()
        {
            AppHost host = Started("e1f_reject");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Assert.IsNull(await AskPermit(host, h.Id));
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = "reject", Note = "مدارک باید پیش از ابلاغ کامل شود" }, Ct)));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Refused(await Act(host, Pmo2, "release", d => d.Id = h.Id), "قفل");
            Assert.IsTrue((await GetAs(host, Mgr1, h.Id)).CanRequestWaiver);
        }

        [TestMethod]
        public async Task A_Permit_Approved_In_The_Draft_Under_The_Old_Order_Is_Not_Voided_And_Its_Release_Works()
        {
            AppHost host = Started("e1f_old");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            // data of the old order: the permit was asked in the draft and approved there, not yet used
            host.Write(u =>
            {
                var lines = u.Set<EnHandoverLine>().Where(l => l.HandoverId == h.Id).ToList();
                string snapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { Lines = EnHandoverRules.MissingKeys(lines), DossierUnconfirmed = false });
                AppHost.Add(u, new EnWaiver { Number = "EWV-OLD", HandoverId = h.Id, CaseId = caseId, Reason = Reason, DebtHours = 24, DebtDueAt = DateTimeOffset.Now.AddHours(20), MissingSnapshot = snapshot, Status = EnWaiverStatus.Approved, RequestedByUserId = 2121, RequestedAt = DateTimeOffset.Now.AddHours(-2), DecidedByUserId = 2122, DecidedAt = DateTimeOffset.Now.AddHours(-1) });
                u.SaveChanges();
            }, Seeder);
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Assert.AreEqual(0, host.Read(u => u.Set<EnDocumentDebt>().Count()), "no debt before the release");
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            Assert.IsTrue((await GetAs(host, Pmo2, h.Id)).CanRelease);
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual(2, rel.Debts.Count);
            Assert.AreEqual("EWV-OLD", rel.WaiverNumber);
            Assert.AreEqual(EnWaiverStatus.Approved, host.Read(u => u.Set<EnWaiver>().Single().Status));
        }

        [TestMethod]
        public async Task The_Incomplete_Notice_Names_The_Counts_And_Follows_The_Dossier()
        {
            string both = EnHandoverRules.IncompleteNotice(2, true);
            StringAssert.StartsWith(both, "این بسته ناقص است: 2 مدرک الزامی هنوز موجود نیست و پروندهٔ پلتفرم هنوز توسط مدیر مهندسی تایید نشده است.");
            StringAssert.EndsWith(both, "رفت‌وبرگشت با PMO ادامه دارد؛ ابلاغ به اجرا فقط با مدارک کامل یا مجوز اضطرار مدیرعامل ممکن است.");
            StringAssert.Contains(EnHandoverRules.IncompleteNotice(0, true), "پروندهٔ پلتفرم هنوز کامل و تایید نشده است");
            Assert.IsNull(EnHandoverRules.IncompleteNotice(0, false));
            Assert.IsNull(EnHandoverRules.LockMessage(0, false));
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task The_Decision_Flag_Of_The_Ceo_Follows_The_State_Of_The_Handover()
        {
            AppHost host = Started("e1f_decide");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            Ok(await Act(host, Mgr1, "submit", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "accept", d => d.Id = h.Id));
            Assert.IsNull(await AskPermit(host, h.Id));
            int waiver = host.Read(u => u.Set<EnWaiver>().Single().Id);
            EnWaiverDto w = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().GetWaiverAsync(waiver, Ct)));
            Assert.IsTrue(w.CanDecide, w.DecideBlockedReason);
            host.Write(u => { u.Set<EnHandover>().First(x => x.Id == h.Id).Status = EnHandoverStatus.Void; u.SaveChanges(); }, Seeder);
            w = Ok(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().GetWaiverAsync(waiver, Ct)));
            Assert.IsFalse(w.CanDecide, "the flag and the action agree: a void handover waits for no permit");
            Refused(await host.As(Ceo, p => p.GetRequiredService<IEnHandoverService>().DecideWaiverAsync(new EnWaiverDecisionDto { Id = waiver, Decision = "approve" }, Ct)), "دیگر منتظر");
        }
    }
}
