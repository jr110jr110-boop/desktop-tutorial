using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Procurement;
using Services.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProductionDtos;
using ViewModels.NasbNiro.QualityDtos;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Finance
{
    public interface ICeoDashboardService
    {
        /// <param name="productionAsOf">The day the production figures (overdue maintenance, the next four weeks of load) are counted for; the server date when empty (tests and what-if views).</param>
        Task<ResponseDto<CeoDashboardDto>> GetAsync(CancellationToken cancellationToken, DateTime? productionAsOf = null);

        /// <summary>
        /// The dashboard of today together with the module results it was made from (the quality dashboard, the production load, the sales pipeline and results), computed once:
        /// the CEO overview draws its sections from the same results instead of asking the modules a second time.
        /// </summary>
        Task<CeoDashboardParts> GetPartsAsync(CancellationToken cancellationToken);
    }

    /// <summary>What the CEO dashboard computed on its way, for the CEO overview to reuse. Read only and never serialised.</summary>
    public sealed class CeoDashboardParts
    {
        public CeoDashboardDto Dashboard { get; set; }

        /// <summary>The quality dashboard; the counts the CEO dashboard shows are the base part of it.</summary>
        public QualityDashboardDto Quality { get; set; }

        /// <summary>The load of the next four weeks (110 percent threshold) the overload count was made from.</summary>
        public LoadReportDto ProductionLoad { get; set; }

        /// <summary>The sales numbers of the CEO dashboard with the pipeline by stage and the proposals won and lost.</summary>
        public SlCeoCountsDto Sales { get; set; }
    }

    /// <summary>
    /// Read-only figures for the CEO dashboard. Every figure comes from the PMIS records themselves
    /// (stock ledger, cost entries, accounting events, procurement), so it is the same number Sepidar receives.
    /// </summary>
    public class CeoDashboardService : ICeoDashboardService
    {
        private readonly IUnitOfWork _uow;

        private readonly Quality.IQualityDashboardService _quality;
        private readonly ProjectControl.IPcReportService _projectControl;
        private readonly IFinanceDashboardService _finance;
        private readonly IInventoryUserContext _user;

        public CeoDashboardService(IUnitOfWork uow, Quality.IQualityDashboardService quality, ProjectControl.IPcReportService projectControl,
            IFinanceDashboardService finance, IInventoryUserContext user)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _finance = finance ?? throw new ArgumentNullException(nameof(finance));
            _projectControl = projectControl ?? throw new ArgumentNullException(nameof(projectControl));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _quality = quality ?? throw new ArgumentNullException(nameof(quality));
        }

        public async Task<ResponseDto<CeoDashboardDto>> GetAsync(CancellationToken ct, DateTime? productionAsOf = null)
            => ResponseDtoHelper.Ok((await BuildAsync(ct, productionAsOf, false)).Dashboard);

        public Task<CeoDashboardParts> GetPartsAsync(CancellationToken ct) => BuildAsync(ct, null, true);

        /// <param name="full">True reads the whole quality dashboard (the counts are its base part) so that the overview does not read the quality module twice.</param>
        private async Task<CeoDashboardParts> BuildAsync(CancellationToken ct, DateTime? productionAsOf, bool full)
        {
            DateTime today = DateTime.Today;
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            var d = new CeoDashboardDto { GeneratedAt = DateTimeOffset.Now };

            // ---------------------------------------------------------------- stock
            d.StockValue = await _uow.Set<ItemCost>().SumAsync(c => (decimal?)c.Value, ct) ?? 0;
            var minimums = await _uow.Set<InventoryItem>().Where(i => !i.Deleted && i.IsActive && i.MinStock > 0)
                .Select(i => new { i.Id, i.MinStock }).ToListAsync(ct);
            List<int> minIds = minimums.Select(m => m.Id).ToList();
            var onHand = await _uow.Set<StockBalance>().Where(b => minIds.Contains(b.ItemId) && b.Warehouse.Kind != WarehouseKind.Scrap)
                .GroupBy(b => b.ItemId).Select(g => new { g.Key, Quantity = g.Sum(b => b.Quantity) }).ToListAsync(ct);
            d.ItemsBelowMinimum = minimums.Count(m => (onHand.FirstOrDefault(o => o.Key == m.Id)?.Quantity ?? 0) < m.MinStock);
            d.QuarantineLines = await _uow.Set<StockBalance>().CountAsync(b => b.Quantity > 0 && b.Warehouse.Kind == WarehouseKind.Quarantine, ct);

            // ---------------------------------------------------------------- procurement
            d.OpenPurchaseRequests = await _uow.Set<PurchaseRequest>().CountAsync(r => !r.Deleted &&
                (r.Status == PurchaseRequestStatus.Submitted || r.Status == PurchaseRequestStatus.Approved), ct);
            d.PurchaseRequestsWaitingApproval = await _uow.Set<PurchaseRequest>().CountAsync(r => !r.Deleted && r.Status == PurchaseRequestStatus.Submitted, ct);
            IQueryable<PurchaseOrder> openOrders = _uow.Set<PurchaseOrder>().Where(o => !o.Deleted &&
                (o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived));
            d.OpenPurchaseOrders = await openOrders.CountAsync(ct);
            d.OpenPurchaseOrdersAmount = await openOrders.SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0;
            d.LateOrderLines = await _uow.Set<PurchaseOrderLine>().CountAsync(l => !l.PurchaseOrder.Deleted &&
                (l.PurchaseOrder.Status == PurchaseOrderStatus.Released || l.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived) &&
                l.ReceivedQuantity < l.Quantity && (l.DeliveryDate ?? l.PurchaseOrder.DeliveryDate) < today, ct);

            IQueryable<AccountingEventLine> liveLines = _uow.Set<AccountingEventLine>().Where(l => l.Event.Status != AccountingEventStatus.NotRequired);
            d.ReceivedNotInvoiced = -(await liveLines.Where(l => l.AccountRole == AccountRoles.Grni).SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0);

            d.InvoicesWaitingApproval = await _uow.Set<SupplierInvoice>().CountAsync(i => !i.Deleted &&
                (i.Status == SupplierInvoiceStatus.Matched || i.Status == SupplierInvoiceStatus.NeedsApproval), ct);
            decimal approvedInvoices = await _uow.Set<SupplierInvoice>().Where(i => !i.Deleted && i.Status == SupplierInvoiceStatus.Approved)
                .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0;
            decimal paidOnInvoices = await _uow.Set<PaymentRequest>().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid && p.InvoiceId.HasValue)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            d.PayablesOpen = approvedInvoices - paidOnInvoices;
            IQueryable<PaymentRequest> waiting = _uow.Set<PaymentRequest>().Where(p => !p.Deleted &&
                (p.Status == PaymentRequestStatus.Draft || p.Status == PaymentRequestStatus.Approved));
            d.PaymentsWaitingApproval = await waiting.CountAsync(ct);
            d.PaymentsWaitingAmount = await waiting.SumAsync(p => (decimal?)(p.Amount - p.Deductions), ct) ?? 0;
            DateTimeOffset monthStartOffset = new DateTimeOffset(monthStart);
            d.PaidThisMonth = await _uow.Set<PaymentRequest>().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid && p.PaidAt >= monthStartOffset)
                .SumAsync(p => (decimal?)(p.Amount - p.Deductions), ct) ?? 0;
            d.BlockedSuppliers = await _uow.Set<SupplierProfile>().CountAsync(p => p.Blocked, ct);
            d.SupplierEvaluationsDue = await _uow.Set<SupplierProfile>().CountAsync(p => !p.Blocked && p.NextEvaluation.HasValue && p.NextEvaluation <= today, ct);

            // ---------------------------------------------------------------- quality and warehouse
            d.PendingInspections = await _uow.Set<QcInspection>().CountAsync(q => !q.Deleted && q.Status == InspectionStatus.Pending, ct);
            // open = neither closed nor cancelled: the same rule as the quality dashboard (its age buckets) and the quality page, so the figure, its alert and the CEO overview agree
            d.OpenNcrs = await _uow.Set<NonConformance>().CountAsync(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled, ct);
            d.OpenMaterialRequests = await _uow.Set<MaterialRequest>().CountAsync(m => !m.Deleted &&
                (m.Status == MaterialRequestStatus.Submitted || m.Status == MaterialRequestStatus.Approved), ct);
            d.DocumentsWaitingApproval = await _uow.Set<InventoryDocument>().CountAsync(x => !x.Deleted && x.Status == InventoryDocumentStatus.Draft &&
                (x.Type == InventoryDocumentType.AdjustmentIncrease || x.Type == InventoryDocumentType.AdjustmentDecrease ||
                 x.Type == InventoryDocumentType.OpeningBalance || x.Type == InventoryDocumentType.ScrapSale), ct);

            // ---------------------------------------------------------------- Sepidar
            d.EventsPending = await _uow.Set<AccountingEvent>().CountAsync(e => e.Status == AccountingEventStatus.Pending, ct);
            d.EventsFailed = await _uow.Set<AccountingEvent>().CountAsync(e => e.Status == AccountingEventStatus.Failed, ct);
            d.LastEventSent = await _uow.Set<AccountingEvent>().Where(e => e.SentAt.HasValue).MaxAsync(e => e.SentAt, ct);

            // ---------------------------------------------------------------- projects
            var costs = await _uow.Set<CostEntry>().Where(c => c.ContractId.HasValue)
                .GroupBy(c => new { ContractId = c.ContractId.Value, c.Kind })
                .Select(g => new { g.Key.ContractId, g.Key.Kind, Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
            var committed = await _uow.Set<PurchaseOrderLine>().Where(l => l.ContractId.HasValue && !l.PurchaseOrder.Deleted &&
                    (l.PurchaseOrder.Status == PurchaseOrderStatus.Approved || l.PurchaseOrder.Status == PurchaseOrderStatus.Released ||
                     l.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived) && l.ReceivedQuantity < l.Quantity)
                .GroupBy(l => l.ContractId.Value).Select(g => new { g.Key, Amount = g.Sum(l => (l.Quantity - l.ReceivedQuantity) * l.UnitPrice) }).ToListAsync(ct);
            List<int> contractIds = costs.Select(c => c.ContractId).Concat(committed.Select(c => c.Key)).Distinct().ToList();
            var contracts = await _uow.Set<ContractEntity>().Where(c => contractIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Code, c.Title, c.Price }).ToListAsync(ct);
            foreach (var c in contracts)
            {
                decimal Sum(params CostKind[] kinds) => costs.Where(x => x.ContractId == c.Id && kinds.Contains(x.Kind)).Sum(x => x.Amount);
                var p = new ProjectCostDto
                {
                    ContractId = c.Id, Code = c.Code, Title = c.Title, ContractPrice = c.Price,
                    MaterialCost = Sum(CostKind.Material),
                    SubcontractCost = Sum(CostKind.Subcontract),
                    OtherCost = Sum(CostKind.Labour, CostKind.Overhead, CostKind.PurchasedService),
                    Committed = Math.Round(committed.FirstOrDefault(x => x.Key == c.Id)?.Amount ?? 0, 0),
                };
                p.ActualCost = p.MaterialCost + p.SubcontractCost + p.OtherCost;
                if (c.Price.HasValue && c.Price.Value > 0) p.CostToPricePercent = Math.Round(100m * (p.ActualCost + p.Committed) / c.Price.Value, 1);
                d.Projects.Add(p);
            }
            d.Projects = d.Projects.OrderByDescending(p => p.CostToPricePercent ?? 0).ThenByDescending(p => p.ActualCost).ToList();

            // ---------------------------------------------------------------- alerts
            void Alert(bool when, string level, string text, string link) { if (when) d.Alerts.Add(new DashboardAlertDto { Level = level, Text = text, Link = link }); }
            foreach (ProjectCostDto p in d.Projects.Where(p => p.CostToPricePercent >= 90))
                Alert(true, p.CostToPricePercent >= 100 ? "danger" : "warning", $"پروژه {p.Code}: هزینه واقعی و تعهدشده {p.CostToPricePercent}٪ مبلغ قرارداد", "/finance/costs");
            Alert(d.EventsFailed > 0, "danger", $"{d.EventsFailed} رویداد حسابداری به سپیدار ارسال نشده (خطا)", "/finance/events");
            Alert(d.LateOrderLines > 0, "warning", $"{d.LateOrderLines} ردیف سفارش خرید از موعد تحویل گذشته است", "/procurement/orders");
            Alert(d.ItemsBelowMinimum > 0, "warning", $"{d.ItemsBelowMinimum} کالا زیر حداقل موجودی است", "/inventory/stock");
            Alert(d.OpenNcrs > 0, "info", $"{d.OpenNcrs} گزارش عدم انطباق باز است", "/inventory/ncr");
            Alert(d.PaymentsWaitingApproval > 0, "info", $"{d.PaymentsWaitingApproval} درخواست پرداخت در انتظار تایید یا پرداخت", "/procurement/payments");
            Alert(d.SupplierEvaluationsDue > 0, "info", $"ارزیابی دوره‌ای {d.SupplierEvaluationsDue} تامین‌کننده سررسید شده است", "/procurement/suppliers");

            // quality: deadlines and gates computed by the quality module, shown here without any manual step
            QualityDashboardDto qualityDashboard = null;
            QualityCountsDto q;
            if (full)
            {
                qualityDashboard = (await _quality.GetDashboardAsync(ct))?.Data ?? throw new InvalidOperationException("The quality dashboard is not available.");
                q = qualityDashboard;
            }
            else q = await _quality.GetCountsAsync(ct);
            d.OverdueCapas = q.OverdueCapas;
            d.CalibrationOverdue = q.CalibrationOverdue;
            d.CalibrationDue30 = q.CalibrationDue30;
            d.PendingHPoints = q.PendingHPoints;
            d.QualificationsLapsing = q.QualificationsLapsing;
            d.ComplaintsOverdue = q.ComplaintsOverdue;
            d.WitnessNoticesDue = q.WitnessNoticesDue;
            foreach (var a in q.Alerts) d.Alerts.Add(new DashboardAlertDto { Level = a.Level, Text = a.Text, Link = a.Link });

            // project control: schedule and cost performance, change requests, guarantees, penalties (computed, read only)
            var pc = await _projectControl.GetDashboardAsync(ct);
            d.PcActiveProjects = pc.ActiveProjects; d.PcProjectsRed = pc.ProjectsRed; d.PcProjectsAmber = pc.ProjectsAmber; d.PcLateActivities = pc.LateActivities;
            d.PcOpenChangeRequests = pc.OpenChangeRequests; d.PcOverdueChangeRequests = pc.OverdueChangeRequests; d.PcGuaranteeAlerts = pc.GuaranteeAlerts;
            d.PcOverdueMilestones = pc.OverdueMilestones; d.PcOverdueInitiationSteps = pc.OverdueInitiationSteps; d.PcOverdueClientResponses = pc.OverdueClientResponses; d.PcPenaltyExposure = pc.PenaltyExposure;
            Alert(pc.ProjectsRed > 0, "danger", $"{pc.ProjectsRed} پروژه در وضعیت قرمز برنامه یا هزینه است", "/project-control/portfolio");
            Alert(pc.ProjectsAmber > 0, "warning", $"{pc.ProjectsAmber} پروژه در وضعیت زرد برنامه یا هزینه است", "/project-control/portfolio");
            Alert(pc.GuaranteeAlerts > 0, "warning", $"{pc.GuaranteeAlerts} ضمانت‌نامه به سررسید ۳۰ روز یا کمتر رسیده است", "/project-control/guarantees");
            Alert(pc.OverdueChangeRequests > 0, "warning", $"{pc.OverdueChangeRequests} درخواست تغییر از مهلت تحلیل گذشته است", "/project-control/changes");
            Alert(pc.OverdueMilestones > 0, "info", $"{pc.OverdueMilestones} مرحله پرداخت از موعد گذشته است", "/project-control/milestones");
            Alert(pc.OverdueClientResponses > 0, "info", $"{pc.OverdueClientResponses} پاسخ کارفرما به مدارک ارسالی عقب افتاده است", "/project-control/transmittals");
            Alert(pc.PenaltyExposure > 0, "warning", "جریمه تاخیر محاسبه‌شده وجود دارد", "/project-control/penalties");
            // office automation: open decisions of approved minutes past their due date (read directly, so the constructor stays as it is)
            d.OverdueDecisions = await (from dec in _uow.Set<OfDecision>().AsNoTracking()
                                        join meeting in _uow.Set<OfMeeting>().AsNoTracking() on dec.MeetingId equals meeting.Id
                                        where dec.Status == OfDecisionStatus.Open && dec.DueDate != null && dec.DueDate < today && meeting.Status == OfMeetingStatus.MinutesApproved && !meeting.Deleted
                                        select dec.Id).CountAsync(ct);
            Alert(d.OverdueDecisions > 0, "warning", $"{d.OverdueDecisions} مصوبه جلسه از موعد گذشته است", "/office/decisions");
            // local AI: a seat whose latest run failed (read directly, so the constructor stays as it is)
            var aiSeats = await _uow.Set<Entities.Model.NasbNiro.Ai.AiSeat>().AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.Id).Select(x => new { x.Id, x.Title }).ToListAsync(ct);
            foreach (var aiSeat in aiSeats)
            {
                var lastStatus = await _uow.Set<Entities.Model.NasbNiro.Ai.AiSeatRun>().AsNoTracking().Where(r => r.SeatId == aiSeat.Id).OrderByDescending(r => r.Id)
                    .Select(r => (Entities.Model.NasbNiro.Ai.AiSeatRunStatus?)r.Status).FirstOrDefaultAsync(ct);
                Alert(lastStatus == Entities.Model.NasbNiro.Ai.AiSeatRunStatus.Failed, "warning", $"اجرای آخر صندلی هوش مصنوعی «{aiSeat.Title}» با خطا پایان یافته است", "/ai/seats");
            }
            // production: late orders, machines down, overload of the next four weeks, overdue maintenance (read directly, so the constructor stays as it is)
            var production = await Production.MfDashboardService.CeoCountsAsync(_uow, productionAsOf?.Date ?? today, ct);
            d.ProductionLateOrders = production.LateOrders;
            d.ProductionMachinesDown = production.MachinesDown;
            d.ProductionOverloadWeeks = production.OverloadWeeks;
            d.ProductionMaintenanceOverdue = production.MaintenanceOverdue;
            Alert(production.LateOrders > 0, "warning", $"{production.LateOrders} دستور تولید از تاریخ نیاز عقب است", "/production/orders");
            Alert(production.MachinesDown > 0, "danger", $"{production.MachinesDown} ماشین خراب است", "/production/machines");
            Alert(production.OverloadWeeks > 0, "warning", $"بار {production.OverloadWeeks} هفته مرکز کار در چهار هفته آینده بیش از ۱۱۰٪ ظرفیت است", "/production/load");
            Alert(production.MaintenanceOverdue > 0, "info", $"{production.MaintenanceOverdue} دستور نگهداری از موعد گذشته است", "/production/maintenance-orders");
            // sales: pipeline, gates waiting for the CEO and deadlines
            var sales = await Sales.SlDashboardService.CeoCountsAsync(_uow, productionAsOf?.Date ?? today, ct);
            d.SalesOpenInquiries = sales.OpenInquiries;
            d.SalesPipelineValue = sales.PipelineValue;
            d.SalesBidDecisionsWaiting = sales.BidDecisionsWaiting;
            d.SalesProposalsWaitingApproval = sales.ProposalsWaitingApproval;
            d.SalesProposalsDueSoon = sales.ProposalsDueSoon;
            d.SalesBelowCostProposals = sales.BelowCostProposals;
            d.SalesOrdersWaitingCredit = sales.OrdersWaitingCredit;
            d.SalesHitRatePercent = sales.HitRatePercent;
            Alert(sales.ProposalsDueSoon > 0, "warning", $"{sales.ProposalsDueSoon} پیشنهاد تا ۳ روز دیگر (یا گذشته) به مهلت ارسال می‌رسد و هنوز ارسال نشده است", "/sales/inquiries");
            Alert(sales.BelowCostProposals > 0, "danger", $"{sales.BelowCostProposals} پیشنهاد با قیمت کمتر از بهای تمام‌شده منتظر تصمیم مدیرعامل است", "/sales/proposals");
            Alert(sales.BidDecisionsWaiting > 0, "info", $"{sales.BidDecisionsWaiting} استعلام منتظر تصمیم شرکت یا عدم شرکت است", "/sales/bid-decisions");
            // after-sales service: open requests, SLA breaches, units in warranty, the warranty cost of the year, goodwill waiting for the CEO, the satisfaction average
            // the warranty cost needs a mask only for an order closed by an earlier release at the engineers' own pay (one or two payslips behind it); the new labour is absorbed at the standard rate. The satisfaction average counts verified answers only and the unverified ones are counted next to it (R1p D25)
            var service = await AfterSales.SvDashboardService.CoreAsync(_uow, productionAsOf ?? DateTime.Now, AfterSales.SvRoles.SeesPayroll(_user.IsInRole(AfterSales.SvRoles.PayrollSeers)), ct);
            d.ServiceOpenRequests = service.OpenRequests;
            d.ServiceSlaBreaches = service.SlaBreaches;
            d.ServiceUnitsInWarranty = service.UnitsInWarranty;
            d.ServiceWarrantyCostYtd = service.WarrantyCostYtd;
            d.ServiceWarrantyCostHidden = service.WarrantyCostHidden;
            d.ServiceGoodwillWaiting = service.GoodwillWaiting;
            d.ServiceSurveyAverageScore = service.SurveyAverageScore;
            d.ServiceSurveysUnverified = service.SurveysUnverified;
            Alert(service.SlaBreaches > 0, "danger", $"{service.SlaBreaches} درخواست خدمات پس از فروش از موعد SLA گذشته است", "/service/requests");
            Alert(service.GoodwillWaiting > 0, "info", $"{service.GoodwillWaiting} درخواست حسن نیت خدمات منتظر تصمیم مدیرعامل است", "/service/orders");
            // finance module: cash, receivables, cheques, profit, margins
            d.Finance = await _finance.GetCeoFinanceAsync(ct);
            d.Alerts.AddRange(d.Finance.Alerts);
            return new CeoDashboardParts { Dashboard = d, Quality = qualityDashboard, ProductionLoad = production.Load, Sales = sales };
        }
    }
}
