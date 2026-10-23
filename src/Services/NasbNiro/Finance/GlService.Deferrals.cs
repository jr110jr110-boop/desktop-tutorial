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
    public interface IDeferralService
    {
        Task<ResponseDto<FnDeferralDto>> SaveDeferralAsync(FnSaveDeferralDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnDeferralDto>>> GetDeferralsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnDeferralDto>> GetDeferralAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnDeferralEntryDto>>> RunAmortisationAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<FnDeferralEntryDto>> ApproveEntryAsync(int entryId, CancellationToken cancellationToken);
        Task<ResponseDto<FnDeferralDto>> CancelDeferralAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnReversingRunDto>> RunReversingAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnProvisionDto>>> GetProvisionsAsync(DateTime? reviewDueAsOf, CancellationToken cancellationToken);
        Task<ResponseDto<FnProvisionDto>> SaveProvisionAsync(FnSaveProvisionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnProvisionDto>> ProvisionMovementAsync(FnProvisionMovementDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnTrueUpDto>> PayrollProvisionTrueUpAsync(FnTrueUpDto dto, CancellationToken cancellationToken);
    }

    // Prepaid expenses, deferred income, accrual reversals and the provisions register (F3 section 5b).
    public partial class GlService : IDeferralService
    {
        private static readonly PersianCalendar PersianCal5b = new PersianCalendar();
        private static int MonthIndex(DateTime d) => PersianCal5b.GetYear(d) * 12 + PersianCal5b.GetMonth(d) - 1;

        /// <summary>Number of Persian months the deferral touches (start month to end month inclusive).</summary>
        private static int DeferralMonths(DateTime start, DateTime end) => MonthIndex(end) - MonthIndex(start) + 1;

        /// <summary>Straight-line by Persian month; the cumulative method makes the rounding land exactly on the total.</summary>
        private static decimal DeferralAmount(decimal total, int months, int sequence) => R(total * sequence / months) - R(total * (sequence - 1) / months);

        private async Task<FnDeferralDto> DeferralDtoAsync(FnDeferral d, CancellationToken ct)
        {
            var entries = await _uow.Set<FnDeferralEntry>().AsNoTracking().Where(e => e.DeferralId == d.Id).OrderBy(e => e.Sequence).ToListAsync(ct);
            var codes = await _uow.Set<FnAccount>().AsNoTracking().Where(a => a.Id == d.BalanceAccountId || a.Id == d.ResultAccountId).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            decimal amortised = entries.Where(e => e.Status == 1).Sum(e => e.Amount);
            return new FnDeferralDto
            {
                Id = d.Id, Number = d.Number, Kind = d.Kind, Description = d.Description, TotalAmount = d.TotalAmount, StartDate = d.StartDate, EndDate = d.EndDate, Months = DeferralMonths(d.StartDate, d.EndDate),
                BalanceAccountCode = codes.GetValueOrDefault(d.BalanceAccountId), ResultAccountCode = codes.GetValueOrDefault(d.ResultAccountId), Status = d.Status, Amortised = amortised, Remaining = d.TotalAmount - amortised,
                Entries = entries.Select(e => new FnDeferralEntryDto { Id = e.Id, DeferralId = e.DeferralId, PeriodId = e.PeriodId, Sequence = e.Sequence, Amount = e.Amount, Status = e.Status, PreparedByUserId = e.PreparedByUserId, ApprovedByUserId = e.ApprovedByUserId, VoucherId = e.VoucherId }).ToList(),
            };
        }

        public async Task<ResponseDto<FnDeferralDto>> SaveDeferralAsync(FnSaveDeferralDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnDeferralDto>(NotAllowed);
            if (dto == null || Clean(dto.Description) == null) return Bad<FnDeferralDto>("شرح را وارد کنید.");
            if (dto.TotalAmount <= 0) return Bad<FnDeferralDto>("مبلغ کل باید بیشتر از صفر باشد.");
            if (dto.StartDate == default || dto.EndDate.Date < dto.StartDate.Date) return Bad<FnDeferralDto>("بازه زمانی نامعتبر است.");
            if (!Enum.IsDefined(typeof(FnDeferralKind), dto.Kind)) return Bad<FnDeferralDto>("نوع نامعتبر است.");
            FnAccount balance = await AccountByCodeAsync(dto.BalanceAccountCode, ct), result = await AccountByCodeAsync(dto.ResultAccountCode, ct);
            if (balance == null || result == null || balance.Level != FnAccountLevel.Subsidiary || result.Level != FnAccountLevel.Subsidiary) return Bad<FnDeferralDto>("حساب مانده یا حساب هزینه/درآمد پیدا نشد.");
            if (dto.Kind == FnDeferralKind.PrepaidExpense && (balance.Type != FnAccountType.Asset || result.Type != FnAccountType.Expense)) return Bad<FnDeferralDto>("پیش‌پرداخت: حساب مانده باید دارایی و حساب مقابل هزینه باشد.");
            if (dto.Kind == FnDeferralKind.DeferredIncome && (balance.Type != FnAccountType.Liability || result.Type != FnAccountType.Revenue)) return Bad<FnDeferralDto>("درآمد انتقالی: حساب مانده باید بدهی و حساب مقابل درآمد باشد.");
            int? cc = null;
            if (Clean(dto.CostCenterCode) != null)
            {
                cc = await _uow.Set<CostCenter>().AsNoTracking().Where(c => c.Code == dto.CostCenterCode.Trim()).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
                if (!cc.HasValue) return Bad<FnDeferralDto>("مرکز هزینه پیدا نشد.");
            }
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var d = new FnDeferral
            {
                Number = await NextNumberAsync("DEF", FinanceNumbers.Deferral, dto.StartDate, ct), LegalEntityId = entityId, Kind = dto.Kind, Description = Clean(dto.Description), TotalAmount = R(dto.TotalAmount),
                StartDate = dto.StartDate.Date, EndDate = dto.EndDate.Date, BalanceAccountId = balance.Id, ResultAccountId = result.Id, CostCenterId = cc, ContractId = dto.ContractId, Status = FnDeferralStatus.Active,
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnDeferral>().Add(d);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await DeferralDtoAsync(d, ct));
        }

        public async Task<ResponseDto<List<FnDeferralDto>>> GetDeferralsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnDeferralDto>>(NotAllowed);
            var rows = await _uow.Set<FnDeferral>().AsNoTracking().OrderByDescending(d => d.Id).Take(200).ToListAsync(ct);
            var list = new List<FnDeferralDto>();
            foreach (FnDeferral d in rows) list.Add(await DeferralDtoAsync(d, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<FnDeferralDto>> GetDeferralAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnDeferralDto>(NotAllowed);
            FnDeferral d = await _uow.Set<FnDeferral>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return d == null ? ResponseDtoHelper.NotFound<FnDeferralDto>("برنامه پیدا نشد.") : ResponseDtoHelper.Ok(await DeferralDtoAsync(d, ct));
        }

        public async Task<ResponseDto<List<FnDeferralEntryDto>>> RunAmortisationAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<List<FnDeferralEntryDto>>(NotAllowed);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<List<FnDeferralEntryDto>>("دوره پیدا نشد.");
            int pm = MonthIndex(period.StartDate);
            var created = new List<FnDeferralEntry>();
            var active = await _uow.Set<FnDeferral>().Where(d => d.Status == FnDeferralStatus.Active && d.LegalEntityId == period.FiscalYear.LegalEntityId).ToListAsync(ct);
            foreach (FnDeferral d in active)
            {
                int months = DeferralMonths(d.StartDate, d.EndDate);
                int seq = pm - MonthIndex(d.StartDate) + 1;
                if (seq < 1 || seq > months) continue;
                if (await _uow.Set<FnDeferralEntry>().AnyAsync(e => e.DeferralId == d.Id && (e.PeriodId == periodId || e.Sequence == seq), ct)) continue;
                var e = new FnDeferralEntry { DeferralId = d.Id, PeriodId = periodId, Sequence = seq, Amount = DeferralAmount(d.TotalAmount, months, seq), Status = 0, PreparedByUserId = Me };
                _uow.Set<FnDeferralEntry>().Add(e);
                created.Add(e);
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(created.Select(e => new FnDeferralEntryDto { Id = e.Id, DeferralId = e.DeferralId, PeriodId = e.PeriodId, Sequence = e.Sequence, Amount = e.Amount, Status = e.Status, PreparedByUserId = e.PreparedByUserId }).ToList());
        }

        public async Task<ResponseDto<FnDeferralEntryDto>> ApproveEntryAsync(int entryId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnDeferralEntryDto>(NotAllowed);
            FnDeferralEntry e = await _uow.Set<FnDeferralEntry>().Include(x => x.Deferral).FirstOrDefaultAsync(x => x.Id == entryId, ct);
            if (e == null) return ResponseDtoHelper.NotFound<FnDeferralEntryDto>("ردیف استهلاک پیدا نشد.");
            if (e.Status != 0) return Bad<FnDeferralEntryDto>("این ردیف قبلاً تأیید شده است.");
            if (e.PreparedByUserId == Me) return Bad<FnDeferralEntryDto>(SameUser);
            if (e.Deferral.Status != FnDeferralStatus.Active) return Bad<FnDeferralEntryDto>("برنامه فعال نیست.");
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == e.PeriodId, ct);
            FnAccount balance = await _uow.Set<FnAccount>().FirstAsync(a => a.Id == e.Deferral.BalanceAccountId, ct), result = await _uow.Set<FnAccount>().FirstAsync(a => a.Id == e.Deferral.ResultAccountId, ct);
            string text = $"استهلاک ماهانه {e.Deferral.Number} ({e.Sequence}/{DeferralMonths(e.Deferral.StartDate, e.Deferral.EndDate)}) - {e.Deferral.Description}";
            var lines = e.Deferral.Kind == FnDeferralKind.PrepaidExpense
                ? new List<AutoLine>
                {
                    new AutoLine { Account = result, CostCenterId = e.Deferral.CostCenterId, ContractId = e.Deferral.ContractId, Debit = e.Amount, Description = text },
                    new AutoLine { Account = balance, ContractId = e.Deferral.ContractId, Credit = e.Amount, Description = text },
                }
                : new List<AutoLine>
                {
                    new AutoLine { Account = balance, ContractId = e.Deferral.ContractId, Debit = e.Amount, Description = text },
                    new AutoLine { Account = result, CostCenterId = e.Deferral.CostCenterId, ContractId = e.Deferral.ContractId, Credit = e.Amount, Description = text },
                };
            var v = await AddAutoVoucherAsync(e.Deferral.LegalEntityId, period.EndDate.Date, text, "Deferral", e.Deferral.Number, lines, FnVoucherType.Auto, null, ct);
            if (v.Error != null) return Bad<FnDeferralEntryDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            e.Status = 1; e.ApprovedByUserId = Me; e.VoucherId = v.Voucher.Id;
            decimal done = await _uow.Set<FnDeferralEntry>().Where(x => x.DeferralId == e.DeferralId && x.Status == 1 && x.Id != e.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            if (done + e.Amount == e.Deferral.TotalAmount) e.Deferral.Status = FnDeferralStatus.Completed;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnDeferralEntryDto { Id = e.Id, DeferralId = e.DeferralId, PeriodId = e.PeriodId, Sequence = e.Sequence, Amount = e.Amount, Status = e.Status, PreparedByUserId = e.PreparedByUserId, ApprovedByUserId = e.ApprovedByUserId, VoucherId = e.VoucherId });
        }

        public async Task<ResponseDto<FnDeferralDto>> CancelDeferralAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnDeferralDto>(NotAllowed + " (مدیر مالی)");
            FnDeferral d = await _uow.Set<FnDeferral>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d == null) return ResponseDtoHelper.NotFound<FnDeferralDto>("برنامه پیدا نشد.");
            if (d.Status != FnDeferralStatus.Active) return Bad<FnDeferralDto>("برنامه فعال نیست.");
            if (await _uow.Set<FnDeferralEntry>().AnyAsync(e => e.DeferralId == id && e.Status == 0, ct)) return Bad<FnDeferralDto>("ردیف تأییدنشده دارد؛ ابتدا تأیید کنید.");
            d.Status = FnDeferralStatus.Cancelled;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await DeferralDtoAsync(d, ct));
        }

        // ------------------------------------------------------------------------------------------ accruals with automatic reversal

        public async Task<ResponseDto<FnReversingRunDto>> RunReversingAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnReversingRunDto>(NotAllowed);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<FnReversingRunDto>("دوره پیدا نشد.");
            DateTime date = period.EndDate.Date.AddDays(1);
            int entityId = period.FiscalYear.LegalEntityId;
            var result = new FnReversingRunDto { PeriodId = periodId };
            var vouchers = await _uow.Set<FnVoucher>().Include(v => v.Lines).ThenInclude(l => l.Account)
                .Where(v => v.ReverseNextPeriod && v.AutoReversalVoucherId == null && v.Date >= period.StartDate && v.Date <= period.EndDate && v.Status != FnVoucherStatus.Draft && v.FiscalYear.LegalEntityId == entityId).ToListAsync(ct);
            var made = new List<(FnVoucher Original, FnVoucher Reversal)>();
            foreach (FnVoucher o in vouchers)
            {
                var lines = o.Lines.OrderBy(l => l.RowNumber).Select(l => new AutoLine
                {
                    Account = l.Account, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Credit, Credit = l.Debit, Description = l.Description, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId,
                    CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate,
                }).ToList();
                var r = await AddAutoVoucherAsync(entityId, date, $"برگشت خودکار سند تعهدی {o.Number?.ToString() ?? o.TemporaryNumber.ToString()}", "AccrualReversal", o.Number?.ToString() ?? o.TemporaryNumber.ToString(), lines, FnVoucherType.Reversal, o, ct, FnVoucherStatus.Draft);
                if (r.Error != null) { result.Skipped.Add($"{o.TemporaryNumber}: {r.Error}"); continue; }
                made.Add((o, r.Voucher));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var m in made) { m.Original.AutoReversalVoucherId = m.Reversal.Id; result.CreatedVoucherIds.Add(m.Reversal.Id); }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        // ------------------------------------------------------------------------------------------ provisions register

        private async Task<FnProvisionDto> ProvisionDtoAsync(FnProvision p, DateTime asOf, CancellationToken ct)
        {
            var movements = await _uow.Set<FnProvisionMovement>().AsNoTracking().Where(m => m.ProvisionId == p.Id).OrderBy(m => m.Id).ToListAsync(ct);
            var codes = await _uow.Set<FnAccount>().AsNoTracking().Where(a => a.Id == p.ExpenseAccountId || a.Id == p.LiabilityAccountId).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            return new FnProvisionDto
            {
                Id = p.Id, Number = p.Number, Kind = p.Kind, Description = p.Description, Balance = p.Balance, ReviewDate = p.ReviewDate, ReviewDue = p.IsActive && p.ReviewDate.HasValue && p.ReviewDate.Value.Date <= asOf.Date,
                IsActive = p.IsActive, ExpenseAccountCode = codes.GetValueOrDefault(p.ExpenseAccountId), LiabilityAccountCode = codes.GetValueOrDefault(p.LiabilityAccountId),
                Movements = movements.Select(m => new FnProvisionMovementDto { ProvisionId = m.ProvisionId, Date = m.Date, Amount = m.Amount, Reason = m.Reason, VoucherId = m.VoucherId }).ToList(),
            };
        }

        public async Task<ResponseDto<List<FnProvisionDto>>> GetProvisionsAsync(DateTime? reviewDueAsOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnProvisionDto>>(NotAllowed);
            DateTime asOf = (reviewDueAsOf ?? DateTime.Today).Date;
            var rows = await _uow.Set<FnProvision>().AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
            var list = new List<FnProvisionDto>();
            foreach (FnProvision p in rows) list.Add(await ProvisionDtoAsync(p, asOf, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<FnProvisionDto>> SaveProvisionAsync(FnSaveProvisionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnProvisionDto>(NotAllowed);
            if (dto == null || Clean(dto.Description) == null) return Bad<FnProvisionDto>("شرح را وارد کنید.");
            if (!Enum.IsDefined(typeof(FnProvisionKind), dto.Kind)) return Bad<FnProvisionDto>("نوع ذخیره نامعتبر است.");
            FnAccount expense = await AccountByCodeAsync(dto.ExpenseAccountCode, ct), liability = await AccountByCodeAsync(dto.LiabilityAccountCode, ct);
            if (expense == null || liability == null || expense.Level != FnAccountLevel.Subsidiary || liability.Level != FnAccountLevel.Subsidiary) return Bad<FnProvisionDto>("حساب هزینه یا حساب ذخیره پیدا نشد.");
            if (liability.Type != FnAccountType.Liability || expense.Type != FnAccountType.Expense) return Bad<FnProvisionDto>("حساب ذخیره باید بدهی و حساب مقابل هزینه باشد.");
            FnProvision p = dto.Id.HasValue ? await _uow.Set<FnProvision>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && p == null) return ResponseDtoHelper.NotFound<FnProvisionDto>("ذخیره پیدا نشد.");
            if (p == null)
            {
                p = new FnProvision { Number = await NextNumberAsync("PRV", FinanceNumbers.Provision, DateTime.Today, ct), CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
                _uow.Set<FnProvision>().Add(p);
            }
            else if (p.Movements != null && (p.ExpenseAccountId != expense.Id || p.LiabilityAccountId != liability.Id) && p.Balance != 0) return Bad<FnProvisionDto>("پس از ثبت مانده، حساب‌ها تغییر نمی‌کند.");
            p.Kind = dto.Kind; p.Description = Clean(dto.Description); p.ReviewDate = dto.ReviewDate?.Date; p.ExpenseAccountId = expense.Id; p.LiabilityAccountId = liability.Id; p.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ProvisionDtoAsync(p, DateTime.Today, ct));
        }

        public async Task<ResponseDto<FnProvisionDto>> ProvisionMovementAsync(FnProvisionMovementDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnProvisionDto>(NotAllowed);
            if (dto == null || !dto.ProvisionId.HasValue || dto.Amount == 0 || dto.Date == default) return Bad<FnProvisionDto>("ذخیره، تاریخ و مبلغ را وارد کنید.");
            if (Clean(dto.Reason) == null) return Bad<FnProvisionDto>("دلیل حرکت را وارد کنید.");
            FnProvision p = await _uow.Set<FnProvision>().FirstOrDefaultAsync(x => x.Id == dto.ProvisionId.Value, ct);
            if (p == null) return ResponseDtoHelper.NotFound<FnProvisionDto>("ذخیره پیدا نشد.");
            if (!p.IsActive) return Bad<FnProvisionDto>("ذخیره غیرفعال است.");
            decimal amount = R(dto.Amount);
            if (p.Balance + amount < 0) return Bad<FnProvisionDto>("مصرف ذخیره از مانده آن بیشتر است.");
            FnAccount expense = await _uow.Set<FnAccount>().FirstAsync(a => a.Id == p.ExpenseAccountId, ct), liability = await _uow.Set<FnAccount>().FirstAsync(a => a.Id == p.LiabilityAccountId, ct);
            FnAccount counter = expense;
            if (amount < 0 && Clean(dto.CounterAccountCode) != null)
            {
                counter = await AccountByCodeAsync(dto.CounterAccountCode, ct);
                if (counter == null || counter.Level != FnAccountLevel.Subsidiary) return Bad<FnProvisionDto>("حساب مقابل پیدا نشد.");
            }
            decimal abs = Math.Abs(amount);
            string text = $"{(amount > 0 ? "افزایش" : "مصرف/برگشت")} ذخیره {p.Number}: {dto.Reason.Trim()}";
            var lines = amount > 0
                ? new List<AutoLine> { new AutoLine { Account = expense, Debit = abs, Description = text }, new AutoLine { Account = liability, Credit = abs, Description = text } }
                : new List<AutoLine> { new AutoLine { Account = liability, Debit = abs, Description = text }, new AutoLine { Account = counter, Credit = abs, Description = text } };
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var v = await AddAutoVoucherAsync(entityId, dto.Date.Date, text, "Provision", p.Number, lines, FnVoucherType.Auto, null, ct);
            if (v.Error != null) return Bad<FnProvisionDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            p.Balance += amount;
            _uow.Set<FnProvisionMovement>().Add(new FnProvisionMovement { ProvisionId = p.Id, Date = dto.Date.Date, Amount = amount, Reason = dto.Reason.Trim(), VoucherId = v.Voucher.Id, UserId = Me });
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ProvisionDtoAsync(p, DateTime.Today, ct));
        }

        /// <summary>
        /// Year-end / period-close true-up of a payroll provision (bonus 30402, leave 30403, severance 40201) to the amount the accountant computed:
        /// a DRAFT voucher for the difference, which a second person checks and the finance manager finalises like any manual voucher.
        /// </summary>
        public async Task<ResponseDto<FnTrueUpDto>> PayrollProvisionTrueUpAsync(FnTrueUpDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnTrueUpDto>(NotAllowed);
            if (dto == null || dto.RequiredBalance < 0 || dto.Date == default) return Bad<FnTrueUpDto>("تاریخ و مانده لازم ذخیره را وارد کنید.");
            if (Clean(dto.Reason) == null) return Bad<FnTrueUpDto>("توضیح محاسبه را بنویسید.");
            string kind = (Clean(dto.Kind) ?? "").ToLowerInvariant();
            string provisionRole, expenseRole, fallbackProvision, fallbackExpense;
            switch (kind)
            {
                case "bonus": provisionRole = AccountRoles.BonusProvision; expenseRole = AccountRoles.PayrollExpenseAdmin; fallbackProvision = "30402"; fallbackExpense = "80101"; break;
                case "leave": provisionRole = AccountRoles.LeaveProvision; expenseRole = AccountRoles.PayrollExpenseAdmin; fallbackProvision = "30403"; fallbackExpense = "80101"; break;
                case "severance": provisionRole = AccountRoles.SeveranceProvision; expenseRole = AccountRoles.SeveranceExpense; fallbackProvision = "40201"; fallbackExpense = "80103"; break;
                default: return Bad<FnTrueUpDto>("نوع ذخیره باید bonus، leave یا severance باشد.");
            }
            FnAccount provision = await RoleAccountAsync(provisionRole, fallbackProvision, ct), expense = await RoleAccountAsync(expenseRole, fallbackExpense, ct);
            if (provision == null || expense == null) return Bad<FnTrueUpDto>("حساب ذخیره یا هزینه پیدا نشد.");
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            decimal balance = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.AccountId == provision.Id && l.Voucher.Date <= dto.Date.Date && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose)
                .SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            decimal diff = R(dto.RequiredBalance) - balance;
            var result = new FnTrueUpDto { Kind = kind, Date = dto.Date.Date, RequiredBalance = R(dto.RequiredBalance), CurrentBalance = balance, Difference = diff, Reason = dto.Reason };
            if (diff == 0) return Bad<FnTrueUpDto>("مانده ذخیره با مقدار لازم برابر است؛ سندی لازم نیست.");
            string text = $"تعدیل ذخیره {provision.Code} به {R(dto.RequiredBalance):N0}: {dto.Reason.Trim()}";
            decimal abs = Math.Abs(diff);
            var lines = diff > 0
                ? new List<AutoLine> { new AutoLine { Account = expense, Debit = abs, Description = text }, new AutoLine { Account = provision, Credit = abs, Description = text } }
                : new List<AutoLine> { new AutoLine { Account = provision, Debit = abs, Description = text }, new AutoLine { Account = expense, Credit = abs, Description = text } };
            var v = await AddAutoVoucherAsync(entityId, dto.Date.Date, text, "ProvisionTrueUp", provision.Code, lines, FnVoucherType.Normal, null, ct, FnVoucherStatus.Draft);
            if (v.Error != null) return Bad<FnTrueUpDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            result.VoucherId = v.Voucher.Id;
            return ResponseDtoHelper.Ok(result);
        }
    }
}
