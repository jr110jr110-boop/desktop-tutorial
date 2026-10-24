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
    // Finance release F3b: the social-insurance clearance letter of contractors and the release of their performance retention.
    public partial class GlService
    {
        private static FnClearanceDto ClearanceDto(FnInsuranceClearance c, string providerName, DateTime asOf) => new FnClearanceDto
        {
            Id = c.Id, Number = c.Number, ProviderId = c.ProviderId, ProviderName = providerName, ContractId = c.ContractId, LetterNo = c.LetterNo, IssueDate = c.IssueDate, ValidUntil = c.ValidUntil,
            DocumentId = c.DocumentId, HasDocument = c.DocumentId.HasValue && c.DocumentId.Value != Guid.Empty, IsRevoked = c.IsRevoked, IsValid = IsClearanceValid(c, asOf), Note = c.Note,
        };

        /// <summary>A clearance counts only with its scanned letter attached, not revoked and covering the date.</summary>
        private static bool IsClearanceValid(FnInsuranceClearance c, DateTime day) =>
            c.DocumentId.HasValue && c.DocumentId.Value != Guid.Empty && !c.IsRevoked && c.IssueDate.Date <= day.Date && c.ValidUntil.Date >= day.Date;

        public async Task<ResponseDto<List<FnClearanceDto>>> GetClearancesAsync(int? providerId, DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnClearanceDto>>(NotAllowed);
            DateTime day = (asOf ?? DateTime.Today).Date;
            IQueryable<FnInsuranceClearance> q = _uow.Set<FnInsuranceClearance>().AsNoTracking();
            if (providerId.HasValue) q = q.Where(c => c.ProviderId == providerId.Value);
            List<FnInsuranceClearance> rows = await q.OrderByDescending(c => c.ValidUntil).ThenByDescending(c => c.Id).Take(500).ToListAsync(ct);
            List<int> ids = rows.Select(r => r.ProviderId).Distinct().ToList();
            Dictionary<int, string> names = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            return ResponseDtoHelper.Ok(rows.Select(r => ClearanceDto(r, names.GetValueOrDefault(r.ProviderId), day)).ToList());
        }

        public async Task<ResponseDto<FnClearanceDto>> SaveClearanceAsync(FnSaveClearanceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnClearanceDto>(NotAllowed);
            if (dto == null || Clean(dto.LetterNo) == null) return Bad<FnClearanceDto>("شماره مفاصاحساب را وارد کنید.");
            if (dto.IssueDate == default || dto.ValidUntil.Date < dto.IssueDate.Date) return Bad<FnClearanceDto>("تاریخ اعتبار نمی‌تواند قبل از تاریخ صدور باشد.");
            var provider = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == dto.ProviderId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct);
            if (provider == null) return Bad<FnClearanceDto>("تامین‌کننده یا پیمانکار پیدا نشد.");
            FnInsuranceClearance c = null;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<FnInsuranceClearance>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (c == null) return ResponseDtoHelper.NotFound<FnClearanceDto>("مفاصاحساب پیدا نشد.");
                if (c.ProviderId != dto.ProviderId) return Bad<FnClearanceDto>("پیمانکار مفاصاحساب تغییر نمی‌کند.");
            }
            else
            {
                c = new FnInsuranceClearance { Number = await NextNumberAsync("ICL", FinanceNumbers.InsuranceClearance, dto.IssueDate, ct), ProviderId = dto.ProviderId, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
                _uow.Set<FnInsuranceClearance>().Add(c);
            }
            c.ContractId = dto.ContractId; c.LetterNo = Clean(dto.LetterNo); c.IssueDate = dto.IssueDate.Date; c.ValidUntil = dto.ValidUntil.Date;
            c.DocumentId = dto.DocumentId == Guid.Empty ? null : dto.DocumentId; c.IsRevoked = dto.IsRevoked; c.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ClearanceDto(c, provider.Name, DateTime.Today));
        }

        private async Task<FnRetentionStatusDto> RetentionStatusCoreAsync(int providerId, int? contractId, DateTime day, CancellationToken ct)
        {
            var provider = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == providerId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct);
            if (provider == null) return null;
            FnAccount retention = await RoleAccountAsync(AccountRoles.PerformanceRetentionPayable, "30206", ct);
            decimal held = 0;
            FnFiscalYear year = await LedgerYearAsync(day, ct);
            if (retention != null && year != null)
            {
                IQueryable<FnVoucherLine> lines = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.AccountId == retention.Id && l.Voucher.FiscalYearId == year.Id && l.Voucher.Status == FnVoucherStatus.Final && l.Voucher.Date <= day
                    && ((l.Detail1 != null && l.Detail1.Type == FnDetailType.Supplier && l.Detail1.LinkedEntityId == providerId) || (l.Detail2 != null && l.Detail2.Type == FnDetailType.Supplier && l.Detail2.LinkedEntityId == providerId)));
                if (contractId.HasValue) lines = lines.Where(l => l.ContractId == contractId.Value);
                decimal credit = await lines.SumAsync(l => (decimal?)l.Credit, ct) ?? 0;
                decimal debit = await lines.Where(l => l.Voucher.SourceType != "RetentionRelease").SumAsync(l => (decimal?)l.Debit, ct) ?? 0;
                held = credit - debit;
            }
            DateTime yearStart = year?.StartDate ?? DateTime.MinValue;
            IQueryable<FnRetentionRelease> rel = _uow.Set<FnRetentionRelease>().AsNoTracking().Where(r => r.ProviderId == providerId && r.Date <= day && r.Date >= yearStart);
            if (contractId.HasValue) rel = rel.Where(r => r.ContractId == contractId.Value);
            decimal released = await rel.SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
            var clearances = await _uow.Set<FnInsuranceClearance>().AsNoTracking().Where(c => c.ProviderId == providerId && (c.ContractId == null || c.ContractId == contractId)).OrderByDescending(c => c.ValidUntil).ThenByDescending(c => c.Id).ToListAsync(ct);
            FnInsuranceClearance valid = clearances.FirstOrDefault(c => IsClearanceValid(c, day));
            decimal releasable = Math.Max(0, held - released);
            var dto = new FnRetentionStatusDto
            {
                ProviderId = providerId, ProviderName = provider.Name, ContractId = contractId, AsOf = day, Held = held, Released = released, Releasable = releasable,
                ValidClearanceId = valid?.Id, CanRelease = releasable > 0 && valid != null,
            };
            dto.Message = releasable <= 0 ? "کسور حسن انجام کاری برای آزادسازی وجود ندارد."
                : valid == null ? (clearances.Count == 0 ? "مفاصاحساب بیمه این پیمانکار ثبت نشده است؛ آزادسازی کسور ممکن نیست."
                    : "مفاصاحساب بیمه معتبر (با فایل پیوست، ابطال‌نشده و در مهلت اعتبار) وجود ندارد؛ آزادسازی کسور ممکن نیست.")
                : "آزادسازی ممکن است.";
            return dto;
        }

        public async Task<ResponseDto<FnRetentionStatusDto>> RetentionStatusAsync(int providerId, int? contractId, DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnRetentionStatusDto>(NotAllowed);
            FnRetentionStatusDto dto = await RetentionStatusCoreAsync(providerId, contractId, (asOf ?? DateTime.Today).Date, ct);
            return dto == null ? ResponseDtoHelper.NotFound<FnRetentionStatusDto>("تامین‌کننده پیدا نشد.") : ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnRetentionReleaseDto>> ReleaseRetentionAsync(FnRetentionReleaseRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnRetentionReleaseDto>(NotAllowed);
            if (dto == null || dto.ProviderId <= 0) return Bad<FnRetentionReleaseDto>("پیمانکار را مشخص کنید.");
            if (dto.Amount < 0) return Bad<FnRetentionReleaseDto>("مبلغ نمی‌تواند منفی باشد.");
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            FnRetentionStatusDto status = await RetentionStatusCoreAsync(dto.ProviderId, dto.ContractId, date, ct);
            if (status == null) return ResponseDtoHelper.NotFound<FnRetentionReleaseDto>("تامین‌کننده پیدا نشد.");
            if (status.Releasable <= 0) return Bad<FnRetentionReleaseDto>(status.Message);
            if (!status.ValidClearanceId.HasValue) return Bad<FnRetentionReleaseDto>(status.Message);
            decimal amount = dto.Amount == 0 ? status.Releasable : R(dto.Amount);
            if (amount > status.Releasable) return Bad<FnRetentionReleaseDto>($"مبلغ آزادسازی از کسور قابل آزادسازی ({status.Releasable:0}) بیشتر است.");
            FnAccount retention = await RoleAccountAsync(AccountRoles.PerformanceRetentionPayable, "30206", ct), payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct);
            if (retention == null || payable == null) return Bad<FnRetentionReleaseDto>("حساب کسور حسن انجام کار یا حساب پرداختنی پیدا نشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var details = new DetailResolver(_uow);
            FnDetailAccount supplier = await details.GetAsync(FnDetailType.Supplier, dto.ProviderId, ct);
            string number = await NextNumberAsync("RRL", FinanceNumbers.RetentionRelease, date, ct);
            string text = $"آزادسازی کسور حسن انجام کار {status.ProviderName} ({number})";
            var lines = new List<AutoLine>
            {
                new AutoLine { Account = retention, Detail1 = supplier, Debit = amount, Description = text, ContractId = dto.ContractId },
                new AutoLine { Account = payable, Detail1 = supplier, Credit = amount, Description = text, ContractId = dto.ContractId },
            };
            var v = await AddAutoVoucherAsync(entityId, date, text, "RetentionRelease", number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnRetentionReleaseDto>(v.Error);
            var r = new FnRetentionRelease
            {
                Number = number, ProviderId = dto.ProviderId, ContractId = dto.ContractId, Date = date, Amount = amount, ClearanceId = status.ValidClearanceId.Value, Voucher = v.Voucher,
                Note = Clean(dto.Note), CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnRetentionRelease>().Add(r);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnRetentionReleaseDto { Id = r.Id, Number = r.Number, ProviderId = r.ProviderId, ContractId = r.ContractId, Date = r.Date, Amount = r.Amount, ClearanceId = r.ClearanceId, VoucherId = r.VoucherId, Note = r.Note });
        }
    }
}
