using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>Statutory rates are data: nothing here is hard-coded except the lookup rule.</summary>
    internal static class TaxRates
    {
        /// <summary>The rate (in percent) valid on a date, or null when the table has no row for it.</summary>
        public static Task<decimal?> RateAtAsync(IUnitOfWork uow, FnTaxKind kind, DateTime date, CancellationToken ct) =>
            uow.Set<FnTaxRate>().AsNoTracking()
                .Where(x => x.Kind == kind && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date))
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
                .Select(x => (decimal?)x.Rate).FirstOrDefaultAsync(ct);
    }

    public interface ITaxService
    {
        Task<ResponseDto<PagedList<TaxRateDto>>> GetRatesAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<TaxRateDto>> SaveRateAsync(SaveTaxRateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<VatSeasonDto>> GetVatSeasonAsync(int year, int season, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<TaxpayerSaleDto>>> GetTaxpayerSalesAsync(TreasuryQuery query, CancellationToken cancellationToken);
    }

    public class TaxService : ITaxService
    {
        private readonly IUnitOfWork _uow;
        private readonly Inventory.IInventoryUserContext _user;

        public TaxService(IUnitOfWork uow, Inventory.IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private static PagedList<T> Page<T>(List<T> all, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(all, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        private static TaxRateDto ToDto(FnTaxRate r) => new TaxRateDto
        {
            Id = r.Id, Kind = r.Kind, Rate = r.Rate, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, Source = r.Source, Verify = r.Verify, Note = r.Note,
        };

        public async Task<ResponseDto<PagedList<TaxRateDto>>> GetRatesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<TaxRateDto>>("شما مجوز این کار را ندارید");
            await FinanceSeed.EnsureTaxRatesAsync(_uow, ct);
            IQueryable<FnTaxRate> q = _uow.Set<FnTaxRate>().AsNoTracking();
            if (query.Kind.HasValue) q = q.Where(r => (int)r.Kind == query.Kind.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<FnTaxRate> rows = await q.OrderBy(r => r.Kind).ThenByDescending(r => r.EffectiveFrom).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(ToDto).ToList(), total, number, size));
        }

        public async Task<ResponseDto<TaxRateDto>> SaveRateAsync(SaveTaxRateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<TaxRateDto>("شما مجوز این کار را ندارید");
            if (dto == null) return Bad<TaxRateDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!Enum.IsDefined(typeof(FnTaxKind), dto.Kind)) return Bad<TaxRateDto>("نوع نرخ نامعتبر است.");
            if (dto.Rate < 0 || dto.Rate > 100) return Bad<TaxRateDto>("نرخ باید بین ۰ و ۱۰۰ درصد باشد.");
            if (dto.EffectiveFrom == default) return Bad<TaxRateDto>("تاریخ شروع اعتبار لازم است.");
            if (dto.EffectiveTo.HasValue && dto.EffectiveTo.Value < dto.EffectiveFrom) return Bad<TaxRateDto>("پایان اعتبار قبل از شروع آن است.");
            if (Clean(dto.Source) == null) return Bad<TaxRateDto>("منبع قانونی نرخ را بنویسید.");
            await FinanceSeed.EnsureAsync(_uow, ct);

            FnTaxRate r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<FnTaxRate>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (r == null) return ResponseDtoHelper.NotFound<TaxRateDto>("نرخ پیدا نشد.");
            }
            else
            {
                r = new FnTaxRate { Jurisdiction = FinanceSeed.Iran, CreatedAt = DateTimeOffset.Now, CreatedByUserId = _user.UserId };
                // a new open-ended rate closes the previous open-ended one the day before
                if (!dto.EffectiveTo.HasValue)
                {
                    FnTaxRate previous = await _uow.Set<FnTaxRate>().Where(x => x.Kind == dto.Kind && x.EffectiveTo == null && x.EffectiveFrom < dto.EffectiveFrom)
                        .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
                    if (previous != null) previous.EffectiveTo = dto.EffectiveFrom.Date.AddDays(-1);
                }
                _uow.Set<FnTaxRate>().Add(r);
            }
            r.Kind = dto.Kind; r.Rate = dto.Rate; r.EffectiveFrom = dto.EffectiveFrom.Date; r.EffectiveTo = dto.EffectiveTo?.Date;
            r.Source = Clean(dto.Source); r.Verify = dto.Verify; r.Note = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(r));
        }

        public async Task<ResponseDto<VatSeasonDto>> GetVatSeasonAsync(int year, int season, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<VatSeasonDto>("شما مجوز این کار را ندارید");
            return await BuildVatSeasonAsync(_uow, year, season, ct);
        }

        /// <summary>The season report without a role check (shared with the Sepidar outbox job).</summary>
        internal static async Task<ResponseDto<VatSeasonDto>> BuildVatSeasonAsync(IUnitOfWork uow, int year, int season, CancellationToken ct)
        {
            if (year < 1300 || year > 1600 || season < 1 || season > 4) return Bad<VatSeasonDto>("سال یا فصل نامعتبر است.");
            DateTime from = FinanceSeed.PersianDate(year, (season - 1) * 3 + 1, 1);
            DateTime next = season == 4 ? FinanceSeed.PersianDate(year + 1, 1, 1) : FinanceSeed.PersianDate(year, season * 3 + 1, 1);
            DateTime to = next.AddDays(-1);
            var dto = new VatSeasonDto { Year = year, Season = season, From = from, To = to };

            var sales = await uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.RetentionRelease && i.Date >= from && i.Date <= to)
                .Select(i => new { i.CustomerId, CustomerName = i.Customer.Name, i.Customer.EconomicCode, i.Customer.NationalId, i.Type, i.NetAmount, i.VatAmount }).ToListAsync(ct);
            foreach (var g in sales.GroupBy(s => s.CustomerId))
            {
                decimal net = g.Sum(s => s.Type == FnInvoiceType.CreditNote ? -s.NetAmount : s.NetAmount);
                decimal vat = g.Sum(s => s.Type == FnInvoiceType.CreditNote ? -s.VatAmount : s.VatAmount);
                dto.Rows.Add(new VatSeasonRowDto { Side = "Sales", PartyName = g.First().CustomerName, EconomicCode = g.First().EconomicCode, NationalId = g.First().NationalId, NetAmount = net, VatAmount = vat, Documents = g.Count() });
                dto.SalesNet += net; dto.SalesVat += vat;
            }

            // purchases: the approved supplier invoices of the season (net and VAT from the invoice itself, one row per supplier)
            var invoices = await uow.Set<Entities.Model.NasbNiro.Procurement.SupplierInvoice>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == Entities.Model.NasbNiro.Procurement.SupplierInvoiceStatus.Approved && i.InvoiceDate >= from && i.InvoiceDate <= to)
                .Select(i => new { i.ProviderId, ProviderName = i.Provider.Name, i.NetAmount, i.VatAmount, i.NonRecoverableVatAmount }).ToListAsync(ct);
            List<int> providerIds = invoices.Select(i => i.ProviderId).Distinct().ToList();
            var idCodes = await uow.Set<Entities.Model.NasbNiro.Procurement.SupplierProfile>().AsNoTracking().Where(p => providerIds.Contains(p.ProviderId)).Select(p => new { p.ProviderId, p.EconomicCode, p.NationalId }).ToListAsync(ct);
            foreach (var g in invoices.GroupBy(i => i.ProviderId))
            {
                // only the deductible VAT goes to the return; the rest is cost (F3b)
                decimal net = g.Sum(x => x.NetAmount), vat = g.Sum(x => x.VatAmount - x.NonRecoverableVatAmount);
                dto.NonRecoverableVat += g.Sum(x => x.NonRecoverableVatAmount);
                var codes = idCodes.FirstOrDefault(c => c.ProviderId == g.Key);
                dto.Rows.Add(new VatSeasonRowDto { Side = "Purchases", PartyName = g.First().ProviderName, EconomicCode = codes?.EconomicCode, NationalId = codes?.NationalId, NetAmount = net, VatAmount = vat, Documents = g.Count() });
                dto.PurchasesNet += net; dto.PurchasesVat += vat;
            }
            // supplier credit / debit notes of the season change the input VAT and the purchases (F3b)
            var notes = await uow.Set<FnSupplierNote>().AsNoTracking().Where(n => n.Status == FnSettlementStatus.Posted && n.Date >= from && n.Date <= to)
                .Select(n => new { n.ProviderId, ProviderName = uow.Set<Entities.Model.NasbNiro.Providers.Provider>().Where(p => p.Id == n.ProviderId).Select(p => p.Name).FirstOrDefault(), n.Kind, n.NetAmount, n.VatAmount, n.NonRecoverableVatAmount }).ToListAsync(ct);
            foreach (var g in notes.GroupBy(n => n.ProviderId))
            {
                decimal net = g.Sum(n => n.Kind == FnSupplierNoteKind.Credit ? -n.NetAmount : n.NetAmount), vat = g.Sum(n => n.Kind == FnSupplierNoteKind.Credit ? -(n.VatAmount - n.NonRecoverableVatAmount) : n.VatAmount - n.NonRecoverableVatAmount);
                dto.Rows.Add(new VatSeasonRowDto { Side = "Purchases", PartyName = (g.First().ProviderName ?? "") + " (یادداشت بدهکار/بستانکار)", NetAmount = net, VatAmount = vat, Documents = g.Count() });
                dto.PurchasesNet += net; dto.PurchasesVat += vat;
            }
            // sale of fixed assets with VAT is a sale of the season (F3b)
            foreach (var x in await uow.Set<FnAssetDisposal>().AsNoTracking().Where(d => d.Status == FnDocStatus.Posted && d.Kind == FnDisposalKind.Sale && d.VatAmount > 0 && d.Date >= from && d.Date <= to)
                         .Select(d => new { d.Number, d.ProceedsAmount, d.VatAmount, AssetCode = d.Asset.Code }).ToListAsync(ct))
            {
                dto.Rows.Add(new VatSeasonRowDto { Side = "Sales", PartyName = "فروش دارایی " + x.AssetCode, NetAmount = x.ProceedsAmount, VatAmount = x.VatAmount, Documents = 1 });
                dto.SalesNet += x.ProceedsAmount; dto.SalesVat += x.VatAmount;
            }
            // other documents that carry purchase VAT (not supplier invoices): VAT only, one row per document
            var others = await uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.AccountRole == AccountRoles.Vat && l.Event.Status != AccountingEventStatus.NotRequired && l.Event.SourceType != "SupplierInvoice"
                            && l.Event.EventDate >= from && l.Event.EventDate <= to && (l.Event.LegalEntityId == FinanceSeed.MainEntityId || l.Event.LegalEntityId == 0))   // the VAT return is the main company's: other entities file their own
                .GroupBy(l => new { l.EventId, l.Event.Description, l.Event.SourceNumber })
                .Select(g => new { g.Key.Description, g.Key.SourceNumber, Vat = g.Sum(x => x.Debit - x.Credit) }).Take(5000).ToListAsync(ct);
            foreach (var p in others.Where(p => p.Vat != 0))
            {
                dto.Rows.Add(new VatSeasonRowDto { Side = "Purchases", PartyName = p.Description ?? p.SourceNumber, VatAmount = p.Vat, Documents = 1 });
                dto.PurchasesVat += p.Vat;
            }
            dto.VatPayable = dto.SalesVat - dto.PurchasesVat;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<PagedList<TaxpayerSaleDto>>> GetTaxpayerSalesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<TaxpayerSaleDto>>("شما مجوز این کار را ندارید");
            IQueryable<FnSalesInvoice> q = _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.RetentionRelease);
            if (query.From.HasValue) q = q.Where(i => i.Date >= query.From.Value);
            if (query.To.HasValue) q = q.Where(i => i.Date <= query.To.Value);
            IQueryable<TaxpayerSaleDto> rows = q.OrderBy(i => i.Date).ThenBy(i => i.Id).Select(i => new TaxpayerSaleDto
            {
                InvoiceId = i.Id, Number = i.Number, Date = i.Date, CustomerName = i.Customer.Name, EconomicCode = i.Customer.EconomicCode, NationalId = i.Customer.NationalId,
                PostalCode = i.Customer.PostalCode, NetAmount = i.NetAmount, VatAmount = i.VatAmount, GrossAmount = i.GrossAmount, TaxId = i.TaxId, TaxSentStatus = i.TaxSentStatus, Type = i.Type,
            });
            return ResponseDtoHelper.Ok(await PagedList<TaxpayerSaleDto>.CreateAsync(rows, query.PageNumber, query.PageSize, ct));
        }
    }
}
