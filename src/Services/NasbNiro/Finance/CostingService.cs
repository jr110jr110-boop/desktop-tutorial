using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Finance
{
    /// <summary>Account roles used in accounting events. Sepidar account codes are set per role in AccountMapping.</summary>
    public static class AccountRoles
    {
        public const string Inventory = "Inventory";            // موجودی کالا (per item group: Inventory.01 ... Inventory.12)
        public const string Grni = "GRNI";                      // کالای دریافتی فاکتورنشده (حساب‌های پرداختنی موقت)
        public const string Wip = "WIP";                        // کالای در جریان ساخت (per work order / project)
        public const string MaterialExpense = "MaterialExpense"; // هزینه مواد مصرفی (مرکز هزینه)
        public const string Cogs = "COGS";                      // بهای تمام‌شده کالای فروش‌رفته
        public const string InventoryGain = "InventoryGain";    // اضافات انبار
        public const string InventoryLoss = "InventoryLoss";    // کسورات انبار
        public const string OpeningBalance = "OpeningBalance";  // تراز افتتاحیه موجودی
        public const string ScrapRecovery = "ScrapRecovery";    // بازیافت ضایعات
        public const string ScrapCost = "ScrapCost";            // بهای تمام‌شده ضایعات فروخته‌شده
        public const string ScrapRevenue = "ScrapRevenue";      // درآمد فروش ضایعات
        public const string Bank = "Bank";                     // بانک (حساب رسمی شرکت)
        public const string Revaluation = "InventoryRevaluation"; // تعدیل ارزش موجودی (گردکردن، مانده صفر)
        public const string AccountsPayable = "AccountsPayable"; // حساب‌های پرداختنی تجاری (تامین‌کننده)
        public const string PurchasePriceVariance = "PPV";      // اختلاف قیمت خرید
        public const string Vat = "VAT";                        // مالیات و عوارض ارزش افزوده خرید
        public const string SupplierAdvance = "SupplierAdvance"; // پیش‌پرداخت به تامین‌کننده
        public const string PurchasedServices = "PurchasedServices"; // خدمات و پیمانکاری خریداری‌شده

        public static string InventoryOf(string itemCode)
        {
            string prefix = itemCode != null && itemCode.Length >= 2 && char.IsDigit(itemCode[0]) && char.IsDigit(itemCode[1]) ? itemCode.Substring(0, 2) : null;
            return prefix == null ? Inventory : Inventory + "." + prefix;
        }

        public static IEnumerable<(string Role, string Title)> Standard()
        {
            yield return (Inventory, "موجودی کالا");
            yield return (Inventory + ".01", "موجودی متریال");
            yield return (Inventory + ".02", "موجودی قطعات و ریخته‌گری");
            yield return (Inventory + ".03", "موجودی محصول");
            yield return (Inventory + ".04", "موجودی مدل");
            yield return (Inventory + ".05", "موجودی ملزومات مصرفی");
            yield return (Inventory + ".07", "موجودی ابزارآلات");
            yield return (Inventory + ".08", "موجودی قطعات استاندارد");
            yield return (Inventory + ".12", "موجودی ابزار دقیق");
            yield return (Grni, "کالای دریافتی فاکتورنشده");
            yield return (Wip, "کالای در جریان ساخت");
            yield return (MaterialExpense, "هزینه مواد مصرفی");
            yield return (Cogs, "بهای تمام‌شده کالای فروش‌رفته");
            yield return (InventoryGain, "اضافات انبار");
            yield return (InventoryLoss, "کسورات انبار");
            yield return (OpeningBalance, "تراز افتتاحیه موجودی");
            yield return (ScrapRecovery, "بازیافت ضایعات");
            yield return (ScrapCost, "بهای تمام‌شده ضایعات فروخته‌شده");
            yield return (ScrapRevenue, "درآمد فروش ضایعات");
            yield return (Bank, "بانک - حساب رسمی شرکت");
            yield return (Revaluation, "تعدیل ارزش موجودی");
            yield return (AccountsPayable, "حساب‌های پرداختنی تجاری");
            yield return (PurchasePriceVariance, "اختلاف قیمت خرید");
            yield return (Vat, "مالیات بر ارزش افزوده خرید");
            yield return (SupplierAdvance, "پیش‌پرداخت خرید");
            yield return (PurchasedServices, "خدمات و پیمانکاری خریداری‌شده");
        }
    }

    public interface ICostingService
    {
        /// <summary>
        /// Values the stock movements of a confirmed document (weighted average), charges material to work orders
        /// and writes the accounting event. Called inside the confirm, before SaveChanges.
        /// </summary>
        Task PostDocumentAsync(InventoryDocument document, IReadOnlyList<StockTransaction> transactions, CancellationToken cancellationToken);

        /// <summary>Reverses the value, the costs and the accounting events of a cancelled document. Before SaveChanges.</summary>
        Task ReverseDocumentAsync(InventoryDocument document, IReadOnlyList<StockTransaction> original, IReadOnlyList<StockTransaction> reversals, CancellationToken cancellationToken);

        /// <summary>Writes a balanced event (used by procurement). Lines with zero amounts are dropped.</summary>
        AccountingEvent AddEvent(AccountingEventType type, DateTime date, string sourceType, int sourceId, string sourceNumber, string description, IEnumerable<AccountingEventLine> lines);

        Task EnsureAccountRolesAsync(CancellationToken cancellationToken);
    }

    public class CostingService : ICostingService
    {
        public const string InventoryDocumentSource = "InventoryDocument";

        private readonly IUnitOfWork _uow;
        private readonly Inventory.IInventoryUserContext _user;

        public CostingService(IUnitOfWork uow, Inventory.IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static decimal R(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

        private async Task<Dictionary<int, ItemCost>> LoadCostsAsync(IEnumerable<int> itemIds, CancellationToken cancellationToken)
        {
            List<int> ids = itemIds.Distinct().ToList();
            Dictionary<int, ItemCost> costs = await _uow.Set<ItemCost>().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, cancellationToken);
            foreach (int id in ids.Where(i => !costs.ContainsKey(i)))
            {
                var cost = new ItemCost { ItemId = id, UpdatedAt = DateTimeOffset.Now };
                _uow.Set<ItemCost>().Add(cost);
                costs[id] = cost;
            }
            return costs;
        }

        /// <summary>Applies a valued movement to the item's average. Returns the value that could not stay in stock (quantity reached zero).</summary>
        private static decimal Apply(ItemCost cost, decimal quantity, decimal amount)
        {
            cost.Quantity += quantity;
            cost.Value += amount;
            decimal residual = 0;
            if (cost.Quantity <= 0)
            {
                residual = cost.Value;   // goes to revaluation so that no value stays without stock
                cost.Value = 0;
                if (cost.Quantity < 0) cost.Quantity = 0;
            }
            else
            {
                cost.AverageCost = Math.Round(cost.Value / cost.Quantity, 4);
            }
            cost.UpdatedAt = DateTimeOffset.Now;
            return residual;
        }

        public async Task PostDocumentAsync(InventoryDocument document, IReadOnlyList<StockTransaction> transactions, CancellationToken cancellationToken)
        {
            if (transactions.Count == 0) return;
            InventoryDocumentType type = document.Type;
            List<int> warehouseIds = transactions.Select(t => t.WarehouseId).Distinct().ToList();
            Dictionary<int, WarehouseKind> kinds = await _uow.Set<Warehouse>().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Kind, cancellationToken);
            bool Valued(StockTransaction t) => kinds.TryGetValue(t.WarehouseId, out WarehouseKind k) && k != WarehouseKind.CustomerProperty;

            List<int> itemIds = transactions.Select(t => t.ItemId).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
            Dictionary<int, ItemCost> costs = await LoadCostsAsync(itemIds, cancellationToken);
            Dictionary<int, InventoryDocumentLine> lines = document.Lines.ToDictionary(l => l.Id);

            // 1) everything that goes out takes the current average
            var outValueByLine = new Dictionary<int, decimal>();
            var outValueByRow = new Dictionary<int, decimal>();
            var roleNet = new Dictionary<string, decimal>();
            void AddRole(string role, decimal amount) => roleNet[role] = (roleNet.TryGetValue(role, out decimal v) ? v : 0) + amount;
            decimal residuals = 0;

            foreach (StockTransaction t in transactions.Where(t => t.Quantity < 0))
            {
                if (!Valued(t)) { t.UnitCost = 0; t.CostAmount = 0; continue; }
                ItemCost cost = costs[t.ItemId];
                t.UnitCost = cost.AverageCost;
                t.CostAmount = R(t.Quantity * cost.AverageCost);
                residuals += Apply(cost, t.Quantity, t.CostAmount);
                AddRole(AccountRoles.InventoryOf(codes[t.ItemId]), t.CostAmount);
                if (t.DocumentLineId.HasValue)
                {
                    outValueByLine[t.DocumentLineId.Value] = (outValueByLine.TryGetValue(t.DocumentLineId.Value, out decimal v) ? v : 0) - t.CostAmount;
                    if (lines.TryGetValue(t.DocumentLineId.Value, out InventoryDocumentLine ol))
                        outValueByRow[ol.RowNumber] = (outValueByRow.TryGetValue(ol.RowNumber, out decimal r) ? r : 0) - t.CostAmount;
                }
            }

            // 2) what comes in: the value of the same line (transfer), of the source row (stage conversion) or the price
            var inQtyByLine = transactions.Where(t => t.Quantity > 0 && t.DocumentLineId.HasValue)
                .GroupBy(t => t.DocumentLineId.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity));
            var inQtyBySourceRow = new Dictionary<int, decimal>();
            foreach (StockTransaction t in transactions.Where(t => t.Quantity > 0 && t.DocumentLineId.HasValue))
                if (lines.TryGetValue(t.DocumentLineId.Value, out InventoryDocumentLine il) && il.SourceRowNumber.HasValue)
                    inQtyBySourceRow[il.SourceRowNumber.Value] = (inQtyBySourceRow.TryGetValue(il.SourceRowNumber.Value, out decimal q) ? q : 0) + t.Quantity;

            foreach (StockTransaction t in transactions.Where(t => t.Quantity > 0))
            {
                if (!Valued(t)) { t.UnitCost = 0; t.CostAmount = 0; continue; }
                ItemCost cost = costs[t.ItemId];
                InventoryDocumentLine line = t.DocumentLineId.HasValue && lines.TryGetValue(t.DocumentLineId.Value, out InventoryDocumentLine l) ? l : null;
                decimal unit;
                if (line != null && outValueByLine.TryGetValue(line.Id, out decimal lineOut) && inQtyByLine[line.Id] > 0)
                    unit = lineOut / inQtyByLine[line.Id];
                else if (line?.SourceRowNumber != null && outValueByRow.TryGetValue(line.SourceRowNumber.Value, out decimal rowOut) &&
                         inQtyBySourceRow.TryGetValue(line.SourceRowNumber.Value, out decimal rowQty) && rowQty > 0)
                    unit = rowOut / rowQty;
                else if (t.UnitPrice.HasValue && t.UnitPrice.Value > 0)
                    unit = t.UnitPrice.Value;
                else if (type == InventoryDocumentType.ScrapReceipt)
                    unit = 0;
                else
                    unit = cost.AverageCost; // returns and adjustments without a price
                t.UnitCost = Math.Round(unit, 4);
                t.CostAmount = R(t.Quantity * unit);
                if ((type == InventoryDocumentType.PurchaseReceipt) && t.UnitPrice > 0) cost.LastPurchasePrice = t.UnitPrice;
                residuals += Apply(cost, t.Quantity, t.CostAmount);
                AddRole(AccountRoles.InventoryOf(codes[t.ItemId]), t.CostAmount);
            }
            if (residuals != 0)
            {
                // value left without stock: out of inventory into revaluation
                AddRole(AccountRoles.InventoryOf(null), -residuals);
                AddRole(AccountRoles.Revaluation, residuals);
            }

            // 3) material cost of work orders and projects
            int? contractId = document.ContractId;
            if (document.WorkOrderId.HasValue && !contractId.HasValue)
                contractId = await _uow.Set<WorkOrder>().Where(w => w.Id == document.WorkOrderId.Value).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(cancellationToken);
            if (type == InventoryDocumentType.Issue || type == InventoryDocumentType.ProductionReturn)
            {
                foreach (var g in transactions.Where(t => Valued(t)).GroupBy(t => t.ItemId))
                {
                    decimal amount = -g.Sum(t => t.CostAmount ?? 0);
                    if (amount == 0) continue;
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = document.DocumentDate, Kind = CostKind.Material, WorkOrderId = document.WorkOrderId, ContractId = contractId,
                        ItemId = g.Key, Quantity = -g.Sum(t => t.Quantity), Amount = amount,
                        SourceType = InventoryDocumentSource, SourceId = document.Id, SourceNumber = document.Number,
                        Description = type == InventoryDocumentType.Issue ? "حواله مواد" : "برگشت از تولید",
                        CreatedAt = DateTimeOffset.Now, CreatedByUserId = _user.UserId,
                    });
                }
            }

            // 4) the accounting event: inventory roles against the account of this kind of document
            string counter = CounterRole(type, document.WorkOrderId.HasValue || contractId.HasValue);
            var eventLines = new List<AccountingEventLine>();
            foreach (var pair in roleNet.Where(p => R(p.Value) != 0).OrderBy(p => p.Key))
            {
                decimal amount = R(pair.Value);
                eventLines.Add(new AccountingEventLine
                {
                    AccountRole = pair.Key, Debit = amount > 0 ? amount : 0, Credit = amount < 0 ? -amount : 0,
                    ProviderId = pair.Key == AccountRoles.Revaluation ? null : null,
                });
            }
            decimal net = eventLines.Sum(l => l.Debit - l.Credit);
            if (net != 0)
            {
                eventLines.Add(new AccountingEventLine
                {
                    AccountRole = counter ?? AccountRoles.Revaluation,
                    Debit = net < 0 ? -net : 0, Credit = net > 0 ? net : 0,
                    ProviderId = counter == AccountRoles.Grni ? document.ProviderId : null,
                    WorkOrderId = counter == AccountRoles.Wip || counter == AccountRoles.Cogs ? document.WorkOrderId : null,
                    ContractId = counter == AccountRoles.Wip || counter == AccountRoles.Cogs ? contractId : null,
                });
            }

            // scrap sale: the money (deposited into the company account) against the revenue
            if (type == InventoryDocumentType.ScrapSale)
            {
                decimal sale = R(document.Lines.Sum(l => l.Quantity * (l.UnitPrice ?? 0)));
                if (sale > 0)
                {
                    eventLines.Add(new AccountingEventLine { AccountRole = AccountRoles.Bank, Debit = sale, Description = "واریز " + document.DepositReference });
                    eventLines.Add(new AccountingEventLine { AccountRole = AccountRoles.ScrapRevenue, Credit = sale, Description = document.BuyerName });
                }
            }

            AddEvent(EventType(type), document.DocumentDate, InventoryDocumentSource, document.Id, document.Number,
                InventoryText(document), eventLines);
        }

        private static string InventoryText(InventoryDocument d) =>
            Inventory.InventoryDocumentRules.Title(d.Type) + (d.ReferenceNumber != null ? " - " + d.ReferenceNumber : "");

        private static string CounterRole(InventoryDocumentType type, bool forWork)
        {
            switch (type)
            {
                case InventoryDocumentType.PurchaseReceipt:
                case InventoryDocumentType.SupplierReturn: return AccountRoles.Grni;
                case InventoryDocumentType.OpeningBalance: return AccountRoles.OpeningBalance;
                case InventoryDocumentType.AdjustmentIncrease: return AccountRoles.InventoryGain;
                case InventoryDocumentType.AdjustmentDecrease: return AccountRoles.InventoryLoss;
                case InventoryDocumentType.Issue:
                case InventoryDocumentType.ProductionReturn: return forWork ? AccountRoles.Wip : AccountRoles.MaterialExpense;
                case InventoryDocumentType.ProductionReceipt: return AccountRoles.Wip;
                case InventoryDocumentType.CustomerDelivery: return AccountRoles.Cogs;
                case InventoryDocumentType.ScrapReceipt: return AccountRoles.ScrapRecovery;
                case InventoryDocumentType.ScrapSale: return AccountRoles.ScrapCost;
                default: return AccountRoles.Revaluation; // transfers and conversions: only differences between item groups
            }
        }

        private static AccountingEventType EventType(InventoryDocumentType type)
        {
            switch (type)
            {
                case InventoryDocumentType.PurchaseReceipt: return AccountingEventType.PurchaseReceipt;
                case InventoryDocumentType.SupplierReturn: return AccountingEventType.SupplierReturn;
                case InventoryDocumentType.Issue: return AccountingEventType.MaterialIssue;
                case InventoryDocumentType.ProductionReturn: return AccountingEventType.ProductionReturn;
                case InventoryDocumentType.ProductionReceipt: return AccountingEventType.ProductionReceipt;
                case InventoryDocumentType.AdjustmentIncrease: return AccountingEventType.InventoryGain;
                case InventoryDocumentType.AdjustmentDecrease: return AccountingEventType.InventoryLoss;
                case InventoryDocumentType.OpeningBalance: return AccountingEventType.OpeningBalance;
                case InventoryDocumentType.CustomerDelivery: return AccountingEventType.CustomerDelivery;
                case InventoryDocumentType.ScrapReceipt: return AccountingEventType.ScrapReceipt;
                case InventoryDocumentType.ScrapSale: return AccountingEventType.ScrapSale;
                default: return AccountingEventType.StageConversion;
            }
        }

        public AccountingEvent AddEvent(AccountingEventType type, DateTime date, string sourceType, int sourceId, string sourceNumber, string description,
            IEnumerable<AccountingEventLine> lines)
        {
            List<AccountingEventLine> list = lines.Where(l => l.Debit != 0 || l.Credit != 0).ToList();
            decimal difference = list.Sum(l => l.Debit) - list.Sum(l => l.Credit);
            if (difference != 0)
                throw new InvalidOperationException($"Accounting event {sourceType}/{sourceId} is not balanced ({difference}).");
            int row = 1;
            foreach (AccountingEventLine line in list) line.RowNumber = row++;
            var e = new AccountingEvent
            {
                Type = type,
                EventDate = date.Date,
                Status = list.Count == 0 ? AccountingEventStatus.NotRequired : AccountingEventStatus.Pending,
                SourceType = sourceType,
                SourceId = sourceId,
                SourceNumber = sourceNumber,
                Description = description,
                Key = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.Now,
                CreatedByUserId = _user.UserId,
                Lines = list,
            };
            _uow.Set<AccountingEvent>().Add(e);
            return e;
        }

        public async Task ReverseDocumentAsync(InventoryDocument document, IReadOnlyList<StockTransaction> original, IReadOnlyList<StockTransaction> reversals,
            CancellationToken cancellationToken)
        {
            // value: each reversal takes back exactly the value of its original movement
            Dictionary<int, ItemCost> costs = await LoadCostsAsync(original.Select(t => t.ItemId), cancellationToken);
            decimal residuals = 0;
            for (int i = 0; i < reversals.Count && i < original.Count; i++)
            {
                StockTransaction o = original[i], r = reversals[i];
                r.UnitCost = o.UnitCost;
                r.CostAmount = -(o.CostAmount ?? 0);
                if (o.CostAmount.HasValue || o.UnitCost.HasValue)
                    residuals += Apply(costs[r.ItemId], r.Quantity, r.CostAmount ?? 0);
            }

            // costs of work orders
            List<CostEntry> entries = await _uow.Set<CostEntry>()
                .Where(c => c.SourceType == InventoryDocumentSource && c.SourceId == document.Id).ToListAsync(cancellationToken);
            foreach (CostEntry c in entries)
            {
                _uow.Set<CostEntry>().Add(new CostEntry
                {
                    EntryDate = c.EntryDate, Kind = c.Kind, WorkOrderId = c.WorkOrderId, ContractId = c.ContractId, CostCenter = c.CostCenter,
                    ItemId = c.ItemId, Quantity = -c.Quantity, Amount = -c.Amount, SourceType = c.SourceType, SourceId = c.SourceId,
                    SourceNumber = c.SourceNumber, Description = "ابطال: " + c.Description, CreatedAt = DateTimeOffset.Now, CreatedByUserId = _user.UserId,
                });
            }

            // accounting events: one reversing event per original event (Sepidar gets the reversal, nothing is deleted)
            List<AccountingEvent> events = await _uow.Set<AccountingEvent>().Include(e => e.Lines)
                .Where(e => e.SourceType == InventoryDocumentSource && e.SourceId == document.Id && e.Type != AccountingEventType.Reversal)
                .ToListAsync(cancellationToken);
            foreach (AccountingEvent e in events)
            {
                if (e.Status == AccountingEventStatus.Pending || e.Status == AccountingEventStatus.Failed)
                {
                    e.Status = AccountingEventStatus.NotRequired; // never sent: nothing to reverse in Sepidar
                    e.Error = "سند مبدا باطل شد پیش از ارسال.";
                    continue;
                }
                if (e.Status == AccountingEventStatus.NotRequired) continue;
                AccountingEvent reversal = AddEvent(AccountingEventType.Reversal, DateTime.Today, e.SourceType, e.SourceId, e.SourceNumber,
                    "ابطال " + e.Description, e.Lines.Select(l => new AccountingEventLine
                    {
                        AccountRole = l.AccountRole, Debit = l.Credit, Credit = l.Debit, ProviderId = l.ProviderId, WorkOrderId = l.WorkOrderId,
                        ContractId = l.ContractId, CostCenter = l.CostCenter, ItemId = l.ItemId, Quantity = l.Quantity, Description = l.Description,
                    }).ToList());
                reversal.ReversesEventId = e.Id;
            }
            if (R(residuals) != 0)
            {
                decimal amount = R(residuals);
                AddEvent(AccountingEventType.Reversal, DateTime.Today, InventoryDocumentSource, document.Id, document.Number, "مانده ارزش پس از ابطال", new[]
                {
                    new AccountingEventLine { AccountRole = AccountRoles.Revaluation, Debit = amount > 0 ? amount : 0, Credit = amount < 0 ? -amount : 0 },
                    new AccountingEventLine { AccountRole = AccountRoles.Inventory, Debit = amount < 0 ? -amount : 0, Credit = amount > 0 ? amount : 0 },
                });
            }
        }

        public async Task EnsureAccountRolesAsync(CancellationToken cancellationToken)
        {
            DbSet<AccountMapping> set = _uow.Set<AccountMapping>();
            HashSet<string> existing = (await set.Select(m => m.Role).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (role, title) in AccountRoles.Standard())
                if (existing.Add(role)) set.Add(new AccountMapping { Role = role, Title = title, IsActive = true });
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }
}
