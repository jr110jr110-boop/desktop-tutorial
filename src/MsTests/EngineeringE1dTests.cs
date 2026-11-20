using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;
using static MsTests.EngineeringStandardsTests;

namespace MsTests
{
    /// <summary>ENG-E1d: office hours for the stopped human work of engineering (employer rule 14), the source file of a reference list, the data sheet file of a platform dossier.</summary>
    [TestClass]
    public class EngineeringE1dTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }
        private static bool Open(AppHost host, string key) => host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == key && i.Status < OfInboxStatus.Done));

        private static int AddTask(AppHost host, string step, AgentTaskState state, DateTimeOffset? dueAt, DateTimeOffset? escalatedAt = null, string roles = "ENGINEERING,ENGINEER")
        {
            int id = 0;
            host.Write(u =>
            {
                AgentTask t = AppHost.Add(u, new AgentTask { StepCode = step, Station = 5, AgentCode = "AG-ENG", Kind = AgentStepKind.Input, State = state, TargetType = "PbProduct", TargetId = 1, TargetNumber = "P-1", Title = "کار آزمون " + step, GateRoles = roles, DueAt = dueAt, EscalatedAt = escalatedAt, Link = "/engineering/platforms/1", CreateDate = DateTimeOffset.Now.AddDays(-20) });
                u.SaveChanges();
                id = t.Id;
            }, Seeder);
            return id;
        }

        private static void SetState(AppHost host, int id, AgentTaskState state) => host.Write(u => { u.Set<AgentTask>().Single(t => t.Id == id).State = state; u.SaveChanges(); }, Seeder);

        // ------------------------------------------------------------------------------------------ D3

        [TestMethod]
        public async Task An_Engineering_Gate_Counts_72_Office_Hours_Other_Modules_Keep_48_Calendar_Hours()
        {
            AppHost host = await TwoStandardsAsync("e1d3a");
            Product(host);
            DateTimeOffset before = DateTimeOffset.Now.AddSeconds(-5);
            await host.As(Pmo1, p => p.GetRequiredService<IAgentReconciler>().ReconcileAsync(null, Ct));
            DateTimeOffset after = DateTimeOffset.Now.AddSeconds(5);
            AgentTask task = host.Read(u => u.Set<AgentTask>().AsNoTracking().First(t => t.StepCode == "ENG.PLATFORM.STANDARD" && t.State < AgentTaskState.Done));
            Assert.IsNotNull(task.DueAt);
            Assert.IsTrue(task.DueAt >= OfficeClock.Add(before, 72) && task.DueAt <= OfficeClock.Add(after, 72), "72 office hours: " + task.DueAt);

            // the decision itself, per step: engineering steps under the rule, the two exempt steps and a step of another module
            await host.As(Pmo1, p =>
            {
                List<IAgentStep> steps = p.GetServices<IAgentStep>().ToList();
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (IAgentStep s in steps.Where(x => x.Code.StartsWith("ENG.", StringComparison.Ordinal) && x is IOfficeDeadlineStep o && o.OfficeDeadlineHours > 0))
                    Assert.AreEqual(OfficeClock.Add(now, 72), AgentDeadlines.Due(s, null, 48, now), s.Code);
                foreach (string code in new[] { "ENG.RISK.WATCH", "ENG.DEBT.SUPPLY" })
                    Assert.AreEqual(now.AddHours(48), AgentDeadlines.Due(steps.Single(x => x.Code == code), null, 48, now), code + " keeps the calendar rule");
                IAgentStep other = steps.First(x => !x.Code.StartsWith("ENG.", StringComparison.Ordinal));
                Assert.IsFalse(other is IOfficeDeadlineStep, other.Code);
                Assert.AreEqual(now.AddHours(48), AgentDeadlines.Due(other, null, 48, now));
                Assert.AreEqual(now.AddHours(5), AgentDeadlines.Due(other, new AgentGateInfo { DueHours = 5 }, 48, now));
                Assert.AreEqual(now.AddDays(9), AgentDeadlines.Due(steps.Single(x => x.Code == "ENG.PLATFORM.STANDARD"), new AgentGateInfo { DueAt = now.AddDays(9) }, 48, now), "an exact instant of the step wins");
                return Task.FromResult(true);
            });
            Assert.IsFalse(EnRisk.RuleStepCodes().Contains("ENG.RISK.WATCH"));
            Assert.IsFalse(EnRisk.RuleStepCodes().Contains("ENG.DEBT.SUPPLY"));
            Assert.IsTrue(EnRisk.RuleStepCodes().Contains("ENG.PLATFORM.STANDARD"));
        }

        [TestMethod]
        public async Task A_Waiting_Engineering_Task_Past_Its_Due_Is_A_Stall_Key_That_Goes_When_The_Task_Is_Done()
        {
            AppHost host = Started("e1d3b");
            int late = AddTask(host, "ENG.PLATFORM.STANDARD", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddHours(-1));
            int onTime = AddTask(host, "ENG.PLATFORM.STANDARD", AgentTaskState.WaitingHumanGate, DateTimeOffset.Now.AddDays(1));
            int watch = AddTask(host, "ENG.RISK.WATCH", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddDays(-9));
            int supply = AddTask(host, "ENG.DEBT.SUPPLY", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddDays(-9));
            int other = AddTask(host, "PROC.INQ.OPEN", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddDays(-9));
            int failedOld = AddTask(host, "ENG.PLATFORM.ASSESS", AgentTaskState.Failed, null, DateTimeOffset.Now.AddDays(-30));
            int failedFresh = AddTask(host, "ENG.PLATFORM.ASSESS", AgentTaskState.Failed, null, DateTimeOffset.Now.AddHours(-3));
            HashSet<string> keys = await host.As(Ceo, p => EnRisk.DesiredKeysAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct));
            CollectionAssert.AreEquivalent(new[] { "EN-STALL-" + late, "EN-STALL-" + failedOld }, keys.Where(k => k.StartsWith("EN-STALL-")).ToArray());
            SetState(host, late, AgentTaskState.Done);
            SetState(host, failedOld, AgentTaskState.Cancelled);
            keys = await host.As(Ceo, p => EnRisk.DesiredKeysAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct));
            Assert.AreEqual(0, keys.Count(k => k.StartsWith("EN-STALL-")), "a task that left the waiting or failed state closes the alarm");
            Assert.AreNotEqual(onTime + watch + supply + other + failedFresh, 0);
        }

        [TestMethod]
        public async Task The_Escalate_Step_Opens_And_Closes_The_Ceo_Item_For_A_Stalled_Task_And_Touches_Nothing()
        {
            AppHost host = Started("e1d3c");
            var seat = await SeatAsync(host);
            int id = AddTask(host, "ENG.PLATFORM.STANDARD", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddHours(-2));
            string key = "EN-STALL-" + id;
            AgentWorkItem item = (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single(i => i.TargetNumber == key);
            Assert.AreEqual("AgentTask", item.TargetType);
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.RISK.ESCALATE", item)).Outcome);
            Assert.IsTrue(Open(host, key));
            OfInboxItem inbox = host.Read(u => u.Set<OfInboxItem>().AsNoTracking().First(i => i.SourceKey == key));
            Assert.AreEqual("CEO", inbox.RecipientRole);
            Assert.AreEqual(OfUrgency.Urgent, inbox.Priority);
            StringAssert.Contains(inbox.Title, "بیش از ۷۲ ساعت اداری منتظر");
            StringAssert.Contains(inbox.Title, "تصمیم یا پیگیری مدیرعامل لازم است");
            Assert.AreEqual("/engineering/platforms/1", inbox.Link);
            Assert.AreEqual(0, (await Discover(host, seat, "ENG.RISK.ESCALATE")).Count(i => i.TargetNumber == key), "idempotent");
            // reading the item does not silence it
            host.Write(u => { foreach (OfInboxItem i in u.Set<OfInboxItem>().Where(x => x.SourceKey == key)) i.Status = OfInboxStatus.Done; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(1, (await Discover(host, seat, "ENG.RISK.ESCALATE")).Count(i => i.TargetNumber == key));
            await Run(host, seat, "ENG.RISK.ESCALATE", (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single(i => i.TargetNumber == key));
            Assert.IsTrue(Open(host, key));
            // the task is untouched by the alarm
            Assert.AreEqual(AgentTaskState.WaitingInput, host.Read(u => u.Set<AgentTask>().Single(t => t.Id == id).State));
            // the overview: the engineering manager and the CEO see it, an engineer does not
            EnOverviewDto o = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.StalledTasksCount);
            Assert.AreEqual(id, o.StalledTasks.Single().TaskId);
            Assert.IsFalse(string.IsNullOrEmpty(o.StalledTasks.Single().DueAtText));
            Assert.AreEqual(1, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).StalledTasksCount);
            Assert.AreEqual(0, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).StalledTasksCount);
            // closes only when the task left the state
            SetState(host, id, AgentTaskState.Done);
            AgentWorkItem close = (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single(i => i.TargetNumber == key);
            Assert.AreEqual(AgentStepOutcome.Done, (await Run(host, seat, "ENG.RISK.ESCALATE", close)).Outcome);
            Assert.IsFalse(Open(host, key));
        }

        [TestMethod]
        public async Task A_Failed_Agent_Task_Past_72_Office_Hours_Is_Worded_As_Failed()
        {
            AppHost host = Started("e1d3d");
            var seat = await SeatAsync(host);
            int id = AddTask(host, "ENG.PLATFORM.ASSESS", AgentTaskState.Failed, null, DateTimeOffset.Now.AddDays(-30));
            string key = "EN-STALL-" + id;
            await Run(host, seat, "ENG.RISK.ESCALATE", (await Discover(host, seat, "ENG.RISK.ESCALATE")).Single(i => i.TargetNumber == key));
            StringAssert.Contains(host.Read(u => u.Set<OfInboxItem>().AsNoTracking().First(i => i.SourceKey == key)).Title, "بیش از ۷۲ ساعت اداری ناموفق مانده است");
        }

        // ------------------------------------------------------------------------------------------ D5

        private static Task<Common.Results.ResponseDto<EnDossierDto>> NativeWith(AppHost host, System.Security.Claims.ClaimsPrincipal who, int product, Guid? file, string code = "API-611") =>
            host.As(who, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = code, StandardBasis = "دیتاشیت پروژهٔ اول، بند ۳", StandardAttachmentId = file }, Ct));

        [TestMethod]
        public async Task The_Native_Standard_Of_A_Platform_Needs_The_Data_Sheet_File_From_A_Person_And_The_Seat_Is_Still_Refused()
        {
            AppHost host = await TwoStandardsAsync("e1d5");
            int product = Product(host);
            var seat = await SeatAsync(host);
            Refused(await NativeWith(host, Eng1, product, null), "فایل دیتاشیت اصلی پلتفرم را بارگذاری کنید.");
            Refused(await NativeWith(host, Eng1, product, Guid.NewGuid()), "فایل");                           // not a stored file
            Refused(await NativeWith(host, Eng1, product, File(host, 2112)), "خود شما");                       // somebody else's file
            Refused(await NativeWith(host, seat, product, File(host, 2111)), "هوش مصنوعی");                      // the seat path is unchanged
            Assert.AreEqual(0, host.Read(u => u.Set<Entities.Model.NasbNiro.Engineering.EnPlatformDossier>().Count()));
            Guid file = File(host, 2111);
            EnDossierDto d = Ok(await NativeWith(host, Eng1, product, file));
            Assert.AreEqual(file, d.StandardAttachmentId);
            Assert.AreEqual(file, host.Read(u => u.Set<Entities.Model.NasbNiro.Engineering.EnPlatformDossier>().Single().StandardDocumentId));
            Assert.AreEqual(file, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, "API-611", Ct))).StandardAttachmentId);
        }

        // ------------------------------------------------------------------------------------------ D6

        private const string CsvHead = "docType,title,discipline,required,tender,units\n";
        private static byte[] CsvBytes(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray();
        private static async Task<byte[]> StoredAsync(AppHost host, Guid id)
        {
            var doc = await host.As(Eng1, p => p.GetRequiredService<Services.Contracts.IDocumentService>().DownloadDocumentAsync(id));
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await doc.FileStream.CopyToAsync(ms);
            return ms.ToArray();
        }

        [TestMethod]
        public async Task An_Uploaded_Csv_Is_Kept_As_Evidence_On_A_New_List_And_A_Later_Upload_Replaces_The_Pointer()
        {
            AppHost host = Started("e1d6");
            byte[] first = CsvBytes(CsvHead + "DWG,نقشه,,1,1,\nBOM,فهرست قطعات,,1,0,تولید\n");
            EnReferenceListDto made = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "VDDR-SRC", Title = "فهرست از فایل" }, first, Ct, "mdl-first.csv")));
            Assert.IsNotNull(made.SourceDocumentId);
            Assert.AreEqual("mdl-first.csv", made.SourceFileName);
            CollectionAssert.AreEqual(first, await StoredAsync(host, made.SourceDocumentId.Value), "the stored bytes equal the upload");
            Guid firstDoc = made.SourceDocumentId.Value;

            // saving through the JSON API does not clear the pointer
            EnReferenceListDto saved = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(new EnSaveReferenceDto { Id = made.Id, Code = made.Code, Title = "عنوان تازه", RowVersion = made.RowVersion, Lines = made.Lines }, Ct)));
            Assert.AreEqual(firstDoc, saved.SourceDocumentId);
            Assert.AreEqual("mdl-first.csv", saved.SourceFileName);

            // a later upload replaces the pointer; the old document is not deleted
            byte[] second = CsvBytes(CsvHead + "DWG,نقشه تازه,,1,1,\n");
            EnReferenceListDto again = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().UploadLinesAsync(saved.Id, saved.RowVersion, second, Ct, "mdl-second.csv")));
            Assert.AreNotEqual(firstDoc, again.SourceDocumentId);
            Assert.AreEqual("mdl-second.csv", again.SourceFileName);
            CollectionAssert.AreEqual(second, await StoredAsync(host, again.SourceDocumentId.Value));
            CollectionAssert.AreEqual(first, await StoredAsync(host, firstDoc), "the first file stays");
            Assert.AreEqual(1, again.Lines.Count);

            // a refused file leaves the pointer as it was
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().UploadLinesAsync(again.Id, again.RowVersion, CsvBytes("a,b\n1,2\n"), Ct, "bad.csv")), "سرستون");
            Assert.AreEqual(again.SourceDocumentId, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetListAsync(again.Id, Ct))).SourceDocumentId);
        }

        // ------------------------------------------------------------------------------------------ fix round
        [TestMethod]
        public async Task The_Escalate_Step_Is_Never_Under_The_Stall_Rule_Itself()
        {
            AppHost host = Started("e1dfh1");
            Assert.IsFalse(EnRisk.RuleStepCodes().Contains("ENG.RISK.ESCALATE"));
            int failed = AddTask(host, "ENG.RISK.ESCALATE", AgentTaskState.Failed, null, DateTimeOffset.Now.AddDays(-30));
            int fallback = AddTask(host, "ENG.RISK.ESCALATE", AgentTaskState.WaitingInput, DateTimeOffset.Now.AddDays(-30));
            HashSet<string> keys = await host.As(Ceo, p => EnRisk.DesiredKeysAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), Ct));
            Assert.AreEqual(0, keys.Count(k => k.StartsWith("EN-STALL-")), "tasks " + failed + "," + fallback);
        }
    }
}