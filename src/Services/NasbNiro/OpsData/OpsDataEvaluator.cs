using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.OpsData
{
    /// <summary>A booking, a scrap quantity or a QC decision that is about to be saved: the rule runs on the data as it will be.</summary>
    public sealed class OpsOperationDelta
    {
        public decimal SetupHours { get; set; }
        public decimal RunHours { get; set; }
        public decimal Scrap { get; set; }
        public decimal Good { get; set; }
    }

    public sealed class OpsQcMeasure
    {
        public string Characteristic { get; set; }
        public decimal Measured { get; set; }
    }

    public interface IOpsDataEvaluator
    {
        /// <summary>Evaluates warehouse documents with a fixed number of queries per rule family, whatever the number of documents.</summary>
        Task<Dictionary<int, OpsEvaluation>> EvaluateInventoryAsync(IReadOnlyList<int> documentIds, DateTime today, CancellationToken ct);

        Task<OpsEvaluation> EvaluateStockCountAsync(int countId, CancellationToken ct);
        Task<OpsEvaluation> EvaluateOperationAsync(int operationId, OpsOperationDelta delta, CancellationToken ct);
        Task<OpsEvaluation> EvaluateQcDecisionAsync(int operationId, QcResult result, int? activityId, IReadOnlyList<OpsQcMeasure> measurements, CancellationToken ct);
        Task<OpsEvaluation> EvaluateInspectionAsync(int inspectionId, decimal? accepted, decimal? rejected, CancellationToken ct);
        Task<OpsEvaluation> EvaluateInvoiceAsync(int invoiceId, CancellationToken ct);

        /// <summary>By document type (the endpoint and the seat).</summary>
        Task<OpsEvaluation> EvaluateAsync(string documentType, int documentId, CancellationToken ct);

        Task<IReadOnlyDictionary<string, decimal>> SettingsAsync(CancellationToken ct);
    }

    /// <summary>
    /// Reads the facts the rules need (batched, no query per line) and runs the pure rule functions of <see cref="OpsRules"/>. It writes nothing.
    /// </summary>
    public partial class OpsDataEvaluator : IOpsDataEvaluator
    {
        private readonly IUnitOfWork _uow;

        public OpsDataEvaluator(IUnitOfWork uow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        public async Task<IReadOnlyDictionary<string, decimal>> SettingsAsync(CancellationToken ct) => await QualitySettingKeys.LoadAsync(_uow, ct);

        private static OpsRuleSetting S(IReadOnlyDictionary<string, decimal> all, string code) => OpsRuleCatalog.SettingOf(all, code);

        public Task<OpsEvaluation> EvaluateAsync(string documentType, int documentId, CancellationToken ct)
        {
            switch (documentType)
            {
                case OpsDocTypes.InventoryDocument:
                    return EvaluateOneInventoryAsync(documentId, ct);
                case OpsDocTypes.StockCount: return EvaluateStockCountAsync(documentId, ct);
                case OpsDocTypes.Operation: return EvaluateOperationAsync(documentId, null, ct);
                case OpsDocTypes.Inspection: return EvaluateInspectionAsync(documentId, null, null, ct);
                case OpsDocTypes.Invoice: return EvaluateInvoiceAsync(documentId, ct);
                default: return Task.FromResult<OpsEvaluation>(null);
            }
        }

        private async Task<OpsEvaluation> EvaluateOneInventoryAsync(int id, CancellationToken ct)
        {
            Dictionary<int, OpsEvaluation> all = await EvaluateInventoryAsync(new[] { id }, DateTime.Today, ct);
            return all.TryGetValue(id, out OpsEvaluation e) ? e : null;
        }

        // ================================================================================== warehouse documents

        /// <summary>The types a person enters: only these can be blocked. Documents the system makes from other flows give warnings, so no automatic flow stalls.</summary>
        private static bool IsHumanType(InventoryDocumentType t) =>
            t == InventoryDocumentType.PurchaseReceipt || t == InventoryDocumentType.Issue || t == InventoryDocumentType.Transfer || t == InventoryDocumentType.SupplierReturn ||
            t == InventoryDocumentType.AdjustmentIncrease || t == InventoryDocumentType.AdjustmentDecrease || t == InventoryDocumentType.SubcontractIssue ||
            t == InventoryDocumentType.SubcontractReturn || t == InventoryDocumentType.CustomerDelivery || t == InventoryDocumentType.ConsignmentReceipt ||
            t == InventoryDocumentType.ConsignmentIssue || t == InventoryDocumentType.CustomerPropertyReceipt || t == InventoryDocumentType.ScrapSale;

        public async Task<Dictionary<int, OpsEvaluation>> EvaluateInventoryAsync(IReadOnlyList<int> documentIds, DateTime today, CancellationToken ct)
        {
            var result = new Dictionary<int, OpsEvaluation>();
            List<int> ids = (documentIds ?? new List<int>()).Distinct().ToList();
            if (ids.Count == 0) return result;
            IReadOnlyDictionary<string, decimal> st = await SettingsAsync(ct);

            List<InventoryDocument> docs = await _uow.Set<InventoryDocument>().AsNoTracking().Include(d => d.Lines).Where(d => ids.Contains(d.Id) && !d.Deleted).ToListAsync(ct);
            if (docs.Count == 0) return result;
            List<int> itemIds = docs.SelectMany(d => d.Lines).Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

            // purchase order lines of the receipts
            List<int> poLineIds = docs.SelectMany(d => d.Lines).Where(l => l.PurchaseOrderLineId.HasValue).Select(l => l.PurchaseOrderLineId.Value).Distinct().ToList();
            Dictionary<int, PurchaseOrderLine> poLines = poLineIds.Count == 0 ? new Dictionary<int, PurchaseOrderLine>()
                : await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

            // price history: the last receipts of each item (this document excluded) and the weighted average cost
            Dictionary<int, List<decimal>> history = await LastReceiptPricesAsync(docs.Where(d => d.Type == InventoryDocumentType.PurchaseReceipt).SelectMany(d => d.Lines).Select(l => l.ItemId).Distinct().ToList(), ids, ct);
            Dictionary<int, decimal> averageCost = itemIds.Count == 0 ? new Dictionary<int, decimal>()
                : await _uow.Set<ItemCost>().AsNoTracking().Where(c => itemIds.Contains(c.ItemId) && c.AverageCost > 0).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);

            OpsIssueFacts issueFacts = await LoadIssueFactsAsync(docs, items, ct);
            OpsStageFacts stageFacts = await LoadStageFactsAsync(items.Values, ct);
            HashSet<int> makerIds = new HashSet<int>();

            foreach (InventoryDocument d in docs)
            {
                var doc = new OpsDocRef
                {
                    Type = OpsDocTypes.InventoryDocument, Id = d.Id, Number = d.Number ?? ("پیش‌نویس " + d.Id), Unit = OpsUnit.Warehouse, Confirmed = d.Status == InventoryDocumentStatus.Confirmed,
                    Blockable = IsHumanType(d.Type) && !d.NcrId.HasValue && !d.InspectionId.HasValue && !d.StockCountId.HasValue,
                };
                foreach (int? u in new[] { (int?)d.CreatorUserId, d.ModifierUserId, d.ConfirmedByUserId }) if (u.HasValue && u.Value > 0) doc.Makers.Add(u.Value);
                var ev = new OpsEvaluation { Doc = doc };
                void Add(RuleFinding f) { if (f != null) ev.Findings.Add(f); }

                Add(OpsRules.DateBack(S(st, OpsRuleCodes.Date), d.DocumentDate, today));
                Add(OpsRules.DateForward(S(st, OpsRuleCodes.DateFwd), d.DocumentDate, today));

                foreach (InventoryDocumentLine l in d.Lines.OrderBy(x => x.RowNumber))
                {
                    if (!items.TryGetValue(l.ItemId, out InventoryItem item)) continue;
                    string title = item.Title ?? item.Code;
                    string unit = item.Unit ?? string.Empty;
                    PurchaseOrderLine po = l.PurchaseOrderLineId.HasValue && poLines.TryGetValue(l.PurchaseOrderLineId.Value, out PurchaseOrderLine p) ? p : null;
                    bool receipt = d.Type == InventoryDocumentType.PurchaseReceipt || d.Type == InventoryDocumentType.ConsignmentReceipt;

                    if (receipt)
                    {
                        // quantity against the open quantity of the order line (this document's own part is not yet in ReceivedQuantity while it is a draft)
                        RuleFinding uom = null, qty = null;
                        if (po != null && d.Type == InventoryDocumentType.PurchaseReceipt)
                        {
                            decimal already = po.ReceivedQuantity - (doc.Confirmed ? d.Lines.Where(x => x.PurchaseOrderLineId == po.Id).Sum(x => x.Quantity) : 0m);
                            decimal open = po.Quantity - already;
                            decimal mine = d.Lines.Where(x => x.PurchaseOrderLineId == po.Id).Sum(x => x.Quantity);
                            uom = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, l.RowNumber, item.Id, title, "مقدار رسید", l.Quantity, Math.Max(open, 0m));
                            qty = uom == null && l.Id == d.Lines.Where(x => x.PurchaseOrderLineId == po.Id).OrderBy(x => x.RowNumber).First().Id
                                ? OpsRules.RcvQty(S(st, OpsRuleCodes.RcvQty), l.Id, l.RowNumber, item.Id, title, unit, mine, open) : null;
                        }
                        Add(uom); Add(qty);

                        // weight
                        decimal? expectedKg = FreightRules.WeightKg(unit, l.Quantity, item.Weight);
                        RuleFinding wUom = l.WeighedWeightKg.HasValue && expectedKg.HasValue
                            ? OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, l.RowNumber, item.Id, title, "وزن توزین‌شده", l.WeighedWeightKg.Value, expectedKg.Value) : null;
                        Add(wUom);
                        if (wUom == null) Add(OpsRules.RcvWeight(S(st, OpsRuleCodes.RcvWeight), l.Id, l.RowNumber, item.Id, title, l.WeighedWeightKg, l.CertificateWeightKg, expectedKg));

                        if (d.Type == InventoryDocumentType.PurchaseReceipt)
                        {
                            // price: the order price is the price of the receipt; a typed price that differs is told
                            decimal? effective = po != null ? po.UnitPrice : l.UnitPrice;
                            if (po != null && l.UnitPrice.HasValue && l.UnitPrice.Value > 0 && !doc.Confirmed)
                                Add(OpsRules.TypedPriceDiffersFromOrder(S(st, OpsRuleCodes.RcvPrice), l.Id, l.RowNumber, item.Id, title, l.UnitPrice.Value, po.UnitPrice));
                            if (effective.HasValue && effective.Value > 0)
                            {
                                (decimal? reference, string text) = ReferencePrice(history, averageCost, item.Id, (int)S(st, OpsRuleCodes.RcvPrice).MinHistory);
                                if (reference.HasValue)
                                {
                                    RuleFinding pUom = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, l.RowNumber, item.Id, title, "فی", effective.Value, reference.Value, true);
                                    Add(pUom);
                                    if (pUom == null) Add(OpsRules.Price(OpsRuleCodes.RcvPrice, S(st, OpsRuleCodes.RcvPrice), l.Id, l.RowNumber, item.Id, title, effective.Value, reference.Value, text));
                                }
                            }
                            // certificate / material against the specification
                            bool certMissing = item.RequiredCertificateType.HasValue && string.IsNullOrWhiteSpace(l.CertificateNumber) && string.IsNullOrWhiteSpace(l.HeatNumber) && !l.LotId.HasValue;
                            string expectedMaterial = issueFacts.BomMaterial(d.WorkOrderId, item.Id);
                            Add(OpsRules.Spec(S(st, OpsRuleCodes.BlSpec), l.Id, l.RowNumber, item.Id, title, expectedMaterial, expectedMaterial == null || string.IsNullOrWhiteSpace(item.Material) ? null : item.Material, certMissing));
                        }
                    }

                    if (d.Type == InventoryDocumentType.Issue)
                    {
                        string wo = d.WorkOrderId.HasValue && issueFacts.WorkOrderCode.TryGetValue(d.WorkOrderId.Value, out string code) ? code : (d.WorkOrderId?.ToString() ?? "-");
                        int? woId = d.WorkOrderId ?? issueFacts.RequestWorkOrder(d.MaterialRequestId);
                        decimal mineSum = d.Lines.Where(x => x.ItemId == l.ItemId).Sum(x => x.Quantity);
                        bool firstOfItem = d.Lines.Where(x => x.ItemId == l.ItemId).OrderBy(x => x.RowNumber).First().Id == l.Id;
                        if (firstOfItem)
                        {
                            // standard requirement of the kit
                            decimal? standard = issueFacts.KitStandard(d.MaterialRequestId, l.ItemId);
                            decimal issuedSoFar = issueFacts.KitIssued(d.MaterialRequestId, l.ItemId);
                            decimal cumulativeKit = doc.Confirmed ? issuedSoFar : issuedSoFar + mineSum;
                            if (standard.HasValue)
                            {
                                RuleFinding u = OpsRules.UnitMismatch(S(st, OpsRuleCodes.Uom), l.Id, l.RowNumber, item.Id, title, "مقدار حواله", mineSum, standard.Value);
                                Add(u);
                                if (u == null) Add(OpsRules.IssueAgainstKit(S(st, OpsRuleCodes.IssQty), l.Id, l.RowNumber, item.Id, title, unit, cumulativeKit, standard.Value));
                            }
                            // ceiling and BOM quantity: the net consumption of the work order
                            if (woId.HasValue)
                            {
                                decimal net = issueFacts.NetIssued(woId.Value, item.Id);
                                decimal cumulative = doc.Confirmed ? net : net + mineSum;
                                decimal? ceiling = issueFacts.Ceiling(woId.Value, item.Id);
                                if (ceiling.HasValue) Add(OpsRules.IssueAgainstCeiling(S(st, OpsRuleCodes.IssCeil), l.Id, l.RowNumber, item.Id, title, unit, cumulative, ceiling.Value));
                                if (issueFacts.HasBaseline(woId.Value))
                                {
                                    bool inBom = issueFacts.InBaseline(woId.Value, item);
                                    Add(OpsRules.BomItem(S(st, OpsRuleCodes.BlBomItem), l.Id, l.RowNumber, item.Id, title, wo, inBom));
                                    decimal? bomQty = inBom ? issueFacts.BomQuantity(woId.Value, item.Id) : null;
                                    if (bomQty.HasValue) Add(OpsRules.BomQuantity(S(st, OpsRuleCodes.BlBomQty), l.Id, l.RowNumber, item.Id, title, unit, cumulative, bomQty.Value));
                                }
                            }
                        }
                    }

                    // the stage suffix of a part against its OPC routing
                    if ((d.Type == InventoryDocumentType.ProductionReceipt || d.Type == InventoryDocumentType.Transfer || d.Type == InventoryDocumentType.StageConversion) && !string.IsNullOrEmpty(item.StageCode2) && !string.IsNullOrEmpty(item.PartNumber))
                    {
                        OpsStageCheck sc = stageFacts.Check(item);
                        Add(OpsRules.OpcStage(S(st, OpsRuleCodes.BlOpcStage), l.Id, l.RowNumber, item.Id, title, item.PartNumber, item.StageCode2, sc.RoutingExists, sc.StageInRouting, sc.PreviousStage, sc.PreviousDone));
                    }
                }

                // the activity of the kit request must be open
                if (d.Type == InventoryDocumentType.Issue && d.MaterialRequestId.HasValue)
                {
                    OpsActivityFact act = issueFacts.Activity(d.MaterialRequestId.Value);
                    if (act != null) Add(OpsRules.Wbs(S(st, OpsRuleCodes.BlWbs), null, act.Title, act.Exists, act.Open));
                }
                result[d.Id] = ev;
            }
            return result;
        }

        private static (decimal? Price, string Text) ReferencePrice(Dictionary<int, List<decimal>> history, Dictionary<int, decimal> averageCost, int itemId, int minHistory)
        {
            if (history.TryGetValue(itemId, out List<decimal> prices) && prices.Count >= Math.Max(1, minHistory))
                return (Math.Round(prices.Average(), 2), $"میانگین {prices.Count} رسید آخر");
            if (averageCost.TryGetValue(itemId, out decimal avg) && avg > 0) return (avg, "میانگین موزون بهای کالا");
            return (null, null);
        }

        /// <summary>The prices of the last three confirmed receipts of each item, newest first (documents in <paramref name="excludeDocuments"/> left out).</summary>
        private async Task<Dictionary<int, List<decimal>>> LastReceiptPricesAsync(List<int> itemIds, List<int> excludeDocuments, CancellationToken ct)
        {
            var result = new Dictionary<int, List<decimal>>();
            if (itemIds.Count == 0) return result;
            var rows = await _uow.Set<StockTransaction>().AsNoTracking()
                .Where(t => itemIds.Contains(t.ItemId) && t.Quantity > 0 && !t.IsReversal && t.UnitPrice != null && t.UnitPrice > 0 && !excludeDocuments.Contains(t.DocumentId)
                            && t.Document.Type == InventoryDocumentType.PurchaseReceipt && t.Document.Status == InventoryDocumentStatus.Confirmed)
                .OrderByDescending(t => t.Id).Select(t => new { t.ItemId, t.DocumentId, Price = t.UnitPrice.Value }).Take(Math.Max(200, itemIds.Count * 40)).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.ItemId))
            {
                // one price per document (a document can have several lines of the item), the newest three documents
                result[g.Key] = g.GroupBy(x => x.DocumentId).Take(3).Select(x => x.First().Price).ToList();
            }
            return result;
        }
    }
}
