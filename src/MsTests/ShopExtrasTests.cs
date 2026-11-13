using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>
    /// Production release P2 part C: the in-process checklist (rules, template, answers, the finish gate, the review by another person), the staff performance events (maker-checker, QC writes quality
    /// feedback only, an employee sees his own approved events, the summary) and the camera view (LAN addresses only, the snapshot fetched by the server with the login of the settings).
    /// </summary>
    [TestClass]
    public class ShopExtrasTests
    {
        private sealed class User : IInventoryUserContext
        {
            public int UserId { get; set; } = 1;
            public string Roles { get; set; } = "PRODUCTIONMANAGER";
            public bool IsInRole(string role) => (role ?? "").Split(',').Select(r => r.Trim()).Any(r => Roles.Split(',').Contains(r));
        }

        private static (ServiceProvider Provider, User User) Host(Dictionary<string, string> settings = null)
        {
            var user = new User();
            ServiceProvider provider = CeoOverviewServiceTests.InMemory(s =>
            {
                s.AddScoped<IInventoryUserContext>(_ => user);
                s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string>()).Build());
                s.AddScoped<IMfChecklistService, MfChecklistService>();
                s.AddScoped<IHrEventService, HrEventService>();
                s.AddScoped<IMfCameraService, MfCameraService>();
            });
            return (provider, user);
        }

        [TestMethod]
        public void A_Checklist_Passes_Only_With_Every_Mandatory_Item_Answered_And_Every_Answer_Within_Its_Rule()
        {
            var yes = new MfChecklistItem { Id = 1, Kind = MfChecklistItemKind.YesNo, Mandatory = true, IsActive = true };
            var dia = new MfChecklistItem { Id = 2, Kind = MfChecklistItemKind.Measure, Min = 49.98m, Max = 50.02m, Mandatory = true, IsActive = true };
            var note = new MfChecklistItem { Id = 3, Kind = MfChecklistItemKind.Text, Mandatory = false, IsActive = true };
            var items = new[] { yes, dia, note };
            Assert.IsTrue(MfChecklistRules.Passed(dia, null, 50.02m, null), "the limit itself is inside");
            Assert.IsFalse(MfChecklistRules.Passed(dia, null, 50.03m, null));
            Assert.IsTrue(MfChecklistRules.AllPassed(items, new[] { new MfChecklistAnswer { ItemId = 1, YesNo = true }, new MfChecklistAnswer { ItemId = 2, Value = 50m } }), "the optional note may stay empty");
            Assert.IsFalse(MfChecklistRules.AllPassed(items, new[] { new MfChecklistAnswer { ItemId = 1, YesNo = true } }), "the diameter is missing");
            Assert.IsFalse(MfChecklistRules.AllPassed(items, new[] { new MfChecklistAnswer { ItemId = 1, YesNo = false }, new MfChecklistAnswer { ItemId = 2, Value = 50m } }), "a no fails");
        }

        [TestMethod]
        public async Task The_Checklist_Is_Filled_At_The_Terminal_Gates_The_Finish_And_Is_Reviewed_By_Another_Person()
        {
            var (provider, user) = Host();
            using var _ = provider;
            using (var sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<OperationTemplate>().Add(new OperationTemplate { Id = 5, Title = "فرز کاری" });
                db.Set<MfOperation>().Add(new MfOperation { Id = 1, OrderId = 1, Sequence = 10, OperationTemplateId = 5, Title = "فرز کاری", Status = MfOperationStatus.InProgress });
                await db.SaveChangesAsync();
            }
            ChecklistTemplateDto template = new ChecklistTemplateDto
            {
                OperationTemplateId = 5, Title = "کنترل حین فرز", RequiredToFinish = true,
                Items = { new ChecklistItemDto { Text = "سطح بدون پلیسه", Kind = MfChecklistItemKind.YesNo }, new ChecklistItemDto { Text = "قطر سوراخ", Kind = MfChecklistItemKind.Measure, Nominal = 50, Min = 49.98m, Max = 50.02m, Unit = "mm" } },
            };
            using (var sc = provider.CreateScope())
            {
                user.Roles = "OPERATOR";
                Assert.IsFalse((await sc.ServiceProvider.GetRequiredService<IMfChecklistService>().SaveTemplateAsync(template, default)).Success, "an operator does not write templates");
                user.Roles = "QC"; user.UserId = 30;
                var saved = await sc.ServiceProvider.GetRequiredService<IMfChecklistService>().SaveTemplateAsync(template, default);
                Assert.IsTrue(saved.Success, saved.Message);
                Assert.AreEqual(2, saved.Data.Items.Count);
            }
            int yesId, diaId;
            using (var sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                MfOperation op = await db.Set<MfOperation>().AsNoTracking().FirstAsync();
                StringAssert.Contains(await MfChecklistGate.FinishProblemAsync(db, op, default), "کنترل حین فرز");
                user.Roles = "OPERATOR"; user.UserId = 13;
                var svc = sc.ServiceProvider.GetRequiredService<IMfChecklistService>();
                var c = (await svc.GetForOperationAsync(1, default)).Data;
                Assert.IsTrue(c.HasTemplate);
                CollectionAssert.AreEqual(new[] { "save", "submit" }, c.AllowedActions);
                yesId = c.Template.Items[0].Id.Value; diaId = c.Template.Items[1].Id.Value;
                // the diameter is out of tolerance: submitted, but the finish stays closed
                var r = await svc.SaveAnswersAsync(new SaveChecklistAnswersDto { OperationId = 1, Submit = true, Answers = { new ChecklistAnswerDto { ItemId = yesId, YesNo = true }, new ChecklistAnswerDto { ItemId = diaId, Value = 50.05m } } }, default);
                Assert.IsTrue(r.Success, r.Message);
                Assert.AreEqual(MfChecklistStatus.Submitted, r.Data.Status);
                Assert.IsFalse(r.Data.AllPassed);
                StringAssert.Contains(await MfChecklistGate.FinishProblemAsync(db, op, default), "عدم انطباق");
            }
            using (var sc = provider.CreateScope())
            {
                var svc = sc.ServiceProvider.GetRequiredService<IMfChecklistService>();
                user.Roles = "QC"; user.UserId = 13;
                Assert.IsFalse((await svc.ReviewAsync(new ChecklistReviewDto { OperationId = 1, Action = "reject", Note = "x" }, default)).Success, "the person who filled it does not review it");
                user.UserId = 30;
                Assert.IsFalse((await svc.ReviewAsync(new ChecklistReviewDto { OperationId = 1, Action = "approve" }, default)).Success, "a failing checklist is not approved");
                var rej = await svc.ReviewAsync(new ChecklistReviewDto { OperationId = 1, Action = "reject", Note = "قطر خارج از تلرانس؛ بازکاری" }, default);
                Assert.IsTrue(rej.Success, rej.Message);
                Assert.AreEqual(MfChecklistStatus.Rejected, rej.Data.Status);
            }
            using (var sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var svc = sc.ServiceProvider.GetRequiredService<IMfChecklistService>();
                user.Roles = "OPERATOR"; user.UserId = 13;
                var r = await svc.SaveAnswersAsync(new SaveChecklistAnswersDto { OperationId = 1, Submit = true, Answers = { new ChecklistAnswerDto { ItemId = diaId, Value = 50.01m } } }, default);
                Assert.IsTrue(r.Success && r.Data.AllPassed, r.Message);
                Assert.IsNull(await MfChecklistGate.FinishProblemAsync(db, await db.Set<MfOperation>().AsNoTracking().FirstAsync(), default), "submitted and passing: the finish is open");
                user.Roles = "SHOPSUPERVISOR"; user.UserId = 21;
                Assert.AreEqual(MfChecklistStatus.Approved, (await svc.ReviewAsync(new ChecklistReviewDto { OperationId = 1, Action = "approve" }, default)).Data.Status);
            }
        }

        [TestMethod]
        public async Task A_NonReader_Without_A_Personnel_File_Is_Told_So_And_Not_Refused_And_With_A_File_Gets_An_Answer()
        {
            var (provider, user) = Host();
            using var _ = provider;
            using (var sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<FnEmployee>().Add(new FnEmployee { Id = 7, PersonnelCode = "MF01", FirstName = "اپراتور", LastName = "آزمون", NationalCode = "0084575948", UserId = 13, IsActive = true });
                await db.SaveChangesAsync();
            }
            using var scope = provider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IHrEventService>();
            user.Roles = "SECURITY"; user.UserId = 99;
            var without = await svc.GetEventsAsync(new HrEventQuery { Mine = true }, default);
            Assert.IsFalse(without.Success);
            Assert.AreEqual(Services.NasbNiro.Production.MfShopFloorService.NoEmployee, without.Message);
            Assert.IsTrue(without.Message.Contains("پرونده پرسنلی"));
            Assert.AreNotEqual(Services.NasbNiro.Production.MfText.NotAllowed, without.Message);
            user.Roles = "OPERATOR"; user.UserId = 13;
            var with = await svc.GetEventsAsync(new HrEventQuery(), default);
            Assert.IsTrue(with.Success, with.Message);
            Assert.AreEqual(0, with.Data.Count);
        }

        [TestMethod]
        public async Task Performance_Events_Need_A_Second_Person_And_An_Employee_Sees_Only_His_Approved_Events()
        {
            var (provider, user) = Host();
            using var _ = provider;
            using (var sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<FnEmployee>().Add(new FnEmployee { Id = 7, PersonnelCode = "MF01", FirstName = "اپراتور", LastName = "آزمون", NationalCode = "0084575948", UserId = 13, IsActive = true });
                await db.SaveChangesAsync();
            }
            int id;
            using (var sc = provider.CreateScope())
            {
                var svc = sc.ServiceProvider.GetRequiredService<IHrEventService>();
                user.Roles = "QC"; user.UserId = 30;
                Assert.IsFalse((await svc.SaveAsync(new SaveHrEventDto { EmployeeId = 7, Kind = HrEventKind.Disciplinary, EventDate = DateTime.Today, Title = "x" }, default)).Success, "QC writes quality feedback only");
                user.Roles = "SHOPSUPERVISOR"; user.UserId = 21;
                Assert.IsFalse((await svc.SaveAsync(new SaveHrEventDto { EmployeeId = 7, Kind = HrEventKind.Evaluation, EventDate = DateTime.Today, Title = "ارزیابی ماهانه", Score = 6 }, default)).Success, "score 1..5");
                var e = await svc.SaveAsync(new SaveHrEventDto { EmployeeId = 7, Kind = HrEventKind.Evaluation, EventDate = DateTime.Today, Title = "ارزیابی ماهانه", Score = 4 }, default);
                Assert.IsTrue(e.Success, e.Message);
                id = e.Data.Id;
                Assert.IsTrue((await svc.ActionAsync(new HrEventActionDto { Id = id, Action = "submit" }, default)).Success);
            }
            using (var sc = provider.CreateScope())
            {
                var svc = sc.ServiceProvider.GetRequiredService<IHrEventService>();
                user.Roles = "PRODUCTIONMANAGER,SHOPSUPERVISOR"; user.UserId = 21;
                Assert.IsFalse((await svc.ActionAsync(new HrEventActionDto { Id = id, Action = "approve" }, default)).Success, "the writer never approves");
                user.Roles = "OPERATOR"; user.UserId = 13;
                Assert.AreEqual(0, (await svc.GetEventsAsync(new HrEventQuery(), default)).Data.Count, "not approved yet: the employee sees nothing");
                user.Roles = "HR"; user.UserId = 40;
                Assert.AreEqual(HrEventStatus.Approved, (await svc.ActionAsync(new HrEventActionDto { Id = id, Action = "approve" }, default)).Data.Status);
                user.Roles = "OPERATOR"; user.UserId = 13;
                var mine = (await svc.GetEventsAsync(new HrEventQuery(), default)).Data;
                Assert.AreEqual(1, mine.Count);
                Assert.AreEqual("MF01 اپراتور آزمون", mine[0].EmployeeName);
                user.Roles = "HR"; user.UserId = 40;
                var summary = (await svc.GetSummaryAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), default)).Data.Single();
                Assert.AreEqual(1, summary.Evaluations);
                Assert.AreEqual(4m, summary.AverageScore);
            }
        }

        [TestMethod]
        public void Only_Private_Lan_Addresses_Are_Accepted_For_A_Camera()
        {
            Assert.IsTrue(MfCameraService.LanUrl("http://192.168.10.64/ISAPI/Streaming/channels/101/picture"));
            Assert.IsTrue(MfCameraService.LanUrl("http://10.0.0.5:8080/snap.jpg"));
            Assert.IsTrue(MfCameraService.LanUrl("https://172.20.1.2/x"));
            Assert.IsFalse(MfCameraService.LanUrl("http://172.32.1.2/x"), "outside 172.16/12");
            Assert.IsFalse(MfCameraService.LanUrl("http://8.8.8.8/x"));
            Assert.IsFalse(MfCameraService.LanUrl("http://camera.example.com/x"), "a name could point anywhere");
            Assert.IsFalse(MfCameraService.LanUrl("ftp://192.168.1.2/x"));
            Assert.IsFalse(MfCameraService.LanUrl("http://admin:pw@192.168.1.2/x"), "no login inside the address");
        }

        [TestMethod]
        public async Task The_Server_Fetches_The_Snapshot_Of_A_Camera_With_The_Login_Of_The_Settings()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var http = new HttpListener();
            http.Prefixes.Add($"http://127.0.0.1:{port}/");
            http.AuthenticationSchemes = AuthenticationSchemes.Basic;
            http.Start();
            byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 };
            Task server = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    HttpListenerContext ctx = await http.GetContextAsync();
                    var id = ctx.User?.Identity as HttpListenerBasicIdentity;
                    if (id == null || id.Name != "viewer" || id.Password != "cam-secret") { ctx.Response.StatusCode = 401; ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"cam\""); ctx.Response.Close(); continue; }
                    ctx.Response.ContentType = "image/jpeg";
                    await ctx.Response.OutputStream.WriteAsync(jpeg);
                    ctx.Response.Close();
                    return;
                }
            });
            try
            {
                var (provider, user) = Host(new Dictionary<string, string> { ["Dnc:Credentials:cam1:User"] = "viewer", ["Dnc:Credentials:cam1:Password"] = "cam-secret" });
                using var _ = provider;
                using var sc = provider.CreateScope();
                var svc = sc.ServiceProvider.GetRequiredService<IMfCameraService>();
                user.Roles = "PRODUCTIONMANAGER";
                Assert.IsFalse((await svc.SaveAsync(new CameraDto { Title = "سالن", SnapshotUrl = "http://8.8.8.8/snap.jpg" }, default)).Success);
                var cam = await svc.SaveAsync(new CameraDto { Title = "سالن ماشینکاری", SnapshotUrl = $"http://127.0.0.1:{port}/snap.jpg", CredentialKey = "cam1" }, default);
                Assert.IsTrue(cam.Success, cam.Message);
                user.Roles = "OPERATOR";
                Assert.IsFalse((await svc.SnapshotAsync(cam.Data.Id.Value, default)).Success, "operators are not camera viewers");
                user.Roles = "SHOPSUPERVISOR";
                Assert.IsNull((await svc.GetCamerasAsync(default)).Data.Single().SnapshotUrl, "the address is shown to the editors only");
                var pic = await svc.SnapshotAsync(cam.Data.Id.Value, default);
                Assert.IsTrue(pic.Success, pic.Message);
                Assert.AreEqual("image/jpeg", pic.Data.ContentType);
                CollectionAssert.AreEqual(jpeg, pic.Data.Bytes);
                await server;
            }
            finally { http.Stop(); }
        }
    }
}
