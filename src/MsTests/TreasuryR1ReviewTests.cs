using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>R1t review round: M3 (override coverage), M4 (facility recorder), M6 (fee leg again), M7 (closed period), L7 (voucher signature).</summary>
    [TestClass]
    public class TreasuryR1ReviewTests
    {
        private static AppHost NewHost() => FinanceTestSupport.NewHost("TreasuryR1Review");

        private static Task<ResponseDto<FiscalYearDto>> PeriodAct(AppHost host, System.Security.Claims.ClaimsPrincipal who, int id, string action) =>
            host.As(who, sp => sp.GetRequiredService<IGlService>().PeriodActionAsync(new PeriodActionDto { Id = id, Action = action }, CancellationToken.None));

        private static Task<ResponseDto<ReceiptDto>> ReceiptAction(AppHost host, System.Security.Claims.ClaimsPrincipal who, int id, string action, string reference = null) =>
            host.As(who, sp => sp.GetRequiredService<ITreasuryService>().ReceiptActionAsync(new DocActionDto { Id = id, Action = action, Reference = reference }, CancellationToken.None));

        // ------------------------------------------------------------------------------------------------ M3

        [TestMethod]
        public async Task M3_TheApproverCannotApproveARedItemThatTheRequesterNeverAskedFor()
        {
            AppHost host = NewHost();
            List<FiscalYearDto> years = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().GetYearsAsync(null, CancellationToken.None)));
            PeriodDto p = years.SelectMany(y => y.Periods).First(x => x.StartDate <= DateTime.Today && DateTime.Today <= x.EndDate);
            Ok(await host.As(Fin, sp => sp.GetRequiredService<IGlService>().SaveVoucherAsync(new SaveVoucherDto
            {
                Date = DateTime.Today, Description = "پیش‌نویس",
                Lines = new List<SaveVoucherLineDto> { new SaveVoucherLineDto { AccountCode = "80108", Debit = 1000m }, new SaveVoucherLineDto { AccountCode = "10101", Credit = 1000m } },
            }, CancellationToken.None)));
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().OverrideChecklistAsync(p.Id, new ChecklistOverrideDto { Action = "request", Reason = "پیش‌نویس منتقل می‌شود" }, CancellationToken.None)));

            // a new red item appears after the request: an event that waits
            host.Write(uow => { AppHost.Add(uow, new AccountingEvent { EventDate = DateTime.Today, Status = AccountingEventStatus.Pending, LegalEntityId = 1, SourceType = "Test", SourceNumber = "M3", Key = Guid.NewGuid() }); uow.SaveChanges(); });
            ResponseDto<CloseChecklistDto> refused = await host.As(Fm2, sp => sp.GetRequiredService<IGlControlsService>().OverrideChecklistAsync(p.Id, new ChecklistOverrideDto { Action = "approve" }, CancellationToken.None));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "در درخواست نبوده");

            // the requester asks again with the present list and the other manager can approve it
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().OverrideChecklistAsync(p.Id, new ChecklistOverrideDto { Action = "request", Reason = "پیش‌نویس و رویداد منتقل می‌شود" }, CancellationToken.None)));
            Assert.IsTrue(Ok(await host.As(Fm2, sp => sp.GetRequiredService<IGlControlsService>().OverrideChecklistAsync(p.Id, new ChecklistOverrideDto { Action = "approve" }, CancellationToken.None))).Overridden);
        }

        // ------------------------------------------------------------------------------------------------ M4

        [TestMethod]
        public async Task M4_ThePersonWhoRecordedTheLoanNeitherApprovesNorPostsItsLegs()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "5234567890123456789012");
            // the finance manager records the loan itself; an accountant presses "disburse"
            FnLoanDto loan = Ok(await host.As(Fm, sp => sp.GetRequiredService<ILoanService>().SaveLoanAsync(new FnSaveLoanDto
            {
                BankName = "بانک آزمون", Amount = 500_000_000m, FeeAmount = 0m, RatePercent = 18, ScheduleKind = FnScheduleKind.EqualPrincipal, InstallmentCount = 2, StartDate = DateTime.Today, BankAccountId = bank,
            }, CancellationToken.None)));
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;

            ResponseDto<ReceiptDto> own = await ReceiptAction(host, Fm, receipt, "approve");
            Assert.IsFalse(own.Success, "the finance manager who recorded the loan does not approve its leg");
            StringAssert.Contains(own.Message, "ثبت‌کننده رکورد");
            Ok(await ReceiptAction(host, Fm2, receipt, "approve"));
            Ok(await ReceiptAction(host, Tr, receipt, "post", "TRK-M4"));
            Assert.AreEqual(500_000_000m, await BalanceAsync(host, bank));
        }

        [TestMethod]
        public async Task M4_ATreasuryUserWhoRecordedTheLoanCannotPostTheLegAnAccountantDrafted()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "6234567890123456789012");
            FnLoanDto loan = Ok(await host.As(FinTr, sp => sp.GetRequiredService<ILoanService>().SaveLoanAsync(new FnSaveLoanDto
            {
                BankName = "بانک آزمون", Amount = 300_000_000m, FeeAmount = 0m, RatePercent = 18, ScheduleKind = FnScheduleKind.EqualPrincipal, InstallmentCount = 2, StartDate = DateTime.Today, BankAccountId = bank,
            }, CancellationToken.None)));
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;
            Ok(await ReceiptAction(host, Fm, receipt, "approve"));
            ResponseDto<ReceiptDto> refused = await ReceiptAction(host, FinTr, receipt, "post", "TRK-M4B");
            Assert.IsFalse(refused.Success, "the recorder of the loan is not the creator of the leg, yet he does not post it");
            StringAssert.Contains(refused.Message, "ثبت‌کننده رکورد");
            Ok(await ReceiptAction(host, Tr, receipt, "post", "TRK-M4B"));
        }

        // ------------------------------------------------------------------------------------------------ M7

        [TestMethod]
        public async Task M7_ATreasuryDocumentDatedInAClosedPeriodIsRefused()
        {
            AppHost host = NewHost();
            await host.As(Fm, sp => sp.GetRequiredService<IGlControlsService>().SaveSettingAsync(new SaveFinanceSettingDto { Key = "CloseChecklist.Enforce", Value = "0" }, CancellationToken.None));
            FiscalYearDto y = Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().EnsureYearAsync(new EnsureYearDto { Date = new DateTime(2017, 6, 1) }, CancellationToken.None)));
            PeriodDto p = y.Periods.First(x => x.StartDate <= new DateTime(2017, 6, 10) && new DateTime(2017, 6, 10) <= x.EndDate);
            Ok(await PeriodAct(host, Fin, p.Id, "lock"));
            Ok(await PeriodAct(host, Ceo, p.Id, "close"));
            int bank = await BankAsync(host, "7234567890123456789012");
            int customer = 0;
            host.Write(uow => { FnCustomer c = AppHost.Add(uow, new FnCustomer { Name = "مشتری آزمون", IsActive = true }); uow.SaveChanges(); customer = c.Id; });

            ResponseDto<ReceiptDto> receipt = await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().SaveReceiptAsync(new SaveReceiptDto
            {
                Date = new DateTime(2017, 6, 10), CustomerId = customer, Lines = new List<ReceiptLineDto> { new ReceiptLineDto { Instrument = FnInstrument.BankTransfer, Amount = 1_000_000m, BankAccountId = bank } },
            }, CancellationToken.None));
            Assert.IsFalse(receipt.Success);
            StringAssert.Contains(receipt.Message, "بسته‌شده");

            // a date in an open period is fine
            ResponseDto<ReceiptDto> today = await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().SaveReceiptAsync(new SaveReceiptDto
            {
                Date = DateTime.Today, CustomerId = customer, Lines = new List<ReceiptLineDto> { new ReceiptLineDto { Instrument = FnInstrument.BankTransfer, Amount = 1_000_000m, BankAccountId = bank } },
            }, CancellationToken.None));
            Assert.IsTrue(today.Success, today.Message);
        }

        // ------------------------------------------------------------------------------------------------ M6

        [TestMethod]
        public async Task M6_ACancelledGuaranteeFeeCanBeDraftedAgainAndNotTwice()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "8234567890123456789012");
            FnBankGuaranteeDto g = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().SaveBankGuaranteeAsync(new FnSaveBankGuaranteeDto
            {
                Direction = FnBankGuaranteeDirection.Issued, Kind = FnBankGuaranteeKind.Performance, BankName = "بانک آزمون", BankAccountId = bank, PartyName = "طرف آزمون", Amount = 1_000_000_000m,
                IssueDate = DateTime.Today, ExpiryDate = DateTime.Today.AddDays(365), FeeAmount = 1_200_000m,
            }, CancellationToken.None)));
            Assert.AreEqual(1, g.OpenCashLegs.Count);
            Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().PaymentActionAsync(new DocActionDto { Id = g.OpenCashLegs[0].Id, Action = "cancel" }, CancellationToken.None)));

            ResponseDto<FnBankGuaranteeDto> none = await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().BankGuaranteeActionAsync(new FnGuaranteeActionDto { Id = g.Id, Action = "fee-leg" }, CancellationToken.None));
            Assert.IsTrue(none.Success, none.Message);
            Assert.AreEqual(1, none.Data.OpenCashLegs.Count);
            Assert.AreEqual(1_200_000m, none.Data.OpenCashLegs[0].Amount);

            ResponseDto<FnBankGuaranteeDto> twice = await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().BankGuaranteeActionAsync(new FnGuaranteeActionDto { Id = g.Id, Action = "fee-leg" }, CancellationToken.None));
            Assert.IsFalse(twice.Success, "an open leg exists: nothing is drafted twice");
            Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().PaymentActionAsync(new DocActionDto { Id = none.Data.OpenCashLegs[0].Id, Action = "cancel" }, CancellationToken.None)));
            Assert.IsTrue((await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().BankGuaranteeActionAsync(new FnGuaranteeActionDto { Id = g.Id, Action = "fee-leg" }, CancellationToken.None))).Success);
        }

        // ------------------------------------------------------------------------------------------------ L7

        [TestMethod]
        public void L7_TheVoucherSignatureDoesNotDependOnTheRowIds()
        {
            FnVoucherLine Line(int id, decimal debit, decimal credit, int row) => new FnVoucherLine { Id = id, RowNumber = row, AccountId = row, Debit = debit, Credit = credit, Description = "ردیف" };
            var stored = new List<FnVoucherLine> { Line(11, 100m, 0m, 1), Line(12, 0m, 100m, 2) };
            var built = new List<FnVoucherLine> { Line(0, 100m, 0m, 1), Line(0, 0m, 100m, 2) };
            DateTime d = new DateTime(2026, 1, 1);
            Assert.AreEqual(GlService.VoucherSignature(d, "شرح", false, stored), GlService.VoucherSignature(d, "شرح", false, built), "a re-save with new objects is no change");
            built[0].Debit = 101m; built[1].Credit = 101m;
            Assert.AreNotEqual(GlService.VoucherSignature(d, "شرح", false, stored), GlService.VoucherSignature(d, "شرح", false, built), "an amount is a change");
        }
    }
}
