using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 2: sales. Backlog, pipeline, hit rate, proposals near their deadline and waiting for a decision, orders waiting for credit;
    // pipeline by stage, orders by month, proposal results by month.
    public sealed partial class CeoOverviewService
    {
        private async Task<CeoSectionDto> SalesAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = await BaseAsync(ctx, ct);
            CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(_uow, ct);
            ResponseDto<SlDashboardDto> answer = await _sales.GetAsync(ctx.Today, ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The sales dashboard is not available: " + answer?.Message);
            SlDashboardDto sales = answer.Data;

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.SalesBacklog, "سفارش‌های در جریان (backlog)", backlog.Total, CeoUnit.Rial, null, CeoLinks.SalesOrders, CeoDrillRef.SlOrdersOpen,
                "سفارش‌های فروش تأییدشده و فاکتورنشده: " + CeoText.ShortRial(backlog.Orders) + "؛ قراردادهای در جریان که هنوز فاکتور نشده‌اند: " + CeoText.ShortRial(backlog.Contracts)), higherIsBetter: true));
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.SalesPipelineValue, "ارزش خط لوله‌ی فروش", b.SalesPipelineValue, CeoUnit.Rial, null, CeoLinks.SalesDashboard, CeoDrillRef.SlPipelineByStage,
                CountText(b.SalesOpenInquiries) + " استعلام باز؛ ارزش هر استعلام: پیشنهاد زنده یا برآورد قیمت"), higherIsBetter: true));
            decimal? hitRate = b.SalesHitRatePercent;
            section.Kpis.Add(Kpi("salesHitRate", "نرخ برد پیشنهادها", hitRate, CeoUnit.Percent, hitRate.HasValue ? CeoRules.HitRate(hitRate.Value) : CeoStatus.Neutral, CeoLinks.SalesProposals, CeoDrillRef.SlHitRateByMonth,
                "برده: " + CeoText.Number(sales.Won) + "؛ باخته: " + CeoText.Number(sales.Lost)));
            section.Kpis.Add(Kpi("salesProposalsDueSoon", "پیشنهادهای نزدیک به مهلت ارسال", b.SalesProposalsDueSoon, CeoUnit.Count, CeoRules.ByAny(b.SalesProposalsDueSoon, 3), CeoLinks.SalesInquiries, CeoDrillRef.SlProposalsDue,
                "مهلت تا سه روز آینده (یا گذشته) و هنوز ارسال نشده"));
            section.Kpis.Add(Kpi("salesProposalsWaiting", "پیشنهادهای منتظر تأیید", b.SalesProposalsWaitingApproval, CeoUnit.Count, CeoRules.ByCount(b.SalesProposalsWaitingApproval, 1, 5), CeoLinks.SalesProposals, CeoDrillRef.SlProposalsWaiting,
                "پیش‌نویس‌هایی که تأیید قیمت می‌خواهند؛ زیر قیمت تمام‌شده: " + CeoText.Number(b.SalesBelowCostProposals)));
            section.Kpis.Add(Kpi("salesOrdersWaitingCredit", "سفارش‌های منتظر تأیید اعتبار", b.SalesOrdersWaitingCredit, CeoUnit.Count, CeoRules.ByCount(b.SalesOrdersWaitingCredit, 1, 5), CeoLinks.SalesOrders, CeoDrillRef.SlOrdersWaitingCredit,
                "مشتری از سقف اعتبار گذشته یا سقفی ندارد"));

            // chart 1: the open inquiries by stage (value in rial)
            section.Charts.Add(Chart("pipelineByStage", "خط لوله‌ی فروش به تفکیک مرحله", CeoKind.HBar, CeoUnit.Rial, sales.PipelineByStage.Select(s => s.Name), CeoLinks.SalesDashboard, CeoDrillRef.SlPipelineByStage,
                "ارزش استعلام‌های باز؛ تعداد: " + string.Join("، ", sales.PipelineByStage.Select(s => s.Name + " " + CeoText.Number(s.Count))),
                Series("ارزش", sales.PipelineByStage.Select(s => s.Value))));

            // chart 2: the value of the orders confirmed (and later) by order month
            var orders = await _uow.Set<SlSalesOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.OrderDate >= ctx.From && o.OrderDate < ctx.End && o.Status != SlOrderStatus.Draft && o.Status != SlOrderStatus.Cancelled)
                .Select(o => new { o.OrderDate, o.NetAmount }).ToListAsync(ct);
            section.Charts.Add(Chart("ordersByMonth", "ارزش سفارش‌های فروش به تفکیک ماه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.SalesOrders, CeoDrillRef.SlOrdersOpen,
                "سفارش‌های تأییدشده تا بسته‌شده، پیش از مالیات.",
                Series("سفارش‌ها", CeoTrends.SumByMonth(ctx.Window, orders.Select(o => (o.OrderDate, o.NetAmount))))));

            // chart 3: the proposals that ended in a win or a loss, by month of the result
            DateTimeOffset from = new DateTimeOffset(ctx.From);
            var results = await _uow.Set<SlProposal>().AsNoTracking()
                .Where(p => !p.Deleted && (p.Status == SlProposalStatus.Won || p.Status == SlProposalStatus.Lost) && p.ResultAt != null && p.ResultAt >= from)
                .Select(p => new { p.Status, p.ResultAt }).ToListAsync(ct);
            IEnumerable<DateTime> Days(SlProposalStatus status) => results.Where(r => r.Status == status).Select(r => r.ResultAt.Value.LocalDateTime.Date);
            section.Charts.Add(Chart("proposalResults", "نتیجه‌ی پیشنهادها به تفکیک ماه", CeoKind.StackedBar, CeoUnit.Count, ctx.Labels, CeoLinks.SalesProposals, CeoDrillRef.SlHitRateByMonth,
                "ماهِ ثبت نتیجه؛ نرخ برد = برده تقسیم بر مجموع برده و باخته.",
                Series("برده", CountByMonth(ctx, Days(SlProposalStatus.Won)), CeoRole.Positive), Series("باخته", CountByMonth(ctx, Days(SlProposalStatus.Lost)), CeoRole.Negative)));
            return section;
        }
    }
}
