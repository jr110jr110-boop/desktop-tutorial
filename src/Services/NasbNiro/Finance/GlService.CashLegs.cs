using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // R1t D7: the cash legs of loans, letters of credit and bank guarantees go through treasury.
    //  - the recording side (loan, LC and guarantee actions) drafts a treasury document with the bank account (a payment or a receipt) and changes nothing else;
    //  - treasury (TreasuryService) approves and settles the document through the normal flow; settling calls SettleAsync below, which builds the ledger side of the accounting event
    //    (treasury adds the bank line, so the bank book and the reconciliation see the leg) and completes the facility record (the loan becomes active, the instalment is paid ...).
    public partial class GlService : ICashLegService
    {
        /// <summary>Everything a leg needs to be settled later; it is stored as JSON on the treasury document, so the ledger side is computed from the facts at settlement time.</summary>
        private sealed class LegData
        {
            public int? Sequence { get; set; }
            public decimal? Penalty { get; set; }
            public decimal? Rate { get; set; }
            public string Note { get; set; }
            public DateTime? From { get; set; }
            public DateTime? To { get; set; }
            public string Text { get; set; }
        }

        private sealed class LegSpec
        {
            public string SourceType;
            public int SourceId;
            public string Action;
            public bool Inflow;
            public int BankAccountId;
            public decimal Amount;
            public DateTime Date;
            public string Party;
            public string Description;
            public LegData Data = new LegData();
        }

        private static string PackLeg(LegData d) => JsonSerializer.Serialize(d ?? new LegData());

        private static LegData UnpackLeg(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new LegData() : JsonSerializer.Deserialize<LegData>(json) ?? new LegData(); }
            catch (JsonException) { return new LegData(); }
        }

        private static string Direct(FnAccount account) => EventPostingService.DirectRolePrefix + account.Code;

        // ------------------------------------------------------------------------------------------ recording side

        /// <summary>The refusal text when the facility record still has a treasury document that is not settled or cancelled (one open document per record, so the state it depends on cannot move under it).</summary>
        private async Task<string> OpenLegMessageAsync(string sourceType, int sourceId, CancellationToken ct)
        {
            string payment = await _uow.Set<FnPayment>().AsNoTracking().Where(p => p.SourceType == sourceType && p.SourceId == sourceId && (p.Status == FnDocStatus.Draft || p.Status == FnDocStatus.Approved))
                .Select(p => p.Number).FirstOrDefaultAsync(ct);
            string receipt = payment != null ? null : await _uow.Set<FnReceipt>().AsNoTracking().Where(r => r.SourceType == sourceType && r.SourceId == sourceId && (r.Status == FnDocStatus.Draft || r.Status == FnDocStatus.Approved))
                .Select(r => r.Number).FirstOrDefaultAsync(ct);
            string number = payment ?? receipt;
            return number == null ? null : $"سند خزانه {number} برای این رکورد هنوز نهایی نشده است؛ ابتدا خزانه آن را تایید و ثبت کند یا باطل شود.";
        }

        private async Task<List<FnCashLegRefDto>> OpenLegsAsync(string sourceType, IReadOnlyCollection<int> sourceIds, CancellationToken ct)
        {
            var list = new List<FnCashLegRefDto>();
            if (sourceIds.Count == 0) return list;
            var ids = sourceIds.ToList();
            foreach (var p in await _uow.Set<FnPayment>().AsNoTracking().Where(p => p.SourceType == sourceType && p.SourceId != null && ids.Contains(p.SourceId.Value) && (p.Status == FnDocStatus.Draft || p.Status == FnDocStatus.Approved))
                         .Select(p => new { p.Id, p.Number, p.Status, p.NetAmount, p.SourceAction, p.Date, p.SourceId }).ToListAsync(ct))
                list.Add(new FnCashLegRefDto { Kind = "Payment", Id = p.Id, Number = p.Number, Status = (int)p.Status, Amount = p.NetAmount, Action = p.SourceAction, Date = p.Date, SourceId = p.SourceId ?? 0 });
            foreach (var r in await _uow.Set<FnReceipt>().AsNoTracking().Where(r => r.SourceType == sourceType && r.SourceId != null && ids.Contains(r.SourceId.Value) && (r.Status == FnDocStatus.Draft || r.Status == FnDocStatus.Approved))
                         .Select(r => new { r.Id, r.Number, r.Status, r.TotalAmount, r.SourceAction, r.Date, r.SourceId }).ToListAsync(ct))
                list.Add(new FnCashLegRefDto { Kind = "Receipt", Id = r.Id, Number = r.Number, Status = (int)r.Status, Amount = r.TotalAmount, Action = r.SourceAction, Date = r.Date, SourceId = r.SourceId ?? 0 });
            return list.OrderBy(x => x.Id).ToList();
        }

        /// <summary>Adds (does not save) the treasury document of a cash leg: a payment when the money leaves the bank, a receipt when it comes in. Returns the refusal text, or null.</summary>
        private async Task<string> AddCashLegAsync(LegSpec s, CancellationToken ct)
        {
            if (s.Amount <= 0) return "مبلغ سند خزانه باید بیشتر از صفر باشد.";
            string open = await OpenLegMessageAsync(s.SourceType, s.SourceId, ct);
            if (open != null) return open;
            FnBankAccount bank = await _uow.Set<FnBankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == s.BankAccountId, ct);
            if (bank == null || !bank.IsActive) return "حساب بانکی پیدا نشد یا غیرفعال است.";
            if (!FinanceFeatures.IsRial(bank.CurrencyCode)) return "حساب بانکی ارزی در این مسیر پشتیبانی نمی‌شود؛ حساب ریالی را انتخاب کنید.";
            string party = Clean(s.Party) ?? bank.BankName;
            if (party != null && party.Length > 300) party = party.Substring(0, 300);
            string description = s.Description != null && s.Description.Length > 1000 ? s.Description.Substring(0, 1000) : s.Description;
            DateTime date = s.Date.Date;
            string json = PackLeg(s.Data);
            if (s.Inflow)
            {
                _uow.Set<FnReceipt>().Add(new FnReceipt
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Receipt, "RCPT", date, ct), Date = date, Status = FnDocStatus.Draft, PayerName = party, Description = description,
                    TotalAmount = s.Amount, AllocatedAmount = 0, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, SourceType = s.SourceType, SourceId = s.SourceId, SourceAction = s.Action, SourceData = json,
                    Lines = new List<FnReceiptLine> { new FnReceiptLine { RowNumber = 1, Instrument = FnInstrument.BankTransfer, Amount = s.Amount, BankAccountId = bank.Id } },
                    Allocations = new List<FnReceiptAllocation>(),
                });
            }
            else
            {
                _uow.Set<FnPayment>().Add(new FnPayment
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Payment, "PAYM", date, ct), Date = date, Status = FnDocStatus.Draft, Purpose = FnPaymentPurpose.FacilityLeg,
                    PayeeName = party, Description = description, GrossAmount = s.Amount, DeductionAmount = 0, NetAmount = s.Amount, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                    SourceType = s.SourceType, SourceId = s.SourceId, SourceAction = s.Action, SourceData = json,
                    Lines = new List<FnPaymentLine> { new FnPaymentLine { RowNumber = 1, Instrument = FnInstrument.BankTransfer, Amount = s.Amount, BankAccountId = bank.Id } },
                    Requests = new List<FnPaymentRequestLink>(),
                });
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------ settlement side (called by treasury)

        public async Task<CashLegSettlement> SettleAsync(CashLegRequest leg, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            LegData d = UnpackLeg(leg.Data);
            switch (leg.SourceType)
            {
                case CashLegSources.Loan:
                    if (leg.Action == "disburse" && leg.Inflow) return await SettleLoanDisbursementAsync(leg, ct);
                    if (leg.Action == "installment" && !leg.Inflow) return await SettleLoanInstallmentAsync(leg, d, ct);
                    break;
                case CashLegSources.LetterOfCredit:
                    return await SettleLetterOfCreditAsync(leg, d, ct);
                case CashLegSources.BankGuarantee:
                    if (!leg.Inflow && (leg.Action == "guarantee-fee" || leg.Action == "extend-fee")) return await SettleGuaranteeFeeAsync(leg, d, ct);
                    break;
            }
            return CashLegSettlement.Fail("نوع سند خزانه شناخته نشد؛ آن را باطل و دوباره از رکورد اصلی ثبت کنید.");
        }

        public async Task ReleaseAsync(CashLegRequest leg, CancellationToken ct)
        {
            if (leg.SourceType == CashLegSources.Loan)
            {
                FnLoan l = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == leg.SourceId, ct);
                if (l == null) return;
                if (leg.Action == "disburse" && l.Status == FnLoanStatus.Disbursing) l.Status = FnLoanStatus.Draft;
                else if (leg.Action == "installment")
                {
                    int sequence = UnpackLeg(leg.Data).Sequence ?? 0;
                    FnLoanInstallment inst = await _uow.Set<FnLoanInstallment>().FirstOrDefaultAsync(i => i.LoanId == l.Id && i.Sequence == sequence, ct);
                    if (inst != null && inst.Status == 2) inst.Status = 0;
                }
            }
            // letters of credit and guarantees keep their state while a leg is open, so there is nothing to give back
        }

        private async Task<CashLegSettlement> SettleLoanDisbursementAsync(CashLegRequest leg, CancellationToken ct)
        {
            FnLoan l = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == leg.SourceId, ct);
            if (l == null || l.Status != FnLoanStatus.Disbursing) return CashLegSettlement.Fail("تسهیلات دیگر در وضعیت «در انتظار دریافت وجه» نیست.");
            if (R(l.Amount - l.FeeAmount) != leg.BankAmount) return CashLegSettlement.Fail("مبلغ سند دریافت با مبلغ خالص تسهیلات پس از کارمزد فرق دارد.");
            FnAccount liability = await LoanAccountAsync(l, ct);
            if (liability == null) return CashLegSettlement.Fail("حساب تسهیلات در کدینگ نیست.");
            string text = $"دریافت تسهیلات {l.Number} از {l.BankName}";
            var s = new CashLegSettlement { Text = text };
            s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(liability), Credit = l.Amount, Description = text });
            if (l.FeeAmount > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = AccountRoles.BankFees, Debit = l.FeeAmount, Description = "کارمزد " + text });
            l.Status = FnLoanStatus.Active; l.BankAccountId = leg.BankAccountId; l.DisbursedDate = leg.Date.Date;
            return s;
        }

        private async Task<CashLegSettlement> SettleLoanInstallmentAsync(CashLegRequest leg, LegData d, CancellationToken ct)
        {
            FnLoan l = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == leg.SourceId, ct);
            FnLoanInstallment inst = l == null ? null : await _uow.Set<FnLoanInstallment>().FirstOrDefaultAsync(i => i.LoanId == l.Id && i.Sequence == (d.Sequence ?? 0), ct);
            if (l == null || inst == null || l.Status != FnLoanStatus.Active || inst.Status != 2) return CashLegSettlement.Fail("قسط دیگر در وضعیت «در انتظار پرداخت خزانه» نیست.");
            decimal penalty = R(d.Penalty ?? 0);
            if (R(inst.Principal + inst.Interest + penalty) != leg.BankAmount) return CashLegSettlement.Fail("مبلغ سند پرداخت با قسط و جریمه فرق دارد.");
            FnAccount liability = await LoanAccountAsync(l, ct), expense = await AccountByCodeAsync("80202", ct), penaltyAccount = await AccountByCodeAsync("80303", ct);
            if (liability == null || expense == null || penaltyAccount == null) return CashLegSettlement.Fail("حساب‌های تسهیلات، سود تسهیلات یا جریمه در کدینگ نیست.");
            // the split between the interest accrued earlier and the interest expensed now is read from the books at settlement time, so an accrual approved while the leg waited is not expensed twice
            decimal sumInterestUpTo = await _uow.Set<FnLoanInstallment>().Where(i => i.LoanId == l.Id && i.Sequence <= inst.Sequence).SumAsync(i => i.Interest, ct);
            decimal expenseNeeded = Math.Max(0, sumInterestUpTo - l.InterestExpensed), fromAccrual = inst.Interest - expenseNeeded;
            string text = $"قسط {inst.Sequence} تسهیلات {l.Number}";
            var s = new CashLegSettlement { Text = text };
            s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(liability), Debit = inst.Principal, Description = text });
            if (fromAccrual > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = AccountRoles.OtherPayable, Debit = fromAccrual, Description = "سود تعهدی " + text });
            if (expenseNeeded > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(expense), Debit = expenseNeeded, Description = "سود " + text });
            if (penalty > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(penaltyAccount), Debit = penalty, Description = "جریمه " + text });
            // a pending (not yet approved) accrual for interest that this payment expenses now would be expensed twice: it is superseded
            if (expenseNeeded > 0)
                foreach (FnLoanAccrual pend in await _uow.Set<FnLoanAccrual>().Where(a => a.LoanId == l.Id && a.Status == 0).ToListAsync(ct)) pend.Status = 2;
            inst.Status = 1; inst.PaidDate = leg.Date.Date; inst.PenaltyPaid = penalty;
            l.InterestExpensed += expenseNeeded; l.InterestPaid += inst.Interest; l.PrincipalPaid += inst.Principal;
            if (!await _uow.Set<FnLoanInstallment>().AnyAsync(i => i.LoanId == l.Id && i.Status != 1 && i.Id != inst.Id, ct)) l.Status = FnLoanStatus.Closed;
            return s;
        }

        private sealed class LcPayPlan
        {
            public string Error;
            public FnAccount Payable, Deposit;
            public decimal Settled, Release, Cash, CarryBase, Diff;
        }

        /// <summary>The amounts of a letter-of-credit settlement: what the supplier is paid, how much of it the margin deposit covers, what the bank pays in cash, and the exchange difference against the carrying rate of the payable.</summary>
        private async Task<LcPayPlan> PlanLcPayAsync(FnLetterOfCredit l, decimal rate, CancellationToken ct)
        {
            var plan = new LcPayPlan();
            plan.Payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct);
            plan.Deposit = await AccountByCodeAsync("10402", ct);
            if (plan.Payable == null || plan.Deposit == null) { plan.Error = "حساب پرداختنی یا سپرده در کدینگ نیست."; return plan; }
            plan.Settled = R(l.Amount * rate);
            plan.Release = Math.Min(l.MarginPaid, plan.Settled);
            plan.Cash = plan.Settled - plan.Release;
            plan.CarryBase = plan.Settled;
            // the payable leaves the books at its carrying rate (the currency-tagged lines of the supplier in this currency) and the difference to the payment rate is the realised gain or loss
            FnDetailAccount supplier = await _uow.Set<FnDetailAccount>().AsNoTracking().FirstOrDefaultAsync(x => x.Type == FnDetailType.Supplier && x.LinkedEntityId == l.ProviderId, ct);
            if (supplier != null)
            {
                var tagged = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(x => x.AccountId == plan.Payable.Id && x.Detail1Id == supplier.Id && x.CurrencyCode == l.CurrencyCode && x.CurrencyAmount != null && x.Voucher.Status != FnVoucherStatus.Draft)
                    .GroupBy(x => 1).Select(g => new { Base = g.Sum(x => x.Credit - x.Debit), Cur = g.Sum(x => x.Credit > 0 ? x.CurrencyAmount.Value : -x.CurrencyAmount.Value) }).FirstOrDefaultAsync(ct);
                if (tagged != null && tagged.Cur >= l.Amount && tagged.Cur > 0) plan.CarryBase = R(tagged.Base * l.Amount / tagged.Cur);
            }
            plan.Diff = plan.Settled - plan.CarryBase;   // positive: paid more rials than carried = loss
            return plan;
        }

        private async Task<CashLegSettlement> SettleLetterOfCreditAsync(CashLegRequest leg, LegData d, CancellationToken ct)
        {
            FnLetterOfCredit l = await _uow.Set<FnLetterOfCredit>().FirstOrDefaultAsync(x => x.Id == leg.SourceId, ct);
            if (l == null) return CashLegSettlement.Fail("اعتبار اسنادی پیدا نشد.");
            bool live = l.Status == FnLcStatus.Opened || l.Status == FnLcStatus.DocumentsReceived;
            DateTime date = leg.Date.Date;
            FnAccount deposit = await AccountByCodeAsync("10402", ct);
            var s = new CashLegSettlement();
            switch (leg.Action)
            {
                case "margin":
                case "fee":
                {
                    if (!live) return CashLegSettlement.Fail("اعتبار بسته شده است.");
                    bool margin = leg.Action == "margin";
                    if (margin && deposit == null) return CashLegSettlement.Fail("حساب سپرده اعتبار اسنادی در کدینگ نیست.");
                    s.Text = (margin ? "سپرده اعتبار اسنادی " : "کارمزد اعتبار اسنادی ") + l.Number;
                    s.CounterLines.Add(new AccountingEventLine { AccountRole = margin ? Direct(deposit) : AccountRoles.BankFees, Debit = leg.BankAmount, Description = s.Text });
                    if (margin) l.MarginPaid += leg.BankAmount; else l.FeesPaid += leg.BankAmount;
                    _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = margin ? FnLcPostingKind.Margin : FnLcPostingKind.Fee, Date = date, Amount = leg.BankAmount, Note = d.Note, CreatedByUserId = leg.RecordedByUserId });
                    return s;
                }
                case "pay":
                {
                    if (l.Status != FnLcStatus.DocumentsReceived) return CashLegSettlement.Fail("پرداخت فقط پس از دریافت اسناد انجام می‌شود.");
                    if (!d.Rate.HasValue || d.Rate.Value <= 0) return CashLegSettlement.Fail("نرخ تسویه در سند خزانه نیست.");
                    LcPayPlan plan = await PlanLcPayAsync(l, d.Rate.Value, ct);
                    if (plan.Error != null) return CashLegSettlement.Fail(plan.Error);
                    if (plan.Cash != leg.BankAmount) return CashLegSettlement.Fail("مبلغ سند پرداخت با مبلغ نقدی تسویه اعتبار اسنادی فرق دارد.");
                    s.Text = "تسویه اعتبار اسنادی " + l.Number;
                    bool rial = FinanceFeatures.IsRial(l.CurrencyCode);
                    s.CounterLines.Add(new AccountingEventLine
                    {
                        AccountRole = AccountRoles.AccountsPayable, ProviderId = l.ProviderId, Debit = plan.CarryBase, Description = s.Text,
                        CurrencyCode = rial ? null : l.CurrencyCode, CurrencyAmount = rial ? (decimal?)null : l.Amount, Rate = rial ? (decimal?)null : (l.Amount == 0 ? d.Rate : R(plan.CarryBase / l.Amount)),
                    });
                    if (plan.Diff > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = AccountRoles.FxLoss, Debit = plan.Diff, Description = "زیان تسعیر در تسویه " + s.Text });
                    else if (plan.Diff < 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = AccountRoles.FxGain, Credit = -plan.Diff, Description = "سود تسعیر در تسویه " + s.Text });
                    if (plan.Release > 0) s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(plan.Deposit), Credit = plan.Release, Description = s.Text });
                    if (plan.Release > 0) _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.MarginRelease, Date = date, Amount = plan.Release, CreatedByUserId = leg.RecordedByUserId });
                    _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.Settlement, Date = date, Amount = plan.Settled, Note = d.Note, CreatedByUserId = leg.RecordedByUserId });
                    l.Status = FnLcStatus.Paid; l.SettlementRate = d.Rate; l.SettledBase = plan.Settled; l.PaidDate = date; l.MarginPaid -= plan.Release;
                    return s;
                }
                case "refund":
                {
                    if (!live) return CashLegSettlement.Fail("اعتبار بسته شده است.");
                    if (deposit == null) return CashLegSettlement.Fail("حساب سپرده اعتبار اسنادی در کدینگ نیست.");
                    if (l.MarginPaid != leg.BankAmount || leg.BankAmount <= 0) return CashLegSettlement.Fail("مبلغ سند دریافت با سپرده ثبت‌شده اعتبار فرق دارد.");
                    s.Text = "استرداد سپرده اعتبار اسنادی " + l.Number;
                    s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(deposit), Credit = leg.BankAmount, Description = s.Text });
                    _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.MarginRelease, Date = date, Amount = leg.BankAmount, CreatedByUserId = leg.RecordedByUserId });
                    l.MarginPaid = 0; l.Status = FnLcStatus.Cancelled;
                    return s;
                }
            }
            return CashLegSettlement.Fail("نوع سند خزانه شناخته نشد.");
        }

        private async Task<CashLegSettlement> SettleGuaranteeFeeAsync(CashLegRequest leg, LegData d, CancellationToken ct)
        {
            FnBankGuarantee g = await _uow.Set<FnBankGuarantee>().FirstOrDefaultAsync(x => x.Id == leg.SourceId, ct);
            if (g == null || g.Status != FnBankGuaranteeStatus.Active) return CashLegSettlement.Fail("ضمانت‌نامه دیگر فعال نیست.");
            if (!d.From.HasValue || !d.To.HasValue || d.To.Value.Date < d.From.Value.Date) return CashLegSettlement.Fail("بازه کارمزد در سند خزانه نامعتبر است.");
            FnAccount prepaid = await AccountByCodeAsync(FeePrepaidCode, ct), expense = await RoleAccountAsync(AccountRoles.BankFees, "80201", ct);
            if (prepaid == null || expense == null) return CashLegSettlement.Fail("حساب پیش‌پرداخت یا کارمزد بانکی پیدا نشد.");
            string text = d.Text ?? $"کارمزد ضمانت‌نامه {g.Number}";
            var s = new CashLegSettlement { Text = text };
            s.CounterLines.Add(new AccountingEventLine { AccountRole = Direct(prepaid), Debit = leg.BankAmount, Description = text, ContractId = g.ContractId });
            // the fee is a prepaid expense from the day it is really paid: the schedule that spreads it over the life of the guarantee is created now
            _uow.Set<FnDeferral>().Add(new FnDeferral
            {
                Number = await NextNumberAsync("DEF", FinanceNumbers.Deferral, d.From.Value, ct), LegalEntityId = g.LegalEntityId, Kind = FnDeferralKind.PrepaidExpense, Description = text,
                TotalAmount = leg.BankAmount, StartDate = d.From.Value.Date, EndDate = d.To.Value.Date, BalanceAccountId = prepaid.Id, ResultAccountId = expense.Id, ContractId = g.ContractId,
                GuaranteeId = g.Id, Status = FnDeferralStatus.Active, CreatedByUserId = leg.RecordedByUserId, CreatedAt = DateTimeOffset.Now,
            });
            return s;
        }
    }
}
