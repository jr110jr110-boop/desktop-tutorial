using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>One approved supplier invoice that is unpaid and past the payment term.</summary>
    public sealed class CeoOverduePayable
    {
        public int InvoiceId { get; set; }
        public int ProviderId { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }

        /// <summary>Total less what was paid on the invoice (never below zero).</summary>
        public decimal Balance { get; set; }

        /// <summary>Days past the payment term.</summary>
        public int DaysOverdue { get; set; }
    }

    /// <summary>The value of the company-owned stock in one warehouse.</summary>
    public sealed class CeoWarehouseValue
    {
        public int WarehouseId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>Rial: quantity on hand times the weighted average cost of the item.</summary>
        public decimal Value { get; set; }
    }

    /// <summary>The sales backlog and its two parts.</summary>
    public sealed class CeoBacklog
    {
        /// <summary>Confirmed and partly delivered sales orders: the net amount of what is not yet invoiced.</summary>
        public decimal Orders { get; set; }

        /// <summary>Contracts in progress whose project-control terms give a value: that value less the net invoiced so far.</summary>
        public decimal Contracts { get; set; }

        public decimal Total => Orders + Contracts;
    }

    /// <summary>
    /// The figures the CEO overview defines itself because no module dashboard has them (★ definitions), and the snapshot values.
    /// Everything here reads through the unit of work only (no user), so the nightly job and the overview compute the same numbers.
    /// </summary>
    public static class CeoFigures
    {
        /// <summary>
        /// ★ Payment term of a supplier invoice when nothing else is known: 30 days from the invoice date (the procurement module keeps the term only as free text).
        /// </summary>
        public const int PayableTermDays = 30;

        /// <summary>
        /// ★ Overdue payables: approved supplier invoices whose invoice date plus <see cref="PayableTermDays"/> days is before today and whose balance
        /// (invoice total less the paid payment requests on it, the same rule as the open payables of the CEO dashboard) is above zero; one row per invoice.
        /// </summary>
        public static async Task<List<CeoOverduePayable>> OverduePayablesAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTime cutoff = today.Date.AddDays(-PayableTermDays);
            var invoices = await uow.Set<SupplierInvoice>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == SupplierInvoiceStatus.Approved && i.InvoiceDate < cutoff)
                .Select(i => new { i.Id, i.ProviderId, i.InvoiceNumber, i.InvoiceDate, i.TotalAmount }).ToListAsync(ct);
            var result = new List<CeoOverduePayable>();
            if (invoices.Count == 0) return result;
            List<int> ids = invoices.Select(i => i.Id).ToList();
            var paid = await uow.Set<PaymentRequest>().AsNoTracking()
                .Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid && p.InvoiceId != null && ids.Contains(p.InvoiceId.Value))
                .GroupBy(p => p.InvoiceId.Value).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
            Dictionary<int, decimal> paidBy = paid.ToDictionary(p => p.Key, p => p.Amount);
            foreach (var i in invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id))
            {
                decimal amountPaid = paidBy.TryGetValue(i.Id, out decimal v) ? v : 0m;
                decimal balance = Math.Max(0m, i.TotalAmount - amountPaid);
                if (balance <= 0m) continue;
                result.Add(new CeoOverduePayable
                {
                    InvoiceId = i.Id, ProviderId = i.ProviderId, InvoiceNumber = i.InvoiceNumber, InvoiceDate = i.InvoiceDate.Date, Total = i.TotalAmount, Paid = amountPaid, Balance = balance,
                    DaysOverdue = (int)(today.Date - i.InvoiceDate.Date.AddDays(PayableTermDays)).TotalDays,
                });
            }
            return result;
        }

        /// <summary>The sum of <see cref="OverduePayablesAsync"/>.</summary>
        public static async Task<decimal> PayablesOverdueAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await OverduePayablesAsync(uow, today, ct)).Sum(r => r.Balance);

        /// <summary>
        /// ★ Sales backlog = (A) for the confirmed and partly delivered sales orders: each line's amount times the share of its quantity not yet invoiced,
        /// plus (B) for the contracts in progress whose project-control terms carry a value (before VAT): that value less the net amount of their posted sales invoices
        /// (credit notes deducted; advance and retention-release invoices left out), never below zero.
        /// </summary>
        public static async Task<CeoBacklog> SalesBacklogAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var backlog = new CeoBacklog();

            var lines = await uow.Set<SlSalesOrder>().AsNoTracking()
                .Where(o => !o.Deleted && (o.Status == SlOrderStatus.Confirmed || o.Status == SlOrderStatus.PartlyDelivered))
                .SelectMany(o => o.Lines).Select(l => new { l.Amount, l.Quantity, l.InvoicedQuantity }).ToListAsync(ct);
            foreach (var l in lines)
            {
                if (l.Quantity <= 0m) continue;
                backlog.Orders += l.Amount * Math.Max(0m, l.Quantity - l.InvoicedQuantity) / l.Quantity;
            }
            backlog.Orders = Math.Round(backlog.Orders, 0, MidpointRounding.AwayFromZero);

            var terms = await uow.Set<PcContractTerms>().AsNoTracking()
                .Where(t => !t.Deleted && t.ValueExVat != null && t.ValueExVat > 0 && !t.Contract.Deleted && t.Contract.Status == ContractStatus.Current)
                .Select(t => new { t.ContractId, Value = t.ValueExVat.Value }).ToListAsync(ct);
            if (terms.Count > 0)
            {
                List<int> ids = terms.Select(t => t.ContractId).Distinct().ToList();
                var invoiced = await uow.Set<FnSalesInvoice>().AsNoTracking()
                    .Where(i => i.Status == FnDocStatus.Posted && i.ContractId != null && ids.Contains(i.ContractId.Value) && i.Type != FnInvoiceType.Advance && i.Type != FnInvoiceType.RetentionRelease)
                    .GroupBy(i => i.ContractId.Value).Select(g => new { g.Key, Net = g.Sum(i => i.Type == FnInvoiceType.CreditNote ? -i.NetAmount : i.NetAmount) }).ToListAsync(ct);
                Dictionary<int, decimal> net = invoiced.ToDictionary(x => x.Key, x => x.Net);
                foreach (var t in terms.GroupBy(t => t.ContractId).Select(g => g.First()))
                    backlog.Contracts += Math.Max(0m, t.Value - (net.TryGetValue(t.ContractId, out decimal n) ? n : 0m));
            }
            return backlog;
        }

        /// <summary>
        /// ★ Stock value by warehouse: for every balance with a quantity on hand (customer property excluded, as in the item costs), the quantity times the
        /// weighted average cost of the item (<see cref="ItemCost"/>), summed per warehouse; warehouses without value are left out; the biggest first.
        /// The total equals the stock value of the CEO dashboard (the sum of the item costs) apart from stock held in a warehouse for an item without a cost row.
        /// </summary>
        public static async Task<List<CeoWarehouseValue>> StockValueByWarehouseAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var sums = await uow.Set<StockBalance>().AsNoTracking()
                .Where(b => b.Quantity > 0 && (b.LotId == null || !b.Lot.IsCustomerProperty))
                .Join(uow.Set<ItemCost>().AsNoTracking(), b => b.ItemId, c => c.ItemId, (b, c) => new { b.WarehouseId, Value = b.Quantity * c.AverageCost })
                .GroupBy(x => x.WarehouseId).Select(g => new { WarehouseId = g.Key, Value = g.Sum(x => x.Value) }).ToListAsync(ct);
            List<int> ids = sums.Where(s => s.Value > 0m).Select(s => s.WarehouseId).ToList();
            if (ids.Count == 0) return new List<CeoWarehouseValue>();
            var names = await uow.Set<Warehouse>().AsNoTracking().Where(w => ids.Contains(w.Id)).Select(w => new { w.Id, w.Code, w.Title }).ToListAsync(ct);
            return sums.Where(s => s.Value > 0m)
                .Join(names, s => s.WarehouseId, w => w.Id, (s, w) => new CeoWarehouseValue { WarehouseId = w.Id, Code = w.Code, Title = string.IsNullOrWhiteSpace(w.Title) ? w.Code : w.Title, Value = Math.Round(s.Value, 0, MidpointRounding.AwayFromZero) })
                .OrderByDescending(w => w.Value).ThenBy(w => w.WarehouseId).ToList();
        }

        /// <summary>
        /// The values of the snapshot keys (<see cref="CeoKeys.Snapshot"/>, in that order) from the CEO dashboard and the two figures defined here.
        /// </summary>
        public static List<KeyValuePair<string, decimal>> SnapshotValues(CeoDashboardDto d, decimal payablesOverdue, decimal salesBacklog)
        {
            CeoFinanceDto f = d.Finance ?? new CeoFinanceDto();
            decimal Value(string key)
            {
                switch (key)
                {
                    case CeoKeys.Cash: return f.CashAndBank;
                    case CeoKeys.ReceivablesOpen: return f.ReceivablesOpen;
                    case CeoKeys.PayablesOverdue: return payablesOverdue;
                    case CeoKeys.SalesBacklog: return salesBacklog;
                    case CeoKeys.ActiveProjects: return d.PcActiveProjects;
                    case CeoKeys.LateProductionOrders: return d.ProductionLateOrders;
                    case CeoKeys.OpenNcrs: return d.OpenNcrs;
                    case CeoKeys.ServiceSlaBreaches: return d.ServiceSlaBreaches;
                    case CeoKeys.StockValue: return d.StockValue;
                    case CeoKeys.ItemsBelowMinimum: return d.ItemsBelowMinimum;
                    case CeoKeys.OpenPurchaseOrdersAmount: return d.OpenPurchaseOrdersAmount;
                    case CeoKeys.SalesPipelineValue: return d.SalesPipelineValue;
                    default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown snapshot key.");
                }
            }
            return CeoKeys.Snapshot.Select(k => new KeyValuePair<string, decimal>(k, Math.Round(Value(k), 2, MidpointRounding.AwayFromZero))).ToList();
        }
    }
}
