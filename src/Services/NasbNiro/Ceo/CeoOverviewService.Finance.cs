using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 1: finance. Cash, receivables, overdue payables, issued cheques, profit of the year, vouchers waiting; revenue and cost by month, receivables ageing, cash movement by month.
    public sealed partial class CeoOverviewService
    {
        private async Task<CeoSectionDto> FinanceAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = await BaseAsync(ctx, ct);
            CeoFinanceDto f = b.Finance ?? new CeoFinanceDto();
            decimal cash = f.CashAndBank;

            // payables past the payment term (★ 30 days from the invoice date: the procurement module keeps the term only as free text)
            List<CeoOverduePayable> overduePayables = await CeoFigures.OverduePayablesAsync(_uow, ctx.Today, ct);
            decimal payablesOverdue = overduePayables.Sum(p => p.Balance);

            // issued cheques of the next 30 days (the next 7 days come from the dashboard)
            DateTime in30 = ctx.Today.AddDays(30);
            decimal chequesDue30 = await _uow.Set<FnIssuedCheque>().AsNoTracking()
                .Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null && c.DueDate <= in30).SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
            decimal chequesDue7 = f.IssuedChequesDue7Amount;

            int vouchersDraft = await _uow.Set<FnVoucher>().AsNoTracking().CountAsync(v => v.Status == FnVoucherStatus.Draft, ct);
            int vouchersChecked = await _uow.Set<FnVoucher>().AsNoTracking().CountAsync(v => v.Status == FnVoucherStatus.Checked, ct);

            var section = new CeoSectionDto();
            string overdueShare = f.ReceivablesOpen > 0m ? " (" + CeoText.Percent(f.ReceivablesOverdue * 100m / f.ReceivablesOpen, 0) + " از مطالبات باز)" : string.Empty;

            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.Cash, "نقد و بانک", cash, CeoUnit.Rial, CeoRules.Cash(cash), CeoLinks.CashPosition, CeoDrillRef.FinCashAccounts,
                "مانده‌ی صندوق‌ها و حساب‌های بانکی؛ تنخواه‌گردان‌ها جدا هستند."), higherIsBetter: true));
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.ReceivablesOpen, "مطالبات باز", f.ReceivablesOpen, CeoUnit.Rial, CeoRules.ReceivablesOverdue(f.ReceivablesOverdue, f.ReceivablesOpen), CeoLinks.ReceivablesAgeing,
                CeoDrillRef.FinReceivablesByCustomer, "سررسیدگذشته: " + CeoText.ShortRial(f.ReceivablesOverdue) + overdueShare), higherIsBetter: false));
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.PayablesOverdue, "بدهی سررسیدگذشته به تأمین‌کنندگان", payablesOverdue, CeoUnit.Rial, CeoRules.PayablesOverdue(payablesOverdue, cash), CeoLinks.ProcurementInvoices,
                CeoDrillRef.FinPayablesOverdue, CountText(overduePayables.Count) + " فاکتور تأییدشده‌ی پرداخت‌نشده که بیش از " + CeoText.Number(CeoFigures.PayableTermDays) + " روز از تاریخ فاکتور گذشته است."), higherIsBetter: false));
            section.Kpis.Add(Kpi("issuedChequesDue7", "چک‌های صادره با سررسید هفت روز آینده", chequesDue7, CeoUnit.Rial, CeoRules.IssuedCheques(chequesDue7, chequesDue30, cash), CeoLinks.Cheques,
                CeoDrillRef.FinChequesDue, CountText(f.IssuedChequesDue7Count) + "؛ تا سی روز آینده: " + CeoText.ShortRial(chequesDue30)));
            section.Kpis.Add(Kpi("netProfitYtd", "سود خالص از ابتدای سال مالی", f.NetProfitYtd, CeoUnit.Rial, CeoRules.Profit(f.NetProfitYtd), CeoLinks.IncomeStatement,
                CeoDrillRef.FinRevenueByMonth, "درآمد منهای هزینه‌ی اسناد قطعی؛ اسناد بستن حساب‌ها حساب نمی‌شوند."));
            section.Kpis.Add(Kpi("vouchersWaiting", "اسناد حسابداری منتظر بررسی یا قطعی‌شدن", vouchersDraft + vouchersChecked, CeoUnit.Count, CeoRules.ByCount(vouchersDraft + vouchersChecked, 10, 30), CeoLinks.Vouchers,
                CeoDrillRef.FinVouchersWaiting, "پیش‌نویس: " + CeoText.Number(vouchersDraft) + "؛ بررسی‌شده و منتظر قطعی: " + CeoText.Number(vouchersChecked)));

            // chart 1: revenue and cost of the final vouchers by month
            var voucherRows = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.Date >= ctx.From && l.Voucher.Date < ctx.End && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose &&
                            l.Voucher.Status == FnVoucherStatus.Final && l.Voucher.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId &&
                            (l.Account.Type == FnAccountType.Revenue || l.Account.Type == FnAccountType.Expense))
                .GroupBy(l => new { l.Voucher.Date, l.Account.Type })
                .Select(g => new { g.Key.Date, g.Key.Type, Net = g.Sum(l => l.Credit - l.Debit) }).ToListAsync(ct);
            decimal[] revenue = CeoTrends.SumByMonth(ctx.Window, voucherRows.Where(r => r.Type == FnAccountType.Revenue).Select(r => (r.Date, r.Net)));
            decimal[] cost = CeoTrends.SumByMonth(ctx.Window, voucherRows.Where(r => r.Type == FnAccountType.Expense).Select(r => (r.Date, -r.Net)));
            section.Charts.Add(Chart("revenueCost", "درآمد و هزینه‌ی ماهانه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.IncomeStatement, CeoDrillRef.FinRevenueByMonth,
                "بر پایه‌ی اسناد قطعی دفتر کل؛ دوازده ماه شمسی تا امروز.",
                Series("درآمد", revenue, CeoRole.Positive), Series("هزینه", cost, CeoRole.Negative)));

            // chart 2: receivables ageing as the receivables module reports it
            ResponseDto<AgeingDto> ageing = await _receivables.GetAgeingAsync(ctx.Today, ct);
            if (ageing == null || !ageing.Success || ageing.Data == null) throw new InvalidOperationException("Receivables ageing is not available: " + ageing?.Message);
            AgeingDto a = ageing.Data;
            section.Charts.Add(Chart("receivablesAgeing", "سن مطالبات", CeoKind.Donut, CeoUnit.Rial,
                new[] { "جاری تا ۳۰ روز", "۳۱ تا ۶۰ روز", "۶۱ تا ۹۰ روز", "بیش از ۹۰ روز" }, CeoLinks.ReceivablesAgeing, CeoDrillRef.FinReceivableInvoicesOverdue,
                "از روی سررسید فاکتور (یا تاریخ فاکتور)؛ جمع: " + CeoText.ShortRial(a.Total),
                Series("مطالبات", new[] { a.Current, a.Days31To60, a.Days61To90, a.Over90 })));

            // chart 3: money into and out of the cash boxes and bank accounts by month
            var flows = await _uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.Event.Status != AccountingEventStatus.NotRequired && (l.PartyType == "CashBox" || l.PartyType == "BankAccount") && l.Event.EventDate >= ctx.From && l.Event.EventDate < ctx.End)
                .GroupBy(l => l.Event.EventDate.Date).Select(g => new { Date = g.Key, In = g.Sum(l => l.Debit), Out = g.Sum(l => l.Credit) }).ToListAsync(ct);
            section.Charts.Add(Chart("cashFlow", "ورودی و خروجی نقد و بانک", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.CashPosition, CeoDrillRef.FinCashAccounts,
                "از روی همان رویدادهای حسابداری که مانده‌ی نقد و بانک را می‌سازند.",
                Series("ورودی", CeoTrends.SumByMonth(ctx.Window, flows.Select(r => (r.Date, r.In))), CeoRole.Positive),
                Series("خروجی", CeoTrends.SumByMonth(ctx.Window, flows.Select(r => (r.Date, r.Out))), CeoRole.Negative)));
            return section;
        }
    }
}
