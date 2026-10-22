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
    public interface ITaxCompletenessService
    {
        Task<ResponseDto<FnVatDetailDto>> VatDetailAsync(int year, int season, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnTaxAdjustmentDto>>> GetAdjustmentsAsync(int fiscalYearId, CancellationToken cancellationToken);
        Task<ResponseDto<FnTaxAdjustmentDto>> SaveAdjustmentAsync(FnSaveTaxAdjustmentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteAdjustmentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnTaxableProfitDto>> TaxableProfitAsync(FnTaxableProfitQuery query, CancellationToken cancellationToken);
    }

    // Tax books (F3 section 7): the document list behind the VAT season report, carry-forward, book-tax adjustments and the taxable profit.
    public partial class GlService : ITaxCompletenessService
    {
        public async Task<ResponseDto<FnVatDetailDto>> VatDetailAsync(int year, int season, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnVatDetailDto>(NotAllowed);
            var total = await TaxService.BuildVatSeasonAsync(_uow, year, season, ct);
            if (!total.Success) return Bad<FnVatDetailDto>(total.Message);
            VatSeasonDto t = total.Data;
            var dto = new FnVatDetailDto { Year = year, Season = season, From = t.From, To = t.To, SalesNet = t.SalesNet, SalesVat = t.SalesVat, PurchasesNet = t.PurchasesNet, PurchasesVat = t.PurchasesVat, VatPayable = t.VatPayable };

            // same filters as the season report, one row per document
            foreach (var i in await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Date >= t.From && i.Date <= t.To)
                         .OrderBy(i => i.Date).ThenBy(i => i.Id).Select(i => new { i.Number, i.Date, i.Type, i.NetAmount, i.VatAmount, Name = i.Customer.Name, i.Customer.EconomicCode }).ToListAsync(ct))
            {
                int sign = i.Type == FnInvoiceType.CreditNote ? -1 : 1;
                dto.Rows.Add(new FnVatDetailRowDto { Side = "Sales", Date = i.Date, DocumentNumber = i.Number, PartyName = i.Name, EconomicCode = i.EconomicCode, NetAmount = sign * i.NetAmount, VatAmount = sign * i.VatAmount, SourceType = i.Type == FnInvoiceType.CreditNote ? "CreditNote" : "SalesInvoice" });
            }
            foreach (var i in await _uow.Set<Entities.Model.NasbNiro.Procurement.SupplierInvoice>().AsNoTracking()
                         .Where(i => !i.Deleted && i.Status == Entities.Model.NasbNiro.Procurement.SupplierInvoiceStatus.Approved && i.InvoiceDate >= t.From && i.InvoiceDate <= t.To)
                         .OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id).Select(i => new { i.InvoiceNumber, i.Number, i.InvoiceDate, i.NetAmount, i.VatAmount, Name = i.Provider.Name }).ToListAsync(ct))
                dto.Rows.Add(new FnVatDetailRowDto { Side = "Purchases", Date = i.InvoiceDate, DocumentNumber = i.InvoiceNumber ?? i.Number, PartyName = i.Name, NetAmount = i.NetAmount, VatAmount = i.VatAmount, SourceType = "SupplierInvoice" });
            foreach (var o in await _uow.Set<AccountingEventLine>().AsNoTracking()
                         .Where(l => l.AccountRole == AccountRoles.Vat && l.Event.Status != AccountingEventStatus.NotRequired && l.Event.SourceType != "SupplierInvoice" && l.Event.EventDate >= t.From && l.Event.EventDate <= t.To)
                         .GroupBy(l => new { l.EventId, l.Event.Description, l.Event.SourceNumber, l.Event.EventDate, l.Event.SourceType })
                         .Select(g => new { g.Key.Description, g.Key.SourceNumber, g.Key.EventDate, g.Key.SourceType, Vat = g.Sum(x => x.Debit - x.Credit) }).Take(5000).ToListAsync(ct))
                if (o.Vat != 0) dto.Rows.Add(new FnVatDetailRowDto { Side = "Purchases", Date = o.EventDate, DocumentNumber = o.SourceNumber, PartyName = o.Description ?? o.SourceNumber, VatAmount = o.Vat, SourceType = o.SourceType });

            // carry-forward of a credit position inside the Persian year
            decimal carry = 0;
            for (int s = 1; s < season; s++)
            {
                var prev = await TaxService.BuildVatSeasonAsync(_uow, year, s, ct);
                decimal net = prev.Data.VatPayable - carry;
                carry = net < 0 ? -net : 0;
            }
            dto.CarryIn = carry;
            decimal result = dto.VatPayable - carry;
            dto.NetPayable = result > 0 ? result : 0;
            dto.CarryOut = result < 0 ? -result : 0;
            return ResponseDtoHelper.Ok(dto);
        }

        private static FnTaxAdjustmentDto AdjustmentDto(FnTaxAdjustment a) => new FnTaxAdjustmentDto { Id = a.Id, FiscalYearId = a.FiscalYearId, Kind = (int)a.Kind, Description = a.Description, Amount = a.Amount, Source = a.Source, Verify = a.Verify };

        public async Task<ResponseDto<List<FnTaxAdjustmentDto>>> GetAdjustmentsAsync(int fiscalYearId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnTaxAdjustmentDto>>(NotAllowed);
            var rows = await _uow.Set<FnTaxAdjustment>().AsNoTracking().Where(a => a.FiscalYearId == fiscalYearId).OrderBy(a => a.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(AdjustmentDto).ToList());
        }

        public async Task<ResponseDto<FnTaxAdjustmentDto>> SaveAdjustmentAsync(FnSaveTaxAdjustmentDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnTaxAdjustmentDto>(NotAllowed);
            if (dto == null || Clean(dto.Description) == null) return Bad<FnTaxAdjustmentDto>("شرح تعدیل را وارد کنید.");
            if (!Enum.IsDefined(typeof(FnTaxAdjustmentKind), dto.Kind)) return Bad<FnTaxAdjustmentDto>("نوع تعدیل نامعتبر است.");
            if (dto.Amount == 0) return Bad<FnTaxAdjustmentDto>("مبلغ تعدیل صفر است.");
            if (Clean(dto.Source) == null && !dto.Verify) return Bad<FnTaxAdjustmentDto>("برای تعدیل بدون «نیاز به تایید»، مرجع (ماده قانون یا نظر مشاور) را بنویسید.");
            if (!await _uow.Set<FnFiscalYear>().AnyAsync(y => y.Id == dto.FiscalYearId, ct)) return ResponseDtoHelper.NotFound<FnTaxAdjustmentDto>("سال مالی پیدا نشد.");
            FnTaxAdjustment a = dto.Id.HasValue ? await _uow.Set<FnTaxAdjustment>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && a == null) return ResponseDtoHelper.NotFound<FnTaxAdjustmentDto>("تعدیل پیدا نشد.");
            if (a == null) { a = new FnTaxAdjustment { CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now }; _uow.Set<FnTaxAdjustment>().Add(a); }
            a.FiscalYearId = dto.FiscalYearId; a.Kind = (FnTaxAdjustmentKind)dto.Kind; a.Description = Clean(dto.Description); a.Amount = R(dto.Amount); a.Source = Clean(dto.Source); a.Verify = dto.Verify;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(AdjustmentDto(a));
        }

        public async Task<ResponseDto<bool>> DeleteAdjustmentAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<bool>(NotAllowed);
            FnTaxAdjustment a = await _uow.Set<FnTaxAdjustment>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<bool>("تعدیل پیدا نشد.");
            _uow.Set<FnTaxAdjustment>().Remove(a);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<FnTaxableProfitDto>> TaxableProfitAsync(FnTaxableProfitQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnTaxableProfitDto>(NotAllowed);
            if (query == null) return Bad<FnTaxableProfitDto>("سال مالی را وارد کنید.");
            if (query.CreateProvision && !_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnTaxableProfitDto>(NotAllowed);
            if (query.RatePercent.HasValue && (query.RatePercent < 0 || query.RatePercent > 100)) return Bad<FnTaxableProfitDto>("نرخ مالیات نامعتبر است.");
            FnFiscalYear year = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == query.FiscalYearId, ct);
            if (year == null) return ResponseDtoHelper.NotFound<FnTaxableProfitDto>("سال مالی پیدا نشد.");
            // book profit before tax: income statement accounts except 804 (income tax), without closing vouchers
            decimal net = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.FiscalYearId == year.Id && l.Voucher.Status == FnVoucherStatus.Final && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                            && (l.Account.Code.StartsWith("6") || l.Account.Code.StartsWith("7") || l.Account.Code.StartsWith("8")) && !l.Account.Code.StartsWith("804"))
                .SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            var items = await _uow.Set<FnTaxAdjustment>().AsNoTracking().Where(a => a.FiscalYearId == year.Id).OrderBy(a => a.Id).ToListAsync(ct);
            var dto = new FnTaxableProfitDto { FiscalYearId = year.Id, BookProfitBeforeTax = -net, Adjustments = items.Sum(a => a.Amount), Items = items.Select(AdjustmentDto).ToList(), RatePercent = query.RatePercent };
            dto.TaxableProfit = dto.BookProfitBeforeTax + dto.Adjustments;
            if (query.RatePercent.HasValue && dto.TaxableProfit > 0) dto.TaxAmount = R(dto.TaxableProfit * query.RatePercent.Value / 100m);
            if (query.CreateProvision)
            {
                if (!query.RatePercent.HasValue) return Bad<FnTaxableProfitDto>("برای سند ذخیره مالیات، نرخ مالیات را وارد کنید.");
                if (dto.TaxAmount <= 0) return Bad<FnTaxableProfitDto>("سود مشمول مالیات صفر یا منفی است؛ ذخیره‌ای ثبت نمی‌شود.");
                int entityId = await EntityScope.ResolveAsync(_uow, year.LegalEntityId, Me, ct);
                FnAccount expense = await AccountByCodeAsync("80401", ct), liability = await AccountByCodeAsync("30401", ct);
                if (expense == null || liability == null) return Bad<FnTaxableProfitDto>("حساب‌های ۸۰۴۰۱ و ۳۰۴۰۱ پیدا نشد.");
                string number = await NextNumberAsync("TP", FinanceNumbers.TaxProvision, year.EndDate, ct);
                string text = $"ذخیره مالیات بر درآمد سال مالی {year.Title ?? year.Id.ToString()} ({number})";
                var lines = new List<AutoLine> { new AutoLine { Account = expense, Debit = dto.TaxAmount, Description = text }, new AutoLine { Account = liability, Credit = dto.TaxAmount, Description = text } };
                // a draft: the finance manager reviews it like any manual voucher
                var v = await AddAutoVoucherAsync(entityId, year.EndDate.Date, text, "TaxProvision", number, lines, FnVoucherType.Normal, null, ct, FnVoucherStatus.Draft);
                if (v.Error != null) return Bad<FnTaxableProfitDto>(v.Error);
                await _uow.SaveChangesAsync(ct);
                dto.ProvisionVoucherId = v.Voucher.Id;
            }
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
