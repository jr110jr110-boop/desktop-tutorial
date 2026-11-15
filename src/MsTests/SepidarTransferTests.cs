using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Finance.Sp;
using Microsoft.Extensions.Configuration;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>
    /// Sepidar transfer per document (station 13): kinds that are all off until the owner enables them, the queue of prepared items, the human approval (never the maker, never a seat), the pluggable sender
    /// (none, a file drop, a fake for the tests) with the exact backoff, and the privacy of small payroll totals.
    /// </summary>
    [TestClass]
    public class SepidarTransferTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(1604, "accountant", "FINANCIAL");
        private static readonly ClaimsPrincipal FinanceManager = AppHost.Principal(1605, "finance.manager", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal FinanceManager2 = AppHost.Principal(1609, "finance.manager.two", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(1610, "ceo", "CEO");
        private static readonly ClaimsPrincipal Auditor = AppHost.Principal(1611, "auditor", "AUDITOR");

        /// <summary>A sender for the tests: it records what it was given and answers what the test says.</summary>
        private sealed class FakeSender : ISepidarSender
        {
            public readonly List<SepidarEnvelope> Sent = new List<SepidarEnvelope>();
            public bool Fail;
            public bool Throw;
            public string Name => "Fake";
            public bool IsConfigured => true;

            public Task<SpSendResult> SendAsync(SepidarEnvelope envelope, CancellationToken ct)
            {
                Sent.Add(envelope);
                if (Throw) throw new InvalidOperationException("secret-host.example failed at C:\\secret\\path");
                return Task.FromResult(Fail ? SpSendResult.Fail("پاسخ نادرست") : SpSendResult.Ok("EXT-" + envelope.ItemId));
            }
        }

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
            public FakeSender Fake;
            public int FiscalYearId;
        }

        private static async Task<Env> NewEnv(string name, bool fake = false, int? maxAttempts = null)
        {
            FieldInfo done = typeof(Services.NasbNiro.Finance.FinanceSeed).GetField("_done", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(done, "FinanceSeed._done was renamed: update the test support");
            done.SetValue(null, false);
            var fakeSender = fake ? new FakeSender() : null;
            var settings = new Dictionary<string, string>();
            if (maxAttempts.HasValue) settings["Sepidar:MaxAttempts"] = maxAttempts.Value.ToString();
            AppHost host = AppHost.WithServices("Sp_" + name + "_" + Guid.NewGuid().ToString("N"), s => { if (fakeSender != null) s.AddScoped<ISepidarSender>(sp => fakeSender); }, settings);
            var e = new Env { Host = host, Fake = fakeSender, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                foreach ((int id, string n) in new[] { (1604, "accountant"), (1605, "finance.manager"), (1609, "finance.manager.two"), (1610, "ceo"), (1611, "auditor") })
                    AppHost.Add(u, new User { Id = id, UserName = n, Email = n + "@example.com", IsActive = true });
                u.SaveChanges();
                FnFiscalYear fy = AppHost.Add(u, new FnFiscalYear { Year = 1405, Title = "1405", StartDate = new DateTime(2026, 3, 21), EndDate = new DateTime(2027, 3, 20) });
                u.SaveChanges();
                e.FiscalYearId = fy.Id;
            }, Seeder);
            return e;
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-FIN"); }
            await Run(e, "AG-MASTER");
        }

        /// <summary>Runs a service call as the finance agent seat (the way the steps do).</summary>
        private static Task<T> AsSeat<T>(Env e, Func<ISpOutboxService, Task<T>> call) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-FIN"], "AG-FIN", sp => call(sp.GetRequiredService<ISpOutboxService>()));

        private static Task<T> As<T>(Env e, ClaimsPrincipal who, Func<ISpOutboxService, Task<T>> call) => e.Host.As(who, sp => call(sp.GetRequiredService<ISpOutboxService>()));

        private static async Task Enable(Env e, string code, bool on = true)
        {
            ResponseDto<SpTransferTypeDto> r = await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = code, IsEnabled = on, FromDate = on ? DateTime.Today : (DateTime?)null }, Ct));
            Assert.IsTrue(r.Success, r.Message);
        }

        private static async Task<SpPrepareResult> Prepare(Env e)
        {
            ResponseDto<SpPrepareResult> r = await AsSeat(e, s => s.PrepareAsync(Ct));
            Assert.IsTrue(r.Success, r.Message);
            return r.Data;
        }

        private static List<SpOutboxItem> Items(Env e) => e.Host.Read(u => u.Set<SpOutboxItem>().AsNoTracking().OrderBy(i => i.Id).ToList());
        private static SpOutboxItem Item(Env e, int id) => Items(e).Single(i => i.Id == id);
        private static List<SpOutboxAttempt> Attempts(Env e, int itemId) => e.Host.Read(u => u.Set<SpOutboxAttempt>().AsNoTracking().Where(a => a.ItemId == itemId).OrderBy(a => a.AttemptNumber).ToList());
        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        private static int Voucher(Env e, int number, int createdBy = 1604, FnVoucherStatus status = FnVoucherStatus.Final, bool imported = false, string description = "سند آزمایشی")
        {
            int id = 0;
            e.Host.Write(u =>
            {
                FnVoucher v = AppHost.Add(u, new FnVoucher
                {
                    FiscalYearId = e.FiscalYearId, Number = status == FnVoucherStatus.Final ? number : (int?)null, TemporaryNumber = number, Date = DateTime.Today, Status = status, Description = description, TotalAmount = 1_000_000m,
                    IsImported = imported, CreatedByUserId = createdBy, CreatedAt = DateTimeOffset.Now, FinalizedAt = status == FnVoucherStatus.Final ? DateTimeOffset.Now : (DateTimeOffset?)null,
                });
                u.SaveChanges();
                id = v.Id;
            }, Seeder);
            return id;
        }

        /// <summary>The in-memory database has no row versions: a test changes the document and moves its version by hand (as the real database does on every save).</summary>
        private static void ChangeVoucher(Env e, int id, string description, byte version)
        {
            e.Host.Write(u =>
            {
                FnVoucher v = u.Set<FnVoucher>().Single(x => x.Id == id);
                v.Description = description; v.RowVersion = new byte[] { version, 0, 0, 0, 0, 0, 0, 1 };
                u.SaveChanges();
            }, Seeder);
        }

        private static async Task<SpOutboxItem> Prepared(Env e, int voucher)
        {
            await Prepare(e);
            return Items(e).Single(i => i.SourceId == voucher && i.Status == SpOutboxStatus.AwaitingApproval);
        }

        private static void StampVersion(Env e, int itemId, byte version) =>
            e.Host.Write(u => { u.Set<SpOutboxItem>().Single(i => i.Id == itemId).RowVersion = new byte[] { version, 1, 1, 1, 1, 1, 1, 1 }; u.SaveChanges(); }, Seeder);

        private static string V(byte version) => Convert.ToBase64String(new byte[] { version, 1, 1, 1, 1, 1, 1, 1 });

        private static Task<ResponseDto<SpOutboxItemDto>> Approve(Env e, ClaimsPrincipal who, int id, string rowVersion = null) =>
            As(e, who, s => s.DecideAsync(new SpDecideDto { Id = id, Action = "approve", RowVersion = rowVersion }, Ct));

        // ------------------------------------------------------------------------------------------------ the kinds

        [TestMethod]
        public async Task The_Six_Kinds_Exist_And_Are_All_Off_And_The_Switch_Is_Audited_And_Needs_The_Finance_Manager_Or_The_Ceo()
        {
            Env e = await NewEnv("types");
            List<SpTransferTypeDto> types = (await As(e, Auditor, s => s.GetTypesAsync(Ct))).Data;
            CollectionAssert.AreEquivalent(new[] { "VOUCHER", "SALES_INVOICE", "PURCHASE_INVOICE", "RECEIPT", "PAYMENT", "PAYROLL_SUMMARY" }, types.Select(t => t.Code).ToArray());
            Assert.IsTrue(types.All(t => !t.IsEnabled && t.RequiresApproval), "every kind is off and needs approval by default");

            Assert.IsFalse((await As(e, Accountant, s => s.GetTypesAsync(Ct))).Success, "an accountant does not read the kinds");
            Assert.IsFalse((await As(e, Accountant, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success, "an accountant does not switch");
            Assert.IsFalse((await As(e, Auditor, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success, "the auditor reads only");
            Assert.IsFalse((await AsSeat(e, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success, "a seat does not switch");

            ResponseDto<SpTransferTypeDto> off = await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today, RequiresApproval = false }, Ct));
            Assert.IsFalse(off.Success, "the approval cannot be switched off");
            StringAssert.Contains(off.Message, "الزامی");
            Assert.IsFalse((await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "NOPE", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success);

            Assert.IsTrue((await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Data.IsEnabled);
            Assert.IsTrue((await As(e, Ceo, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "RECEIPT", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Data.IsEnabled, "the CEO may switch too");
            Assert.IsTrue((await As(e, Ceo, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "RECEIPT", IsEnabled = false }, Ct))).Success);

            List<FnAuditLog> audit = e.Host.Read(u => u.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == nameof(SpTransferType) && a.Action == "SepidarTypeSwitch").OrderBy(a => a.Id).ToList());
            Assert.AreEqual(3, audit.Count, "every real switch is audited (the refused ones are not)");
            CollectionAssert.AreEqual(new[] { 1605, 1610, 1610 }, audit.Select(a => a.UserId).ToArray());
            // a switch that changes nothing is no audit line
            await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct));
            Assert.AreEqual(3, e.Host.Read(u => u.Set<FnAuditLog>().Count(a => a.EntityType == nameof(SpTransferType) && a.Action == "SepidarTypeSwitch")));
        }

        // ------------------------------------------------------------------------------------------------ preparing

        [TestMethod]
        public async Task A_Disabled_Kind_Is_Never_Queued_And_Only_Final_Documents_Are_Prepared()
        {
            Env e = await NewEnv("final");
            int final = Voucher(e, 1);
            int draft = Voucher(e, 2, status: FnVoucherStatus.Draft);
            int imported = Voucher(e, 3, imported: true);

            SpPrepareResult off = await Prepare(e);
            Assert.AreEqual(0, off.Prepared);
            Assert.AreEqual(0, Items(e).Count, "nothing is queued for a kind that is off");

            await Enable(e, "VOUCHER");
            SpPrepareResult on = await Prepare(e);
            Assert.AreEqual(1, on.Prepared, on.Describe());
            List<SpOutboxItem> items = Items(e);
            Assert.AreEqual(1, items.Count, "the draft and the imported voucher are not transferable");
            Assert.AreEqual(final, items[0].SourceId);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, items[0].Status);
            Assert.AreEqual("AG-FIN", items[0].PreparedByAgent);
            Assert.AreEqual(e.Seats["AG-FIN"].UserId, items[0].PreparedByUserId);
            Assert.IsFalse(items[0].IsCorrection);
            StringAssert.Contains(items[0].PayloadJson, "\"schemaVersion\":1");
            Assert.AreEqual(64, items[0].PayloadHash.Length);
            CollectionAssert.DoesNotContain(items.Select(i => i.SourceId).ToList(), draft);
            CollectionAssert.DoesNotContain(items.Select(i => i.SourceId).ToList(), imported);

            SpPrepareResult again = await Prepare(e);
            Assert.AreEqual(0, again.Prepared, "idempotent: the same document is not prepared twice");
            Assert.AreEqual(1, Items(e).Count);

            // a kind that is switched off again stops the queue at once (items already there stay, nothing new comes)
            await Enable(e, "VOUCHER", false);
            Voucher(e, 4);
            Assert.AreEqual(0, (await Prepare(e)).Prepared);
            Assert.AreEqual(1, Items(e).Count);
        }

        [TestMethod]
        public async Task A_Change_Before_Sending_Supersedes_The_Item_And_A_Change_After_Sending_Makes_A_Correction()
        {
            Env e = await NewEnv("change", fake: true);
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1);
            SpOutboxItem first = await Prepared(e, voucher);

            // the source changes before anyone approved: the old item is replaced, there is still exactly one live item
            ChangeVoucher(e, voucher, "سند اصلاح‌شده", 2);
            SpPrepareResult second = await Prepare(e);
            Assert.AreEqual(1, second.Prepared); Assert.AreEqual(1, second.Superseded); Assert.AreEqual(0, second.Corrections);
            List<SpOutboxItem> items = Items(e);
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(SpOutboxStatus.Superseded, items[0].Status);
            Assert.AreEqual(items[1].Id, items[0].SupersededByItemId);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, items[1].Status);
            Assert.AreNotEqual(first.PayloadHash, items[1].PayloadHash);
            Assert.AreEqual(1, items.Count(i => i.Status != SpOutboxStatus.Superseded), "one live item per document");

            // approve and send: the item is Sent
            Assert.IsTrue((await Approve(e, FinanceManager, items[1].Id)).Success);
            Assert.AreEqual(SpSendOutcome.Sent, (await AsSeat(e, s => s.SendAsync(items[1].Id, Ct))).Data);
            SpOutboxItem sent = Item(e, items[1].Id);
            Assert.AreEqual(SpOutboxStatus.Sent, sent.Status);
            Assert.AreEqual("EXT-" + sent.Id, sent.ExternalReference);
            Assert.IsNotNull(sent.SentAt);

            // the source changes after it was sent: the sent item is never altered, a new item is a correction and says so
            ChangeVoucher(e, voucher, "اصلاح پس از ارسال", 3);
            SpPrepareResult third = await Prepare(e);
            Assert.AreEqual(1, third.Prepared); Assert.AreEqual(1, third.Corrections); Assert.AreEqual(0, third.Superseded);
            items = Items(e);
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(SpOutboxStatus.Sent, items[1].Status, "a sent item is never altered");
            Assert.AreEqual(sent.PayloadHash, items[1].PayloadHash);
            SpOutboxItem correction = items[2];
            Assert.IsTrue(correction.IsCorrection);
            Assert.AreEqual(sent.Id, correction.CorrectsItemId);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, correction.Status);
            StringAssert.Contains(correction.Title, "اصلاحیه");

            // the gate text of the task says that it is a correction
            await Run(e, "AG-MASTER");
            AgentTask gate = Open(e, "SP.OUTBOX.APPROVE").Single(t => t.TargetId == correction.Id);
            StringAssert.Contains(gate.Summary, "اصلاحیه");

            // the correction carries what it corrects in its envelope
            Assert.IsTrue((await Approve(e, FinanceManager2, correction.Id)).Success);
            await AsSeat(e, s => s.SendAsync(correction.Id, Ct));
            Assert.AreEqual("EXT-" + sent.Id, e.Fake.Sent.Last().CorrectsExternalReference);
            Assert.IsTrue(e.Fake.Sent.Last().IsCorrection);
        }

        // ------------------------------------------------------------------------------------------------ the decisions

        [TestMethod]
        public async Task The_Maker_The_Seat_And_Other_Roles_Cannot_Approve_And_A_Reject_Needs_A_Reason()
        {
            Env e = await NewEnv("maker");
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1, createdBy: 1605);
            SpOutboxItem item = await Prepared(e, voucher);
            StringAssert.Contains(item.SourceMakerUserIds, ",1605,");

            ResponseDto<SpOutboxItemDto> self = await Approve(e, FinanceManager, item.Id);
            Assert.IsFalse(self.Success, "the maker of the source document does not approve");
            StringAssert.Contains(self.Message, "ایجادکننده");
            Assert.IsFalse((await Approve(e, Accountant, item.Id)).Success, "only the finance manager or the CEO approves");
            Assert.IsTrue((await As(e, Ceo, s => s.GetItemAsync(item.Id, Ct))).Data.CanDecide, "the CEO is the second decider (he is not the maker here)");
            Assert.IsFalse((await Approve(e, Auditor, item.Id)).Success);
            ResponseDto<SpOutboxItemDto> seat = await AsSeat(e, s => s.DecideAsync(new SpDecideDto { Id = item.Id, Action = "approve" }, Ct));
            Assert.IsFalse(seat.Success, "an agent seat never approves");
            StringAssert.Contains(seat.Message, "انسان");
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, item.Id).Status);

            // the gate shows the viewer why the buttons are off
            SpOutboxItemDto asMaker = (await As(e, FinanceManager, s => s.GetItemAsync(item.Id, Ct))).Data;
            Assert.IsFalse(asMaker.CanDecide);
            StringAssert.Contains(asMaker.CannotDecideReason, "ایجادکننده");
            Assert.IsTrue((await As(e, FinanceManager2, s => s.GetItemAsync(item.Id, Ct))).Data.CanDecide);

            Assert.IsFalse((await As(e, FinanceManager2, s => s.DecideAsync(new SpDecideDto { Id = item.Id, Action = "reject" }, Ct))).Success, "a reject needs a reason");
            ResponseDto<SpOutboxItemDto> rejected = await As(e, FinanceManager2, s => s.DecideAsync(new SpDecideDto { Id = item.Id, Action = "reject", Reason = "مبلغ نادرست" }, Ct));
            Assert.IsTrue(rejected.Success, rejected.Message);
            SpOutboxItem row = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Rejected, row.Status);
            Assert.AreEqual(1609, row.RejectedByUserId);
            Assert.AreEqual("مبلغ نادرست", row.RejectReason);
            Assert.IsNull(row.ApprovedByUserId);

            // a rejected item is not prepared again until the document changes
            Assert.AreEqual(0, (await Prepare(e)).Prepared);
        }

        [TestMethod]
        public async Task A_Stale_Or_Missing_Row_Version_Is_Refused()
        {
            Env e = await NewEnv("rowversion");
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            StampVersion(e, item.Id, 5);

            ResponseDto<SpOutboxItemDto> missing = await Approve(e, FinanceManager, item.Id);
            Assert.IsFalse(missing.Success); StringAssert.Contains(missing.Message, "نسخه");
            ResponseDto<SpOutboxItemDto> stale = await Approve(e, FinanceManager, item.Id, V(9));
            Assert.IsFalse(stale.Success); StringAssert.Contains(stale.Message, "همزمان");
            Assert.IsFalse((await Approve(e, FinanceManager, item.Id, "###")).Success, "garbage is refused, not a 500");
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, item.Id).Status);

            ResponseDto<SpOutboxItemDto> ok = await Approve(e, FinanceManager, item.Id, V(5));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(SpOutboxStatus.Approved, Item(e, item.Id).Status);
            Assert.AreEqual(1605, Item(e, item.Id).ApprovedByUserId);
        }

        [TestMethod]
        public async Task Bulk_Approve_Checks_Each_Row_And_Reports_Exact_Counts()
        {
            Env e = await NewEnv("bulk");
            await Enable(e, "VOUCHER");
            int v1 = Voucher(e, 1), v2 = Voucher(e, 2), v3 = Voucher(e, 3, createdBy: 1609), v4 = Voucher(e, 4);
            await Prepare(e);
            Dictionary<int, SpOutboxItem> byVoucher = Items(e).ToDictionary(i => i.SourceId);
            StampVersion(e, byVoucher[v2].Id, 7);

            var dto = new SpBulkApproveDto
            {
                Items = new List<SpBulkRowDto>
                {
                    new SpBulkRowDto { Id = byVoucher[v1].Id },                          // ok
                    new SpBulkRowDto { Id = byVoucher[v2].Id, RowVersion = V(1) },       // stale
                    new SpBulkRowDto { Id = byVoucher[v3].Id },                          // the approver made this voucher
                    new SpBulkRowDto { Id = byVoucher[v1].Id },                          // twice in the request
                    new SpBulkRowDto { Id = 987654 },                                    // unknown
                },
            };
            ResponseDto<SpBulkResultDto> r = await As(e, FinanceManager2, s => s.BulkApproveAsync(dto, Ct));
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(5, r.Data.Requested);
            Assert.AreEqual(1, r.Data.Approved);
            Assert.AreEqual(4, r.Data.Refused);
            Assert.AreEqual(5, r.Data.Rows.Count);
            CollectionAssert.AreEqual(new[] { true, false, false, false, false }, r.Data.Rows.Select(x => x.Approved).ToArray());
            // each row is atomic: only the first changed, the refused ones are untouched
            Assert.AreEqual(SpOutboxStatus.Approved, Item(e, byVoucher[v1].Id).Status);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, byVoucher[v2].Id).Status);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, byVoucher[v3].Id).Status);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, byVoucher[v4].Id).Status);

            Assert.IsFalse((await As(e, FinanceManager2, s => s.BulkApproveAsync(new SpBulkApproveDto(), Ct))).Success);
            Assert.IsFalse((await As(e, Accountant, s => s.BulkApproveAsync(dto, Ct))).Success);
            Assert.IsFalse((await AsSeat(e, s => s.BulkApproveAsync(dto, Ct))).Success, "a seat never approves in bulk either");
        }

        [TestMethod]
        public async Task A_Source_That_Changed_After_Preparing_Cannot_Be_Approved()
        {
            Env e = await NewEnv("staleapprove");
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1);
            SpOutboxItem item = await Prepared(e, voucher);
            ChangeVoucher(e, voucher, "تغییر پیش از تایید", 2);
            ResponseDto<SpOutboxItemDto> r = await Approve(e, FinanceManager, item.Id);
            Assert.IsFalse(r.Success);
            StringAssert.Contains(r.Message, "تغییر کرده");
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, item.Id).Status);
        }

        // ------------------------------------------------------------------------------------------------ the sender

        [TestMethod]
        public async Task With_No_Sender_An_Approved_Item_Waits_With_No_Attempt_And_No_Error()
        {
            Env e = await NewEnv("none");
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1);
            SpOutboxItem item = await Prepared(e, voucher);
            Assert.IsTrue((await Approve(e, FinanceManager, item.Id)).Success);

            Assert.AreEqual(SpSendOutcome.NoSender, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
            await Settle(e);
            await Settle(e);
            SpOutboxItem row = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Approved, row.Status);
            Assert.AreEqual(0, row.AttemptCount);
            Assert.AreEqual(0, Attempts(e, item.Id).Count);
            Assert.IsNull(row.LastError);
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), "no run reports an error: " + Dump(e));
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "SP.OUTBOX.SEND"), "the send step finds nothing while there is no sender: " + Dump(e));

            SpOutboxSummaryDto summary = (await As(e, Auditor, s => s.GetSummaryAsync(Ct))).Data;
            Assert.AreEqual("None", summary.SenderName);
            Assert.IsFalse(summary.IsConfigured);
            Assert.IsNull(summary.LastSuccessfulSendAt);
            Assert.AreEqual(1, summary.Counts.Single(c => c.Status == SpOutboxStatus.Approved).Count);
            Assert.AreEqual(1, summary.Total);
        }

        [TestMethod]
        public async Task The_Steps_Prepare_Gate_And_Send_An_Item_End_To_End_With_A_Sender()
        {
            Env e = await NewEnv("e2e", fake: true);
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1);
            await Settle(e);
            SpOutboxItem item = Items(e).Single(i => i.SourceId == voucher);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, item.Status);
            AgentTask gate = Open(e, "SP.OUTBOX.APPROVE").Single();
            Assert.AreEqual(item.Id, gate.TargetId);
            Assert.AreEqual("FINANCEMANAGER,CEO", gate.GateRoles);
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual("/fn/sepidar-transfer?item=" + item.Id, gate.Link);

            Assert.IsTrue((await Approve(e, FinanceManager2, item.Id)).Success);
            await Settle(e);
            SpOutboxItem sent = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Sent, sent.Status, Dump(e));
            Assert.AreEqual(1, e.Fake.Sent.Count);
            Assert.AreEqual(item.Id + ":" + item.PayloadHash, e.Fake.Sent[0].IdempotencyKey, "the key is the item id and the hash");
            Assert.AreEqual(0, Open(e, "SP.OUTBOX.APPROVE").Count, "the gate closed with the decision");
            Assert.AreEqual(0, Tasks(e).Count(t => t.State == AgentTaskState.Failed), Dump(e));
            SpOutboxAttempt attempt = Attempts(e, item.Id).Single();
            Assert.AreEqual(SpAttemptOutcome.Succeeded, attempt.Outcome);
            Assert.AreEqual("Fake", attempt.SenderName);
        }

        [TestMethod]
        public async Task A_Source_That_Changed_After_Approval_Is_Never_Sent()
        {
            Env e = await NewEnv("hashmismatch", fake: true);
            await Enable(e, "VOUCHER");
            int voucher = Voucher(e, 1);
            SpOutboxItem item = await Prepared(e, voucher);
            Assert.IsTrue((await Approve(e, FinanceManager, item.Id)).Success);
            ChangeVoucher(e, voucher, "تغییر پس از تایید", 2);

            Assert.AreEqual(SpSendOutcome.Superseded, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
            Assert.AreEqual(0, e.Fake.Sent.Count, "nothing is handed over on a hash mismatch");
            Assert.AreEqual(SpOutboxStatus.Superseded, Item(e, item.Id).Status);
            Assert.AreEqual(0, Attempts(e, item.Id).Count);
            SpPrepareResult next = await Prepare(e);
            Assert.AreEqual(1, next.Prepared, "the agent prepares the new item, a person approves it");
        }

        [TestMethod]
        public void The_Backoff_Is_5_Minutes_30_Minutes_2_Hours_12_Hours_Then_A_Person()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(5), SpFacts.NextDelay(1, 5));
            Assert.AreEqual(TimeSpan.FromMinutes(30), SpFacts.NextDelay(2, 5));
            Assert.AreEqual(TimeSpan.FromHours(2), SpFacts.NextDelay(3, 5));
            Assert.AreEqual(TimeSpan.FromHours(12), SpFacts.NextDelay(4, 5));
            Assert.IsNull(SpFacts.NextDelay(5, 5), "the 5th failure is the last automatic one");
            Assert.AreEqual(TimeSpan.FromMinutes(5), SpFacts.NextDelay(1, 2));
            Assert.IsNull(SpFacts.NextDelay(2, 2));
            Assert.AreEqual(5, SepidarConfigKeys.MaxAttemptsOf(null), "the default is 5");
            Assert.AreEqual("SpSend4", SpFacts.SendTargetType(4), "one target type per attempt, the target id stays the item id");
            Assert.AreEqual("SpSend99", SpFacts.SendTargetType(5000), "the discriminator is capped, never an overflow");
        }

        [TestMethod]
        public async Task A_Failing_Sender_Backs_Off_Exactly_Then_Stays_Failed_And_A_Person_Retries_Or_Cancels()
        {
            Env e = await NewEnv("backoff", fake: true);
            e.Fake.Fail = true;
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            Assert.IsTrue((await Approve(e, FinanceManager, item.Id)).Success);

            TimeSpan[] expected = { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12) };
            for (int n = 1; n <= 4; n++)
            {
                Assert.AreEqual(SpSendOutcome.Failed, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
                SpOutboxItem row = Item(e, item.Id);
                SpOutboxAttempt last = Attempts(e, item.Id).Last();
                Assert.AreEqual(n, row.AttemptCount);
                Assert.AreEqual(SpOutboxStatus.Failed, row.Status);
                Assert.AreEqual(expected[n - 1], row.NextAttemptAt.Value - last.FinishedAt.Value, "wait after failure " + n);
                // not due yet: an early run does nothing
                Assert.AreEqual(SpSendOutcome.NotDue, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
                Assert.AreEqual(n, Item(e, item.Id).AttemptCount);
                // time passes
                e.Host.Write(u => { u.Set<SpOutboxItem>().Single(i => i.Id == item.Id).NextAttemptAt = DateTimeOffset.Now.AddSeconds(-1); u.SaveChanges(); }, Seeder);
            }
            Assert.AreEqual(SpSendOutcome.Failed, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
            SpOutboxItem failed = Item(e, item.Id);
            Assert.AreEqual(5, failed.AttemptCount);
            Assert.AreEqual(SpOutboxStatus.Failed, failed.Status);
            Assert.IsNull(failed.NextAttemptAt, "after the last automatic attempt it stays failed");
            Assert.AreEqual(5, Attempts(e, item.Id).Count);
            Assert.IsTrue(Attempts(e, item.Id).All(a => a.Outcome == SpAttemptOutcome.Failed && a.Error == "پاسخ نادرست"));
            Assert.AreEqual(SpSendOutcome.NotDue, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data, "no sixth automatic attempt");

            // the FIX task waits for the finance manager
            await Settle(e);
            AgentTask fix = Open(e, "SP.OUTBOX.FIX").Single();
            Assert.AreEqual(item.Id, fix.TargetId);
            Assert.AreEqual("FINANCEMANAGER,CEO", fix.GateRoles);
            Assert.AreEqual("/fn/sepidar-transfer?item=" + item.Id, fix.Link);

            // an accountant, a seat and a cancel without a reason are refused
            Assert.IsFalse((await As(e, Accountant, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "retry" }, Ct))).Success);
            Assert.IsFalse((await AsSeat(e, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "retry" }, Ct))).Success);
            Assert.IsFalse((await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "cancel" }, Ct))).Success);

            // retry: back to approved with a full set of attempts again
            e.Fake.Fail = false;
            Assert.IsTrue((await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "retry" }, Ct))).Success);
            SpOutboxItem retried = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Approved, retried.Status);
            Assert.AreEqual(5, retried.RetryBaseline);
            Assert.AreEqual(SpSendOutcome.Sent, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
            Assert.AreEqual(6, Item(e, item.Id).AttemptCount);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SP.OUTBOX.FIX").Count, "the fix task closed");

            // a sent item is neither retried nor cancelled
            Assert.IsFalse((await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "cancel", Reason = "x" }, Ct))).Success);
        }

        [TestMethod]
        public async Task The_Maximum_Of_Attempts_Comes_From_The_Configuration_And_A_Cancel_Needs_A_Reason()
        {
            Env e = await NewEnv("maxattempts", fake: true, maxAttempts: 2);
            e.Fake.Fail = true;
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            await Approve(e, FinanceManager, item.Id);
            await AsSeat(e, s => s.SendAsync(item.Id, Ct));
            Assert.IsNotNull(Item(e, item.Id).NextAttemptAt);
            e.Host.Write(u => { u.Set<SpOutboxItem>().Single(i => i.Id == item.Id).NextAttemptAt = DateTimeOffset.Now.AddSeconds(-1); u.SaveChanges(); }, Seeder);
            await AsSeat(e, s => s.SendAsync(item.Id, Ct));
            SpOutboxItem row = Item(e, item.Id);
            Assert.AreEqual(2, row.AttemptCount);
            Assert.IsNull(row.NextAttemptAt, "the second failure is the last one when the maximum is 2");

            ResponseDto<SpOutboxItemDto> cancelled = await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "cancel", Reason = "سند منبع باطل شد" }, Ct));
            Assert.IsTrue(cancelled.Success, cancelled.Message);
            SpOutboxItem done = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Cancelled, done.Status);
            Assert.AreEqual(1605, done.CancelledByUserId);
            Assert.AreEqual("سند منبع باطل شد", done.CancelReason);
            Assert.AreEqual(0, (await Prepare(e)).Prepared, "a cancelled item is not prepared again until the document changes");
        }

        [TestMethod]
        public async Task A_Sender_That_Throws_Leaves_A_Safe_Short_Text_Without_The_Exception_Message()
        {
            Env e = await NewEnv("throws", fake: true);
            e.Fake.Throw = true;
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            await Approve(e, FinanceManager, item.Id);
            Assert.AreEqual(SpSendOutcome.Failed, (await AsSeat(e, s => s.SendAsync(item.Id, Ct))).Data);
            SpOutboxItem row = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Failed, row.Status);
            Assert.IsFalse(row.LastError.Contains("secret"), row.LastError);
            Assert.IsFalse(Attempts(e, item.Id).Single().Error.Contains("secret"));
        }

        [TestMethod]
        public async Task The_File_Sender_Writes_One_File_Per_Envelope_And_A_Repeat_Writes_The_Same_File()
        {
            string folder = Path.Combine(Path.GetTempPath(), "sepidar-drop-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sender = new FileSepidarSender(folder);
                Assert.AreEqual("File", sender.Name);
                Assert.IsTrue(sender.IsConfigured);
                var envelope = new SepidarEnvelope
                {
                    ItemId = 12, IdempotencyKey = "12:abc", TransferTypeCode = "VOUCHER", SourceType = "FnVoucher", SourceId = 3, SourceNumber = "7", SchemaVersion = 1,
                    PayloadHash = "0123456789abcdef0123456789abcdef", PayloadJson = "{\"schemaVersion\":1,\"data\":{\"x\":1}}", PreparedAt = DateTimeOffset.Now,
                };
                SpSendResult one = await sender.SendAsync(envelope, Ct);
                Assert.IsTrue(one.Success, one.Error);
                Assert.AreEqual("sepidar-12-0123456789abcdef.json", one.ExternalReference);
                Assert.AreEqual(1, Directory.GetFiles(folder).Length);
                string text = await File.ReadAllTextAsync(Path.Combine(folder, one.ExternalReference));
                StringAssert.Contains(text, "\"idempotencyKey\": \"12:abc\"");
                StringAssert.Contains(text, "\"x\": 1");

                SpSendResult two = await sender.SendAsync(envelope, Ct);
                Assert.IsTrue(two.Success);
                Assert.AreEqual(one.ExternalReference, two.ExternalReference);
                Assert.AreEqual(1, Directory.GetFiles(folder).Length, "a repeat never makes a second file (and leaves no temporary file)");
                Assert.AreEqual(text, await File.ReadAllTextAsync(Path.Combine(folder, one.ExternalReference)));

                envelope.ItemId = 13;
                Assert.IsTrue((await sender.SendAsync(envelope, Ct)).Success);
                Assert.AreEqual(2, Directory.GetFiles(folder).Length);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [TestMethod]
        public async Task The_File_Sender_Refuses_A_Missing_Or_Unsafe_Folder_And_The_Factory_Picks_By_Configuration()
        {
            Assert.IsFalse(new FileSepidarSender((string)null).IsConfigured);
            Assert.IsFalse(new FileSepidarSender("  ").IsConfigured);
            Assert.IsFalse(new FileSepidarSender("/tmp/drop/../etc").IsConfigured, "no traversal");
            Assert.IsFalse(new FileSepidarSender("..\\x").IsConfigured);
            SpSendResult bad = await new FileSepidarSender("/tmp/a/../b").SendAsync(new SepidarEnvelope { ItemId = 1, PayloadHash = "abcd" }, Ct);
            Assert.IsFalse(bad.Success);
            Assert.IsFalse(string.IsNullOrWhiteSpace(bad.Error));
            Assert.IsFalse((await new FileSepidarSender("/tmp/ok").SendAsync(null, Ct)).Success);

            Func<string, string, ISepidarSender> make = (kind, folder) =>
            {
                var d = new Dictionary<string, string>();
                if (kind != null) d["Sepidar:Sender"] = kind;
                if (folder != null) d["Sepidar:FileDropFolder"] = folder;
                return SepidarSenderFactory.Create(new ConfigurationBuilder().AddInMemoryCollection(d).Build());
            };
            Assert.AreEqual("None", make(null, null).Name);
            Assert.IsFalse(make(null, null).IsConfigured);
            Assert.AreEqual("None", make("garbage", null).Name, "an unknown value is None");
            Assert.AreEqual("File", make("file", "/tmp/sp-drop").Name);
            Assert.IsTrue(make("File", "/tmp/sp-drop").IsConfigured);
            Assert.IsFalse(make("File", null).IsConfigured);
            ISepidarSender api = make("Api", null);
            Assert.AreEqual("Api", api.Name);
            Assert.IsFalse(api.IsConfigured, "the API client is a placeholder until the owner delivers the API");
            Assert.IsFalse((await api.SendAsync(new SepidarEnvelope(), Ct)).Success);
        }

        // ------------------------------------------------------------------------------------------------ payroll privacy

        [TestMethod]
        public async Task Every_Payroll_Item_Is_Restricted_To_The_Readers_Of_Payroll_In_Every_Read_Path_Also_A_Superseded_Pair()
        {
            Env e = await NewEnv("payroll");
            await Enable(e, "PAYROLL_SUMMARY");
            int small = 0, large = 0;
            e.Host.Write(u =>
            {
                FnPayrollRun a = AppHost.Add(u, new FnPayrollRun { Number = "PR-1", Year = 1405, Month = 4, Status = FnPayrollStatus.Posted, PostedAt = DateTimeOffset.Now, CreatedByUserId = 1604, PreparedByUserId = 1604, EmployeeCount = 2, TotalGross = 900_000_000m, TotalNet = 700_000_000m });
                FnPayrollRun b = AppHost.Add(u, new FnPayrollRun { Number = "PR-2", Year = 1405, Month = 5, Status = FnPayrollStatus.Paid, PostedAt = DateTimeOffset.Now, CreatedByUserId = 1604, PreparedByUserId = 1604, EmployeeCount = 12, TotalGross = 9_000_000_000m, TotalNet = 7_000_000_000m });
                AppHost.Add(u, new FnPayrollRun { Number = "PR-3", Year = 1405, Month = 6, Status = FnPayrollStatus.Calculated, CreatedByUserId = 1604, PreparedByUserId = 1604, EmployeeCount = 12, TotalGross = 1m });
                u.SaveChanges();
                small = a.Id; large = b.Id;
            }, Seeder);
            SpPrepareResult prepared = await Prepare(e);
            Assert.AreEqual(2, prepared.Prepared, "only posted or paid runs are final: " + prepared.Describe());
            Dictionary<int, SpOutboxItem> items = Items(e).ToDictionary(i => i.SourceId);
            Assert.IsTrue(items[small].PayrollRestricted, "few people");
            Assert.IsTrue(items[large].PayrollRestricted, "many people: the totals of a run are pay data whatever the number");
            // totals only: no per-person line or name in the payload
            StringAssert.Contains(items[large].PayloadJson, "\"totals\"");
            StringAssert.Contains(items[large].PayloadJson, "\"employeeCount\":12");
            Assert.IsFalse(items[large].PayloadJson.Contains("employees\""), "no per-person list");
            Assert.IsFalse(items[large].PayloadJson.Contains("iban", StringComparison.OrdinalIgnoreCase));

            // a changed run is replaced by a new item: the superseded pair is restricted too
            e.Host.Write(u => { FnPayrollRun r = u.Set<FnPayrollRun>().Single(x => x.Id == large); r.TotalGross = 9_500_000_000m; r.RowVersion = new byte[] { 9, 0, 0, 0, 0, 0, 0, 1 }; u.SaveChanges(); }, Seeder);
            SpPrepareResult second = await Prepare(e);
            Assert.AreEqual(1, second.Prepared, second.Describe());
            Assert.AreEqual(1, second.Superseded, second.Describe());
            Assert.IsTrue(Items(e).Where(i => i.SourceId == large).All(i => i.PayrollRestricted), "both items of the pair");

            foreach ((ClaimsPrincipal who, bool sees) in new[] { (FinanceManager, true), (Ceo, true), (Auditor, false) })
            {
                PagedList<SpOutboxItemDto> list = (await As(e, who, s => s.GetItemsAsync(new SpOutboxQuery { PageSize = 50 }, Ct))).Data;
                Assert.AreEqual(sees ? 3 : 0, list.Count, who.Identity.Name + " list");
                foreach (SpOutboxItem item in Items(e))
                {
                    ResponseDto<SpOutboxItemDetailDto> detail = await As(e, who, s => s.GetItemAsync(item.Id, Ct));
                    Assert.AreEqual(sees, detail.Success, who.Identity.Name + " detail of " + item.Id);
                    if (detail.Success) StringAssert.Contains(detail.Data.PayloadPretty, "totals");
                }
                Assert.AreEqual(sees ? 3 : 0, (await As(e, who, s => s.GetSummaryAsync(Ct))).Data.Total, who.Identity.Name + " summary");
                Assert.AreEqual(sees ? 3 : 0, (await As(e, who, s => s.GetItemsAsync(new SpOutboxQuery { TypeCode = "PAYROLL_SUMMARY", Search = "PR" }, Ct))).Data.Count, who.Identity.Name + " filtered list");
            }
            // the auditor cannot even decide on it by guessing its id (not found, no hint that it exists)
            Assert.IsFalse((await As(e, Auditor, s => s.DecideAsync(new SpDecideDto { Id = items[small].Id, Action = "reject", Reason = "x" }, Ct))).Success);
            Assert.AreEqual(SpOutboxStatus.AwaitingApproval, Item(e, items[small].Id).Status);

            // the task texts of a restricted item never carry its number or title
            await Settle(e);
            foreach (AgentTask t in Tasks(e).Where(t => t.StepCode == "SP.OUTBOX.APPROVE"))
            {
                Assert.IsFalse((t.Summary ?? string.Empty).Contains("PR-"), "summary: " + t.Summary);
                Assert.IsFalse((t.TargetNumber ?? string.Empty).Contains("PR-"), "number: " + t.TargetNumber);
            }
            Assert.IsTrue(Tasks(e).Any(t => t.StepCode == "SP.OUTBOX.APPROVE"));
            // the CEO may decide on it (he is not the maker)
            Assert.IsTrue((await Approve(e, Ceo, items[small].Id)).Success, "the CEO decides");
        }

        // ------------------------------------------------------------------------------------------------ the queue reads

        [TestMethod]
        public async Task The_Queue_Is_Ordered_Paged_And_Filtered_And_The_Detail_Has_A_Pretty_Payload_And_The_Attempts()
        {
            Env e = await NewEnv("reads", fake: true);
            await Enable(e, "VOUCHER");
            for (int n = 1; n <= 5; n++) Voucher(e, n);
            await Prepare(e);
            List<SpOutboxItem> all = Items(e);
            Assert.AreEqual(5, all.Count);

            PagedList<SpOutboxItemDto> page1 = (await As(e, Auditor, s => s.GetItemsAsync(new SpOutboxQuery { PageNumber = 1, PageSize = 2 }, Ct))).Data;
            PagedList<SpOutboxItemDto> page3 = (await As(e, Auditor, s => s.GetItemsAsync(new SpOutboxQuery { PageNumber = 3, PageSize = 2 }, Ct))).Data;
            Assert.AreEqual(5, page1.TotalCount);
            CollectionAssert.AreEqual(all.OrderByDescending(i => i.Id).Take(2).Select(i => i.Id).ToArray(), page1.Select(i => i.Id).ToArray(), "newest first, ordered before paging");
            Assert.AreEqual(1, page3.Count);
            Assert.AreEqual(all.OrderBy(i => i.Id).First().Id, page3[0].Id);

            int target = all[0].Id;
            Assert.IsTrue((await Approve(e, FinanceManager, target)).Success);
            await AsSeat(e, s => s.SendAsync(target, Ct));
            Assert.AreEqual(1, (await As(e, Ceo, s => s.GetItemsAsync(new SpOutboxQuery { Status = SpOutboxStatus.Sent }, Ct))).Data.Count);
            Assert.AreEqual(4, (await As(e, Ceo, s => s.GetItemsAsync(new SpOutboxQuery { Status = SpOutboxStatus.AwaitingApproval }, Ct))).Data.TotalCount);
            Assert.AreEqual(0, (await As(e, Ceo, s => s.GetItemsAsync(new SpOutboxQuery { TypeCode = "RECEIPT" }, Ct))).Data.TotalCount);
            Assert.AreEqual(1, (await As(e, Ceo, s => s.GetItemsAsync(new SpOutboxQuery { SourceType = "FnVoucher", SourceId = all[0].SourceId }, Ct))).Data.TotalCount);

            SpOutboxItemDetailDto detail = (await As(e, Auditor, s => s.GetItemAsync(target, Ct))).Data;
            StringAssert.Contains(detail.PayloadPretty, "\n");
            StringAssert.Contains(detail.PayloadPretty, "\"schemaVersion\": 1");
            Assert.AreEqual(1, detail.Attempts.Count);
            Assert.AreEqual(SpAttemptOutcome.Succeeded, detail.Attempts[0].Outcome);
            Assert.IsFalse(detail.CanDecide);
            Assert.IsFalse((await As(e, Accountant, s => s.GetItemsAsync(new SpOutboxQuery(), Ct))).Success, "the accountant does not read the queue");
            Assert.IsFalse((await As(e, Auditor, s => s.FixAsync(new SpFixDto { Id = target, Action = "cancel", Reason = "x" }, Ct))).Success, "the auditor reads only");

            SpOutboxSummaryDto summary = (await As(e, Auditor, s => s.GetSummaryAsync(Ct))).Data;
            Assert.AreEqual("Fake", summary.SenderName);
            Assert.IsTrue(summary.IsConfigured);
            Assert.IsNotNull(summary.LastSuccessfulSendAt);
            Assert.AreEqual(1, summary.EnabledTypes);
            Assert.AreEqual(9, summary.Counts.Count, "one row per status");
            Assert.AreEqual(1, summary.Counts.Single(c => c.Status == SpOutboxStatus.Sent).Count);
            Assert.AreEqual(4, summary.Counts.Single(c => c.Status == SpOutboxStatus.AwaitingApproval).Count);
            Assert.AreEqual(5, summary.Total);
        }

        // ------------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public async Task The_Four_Steps_Are_In_The_Catalogue_With_The_Right_Kind_Station_And_Agent()
        {
            Env e = await NewEnv("catalogue");
            List<IAgentStep> steps = e.Host.Provider.CreateScope().ServiceProvider.GetServices<IAgentStep>().Where(s => s.Code.StartsWith("SP.OUTBOX.", StringComparison.Ordinal)).OrderBy(s => s.Code).ToList();
            CollectionAssert.AreEqual(new[] { "SP.OUTBOX.APPROVE", "SP.OUTBOX.FIX", "SP.OUTBOX.PREPARE", "SP.OUTBOX.SEND" }, steps.Select(s => s.Code).ToArray());
            Assert.IsTrue(steps.All(s => s.Station == 13 && s.AgentCode == "AG-FIN"));
            Assert.AreEqual(AgentStepKind.HumanGate, steps.Single(s => s.Code == "SP.OUTBOX.APPROVE").Kind);
            Assert.AreEqual(AgentStepKind.Input, steps.Single(s => s.Code == "SP.OUTBOX.FIX").Kind);
            Assert.AreEqual(AgentStepKind.Agent, steps.Single(s => s.Code == "SP.OUTBOX.PREPARE").Kind);
            Assert.AreEqual(AgentStepKind.Agent, steps.Single(s => s.Code == "SP.OUTBOX.SEND").Kind);
        }

        // ------------------------------------------------------------------------------------------------ review fixes

        private static void SetFinalized(Env e, int voucherId, DateTimeOffset at) =>
            e.Host.Write(u => { u.Set<FnVoucher>().Single(v => v.Id == voucherId).FinalizedAt = at; u.SaveChanges(); }, Seeder);

        /// <summary>A voucher with two ordinary lines and, on request, a protected labour line or a line on an employee detail account.</summary>
        private static int VoucherWithLines(Env e, int number, bool labourProtected, bool employeeDetail)
        {
            int id = Voucher(e, number);
            e.Host.Write(u =>
            {
                FnAccount cash = AppHost.Add(u, new FnAccount { Code = "1101-" + number, Title = "Cash title " + number, IsActive = true });
                FnAccount sales = AppHost.Add(u, new FnAccount { Code = "4101-" + number, Title = "Sales title " + number, IsActive = true });
                FnDetailAccount person = AppHost.Add(u, new FnDetailAccount { Code = "EMP-" + number, Title = "Employee title " + number, Type = FnDetailType.Employee, IsActive = true });
                u.SaveChanges();
                u.Set<FnVoucherLine>().Add(new FnVoucherLine { VoucherId = id, RowNumber = 1, AccountId = cash.Id, Debit = 600_000m });
                u.Set<FnVoucherLine>().Add(new FnVoucherLine { VoucherId = id, RowNumber = 2, AccountId = sales.Id, Credit = 400_000m, Detail1Id = employeeDetail ? person.Id : (int?)null });
                u.Set<FnVoucherLine>().Add(new FnVoucherLine { VoucherId = id, RowNumber = 3, AccountId = sales.Id, Credit = 200_000m, LabourProtected = labourProtected });
                u.SaveChanges();
            }, Seeder);
            return id;
        }

        [TestMethod]
        public async Task A_Voucher_Carries_Every_Line_And_Is_Restricted_Only_With_A_Protected_Labour_Line_Or_An_Employee_Detail_Account()
        {
            Env e = await NewEnv("voucherlines");
            await Enable(e, "VOUCHER");
            int plain = VoucherWithLines(e, 1, false, false), labour = VoucherWithLines(e, 2, true, false), employee = VoucherWithLines(e, 3, false, true);
            Assert.AreEqual(3, (await Prepare(e)).Prepared);
            Dictionary<int, SpOutboxItem> items = Items(e).ToDictionary(i => i.SourceId);
            Assert.IsFalse(items[plain].PayrollRestricted, "an ordinary voucher is not restricted");
            Assert.IsTrue(items[labour].PayrollRestricted, "a protected labour line");
            Assert.IsTrue(items[employee].PayrollRestricted, "a line on an employee detail account");
            foreach (int id in new[] { plain, labour, employee })
            {
                string json = items[id].PayloadJson;
                Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(json, "\"row\":").Count, "every line is in the payload (also the protected one): " + json);
                Assert.IsFalse(json.Contains("title " + (id == plain ? 1 : id == labour ? 2 : 3), StringComparison.OrdinalIgnoreCase), "no renameable master-data title in the payload");
            }
            foreach ((ClaimsPrincipal who, int expected) in new[] { (FinanceManager, 3), (Ceo, 3), (Auditor, 1) })
            {
                PagedList<SpOutboxItemDto> list = (await As(e, who, s => s.GetItemsAsync(new SpOutboxQuery { PageSize = 50 }, Ct))).Data;
                Assert.AreEqual(expected, list.Count, who.Identity.Name);
                Assert.AreEqual(expected, (await As(e, who, s => s.GetSummaryAsync(Ct))).Data.Total, who.Identity.Name + " summary");
                Assert.AreEqual(who == Auditor, (await As(e, who, s => s.GetItemAsync(items[labour].Id, Ct))).Success == false, who.Identity.Name + " labour detail");
                Assert.AreEqual(who != Auditor, (await As(e, who, s => s.GetItemAsync(items[employee].Id, Ct))).Success, who.Identity.Name + " employee detail");
                Assert.IsTrue((await As(e, who, s => s.GetItemAsync(items[plain].Id, Ct))).Success, who.Identity.Name + " plain detail");
            }
            await Settle(e);
            foreach (AgentTask t in Tasks(e).Where(t => t.StepCode == "SP.OUTBOX.APPROVE" && (t.TargetId == items[labour].Id || t.TargetId == items[employee].Id)))
                Assert.AreEqual("SP-" + t.TargetId, t.TargetNumber, "a restricted item shows only its id in the task");
        }

        [TestMethod]
        public async Task A_Kind_Needs_A_First_Day_And_Only_Documents_Finalised_From_That_Day_Are_Queued()
        {
            Env e = await NewEnv("fromdate");
            ResponseDto<SpTransferTypeDto> none = await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true }, Ct));
            Assert.IsFalse(none.Success);
            Assert.AreEqual("تاریخ شروع انتقال را مشخص کنید؛ فقط اسنادی که از این روز به بعد نهایی می‌شوند منتقل می‌شوند.", none.Message);
            int old = Voucher(e, 1), fresh = Voucher(e, 2);
            SetFinalized(e, old, DateTimeOffset.Now.AddDays(-3));
            Assert.IsTrue((await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success);
            SpPrepareResult r = await Prepare(e);
            Assert.AreEqual(1, r.Prepared, r.Describe());
            CollectionAssert.AreEqual(new[] { fresh }, Items(e).Select(i => i.SourceId).ToArray(), "the document finalised before the first day is never queued");
            Assert.AreEqual(DateTime.Today, (await As(e, Auditor, s => s.GetTypesAsync(Ct))).Data.Single(t => t.Code == "VOUCHER").FromDate);
        }

        [TestMethod]
        public async Task A_Kind_Cannot_Be_Switched_On_While_The_Old_Monthly_Package_Of_The_Same_Data_Covers_The_Period()
        {
            Env e = await NewEnv("overlap");
            e.Host.Write(u =>
            {
                AppHost.Add(u, new FnOutboxPackage { Number = "OB-1", Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = 1405, Month = 8, Status = FnOutboxStatus.Ready, FromDate = DateTime.Today.AddDays(-20), ToDate = DateTime.Today.AddDays(5), CreatedAt = DateTimeOffset.Now });
                AppHost.Add(u, new FnOutboxPackage { Number = "OB-2", Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = 1405, Month = 9, Status = FnOutboxStatus.Superseded, FromDate = DateTime.Today.AddDays(5), ToDate = DateTime.Today.AddDays(40), CreatedAt = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);
            ResponseDto<SpTransferTypeDto> refused = await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "SALES_INVOICE", IsEnabled = true, FromDate = DateTime.Today }, Ct));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "بستهٔ ماهانهٔ «صورت‌حساب فروش (مودیان)»");
            // a first day after the end of every live package is fine; a superseded package never blocks
            Assert.IsTrue((await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "SALES_INVOICE", IsEnabled = true, FromDate = DateTime.Today.AddDays(6) }, Ct))).Success);
            Assert.IsTrue((await As(e, FinanceManager, s => s.SwitchTypeAsync(new SpTypeSwitchDto { Code = "VOUCHER", IsEnabled = true, FromDate = DateTime.Today }, Ct))).Success, "a kind with no old twin is never blocked");
            Assert.IsNotNull((await As(e, Auditor, s => s.GetTypesAsync(Ct))).Data.Single(t => t.Code == "SALES_INVOICE").OverlapNote);
        }

        [TestMethod]
        public async Task The_Agent_Cancels_An_Item_Whose_Source_Is_No_Longer_Final_With_The_Reason_And_No_Decision_Column()
        {
            Env e = await NewEnv("nolongerfinal");
            await Enable(e, "VOUCHER");
            int v = Voucher(e, 1);
            SpOutboxItem item = await Prepared(e, v);
            e.Host.Write(u => { FnVoucher x = u.Set<FnVoucher>().Single(y => y.Id == v); x.Status = FnVoucherStatus.Draft; x.Number = null; u.SaveChanges(); }, Seeder);
            SpPrepareResult r = await Prepare(e);
            Assert.AreEqual(1, r.Cancelled, r.Describe());
            SpOutboxItem after = Item(e, item.Id);
            Assert.AreEqual(SpOutboxStatus.Cancelled, after.Status);
            Assert.AreEqual("سند مبدأ دیگر نهایی نیست", after.CancelReason);
            Assert.IsNull(after.CancelledByUserId, "an agent seat never writes a decision column");
            Assert.IsNotNull(after.CancelledAt);
        }

        [TestMethod]
        public async Task A_Stuck_Sending_Item_Is_Recovered_By_A_Person_And_Goes_Back_To_Approved_When_The_Kind_Is_Off_Without_An_Extra_Attempt()
        {
            Env e = await NewEnv("stale", fake: true);
            await Enable(e, "VOUCHER");
            SpOutboxItem a = await Prepared(e, Voucher(e, 1));
            Assert.IsTrue((await Approve(e, FinanceManager, a.Id)).Success);
            Action<int, int> stick = (id, minutes) => e.Host.Write(u =>
            {
                SpOutboxItem x = u.Set<SpOutboxItem>().Single(y => y.Id == id);
                x.Status = SpOutboxStatus.Sending; x.AttemptCount = 1; x.LastAttemptAt = DateTimeOffset.Now.AddMinutes(-minutes);
                u.Set<SpOutboxAttempt>().Add(new SpOutboxAttempt { ItemId = id, AttemptNumber = 1, StartedAt = DateTimeOffset.Now.AddMinutes(-minutes), Outcome = SpAttemptOutcome.Started, SenderName = "Fake" });
                u.SaveChanges();
            }, Seeder);
            stick(a.Id, 5);
            Assert.IsFalse((await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = a.Id, Action = "retry" }, Ct))).Success, "a send that is only 5 minutes old is still running");
            e.Host.Write(u => { u.Set<SpOutboxAttempt>().RemoveRange(u.Set<SpOutboxAttempt>()); u.SaveChanges(); }, Seeder);
            stick(a.Id, 60);
            ResponseDto<SpOutboxItemDto> retry = await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = a.Id, Action = "retry" }, Ct));
            Assert.IsTrue(retry.Success, retry.Message);
            Assert.AreEqual(SpOutboxStatus.Approved, Item(e, a.Id).Status);
            Assert.AreEqual(SpAttemptOutcome.Failed, Attempts(e, a.Id).Single().Outcome, "the open attempt is closed");

            // automatic: stuck, and the kind is switched off -> back to approved, no attempt counted
            e.Host.Write(u => { u.Set<SpOutboxAttempt>().RemoveRange(u.Set<SpOutboxAttempt>()); u.SaveChanges(); }, Seeder);
            stick(a.Id, 60);
            await Enable(e, "VOUCHER", on: false);
            SpPrepareResult r = await Prepare(e);
            Assert.AreEqual(1, r.Recovered, r.Describe());
            SpOutboxItem back = Item(e, a.Id);
            Assert.AreEqual(SpOutboxStatus.Approved, back.Status);
            Assert.AreEqual(1, back.AttemptCount, "no extra attempt");
        }

        [TestMethod]
        public async Task The_Page_Size_Is_Clamped_To_200_And_Zero_Gives_The_Default_And_A_Missing_Row_Version_Is_Refused_On_Fix()
        {
            Env e = await NewEnv("paging");
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            Assert.AreEqual(200, (await As(e, Auditor, s => s.GetItemsAsync(new SpOutboxQuery { PageSize = 1000 }, Ct))).Data.PageSize);
            Assert.AreEqual(50, (await As(e, Auditor, s => s.GetItemsAsync(new SpOutboxQuery { PageSize = 0 }, Ct))).Data.PageSize);
            Assert.AreEqual(7, (await As(e, Auditor, s => s.GetItemsAsync(new SpOutboxQuery { PageSize = 7 }, Ct))).Data.PageSize);
            Assert.IsTrue((await Approve(e, FinanceManager, item.Id)).Success);
            StampVersion(e, item.Id, 3);
            ResponseDto<SpOutboxItemDto> noVersion = await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "cancel", Reason = "x" }, Ct));
            Assert.IsFalse(noVersion.Success);
            StringAssert.Contains(noVersion.Message, "نسخه");
            Assert.AreEqual(SpOutboxStatus.Approved, Item(e, item.Id).Status);
            Assert.IsTrue((await As(e, FinanceManager, s => s.FixAsync(new SpFixDto { Id = item.Id, Action = "cancel", Reason = "x", RowVersion = V(3) }, Ct))).Success);
        }

        [TestMethod]
        public async Task The_Failed_Items_Alert_On_The_Finance_Dashboard_Is_For_The_Readers_Of_The_Page_Only()
        {
            Env e = await NewEnv("alert");
            await Enable(e, "VOUCHER");
            SpOutboxItem item = await Prepared(e, Voucher(e, 1));
            e.Host.Write(u => { u.Set<SpOutboxItem>().Single(i => i.Id == item.Id).Status = SpOutboxStatus.Failed; u.SaveChanges(); }, Seeder);
            Func<ClaimsPrincipal, Task<bool>> seen = async who => (await e.Host.As(who, sp => sp.GetRequiredService<Services.NasbNiro.Finance.IFinanceDashboardService>().GetAsync(Ct))).Data.Alerts.Any(a => a.Contains("انتقال به سپیدار ناموفق"));
            Assert.IsTrue(await seen(FinanceManager));
            Assert.IsTrue(await seen(Auditor));
            Assert.IsFalse(await seen(Accountant), "an accountant does not read the Sepidar page");
        }
    }
}
