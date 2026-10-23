using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Finance
{
    public interface IFinanceDashboardService
    {
        Task<ResponseDto<FinanceDashboardDto>> GetAsync(CancellationToken cancellationToken);

        /// <summary>The figures the CEO dashboard shows from the ledger (no role check: the caller is the CEO dashboard).</summary>
        Task<CeoFinanceDto> GetCeoFinanceAsync(CancellationToken cancellationToken);
    }

    /// <summary>Read-only finance figures. Everything is computed from the ledger and treasury records.</summary>
    public class FinanceDashboardService : IFinanceDashboardService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IGlService _gl;

        public FinanceDashboardService(IUnitOfWork uow, IInventoryUserContext user, IGlService gl)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        }

        public async Task<ResponseDto<FinanceDashboardDto>> GetAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return ResponseDtoHelper.BadRequest<FinanceDashboardDto>("شما مجوز این کار را ندارید");
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime today = DateTime.Today;
            var d = new FinanceDashboardDto { GeneratedAt = DateTimeOffset.Now };

            IQueryable<AccountingEventLine> live = _uow.Set<AccountingEventLine>().AsNoTracking().Where(l => l.Event.Status != AccountingEventStatus.NotRequired);
            d.CashTotal = await live.Where(l => l.PartyType == "CashBox").SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            d.BankTotal = await live.Where(l => l.PartyType == "BankAccount").SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            d.PettyCashTotal = await _uow.Set<FnPettyCash>().AsNoTracking().Where(p => p.IsActive).SumAsync(p => (decimal?)p.Balance, ct) ?? 0;

            var due3 = _uow.Set<FnReceivedCheque>().AsNoTracking().Where(c => c.Status == FnReceivedChequeStatus.InBox && c.DueDate <= today.AddDays(3));
            d.ReceivedChequesDue3Days = await due3.CountAsync(ct);
            d.ReceivedChequesDue3DaysAmount = await due3.SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
            var due7 = _uow.Set<FnIssuedCheque>().AsNoTracking().Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null && c.DueDate <= today.AddDays(7));
            d.IssuedChequesDue7Days = await due7.CountAsync(ct);
            d.IssuedChequesDue7DaysAmount = await due7.SumAsync(c => (decimal?)c.Amount, ct) ?? 0;

            d.VouchersWaitingCheck = await _uow.Set<FnVoucher>().CountAsync(v => v.Status == FnVoucherStatus.Draft, ct);
            d.VouchersWaitingFinalize = await _uow.Set<FnVoucher>().CountAsync(v => v.Status == FnVoucherStatus.Checked, ct);
            d.UnpostedEvents = await _uow.Set<AccountingEvent>().CountAsync(e => e.Status == AccountingEventStatus.Pending || e.Status == AccountingEventStatus.Exported ||
                                                                              e.Status == AccountingEventStatus.Failed || e.Status == AccountingEventStatus.Sent, ct);
            d.OpenPeriods = await _uow.Set<FnPeriod>().CountAsync(p => p.Status == FnPeriodStatus.Open && p.FiscalYear.Status == FnYearStatus.Open && p.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId, ct);
            var openRecs = await _uow.Set<FnBankReconciliation>().AsNoTracking().Where(r => r.Status == 0).Select(r => r.Difference).ToListAsync(ct);
            d.UnapprovedReconciliations = openRecs.Count;
            d.ReconciliationDifference = openRecs.Sum(x => Math.Abs(x));

            (d.ReceivablesOpen, d.ReceivablesOverdue) = await Receivables(today, ct);

            d.PayablesOpen = await live.Where(l => l.AccountRole == AccountRoles.AccountsPayable).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            d.PayablesDue30 = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Approved).SumAsync(p => (decimal?)(p.Amount - p.Deductions), ct) ?? 0;

            int year = FinanceSeed.PersianYearOf(today);
            DateTime yearStart = FinanceSeed.YearStart(year);
            d.VatPayableEstimate = await VatEstimateAsync(yearStart, today, ct);
            d.NetProfitYtd = await NetProfitAsync(yearStart, today, ct);
            SetupCheckDto setup = (await _gl.SetupCheckAsync(ct)).Data;
            d.SetupProblems = setup?.Problems.Count ?? 0;

            if (d.ReceivedChequesDue3Days > 0) d.Alerts.Add($"{d.ReceivedChequesDue3Days} چک دریافتی تا ۳ روز دیگر سررسید می‌شود.");
            if (d.IssuedChequesDue7DaysAmount > d.BankTotal) d.Alerts.Add("چک‌های صادره ۷ روز آینده از موجودی بانک‌ها بیشتر است.");
            if (d.UnpostedEvents > 0) d.Alerts.Add($"{d.UnpostedEvents} رویداد حسابداری هنوز سند نشده است.");
            if (d.VouchersWaitingFinalize > 0) d.Alerts.Add($"{d.VouchersWaitingFinalize} سند منتظر قطعی شدن است.");
            if (d.UnapprovedReconciliations > 0) d.Alerts.Add($"{d.UnapprovedReconciliations} مغایرت‌گیری بانکی تایید نشده است.");
            if (d.ReceivablesOverdue > 0) d.Alerts.Add($"مطالبات سررسیدگذشته: {d.ReceivablesOverdue:0} ریال.");
            if (d.SetupProblems > 0) d.Alerts.Add($"{d.SetupProblems} مورد تنظیمات مالی ناقص است.");
            return ResponseDtoHelper.Ok(d);
        }

        private async Task<(decimal Open, decimal Overdue)> Receivables(DateTime today, CancellationToken ct)
        {
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote)
                .Select(i => new { i.Id, i.Date, i.DueDate }).ToListAsync(ct);
            Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, invoices.Select(i => i.Id).ToList(), null, ct);
            decimal total = 0, overdue = 0;
            foreach (var i in invoices)
            {
                decimal amount = open.TryGetValue(i.Id, out decimal o) ? o : 0;
                if (amount <= 0) continue;
                total += amount;
                if ((i.DueDate ?? i.Date).Date < today) overdue += amount;
            }
            return (total, overdue);
        }

        /// <summary>Sales VAT minus purchase VAT of the events from a date: what would be payable now.</summary>
        private async Task<decimal> VatEstimateAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            var lines = _uow.Set<AccountingEventLine>().AsNoTracking().Where(l => l.Event.Status != AccountingEventStatus.NotRequired && l.Event.EventDate >= from && l.Event.EventDate <= to);
            decimal sales = await lines.Where(l => l.AccountRole == AccountRoles.VatSales).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            decimal purchases = await lines.Where(l => l.AccountRole == AccountRoles.Vat).SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            return sales - purchases;
        }

        /// <summary>Revenue minus expense of the ledger vouchers of the period (closing vouchers are left out).</summary>
        private async Task<decimal> NetProfitAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            return await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.Date >= from && l.Voucher.Date <= to && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose &&
                            l.Voucher.Status == FnVoucherStatus.Final && l.Voucher.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId &&
                            (l.Account.Type == FnAccountType.Revenue || l.Account.Type == FnAccountType.Expense))
                .SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
        }

        public async Task<CeoFinanceDto> GetCeoFinanceAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime today = DateTime.Today;
            var dto = new CeoFinanceDto();
            IQueryable<AccountingEventLine> live = _uow.Set<AccountingEventLine>().AsNoTracking().Where(l => l.Event.Status != AccountingEventStatus.NotRequired);
            dto.CashAndBank = await live.Where(l => l.PartyType == "CashBox" || l.PartyType == "BankAccount").SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            (dto.ReceivablesOpen, dto.ReceivablesOverdue) = await Receivables(today, ct);
            dto.PayablesDue30 = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Approved).SumAsync(p => (decimal?)(p.Amount - p.Deductions), ct) ?? 0;
            var due7 = _uow.Set<FnIssuedCheque>().AsNoTracking().Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null && c.DueDate <= today.AddDays(7));
            dto.IssuedChequesDue7Count = await due7.CountAsync(ct);
            dto.IssuedChequesDue7Amount = await due7.SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
            dto.NetProfitYtd = await NetProfitAsync(FinanceSeed.YearStart(FinanceSeed.PersianYearOf(today)), today, ct);
            dto.FinanceSetupProblems = (await _gl.SetupCheckAsync(ct)).Data?.Problems.Count ?? 0;

            // release F2: counters only (no person-level data): Sepidar packages, fixed assets, depreciation and payroll in progress
            await F2Seed.EnsureAsync(_uow, ct);
            OutboxSummaryDto outbox = await SepidarOutboxService.CountersAsync(_uow, ct);
            dto.SepidarPendingPackages = outbox.Pending + outbox.ReadyToSend;
            dto.SepidarFailedPackages = outbox.Failed;
            dto.SepidarLastSentAt = outbox.LastSentAt;
            var assets = await _uow.Set<FnAsset>().AsNoTracking().Where(a => a.Status != FnAssetStatus.Draft && a.Status != FnAssetStatus.Disposed)
                .GroupBy(a => 1).Select(g => new { Count = g.Count(), Cost = g.Sum(a => a.Cost), Accumulated = g.Sum(a => a.AccumulatedDepreciation) }).FirstOrDefaultAsync(ct);
            dto.AssetsCount = assets?.Count ?? 0;
            dto.AssetsNetBookValue = assets == null ? 0 : assets.Cost - assets.Accumulated;
            dto.DepreciationRunsWaiting = await _uow.Set<FnDepreciationRun>().AsNoTracking().CountAsync(r => r.Status == FnRunStatus.Draft, ct);
            dto.PayrollRunsInProgress = await _uow.Set<FnPayrollRun>().AsNoTracking().CountAsync(r => r.Status < FnPayrollStatus.Paid, ct);

            // gross margin per contract: invoiced net (credit notes deducted) against the cost entries
            var revenue = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.ContractId != null && i.Type != FnInvoiceType.Advance && i.Type != FnInvoiceType.RetentionRelease)
                .GroupBy(i => i.ContractId.Value).Select(g => new { g.Key, Net = g.Sum(i => i.Type == FnInvoiceType.CreditNote ? -i.NetAmount : i.NetAmount) }).ToListAsync(ct);
            List<int> ids = revenue.Select(r => r.Key).ToList();
            var costs = await _uow.Set<CostEntry>().AsNoTracking().Where(c => c.ContractId != null && ids.Contains(c.ContractId.Value))
                .GroupBy(c => c.ContractId.Value).Select(g => new { g.Key, Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
            Dictionary<int, string> codes = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            foreach (var r in revenue.OrderByDescending(r => r.Net).Take(10))
            {
                decimal cost = costs.FirstOrDefault(c => c.Key == r.Key)?.Amount ?? 0;
                dto.GrossMargins.Add(new GrossMarginRowDto
                {
                    ContractId = r.Key, ContractCode = codes.TryGetValue(r.Key, out string code) ? code : null, Revenue = r.Net, Cost = cost,
                    MarginPercent = r.Net > 0 ? Math.Round(100m * (r.Net - cost) / r.Net, 1) : 0,
                });
            }

            void Alert(bool when, string level, string text, string link) { if (when) dto.Alerts.Add(new DashboardAlertDto { Level = level, Text = text, Link = link }); }
            foreach (GrossMarginRowDto m in dto.GrossMargins.Where(m => m.Revenue > 0 && m.MarginPercent < 0))
                Alert(true, "danger", $"قرارداد {m.ContractCode}: هزینه از درآمد فاکتورشده بیشتر است", "/finance/dashboard");
            Alert(dto.IssuedChequesDue7Amount > dto.CashAndBank, "danger", "چک‌های صادره ۷ روز آینده از نقد و بانک بیشتر است", "/finance/treasury/cash-position");
            Alert(dto.ReceivablesOverdue > 0, "warning", $"مطالبات سررسیدگذشته {dto.ReceivablesOverdue:0} ریال", "/finance/receivables/ageing");
            Alert(dto.FinanceSetupProblems > 0, "warning", $"{dto.FinanceSetupProblems} مورد تنظیمات مالی ناقص است", "/finance/gl/setup-check");
            return dto;
        }
    }
}
