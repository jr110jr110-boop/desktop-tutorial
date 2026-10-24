using System;
using System.Collections.Generic;
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
    // Finance release F3b: the bank-guarantee register (ضمانت‌نامه بانکی). Issued guarantees are booked as memorandum entries (90101 / 90201),
    // count against the limit the bank gave, and their fee is a prepaid expense amortised month by month through a deferral.
    public partial class GlService
    {
        private const string FeePrepaidCode = "10604";

        private async Task<List<FnBankGuaranteeDto>> GuaranteeDtosAsync(List<FnBankGuarantee> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(g => g.Id).ToList();
            var events = await _uow.Set<FnBankGuaranteeEvent>().AsNoTracking().Where(e => ids.Contains(e.GuaranteeId)).OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync(ct);
            var deferrals = await _uow.Set<FnDeferral>().AsNoTracking().Where(d => d.GuaranteeId != null && ids.Contains(d.GuaranteeId.Value)).Select(d => new { d.Id, GuaranteeId = d.GuaranteeId.Value, d.TotalAmount }).ToListAsync(ct);
            List<int> deferralIds = deferrals.Select(d => d.Id).ToList();
            var done = await _uow.Set<FnDeferralEntry>().AsNoTracking().Where(e => deferralIds.Contains(e.DeferralId) && e.Status == 1).GroupBy(e => e.DeferralId).Select(g => new { g.Key, Amount = g.Sum(e => e.Amount) }).ToListAsync(ct);
            DateTime today = DateTime.Today;
            var list = new List<FnBankGuaranteeDto>();
            foreach (FnBankGuarantee g in rows)
            {
                decimal total = deferrals.Where(d => d.GuaranteeId == g.Id).Sum(d => d.TotalAmount);
                decimal amortised = deferrals.Where(d => d.GuaranteeId == g.Id).Sum(d => done.FirstOrDefault(x => x.Key == d.Id)?.Amount ?? 0);
                list.Add(new FnBankGuaranteeDto
                {
                    Id = g.Id, Number = g.Number, Direction = g.Direction, Kind = g.Kind, BankName = g.BankName, GuaranteeNo = g.GuaranteeNo, PartyName = g.PartyName, ProviderId = g.ProviderId,
                    CustomerId = g.CustomerId, ContractId = g.ContractId, Amount = g.Amount, IssueDate = g.IssueDate, ExpiryDate = g.ExpiryDate, FeeAmount = total, FeeAmortised = amortised,
                    FeeRemaining = g.Status == FnBankGuaranteeStatus.Active ? total - amortised : 0, Status = g.Status, ClosedDate = g.ClosedDate, DaysToExpiry = (int)(g.ExpiryDate.Date - today).TotalDays, Note = g.Note,
                    Events = events.Where(e => e.GuaranteeId == g.Id).Select(e => new FnGuaranteeEventDto { Id = e.Id, Date = e.Date, Kind = e.Kind, OldExpiry = e.OldExpiry, NewExpiry = e.NewExpiry, Amount = e.Amount, FeeAmount = e.FeeAmount, Note = e.Note }).ToList(),
                });
            }
            return list;
        }

        public async Task<ResponseDto<List<FnBankGuaranteeDto>>> GetBankGuaranteesAsync(int? direction, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnBankGuaranteeDto>>(NotAllowed);
            IQueryable<FnBankGuarantee> q = _uow.Set<FnBankGuarantee>().AsNoTracking();
            if (direction.HasValue) q = q.Where(g => (int)g.Direction == direction.Value);
            return ResponseDtoHelper.Ok(await GuaranteeDtosAsync(await q.OrderByDescending(g => g.Id).Take(500).ToListAsync(ct), ct));
        }

        private AutoLine MemoLine(FnAccount account, decimal debit, decimal credit, string text, int? contractId) => new AutoLine { Account = account, Debit = debit, Credit = credit, Description = text, ContractId = contractId };

        /// <summary>The fee lines of a guarantee (debit prepaid, credit the bank account) and the deferral that spreads it.</summary>
        private async Task<(List<AutoLine> Lines, FnDeferral Deferral, string Error)> GuaranteeFeeAsync(FnBankGuarantee g, decimal fee, DateTime from, DateTime to, string text, CancellationToken ct)
        {
            FnAccount prepaid = await AccountByCodeAsync(FeePrepaidCode, ct), expense = await RoleAccountAsync(AccountRoles.BankFees, "80201", ct);
            if (prepaid == null || expense == null) return (null, null, "حساب پیش‌پرداخت یا کارمزد بانکی پیدا نشد.");
            if (!g.BankAccountId.HasValue) return (null, null, "برای ثبت کارمزد، حساب بانکی پرداخت‌کننده را وارد کنید.");
            var details = new DetailResolver(_uow);
            var bank = await BankLedgerAsync(g.BankAccountId.Value, details, ct);
            if (bank.Account == null || !bank.Bank.IsActive) return (null, null, "حساب بانکی پیدا نشد یا غیرفعال است.");
            if (to.Date < from.Date) return (null, null, "بازه کارمزد نامعتبر است.");
            var lines = new List<AutoLine>
            {
                new AutoLine { Account = prepaid, Debit = fee, Description = text, ContractId = g.ContractId },
                new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Credit = fee, Description = text },
            };
            var deferral = new FnDeferral
            {
                Number = await NextNumberAsync("DEF", FinanceNumbers.Deferral, from, ct), LegalEntityId = g.LegalEntityId, Kind = FnDeferralKind.PrepaidExpense, Description = text,
                TotalAmount = fee, StartDate = from.Date, EndDate = to.Date, BalanceAccountId = prepaid.Id, ResultAccountId = expense.Id, ContractId = g.ContractId,
                Status = FnDeferralStatus.Active, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            return (lines, deferral, null);
        }

        public async Task<ResponseDto<FnBankGuaranteeDto>> SaveBankGuaranteeAsync(FnSaveBankGuaranteeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnBankGuaranteeDto>(NotAllowed);
            if (dto == null || Clean(dto.BankName) == null || Clean(dto.PartyName) == null) return Bad<FnBankGuaranteeDto>("بانک و نام طرف حساب را وارد کنید.");
            if (!Enum.IsDefined(typeof(FnBankGuaranteeDirection), dto.Direction) || !Enum.IsDefined(typeof(FnBankGuaranteeKind), dto.Kind)) return Bad<FnBankGuaranteeDto>("جهت یا نوع ضمانت‌نامه نامعتبر است.");
            decimal amount = R(dto.Amount), fee = R(dto.FeeAmount);
            if (amount <= 0) return Bad<FnBankGuaranteeDto>("مبلغ باید بیشتر از صفر باشد.");
            if (dto.IssueDate == default || dto.ExpiryDate.Date < dto.IssueDate.Date) return Bad<FnBankGuaranteeDto>("تاریخ سررسید نمی‌تواند قبل از تاریخ صدور باشد.");
            if (fee < 0) return Bad<FnBankGuaranteeDto>("کارمزد نمی‌تواند منفی باشد.");
            if (dto.Direction == FnBankGuaranteeDirection.Received && (fee != 0 || dto.BankAccountId.HasValue)) return Bad<FnBankGuaranteeDto>("کارمزد و حساب بانکی فقط برای ضمانت‌نامه صادره است.");
            string no = Clean(dto.GuaranteeNo);
            string bankName = Clean(dto.BankName);
            if (no != null && await _uow.Set<FnBankGuarantee>().AnyAsync(x => x.Direction == dto.Direction && x.BankName == bankName && x.GuaranteeNo == no, ct)) return Bad<FnBankGuaranteeDto>("ضمانت‌نامه‌ای با این شماره در همین بانک ثبت شده است.");
            if (dto.ProviderId.HasValue && !await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value, ct)) return Bad<FnBankGuaranteeDto>("تامین‌کننده پیدا نشد.");
            if (dto.CustomerId.HasValue && !await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId.Value, ct)) return Bad<FnBankGuaranteeDto>("مشتری پیدا نشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var g = new FnBankGuarantee
            {
                Number = await NextNumberAsync("BG", FinanceNumbers.BankGuarantee, dto.IssueDate, ct), LegalEntityId = entityId, Direction = dto.Direction, Kind = dto.Kind, BankName = bankName,
                BankAccountId = dto.BankAccountId, GuaranteeNo = no, PartyName = Clean(dto.PartyName), ProviderId = dto.ProviderId, CustomerId = dto.CustomerId, ContractId = dto.ContractId, Amount = amount,
                IssueDate = dto.IssueDate.Date, ExpiryDate = dto.ExpiryDate.Date, FeeAmount = fee, Status = FnBankGuaranteeStatus.Active, Note = Clean(dto.Note), CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            var lines = new List<AutoLine>();
            FnDeferral deferral = null;
            if (g.Direction == FnBankGuaranteeDirection.Issued)
            {
                FnAccount memo = await RoleAccountAsync(AccountRoles.GuaranteeIssued, "90101", ct), contra = await RoleAccountAsync(AccountRoles.GuaranteeIssuedContra, "90201", ct);
                if (memo == null || contra == null) return Bad<FnBankGuaranteeDto>("حساب‌های انتظامی ضمانت‌نامه پیدا نشد.");
                string text = $"ضمانت‌نامه بانکی صادره {g.Number} - {g.BankName} - {g.PartyName}";
                lines.Add(MemoLine(memo, amount, 0, text, g.ContractId));
                lines.Add(MemoLine(contra, 0, amount, text, g.ContractId));
                if (fee > 0)
                {
                    var f = await GuaranteeFeeAsync(g, fee, g.IssueDate, g.ExpiryDate, $"کارمزد ضمانت‌نامه {g.Number}", ct);
                    if (f.Error != null) return Bad<FnBankGuaranteeDto>(f.Error);
                    lines.AddRange(f.Lines);
                    deferral = f.Deferral;
                }
                var v = await AddAutoVoucherAsync(entityId, g.IssueDate, "ثبت " + text, "BankGuarantee", g.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
                if (v.Error != null) return Bad<FnBankGuaranteeDto>(v.Error);
                g.RegistrationVoucher = v.Voucher;
            }
            _uow.Set<FnBankGuarantee>().Add(g);
            await _uow.SaveChangesAsync(ct);
            if (deferral != null)
            {
                deferral.GuaranteeId = g.Id;
                _uow.Set<FnDeferral>().Add(deferral);
                await _uow.SaveChangesAsync(ct);
            }
            return ResponseDtoHelper.Ok((await GuaranteeDtosAsync(new List<FnBankGuarantee> { g }, ct))[0]);
        }

        public async Task<ResponseDto<FnBankGuaranteeDto>> BankGuaranteeActionAsync(FnGuaranteeActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnBankGuaranteeDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnBankGuaranteeDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBankGuarantee g = await _uow.Set<FnBankGuarantee>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (g == null) return ResponseDtoHelper.NotFound<FnBankGuaranteeDto>("ضمانت‌نامه پیدا نشد.");
            if (g.Status != FnBankGuaranteeStatus.Active) return Bad<FnBankGuaranteeDto>("این ضمانت‌نامه دیگر فعال نیست.");
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            if (date < g.IssueDate) return Bad<FnBankGuaranteeDto>("تاریخ رویداد قبل از تاریخ صدور است.");
            string action = dto.Action.Trim().ToLowerInvariant();
            bool issued = g.Direction == FnBankGuaranteeDirection.Issued;
            var lines = new List<AutoLine>();
            var ev = new FnBankGuaranteeEvent { GuaranteeId = g.Id, Date = date, Note = Clean(dto.Note), UserId = Me };
            FnDeferral extraDeferral = null;
            string description;
            FnAccount memo = null, contra = null;
            if (issued)
            {
                memo = await RoleAccountAsync(AccountRoles.GuaranteeIssued, "90101", ct); contra = await RoleAccountAsync(AccountRoles.GuaranteeIssuedContra, "90201", ct);
                if (memo == null || contra == null) return Bad<FnBankGuaranteeDto>("حساب‌های انتظامی ضمانت‌نامه پیدا نشد.");
            }
            switch (action)
            {
                case "extend":
                {
                    if (!dto.NewExpiry.HasValue || dto.NewExpiry.Value.Date <= g.ExpiryDate) return Bad<FnBankGuaranteeDto>("تاریخ سررسید جدید باید بعد از سررسید فعلی باشد.");
                    decimal fee = R(dto.FeeAmount);
                    if (fee < 0 || (!issued && fee != 0)) return Bad<FnBankGuaranteeDto>("کارمزد تمدید فقط برای ضمانت‌نامه صادره و غیرمنفی است.");
                    ev.Kind = FnGuaranteeEventKind.Extended; ev.OldExpiry = g.ExpiryDate; ev.NewExpiry = dto.NewExpiry.Value.Date; ev.FeeAmount = fee;
                    description = $"تمدید ضمانت‌نامه {g.Number} تا {FiscalCalendars.Text(ev.NewExpiry.Value)}";
                    if (fee > 0)
                    {
                        var f = await GuaranteeFeeAsync(g, fee, g.ExpiryDate.AddDays(1), ev.NewExpiry.Value, $"کارمزد تمدید ضمانت‌نامه {g.Number}", ct);
                        if (f.Error != null) return Bad<FnBankGuaranteeDto>(f.Error);
                        lines.AddRange(f.Lines);
                        extraDeferral = f.Deferral;
                    }
                    g.ExpiryDate = ev.NewExpiry.Value;
                    break;
                }
                case "reduce":
                {
                    decimal part = R(dto.Amount);
                    if (part <= 0 || part >= g.Amount) return Bad<FnBankGuaranteeDto>("مبلغ کاهش باید بیشتر از صفر و کمتر از مبلغ ضمانت‌نامه باشد.");
                    ev.Kind = FnGuaranteeEventKind.Reduced; ev.Amount = part;
                    description = $"کاهش مبلغ ضمانت‌نامه {g.Number} به اندازه {part:N0}";
                    if (issued) { lines.Add(MemoLine(contra, part, 0, description, g.ContractId)); lines.Add(MemoLine(memo, 0, part, description, g.ContractId)); }
                    g.Amount -= part;
                    break;
                }
                case "release":
                case "expire":
                case "claim":
                {
                    bool claim = action == "claim";
                    decimal claimed = 0;
                    if (claim)
                    {
                        claimed = R(dto.Amount) == 0 ? g.Amount : R(dto.Amount);
                        if (claimed <= 0 || claimed > g.Amount) return Bad<FnBankGuaranteeDto>("مبلغ مطالبه‌شده باید بیشتر از صفر و حداکثر مبلغ ضمانت‌نامه باشد.");
                    }
                    ev.Kind = claim ? FnGuaranteeEventKind.Claimed : action == "expire" ? FnGuaranteeEventKind.Expired : FnGuaranteeEventKind.Released;
                    ev.Amount = claim ? claimed : g.Amount;
                    description = (claim ? "مطالبه ضمانت‌نامه " : action == "expire" ? "انقضای ضمانت‌نامه " : "آزادسازی ضمانت‌نامه ") + g.Number;
                    if (issued)
                    {
                        lines.Add(MemoLine(contra, g.Amount, 0, description, g.ContractId)); lines.Add(MemoLine(memo, 0, g.Amount, description, g.ContractId));
                        // the part of the fee that is not yet expensed is expensed now and the schedule stops
                        List<FnDeferral> open = await _uow.Set<FnDeferral>().Where(d => d.GuaranteeId == g.Id && d.Status == FnDeferralStatus.Active).ToListAsync(ct);
                        FnAccount prepaid = await AccountByCodeAsync(FeePrepaidCode, ct), expense = await RoleAccountAsync(AccountRoles.BankFees, "80201", ct);
                        foreach (FnDeferral d in open)
                        {
                            if (await _uow.Set<FnDeferralEntry>().AnyAsync(e => e.DeferralId == d.Id && e.Status == 0, ct)) return Bad<FnBankGuaranteeDto>("ردیف استهلاک تأییدنشده کارمزد دارد؛ ابتدا آن را تأیید کنید.");
                            decimal done = await _uow.Set<FnDeferralEntry>().Where(e => e.DeferralId == d.Id && e.Status == 1).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
                            decimal rest = d.TotalAmount - done;
                            if (rest > 0)
                            {
                                lines.Add(new AutoLine { Account = expense, Debit = rest, Description = "باقیمانده کارمزد " + g.Number, ContractId = g.ContractId });
                                lines.Add(new AutoLine { Account = prepaid, Credit = rest, Description = "باقیمانده کارمزد " + g.Number, ContractId = g.ContractId });
                            }
                            d.Status = rest > 0 ? FnDeferralStatus.Cancelled : FnDeferralStatus.Completed;
                        }
                        if (claim)
                        {
                            FnAccount loss = await AccountByCodeAsync("80303", ct), owed = await RoleAccountAsync(AccountRoles.OtherPayable, "30208", ct);
                            if (loss == null || owed == null) return Bad<FnBankGuaranteeDto>("حساب خسارت یا سایر پرداختنی پیدا نشد.");
                            lines.Add(new AutoLine { Account = loss, Debit = claimed, Description = description, ContractId = g.ContractId });
                            lines.Add(new AutoLine { Account = owed, Credit = claimed, Description = description });
                        }
                    }
                    g.Status = claim ? FnBankGuaranteeStatus.Claimed : action == "expire" ? FnBankGuaranteeStatus.Expired : FnBankGuaranteeStatus.Released;
                    g.ClosedDate = date;
                    break;
                }
                default:
                    return Bad<FnBankGuaranteeDto>("عمل نامعتبر است.");
            }
            FnVoucher voucher = null;
            if (lines.Count > 0)
            {
                int entityId = g.LegalEntityId;
                var v = await AddAutoVoucherAsync(entityId, date, description, "BankGuarantee", g.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
                if (v.Error != null) return Bad<FnBankGuaranteeDto>(v.Error);
                voucher = v.Voucher;
            }
            ev.Voucher = voucher;
            _uow.Set<FnBankGuaranteeEvent>().Add(ev);
            if (extraDeferral != null) { extraDeferral.GuaranteeId = g.Id; _uow.Set<FnDeferral>().Add(extraDeferral); }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GuaranteeDtosAsync(new List<FnBankGuarantee> { g }, ct))[0]);
        }

        public async Task<ResponseDto<FnGuaranteeLimitDto>> SetGuaranteeLimitAsync(FnGuaranteeLimitDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnGuaranteeLimitDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.BankName) == null || dto.Limit < 0) return Bad<FnGuaranteeLimitDto>("بانک و سقف را وارد کنید.");
            string bank = Clean(dto.BankName);
            FnBankGuaranteeLimit l = await _uow.Set<FnBankGuaranteeLimit>().FirstOrDefaultAsync(x => x.BankName == bank, ct);
            if (l == null) { l = new FnBankGuaranteeLimit { BankName = bank }; _uow.Set<FnBankGuaranteeLimit>().Add(l); }
            l.Limit = R(dto.Limit); l.Note = Clean(dto.Note); l.UpdatedByUserId = Me; l.UpdatedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnGuaranteeLimitDto { BankName = l.BankName, Limit = l.Limit, Note = l.Note });
        }

        public async Task<ResponseDto<FnGuaranteeExposureDto>> GuaranteeExposureAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnGuaranteeExposureDto>(NotAllowed);
            DateTime day = (asOf ?? DateTime.Today).Date;
            var active = await _uow.Set<FnBankGuarantee>().AsNoTracking().Where(g => g.Status == FnBankGuaranteeStatus.Active && g.IssueDate <= day).ToListAsync(ct);
            var limits = await _uow.Set<FnBankGuaranteeLimit>().AsNoTracking().ToListAsync(ct);
            var dto = new FnGuaranteeExposureDto { AsOf = day };
            foreach (var group in active.Where(g => g.Direction == FnBankGuaranteeDirection.Issued).GroupBy(g => g.BankName).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                decimal limit = limits.FirstOrDefault(l => l.BankName == group.Key)?.Limit ?? 0;
                bool hasLimit = limits.Any(l => l.BankName == group.Key);
                decimal exposure = group.Sum(g => g.Amount);
                dto.Banks.Add(new FnGuaranteeBankExposureDto { BankName = group.Key, ActiveCount = group.Count(), Exposure = exposure, Limit = limit, Available = hasLimit ? limit - exposure : (decimal?)null });
            }
            foreach (var l in limits.Where(l => !dto.Banks.Any(b => b.BankName == l.BankName)).OrderBy(l => l.BankName, StringComparer.Ordinal))
                dto.Banks.Add(new FnGuaranteeBankExposureDto { BankName = l.BankName, ActiveCount = 0, Exposure = 0, Limit = l.Limit, Available = l.Limit });
            dto.TotalIssued = active.Where(g => g.Direction == FnBankGuaranteeDirection.Issued).Sum(g => g.Amount);
            dto.TotalReceived = active.Where(g => g.Direction == FnBankGuaranteeDirection.Received).Sum(g => g.Amount);
            var soon = active.Where(g => (g.ExpiryDate.Date - day).TotalDays <= 30).OrderBy(g => g.ExpiryDate).ToList();
            dto.ExpiringWithin30Days = await GuaranteeDtosAsync(soon, ct);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
