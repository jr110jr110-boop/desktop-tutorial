using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using static MsTests.FinanceTestSupport;

namespace MsTests
{
    /// <summary>
    /// R1t: the general ledger rules of the audit remediation. The real services run on the in-memory database.
    /// D9 (a CEO approval does not survive a later edit), D10 (two-step period close and its guards), D11 (late posting),
    /// D12 / D26 (system vouchers and who may uncheck or delete).
    /// </summary>
    [TestClass]
    public class LedgerR1Tests
    {
        private static AppHost NewHost() => FinanceTestSupport.NewHost("LedgerR1");

        private static SaveVoucherDto Voucher(int? id, decimal amount, string description = "آزمون", DateTime? date = null) => new SaveVoucherDto
        {
            Id = id, Date = (date ?? DateTime.Today).Date, Description = description,
            Lines = new List<SaveVoucherLineDto>
            {
                new SaveVoucherLineDto { AccountCode = "80108", Debit = amount },
                new SaveVoucherLineDto { AccountCode = "10101", Credit = amount },
            },
        };

        private static Task<ResponseDto<VoucherDto>> Save(AppHost host, ClaimsPrincipal who, SaveVoucherDto dto) =>
            host.As(who, sp => sp.GetRequiredService<IGlService>().SaveVoucherAsync(dto, CancellationToken.None));

        private static Task<ResponseDto<VoucherDto>> Act(AppHost host, ClaimsPrincipal who, int id, string action) =>
            host.As(who, sp => sp.GetRequiredService<IGlService>().VoucherActionAsync(new VoucherActionDto { Id = id, Action = action }, CancellationToken.None));

        private static Task<ResponseDto<VoucherDto>> Get(AppHost host, int id) =>
            host.As(Fm, sp => sp.GetRequiredService<IGlService>().GetVoucherAsync(id, CancellationToken.None));

        private static async Task SettingAsync(AppHost host, string key, string value) =>
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().SaveSettingAsync(new SaveFinanceSettingDto { Key = key, Value = value }, CancellationToken.None)));

        private static async Task<PeriodDto> CurrentPeriodAsync(AppHost host)
        {
            List<FiscalYearDto> years = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().GetYearsAsync(null, CancellationToken.None)));
            return years.SelectMany(y => y.Periods).First(p => p.StartDate <= DateTime.Today && DateTime.Today <= p.EndDate);
        }

        private static Task<ResponseDto<FiscalYearDto>> PeriodAct(AppHost host, ClaimsPrincipal who, int id, string action, string reason = null) =>
            host.As(who, sp => sp.GetRequiredService<IGlService>().PeriodActionAsync(new PeriodActionDto { Id = id, Action = action, Reason = reason }, CancellationToken.None));

        private static Task<ResponseDto<CloseChecklistDto>> Override(AppHost host, ClaimsPrincipal who, int periodId, string action, string reason = null) =>
            host.As(who, sp => sp.GetRequiredService<IGlControlsService>().OverrideChecklistAsync(periodId, new ChecklistOverrideDto { Action = action, Reason = reason }, CancellationToken.None));

        private static Task<ResponseDto<FiscalYearDto>> CloseYear(AppHost host, ClaimsPrincipal who, int yearId, bool overrideReconciliation = false, string reason = null) =>
            host.As(who, sp => sp.GetRequiredService<IGlService>().CloseYearAsync(yearId, new CloseYearDto { OverrideReconciliation = overrideReconciliation, Reason = reason }, CancellationToken.None));

        private static async Task<FiscalYearDto> EnsureYearAsync(AppHost host, DateTime date) =>
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().EnsureYearAsync(new EnsureYearDto { Date = date }, CancellationToken.None)));

        private static async Task LockAllAsync(AppHost host, FiscalYearDto year)
        {
            foreach (PeriodDto p in year.Periods) Ok(await PeriodAct(host, Fin, p.Id, "lock"));
        }

        /// <summary>Check by a second accountant and finalise by the finance manager: a manual voucher becomes final.</summary>
        private static async Task FinalizeAsync(AppHost host, int voucherId)
        {
            Ok(await Act(host, Fm2, voucherId, "check"));
            Ok(await Act(host, Fm, voucherId, "finalize"));
        }

        // ------------------------------------------------------------------------------------------------ D9

        [TestMethod]
        public async Task AnEditAfterTheCeoApprovedVoidsTheApprovalAndTheVoucherCannotBeFinalizedUntilItIsApprovedAgain()
        {
            AppHost host = NewHost();
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().SaveThresholdAsync(new SaveApprovalThresholdDto { MinAmount = 500_000m, IsActive = true }, CancellationToken.None)));

            VoucherDto v = Ok(await Save(host, Fin, Voucher(null, 1_000_000m)));
            Ok(await Act(host, Ceo, v.Id, "ceo-approve"));
            Assert.AreEqual(37, Ok(await Get(host, v.Id)).CeoApprovedByUserId);

            // the accountant changes an amount: the approval is void, the voucher is a draft again
            VoucherDto edited = Ok(await Save(host, Fin, Voucher(v.Id, 1_200_000m)));
            Assert.IsNull(edited.CeoApprovedByUserId, "the CEO approved other numbers");
            Assert.IsNull(edited.CeoApprovedAt);
            Assert.IsNotNull(edited.LastEditedAt);
            Assert.AreEqual(FnVoucherStatus.Draft, edited.Status);

            Ok(await Act(host, Fm2, v.Id, "check"));
            ResponseDto<VoucherDto> early = await Act(host, Fm, v.Id, "finalize");
            Assert.IsFalse(early.Success);
            StringAssert.Contains(early.Message, "تایید مدیرعامل");

            // the invalidation is in the audit trail, with the approver and the time of the approval that no longer counts
            FnAuditLog note = host.Read(uow => uow.Set<FnAuditLog>().Single(a => a.Action == "CeoApprovalVoid" && a.EntityId == v.Id));
            Assert.AreEqual("FnVoucher", note.EntityType);
            Assert.AreEqual(31, note.UserId);
            StringAssert.Contains(note.BeforeJson, "\"CeoApprovedByUserId\":37");
            StringAssert.Contains(note.Summary, "باطل");

            Ok(await Act(host, Ceo, v.Id, "ceo-approve"));
            VoucherDto done = Ok(await Act(host, Fm, v.Id, "finalize"));
            Assert.AreEqual(FnVoucherStatus.Final, done.Status);
            Assert.AreEqual(1_200_000m, done.TotalAmount);
        }

        [TestMethod]
        public async Task ASaveThatChangesNothingKeepsTheApprovalButAnyChangedFieldVoidsIt()
        {
            AppHost host = NewHost();
            VoucherDto v = Ok(await Save(host, Fin, Voucher(null, 800_000m, "اولیه")));
            Ok(await Act(host, Ceo, v.Id, "ceo-approve"));

            VoucherDto same = Ok(await Save(host, Fin, Voucher(v.Id, 800_000m, "اولیه")));
            Assert.AreEqual(37, same.CeoApprovedByUserId, "nothing changed");
            Assert.IsNull(same.LastEditedAt);

            VoucherDto text = Ok(await Save(host, Fin, Voucher(v.Id, 800_000m, "شرح تازه")));
            Assert.IsNull(text.CeoApprovedByUserId, "the description is a header field");

            Ok(await Act(host, Ceo, v.Id, "ceo-approve"));
            VoucherDto moved = Ok(await Save(host, Fin, Voucher(v.Id, 800_000m, "شرح تازه", DateTime.Today.AddDays(-1))));
            Assert.IsNull(moved.CeoApprovedByUserId, "the date is a header field");

            Ok(await Act(host, Ceo, v.Id, "ceo-approve"));
            SaveVoucherDto otherAccount = Voucher(v.Id, 800_000m, "شرح تازه", DateTime.Today.AddDays(-1));
            otherAccount.Lines[0].AccountCode = "80109";
            VoucherDto changedLine = Ok(await Save(host, Fin, otherAccount));
            Assert.IsNull(changedLine.CeoApprovedByUserId, "the account of a line changed");
        }

        [TestMethod]
        public void TheApprovalIsCurrentOnlyWhenItIsNewerThanTheLastEdit()
        {
            DateTimeOffset t = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
            Assert.IsFalse(GlService.CeoApprovalIsCurrent(null, null, null), "no approval");
            Assert.IsTrue(GlService.CeoApprovalIsCurrent(5, t, null), "an approval made before edits were tracked counts");
            Assert.IsTrue(GlService.CeoApprovalIsCurrent(5, t.AddMinutes(1), t), "approved after the last edit");
            Assert.IsFalse(GlService.CeoApprovalIsCurrent(5, t, t), "same instant is not newer");
            Assert.IsFalse(GlService.CeoApprovalIsCurrent(5, t, t.AddSeconds(1)), "edited after the approval");
        }

        // ------------------------------------------------------------------------------------------------ D10: lock, then close by a second person

        [TestMethod]
        public async Task ClosingAPeriodIsASecondStepByTheCeoAndNeverBySomeoneWhoLockedIt()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            PeriodDto p = await CurrentPeriodAsync(host);

            Assert.IsFalse((await PeriodAct(host, Ceo, p.Id, "close")).Success, "an open period is locked first");
            Assert.IsFalse((await PeriodAct(host, Tr, p.Id, "lock")).Success, "treasury does not lock periods");
            Ok(await PeriodAct(host, Fin, p.Id, "lock"));   // an accountant may lock

            ResponseDto<FiscalYearDto> byManager = await PeriodAct(host, Fm, p.Id, "close");
            Assert.IsFalse(byManager.Success, "the company chose the CEO for the second step");
            StringAssert.Contains(byManager.Message, "مجوز");
            Assert.IsFalse((await PeriodAct(host, Fin, p.Id, "close")).Success, "the locker is no closer");

            FiscalYearDto closed = Ok(await PeriodAct(host, Ceo, p.Id, "close"));
            Assert.AreEqual(FnPeriodStatus.Closed, closed.Periods.Single(x => x.Id == p.Id).Status);
            Assert.IsFalse((await PeriodAct(host, Fm, p.Id, "reopen", "x")).Success, "a closed period stays closed");
            Assert.AreEqual(2, host.Read(uow => uow.Set<FnPeriodLog>().Count(l => l.PeriodId == p.Id)), "lock and close are both logged with their users");
        }

        [TestMethod]
        public async Task WhenTheCompanyChoosesItsFinanceManagerTheCloserStillDiffersFromTheLocker()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            await SettingAsync(host, "PeriodClose.CloseBy", "FINANCEMANAGER");
            PeriodDto p = await CurrentPeriodAsync(host);
            Ok(await PeriodAct(host, Fm, p.Id, "lock"));

            ResponseDto<FiscalYearDto> same = await PeriodAct(host, Fm, p.Id, "close");
            Assert.IsFalse(same.Success);
            StringAssert.Contains(same.Message, "غیر از کسی که آن را قفل کرده");
            Assert.IsFalse((await PeriodAct(host, Ceo, p.Id, "close")).Success, "the CEO is not the closer once the company chose its finance manager");
            Ok(await PeriodAct(host, Fm2, p.Id, "close"));
        }

        [TestMethod]
        public async Task TheSettingForTheCloserAcceptsOnlyTheTwoRoles()
        {
            AppHost host = NewHost();
            ResponseDto<FinanceSettingDto> bad = await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().SaveSettingAsync(new SaveFinanceSettingDto { Key = "PeriodClose.CloseBy", Value = "PMO" }, CancellationToken.None));
            Assert.IsFalse(bad.Success);
            List<FinanceSettingDto> all = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().GetSettingsAsync(CancellationToken.None)));
            Assert.AreEqual("CEO", all.Single(x => x.Key == "PeriodClose.CloseBy").Value, "default: the CEO");
            Assert.AreEqual("1", all.Single(x => x.Key == "CloseChecklist.Enforce").Value, "default: the checklist is enforced");
        }

        [TestMethod]
        public async Task ACloseRefusesWhileTheMonthStillHoldsUnfinishedWorkAndNamesEachKind()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            PeriodDto p = await CurrentPeriodAsync(host);
            VoucherDto draft = Ok(await Save(host, Fin, Voucher(null, 100_000m)));
            int pYear = PersianCal.Year(DateTime.Today), pMonth = PersianCal.Month(DateTime.Today);
            host.Write(uow =>
            {
                AppHost.Add(uow, new FnReceipt { Date = DateTime.Today, Status = FnDocStatus.Approved, Number = "RC-T1", TotalAmount = 5 });
                AppHost.Add(uow, new AccountingEvent { EventDate = DateTime.Today, Status = AccountingEventStatus.Pending, LegalEntityId = 1, SourceType = "Test", SourceNumber = "T1", Key = Guid.NewGuid() });
                AppHost.Add(uow, new FnPayrollRun { Year = pYear, Month = pMonth, Status = FnPayrollStatus.Calculated, Number = "PR-T1" });
                uow.SaveChanges();
            });
            Ok(await PeriodAct(host, Fin, p.Id, "lock"));

            ResponseDto<FiscalYearDto> refused = await PeriodAct(host, Ceo, p.Id, "close");
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "سند قطعی‌نشده");
            StringAssert.Contains(refused.Message, "رویداد مالی");
            StringAssert.Contains(refused.Message, "سند خزانه");
            StringAssert.Contains(refused.Message, $"فهرست حقوق {pYear}/{pMonth:00}");

            await FinalizeAsync(host, draft.Id);
            ResponseDto<FiscalYearDto> still = await PeriodAct(host, Ceo, p.Id, "close");
            Assert.IsFalse(still.Success);
            Assert.IsFalse(still.Message.Contains("سند قطعی‌نشده"), "the voucher is final now, the other three still block");

            host.Write(uow =>
            {
                uow.Set<FnReceipt>().Single(r => r.Number == "RC-T1").Status = FnDocStatus.Cancelled;
                uow.Set<AccountingEvent>().Single(e => e.SourceNumber == "T1").Status = AccountingEventStatus.Posted;
                uow.SaveChanges();
            });
            ResponseDto<FiscalYearDto> payrollOnly = await PeriodAct(host, Ceo, p.Id, "close");
            Assert.IsFalse(payrollOnly.Success);
            StringAssert.Contains(payrollOnly.Message, "فهرست حقوق");
            host.Write(uow => { uow.Set<FnPayrollRun>().Single(r => r.Number == "PR-T1").Status = FnPayrollStatus.Posted; uow.SaveChanges(); });
            Ok(await PeriodAct(host, Ceo, p.Id, "close"));
        }

        [TestMethod]
        public async Task PassingARedChecklistItemNeedsOneManagersRequestAndAnotherManagersApproval()
        {
            AppHost host = NewHost();   // the checklist is enforced by default
            PeriodDto p = await CurrentPeriodAsync(host);
            Ok(await Save(host, Fin, Voucher(null, 100_000m)));   // a draft voucher: the item "NonFinalVouchers" is red

            ResponseDto<FiscalYearDto> refused = await PeriodAct(host, Fin, p.Id, "lock");
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "فهرست کنترل");

            Assert.IsFalse((await Override(host, Fin, p.Id, "request", "x")).Success, "only a finance manager asks");
            Assert.IsFalse((await Override(host, Fm, p.Id, "request", null)).Success, "a reason is needed");
            CloseChecklistDto pending = Ok(await Override(host, Fm, p.Id, "request", "پیش‌نویس به ماه بعد منتقل می‌شود"));
            Assert.IsTrue(pending.OverridePending);
            Assert.IsFalse(pending.Overridden);
            Assert.IsFalse(pending.CanLock, "one manager alone does not pass the control");
            Assert.AreEqual(32, pending.OverrideRequestedByUserId);
            Assert.IsFalse((await PeriodAct(host, Fin, p.Id, "lock")).Success);
            Assert.IsFalse((await Override(host, Fm, p.Id, "approve")).Success, "the requester cannot approve his own request");
            Assert.IsFalse((await Override(host, Fin, p.Id, "approve")).Success, "an accountant cannot approve");

            CloseChecklistDto approved = Ok(await Override(host, Fm2, p.Id, "approve"));
            Assert.IsTrue(approved.Overridden);
            Assert.IsTrue(approved.CanLock);
            Assert.AreEqual(36, approved.OverriddenByUserId);
            Assert.AreEqual(32, approved.OverrideRequestedByUserId == null ? 32 : approved.OverrideRequestedByUserId, "the request is no longer pending");
            Assert.IsFalse(approved.OverridePending);

            // both steps are in the audit trail
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(a => a.Action == "CloseOverrideReq" && a.EntityId == p.Id && a.UserId == 32)));
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(a => a.Action == "CloseOverrideOk" && a.EntityId == p.Id && a.UserId == 36)));

            // an item that turns red after the approval is not covered by it
            host.Write(uow => { AppHost.Add(uow, new AccountingEvent { EventDate = DateTime.Today, Status = AccountingEventStatus.Pending, LegalEntityId = 1, SourceType = "Test", SourceNumber = "T2", Key = Guid.NewGuid() }); uow.SaveChanges(); });
            CloseChecklistDto later = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().GetChecklistAsync(p.Id, CancellationToken.None)));
            Assert.IsFalse(later.Overridden, "the approved override covered the items that were red then");
            Assert.IsFalse(later.CanLock);
            host.Write(uow => { uow.Set<AccountingEvent>().Single(e => e.SourceNumber == "T2").Status = AccountingEventStatus.Posted; uow.SaveChanges(); });
            Ok(await PeriodAct(host, Fin, p.Id, "lock"));
        }

        [TestMethod]
        public async Task ARequestForAnOverrideCanBeWithdrawn()
        {
            AppHost host = NewHost();
            PeriodDto p = await CurrentPeriodAsync(host);
            Ok(await Save(host, Fin, Voucher(null, 100_000m)));
            Ok(await Override(host, Fm, p.Id, "request", "آزمون"));
            CloseChecklistDto withdrawn = Ok(await Override(host, Fm, p.Id, "withdraw"));
            Assert.IsFalse(withdrawn.OverridePending);
            Assert.IsFalse((await Override(host, Fm2, p.Id, "approve")).Success, "nothing is left to approve");
        }

        // ------------------------------------------------------------------------------------------------ D10: year close

        private static Task<ResponseDto<VoucherDto>> ImportOpening(AppHost host, int yearId) =>
            host.As(Fin, sp => sp.GetRequiredService<IGlService>().ImportOpeningAsync(new LedgerOpeningImportDto
            {
                FiscalYearId = yearId,
                Rows = new List<OpeningRowDto> { new OpeningRowDto { AccountCode = "10101", Debit = 1000m }, new OpeningRowDto { AccountCode = "50101", Credit = 1000m } },
            }, CancellationToken.None));

        [TestMethod]
        public async Task ClosingAYearRefusesWhenTheNextYearAlreadyHasAnOpeningVoucher()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            FiscalYearDto y96 = await EnsureYearAsync(host, new DateTime(2017, 6, 1));
            FiscalYearDto y97 = await EnsureYearAsync(host, new DateTime(2018, 6, 1));
            Ok(await ImportOpening(host, y97.Id));
            await LockAllAsync(host, y96);

            ResponseDto<FiscalYearDto> refused = await CloseYear(host, Fm, y96.Id);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "سند افتتاحیه");
            Assert.AreEqual(FnYearStatus.Open, Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().GetYearsAsync(null, CancellationToken.None))).Single(y => y.Id == y96.Id).Status);
        }

        [TestMethod]
        public async Task TheYearCloseRefusesWhileAnEventOfTheYearIsNotPosted()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            FiscalYearDto y96 = await EnsureYearAsync(host, new DateTime(2017, 6, 1));
            host.Write(uow => { AppHost.Add(uow, new AccountingEvent { EventDate = new DateTime(2017, 7, 1), Status = AccountingEventStatus.Failed, LegalEntityId = 1, SourceType = "Test", SourceNumber = "Y1", Key = Guid.NewGuid() }); uow.SaveChanges(); });
            await LockAllAsync(host, y96);
            ResponseDto<FiscalYearDto> refused = await CloseYear(host, Fm, y96.Id);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "رویداد مالی");
        }

        [TestMethod]
        public async Task ADifferenceInTheReconciliationIsPassedOnlyByTwoManagersAndTheYearThenCloses()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            FiscalYearDto y96 = await EnsureYearAsync(host, new DateTime(2017, 6, 1));
            // a final voucher inside the year, so that the close has balances to carry
            VoucherDto v = Ok(await Save(host, Fin, Voucher(null, 700_000m, "آزمون سال", new DateTime(2017, 6, 1))));
            await FinalizeAsync(host, v.Id);
            // a stock value without a ledger balance: the inventory row of the reconciliation is not clean
            host.Write(uow => { AppHost.Add(uow, new ItemCost { ItemId = 1, Value = 5000m }); uow.SaveChanges(); });
            await LockAllAsync(host, y96);

            ResponseDto<FiscalYearDto> none = await CloseYear(host, Fm, y96.Id);
            Assert.IsFalse(none.Success);
            StringAssert.Contains(none.Message, "مغایرت");
            Assert.IsFalse((await CloseYear(host, Fm, y96.Id, true, null)).Success, "a reason is needed");

            ResponseDto<FiscalYearDto> asked = await CloseYear(host, Fm, y96.Id, true, "مغایرت موجودی در دست بررسی است");
            Assert.IsFalse(asked.Success, "the first manager only records the request");
            StringAssert.Contains(asked.Message, "مدیر مالی دیگری");
            ResponseDto<FiscalYearDto> again = await CloseYear(host, Fm, y96.Id, true, "مغایرت موجودی در دست بررسی است");
            Assert.IsFalse(again.Success, "the same manager cannot approve his own request");
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnCloseOverride>().Count(o => o.Kind == FnCloseOverrideKind.YearReconciliation && o.Status == FnCloseOverrideStatus.Pending)), "one request, not two");

            FiscalYearDto closed = Ok(await CloseYear(host, Fm2, y96.Id, true, "تایید"));
            Assert.AreEqual(FnYearStatus.Closed, closed.Status);
            Assert.IsNotNull(closed.ClosingVoucherId);
            FnCloseOverride row = host.Read(uow => uow.Set<FnCloseOverride>().Single(o => o.Kind == FnCloseOverrideKind.YearReconciliation));
            Assert.AreEqual(FnCloseOverrideStatus.Approved, row.Status);
            Assert.AreEqual(32, row.RequestedByUserId);
            Assert.AreEqual(36, row.ApprovedByUserId);
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(a => a.Action == "YearOverrideReq")));
            Assert.AreEqual(1, host.Read(uow => uow.Set<FnAuditLog>().Count(a => a.Action == "YearOverrideOk")));
            Assert.IsTrue(host.Read(uow => uow.Set<FnAuditLog>().Single(a => a.Action == "YearClose").Summary.Contains("عبور از مغایرت")));
        }

        // ------------------------------------------------------------------------------------------------ D11: late posting

        [TestMethod]
        public async Task AnEventDatedInAClosedYearPostsIntoTheFirstOpenPeriodOfTheCurrentYearWithTheFlagAndAnAuditRow()
        {
            AppHost host = NewHost();
            await SettingAsync(host, "CloseChecklist.Enforce", "0");
            FiscalYearDto y96 = await EnsureYearAsync(host, new DateTime(2017, 6, 1));
            await LockAllAsync(host, y96);
            Ok(await CloseYear(host, Fm, y96.Id));
            FiscalYearDto y97 = (await host.As(Fm, sp => sp.GetRequiredService<IGlService>().GetYearsAsync(null, CancellationToken.None))).Data.Single(y => y.Year == 1397);
            Assert.AreEqual(FnYearStatus.Open, y97.Status, "1397 is open, and an old logic would have picked its first period");

            int eventId = 0;
            host.Write(uow =>
            {
                var e = AppHost.Add(uow, new AccountingEvent
                {
                    EventDate = new DateTime(2017, 7, 1), Status = AccountingEventStatus.Pending, LegalEntityId = 1, SourceType = "Test", SourceNumber = "L1", SourceId = 1, Key = Guid.NewGuid(),
                    Description = "رویداد دیرهنگام", CreatedByUserId = 32,
                    Lines = new List<AccountingEventLine>
                    {
                        new AccountingEventLine { RowNumber = 1, AccountRole = "Account:80108", Debit = 250_000m },
                        new AccountingEventLine { RowNumber = 2, AccountRole = "Account:10101", Credit = 250_000m },
                    },
                });
                uow.SaveChanges();
                eventId = e.Id;
            });
            PostEventsResultDto posted = Ok(await host.As(Fin, sp => sp.GetRequiredService<IGlService>().PostEventsAsync(CancellationToken.None)));
            Assert.AreEqual(1, posted.Posted);
            Assert.AreEqual(1, posted.LatePosted);

            FnVoucher v = host.Read(uow => uow.Set<FnVoucher>().Single(x => x.AccountingEventId == eventId));
            FnFiscalYear year = host.Read(uow => uow.Set<FnFiscalYear>().Single(y => y.Id == v.FiscalYearId));
            Assert.IsTrue(v.LatePosting);
            Assert.AreEqual(1405, year.Year, "the current year, not the closed 1396 and not the open but old 1397");
            Assert.AreEqual(year.StartDate.Date, v.Date.Date, "the first open period of the current year starts with the year");
            Assert.AreEqual(32, v.TriggeredByUserId);
            Assert.AreEqual(0, v.CreatedByUserId);
            FnAuditLog note = host.Read(uow => uow.Set<FnAuditLog>().Single(a => a.Action == "LatePosting" && a.EntityId == eventId));
            StringAssert.Contains(note.Summary, "L1");

            // D12: the person whose action made the event cannot finalise its voucher
            Ok(await Get(host, v.Id));
            ResponseDto<VoucherDto> byMaker = await Act(host, Fm, v.Id, "finalize");
            Assert.IsFalse(byMaker.Success);
            StringAssert.Contains(byMaker.Message, "شخص دیگری");
            Assert.AreEqual(FnVoucherStatus.Final, Ok(await Act(host, Fm2, v.Id, "finalize")).Status);
        }

        [TestMethod]
        public void TheCurrentYearLookupSkipsClosedYearsAndNeverGoesBack()
        {
            var year = new FnFiscalYear
            {
                Id = 1, Year = 1405, Status = FnYearStatus.Open, StartDate = new DateTime(2026, 3, 21), EndDate = new DateTime(2027, 3, 20),
                Periods = new List<FnPeriod>
                {
                    new FnPeriod { Id = 1, Number = 1, StartDate = new DateTime(2026, 3, 21), EndDate = new DateTime(2026, 4, 20), Status = FnPeriodStatus.Closed },
                    new FnPeriod { Id = 2, Number = 2, StartDate = new DateTime(2026, 4, 21), EndDate = new DateTime(2026, 5, 21), Status = FnPeriodStatus.Open },
                    new FnPeriod { Id = 3, Number = 3, StartDate = new DateTime(2026, 5, 22), EndDate = new DateTime(2026, 6, 21), Status = FnPeriodStatus.Open },
                },
            };
            foreach (FnPeriod p in year.Periods) p.FiscalYear = year;
            var old = new FnFiscalYear { Id = 2, Year = 1397, Status = FnYearStatus.Open, StartDate = new DateTime(2018, 3, 21), EndDate = new DateTime(2019, 3, 20), Periods = new List<FnPeriod> { new FnPeriod { Id = 9, Number = 1, StartDate = new DateTime(2018, 3, 21), EndDate = new DateTime(2018, 4, 20), Status = FnPeriodStatus.Open } } };
            foreach (FnPeriod p in old.Periods) p.FiscalYear = old;
            var cache = new PeriodCache();
            cache.Add(old);
            cache.Add(year);
            Assert.AreEqual(2, cache.FirstOpenOfCurrentYear(new DateTime(2026, 5, 25)).Id, "the first open period of the year of today, not the old open 1397 period");
            year.Periods.First(p => p.Id == 2).Status = FnPeriodStatus.Closed;
            year.Periods.First(p => p.Id == 3).Status = FnPeriodStatus.Closed;
            Assert.IsNull(cache.FirstOpenOfCurrentYear(new DateTime(2026, 5, 25)), "no open period after today and the old year is never used");
        }

        // ------------------------------------------------------------------------------------------------ D12 / D26

        [TestMethod]
        public async Task OnlyTheCheckerOrTheManagerUnchecksAndOnlyTheCreatorOrTheManagerDeletesADraft()
        {
            AppHost host = NewHost();
            VoucherDto v = Ok(await Save(host, Fin, Voucher(null, 100_000m)));
            ClaimsPrincipal other = AppHost.Principal(39, "fin.other", "FINANCIAL");
            Assert.IsFalse((await host.As(other, sp => sp.GetRequiredService<IGlService>().DeleteVoucherAsync(v.Id, CancellationToken.None))).Success, "another accountant cannot delete my draft");
            Ok(await Act(host, Fm2, v.Id, "check"));
            ResponseDto<VoucherDto> byOther = await Act(host, other, v.Id, "uncheck");
            Assert.IsFalse(byOther.Success, "another accountant cannot take a check back");
            StringAssert.Contains(byOther.Message, "کنترل‌کننده");
            Assert.AreEqual(FnVoucherStatus.Draft, Ok(await Act(host, Fm2, v.Id, "uncheck")).Status, "the checker takes it back");
            Ok(await Act(host, Fm2, v.Id, "check"));
            Assert.AreEqual(FnVoucherStatus.Draft, Ok(await Act(host, Fm, v.Id, "uncheck")).Status, "the finance manager may too");

            VoucherDto mine = Ok(await Save(host, Fin, Voucher(null, 50_000m)));
            Assert.IsTrue((await host.As(Fin, sp => sp.GetRequiredService<IGlService>().DeleteVoucherAsync(mine.Id, CancellationToken.None))).Success, "the creator deletes his own draft");
            Assert.IsTrue((await host.As(Fm, sp => sp.GetRequiredService<IGlService>().DeleteVoucherAsync(v.Id, CancellationToken.None))).Success, "the finance manager deletes another accountant's draft");
        }

        [TestMethod]
        public async Task TemplateDraftsBelongToTheManagerWhoGeneratedThemAndPmoNoLongerOpensYearsOrDrafts()
        {
            AppHost host = NewHost();
            ClaimsPrincipal pmo = AppHost.Principal(40, "pmo.one", "PMO");
            PeriodDto p = await CurrentPeriodAsync(host);
            Ok(await host.As(Fin, sp => sp.GetRequiredService<IGlControlsService>().SaveTemplateAsync(new SaveVoucherTemplateDto
            {
                Title = "اجاره ماهانه", DayOfMonth = 1, IsActive = true,
                Lines = new List<VoucherTemplateLineDto> { new VoucherTemplateLineDto { AccountCode = "80105", Debit = 900_000m }, new VoucherTemplateLineDto { AccountCode = "10101", Credit = 900_000m } },
            }, CancellationToken.None)));

            Assert.IsFalse((await host.As(pmo, sp => sp.GetRequiredService<IGlControlsService>().GenerateTemplatesAsync(new GenerateTemplatesDto { PeriodId = p.Id }, CancellationToken.None))).Success, "PMO does not generate drafts");
            Assert.IsFalse((await host.As(pmo, sp => sp.GetRequiredService<IGlService>().EnsureYearAsync(new EnsureYearDto { Date = new DateTime(2017, 6, 1) }, CancellationToken.None))).Success, "PMO does not open fiscal years");
            GenerateTemplatesResultDto made = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().GenerateTemplatesAsync(new GenerateTemplatesDto { PeriodId = p.Id }, CancellationToken.None)));
            Assert.AreEqual(1, made.Drafted);
            FnVoucher draft = host.Read(uow => uow.Set<FnVoucher>().Single(x => x.Id == made.VoucherIds[0]));
            Assert.AreEqual(32, draft.CreatedByUserId, "the generator is the creator, not the system");

            Assert.IsFalse((await Act(host, Fm, draft.Id, "check")).Success, "the generator cannot check his drafts");
            Ok(await Act(host, Fm2, draft.Id, "check"));
            Assert.IsFalse((await Act(host, Fm, draft.Id, "finalize")).Success, "nor finalise them");
            Assert.AreEqual(FnVoucherStatus.Final, Ok(await Act(host, AppHost.Principal(41, "fm.three", "FINANCEMANAGER"), draft.Id, "finalize")).Status);
        }
    }
}
