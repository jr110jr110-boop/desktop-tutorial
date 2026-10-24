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
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IFxService
    {
        Task<ResponseDto<List<CurrencyDto>>> GetCurrenciesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<CurrencyDto>> SaveCurrencyAsync(CurrencyDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ExchangeRateDto>>> GetRatesAsync(FxRateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ExchangeRateDto>> SaveRateAsync(SaveExchangeRateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FxRevaluationDto>> RevaluationAsync(FxRevaluationRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FxRevaluationDto>>> GetRevaluationsAsync(int? periodId, CancellationToken cancellationToken);
        Task<ResponseDto<FxSettlementDto>> SettleAsync(FxSettlementRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FxExposureDto>> ExposureAsync(DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<List<LetterOfCreditDto>>> GetLettersOfCreditAsync(CancellationToken cancellationToken);
        Task<ResponseDto<LetterOfCreditDto>> GetLetterOfCreditAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<LetterOfCreditDto>> SaveLetterOfCreditAsync(SaveLetterOfCreditDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LetterOfCreditDto>> LetterOfCreditActionAsync(LcActionDto dto, CancellationToken cancellationToken);
    }

    // Foreign currency (F3 section 3): currencies and rates (inputs), period-end revaluation with automatic reversal, realised differences,
    // letters of credit and the exposure report. Rates are never seeded.
    public partial class GlService : IFxService
    {
        // ------------------------------------------------------------------------------------------ currencies and rates

        public async Task<ResponseDto<List<CurrencyDto>>> GetCurrenciesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<CurrencyDto>>(NotAllowed);
            var list = await _uow.Set<FnCurrency>().AsNoTracking().OrderBy(c => c.Code).Select(c => new CurrencyDto { Code = c.Code, Title = c.Title, Decimals = c.Decimals, IsBase = c.IsBase }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<CurrencyDto>> SaveCurrencyAsync(CurrencyDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<CurrencyDto>(NotAllowed + " (مدیر مالی)");
            if (!FinanceFeatures.ForeignCurrency && !FinanceFeatures.IsRial(dto?.Code)) return Bad<CurrencyDto>(FinanceFeatures.OnlyRial);
            string code = Clean(dto?.Code)?.ToUpperInvariant();
            if (code == null || code.Length > 10 || Clean(dto.Title) == null) return Bad<CurrencyDto>("کد و عنوان ارز را وارد کنید.");
            if (dto.Decimals < 0 || dto.Decimals > 6) return Bad<CurrencyDto>("تعداد اعشار باید بین ۰ و ۶ باشد.");
            FnCurrency c = await _uow.Set<FnCurrency>().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (c == null) { c = new FnCurrency { Code = code }; _uow.Set<FnCurrency>().Add(c); }
            if (dto.IsBase && await _uow.Set<FnCurrency>().AnyAsync(x => x.IsBase && x.Code != code, ct)) return Bad<CurrencyDto>("ارز پایه فقط یکی است.");
            c.Title = Clean(dto.Title); c.Decimals = dto.Decimals; c.IsBase = dto.IsBase;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new CurrencyDto { Code = c.Code, Title = c.Title, Decimals = c.Decimals, IsBase = c.IsBase });
        }

        private static ExchangeRateDto ToDto(FnExchangeRate r) => new ExchangeRateDto
        {
            Id = r.Id, CurrencyCode = r.CurrencyCode, Date = r.Date, Buy = r.Buy, Sell = r.Sell, Average = r.Average, Source = r.Source, RateType = r.RateType, Note = r.Note,
        };

        public async Task<ResponseDto<PagedList<ExchangeRateDto>>> GetRatesAsync(FxRateQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<ExchangeRateDto>>(NotAllowed);
            query ??= new FxRateQuery();
            IQueryable<FnExchangeRate> q = _uow.Set<FnExchangeRate>().AsNoTracking();
            string code = Clean(query.CurrencyCode)?.ToUpperInvariant();
            if (code != null) q = q.Where(r => r.CurrencyCode == code);
            if (query.RateType.HasValue) q = q.Where(r => r.RateType == query.RateType.Value);
            if (query.From.HasValue) q = q.Where(r => r.Date >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(r => r.Date <= query.To.Value.Date);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnExchangeRate> rows = await q.OrderByDescending(r => r.Date).ThenBy(r => r.CurrencyCode).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(ToDto).ToList(), total, number, size));
        }

        public async Task<ResponseDto<ExchangeRateDto>> SaveRateAsync(SaveExchangeRateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<ExchangeRateDto>(NotAllowed);
            if (!FinanceFeatures.ForeignCurrency) return Bad<ExchangeRateDto>(FinanceFeatures.OnlyRial);
            string code = Clean(dto?.CurrencyCode)?.ToUpperInvariant();
            if (code == null || dto.Date == default) return Bad<ExchangeRateDto>("ارز و تاریخ نرخ را وارد کنید.");
            if (await _uow.Set<FnCurrency>().AnyAsync(c => c.IsBase && c.Code == code, ct)) return Bad<ExchangeRateDto>("برای ارز پایه نرخ ثبت نمی‌شود.");
            if (await _uow.Set<FnCurrency>().AnyAsync(ct) && !await _uow.Set<FnCurrency>().AnyAsync(c => c.Code == code, ct)) return Bad<ExchangeRateDto>("این ارز تعریف نشده است: " + code);
            if ((dto.Buy.HasValue && dto.Buy.Value <= 0) || (dto.Sell.HasValue && dto.Sell.Value <= 0) || (dto.Average.HasValue && dto.Average.Value <= 0)) return Bad<ExchangeRateDto>("نرخ باید بیشتر از صفر باشد.");
            decimal? average = dto.Average ?? (dto.Buy.HasValue && dto.Sell.HasValue ? Math.Round((dto.Buy.Value + dto.Sell.Value) / 2m, 6, MidpointRounding.AwayFromZero) : (decimal?)null);
            if (!average.HasValue) return Bad<ExchangeRateDto>("نرخ میانگین یا هر دو نرخ خرید و فروش را وارد کنید.");
            DateTime date = dto.Date.Date;
            FnExchangeRate r = await _uow.Set<FnExchangeRate>().FirstOrDefaultAsync(x => x.CurrencyCode == code && x.Date == date && x.RateType == dto.RateType, ct);
            if (r == null)
            {
                r = new FnExchangeRate { CurrencyCode = code, Date = date, RateType = dto.RateType, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnExchangeRate>().Add(r);
            }
            r.Buy = dto.Buy; r.Sell = dto.Sell; r.Average = average.Value; r.Source = dto.Source; r.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(r));
        }

        /// <summary>The latest rate on or before the date; null when none was entered.</summary>
        private async Task<decimal?> RateOnAsync(string currency, DateTime date, FnRateType type, CancellationToken ct)
        {
            DateTime d = date.Date;
            return await _uow.Set<FnExchangeRate>().AsNoTracking().Where(r => r.CurrencyCode == currency && r.RateType == type && r.Date <= d)
                .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Average).FirstOrDefaultAsync(ct);
        }

        private async Task<FnRateType> RateTypeSettingAsync(CancellationToken ct)
        {
            string v = await SettingAsync(FinanceSeed.SettingFxRateType, "1", ct);
            return int.TryParse(v, out int t) && Enum.IsDefined(typeof(FnRateType), t) ? (FnRateType)t : FnRateType.Official;
        }

        // ------------------------------------------------------------------------------------------ period-end revaluation

        private sealed class RevalItem
        {
            public int AccountId; public int? Detail1Id; public string Currency; public decimal CurrencyBalance; public decimal Book; public decimal Rate; public decimal NewBase; public decimal Diff;
        }

        public async Task<ResponseDto<FxRevaluationDto>> RevaluationAsync(FxRevaluationRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FxRevaluationDto>(NotAllowed);
            if (!FinanceFeatures.ForeignCurrency) return Bad<FxRevaluationDto>(FinanceFeatures.OnlyRial);
            if (dto == null) return Bad<FxRevaluationDto>("اطلاعات ارسال‌شده کامل نیست.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == dto.PeriodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<FxRevaluationDto>("دوره پیدا نشد.");
            int entityId = period.FiscalYear.LegalEntityId;
            FnLegalEntity entity = await _uow.Set<FnLegalEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == entityId, ct);
            string baseCurrency = entity?.BaseCurrency ?? "IRR";
            FnRateType rateType = dto.RateType ?? await RateTypeSettingAsync(ct);
            bool reverse = dto.Reverse ?? await SettingAsync(FinanceSeed.SettingFxAutoReverse, "1", ct) == "1";
            DateTime end = period.EndDate.Date;

            IQueryable<FnVoucherLine> baseQ = _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Account.IsMonetaryForeign && l.Voucher.FiscalYear.LegalEntityId == entityId && l.Voucher.Date <= end && l.Voucher.Status != FnVoucherStatus.Draft);
            var book = await baseQ.GroupBy(l => new { l.AccountId, l.Detail1Id }).Select(g => new { g.Key.AccountId, g.Key.Detail1Id, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
            var tagged = await baseQ.Where(l => l.CurrencyCode != null && l.CurrencyAmount != null)
                .GroupBy(l => new { l.AccountId, l.Detail1Id, l.CurrencyCode })
                .Select(g => new { g.Key.AccountId, g.Key.Detail1Id, g.Key.CurrencyCode, Amount = g.Sum(x => x.Debit > 0 ? x.CurrencyAmount.Value : -x.CurrencyAmount.Value) }).ToListAsync(ct);

            var errors = new List<string>();
            var items = new List<RevalItem>();
            foreach (var b in book)
            {
                var cur = tagged.Where(t => t.AccountId == b.AccountId && t.Detail1Id == b.Detail1Id && t.CurrencyCode != baseCurrency).ToList();
                if (cur.Count == 0) continue;
                if (cur.Count > 1) { errors.Add($"حساب شماره {b.AccountId} در یک تفصیلی بیش از یک ارز دارد؛ باید تفکیک شود."); continue; }
                string currency = cur[0].CurrencyCode;
                decimal cb = cur[0].Amount;
                decimal rate = 0;
                if (cb != 0)
                {
                    decimal? found = await RateOnAsync(currency, end, rateType, ct);
                    if (!found.HasValue) { errors.Add($"نرخ {currency} ({rateType}) تا تاریخ پایان دوره ثبت نشده است."); continue; }
                    rate = found.Value;
                }
                decimal newBase = R(cb * rate);
                decimal diff = newBase - b.Net;
                items.Add(new RevalItem { AccountId = b.AccountId, Detail1Id = b.Detail1Id, Currency = currency, CurrencyBalance = cb, Book = b.Net, Rate = rate, NewBase = newBase, Diff = diff });
            }
            if (errors.Count > 0) return Bad<FxRevaluationDto>(string.Join(" | ", errors.Distinct()));

            Dictionary<int, FnAccount> accounts = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
            List<int> detailIds = items.Where(i => i.Detail1Id.HasValue).Select(i => i.Detail1Id.Value).Distinct().ToList();
            Dictionary<int, string> detailCodes = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => detailIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct);
            var result = new FxRevaluationDto { PeriodId = period.Id, LegalEntityId = entityId, DryRun = dto.DryRun };
            foreach (RevalItem i in items.OrderBy(x => accounts[x.AccountId].Code, StringComparer.Ordinal).ThenBy(x => x.Detail1Id))
                result.Lines.Add(new FxRevaluationLineDto
                {
                    AccountCode = accounts[i.AccountId].Code, DetailCode = i.Detail1Id.HasValue ? detailCodes[i.Detail1Id.Value] : null, CurrencyCode = i.Currency,
                    CurrencyBalance = i.CurrencyBalance, BookBalance = i.Book, Rate = i.Rate, NewBalance = i.NewBase, Difference = i.Diff,
                });
            result.TotalGain = items.Where(i => i.Diff > 0).Sum(i => i.Diff);
            result.TotalLoss = -items.Where(i => i.Diff < 0).Sum(i => i.Diff);
            List<RevalItem> moving = items.Where(i => i.Diff != 0).ToList();
            if (dto.DryRun || moving.Count == 0) return ResponseDtoHelper.Ok(result);

            FnAccount gain = await RoleAccountAsync(AccountRoles.FxGain, "60303", ct), loss = await RoleAccountAsync(AccountRoles.FxLoss, "80302", ct);
            if (gain == null || loss == null) return Bad<FxRevaluationDto>("حساب سود و زیان تسعیر ارز در کدینگ نیست.");
            string number = await NextNumberAsync("FXR", FinanceNumbers.FxRevaluation, end, ct);
            string text = $"تسعیر ارز پایان دوره {period.Title} ({number})";
            decimal gainSum = moving.Where(i => i.Diff > 0).Sum(i => i.Diff), lossSum = -moving.Where(i => i.Diff < 0).Sum(i => i.Diff);

            // the run record is saved FIRST with the next sequence number of the period: the unique index (entity, period, sequence) stops two concurrent runs from posting the same differences twice
            int sequence = await _uow.Set<FnFxRevaluation>().CountAsync(r => r.LegalEntityId == entityId && r.PeriodId == period.Id, ct) + 1;
            var run = new FnFxRevaluation
            {
                Number = number, LegalEntityId = entityId, PeriodId = period.Id, Sequence = sequence, RateType = rateType, Reverse = reverse, TotalGain = gainSum, TotalLoss = lossSum,
                VoucherId = 0, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnFxRevaluationLine>(),
            };
            foreach (RevalItem i in moving)
                run.Lines.Add(new FnFxRevaluationLine { AccountId = i.AccountId, Detail1Id = i.Detail1Id, CurrencyCode = i.Currency, CurrencyBalance = i.CurrencyBalance, BookBalance = i.Book, Rate = i.Rate, NewBalance = i.NewBase, Difference = i.Diff });
            _uow.Set<FnFxRevaluation>().Add(run);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<FxRevaluationDto>("تسعیر ارز این دوره همزمان توسط کاربر دیگری انجام شد؛ دوباره بررسی کنید."); }

            // account entities must be tracked by this context
            List<int> movingIds = moving.Select(m => m.AccountId).ToList();
            Dictionary<int, FnAccount> tracked = await _uow.Set<FnAccount>().Where(a => movingIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            var mainLines = new List<AutoLine>();
            var reverseLines = new List<AutoLine>();
            foreach (RevalItem i in moving)
            {
                mainLines.Add(new AutoLine { Account = tracked[i.AccountId], Detail1Id = i.Detail1Id, Debit = i.Diff > 0 ? i.Diff : 0, Credit = i.Diff < 0 ? -i.Diff : 0, Description = text });
                reverseLines.Add(new AutoLine { Account = tracked[i.AccountId], Detail1Id = i.Detail1Id, Debit = i.Diff < 0 ? -i.Diff : 0, Credit = i.Diff > 0 ? i.Diff : 0, Description = "برگشت " + text });
            }
            mainLines.Add(new AutoLine { Account = gain, Credit = gainSum, Description = text });
            mainLines.Add(new AutoLine { Account = loss, Debit = lossSum, Description = text });
            reverseLines.Add(new AutoLine { Account = gain, Debit = gainSum, Description = "برگشت " + text });
            reverseLines.Add(new AutoLine { Account = loss, Credit = lossSum, Description = "برگشت " + text });

            var main = await AddAutoVoucherAsync(entityId, end, text, "FxRevaluation", number, mainLines, FnVoucherType.Auto, null, ct);
            if (main.Error != null) { await _uow.Set<FnFxRevaluation>().Where(x => x.Id == run.Id).ExecuteDeleteAsync(ct); return Bad<FxRevaluationDto>(main.Error); }
            string warning = null;
            FnVoucher reversal = null;
            if (reverse)
            {
                var rev = await AddAutoVoucherAsync(entityId, end.AddDays(1), "برگشت " + text, "FxRevaluation", number, reverseLines, FnVoucherType.Reversal, main.Voucher, ct);
                if (rev.Error != null) warning = "برگشت خودکار ساخته نشد: " + rev.Error;
                else reversal = rev.Voucher;
            }
            await _uow.SaveChangesAsync(ct);
            run.VoucherId = main.Voucher.Id; run.ReversalVoucherId = reversal?.Id; run.Warning = warning;
            await _uow.SaveChangesAsync(ct);
            result.Id = run.Id; result.Number = number; result.VoucherId = run.VoucherId; result.ReversalVoucherId = run.ReversalVoucherId; result.Warning = warning; result.CreatedAt = run.CreatedAt;
            result.TotalGain = gainSum; result.TotalLoss = lossSum;
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<List<FxRevaluationDto>>> GetRevaluationsAsync(int? periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FxRevaluationDto>>(NotAllowed);
            IQueryable<FnFxRevaluation> q = _uow.Set<FnFxRevaluation>().AsNoTracking();
            if (periodId.HasValue) q = q.Where(r => r.PeriodId == periodId.Value);
            var rows = await q.OrderByDescending(r => r.Id).Take(100).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new FxRevaluationDto
            {
                Id = r.Id, Number = r.Number, PeriodId = r.PeriodId, LegalEntityId = r.LegalEntityId, TotalGain = r.TotalGain, TotalLoss = r.TotalLoss, VoucherId = r.VoucherId,
                ReversalVoucherId = r.ReversalVoucherId, Warning = r.Warning, CreatedAt = r.CreatedAt,
            }).ToList());
        }

        // ------------------------------------------------------------------------------------------ realised difference at settlement

        public async Task<ResponseDto<FxSettlementDto>> SettleAsync(FxSettlementRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FxSettlementDto>(NotAllowed);
            if (!FinanceFeatures.ForeignCurrency) return Bad<FxSettlementDto>(FinanceFeatures.OnlyRial);
            if (dto == null || dto.Date == default) return Bad<FxSettlementDto>("اطلاعات ارسال‌شده کامل نیست.");
            string direction = Clean(dto.Direction);
            if (direction != "Receivable" && direction != "Payable") return Bad<FxSettlementDto>("نوع تسویه باید Receivable یا Payable باشد.");
            string currency = Clean(dto.CurrencyCode)?.ToUpperInvariant();
            if (currency == null) return Bad<FxSettlementDto>("کد ارز را وارد کنید.");
            if (dto.CurrencyAmount <= 0 || dto.BookRate <= 0 || dto.SettleRate <= 0) return Bad<FxSettlementDto>("مبلغ ارزی و نرخ‌ها باید بیشتر از صفر باشند.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, dto.LegalEntityId, Me, ct);
            FnAccount account = await AccountByCodeAsync(dto.AccountCode, ct), counter = await AccountByCodeAsync(dto.CounterAccountCode, ct);
            if (account == null || counter == null || account.Level != FnAccountLevel.Subsidiary || counter.Level != FnAccountLevel.Subsidiary) return Bad<FxSettlementDto>("حساب‌ها باید معین و موجود باشند.");
            if (!account.IsMonetaryForeign) return Bad<FxSettlementDto>($"حساب {account.Code} حساب ارزی (قلم پولی) نیست.");
            FnAccount gain = await RoleAccountAsync(AccountRoles.FxGain, "60303", ct), loss = await RoleAccountAsync(AccountRoles.FxLoss, "80302", ct);
            if (gain == null || loss == null) return Bad<FxSettlementDto>("حساب سود و زیان تسعیر ارز در کدینگ نیست.");
            FnDetailAccount d1 = null, d2 = null;
            if (Clean(dto.DetailCode) != null)
            {
                d1 = await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(d => d.Code == dto.DetailCode.Trim() && d.IsActive, ct);
                if (d1 == null) return Bad<FxSettlementDto>("تفصیلی پیدا نشد.");
            }
            if (Clean(dto.CounterDetailCode) != null)
            {
                d2 = await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(d => d.Code == dto.CounterDetailCode.Trim() && d.IsActive, ct);
                if (d2 == null) return Bad<FxSettlementDto>("تفصیلی حساب مقابل پیدا نشد.");
            }
            decimal bookBase = R(dto.CurrencyAmount * dto.BookRate), settled = R(dto.CurrencyAmount * dto.SettleRate);
            decimal diff = direction == "Receivable" ? settled - bookBase : bookBase - settled; // positive = gain
            string number = await NextNumberAsync("FXS", FinanceNumbers.FxSettlement, dto.Date, ct);
            string text = Clean(dto.Description) ?? $"تسویه {(direction == "Receivable" ? "دریافتنی" : "پرداختنی")} ارزی ({number})";
            var lines = new List<AutoLine>();
            if (direction == "Receivable")
            {
                lines.Add(new AutoLine { Account = counter, Detail1 = null, Detail1Id = d2?.Id, Debit = settled, Description = text });
                lines.Add(new AutoLine { Account = account, Detail1Id = d1?.Id, Credit = bookBase, Description = text, CurrencyCode = currency, CurrencyAmount = dto.CurrencyAmount, Rate = dto.BookRate });
            }
            else
            {
                lines.Add(new AutoLine { Account = account, Detail1Id = d1?.Id, Debit = bookBase, Description = text, CurrencyCode = currency, CurrencyAmount = dto.CurrencyAmount, Rate = dto.BookRate });
                lines.Add(new AutoLine { Account = counter, Detail1Id = d2?.Id, Credit = settled, Description = text });
            }
            if (diff > 0) lines.Add(new AutoLine { Account = gain, Credit = diff, Description = text });
            else if (diff < 0) lines.Add(new AutoLine { Account = loss, Debit = -diff, Description = text });
            var v = await AddAutoVoucherAsync(entityId, dto.Date.Date, text, "FxSettlement", number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FxSettlementDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            var rec = new FnFxSettlement
            {
                Number = number, LegalEntityId = entityId, Date = dto.Date.Date, Direction = direction, CurrencyCode = currency, CurrencyAmount = dto.CurrencyAmount, BookRate = dto.BookRate, SettleRate = dto.SettleRate,
                BookBase = bookBase, SettledBase = settled, Difference = diff, VoucherId = v.Voucher.Id, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnFxSettlement>().Add(rec);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FxSettlementDto
            {
                Id = rec.Id, Number = number, Date = rec.Date, Direction = direction, CurrencyCode = currency, CurrencyAmount = rec.CurrencyAmount, BookRate = rec.BookRate, SettleRate = rec.SettleRate,
                BookBase = bookBase, SettledBase = settled, Difference = diff, VoucherId = rec.VoucherId,
            });
        }

        // ------------------------------------------------------------------------------------------ exposure

        public async Task<ResponseDto<FxExposureDto>> ExposureAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FxExposureDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            DateTime date = (asOf ?? DateTime.Today).Date;
            IQueryable<FnVoucherLine> baseQ = _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Account.IsMonetaryForeign && l.Voucher.FiscalYear.LegalEntityId == entityId && l.Voucher.Date <= date && l.Voucher.Status != FnVoucherStatus.Draft);
            var book = await baseQ.GroupBy(l => new { l.AccountId, l.Detail1Id }).Select(g => new { g.Key.AccountId, g.Key.Detail1Id, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
            var tagged = await baseQ.Where(l => l.CurrencyCode != null && l.CurrencyAmount != null).GroupBy(l => new { l.AccountId, l.Detail1Id, l.CurrencyCode })
                .Select(g => new { g.Key.AccountId, g.Key.Detail1Id, g.Key.CurrencyCode, Amount = g.Sum(x => x.Debit > 0 ? x.CurrencyAmount.Value : -x.CurrencyAmount.Value) }).ToListAsync(ct);
            var rows = new Dictionary<string, FxExposureRowDto>();
            FxExposureRowDto Row(string c)
            {
                if (!rows.TryGetValue(c, out FxExposureRowDto r)) { r = new FxExposureRowDto { CurrencyCode = c }; rows[c] = r; }
                return r;
            }
            foreach (var t in tagged)
            {
                decimal net = book.Where(b => b.AccountId == t.AccountId && b.Detail1Id == t.Detail1Id).Sum(b => b.Net);
                FxExposureRowDto r = Row(t.CurrencyCode);
                if (t.Amount > 0) { r.Receivables += t.Amount; r.BaseReceivables += net; }
                else if (t.Amount < 0) { r.Payables += -t.Amount; r.BasePayables += -net; }
            }
            var lcs = await _uow.Set<FnLetterOfCredit>().AsNoTracking().Where(l => l.Status == FnLcStatus.Opened || l.Status == FnLcStatus.DocumentsReceived)
                .GroupBy(l => l.CurrencyCode).Select(g => new { Code = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
            foreach (var l in lcs) Row(l.Code).OpenLetterOfCredit = l.Amount;
            foreach (FxExposureRowDto r in rows.Values) { r.Net = r.Receivables - r.Payables; r.NetWithLetterOfCredit = r.Net - r.OpenLetterOfCredit; }
            return ResponseDtoHelper.Ok(new FxExposureDto { AsOf = date, Rows = rows.Values.OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ToList() });
        }

        // ------------------------------------------------------------------------------------------ letters of credit

        private async Task<LetterOfCreditDto> LcDtoAsync(FnLetterOfCredit l, CancellationToken ct)
        {
            List<FnLcPosting> postings = await _uow.Set<FnLcPosting>().AsNoTracking().Where(p => p.LcId == l.Id).OrderBy(p => p.Id).ToListAsync(ct);
            return new LetterOfCreditDto
            {
                Id = l.Id, Number = l.Number, LcNumber = l.LcNumber, ProviderId = l.ProviderId, BankAccountId = l.BankAccountId, CurrencyCode = l.CurrencyCode, Amount = l.Amount,
                OpenDate = l.OpenDate, ExpiryDate = l.ExpiryDate, MarginPercent = l.MarginPercent, MarginPaid = l.MarginPaid, FeesPaid = l.FeesPaid, Status = l.Status,
                PurchaseOrderId = l.PurchaseOrderId, ForeignInvoiceRef = l.ForeignInvoiceRef, SettlementRate = l.SettlementRate, SettledBase = l.SettledBase, PaidDate = l.PaidDate, Note = l.Note,
                Postings = postings.Select(p => new LcPostingDto { Kind = p.Kind, Date = p.Date, Amount = p.Amount, VoucherId = p.VoucherId, Note = p.Note }).ToList(),
            };
        }

        public async Task<ResponseDto<List<LetterOfCreditDto>>> GetLettersOfCreditAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<LetterOfCreditDto>>(NotAllowed);
            List<FnLetterOfCredit> list = await _uow.Set<FnLetterOfCredit>().AsNoTracking().OrderByDescending(l => l.Id).Take(200).ToListAsync(ct);
            var dtos = new List<LetterOfCreditDto>();
            foreach (FnLetterOfCredit l in list) dtos.Add(await LcDtoAsync(l, ct));
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<LetterOfCreditDto>> GetLetterOfCreditAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<LetterOfCreditDto>(NotAllowed);
            FnLetterOfCredit l = await _uow.Set<FnLetterOfCredit>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return l == null ? ResponseDtoHelper.NotFound<LetterOfCreditDto>("اعتبار اسنادی پیدا نشد.") : ResponseDtoHelper.Ok(await LcDtoAsync(l, ct));
        }

        public async Task<ResponseDto<LetterOfCreditDto>> SaveLetterOfCreditAsync(SaveLetterOfCreditDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<LetterOfCreditDto>(NotAllowed);
            if (dto == null) return Bad<LetterOfCreditDto>("اطلاعات ارسال‌شده کامل نیست.");
            string currency = Clean(dto.CurrencyCode)?.ToUpperInvariant();
            if (currency == null) return Bad<LetterOfCreditDto>("کد ارز را وارد کنید.");
            if (!FinanceFeatures.ForeignCurrency && !FinanceFeatures.IsRial(currency)) return Bad<LetterOfCreditDto>(FinanceFeatures.OnlyRial);
            if (dto.Amount <= 0) return Bad<LetterOfCreditDto>("مبلغ اعتبار باید بیشتر از صفر باشد.");
            if (dto.MarginPercent < 0 || dto.MarginPercent > 100) return Bad<LetterOfCreditDto>("درصد سپرده باید بین ۰ و ۱۰۰ باشد.");
            if (dto.OpenDate == default || dto.ExpiryDate.Date <= dto.OpenDate.Date) return Bad<LetterOfCreditDto>("تاریخ سررسید باید بعد از تاریخ گشایش باشد.");
            if (!await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AnyAsync(p => p.Id == dto.ProviderId, ct)) return Bad<LetterOfCreditDto>("تامین‌کننده پیدا نشد.");
            if (!await _uow.Set<FnBankAccount>().AnyAsync(b => b.Id == dto.BankAccountId && b.IsActive, ct)) return Bad<LetterOfCreditDto>("حساب بانکی پیدا نشد یا غیرفعال است.");
            if (await _uow.Set<FnCurrency>().AnyAsync(ct) && !await _uow.Set<FnCurrency>().AnyAsync(c => c.Code == currency, ct)) return Bad<LetterOfCreditDto>("این ارز تعریف نشده است: " + currency);
            FnLetterOfCredit l;
            if (dto.Id.HasValue)
            {
                l = await _uow.Set<FnLetterOfCredit>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (l == null) return ResponseDtoHelper.NotFound<LetterOfCreditDto>("اعتبار اسنادی پیدا نشد.");
                if (l.Status != FnLcStatus.Opened) return Bad<LetterOfCreditDto>("فقط اعتبار در وضعیت گشایش‌شده ویرایش می‌شود.");
                if ((l.MarginPaid > 0 || l.FeesPaid > 0) && (l.CurrencyCode != currency || l.Amount != dto.Amount)) return Bad<LetterOfCreditDto>("پس از ثبت سپرده یا کارمزد، ارز و مبلغ تغییر نمی‌کند.");
            }
            else
            {
                l = new FnLetterOfCredit { Status = FnLcStatus.Opened, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me, Number = await NextNumberAsync("LC", FinanceNumbers.LetterOfCredit, dto.OpenDate, ct) };
                _uow.Set<FnLetterOfCredit>().Add(l);
            }
            l.LcNumber = Clean(dto.LcNumber); l.ProviderId = dto.ProviderId; l.BankAccountId = dto.BankAccountId; l.CurrencyCode = currency; l.Amount = dto.Amount;
            l.OpenDate = dto.OpenDate.Date; l.ExpiryDate = dto.ExpiryDate.Date; l.MarginPercent = dto.MarginPercent; l.PurchaseOrderId = dto.PurchaseOrderId; l.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await LcDtoAsync(l, ct));
        }

        public async Task<ResponseDto<LetterOfCreditDto>> LetterOfCreditActionAsync(LcActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<LetterOfCreditDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<LetterOfCreditDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnLetterOfCredit l = await _uow.Set<FnLetterOfCredit>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (l == null) return ResponseDtoHelper.NotFound<LetterOfCreditDto>("اعتبار اسنادی پیدا نشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            string action = dto.Action.Trim().ToLowerInvariant();
            bool live = l.Status == FnLcStatus.Opened || l.Status == FnLcStatus.DocumentsReceived;
            var details = new DetailResolver(_uow);
            var bank = await BankLedgerAsync(l.BankAccountId, details, ct);
            if (bank.Bank == null) return Bad<LetterOfCreditDto>("حساب بانکی پیدا نشد.");
            int entityId = bank.Bank.LegalEntityId <= 0 ? FinanceSeed.MainEntityId : bank.Bank.LegalEntityId;
            FnAccount deposit = await AccountByCodeAsync("10402", ct);
            string text;

            switch (action)
            {
                case "margin":
                case "fee":
                {
                    if (!live) return Bad<LetterOfCreditDto>("اعتبار بسته شده است.");
                    decimal amount = R(dto.Amount);
                    if (amount <= 0) return Bad<LetterOfCreditDto>("مبلغ باید بیشتر از صفر باشد.");
                    FnAccount debitAccount = action == "margin" ? deposit : await RoleAccountAsync(AccountRoles.BankFees, "80201", ct);
                    if (debitAccount == null) return Bad<LetterOfCreditDto>("حساب سپرده یا کارمزد بانکی در کدینگ نیست.");
                    text = (action == "margin" ? "سپرده اعتبار اسنادی " : "کارمزد اعتبار اسنادی ") + l.Number;
                    var lines = new List<AutoLine>
                    {
                        new AutoLine { Account = debitAccount, Debit = amount, Description = text },
                        new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Credit = amount, Description = text },
                    };
                    var v = await AddAutoVoucherAsync(entityId, date, text, "LetterOfCredit", l.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
                    if (v.Error != null) return Bad<LetterOfCreditDto>(v.Error);
                    await _uow.SaveChangesAsync(ct);
                    if (action == "margin") l.MarginPaid += amount; else l.FeesPaid += amount;
                    _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = action == "margin" ? FnLcPostingKind.Margin : FnLcPostingKind.Fee, Date = date, Amount = amount, VoucherId = v.Voucher.Id, Note = Clean(dto.Note), CreatedByUserId = Me });
                    break;
                }
                case "documents":
                    if (l.Status != FnLcStatus.Opened) return Bad<LetterOfCreditDto>("اسناد فقط برای اعتبار گشایش‌شده دریافت می‌شود.");
                    if (Clean(dto.ForeignInvoiceRef) == null) return Bad<LetterOfCreditDto>("شماره فاکتور خارجی را وارد کنید.");
                    l.ForeignInvoiceRef = Clean(dto.ForeignInvoiceRef);
                    l.Status = FnLcStatus.DocumentsReceived;
                    break;
                case "pay":
                {
                    if (l.Status != FnLcStatus.DocumentsReceived) return Bad<LetterOfCreditDto>("پرداخت فقط پس از دریافت اسناد انجام می‌شود.");
                    bool rialLc = FinanceFeatures.IsRial(l.CurrencyCode);
                    // a rial letter of credit settles at exactly its amount: a typed rate other than 1 would pay more or less than the debt (review 3, H7)
                    if (rialLc && dto.Rate.HasValue && dto.Rate.Value != 1m) return Bad<LetterOfCreditDto>("اعتبار اسنادی ریالی با نرخ ۱ تسویه می‌شود؛ نرخ دیگری پذیرفته نمی‌شود.");
                    if (rialLc) dto.Rate = 1m;
                    if (!dto.Rate.HasValue || dto.Rate.Value <= 0) return Bad<LetterOfCreditDto>("نرخ تسویه را وارد کنید.");
                    FnAccount payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct);
                    if (payable == null || deposit == null) return Bad<LetterOfCreditDto>("حساب پرداختنی یا سپرده در کدینگ نیست.");
                    decimal settled = R(l.Amount * dto.Rate.Value);
                    decimal release = Math.Min(l.MarginPaid, settled), cash = settled - release;
                    FnDetailAccount supplier = await details.GetAsync(FnDetailType.Supplier, l.ProviderId, ct);
                    text = "تسویه اعتبار اسنادی " + l.Number;
                    // the payable leaves the books at its carrying rate (the currency-tagged lines of the supplier in this currency) and the difference to the payment rate is the realised gain or loss
                    decimal carryBase = settled;
                    if (supplier.Id > 0)
                    {
                        var tagged = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(x => x.AccountId == payable.Id && x.Detail1Id == supplier.Id && x.CurrencyCode == l.CurrencyCode && x.CurrencyAmount != null && x.Voucher.Status != FnVoucherStatus.Draft)
                            .GroupBy(x => 1).Select(g => new { Base = g.Sum(x => x.Credit - x.Debit), Cur = g.Sum(x => x.Credit > 0 ? x.CurrencyAmount.Value : -x.CurrencyAmount.Value) }).FirstOrDefaultAsync(ct);
                        if (tagged != null && tagged.Cur >= l.Amount && tagged.Cur > 0) carryBase = R(tagged.Base * l.Amount / tagged.Cur);
                    }
                    decimal lcDiff = settled - carryBase;   // positive: paid more rials than carried = loss
                    var lines = new List<AutoLine>
                    {
                        new AutoLine { Account = payable, Detail1 = supplier, Debit = carryBase, Description = text, CurrencyCode = FinanceFeatures.IsRial(l.CurrencyCode) ? null : l.CurrencyCode, CurrencyAmount = FinanceFeatures.IsRial(l.CurrencyCode) ? (decimal?)null : l.Amount, Rate = FinanceFeatures.IsRial(l.CurrencyCode) ? (decimal?)null : (l.Amount == 0 ? dto.Rate : R(carryBase / l.Amount)) },
                    };
                    if (lcDiff != 0)
                    {
                        FnAccount lcGain = await RoleAccountAsync(AccountRoles.FxGain, "60303", ct), lcLoss = await RoleAccountAsync(AccountRoles.FxLoss, "80302", ct);
                        if (lcGain == null || lcLoss == null) return Bad<LetterOfCreditDto>("حساب سود و زیان تسعیر ارز در کدینگ نیست.");
                        if (lcDiff > 0) lines.Add(new AutoLine { Account = lcLoss, Debit = lcDiff, Description = "زیان تسعیر در تسویه " + text });
                        else lines.Add(new AutoLine { Account = lcGain, Credit = -lcDiff, Description = "سود تسعیر در تسویه " + text });
                    }
                    if (release > 0) lines.Add(new AutoLine { Account = deposit, Credit = release, Description = text });
                    if (cash > 0) lines.Add(new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Credit = cash, Description = text });
                    var v = await AddAutoVoucherAsync(entityId, date, text, "LetterOfCredit", l.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
                    if (v.Error != null) return Bad<LetterOfCreditDto>(v.Error);
                    await _uow.SaveChangesAsync(ct);
                    if (release > 0) _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.MarginRelease, Date = date, Amount = release, VoucherId = v.Voucher.Id, CreatedByUserId = Me });
                    _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.Settlement, Date = date, Amount = settled, VoucherId = v.Voucher.Id, Note = Clean(dto.Note), CreatedByUserId = Me });
                    l.Status = FnLcStatus.Paid; l.SettlementRate = dto.Rate; l.SettledBase = settled; l.PaidDate = date; l.MarginPaid -= release;
                    break;
                }
                case "expire":
                    if (!live) return Bad<LetterOfCreditDto>("اعتبار بسته شده است.");
                    if (l.ExpiryDate.Date >= date) return Bad<LetterOfCreditDto>("اعتبار هنوز به سررسید نرسیده است.");
                    l.Status = FnLcStatus.Expired;
                    break;
                case "cancel":
                {
                    if (!live) return Bad<LetterOfCreditDto>("اعتبار بسته شده است.");
                    if (l.MarginPaid > 0 && deposit != null)
                    {
                        text = "استرداد سپرده اعتبار اسنادی " + l.Number;
                        var lines = new List<AutoLine>
                        {
                            new AutoLine { Account = bank.Account, Detail1 = bank.Detail, Debit = l.MarginPaid, Description = text },
                            new AutoLine { Account = deposit, Credit = l.MarginPaid, Description = text },
                        };
                        var v = await AddAutoVoucherAsync(entityId, date, text, "LetterOfCredit", l.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
                        if (v.Error != null) return Bad<LetterOfCreditDto>(v.Error);
                        await _uow.SaveChangesAsync(ct);
                        _uow.Set<FnLcPosting>().Add(new FnLcPosting { LcId = l.Id, Kind = FnLcPostingKind.MarginRelease, Date = date, Amount = l.MarginPaid, VoucherId = v.Voucher.Id, CreatedByUserId = Me });
                        l.MarginPaid = 0;
                    }
                    l.Status = FnLcStatus.Cancelled;
                    break;
                }
                default:
                    return Bad<LetterOfCreditDto>("عمل نامعتبر است.");
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await LcDtoAsync(l, ct));
        }
    }
}
