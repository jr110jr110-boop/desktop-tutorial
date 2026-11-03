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
using ViewModels.NasbNiro.Pagination;
using static MsTests.FinanceTestSupport;

namespace MsTests
{
    /// <summary>
    /// R1t D7: the cash legs of loans, letters of credit and guarantees go through treasury documents (approved by the finance manager, settled by a treasury user
    /// other than the person who recorded or approved them) and the bank reconciliation accepts a difference only with explained reconciling items.
    /// The real services run on the in-memory database.
    /// </summary>
    [TestClass]
    public class TreasuryR1Tests
    {
        private static AppHost NewHost() => FinanceTestSupport.NewHost("TreasuryR1");

        private static async Task<FnLoanDto> NewLoanAsync(AppHost host, int bankId, decimal amount, decimal fee, int count = 4)
        {
            return Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().SaveLoanAsync(new FnSaveLoanDto
            {
                BankName = "بانک آزمون", Amount = amount, FeeAmount = fee, RatePercent = 18, ScheduleKind = FnScheduleKind.EqualPrincipal, InstallmentCount = count,
                StartDate = DateTime.Today, BankAccountId = bankId,
            }, CancellationToken.None)));
        }

        private static Task<ResponseDto<ReceiptDto>> ReceiptAction(AppHost host, ClaimsPrincipal who, int id, string action, string reference = null) =>
            host.As(who, sp => sp.GetRequiredService<ITreasuryService>().ReceiptActionAsync(new DocActionDto { Id = id, Action = action, Reference = reference }, CancellationToken.None));

        private static Task<ResponseDto<PaymentDto>> PaymentAction(AppHost host, ClaimsPrincipal who, int id, string action, string reference = null) =>
            host.As(who, sp => sp.GetRequiredService<ITreasuryService>().PaymentActionAsync(new DocActionDto { Id = id, Action = action, Reference = reference }, CancellationToken.None));

        // ------------------------------------------------------------------------------------------------ loan disbursement

        [TestMethod]
        public async Task LoanDisbursementWaitsForTreasuryAndMovesTheBankBalanceOnlyWhenSettled()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "1234567890123456789012");
            FnLoanDto loan = await NewLoanAsync(host, bank, 1_000_000_000m, 10_000_000m);

            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            Assert.AreEqual(FnLoanStatus.Disbursing, waiting.Status, "the loan only waits for the money");
            Assert.AreEqual(1, waiting.OpenCashLegs.Count);
            FnCashLegRefDto leg = waiting.OpenCashLegs[0];
            Assert.AreEqual("Receipt", leg.Kind);
            Assert.AreEqual(990_000_000m, leg.Amount, "the money that comes in is the loan less the bank fee");
            Assert.AreEqual(0m, await BalanceAsync(host, bank), "nothing is in the bank book before treasury settles the receipt");

            // a second request while the first waits is refused
            ResponseDto<FnLoanDto> again = await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None));
            Assert.IsFalse(again.Success);

            // the document cannot be edited by hand (a real customer, so that the refusal is the leg rule and nothing else)
            int customer = 0;
            host.Write(uow => { FnCustomer c = AppHost.Add(uow, new FnCustomer { Name = "مشتری آزمون", IsActive = true }); uow.SaveChanges(); customer = c.Id; });
            ResponseDto<ReceiptDto> edit = await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().SaveReceiptAsync(new SaveReceiptDto
            {
                Id = leg.Id, Date = DateTime.Today, CustomerId = customer, Lines = new List<ReceiptLineDto> { new ReceiptLineDto { Instrument = FnInstrument.BankTransfer, Amount = 990_000_000m, BankAccountId = bank } },
            }, CancellationToken.None));
            Assert.IsFalse(edit.Success, "a leg document is not edited by hand");
            StringAssert.Contains(edit.Message, "فقط از خود آن رکورد");

            // the recorder cannot approve; the finance manager does
            Assert.IsFalse((await ReceiptAction(host, Fin, leg.Id, "approve")).Success, "FINANCIAL is not a manager");
            Assert.AreEqual(FnDocStatus.Approved, Ok(await ReceiptAction(host, Fm, leg.Id, "approve")).Status);

            // the approver and the recorder cannot settle, even with the treasury role
            Assert.IsFalse((await ReceiptAction(host, Fm, leg.Id, "post", "TRK-1")).Success, "the approver cannot settle");
            Assert.IsFalse((await ReceiptAction(host, Fin, leg.Id, "post", "TRK-1")).Success, "the recorder has no treasury role");
            Assert.AreEqual(0m, await BalanceAsync(host, bank));

            ReceiptDto posted = Ok(await ReceiptAction(host, Tr, leg.Id, "post", "TRK-1"));
            Assert.AreEqual(FnDocStatus.Posted, posted.Status);
            Assert.AreEqual(990_000_000m, await BalanceAsync(host, bank), "the bank book shows exactly the net amount");

            FnLoanDto active = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().GetLoanAsync(loan.Id, CancellationToken.None)));
            Assert.AreEqual(FnLoanStatus.Active, active.Status);
            Assert.AreEqual(0, active.OpenCashLegs.Count);
            Assert.AreEqual(bank, active.BankAccountId);
        }

        [TestMethod]
        public async Task ARecorderWhoAlsoHasTheTreasuryRoleCannotSettleTheirOwnLeg()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "2234567890123456789012");
            FnLoanDto loan = await NewLoanAsync(host, bank, 400_000_000m, 0m, 2);
            FnLoanDto waiting = Ok(await host.As(FinTr, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;
            Ok(await ReceiptAction(host, Fm, receipt, "approve"));
            ResponseDto<ReceiptDto> refused = await ReceiptAction(host, FinTr, receipt, "post", "TRK-2");
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "ثبت‌کننده");
            Assert.AreEqual(0m, await BalanceAsync(host, bank));
            Ok(await ReceiptAction(host, Tr2, receipt, "post", "TRK-2"));
            Assert.AreEqual(400_000_000m, await BalanceAsync(host, bank));
        }

        [TestMethod]
        public async Task CancellingTheReceiptGivesTheLoanBack()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "3234567890123456789012");
            FnLoanDto loan = await NewLoanAsync(host, bank, 300_000_000m, 3_000_000m, 3);
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            Assert.IsFalse((await ReceiptAction(host, Fin, waiting.OpenCashLegs.Single().Id, "cancel")).Success, "cancelling is a treasury action");
            Assert.AreEqual(FnDocStatus.Cancelled, Ok(await ReceiptAction(host, Tr, waiting.OpenCashLegs.Single().Id, "cancel")).Status);
            FnLoanDto back = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().GetLoanAsync(loan.Id, CancellationToken.None)));
            Assert.AreEqual(FnLoanStatus.Draft, back.Status);
            Assert.AreEqual(0, back.OpenCashLegs.Count);
            Assert.AreEqual(0m, await BalanceAsync(host, bank));
            FnLoanDto second = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            Assert.AreEqual(FnLoanStatus.Disbursing, second.Status);
        }

        // ------------------------------------------------------------------------------------------------ instalment payment

        [TestMethod]
        public async Task AnInstallmentIsPaidThroughATreasuryPaymentAndBooksPrincipalAndInterest()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "4234567890123456789012");
            FnLoanDto loan = await NewLoanAsync(host, bank, 1_200_000_000m, 0m, 4);
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;
            Ok(await ReceiptAction(host, Fm, receipt, "approve"));
            Ok(await ReceiptAction(host, Tr, receipt, "post", "TRK-A"));
            Assert.AreEqual(1_200_000_000m, await BalanceAsync(host, bank));

            FnLoanDto loaded = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().GetLoanAsync(loan.Id, CancellationToken.None)));
            FnInstallmentDto first = loaded.Installments.First(i => i.Sequence == 1);
            decimal total = first.Principal + first.Interest;

            FnLoanDto paying = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().PayInstallmentAsync(new FnLoanPayDto { LoanId = loan.Id, Sequence = 1, BankAccountId = bank }, CancellationToken.None)));
            Assert.AreEqual(2, paying.Installments.First(i => i.Sequence == 1).Status, "the instalment waits for treasury");
            FnCashLegRefDto leg = paying.OpenCashLegs.Single();
            Assert.AreEqual("Payment", leg.Kind);
            Assert.AreEqual(total, leg.Amount);
            Assert.AreEqual(1_200_000_000m, await BalanceAsync(host, bank), "the bank balance moves only when treasury settles");

            // the same instalment cannot be requested twice, and the next one waits for the first
            Assert.IsFalse((await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().PayInstallmentAsync(new FnLoanPayDto { LoanId = loan.Id, Sequence = 1, BankAccountId = bank }, CancellationToken.None))).Success);
            Assert.IsFalse((await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().PayInstallmentAsync(new FnLoanPayDto { LoanId = loan.Id, Sequence = 2, BankAccountId = bank }, CancellationToken.None))).Success);

            Assert.IsFalse((await PaymentAction(host, Fin, leg.Id, "approve")).Success, "FINANCIAL cannot approve");
            Ok(await PaymentAction(host, Fm, leg.Id, "approve"));
            Assert.IsFalse((await PaymentAction(host, Fm, leg.Id, "post", "TRK-B")).Success, "the approver cannot settle");
            PaymentDto posted = Ok(await PaymentAction(host, Tr, leg.Id, "post", "TRK-B"));
            Assert.AreEqual(FnDocStatus.Posted, posted.Status);
            Assert.AreEqual(1_200_000_000m - total, await BalanceAsync(host, bank), "the bank book shows exactly principal plus interest");

            FnLoanDto after = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().GetLoanAsync(loan.Id, CancellationToken.None)));
            Assert.AreEqual(1, after.Installments.First(i => i.Sequence == 1).Status);
            Assert.AreEqual(0, after.OpenCashLegs.Count);
        }

        // ------------------------------------------------------------------------------------------------ ledger side of a settled leg

        [TestMethod]
        public async Task ASettledDisbursementPostsTheLiabilityTheFeeAndTheBankInOneBalancedVoucher()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "5234567890123456789012");
            FnLoanDto loan = await NewLoanAsync(host, bank, 1_000_000_000m, 10_000_000m);
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;
            Ok(await ReceiptAction(host, Fm, receipt, "approve"));
            Ok(await ReceiptAction(host, Tr, receipt, "post", "TRK-L"));

            PostEventsResultDto posted = await host.As(Fin, sp => sp.GetRequiredService<IEventPostingService>().PostPendingAsync(CancellationToken.None));
            Assert.IsTrue(posted.Posted >= 1);
            var lines = host.Read(uow => uow.Set<FnVoucherLine>().Where(l => l.Voucher.SourceType == "FnReceipt").Select(l => new { l.Account.Code, l.Debit, l.Credit }).ToList());
            Assert.AreEqual(1_000_000_000m, lines.Where(l => l.Code == "30501").Sum(l => l.Credit), "the loan is a short-term liability (4 instalments)");
            Assert.AreEqual(10_000_000m, lines.Where(l => l.Code == "80201").Sum(l => l.Debit), "the bank fee is an expense");
            Assert.AreEqual(990_000_000m, lines.Where(l => l.Code == "10102").Sum(l => l.Debit), "the bank account receives the net amount");
            Assert.AreEqual(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        }

        // ------------------------------------------------------------------------------------------------ letter of credit

        [TestMethod]
        public async Task ALetterOfCreditMarginAndSettlementWaitForTreasuryAndOnlyOneLegIsOpenAtATime()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "6234567890123456789012");
            int provider = 0;
            host.Write(uow => { Entities.Model.NasbNiro.Providers.Provider p = AppHost.Add(uow, new Entities.Model.NasbNiro.Providers.Provider { Name = "تامین‌کننده آزمون" }); uow.SaveChanges(); provider = p.Id; });
            LetterOfCreditDto lc = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().SaveLetterOfCreditAsync(new SaveLetterOfCreditDto
            {
                LcNumber = "LC-1", ProviderId = provider, BankAccountId = bank, CurrencyCode = "IRR", Amount = 500_000_000m, OpenDate = DateTime.Today, ExpiryDate = DateTime.Today.AddDays(90), MarginPercent = 20,
            }, CancellationToken.None)));

            LetterOfCreditDto margin = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().LetterOfCreditActionAsync(new LcActionDto { Id = lc.Id, Action = "margin", Amount = 100_000_000m }, CancellationToken.None)));
            Assert.AreEqual(0m, margin.MarginPaid, "the margin is recorded only when treasury settles the payment");
            FnCashLegRefDto leg = margin.OpenCashLegs.Single();
            Assert.AreEqual("Payment", leg.Kind);
            Assert.AreEqual(100_000_000m, leg.Amount);
            Assert.IsFalse((await host.As(Fin, sp => sp.GetRequiredService<IFxService>().LetterOfCreditActionAsync(new LcActionDto { Id = lc.Id, Action = "fee", Amount = 1_000_000m }, CancellationToken.None))).Success, "one open leg at a time");

            Ok(await PaymentAction(host, Fm, leg.Id, "approve"));
            Assert.IsFalse((await PaymentAction(host, Fm, leg.Id, "post", "TRK-M")).Success);
            Ok(await PaymentAction(host, Tr, leg.Id, "post", "TRK-M"));
            Assert.AreEqual(-100_000_000m, await BalanceAsync(host, bank));
            LetterOfCreditDto afterMargin = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().GetLetterOfCreditAsync(lc.Id, CancellationToken.None)));
            Assert.AreEqual(100_000_000m, afterMargin.MarginPaid);
            Assert.AreEqual(0, afterMargin.OpenCashLegs.Count);

            Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().LetterOfCreditActionAsync(new LcActionDto { Id = lc.Id, Action = "documents", ForeignInvoiceRef = "INV-1" }, CancellationToken.None)));
            LetterOfCreditDto paying = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().LetterOfCreditActionAsync(new LcActionDto { Id = lc.Id, Action = "pay", Rate = 1m }, CancellationToken.None)));
            Assert.AreEqual(FnLcStatus.DocumentsReceived, paying.Status, "the letter of credit is paid only when treasury settles the cash part");
            FnCashLegRefDto pay = paying.OpenCashLegs.Single();
            Assert.AreEqual(400_000_000m, pay.Amount, "500m less the 100m margin");
            Ok(await PaymentAction(host, Fm, pay.Id, "approve"));
            Ok(await PaymentAction(host, Tr2, pay.Id, "post", "TRK-P"));
            Assert.AreEqual(-500_000_000m, await BalanceAsync(host, bank));
            LetterOfCreditDto paid = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFxService>().GetLetterOfCreditAsync(lc.Id, CancellationToken.None)));
            Assert.AreEqual(FnLcStatus.Paid, paid.Status);
            Assert.AreEqual(0m, paid.MarginPaid, "the margin deposit was released against the settlement");
            Assert.AreEqual(500_000_000m, paid.SettledBase);
        }

        // ------------------------------------------------------------------------------------------------ bank guarantee fee

        [TestMethod]
        public async Task TheFeeOfAnIssuedGuaranteeIsPaidByTreasuryAndOnlyThenSpreadOverItsLife()
        {
            AppHost host = NewHost();
            int bank = await BankAsync(host, "7234567890123456789012");
            FnBankGuaranteeDto g = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().SaveBankGuaranteeAsync(new FnSaveBankGuaranteeDto
            {
                Direction = FnBankGuaranteeDirection.Issued, Kind = FnBankGuaranteeKind.Performance, BankName = "بانک آزمون", BankAccountId = bank, GuaranteeNo = "G-1", PartyName = "کارفرمای آزمون",
                Amount = 2_000_000_000m, IssueDate = DateTime.Today, ExpiryDate = DateTime.Today.AddDays(360), FeeAmount = 24_000_000m,
            }, CancellationToken.None)));
            Assert.AreEqual(0m, g.FeeAmount, "no fee is booked before treasury pays it");
            FnCashLegRefDto leg = g.OpenCashLegs.Single();
            Assert.AreEqual(24_000_000m, leg.Amount);
            Assert.AreEqual(0m, await BalanceAsync(host, bank));
            Assert.IsFalse((await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().BankGuaranteeActionAsync(new FnGuaranteeActionDto { Id = g.Id, Action = "release" }, CancellationToken.None))).Success, "the guarantee waits for its fee leg");
            Ok(await PaymentAction(host, Fm, leg.Id, "approve"));
            Ok(await PaymentAction(host, Tr, leg.Id, "post", "TRK-G"));
            Assert.AreEqual(-24_000_000m, await BalanceAsync(host, bank));
            List<FnBankGuaranteeDto> all = Ok(await host.As(Fin, sp => sp.GetRequiredService<IFinanceCompletenessService>().GetBankGuaranteesAsync(null, CancellationToken.None)));
            FnBankGuaranteeDto after = all.First(y => y.Id == g.Id);
            Assert.AreEqual(24_000_000m, after.FeeAmount, "the deferral that spreads the fee exists now");
            Assert.AreEqual(0, after.OpenCashLegs.Count);
        }

        // ------------------------------------------------------------------------------------------------ reconciliation

        private static Task<ResponseDto<ReconciliationDto>> ReconAction(AppHost host, ClaimsPrincipal who, int id, string action, List<ReconciliationItemDto> items = null) =>
            host.As(who, sp => sp.GetRequiredService<ITreasuryService>().ReconciliationActionAsync(new DocActionDto { Id = id, Action = action, Items = items }, CancellationToken.None));

        /// <summary>A bank whose books show 990m (a settled loan receipt) and whose statement shows 1,040m: 50m existed before the system went live.</summary>
        private static async Task<(int Bank, int Reconciliation)> ReconciliationWithDifferenceAsync(AppHost host, string digits)
        {
            int bank = await BankAsync(host, digits);
            FnLoanDto loan = await NewLoanAsync(host, bank, 1_000_000_000m, 10_000_000m);
            FnLoanDto waiting = Ok(await host.As(Fin, sp => sp.GetRequiredService<ILoanService>().LoanActionAsync(new FnLoanActionDto { Id = loan.Id, Action = "disburse", BankAccountId = bank }, CancellationToken.None)));
            int receipt = waiting.OpenCashLegs.Single().Id;
            Ok(await ReceiptAction(host, Fm, receipt, "approve"));
            Ok(await ReceiptAction(host, Tr, receipt, "post", "TRK-R"));
            BankStatementDto st = Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().ImportStatementAsync(new ImportStatementDto
            {
                BankAccountId = bank, OpeningBalance = 50_000_000m, ClosingBalance = 1_040_000_000m,
                Rows = new List<StatementRowDto> { new StatementRowDto { Date = DateTime.Today, Description = "واریز تسهیلات TRK-R", Deposit = 990_000_000m, Reference = "TRK-R" } },
            }, CancellationToken.None)));
            BankStatementDto matched = Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().AutoMatchAsync(st.Id, CancellationToken.None)));
            Assert.AreEqual(FnStatementMatch.Receipt, matched.Lines.Single().MatchKind, "the settled loan receipt is found in the bank book and matched to the statement line");
            ReconciliationDto rec = Ok(await host.As(Fin, sp => sp.GetRequiredService<ITreasuryService>().CreateReconciliationAsync(new CreateReconciliationDto { StatementId = st.Id }, CancellationToken.None)));
            Assert.AreEqual(50_000_000m, rec.Difference);
            Assert.AreEqual(0, rec.UnmatchedLines);
            return (bank, rec.Id);
        }

        [TestMethod]
        public async Task AReconciliationWithADifferenceIsApprovedOnlyWithItemsThatAddUpToIt()
        {
            AppHost host = NewHost();
            var (_, rec) = await ReconciliationWithDifferenceAsync(host, "8234567890123456789012");

            ResponseDto<ReconciliationDto> none = await ReconAction(host, Fm, rec, "approve");
            Assert.IsFalse(none.Success);
            StringAssert.Contains(none.Message, "مغایرت صفر نیست");

            var wrong = new List<ReconciliationItemDto> { new ReconciliationItemDto { Kind = 5, Amount = 49_000_000m, Note = "مانده قبل از شروع کار سیستم" } };
            Ok(await ReconAction(host, Fin, rec, "items", wrong));
            ResponseDto<ReconciliationDto> off = await ReconAction(host, Fm, rec, "approve");
            Assert.IsFalse(off.Success, "the items must add up to exactly the difference");
            StringAssert.Contains(off.Message, "برابر نیست");

            // an item needs a known kind, an amount and a reason
            Assert.IsFalse((await ReconAction(host, Fin, rec, "items", new List<ReconciliationItemDto> { new ReconciliationItemDto { Kind = 5, Amount = 50_000_000m, Note = " " } })).Success);
            Assert.IsFalse((await ReconAction(host, Fin, rec, "items", new List<ReconciliationItemDto> { new ReconciliationItemDto { Kind = 77, Amount = 50_000_000m, Note = "x" } })).Success);
            Assert.IsFalse((await ReconAction(host, Fin, rec, "items", new List<ReconciliationItemDto> { new ReconciliationItemDto { Kind = 5, Amount = 0m, Note = "x" } })).Success);

            var right = new List<ReconciliationItemDto>
            {
                new ReconciliationItemDto { Kind = 5, Amount = 60_000_000m, Note = "مانده قبل از شروع کار سیستم" },
                new ReconciliationItemDto { Kind = 1, Amount = -10_000_000m, Note = "کارمزد بانکی ثبت‌نشده" },
            };
            ReconciliationDto saved = Ok(await ReconAction(host, Fm, rec, "items", right));
            Assert.AreEqual(50_000_000m, saved.ExplainedAmount);
            Assert.AreEqual(2, saved.Items.Count);

            // the person who saved the items cannot approve; another finance manager can
            ResponseDto<ReconciliationDto> self = await ReconAction(host, Fm, rec, "approve");
            Assert.IsFalse(self.Success);
            StringAssert.Contains(self.Message, "اقلام مغایرت");
            Assert.IsFalse((await ReconAction(host, Fin, rec, "approve")).Success, "the preparer is not a manager");
            ReconciliationDto approved = Ok(await ReconAction(host, Fm2, rec, "approve"));
            Assert.AreEqual(1, approved.Status);
            Assert.AreEqual(50_000_000m, approved.Difference);
            Assert.AreEqual(50_000_000m, approved.ExplainedAmount);

            // an approved reconciliation keeps its items
            Assert.IsFalse((await ReconAction(host, Fin, rec, "items", new List<ReconciliationItemDto>())).Success);
        }
    }
}
