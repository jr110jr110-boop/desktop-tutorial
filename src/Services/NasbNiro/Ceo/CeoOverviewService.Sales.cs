using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
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
            CeoDashboardParts parts = await PartsAsync(ctx, ct);
            CeoDashboardDto b = parts.Dashboard;
            // the pipeline by stage and the proposals won and lost were read for the CEO dashboard already: the same lists, not a second pass over the sales module
            SlCeoCountsDto sales = parts.Sales ?? throw new InvalidOperationException("The sales numbers of the CEO dashboard are not available.");
            CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(_uow, ct);

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.SalesBacklog, backlog.Total, null,
                "سفارش‌های فروش تأییدشده و فاکتورنشده: " + CeoText.ShortRial(backlog.Orders) + "؛ قراردادهای در جریان که هنوز فاکتور نشده‌اند: " + CeoText.ShortRial(backlog.Contracts) +
                "؛ فهرست جزئیات فقط سفارش‌های فروش را نشان می‌دهد و قراردادها در رقم هستند ولی در فهرست نیستند."), higherIsBetter: true));
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.SalesPipelineValue, b.SalesPipelineValue, null,
                CountText(b.SalesOpenInquiries) + " استعلام باز؛ ارزش هر استعلام: پیشنهاد زنده یا برآورد قیمت"), higherIsBetter: true));
            decimal? hitRate = b.SalesHitRatePercent;
            section.Kpis.Add(Kpi(ctx, "salesHitRate", hitRate, hitRate.HasValue ? CeoRules.HitRate(hitRate.Value) : CeoStatus.Neutral,
                "برنده: " + CeoText.Number(sales.Won) + "؛ بازنده: " + CeoText.Number(sales.Lost)));
            section.Kpis.Add(Kpi(ctx, "salesProposalsDueSoon", b.SalesProposalsDueSoon, CeoRules.ByAny(b.SalesProposalsDueSoon, 3),
                "مهلت تا سه روز آینده (یا گذشته) و هنوز ارسال نشده"));
            section.Kpis.Add(Kpi(ctx, "salesProposalsWaiting", b.SalesProposalsWaitingApproval, CeoRules.ByCount(b.SalesProposalsWaitingApproval, 1, 5),
                "پیش‌نویس‌هایی که تأیید قیمت می‌خواهند؛ زیر قیمت تمام‌شده: " + CeoText.Number(b.SalesBelowCostProposals)));
            section.Kpis.Add(Kpi(ctx, "salesOrdersWaitingCredit", b.SalesOrdersWaitingCredit, CeoRules.ByCount(b.SalesOrdersWaitingCredit, 1, 5),
                "مشتری از سقف اعتبار گذشته یا سقفی ندارد"));

            // chart 1: the open inquiries by stage (value in rial)
            List<SlStageValueDto> stages = sales.PipelineByStage ?? new List<SlStageValueDto>();
            section.Charts.Add(Chart("pipelineByStage", "خط لوله‌ی فروش به تفکیک مرحله", CeoKind.HBar, CeoUnit.Rial, stages.Select(s => s.Name), CeoLinks.SalesDashboard, CeoDrillRef.SlPipelineByStage,
                "ارزش استعلام‌های باز؛ تعداد: " + string.Join("، ", stages.Select(s => s.Name + " " + CeoText.Number(s.Count))),
                Series("ارزش", stages.Select(s => s.Value))));

            // chart 2: the value of the orders confirmed (and later) by order month. The drill behind it lists the open orders and has no month filter, so it carries no period.
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
            section.Charts.Add(WithDrillParams(Chart("proposalResults", "نتیجه‌ی پیشنهادها به تفکیک ماه", CeoKind.StackedBar, CeoUnit.Count, ctx.Labels, CeoLinks.SalesProposals, CeoDrillRef.SlHitRateByMonth,
                "ماهِ ثبت نتیجه؛ نرخ برد = برنده تقسیم بر مجموع برنده و بازنده.",
                Series("برنده", CountByMonth(ctx, Days(SlProposalStatus.Won)), CeoRole.Positive), Series("بازنده", CountByMonth(ctx, Days(SlProposalStatus.Lost)), CeoRole.Negative)), ctx.LatestMonth()));
            return section;
        }
    }
}
