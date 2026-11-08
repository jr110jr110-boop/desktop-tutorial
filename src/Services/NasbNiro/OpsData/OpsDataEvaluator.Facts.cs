using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.OpsData
{
    public sealed class OpsActivityFact
    {
        public string Title { get; set; }
        public bool Exists { get; set; }
        public bool Open { get; set; }
    }

    public sealed class OpsStageCheck
    {
        public bool RoutingExists { get; set; }
        public bool StageInRouting { get; set; }
        public string PreviousStage { get; set; }
        public bool PreviousDone { get; set; } = true;
    }

    /// <summary>Everything the issue rules read about a set of warehouse documents, loaded in batches and answered from memory.</summary>
    public sealed class OpsIssueFacts
    {
        public Dictionary<int, string> WorkOrderCode { get; } = new Dictionary<int, string>();
        public Dictionary<int, int?> RequestWorkOrderMap { get; } = new Dictionary<int, int?>();
        public Dictionary<int, Dictionary<int, decimal>> KitStandards { get; } = new Dictionary<int, Dictionary<int, decimal>>();
        public Dictionary<int, Dictionary<int, decimal>> KitIssuedMap { get; } = new Dictionary<int, Dictionary<int, decimal>>();
        public Dictionary<(int Wo, int Item), decimal> Net { get; } = new Dictionary<(int, int), decimal>();
        public Dictionary<(int Wo, int Item), decimal> Ceilings { get; } = new Dictionary<(int, int), decimal>();
        public Dictionary<int, decimal> GlobalCeilings { get; } = new Dictionary<int, decimal>();
        public HashSet<int> WorkOrdersWithBaseline { get; } = new HashSet<int>();
        public Dictionary<int, HashSet<int>> BaselineItems { get; } = new Dictionary<int, HashSet<int>>();
        public Dictionary<int, HashSet<string>> BaselineParts { get; } = new Dictionary<int, HashSet<string>>();
        public Dictionary<int, Dictionary<int, decimal>> BaselineQty { get; } = new Dictionary<int, Dictionary<int, decimal>>();
        public Dictionary<int, Dictionary<int, string>> BaselineMaterial { get; } = new Dictionary<int, Dictionary<int, string>>();
        public Dictionary<int, OpsActivityFact> RequestActivity { get; } = new Dictionary<int, OpsActivityFact>();

        public int? RequestWorkOrder(int? requestId) => requestId.HasValue && RequestWorkOrderMap.TryGetValue(requestId.Value, out int? w) ? w : null;
        public decimal? KitStandard(int? requestId, int itemId) => requestId.HasValue && KitStandards.TryGetValue(requestId.Value, out var m) && m.TryGetValue(itemId, out decimal q) ? q : (decimal?)null;
        public decimal KitIssued(int? requestId, int itemId) => requestId.HasValue && KitIssuedMap.TryGetValue(requestId.Value, out var m) && m.TryGetValue(itemId, out decimal q) ? q : 0m;
        public decimal NetIssued(int wo, int item) => Net.TryGetValue((wo, item), out decimal q) ? q : 0m;
        public decimal? Ceiling(int wo, int item) => Ceilings.TryGetValue((wo, item), out decimal q) ? q : GlobalCeilings.TryGetValue(item, out decimal g) ? g : (decimal?)null;
        public bool HasBaseline(int wo) => WorkOrdersWithBaseline.Contains(wo);

        public bool InBaseline(int wo, InventoryItem item) =>
            (BaselineItems.TryGetValue(wo, out var ids) && ids.Contains(item.Id)) || (!string.IsNullOrEmpty(item.PartNumber) && BaselineParts.TryGetValue(wo, out var parts) && parts.Contains(item.PartNumber));

        public decimal? BomQuantity(int wo, int itemId) => BaselineQty.TryGetValue(wo, out var m) && m.TryGetValue(itemId, out decimal q) && q > 0 ? q : (decimal?)null;
        public string BomMaterial(int? wo, int itemId) => wo.HasValue && BaselineMaterial.TryGetValue(wo.Value, out var m) && m.TryGetValue(itemId, out string s) ? s : null;
        public OpsActivityFact Activity(int requestId) => RequestActivity.TryGetValue(requestId, out OpsActivityFact a) ? a : null;
    }

    public sealed class OpsStageFacts
    {
        // part number -> stage codes of the approved routing in sequence
        public Dictionary<string, List<string>> Sequence { get; } = new Dictionary<string, List<string>>();
        public HashSet<(string Part, string Stage)> StagesWithStock { get; } = new HashSet<(string, string)>();
        public HashSet<(string Part, string Stage)> ExistingStageItems { get; } = new HashSet<(string, string)>();
        public HashSet<string> StockableStages { get; } = new HashSet<string>();

        public OpsStageCheck Check(InventoryItem item)
        {
            var r = new OpsStageCheck();
            if (!Sequence.TryGetValue(item.PartNumber, out List<string> stages) || stages.Count == 0) return r;
            r.RoutingExists = true;
            int at = stages.IndexOf(item.StageCode2);
            r.StageInRouting = at >= 0;
            if (at > 0)
            {
                string prev = stages[at - 1];
                r.PreviousStage = prev;
                // only a stage that holds stock of its own can be checked, and only when its item exists (otherwise nothing says it did not happen)
                r.PreviousDone = !StockableStages.Contains(prev) || !ExistingStageItems.Contains((item.PartNumber, prev)) || StagesWithStock.Contains((item.PartNumber, prev));
            }
            return r;
        }
    }

    public partial class OpsDataEvaluator
    {
        private async Task<OpsIssueFacts> LoadIssueFactsAsync(List<InventoryDocument> docs, Dictionary<int, InventoryItem> items, CancellationToken ct)
        {
            var f = new OpsIssueFacts();
            List<InventoryDocument> issues = docs.Where(d => d.Type == InventoryDocumentType.Issue).ToList();
            List<int> woIds = docs.Where(d => d.WorkOrderId.HasValue).Select(d => d.WorkOrderId.Value).Distinct().ToList();
            List<int> requestIds = issues.Where(d => d.MaterialRequestId.HasValue).Select(d => d.MaterialRequestId.Value).Distinct().ToList();

            List<MaterialRequest> requests = requestIds.Count == 0 ? new List<MaterialRequest>()
                : await _uow.Set<MaterialRequest>().AsNoTracking().Include(r => r.Lines).Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
            foreach (MaterialRequest r in requests)
            {
                f.RequestWorkOrderMap[r.Id] = r.WorkOrderId;
                if (r.WorkOrderId.HasValue && !woIds.Contains(r.WorkOrderId.Value)) woIds.Add(r.WorkOrderId.Value);
                f.KitIssuedMap[r.Id] = r.Lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.IssuedQuantity));
            }
            if (woIds.Count > 0)
            {
                var wos = await _uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct);
                foreach (var w in wos) f.WorkOrderCode[w.Id] = w.Code;
            }

            // the standard requirement: the components of the production order the request is the kit of
            if (requestIds.Count > 0)
            {
                var orders = await _uow.Set<Entities.Model.NasbNiro.Production.MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.KitRequestId != null && requestIds.Contains(o.KitRequestId.Value))
                    .Select(o => new { o.Id, Kit = o.KitRequestId.Value }).ToListAsync(ct);
                List<int> orderIds = orders.Select(o => o.Id).ToList();
                var comps = orderIds.Count == 0 ? null : await _uow.Set<Entities.Model.NasbNiro.Production.MfOrderComponent>().AsNoTracking().Where(c => orderIds.Contains(c.OrderId)).ToListAsync(ct);
                if (comps != null)
                    foreach (var o in orders)
                        f.KitStandards[o.Kit] = comps.Where(c => c.OrderId == o.Id).GroupBy(c => c.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            }

            List<int> issueItemIds = issues.SelectMany(d => d.Lines).Select(l => l.ItemId).Distinct().ToList();
            if (woIds.Count > 0 && issueItemIds.Count > 0)
            {
                var net = await _uow.Set<StockTransaction>().AsNoTracking()
                    .Where(t => issueItemIds.Contains(t.ItemId) && t.Document.WorkOrderId != null && woIds.Contains(t.Document.WorkOrderId.Value)
                                && (t.Document.Type == InventoryDocumentType.Issue || t.Document.Type == InventoryDocumentType.ProductionReturn))
                    .GroupBy(t => new { t.ItemId, Wo = t.Document.WorkOrderId.Value }).Select(g => new { g.Key.ItemId, g.Key.Wo, Q = -g.Sum(x => x.Quantity) }).ToListAsync(ct);
                foreach (var n in net) f.Net[(n.Wo, n.ItemId)] = n.Q;

                var ceilings = await _uow.Set<InventoryConsumptionCeiling>().AsNoTracking()
                    .Where(c => !c.Deleted && issueItemIds.Contains(c.ItemId) && (c.WorkOrderId == null || woIds.Contains(c.WorkOrderId.Value))).ToListAsync(ct);
                foreach (var c in ceilings)
                {
                    if (c.WorkOrderId.HasValue) f.Ceilings[(c.WorkOrderId.Value, c.ItemId)] = c.MaxQuantity;
                    else f.GlobalCeilings[c.ItemId] = c.MaxQuantity;
                }
            }

            // the BOM baseline of each work order
            if (woIds.Count > 0)
            {
                var baselines = await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && woIds.Contains(b.WorkOrderId)).Select(b => new { b.Id, b.WorkOrderId, b.ContractId }).ToListAsync(ct);
                List<int> baselineIds = baselines.Select(b => b.Id).ToList();
                var lines = baselineIds.Count == 0 ? new List<PbProjectBomLine>()
                    : await _uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => !l.Deleted && !l.IsRemoved && !l.IsHeading && baselineIds.Contains(l.BaselineId)).ToListAsync(ct);
                var changes = baselineIds.Count == 0 ? new List<PbProjectBomChange>()
                    : await _uow.Set<PbProjectBomChange>().AsNoTracking().Where(c => !c.Deleted && baselineIds.Contains(c.BaselineId) && (c.Status == PbChangeStatus.Approved || c.Status == PbChangeStatus.Applied) && c.ItemId != null).ToListAsync(ct);
                DateTime now = DateTime.Today;
                var subs = await _uow.Set<ItemSubstitute>().AsNoTracking().Where(s => !s.Deleted && s.Status == SubstituteStatus.Approved && (s.ValidFrom == null || s.ValidFrom <= now) && (s.ValidTo == null || s.ValidTo >= now)).ToListAsync(ct);
                // PB8 fix round (H3): the requirement is the latest material-balance run of the work order (raw materials of made parts, scrap, converted units and substitutes included),
                // not the raw lines of the project BOM; a work order without a run has nothing to compare with
                var runs = baselineIds.Count == 0 ? null : await _uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted && baselineIds.Contains(r.BaselineId)).Select(r => new { r.Id, r.BaselineId, r.RunNumber }).ToListAsync(ct);
                var latestRun = new Dictionary<int, int>();
                if (runs != null) foreach (var g in runs.GroupBy(r => r.BaselineId)) latestRun[g.Key] = g.OrderByDescending(r => r.RunNumber).First().Id;
                List<int> runIds = latestRun.Values.ToList();
                var balanceLines = runIds.Count == 0 ? new List<ProjectMaterialBalanceLine>()
                    : await _uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().Where(l => !l.Deleted && runIds.Contains(l.BalanceId) && l.ItemId != null).ToListAsync(ct);
                foreach (var b in baselines)
                {
                    int wo = b.WorkOrderId;
                    var bl = lines.Where(l => l.BaselineId == b.Id).ToList();
                    f.BaselineParts[wo] = new HashSet<string>(bl.Where(l => !string.IsNullOrEmpty(l.PartNumber)).Select(l => l.PartNumber));
                    f.BaselineMaterial[wo] = bl.Where(l => l.ItemId.HasValue && !string.IsNullOrWhiteSpace(l.Material)).GroupBy(l => l.ItemId.Value).ToDictionary(g => g.Key, g => g.First().Material);
                    if (!latestRun.TryGetValue(b.Id, out int runId)) continue;
                    f.WorkOrdersWithBaseline.Add(wo);
                    var rl = balanceLines.Where(l => l.BalanceId == runId).ToList();
                    var ids = new HashSet<int>(rl.Select(l => l.ItemId.Value));
                    foreach (var l in bl.Where(l => l.ItemId.HasValue)) ids.Add(l.ItemId.Value);   // an item of the BOM itself is in the baseline, with or without a requirement line
                    var qty = rl.GroupBy(l => l.ItemId.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.RequiredQty));
                    foreach (var l in rl.Where(l => l.SubstituteItemId.HasValue))
                    {
                        ids.Add(l.SubstituteItemId.Value);
                        qty[l.SubstituteItemId.Value] = (qty.TryGetValue(l.SubstituteItemId.Value, out decimal sq) ? sq : 0m) + l.RequiredQty;
                    }
                    foreach (var c in changes.Where(c => c.BaselineId == b.Id)) ids.Add(c.ItemId.Value);
                    foreach (var s in subs)
                    {
                        bool applies = s.Scope == SubstituteScope.Global || (s.WorkOrderId == wo) || (s.ContractId.HasValue && b.ContractId == s.ContractId);
                        if (applies && ids.Contains(s.ItemId)) ids.Add(s.SubstituteItemId);
                    }
                    f.BaselineItems[wo] = ids;
                    f.BaselineQty[wo] = qty;
                }
            }

            // the project-control activity of a kit request must still be open
            List<int> activityIds = requests.Where(r => r.ActivityId.HasValue).Select(r => r.ActivityId.Value).Distinct().ToList();
            if (activityIds.Count > 0)
            {
                var acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => activityIds.Contains(a.Id)).Select(a => new { a.Id, a.Title, a.Deleted, a.Status }).ToListAsync(ct);
                foreach (MaterialRequest r in requests.Where(r => r.ActivityId.HasValue))
                {
                    var a = acts.FirstOrDefault(x => x.Id == r.ActivityId.Value);
                    f.RequestActivity[r.Id] = a == null || a.Deleted ? new OpsActivityFact { Title = "؟", Exists = false, Open = false } : new OpsActivityFact { Title = a.Title, Exists = true, Open = a.Status != PcActivityStatus.Done };
                }
            }
            return f;
        }

        private async Task<OpsStageFacts> LoadStageFactsAsync(IEnumerable<InventoryItem> items, CancellationToken ct)
        {
            var f = new OpsStageFacts();
            List<string> parts = items.Where(i => !string.IsNullOrEmpty(i.StageCode2) && !string.IsNullOrEmpty(i.PartNumber)).Select(i => i.PartNumber).Distinct().ToList();
            if (parts.Count == 0) return f;
            var steps = await _uow.Set<PbOpcStep>().AsNoTracking().Where(s => !s.Deleted && parts.Contains(s.PartNumber) && s.Routing.Status == PbRoutingStatus.Approved && !s.Routing.Deleted)
                .Select(s => new { s.PartNumber, s.StageCode2, s.Seq, s.RoutingId, Rev = s.Routing.Revision }).ToListAsync(ct);
            foreach (var g in steps.GroupBy(s => s.PartNumber))
            {
                int latest = g.Max(s => s.Rev);
                f.Sequence[g.Key] = g.Where(s => s.Rev == latest && !string.IsNullOrEmpty(s.StageCode2)).OrderBy(s => s.Seq).Select(s => s.StageCode2).Distinct().ToList();
            }
            var stageItems = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => parts.Contains(i.PartNumber) && i.StageCode2 != null && i.StageCode2 != "").Select(i => new { i.Id, i.PartNumber, i.StageCode2 }).ToListAsync(ct);
            foreach (var i in stageItems) f.ExistingStageItems.Add((i.PartNumber, i.StageCode2));
            List<int> stageItemIds = stageItems.Select(i => i.Id).ToList();
            if (stageItemIds.Count > 0)
            {
                List<int> withStock = await _uow.Set<StockTransaction>().AsNoTracking().Where(t => stageItemIds.Contains(t.ItemId) && t.Quantity > 0).Select(t => t.ItemId).Distinct().ToListAsync(ct);
                foreach (var i in stageItems.Where(i => withStock.Contains(i.Id))) f.StagesWithStock.Add((i.PartNumber, i.StageCode2));
            }
            foreach (string code in await _uow.Set<PbStage>().AsNoTracking().Where(s => s.IsStockable).Select(s => s.Code).ToListAsync(ct)) f.StockableStages.Add(code);
            return f;
        }
    }
}
