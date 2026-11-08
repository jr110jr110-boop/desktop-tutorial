using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.OpsData
{
    public partial class OpsDataEvaluator
    {
        private static void AddMaker(OpsDocRef doc, int? user) { if (user.HasValue && user.Value > 0) doc.Makers.Add(user.Value); }

        // ================================================================================== stock counts

        public async Task<OpsEvaluation> EvaluateStockCountAsync(int countId, CancellationToken ct)
        {
            StockCount c = await _uow.Set<StockCount>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == countId && !x.Deleted, ct);
            if (c == null) return null;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            var doc = new OpsDocRef { Type = OpsDocTypes.StockCount, Id = c.Id, Number = c.Number, Unit = OpsUnit.Warehouse, Confirmed = c.Status == StockCountStatus.Posted };
            AddMaker(doc, c.CreatorUserId); AddMaker(doc, c.ModifierUserId); AddMaker(doc, c.SubmittedByUserId());
            var ev = new OpsEvaluation { Doc = doc };
            List<int> itemIds = c.Lines.Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            Dictionary<int, decimal> cost = await _uow.Set<ItemCost>().AsNoTracking().Where(x => itemIds.Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId, x => x.AverageCost, ct);
            int row = 0;
            foreach (StockCountLine l in c.Lines.OrderBy(x => x.Id))
            {
                row++;
                decimal? counted = l.RecountedQuantity ?? l.CountedQuantity;
                if (!counted.HasValue || !items.TryGetValue(l.ItemId, out InventoryItem item)) continue;
                decimal? unitCost = cost.TryGetValue(l.ItemId, out decimal uc) && uc > 0 ? uc : (decimal?)null;
                RuleFinding u = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, row, item.Id, item.Title, "مقدار شمارش‌شده", counted.Value, l.SystemQuantity);
                if (u != null) ev.Findings.Add(u);
                else
                {
                    RuleFinding f = OpsRules.CountVariance(S(st, OpsRuleCodes.CntVar), l.Id, row, item.Id, item.Title, item.Unit ?? string.Empty, l.SystemQuantity, counted.Value, unitCost);
                    if (f != null) ev.Findings.Add(f);
                }
            }
            return ev;
        }

        // ================================================================================== production operations

        public async Task<OpsEvaluation> EvaluateOperationAsync(int operationId, OpsOperationDelta delta, CancellationToken ct)
        {
            delta ??= new OpsOperationDelta();
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId && !o.Deleted, ct);
            if (op == null) return null;
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == op.OrderId, ct);
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            var doc = new OpsDocRef { Type = OpsDocTypes.Operation, Id = op.Id, Number = (order?.Number ?? "?") + " / " + op.Sequence, Unit = OpsUnit.Production, Confirmed = op.Status == MfOperationStatus.Done };
            AddMaker(doc, op.CreatorUserId); AddMaker(doc, op.ModifierUserId);
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.OperationId == op.Id).Select(b => new { b.Kind, b.Hours, b.CreatorUserId }).ToListAsync(ct);
            foreach (var b in bookings) AddMaker(doc, b.CreatorUserId);
            var ev = new OpsEvaluation { Doc = doc };
            string title = op.Title ?? ("عملیات " + op.Sequence);

            // time against the standard of the routing
            decimal setup = bookings.Where(b => b.Kind == MfBookingKind.Setup).Sum(b => b.Hours) + delta.SetupHours;
            decimal run = bookings.Where(b => b.Kind == MfBookingKind.Run).Sum(b => b.Hours) + delta.RunHours;
            RuleFinding f = OpsRules.ProductionTime(S(st, OpsRuleCodes.PrdTime), null, title, "آماده‌سازی", setup, op.SetupHoursPlanned);
            if (f != null) ev.Findings.Add(f);
            f = OpsRules.ProductionTime(S(st, OpsRuleCodes.PrdTime), 1, title, "اجرا", run, op.RunHoursPlanned);
            if (f != null) ev.Findings.Add(f);

            // scrap against the history of the same operation template
            decimal scrap = op.ScrapQty + delta.Scrap;
            decimal total = op.GoodQty + op.ScrapQty + delta.Good + delta.Scrap;
            if (scrap > 0 && op.OperationTemplateId.HasValue)
            {
                var hist = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && o.OperationTemplateId == op.OperationTemplateId && o.Id != op.Id && o.Status == MfOperationStatus.Done && !o.IsRework)
                    .Select(o => new { o.GoodQty, o.ScrapQty }).ToListAsync(ct);
                decimal all = hist.Sum(h => h.GoodQty + h.ScrapQty);
                if (hist.Count > 0 && all > 0)
                {
                    f = OpsRules.ProductionScrap(S(st, OpsRuleCodes.PrdScrap), 2, title, scrap, Math.Max(total, order?.Quantity ?? 0), hist.Sum(h => h.ScrapQty) / all, hist.Count);
                    if (f != null) ev.Findings.Add(f);
                }
            }

            // sequence and work centre of the routing
            if (!op.IsRework)
            {
                List<string> before = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && o.OrderId == op.OrderId && !o.IsRework && o.Sequence < op.Sequence
                    && o.Status != MfOperationStatus.Done && o.Status != MfOperationStatus.Skipped).OrderBy(o => o.Sequence).Select(o => o.Title).ToListAsync(ct);
                bool mismatch = false; string routingWc = null, usedWc = null;
                if (op.OperationTemplateId.HasValue && op.WorkCenterId.HasValue)
                {
                    int? stdWc = await _uow.Set<MfOperationStandard>().AsNoTracking().Where(s => !s.Deleted && s.OperationTemplateId == op.OperationTemplateId && s.WorkCenterId != null).Select(s => s.WorkCenterId).FirstOrDefaultAsync(ct);
                    if (stdWc.HasValue && stdWc.Value != op.WorkCenterId.Value)
                    {
                        mismatch = true;
                        var centres = await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => w.Id == stdWc.Value || w.Id == op.WorkCenterId.Value).Select(w => new { w.Id, w.Title }).ToListAsync(ct);
                        routingWc = centres.FirstOrDefault(w => w.Id == stdWc.Value)?.Title; usedWc = centres.FirstOrDefault(w => w.Id == op.WorkCenterId.Value)?.Title;
                    }
                }
                f = OpsRules.OpcOperation(S(st, OpsRuleCodes.BlOpcOp), 3, title, order?.Number ?? "?", before, mismatch, routingWc, usedWc);
                if (f != null) ev.Findings.Add(f);
            }
            return ev;
        }

        public async Task<OpsEvaluation> EvaluateQcDecisionAsync(int operationId, QcResult result, int? activityId, IReadOnlyList<OpsQcMeasure> measurements, CancellationToken ct)
        {
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId && !o.Deleted, ct);
            if (op == null) return null;
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == op.OrderId, ct);
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            var doc = new OpsDocRef { Type = OpsDocTypes.Operation, Id = op.Id, Number = (order?.Number ?? "?") + " / " + op.Sequence, Unit = OpsUnit.Quality, Confirmed = false };
            AddMaker(doc, op.CreatorUserId); AddMaker(doc, op.ModifierUserId);
            foreach (int b in await _uow.Set<MfTimeBooking>().AsNoTracking().Where(x => !x.Deleted && x.OperationId == op.Id).Select(x => x.CreatorUserId).Distinct().ToListAsync(ct)) AddMaker(doc, b);
            var ev = new OpsEvaluation { Doc = doc };
            if (result != QcResult.Accept || !activityId.HasValue) return ev;
            ItpActivity activity = await _uow.Set<ItpActivity>().AsNoTracking().Include(a => a.Parameters).FirstOrDefaultAsync(a => a.Id == activityId.Value, ct);
            if (activity == null) return ev;
            var missing = new List<string>();
            var outside = new List<string>();
            foreach (ItpActivityParameter p in (activity.Parameters ?? new List<ItpActivityParameter>()).Where(p => !p.RequiresLimits && (p.Min.HasValue || p.Max.HasValue)))
            {
                string key = Squash(p.Name);
                OpsQcMeasure m = (measurements ?? new List<OpsQcMeasure>()).FirstOrDefault(x => Squash(x.Characteristic) == key || (key.Length > 2 && Squash(x.Characteristic).Contains(key)));
                if (m == null) { missing.Add(p.Name); continue; }
                if ((p.Min.HasValue && m.Measured < p.Min.Value) || (p.Max.HasValue && m.Measured > p.Max.Value)) outside.Add(p.Name);
            }
            RuleFinding f = OpsRules.ItpCoverage(S(st, OpsRuleCodes.BlItp), 4, activity.Activity ?? "ITP", missing, outside);
            if (f != null) ev.Findings.Add(f);
            return ev;
        }

        private static string Squash(string s) => new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        // ================================================================================== incoming inspection

        public async Task<OpsEvaluation> EvaluateInspectionAsync(int inspectionId, decimal? accepted, decimal? rejected, CancellationToken ct)
        {
            QcInspection q = await _uow.Set<QcInspection>().AsNoTracking().Include(x => x.Item).Include(x => x.ReceiptDocument).FirstOrDefaultAsync(x => x.Id == inspectionId && !x.Deleted, ct);
            if (q == null) return null;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            var doc = new OpsDocRef { Type = OpsDocTypes.Inspection, Id = q.Id, Number = q.Number, Unit = OpsUnit.Quality, Confirmed = q.Status == InspectionStatus.Done };
            AddMaker(doc, q.CreatorUserId); AddMaker(doc, q.ModifierUserId); AddMaker(doc, q.InspectedByUserId);
            var ev = new OpsEvaluation { Doc = doc };
            decimal rej = rejected ?? q.RejectedQuantity;
            if (rej <= 0) return ev;
            int? provider = q.ReceiptDocument?.ProviderId;
            var hist = await _uow.Set<QcInspection>().AsNoTracking().Where(x => !x.Deleted && x.Id != q.Id && x.ItemId == q.ItemId && x.Status == InspectionStatus.Done
                    && (provider == null || x.ReceiptDocument.ProviderId == provider)).Select(x => new { x.Quantity, x.RejectedQuantity }).ToListAsync(ct);
            decimal all = hist.Sum(h => h.Quantity);
            if (hist.Count > 0 && all > 0)
            {
                RuleFinding f = OpsRules.QcRejected(S(st, OpsRuleCodes.QcReject), null, q.ItemId, q.Item?.Title ?? "کالا", rej, q.Quantity, hist.Sum(h => h.RejectedQuantity) / all, hist.Count);
                if (f != null) ev.Findings.Add(f);
            }
            return ev;
        }

        // ================================================================================== supplier invoices

        public async Task<OpsEvaluation> EvaluateInvoiceAsync(int invoiceId, CancellationToken ct)
        {
            SupplierInvoice inv = await _uow.Set<SupplierInvoice>().AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && !i.Deleted, ct);
            if (inv == null) return null;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            var doc = new OpsDocRef { Type = OpsDocTypes.Invoice, Id = inv.Id, Number = inv.Number, Unit = OpsUnit.Finance, Confirmed = inv.Status == SupplierInvoiceStatus.Approved };
            AddMaker(doc, inv.CreatorUserId); AddMaker(doc, inv.ModifierUserId); AddMaker(doc, inv.ApprovedByUserId);
            var ev = new OpsEvaluation { Doc = doc };
            List<int> lineIds = inv.Lines.Select(l => l.OrderLineId).Distinct().ToList();
            var orderLines = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.RowNumber, l.UnitPrice, l.ItemId, l.Description, ItemTitle = l.Item != null ? l.Item.Title : null }).ToDictionaryAsync(l => l.Id, ct);
            int row = 0;
            foreach (SupplierInvoiceLine l in inv.Lines.OrderBy(x => x.Id))
            {
                row++;
                if (!orderLines.TryGetValue(l.OrderLineId, out var ol) || ol.UnitPrice <= 0) continue;
                string title = ol.ItemTitle ?? ol.Description ?? ("ردیف سفارش " + ol.RowNumber);
                RuleFinding u = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, row, ol.ItemId ?? 0, title, "فی فاکتور", l.UnitPrice, ol.UnitPrice, true);
                if (u != null) { ev.Findings.Add(u); continue; }
                RuleFinding f = OpsRules.Price(OpsRuleCodes.InvPrice, S(st, OpsRuleCodes.InvPrice), l.Id, row, ol.ItemId ?? 0, title, l.UnitPrice, ol.UnitPrice, "فی سفارش خرید");
                if (f != null) ev.Findings.Add(f);
            }
            RuleFinding d = OpsRules.DateBack(S(st, OpsRuleCodes.Date), inv.InvoiceDate, DateTime.Today);
            if (d != null) ev.Findings.Add(d);
            return ev;
        }
    }

    internal static class OpsStockCountExtensions
    {
        // the stock count does not keep the id of the person who submitted it; the approvals are the only other actors it knows
        public static int? SubmittedByUserId(this StockCount c) => c.PostedByUserId;
    }
}
