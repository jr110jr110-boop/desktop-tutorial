using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ILoanService
    {
        Task<ResponseDto<List<FnLoanDto>>> GetLoansAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnLoanDto>> GetLoanAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnLoanDto>> SaveLoanAsync(FnSaveLoanDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnLoanDto>> LoanActionAsync(FnLoanActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnLoanDto>> PayInstallmentAsync(FnLoanPayDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnLoanAccrualDto>>> RunAccrualAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<FnLoanAccrualDto>> ApproveAccrualAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnCurrentPortionDto>> CurrentPortionAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<FnFacilityLimitDto>> SaveFacilityLimitAsync(FnFacilityLimitDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnUtilisationDto>> UtilisationAsync(CancellationToken cancellationToken);
    }

    // Financing (F3 section 6): loans with a schedule on the Persian calendar, monthly interest accrual, instalment payment, facility limits.
    public partial class GlService : ILoanService
    {
        private static readonly PersianCalendar LoanCal = new PersianCalendar();

        /// <summary>The schedule of a loan. Interest of an instalment = outstanding principal x annual rate / 1200; the last instalment closes the balance exactly.</summary>
        internal static List<FnScheduleRowDto> BuildSchedule(decimal amount, decimal ratePercent, int count, FnScheduleKind kind, DateTime start)
        {
            decimal r = ratePercent / 1200m;
            var rows = new List<FnScheduleRowDto>();
            decimal balance = amount, payment = 0;
            if (kind == FnScheduleKind.Annuity)
            {
                if (r == 0) payment = R(amount / count);
                else
                {
                    decimal pow = 1;
                    for (int i = 0; i < count; i++) pow *= 1 + r;
                    payment = R(amount * r * pow / (pow - 1));
                }
            }
            for (int k = 1; k <= count; k++)
            {
                decimal interest = R(balance * r), principal;
                if (k == count) principal = balance;
                else if (kind == FnScheduleKind.EqualPrincipal) principal = R(amount / count);
                else principal = payment - interest;
                rows.Add(new FnScheduleRowDto { DueDate = LoanCal.AddMonths(start.Date, k), Principal = principal, Interest = interest });
                balance -= principal;
            }
            return rows;
        }

        private async Task<FnLoanDto> LoanDtoAsync(FnLoan l, CancellationToken ct)
        {
            var inst = await _uow.Set<FnLoanInstallment>().AsNoTracking().Where(i => i.LoanId == l.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
            return new FnLoanDto
            {
                Id = l.Id, Number = l.Number, BankName = l.BankName, FacilityType = l.FacilityType, ContractNo = l.ContractNo, Amount = l.Amount, CurrencyCode = l.CurrencyCode, StartDate = l.StartDate, EndDate = l.EndDate,
                RatePercent = l.RatePercent, ScheduleKind = l.ScheduleKind, InstallmentCount = l.InstallmentCount, FeeAmount = l.FeeAmount, Collateral = l.Collateral, Status = l.Status, FacilityLimitId = l.FacilityLimitId,
                BankAccountId = l.BankAccountId, DisbursementVoucherId = l.DisbursementVoucherId, TotalInterest = inst.Sum(i => i.Interest),
                OutstandingPrincipal = l.Status == FnLoanStatus.Active ? l.Amount - l.PrincipalPaid : 0, AccruedInterestPayable = l.InterestExpensed - l.InterestPaid,
                Installments = inst.Select(i => new FnInstallmentDto { Id = i.Id, Sequence = i.Sequence, DueDate = i.DueDate, Principal = i.Principal, Interest = i.Interest, Total = i.Principal + i.Interest, Status = i.Status, PaidDate = i.PaidDate, PenaltyPaid = i.PenaltyPaid, VoucherId = i.VoucherId }).ToList(),
            };
        }

        public async Task<ResponseDto<List<FnLoanDto>>> GetLoansAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnLoanDto>>(NotAllowed);
            var rows = await _uow.Set<FnLoan>().AsNoTracking().OrderByDescending(l => l.Id).Take(200).ToListAsync(ct);
            var list = new List<FnLoanDto>();
            foreach (FnLoan l in rows) { FnLoanDto d = await LoanDtoAsync(l, ct); d.Installments = new List<FnInstallmentDto>(); list.Add(d); }
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<FnLoanDto>> GetLoanAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnLoanDto>(NotAllowed);
            FnLoan l = await _uow.Set<FnLoan>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return l == null ? ResponseDtoHelper.NotFound<FnLoanDto>("تسهیلات پیدا نشد.") : ResponseDtoHelper.Ok(await LoanDtoAsync(l, ct));
        }

        public async Task<ResponseDto<FnLoanDto>> SaveLoanAsync(FnSaveLoanDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnLoanDto>(NotAllowed);
            if (dto == null || Clean(dto.BankName) == null) return Bad<FnLoanDto>("نام بانک را وارد کنید.");
            if (dto.Amount <= 0) return Bad<FnLoanDto>("مبلغ تسهیلات باید بیشتر از صفر باشد.");
            if (dto.RatePercent < 0 || dto.RatePercent > 100) return Bad<FnLoanDto>("نرخ سود باید بین ۰ و ۱۰۰ درصد باشد.");
            if (!FinanceFeatures.ForeignCurrency && !FinanceFeatures.IsRial(dto.CurrencyCode)) return Bad<FnLoanDto>(FinanceFeatures.OnlyRial);
            if (dto.StartDate == default) return Bad<FnLoanDto>("تاریخ شروع را وارد کنید.");
            if (dto.FeeAmount < 0) return Bad<FnLoanDto>("کارمزد منفی مجاز نیست.");
            if (!Enum.IsDefined(typeof(FnFacilityType), dto.FacilityType) || !Enum.IsDefined(typeof(FnScheduleKind), dto.ScheduleKind)) return Bad<FnLoanDto>("نوع تسهیلات یا روش بازپرداخت نامعتبر است.");
            List<FnScheduleRowDto> schedule;
            if (dto.ScheduleKind == FnScheduleKind.Custom)
            {
                schedule = (dto.CustomRows ?? new List<FnScheduleRowDto>()).OrderBy(r => r.DueDate).ToList();
                if (schedule.Count == 0) return Bad<FnLoanDto>("برنامه سفارشی ردیف ندارد.");
                if (schedule.Sum(r => r.Principal) != dto.Amount) return Bad<FnLoanDto>("جمع اصل اقساط باید برابر مبلغ تسهیلات باشد.");
                if (schedule.Any(r => r.Principal < 0 || r.Interest < 0 || r.DueDate.Date <= dto.StartDate.Date)) return Bad<FnLoanDto>("ردیف‌های برنامه سفارشی نامعتبر است.");
                dto.InstallmentCount = schedule.Count;
            }
            else
            {
                if (dto.InstallmentCount < 1 || dto.InstallmentCount > 360) return Bad<FnLoanDto>("تعداد اقساط باید بین ۱ و ۳۶۰ باشد.");
                schedule = BuildSchedule(R(dto.Amount), dto.RatePercent, dto.InstallmentCount, dto.ScheduleKind, dto.StartDate);
            }
            if (dto.FacilityLimitId.HasValue && !await _uow.Set<FnFacilityLimit>().AnyAsync(f => f.Id == dto.FacilityLimitId.Value, ct)) return Bad<FnLoanDto>("سقف اعتباری پیدا نشد.");
            if (dto.BankAccountId.HasValue && !await _uow.Set<FnBankAccount>().AnyAsync(b => b.Id == dto.BankAccountId.Value && b.IsActive, ct)) return Bad<FnLoanDto>("حساب بانکی پیدا نشد.");
            FnLoan loan;
            if (dto.Id.HasValue)
            {
                loan = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (loan == null) return ResponseDtoHelper.NotFound<FnLoanDto>("تسهیلات پیدا نشد.");
                if (loan.Status != FnLoanStatus.Draft) return Bad<FnLoanDto>("فقط پیش‌نویس تسهیلات ویرایش می‌شود.");
                _uow.Set<FnLoanInstallment>().RemoveRange(await _uow.Set<FnLoanInstallment>().Where(i => i.LoanId == loan.Id).ToListAsync(ct));
            }
            else
            {
                int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
                loan = new FnLoan { LegalEntityId = entityId, Status = FnLoanStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Number = await NextNumberAsync("LN", FinanceNumbers.Loan, dto.StartDate, ct) };
                _uow.Set<FnLoan>().Add(loan);
            }
            loan.BankName = Clean(dto.BankName); loan.FacilityType = dto.FacilityType; loan.ContractNo = Clean(dto.ContractNo); loan.Amount = R(dto.Amount); loan.CurrencyCode = Clean(dto.CurrencyCode)?.ToUpperInvariant() ?? "IRR";
            loan.StartDate = dto.StartDate.Date; loan.EndDate = schedule.Last().DueDate.Date; loan.RatePercent = dto.RatePercent; loan.ScheduleKind = dto.ScheduleKind; loan.InstallmentCount = schedule.Count;
            loan.FeeAmount = R(dto.FeeAmount); loan.Collateral = Clean(dto.Collateral); loan.FacilityLimitId = dto.FacilityLimitId; loan.BankAccountId = dto.BankAccountId;
            int seq = 1;
            foreach (FnScheduleRowDto row in schedule)
                _uow.Set<FnLoanInstallment>().Add(new FnLoanInstallment { Loan = loan, Sequence = seq++, DueDate = row.DueDate.Date, Principal = row.Principal, Interest = row.Interest });
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await LoanDtoAsync(loan, ct));
        }

        private async Task<bool> IsForeignLoanAsync(FnLoan l, CancellationToken ct)
        {
            if (!FinanceFeatures.ForeignCurrency) return false;
            string baseCurrency = await _uow.Set<FnLegalEntity>().AsNoTracking().Where(e => e.Id == l.LegalEntityId).Select(e => e.BaseCurrency).FirstOrDefaultAsync(ct) ?? "IRR";
            return !string.Equals(l.CurrencyCode ?? baseCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The detail that isolates a foreign loan on the liability account (its currency balance is revalued on its own).</summary>
        private async Task<FnDetailAccount> LoanDetailAsync(FnLoan l, CancellationToken ct)
        {
            string code = "LN-" + l.Number;
            FnDetailAccount d = _uow.Set<FnDetailAccount>().Local.FirstOrDefault(x => x.Code == code) ?? await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (d == null) { d = new FnDetailAccount { Code = code, Title = "تسهیلات ارزی " + l.Number, Type = FnDetailType.Other, IsActive = true }; _uow.Set<FnDetailAccount>().Add(d); }
            return d;
        }

        private async Task<FnAccount> LoanAccountAsync(FnLoan l, CancellationToken ct) =>
            await AccountByCodeAsync(l.FacilityType == FnFacilityType.Overdraft || l.InstallmentCount <= 12 ? "30501" : "40101", ct);

        public async Task<ResponseDto<FnLoanDto>> LoanActionAsync(FnLoanActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnLoanDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnLoanDto>("عمل را مشخص کنید.");
            FnLoan l = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (l == null) return ResponseDtoHelper.NotFound<FnLoanDto>("تسهیلات پیدا نشد.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "cancel")
            {
                if (l.Status != FnLoanStatus.Draft) return Bad<FnLoanDto>("فقط پیش‌نویس لغو می‌شود.");
                l.Status = FnLoanStatus.Cancelled;
                await _uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok(await LoanDtoAsync(l, ct));
            }
            if (action != "disburse") return Bad<FnLoanDto>("عمل نامعتبر است.");
            if (l.Status != FnLoanStatus.Draft) return Bad<FnLoanDto>("این تسهیلات قبلاً پرداخت شده یا لغو شده است.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int bankId = dto.BankAccountId ?? l.BankAccountId ?? 0;
            var details = new DetailResolver(_uow);
            var bank = await BankLedgerAsync(bankId, details, ct);
            if (bank.Bank == null) return Bad<FnLoanDto>("حساب بانکی دریافت وجه را مشخص کنید.");
            FnAccount liability = await LoanAccountAsync(l, ct), feeAccount = await RoleAccountAsync(AccountRoles.BankFees, "80201", ct);
            if (liability == null || feeAccount == null) return Bad<FnLoanDto>("حساب تسهیلات یا کارمزد در کدینگ نیست.");
            DateTime date = (dto.Date ?? l.StartDate).Date;
            string text = $"دریافت تسهیلات {l.Number} از {l.BankName}";
            bool foreign = await IsForeignLoanAsync(l, ct);
            decimal rate = 1m;
            List<AutoLine> lines;
            if (foreign)
            {
                if (!dto.Rate.HasValue || dto.Rate.Value <= 0) return Bad<FnLoanDto>($"تسهیلات ارزی ({l.CurrencyCode}) است؛ نرخ تبدیل به ریال در تاریخ دریافت را وارد کنید.");
                rate = dto.Rate.Value;
                liability.CurrencyAllowed = true; liability.IsMonetaryForeign = true;
                FnDetailAccount loanDetail = await LoanDetailAsync(l, ct);
                decimal baseAmount = R(l.Amount * rate), baseFee = R(l.FeeAmount * rate);
                lines = new List<AutoLine>
                {
                    new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Debit = baseAmount - baseFee, Description = text },
                    new AutoLine { Account = liability, Detail1 = loanDetail, Credit = baseAmount, Description = text, CurrencyCode = l.CurrencyCode, CurrencyAmount = l.Amount, Rate = rate },
                };
                if (baseFee > 0) lines.Add(new AutoLine { Account = feeAccount, Debit = baseFee, Description = "کارمزد " + text });
            }
            else
            {
                lines = new List<AutoLine>
                {
                    new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Debit = l.Amount - l.FeeAmount, Description = text },
                    new AutoLine { Account = liability, Credit = l.Amount, Description = text },
                };
                if (l.FeeAmount > 0) lines.Add(new AutoLine { Account = feeAccount, Debit = l.FeeAmount, Description = "کارمزد " + text });
            }
            var v = await AddAutoVoucherAsync(l.LegalEntityId, date, text, "Loan", l.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnLoanDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            l.Status = FnLoanStatus.Active; l.BankAccountId = bank.Bank.Id; l.DisbursementVoucherId = v.Voucher.Id; l.DisbursedDate = date;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await LoanDtoAsync(l, ct));
        }

        public async Task<ResponseDto<FnLoanDto>> PayInstallmentAsync(FnLoanPayDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnLoanDto>(NotAllowed);
            if (dto == null || dto.Penalty < 0) return Bad<FnLoanDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnLoan l = await _uow.Set<FnLoan>().FirstOrDefaultAsync(x => x.Id == dto.LoanId, ct);
            if (l == null) return ResponseDtoHelper.NotFound<FnLoanDto>("تسهیلات پیدا نشد.");
            if (l.Status != FnLoanStatus.Active) return Bad<FnLoanDto>("تسهیلات فعال نیست.");
            FnLoanInstallment inst = await _uow.Set<FnLoanInstallment>().FirstOrDefaultAsync(i => i.LoanId == l.Id && i.Sequence == dto.Sequence, ct);
            if (inst == null) return ResponseDtoHelper.NotFound<FnLoanDto>("قسط پیدا نشد.");
            if (inst.Status == 1) return Bad<FnLoanDto>("این قسط پرداخت شده است.");
            if (await _uow.Set<FnLoanInstallment>().AnyAsync(i => i.LoanId == l.Id && i.Sequence < inst.Sequence && i.Status == 0, ct)) return Bad<FnLoanDto>("اقساط قبلی باید پرداخت شده باشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            var details = new DetailResolver(_uow);
            var bank = await BankLedgerAsync(dto.BankAccountId ?? l.BankAccountId ?? 0, details, ct);
            if (bank.Bank == null) return Bad<FnLoanDto>("حساب بانکی پرداخت را مشخص کنید.");
            FnAccount liability = await LoanAccountAsync(l, ct), expense = await AccountByCodeAsync("80202", ct), accrued = await RoleAccountAsync(AccountRoles.OtherPayable, "30208", ct), penaltyAccount = await AccountByCodeAsync("80303", ct);
            if (liability == null || expense == null || accrued == null || penaltyAccount == null) return Bad<FnLoanDto>("حساب‌های تسهیلات، سود تسهیلات یا جریمه در کدینگ نیست.");
            DateTime date = (dto.Date ?? inst.DueDate).Date;
            decimal sumInterestUpTo = await _uow.Set<FnLoanInstallment>().Where(i => i.LoanId == l.Id && i.Sequence <= inst.Sequence).SumAsync(i => i.Interest, ct);
            decimal expenseNeeded = Math.Max(0, sumInterestUpTo - l.InterestExpensed), fromAccrual = inst.Interest - expenseNeeded;
            decimal penalty = R(dto.Penalty);
            string text = $"قسط {inst.Sequence} تسهیلات {l.Number}";
            bool foreign = await IsForeignLoanAsync(l, ct);
            List<AutoLine> lines;
            if (foreign)
            {
                // foreign-currency loan: everything is converted at the payment rate; the liability leaves the books at its carrying rate and the difference is the realised FX gain or loss
                if (!dto.Rate.HasValue || dto.Rate.Value <= 0) return Bad<FnLoanDto>($"تسهیلات ارزی ({l.CurrencyCode}) است؛ نرخ تبدیل به ریال در تاریخ پرداخت را وارد کنید.");
                decimal payRate = dto.Rate.Value;
                FnDetailAccount loanDetail = await LoanDetailAsync(l, ct);
                var carry = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(x => x.AccountId == liability.Id && x.Detail1.Code == loanDetail.Code && x.Voucher.Status != FnVoucherStatus.Draft)
                    .GroupBy(x => 1).Select(g => new { Base = g.Sum(x => x.Credit - x.Debit), Cur = g.Sum(x => x.CurrencyAmount == null ? 0 : (x.Credit > 0 ? x.CurrencyAmount.Value : -x.CurrencyAmount.Value)) }).FirstOrDefaultAsync(ct);
                decimal principalBase = R(inst.Principal * payRate), interestBase = R(inst.Interest * payRate), penaltyBase = R(penalty * payRate);
                decimal carryBase = carry != null && carry.Cur > 0 ? R(carry.Base * inst.Principal / carry.Cur) : principalBase;
                decimal fxDiff = principalBase - carryBase;   // positive: more rials paid than carried = loss
                FnAccount fxGain = await RoleAccountAsync(AccountRoles.FxGain, "60303", ct), fxLoss = await RoleAccountAsync(AccountRoles.FxLoss, "80302", ct);
                if (fxGain == null || fxLoss == null) return Bad<FnLoanDto>("حساب سود و زیان تسعیر ارز در کدینگ نیست.");
                lines = new List<AutoLine> { new AutoLine { Account = liability, Detail1 = loanDetail, Debit = carryBase, Description = text, CurrencyCode = l.CurrencyCode, CurrencyAmount = inst.Principal, Rate = inst.Principal == 0 ? payRate : R(carryBase / inst.Principal) } };
                lines.Add(new AutoLine { Account = expense, Debit = interestBase, Description = "سود " + text });
                if (penaltyBase > 0) lines.Add(new AutoLine { Account = penaltyAccount, Debit = penaltyBase, Description = "جریمه " + text });
                if (fxDiff > 0) lines.Add(new AutoLine { Account = fxLoss, Debit = fxDiff, Description = "زیان تسعیر در تسویه " + text });
                else if (fxDiff < 0) lines.Add(new AutoLine { Account = fxGain, Credit = -fxDiff, Description = "سود تسعیر در تسویه " + text });
                lines.Add(new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Credit = principalBase + interestBase + penaltyBase, Description = text });
                expenseNeeded = inst.Interest; fromAccrual = 0;
            }
            else
            {
                lines = new List<AutoLine> { new AutoLine { Account = liability, Debit = inst.Principal, Description = text } };
                if (fromAccrual > 0) lines.Add(new AutoLine { Account = accrued, Debit = fromAccrual, Description = "سود تعهدی " + text });
                if (expenseNeeded > 0) lines.Add(new AutoLine { Account = expense, Debit = expenseNeeded, Description = "سود " + text });
                if (penalty > 0) lines.Add(new AutoLine { Account = penaltyAccount, Debit = penalty, Description = "جریمه " + text });
                lines.Add(new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Credit = inst.Principal + inst.Interest + penalty, Description = text });
            }
            // a pending (not yet approved) accrual for interest that this payment expenses now would be expensed twice: it is superseded
            if (expenseNeeded > 0)
                foreach (FnLoanAccrual pend in await _uow.Set<FnLoanAccrual>().Where(a => a.LoanId == l.Id && a.Status == 0).ToListAsync(ct)) pend.Status = 2;
            var v = await AddAutoVoucherAsync(l.LegalEntityId, date, text, "Loan", l.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnLoanDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            inst.Status = 1; inst.PaidDate = date; inst.PenaltyPaid = penalty; inst.VoucherId = v.Voucher.Id;
            l.InterestExpensed += expenseNeeded; l.InterestPaid += inst.Interest; l.PrincipalPaid += inst.Principal;
            if (!await _uow.Set<FnLoanInstallment>().AnyAsync(i => i.LoanId == l.Id && i.Status == 0 && i.Id != inst.Id, ct)) l.Status = FnLoanStatus.Closed;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await LoanDtoAsync(l, ct));
        }

        /// <summary>Interest accrued up to a day: every instalment interval contributes in proportion to the days elapsed (inclusive of the day).</summary>
        private static decimal TheoreticalAccrued(FnLoan l, List<FnLoanInstallment> inst, DateTime day)
        {
            decimal total = 0;
            DateTime start = l.DisbursedDate ?? l.StartDate;
            foreach (FnLoanInstallment i in inst.OrderBy(x => x.Sequence))
            {
                DateTime s = i.Sequence == 1 ? start : inst.First(x => x.Sequence == i.Sequence - 1).DueDate;
                double length = (i.DueDate.Date - s.Date).TotalDays;
                if (length <= 0) { if (day.Date >= i.DueDate.Date) total += i.Interest; s = i.DueDate; continue; }
                double elapsed = (day.Date - s.Date).TotalDays + 1;
                decimal fraction = elapsed <= 0 ? 0 : elapsed >= length ? 1 : (decimal)elapsed / (decimal)length;
                total += R(i.Interest * fraction);
            }
            return total;
        }

        public async Task<ResponseDto<List<FnLoanAccrualDto>>> RunAccrualAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<List<FnLoanAccrualDto>>(NotAllowed);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<List<FnLoanAccrualDto>>("دوره پیدا نشد.");
            DateTime end = period.EndDate.Date;
            var made = new List<FnLoanAccrual>();
            var loans = await _uow.Set<FnLoan>().Where(l => l.Status == FnLoanStatus.Active && l.LegalEntityId == period.FiscalYear.LegalEntityId && l.DisbursedDate <= end).ToListAsync(ct);
            foreach (FnLoan l in loans)
            {
                if (await _uow.Set<FnLoanAccrual>().AnyAsync(a => a.LoanId == l.Id && a.PeriodId == periodId, ct)) continue;
                if (await IsForeignLoanAsync(l, ct)) continue;   // foreign-currency interest is expensed at the payment rate when the instalment is paid
                var inst = await _uow.Set<FnLoanInstallment>().AsNoTracking().Where(i => i.LoanId == l.Id).ToListAsync(ct);
                decimal pending = await _uow.Set<FnLoanAccrual>().Where(a => a.LoanId == l.Id && a.Status == 0).SumAsync(a => (decimal?)a.Amount, ct) ?? 0;
                decimal amount = TheoreticalAccrued(l, inst, end) - l.InterestExpensed - pending;
                if (amount <= 0) continue;
                var a2 = new FnLoanAccrual { LoanId = l.Id, PeriodId = periodId, Amount = amount, Status = 0, PreparedByUserId = Me };
                _uow.Set<FnLoanAccrual>().Add(a2);
                made.Add(a2);
            }
            await _uow.SaveChangesAsync(ct);
            var numbers = await _uow.Set<FnLoan>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Number, ct);
            return ResponseDtoHelper.Ok(made.Select(a => new FnLoanAccrualDto { Id = a.Id, LoanId = a.LoanId, LoanNumber = numbers.GetValueOrDefault(a.LoanId), PeriodId = a.PeriodId, Amount = a.Amount, Status = a.Status, PreparedByUserId = a.PreparedByUserId }).ToList());
        }

        public async Task<ResponseDto<FnLoanAccrualDto>> ApproveAccrualAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnLoanAccrualDto>(NotAllowed);
            FnLoanAccrual a = await _uow.Set<FnLoanAccrual>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<FnLoanAccrualDto>("ردیف تعهدی پیدا نشد.");
            if (a.Status != 0) return Bad<FnLoanAccrualDto>("این ردیف قبلاً تأیید شده است.");
            if (a.PreparedByUserId == Me) return Bad<FnLoanAccrualDto>(SameUser);
            FnLoan l = await _uow.Set<FnLoan>().FirstAsync(x => x.Id == a.LoanId, ct);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().FirstAsync(p => p.Id == a.PeriodId, ct);
            FnAccount expense = await AccountByCodeAsync("80202", ct), accrued = await RoleAccountAsync(AccountRoles.OtherPayable, "30208", ct);
            if (expense == null || accrued == null) return Bad<FnLoanAccrualDto>("حساب سود تسهیلات یا پرداختنی در کدینگ نیست.");
            // the amount cannot exceed what is still unexpensed at approval time (an instalment paid in between may have expensed it)
            var allInst = await _uow.Set<FnLoanInstallment>().AsNoTracking().Where(i => i.LoanId == l.Id).ToListAsync(ct);
            decimal cap = TheoreticalAccrued(l, allInst, period.EndDate.Date) - l.InterestExpensed;
            if (cap < a.Amount) a.Amount = cap > 0 ? cap : 0;
            if (a.Amount <= 0) { a.Status = 2; await _uow.SaveChangesAsync(ct); return Bad<FnLoanAccrualDto>("سود این ردیف تعهدی در قسط پرداخت‌شده قبلاً هزینه شده است؛ ردیف ابطال شد."); }
            string text = $"سود تعهدی تسهیلات {l.Number} تا پایان {period.Title}";
            var lines = new List<AutoLine> { new AutoLine { Account = expense, Debit = a.Amount, Description = text }, new AutoLine { Account = accrued, Credit = a.Amount, Description = text } };
            var v = await AddAutoVoucherAsync(l.LegalEntityId, period.EndDate.Date, text, "LoanAccrual", l.Number, lines, FnVoucherType.Auto, null, ct);
            if (v.Error != null) return Bad<FnLoanAccrualDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            a.Status = 1; a.ApprovedByUserId = Me; a.VoucherId = v.Voucher.Id; l.InterestExpensed += a.Amount;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnLoanAccrualDto { Id = a.Id, LoanId = a.LoanId, LoanNumber = l.Number, PeriodId = a.PeriodId, Amount = a.Amount, Status = a.Status, PreparedByUserId = a.PreparedByUserId, ApprovedByUserId = a.ApprovedByUserId, VoucherId = a.VoucherId });
        }

        /// <summary>
        /// Period-close run: the principal of long-term loans (40101) due within 12 months after the period end moves to short-term facilities (30501) as a DRAFT voucher
        /// (reviewed by a second person like any manual voucher). Each run posts only the change since the previous one.
        /// </summary>
        public async Task<ResponseDto<FnCurrentPortionDto>> CurrentPortionAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnCurrentPortionDto>(NotAllowed);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<FnCurrentPortionDto>("دوره پیدا نشد.");
            DateTime end = period.EndDate.Date, horizon = LoanCal.AddMonths(end, 12);
            FnAccount longTerm = await AccountByCodeAsync("40101", ct), shortTerm = await AccountByCodeAsync("30501", ct);
            if (longTerm == null || shortTerm == null) return Bad<FnCurrentPortionDto>("حساب تسهیلات کوتاه‌مدت (30501) یا بلندمدت (40101) در کدینگ نیست.");
            var loans = await _uow.Set<FnLoan>().Where(l => l.Status == FnLoanStatus.Active && l.LegalEntityId == period.FiscalYear.LegalEntityId && l.FacilityType != FnFacilityType.Overdraft && l.InstallmentCount > 12 && l.DisbursedDate <= end).OrderBy(l => l.Id).ToListAsync(ct);
            var result = new FnCurrentPortionDto { PeriodId = periodId };
            var lines = new List<AutoLine>();
            foreach (FnLoan l in loans)
            {
                decimal due = await _uow.Set<FnLoanInstallment>().Where(i => i.LoanId == l.Id && i.Status == 0 && i.DueDate > end && i.DueDate <= horizon).SumAsync(i => (decimal?)i.Principal, ct) ?? 0;
                // what is already reclassified is read from the vouchers themselves (a deleted draft leaves nothing behind), not from a counter that outlives them (review 3, M1)
                string tag = "حصه جاری تسهیلات " + l.Number;
                decimal already = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(x => x.Voucher.SourceType == "LoanCurrentPortion" && x.Account.Code == "30501" && x.Description == tag).SumAsync(x => (decimal?)(x.Credit - x.Debit), ct) ?? 0;
                decimal delta = due - already;
                result.Rows.Add(new FnCurrentPortionRowDto { LoanId = l.Id, LoanNumber = l.Number, CurrentPortion = due, AlreadyReclassified = already, Change = delta });
                if (delta == 0) continue;
                lines.Add(new AutoLine { Account = longTerm, Debit = delta > 0 ? delta : 0, Credit = delta < 0 ? -delta : 0, Description = $"حصه جاری تسهیلات {l.Number}" });
                lines.Add(new AutoLine { Account = shortTerm, Credit = delta > 0 ? delta : 0, Debit = delta < 0 ? -delta : 0, Description = $"حصه جاری تسهیلات {l.Number}" });
                l.CurrentPortionReclassified = due;
            }
            result.Total = result.Rows.Sum(r => r.Change);
            if (lines.Count == 0) return ResponseDtoHelper.Ok(result);
            string number = await NextNumberAsync("CPL", FinanceNumbers.CurrentPortion, end, ct);
            var v = await AddAutoVoucherAsync(period.FiscalYear.LegalEntityId, end, $"انتقال حصه جاری تسهیلات بلندمدت {period.Title} ({number})", "LoanCurrentPortion", number, lines, FnVoucherType.Normal, null, ct, FnVoucherStatus.Draft);
            if (v.Error != null) return Bad<FnCurrentPortionDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            result.VoucherId = v.Voucher.Id;
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<FnFacilityLimitDto>> SaveFacilityLimitAsync(FnFacilityLimitDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnFacilityLimitDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.BankName) == null || dto.LimitAmount <= 0 || dto.ValidFrom == default) return Bad<FnFacilityLimitDto>("بانک، مبلغ سقف و تاریخ شروع را وارد کنید.");
            FnFacilityLimit f = dto.Id.HasValue ? await _uow.Set<FnFacilityLimit>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && f == null) return ResponseDtoHelper.NotFound<FnFacilityLimitDto>("سقف پیدا نشد.");
            if (f == null) { f = new FnFacilityLimit(); _uow.Set<FnFacilityLimit>().Add(f); }
            f.BankName = Clean(dto.BankName); f.LimitAmount = R(dto.LimitAmount); f.ValidFrom = dto.ValidFrom.Date; f.ValidTo = dto.ValidTo?.Date; f.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            dto.Id = f.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnUtilisationDto>> UtilisationAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnUtilisationDto>(NotAllowed);
            var limits = await _uow.Set<FnFacilityLimit>().AsNoTracking().OrderBy(f => f.BankName).ThenBy(f => f.Id).ToListAsync(ct);
            var used = await _uow.Set<FnLoan>().AsNoTracking().Where(l => l.Status == FnLoanStatus.Active && l.FacilityLimitId != null).GroupBy(l => l.FacilityLimitId.Value)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount - x.PrincipalPaid) }).ToListAsync(ct);
            var dto = new FnUtilisationDto();
            foreach (FnFacilityLimit f in limits)
            {
                decimal u = used.Where(x => x.Id == f.Id).Sum(x => x.Amount);
                dto.Facilities.Add(new FnFacilityLimitDto { Id = f.Id, BankName = f.BankName, LimitAmount = f.LimitAmount, ValidFrom = f.ValidFrom, ValidTo = f.ValidTo, Note = f.Note, Utilised = u, Available = f.LimitAmount - u });
            }
            dto.TotalLimit = dto.Facilities.Sum(f => f.LimitAmount); dto.TotalUtilised = dto.Facilities.Sum(f => f.Utilised); dto.TotalAvailable = dto.TotalLimit - dto.TotalUtilised;
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
