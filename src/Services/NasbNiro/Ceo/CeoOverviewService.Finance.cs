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
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;
            CeoFinanceDto f = b.Finance ?? new CeoFinanceDto();
            decimal cash = f.CashAndBank;

            // payables past the payment term: the payables ageing of the general ledger (the one behind the notes of the financial statements), the buckets older than 30 days
            List<CeoOverduePayable> overduePayables = await CeoFigures.OverduePayablesAsync(_payables, ctx.Today, ct);
            decimal payablesOverdue = overduePayables.Sum(p => p.Overdue);

            // issued cheques of the next 30 days (the next 7 days come from the dashboard)
            DateTime in30 = ctx.Today.AddDays(30);
            decimal chequesDue30 = await _uow.Set<FnIssuedCheque>().AsNoTracking()
                .Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null && c.DueDate <= in30).SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
            decimal chequesDue7 = f.IssuedChequesDue7Amount;

            int vouchersDraft = await _uow.Set<FnVoucher>().AsNoTracking().CountAsync(v => v.Status == FnVoucherStatus.Draft, ct);
            int vouchersChecked = await _uow.Set<FnVoucher>().AsNoTracking().CountAsync(v => v.Status == FnVoucherStatus.Checked, ct);

            var section = new CeoSectionDto();
            string overdueShare = f.ReceivablesOpen > 0m ? " (" + CeoText.Percent(f.ReceivablesOverdue * 100m / f.ReceivablesOpen, 0) + " از مطالبات باز)" : string.Empty;

            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.Cash, cash, CeoRules.Cash(cash),
                "مانده‌ی صندوق‌ها و حساب‌های بانکی؛ تنخواه‌گردان‌ها جدا هستند."), higherIsBetter: true));
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.ReceivablesOpen, f.ReceivablesOpen, CeoRules.ReceivablesOverdue(f.ReceivablesOverdue, f.ReceivablesOpen),
                "سررسیدگذشته: " + CeoText.ShortRial(f.ReceivablesOverdue) + overdueShare), higherIsBetter: false));
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.PayablesOverdue, payablesOverdue, CeoRules.PayablesOverdue(payablesOverdue, cash), PayablesNote(overduePayables)), higherIsBetter: false));
            section.Kpis.Add(Kpi(ctx, "issuedChequesDue7", chequesDue7, CeoRules.IssuedCheques(chequesDue7, chequesDue30, cash),
                CountText(f.IssuedChequesDue7Count) + "؛ تا سی روز آینده: " + CeoText.ShortRial(chequesDue30)));
            section.Kpis.Add(Kpi(ctx, "netProfitYtd", f.NetProfitYtd, CeoRules.Profit(f.NetProfitYtd),
                "درآمد منهای هزینه‌ی اسناد قطعی؛ اسناد بستن حساب‌ها حساب نمی‌شوند."));
            section.Kpis.Add(Kpi(ctx, "vouchersWaiting", vouchersDraft + vouchersChecked, CeoRules.ByCount(vouchersDraft + vouchersChecked, 10, 30),
                "پیش‌نویس: " + CeoText.Number(vouchersDraft) + "؛ بررسی‌شده و منتظر قطعی: " + CeoText.Number(vouchersChecked)));

            // chart 1: revenue and cost of the final vouchers by month
            var voucherRows = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.Date >= ctx.From && l.Voucher.Date < ctx.End && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose &&
                            l.Voucher.Status == FnVoucherStatus.Final && l.Voucher.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId &&
                            (l.Account.Type == FnAccountType.Revenue || l.Account.Type == FnAccountType.Expense))
                .GroupBy(l => new { l.Voucher.Date, l.Account.Type })
                .Select(g => new { g.Key.Date, g.Key.Type, Net = g.Sum(l => l.Credit - l.Debit) }).ToListAsync(ct);
            decimal[] revenue = CeoTrends.SumByMonth(ctx.Window, voucherRows.Where(r => r.Type == FnAccountType.Revenue).Select(r => (r.Date, r.Net)));
            decimal[] cost = CeoTrends.SumByMonth(ctx.Window, voucherRows.Where(r => r.Type == FnAccountType.Expense).Select(r => (r.Date, -r.Net)));
            section.Charts.Add(WithDrillParams(Chart("revenueCost", "درآمد و هزینه‌ی ماهانه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.IncomeStatement, CeoDrillRef.FinRevenueByMonth,
                "بر پایه‌ی اسناد قطعی دفتر کل؛ دوازده ماه شمسی تا امروز.",
                Series("درآمد", revenue, CeoRole.Positive), Series("هزینه", cost, CeoRole.Negative)), ctx.LatestMonth()));

            // chart 2: receivables ageing as the receivables module reports it (as of today)
            ResponseDto<AgeingDto> ageing = await _receivables.GetAgeingAsync(ctx.Today, ct);
            if (ageing == null || !ageing.Success || ageing.Data == null) throw new InvalidOperationException("Receivables ageing is not available: " + ageing?.Message);
            AgeingDto a = ageing.Data;
            section.Charts.Add(Chart("receivablesAgeing", "سن مطالبات", CeoKind.Donut, CeoUnit.Rial,
                new[] { "جاری تا ۳۰ روز", "۳۱ تا ۶۰ روز", "۶۱ تا ۹۰ روز", "بیش از ۹۰ روز" }, CeoLinks.ReceivablesAgeing, CeoDrillRef.FinReceivableInvoicesOverdue,
                AgeingNote(a.Total, f.ReceivablesOpen),
                Series("مطالبات", new[] { a.Current, a.Days31To60, a.Days61To90, a.Over90 })));

            // chart 3: money into and out of the cash boxes and bank accounts by month, without the transfers between them
            var events = await _uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.Event.Status != AccountingEventStatus.NotRequired && (l.PartyType == "CashBox" || l.PartyType == "BankAccount") && l.Event.EventDate >= ctx.From && l.Event.EventDate < ctx.End)
                .GroupBy(l => new { l.EventId, l.Event.EventDate })
                .Select(g => new { g.Key.EventDate, In = g.Sum(l => l.Debit), Out = g.Sum(l => l.Credit) }).ToListAsync(ct);
            // an event that takes money out of one cash box or bank account and puts it into another is neither an inflow nor an outflow of the company: the part of the event that stays inside is taken off both sides
            var flows = events.Select(e => (Day: e.EventDate.Date, In: e.In - Math.Min(e.In, e.Out), Out: e.Out - Math.Min(e.In, e.Out))).ToList();
            section.Charts.Add(Chart("cashFlow", "ورودی و خروجی نقد و بانک", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.CashPosition, CeoDrillRef.FinCashAccounts,
                "از روی همان رویدادهای حسابداری که مانده‌ی نقد و بانک را می‌سازند؛ انتقال میان صندوق‌ها و حساب‌های بانکی نه ورودی است و نه خروجی و در این نمودار نیست.",
                Series("ورودی", CeoTrends.SumByMonth(ctx.Window, flows.Select(r => (r.Day, r.In))), CeoRole.Positive),
                Series("خروجی", CeoTrends.SumByMonth(ctx.Window, flows.Select(r => (r.Day, r.Out))), CeoRole.Negative)));
            return section;
        }

        /// <summary>
        /// The note of the overdue payables: how many suppliers, where the figure comes from, and what is not in it. A payment in advance or a progress payment made without an invoice
        /// stays in the supplier advance account of the ledger, so it does not reduce the debt shown here until it is recovered against an invoice.
        /// </summary>
        private static string PayablesNote(List<CeoOverduePayable> overdue) =>
            CeoText.Number(overdue.Count) + " تأمین‌کننده با بدهی بیش از " + CeoText.Number(CeoFigures.PayableTermDays) + " روز از تاریخ سند؛ بر پایه‌ی سن بدهی دفتر کل (همان جدول یادداشت‌های صورت‌های مالی) و پرداخت‌ها از قدیمی‌ترین سند کم می‌شوند. " +
            "پیش‌پرداخت و پرداخت علی‌الحساب بدون فاکتور در حساب پیش‌پرداخت تأمین‌کنندگان می‌ماند و تا زمانی که با فاکتور تسویه نشود از این بدهی کم نمی‌شود.";

        /// <summary>
        /// The note of the ageing donut. The donut is the as-of-today ageing of the receivables module, the "open receivables" figure is the finance dashboard's, which has no date filter:
        /// an invoice or a receipt dated after today makes them differ, and the note says by how much.
        /// </summary>
        private static string AgeingNote(decimal ageingTotal, decimal open)
        {
            string note = "از روی سررسید فاکتور (یا تاریخ فاکتور)؛ جمع: " + CeoText.ShortRial(ageingTotal);
            decimal difference = Math.Abs(open - ageingTotal);
            if (difference >= 1m)
                note += "؛ با مطالبات باز (" + CeoText.ShortRial(open) + ") " + CeoText.ShortRial(difference) + " اختلاف دارد، چون فاکتور یا دریافت با تاریخ بعد از امروز در سن مطالبات امروز حساب نمی‌شود.";
            return note;
        }
    }
}
