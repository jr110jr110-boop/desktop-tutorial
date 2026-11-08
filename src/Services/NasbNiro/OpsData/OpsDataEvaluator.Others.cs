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
            foreach (StockCountLine cl in c.Lines) AddMaker(doc, cl.CountedByUserId);   // the people who counted are makers of the count: they never approve its exception
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
                if (u != null) { u.Field = "qty"; ev.Findings.Add(u); }
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
            Dictionary<int, OpsEvaluation> all = await EvaluateOperationsAsync(new[] { operationId }, delta, ct);
            return all.TryGetValue(operationId, out OpsEvaluation e) ? e : null;
        }

        /// <summary>The operations of a list with a fixed number of queries (PB8 fix M10): the seat evaluates hundreds of them.</summary>
        public async Task<Dictionary<int, OpsEvaluation>> EvaluateOperationsAsync(IReadOnlyList<int> operationIds, OpsOperationDelta delta, CancellationToken ct)
        {
            var result = new Dictionary<int, OpsEvaluation>();
            List<int> ids = (operationIds ?? new List<int>()).Distinct().ToList();
            if (ids.Count == 0) return result;
            delta ??= new OpsOperationDelta();
            List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.Id != 0 && ids.Contains(o.Id) && !o.Deleted).ToListAsync(ct);
            if (ops.Count == 0) return result;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            List<int> orderIds = ops.Select(o => o.OrderId).Distinct().ToList();
            Dictionary<int, MfProductionOrder> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            var bookingRows = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && ids.Contains(b.OperationId)).Select(b => new { b.OperationId, b.Kind, b.Hours, b.CreatorUserId }).ToListAsync(ct);
            List<int> templates = ops.Where(o => o.OperationTemplateId.HasValue).Select(o => o.OperationTemplateId.Value).Distinct().ToList();
            var history = templates.Count == 0 ? null : await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && o.OperationTemplateId != null && templates.Contains(o.OperationTemplateId.Value) && o.Status == MfOperationStatus.Done && !o.IsRework)
                .Select(o => new { o.Id, Template = o.OperationTemplateId.Value, o.GoodQty, o.ScrapQty }).ToListAsync(ct);
            // earlier operations of the same orders that have not begun at all (overlapping or lot-split work is not "out of sequence")
            var siblings = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && orderIds.Contains(o.OrderId) && !o.IsRework && (o.Status == MfOperationStatus.Waiting || o.Status == MfOperationStatus.Ready) && o.GoodQty == 0 && o.ScrapQty == 0)
                .Select(o => new { o.OrderId, o.Sequence, o.Title }).ToListAsync(ct);
            var standards = templates.Count == 0 ? null : await _uow.Set<MfOperationStandard>().AsNoTracking().Where(s => !s.Deleted && templates.Contains(s.OperationTemplateId) && s.WorkCenterId != null)
                .Select(s => new { s.OperationTemplateId, Centre = s.WorkCenterId.Value }).Distinct().ToListAsync(ct);
            List<int> centreIds = (standards?.Select(s => s.Centre) ?? Enumerable.Empty<int>()).Concat(ops.Where(o => o.WorkCenterId.HasValue).Select(o => o.WorkCenterId.Value)).Distinct().ToList();
            Dictionary<int, string> centreTitles = centreIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => centreIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Title, ct);

            foreach (MfOperation op in ops)
            {
                orders.TryGetValue(op.OrderId, out MfProductionOrder order);
                var doc = new OpsDocRef { Type = OpsDocTypes.Operation, Id = op.Id, Number = (order?.Number ?? "?") + " / " + op.Sequence, Unit = OpsUnit.Production, Confirmed = op.Status == MfOperationStatus.Done };
                AddMaker(doc, op.CreatorUserId); AddMaker(doc, op.ModifierUserId);
                var bookings = bookingRows.Where(b => b.OperationId == op.Id).ToList();
                foreach (var b in bookings) AddMaker(doc, b.CreatorUserId);
                var ev = new OpsEvaluation { Doc = doc, Scope = new HashSet<string> { OpsRuleCodes.PrdTime, OpsRuleCodes.PrdScrap, OpsRuleCodes.BlOpcOp } };
                string title = op.Title ?? ("عملیات " + op.Sequence);
                OpsOperationDelta d = ids.Count == 1 ? delta : new OpsOperationDelta();

                // time against the standard of the routing (no standard = nothing to compare with)
                decimal setup = bookings.Where(b => b.Kind == MfBookingKind.Setup).Sum(b => b.Hours) + d.SetupHours;
                decimal run = bookings.Where(b => b.Kind == MfBookingKind.Run).Sum(b => b.Hours) + d.RunHours;
                RuleFinding f = OpsRules.ProductionTime(S(st, OpsRuleCodes.PrdTime), null, title, "آماده‌سازی", setup, op.SetupHoursPlanned);
                if (f != null) ev.Findings.Add(f);
                f = OpsRules.ProductionTime(S(st, OpsRuleCodes.PrdTime), 1, title, "اجرا", run, op.RunHoursPlanned);
                if (f != null) ev.Findings.Add(f);

                // scrap against the history of the same operation template
                decimal scrap = op.ScrapQty + d.Scrap;
                decimal total = op.GoodQty + op.ScrapQty + d.Good + d.Scrap;
                if (scrap > 0 && op.OperationTemplateId.HasValue && history != null)
                {
                    var hist = history.Where(h => h.Template == op.OperationTemplateId.Value && h.Id != op.Id).ToList();
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
                    List<string> before = siblings.Where(o => o.OrderId == op.OrderId && o.Sequence < op.Sequence).OrderBy(o => o.Sequence).Select(o => o.Title).ToList();
                    bool mismatch = false; string routingWc = null, usedWc = null;
                    if (op.OperationTemplateId.HasValue && op.WorkCenterId.HasValue && standards != null)
                    {
                        // alternative work centres: every centre that has a standard for this operation is the routing's (the first is only the preferred one)
                        List<int> stdCentres = standards.Where(s => s.OperationTemplateId == op.OperationTemplateId.Value).Select(s => s.Centre).ToList();
                        if (stdCentres.Count > 0 && !stdCentres.Contains(op.WorkCenterId.Value))
                        {
                            mismatch = true;
                            routingWc = string.Join("، ", stdCentres.Select(c => centreTitles.TryGetValue(c, out string t) ? t : "؟"));
                            usedWc = centreTitles.TryGetValue(op.WorkCenterId.Value, out string u) ? u : "؟";
                        }
                    }
                    f = OpsRules.OpcOperation(S(st, OpsRuleCodes.BlOpcOp), 3, title, order?.Number ?? "?", before, mismatch, routingWc, usedWc);
                    if (f != null) ev.Findings.Add(f);
                }
                result[op.Id] = ev;
            }
            return result;
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
            var ev = new OpsEvaluation { Doc = doc, Scope = new HashSet<string> { OpsRuleCodes.BlItp } };
            if (result != QcResult.Accept || !activityId.HasValue) return ev;
            ItpActivity activity = await _uow.Set<ItpActivity>().AsNoTracking().Include(a => a.Parameters).FirstOrDefaultAsync(a => a.Id == activityId.Value, ct);
            if (activity == null) return ev;
            // PB8 fix M6: the point is already released in the quality module (the MES refuses an acceptance before that), so its record carries the measurements; they are not required again here.
            // Only values the inspector types in the decision are held against the tolerance of the ITP.
            bool released = activity.Status == ItpActivityStatus.Done || activity.Status == ItpActivityStatus.Waived || activity.Status == ItpActivityStatus.NotApplicable;
            var missing = new List<string>();
            var outside = new List<string>();
            foreach (ItpActivityParameter p in (activity.Parameters ?? new List<ItpActivityParameter>()).Where(p => !p.RequiresLimits && (p.Min.HasValue || p.Max.HasValue)))
            {
                string key = Squash(p.Name);
                OpsQcMeasure m = (measurements ?? new List<OpsQcMeasure>()).FirstOrDefault(x => Squash(x.Characteristic) == key || (key.Length > 2 && Squash(x.Characteristic).Contains(key)));
                if (m == null) { if (!released) missing.Add(p.Name); continue; }
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
            Dictionary<int, OpsEvaluation> all = await EvaluateInvoicesAsync(new[] { invoiceId }, ct);
            return all.TryGetValue(invoiceId, out OpsEvaluation e) ? e : null;
        }

        /// <summary>The invoices of a list with a fixed number of queries (PB8 fix M10).</summary>
        public async Task<Dictionary<int, OpsEvaluation>> EvaluateInvoicesAsync(IReadOnlyList<int> invoiceIds, CancellationToken ct)
        {
            var result = new Dictionary<int, OpsEvaluation>();
            List<int> ids = (invoiceIds ?? new List<int>()).Distinct().ToList();
            if (ids.Count == 0) return result;
            List<SupplierInvoice> invoices = await _uow.Set<SupplierInvoice>().AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id) && !i.Deleted).ToListAsync(ct);
            if (invoices.Count == 0) return result;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);
            List<int> lineIds = invoices.SelectMany(i => i.Lines).Select(l => l.OrderLineId).Distinct().ToList();
            var orderLines = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.RowNumber, l.UnitPrice, l.ItemId, l.Description, ItemTitle = l.Item != null ? l.Item.Title : null }).ToDictionaryAsync(l => l.Id, ct);
            foreach (SupplierInvoice inv in invoices)
            {
                var doc = new OpsDocRef { Type = OpsDocTypes.Invoice, Id = inv.Id, Number = inv.Number, Unit = OpsUnit.Finance, Confirmed = inv.Status == SupplierInvoiceStatus.Approved };
                AddMaker(doc, inv.CreatorUserId); AddMaker(doc, inv.ModifierUserId); AddMaker(doc, inv.ApprovedByUserId);
                var ev = new OpsEvaluation { Doc = doc };
                int row = 0;
                foreach (SupplierInvoiceLine l in inv.Lines.OrderBy(x => x.Id))
                {
                    row++;
                    if (!orderLines.TryGetValue(l.OrderLineId, out var ol) || ol.UnitPrice <= 0) continue;
                    string title = ol.ItemTitle ?? ol.Description ?? ("ردیف سفارش " + ol.RowNumber);
                    RuleFinding u = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, row, ol.ItemId ?? 0, title, "فی فاکتور", l.UnitPrice, ol.UnitPrice, true);
                    if (u != null) { u.Field = "price"; ev.Findings.Add(u); continue; }
                    RuleFinding f = OpsRules.Price(OpsRuleCodes.InvPrice, S(st, OpsRuleCodes.InvPrice), l.Id, row, ol.ItemId ?? 0, title, l.UnitPrice, ol.UnitPrice, "فی سفارش خرید");
                    if (f != null) ev.Findings.Add(f);
                }
                // an approved invoice is dated as of its approval, not as of today
                DateTime today = doc.Confirmed && inv.ApprovedAt.HasValue ? inv.ApprovedAt.Value.LocalDateTime.Date : DateTime.Today;
                RuleFinding d = OpsRules.DateBack(S(st, OpsRuleCodes.Date), inv.InvoiceDate, today);
                if (d != null) ev.Findings.Add(d);
                result[inv.Id] = ev;
            }
            return result;
        }
    }

    internal static class OpsStockCountExtensions
    {
        // the stock count does not keep the id of the person who submitted it; the approvals are the only other actors it knows
        public static int? SubmittedByUserId(this StockCount c) => c.PostedByUserId;
    }
}
