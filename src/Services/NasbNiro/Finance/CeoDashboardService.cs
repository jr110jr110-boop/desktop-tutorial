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
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Finance
{
    public interface ICeoDashboardService
    {
        Task<ResponseDto<CeoDashboardDto>> GetAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Read-only figures for the CEO dashboard. Every figure comes from the PMIS records themselves
    /// (stock ledger, cost entries, accounting events, procurement), so it is the same number Sepidar receives.
    /// </summary>
    public class CeoDashboardService : ICeoDashboardService
    {
        private readonly IUnitOfWork _uow;

        private readonly Quality.IQualityDashboardService _quality;

        public CeoDashboardService(IUnitOfWork uow, Quality.IQualityDashboardService quality)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _quality = quality ?? throw new ArgumentNullException(nameof(quality));
        }

        public async Task<ResponseDto<CeoDashboardDto>> GetAsync(CancellationToken ct)
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
            d.OpenNcrs = await _uow.Set<NonConformance>().CountAsync(n => !n.Deleted && n.Status != NcrStatus.Closed, ct);
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
            var q = await _quality.GetCountsAsync(ct);
            d.OverdueCapas = q.OverdueCapas;
            d.CalibrationOverdue = q.CalibrationOverdue;
            d.CalibrationDue30 = q.CalibrationDue30;
            d.PendingHPoints = q.PendingHPoints;
            d.QualificationsLapsing = q.QualificationsLapsing;
            d.ComplaintsOverdue = q.ComplaintsOverdue;
            d.WitnessNoticesDue = q.WitnessNoticesDue;
            foreach (var a in q.Alerts) d.Alerts.Add(new DashboardAlertDto { Level = a.Level, Text = a.Text, Link = a.Link });
            return ResponseDtoHelper.Ok(d);
        }
    }
}
