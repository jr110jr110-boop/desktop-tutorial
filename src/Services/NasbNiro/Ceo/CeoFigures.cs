using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>One supplier and what the company owes him for more than <see cref="CeoFigures.PayableTermDays"/> days: a row of the payables ageing of the general ledger.</summary>
    public sealed class CeoOverduePayable
    {
        /// <summary>The name of the supplier (the title of his detail account in the ledger).</summary>
        public string Supplier { get; set; }

        /// <summary>Owed for up to 30 days from the date of the document: not overdue.</summary>
        public decimal Current { get; set; }

        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }

        /// <summary>What is overdue: the three buckets older than 30 days.</summary>
        public decimal Overdue => Days31To60 + Days61To90 + Over90;

        /// <summary>Everything owed to the supplier, overdue or not.</summary>
        public decimal Total { get; set; }
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
        /// ★ Payment term when nothing else is known: 30 days from the date of the document (the procurement module keeps the term only as free text, and the notes schedule
        /// of the financial statements ages the payables in buckets of 30 days).
        /// </summary>
        public const int PayableTermDays = 30;

        /// <summary>
        /// ★ Overdue payables, from the payables ageing of the general ledger (the one in the notes of the financial statements, see <see cref="IPayableAgeingService"/>):
        /// per supplier, what is owed for more than <see cref="PayableTermDays"/> days (the buckets 31-60, 61-90 and over 90) on <paramref name="today"/>; suppliers with nothing overdue are left out,
        /// the biggest overdue amount first. Payments and returns are matched against the oldest documents first. An advance (or a progress payment made without an invoice) sits in the supplier advance
        /// account, not in the payable account, so it reduces the debt only after a payment request has recovered it. This is the single function behind the KPI and the snapshot of
        /// <c>payablesOverdue</c> and the drill <c>fin.payables_overdue</c>; it does not check roles (the caller does).
        /// </summary>
        public static async Task<List<CeoOverduePayable>> OverduePayablesAsync(IPayableAgeingService ageing, DateTime today, CancellationToken ct)
        {
            List<FnNoteAgeingDto> rows = await ageing.PayableAgeingAsync(today.Date, ct) ?? new List<FnNoteAgeingDto>();
            return rows.Select(r => new CeoOverduePayable { Supplier = r.Name, Current = r.Current, Days31To60 = r.Days31To60, Days61To90 = r.Days61To90, Over90 = r.Over90, Total = r.Total })
                .Where(r => r.Overdue > 0m)
                .OrderByDescending(r => r.Overdue).ThenBy(r => r.Supplier, StringComparer.Ordinal).ToList();
        }

        /// <summary>The sum of the overdue amounts of <see cref="OverduePayablesAsync"/>.</summary>
        public static async Task<decimal> PayablesOverdueAsync(IPayableAgeingService ageing, DateTime today, CancellationToken ct) =>
            (await OverduePayablesAsync(ageing, today, ct)).Sum(r => r.Overdue);

        /// <summary>
        /// The part of a sales-order line that is not invoiced yet: its amount times the share of its quantity still to be invoiced (zero for a line without a quantity, never negative).
        /// The one function behind the order part of the sales backlog and the column <c>remainingAmount</c> of the drill <c>sl.orders_open</c>, so the two always agree.
        /// </summary>
        public static decimal UninvoicedAmount(decimal amount, decimal quantity, decimal invoicedQuantity) =>
            quantity <= 0m ? 0m : amount * Math.Max(0m, quantity - invoicedQuantity) / quantity;

        /// <summary>A rial amount rounded to the rial: the sum of <see cref="UninvoicedAmount"/> is rounded once, after adding the lines up.</summary>
        public static decimal RoundRial(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

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
            foreach (var l in lines) backlog.Orders += UninvoicedAmount(l.Amount, l.Quantity, l.InvoicedQuantity);
            backlog.Orders = RoundRial(backlog.Orders);

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
