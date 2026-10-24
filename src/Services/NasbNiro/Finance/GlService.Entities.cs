using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Legal entities (F3 seam): entities, the user's default entity, intercompany pairs and postings, consolidation view.
    public partial class GlService
    {
        private static LegalEntityDto ToDto(FnLegalEntity e, int defaultId) => new LegalEntityDto
        {
            Id = e.Id, Code = e.Code, Name = e.Name, Jurisdiction = e.Jurisdiction, BaseCurrency = e.BaseCurrency, IsActive = e.IsActive, IsDefault = e.Id == defaultId,
        };

        public async Task<ResponseDto<List<LegalEntityDto>>> GetEntitiesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<LegalEntityDto>>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            int def = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            List<FnLegalEntity> list = await _uow.Set<FnLegalEntity>().AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list.Select(e => ToDto(e, def)).ToList());
        }

        public async Task<ResponseDto<LegalEntityDto>> SaveEntityAsync(SaveLegalEntityDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<LegalEntityDto>(NotAllowed + " (مدیر مالی)");
            if (!FinanceFeatures.MultiEntity && (dto == null || dto.Id != FinanceSeed.MainEntityId)) return Bad<LegalEntityDto>(FinanceFeatures.OnlyRial);
            if (dto == null || Clean(dto.Name) == null) return Bad<LegalEntityDto>("نام واحد حقوقی را وارد کنید.");
            string code = Clean(dto.Code)?.ToUpperInvariant();
            string jurisdiction = Clean(dto.Jurisdiction)?.ToUpperInvariant();
            string currency = Clean(dto.BaseCurrency)?.ToUpperInvariant();
            if (code == null || code.Length > 20) return Bad<LegalEntityDto>("کد واحد حقوقی را وارد کنید.");
            if (jurisdiction != "IR" && jurisdiction != "DE") return Bad<LegalEntityDto>("حوزه قضایی باید IR یا DE باشد.");
            if (currency == null || currency.Length > 10) return Bad<LegalEntityDto>("ارز پایه را وارد کنید.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnLegalEntity e;
            if (dto.Id.HasValue)
            {
                e = await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (e == null) return ResponseDtoHelper.NotFound<LegalEntityDto>("واحد حقوقی پیدا نشد.");
                if (e.Id == FinanceSeed.MainEntityId && !dto.IsActive) return Bad<LegalEntityDto>("شرکت اصلی غیرفعال نمی‌شود.");
                if (e.Code != code) return Bad<LegalEntityDto>("کد واحد حقوقی تغییر نمی‌کند.");
                if (e.BaseCurrency != currency && await _uow.Set<FnFiscalYear>().AnyAsync(y => y.LegalEntityId == e.Id && y.Periods.Any(), ct) && await _uow.Set<FnVoucher>().AnyAsync(v => v.FiscalYear.LegalEntityId == e.Id, ct))
                    return Bad<LegalEntityDto>("ارز پایه واحدی که سند دارد تغییر نمی‌کند.");
            }
            else
            {
                if (await _uow.Set<FnLegalEntity>().AnyAsync(x => x.Code == code, ct)) return Bad<LegalEntityDto>("این کد قبلاً ثبت شده است: " + code);
                e = new FnLegalEntity { Code = code, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnLegalEntity>().Add(e);
            }
            e.Name = Clean(dto.Name);
            e.Jurisdiction = jurisdiction;
            e.BaseCurrency = currency;
            e.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(e, await EntityScope.ResolveAsync(_uow, null, Me, ct)));
        }

        public async Task<ResponseDto<LegalEntityDto>> SetDefaultEntityAsync(SetDefaultEntityDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<LegalEntityDto>(NotAllowed);
            if (dto == null) return Bad<LegalEntityDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnLegalEntity e = await _uow.Set<FnLegalEntity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.LegalEntityId, ct);
            if (e == null || !e.IsActive) return Bad<LegalEntityDto>("واحد حقوقی پیدا نشد یا غیرفعال است.");
            FnUserSetting s = await _uow.Set<FnUserSetting>().FirstOrDefaultAsync(x => x.UserId == Me && x.Key == EntityScope.DefaultKey, ct);
            if (s == null) { s = new FnUserSetting { UserId = Me, Key = EntityScope.DefaultKey }; _uow.Set<FnUserSetting>().Add(s); }
            s.Value = e.Id.ToString(CultureInfo.InvariantCulture);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(e, e.Id));
        }

        // ------------------------------------------------------------------------------------------ intercompany pairs

        public async Task<ResponseDto<List<IntercompanyPairDto>>> GetPairsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<IntercompanyPairDto>>(NotAllowed);
            var list = await _uow.Set<FnIntercompanyPair>().AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new IntercompanyPairDto
                {
                    Id = p.Id, Title = p.Title, EntityAId = p.EntityAId, EntityBId = p.EntityBId, AccountAId = p.AccountAId, AccountACode = p.AccountA.Code,
                    AccountBId = p.AccountBId, AccountBCode = p.AccountB.Code, IsActive = p.IsActive,
                }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<IntercompanyPairDto>> SavePairAsync(SaveIntercompanyPairDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<IntercompanyPairDto>(NotAllowed + " (مدیر مالی)");
            if (!FinanceFeatures.MultiEntity) return Bad<IntercompanyPairDto>(FinanceFeatures.OnlyRial);
            if (dto == null) return Bad<IntercompanyPairDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (dto.EntityAId == dto.EntityBId) return Bad<IntercompanyPairDto>("دو واحد حقوقی باید متفاوت باشند.");
            int found = await _uow.Set<FnLegalEntity>().CountAsync(x => x.IsActive && (x.Id == dto.EntityAId || x.Id == dto.EntityBId), ct);
            if (found != 2) return Bad<IntercompanyPairDto>("واحد حقوقی پیدا نشد یا غیرفعال است.");
            FnAccount a = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == Digits(Clean(dto.AccountACode) ?? ""), ct);
            FnAccount b = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == Digits(Clean(dto.AccountBCode) ?? ""), ct);
            if (a == null || b == null || a.Level != FnAccountLevel.Subsidiary || b.Level != FnAccountLevel.Subsidiary) return Bad<IntercompanyPairDto>("حساب‌های طرف مقابل باید معین و موجود باشند.");
            FnIntercompanyPair p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<FnIntercompanyPair>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (p == null) return ResponseDtoHelper.NotFound<IntercompanyPairDto>("جفت حساب پیدا نشد.");
            }
            else
            {
                p = new FnIntercompanyPair();
                _uow.Set<FnIntercompanyPair>().Add(p);
            }
            p.Title = Clean(dto.Title);
            p.EntityAId = dto.EntityAId; p.EntityBId = dto.EntityBId;
            p.AccountAId = a.Id; p.AccountBId = b.Id;
            p.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new IntercompanyPairDto
            {
                Id = p.Id, Title = p.Title, EntityAId = p.EntityAId, EntityBId = p.EntityBId, AccountAId = a.Id, AccountACode = a.Code, AccountBId = b.Id, AccountBCode = b.Code, IsActive = p.IsActive,
            });
        }

        // ------------------------------------------------------------------------------------------ intercompany posting

        private static AccountingEvent IcEvent(int entityId, DateTime date, string number, string description, string debitCode, string creditCode, decimal amount, int userId)
        {
            return new AccountingEvent
            {
                Type = AccountingEventType.Intercompany, EventDate = date, Status = AccountingEventStatus.Pending, SourceType = "Intercompany", SourceId = 0, SourceNumber = number,
                Description = description, Key = Guid.NewGuid(), CreatedAt = DateTimeOffset.Now, CreatedByUserId = userId, LegalEntityId = entityId,
                Lines = new List<AccountingEventLine>
                {
                    new AccountingEventLine { RowNumber = 1, AccountRole = EventPostingService.DirectRolePrefix + debitCode, Debit = amount, Description = description },
                    new AccountingEventLine { RowNumber = 2, AccountRole = EventPostingService.DirectRolePrefix + creditCode, Credit = amount, Description = description },
                },
            };
        }

        public async Task<ResponseDto<IntercompanyResultDto>> PostIntercompanyAsync(IntercompanyPostDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<IntercompanyResultDto>(NotAllowed);
            if (!FinanceFeatures.MultiEntity) return Bad<IntercompanyResultDto>(FinanceFeatures.OnlyRial);
            if (dto == null || dto.Date == default) return Bad<IntercompanyResultDto>("اطلاعات ارسال‌شده کامل نیست.");
            decimal amount = R(dto.Amount);
            if (amount <= 0) return Bad<IntercompanyResultDto>("مبلغ باید بیشتر از صفر باشد.");
            FnIntercompanyPair pair = await _uow.Set<FnIntercompanyPair>().Include(p => p.AccountA).Include(p => p.AccountB).FirstOrDefaultAsync(p => p.Id == dto.PairId, ct);
            if (pair == null || !pair.IsActive) return Bad<IntercompanyResultDto>("جفت حساب بین‌شرکتی پیدا نشد یا غیرفعال است.");
            if (dto.ChargerEntityId != pair.EntityAId && dto.ChargerEntityId != pair.EntityBId) return Bad<IntercompanyResultDto>("واحد صادرکننده باید یکی از دو واحد جفت حساب باشد.");
            // each entity books the amount in its own currency; when the currencies differ the exchange rate (units of the other currency per one unit of the charger's) is mandatory
            FnLegalEntity entA = await _uow.Set<FnLegalEntity>().AsNoTracking().FirstAsync(x => x.Id == pair.EntityAId, ct), entB = await _uow.Set<FnLegalEntity>().AsNoTracking().FirstAsync(x => x.Id == pair.EntityBId, ct);
            bool chargerIsA = dto.ChargerEntityId == pair.EntityAId;
            string chargerCurrency = chargerIsA ? entA.BaseCurrency : entB.BaseCurrency, otherCurrency = chargerIsA ? entB.BaseCurrency : entA.BaseCurrency;
            decimal otherAmount = amount, rateUsed = 1m;
            if (!string.Equals(chargerCurrency, otherCurrency, StringComparison.OrdinalIgnoreCase))
            {
                if (!dto.Rate.HasValue || dto.Rate.Value <= 0) return Bad<IntercompanyResultDto>($"ارز دو واحد متفاوت است ({chargerCurrency} و {otherCurrency}); نرخ تبدیل را وارد کنید.");
                rateUsed = dto.Rate.Value;
                otherAmount = R(amount * rateUsed);
                if (otherAmount <= 0) return Bad<IntercompanyResultDto>("مبلغ تبدیل‌شده باید بیشتر از صفر باشد.");
            }
            decimal amountA = chargerIsA ? amount : otherAmount, amountB = chargerIsA ? otherAmount : amount;
            FnAccount counterA = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == Digits(Clean(dto.CounterAccountACode) ?? ""), ct);
            FnAccount counterB = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == Digits(Clean(dto.CounterAccountBCode) ?? ""), ct);
            if (counterA == null || counterB == null || !counterA.IsActive || !counterB.IsActive || counterA.Level != FnAccountLevel.Subsidiary || counterB.Level != FnAccountLevel.Subsidiary)
                return Bad<IntercompanyResultDto>("حساب‌های مقابل (درآمد و هزینه) باید معین و فعال باشند.");
            // both entities must be able to take the date (open period)
            foreach (int entityId in new[] { pair.EntityAId, pair.EntityBId })
            {
                var check = await CheckDateAsync(dto.Date.Date, entityId, ct);
                if (check.Error != null) return Bad<IntercompanyResultDto>(check.Error);
            }

            int year = FinanceSeed.PersianYearOf(dto.Date);
            string number = "IC-" + year.ToString(CultureInfo.InvariantCulture) + "-" + (await InventoryNumbers.NextAsync(_uow, FinanceNumbers.IntercompanyPost, year, ct)).ToString("D4", CultureInfo.InvariantCulture);
            string text = Clean(dto.Description) ?? "تراکنش بین‌شرکتی";
            AccountingEvent eventA, eventB;
            if (dto.ChargerEntityId == pair.EntityAId)
            {
                eventA = IcEvent(pair.EntityAId, dto.Date.Date, number, text, pair.AccountA.Code, counterA.Code, amountA, Me);
                eventB = IcEvent(pair.EntityBId, dto.Date.Date, number, text, counterB.Code, pair.AccountB.Code, amountB, Me);
            }
            else
            {
                eventB = IcEvent(pair.EntityBId, dto.Date.Date, number, text, pair.AccountB.Code, counterB.Code, amountB, Me);
                eventA = IcEvent(pair.EntityAId, dto.Date.Date, number, text, counterA.Code, pair.AccountA.Code, amountA, Me);
            }
            _uow.Set<AccountingEvent>().Add(eventA);
            _uow.Set<AccountingEvent>().Add(eventB);
            await _uow.SaveChangesAsync(ct);

            var post = new FnIntercompanyPost
            {
                Number = number, PairId = pair.Id, Date = dto.Date.Date, Amount = amount, AmountA = amountA, AmountB = amountB, Rate = rateUsed, ChargerEntityId = dto.ChargerEntityId,
                CounterAccountAId = counterA.Id, CounterAccountBId = counterB.Id, Description = text,
                EventAId = eventA.Id, EventBId = eventB.Id, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnIntercompanyPost>().Add(post);
            await _uow.SaveChangesAsync(ct);

            // the one posting path: events become vouchers (status Checked; the finance manager finalises)
            await _posting.PostPendingAsync(ct);
            var vouchers = await _uow.Set<FnVoucher>().AsNoTracking().Where(v => v.AccountingEventId == eventA.Id || v.AccountingEventId == eventB.Id)
                .Select(v => new { v.Id, v.AccountingEventId }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(new IntercompanyResultDto
            {
                Id = post.Id, Number = number, PairId = pair.Id, Date = post.Date, Amount = amount, AmountA = amountA, AmountB = amountB, Rate = rateUsed, ChargerEntityId = post.ChargerEntityId, Description = text,
                EventAId = eventA.Id, EventBId = eventB.Id,
                VoucherAId = vouchers.Where(v => v.AccountingEventId == eventA.Id).Select(v => (int?)v.Id).FirstOrDefault(),
                VoucherBId = vouchers.Where(v => v.AccountingEventId == eventB.Id).Select(v => (int?)v.Id).FirstOrDefault(),
            });
        }

        // ------------------------------------------------------------------------------------------ consolidation

        private static Dictionary<int, decimal> ParseRates(string text, out string error)
        {
            error = null;
            var map = new Dictionary<int, decimal>();
            if (Clean(text) == null) return map;
            foreach (string part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    || !decimal.TryParse(Digits(kv[1].Trim()), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate) || rate <= 0)
                {
                    error = "قالب نرخ‌ها نامعتبر است؛ نمونه: 2=1.1,3=0.9";
                    return map;
                }
                map[id] = rate;
            }
            return map;
        }

        public async Task<ResponseDto<ConsolidationDto>> ConsolidationAsync(ConsolidationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<ConsolidationDto>(NotAllowed);
            query ??= new ConsolidationQuery();
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime from = (query.From ?? new DateTime(2000, 1, 1)).Date, to = (query.To ?? new DateTime(2100, 1, 1)).Date;
            if (to < from) return Bad<ConsolidationDto>("بازه تاریخ نامعتبر است.");
            Dictionary<int, decimal> rates = ParseRates(query.Rates, out string rateError);
            if (rateError != null) return Bad<ConsolidationDto>(rateError);

            List<FnLegalEntity> entities = await _uow.Set<FnLegalEntity>().AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Id).ToListAsync(ct);
            FnLegalEntity main = entities.FirstOrDefault(e => e.Id == FinanceSeed.MainEntityId) ?? entities.FirstOrDefault();
            if (main == null) return Bad<ConsolidationDto>("واحد حقوقی تعریف نشده است.");
            var dto = new ConsolidationDto { From = from, To = to, PresentationCurrency = main.BaseCurrency };
            foreach (FnLegalEntity e in entities)
            {
                decimal rate = 1m;
                if (e.BaseCurrency != main.BaseCurrency)
                {
                    if (!rates.TryGetValue(e.Id, out rate)) return Bad<ConsolidationDto>($"برای واحد {e.Code} با ارز {e.BaseCurrency} نرخ تبدیل به {main.BaseCurrency} لازم است (ورودی rates).");
                }
                else if (rates.TryGetValue(e.Id, out decimal given)) rate = given;
                dto.Entities.Add(new ConsolidationEntityDto { EntityId = e.Id, Code = e.Code, Currency = e.BaseCurrency, Rate = rate });
            }
            Dictionary<int, decimal> rateOf = dto.Entities.ToDictionary(x => x.EntityId, x => x.Rate);

            var sums = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.Date >= from && l.Voucher.Date <= to && (query.IncludeDraft || l.Voucher.Status == FnVoucherStatus.Final))
                .GroupBy(l => new { EntityId = l.Voucher.FiscalYear.LegalEntityId, l.AccountId })
                .Select(g => new { g.Key.EntityId, g.Key.AccountId, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
            Dictionary<int, FnAccount> accounts = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Id, ct);

            var rows = new Dictionary<string, ConsolidationRowDto>();
            ConsolidationRowDto RowFor(FnAccount a)
            {
                if (!rows.TryGetValue(a.Code, out ConsolidationRowDto r)) { r = new ConsolidationRowDto { AccountCode = a.Code, AccountTitle = a.Title }; rows[a.Code] = r; }
                return r;
            }
            foreach (var s in sums)
            {
                if (!rateOf.TryGetValue(s.EntityId, out decimal rate)) continue;
                ConsolidationRowDto row = RowFor(accounts[s.AccountId]);
                row.ByEntity.Add(new ConsolidationAmountDto { EntityId = s.EntityId, Amount = R((s.Debit - s.Credit) * rate) });
            }

            List<FnIntercompanyPair> pairs = await _uow.Set<FnIntercompanyPair>().AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id).ToListAsync(ct);
            foreach (FnIntercompanyPair p in pairs)
            {
                FnAccount accA = accounts[p.AccountAId], accB = accounts[p.AccountBId];
                decimal balA = rows.TryGetValue(accA.Code, out ConsolidationRowDto ra) ? ra.ByEntity.Where(x => x.EntityId == p.EntityAId).Sum(x => x.Amount) : 0;
                decimal balB = rows.TryGetValue(accB.Code, out ConsolidationRowDto rb) ? rb.ByEntity.Where(x => x.EntityId == p.EntityBId).Sum(x => x.Amount) : 0;
                dto.Pairs.Add(new ConsolidationPairDto { PairId = p.Id, Title = p.Title, BalanceA = balA, BalanceB = balB, Difference = balA + balB, Matched = balA + balB == 0 });
                RowFor(accA).Elimination -= balA;
                RowFor(accB).Elimination -= balB;

                // revenue and expense of the postings of the pair inside the range
                List<FnIntercompanyPost> posts = await _uow.Set<FnIntercompanyPost>().AsNoTracking().Where(x => x.PairId == p.Id && x.Date >= from && x.Date <= to).ToListAsync(ct);
                foreach (FnIntercompanyPost post in posts)
                {
                    int posted = await _uow.Set<FnVoucher>().CountAsync(v => (v.AccountingEventId == post.EventAId || v.AccountingEventId == post.EventBId) && (query.IncludeDraft || v.Status == FnVoucherStatus.Final), ct);
                    if (posted != 2) continue;
                    decimal amountA = R((post.AmountA != 0 ? post.AmountA : post.Amount) * rateOf[p.EntityAId]), amountB = R((post.AmountB != 0 ? post.AmountB : post.Amount) * rateOf[p.EntityBId]);
                    FnAccount ca = accounts[post.CounterAccountAId], cb = accounts[post.CounterAccountBId];
                    if (post.ChargerEntityId == p.EntityAId) { RowFor(ca).Elimination += amountA; RowFor(cb).Elimination -= amountB; }
                    else { RowFor(cb).Elimination += amountB; RowFor(ca).Elimination -= amountA; }
                }
            }
            foreach (ConsolidationRowDto r in rows.Values)
            {
                r.Total = r.ByEntity.Sum(x => x.Amount);
                r.Consolidated = r.Total + r.Elimination;
            }
            dto.Rows = rows.Values.OrderBy(r => r.AccountCode).ToList();
            dto.TotalBefore = dto.Rows.Sum(r => r.Total);
            dto.TotalEliminations = dto.Rows.Sum(r => r.Elimination);
            dto.TotalConsolidated = dto.Rows.Sum(r => r.Consolidated);
            dto.Balanced = dto.TotalConsolidated == 0 && dto.Pairs.All(x => x.Matched);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
